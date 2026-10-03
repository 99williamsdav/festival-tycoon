using Festival.Simulation;
using Festival.Persistence;
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

public partial class Main
{
    private Label? _equipmentSummary;
    private readonly Dictionary<EquipmentAction, Button> _equipmentButtons = [];
    private Node3D? _equipmentVisual;
    private ulong _generatorPickId;
    private bool _selectedGenerator;

    private void SelectGenerator()
    {
        ClearSelection(); _selectedGenerator = true;
        RefreshMedicalActionInspector(); RefreshStagePowerAction(); RefreshGeneratorInspector();
        _highlight.Position = _equipmentVisual!.Position + new Vector3(0, .08f, 0);
        _highlight.Scale = new Vector3(4, 1, 4); _highlight.Visible = true;
    }
    private void RefreshGeneratorInspector()
    {
        if (!_selectedGenerator || _session.CaptureEquipment() is not { } equipment) return;
        _inspectorTitle.Text = "Stage generator";
        _inspectorBody.Text = $"This generator currently supplies the trailer stage only.\nCircuit: {equipment.Stage} · load {equipment.LoadPercent}% · condition {equipment.Condition / 100}%\nIsolation removes the modeled load, stops overload escalation and interrupts stage music.";
        RefreshStagePowerAction(); RefreshContextPanelVisibility();
    }
    private EquipmentStage? _equipmentVisualStage;

    private void BuildEquipmentControls(VBoxContainer box)
    {
        _equipmentSummary = LabelText("", 14, new Color("7a321c"));
        _equipmentSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _equipmentSummary.CustomMinimumSize = new Vector2(370, 110); box.AddChild(_equipmentSummary);
        foreach (var row in new[] {
            new[] { (EquipmentAction.ShedLoad, "SHED LOAD") },
            new[] { (EquipmentAction.DispatchMaintenance, "DISPATCH MORGAN"), (EquipmentAction.Acknowledge, "ACKNOWLEDGE") } })
        {
            var line = new HBoxContainer(); box.AddChild(line);
            foreach (var (action, title) in row)
            {
                var button = ButtonText(title, () => CommitEquipmentAction(new EquipmentCommand(action)));
                button.AddThemeFontSizeOverride("font_size", 12); line.AddChild(button); _equipmentButtons.Add(action, button);
            }
        }
    }

    private void CommitEquipmentAction(SessionCommand command)
    {
        if (_host.Execute(command, out var error))
        {
            if (command is ChoosePerkCommand) SyncExtraWaterWorld();
            _preparationMessage = "Plan updated; saves every 30 unpaused seconds and at opening.";
            if (command is ChoosePerkCommand)
            {
                if (_session.CapturePreparation()?.Attempt == 1) OpenBuildCatalogue();
                else SelectHudTab("Overview");
            }
        }
        else _preparationMessage = error!;
        RefreshPreparationHud();
    }

    private void RefreshEquipmentControls()
    {
        RefreshGeneratorInspector();
        if (_equipmentSummary is null || _session.CaptureEquipment() is not { } e) return;
        var left = e.WarningTick < 0 ? GameSession.EquipmentDeathDelayTicks : Math.Max(0, e.WarningTick + GameSession.EquipmentDeathDelayTicks - _session.CurrentTick);
        _equipmentSummary.Text = $"STAGE GENERATOR • {e.Stage}\nLoad {e.LoadPercent}% / safe 100% • condition {e.Condition / 100}%\n" +
            (e.Stage is EquipmentStage.Warning or EquipmentStage.DangerousFault ? $"GENERATOR OVERLOAD • {left / 80m:0.0}s to lethal eligibility at 1×\n" : e.Stage == EquipmentStage.Normal ? "Visible load exceeds capacity before any alarm.\n" : "Unit made safe; no further escalation.\n") +
            $"{e.Response}\nMaintenance: {e.JobStage}. Repair needs arrival + 20s.";
        if (e.Stage == EquipmentStage.Terminal)
            _equipmentSummary.Text = "WEEKEND ENDED • COUNCIL HEARING\n" + StewardWording(_session.CaptureLifecycleSnapshot()!.Casualties.Last().Cause) +
                (_host.SaveError is not null ? "\nSave failed; use the visible Retry save control." : "\nThe failure checkpoint has been saved.");
        foreach (var (action, button) in _equipmentButtons)
            button.Disabled = _session.ValidateCommand(CampaignEnvelope(new EquipmentCommand(action))) is not null;
        if (_equipmentVisualStage == e.Stage) return;
        _equipmentVisual?.QueueFree();
        var variant = e.Stage switch { EquipmentStage.Warning => "overloaded", EquipmentStage.DangerousFault or EquipmentStage.Terminal => "fault", EquipmentStage.Isolated => "isolated", _ => "normal" };
        _equipmentVisual = AddAsset($"res://assets/equipment/lwf_towable_generator_{variant}_v1.glb", new Vector3(e.XMillimetres / 1000f, 0, e.ZMillimetres / 1000f));
        var pick = new StaticBody3D { CollisionLayer = 1, CollisionMask = 1, Position = new Vector3(0, 1.3f, 0) };
        pick.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 2.6f, 3) } });
        _equipmentVisual.AddChild(pick); _generatorPickId = pick.GetInstanceId();
        var marker = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 3.9f, OuterRadius = 4f, Rings = 32, RingSegments = 8 },
            Scale = new Vector3(1, .04f, 1), Position = new Vector3(0, .07f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("e0ad45") } };
        _equipmentVisual.AddChild(marker);
        // Smaller and lower than the stage it powers, so the two names don't collide.
        var sign = BuildingName("GENERATOR", new Vector3(0, 1.4f, 0), 28);
        _equipmentVisual.AddChild(sign); _equipmentVisualStage = e.Stage;
    }

    // Scripted normal buttons for prevention; ignored response is explicitly labelled.
    // Samples stay in memory; screenshots/writes occur only at four checkpoints.
}

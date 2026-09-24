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
    private EquipmentStage? _equipmentVisualStage;
    private string? _equipmentCaptureDirectory;
    private string _equipmentCaptureMode = "prevent";
    private int _equipmentCaptureStep;
    private long _equipmentCaptureStarted;
    private long _equipmentPriorFrame;
    private readonly List<double> _equipmentFrames = [];

    private void BuildEquipmentControls(VBoxContainer box)
    {
        _equipmentSummary = LabelText("", 14, new Color("7a321c"));
        _equipmentSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _equipmentSummary.CustomMinimumSize = new Vector2(370, 110); box.AddChild(_equipmentSummary);
        foreach (var row in new[] {
            new[] { (EquipmentAction.ShedLoad, "SHED LOAD"), (EquipmentAction.Isolate, "EMERGENCY CUTOFF") },
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
        var result = EquipmentCommandCoordinator.Execute(SaveDirectory, _session, command, _saveCompatibility, DateTimeOffset.UtcNow, _autosaveGeneration);
        if (result.IsSuccess)
        {
            _session = result.Session; _autosaveGeneration++;
            _preparationSaveBlocked = false;
            _preparationMessage = "Action committed and autosaved.";
        }
        else { _preparationMessage = result.Error!; if (result.Autosave is not null) _preparationSaveBlocked = true; }
        RefreshPreparationHud();
    }

    private void RefreshEquipmentControls()
    {
        if (_equipmentSummary is null || _session.CaptureEquipment() is not { } e) return;
        var left = e.WarningTick < 0 ? GameSession.EquipmentDeathDelayTicks : Math.Max(0, e.WarningTick + GameSession.EquipmentDeathDelayTicks - _session.CurrentTick);
        _equipmentSummary.Text = $"STAGE GENERATOR • {e.Stage}\nLoad {e.LoadPercent}% / safe 100% • condition {e.Condition / 100}%\n" +
            (e.Stage is EquipmentStage.Warning or EquipmentStage.DangerousFault ? $"GENERATOR OVERLOAD • {left / 80m:0.0}s to lethal eligibility at 1×\n" : e.Stage == EquipmentStage.Normal ? "Visible load exceeds capacity before any alarm.\n" : "Unit made safe; no further escalation.\n") +
            $"{e.Response}\nMaintenance: {e.JobStage}. Repair needs arrival + 20s.";
        if (e.Stage == EquipmentStage.Terminal)
            _equipmentSummary.Text = "EDITION FROZEN • COUNCIL HEARING\n" + _session.CaptureLifecycleSnapshot()!.Casualties.Single().Cause + "\nHearing saved. Retry/Favour gameplay is not part of this slice.";
        if (_equipmentCaptureMode == "escalate" && _equipmentCaptureDirectory is not null)
            _equipmentSummary.Text = "LABELLED IGNORED-RESPONSE FIXTURE\n" + _equipmentSummary.Text;
        foreach (var (action, button) in _equipmentButtons)
            button.Disabled = _session.ValidateCommand(CampaignEnvelope(new EquipmentCommand(action))) is not null;
        if (_equipmentVisualStage == e.Stage) return;
        _equipmentVisual?.QueueFree();
        var variant = e.Stage switch { EquipmentStage.Warning => "overloaded", EquipmentStage.DangerousFault or EquipmentStage.Terminal => "fault", EquipmentStage.Isolated => "isolated", _ => "normal" };
        _equipmentVisual = AddAsset($"res://assets/equipment/lwf_towable_generator_{variant}_v1.glb", new Vector3(e.XMillimetres / 1000f, 0, e.ZMillimetres / 1000f));
        var marker = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 3.9f, OuterRadius = 4f, Rings = 32, RingSegments = 8 },
            Scale = new Vector3(1, .04f, 1), Position = new Vector3(0, .07f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("e0ad45") } };
        _equipmentVisual.AddChild(marker);
        var sign = new Label3D { Text = "GENERATOR", Position = new Vector3(0, 2.7f, 0), FontSize = 48, PixelSize = .010f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled };
        _equipmentVisual.AddChild(sign); _equipmentVisualStage = e.Stage;
    }

    // Scripted normal buttons for prevention; ignored response is explicitly labelled.
    // Samples stay in memory; screenshots/writes occur only at four checkpoints.
    private void ProcessEquipmentCapture()
    {
        var now = Stopwatch.GetTimestamp();
        if (_equipmentPriorFrame != 0 && _equipmentCaptureStarted != 0) _equipmentFrames.Add(Stopwatch.GetElapsedTime(_equipmentPriorFrame, now).TotalMilliseconds);
        _equipmentPriorFrame = now;
        if (_equipmentCaptureStep == 0)
        {
            foreach (var id in new[] { "act.folk", "staff.steward", "equipment.buy", "maintenance.worker" }) _offerButtons[id].EmitSignal(Button.SignalName.Pressed);
            _equipmentCaptureStep = 1; return;
        }
        if (_equipmentCaptureStep == 1)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_equipmentCaptureDirectory!, "pre-warning.png"));
            _preparationStart.EmitSignal(Button.SignalName.Pressed);
            _equipmentCaptureStarted = now; _equipmentCaptureStep = 2; return;
        }
        var e = _session.CaptureEquipment()!;
        if (_equipmentCaptureStep == 2 && e.Stage == EquipmentStage.Warning)
        {
            RefreshPreparationHud();
            _equipmentCaptureStep = 20; return;
        }
        if (_equipmentCaptureStep == 20)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_equipmentCaptureDirectory!, "warning.png"));
            PreparationSave(); PreparationLoad();
            if (_equipmentCaptureMode != "escalate") _equipmentButtons[EquipmentAction.DispatchMaintenance].EmitSignal(Button.SignalName.Pressed);
            _equipmentCaptureStep = 3;
        }
        if (_equipmentCaptureStep == 3 && (e.Stage is EquipmentStage.Resolved or EquipmentStage.Terminal || Stopwatch.GetElapsedTime(_equipmentCaptureStarted).TotalSeconds > 110 || _preparationSaveBlocked))
        {
            RefreshPreparationHud(); _equipmentCaptureStep = 4; return;
        }
        if (_equipmentCaptureStep != 4) return;
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_equipmentCaptureDirectory!, "outcome.png"));
        var loaded = AutosaveRotation.LoadNewestValid(SaveDirectory, _saveCompatibility);
        var exact = loaded.IsSuccess && loaded.Session!.CaptureSnapshot().AuthoritativeHash == _session.CaptureSnapshot().AuthoritativeHash;
        // A safe live session may have advanced beyond the boundary autosave; compare a manual round trip as well.
        var before = _session.CaptureSnapshot().AuthoritativeHash; PreparationSave(); PreparationLoad();
        var roundTrip = before == _session.CaptureSnapshot().AuthoritativeHash;
        var sorted = _equipmentFrames.Order().ToArray();
        double P(double p) => sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * p) - 1, 0, sorted.Length - 1)];
        var seconds = Stopwatch.GetElapsedTime(_equipmentCaptureStarted).TotalSeconds;
        var passed = roundTrip && (_equipmentCaptureMode == "escalate" ? e.Stage == EquipmentStage.Terminal && exact : e.JobStage == MaintenanceStage.Completed);
        File.WriteAllText(Path.Combine(_equipmentCaptureDirectory!, "result.json"), JsonSerializer.Serialize(new { passed, mode = _equipmentCaptureMode, roundTrip, latestAutosaveExact = exact,
            ticks = _session.CurrentTick, wallSeconds = seconds, attainedSpeed = _session.CurrentTick / (80 * seconds), people = _session.CapturePreparation()!.People.Length,
            samples = sorted.Length, frameP50 = P(.5), frameP95 = P(.95), frameP99 = P(.99), frameMax = sorted[^1],
            peakMemoryBytes = Process.GetCurrentProcess().PeakWorkingSet64, equipment = e, lifecycle = _session.CaptureLifecycleSnapshot() }, new JsonSerializerOptions { WriteIndented = true }));
        GetTree().Quit(passed ? 0 : 2);
    }
}

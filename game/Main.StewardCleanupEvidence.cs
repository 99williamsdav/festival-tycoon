using Festival.ContentAdapter;
using Festival.Simulation;
using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Festival.Game;

// Labelled opt-in render fixture. Never enters ordinary campaigns or creates player saves.
public partial class Main
{
    private string? _cleanupEvidenceOutput;
    private int _cleanupEvidenceFrame;
    private ulong _cleanupEvidenceWorker;
    private GridCell _cleanupEvidenceCell = new(116, 154);
    private readonly List<object> _cleanupEvidenceChecks = [];
    private Label? _cleanupEvidenceLabel;
    private Transform3D _cleanupEvidenceFoot;
    private static readonly (string Name, int Tick)[] CleanupEvidenceStages =
    [ ("equipped", -1), ("aim", 16), ("contact", 36), ("lift", 52), ("bag", 68),
      ("cup-contact", 36), ("cup-bag", 68), ("foot-centre-contact", 36),
      ("bin-empty", 120), ("safety-stowed", 52), ("held-food-stowed", 52),
      ("collapse-stowed", 52), ("departure-stowed", 52), ("reconstructed-mid-pickup", 52), ("wide-equipped", -1) ];
    private void SetCleanupEvidenceStage(string variant, string name, int tick)
    {
        var worker = _cleanupEvidenceWorker; var cell = _cleanupEvidenceCell; var pos = TraversalGrid.CellCentre(cell);
        EvidenceInvoke(_session, "MutatePerson", worker, (Action<Person>)(p =>
        { p.Admitted = true; p.Departed = name == "departure-stowed"; p.Held = name == "held-food-stowed" ? new("labelled-food", ImmersionProduct.Chips, 0) : null;
          p.HealthStage = name == "collapse-stowed" ? MedicalStage.Collapsed : MedicalStage.Clear;
          p.Intent = name == "safety-stowed" ? MedicalIntent.SeekWater : MedicalIntent.WatchShow;
          p.Thirst = p.Hunger = p.HeatExposure = p.ToiletNeed = 2000; }));
        typeof(GameSession).GetProperty("CurrentTick")!.SetValue(_session, 800L + Math.Max(0, tick));
        var centre = name == "foot-centre-contact";
        var cup = name.StartsWith("cup-");
        var piece = new WastePiece(cup ? "labelled-cleanup-soft" : "labelled-cleanup-chips", 1,
            cup ? ImmersionProduct.SoftDrink : ImmersionProduct.Chips, 0, WasteLocation.Ground,
            pos.XMillimetres + (centre ? 0 : 180), pos.ZMillimetres + (centre ? 0 : 150));
        var job = new CleanupSweep(worker, cell, 16, 6, 2400, false, piece.Id, false, cell, tick < 0 ? -1 : 800);
        if (name == "bin-empty") job = job with { TargetIsBin = true, TargetId = "bin.1" };
        typeof(GameSession).GetField("_litter", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session, new LitterSnapshot(1, [piece], [job]));
        var actor = _attendeeVisuals[new(worker)];
        if (actor.GetMeta("RoleVariant").AsString() != variant || name == "reconstructed-mid-pickup")
            ResetStewardCleanupPresentation();
        if (actor.GetMeta("RoleVariant").AsString() != variant)
        {
            var old = actor.GetNode<Node3D>("RoleGarment"); actor.RemoveChild(old); old.QueueFree();
            var garment = InstantiateAsset($"res://assets/characters/lwf_steward_{variant}_overlay_v2.glb");
            garment.Name = "RoleGarment"; actor.AddChild(garment);
        }
        actor.SetMeta("RoleVariant", variant);
        _foundationPresentation.Reset(_session.CaptureObservation()); _host.Clock.ResetBoundary();
        _rig.Frame(ImmersionPosition(cell), name == "wide-equipped" ? 32 : 4.5f);
        _cleanupEvidenceLabel!.Text = $"STAGED CLEANUP PRESENTATION · {variant.ToUpperInvariant()} · {name}\nExisting action phase; no player save. Paused render fixture.";
    }
    private void SetupCleanupEvidence()
    {
        SetupLitterEvidence(); ClearSelection();
        _cleanupEvidenceWorker = _session.CaptureDisorder()!.SecurityId;
        var agents = (IDictionary)typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_session)!;
        var nav = agents[new EntityId(_cleanupEvidenceWorker)]!; var pos = TraversalGrid.CellCentre(_cleanupEvidenceCell);
        void Set(string name, object value) => nav.GetType().GetProperty(name)!.SetValue(nav, value);
        Set("XMillimetres", pos.XMillimetres); Set("ZMillimetres", pos.ZMillimetres);
        Set("SegmentOriginXMillimetres", pos.XMillimetres); Set("SegmentOriginZMillimetres", pos.ZMillimetres);
        Set("Route", new List<GridCell>()); Set("RouteIndex", 0); Set("SegmentProgressMicrometres", 0);
        Set("Action", AgentNavigationAction.Arrived); Set("Destination", _cleanupEvidenceCell); Set("IntentId", "litter.visual-proof");
        var canvas = new CanvasLayer { Layer = 90 }; AddChild(canvas);
        _cleanupEvidenceLabel = new Label { Position = new(22, 72) };
        _cleanupEvidenceLabel.AddThemeFontSizeOverride("font_size", 20); canvas.AddChild(_cleanupEvidenceLabel);
        SetCleanupEvidenceStage("male", "equipped", -1);
        AdvancePreparationPresentation(0);
        _cleanupEvidenceFoot = _attendeeVisuals[new(_cleanupEvidenceWorker)].GlobalTransform;
    }
    private void CaptureCleanupEvidence(string variant, string name)
    {
        var actor = _attendeeVisuals[new(_cleanupEvidenceWorker)];
        var visual = StewardCleanupPresentation.Read(_session, _cleanupEvidenceWorker);
        var active = visual.Mode != StewardCleanupMode.Stowed;
        _cleanupViews.TryGetValue(new(_cleanupEvidenceWorker), out var view);
        var nodes = view is null ? [] : view.Pivots.Values.Append(view.Root).Append(view.Picker).Append(view.Bag)
            .Concat(view.Waste is null ? [] : new Node3D[] { view.Waste }).ToArray();
        var before = nodes.Select(n => n.GlobalTransform).ToArray();
        var hash = _session.CaptureSnapshot().AuthoritativeHash;
        AdvanceStewardCleanupPresentation(); SyncLitterWorld();
        var pauseExact = before.SequenceEqual(nodes.Select(n => n.GlobalTransform)) && hash == _session.CaptureSnapshot().AuthoritativeHash;
        var represented = VisibleLitterInstanceCount + _cleanupLiftedWaste.Count;
        var ordinary = actor.GetNode<Node3D>("RoleBody").Visible;
        var equipmentCorrect = active ? view?.Root.Visible == true && !ordinary : view?.Root.Visible != true && ordinary;
        var footFixed = name is "collapse-stowed" or "departure-stowed" || actor.GlobalPosition == _cleanupEvidenceFoot.Origin;
        var lowerFixed = view is null || ((Node3D)view.Body.FindChild("LowerBody", true, false)).Transform == Transform3D.Identity;
        var endpoint = true;
        if ((name.EndsWith("contact") || name.EndsWith("bag")) && view is not null)
        {
            var piece = _session.CaptureLitter()!.Pieces.Single();
            var ground = GroundWasteTransform(piece, new(piece.XMillimetres / 1000f, 0, piece.ZMillimetres / 1000f));
            var target = name.EndsWith("contact") ? ground * new Vector3(0, piece.Product == ImmersionProduct.Chips ? .025f : .075f, 0) : ((Node3D)view.Bag.FindChild("BagMouthSocket", true, false)).GlobalPosition;
            endpoint = view.Jaw.GlobalPosition.DistanceTo(target) < .0001f;
        }
        var passed = pauseExact && represented == 1 && equipmentCorrect && footFixed && lowerFixed && endpoint;
        _cleanupEvidenceChecks.Add(new { variant, name, mode = visual.Mode.ToString(), visual.Progress, represented,
            lifted = _cleanupLiftedWaste.Count, pauseExact, equipmentCorrect, footFixed, lowerFixed, endpoint, passed });
        if (!passed) throw new InvalidOperationException($"Cleanup evidence failed: {variant} {name}: {JsonSerializer.Serialize(_cleanupEvidenceChecks[^1])}");
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_cleanupEvidenceOutput!, $"{variant}-{name}.png"));
    }
    private void ProcessCleanupEvidence()
    {
        if (_cleanupEvidenceOutput is null) return;
        if (_cleanupEvidenceFrame == 0) SetupCleanupEvidence();
        var frame = _cleanupEvidenceFrame++;
        var stages = CleanupEvidenceStages.Length * 2;
        if (frame < stages * 5)
        {
            var index = frame / 5; var stage = CleanupEvidenceStages[index % CleanupEvidenceStages.Length];
            var variant = index < CleanupEvidenceStages.Length ? "male" : "female";
            if (frame % 5 == 0) SetCleanupEvidenceStage(variant, stage.Name, stage.Tick);
            if (frame % 5 == 3) CaptureCleanupEvidence(variant, stage.Name);
            return;
        }
        var clipFrame = frame - stages * 5;
        if (OS.GetCmdlineUserArgs().Contains("--cleanup-sequence") && clipFrame <= 80)
        {
            var directory = Path.Combine(_cleanupEvidenceOutput, "sequence"); Directory.CreateDirectory(directory);
            if (clipFrame > 0) GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, $"{clipFrame - 1:000}.png"));
            if (clipFrame < 80) { SetCleanupEvidenceStage("male", "pickup-sequence", clipFrame); return; }
        }
        File.WriteAllText(Path.Combine(_cleanupEvidenceOutput, "cleanup-evidence.json"), JsonSerializer.Serialize(new {
            diagnostic = "staged-steward-cleanup", debug = OS.IsDebugBuild(), resolution = GetWindow().Size.ToString(),
            checks = _cleanupEvidenceChecks, passed = true,
            caveat = "Staged paused visual fixture, two cosmetic body variants. Real pause and mid-action save/restore tested separately. No human playtest or FPS certification." }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("CLEANUP_EVIDENCE_COMPLETE passed=true"); GetTree().Quit();
    }
    private void FinalizeCleanupEvidenceFrame()
    {
        if (_cleanupEvidenceOutput is null || _cleanupEvidenceFrame == 0) return;
        // Isolate the commissioned body and equipment in the staged close views.
        foreach (var (id, actor) in _attendeeVisuals)
            if (id.Value != _cleanupEvidenceWorker) actor.Hide();
        foreach (var label in FindChildren("*", "Label3D", true, false).OfType<Label3D>()) label.Hide();
    }
}

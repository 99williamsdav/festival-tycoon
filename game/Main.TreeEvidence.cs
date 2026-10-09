using Godot;
using Festival.Simulation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

// Opt-in exported before/after fixture. No new simulation or save behavior.
public partial class Main
{
    private string? _treeEvidenceOutput;
    private int _treeEvidenceFrame;
    private readonly List<(Node3D Before, Node3D After)> _treeEvidencePairs = [];
    private Control? _treeEvidenceReceipt;
    private string _treeEvidenceHash = "";
    private readonly List<double> _treeEvidenceTimes = [], _treeEvidenceDraws = [];
    private readonly List<object> _treeEvidenceMeasurements = [];
    private long _treeEvidenceTimestamp;
    private Transform3D[] _treeEvidenceMoving = [], _treeEvidencePaused = [];
    private string _treeEvidencePausedHash = "";
    private bool _treeEvidenceUnchanged, _treeEvidenceSway;

    private Transform3D[] TreeCrownTransforms() => _treeEvidencePairs.Select(pair => ((Node3D)pair.After.FindChild("Crown", true, false)).GlobalTransform).ToArray();

    private void RegisterTreeEvidence(Node3D tree, string asset, Vector3 at)
    {
        if (_treeEvidenceOutput is null || !asset.EndsWith("_v2")) return;
        var old = AddAsset($"res://assets/environment/{asset.Replace("_v2", "_v1")}.glb", at);
        old.Hide(); _treeEvidencePairs.Add((old, tree));
        foreach (var type in new[] { "CollisionObject3D", "NavigationRegion3D", "AnimationPlayer", "Skeleton3D" })
            if (tree.FindChildren("*", type, true, false).Count != 0) throw new InvalidOperationException($"Tree has unexpected {type}");
        if (tree.FindChild("Crown", true, false) is not MeshInstance3D || tree.FindChild("Trunk", true, false) is not MeshInstance3D)
            throw new InvalidOperationException("Tree must retain Crown and Trunk meshes");
    }

    private void ShowTreeEvidence(bool before)
    {
        foreach (var pair in _treeEvidencePairs) { pair.Before.Visible = before; pair.After.Visible = !before; }
    }

    private void ProcessTreeEvidence()
    {
        if (_treeEvidenceOutput is null) return;
        try { CaptureTreeEvidence(); }
        catch (Exception error) { GD.PushError(error.ToString()); _treeEvidenceOutput = null; GetTree().Quit(2); }
    }

    private void CaptureTreeEvidence()
    {
        if (_treeEvidenceFrame == 0)
        {
            if (_treeEvidencePairs.Count != 2) throw new InvalidOperationException("Expected oak and apple pairs");
            Directory.CreateDirectory(_treeEvidenceOutput!);
            var perk = _session.CapturePerks()!;
            foreach (var command in new SessionCommand[] { new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]), new UseDefaultBuildLayoutCommand() })
            {
                var result = _session.Execute(CampaignEnvelope(command));
                if (!result.IsAccepted) throw new InvalidOperationException(result.Message);
            }
            SyncBuildWorld(); _hudWorkspaceOpen = _buildDrawerOpen = false;
            _boxOfficeSeenAttempt = _session.CapturePreparation()!.Attempt; RefreshPreparationHud();
            _treeEvidenceHash = _session.CaptureSnapshot().AuthoritativeHash.ToString();
            _treeEvidenceReceipt = typeof(PreparationDock).GetField("_receipt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(Dock) as Control;
            Engine.MaxFps = 0; DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        }
        Dock.Readiness?.Hide(); _treeEvidenceReceipt?.CallDeferred(CanvasItem.MethodName.Hide);
        var frame = _treeEvidenceFrame++;
        if (frame < 288)
        {
            var index = frame / 12; var within = frame % 12; var view = index % 6;
            if (within == 0)
            {
                if (view == 0 && index > 0) _rig.Rotate(1);
                var target = view < 2 ? Vector3.Zero : view < 4 ? new Vector3(-32, 3, 9) : new Vector3(-29.2f, 1, -6.6f);
                _rig.Frame(target, view < 2 ? 66 : view < 4 ? 17 : 9);
                ShowTreeEvidence(view % 2 == 0);
            }
            if (within == 10) GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_treeEvidenceOutput!,
                $"{index / 6}-{_rig.OrientationName.ToLowerInvariant()}-{(view < 2 ? "farm" : view < 4 ? "oak" : "apple")}-{(view % 2 == 0 ? "before" : "after")}.png"));
            return;
        }
        var performanceFrame = frame - 288; var phase = performanceFrame / 120; var sample = performanceFrame % 120;
        if (frame == 508) _treeEvidenceMoving = TreeCrownTransforms();
        if (phase < 2)
        {
            if (sample == 0) { _rig.Frame(Vector3.Zero, 66); ShowTreeEvidence(phase == 0); _treeEvidenceTimes.Clear(); _treeEvidenceDraws.Clear(); }
            var now = Stopwatch.GetTimestamp();
            if (sample >= 30) { _treeEvidenceTimes.Add(Stopwatch.GetElapsedTime(_treeEvidenceTimestamp, now).TotalMilliseconds); _treeEvidenceDraws.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)); }
            _treeEvidenceTimestamp = now;
            if (sample == 119)
            {
                var sorted = _treeEvidenceTimes.Order().ToArray();
                _treeEvidenceMeasurements.Add(new { before = phase == 0, frames = 90, medianMs = sorted[45], p95Ms = sorted[85], draws = _treeEvidenceDraws.Average() });
            }
            return;
        }
        if (frame == 528)
        {
            _treeEvidenceUnchanged = _treeEvidenceHash == _session.CaptureSnapshot().AuthoritativeHash.ToString();
            _treeEvidenceSway = !_treeEvidenceMoving.SequenceEqual(TreeCrownTransforms());
            var pause = _session.Execute(CampaignEnvelope(new SetPausedCommand(true)));
            if (!pause.IsAccepted) throw new InvalidOperationException(pause.Message);
            _treeEvidencePaused = TreeCrownTransforms();
            _treeEvidencePausedHash = _session.CaptureSnapshot().AuthoritativeHash.ToString();
        }
        if (frame < 548) return;
        var pauseExact = _treeEvidencePaused.SequenceEqual(TreeCrownTransforms()) && _treeEvidencePausedHash == _session.CaptureSnapshot().AuthoritativeHash.ToString();
        if (!_treeEvidenceUnchanged || !pauseExact || !_treeEvidenceSway) throw new InvalidOperationException("Tree simulation/sway/pause verification failed");
        File.WriteAllText(Path.Combine(_treeEvidenceOutput!, "tree-evidence.json"), JsonSerializer.Serialize(new { passed = true, simulationUnchanged = _treeEvidenceUnchanged,
            crownSwayObserved = _treeEvidenceSway, pauseExact,
            trees = 2, crownTrunkNodesRetained = true, noCollisionNavigationAnimationSkeletonNodes = true, measurements = _treeEvidenceMeasurements,
            caveat = "Scripted preparation fixture; ambient poses may differ. Whole-app uncapped wall-frame timings, not isolated GPU cost or live-festival FPS certification. No manual playthrough." }, new JsonSerializerOptions { WriteIndented = true }));
        GetTree().Quit();
    }
}

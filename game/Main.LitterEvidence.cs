using Festival.Simulation;
using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Festival.Game;

// Opt-in, labelled visual/load fixture. Never runs on the ordinary New Game route.
public partial class Main
{
    private string? _litterEvidenceOutput;
    private int _litterEvidenceFrame;
    private readonly List<double> _litterFrameTimes = [];
    private readonly List<double> _litterDrawCalls = [];
    private long _litterEvidencePrevious;
    private object? _litterBaseline;
    private Transform3D[]? _pausedWaspTransforms;
    private static void EvidenceInvoke(GameSession s, string name, params object[] args) => typeof(GameSession)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, args);
    private void SetEvidenceWaste(int groundCount, bool full = true)
    {
        var bin = _session.CaptureBins().Single(); var pieces = new List<WastePiece>();
        if (full) for (var i = 0; i < 26; i++) pieces.Add(new("visual-bin:" + i, 1, (ImmersionProduct)(i % 3), 0,
            WasteLocation.Bin, 0, 0, bin.Id));
        for (var i = 0; i < groundCount; i++)
        {
            var x = i < 12 ? bin.Cell.X - 6 + i % 4 * 3 : 70 + i % 120;
            var z = i < 12 ? bin.Cell.Z - 5 + i / 4 * 3 : 108 + i / 120 % 82;
            var pos = TraversalGrid.CellCentre(new(x, z));
            pieces.Add(new("visual-ground:" + i, 1, (ImmersionProduct)(i % 3), 0, WasteLocation.Ground, pos.XMillimetres, pos.ZMillimetres));
        }
        if (groundCount == 12)
            foreach (var (id, product) in _session.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest).Take(3).Select((p, i) => (p.AgentId, (ImmersionProduct)i)))
                pieces.Add(new("visual-carried:" + id, id, product, 0, WasteLocation.Carried, 0, 0));
        typeof(GameSession).GetField("_litter", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session, new LitterSnapshot(1, pieces.ToArray(), []));
        SyncLitterWorld();
    }
    private void SetupLitterEvidence()
    {
        Directory.CreateDirectory(_litterEvidenceOutput!);
        void Send(SessionCommand command)
        { var result = _host.Submit(command); if (!result.IsAccepted) throw new InvalidOperationException($"Litter evidence: {command}: {result.Message}"); }
        var perk = _session.CapturePerks()!;
        Send(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0])); Send(new UseDefaultBuildLayoutCommand());
        Send(new PlaceBuildServiceCommand(BuildServiceKind.Bin, new(118, 166)));
        Send(new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"]));
        foreach (var group in _session.GetStaffCandidates().GroupBy(c => c.Role)) Send(new AcceptPreparationOfferCommand(group.OrderBy(c => c.Traits.Length).ThenBy(c => c.Grade > 0).First().Id));
        Send(new SetPreparationStockCommand(40, 40, 32)); Send(new StartPreparedEditionCommand()); Send(new SetPausedCommand(true));
        var guests = _session.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest).ToArray();
        var agents = (IDictionary)typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_session)!;
        for (var i = 0; i < 3; i++)
        {
            var id = guests[i].AgentId; var pos = TraversalGrid.CellCentre(new(114 + i * 4, 162)); var nav = agents[new EntityId(id)]!;
            void Set(string name, object value) => nav.GetType().GetProperty(name)!.SetValue(nav, value);
            Set("XMillimetres", pos.XMillimetres); Set("ZMillimetres", pos.ZMillimetres);
            Set("SegmentOriginXMillimetres", pos.XMillimetres); Set("SegmentOriginZMillimetres", pos.ZMillimetres);
            Set("Action", AgentNavigationAction.Idle); Set("Route", new List<GridCell>()); Set("RouteIndex", 0);
            EvidenceInvoke(_session, "MutatePerson", id, (Action<Person>)(p => { p.Admitted = true; p.Thirst = 2000; p.HeatExposure = 2000; p.ToiletNeed = 2000; }));
        }
        SetEvidenceWaste(12); SyncBuildWorld(); BuildAttendee();
        _foundationPresentation.Reset(_session.CaptureObservation()); _host.Clock.ResetBoundary();
        _hudWorkspaceOpen = _buildDrawerOpen = false; _boxOfficeSeenAttempt = 1;
        _rig.Frame(ImmersionPosition(new(118, 166)), 5); SelectBin("bin.1"); RefreshPreparationHud();
    }
    private void SaveLitterFrame(string name) => GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_litterEvidenceOutput!, name + ".png"));
    private static object LitterStats(IReadOnlyList<double> values)
    {
        var a = values.Order().ToArray(); return new { count = a.Length, mean = a.Average(), p50 = a[a.Length / 2], p95 = a[(int)(a.Length * .95)], max = a[^1] };
    }
    private void ProcessLitterEvidence()
    {
        if (_litterEvidenceOutput is null) return;
        if (_litterEvidenceFrame == 0) SetupLitterEvidence();
        _litterEvidenceFrame++;
        if (_litterEvidenceFrame == 8)
        {
            SaveLitterFrame("01-close-full-overflow-carried");
            _pausedWaspTransforms = Enumerable.Range(0, 5).Select(i => _binViews["bin.1"].Wasps.Multimesh.GetInstanceTransform(i)).ToArray();
        }
        if (_litterEvidenceFrame == 14) { SaveLitterFrame("02-close-paused"); _rig.Rotate(1); }
        if (_litterEvidenceFrame == 20) { SaveLitterFrame("03-close-west"); _rig.Rotate(1); }
        if (_litterEvidenceFrame == 26) { SaveLitterFrame("04-close-north"); _rig.Rotate(1); }
        if (_litterEvidenceFrame == 32) { SaveLitterFrame("05-close-east"); _rig.Frame(ImmersionPosition(new(118, 166)), 32); }
        if (_litterEvidenceFrame == 38) { SaveLitterFrame("06-game-zoom"); ClearSelection(); SetEvidenceWaste(0, false); _rig.Frame(new(0, 0, 8), 62); }
        var now = Stopwatch.GetTimestamp();
        if (_litterEvidenceFrame is > 55 and <= 235 or > 255 and <= 435)
        {
            _litterFrameTimes.Add(Stopwatch.GetElapsedTime(_litterEvidencePrevious, now).TotalMilliseconds);
            _litterDrawCalls.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
        }
        _litterEvidencePrevious = now;
        if (_litterEvidenceFrame == 235)
        {
            _litterBaseline = new { frameMs = LitterStats(_litterFrameTimes), drawCalls = LitterStats(_litterDrawCalls), instances = VisibleLitterInstanceCount };
            _litterFrameTimes.Clear(); _litterDrawCalls.Clear(); SetEvidenceWaste(10000);
        }
        if (_litterEvidenceFrame == 265) SaveLitterFrame("07-ten-thousand-pieces-wide");
        if (_litterEvidenceFrame == 435)
        {
            var pauseExact = _pausedWaspTransforms!.Select((t, i) => t == _binViews["bin.1"].Wasps.Multimesh.GetInstanceTransform(i)).All(x => x);
            var result = new { diagnostic = "staged-litter-render", debug = OS.IsDebugBuild(), resolution = GetWindow().Size.ToString(),
                renderer = RenderingServer.GetCurrentRenderingMethod(), gpu = RenderingServer.GetVideoAdapterName(), vsync = DisplayServer.WindowGetVsyncMode().ToString(),
                baseline = _litterBaseline, stress = new { groundRecords = 10000, overflowRecords = 6, expectedInstances = 10006,
                    actualInstances = VisibleLitterInstanceCount, batches = _litterBatches.Count, frameMs = LitterStats(_litterFrameTimes), drawCalls = LitterStats(_litterDrawCalls) },
                pauseExact, peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
                caveat = "Staged count/render fixture with simulation paused; behavior and current-version restore checked separately. No FPS certification." };
            File.WriteAllText(Path.Combine(_litterEvidenceOutput, "render-evidence.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"LITTER_EVIDENCE_COMPLETE instances={VisibleLitterInstanceCount} pause={pauseExact}");
            GetTree().Quit(VisibleLitterInstanceCount == 10006 && pauseExact ? 0 : 2);
        }
    }
}

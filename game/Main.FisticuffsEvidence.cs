using Festival.ContentAdapter;
using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Festival.Game;

// Opt-in controlled encounter evidence, usable in the exported EXE. Never opens or writes player saves.
public partial class Main
{
    private string? _fisticuffsEvidenceOutput;
    private int _fightEvidenceFrame, _fightEvidenceRemainder;
    private bool _fightEvidenceProfileDisabled, _fightEvidenceChecksOnly;
    private Label _fightEvidenceLabel = null!;
    private ulong _fightEvidenceA, _fightEvidenceB;
    private string _fightEvidenceName = "";
    private readonly List<object> _fightEvidenceChecks = [], _fightEvidenceTrace = [];
    private readonly List<double> _fightEvidenceBaseline = [], _fightEvidenceActive = [];
    private readonly List<double> _fightEvidenceWallBaseline = [], _fightEvidenceWallActive = [];
    private long _fightEvidenceLastWall;
    private sealed record FightFixture(string name, ulong a, ulong b, ulong steward, long startingTick);

    private void FightEvidenceCheck(string name, bool passed)
    {
        _fightEvidenceChecks.Add(new { name, passed });
        if (!passed) throw new InvalidOperationException("Fisticuffs evidence failed: " + name);
    }
    private Node3D FightEvidenceBody(ulong id) => _attendeeVisuals[new(id)];
    private static AnimationPlayer FightEvidencePlayer(Node3D body) => body.FindChildren("*", "AnimationPlayer", true, false)
        .OfType<AnimationPlayer>().First(p => p.HasAnimationLibrary("fisticuffs"));
    private static Skeleton3D FightEvidenceSkeleton(Node3D body) => body.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().First();
    private string FightEvidenceFingerprint()
    {
        string Actor(ulong id)
        {
            var body = FightEvidenceBody(id); var player = FightEvidencePlayer(body); var skeleton = FightEvidenceSkeleton(body);
            return body.Transform + "|" + player.CurrentAnimation + "|" + player.CurrentAnimationPosition.ToString("R") + "|" +
                string.Join(";", Enumerable.Range(0, skeleton.GetBoneCount()).Select(i => skeleton.GetBonePose(i).ToString())) + "|" +
                string.Join(";", body.FindChildren("*", "Node3D", true, false).OfType<Node3D>().Select(n => n.Name + ":" + n.Visible));
        }
        return Actor(_fightEvidenceA) + "\n" + Actor(_fightEvidenceB) + "\n" +
            string.Join(";", _fightViews.Values.Select(v => $"{v.State.First}:{v.State.Second}:{v.State.Turn}:{v.State.Seconds:R}"));
    }
    private void LoadFightEvidence(string name, SessionPersistenceSnapshot? continuation = null, bool startFight = true)
    {
        ResetFisticuffsPresentation(); ResetImmersionHeldVisuals(); ResetLivePerformancePresentation();
        foreach (var body in _attendeeVisuals.Values) { body.Hide(); body.QueueFree(); }
        _attendeeVisuals.Clear(); _attendeePickRegistry.Clear(); _lastPresentedPersonPositions.Clear(); _personPace.Clear();
        var folder = Path.Combine(_fisticuffsEvidenceOutput!, "fixtures");
        var meta = JsonSerializer.Deserialize<FightFixture[]>(File.ReadAllText(Path.Combine(folder, "manifest.json")))!.Single(m => m.name == name);
        var snapshot = continuation ?? JsonSerializer.Deserialize<SessionPersistenceSnapshot>(File.ReadAllText(Path.Combine(folder, name + ".json")))!;
        var loaded = GameSession.Restore(snapshot); if (!loaded.IsSuccess) throw new InvalidOperationException(loaded.Error);
        _host = new SessionHost(loaded.Session!, Path.Combine(_fisticuffsEvidenceOutput!, "unused-diagnostic-save"), _saveCompatibility);
        _fightEvidenceA = meta.a; _fightEvidenceB = meta.b; _fightEvidenceName = name; _fightEvidenceRemainder = 0;
        _foundationPresentation.Reset(_session.CaptureObservation()); _host.Clock.ResetBoundary(); SyncBuildWorld(); BuildAttendee();
        _hudWorkspaceOpen = _buildDrawerOpen = false; _boxOfficeSeenAttempt = 1; RefreshPreparationHud(); Dock.Readiness?.Hide();
        if (continuation is null && startFight)
        {
            var before = _session.CaptureObservation().NavigationAgents.Select(n => (n.Id, n.XMillimetres, n.ZMillimetres)).ToArray();
            typeof(GameSession).GetMethod("BeginDisorderFight", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(_session, [meta.a, meta.b, "labelled:integrated-fisticuffs-evidence"]);
            FightEvidenceCheck(name + "-no-teleport-at-fight-start", before.SequenceEqual(_session.CaptureObservation().NavigationAgents.Select(n => (n.Id, n.XMillimetres, n.ZMillimetres))));
        }
        AdvancePreparationPresentation(0);
        _rig.Frame((FightEvidenceBody(meta.a).Position + FightEvidenceBody(meta.b).Position) / 2 + new Vector3(0, .6f, 0), 6);
    }
    private void StepFightEvidence(bool simulation)
    {
        if (simulation)
        {
            _fightEvidenceRemainder += 80;
            _session.AdvanceWithoutSnapshot(_fightEvidenceRemainder / 30); _fightEvidenceRemainder %= 30;
        }
        AdvancePreparationPresentation(1.0 / 30);
    }
    private void TraceFightEvidence()
    {
        var pair = FightPresentation.Read(_session).FirstOrDefault(p => p.Key.Initiator == _fightEvidenceA && p.Key.Opponent == _fightEvidenceB);
        var view = pair is null ? null : _fightViews.GetValueOrDefault(pair.Key);
        _fightEvidenceTrace.Add(new { fixture = _fightEvidenceName, frame = _fightEvidenceFrame, tick = _session.CurrentTick,
            gap = pair?.DistanceMetres, moving = pair?.Moving, handler = pair?.Handler,
            first = view?.State.First, second = view?.State.Second, turn = view?.State.Turn,
            nativeFirst = CrowdBodies.FightOwned(FightEvidenceBody(_fightEvidenceA)) ? FightEvidencePlayer(FightEvidenceBody(_fightEvidenceA)).AssignedAnimation.ToString() : "generic",
            nativeSecond = CrowdBodies.FightOwned(FightEvidenceBody(_fightEvidenceB)) ? FightEvidencePlayer(FightEvidenceBody(_fightEvidenceB)).AssignedAnimation.ToString() : "generic",
            positions = _session.CaptureObservation().NavigationAgents.Where(n => n.Id.Value == _fightEvidenceA || n.Id.Value == _fightEvidenceB || n.Id.Value == pair?.Handler)
                .Select(n => new { id = n.Id.Value, n.XMillimetres, n.ZMillimetres }).ToArray(),
            held = _session.CaptureImmersion()!.People.Where(p => p.AgentId == _fightEvidenceA || p.AgentId == _fightEvidenceB).Select(p => new { p.AgentId, p.Held }).ToArray(),
            incident = _session.CaptureDisorder()!.Incidents.LastOrDefault(i => i.InitiatorId == _fightEvidenceA && i.OpponentId == _fightEvidenceB) });
    }
    private void FightEvidenceFreezeChecks()
    {
        void Freeze(string name, Action enter, Action leave)
        {
            enter(); var before = FightEvidenceFingerprint(); var hash = _session.CaptureSnapshot().AuthoritativeHash;
            for (var i = 0; i < 5; i++) AdvancePreparationPresentation(600);
            FightEvidenceCheck(name, before == FightEvidenceFingerprint() && hash == _session.CaptureSnapshot().AuthoritativeHash); leave();
        }
        Freeze("pause-exact-pose-and-pending-beats", () => _session.Execute(CampaignEnvelope(new SetPausedCommand(true))), () => _session.Execute(CampaignEnvelope(new SetPausedCommand(false))));
        Freeze("start-menu-exact-pose-and-pending-beats", BuildStartSplash, StartMenu.Close);
        var hiddenParent = new Node3D(); AddChild(hiddenParent);
        var body = FightEvidenceBody(_fightEvidenceA); var oldParent = body.GetParent(); body.Reparent(hiddenParent);
        Freeze("hidden-parent-freezes-both-participants", hiddenParent.Hide, hiddenParent.Show);
        body.Reparent(oldParent); hiddenParent.QueueFree();
        var prior = _fightViews.Values.Single().State.Seconds; AdvanceFisticuffsPresentation(1.0 / 30);
        FightEvidenceCheck("resume-no-catchup", _fightViews.Values.Single().State.Seconds <= prior + .034);
        var snapshot = _session.CapturePersistenceSnapshot(); LoadFightEvidence(_fightEvidenceName, snapshot);
        AdvanceFisticuffsPresentation(1.0 / 30);
        FightEvidenceCheck("restored-active-fight-baselines-guard", _fightViews.Values.Single().State is { Turn: 0, First.Pose: FightPose.Guard, Second.Pose: FightPose.Guard });
        var preparation = _session.CapturePreparation()!;
        var field = typeof(GameSession).GetProperty("PreparationView", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var status in new[] { PreparationStatus.Failed, PreparationStatus.Finished })
        {
            field.SetValue(_session, preparation with { Status = status });
            var before = new[] { _fightEvidenceA, _fightEvidenceB }.Select(id => FightEvidencePlayer(FightEvidenceBody(id)).CurrentAnimationPosition).ToArray();
            for (var i = 0; i < 5; i++) AdvanceFisticuffsPresentation(600);
            FightEvidenceCheck("terminal-" + status + "-frozen-drained", _fightViews.Count == 0 && before.SequenceEqual(new[] { _fightEvidenceA, _fightEvidenceB }.Select(id => FightEvidencePlayer(FightEvidenceBody(id)).CurrentAnimationPosition)));
        }
        // Terminal tests are labelled status fixtures, never a gameplay resurrection.
        LoadFightEvidence("male-female"); for (var i = 0; i < 60; i++) StepFightEvidence(true);
    }
    private void SaveFightEvidenceImage(string name) => GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_fisticuffsEvidenceOutput!, name + ".png"));
    private void ProcessFisticuffsEvidence()
    {
        try { CaptureFisticuffsEvidence(); }
        catch (Exception error)
        {
            Directory.CreateDirectory(_fisticuffsEvidenceOutput!);
            File.WriteAllText(Path.Combine(_fisticuffsEvidenceOutput!, "failure.txt"), error.ToString());
            GD.PushError(error.ToString()); GetTree().Quit(2);
        }
    }
    private void CaptureFisticuffsEvidence()
    {
        if (_fightEvidenceChecksOnly && _fightEvidenceFrame == 0) _fightEvidenceFrame = 570;
        var frame = _fightEvidenceFrame++;
        if (frame == 0 || (_fightEvidenceChecksOnly && frame == 570))
        {
            Directory.CreateDirectory(_fisticuffsEvidenceOutput!); Directory.CreateDirectory(Path.Combine(_fisticuffsEvidenceOutput!, "paired"));
            Directory.CreateDirectory(Path.Combine(_fisticuffsEvidenceOutput!, "handling"));
            Engine.MaxFps = 0; DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            var canvas = new CanvasLayer { Layer = 90 }; AddChild(canvas);
            _fightEvidenceLabel = new Label { Position = new(20, 72) }; canvas.AddChild(_fightEvidenceLabel); _fightEvidenceLabel.AddThemeFontSizeOverride("font_size", 17);
            LoadFightEvidence("male-female");
            _rig.Rotate(1); // Side-on to the preferred diagonal, so both bodies remain readable.
        }
        if (frame < 360)
        {
            if (frame is 45 or 90 or 120 or 180 or 240)
            {
                if (frame == 120) _rig.Rotate(1);
                _rig.Frame((FightEvidenceBody(_fightEvidenceA).Position + FightEvidenceBody(_fightEvidenceB).Position) / 2 + new Vector3(0, .6f, 0),
                    frame is 45 or 180 ? 18 : frame is 90 or 240 ? 62 : 6);
            }
            _fightEvidenceLabel.Text = "CONTROLLED GAME FIXTURE · comic jab bursts / real movement\n" + $"male / female · tick {_session.CurrentTick} · camera size {_rig.Camera.Size} · visual acting only";
            StepFightEvidence(true); TraceFightEvidence();
            SaveFightEvidenceImage($"paired/{frame:D4}"); return;
        }
        if (frame == 360) LoadFightEvidence("handling");
        if (frame < 570)
        {
            _fightEvidenceLabel.Text = "CONTROLLED GAME FIXTURE · actual physical steward response\nDispatch and travel preserve acting; actual handling cancels swings.";
            StepFightEvidence(true); TraceFightEvidence(); SaveFightEvidenceImage($"handling/{frame - 360:D4}"); return;
        }
        var names = new[] { "male-female", "male-male", "female-female", "female-male", "male-female-north", "guest-steward" };
        if (frame < 570 + names.Length * 100)
        {
            var local = (frame - 570) % 100; var index = (frame - 570) / 100;
            if (local == 0) LoadFightEvidence(names[index]);
            if (local < 60) StepFightEvidence(true); else StepFightEvidence(false); // Labelled pose sampling holds authority after real approach.
            _fightEvidenceLabel.Text = $"CONTROLLED GAME FIXTURE · {names[index]} · {_rig.OrientationName}\nNative poses, real navigation spacing. After approach: authority held for inspection.";
            if (local == 60)
            {
                var pair = FightPresentation.Read(_session).Single();
                FightEvidenceCheck(names[index] + "-preferred-contact-spacing", !pair.Moving && pair.DistanceMetres >= .7 && pair.DistanceMetres <= .8);
                foreach (var id in new[] { _fightEvidenceA, _fightEvidenceB })
                {
                    var body = FightEvidenceBody(id); var player = FightEvidencePlayer(body); var skeleton = FightEvidenceSkeleton(body);
                    FightEvidenceCheck(names[index] + "-native-library-17-bones-8-clips-51-tracks-" + id,
                        skeleton.GetBoneCount() == 17 && player.GetAnimationLibrary("fisticuffs").GetAnimationList().Count == 8 &&
                        player.GetAnimation("fisticuffs/fight_swing_l").GetTrackCount() == 51);
                    foreach (var foot in new[] { "LeftFoot", "RightFoot" })
                    {
                        var bone = skeleton.FindBone(foot);
                        FightEvidenceCheck(names[index] + "-" + foot + "-restored-after-walk-" + id,
                            skeleton.GetBoneGlobalPose(bone).Origin.DistanceTo(skeleton.GetBoneGlobalRest(bone).Origin) < .001);
                    }
                    var other = FightEvidenceBody(id == _fightEvidenceA ? _fightEvidenceB : _fightEvidenceA);
                    var target = (other.Position - body.Position).Normalized();
                    FightEvidenceCheck(names[index] + "-face-opponent-" + id, (-body.Transform.Basis.Z).Dot(target) > .99);
                }
                TraceFightEvidence();
            }
            if (local == 66 && index == 0) FightEvidenceFreezeChecks();
            if (local is 70 or 77 or 84 or 91) _rig.Rotate(1);
            if (local is 73 or 80 or 87 or 94) { SaveFightEvidenceImage(names[index] + "-" + _rig.OrientationName); TraceFightEvidence(); }
            if (local == 95) _rig.Frame(_rig.Focus, 18);
            if (local == 98) SaveFightEvidenceImage(names[index] + "-minimum18");
            return;
        }
        var profileFrame = frame - 1170;
        if (profileFrame == 0)
        {
            LoadFightEvidence("male-female"); for (var i = 0; i < 60; i++) StepFightEvidence(true);
            _fightEvidenceProfileDisabled = true; ResetFisticuffsPresentation(); _fightEvidenceLastWall = Stopwatch.GetTimestamp();
        }
        if (profileFrame == 150) { _fightEvidenceProfileDisabled = false; _fightEvidenceLastWall = Stopwatch.GetTimestamp(); }
        if (profileFrame < 300)
        {
            var now = Stopwatch.GetTimestamp(); var wall = Stopwatch.GetElapsedTime(_fightEvidenceLastWall, now).TotalMilliseconds; _fightEvidenceLastWall = now;
            var started = Stopwatch.GetTimestamp(); StepFightEvidence(false); var cpu = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (profileFrame % 150 >= 60)
            {
                (profileFrame < 150 ? _fightEvidenceBaseline : _fightEvidenceActive).Add(cpu);
                (profileFrame < 150 ? _fightEvidenceWallBaseline : _fightEvidenceWallActive).Add(wall);
            }
            _fightEvidenceLabel.Text = "CONTROLLED GAME FIXTURE · two-actor presentation profile\nAuthority held; vsync off; no image writes; whole presentation CPU / wall frame.";
            return;
        }
        if (profileFrame == 300) LoadFightEvidence("male-female", startFight: false);
        if (profileFrame < 400)
        {
            StepFightEvidence(false);
            _fightEvidenceLabel.Text = "CONTROLLED GAME FIXTURE · authoritative Argument, not Fight\nSmall occasional gesture. Paid held item remains retained; authority held for inspection.";
            FightEvidenceCheck("arguments-never-create-fight-view-" + profileFrame, _fightViews.Count == 0);
            if (profileFrame == 380)
            {
                FightEvidenceCheck("empty-handed-argument-native-gesture", FightEvidencePlayer(FightEvidenceBody(_fightEvidenceB)).AssignedAnimation == "fisticuffs/argue_gesture");
                SaveFightEvidenceImage("argument-gesture");
            }
            if (profileFrame == 390) { Bodies.SetRain(FightEvidenceBody(_fightEvidenceB), false, CrowdBodies.RainMode.None, true); _rig.Frame(_rig.Focus, 6); }
            if (profileFrame == 393) SaveFightEvidenceImage("poncho-attachment-inspection");
            if (profileFrame == 395) _rig.Frame(_rig.Focus, 62);
            if (profileFrame == 398) SaveFightEvidenceImage("argument-default62");
            return;
        }
        if (profileFrame == 400)
        {
            LoadFightEvidence("male-female"); for (var i = 0; i < 60; i++) StepFightEvidence(true);
            Bodies.SetRain(FightEvidenceBody(_fightEvidenceA), false, CrowdBodies.RainMode.None, true);
        }
        if (profileFrame < 416)
        {
            StepFightEvidence(false);
            _fightEvidenceLabel.Text = "CONTROLLED GAME FIXTURE · fight with existing poncho attachment\nAttachment explicitly enabled for inspection; original native body / animation.";
            if (profileFrame == 408) SaveFightEvidenceImage("fight-poncho-inspection");
            if (profileFrame == 410) _rig.Frame(_rig.Focus, 62);
            if (profileFrame == 413) SaveFightEvidenceImage("fight-default62");
            return;
        }
        object Stats(List<double> samples) => new { samples = samples.Count, meanMs = samples.Average(), p95Ms = samples.Order().ElementAt((int)(samples.Count * .95)) };
        var options = new JsonSerializerOptions { WriteIndented = true };
        FightEvidenceCheck("diagnostic-never-saves", !Directory.Exists(Path.Combine(_fisticuffsEvidenceOutput!, "unused-diagnostic-save")));
        File.WriteAllText(Path.Combine(_fisticuffsEvidenceOutput!, "checks.json"), JsonSerializer.Serialize(_fightEvidenceChecks, options));
        File.WriteAllText(Path.Combine(_fisticuffsEvidenceOutput!, "trace.json"), JsonSerializer.Serialize(_fightEvidenceTrace, options));
        File.WriteAllText(Path.Combine(_fisticuffsEvidenceOutput!, "profile.json"), JsonSerializer.Serialize(new {
            caveat = "Two actors in a controlled native game fixture. CPU is the entire presentation call; wall frames include the whole game/render. Not an isolated GPU or full-live-crowd benchmark. 60 warmup + 90 measured frames per condition; fixed 30Hz logical delta, vsync disabled, cached libraries, no capture writes.",
            baselineCpu = Stats(_fightEvidenceBaseline), activeCpu = Stats(_fightEvidenceActive), baselineWall = Stats(_fightEvidenceWallBaseline), activeWall = Stats(_fightEvidenceWallActive) }, options));
        GD.Print("FISTICUFFS_EVIDENCE_COMPLETE"); GetTree().Quit();
    }
}

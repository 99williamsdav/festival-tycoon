using Festival.ContentAdapter;
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

// Opt-in staged integrated-game capture and runtime assertions. Never opens or writes a player save.
public partial class Main
{
    private string? _serverEvidenceOutput;
    private int _serverEvidenceFrame;
    private readonly List<object> _serverEvidenceChecks = [];
    private readonly List<object> _serverPerformance = [];
    private readonly List<double> _serverFrameTimes = [], _serverDraws = [];
    private long _serverTimestamp;
    private Label? _serverEvidenceLabel;
    private ServerView? _serverEvidenceView;
    private string _serverEvidenceName = "";
    private readonly string[] _serverVariants = ["bar-male", "bar-female", "food-male", "food-female", "korma-female", "pizza-male"];

    private void ServerCheck(string name, bool passed)
    {
        _serverEvidenceChecks.Add(new { name, passed });
        if (!passed) throw new InvalidOperationException("Server runtime check failed: " + name);
    }
    private static string ServerFingerprint(ServerView v)
    {
        var skeleton = v.Root.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().Single();
        return JsonSerializer.Serialize(new { phase = v.State.Phase.ToString(), seconds = v.State.Seconds, v.Time,
            prop = v.Prop?.ToString(), root = v.Root.Transform.ToString(),
            poses = Enumerable.Range(0, skeleton.GetBoneCount()).Select(i => skeleton.GetBonePose(i).ToString()),
            props = v.Props.Values.Select(p => p.Visible) });
    }
    private void PoseServerEvidence(ServerView view, int remaining = 1, ImmersionProduct? prop = null, bool snap = true)
    {
        var product = prop ?? (Stalls.IsVan(_serverViews.First(k => k.Value == view).Key) ? ImmersionProduct.Chips : ImmersionProduct.Beer);
        view.State.Advance(999, product, remaining, GameSession.ImmersionServiceDuration(product), true,
            view.State.Transaction, -1, _session.CurrentTick, .02, false,
            view.Lengths[ServerPhase.Turn], view.Lengths[ServerPhase.Pickup], view.Lengths[ServerPhase.Offer],
            view.Lengths[ServerPhase.Retract], view.Lengths[ServerPhase.Reset], view.Lengths[ServerPhase.Idle]);
        ApplyServiceServer(view, .02, snap);
    }

    private void CheckServerBlends(ServerView view, string name, bool food)
    {
        var skeleton = view.Root.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().Single();
        var feet = new[] { skeleton.FindBone("LeftFoot"), skeleton.FindBone("RightFoot") };
        var baseline = feet.Select(i => skeleton.GetBoneGlobalPose(i).Origin).ToArray();
        var minClearance = double.MaxValue; var maxTilt = 0.0; var maxFoot = 0.0;
        var products = food ? new[] { ImmersionProduct.Chips, ImmersionProduct.Pizza, ImmersionProduct.Curry }
            : new[] { ImmersionProduct.Beer, ImmersionProduct.SoftDrink, ImmersionProduct.Water };
        foreach (var product in products)
        {
            var duration = GameSession.ImmersionServiceDuration(product);
            for (var sample = 0; sample <= 120; sample++)
            {
                PoseServerEvidence(view, Math.Max(1, duration - duration * sample / 120), product, false);
                foreach (var attachment in view.Root.FindChildren("*", "BoneAttachment3D", true, false).OfType<BoneAttachment3D>()) attachment.OnSkeletonUpdate();
                for (var i = 0; i < feet.Length; i++) maxFoot = Math.Max(maxFoot, skeleton.GetBoneGlobalPose(feet[i]).Origin.DistanceTo(baseline[i]));
                if (view.Prop is not { } prop) continue;
                var item = view.Props[prop];
                maxTilt = Math.Max(maxTilt, Mathf.RadToDeg(item.GlobalBasis.Y.Normalized().AngleTo(Vector3.Up)));
                foreach (var mesh in item.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
                    for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                    {
                        var vertices = mesh.Mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                        foreach (var v in vertices)
                        {
                            var y = view.Vendor.ToLocal(mesh.GlobalTransform * v).Y;
                            minClearance = Math.Min(minClearance, y - (food ? 1.235 : 1.10));
                        }
                    }
            }
            // Deliberate skipped-time jump from low idle to visible offer.
            view.State.Advance(null, null, 0, 0, false, null, -1, _session.CurrentTick, 2, false, .375, .542, .542, .542, 1.042);
            ApplyServiceServer(view, .02); PoseServerEvidence(view, 1, product, false);
        }
        var passed = maxFoot < .001 && minClearance > .15 && maxTilt < 1;
        _serverEvidenceChecks.Add(new { name = name + "-runtime-blends", samples = products.Length * 121, maxFootMotion = maxFoot, minPropClearance = minClearance, maxPropTiltDegrees = maxTilt, passed });
        if (!passed) throw new InvalidOperationException($"Server blend/prop clearance failed: {name} foot={maxFoot} clearance={minClearance} tilt={maxTilt}");
    }

    private void SetupServerEvidence()
    {
        Directory.CreateDirectory(_serverEvidenceOutput!);
        var perk = _session.CapturePerks()!;
        foreach (var command in new SessionCommand[] { new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]), new UseDefaultBuildLayoutCommand() })
        {
            var result = _session.Execute(CampaignEnvelope(command));
            if (!result.IsAccepted) throw new InvalidOperationException(result.Message);
        }
        SyncBuildWorld(); ProcessServiceServers(0);
        _hudWorkspaceOpen = _buildDrawerOpen = false; _boxOfficeSeenAttempt = 1; RefreshPreparationHud();
        Engine.MaxFps = 0; DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        var canvas = new CanvasLayer { Layer = 90 }; AddChild(canvas);
        _serverEvidenceLabel = new Label { Position = new(22, 74) }; canvas.AddChild(_serverEvidenceLabel);
        _serverEvidenceLabel.AddThemeFontSizeOverride("font_size", 18);
        ServerCheck("one-owned-server-per-pitched-vendor", _serverViews.Count == _immersionVendors.Count && _serverViews.Count == 4);
        foreach (var (id, view) in _serverViews)
        {
            var skeleton = view.Root.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().Single();
            ServerCheck(id + "-native-rig-and-placement", skeleton.GetBoneCount() == 17 && view.Root.Scale == Vector3.One &&
                view.Root.Position == (Stalls.IsVan(id) ? new Vector3(0, .47f, .88f) : new Vector3(.35f, 0, .30f)) &&
                view.Root.GetParent() == view.Vendor && view.Root.FindChildren("*", "CollisionObject3D", true, false).Count == 0);
            PoseServerEvidence(view);
        }
        void Freeze(string name, Action enter, Action leave)
        {
            enter(); var before = _serverViews.Values.Select(ServerFingerprint).ToArray();
            var hash = _session.CaptureSnapshot().AuthoritativeHash; ProcessServiceServers(600);
            ServerCheck(name, before.SequenceEqual(_serverViews.Values.Select(ServerFingerprint)) && hash == _session.CaptureSnapshot().AuthoritativeHash);
            leave();
        }
        var preparation = _session.CapturePreparation()!;
        var field = typeof(GameSession).GetProperty("PreparationView", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Freeze("preparation-freeze", () => { }, () => { });
        field.SetValue(_session, preparation with { Status = PreparationStatus.Running });
        var paused = _session.IsPaused;
        if (paused) _session.Execute(CampaignEnvelope(new SetPausedCommand(false)));
        foreach (var status in new[] { PreparationStatus.Failed, PreparationStatus.Finished })
            Freeze("terminal-" + status, () => field.SetValue(_session, preparation with { Status = status }),
                () => field.SetValue(_session, preparation with { Status = PreparationStatus.Running }));
        Freeze("pause", () => _session.Execute(CampaignEnvelope(new SetPausedCommand(true))),
            () => _session.Execute(CampaignEnvelope(new SetPausedCommand(false))));
        Freeze("start-menu", BuildStartSplash, StartMenu.Close);
        Freeze("vendor-hidden", () => { foreach (var v in _serverViews.Values) v.Vendor.Hide(); },
            () => { foreach (var v in _serverViews.Values) v.Vendor.Show(); });
        // Resume from a pose with a small visible increment; no 600s accumulated time.
        foreach (var v in _serverViews.Values)
        {
            v.State.Advance(null, null, 0, 0, false, "runtime-test", _session.CurrentTick, _session.CurrentTick, .02, false,
                .375, .542, .542, .542, 1.042);
            ApplyServiceServer(v, .02);
            ServerCheck("resume-empty-retract-" + v.Vendor.GetInstanceId(), v.Prop is null && v.State.Seconds < .03);
        }
        field.SetValue(_session, preparation);
        _session.Execute(CampaignEnvelope(new SetPausedCommand(paused)));
        var bar = _session.CaptureVendors().First(v => Stalls.IsBar(v.Id));
        var movedView = _serverViews[bar.Id]; var local = movedView.Root.Transform;
        var destination = new[] { new GridCell(bar.Cell.X - 12, bar.Cell.Z), new GridCell(bar.Cell.X, bar.Cell.Z - 12), new GridCell(bar.Cell.X + 12, bar.Cell.Z) }
            .First(cell => _session.ValidateCommand(CampaignEnvelope(new MoveBuildServiceCommand(bar.Id, cell, bar.QuarterTurns))) is null);
        var relocated = _session.Execute(CampaignEnvelope(new MoveBuildServiceCommand(bar.Id, destination, bar.QuarterTurns)));
        ServerCheck("vendor-relocation-command", relocated.IsAccepted); SyncImmersionWorld(); ProcessServiceServers(0);
        ServerCheck("relocation-follows-parent", movedView.Root.Transform == local && movedView.Vendor.Position == ImmersionPosition(destination));
        _session.Execute(CampaignEnvelope(new MoveBuildServiceCommand(bar.Id, bar.Cell, bar.QuarterTurns))); SyncImmersionWorld();
        var move = _session.Execute(CampaignEnvelope(new MoveBuildServiceCommand(bar.Id, bar.Cell, (bar.QuarterTurns + 2) % 4)));
        ServerCheck("vendor-rotation-command", move.IsAccepted); SyncImmersionWorld(); ProcessServiceServers(0);
        ServerCheck("rotation-follows-parent", movedView.Root.Transform == local && movedView.Vendor.RotationDegrees.Y == 90 * ((bar.QuarterTurns + 2) % 4));
        _session.Execute(CampaignEnvelope(new MoveBuildServiceCommand(bar.Id, bar.Cell, bar.QuarterTurns))); SyncImmersionWorld();
        var remove = _session.Execute(CampaignEnvelope(new RemoveBuildServiceCommand(bar.Id)));
        ServerCheck("vendor-removal-command", remove.IsAccepted); SyncImmersionWorld(); ProcessServiceServers(0);
        ServerCheck("removed-rig-stowed-no-registry-orphan", !_serverViews.ContainsKey(bar.Id) && movedView.Root.IsQueuedForDeletion() && !movedView.Root.Visible);
        _session.Execute(CampaignEnvelope(new UseDefaultBuildLayoutCommand())); SyncBuildWorld(); ProcessServiceServers(0);
        ServerCheck("vendor-rebuild-baselines-history", _serverViews.Count == 4 && _serverViews[bar.Id].State.Phase == ServerPhase.Idle);
    }

    private void SetServerCapture(int variant, int orientation, int zoom)
    {
        var name = _serverVariants[variant]; var food = !name.StartsWith("bar", StringComparison.Ordinal);
        var id = name.StartsWith("korma", StringComparison.Ordinal) ? Stalls.Id(BuildServiceKind.FoodVan, 2) : food ? Stalls.FirstVan : Stalls.FirstBar;
        if (name.StartsWith("pizza", StringComparison.Ordinal))
        {
            var command = new ChooseFoodTraderCommand("trader.pizza-the-action", Stalls.FirstVan);
            var result = _session.Execute(CampaignEnvelope(command)); if (!result.IsAccepted) throw new InvalidOperationException(result.Message);
            SyncImmersionWorld(); ProcessServiceServers(0);
        }
        var body = _immersionVendors[id];
        foreach (var v in _immersionVendors.Values) v.Show();
        if (_serverViews.TryGetValue(id, out var prior)) { prior.Root.Hide(); prior.Root.QueueFree(); }
        var view = CreateServiceServer(id, body, name.EndsWith("female", StringComparison.Ordinal) ? "female" : "male");
        _serverViews[id] = view; _serverEvidenceView = view;
        if (orientation == 0 && zoom == 0) CheckServerBlends(view, name, food);
        PoseServerEvidence(view, 1, food ? _session.TraderAt(id)!.Product : ImmersionProduct.Beer);
        _rig.Rotate(orientation - Array.IndexOf(new[] { "South", "West", "North", "East" }, _rig.OrientationName));
        _rig.Frame(body.Position, zoom == 0 ? 6 : zoom == 1 ? 18 : 62);
        _serverEvidenceName = $"{name}-angle{orientation}-{(zoom == 0 ? "inspection6" : zoom == 1 ? "minimum18" : "default62")}";
        _serverEvidenceLabel!.Text = $"STAGED INTEGRATED SERVER · {name} · {_rig.OrientationName} · camera size {_rig.Camera.Size}\nOffer pose; existing livery, counter, furniture and game light. No purchase in this render fixture.";
    }

    private void ProcessServerEvidence()
    {
        if (_serverEvidenceOutput is null) return;
        try { AdvanceServerEvidence(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(2); _serverEvidenceOutput = null; }
    }
    private void AdvanceServerEvidence()
    {
        if (_serverEvidenceFrame == 0) SetupServerEvidence();
        var frame = _serverEvidenceFrame++;
        var captureFrames = _serverVariants.Length * 12 * 5;
        if (frame < captureFrames)
        {
            var index = frame / 5;
            if (frame % 5 == 0) SetServerCapture(index / 12, index % 12 / 3, index % 3);
            if (frame % 5 == 3) GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_serverEvidenceOutput!, _serverEvidenceName + ".png"));
            return;
        }
        var sequence = frame - captureFrames;
        if (sequence < 100)
        {
            if (sequence == 0) { SetServerCapture(1, 2, 0); Directory.CreateDirectory(Path.Combine(_serverEvidenceOutput!, "sequence")); }
            if (sequence > 0) GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_serverEvidenceOutput!, "sequence", $"{sequence - 1:000}.png"));
            var view = _serverEvidenceView!;
            if (sequence < 60) PoseServerEvidence(view, Math.Max(1, 160 - sequence * 160 / 60), ImmersionProduct.Beer, false);
            else
            {
                view.State.Advance(null, null, 0, 0, false, "sequence-handoff", _session.CurrentTick, _session.CurrentTick, 1.0 / 30, false,
                    view.Lengths[ServerPhase.Turn], view.Lengths[ServerPhase.Pickup], view.Lengths[ServerPhase.Offer], view.Lengths[ServerPhase.Retract], view.Lengths[ServerPhase.Reset]);
                ApplyServiceServer(view, 1.0 / 30);
            }
            return;
        }
        var sample = sequence - 100;
        if (sample < 240)
        {
            var phase = sample / 120; var within = sample % 120;
            if (within == 0)
            {
                _rig.Frame(new(4, 0, 8), 32); _serverFrameTimes.Clear(); _serverDraws.Clear();
                foreach (var v in _serverViews.Values) v.Root.Visible = phase == 1;
                _serverEvidenceLabel!.Text = "STAGED PREPARATION · SERVER HIDDEN/VISIBLE PERFORMANCE SAMPLE";
            }
            var now = Stopwatch.GetTimestamp();
            if (within >= 30) { _serverFrameTimes.Add(Stopwatch.GetElapsedTime(_serverTimestamp, now).TotalMilliseconds); _serverDraws.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)); }
            _serverTimestamp = now;
            if (within == 119)
            {
                var sorted = _serverFrameTimes.Order().ToArray();
                _serverPerformance.Add(new { serversVisible = phase == 1, frames = 90, medianMs = sorted[45], p95Ms = sorted[85], drawCalls = _serverDraws.Average() });
            }
            return;
        }
        File.WriteAllText(Path.Combine(_serverEvidenceOutput!, "server-evidence.json"), JsonSerializer.Serialize(new { passed = true,
            resolution = GetWindow().Size.ToString(), checks = _serverEvidenceChecks, performance = _serverPerformance,
            caveat = "Staged integrated-game views and 30fps pose sequence, not live purchases or a manual playthrough. Whole-app vsync-off hidden/visible measurement, not isolated GPU cost. Simulation sales/failures/load tested separately. Existing roof/fascia occludes food heads and rear views." }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("SERVER_EVIDENCE_COMPLETE passed=true"); GetTree().Quit();
    }
}

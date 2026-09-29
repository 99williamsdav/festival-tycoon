using Festival.Simulation;
using Festival.ContentAdapter;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Festival.Game;

public partial class Main
{
    private string? _playtestCaptureDirectory;
    private int _playtestStep;
    private double _playtestWait;
    private long _playtestPausedTick;
    private float _playtestMusicPosition;
    private float _playtestResumePosition;
    private float _playtestAmbientPosition;
    private readonly Dictionary<ulong, Transform3D> _playtestTransforms = [];
    private readonly Dictionary<ulong, double> _playtestAnimationPositions = [];
    private ImmersionSnapshot? _playtestOriginalImmersion;
    private Vector3 _playtestCameraFocus;

    private static void PlaytestRequire(bool valid, string message)
    { if (!valid) throw new InvalidOperationException("PLAYTEST_ASSERT " + message); }
    private void PlaytestSet(string field, object value) => typeof(GameSession).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session, value);
    private void PlaytestImage(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        PlaytestRequire(image.GetWidth() == GetWindow().Size.X && image.GetHeight() == GetWindow().Size.Y, "true native resolution");
        PlaytestRequire(image.SavePng(Path.Combine(_playtestCaptureDirectory!, name + ".png")) == Error.Ok, "save screenshot");
        GD.Print("PLAYTEST_IMAGE " + name);
    }
    private AnimationPlayer[] PlaytestPlayers() => _attendeeVisuals.Values.SelectMany(body => body.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>()).ToArray();
    private Node3D[] PlaytestNodes() => _attendeeVisuals.Values.SelectMany(body => body.FindChildren("*", "Node3D", true, false).OfType<Node3D>().Prepend(body)).ToArray();
    private void PlaytestToilet(bool doorOpen, bool owned)
    {
        var m = _playtestOriginalImmersion!; var toilet = m.Toilet!;
        var id = _session.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest).AgentId;
        PlaytestSet("_immersion", m with { Toilet = toilet with { DoorOpen = doorOpen, OwnerId = owned ? id : null } });
        SyncToiletWorld(); SelectToilet();
        PlaytestRequire(_toiletFreeIndicator!.Visible == (doorOpen || !owned) && _toiletOccupiedIndicator!.Visible == (!doorOpen && owned), "actual toilet door signal");
        PlaytestRequire(!_inspectorBody.Text.Contains("Facing") && !_inspectorBody.Text.Contains("preparation placement"), "concise context");
    }
    private void ProcessPlaytestCapture(double delta)
    {
        if (_playtestCaptureDirectory is null) return;
        _playtestWait += delta;
        if (_playtestWait < (_playtestStep is 2 or 4 or 5 ? 1.0 : .2)) return;
        _playtestWait = 0;
        try
        {
            switch (_playtestStep++)
            {
                case 0:
                    var perk = _session.CapturePerks()!;
                    CommitEquipmentAction(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
                    CommitEquipmentAction(new UseDefaultBuildLayoutCommand());
                    CommitEquipmentAction(new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"]));
                    foreach (var offer in new[] { "staff.steward", "maintenance.worker", "equipment.buy" }) CommitEquipmentAction(new AcceptPreparationOfferCommand(offer));
                    RefreshPreparationHud();
                    PlaytestRequire(_hudAttendance!.Text == "Attendees\n0 / 20", "preparation guest count");
                    // Labelled layout fixture: preparation has no natural emergencies.
                    _urgentAlertDisplay.Observe([new("layout", "Layout fixture: urgent text below the top bar", 100)]); RenderUrgentAlerts();
                    break;
                case 1:
                    PlaytestImage("01-preparation-layout");
                    PreparationStart();
                    PlaytestRequire(_session.PreparedStatus == PreparationStatus.Running, "festival started");
                    _session.AdvanceWithoutSnapshot(5200);
                    _foundationPresentation.Reset(_session.CaptureObservation());
                    var immersion = _session.CaptureImmersion()!;
                    var tipsy = immersion.People.First(p => p.VendorId is null && _session.ImmersionHandsAvailable(p.AgentId));
                    PlaytestSet("_immersion", immersion with { People = immersion.People.Select(p => p.AgentId == tipsy.AgentId ? p with { Intoxication = 5500 } : p).ToArray() });
                    _hudWorkspaceOpen = false; _buildDrawerOpen = false; _hudProgrammeOpen = false; ClearSelection();
                    _focus = new(-12, 0, 15); _camera.Size = 30; ApplyCamera();
                    AdvancePreparationPresentation(0); RefreshPreparationHud();
                    break;
                case 2:
                    PlaytestRequire(_stageMusic is { Playing: true, StreamPaused: false }, "actual live music is playing");
                    PlaytestRequire(PlaytestPlayers().Length > 0 && PlaytestPlayers().Any(p => p.CurrentAnimationPosition > 0), "actual instrument animation advances");
                    PlaytestRequire(_hudAttendance!.Text == $"Attendees\n{_session.OnSiteAttendeeCount} / 20", "live guest-only count");
                    PlaytestImage("02-live-before-pause");
                    _hudPause!.EmitSignal(BaseButton.SignalName.Pressed);
                    break;
                case 3:
                    PlaytestRequire(_session.IsPaused && _stageMusic!.StreamPaused, "actual HUD pause pauses music");
                    _playtestPausedTick = _session.CurrentTick; _playtestMusicPosition = _stageMusic!.GetPlaybackPosition();
                    PlaytestRequire(_ambientCrowd is { Stream: not null, StreamPaused: true }, "ambient crowd shares pause");
                    _playtestAmbientPosition = _ambientCrowd!.GetPlaybackPosition();
                    foreach (var node in PlaytestNodes()) _playtestTransforms[node.GetInstanceId()] = node.Transform;
                    foreach (var player in PlaytestPlayers()) _playtestAnimationPositions[player.GetInstanceId()] = player.CurrentAnimationPosition;
                    _playtestCameraFocus = _focus; Pan(new Vector2(.5f, 0));
                    break;
                case 4:
                    PlaytestRequire(_session.CurrentTick == _playtestPausedTick, "pause freezes simulation tick");
                    PlaytestRequire(Math.Abs(_stageMusic!.GetPlaybackPosition() - _playtestMusicPosition) < .04, "pause preserves music playback position");
                    PlaytestRequire(Math.Abs(_ambientCrowd!.GetPlaybackPosition() - _playtestAmbientPosition) < .04, "pause preserves ambience playback position");
                    foreach (var node in PlaytestNodes()) PlaytestRequire(node.Transform.IsEqualApprox(_playtestTransforms[node.GetInstanceId()]), "paused character transform " + node.Name);
                    foreach (var player in PlaytestPlayers()) PlaytestRequire(Math.Abs(player.CurrentAnimationPosition - _playtestAnimationPositions[player.GetInstanceId()]) < .0001, "paused instrument playback");
                    PlaytestRequire(_focus != _playtestCameraFocus, "camera remains responsive");
                    PlaytestImage("03-paused-characters-and-music");
                    _hudPause!.EmitSignal(BaseButton.SignalName.Pressed);
                    break;
                case 5:
                    _playtestResumePosition = _stageMusic!.GetPlaybackPosition();
                    PlaytestRequire(!_session.IsPaused && !_stageMusic.StreamPaused && _session.CurrentTick > _playtestPausedTick, "resume releases music and simulation");
                    PlaytestRequire(_playtestResumePosition > _playtestMusicPosition + .2, "resume continues music rather than restarting");
                    PlaytestRequire(!_ambientCrowd!.StreamPaused && _ambientCrowd.GetPlaybackPosition() > _playtestAmbientPosition + .2, "ambience resumes without restart");
                    PlaytestRequire(PlaytestPlayers().Any(p => Math.Abs(p.CurrentAnimationPosition - _playtestAnimationPositions[p.GetInstanceId()]) > .01), "instrument animation resumes");
                    _hudPause!.EmitSignal(BaseButton.SignalName.Pressed);
                    var medical = _session.CaptureMedical()!; var guests = _session.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed).Take(6).Select(p => p.AgentId).ToArray();
                    PlaytestSet("_medical", medical with { Needs = medical.Needs.Select(n => guests.Contains(n.AgentId) ? n with { Stage = MedicalStage.Distress } : n).ToArray() });
                    RefreshPreparationHud();
                    PlaytestRequire(_hudAlertActions.Count == 4 && _hudAlerts is not PanelContainer && _hudAlerts!.Position == new Vector2(15,70), "bounded top-left plain alerts");
                    break;
                case 6:
                    PlaytestImage("04-live-alerts-bounded");
                    _hudAlertToggle!.EmitSignal(BaseButton.SignalName.Pressed);
                    PlaytestRequire(_hudAlertActions.Count <= 4, "paged alerts stay bounded");
                    break;
                case 7:
                    PlaytestImage("05-live-alerts-next-page");
                    _playtestOriginalImmersion = _session.CaptureImmersion();
                    _focus = _toiletBody!.Position; _camera.Size = 10; _orientation = 2; ApplyCamera();
                    PlaytestToilet(true, true);
                    break;
                case 8:
                    PlaytestImage("06-toilet-entry-green"); PlaytestToilet(false, true); break;
                case 9:
                    PlaytestImage("07-toilet-closed-red"); PlaytestToilet(true, true); break;
                case 10:
                    PlaytestImage("08-toilet-exit-green"); PlaytestToilet(false, false); break;
                case 11:
                    PlaytestImage("09-toilet-empty-green");
                    PlaytestSet("_immersion", _playtestOriginalImmersion!);
                    File.WriteAllText(Path.Combine(_playtestCaptureDirectory, "presentation.json"), JsonSerializer.Serialize(new {
                        mode = "scripted actual presentation; labelled needs and toilet visual fixtures", resolution = GetWindow().Size.ToString(),
                        pausedTick = _playtestPausedTick, pausedMusicSeconds = _playtestMusicPosition, resumedMusicSeconds = _playtestResumePosition, pausedAmbientSeconds = _playtestAmbientPosition, ambienceContinued = true,
                        pausedNodeCount = _playtestTransforms.Count, pausedAnimationPlayers = _playtestAnimationPositions.Count,
                        pauseDurationSeconds = 1, cameraResponsive = true, guestOnlyHud = true, boundedPlainAlerts = true, doorSignalPhases = 4,
                        manualWalkthrough = false, audioAudibilityManuallyVerified = false
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    GD.Print("PLAYTEST_CAPTURE_COMPLETE " + _playtestCaptureDirectory); _playtestCaptureDirectory = null; GetTree().Quit(); break;
            }
        }
        catch (Exception error) { GD.PushError("PLAYTEST_CAPTURE_FAILED " + error); _playtestCaptureDirectory = null; GetTree().Quit(2); }
    }
}

using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Festival.Game;

public partial class Main
{
    private readonly Dictionary<EntityId, Node3D> _performerInstruments = [];
    private readonly Dictionary<EntityId, Vector3> _lastPresentedPersonPositions = [];
    private Node3D? _stageDrumKit;
    private AudioStreamPlayer? _stageMusic;
    private AudioStreamPlayer? _crowdBoo;
    private AudioStreamPlayer? _crowdCheer;
    private AudioStreamPlayer? _bandEntryApplause;
    private bool _bandEntryReactionPlayed;
    private int _stageAudioBus = -1;
    private int _lastReactionSequence = -1;
    private LiveSetStage? _presentedSetStage;
    private string? _liveCaptureDirectory;
    private int _liveCaptureFrame;
    private int _liveMeasurementTier;
    private Label? _liveSetCue;
    private Button? _stageMuteButton;
    private bool _stageMuted;
    private OmniLight3D[]? _stageLights;
    private Label3D? _stageWorldCue;
    private float _booFadeSeconds;
    private float _booTargetDb;

    private void ResetLivePerformancePresentation()
    {
        _performerInstruments.Clear();
        _lastPresentedPersonPositions.Clear();
        _presentedSetStage = null;
        _lastReactionSequence = _session.CaptureLivePerformance()?.ReactionSequence ?? -1;
        _bandEntryReactionPlayed = _session.CaptureLivePerformance()?.Performers.Any(item => item.OnStage) ?? false;
        _stageMusic?.Stop();
        _crowdBoo?.Stop();
        _crowdCheer?.Stop();
        _bandEntryApplause?.Stop();
        ResetIncidentAudioPresentation();
        _booFadeSeconds = 0;
        if (_liveSetCue is not null) _liveSetCue.Text = "STAGE • awaiting booking";
        if (_stageWorldCue is not null)
        {
            _stageWorldCue.Text = "SET READY";
            _stageWorldCue.Modulate = new Color("f7e4a4");
        }
        if (_stageLights is not null)
            foreach (var light in _stageLights) light.LightEnergy = 0;
    }

    private void UpdatePersonFacing(EntityId id, Node3D visual, Vector3 position,
        AgentNavigationAction action, bool watchingStage, bool onStage, double delta)
    {
        var hasPrevious = _lastPresentedPersonPositions.TryGetValue(id, out var previous);
        _lastPresentedPersonPositions[id] = position;
        var direction = position - previous;
        direction.Y = 0;
        // All protected people face actual rendered travel. A desired path is not a visual heading.
        if (onStage)
        {
            // Approved GLBs face -Z; the yawed trailer opens toward world +X.
            visual.Rotation = new Vector3(0, -Mathf.Pi / 2f, 0);
            return;
        }
        if (action != AgentNavigationAction.Travelling || !hasPrevious || direction.LengthSquared() < 0.000036f)
        {
            if (!watchingStage) return;
            direction = new Vector3(-16f - position.X, 0, 11f - position.Z);
        }
        if (direction.LengthSquared() < 0.000036f) return;
        var targetYaw = Mathf.Atan2(-direction.X, -direction.Z);
        visual.Rotation = new Vector3(0, Mathf.LerpAngle(visual.Rotation.Y, targetYaw,
            Mathf.Clamp((float)delta * 7f, 0f, 1f)), 0);
    }

    private static string PerformerBodyPath(string name) => name switch
    {
        "Alex Reed" => "res://assets/characters/lwf_performer_frontperson_body_v1.glb",
        "Blair Moss" => "res://assets/characters/lwf_performer_bassist_body_v1.glb",
        "Kit Rowan" => "res://assets/characters/lwf_performer_drummer_body_v1.glb",
        _ => "res://assets/characters/lwf_generic_attendee_v1.glb"
    };

    private static string PerformerKitPath(string name) => name switch
    {
        "Alex Reed" => "res://assets/characters/lwf_performer_acoustic_guitar_kit_v1.glb",
        "Blair Moss" => "res://assets/characters/lwf_performer_solid_bass_kit_v1.glb",
        _ => throw new InvalidOperationException("The drum kit is a fixed stage prop, not a body attachment.")
    };

    private void RefreshLivePerformanceHud()
    {
        var live = _session.CaptureLivePerformance();
        if (_liveSetCue is null) return;
        if (live is null)
        {
            _liveSetCue.Text = "STAGE • awaiting booking";
            return;
        }
        var listeners = live.Listeners.Count(item => item.AtPlace);
        var elapsed = live.StartedTick < 0 ? 0 : Math.Min(GameSession.LiveSetDurationTicks, _session.CurrentTick - live.StartedTick);
        _liveSetCue.Text = live.Stage == LiveSetStage.BeforeSet
            ? $"STAGE • PRE-SHOW • starts in {Math.Max(0, (live.PlannedTick - _session.CurrentTick + 79) / 80)}s\n" +
              $"Band {live.Performers.Count(item => item.OnStage)}/3 on deck • listeners {listeners}/{live.Listeners.Length}"
            : $"STAGE • {live.Stage} • {elapsed / 80}s / 120s\n" +
              $"Band {live.Performers.Count(item => item.OnStage)}/3 • listeners {listeners}/{live.Listeners.Length} • cue {live.LastReaction}";
    }

    private void RefreshLivePersonInspector(EntityId id, NavigationObservation navigation, PreparationSnapshot preparation)
    {
        var person = preparation.People.Single(item => item.AgentId == id.Value);
        RefreshSatisfactionBar(person.Role == ProtectedPersonRole.Guest ? person.Satisfaction : null);
        var live = _session.CaptureLivePerformance();
        var medical = _session.CaptureMedical();
        var need = medical?.Needs.SingleOrDefault(item => item.AgentId == id.Value);
        RefreshMedicalNeedBars(need);
        var listening = live?.Listeners.SingleOrDefault(item => item.AgentId == id.Value);
        var performer = live?.Performers.SingleOrDefault(item => item.AgentId == id.Value);
        var detail = listening is not null
            ? $"FIT  {(listening.Enthusiasm >= 60 ? "booked style" : "other style")} • interest {listening.Enthusiasm}%\n" +
              $"CHOICE  {(listening.Enthusiasm >= 90 ? "strong fan/front" : listening.Enthusiasm >= 60 ? "comfortable middle" : "casual/rear")} • safe route/space\n" +
              $"PLACE  {(listening.Place is { } place ? $"{place.X},{place.Z} ({(place.X <= 107 ? "front" : place.X <= 113 ? "middle" : "rear")})" : "not reserved")} • {(listening.AtPlace ? "watching" : "travelling/not watching")}\n" +
              $"LISTENED  {listening.ListenedTicks / 80}s • enjoyment +{listening.EnjoymentEarned / 100m:0.00}%"
            : performer is not null ? $"STAGE  {performer.StageCell.X},{performer.StageCell.Z} • {(performer.OnStage ? "on stage" : "travelling/exit")}\n" +
              $"INSTRUMENT  {(performer.InstrumentAttached ? "attached for set" : "detached")}" :
              person.Name == "Jordan Hale" ? "STEWARD • autonomous physical route" : "STAFF • autonomous physical route";
        _highlight.Position = _attendeeVisuals[id].Position + new Vector3(0, 0.08f, 0);
        _inspectorTitle.Text = $"{person.Name} • {(person.Name == "Jordan Hale" ? "Steward" : person.Role)}";
        _inspectorBody.Text = $"{navigation.Action} • {StewardWording(navigation.IntentId ?? "None")}\n" +
            $"POSITION  {navigation.XMillimetres / 1000.0:0.00} m, {navigation.ZMillimetres / 1000.0:0.00} m\n" +
            $"Admitted {person.Admitted}\n" +
            (need is null ? "" : $"HOT • thirst {need.Thirst / 100m:0}% • heat {need.HeatExposure / 100m:0}% • " +
                $"{(need.Profile == MedicalNeedProfile.Performer ? need.Stage : id.Value == medical!.AtRiskGuestId ? medical.Stage : need.Stage)}\n" +
                $"INTENT {need.Intent} • {StewardWording(need.Reason)}\n" +
                (medical!.WaterOwnerId == id.Value
                    ? $"DRINKING • thirst {need.Thirst / 100m:0}% • heat {need.HeatExposure / 100m:0}%\n"
                    : "")) + DisorderPersonInspectorText(id.Value) + detail;
    }

    private void EnsureStageDrumKit()
    {
        if (_stageDrumKit is not null) return;
        var drumMark = TraversalGrid.CellCentre(new GridCell(93, 152));
        _stageDrumKit = AddAsset("res://assets/characters/lwf_performer_compact_drum_kit_v1.glb",
            new Vector3(drumMark.XMillimetres / 1000f, 1.19f, drumMark.ZMillimetres / 1000f));
        _stageDrumKit.RotationDegrees = new Vector3(0, -90, 0);
    }

    private void EnsureStageAudio()
    {
        EnsureStageDrumKit();
        if (_stageMusic is not null) return;
        _stageAudioBus = AudioServer.GetBusCount();
        AudioServer.AddBus(_stageAudioBus);
        AudioServer.SetBusName(_stageAudioBus, "Outdoor Stage");
        AudioServer.SetBusSend(_stageAudioBus, "Master");
        AudioServer.SetBusMute(_stageAudioBus, _stageMuted);
        AudioServer.AddBusEffect(_stageAudioBus, new AudioEffectLowPassFilter { CutoffHz = 12000 });
        AudioServer.AddBusEffect(_stageAudioBus, new AudioEffectReverb { RoomSize = 0.12f, Wet = 0.04f, Dry = 1f });
        _stageMusic = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = -8 };
        AddChild(_stageMusic);
        _crowdBoo = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = -20,
            Stream = GD.Load<AudioStream>("res://assets/audio/264378__howardv__crowd-booing.wav") };
        AddChild(_crowdBoo);
        _crowdCheer = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = -24,
            Stream = GD.Load<AudioStream>("res://assets/audio/set_start_cheer.wav") };
        AddChild(_crowdCheer);
        _bandEntryApplause = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = -24 };
        AddChild(_bandEntryApplause);
        _stageLights = [new OmniLight3D { Position = new Vector3(-18, 2.2f, 10), OmniRange = 8,
            LightColor = new Color("ffd18a"), LightEnergy = 0.8f },
            new OmniLight3D { Position = new Vector3(-14.5f, 2.2f, 10), OmniRange = 8,
                LightColor = new Color("eaa8ff"), LightEnergy = 0.8f }];
        foreach (var light in _stageLights) AddChild(light);
        _stageWorldCue = new Label3D { Position = new Vector3(-16, 3.4f, 11),
            FontSize = 56, PixelSize = 0.011f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = new Color("f7e4a4"), Text = "SET READY" };
        AddChild(_stageWorldCue);
    }

    private void AdvanceLivePerformancePresentation(double delta)
    {
        var live = _session.CaptureLivePerformance();
        if (live is null) return;
        EnsureStageAudio();
        var roster = _session.CapturePreparation()!.People;
        var navigation = _session.CaptureObservation().NavigationAgents.ToDictionary(item => item.Id);
        var collapsed = _session.CaptureMedical()?.Needs.Where(item => item.Intent == MedicalIntent.Collapsed)
            .Select(item => item.AgentId).ToHashSet() ?? [];
        foreach (var performer in live.Performers)
        {
            var id = new EntityId(performer.AgentId);
            if (!_attendeeVisuals.TryGetValue(id, out var body)) continue;
            if (performer.InstrumentAttached && !_performerInstruments.ContainsKey(id))
            {
                var name = roster.Single(item => item.AgentId == performer.AgentId).Name;
                var kit = InstantiateAsset(PerformerKitPath(name));
                body.AddChild(kit);
                _performerInstruments.Add(id, kit);
                SetNeutralArmsVisible(body, false);
            }
            else if (!performer.InstrumentAttached && _performerInstruments.Remove(id, out var kit))
            {
                kit.QueueFree();
                SetNeutralArmsVisible(body, true);
            }
            // Heading is assigned with every other protected person's rendered motion.
            // Instrument kits are children, so they follow that same body yaw.
            // The visible south-end stair rises along world X after trailer yaw;
            // the body remains grounded before the stair and after exit.
            var deckRoute = navigation[id].IntentId is "performance.visible-stairs" or "performance.stage-entry" or
                "performance.stage-exit-stair" or "performance.stage-exit-access";
            var ramp = deckRoute || performer.OnStage ? Mathf.Clamp((-body.Position.X - 13.55f) / 1.6f, 0f, 1f) : 0f;
            if (!collapsed.Contains(performer.AgentId))
                body.Position = new Vector3(body.Position.X, 0.04f + ramp * 1.15f, body.Position.Z);
        }
        var power = _session.CaptureEquipment()?.LoadPercent ?? 80;
        var cutoffVisual = power == 0 && live.Stage is LiveSetStage.Live or LiveSetStage.Interrupted;
        _stageWorldCue!.Text = cutoffVisual ?
            live.LastReaction == "sustained-boo" ? "POWER CUT • BOOS" : "POWER CUT • SILENCE" : live.Stage switch
        {
            LiveSetStage.Live => _stageMuted ? "LIVE SET • MUTED" : "LIVE SET",
            LiveSetStage.Interrupted => live.LastReaction == "sustained-boo" ? "POWER CUT • BOOS" : "POWER CUT • SILENCE",
            LiveSetStage.Finished => "SET FINISHED",
            _ => "SET READY"
        };
        _stageWorldCue.Modulate = cutoffVisual ? new Color("ff7777") : new Color("f7e4a4");
        foreach (var light in _stageLights!) light.LightEnergy = live.Stage == LiveSetStage.Live ?
            power == 0 ? 0 : power == 80 ? 0.35f : 0.8f : 0;
        var audible = live.Stage == LiveSetStage.Live && power > 0;
        if (_presentedSetStage != live.Stage)
        {
            if (audible)
            {
                var punk = _session.CapturePreparation()!.AcceptedOffers.Contains("act.punk");
                _stageMusic!.Stream = GD.Load<AudioStream>(punk ? "res://assets/audio/punk_loop_v1.wav" : "res://assets/audio/folk_loop_v1.wav");
                _stageMusic.Play();
            }
            else _stageMusic!.Stop();
            _presentedSetStage = live.Stage;
        }
        if (audible && !_stageMusic!.Playing) _stageMusic.Play();
        if (!audible && _stageMusic!.Playing) _stageMusic.Stop();
        // Use the ground-plane focus instead of the elevated isometric camera position;
        // zoom alters framing, not the physical PA distance.
        var distance = new Vector2(_focus.X + 16f, _focus.Z - 11f).Length();
        var attenuation = Mathf.Clamp(1f - distance / 90f, 0.08f, 1f);
        if (!_bandEntryReactionPlayed && live.Stage == LiveSetStage.BeforeSet && live.Performers.Any(item => item.OnStage))
        {
            _bandEntryReactionPlayed = true;
            var watchers = live.Listeners.Where(item => item.AtPlace).ToArray();
            if (watchers.Length >= 3)
            {
                var averageEnthusiasm = watchers.Average(item => item.Enthusiasm);
                var enthusiastic = watchers.Length >= 20 && averageEnthusiasm >= 65;
                _bandEntryApplause!.Stream = GD.Load<AudioStream>(enthusiastic
                    ? "res://assets/audio/band_entry_enthusiastic_applause.wav"
                    : "res://assets/audio/band_entry_polite_applause.wav");
                var strength = Mathf.Clamp(watchers.Length / 40f, 0.15f, 0.65f) *
                    (0.5f + 0.5f * (float)averageEnthusiasm / 100f);
                _bandEntryApplause.VolumeDb = Mathf.LinearToDb(strength * attenuation);
                _bandEntryApplause.Play();
            }
        }
        var shed = power == 80 ? 0.72f : 1f;
        _stageMusic!.VolumeDb = Mathf.LinearToDb(Math.Max(0.001f, attenuation * shed * 0.42f));
        if (AudioServer.GetBusEffect(_stageAudioBus, 0) is AudioEffectLowPassFilter filter)
            filter.CutoffHz = Mathf.Lerp(1800f, 12000f, attenuation);
        if (_lastReactionSequence != live.ReactionSequence)
        {
            if (live.LastReaction is "set-start-cheer" or "set-start-muted") _bandEntryApplause?.Stop();
            if (live.LastReaction == "set-start-cheer")
            {
                var watchers = live.Listeners.Where(item => item.AtPlace).ToArray();
                var enthusiasm = watchers.Length == 0 ? 0f : (float)watchers.Average(item => item.Enthusiasm) / 100f;
                var size = Mathf.Clamp(watchers.Length / 40f, 0f, 1f);
                var strength = Mathf.Clamp(size * (0.35f + 0.65f * enthusiasm), 0.02f, 0.55f);
                _crowdCheer!.VolumeDb = Mathf.LinearToDb(strength * attenuation);
                _crowdCheer.Play();
            }
            // No immediate gasp on cutoff. Only the provisional CC0 sustained boo is audible.
            if (live.LastReaction == "sustained-boo" && live.Listeners.Count(item => item.AtPlace) >= 5)
            {
                _booTargetDb = Mathf.LinearToDb(Mathf.Clamp(live.Listeners.Count(item => item.AtPlace) / 40f, 0.1f, 0.6f) * attenuation);
                _booFadeSeconds = 0;
                _crowdBoo!.VolumeDb = -48;
                _crowdBoo.Play();
                if (_liveCaptureDirectory is not null) GD.Print($"LIVE_AUDIO boo_start_db={_crowdBoo.VolumeDb:0.00} target_db={_booTargetDb:0.00} fade_seconds=1.8");
            }
            _lastReactionSequence = live.ReactionSequence;
        }
        if (_crowdBoo!.Playing && cutoffVisual)
        {
            _booFadeSeconds += (float)delta;
            _crowdBoo.VolumeDb = Mathf.Lerp(-48f, _booTargetDb, Mathf.Clamp(_booFadeSeconds / 1.8f, 0f, 1f));
        }
        else if (_crowdBoo.Playing && !cutoffVisual) _crowdBoo.Stop();
    }

    private static void SetNeutralArmsVisible(Node3D body, bool visible)
    {
        foreach (var node in body.FindChildren("*", "MeshInstance3D", true, false))
            if (node is MeshInstance3D mesh && mesh.Name.ToString().Contains("Idle", StringComparison.OrdinalIgnoreCase) &&
                mesh.Name.ToString().EndsWith("Arm", StringComparison.OrdinalIgnoreCase))
                mesh.Visible = visible;
    }

    private void ToggleStageMute()
    {
        _stageMuted = !_stageMuted;
        if (_stageAudioBus >= 0) AudioServer.SetBusMute(_stageAudioBus, _stageMuted);
        if (_stageMuteButton is not null) _stageMuteButton.Text = _stageMuted ? "UNMUTE AUDIO" : "MUTE AUDIO";
    }

    private void ProcessLivePerformanceCapture()
    {
        if (_liveCaptureDirectory is null) return;
        Directory.CreateDirectory(_liveCaptureDirectory);
        _liveCaptureFrame++;
        if (_liveCaptureFrame == 4)
        {
            _offerButtons["act.folk"].EmitSignal(Button.SignalName.Pressed);
            _offerButtons["staff.steward"].EmitSignal(Button.SignalName.Pressed);
            _offerButtons["equipment.buy"].EmitSignal(Button.SignalName.Pressed);
            _offerButtons["maintenance.worker"].EmitSignal(Button.SignalName.Pressed);
        }
        if (_liveCaptureFrame == 5)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_liveCaptureDirectory, "pre-start-stage.png"));
            GD.Print($"LIVE_CAPTURE prestart_drum={_stageDrumKit?.IsInsideTree() == true} performers={_session.CaptureLivePerformance()?.Performers.Length ?? 0}");
        }
        if (_liveCaptureFrame == 6) _preparationStart.EmitSignal(Button.SignalName.Pressed);
        if (_liveCaptureFrame == 8)
        {
            _session.AdvanceWithoutSnapshot(1_600);
            _foundationPresentation.Reset(_session.CaptureObservation());
            _foundationClock.ResetBoundary();
            _focus = new Vector3(-16, 0, 11);
            ApplyCamera();
            RefreshPreparationHud();
            GD.Print($"LIVE_CAPTURE holding={_session.CaptureLivePerformance()?.Performers.Count(item => item.AccessReached && !item.StairReached)}");
        }
        if (_liveCaptureFrame == 11)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_liveCaptureDirectory, "pre-show-holding.png"));
            _session.AdvanceWithoutSnapshot(600);
            _foundationPresentation.Reset(_session.CaptureObservation());
            _foundationClock.ResetBoundary();
            RefreshPreparationHud();
            GD.Print($"LIVE_CAPTURE ready={_session.CaptureLivePerformance()?.Performers.Count(item => item.OnStage)} guitars={_session.CaptureLivePerformance()?.Performers.Count(item => item.InstrumentAttached)} stage={_session.CaptureLivePerformance()?.Stage}");
        }
        if (_liveCaptureFrame == 14)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_liveCaptureDirectory, "pre-show-ready.png"));
            _session.AdvanceWithoutSnapshot(1_000);
            _foundationPresentation.Reset(_session.CaptureObservation());
            _foundationClock.ResetBoundary();
            RefreshPreparationHud();
            GD.Print($"LIVE_CAPTURE stage={_session.CaptureLivePerformance()?.Stage} listeners={_session.CaptureLivePerformance()?.Listeners.Count(item => item.AtPlace)}");
        }
        if (_liveCaptureFrame == 17)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_liveCaptureDirectory, "live-set-default-camera.png"));
            _camera.Size = 26;
            ApplyCamera();
        }
        if (_liveCaptureFrame == 18)
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_liveCaptureDirectory, "live-set-ui.png"));
        if (_liveCaptureFrame == 19)
        {
            foreach (var layer in GetChildren().OfType<CanvasLayer>()) layer.Visible = false;
            _focus = new Vector3(-11, 0, 11);
            _camera.Size = 24;
            ApplyCamera();
        }
        if (_liveCaptureFrame == 22)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_liveCaptureDirectory, "live-set.png"));
            GD.Print($"LIVE_AUDIO near_db={_stageMusic!.VolumeDb:0.00} near_cutoff_hz={((AudioEffectLowPassFilter)AudioServer.GetBusEffect(_stageAudioBus, 0)).CutoffHz:0}");
        }
        if (_liveCaptureFrame == 23)
        {
            _focus = new Vector3(40, 0, 40);
            ApplyCamera();
        }
        if (_liveCaptureFrame == 24)
        {
            GD.Print($"LIVE_AUDIO far_db={_stageMusic!.VolumeDb:0.00} far_cutoff_hz={((AudioEffectLowPassFilter)AudioServer.GetBusEffect(_stageAudioBus, 0)).CutoffHz:0}");
            CommitEquipmentAction(new EquipmentCommand(EquipmentAction.Isolate));
            _session.AdvanceWithoutSnapshot(GameSession.SustainedBooDelayTicks + 1);
            _foundationPresentation.Reset(_session.CaptureObservation());
            _foundationClock.ResetBoundary();
            _focus = new Vector3(-11, 0, 11);
            ApplyCamera();
            GD.Print($"LIVE_CAPTURE interruption={_session.CaptureLivePerformance()?.LastReaction}");
        }
        if (_liveCaptureFrame == 28)
        {
            GD.Print($"LIVE_AUDIO boo_fading_db={_crowdBoo?.VolumeDb:0.00}");
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_liveCaptureDirectory, "interruption.png"));
            GetTree().Quit();
        }
    }
}

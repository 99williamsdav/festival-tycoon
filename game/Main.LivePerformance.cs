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
    private AudioStreamPlayer? _setEndApplause;
    private bool _bandEntryReactionPlayed;
    private int _stageAudioBus = -1;
    private int _lastReactionSequence = -1;
    private LiveSetStage? _presentedSetStage;
    private Label? _liveSetCue;
    private Button? _stageMuteButton;
    private bool _stageMuted;
    private OmniLight3D[]? _stageLights;
    private Label3D? _stageWorldCue;
    private float _booFadeSeconds;
    private float _booTargetDb;

    private void ResetLivePerformancePresentation()
    {
        ResetImmersionHeldVisuals();
        ResetImmersionCuePresentation();
        _performerInstruments.Clear();
        _lastPresentedPersonPositions.Clear();
        _presentedSetStage = null;
        _presentedActId = null;
        _lastReactionSequence = _session.CaptureLivePerformance()?.ReactionSequence ?? -1;
        _bandEntryReactionPlayed = _session.CaptureLivePerformance()?.Performers.Any(item => item.OnStage) ?? false;
        _stageMusic?.Stop();
        _crowdBoo?.Stop();
        _crowdCheer?.Stop();
        _bandEntryApplause?.Stop();
        _setEndApplause?.Stop();
        ResetIncidentAudioPresentation();
        _booFadeSeconds = 0;
        if (_liveSetCue is not null) _liveSetCue.Text = "STAGE • awaiting booking";
        if (_stageWorldCue is not null)
        {
            _stageWorldCue.Text = "TRAILER STAGE";
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
        // Once physically attending, face the live person. Travel still follows
        // rendered motion below, and the later fight pass retains final priority.
        var attending = _session.GetStewardResponses().FirstOrDefault(job => job.WorkerId == id.Value &&
            !job.Incapacitated && job.TargetId is not null &&
            job.Stage is SecurityResponseStage.Calming or SecurityResponseStage.Confronting);
        if (action == AgentNavigationAction.Arrived && (!hasPrevious || direction.LengthSquared() < 0.000036f) && attending?.TargetId is { } targetId &&
            _session.CapturePreparation()?.People.Any(person => person.AgentId == targetId &&
                person.Admitted && !person.Departed) == true &&
            _session.CaptureMedical()?.Needs.Any(need => need.AgentId == targetId &&
                (need.Intent == MedicalIntent.Collapsed || need.Stage is MedicalStage.Collapsed or MedicalStage.Critical or MedicalStage.Terminal)) != true &&
            _attendeeVisuals.TryGetValue(new EntityId(targetId), out var targetVisual) && targetVisual.Visible)
        {
            var toward = targetVisual.Position - position;
            toward.Y = 0;
            if (toward.LengthSquared() > 0.000001f)
            {
                var yaw = Mathf.Atan2(-toward.X, -toward.Z);
                visual.Rotation = new Vector3(0, Mathf.LerpAngle(visual.Rotation.Y, yaw,
                    Mathf.Clamp((float)delta * 7f, 0f, 1f)), 0);
                return;
            }
        }
        // Actual rendered motion wins, including the final interpolated step after
        // authoritative arrival. Actor GLBs face -Z; facilities face +Z.
        if ((!hasPrevious || direction.LengthSquared() < 0.0000000001f) &&
            _session.IdleResponseStaffRole(id, position.X * 1000, position.Z * 1000) is { } idleRole)
        {
            visual.Rotation = new Vector3(0, Mathf.Pi+_session.CaptureResponsePost(idleRole).QuarterTurns*Mathf.Pi/2,0);
            return;
        }
        var backstepping = hasPrevious && _session.ShouldAudienceBackstepFacingStage(id,
            position.X * 1000, position.Z * 1000, direction.X * 1000, direction.Z * 1000);
        if (backstepping || action != AgentNavigationAction.Travelling || !hasPrevious || direction.LengthSquared() < 0.000036f)
        {
            if (!watchingStage && !backstepping) return;
            direction = new Vector3(AudienceFacingMath.StageXMillimetres / 1000f - position.X, 0,
                AudienceFacingMath.StageZMillimetres / 1000f - position.Z);
        }
        if (direction.LengthSquared() < 0.000036f) return;
        var targetYaw = Mathf.Atan2(-direction.X, -direction.Z);
        visual.Rotation = new Vector3(0, Mathf.LerpAngle(visual.Rotation.Y, targetYaw,
            Mathf.Clamp((float)delta * 7f, 0f, 1f)), 0);
    }

    private static string PerformerKitPath(int role, string variant) => role switch
    {
        0 => $"res://assets/characters/lwf_guitarist_{variant}_kit_v2.glb",
        1 => $"res://assets/characters/lwf_bassist_{variant}_kit_v2.glb",
        2 => $"res://assets/characters/lwf_drummer_{variant}_kit_v2.glb",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
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
        if (_session.CaptureProgramme() is { } programme)
        {
            var act = _session.CurrentFestivalAct;
            var next = _session.UpcomingFestivalAct;
            var status = _session.PreparedStatus switch
            {
                PreparationStatus.Finished => "Festival finished • everyone physically departed",
                PreparationStatus.Departing => "Festival closing • ordinary gate departure",
                _ => programme.Status
            };
            if (_session.LateReadyFestivalAct is { } late)
                status = $"LATE • waiting for {late.Name} • performers not ready";
            _liveSetCue.Text = $"FESTIVAL • SET {Math.Clamp(programme.CurrentSlot + 1, 1, 3)}/3 • {live.Stage}\n" +
                $"{act?.Name ?? "Awaiting booking"} • {FestivalGenreName(act?.Genre ?? -1)} • {live.Performers.Count(item => item.OnStage)}/3 ready\n" +
                $"{status} • next {next?.Name ?? "wind-down"}";
            if (ImmersionHeavyOnSiteCount() is var heavy && heavy > 0)
                _liveSetCue.Text += $"\n! {heavy} HEAVY INTOXICATION • SELECT FOR MEDIC CARE";
        }
        if (_hudMoney is not null)
        {
            var compactProgramme = _session.CaptureProgramme();
            var act = _session.CurrentFestivalAct;
            var next = _session.UpcomingFestivalAct;
            var remaining = compactProgramme is not null ? Math.Max(0, compactProgramme.SlotEndTick - _session.CurrentTick) : Math.Max(0, live.StartedTick + GameSession.LiveSetDurationTicks - _session.CurrentTick);
            _liveSetCue.Text = $"ON STAGE · {live.Stage}\n{act?.Name ?? (_session.PreparedStatus == PreparationStatus.Preparing ? "Awaiting booking" : "Booked act")}\n" +
                (live.Stage is LiveSetStage.Live or LiveSetStage.Interrupted ? $"{FestivalGenreName(act?.Genre ?? 0)} · {HudTime(remaining)} remaining" :
                    _session.PreparedStatus == PreparationStatus.Departing ? "Final set finished · physical departures in progress" : compactProgramme?.Status ?? "Performers approaching stage") +
                $"\n\nNEXT · {(_session.UpcomingFestivalTick < 0 ? "—" : HudTime(_session.UpcomingFestivalTick - _session.CapturePreparation()!.StartedTick))}\n{next?.Name ?? "No further set"}";
        }
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
        var placeActivity = listening?.AtPlace == true ? "watching" : "travelling/not watching";
        if (_session.CaptureProgramme() is not null)
            placeActivity = person.Departed ? "left festival" : live?.Stage != LiveSetStage.Live
                ? "not listening • scheduled silence or interrupted set" : placeActivity;
        var detail = listening is not null
            ? $"FIT  {(listening.Enthusiasm >= 60 ? "booked style" : "other style")} • interest {listening.Enthusiasm}%\n" +
              "SPACE  personal crowd comfort • more interest tolerates more nearby people\nClear forward space preferred; step aside/back when too crowded\n" +
              $"VOLUNTARY STAGE WALK  {GameSession.AudiencePacePermille(listening.Enthusiasm) / 10m:0}% × natural {GameSession.GetWalkingSpeedPermille(id) / 10m:0}%\nOther routes keep natural pace\n" +
              $"PLACE  {(listening.Place is { } place ? $"{place.X},{place.Z}" : "not reserved")} • {placeActivity}\n" +
              $"LISTENED  {listening.ListenedTicks / 80}s • enjoyment +{listening.EnjoymentEarned / 100m:0.00}%"
            : performer is not null ? $"STAGE  {performer.StageCell.X},{performer.StageCell.Z} • {(performer.OnStage ? "on stage" : "travelling/exit")}\n" +
              $"INSTRUMENT  {(performer.InstrumentAttached ? "attached for set" : "detached")}" :
              person.Name == "Jordan Hale" ? "STEWARD • autonomous physical route" : "STAFF • autonomous physical route";
        if (_session.CaptureProgramme() is { } programme)
        {
            if (listening is not null)
                detail = $"MAIN TASTE {FestivalGenreName(person.ExpectedGenre)}\n" +
                    $"CURRENT {_session.CurrentFestivalAct?.Name ?? "none"} • interest {listening.Enthusiasm}%\n" +
                    $"UPCOMING {_session.UpcomingFestivalAct?.Name ?? "none"}" +
                    (_session.UpcomingFestivalAct is { } next ? $" • interest {_session.FestivalAffinity(id.Value, next)}%" : "") + "\n" + detail;
            else if (programme.Performers.SingleOrDefault(item => item.AgentId == id.Value) is { } bandMember)
                detail = $"SET {bandMember.SlotIndex + 1} • " +
                    (programme.ActIds.Length == 3 ? _session.GetFestivalActs().Single(act => act.Id == programme.ActIds[bandMember.SlotIndex]).Name : "not booked") +
                    "\nProtected all festival; ordinary water/rest and physical departure.\n" + detail;
        }
        _highlight.Position = _attendeeVisuals[id].Position + new Vector3(0, 0.08f, 0);
        _inspectorTitle.Text = $"{person.Name} • {PersonPresentationRole(person)}";
        _inspectorBody.Text = $"{navigation.Action} • {StewardWording(navigation.IntentId ?? "None")}\n" +
            $"POSITION  {navigation.XMillimetres / 1000.0:0.00} m, {navigation.ZMillimetres / 1000.0:0.00} m\n" +
            $"Admitted {person.Admitted}\n" +
            (need is null ? "" : $"HOT • thirst {need.Thirst / 100m:0}% • heat {need.HeatExposure / 100m:0}% • " +
                $"{need.Stage}\n" +
                $"INTENT {need.Intent} • {StewardWording(need.Reason)}\n" +
                (_session.CaptureWaterPoints().Any(point => point.OwnerId == id.Value)
                    ? $"DRINKING • thirst {need.Thirst / 100m:0}% • heat {need.HeatExposure / 100m:0}%\n"
                : "")) + StaffInterventionTargetText(id.Value) + DisorderPersonInspectorText(id.Value) + detail + ImmersionPersonInspectorText(id.Value);
        if (_hudMoney is not null && !_hudDevelopment)
        {
            var immersion = _session.CaptureImmersion()?.People.SingleOrDefault(item => item.AgentId == id.Value);
            var disorder = _session.CaptureDisorder()?.People.SingleOrDefault(item => item.AgentId == id.Value);
            var worker = _session.GetResponseStaff().SingleOrDefault(item => item.AgentId == id.Value);
            var activity = person.Departed ? "Left the festival" : need?.Intent == MedicalIntent.Collapsed ? "Collapsed · needs care" :
                need is not null ? StewardWording(System.Text.RegularExpressions.Regex.Replace(need.Reason, @"\s+at tick \d+", "")) : performer?.OnStage == true ? "Performing on stage" : "Walking through the festival";
            _inspectorBody.Text = $"{(person.Role == ProtectedPersonRole.Guest ? "Adult · prefers " + FestivalGenreName(person.ExpectedGenre) : "Protected festival worker")}\n{activity}\n" +
                (listening is null ? "" : $"{placeActivity} · interest {listening.Enthusiasm}%\n") +
                (disorder is null || disorder.Stage is DisorderStage.Calm or DisorderStage.Resolved ? "" : $"{disorder.Stage} · pressure {disorder.Pressure / 100m:0}% · {disorder.Grievance}\n" +
                    (DisorderCuePlanner.CurrentOpponentId(_session.CaptureDisorder()!, disorder) is { } opponent ? $"COUNTERPART · {preparation.People.SingleOrDefault(item => item.AgentId == opponent)?.Name ?? "festival worker"}\n" : "") + "Reduce pressure or ask a steward for help. Injury needs a medic.\n") +
                (immersion?.Held is { } held ? $"Holding {ImmersionProductName(held.Product)} · {(!_session.IsPaused && _session.ImmersionConsumptionEligible(id.Value) ? "consuming" : "retained; consumption paused")}\n" : "") +
                (immersion is null ? "" : $"Personal budget {FestivalCurrency.Format(_session.CaptureSnapshot().Wallets.Single(w => w.OwnerId.Value == id.Value).CashPennies)}\n") +
                StaffInterventionTargetText(id.Value) +
                (worker is null ? "" : ResponseStaffInspectorText(id.Value));
        }
    }

    private void EnsureStageDrumKit()
    {
        if (_stageDrumKit is not null) return;
        var drumMark = TraversalGrid.CellCentre(new GridCell(93, 152));
        _stageDrumKit = AddAsset("res://assets/characters/lwf_drum_hardware_only_v2.glb",
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
        _setEndApplause = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = -24 };
        AddChild(_setEndApplause);
        _stageLights = [new OmniLight3D { Position = new Vector3(-18, 2.2f, 10), OmniRange = 8,
            LightColor = new Color("ffd18a"), LightEnergy = 0.8f },
            new OmniLight3D { Position = new Vector3(-14.5f, 2.2f, 10), OmniRange = 8,
                LightColor = new Color("eaa8ff"), LightEnergy = 0.8f }];
        foreach (var light in _stageLights) AddChild(light);
        _stageWorldCue = BuildingName("TRAILER STAGE",new Vector3(-16,4.2f,11));
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
        var attachedIds = live.Performers.Where(item => item.InstrumentAttached).Select(item => new EntityId(item.AgentId)).ToHashSet();
        foreach (var oldId in _performerInstruments.Keys.Where(id => !attachedIds.Contains(id)).ToArray())
        {
            _performerInstruments[oldId].QueueFree(); _performerInstruments.Remove(oldId);
            if (_attendeeVisuals.TryGetValue(oldId, out var oldBody)) SetNeutralArmsVisible(oldBody, true);
        }
        foreach (var performer in live.Performers)
        {
            var id = new EntityId(performer.AgentId);
            if (!_attendeeVisuals.TryGetValue(id, out var body)) continue;
            if (performer.InstrumentAttached && !_performerInstruments.ContainsKey(id))
            {
                var name = roster.Single(item => item.AgentId == performer.AgentId).Name;
                var kit = InstantiateAsset(PerformerKitPath(PerformerPresentationRole(performer.AgentId, name),
                    body.GetMeta("RoleVariant").AsString()));
                ApplyRolePlayingArmPalette(body, kit);
                body.AddChild(kit);
                foreach (var player in kit.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>())
                    foreach (var animationName in player.GetAnimationList())
                    {
                        player.GetAnimation(animationName).LoopMode = Animation.LoopModeEnum.Linear;
                        player.Play(animationName);
                    }
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
        _stageWorldCue!.Text = "TRAILER STAGE";
        foreach (var light in _stageLights!) light.LightEnergy = live.Stage == LiveSetStage.Live ?
            power == 0 ? 0 : power == 80 ? 0.35f : 0.8f : 0;
        var audible = live.Stage == LiveSetStage.Live && power > 0;
        var actId = _session.CurrentFestivalAct?.Id;
        var actChanged = _presentedActId != actId;
        if (actChanged) _bandEntryReactionPlayed = live.Stage == LiveSetStage.Live;
        if (_presentedSetStage != live.Stage || actChanged)
        {
            if (audible)
            {
                var genre = _session.CurrentFestivalAct?.Genre ?? (_session.CapturePreparation()!.AcceptedOffers.Contains("act.punk") ? 1 : 0);
                var path = genre switch { 1 => _session.CaptureProgramme() is null ? "res://assets/audio/punk_loop_v1.wav" : "res://assets/audio/rock_loop_v2.wav", 2 => "res://assets/audio/pop_loop_v1.wav",
                    3 => "res://assets/audio/electronic_loop_v1.wav", _ => "res://assets/audio/folk_loop_v1.wav" };
                // Exact genre assets are integrated only after their approval gate.
                var approved = _session.CaptureProgramme() is null || genre != 1 || FestivalRockAudioApproved;
                _stageMusic!.Stream = approved && ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
                if (_stageMusic.Stream is not null) _stageMusic.Play();
            }
            else _stageMusic!.Stop();
            _presentedSetStage = live.Stage;
            _presentedActId = actId;
        }
        if (audible && _stageMusic!.Stream is not null && !_stageMusic.Playing && !_stageMusic.StreamPaused) _stageMusic.Play();
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
            if (live.LastReaction == "set-finished-applause" && live.Stage == LiveSetStage.Finished && live.SetEndAudienceCount > 0)
            {
                var enthusiastic = PerformanceApplauseMath.IsEnthusiastic(live.SetEndAudienceCount, live.SetEndEnjoymentTotal);
                _setEndApplause!.Stream = GD.Load<AudioStream>(enthusiastic
                    ? "res://assets/audio/band_entry_enthusiastic_applause.wav"
                    : "res://assets/audio/band_entry_polite_applause.wav");
                var strength = PerformanceApplauseMath.Strength(live.SetEndAudienceCount, live.SetEndEnjoymentTotal);
                _setEndApplause.VolumeDb = Mathf.LinearToDb((float)strength * attenuation);
                _setEndApplause.Play();
            }
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

}

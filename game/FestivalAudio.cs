using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// Festival sound on one "Outdoor Stage" bus: the act's music loop and crowd reactions (entry
/// applause, set-start cheer, sustained boo, set-end applause), the ambient crowd, and incident
/// cues (generator explosion, a witness's scream). The camera focus on the ground is the listener.
/// </summary>
internal sealed class FestivalAudio(Node _parent)
{
    private const bool RockAudioApproved = true;
    private AudioStreamPlayer? _stageMusic;
    private AudioStreamPlayer? _crowdBoo;
    private AudioStreamPlayer? _crowdCheer;
    private AudioStreamPlayer? _bandEntryApplause;
    private AudioStreamPlayer? _setEndApplause;
    private bool _bandEntryReactionPlayed;
    private int _stageAudioBus = -1;
    private int _lastReactionSequence = -1;
    private LiveSetStage? _presentedSetStage;
    private string? _presentedActId;
    private bool _muted;
    private float _booFadeSeconds;
    private float _booTargetDb;

    // Local private playtest recordings. Runtime copies are git-ignored and
    // excluded from the export preset; source identity/licence remain unverified.
    private const string AmbientCrowdPath = "res://assets/audio/crowd/background-crowd.wav";
    private const string ExplosionPath = "res://assets/audio/crowd/generator-explosion2.wav";
    private const string ScreamAPath = "res://assets/audio/crowd/exaggerated-female-scream.wav";
    private const string ScreamBPath = "res://assets/audio/crowd/exaggerated-male-scream.mp3";
    private readonly IncidentAudioCueCursor _incidentAudioCursor = new();
    private AudioStreamPlayer? _ambientCrowd;
    private AudioStreamPlayer? _generatorExplosion;
    private AudioStreamPlayer? _screamA;
    private AudioStreamPlayer? _screamB;
    private bool _ambientStartedLogged;

    private static AudioStream? OptionalIncidentStream(string path) =>
        ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;

    private void EnsureIncidents()
    {
        if (_ambientCrowd is not null) return;
        EnsureStage(); // The mute control owns one shared sound bus.
        _ambientCrowd = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = -34,
            Stream = OptionalIncidentStream(AmbientCrowdPath) };
        _generatorExplosion = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = -12,
            Stream = OptionalIncidentStream(ExplosionPath) };
        _screamA = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = -18,
            Stream = OptionalIncidentStream(ScreamAPath) };
        _screamB = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = -18,
            Stream = OptionalIncidentStream(ScreamBPath) };
        foreach (var player in new[] { _ambientCrowd, _generatorExplosion, _screamA, _screamB }) _parent.AddChild(player);
        GD.Print($"INCIDENT_AUDIO_READY ambient={_ambientCrowd.Stream is not null} explosion={_generatorExplosion.Stream is not null} female={_screamA.Stream is not null} male={_screamB.Stream is not null} mode=cc0-integrated");
    }

    private void ResetIncidents(GameSession session)
    {
        _incidentAudioCursor.Reset(session.CaptureEquipment(), session.CaptureMedical());
        _ambientCrowd?.Stop();
        _generatorExplosion?.Stop();
        _screamA?.Stop();
        _screamB?.Stop();
        _ambientStartedLogged = false;
    }

    public void AdvanceIncidents(GameSession session, Vector3 listener)
    {
        EnsureIncidents();
        var preparation = session.CapturePreparation()!;
        var activeGuests = preparation.People.Count(item => item.Role == ProtectedPersonRole.Guest && item.Admitted && !item.Departed);
        var crowdActive = !_muted &&
            preparation.Status == PreparationStatus.Running && activeGuests >= 3 && _ambientCrowd!.Stream is not null;
        if (crowdActive)
        {
            // Ground-plane camera focus is the listener, as for stage music.
            var distance = new Vector2(listener.X + 7f, listener.Z - 13f).Length();
            var attenuation = Mathf.Clamp(1f - distance / 65f, 0.08f, 1f);
            _ambientCrowd!.VolumeDb = Mathf.LinearToDb(Math.Max(0.001f, 0.05f * attenuation));
            if (!_ambientCrowd.Playing && !_ambientCrowd.StreamPaused)
            {
                _ambientCrowd.Play(); // Restarts the long recording after it ends.
                if (!_ambientStartedLogged)
                {
                    GD.Print("INCIDENT_AUDIO_AMBIENT_START mode=cc0-integrated");
                    _ambientStartedLogged = true;
                }
            }
        }
        else if (_ambientCrowd!.Playing) _ambientCrowd.Stop();

        // Always consume evidence, including while muted, so unmuting or loading
        // cannot replay an old explosion or death scream.
        foreach (var cue in _incidentAudioCursor.Observe(session.CaptureEquipment(), session.CaptureMedical(), session.CurrentTick))
        {
            if (_muted) continue;
            var source = cue.Kind == IncidentAudioCueKind.GeneratorExplosion || session.CaptureMedical() is null
                ? new Vector2(GameSession.EquipmentXMillimetres / 1000f, GameSession.EquipmentZMillimetres / 1000f)
                : MedicalDeathPosition(session);
            var distance = new Vector2(listener.X - source.X, listener.Z - source.Y).Length();
            var attenuation = Mathf.Clamp(1f - distance / 70f, 0.05f, 1f);
            if (cue.Kind == IncidentAudioCueKind.GeneratorExplosion)
                PlayIncidentCue(_generatorExplosion!, 0.45f * attenuation, cue);
            else
            {
                // The voice belongs to an off-camera witness, not the victim.
                // Its deterministic source mapping avoids random replay changes.
                var chosen = cue.WitnessVoice == IncidentWitnessVoice.Female ? _screamA! : _screamB!;
                var fallback = chosen == _screamA ? _screamB! : _screamA!;
                PlayIncidentCue(chosen.Stream is null ? fallback : chosen, 0.20f * attenuation, cue);
            }
        }
    }

    private static Vector2 MedicalDeathPosition(GameSession session)
    {
        // The latest casualty; casualty records name the person.
        var victim = session.CaptureLifecycleSnapshot()?.Casualties.LastOrDefault()?.PersonId;
        var id = session.CapturePreparation()!.People.FirstOrDefault(person => person.Name == victim)?.AgentId ?? 0;
        var patient = session.CaptureObservation().NavigationAgents.Single(item => item.Id.Value == id);
        return new Vector2(patient.XMillimetres / 1000f, patient.ZMillimetres / 1000f);
    }

    private static void PlayIncidentCue(AudioStreamPlayer player, float strength, IncidentAudioCue cue)
    {
        if (player.Stream is null)
        {
            GD.Print($"INCIDENT_AUDIO_SKIPPED cue={cue.Kind} tick={cue.Tick} reason=provenance-pending");
            return;
        }
        player.VolumeDb = Mathf.LinearToDb(Math.Max(0.001f, strength));
        player.Stop();
        player.Play();
        GD.Print($"INCIDENT_AUDIO cue={cue.Kind} voice={cue.WitnessVoice} tick={cue.Tick} mode=cc0-integrated");
    }

    public bool Muted => _muted;

    /// <summary>Every player, for pausing with the characters.</summary>
    public IEnumerable<AudioStreamPlayer?> Players =>
        [_stageMusic, _bandEntryApplause, _setEndApplause, _crowdCheer, _crowdBoo, _ambientCrowd, _generatorExplosion, _screamA, _screamB];

    /// <summary>Creates the stage bus and its players once.</summary>
    public void EnsureStage()
    {
        if (_stageMusic is not null) return;
        _stageAudioBus = AudioServer.GetBusCount();
        AudioServer.AddBus(_stageAudioBus);
        AudioServer.SetBusName(_stageAudioBus, "Outdoor Stage");
        AudioServer.SetBusSend(_stageAudioBus, "Master");
        AudioServer.SetBusMute(_stageAudioBus, _muted);
        AudioServer.AddBusEffect(_stageAudioBus, new AudioEffectLowPassFilter { CutoffHz = 12000 });
        AudioServer.AddBusEffect(_stageAudioBus, new AudioEffectReverb { RoomSize = 0.12f, Wet = 0.04f, Dry = 1f });
        _stageMusic = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = -8 };
        _parent.AddChild(_stageMusic);
        _crowdBoo = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = -20,
            Stream = GD.Load<AudioStream>("res://assets/audio/264378__howardv__crowd-booing.wav") };
        _parent.AddChild(_crowdBoo);
        _crowdCheer = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = -24,
            Stream = GD.Load<AudioStream>("res://assets/audio/set_start_cheer.wav") };
        _parent.AddChild(_crowdCheer);
        _bandEntryApplause = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = -24 };
        _parent.AddChild(_bandEntryApplause);
        _setEndApplause = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = -24 };
        _parent.AddChild(_setEndApplause);
    }

    /// <summary>Stops everything and resumes from the session's reaction and incident history, so nothing replays.</summary>
    public void Reset(GameSession session)
    {
        _presentedSetStage = null;
        _presentedActId = null;
        _lastReactionSequence = session.CaptureLivePerformance()?.ReactionSequence ?? -1;
        _bandEntryReactionPlayed = session.CaptureLivePerformance()?.Performers.Any(item => item.OnStage) ?? false;
        _stageMusic?.Stop();
        _crowdBoo?.Stop();
        _crowdCheer?.Stop();
        _bandEntryApplause?.Stop();
        _setEndApplause?.Stop();
        ResetIncidents(session);
        _booFadeSeconds = 0;
    }

    public void AdvanceStage(GameSession session, LivePerformanceSnapshot live, int power, Vector3 listener, double delta)
    {
        var cutoffVisual = power == 0 && live.Stage is LiveSetStage.Live or LiveSetStage.Interrupted;
        var audible = live.Stage == LiveSetStage.Live && power > 0;
        var actId = session.CurrentFestivalAct?.Id;
        var actChanged = _presentedActId != actId;
        if (actChanged) _bandEntryReactionPlayed = live.Stage == LiveSetStage.Live;
        if (_presentedSetStage != live.Stage || actChanged)
        {
            if (audible)
            {
                var genre = session.CurrentFestivalAct?.Genre ?? (session.CapturePreparation()!.AcceptedOffers.Contains("act.punk") ? 1 : 0);
                var path = genre switch { 1 => session.CaptureProgramme() is null ? "res://assets/audio/punk_loop_v1.wav" : "res://assets/audio/rock_loop_v2.wav", 2 => "res://assets/audio/pop_loop_v1.wav",
                    3 => "res://assets/audio/electronic_loop_v1.wav", _ => "res://assets/audio/folk_loop_v1.wav" };
                // Exact genre assets are integrated only after their approval gate.
                var approved = session.CaptureProgramme() is null || genre != 1 || RockAudioApproved;
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
        var distance = new Vector2(listener.X + 16f, listener.Z - 11f).Length();
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

    public void ToggleMute()
    {
        _muted = !_muted;
        if (_stageAudioBus >= 0) AudioServer.SetBusMute(_stageAudioBus, _muted);
    }
}

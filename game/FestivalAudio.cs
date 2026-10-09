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
    // Each stage's own players and what they last presented, the trailer's first.
    private readonly Dictionary<string, StageSound> _stages = new(StringComparer.Ordinal);
    private int _stageAudioBus = -1;
    private bool _muted;

    /// <summary>One stage's music loop and crowd reactions, heard from where that stage stands.</summary>
    private sealed class StageSound
    {
        public required AudioStreamPlayer Music, Boo, Cheer, BandEntryApplause, SetEndApplause;
        public bool BandEntryReactionPlayed;
        public int LastReactionSequence = -1;
        public LiveSetStage? PresentedSetStage;
        public string? PresentedActId;
        public float BooFadeSeconds, BooTargetDb;
        public float Attenuation;
        public IEnumerable<AudioStreamPlayer> Players => [Music, BandEntryApplause, SetEndApplause, Cheer, Boo];
    }

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

    /// <summary>Every player, for pausing with the characters; the trailer stage's music first.</summary>
    public IEnumerable<AudioStreamPlayer?> Players =>
        _stages.Values.SelectMany(stage => stage.Players).Concat(new[] { _ambientCrowd, _generatorExplosion, _screamA, _screamB });

    /// <summary>Creates the stage bus and the trailer stage's players once.</summary>
    public void EnsureStage()
    {
        if (_stageAudioBus >= 0) return;
        _stageAudioBus = AudioServer.GetBusCount();
        AudioServer.AddBus(_stageAudioBus);
        AudioServer.SetBusName(_stageAudioBus, "Outdoor Stage");
        AudioServer.SetBusSend(_stageAudioBus, "Master");
        AudioServer.SetBusMute(_stageAudioBus, _muted);
        AudioServer.AddBusEffect(_stageAudioBus, new AudioEffectLowPassFilter { CutoffHz = 12000 });
        AudioServer.AddBusEffect(_stageAudioBus, new AudioEffectReverb { RoomSize = 0.12f, Wet = 0.04f, Dry = 1f });
        Stage(FestivalStages.MainId);
    }

    private StageSound Stage(string stageId)
    {
        if (_stages.TryGetValue(stageId, out var sound)) return sound;
        AudioStreamPlayer Player(float db, string? path = null)
        {
            var player = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = db, Stream = path is null ? null : GD.Load<AudioStream>(path) };
            _parent.AddChild(player);
            return player;
        }
        sound = new StageSound
        {
            Music = Player(-8), Boo = Player(-20, "res://assets/audio/264378__howardv__crowd-booing.wav"),
            Cheer = Player(-24, "res://assets/audio/set_start_cheer.wav"), BandEntryApplause = Player(-24), SetEndApplause = Player(-24),
        };
        _stages.Add(stageId, sound);
        return sound;
    }

    /// <summary>Stops everything and resumes from the session's reaction and incident history, so nothing replays.</summary>
    public void Reset(GameSession session)
    {
        foreach (var (id, sound) in _stages)
        {
            var live = session.CaptureLivePerformance(id);
            sound.PresentedSetStage = null;
            sound.PresentedActId = null;
            sound.LastReactionSequence = live?.ReactionSequence ?? -1;
            sound.BandEntryReactionPlayed = live?.Performers.Any(item => item.OnStage) ?? false;
            foreach (var player in sound.Players) player.Stop();
            sound.BooFadeSeconds = 0;
            sound.Attenuation = 0;
        }
        ResetIncidents(session);
    }

    /// <summary>
    /// One stage's sound this frame. Each stage is heard from where it stands and fades with the listener's distance,
    /// so two sets at once are both heard, the nearer louder.
    /// </summary>
    public void AdvanceStage(GameSession session, LivePerformanceSnapshot live, int power, Vector3 listener, double delta)
    {
        EnsureStage();
        var known = _stages.ContainsKey(live.StageId);
        var sound = Stage(live.StageId);
        if (!known)
        {
            sound.LastReactionSequence = live.ReactionSequence;
            sound.BandEntryReactionPlayed = live.Performers.Any(item => item.OnStage);
        }
        var cutoffVisual = power == 0 && live.Stage is LiveSetStage.Live or LiveSetStage.Interrupted;
        var audible = live.Stage == LiveSetStage.Live && power > 0;
        var act = session.CurrentStageAct(live.StageId);
        var actId = act?.Id;
        var actChanged = sound.PresentedActId != actId;
        if (actChanged) sound.BandEntryReactionPlayed = live.Stage == LiveSetStage.Live;
        if (sound.PresentedSetStage != live.Stage || actChanged)
        {
            if (audible)
            {
                var genre = act?.Genre ?? (session.CapturePreparation()!.AcceptedOffers.Contains("act.punk") ? 1 : 0);
                var path = genre switch { 1 => session.CaptureProgramme() is null ? "res://assets/audio/punk_loop_v1.wav" : "res://assets/audio/indie_loop_v1.wav", 2 => "res://assets/audio/pop_loop_v1.wav",
                    4 => "res://assets/audio/punk_loop_v1.wav", 5 => "res://assets/audio/metal_loop_v1.wav",
                    3 => "res://assets/audio/electronic_loop_v1.wav", _ => "res://assets/audio/folk_loop_v1.wav" };
                sound.Music.Stream = ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
                if (sound.Music.Stream is not null) sound.Music.Play();
            }
            else sound.Music.Stop();
            sound.PresentedSetStage = live.Stage;
            sound.PresentedActId = actId;
        }
        if (audible && sound.Music.Stream is not null && !sound.Music.Playing && !sound.Music.StreamPaused) sound.Music.Play();
        if (!audible && sound.Music.Playing) sound.Music.Stop();
        // Use the ground-plane focus instead of the elevated isometric camera position;
        // zoom alters framing, not the physical PA distance.
        var source = FestivalStages.Find(live.StageId)?.Placement ?? FestivalStages.Main.Placement;
        var distance = new Vector2(listener.X - source.XMillimetres / 1000f, listener.Z - source.ZMillimetres / 1000f).Length();
        var attenuation = Mathf.Clamp(1f - distance / 90f, 0.08f, 1f);
        sound.Attenuation = audible ? attenuation : 0;
        if (!sound.BandEntryReactionPlayed && live.Stage == LiveSetStage.BeforeSet && live.Performers.Any(item => item.OnStage))
        {
            sound.BandEntryReactionPlayed = true;
            var watchers = live.Listeners.Where(item => item.AtPlace).ToArray();
            if (watchers.Length >= 3)
            {
                var averageEnthusiasm = watchers.Average(item => item.Enthusiasm);
                var enthusiastic = watchers.Length >= 20 && averageEnthusiasm >= 65;
                sound.BandEntryApplause.Stream = GD.Load<AudioStream>(enthusiastic
                    ? "res://assets/audio/band_entry_enthusiastic_applause.wav"
                    : "res://assets/audio/band_entry_polite_applause.wav");
                var strength = Mathf.Clamp(watchers.Length / 40f, 0.15f, 0.65f) *
                    (0.5f + 0.5f * (float)averageEnthusiasm / 100f);
                sound.BandEntryApplause.VolumeDb = Mathf.LinearToDb(strength * attenuation);
                sound.BandEntryApplause.Play();
            }
        }
        var shed = power == 80 ? 0.72f : 1f;
        sound.Music.VolumeDb = Mathf.LinearToDb(Math.Max(0.001f, attenuation * shed * 0.42f));
        // The stages share one bus: it opens up for the nearest stage playing.
        var nearest = _stages.Values.Max(item => item.Attenuation);
        if (AudioServer.GetBusEffect(_stageAudioBus, 0) is AudioEffectLowPassFilter filter)
            filter.CutoffHz = Mathf.Lerp(1800f, 12000f, nearest > 0 ? nearest : attenuation);
        if (sound.LastReactionSequence != live.ReactionSequence)
        {
            if (live.LastReaction == "set-finished-applause" && live.Stage == LiveSetStage.Finished && live.SetEndAudienceCount > 0)
            {
                var enthusiastic = PerformanceApplauseMath.IsEnthusiastic(live.SetEndAudienceCount, live.SetEndEnjoymentTotal);
                sound.SetEndApplause.Stream = GD.Load<AudioStream>(enthusiastic
                    ? "res://assets/audio/band_entry_enthusiastic_applause.wav"
                    : "res://assets/audio/band_entry_polite_applause.wav");
                var strength = PerformanceApplauseMath.Strength(live.SetEndAudienceCount, live.SetEndEnjoymentTotal);
                sound.SetEndApplause.VolumeDb = Mathf.LinearToDb((float)strength * attenuation);
                sound.SetEndApplause.Play();
            }
            if (live.LastReaction is "set-start-cheer" or "set-start-muted") sound.BandEntryApplause.Stop();
            if (live.LastReaction == "set-start-cheer")
            {
                var watchers = live.Listeners.Where(item => item.AtPlace).ToArray();
                var enthusiasm = watchers.Length == 0 ? 0f : (float)watchers.Average(item => item.Enthusiasm) / 100f;
                var size = Mathf.Clamp(watchers.Length / 40f, 0f, 1f);
                var strength = Mathf.Clamp(size * (0.35f + 0.65f * enthusiasm), 0.02f, 0.55f);
                sound.Cheer.VolumeDb = Mathf.LinearToDb(strength * attenuation);
                sound.Cheer.Play();
            }
            // No immediate gasp on cutoff. Only the provisional CC0 sustained boo is audible.
            if (live.LastReaction == "sustained-boo" && live.Listeners.Count(item => item.AtPlace) >= 5)
            {
                sound.BooTargetDb = Mathf.LinearToDb(Mathf.Clamp(live.Listeners.Count(item => item.AtPlace) / 40f, 0.1f, 0.6f) * attenuation);
                sound.BooFadeSeconds = 0;
                sound.Boo.VolumeDb = -48;
                sound.Boo.Play();
            }
            sound.LastReactionSequence = live.ReactionSequence;
        }
        if (sound.Boo.Playing && cutoffVisual)
        {
            sound.BooFadeSeconds += (float)delta;
            sound.Boo.VolumeDb = Mathf.Lerp(-48f, sound.BooTargetDb, Mathf.Clamp(sound.BooFadeSeconds / 1.8f, 0f, 1f));
        }
        else if (sound.Boo.Playing && !cutoffVisual) sound.Boo.Stop();
    }

    public void ToggleMute()
    {
        _muted = !_muted;
        if (_stageAudioBus >= 0) AudioServer.SetBusMute(_stageAudioBus, _muted);
    }
}

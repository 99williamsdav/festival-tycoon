using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
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

    private void EnsureIncidentAudio()
    {
        if (_ambientCrowd is not null) return;
        EnsureStageAudio(); // The existing mute control owns one shared sound bus.
        _ambientCrowd = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = -34,
            Stream = OptionalIncidentStream(AmbientCrowdPath) };
        _generatorExplosion = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = -12,
            Stream = OptionalIncidentStream(ExplosionPath) };
        _screamA = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = -18,
            Stream = OptionalIncidentStream(ScreamAPath) };
        _screamB = new AudioStreamPlayer { Bus = "Outdoor Stage", VolumeDb = -18,
            Stream = OptionalIncidentStream(ScreamBPath) };
        foreach (var player in new[] { _ambientCrowd, _generatorExplosion, _screamA, _screamB }) AddChild(player);
        GD.Print($"INCIDENT_AUDIO_READY ambient={_ambientCrowd.Stream is not null} explosion={_generatorExplosion.Stream is not null} female={_screamA.Stream is not null} male={_screamB.Stream is not null} mode=private-playtest");
    }

    private void ResetIncidentAudioPresentation()
    {
        _incidentAudioCursor.Reset(_session.CaptureEquipment(), _session.CaptureMedical());
        _ambientCrowd?.Stop();
        _generatorExplosion?.Stop();
        _screamA?.Stop();
        _screamB?.Stop();
        _ambientStartedLogged = false;
    }

    private void AdvanceIncidentAudioPresentation()
    {
        EnsureIncidentAudio();
        var preparation = _session.CapturePreparation()!;
        var activeGuests = preparation.People.Count(item => item.Role == ProtectedPersonRole.Guest && item.Admitted && !item.Departed);
        var crowdActive = !_stageMuted && !(_session.IsPaused || _preparationSaveBlocked) &&
            preparation.Status == PreparationStatus.Running && activeGuests >= 3 && _ambientCrowd!.Stream is not null;
        if (crowdActive)
        {
            // Ground-plane camera focus is the listener, as for stage music.
            var distance = new Vector2(_focus.X + 7f, _focus.Z - 13f).Length();
            var attenuation = Mathf.Clamp(1f - distance / 65f, 0.08f, 1f);
            _ambientCrowd!.VolumeDb = Mathf.LinearToDb(Math.Max(0.001f, 0.05f * attenuation));
            if (!_ambientCrowd.Playing)
            {
                _ambientCrowd.Play(); // Restarts the long recording after it ends.
                if (!_ambientStartedLogged)
                {
                    GD.Print("INCIDENT_AUDIO_AMBIENT_START mode=private-playtest");
                    _ambientStartedLogged = true;
                }
            }
        }
        else if (_ambientCrowd!.Playing) _ambientCrowd.Stop();

        // Always consume evidence, including while muted, so unmuting or loading
        // cannot replay an old explosion or death scream.
        foreach (var cue in _incidentAudioCursor.Observe(_session.CaptureEquipment(), _session.CaptureMedical(), _session.CurrentTick))
        {
            if (_stageMuted) continue;
            var source = cue.Kind == IncidentAudioCueKind.GeneratorExplosion || _session.CaptureMedical() is null
                ? new Vector2(GameSession.EquipmentXMillimetres / 1000f, GameSession.EquipmentZMillimetres / 1000f)
                : MedicalDeathPosition();
            var distance = new Vector2(_focus.X - source.X, _focus.Z - source.Y).Length();
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

    private Vector2 MedicalDeathPosition()
    {
        var id = _session.CaptureMedical()!.AtRiskGuestId;
        var patient = _session.CaptureObservation().NavigationAgents.Single(item => item.Id.Value == id);
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
        GD.Print($"INCIDENT_AUDIO cue={cue.Kind} voice={cue.WitnessVoice} tick={cue.Tick} mode=private-playtest");
    }
}

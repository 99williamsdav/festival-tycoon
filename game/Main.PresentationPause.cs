using Godot;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private bool CharacterPresentationPaused => _session.IsPaused ||
        _session.PreparedStatus is not (Festival.Simulation.PreparationStatus.Running or Festival.Simulation.PreparationStatus.Departing);
    private double _characterPresentationSeconds;

    /// <summary>
    /// Particle effects that belong to the world (generator smoke, van steam): paused, every puff holds where it is,
    /// as the rain, the hedges, the pond and the crowd do.
    /// </summary>
    private const string PausableWorldEffects = "PausableWorldEffects";

    private void SyncWorldEffectsPause()
    {
        var speed = _session.IsPaused ? 0 : 1;
        foreach (var node in GetTree().GetNodesInGroup(PausableWorldEffects))
            if (node is CpuParticles3D particles && particles.SpeedScale != speed) particles.SpeedScale = speed;
    }

    private void SyncPresentationPause()
    {
        var paused = CharacterPresentationPaused;
        foreach (var body in _attendeeVisuals.Values)
            foreach (var player in body.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>())
                // A walking guest's pace is set from their speed each frame, pause included.
                if (!player.HasMeta("GuestWalkPlayer")) player.SpeedScale = paused ? 0 : 1;
        foreach (var player in Audio.Players)
            if (player is not null) player.StreamPaused = paused;
    }
}

using Godot;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private bool CharacterPresentationPaused => _session.IsPaused ||
        _session.PreparedStatus is not (Festival.Simulation.PreparationStatus.Running or Festival.Simulation.PreparationStatus.Departing);
    private double _characterPresentationSeconds;

    private void SyncPresentationPause()
    {
        var paused = CharacterPresentationPaused;
        foreach (var body in _attendeeVisuals.Values)
            foreach (var player in body.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>())
                player.SpeedScale = paused ? 0 : 1;
        foreach (var player in new[] { _stageMusic, _bandEntryApplause, _setEndApplause, _crowdCheer, _crowdBoo,
                     _ambientCrowd, _generatorExplosion, _screamA, _screamB })
            if (player is not null) player.StreamPaused = paused;
    }
}

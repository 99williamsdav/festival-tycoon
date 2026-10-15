using Festival.Simulation;
using Godot;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private readonly ChallengeReveal _challengeReveal = new();
    private readonly List<(CanvasLayer Layer, bool Visible)> _challengeHiddenLayers = [];
    private bool _challengeFlowEnabled;
    private void RefreshChallengeReveal()
    {
        if (!_challengeFlowEnabled || StartMenu.IsOpen) return;
        var p = _session.CapturePreparation();
        if (p is not { Status: PreparationStatus.Preparing, ChallengeAcknowledged: false } || _session.CurrentFestival.Challenge is not { } challenge)
        { CloseChallengeReveal(); return; }
        if (_challengeReveal.IsOpen) return;
        CancelBuildPlacement(); ClearSelection();
        _challengeHiddenLayers.Clear();
        foreach (var layer in GetChildren().OfType<CanvasLayer>().Where(layer => !layer.IsQueuedForDeletion()))
        { _challengeHiddenLayers.Add((layer, layer.Visible)); layer.Hide(); }
        _challengeReveal.Open(this, challenge, p.Tier, ContinueChallenge, () => { CloseChallengeReveal(); BuildStartSplash(); });
    }

    private void CloseChallengeReveal()
    {
        _challengeReveal.Close();
        foreach (var (layer, visible) in _challengeHiddenLayers)
            if (GodotObject.IsInstanceValid(layer) && !layer.IsQueuedForDeletion()) layer.Visible = visible;
        _challengeHiddenLayers.Clear();
    }

    private void ContinueChallenge()
    {
        if (!_challengeReveal.IsOpen || _session.CapturePreparation() is not { } p) return;
        var result = _host.Submit(new AcknowledgeFestivalChallengeCommand(p.Tier, p.Attempt));
        if (!result.IsAccepted) return;
        GetViewport().SetInputAsHandled();
        CloseChallengeReveal(); RefreshPreparationHud();
        if (Perks.Panel?.Visible == true)
            Perks.Panel.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(button => !button.Disabled && button.IsVisibleInTree())?.CallDeferred(Control.MethodName.GrabFocus);
    }
}

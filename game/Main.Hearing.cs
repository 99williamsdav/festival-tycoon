using Festival.Simulation;
using Godot;

namespace Festival.Game;

public partial class Main
{
    private HearingPanel? _hearingView;
    private HearingPanel Hearing => _hearingView ??= new(this, ApplyHearingDecision);

    /// <summary>Carries out a confirmed Council decision; a retry rebuilds the live world for the fresh attempt.</summary>
    private void ApplyHearingDecision(SessionCommand command)
    {
        if (!_host.ExecuteMilestone(command, "Council decision", out var error))
        { SyncSaveStatus(); _preparationMessage = error ?? _preparationMessage; RefreshPreparationHud(); return; }
        _host.TakeNotice();
        _preparationMessage = command is SpendCouncilFavourCommand ? "Council Favour spent. Prepare this tier’s next weekend." : "The campaign has ended.";
        if (command is SpendCouncilFavourCommand)
        {
            SelectHudTab("Overview");
            ResetFinanceFeedback();
            foreach (var visual in _attendeeVisuals.Values) visual.QueueFree();
            _attendeeVisuals.Clear(); _attendeePickRegistry.Clear(); _selectedAttendeeId = null;
            ResetLivePerformancePresentation();
            _host.Clock.ResetBoundary(); _foundationPresentation.Reset(_session.CaptureObservation());
            if (_session.CaptureObservation().NavigationAgents.Count > 0) BuildAttendee();
        }
        RefreshPreparationHud();
    }
}

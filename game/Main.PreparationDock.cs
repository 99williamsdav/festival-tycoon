using Festival.Simulation;
using Godot;
using System.Linq;

namespace Festival.Game;

public partial class Main : IPreparationNavigation, ITopBarActions, ILiveBarActions
{
    private StageCard? _stageView;
    private StageCard Stage => _stageView ??= new(this, () => { _hudProgrammeOpen = !_hudProgrammeOpen; RefreshHudWorkspace(); });
    private LiveBar? _liveBarView;
    private LiveBar LiveBottom => _liveBarView ??= new(this, this);
    private BoxOffice? _boxOfficeView;
    private BoxOffice BoxOfficeView => _boxOfficeView ??= new(this, () => { _boxOfficeSeenAttempt = _session.CapturePreparation()!.Attempt; RefreshHudWorkspace(); });
    /// <summary>The attempt whose box office briefing has been dismissed; a presentation preference, never saved.</summary>
    private int _boxOfficeSeenAttempt;
    /// <summary>The box office briefing shows once each attempt, after its perk draft and before planning.</summary>
    private bool BoxOfficeOpen => _session.CapturePreparation() is { Status: PreparationStatus.Preparing } p &&
        _session.CapturePerks()?.Pending != true && p.Attempt != _boxOfficeSeenAttempt;
    bool IPreparationNavigation.Briefing => BoxOfficeOpen;
    private TopBar? _topView;
    private TopBar Top => _topView ??= new(this, this);
    private PreparationDock? _dockView;
    private PreparationDock Dock => _dockView ??= new(this, this);

    private void OpenPreparationDestination(string destination)
    {
        if (_session.PreparedStatus != PreparationStatus.Preparing) return;
        if (_buildGhostKind is not null) CancelBuildPlacement();
        if (destination == "Build")
        {
            if (!_buildDrawerOpen) OpenBuildCatalogue();
            return;
        }
        if (_hudWorkspaceOpen && !_buildDrawerOpen && _hudTabs is not null &&
            _hudPages.Keys.ElementAt(_hudTabs.CurrentTab) == destination) return;
        SelectHudTab(destination);
    }

    string IPreparationNavigation.OpenDestinationName => _buildDrawerOpen ? "Build" : _hudWorkspaceOpen && _hudTabs is not null
        ? _hudPages.Keys.ElementAt(_hudTabs.CurrentTab) : "";
    void IPreparationNavigation.OpenDestination(string destination) => OpenPreparationDestination(destination);
    bool IPreparationNavigation.ReadinessCovered => (_hudWorkspaceOpen && !_buildDrawerOpen && HudProgrammeSelected()) || _contextPanel?.Visible == true;
    bool IPreparationNavigation.Placing => _buildGhostKind is not null;
    void IPreparationNavigation.ConfirmStart() => ShowHudStartConfirmation();
    void ITopBarActions.TogglePause() { _host.Submit(new SetPausedCommand(!_session.IsPaused)); RefreshPreparationHud(); }
    void ITopBarActions.ShowAlerts() { _urgentAlertDisplay.ShowNextPage(); RenderUrgentAlerts(); }
    void ITopBarActions.ToggleMenu() => _hudMenu!.Visible = !_hudMenu.Visible;
    void ITopBarActions.TogglePerks() => Perks.ToggleExpanded();
    void ITopBarActions.ToggleRoster() => _hudRoster!.Visible = !_hudRoster.Visible;
    void ILiveBarActions.ToggleRoster() => _hudRoster!.Visible = !_hudRoster.Visible;
    void ILiveBarActions.TogglePerks() => Perks.ToggleExpanded();
    void ILiveBarActions.RotateView() => _rig.Rotate(1);
    void ILiveBarActions.Zoom(float amount) => _rig.Zoom(amount);
}

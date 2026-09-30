using Festival.Simulation;
using Godot;
using System.Linq;

namespace Festival.Game;

public partial class Main : IPreparationNavigation
{
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
    void IPreparationNavigation.ToggleRoster() => _hudRoster!.Visible = !_hudRoster.Visible;
    void IPreparationNavigation.TogglePerks() => Perks.ToggleExpanded();
    void IPreparationNavigation.RotateView() => Rotate(1);
}

using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private string? _preparationDockCaptureDirectory;
    private int _preparationDockCaptureFrames;
    private int _preparationDockCaptureStep;

    private void PreparationDockCaptureImage(string label)
    {
        var path = Path.Combine(_preparationDockCaptureDirectory!, label + ".png");
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        if (error != Error.Ok) throw new InvalidOperationException($"Preparation dock capture failed: {path}: {error}");
        GD.Print($"PREPARATION_DOCK_CAPTURE {label} hash={_session.CaptureSnapshot().AuthoritativeHash}");
    }

    private void ProcessPreparationDockCapture()
    {
        if (_preparationDockCaptureDirectory is null || ++_preparationDockCaptureFrames < 12) return;
        _preparationDockCaptureFrames = 0;
        try
        {
            switch (_preparationDockCaptureStep++)
            {
                case 0:
                    var perk = _session.CapturePerks()!;
                    CommitEquipmentAction(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
                    if (_session.CapturePerks()?.Pending != false || !_buildDrawerOpen ||
                        _preparationDock?.Visible != true || _buildBudgetFooter?.Visible == true ||
                        _hudLegacyBottom?.Visible == true || _hudWorkspaceFooter?.Visible == true)
                        throw new InvalidOperationException("Preparation dock did not replace the old footer/navigation.");
                    break;
                case 1:
                    if (!_preparationDockBadges["Build"].Visible || !_preparationDockBadges["Programme"].Visible ||
                        !_preparationDockBadges["Staff"].Visible || _preparationDockBadges["Equipment"].Visible ||
                        _preparationDockBadges["Stock"].Visible)
                        throw new InvalidOperationException("Initial dock badges do not match real Start blockers.");
                    if (_preparationReadiness?.Visible != true || !HudBlocksPlacement(_preparationReadiness.GetGlobalRect().GetCenter()))
                        throw new InvalidOperationException("Readiness card leaked world placement input.");
                    var initialChecks = _preparationReadinessRows!.GetChildren().OfType<Button>().ToArray();
                    if (initialChecks.Length != 7 || initialChecks.Count(row => row.Text.StartsWith("! ")) != 6 ||
                        initialChecks.Count(row => row.Text.StartsWith("✓ ")) != 1)
                        throw new InvalidOperationException("Before opening did not retain all seven initial requirements.");
                    PreparationDockCaptureImage("01-build-open-blockers");
                    _buildDrawerClose!.EmitSignal(BaseButton.SignalName.Pressed);
                    break;
                case 2:
                    if (_buildDrawerOpen || _hudWorkspaceOpen || _preparationDock?.Visible != true)
                        throw new InvalidOperationException("X did not collapse only the side panel.");
                    PreparationDockCaptureImage("02-panel-collapsed-dock-kept");
                    SelectPreparationDockDestination("Programme");
                    break;
                case 3:
                    if (_hudWorkspace?.Visible != true || !_hudWorkspaceOpen || !HudProgrammeSelected())
                        throw new InvalidOperationException("Programme dock navigation failed.");
                    PreparationDockCaptureImage("03-programme-selected");
                    SelectPreparationDockDestination("Staff");
                    break;
                case 4:
                    if (_hudPages.Keys.ElementAt(_hudTabs!.CurrentTab) != "Staff")
                        throw new InvalidOperationException("Staff dock navigation failed.");
                    PreparationDockCaptureImage("04-staff-selected");
                    OpenBuildCatalogue(); _buildSiteWaterButton!.EmitSignal(BaseButton.SignalName.Pressed);
                    break;
                case 5:
                    if (_hudPages.Keys.ElementAt(_hudTabs!.CurrentTab) != "Site & water" || _hudWorkspace?.Visible != true)
                        throw new InvalidOperationException("Build did not retain a route to Site & water.");
                    PreparationDockCaptureImage("05-site-water-from-build");
                    OpenBuildCatalogue(); BeginBuildPlacement(BuildServiceKind.Toilet);
                    break;
                case 6:
                    if (_preparationDock?.Visible != true || _buildGhostKind != BuildServiceKind.Toilet ||
                        _hudWorkspace?.Visible == true || _buildDrawer?.Visible == true)
                        throw new InvalidOperationException("Dock did not persist during placement.");
                    PreparationDockCaptureImage("06-placement-dock-kept");
                    CancelBuildPlacement(); OpenBuildCatalogue();
                    CommitEquipmentAction(new UseDefaultBuildLayoutCommand());
                    CommitEquipmentAction(new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"]));
                    CommitEquipmentAction(new AcceptPreparationOfferCommand("staff.steward"));
                    CommitEquipmentAction(new AcceptPreparationOfferCommand("equipment.rent"));
                    RefreshPreparationHud();
                    break;
                case 7:
                    if (_session.GetPreparationStartBlockers().Count != 0 || _preparationDockStart?.Disabled == true ||
                        _preparationDockBadges.Values.Any(badge => badge.Visible))
                        throw new InvalidOperationException("Ready dock retains a false missing-task badge or disabled Start.");
                    var readyChecks = _preparationReadinessRows!.GetChildren().OfType<Button>().ToArray();
                    if (readyChecks.Length != 7 || readyChecks.Any(row => !row.Text.StartsWith("✓ ")))
                        throw new InvalidOperationException("Completed opening requirements disappeared or remained unchecked.");
                    PreparationDockCaptureImage("07-ready-full-draft");
                    PreparationStart();
                    break;
                case 8:
                    if (_session.PreparedStatus != PreparationStatus.Running || _preparationDock?.Visible == true ||
                        _hudLegacyBottom?.Visible != true)
                        throw new InvalidOperationException("Preparation dock did not leave the live HUD unchanged.");
                    PreparationDockCaptureImage("08-live-no-preparation-dock");
                    GD.Print("PREPARATION_DOCK_CAPTURE_COMPLETE steps=8");
                    _preparationDockCaptureDirectory = null;
                    GetTree().Quit();
                    break;
            }
        }
        catch (Exception error)
        {
            GD.PushError("PREPARATION_DOCK_CAPTURE_FAILED " + error);
            _preparationDockCaptureDirectory = null;
            GetTree().Quit(2);
        }
    }
}

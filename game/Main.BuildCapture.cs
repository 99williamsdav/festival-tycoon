using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private string? _buildCaptureDirectory;
    private int _buildCaptureFrame;
    private int _buildCaptureStep;
    private static readonly GridCell BuildBlockedCaptureCell = new(135, 119);

    private void BuildCaptureImage(string name)
    {
        var path = Path.Combine(_buildCaptureDirectory!, name + ".png");
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        if (error != Error.Ok) throw new InvalidOperationException($"Build capture failed: {path}: {error}");
        GD.Print($"BUILD_CAPTURE {name} hash={_session.CaptureSnapshot().AuthoritativeHash}");
    }

    private void ProcessBuildCapture()
    {
        if (_buildCaptureDirectory is null || ++_buildCaptureFrame < 12) return;
        _buildCaptureFrame = 0;
        try
        {
            switch (_buildCaptureStep++)
            {
                case 0:
                    var perk = _session.CapturePerks()!;
                    CommitEquipmentAction(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
                    if (_session.CapturePerks()?.Pending != false || !_buildDrawerOpen || _hudPages.Keys.First() != "Build")
                        throw new InvalidOperationException("Build capture could not settle the opening perk.");
                    break;
                case 1:
                    BuildCaptureImage("01-auto-open-build-catalogue");
                    SelectHudTab("Build");
                    break;
                case 2:
                    if (_hudTabs?.CurrentTab != 0) throw new InvalidOperationException("Build is not the first preparation tab.");
                    BuildCaptureImage("02-build-first-tab-shortcuts");
                    OpenBuildCatalogue();
                    ShowBuildDefaults();
                    if (_buildDefaultsDialog?.DialogText.Contains("Replace 0 placed services") != true ||
                        !_buildDefaultsDialog.DialogText.Contains("6 standard services") ||
                        !_buildDefaultsDialog.DialogText.Contains("£300"))
                        throw new InvalidOperationException("Defaults confirmation omitted count or cost comparison.");
                    break;
                case 3:
                    BuildCaptureImage("03-defaults-cost-confirmation");
                    _buildDefaultsDialog!.Hide(); ApplyBuildDefaults();
                    break;
                case 4:
                    BuildCaptureImage("04-default-layout-cost-and-readiness");
                    if (!_buildCatalogueRows[BuildServiceKind.FirstAid].Action.Disabled ||
                        !_buildCatalogueRows[BuildServiceKind.StewardPost].Action.Disabled)
                        throw new InvalidOperationException("One-slot catalogue rows remain purchasable at capacity.");
                    BeginBuildPlacement(BuildServiceKind.Toilet);
                    if (_buildQuarterTurns != 2) throw new InvalidOperationException("Fresh toilet did not face 180 degrees.");
                    UpdateBuildGhost(_camera.UnprojectPosition(ImmersionPosition(BuildBlockedCaptureCell)));
                    EnableBuildOriginCapture();
                    break;
                case 5:
                    if (!BuildOriginOverlayReady) { _buildCaptureStep--; break; }
                    UpdateBuildGhost(_camera.UnprojectPosition(ImmersionPosition(BuildBlockedCaptureCell)));
                    if (_buildCandidateIssue is null) throw new InvalidOperationException("Blocked ghost fixture was accepted.");
                    BuildCaptureImage("05-blocked-ghost-and-debug-origins");
                    var hash = _session.CaptureSnapshot().AuthoritativeHash;
                    CommitBuildPlacement(_camera.UnprojectPosition(ImmersionPosition(BuildBlockedCaptureCell)));
                    if (_session.CaptureSnapshot().AuthoritativeHash != hash || _buildGhostKind is null)
                        throw new InvalidOperationException("Blocked click changed draft or ended placement.");
                    break;
                case 6:
                    BuildCaptureImage("06-blocked-click-reason");
                    GridCell? valid = null;
                    for (var x = 110; x <= 180 && valid is null; x += 5)
                    for (var z = 110; z <= 175 && valid is null; z += 5)
                    {
                        var cell = new GridCell(x, z);
                        var screen = _camera.UnprojectPosition(ImmersionPosition(cell));
                        if (screen.X < 60 || screen.X > GetViewport().GetVisibleRect().Size.X - 60 ||
                            screen.Y < 100 || screen.Y > GetViewport().GetVisibleRect().Size.Y - 140 || HudBlocksPlacement(screen)) continue;
                        if (_session.ValidateCommand(CampaignEnvelope(new PlaceBuildServiceCommand(BuildServiceKind.Toilet, cell, 2))) is null)
                            valid = cell;
                    }
                    if (valid is null) throw new InvalidOperationException("No visible valid ghost fixture site.");
                    UpdateBuildGhost(_camera.UnprojectPosition(ImmersionPosition(valid.Value)));
                    break;
                case 7:
                    if (_buildCandidateIssue is not null) throw new InvalidOperationException("Valid ghost fixture was blocked.");
                    BuildCaptureImage("07-normal-valid-ghost");
                    var chosenCell = _buildCandidate;
                    CommitBuildPlacement(_camera.UnprojectPosition(ImmersionPosition(chosenCell!.Value)));
                    if (_session.CaptureToilets().Count != 2 || !_buildCatalogueRows[BuildServiceKind.Toilet].Action.Disabled)
                        throw new InvalidOperationException("Second toilet was not autosaved or capacity row stayed active.");
                    break;
                case 8:
                    BuildCaptureImage("08-second-toilet-cap-disabled");
                    SelectHudTab("Programme");
                    break;
                case 9:
                    BuildCaptureImage("09-programme-destination-after-build");
                    _buildCaptureDirectory = null; GetTree().Quit();
                    break;
            }
        }
        catch (Exception error)
        {
            GD.PushError("BUILD_CAPTURE_FAILED " + error);
            _buildCaptureDirectory = null;
            GetTree().Quit(2);
        }
    }
}

using Festival.Simulation;
using Godot;
using System;
using System.IO;

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
                    if (!_session.Execute(CampaignEnvelope(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]))).IsAccepted)
                        throw new InvalidOperationException("Build capture could not settle the opening perk.");
                    _hudWorkspaceOpen = false; _buildDrawerOpen = true; RefreshPreparationHud();
                    break;
                case 1:
                    BuildCaptureImage("01-empty-build-drawer");
                    ShowBuildDefaults();
                    if (_buildDefaultsDialog?.DialogText.Contains("Replace 0 placed services") != true ||
                        !_buildDefaultsDialog.DialogText.Contains("6 standard services") ||
                        !_buildDefaultsDialog.DialogText.Contains("£300"))
                        throw new InvalidOperationException("Defaults confirmation omitted count or cost comparison.");
                    break;
                case 2:
                    BuildCaptureImage("02-defaults-cost-confirmation");
                    _buildDefaultsDialog!.Hide(); ApplyBuildDefaults();
                    break;
                case 3:
                    BuildCaptureImage("03-default-layout-cost-and-readiness");
                    BeginBuildPlacement(BuildServiceKind.WaterTap);
                    UpdateBuildGhost(_camera.UnprojectPosition(ImmersionPosition(BuildBlockedCaptureCell)));
                    break;
                case 4:
                    if (_buildCandidateIssue is null) throw new InvalidOperationException("Blocked ghost fixture was accepted.");
                    if (_hudPlacement?.Visible == true) throw new InvalidOperationException("Legacy placement panel appeared over build ghost.");
                    BuildCaptureImage("04-blocked-red-ghost");
                    var hash = _session.CaptureSnapshot().AuthoritativeHash;
                    CommitBuildPlacement(_camera.UnprojectPosition(ImmersionPosition(BuildBlockedCaptureCell)));
                    if (_session.CaptureSnapshot().AuthoritativeHash != hash || _buildGhostKind is null)
                        throw new InvalidOperationException("Blocked click changed draft or ended placement.");
                    break;
                case 5:
                    BuildCaptureImage("05-blocked-click-reason");
                    GridCell? valid = null;
                    foreach (var cell in new[] { new GridCell(150, 165), new GridCell(145, 165), new GridCell(160, 170), new GridCell(120, 170) })
                        if (_session.ValidateCommand(CampaignEnvelope(new PlaceBuildServiceCommand(BuildServiceKind.WaterTap, cell))) is null)
                        { valid = cell; break; }
                    if (valid is null) throw new InvalidOperationException("No visible valid ghost fixture site.");
                    UpdateBuildGhost(_camera.UnprojectPosition(ImmersionPosition(valid.Value)));
                    break;
                case 6:
                    if (_buildCandidateIssue is not null) throw new InvalidOperationException("Valid ghost fixture was blocked.");
                    BuildCaptureImage("06-normal-valid-ghost");
                    CancelBuildPlacement(); _buildDrawerOpen = false; _hudWorkspaceOpen = true; SelectHudTab("Programme");
                    break;
                case 7:
                    BuildCaptureImage("07-programme-destination-after-build");
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

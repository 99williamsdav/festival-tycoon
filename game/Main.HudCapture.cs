using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private string? _hudCaptureDirectory;
    private int _hudCaptureFrame;
    private string _hudCaptureCosmeticHash = "";
    private Vector2 _hudCapturePlacementScreen;

    private void HudCaptureImage(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        if (image.GetWidth() != GetWindow().Size.X || image.GetHeight() != GetWindow().Size.Y)
            throw new InvalidOperationException("HUD capture is not the actual native window resolution.");
        image.SavePng(Path.Combine(_hudCaptureDirectory!, name + ".png"));
    }

    private void HudCaptureAssertHash(string expected, string label)
    {
        if (_session.CaptureSnapshot().AuthoritativeHash != expected) throw new InvalidOperationException(label + " changed authoritative state.");
    }

    private Vector2 HudCaptureFindPlacement(bool water)
    {
        for (var x = 45; x <= 180; x += 5)
            for (var z = 70; z <= 165; z += 5)
            {
                var cell = new GridCell(x, z); var centre = TraversalGrid.CellCentre(cell);
                var screen = _camera.UnprojectPosition(new Vector3(centre.XMillimetres / 1000f, 0, centre.ZMillimetres / 1000f));
                if (!GetViewport().GetVisibleRect().HasPoint(screen) || HudBlocksPlacement(screen)) continue;
                if (water) UpdateWaterPlacementPreview(screen); else UpdateImmersionPlacementPreview(screen);
                if (water ? _waterPlacementCandidate is not null && _waterPlacementIssue is null : _immersionCandidate is not null && _immersionPlacementIssue is null) return screen;
            }
        throw new InvalidOperationException("No bounded ordinary valid placement candidate was visible.");
    }

    private void ProcessHudCapture()
    {
        if (_hudCaptureDirectory is null) return;
        _hudCaptureFrame++;
        try
        {
            // Each state gets rendered frames after its action. All purchases,
            // placement and live progress use ordinary commands, without need,
            // position, demand, timer, stock or incident injections.
            switch (_hudCaptureFrame)
            {
                case 4: HudCaptureImage("01-new-preparation-overview"); SelectHudTab("Programme"); break;
                case 7: HudCaptureImage("02-programme-disabled-start"); SelectHudTab("Staff"); break;
                case 10: HudCaptureImage("03-staff"); _offerButtons["staff.steward"].EmitSignal(Button.SignalName.Pressed); SelectHudTab("Equipment"); break;
                case 13: HudCaptureImage("04-equipment"); _offerButtons["equipment.buy"].EmitSignal(Button.SignalName.Pressed); SelectHudTab("Stock"); break;
                case 16: HudCaptureImage("05-stock"); _immersionStockButton!.EmitSignal(Button.SignalName.Pressed); SelectHudTab("Site & water"); break;
                case 19: HudCaptureImage("06-site-water"); _hudWaterExact = true; RefreshHudWorkspace(); break;
                case 21: ((_hudTabs!.GetCurrentTabControl() as ScrollContainer)!).EnsureControlVisible(_communityShareButton!); break;
                case 22: HudCaptureImage("07-site-water-exact-effect"); _hudMenu!.Visible = true; break;
                case 25: HudCaptureImage("08-menu-tools-collapsed"); _hudPrototypeOpen = true; RefreshHudWorkspace(); break;
                case 28: HudCaptureImage("09-menu-development-tools"); _hudPrototypeOpen = false; _hudMenu!.Visible = false; SelectHudTab("Programme"); break;
                case 31:
                    _programmeDraft = ["act.meadow-lanterns", "act.orchard-chorus", "act.barnstorm-circuit"];
                    RefreshProgrammeControls(); _programmeBook!.EmitSignal(Button.SignalName.Pressed);
                    if (_session.CaptureProgramme()!.ActIds.Length != 3 || !_session.CaptureImmersion()!.StockPurchased) throw new InvalidOperationException("Atomic booking/stock actions did not commit.");
                    _hudCaptureCosmeticHash = _session.CaptureSnapshot().AuthoritativeHash;
                    ShowHudStartConfirmation(); break;
                case 34: HudCaptureImage("10-start-confirmation"); _hudStartConfirmation!.Hide(); HudCaptureAssertHash(_hudCaptureCosmeticHash, "Start confirmation/cancellation"); break;
                case 37: HudCaptureImage("11-booked-start-cancelled"); _hudWorkspaceOpen = false; _hudRoster!.Visible = true; RefreshHudWorkspace(); break;
                case 40: HudCaptureImage("12-roster-workspace-collapsed"); _hudRoster!.Visible = false; SelectImmersionVendor("drinks"); break;
                case 43: HudCaptureImage("13-vendor-context-move"); BeginImmersionPlacement("drinks"); _hudCapturePlacementScreen = HudCaptureFindPlacement(false); break;
                case 46: UpdateImmersionPlacementPreview(_hudCapturePlacementScreen); HudCaptureImage("14-vendor-placement-preview"); _immersionQuarterTurns = (_immersionQuarterTurns + 1) % 4; UpdateImmersionPlacementPreview(_hudCapturePlacementScreen); break;
                case 49: UpdateImmersionPlacementPreview(_hudCapturePlacementScreen); HudCaptureImage("15-vendor-placement-rotated"); CancelImmersionPlacement(); RefreshHudWorkspace(); HudCaptureAssertHash(_hudCaptureCosmeticHash, "Tabs/collapse/roster/selection/vendor preview/rotation/cancellation"); BeginWaterPlacement(false); _hudCapturePlacementScreen = HudCaptureFindPlacement(true); break;
                case 52: UpdateWaterPlacementPreview(_hudCapturePlacementScreen); HudCaptureImage("16-water-placement-preview"); CommitWaterPlacement(_hudCapturePlacementScreen); break;
                case 55:
                    if (_session.CaptureWaterPoints().Count != 2) throw new InvalidOperationException("Ordinary water placement did not commit atomically.");
                    SelectMedicalFacility(MedicalFacility.Water, "water.extra-1"); break;
                case 58: HudCaptureImage("17-placed-tap-context"); ClearSelection(); PreparationSave(); _hudCaptureCosmeticHash = _session.CaptureSnapshot().AuthoritativeHash; PreparationLoad(); HudCaptureAssertHash(_hudCaptureCosmeticHash, "Ordinary preparation file save/load"); PreparationStart(); break;
                case 61:
                    TimetableAdvanceTo(3200); _session.Execute(CampaignEnvelope(new SetPausedCommand(true))); RefreshPreparationHud(); ClearSelection(); break;
                case 64: HudCaptureImage("18-live-unselected"); SelectAttendee(new EntityId(_session.CapturePreparation()!.People.First(person => person.Role == ProtectedPersonRole.Guest && person.Admitted && !person.Departed).AgentId)); break;
                case 67: HudCaptureImage("19-live-selected-person-actions"); _hudContextScroll!.ScrollVertical = 500; break;
                case 70: HudCaptureImage("20-live-person-actions-scroll"); _hudProgrammeOpen = false; RefreshHudWorkspace(); _hudContextScroll!.ScrollVertical = 0; break;
                case 73: HudCaptureImage("21-live-programme-collapsed"); _hudMenu!.Visible = true; break;
                case 76: HudCaptureImage("22-live-menu"); _hudMenu!.Visible = false; _hudRoster!.Visible = true; break;
                case 79: HudCaptureImage("23-live-roster"); _hudRoster!.Visible = false; SelectObject(LowerWitteringFarmScenario.CreateReadModel().Objects.Single(item => item.Kind == FarmObjectKind.TrailerStage)); break;
                case 82: HudCaptureImage("24-stage-context-actions"); SelectSecurityPost(); break;
                case 85: HudCaptureImage("25-steward-post-context");
                    PreparationSave(); _hudCaptureCosmeticHash = _session.CaptureSnapshot().AuthoritativeHash; PreparationLoad(); HudCaptureAssertHash(_hudCaptureCosmeticHash, "Ordinary live file save/load");
                    if (_contextPanel!.Visible || _cashPopups.Count != 0) throw new InvalidOperationException("Load retained selection or replayed cash popups.");
                    _hudWorkspaceOpen = !_hudWorkspaceOpen; _hudProgrammeOpen = !_hudProgrammeOpen; _hudRoster!.Visible = true; RefreshHudWorkspace(); HudCaptureAssertHash(_hudCaptureCosmeticHash, "Paused cosmetic HUD toggles"); break;
                case 88:
                    HudCaptureImage("26-live-restored-no-selection-or-popup-replay");
                    var legacy = GameSession.CreatePreparedCampaign(20260922);
                    var saved = SaveFileAdapter.SaveSlot(SaveDirectory, "manual-preparation", new SaveWriteRequest(legacy, _saveCompatibility, "hud-isolated-legacy-check", DateTimeOffset.UtcNow));
                    if (!saved.IsSuccess) throw new InvalidOperationException(saved.Error);
                    PreparationLoad(); if (_session.CaptureImmersion() is not null || _contextPanel!.Visible) throw new InvalidOperationException("Legacy load retained current-mode context."); SelectHudTab("Programme"); break;
                case 91: HudCaptureImage("27-legacy-preparation");
                    var current = GameSession.CreateImmersionCampaign(20260922);
                    var currentSave = SaveFileAdapter.SaveSlot(SaveDirectory, "manual-preparation", new SaveWriteRequest(current, _saveCompatibility, "hud-isolated-current-check", DateTimeOffset.UtcNow));
                    if (!currentSave.IsSuccess) throw new InvalidOperationException(currentSave.Error);
                    PreparationLoad(); SelectHudTab("Overview"); break;
                case 94:
                    HudCaptureImage("28-current-after-legacy-load");
                    if (_session.CaptureImmersion() is null || _session.CaptureProgramme() is null || _contextPanel!.Visible || _placingImmersionVendor is not null || _waterPlacementMode != WaterPlacementMode.None) throw new InvalidOperationException("Current reload retained stale placement/context or lost controls.");
                    _staffEffectButtons["staff.medic-slot"].EmitSignal(Button.SignalName.Pressed);
                    _staffEffectButtons["staff.steward-slot"].EmitSignal(Button.SignalName.Pressed);
                    foreach (var id in new[] { "staff.extra-medic", "staff.extra-steward", "staff.steward", "equipment.buy" }) _offerButtons[id].EmitSignal(Button.SignalName.Pressed);
                    _programmeDraft = ["act.meadow-lanterns", "act.orchard-chorus", "act.barnstorm-circuit"]; RefreshProgrammeControls(); _programmeBook!.EmitSignal(Button.SignalName.Pressed);
                    _preparationMessage = "DEVELOPMENT CAPTURE · free role slots, then two actual £30 hires. No incident/need injection.";
                    SelectHudTab("Staff"); break;
                case 97: HudCaptureImage("29-labelled-development-slots-paid-extra-hires"); PreparationStart(); TimetableAdvanceTo(3200); _session.Execute(CampaignEnvelope(new SetPausedCommand(true))); RefreshPreparationHud(); SelectAttendee(new EntityId(_session.CapturePreparation()!.People.First(person => person.Role == ProtectedPersonRole.Guest).AgentId)); _hudProgrammeOpen = false; _hudContextScroll!.ScrollVertical = 220; _preparationMessage = "DEVELOPMENT CAPTURE · actual extra hires; normal live routes and validated person actions."; RefreshPreparationHud(); break;
                case 100: HudCaptureImage("30-live-four-named-response-workers"); _hudContextScroll!.ScrollVertical = 500; break;
                case 103: HudCaptureImage("31-live-extra-worker-guidance-scroll");
                    if (_session.GetResponseStaff().Count != 4 || _staffDispatchButtons.Count != 2) throw new InvalidOperationException("Paid optional-worker action buttons were not built.");
                    _hudAlerts!.Visible = true; break;
                case 107: HudCaptureImage("32-current-alert-panel-locate-options");
                    GD.Print($"HUD_CAPTURE_COMPLETE native={GetWindow().Size.X}x{GetWindow().Size.Y} ordinary_commands=True demand_or_position_injection=False tabs=6 confirmation_cancel_exact=True placement_cancel_exact=True placement_commit=True file_restore_exact=True paused_cosmetic_exact=True legacy_current_context_clear=True actual_extra_hires=2 named_response_workers=4 money={_hudMoney!.Text.Replace('\n', ' ')} hash={_session.CaptureSnapshot().AuthoritativeHash}");
                    GetTree().Quit(); break;
            }
        }
        catch (Exception error) { GD.PushError("HUD_CAPTURE_FAILED " + error); GetTree().Quit(1); }
    }
}

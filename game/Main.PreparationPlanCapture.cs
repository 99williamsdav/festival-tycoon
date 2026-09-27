using Festival.Simulation;
using Festival.Persistence;
using Godot;
using System;
using System.IO;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private string? _planCaptureDirectory;
    private int _planCaptureFrame;
    private string? _planCaptureImage;
    private string _planCaptureSavedHash = "";
    private string _planCapturePerk = "";
    private long _planCaptureTotal;
    private Action<SaveFailurePoint>? _planCaptureFailureInjector;
    private Vector2? _planCapturePlacementPointer;
    private void PlanCheck(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private void PlanShot(string name) { RefreshPreparationHud(); _planCaptureImage = name; }
    private void ProcessPreparationPlanCapture()
    {
        if (_planCaptureDirectory is null) return;
        try
        {
            var frame = ++_planCaptureFrame;
            if (_planCapturePlacementPointer is { } heldPointer && _movingResponsePost is not null)
            {
                HoverMovePointer(heldPointer);
                UpdateResponsePostPreview(heldPointer);
                UpdateHoverFeedback(heldPointer);
            }
            if (frame % 12 == 11 && _planCaptureImage is { } image)
            {
                if (image == "09-revised-placement-preview")
                    PlanCheck(_movingResponsePost == ResponseRole.Medic && _postCandidate == new GridCell(80,120) && _postQuarterTurns == 1 && _postIssue is null && _postPreview?.Visible == true,
                        "The captured revised placement preview is not visibly valid");
                var captured = GetViewport().GetTexture().GetImage();
                PlanCheck(captured.GetWidth() == GetWindow().Size.X && captured.GetHeight() == GetWindow().Size.Y, "Wrong native capture dimensions");
                PlanCheck(captured.SavePng(Path.Combine(_planCaptureDirectory, image + ".png")) == Error.Ok, "PNG failed");
                GD.Print($"PLAN_CAPTURE image={image} cost={_session.PreparationPlanCost} remaining={_session.PreparationRemainingCash} placement_candidate={_postCandidate} placement_issue={_postIssue ?? "none"} hash={_session.CaptureSnapshot().AuthoritativeHash}");
                _planCaptureImage = null;
            }
            if (frame % 12 != 0) return;
            switch (frame / 12)
            {
                case 1:
                    GD.Print("PLAN_CAPTURE_SETUP scripted_native=True manual_desktop_QA=False accelerated_ticks=True fixture=none isolated_saves=True");
                    var perk = _session.CapturePerks()!; _planCapturePerk = perk.Hand[0];
                    CommitEquipmentAction(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, _planCapturePerk));
                    _session.Execute(CampaignEnvelope(new SetPausedCommand(true))); SelectHudTab("Programme"); PlanShot("01-unpaid-empty-readiness"); break;
                case 2:
                    CommitEquipmentAction(new SetProgrammeCommand(["act.meadow-lanterns", "", ""]));
                    PlanCheck(_session.CapturePreparationPlan()!.ActIds[1] == "", "Incomplete lineup not saved"); PlanShot("02-incomplete-lineup-cost"); break;
                case 3:
                    CommitEquipmentAction(new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.field-frequency"]));
                    var beforeFailedEdit = _session.CaptureSnapshot().AuthoritativeHash;
                    var choice = _programmeChoices[0]; var replacement = -1;
                    for (var i = 0; i < choice.ItemCount; i++) if (choice.GetItemMetadata(i).AsString() == "act.orchard-chorus") replacement = i;
                    PlanCheck(replacement >= 0, "Replacement act unavailable");
                    _planCaptureFailureInjector = _ => throw new IOException("labelled capture save-failure injection");
                    choice.Select(replacement); choice.EmitSignal(OptionButton.SignalName.ItemSelected, (long)replacement); _planCaptureFailureInjector = null;
                    PlanCheck(_session.CaptureSnapshot().AuthoritativeHash == beforeFailedEdit && _programmeDraft[0] == "act.meadow-lanterns" && choice.GetItemMetadata(choice.Selected).AsString() == "act.meadow-lanterns", "Failed lineup save retained uncommitted UI selection");
                    GD.Print("PLAN_CAPTURE_FAILED_EDIT_RETAINED fixture=save_failure_injection authoritative_hash=True dropdown=True subtotal=True");
                    SelectHudTab("Staff"); PreparationAccept("staff.steward"); PreparationAccept("maintenance.worker"); PreparationAccept("maintenance.worker");
                    PlanCheck(_session.CaptureEquipment()!.WorkerId is null && _session.CapturePreparation()!.Payments.Length == 0, "Draft activated a hire"); PlanShot("03-editable-staff-remove"); break;
                case 4:
                    SelectHudTab("Equipment"); PreparationAccept("equipment.buy");
                    PlanCheck(_session.CapturePreparation()!.OwnedEquipment.Length == 0, "Draft granted durable rig"); PlanShot("04-planned-rig"); break;
                case 5:
                    PreparationAccept("equipment.buy"); PlanCheck(!_session.CapturePreparationPlan()!.OfferIds.Contains("equipment.buy"), "Rig removal failed");
                    SelectHudTab("Stock"); CommitEquipmentAction(new PurchaseImmersionStarterStockCommand()); PlanShot("05-default-stock-quantities"); break;
                case 6:
                    CommitEquipmentAction(new SetPreparationStockCommand(10000, 10000, 10000));
                    PlanCheck(_preparationStart.Disabled && _session.PreparationRemainingCash < 0, "Over-budget plan opened");
                    PlanCheck(_cashPopups.Count == 0 && _session.CaptureFestivalCashFeedbackEvents().Count == 0, "Unpaid edit emitted money feedback"); PlanShot("06-over-budget-readiness"); break;
                case 7:
                    CommitEquipmentAction(new SetPreparationStockCommand(0, 0, 0)); PlanShot("07-removed-stock-zero-cost"); break;
                case 8:
                    CommitEquipmentAction(new SetPreparationStockCommand(40, 40, 32)); PreparationAccept("equipment.buy");
                    _planCaptureSavedHash = _session.CaptureSnapshot().AuthoritativeHash; PreparationSave(); PreparationLoad();
                    PlanCheck(_session.CaptureSnapshot().AuthoritativeHash == _planCaptureSavedHash && _session.CapturePerks()!.Equipped.Contains(_planCapturePerk), "Unpaid restore/perk changed");
                    // Labelled diagnostic file fixture: matching current header but legacy paid body.
                    var normalDirectory = SaveDirectory; var normalSession = _session;
                    var diagnostic = GameSession.CreateImmersionCampaign(20260922);
                    var diagnosticRequest = new SaveWriteRequest(diagnostic, _saveCompatibility, "diagnostic-fixture", DateTimeOffset.UtcNow);
                    var written = SaveFileAdapter.SaveSlot(normalDirectory, "manual-preparation", diagnosticRequest);
                    PlanCheck(written.IsSuccess, "Diagnostic load-boundary fixture could not save");
                    PlanCheck(SaveFileAdapter.SaveSlot(normalDirectory, "diagnostic-paid-model-fixture", diagnosticRequest).IsSuccess, "Diagnostic fixture backup failed");
                    var diagnosticPath = SaveFileAdapter.ResolveSlotPath(normalDirectory, "manual-preparation"); var diagnosticBytes = File.ReadAllBytes(diagnosticPath);
                    PreparationLoad();
                    PlanCheck(ReferenceEquals(normalSession, _session) && _session.CaptureSnapshot().AuthoritativeHash == _planCaptureSavedHash && SaveDirectory == normalDirectory &&
                        diagnosticBytes.SequenceEqual(File.ReadAllBytes(diagnosticPath)) && _preparationMessage.Contains("matching diagnostic mode"), "Normal load entered the diagnostic economic model or changed its save");
                    GD.Print("PLAN_CAPTURE_DIAGNOSTIC_LOAD_REJECTED fixture=matching_header_legacy_body normal_session=True save_namespace_retained=True file_bytes_unchanged=True");
                    PreparationSave();
                    SelectHudTab("Programme"); PlanShot("08-saved-unpaid-fixed-perk"); break;
                case 9:
                    _hudWorkspaceOpen = false; _hudProgrammeOpen = false; RefreshHudWorkspace();
                    _focus = ImmersionPosition(new(80, 120)); ApplyCamera(); BeginResponsePostPlacement(ResponseRole.Medic); _postQuarterTurns = 1;
                    var pointer = PostScreen(new(80,120)); _planCapturePlacementPointer = pointer; HoverMovePointer(pointer); UpdateResponsePostPreview(pointer);
                    PlanCheck(_postIssue is null, "Valid revised post preview rejected"); PlanShot("09-revised-placement-preview"); break;
                case 10:
                    CommitResponsePostPlacement(PostScreen(new(80,120)));
                    _planCapturePlacementPointer = null;
                    PlanCheck(_movingResponsePost is null && _session.CaptureResponsePost(ResponseRole.Medic) == new ResponsePostPlacement(new(80,120),1), "Draft site move failed");
                    SelectHudTab("Programme"); _planCaptureTotal = _session.PreparationPlanCost; ShowHudStartConfirmation(); PlanShot("10-atomic-start-confirmation"); break;
                case 11:
                    _hudStartConfirmation!.Hide(); PreparationStart();
                    PlanCheck(_session.PreparedStatus == PreparationStatus.Running && _session.CapturePreparation()!.SetupPayments!.Length == 1, "Opening did not commit once");
                    PlanCheck(_session.CaptureSnapshot().FestivalFinances.Single().CashPennies == 80000 - _planCaptureTotal, "Opening charge wrong");
                    PlanCheck(_session.CaptureFestivalCashFeedbackEvents().Single().FestivalCashPennies == -_planCaptureTotal, "Opening aggregate feedback wrong");
                    PlanShot("11-opened-one-setup-payment"); break;
                case 12:
                    _session.Execute(CampaignEnvelope(new SetPausedCommand(false))); _session.AdvanceWithoutSnapshot(5000); _session.Execute(CampaignEnvelope(new SetPausedCommand(true)));
                    _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
                    PlanCheck(_session.CaptureImmersion()!.Purchases.Length > 0, "No ordinary live sale in accelerated route");
                    PlanCheck(_session.CaptureFestivalCashFeedbackEvents().Count(e => e.FestivalCashPennies < 0) == 1, "Setup repeated");
                    _planCaptureSavedHash = _session.CaptureSnapshot().AuthoritativeHash; PreparationSave(); PreparationLoad();
                    PlanCheck(_session.CaptureSnapshot().AuthoritativeHash == _planCaptureSavedHash && _cashPopups.Count == 0, "Live restore mismatch or feedback replay");
                    PlanShot("12-live-sale-save-no-replay"); break;
                case 13:
                    GD.Print($"PLAN_CAPTURE_COMPLETE unpaid_edits=True incomplete_lineup=True stock_quantities=True overbudget=True removal=True revised_placement=True captured_preview_valid=True fixed_perk=True draft_restore=True diagnostic_load_rejected=True atomic_setup=True live_sales=True load_replay=False hash={_session.CaptureSnapshot().AuthoritativeHash}");
                    GetTree().Quit(); break;
            }
        }
        catch (Exception error) { GD.PushError("PLAN_CAPTURE_FAILED " + error); GetTree().Quit(2); }
    }
}

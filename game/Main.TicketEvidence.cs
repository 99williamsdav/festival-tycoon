using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

// Explicit opt-in, isolated save namespaces. Real UI controls plus ordinary paid session commands.
public partial class Main
{
    private string? _ticketEvidenceOutput;
    private int _ticketEvidenceFrame;
    private string[] _ticketEvidenceActs = [];
    private string _ticketUnchanged = "";
    private readonly List<object> _ticketJournal = [];
    private void TicketPicture(string name) => GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_ticketEvidenceOutput!, name + ".png"));
    private void TicketObserve(string name) => _ticketJournal.Add(new { name, state = _session.CapturePersistenceSnapshot(), booking = _session.CaptureClassicBookingDetails() });
    private void TicketClick(string name)
    {
        var button = FindChildren(name, "Button", true, false).OfType<Button>().First(b => b.IsVisibleInTree());
        var viewport = GetViewport(); var point = button.GetGlobalRect().GetCenter();
        if (button.GetWindow() is { } window && window != GetWindow()) point += new Vector2(window.Position.X, window.Position.Y);
        ArchitectureCheck(name + "-actual-pointer-target", !button.Disabled && viewport.GetVisibleRect().HasPoint(point));
        viewport.PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
        viewport.PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left }, true);
        viewport.PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = false }, true);
    }
    private void TicketSite()
    {
        ArchitectureSend(new UseDefaultBuildLayoutCommand()); ArchitectureSend(new SetPreparationStockCommand(24, 16));
        foreach (var person in _session.GetStaffCandidates().GroupBy(c => c.Role).Select(g => g.OrderBy(c => c.WagePennies).First()))
            ArchitectureSend(new AcceptPreparationOfferCommand(person.Id));
    }
    private void TicketGeometry(string name)
    {
        var controls = _classicBookingScreen!.FindChildren("*", "Control", true, false).OfType<Control>()
            .Where(c => c.IsVisibleInTree() && c is Button or SpinBox).Select(c => new { name = c.Name.ToString(), rect = c.GetGlobalRect().ToString(), minimum = c.GetCombinedMinimumSize().ToString() });
        File.WriteAllText(Path.Combine(_ticketEvidenceOutput!, name + "-geometry.json"), JsonSerializer.Serialize(controls));
        foreach (var id in new[] { "ClassicConfirmBookings", "ClassicBookingBack", "ClassicTicketPrice" })
        {
            var control = _classicBookingScreen.FindChildren(id, "Control", true, false).OfType<Control>().First();
            var rect = control.GetGlobalRect(); var viewport = GetViewport().GetVisibleRect();
            ArchitectureCheck(id + "-fully-visible", rect.Position.X >= 0 && rect.Position.Y >= 0 && rect.End.X <= viewport.End.X && rect.End.Y <= viewport.End.Y);
        }
        var sheet = _classicBookingScreen.FindChildren("ClassicBookingSheet", "PanelContainer", true, false).OfType<PanelContainer>().Single();
        File.WriteAllText(Path.Combine(_ticketEvidenceOutput!, name + "-sheet.json"), JsonSerializer.Serialize(new { rect = sheet.GetGlobalRect().ToString(), minimum = sheet.GetCombinedMinimumSize().ToString(),
            children = sheet.FindChildren("*", "Container", true, false).OfType<Control>().Select(c => new { type = c.GetClass().ToString(), rect = c.GetGlobalRect().ToString(), minimum = c.GetCombinedMinimumSize().ToString() }) }));
        ArchitectureCheck("booking-sheet-clears-footer", sheet.GetGlobalRect().End.Y <= Ui.S(638));
    }
    private void ProcessTicketEvidence()
    {
        try
        {
            var f = _ticketEvidenceFrame++;
            if (f == 0)
            {
                Directory.CreateDirectory(_ticketEvidenceOutput!); _challengeFlowEnabled = true;
                BeginClassicCampaign(GameSession.CreateClassicCampaign(14)); _classicOriginalId = _session.CampaignId.Value;
            }
            if (f == 4) { ArchitectureCheck("forecast-before-booking", ClassicForecastOpen && _classicBookingLayer is null); TicketPicture("01-forecast-first"); }
            if (f == 6) TicketClick("ForecastContinue");
            if (f == 8)
            {
                var p = _session.CapturePerks()!; ArchitectureSend(new ChoosePerkCommand(p.DraftAttempt, p.Cursor, p.Hand[0])); RefreshPreparationHud();
            }
            if (f == 12) { TicketGeometry("02-empty"); TicketPicture("02-free-booking-draft"); }
            if (f == 14)
            {
                _ticketEvidenceActs = _session.GetFestivalActs().Where(a => _session.ActStandingOf(a) == ActStanding.Available)
                    .OrderBy(_session.ActFee).ThenBy(a => a.Id, StringComparer.Ordinal).Take(3).Select(a => a.Id).ToArray();
                ArchitectureSend(new SetProgrammeCommand(_ticketEvidenceActs)); RefreshPreparationHud();
                var input = _classicBookingScreen!.FindChildren("ClassicTicketPrice", "SpinBox", true, false).OfType<SpinBox>().Single().GetLineEdit();
                input.GrabFocus(); input.Clear(); input.InsertTextAtCaret("11.25");
                _classicBookingScreen.FindChildren("ClassicTicketPrice", "SpinBox", true, false).OfType<SpinBox>().Single().Apply();
            }
            if (f == 16) { ArchitectureCheck("native-direct-price-entry", _session.CaptureClassicBooking()!.DraftTicketPricePennies == 1125); TicketClick("ClassicPricePlus"); }
            if (f == 18) { ArchitectureCheck("native-price-plus", _session.CaptureClassicBooking()!.DraftTicketPricePennies == 1225); TicketClick("ClassicPriceMinus"); }
            if (f == 20)
            {
                ArchitectureCheck("native-price-minus", _session.CaptureClassicBooking()!.DraftTicketPricePennies == 1125);
                _ticketUnchanged = _session.CaptureSnapshot().AuthoritativeHash.ToString(); TicketPicture("03-complete-sales-estimate"); TicketClick("ClassicBookingBack");
            }
            if (f == 22) { ArchitectureCheck("back-forecast-retains-draft", ClassicForecastOpen && _session.CaptureSnapshot().AuthoritativeHash.ToString() == _ticketUnchanged); TicketClick("ForecastContinue"); }
            if (f == 24) { ArchitectureSave("ticket-draft"); PreparationSave(); PreparationLoad(); }
            if (f == 28) { ArchitectureCheck("draft-load-retains-price", _session.CaptureClassicBooking()!.DraftTicketPricePennies == 1125); TicketClick("ClassicConfirmBookings"); }
            if (f == 32)
            {
                ArchitectureCheck("native-paid-sales-receipt", _session.CaptureClassicBookingDetails()!.Phase == ClassicBookingPhase.SitePreparation && _classicTicketDialog?.Visible == true);
                ArchitectureCheck("confirmation-does-not-repeat", !_host.Submit(new ConfirmClassicBookingsCommand()).IsAccepted);
                File.WriteAllText(Path.Combine(_ticketEvidenceOutput!, "receipt-geometry.json"), JsonSerializer.Serialize(new { size = _classicTicketDialog!.Size.ToString(), ok = _classicTicketDialog.GetOkButton().GetGlobalRect().ToString() }));
                TicketObserve("first-confirmation"); TicketPicture("04-actual-sales-receipt"); TicketClick("ClassicReceiptContinue");
            }
            if (f == 34) { PreparationSave(); PreparationLoad(); }
            if (f == 36)
            {
                ArchitectureCheck("paid-load-returns-site-without-legacy-programme", _classicBookingLayer is null && _hudWorkspace?.Visible != true);
                SelectHudTab("Programme");
            }
            if (f == 38) ArchitectureCheck("direct-programme-shortcut-opens-classic", _classicBookingScreen?.Visible == true);
            if (f == 40) { _ticketUnchanged = _session.CaptureSnapshot().AuthoritativeHash.ToString(); TicketClick("ClassicRemove3"); }
            if (f == 44)
            {
                ArchitectureCheck("preview-does-not-mutate", _session.CaptureSnapshot().AuthoritativeHash.ToString() == _ticketUnchanged);
                ArchitectureCheck("contract-safe-default-focus", ((ConfirmationDialog)_classicTicketDialog!).GetCancelButton().HasFocus());
                TicketPicture("05-release-consequence-preview");
                GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true }, true);
            }
            if (f == 48)
            {
                ArchitectureCheck("escape-keeps-current-contract", _classicTicketDialog is null && _session.CaptureSnapshot().AuthoritativeHash.ToString() == _ticketUnchanged);
                TicketClick("ClassicRemove3");
            }
            if (f == 52) TicketClick("ClassicApplyContract");
            if (f == 56)
            {
                ArchitectureCheck("release-empty-slot-blocks-opening", _session.CapturePreparationPlan()!.ActIds[2] == "" && _session.GetPreparationStartBlockers().Count > 0);
                ArchitectureSave("released-headline"); TicketObserve("released-headline"); TicketPicture("06-empty-paid-slot");
                TicketClick("ClassicAct_" + _ticketEvidenceActs[2].Replace('.', '_'));
            }
            if (f == 60) TicketClick("ClassicSet3");
            if (f == 64) TicketClick("ClassicApplyContract");
            if (f == 66) TicketClick("ClassicBookingBack");
            if (f == 68)
            {
                ArchitectureCheck("back-to-site-hides-legacy-programme", _classicBookingLayer is null && _hudWorkspace?.Visible != true);
                OpenPreparationDestination("Programme");
                ArchitectureCheck("refill-keeps-refunds-no-resale", _session.CaptureClassicBookingDetails()!.Money.Count(m => m.Kind == ClassicBookingMoneyKind.Tickets) == 1 &&
                    _session.CaptureClassicBookingDetails()!.RefundedTickets > 0 && _session.CapturePreparationPlan()!.ActIds[2] != "");
                TicketClick("ClassicCancelFestival");
            }
            if (f == 72) { TicketPicture("07-whole-cancellation-preview"); TicketClick("ClassicKeepContract"); }
            if (f == 76) TicketClick("ClassicCancelFestival");
            if (f == 80) TicketClick("ClassicApplyContract");
            if (f == 84)
            {
                ArchitectureCheck("cancel-returns-actual-opening-money", _session.CapturePersistentCampaignState()!.CashPennies == 20000 && _session.FestivalNumber == 1);
                var costly = _session.GetFestivalActs().Where(a => _session.ActStandingOf(a) != ActStanding.Locked).OrderByDescending(_session.ActFee).Take(3).Select(a => a.Id).ToArray();
                ArchitectureSend(new SetProgrammeCommand(costly)); ArchitectureSend(new SetClassicTicketPriceCommand(10000)); RefreshPreparationHud();
            }
            if (f == 88)
            {
                var confirm = FindChildren("ClassicConfirmBookings", "Button", true, false).OfType<Button>().First(b => b.IsVisibleInTree());
                ArchitectureCheck("risky-booking-warning-without-cap", _session.ClassicBookingEstimate!.Risky && !confirm.Disabled);
                TicketGeometry("08-risk"); TicketPicture("08-risk-warning-can-confirm");
                ArchitectureSend(new SetProgrammeCommand(_ticketEvidenceActs)); RefreshPreparationHud(); TicketClick("ClassicConfirmBookings");
            }
            if (f == 92) TicketClick("ClassicReceiptContinue");
            if (f == 96)
            {
                TicketSite(); PreparationStart(); ClassicFinish(); TicketObserve("zero-sales-real-finish");
                ArchitectureCheck("zero-sales-does-not-end-campaign", _session.CompletedFestivalResult!.AdmittedGuests == 0 && _session.CompletedFestivalAccounts!.Reconciles);
                ClassicNext();
            }
            if (f == 100) TicketClick("ForecastContinue");
            if (f == 104)
            {
                var p = _session.CapturePerks()!; ArchitectureSend(new ChoosePerkCommand(p.DraftAttempt, p.Cursor, p.Hand[0]));
                var costly = _session.GetFestivalActs().Where(a => _session.ActStandingOf(a) != ActStanding.Locked).OrderByDescending(_session.ActFee).Take(3).Select(a => a.Id).ToArray();
                ArchitectureSend(new SetProgrammeCommand(costly)); ArchitectureSend(new SetClassicTicketPriceCommand(10000)); RefreshPreparationHud(); TicketClick("ClassicConfirmBookings");
            }
            if (f == 108) TicketClick("ClassicReceiptContinue");
            if (f == 112)
            {
                ArchitectureCheck("actual-negative-paid-save", _session.CapturePersistentCampaignState()!.CashPennies < -10000);
                ArchitectureSave("actual-negative-booking"); TicketObserve("actual-negative-booking"); OpenPreparationDestination("Programme");
            }
            if (f == 116) { TicketPicture("09-negative-balance-recovery"); TicketClick("ClassicCancelFestival"); }
            if (f == 120) TicketClick("ClassicApplyContract");
            if (f == 124)
            {
                ArchitectureCheck("negative-recovery-same-festival", _session.FestivalNumber == 2 && _session.CaptureClassicBookingDetails()!.Phase == ClassicBookingPhase.Booking);
                ArchitectureSend(new SetClassicTicketPriceCommand(1000));
                var acts = _session.GetFestivalActs().Where(a => _session.ActStandingOf(a) == ActStanding.Available).OrderBy(_session.ActFee).Take(3).Select(a => a.Id).ToArray();
                ArchitectureSend(new SetProgrammeCommand(acts)); RefreshPreparationHud(); TicketClick("ClassicConfirmBookings");
            }
            if (f == 128) TicketClick("ClassicReceiptContinue");
            if (f == 132)
            {
                TicketSite(); var before = _session.CapturePersistentCampaignState()!.CashPennies; var remaining = _session.PreparationRemainingCash;
                PreparationStart(); ArchitectureCheck("start-charges-only-remaining-setup", _session.CapturePersistentCampaignState()!.CashPennies == remaining && before > remaining);
                ClassicFinish(); TicketObserve("recovered-second-festival");
                ArchitectureCheck("actual-recovered-accounts", _session.CompletedFestivalAccounts!.Reconciles);
                ArchitecturePress("AccountsTab");
            }
            if (f == 136) TicketPicture("10-actual-ticket-accounts");
            if (f == 140)
            {
                File.WriteAllText(Path.Combine(_ticketEvidenceOutput!, "evidence.json"), JsonSerializer.Serialize(new { executable = OS.GetExecutablePath(), checks = _architectureChecks, journal = _ticketJournal,
                    caveat = "Known seed, native controls and actual played money; bounded automation, not manual accessibility/balance/performance QA." }, new JsonSerializerOptions { WriteIndented = true }));
                GD.Print("CLASSIC_TICKETS_NATIVE_PASS checks=" + _architectureChecks.Count); GetTree().Quit();
            }
        }
        catch (Exception error)
        {
            Directory.CreateDirectory(_ticketEvidenceOutput!); File.WriteAllText(Path.Combine(_ticketEvidenceOutput!, "failure.txt"), error.ToString());
            File.WriteAllText(Path.Combine(_ticketEvidenceOutput!, "failure-state.json"), JsonSerializer.Serialize(_session.CapturePersistenceSnapshot()));
            File.WriteAllText(Path.Combine(_ticketEvidenceOutput!, "failure-restore.txt"), GameSession.Restore(_session.CapturePersistenceSnapshot()).Error ?? "Restore succeeds");
            File.WriteAllText(Path.Combine(_ticketEvidenceOutput!, "failure-host.txt"), _preparationMessage + "\n" + _host.SaveError);
            File.WriteAllText(Path.Combine(_ticketEvidenceOutput!, "checks.json"), JsonSerializer.Serialize(_architectureChecks));
            GD.PushError(error.ToString()); GetTree().Quit(2);
        }
    }
}

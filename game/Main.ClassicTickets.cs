using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private CanvasLayer? _classicBookingLayer;
    private ClassicBookingScreen? _classicBookingScreen;
    private bool _classicProgrammeRequested;
    private AcceptDialog? _classicTicketDialog;

    private void CloseClassicBookingScreen()
    {
        if (_classicBookingLayer is not null) { _classicBookingLayer.Hide(); _classicBookingLayer.QueueFree(); }
        _classicBookingLayer = null; _classicBookingScreen = null;
    }
    private void ResetClassicTickets()
    {
        CloseClassicBookingScreen(); _classicProgrammeRequested = false;
        if (_classicTicketDialog is not null) { _classicTicketDialog.Hide(); _classicTicketDialog.QueueFree(); _classicTicketDialog = null; }
    }
    internal void ClassicBookingMenu()
    {
        SaveMilestone("Classic booking menu"); CloseClassicBookingScreen(); BuildStartSplash();
    }
    private void RefreshClassicTickets()
    {
        if (!_session.IsClassicCampaign || _session.PreparedStatus != PreparationStatus.Preparing || StartMenu.IsOpen ||
            ClassicForecastOpen || _session.CapturePerks()?.Pending == true)
        { CloseClassicBookingScreen(); return; }
        var phase = _session.CaptureClassicBookingDetails()!.Phase;
        if (phase != ClassicBookingPhase.Booking && !_classicProgrammeRequested) { CloseClassicBookingScreen(); return; }
        if (_classicBookingLayer is null)
        {
            _classicBookingLayer = new CanvasLayer { Layer = 24, Name = "ClassicBookingLayer" }; AddChild(_classicBookingLayer);
            _classicBookingScreen = new ClassicBookingScreen(); _classicBookingLayer.AddChild(_classicBookingScreen);
            _classicBookingScreen.Build(this, () =>
            {
                if (_session.CaptureClassicBookingDetails()!.Phase == ClassicBookingPhase.Booking) ReopenClassicForecast();
                else { _classicProgrammeRequested = false; _hudWorkspaceOpen = _buildDrawerOpen = false; CloseClassicBookingScreen(); RefreshHudWorkspace(); }
            }, ConfirmClassicTickets, ConfirmClassicCancellation, PreviewClassicProgrammeChange);
        }
        _classicBookingScreen!.Refresh();
    }
    private void ConfirmClassicTickets()
    {
        if (_session.CaptureClassicBookingDetails()?.Phase != ClassicBookingPhase.Booking) return;
        var previous = _session.CapturePersistentCampaignState()!.CashPennies;
        if (!_host.ExecuteMilestone(new ConfirmClassicBookingsCommand(), "Classic bookings and ticket sales", out var error))
        { _preparationMessage = error ?? "Booking could not be confirmed."; RefreshPreparationHud(); return; }
        _classicProgrammeRequested = false; CloseClassicBookingScreen();
        _hudWorkspaceOpen = _buildDrawerOpen = false;
        SyncSaveStatus(); RefreshPreparationHud();
        var booking = _session.CaptureClassicBookingDetails()!;
        var dialog = ClassicTicketDialog("Bookings confirmed", false, "Continue to site preparation →");
        var body = ClassicDialogBody(dialog, 920, 514);
        body.AddChild(Ui.Caps("Bookings confirmed", Ui.Ink, 16));
        body.AddChild(Ui.Heading($"{booking.OriginalTickets} tickets sold", 32));
        body.AddChild(ClassicWords($"{FestivalCurrency.Format(booking.TicketPricePennies)} per ticket · Capacity {_session.ClassicSiteCapacity} · Your programme is booked."));
        body.AddChild(new HSeparator());
        var columns = new HBoxContainer(); columns.AddThemeConstantOverride("separation", Ui.Px(40)); body.AddChild(columns);
        var left = new VBoxContainer { CustomMinimumSize = Ui.S(400, 0) }; left.AddThemeConstantOverride("separation", Ui.Px(18)); columns.AddChild(left);
        left.AddChild(Ui.Heading("Ready to prepare the site", 26));
        left.AddChild(ClassicWords($"Build, staff and stock for {booking.TicketHolders} attendees.\n\nProgramme changes remain possible. Changes after sales may disappoint attendees.\n\nReview the opening checklist before starting."));
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", Ui.Px(14)); columns.AddChild(right);
        right.AddChild(Ui.Heading("Your site-prep funds", 26));
        right.AddChild(ClassicWords($"Previous balance  {FestivalCurrency.Format(previous)}\nTicket sales · {booking.OriginalTickets} × {FestivalCurrency.Format(booking.TicketPricePennies)}  + {FestivalCurrency.Format((long)booking.OriginalTickets * booking.TicketPricePennies)}\nConfirmed band spend  − {FestivalCurrency.Format(booking.Contracts.Sum(a => (long)a.PaidFeePennies))}"));
        var budget = new PanelContainer(); budget.AddThemeStyleboxOverride("panel", Ui.Box(Ui.TealWash, 6, padX: 16, padY: 16)); right.AddChild(budget);
        var funds = new VBoxContainer(); budget.AddChild(funds);
        funds.AddChild(ClassicWords("Available for site preparation"));
        funds.AddChild(Ui.Heading(FestivalCurrency.Format(_session.CapturePersistentCampaignState()!.CashPennies), 34));
        funds.AddChild(ClassicWords("Bands paid · no second charge at Start"));
        ShowClassicTicketDialog(dialog, 920, 514);
    }
    private void PreviewClassicProgrammeChange(string[] ids, int slot)
    {
        if (_classicTicketDialog is not null || _session.CaptureClassicBookingDetails() is not { Phase: ClassicBookingPhase.SitePreparation } before) return;
        var command = new RebookClassicProgrammeCommand(ids.ToArray());
        var issue = _session.ValidateCommand(_host.Envelope(command));
        if (issue is not null) { _preparationMessage = issue.Message; RefreshPreparationHud(); return; }
        var quotes = ids.Select(id => id == "" ? ClassicAnnouncedAct.EmptySlot :
            _session.GetFestivalActs().Single(a => a.Id == id) is { } act ? new ClassicAnnouncedAct(id, act.Genre, act.Popularity, _session.ActFee(act)) : throw new InvalidOperationException()).ToArray();
        var projected = ClassicBookingLedger.Evaluate(_session.CampaignSeed, _session.FestivalNumber,
            ClassicBookingLedger.Rebook(_session.CampaignSeed, _session.FestivalNumber, _session.CaptureClassicBooking()!, quotes));
        var movements = projected.Money.Skip(before.Money.Length).ToArray();
        var releases = movements.Where(m => m.Kind == ClassicBookingMoneyKind.BandRelease).Sum(m => m.CashPennies);
        var fees = -movements.Where(m => m.Kind == ClassicBookingMoneyKind.BandFee).Sum(m => m.CashPennies);
        var refunds = -movements.Where(m => m.Kind == ClassicBookingMoneyKind.TicketRefund).Sum(m => m.CashPennies);
        var delta = projected.CashChangePennies - before.CashChangePennies;
        var dialog = (ConfirmationDialog)ClassicTicketDialog("Programme change · tickets already sold", true,
            ids[slot] == "" ? "Release act" : "Confirm programme change");
        dialog.CancelButtonText = "Keep current booking";
        var body = ClassicDialogBody(dialog, 660, 514);
        body.AddChild(Ui.Heading(ids[slot] == "" ? "Release this booking?" : "Change your programme?", 30));
        string NameOf(string id) => id == "" ? "Empty set" : ActCatalogue.Find(id)!.Name;
        body.AddChild(ClassicWords($"{StageTitle(_session.Stages[slot/3])} · Set {slot % 3 + 1} · {(slot % 3 == 2 ? "Headline" : "Support")}\n{NameOf(before.Contracts[slot].ActId)}  →  {NameOf(ids[slot])}"));
        body.AddChild(new HSeparator());
        var impact = projected.HighestChange switch
        {
            ClassicLineupChange.None => "The same acts remain booked; no attendee refund or new disappointment.",
            ClassicLineupChange.Comparable => "A comparable replacement; less disruption for attendees.",
            ClassicLineupChange.Ordinary => "A change from the announced programme may disappoint attendees.",
            _ => "An unmatched announced headliner or a 15-point popularity loss has substantial attendee impact."
        };
        body.AddChild(ClassicWords(impact + $"\nHighest impact retained: {ClassicBookingTerms.RefundPercent(projected.HighestChange)}% ticket refund; {projected.Disappointment / 100} satisfaction points.\nAdditional refunds now: {projected.RefundedTickets - before.RefundedTickets} tickets · {FestivalCurrency.Format(refunds)}."));
        body.AddChild(ClassicWords($"Band refund  + {FestivalCurrency.Format(releases)}   /   Replacement fees  − {FestivalCurrency.Format(fees)}\nNet cash change  {FestivalCurrency.Format(delta)}\nSite-prep funds after change  {FestivalCurrency.Format(_session.CapturePersistentCampaignState()!.CashPennies + delta)}\n" +
            (ids.Any(id => id == "") ? "Empty sets must be filled before opening. " : "") + "This does not run ticket sales again."));
        var expectedSession = _session; var expectedHash = _session.CaptureSnapshot().AuthoritativeHash;
        dialog.Confirmed += () =>
        {
            if (ReferenceEquals(expectedSession, _session) && expectedHash == _session.CaptureSnapshot().AuthoritativeHash)
            {
                _host.ExecuteMilestone(command, "Classic programme change", out var error); _preparationMessage = error ?? "Programme changed; refunds recorded.";
                _classicBookingScreen?.AcceptedEdit(slot%3); SyncSaveStatus(); RefreshPreparationHud();
            }
            _classicBookingScreen?.FocusSet(slot%3);
        };
        dialog.Canceled += () => _classicBookingScreen?.FocusSet(slot%3);
        ShowClassicTicketDialog(dialog, 660, 514);
    }
    private void ConfirmClassicCancellation()
    {
        if (_classicTicketDialog is not null || _session.CaptureClassicBookingDetails() is not { Phase: ClassicBookingPhase.SitePreparation } booking) return;
        var returned = (long)booking.TicketHolders * booking.TicketPricePennies;
        var bands = booking.Contracts.Sum(a => (long)a.PaidFeePennies);
        var dialog = (ConfirmationDialog)ClassicTicketDialog("Cancel this festival?", true, "Cancel festival");
        dialog.CancelButtonText = "Keep festival";
        var body = ClassicDialogBody(dialog, 660, 420);
        body.AddChild(Ui.Heading("Cancel this festival?", 30));
        body.AddChild(ClassicWords($"Last resort: return to booking Festival {_session.FestivalNumber}.\n\nRefund {booking.TicketHolders} remaining tickets: − {FestivalCurrency.Format(returned)}\nRelease all band contracts: + {FestivalCurrency.Format(bands)}\nResulting cash: {FestivalCurrency.Format(_session.CapturePersistentCampaignState()!.CashPennies - returned + bands)}\nReputation: {booking.Reputation} → {Math.Max(0, booking.Reputation - ClassicBookingTerms.CancellationReputationPenalty)}\n\nWeather and the underlying ticket market stay the same. Your unpaid site plan will be cleared."));
        var expectedSession = _session; var expectedHash = _session.CaptureSnapshot().AuthoritativeHash;
        dialog.Confirmed += () =>
        {
            if (!ReferenceEquals(expectedSession, _session) || expectedHash != _session.CaptureSnapshot().AuthoritativeHash) return;
            _host.ExecuteMilestone(new CancelClassicFestivalCommand(), "Classic cancellation", out var error);
            _preparationMessage = error ?? "Cancelled and refunded. Draft a new booking for this festival.";
            _classicProgrammeRequested = false; CloseClassicBookingScreen(); SyncSaveStatus(); RefreshPreparationHud();
        };
        ShowClassicTicketDialog(dialog, 660, 420);
    }
    private AcceptDialog ClassicTicketDialog(string title, bool confirmation, string action)
    {
        AcceptDialog dialog = confirmation ? new ConfirmationDialog() : new AcceptDialog();
        dialog.Title = title; dialog.Name = confirmation ? "ClassicContractConfirmation" : "ClassicSalesReceipt";
        dialog.OkButtonText = action; dialog.Theme = HudTheme(); dialog.Exclusive = true; dialog.Borderless = true;
        dialog.GetOkButton().Name = confirmation ? "ClassicApplyContract" : "ClassicReceiptContinue";
        if (dialog is ConfirmationDialog confirm) confirm.GetCancelButton().Name = "ClassicKeepContract";
        Ui.Style(dialog.GetOkButton(), Ui.ButtonKind.Primary, 16);
        dialog.GetOkButton().CustomMinimumSize = Ui.S(0, 44);
        if (dialog is ConfirmationDialog safe) { Ui.Style(safe.GetCancelButton(), Ui.ButtonKind.Secondary, 16); safe.GetCancelButton().CustomMinimumSize = Ui.S(0, 44); }
        dialog.Theme.SetStylebox("panel", "AcceptDialog", Ui.Sheet(24, 22));
        dialog.GetLabel().Hide(); AddChild(dialog); _classicTicketDialog = dialog;
        dialog.Confirmed += () => { if (_classicTicketDialog == dialog) _classicTicketDialog = null; dialog.Hide(); dialog.QueueFree(); };
        dialog.Canceled += () => { if (_classicTicketDialog == dialog) _classicTicketDialog = null; dialog.Hide(); dialog.QueueFree(); };
        return dialog;
    }
    private static VBoxContainer ClassicDialogBody(AcceptDialog dialog, int width, int height)
    {
        var frame = new Control { CustomMinimumSize = Ui.S(width - 48, height - 100) };
        dialog.AddChild(frame);
        var body = new VBoxContainer { Name = "ClassicDialogBody", Position = Ui.S(6, 6), Size = Ui.S(width - 60, height - 112) };
        body.AddThemeConstantOverride("separation", Ui.Px(14)); frame.AddChild(body); return body;
    }
    private static Label ClassicWords(string text)
    {
        var label = Ui.Text(text, 16, Ui.Ink); label.AutowrapMode = TextServer.AutowrapMode.WordSmart; return label;
    }
    private static void ShowClassicTicketDialog(AcceptDialog dialog, int width, int height)
    {
        dialog.PopupCentered(new Vector2I(Ui.Px(width), Ui.Px(height)));
        if (dialog is ConfirmationDialog confirmation) confirmation.GetCancelButton().CallDeferred(Control.MethodName.GrabFocus);
        else dialog.GetOkButton().CallDeferred(Control.MethodName.GrabFocus);
    }
}

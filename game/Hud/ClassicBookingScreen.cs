using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

/// <summary>Native implementation of the approved three-column booking concept.</summary>
internal sealed partial class ClassicBookingScreen : Control
{
    private IHudHost _hud = null!;
    private BookingPanel _booking = null!;
    private SpinBox _price = null!;
    private Label _heading = null!, _lead = null!, _range = null!, _capacity = null!, _explanation = null!, _money = null!, _message = null!;
    private VBoxContainer _priceBox = null!;
    private Button _confirm = null!, _cancel = null!, _back = null!;

    public void Build(IHudHost hud, Action back, Action confirm, Action cancel, Action<string[], int> edit)
    {
        _hud = hud; Name = "ClassicBookingScreen"; Size = hud.Viewport.GetVisibleRect().Size; Theme = HudTheme();
        MouseFilter = MouseFilterEnum.Stop;
        var background = new ColorRect { Color = Ui.Bar, MouseFilter = MouseFilterEnum.Stop };
        AddChild(background); background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bar = new HBoxContainer { Position = Ui.S(28, 14), Size = Ui.S(1224, 44) }; AddChild(bar);
        var title = Ui.Text($"LOWER WITTERING FARM\nCLASSIC{(hud.Session.IsClassicDevelopment?" DEVELOPMENT TEST":"")} · Festival {hud.Session.FestivalNumber}", 16, Ui.BarText, Ui.BodyBold);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill; bar.AddChild(title);
        var phases = Ui.Text("1 · Band booking    →    2 · Site preparation", 18, Ui.BarText); bar.AddChild(phases);
        bar.AddChild(new Control { CustomMinimumSize = Ui.S(100, 0) });
        var menu = Ui.Style(new Button { Name = "ClassicBookingMenu", Text = "Main menu", CustomMinimumSize = Ui.S(114, 44) }, Ui.ButtonKind.Secondary, 16);
        bar.AddChild(menu); menu.Pressed += () => ((Main)hud).ClassicBookingMenu();

        var sheet = new PanelContainer { Name = "ClassicBookingSheet", Position = Ui.S(24, 88), Size = Ui.S(1232, 550) };
        sheet.AddThemeStyleboxOverride("panel", Ui.Sheet(24, 22)); AddChild(sheet);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", Ui.Px(8)); sheet.AddChild(box);
        _heading = Ui.Heading("", 32); box.AddChild(_heading);
        _lead = Ui.Text("", 16, Ui.InkMuted); box.AddChild(_lead);
        box.AddChild(new Control { CustomMinimumSize = Ui.S(0, 8) });
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", Ui.Px(22)); box.AddChild(columns);
        var bands = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddChild(bands);
        _booking = new(hud, () => { }, text => { if (_message is not null) _message.Text = text; }, true, edit);
        _booking.Build(bands);
        columns.AddChild(new ColorRect { Color = Ui.PaperRule, CustomMinimumSize = Ui.S(1, 0), MouseFilter = MouseFilterEnum.Ignore });
        var tickets = new VBoxContainer { CustomMinimumSize = Ui.S(288, 0) };
        tickets.AddThemeConstantOverride("separation", Ui.Px(6)); columns.AddChild(tickets);
        tickets.AddChild(Ui.Heading("Ticket sales", 24));
        _priceBox = new VBoxContainer(); tickets.AddChild(_priceBox);
        var prices = new HBoxContainer(); prices.AddThemeConstantOverride("separation", Ui.Px(8)); _priceBox.AddChild(prices);
        var minus = Ui.Style(new Button { Name = "ClassicPriceMinus", Text = "−", TooltipText = "Reduce ticket price by £1", CustomMinimumSize = Ui.S(42, 44) }, Ui.ButtonKind.Secondary, 18);
        prices.AddChild(minus);
        _price = new SpinBox { Name = "ClassicTicketPrice", MinValue = 0, MaxValue = 100, Step = 0.01, Prefix = "£", CustomMinimumSize = Ui.S(120, 44),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "Ticket price in pounds, from £0 to £100. Enter pounds and pence." };
        prices.AddChild(_price); _price.GetLineEdit().AddThemeFontSizeOverride("font_size", Ui.Px(20));
        _price.GetLineEdit().AddThemeStyleboxOverride("normal", Ui.Box(Ui.PaperBright, 4, Ui.PaperEdge, 1, 8, 4));
        _price.GetLineEdit().AddThemeColorOverride("font_color", Ui.Ink);
        _price.GetLineEdit().AddThemeStyleboxOverride("focus", Ui.Box(Ui.PaperBright, 4, Ui.GoldShadow, 2, 8, 4));
        var plus = Ui.Style(new Button { Name = "ClassicPricePlus", Text = "+", TooltipText = "Increase ticket price by £1", CustomMinimumSize = Ui.S(42, 44) }, Ui.ButtonKind.Secondary, 18);
        prices.AddChild(plus);
        minus.Pressed += () => SetPrice(_price.Value - 1); plus.Pressed += () => SetPrice(_price.Value + 1);
        _price.ValueChanged += SetPrice;
        _range = Ui.Heading("", 28); tickets.AddChild(_range);
        _capacity = Ui.Text("", 16, Ui.InkMuted); tickets.AddChild(_capacity);
        _explanation = Ui.Text("", 16, Ui.Ink); _explanation.AutowrapMode = TextServer.AutowrapMode.WordSmart; tickets.AddChild(_explanation);
        tickets.AddChild(new HSeparator());
        _money = Ui.Text("", 16, Ui.Ink, Ui.BodySemi); _money.AutowrapMode = TextServer.AutowrapMode.WordSmart; tickets.AddChild(_money);
        _cancel = Ui.Style(new Button { Name = "ClassicCancelFestival", Text = "Cancel festival…", CustomMinimumSize = Ui.S(0, 42) }, Ui.ButtonKind.Secondary, 16);
        tickets.AddChild(_cancel); _cancel.Pressed += cancel;

        var footer = new HBoxContainer { Position = Ui.S(24, 649), Size = Ui.S(1232, 44) };
        footer.AddThemeConstantOverride("separation", Ui.Px(18)); AddChild(footer);
        _back = Ui.Style(new Button { Name = "ClassicBookingBack", CustomMinimumSize = Ui.S(160, 44) }, Ui.ButtonKind.Secondary, 16);
        footer.AddChild(_back); _back.Pressed += back;
        _message = Ui.Text("Draft freely; nothing charged.", 16, Ui.BarText); _message.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _message.AutowrapMode = TextServer.AutowrapMode.WordSmart; footer.AddChild(_message);
        _confirm = Ui.Style(new Button { Name = "ClassicConfirmBookings", Text = "Confirm bookings & sell tickets →", CustomMinimumSize = Ui.S(290, 44) }, Ui.ButtonKind.Primary, 16);
        footer.AddChild(_confirm); _confirm.Pressed += confirm;
        Refresh();
    }

    private void SetPrice(double pounds)
    {
        var pennies = (int)Math.Round(Math.Clamp(pounds, 0, 100) * 100, MidpointRounding.AwayFromZero);
        if (_hud.Session.CaptureClassicBooking()!.DraftTicketPricePennies == pennies) return;
        _hud.Host.Execute(new SetClassicTicketPriceCommand(pennies), out var error);
        if (error is not null) _hud.Message = error;
        Refresh();
    }
    public void FocusSet(int slot) => FindChildren("ClassicSet" + (slot + 1), "Button", true, false).OfType<Button>().FirstOrDefault()?.CallDeferred(Control.MethodName.GrabFocus);
    public void AcceptedEdit(int slot) { _booking.ClearSelection(); FocusSet(slot); }
    public void Refresh()
    {
        var s = _hud.Session; var booking = s.CaptureClassicBookingDetails()!;
        var draft = booking.Phase == ClassicBookingPhase.Booking;
        _heading.Text = draft ? "Book your festival" : "Manage your programme";
        _lead.Text = draft ? "Draft freely. Choose every act and your ticket price, then confirm to resolve sales." :
            $"{booking.TicketHolders} ticket holders · {booking.RefundedTickets} refunded. Review changes before applying them.";
        _back.Text = draft ? "Back to forecast" : "Back to site preparation";
        _priceBox.Visible = _confirm.Visible = draft; _cancel.Visible = !draft;
        _price.SetValueNoSignal(booking.TicketPricePennies / 100d);
        _capacity.Text = $"Maximum capacity: {s.ClassicSiteCapacity}";
        var cash = s.CapturePersistentCampaignState()!.CashPennies;
        if (draft)
        {
            var g = s.ClassicBookingEstimate!;
            _range.Text = $"{g.Sales.MinimumSales}–{g.Sales.MaximumSales} tickets";
            _capacity.Text = $"Estimated sales · capacity {s.ClassicSiteCapacity}";
            _explanation.Text = "Reputation and popularity draw buyers. Higher prices reduce demand; audiences overlap.";
            if(s.IsClassicDevelopment)_explanation.Text=s.CaptureClassicDevelopment()!.GuaranteeHundred?
                "DIAGNOSTIC: 100 ticket-holders guaranteed. Sales and refunds still use real ticket money.":
                "Normal demand at the test site's 100-attendee capacity. The strongest three acts share one overlapping audience.";
            _money.Text = $"Current balance  {FestivalCurrency.Format(cash)}\nBands on confirm  {FestivalCurrency.Format(g.BandFeesPennies)}\nLow-end sales  {FestivalCurrency.Format((long)g.Sales.MinimumSales * booking.TicketPricePennies)}\n" +
                (g.ConservativeSetupCashPennies < 0 ? "Projected shortfall" : "Projected funds") + $"  {FestivalCurrency.Format(g.ConservativeSetupCashPennies)}\n" +
                (g.Risky ? "Risk: little left for site prep. You can still confirm." : "Estimate only; actual sales may vary.");
            _money.AddThemeColorOverride("font_color", g.Risky ? Ui.Alert : Ui.Ink);
            var issue = s.ValidateCommand(_hud.Host.Envelope(new ConfirmClassicBookingsCommand()));
            _confirm.Disabled = issue is not null; _confirm.TooltipText = issue?.Message ?? "Commit band fees and resolve ticket sales once.";
        }
        else
        {
            _range.Text = $"{booking.TicketHolders} tickets";
            _explanation.Text = $"{FestivalCurrency.Format(booking.TicketPricePennies)} per ticket · price locked.\nProgramme changes affect existing attendees. Sales will not run again.";
            _money.Text = $"Net ticket income\n{FestivalCurrency.Format(booking.NetTicketReceiptsPennies)}\nCurrent funds\n{FestivalCurrency.Format(cash)}\nBands paid\n{FestivalCurrency.Format(booking.NetBandCostsPennies)}";
            _money.AddThemeColorOverride("font_color", Ui.Ink);
        }
        _booking.Refresh();
    }
}

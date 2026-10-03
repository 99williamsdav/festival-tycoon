using Festival.Simulation;
using Festival.ContentAdapter;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private VBoxContainer? _immersionControls;
    private Label? _immersionSummary;
    private Button? _immersionStockButton;
    private Button? _immersionMoveButton;
    private Button? _freeWaterButton;
    private readonly Dictionary<string, StaticBody3D> _immersionVendors = [];
    private readonly Dictionary<ulong, string> _immersionVendorPicks = [];
    private string? _selectedImmersionVendor;
    private VBoxContainer? _immersionNeedSection;
    private ProgressBar? _immersionHungerBar;
    private ProgressBar? _immersionIntoxBar;
    private ProgressBar? _immersionToiletBar;
    private Label? _immersionNeedLabel;
    private Label? _immersionHungerLabel;
    private Label? _immersionIntoxLabel;
    private Label? _immersionToiletLabel;
    private readonly Dictionary<ulong, Label3D> _immersionWarningLabels = [];
    private readonly Dictionary<ulong, long> _immersionLastRemark = [];
    private Label3D? _immersionRemark;
    private ulong? _immersionRemarkPerson;
    private long _immersionRemarkUntil;
    private long _immersionLastGlobalRemark = -640;

    private void ResetImmersionCuePresentation()
    {
        foreach (var label in _immersionWarningLabels.Values) { label.Visible = false; label.QueueFree(); }
        _immersionWarningLabels.Clear(); _immersionLastRemark.Clear();
        if (_immersionRemark is not null) { _immersionRemark.Visible = false; _immersionRemark.QueueFree(); }
        _immersionRemark = null; _immersionRemarkPerson = null; _immersionRemarkUntil = 0;
        _immersionLastGlobalRemark = _session.CurrentTick;
        // A load does not replay previous routine remarks.
        foreach (var person in _session.CaptureImmersion()?.People ?? []) _immersionLastRemark[person.AgentId] = _session.CurrentTick;
    }
    private void AdvanceImmersionCuePresentation()
    {
        foreach (var label in _immersionWarningLabels.Values) label.Visible = false;
        if (_immersionRemark is not null) _immersionRemark.Visible = false;
        if (_session.CaptureImmersion() is not { } state) return;
        var onSite = (_session.CapturePreparation()?.People ?? []).Where(p => p.Admitted && !p.Departed).Select(p => p.AgentId).ToHashSet();
        foreach (var person in state.People.Where(p => p.Intoxication >= 7500 && onSite.Contains(p.AgentId)))
        {
            if (_hudAlerts is not null) continue;
            if (!_attendeeVisuals.TryGetValue(new EntityId(person.AgentId), out var body)) continue;
            if (_medicalCueLabels.TryGetValue(person.AgentId, out var medical) && medical.Visible ||
                _disorderCueLabels.TryGetValue(person.AgentId, out var disorder) && disorder.Visible) continue;
            if (!_immersionWarningLabels.TryGetValue(person.AgentId, out var label))
            {
                label = WorldText.Speech(new Label3D { Text = "! Too much beer • need care!",
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = new Color("ffdb73") }, 36);
                AddChild(label); _immersionWarningLabels.Add(person.AgentId, label);
            }
            label.Position = body.Position + new Vector3(0, 2.7f, 0); label.Visible = true;
        }
        // Others' speech comes first; this waits for a gap in the crowd's three bubbles.
        if (_immersionRemarkPerson is null || _session.CurrentTick >= _immersionRemarkUntil) { if (SpeechCrowded()) return; }
        if (_immersionRemarkPerson is { } active && _session.CurrentTick < _immersionRemarkUntil &&
            _attendeeVisuals.TryGetValue(new EntityId(active), out var activeBody))
        { _immersionRemark!.Position = activeBody.Position + new Vector3(0, 2.35f, 0); _immersionRemark.Visible = true; return; }
        if (_session.CurrentTick - _immersionLastGlobalRemark < 640) return;
        var candidate = state.People.FirstOrDefault(p => p.Intoxication is >= 2500 and < 7500 && _session.ImmersionHandsAvailable(p.AgentId) &&
            (!_immersionLastRemark.TryGetValue(p.AgentId, out var last) || _session.CurrentTick - last >= 3200));
        if (candidate is null || !_attendeeVisuals.TryGetValue(new EntityId(candidate.AgentId), out var visual)) return;
        if (_immersionRemark is null)
        {
            _immersionRemark = WorldText.Speech(new Label3D { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled }, 36);
            AddChild(_immersionRemark);
        }
        var lightweight = _session.GuestCharacterOf(candidate.AgentId).Lightweight && candidate.AgentId % 2 == 0;
        string[] lines = candidate.Intoxication >= 5000 ? ["Feeling wobbly • time for a rest", "Everything's a bit spinny", "Need to sit down…"]
            : lightweight ? ["I've only had one!", "That's gone to my head"]
            : ["Feeling a bit tipsy", "Woo! Love this lot!", "Who wants another?", "I'm not drunk, you're drunk"];
        _immersionRemark.Text = lines[(int)((candidate.AgentId + (ulong)_session.CurrentTick / 80) % (ulong)lines.Length)];
        _immersionRemark.Modulate = MoodColour(candidate.Intoxication >= 5000 ? Mood.Grumble : Mood.Happy);
        _immersionRemark.Position = visual.Position + new Vector3(0, 2.35f, 0); _immersionRemark.Visible = true;
        _immersionRemarkPerson = candidate.AgentId; _immersionRemarkUntil = _session.CurrentTick + 240;
        _immersionLastGlobalRemark = _session.CurrentTick; _immersionLastRemark[candidate.AgentId] = _session.CurrentTick;
    }

    private void BuildImmersionNeedBars(VBoxContainer parent)
    {
        _immersionNeedSection = new VBoxContainer { Visible = false }; parent.AddChild(_immersionNeedSection);
        _immersionNeedLabel = LabelText("", 13, new Color("29352c")); _immersionNeedSection.AddChild(_immersionNeedLabel);
        foreach (var title in new[] { "HUNGER", "TOILET NEED", "INTOXICATION • FICTIONAL EXPOSURE" })
        {
            var label = LabelText(title, 12, new Color("29352c")); _immersionNeedSection.AddChild(label);
            if (title == "HUNGER") _immersionHungerLabel = label;
            else if (title == "TOILET NEED") _immersionToiletLabel = label;
            else _immersionIntoxLabel = label;
            var bar = new ProgressBar { MinValue = 0, MaxValue = 100, CustomMinimumSize = new Vector2(340, 15), ShowPercentage = true };
            _immersionNeedSection.AddChild(bar);
            if (title == "HUNGER") _immersionHungerBar = bar;
            else if (title == "TOILET NEED") _immersionToiletBar = bar;
            else _immersionIntoxBar = bar;
        }
    }
    private void RefreshImmersionNeedBars(ulong? id)
    {
        if (_immersionNeedSection is null) return;
        var person = id is { } selected ? _session.CaptureImmersion()?.People.SingleOrDefault(p => p.AgentId == selected) : null;
        _immersionNeedSection.Visible = person is not null;
        if (person is null) return;
        _immersionHungerBar!.Value = person.Hunger / 100d;
        _immersionToiletBar!.Value = person.ToiletNeed / 100d;
        _immersionIntoxBar!.Value = person.Intoxication / 100d;
        if (Top.IsBuilt)
        {
            _immersionHungerLabel!.Text = $"HUNGER  {person.Hunger / 100m:0}%";
            _immersionToiletLabel!.Text = $"TOILET NEED  {person.ToiletNeed / 100m:0}%";
            _immersionIntoxLabel!.Text = $"INTOXICATION  {person.Intoxication / 100m:0}%";
            _immersionHungerBar.ShowPercentage = false; _immersionToiletBar.ShowPercentage = false; _immersionIntoxBar.ShowPercentage = false;
        }
        _immersionHungerBar.Modulate = MedicalNeedColor(person.Hunger);
        _immersionToiletBar.Modulate = MedicalNeedColor(person.ToiletNeed);
        _immersionIntoxBar.Modulate = new Color(person.Intoxication >= 7500 ? "ff7566" : person.Intoxication >= 5000 ? "e8b45b" : "a6c887");
        _immersionNeedLabel!.Text = person.Intoxication >= 7500 ? "HEAVY INTOXICATION • CARE AVAILABLE" : person.Intoxication >= 5000 ? "IMPAIRED • COORDINATION REDUCED" : person.Intoxication >= 2500 ? "TIPSY" : "ADULT FOOD & DRINK NEEDS";
    }

    private static Vector3 ImmersionPosition(GridCell cell)
    {
        var centre = TraversalGrid.CellCentre(cell);
        return new(centre.XMillimetres / 1000f, 0, centre.ZMillimetres / 1000f);
    }
    private static string ImmersionProductKey(ImmersionProduct product) => product switch
    { ImmersionProduct.Chips => "chips", ImmersionProduct.SoftDrink or ImmersionProduct.Water => "soft", _ => "beer" };
    private static string ImmersionProductName(ImmersionProduct product) => product switch
    { ImmersionProduct.Chips => "Chips", ImmersionProduct.SoftDrink => "Soft drink", ImmersionProduct.Water => "Free water", _ => "Beer" };

private void BuildImmersionControls(VBoxContainer parent)
    {
        _immersionControls = new VBoxContainer(); _immersionControls.AddThemeConstantOverride("separation", 0); parent.AddChild(_immersionControls);
        var heading = new HBoxContainer(); _immersionControls.AddChild(heading);
        var caption = Ui.Caps("Food & drink stock", Ui.InkMuted); caption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        caption.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; heading.AddChild(caption);
        _immersionStockButton = Ui.Style(new Button { Text = "Starter bundle · 40 / 40 / 32", CustomMinimumSize = new Vector2(0, Ui.S(28)),
            MouseDefaultCursorShape = Control.CursorShape.PointingHand }, Ui.ButtonKind.Secondary, 12.5f);
        _immersionStockButton.Pressed += () => CommitEquipmentAction(new SetPreparationStockCommand(40, 40, 32));
        _immersionStockButton.TooltipText = "40 chips (£1 each), 40 soft drinks (60p each), 32 beers (£1 each). Paid from festival funds once before opening; no in-day refill.";
        heading.AddChild(_immersionStockButton);
        _immersionSummary = Ui.Text("", 13, Ui.InkMuted); _immersionSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _immersionControls.AddChild(_immersionSummary);
        if (_session.CapturePreparationPlan() is null) { RefreshImmersionControls(); return; }
        _immersionSummary.Visible = false;
        _stockClear = Ui.Style(new Button { Text = "None", CustomMinimumSize = new Vector2(0, Ui.S(28)), TooltipText = "Remove all planned stock.",
            MouseDefaultCursorShape = Control.CursorShape.PointingHand }, Ui.ButtonKind.Quiet, 12.5f);
        _stockClear.Pressed += () => CommitEquipmentAction(new SetPreparationStockCommand(0, 0, 0));
        heading.AddChild(_stockClear);
        _immersionControls.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(6)) });
        var header = StockLine(Ui.Caps("Item", Ui.InkMuted), Ui.Caps("Cost → sells", Ui.InkMuted), Ui.Caps("Quantity", Ui.InkMuted), Ui.Caps("Cost", Ui.InkMuted), true);
        _immersionControls.AddChild(header);
        var items = new[] { ("Chips", "utensils", new Color("b85c28"), 100, 300), ("Soft drink", "cup-soda", new Color("b84a3a"), 60, 200), ("Beer", "beer", new Color("a87a1f"), 100, 300) };
        for (var index = 0; index < 3; index++)
        {
            var (name, icon, tile, cost, sale) = items[index];
            var label = new HBoxContainer(); label.AddThemeConstantOverride("separation", Ui.Px(10));
            var badge = new PanelContainer { CustomMinimumSize = Ui.S(32, 32) }; badge.AddThemeStyleboxOverride("panel", Ui.Box(tile, 6));
            var glyph = Ui.IconRect(icon, 18, Colors.White); glyph.SizeFlagsHorizontal = glyph.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; badge.AddChild(glyph);
            label.AddChild(badge);
            var title = Ui.Text(name, 15, Ui.Ink, Ui.BodyBold); title.VerticalAlignment = VerticalAlignment.Center; label.AddChild(title);
            var prices = Ui.Text($"{FestivalCurrency.Format(cost)} → {FestivalCurrency.Format(sale)}", 14, Ui.Ink);
            var slot = index;
            var stepper = new HBoxContainer(); stepper.AddThemeConstantOverride("separation", 0);
            Button Step(string glyphName, int delta)
            {
                var step = new Button { Icon = Ui.Icon(glyphName), ExpandIcon = true, IconAlignment = HorizontalAlignment.Center, CustomMinimumSize = Ui.S(34, 34),
                    TooltipText = delta > 0 ? $"Plan 4 more {name.ToLowerInvariant()}" : $"Plan 4 fewer {name.ToLowerInvariant()}", MouseDefaultCursorShape = Control.CursorShape.PointingHand };
                Ui.Style(step, Ui.ButtonKind.Secondary);
                var face = Ui.Box(Ui.GoldWash, 6, Ui.PaperEdge, 1);
                foreach (var state in new[] { "normal", "pressed", "focus" }) step.AddThemeStyleboxOverride(state, face);
                foreach (var state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" }) step.AddThemeColorOverride(state, Ui.Ink);
                step.Pressed += () => CommitPlannedStock(slot, PlannedStock(slot) + delta);
                return step;
            }
            stepper.AddChild(Step("minus", -4));
            var amount = new LineEdit { CustomMinimumSize = Ui.S(52, 34), Alignment = HorizontalAlignment.Center, TooltipText = $"Planned {name.ToLowerInvariant()}" };
            amount.AddThemeFontOverride("font", Ui.SlabBold); amount.AddThemeFontSizeOverride("font_size", Ui.Px(17));
            amount.AddThemeColorOverride("font_color", Ui.Ink);
            var field = Ui.Box(Colors.White, 0, Ui.PaperEdge, 1); field.BorderWidthLeft = field.BorderWidthRight = 0;
            amount.AddThemeStyleboxOverride("normal", field); amount.AddThemeStyleboxOverride("focus", field);
            amount.TextSubmitted += text => CommitPlannedStock(slot, int.TryParse(text, out var value) ? value : PlannedStock(slot));
            amount.FocusExited += () => CommitPlannedStock(slot, int.TryParse(amount.Text, out var value) ? value : PlannedStock(slot));
            stepper.AddChild(amount);
            stepper.AddChild(Step("plus", 4));
            var total = Ui.Heading("", 17); total.HorizontalAlignment = HorizontalAlignment.Right;
            _stockRows[index] = (amount, total);
            _immersionControls.AddChild(StockLine(label, prices, stepper, total, false));
        }
        var footer = new HBoxContainer(); footer.AddThemeConstantOverride("separation", Ui.Px(8));
        var footerMargin = new MarginContainer(); footerMargin.AddThemeConstantOverride("margin_top", Ui.Px(8)); footerMargin.AddThemeConstantOverride("margin_left", Ui.Px(4));
        footerMargin.AddThemeConstantOverride("margin_right", Ui.Px(4)); footerMargin.AddChild(footer); _immersionControls.AddChild(footerMargin);
        _stockPotential = Ui.Text("", 13.5f, Ui.InkMuted); _stockPotential.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; footer.AddChild(_stockPotential);
        var stockCaption = Ui.Caps("Stock", Ui.InkMuted); stockCaption.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; footer.AddChild(stockCaption);
        _stockTotal = Ui.Heading("", 20); footer.AddChild(_stockTotal);
        RefreshImmersionControls();
    }

    private readonly (LineEdit Amount, Label Total)[] _stockRows = new (LineEdit, Label)[3];
    private Button? _stockClear;
    private Label? _stockPotential;
    private Label? _stockTotal;

    private static PanelContainer StockLine(Control item, Control prices, Control quantity, Control cost, bool header)
    {
        var line = new PanelContainer { CustomMinimumSize = new Vector2(0, header ? 0 : Ui.S(52)) };
        var rule = Ui.Box(new Color(0, 0, 0, 0), 0, padX: 4, padY: header ? 0 : 0);
        rule.BorderColor = header ? Ui.Ink : Ui.PaperRule; rule.BorderWidthBottom = header ? Ui.Px(1.5f) : 1;
        if (header) rule.ContentMarginBottom = Ui.S(6);
        line.AddThemeStyleboxOverride("panel", rule);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Ui.Px(14)); line.AddChild(row);
        item.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        prices.CustomMinimumSize = new Vector2(Ui.S(120), 0);
        quantity.CustomMinimumSize = new Vector2(Ui.S(150), 0);
        if (quantity is HBoxContainer stepper) stepper.Alignment = BoxContainer.AlignmentMode.Center;
        if (quantity is Label quantityLabel) quantityLabel.HorizontalAlignment = HorizontalAlignment.Center;
        cost.CustomMinimumSize = new Vector2(Ui.S(60), 0);
        if (cost is Label costLabel) costLabel.HorizontalAlignment = HorizontalAlignment.Right;
        foreach (var control in new[] { item, prices, quantity, cost })
        {
            control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            if (control is Label label) label.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(control);
        }
        return line;
    }

    private int PlannedStock(int slot) => _session.CapturePreparationPlan() is { } plan ? slot switch { 0 => plan.Chips, 1 => plan.SoftDrinks, _ => plan.Beers } : 0;

    private void CommitPlannedStock(int slot, int value)
    {
        if (_session.CapturePreparationPlan() is not { } plan || _session.PreparedStatus != PreparationStatus.Preparing) return;
        var amounts = new[] { plan.Chips, plan.SoftDrinks, plan.Beers };
        value = Math.Clamp(value, 0, 10000);
        if (amounts[slot] == value) { RefreshImmersionControls(); return; }
        amounts[slot] = value;
        CommitEquipmentAction(new SetPreparationStockCommand(amounts[0], amounts[1], amounts[2]));
    }

    private void BuildStaffPage(VBoxContainer page)
    {
        page.AddChild(Ui.PageHeading("Festival staff", "The licence needs a sound engineer, a medic and a steward. Everyone is paid at Start."));
        _staffGrids.Clear();
        foreach (var role in new[] { StaffRole.Sound, StaffRole.Medic, StaffRole.Steward })
        {
            page.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(4)) });
            var name = StaffCatalogue.RoleName(role);
            page.AddChild(Ui.Section(char.ToUpperInvariant(name[0]) + name[1..], "Required · choose one"));
            var grid = new GridContainer { Columns = 3 };
            grid.AddThemeConstantOverride("h_separation", Ui.Px(12)); grid.AddThemeConstantOverride("v_separation", Ui.Px(12));
            page.AddChild(grid); _staffGrids[role] = grid;
        }
        page.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(4)) });
        page.AddChild(Ui.Section("Extra hands", "Needs a perk slot"));
        _extraStaffNote = Ui.Text("", 13, Ui.InkMuted); _extraStaffNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        page.AddChild(_extraStaffNote);
        _extraStaffList = new VBoxContainer(); _extraStaffList.AddThemeConstantOverride("separation", 0); page.AddChild(_extraStaffList);
        page.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(4)) });
        page.AddChild(Ui.Section("Site crew · optional"));
        _crewList = new VBoxContainer(); _crewList.AddThemeConstantOverride("separation", 0); page.AddChild(_crewList);
    }

    private void BuildSuppliesPage(VBoxContainer page)
    {
        page.AddChild(Ui.PageHeading("Supplies", "Optional. Stock sells from the food van and bar; free water is always available."));
        page.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(2)) });
        BuildImmersionControls(page);
        page.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(4)) });
        page.AddChild(Ui.Section("Sound rig"));
        _rigChoices = new GridContainer { Columns = 2 };
        _rigChoices.AddThemeConstantOverride("h_separation", Ui.Px(12)); page.AddChild(_rigChoices);
        var generator = new HBoxContainer(); generator.AddThemeConstantOverride("separation", Ui.Px(12)); page.AddChild(generator);
        var name = new HBoxContainer(); name.AddThemeConstantOverride("separation", Ui.Px(6)); generator.AddChild(name);
        var bolt = Ui.IconRect("zap", 16, Ui.Ink); bolt.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; name.AddChild(bolt);
        name.AddChild(Ui.Text("Generator", 13.5f, Ui.Ink, Ui.BodyBold));
        _generatorBar = new ProgressBar { ShowPercentage = false, MaxValue = 100, Value = 80, CustomMinimumSize = new Vector2(0, Ui.S(8)),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        _generatorBar.AddThemeStyleboxOverride("background", Ui.Box(Ui.PaperRule, 4));
        _generatorBar.AddThemeStyleboxOverride("fill", Ui.Box(Ui.Teal, 4));
        generator.AddChild(_generatorBar);
        _generatorText = Ui.Text("80% baseline · safe", 13.5f, Ui.InkMuted); generator.AddChild(_generatorText);
    }

    private void BuildImmersionVendorInspector(VBoxContainer parent)
    {
        _immersionMoveButton = ButtonText("Move", () =>
        {
            if (_selectedImmersionVendor is { } id) BeginImmersionPlacement(id);
        });
        _immersionMoveButton.Visible = false;
        _immersionMoveButton.TooltipText = "Choose a new grass site before opening. Comma/period rotate; right-click or Esc cancels.";
        parent.AddChild(_immersionMoveButton);
        _freeWaterButton = ButtonText("Free water at the bar", () =>
        {
            _preparationMessage = _host.Execute(new SetFreeWaterCommand(!_session.FreeWaterOn), out var error)
                ? _session.FreeWaterOn ? "The bar is handing out free water." : "Free water stopped; anyone already queuing still gets a cup." : error!;
            RefreshPreparationHud();
        });
        _freeWaterButton.Visible = false;
        parent.AddChild(_freeWaterButton);
        BuildToiletInspector(parent);
        BuildLitterInspector(parent);
    }

    private int ImmersionHeavyOnSiteCount()
    {
        if (_session.CaptureImmersion() is not { } state) return 0;
        var onSite = (_session.CapturePreparation()?.People ?? []).Where(p => p.Admitted && !p.Departed).Select(p => p.AgentId).ToHashSet();
        return state.People.Count(p => p.Intoxication >= 7500 && onSite.Contains(p.AgentId));
    }

    private void RefreshImmersionControls()
    {
        if (_immersionControls is null) return;
        var state = _session.CaptureImmersion(); _immersionControls.Visible = state is not null;
        if (state is null) return;
        var preparing = _session.PreparedStatus == PreparationStatus.Preparing;
        _immersionStockButton!.Visible = preparing;
        _immersionStockButton.Disabled = _session.ValidateCommand(CampaignEnvelope(new SetPreparationStockCommand(40, 40, 32))) is not null;
        _immersionStockButton.Text = state.StockPurchased ? "Starter stock purchased · £96" : "Buy starter stock · £96";
        if (_session.CapturePreparationPlan() is { } plan)
        {
            _immersionStockButton.Text = "Starter bundle · 40 / 40 / 32";
            _immersionStockButton.TooltipText = "Plan 40 chips, 40 soft drinks and 32 beers. Unpaid until Start; revise quantities or remove freely.";
            var values = new[] { plan.Chips, plan.SoftDrinks, plan.Beers };
            var costs = new[] { 100, 60, 100 }; var sales = new[] { 300, 200, 300 };
            for (var i = 0; i < 3; i++)
            {
                if (!_stockRows[i].Amount.HasFocus()) _stockRows[i].Amount.Text = values[i].ToString();
                _stockRows[i].Amount.Editable = preparing;
                _stockRows[i].Total.Text = FestivalCurrency.Format(values[i] * costs[i]);
            }
            _stockClear!.Disabled = !preparing || values.Sum() == 0;
            _stockPotential!.Text = $"Sells for up to {FestivalCurrency.Format(values.Select((value, i) => (long)value * sales[i]).Sum())} if every item goes. Staff don't buy beer.";
            _stockTotal!.Text = FestivalCurrency.Format(values.Select((value, i) => (long)value * costs[i]).Sum());
        }
        _immersionSummary!.Text = $"Chips £3 • soft £2 • beer £3\nStock {state.ChipsStock}/{state.SoftStock}/{state.BeerStock} • sales {state.Purchases.Length}\n" +
            "Free water remains available. Personal spending budgets vary; staff do not buy beer.\n" +
            string.Join("\n", _session.CaptureVendors().Select(v => $"{(v.Id == "food" ? "Food van" : "Drinks stall")}: queue {v.Queue.Length} • {(v.OwnerId is null ? "ready" : "serving")}")) +
            (_session.CaptureToilet() is { } toilet ? $"\nPortaloo: {toilet.FullPercent}% full • {(toilet.InterruptedOccupantId is not null ? "unavailable" : toilet.IsFull ? "full" : toilet.OwnerId is null ? "free" : "occupied")}" : "");
        if (_session.PreparedStatus == PreparationStatus.Departing) _immersionSummary.Text += "\nCOUNTERS CLOSED • on-site alcohol risk and medic response continue until physical exit.";
        if (ImmersionHeavyOnSiteCount() > 0) _immersionSummary.Text = "! HEAVY INTOXICATION • select affected people for medic care\n" + _immersionSummary.Text;
        SyncImmersionWorld();
        SyncToiletWorld();
        RefreshImmersionVendorInspector();
    }

    private void SyncImmersionWorld()
    {
        var state = _session.CaptureImmersion();
        if (state is null)
        {
            foreach (var visual in _immersionVendors.Values) { visual.Visible = false; visual.QueueFree(); }
            _immersionVendors.Clear(); _immersionVendorPicks.Clear(); ResetImmersionHeldVisuals();
            return;
        }
        foreach (var stale in _immersionVendors.Keys.Except(_session.CaptureVendors().Select(vendor => vendor.Id)).ToArray())
        {
            var body = _immersionVendors[stale];
            _immersionVendorPicks.Remove(body.GetInstanceId());
            body.QueueFree();
            _immersionVendors.Remove(stale);
        }
        foreach (var vendor in _session.CaptureVendors())
        {
            if (!_immersionVendors.TryGetValue(vendor.Id, out var body))
            {
                body = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
                body.AddChild(InstantiateImmersionVendor(vendor.Id == "food"));
                var size = vendor.Id == "food" ? new Vector3(6, 2.8f, 3) : new Vector3(3.5f, 3.1f, 2.5f);
                body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = new Vector3(vendor.Id == "food" ? -.5f : 0, size.Y / 2, 0) });
                var name=BuildingName(vendor.Id=="food"?"FOOD":"BAR",new Vector3(0,3.4f,0));name.Name="VendorCategoryLabel";body.AddChild(name);
                AddChild(body); _immersionVendors.Add(vendor.Id, body); _immersionVendorPicks.Add(body.GetInstanceId(), vendor.Id);
            }
            body.Position = ImmersionPosition(vendor.Cell); body.RotationDegrees = new Vector3(0, 90 * vendor.QuarterTurns, 0);
        }
    }

    private void BeginImmersionPlacement(string id)
    {
        BeginBuildPlacement(id == "food" ? BuildServiceKind.FoodVan : BuildServiceKind.Bar, id);
    }
    private void SelectImmersionVendor(string id)
    {
        ClearSelection(); _selectedImmersionVendor = id; RefreshImmersionVendorInspector();
    }
    private void RefreshImmersionVendorInspector()
    {
        RefreshContextPanelVisibility();
        if (_immersionMoveButton is not null) _immersionMoveButton.Text = true ? "Move" : "Cancel move";
        if (_immersionMoveButton is not null)
            _immersionMoveButton.Visible = _selectedImmersionVendor is not null &&
                _session.PreparedStatus == PreparationStatus.Preparing && _session.CaptureImmersion() is not null;
        if (_freeWaterButton is not null)
        {
            _freeWaterButton.Visible = _selectedImmersionVendor == "drinks" && _session.PreparedStatus == PreparationStatus.Running;
            _freeWaterButton.Text = _session.FreeWaterOn ? "Stop free water" : $"Free water at the bar · {FestivalCurrency.Format(GameSession.FreeWaterChargePennies)}";
            _freeWaterButton.Disabled = _session.ValidateCommand(CampaignEnvelope(new SetFreeWaterCommand(!_session.FreeWaterOn))) is not null;
            _freeWaterButton.TooltipText = _session.FreeWaterOn
                ? "Stop handing out water. No refund; switching it back on costs again."
                : "Hand out free cups of water from the bar for the rest of the day. Thirsty guests will use it, especially if the taps are broken or busy, and the bar queue will grow.";
        }
        if (_selectedImmersionVendor is not { } id || _session.CaptureImmersion() is not { } state || !_immersionVendors.TryGetValue(id, out var body)) return;
        var vendor = _session.CaptureVendors().Single(v => v.Id == id);
        _inspectorTitle.Text = id == "food" ? "Food van • chips" : "Drinks stall • soft drinks & beer";
        _inspectorBody.Text = $"Queue: {vendor.Queue.Length}\n" +
            (vendor.OwnerId is { } owner ? $"Serving {_session.CapturePreparation()!.People.Single(p => p.AgentId == owner).Name} • {vendor.ServiceTicks / 80m:0.0}s remaining\n" : "Counter ready\n") +
            (id == "food" ? $"Chips {FestivalCurrency.Format(_session.ImmersionListPrice(ImmersionProduct.Chips))} • stock {state.ChipsStock}" : $"Soft {FestivalCurrency.Format(_session.ImmersionListPrice(ImmersionProduct.SoftDrink))} • stock {state.SoftStock}\nBeer {FestivalCurrency.Format(_session.ImmersionListPrice(ImmersionProduct.Beer))} • stock {state.BeerStock}\nNo beer for staff or heavily intoxicated customers.") +
            "\nStaff & band: half price." + (id == "drinks" && state.FreeWater ? "\nFREE WATER • cups for the thirsty" : "");
        _highlight.Position = body.Position + new Vector3(0, .08f, 0); _highlight.Scale = new Vector3(id == "food" ? 3.5f : 2, 1, id == "food" ? 3.5f : 2); _highlight.Visible = true;
    }
    private string ImmersionPersonInspectorText(ulong id)
    {
        RefreshImmersionNeedBars(id);
        if (_session.CaptureImmersion()?.People.SingleOrDefault(p => p.AgentId == id) is not { } person) return "";
        var wallet = _session.CaptureSnapshot().Wallets.Single(w => w.OwnerId.Value == id).CashPennies;
        return $"\nFOOD & DRINK • adult\nBudget {FestivalCurrency.Format(wallet)} remaining / {FestivalCurrency.Format(person.OpeningBudgetPennies)} opening\n" +
            $"Hunger {person.Hunger / 100m:0}% • toilet need {person.ToiletNeed / 100m:0}% • intoxication {person.Intoxication / 100m:0}%\n" +
            (person.ToiletStage != ToiletVisitStage.None ? $"Toilet: {person.ToiletStage.ToString().ToLowerInvariant()}\n" : "") +
            (_session.ToiletSmellPenaltyPerSecond(id) > 0 ? "Nearby toilet smell is gradually reducing satisfaction.\n" : "") +
            (_session.Teetotal(id) ? "Abstains from beer\n" : "Individual food/drink preferences\n") +
            (person.Held is { } held ? $"Holding {ImmersionProductName(held.Product)} • {held.ConsumedTicks / 80m:0.0}/{GameSession.ImmersionConsumeTicks(held.Product) / 80}s consumed • {(!_session.IsPaused && _session.ImmersionConsumptionEligible(id) ? "consuming away from counter" : "retained; consumption paused")}\n" : _session.CaptureCarriedWaste(id) is not null ? "Carrying empty packaging for disposal\n" : "Hands empty\n") +
            (person.PendingDose > 0 ? "Previously ingested dose still absorbing\n" : "") +
            (person.Intoxication >= 7500 ? "HEAVY INTOXICATION • needs care; no further beer\n" : person.Intoxication >= 5000 ? "IMPAIRED • coordination reduced\n" : person.Intoxication >= 2500 ? "TIPSY\n" : "") +
            (person.Intoxication >= 8500 ? $"Continuously high exposure {person.SevereTicks / 80m:0.0}/20s • collapse risk\n" : "") +
            "Water addresses thirst, not sobriety; food slows uptake; time permits recovery.\n";
    }
    private void AdvanceImmersionPresentation(double delta)
    {
        if (_session.CaptureImmersion() is not { } state)
        {
            ResetStewardCleanupPresentation();
            ResetImmersionHeldVisuals();
            foreach (var root in _attendeeVisuals.Values)
                Bodies.SetPose(root, "relaxed", null);
            SyncLitterWorld();
            return;
        }
        foreach (var person in state.People)
            if (_attendeeVisuals.TryGetValue(new EntityId(person.AgentId), out var body))
            {
                var waste = _session.CaptureCarriedWaste(person.AgentId);
                var hands = waste is null ? _session.ImmersionHandsAvailable(person.AgentId) : _session.WasteCarryEligible(person.AgentId);
                var shown = person.Held ?? (waste is null ? null : new ImmersionHeldItem(waste.Id, waste.Product, 0));
                Bodies.SetPose(body, AttendeePose.State(shown, hands, waste is null && _session.ImmersionConsumptionEligible(person.AgentId)), shown?.Product);
                SetImmersionHeldVisual(new(person.AgentId), body, shown is { } held ? ImmersionProductKey(held.Product) : null, hands, person.Intoxication, delta, waste is not null);
                if (hands && person.Intoxication >= 5000)
                    body.Rotation = new Vector3(body.Rotation.X, body.Rotation.Y, Mathf.Sin((float)_characterPresentationSeconds / .35f + person.AgentId) * .035f);
                else if (Mathf.Abs(body.Rotation.X) < .1f) body.Rotation = new Vector3(body.Rotation.X, body.Rotation.Y, 0);
            }
        RefreshImmersionVendorInspector();
        RefreshToiletInspector();
        AdvanceStewardCleanupPresentation();
        SyncLitterWorld();
    }
}

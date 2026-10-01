using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

/// <summary>The end-of-festival paper: the public newspaper review and the festival accounts.</summary>
internal sealed class ResultsPaper(IHudHost _hud)
{
    private CanvasLayer? _layer;
    private ScrollContainer? _scroll;
    private VBoxContainer? _body;
    private Button? _newspaperTab;
    private Button? _accountsTab;
    private bool _showingAccounts;
    private int _newspaperScroll;
    private int _accountsScroll;

    public bool IsOpen => _layer is not null;

    public void Close() { _layer?.QueueFree(); _layer = null; }

    /// <summary>Opens on the newspaper. <paramref name="returnToMenu"/> reports whether leaving succeeded.</summary>
    public void Open(Node parent, Func<bool> returnToMenu)
    {
        _showingAccounts = false; _newspaperScroll = _accountsScroll = 0;
        _layer = new CanvasLayer { Layer = 19 }; parent.AddChild(_layer);
        var shade = new ColorRect { Color = new Color("172d2b") };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect); _layer.AddChild(shade);
        var center = new CenterContainer(); center.SetAnchorsPreset(Control.LayoutPreset.FullRect); shade.AddChild(center);
        var viewport = _hud.Viewport.GetVisibleRect().Size;
        var width = Math.Min(1060, viewport.X - 40);
        var height = Math.Min(850, viewport.Y - 40);
        var paper = new PanelContainer { CustomMinimumSize = new Vector2(width, height) };
        paper.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("fff3d3") }); center.AddChild(paper);
        var margins = HearingMargins(viewport.X < 900 ? 18 : 30, 18); paper.AddChild(margins);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 10); margins.AddChild(column);
        var nav = new HBoxContainer(); nav.AddThemeConstantOverride("separation", 10); column.AddChild(nav);
        nav.AddChild(ResultText("Festival results", viewport.X < 900 ? 20 : 25, true));
        _newspaperTab = ButtonText("Newspaper", () => Show(false));
        _newspaperTab.Name = "NewspaperTab"; _newspaperTab.TooltipText = "Public festival review";
        _newspaperTab.CustomMinimumSize = new Vector2(125, 44); nav.AddChild(_newspaperTab);
        _accountsTab = ButtonText("Accounts", () => Show(true));
        _accountsTab.Name = "AccountsTab"; _accountsTab.TooltipText = "Income, expenditure and cash";
        _accountsTab.CustomMinimumSize = new Vector2(125, 44); nav.AddChild(_accountsTab);
        Button? menu = null;
        menu = ButtonText("Return to menu", () =>
        {
            if (returnToMenu()) return;
            menu!.Text = "Save failed · retry menu";
            menu.TooltipText = _hud.Message;
        });
        menu.Name = "ReturnToMenu"; menu.CustomMinimumSize = new Vector2(170, 44); nav.AddChild(menu);
        column.AddChild(HearingRule());
        _scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FocusMode = Control.FocusModeEnum.All };
        column.AddChild(_scroll);
        _body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 12); _scroll.AddChild(_body);
        Show(false, first: true);
    }

    private Label ResultText(string value, int size = 17, bool serif = false)
    {
        var label = new Label { Text = value, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        label.AddThemeColorOverride("font_color", new Color("293b38"));
        label.AddThemeFontSizeOverride("font_size", size);
        if (serif) label.AddThemeFontOverride("font", Ui.SlabBold);
        return label;
    }

    public void Show(bool accounts, bool first = false)
    {
        if (_body is null || _scroll is null) return;
        if (!first)
        {
            if (_showingAccounts) _accountsScroll = _scroll.ScrollVertical;
            else _newspaperScroll = _scroll.ScrollVertical;
        }
        _showingAccounts = accounts;
        foreach (var child in _body.GetChildren())
        {
            _body.RemoveChild(child); child.QueueFree();
        }
        if (accounts) RenderFestivalAccounts(_body);
        else RenderFestivalNewspaper(_body);
        _newspaperTab!.Disabled = !accounts;
        _accountsTab!.Disabled = accounts;
        var position = accounts ? _accountsScroll : _newspaperScroll;
        _scroll.ScrollVertical = position;
        _scroll.SetDeferred("scroll_vertical", position);
    }

    private void RenderFestivalNewspaper(VBoxContainer body)
    {
        var result = _hud.Session.CompletedFestivalResult!;
        body.AddChild(ResultText("LOCAL EDITION · FESTIVAL REVIEW", 14)); body.AddChild(HearingRule());
        body.AddChild(ResultText("The Lower Wittering Gazette", 39, true)); body.AddChild(HearingRule());
        var headline = result.Stars switch { 5 => "A day to remember", 4 => "Festival hits the right note",
            3 => "A mixed reception in the field", 2 => "Festival leaves room for improvement",
            1 => "A difficult day in the field", _ => "The field falls quiet" };
        body.AddChild(ResultText(headline, 30, true));
        body.AddChild(ResultText(result.Stars is { } stars
            ? $"{new string('★', stars)}{new string('☆', 5 - stars)}    {stars} / 5 · Guest satisfaction {result.SatisfactionPercent:0.0}%"
            : "Unrated · No admitted guests", 23));
        body.AddChild(ResultText($"The rating reflects the final satisfaction of {result.GuestCount} admitted guests, including early leavers.", 17));
        body.AddChild(HearingRule());
        body.AddChild(ResultText("The crowd's verdict", 25, true));
        body.AddChild(ResultText("Every admitted guest has physically left the farm. Their final satisfaction shapes the public review.", 18));
        body.AddChild(HearingRule());
        body.AddChild(ResultText("Around the field", 25, true));
        body.AddChild(ResultText($"Beers finished: {result.BeersFinished?.ToString() ?? "Not recorded"}     " +
            $"Fights: {result.Fights?.ToString() ?? "Not recorded"}     " +
            $"Medical collapses: {result.MedicalCollapses?.ToString() ?? "Not recorded"}", 19));
        body.AddChild(ResultText("Beers finished counts completed guest servings. Fights include encounters involving a guest; medical collapses count guest rescue deadlines. Fight knockouts are excluded.", 15));
        body.AddChild(HearingRule());
        body.AddChild(ResultText("Rating guide: below 20 / 40 / 60 / 80% earns 1 / 2 / 3 / 4 stars; 80% or above earns 5 stars. Provisional balancing thresholds.", 14));
    }

    private void ResultsMoneyRow(VBoxContainer parent, string label, string quantity, string amount, bool total = false)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12);
        row.CustomMinimumSize = new Vector2(0, total ? 32 : 28); parent.AddChild(row);
        var name = ResultText(label, total ? 18 : 16, total);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(name);
        if (quantity.Length > 0)
        {
            var count = ResultText(quantity, total ? 18 : 16);
            count.HorizontalAlignment = HorizontalAlignment.Right;
            count.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
            count.CustomMinimumSize = new Vector2(55, 0); row.AddChild(count);
        }
        var money = ResultText(amount, total ? 18 : 16, total);
        money.HorizontalAlignment = HorizontalAlignment.Right;
        money.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        money.CustomMinimumSize = new Vector2(115, 0); row.AddChild(money);
    }

    private void ResultsSection(VBoxContainer body, string title)
    {
        body.AddChild(HearingRule()); body.AddChild(ResultText(title, 24, true));
    }

    private void RenderFestivalAccounts(VBoxContainer body)
    {
        var report = _hud.Session.CompletedFestivalAccounts!;
        body.AddChild(ResultText("FESTIVAL ACCOUNTS", 36, true));
        body.AddChild(ResultText("Lower Wittering · Income and expenditure", 20, true));
        body.AddChild(ResultText("Actual recorded transactions for this festival attempt", 14));
        var wide = _hud.Viewport.GetVisibleRect().Size.X >= 1100;
        var columns = new HBoxContainer(); columns.AddThemeConstantOverride("separation", 28); body.AddChild(columns);
        var income = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var spending = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        if (wide) { columns.AddChild(income); columns.AddChild(spending); }
        else { columns.AddChild(income); body.AddChild(spending); }

        ResultsSection(income, "Income");
        ResultsMoneyRow(income, "Item / recorded rate", "Qty", "Sales", true);
        foreach (var sale in report.Sales)
        {
            var item = sale.Product switch { ImmersionProduct.Chips => "Chips", ImmersionProduct.SoftDrink => "Soft drink", _ => "Beer" };
            var rate = sale.UnitPricePennies == GameSession.ImmersionPrice(sale.Product) ? "full price" : "50% rate";
            ResultsMoneyRow(income, $"{item} · {rate} {FestivalCurrency.Format(sale.UnitPricePennies)}",
                sale.Quantity.ToString(), FestivalCurrency.Format(sale.AmountPennies));
        }
        if (report.Sales.Length == 0) income.AddChild(ResultText("No sales recorded", 16));
        income.AddChild(HearingRule());
        ResultsMoneyRow(income, "Total income", report.Sales.Sum(sale => sale.Quantity).ToString(),
            FestivalCurrency.Format(report.IncomePennies), true);
        income.AddChild(ResultText("50% applies to staff and performers; discounted beer is performer-only.", 14));

        ResultsSection(income, "Cost of items sold");
        foreach (var cost in report.SoldItemCosts)
            ResultsMoneyRow(income, $"{cost.Label} · {FestivalCurrency.Format(cost.UnitCostPennies)} each",
                cost.Quantity.ToString(), FestivalCurrency.Format(cost.AmountPennies));
        if (report.SoldItemCosts.Length == 0) income.AddChild(ResultText("No consumed stock recorded", 16));
        income.AddChild(HearingRule());
        ResultsMoneyRow(income, "Total cost of items sold", "", FestivalCurrency.Format(report.SoldItemCostPennies), true);

        ResultsSection(income, "Stock purchased · cash only");
        if (!report.StockPurchaseRecorded) income.AddChild(ResultText("Not recorded for this save", 16));
        else if (report.StockDetailRecorded)
            foreach (var stock in report.PurchasedStock)
                ResultsMoneyRow(income, $"{stock.Label} · {FestivalCurrency.Format(stock.UnitCostPennies)} each",
                    stock.Quantity.ToString(), FestivalCurrency.Format(stock.AmountPennies));
        else ResultsMoneyRow(income, "Recorded stock purchase", "", FestivalCurrency.Format(report.StockPurchasesPennies));
        income.AddChild(ResultText("Purchased stock is a cash outflow. Its cost enters operating expenditure only when consumed.", 14));

        ResultsSection(spending, "Operating expenditure");
        foreach (var group in report.OperatingExpenses.GroupBy(line => line.Category)
            .OrderBy(group => group.Key == "Act bookings" ? 0 : group.Key == "Staff and rental" ? 1 : 2))
        {
            spending.AddChild(ResultText(group.Key.ToUpperInvariant(), 16, true));
            foreach (var expense in group)
                ResultsMoneyRow(spending, expense.Label, "", FestivalCurrency.Format(expense.AmountPennies));
        }
        if (report.OperatingExpenses.Length == 0) spending.AddChild(ResultText("No operating payments recorded", 16));
        spending.AddChild(HearingRule());
        ResultsMoneyRow(spending, "Total operating expenditure", "", FestivalCurrency.Format(report.OperatingExpensesPennies), true);
        if (!report.FacilityDetailRecorded)
            spending.AddChild(ResultText("Facility item detail is not recorded for this save; the paid total is shown.", 14));

        ResultsSection(body, report.OperatingResultPennies < 0 ? "Operating loss" : "Operating profit");
        ResultsMoneyRow(body, "Income − items sold − operating expenditure", "",
            FestivalCurrency.Format(report.OperatingResultPennies), true);
        ResultsSection(body, "Cash reconciliation");
        ResultsMoneyRow(body, "Opening cash · not sales income", "", FestivalCurrency.Format(report.OpeningCashPennies));
        ResultsMoneyRow(body, "Sales receipts", "", FestivalCurrency.Format(report.IncomePennies));
        ResultsMoneyRow(body, "Operating payments", "", FestivalCurrency.Format(-report.OperatingExpensesPennies));
        ResultsMoneyRow(body, "Stock purchases", "", report.StockPurchaseRecorded
            ? FestivalCurrency.Format(-report.StockPurchasesPennies) : "Not recorded");
        ResultsMoneyRow(body, "Capital equipment purchases", "", FestivalCurrency.Format(-report.CapitalPurchasesPennies));
        body.AddChild(HearingRule());
        ResultsMoneyRow(body, "Closing cash", "", FestivalCurrency.Format(report.ClosingCashPennies), true);
        ResultsMoneyRow(body, "Net cash change", "", FestivalCurrency.Format(report.NetCashChangePennies), true);
        body.AddChild(ResultText(report.Reconciles ? "Recorded income, expenditure and cash reconcile."
            : "Full cash reconciliation is unavailable for this save; no missing movement has been assumed.", 15));
    }
}

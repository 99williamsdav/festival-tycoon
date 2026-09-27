using Festival.Simulation;
using Godot;
using System;

namespace Festival.Game;

public partial class Main
{
    private CanvasLayer? _festivalPaper;
    private ScrollContainer? _resultsScroll;
    private void RefreshFestivalPaper()
    {
        var result = _session.CompletedFestivalResult;
        if (result is null)
        {
            _festivalPaper?.QueueFree(); _festivalPaper = null;
            if (_session.PreparedStatus == PreparationStatus.Departing)
                _preparationSummary.Text += $"\nFestival finished · Guests leaving: {_session.CapturePreparation()!.People.Count(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed)}";
            return;
        }
        if (_festivalPaper is not null) return;
        _festivalPaper = new CanvasLayer { Layer = 19 }; AddChild(_festivalPaper);
        var shade = new ColorRect { Color = new Color("293b38") }; shade.SetAnchorsPreset(Control.LayoutPreset.FullRect); _festivalPaper.AddChild(shade);
        var center = new CenterContainer(); center.SetAnchorsPreset(Control.LayoutPreset.FullRect); shade.AddChild(center);
        var width = Math.Min(1000, GetViewport().GetVisibleRect().Size.X - 56);
        var paper = new PanelContainer { CustomMinimumSize = new Vector2(width, Math.Min(750, GetViewport().GetVisibleRect().Size.Y - 48)) };
        paper.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("fff3d3") }); center.AddChild(paper);
        var margins = HearingMargins(28, 20); paper.AddChild(margins);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 12); margins.AddChild(column);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _resultsScroll = scroll;
        column.AddChild(scroll); var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 12); scroll.AddChild(body);
        Label Text(string value, int size = 16, bool serif = false)
        {
            var label = new Label { Text = value, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            label.AddThemeColorOverride("font_color", new Color("293b38")); label.AddThemeFontSizeOverride("font_size", size);
            if (serif) label.AddThemeFontOverride("font", HearingSerif()); return label;
        }
        body.AddChild(Text("LOCAL EDITION · FESTIVAL REVIEW", 13)); body.AddChild(HearingRule());
        body.AddChild(Text("The Lower Wittering Gazette", width < 800 ? 32 : 42, true)); body.AddChild(HearingRule());
        var headline = result.Stars switch { 5 => "A day to remember", 4 => "Festival hits the right note", 3 => "A mixed reception in the field", 2 => "Festival leaves room for improvement", 1 => "A difficult day in the field", _ => "The field falls quiet" };
        body.AddChild(Text(headline, 30, true));
        body.AddChild(Text(result.Stars is { } stars ? $"{new string('★', stars)}{new string('☆', 5 - stars)}    {stars} / 5 · Guest satisfaction {result.SatisfactionPercent:0.0}%" : "Unrated · No admitted guests", 23));
        body.AddChild(Text($"Mean final overall satisfaction of {result.GuestCount} admitted, departed guests, including early leavers.\nStars: below 20 / 40 / 60 / 80%; 5 stars at 80% or above. Provisional balancing thresholds.", 13));
        body.AddChild(HearingRule());
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 32); body.AddChild(row);
        var verdict = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1 };
        var accounts = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1 }; row.AddChild(verdict); row.AddChild(accounts);
        verdict.AddChild(Text("The crowd's verdict", 25, true));
        verdict.AddChild(Text("Every admitted guest has physically left the farm. The rating records their final satisfaction; the accounts tell a separate story."));
        accounts.AddChild(Text("Festival accounts", 25, true));
        accounts.AddChild(Text($"Operating {(result.ProfitPennies < 0 ? "loss" : "profit")} {FestivalCurrency.Format(result.ProfitPennies)}", 23));
        accounts.AddChild(Text($"Sales revenue {FestivalCurrency.Format(result.RevenuePennies)}\nWeekend contracts {FestivalCurrency.Format(result.ContractCostsPennies)}\nConsumed stock costs {FestivalCurrency.Format(result.ConsumedStockCostsPennies)}\nCapital purchases {FestivalCurrency.Format(result.CapitalPurchasesPennies)}\nNet festival cash change {FestivalCurrency.Format(result.NetCashChangePennies)}"));
        accounts.AddChild(Text("Operating result excludes capital purchases, unused stock, opening funds and loans. Cash change includes stock and capital purchases.", 13));
        body.AddChild(HearingRule()); body.AddChild(Text("Around the field", 25, true));
        body.AddChild(Text($"Beers finished: {result.BeersFinished?.ToString() ?? "Not recorded"}     Fights: {result.Fights?.ToString() ?? "Not recorded"}     Medical collapses: {result.MedicalCollapses?.ToString() ?? "Not recorded"}", 19));
        body.AddChild(Text("Admitted guests: completed beer servings consumed, encounters involving at least one guest, and medical collapse transitions with a rescue deadline. Fight knockouts excluded.", 13));
        column.AddChild(HearingRule());
        var footer = new HBoxContainer(); column.AddChild(footer);
        footer.AddChild(Text("Demo complete\nAll guests have left. Thanks for playing.", 18));
        var menu = ButtonText("Return to menu", () => { _festivalPaper?.QueueFree(); _festivalPaper = null; BuildStartSplash(); });
        menu.CustomMinimumSize = new Vector2(185, 48); footer.AddChild(menu);
    }
}

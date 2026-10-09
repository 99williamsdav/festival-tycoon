global using static Festival.Game.HudKit;
using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

/// <summary>Shared look and small builders for the HUD: paper panels, labels, buttons and copy.</summary>
internal static class HudKit
{
    internal static readonly Color HudInk = Ui.Ink;
    internal static readonly Color HudPaper = Ui.Paper;
    internal static StyleBoxFlat HudStyle(Color color, int margin = 12)
    {
        var box = new StyleBoxFlat
        {
            BgColor = color, BorderColor = new Color(0, 0, 0, .22f), AntiAliasing = true,
            ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin, ContentMarginBottom = margin,
            ShadowColor = new Color(0, 0, 0, .3f), ShadowSize = 10, ShadowOffset = new Vector2(0, 5),
        };
        box.SetBorderWidthAll(1); box.SetCornerRadiusAll(Ui.Px(6));
        return box;
    }
    private static StyleBoxFlat ControlStyle(Color color, Color border)
    {
        var box = new StyleBoxFlat { BgColor = color, BorderColor = border, AntiAliasing = true,
            ContentMarginLeft = 9, ContentMarginRight = 9, ContentMarginTop = 6, ContentMarginBottom = 6 };
        box.SetBorderWidthAll(1); box.SetCornerRadiusAll(Ui.Px(6));
        return box;
    }
    internal static Theme HudTheme()
    {
        var theme = new Theme { DefaultFontSize = 14, DefaultFont = Ui.Body };
        foreach (var type in new[] { "Button", "OptionButton" })
        {
            theme.SetStylebox("normal", type, ControlStyle(Ui.GoldWash, Ui.PaperEdge));
            theme.SetStylebox("hover", type, ControlStyle(Ui.TealWash, Ui.TealLine));
            theme.SetStylebox("pressed", type, ControlStyle(Ui.PaperRule, Ui.PaperEdge));
            theme.SetStylebox("disabled", type, ControlStyle(new Color(0, 0, 0, 0), Ui.PaperRule));
            theme.SetStylebox("focus", type, ControlStyle(new Color(0, 0, 0, 0), Ui.Gold));
            theme.SetFont("font", type, Ui.BodySemi);
            theme.SetColor("font_color", type, HudInk);
            theme.SetColor("font_hover_color", type, HudInk);
            theme.SetColor("font_pressed_color", type, HudInk);
            theme.SetColor("font_focus_color", type, HudInk);
            theme.SetColor("font_disabled_color", type, Ui.InkMuted);
        }
        theme.SetStylebox("tab_selected", "TabContainer", ControlStyle(Ui.Paper, Ui.PaperEdge));
        theme.SetStylebox("tab_unselected", "TabContainer", ControlStyle(Ui.PaperRule, Ui.PaperEdge));
        theme.SetStylebox("panel", "TabContainer", HudStyle(HudPaper, 14));
        theme.SetColor("font_selected_color", "TabContainer", HudInk);
        theme.SetColor("font_unselected_color", "TabContainer", HudInk);
        theme.SetColor("font_color", "Label", HudInk);
        // Tooltips are a Label variation, so they'd take the dark ink above onto Godot's dark tooltip panel: give them paper.
        theme.SetColor("font_color", "TooltipLabel", HudInk);
        theme.SetFont("font", "TooltipLabel", Ui.Body);
        theme.SetFontSize("font_size", "TooltipLabel", 14);
        var tip = new StyleBoxFlat { BgColor = HudPaper, BorderColor = Ui.PaperEdge, ShadowColor = new Color(0, 0, 0, .25f), ShadowSize = 4 };
        tip.SetBorderWidthAll(1); tip.SetCornerRadiusAll(4); tip.SetContentMarginAll(7);
        theme.SetStylebox("panel", "TooltipPanel", tip);
        theme.SetStylebox("background", "ProgressBar", new StyleBoxFlat { BgColor = new Color("d7cfb0") });
        theme.SetStylebox("fill", "ProgressBar", new StyleBoxFlat { BgColor = Colors.White });
        return theme;
    }
    internal static PanelContainer HudPanel(CanvasLayer layer, Vector2 position, Vector2 size, Color? color = null)
    {
        var panel = new PanelContainer { Position = position, Size = size, Theme = HudTheme() };
        panel.AddThemeStyleboxOverride("panel", HudStyle(color ?? HudPaper)); layer.AddChild(panel);
        return panel;
    }
    internal static Label HudLabel(string text, int size = 14)
    {
        var label = LabelText(text, size, HudInk);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }
    internal static Color CampaignPaletteColor(FestivalPalette palette) => palette switch
    {
        FestivalPalette.Meadow => new Color("dfe8c4"),
        FestivalPalette.Marigold => new Color("f1d28a"),
        FestivalPalette.Berry => new Color("dfb7c5"),
        FestivalPalette.River => new Color("bcd9db"),
        _ => new Color("f5e9c9"),
    };
    internal static StyleBoxFlat PaperStyle(Color color) => new()
    {
        BgColor = color, BorderColor = new Color("5f5a43"), BorderWidthLeft = 2, BorderWidthTop = 2,
        BorderWidthRight = 2, BorderWidthBottom = 2, CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5,
        CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5, ShadowColor = new Color(0, 0, 0, 0.24f), ShadowSize = 5,
    };
    internal static Label LabelText(string text, int size, Color color)
    {
        var label = new Label { Text = text }; label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", color); return label;
    }
    internal static Button ButtonText(string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(68, 38) };
        if (text.StartsWith("Collapse", StringComparison.OrdinalIgnoreCase))
        { button.Text = "×"; button.TooltipText = text; button.CustomMinimumSize = new Vector2(38,38); }
        button.MouseEntered += () => button.MouseDefaultCursorShape = button.Disabled ? Control.CursorShape.Arrow : Control.CursorShape.PointingHand;
        button.Pressed += action; return button;
    }
    // R0.04 saves retain their internal security identifiers and authored text.
    // Present both new and previously saved response prose as steward language.
    internal static string StewardWording(string value) => value
        .Replace("SECURITY", "STEWARD", StringComparison.Ordinal)
        .Replace("Security", "Steward", StringComparison.Ordinal)
        .Replace("security", "steward", StringComparison.Ordinal)
        .Replace("GUARD", "STEWARD", StringComparison.Ordinal)
        .Replace("Guard", "Steward", StringComparison.Ordinal)
        .Replace("guard", "steward", StringComparison.Ordinal);
    internal static string FestivalGenreName(int genre) => FestivalGenre.Name(genre);

    internal static MarginContainer HearingMargins(int horizontal, int vertical)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", horizontal);
        margin.AddThemeConstantOverride("margin_right", horizontal);
        margin.AddThemeConstantOverride("margin_top", vertical);
        margin.AddThemeConstantOverride("margin_bottom", vertical);
        return margin;
    }
    internal static ColorRect HearingRule() => new()
    {
        Color = new Color("8d8b73"), CustomMinimumSize = new Vector2(0, 1),
        MouseFilter = Control.MouseFilterEnum.Ignore
    };
    internal static Control HearingGap(float height) => new() { CustomMinimumSize = new Vector2(0, height) };
    internal static void HearingRecordRow(VBoxContainer parent, string key, Label value)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 14); parent.AddChild(row);
        var keyLabel = LabelText(key, 20, new Color("59645d"));
        keyLabel.CustomMinimumSize = new Vector2(112, 0); row.AddChild(keyLabel);
        value.AddThemeFontSizeOverride("font_size", 22);
        value.AddThemeColorOverride("font_color", new Color("2d3a37"));
        value.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        value.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(value);
    }
    internal static (Button Button, Label Detail) HearingChoice(Color background, Color foreground,
        string verb, string title, string detail, Action action)
    {
        var button = new Button { Text = "", CustomMinimumSize = new Vector2(0, 158),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var normal = PaperStyle(background); normal.BorderColor = new Color("39433c"); normal.ShadowSize = 0;
        var hover = PaperStyle(background.Lightened(0.08f)); hover.BorderColor = new Color("39433c"); hover.ShadowSize = 0;
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.Pressed += action;
        var margin = HearingMargins(20, 17); margin.MouseFilter = Control.MouseFilterEnum.Ignore;
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect); button.AddChild(margin);
        var words = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        words.AddThemeConstantOverride("separation", 5); margin.AddChild(words);
        var verbLabel = LabelText(verb, 17, foreground); verbLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        words.AddChild(verbLabel);
        var titleLabel = LabelText(title, 30, foreground); titleLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        words.AddChild(titleLabel);
        var detailLabel = LabelText(detail, 18, foreground);
        detailLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        detailLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart; words.AddChild(detailLabel);
        return (button, detailLabel);
    }

    /// <summary>Festival mode says "festival" where the older weekend wording said "weekend".</summary>
    internal static string FestivalWording(GameSession session, string text) => session.CaptureProgramme() is null ? text :
        text.Replace("weekend", "festival", StringComparison.Ordinal).Replace("Weekend", "Festival", StringComparison.Ordinal)
            .Replace("WEEKEND", "FESTIVAL", StringComparison.Ordinal);

    /// <summary>Elapsed festival time as mm:ss (80 ticks a second).</summary>
    internal static string FestivalClockText(long ticks) => $"{Math.Max(0, ticks) / 80 / 60:00}:{Math.Max(0, ticks) / 80 % 60:00}";

    internal static string BuildName(BuildServiceKind kind) => kind switch
    {
        BuildServiceKind.WaterTap => "Water tap",
        BuildServiceKind.Toilet => "Toilet",
        BuildServiceKind.FoodVan => "Food van",
        BuildServiceKind.Bar => "Bar",
        BuildServiceKind.FirstAid => "First aid",
        BuildServiceKind.StewardPost => "Steward post",
        BuildServiceKind.Bin => "Litter bin",
        BuildServiceKind.Marquee => "Marquee",
        _ => "Service"
    };
}

/// <summary>The unpaid preparation draft split the way the HUD reports it, in pennies.</summary>
internal sealed record PlanCosts(long Services, long Acts, long Staff, long Equipment, long Stock)
{
    public long Supplies => Equipment + Stock;

    public static PlanCosts Of(GameSession session)
    {
        var plan = session.CapturePreparationPlan();
        if (plan is null) return new(0, 0, 0, 0, 0);
        var offers = session.GetPreparationOffers().ToDictionary(offer => offer.Id);
        long Sum(Func<PreparationOffer, bool> include) => plan.OfferIds.Select(id => offers[id]).Where(include).Sum(offer => (long)offer.PricePennies);
        return new(session.BuildDraftCost,
            plan.ActIds.Where(id => id != "").Sum(id => (long)offers[id].PricePennies),
            Sum(offer => StaffCatalogue.IsWorkCategory(offer.Category)),
            Sum(offer => offer.Category == "equipment"),
            plan.SoftDrinks * GameSession.ImmersionCost(ImmersionProduct.SoftDrink) + plan.Beers * GameSession.ImmersionCost(ImmersionProduct.Beer));
    }
}

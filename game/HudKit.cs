global using static Festival.Game.HudKit;
using Festival.Simulation;
using Godot;
using System;

namespace Festival.Game;

/// <summary>Shared look and small builders for the HUD: paper panels, labels, buttons and copy.</summary>
internal static class HudKit
{
    internal static readonly Color HudInk = new("293b38");
    internal static readonly Color HudPaper = new("fff3d3");
    internal static StyleBoxFlat HudStyle(Color color, int margin = 12) => new()
    {
        BgColor = color, BorderColor = new Color("aa9f78"), BorderWidthBottom = 1,
        BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
        ContentMarginLeft = margin, ContentMarginRight = margin,
        ContentMarginTop = margin, ContentMarginBottom = margin,
        ShadowColor = new Color(0, 0, 0, .16f), ShadowSize = 4
    };
    internal static Theme HudTheme()
    {
        var theme = new Theme { DefaultFontSize = 14 };
        foreach (var type in new[] { "Button", "OptionButton" })
        {
            foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
                theme.SetStylebox(state, type, HudStyle(new Color(state == "hover" ? "d3e8df" : state == "pressed" ? "e5d5aa" : state == "disabled" ? "eee2be" : "fff6df"), 7));
            theme.SetColor("font_color", type, HudInk);
            theme.SetColor("font_hover_color", type, HudInk);
            theme.SetColor("font_pressed_color", type, HudInk);
            theme.SetColor("font_focus_color", type, HudInk);
            theme.SetColor("font_disabled_color", type, new Color("897f63"));
        }
        theme.SetStylebox("tab_selected", "TabContainer", HudStyle(HudPaper, 9));
        theme.SetStylebox("tab_unselected", "TabContainer", HudStyle(new Color("eaddb7"), 9));
        theme.SetStylebox("panel", "TabContainer", HudStyle(HudPaper, 14));
        theme.SetColor("font_selected_color", "TabContainer", HudInk);
        theme.SetColor("font_unselected_color", "TabContainer", HudInk);
        theme.SetColor("font_color", "Label", HudInk);
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
    internal static string FestivalGenreName(int genre) => genre switch
    { 0 => "Folk", 1 => "Rock", 2 => "Pop", 3 => "Electronic", _ => "Unknown" };
}

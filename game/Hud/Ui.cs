using Godot;
using System;

namespace Festival.Game;

/// <summary>
/// The HUD redesign's look: palette, fonts, icons and widget styles. The designer's mockups are
/// drawn for a 1280-wide screen, so mockup measurements go through <see cref="S"/>, which scales
/// them to the HUD canvas (1600 wide in play; 1280 in captures, where they match the mockups exactly).
/// </summary>
internal static class Ui
{
    public static float Scale { get; private set; } = 1.25f;
    /// <summary>Sets the mockup scale from the HUD canvas size, before any HUD is built.</summary>
    public static void Configure(Vector2 canvas) => Scale = canvas.X / 1280f;
    /// <summary>A mockup measurement in HUD canvas units.</summary>
    public static float S(float mockup) => Mathf.Round(mockup * Scale);
    public static int Px(float mockup) => (int)Mathf.Round(mockup * Scale);
    public static Vector2 S(float x, float y) => new(S(x), S(y));

    /// <summary>The top status bar's height.</summary>
    public static float TopBar => S(60);
    /// <summary>Where sheets and cards start below the top bar.</summary>
    public static float ContentTop => S(78);
    /// <summary>The preparation dock's height.</summary>
    public static float Dock => S(108);
    public static float Gutter => S(16);

    // Bars: the top status bar, the dock and dark tooltips.
    public static readonly Color BarDeep = new("0e1f1a");
    public static readonly Color Bar = new("17302a");
    public static readonly Color BarRaised = new("22453a");
    public static readonly Color BarText = new("f5ebd6");
    public static readonly Color BarMuted = new("b9c4b8");
    public static readonly Color BarLine = new(0.96f, 0.92f, 0.84f, 0.16f);
    public static readonly Color Gold = new("d8a43b");
    public static readonly Color GoldShadow = new("9c7424");
    public static readonly Color GoldInk = new("6b4f16");
    public static readonly Color GoldWash = new("fbf4e4");
    public static readonly Color Warn = new("f2a65a");
    public static readonly Color Good = new("8fd1b5");
    // Paper: sheets, cards and the documents.
    public static readonly Color Paper = new("f5ebd6");
    public static readonly Color PaperBright = new("fff8e9");
    public static readonly Color PaperRule = new("e2d3b3");
    public static readonly Color PaperEdge = new("d5c39e");
    public static readonly Color Ink = new("1f2a26");
    public static readonly Color InkMuted = new("56615a");
    public static readonly Color Teal = new("2b6e66");
    public static readonly Color TealDeep = new("1f4f49");
    public static readonly Color TealWash = new("dceae5");
    public static readonly Color TealLine = new("bfd7cf");
    public static readonly Color Alert = new("b8551e");
    public static readonly Color AlertWash = new("f8dcc3");
    public static readonly Color Link = new("9a4415");

    private static FontFile? _bodyFile;
    private static FontVariation? _body, _bodySemi, _bodyBold, _caps;
    private static FontFile? _slab, _slabBold;
    private static FontFile BodyFile => _bodyFile ??= GD.Load<FontFile>("res://assets/ui/fonts/SourceSans3-Variable.ttf");
    /// <summary>Source Sans 3 at regular weight; the variable font's own default is lighter.</summary>
    public static FontVariation Body => _body ??= Weight(400);
    public static FontVariation BodySemi => _bodySemi ??= Weight(600);
    public static FontVariation BodyBold => _bodyBold ??= Weight(700);
    /// <summary>Small letter-spaced capitals for field labels ("CASH", "FESTIVAL CLOCK").</summary>
    public static FontVariation CapsFont => _caps ??= new FontVariation { BaseFont = BodyBold, SpacingGlyph = 1 };
    public static FontFile Slab => _slab ??= GD.Load<FontFile>("res://assets/ui/fonts/ZillaSlab-SemiBold.ttf");
    public static FontFile SlabBold => _slabBold ??= GD.Load<FontFile>("res://assets/ui/fonts/ZillaSlab-Bold.ttf");

    private static FontVariation Weight(int weight)
    {
        var tag = TextServerManager.GetPrimaryInterface().NameToTag("wght");
        return new FontVariation { BaseFont = BodyFile, VariationOpentype = new Godot.Collections.Dictionary { { tag, weight } } };
    }

    /// <summary>A white line icon, tinted where it is drawn.</summary>
    public static Texture2D Icon(string name) => GD.Load<Texture2D>($"res://assets/ui/icons/{name}.svg");

    public static TextureRect IconRect(string name, float mockupSize, Color color) => new()
    {
        Texture = Icon(name), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = S(mockupSize, mockupSize),
        SelfModulate = color, MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>A rounded box; measurements are mockup pixels.</summary>
    public static StyleBoxFlat Box(Color background, float radius = 0, Color? border = null, float borderWidth = 0,
        float padX = 0, float padY = 0, int shadow = 0, float shadowAlpha = 0.35f)
    {
        var box = new StyleBoxFlat { BgColor = background, AntiAliasing = true };
        box.SetCornerRadiusAll(Px(radius));
        if (border is { } edge && borderWidth > 0) { box.BorderColor = edge; box.SetBorderWidthAll(Math.Max(1, Px(borderWidth))); }
        box.ContentMarginLeft = box.ContentMarginRight = S(padX);
        box.ContentMarginTop = box.ContentMarginBottom = S(padY);
        if (shadow > 0) { box.ShadowSize = Px(shadow); box.ShadowColor = new Color(0, 0, 0, shadowAlpha); box.ShadowOffset = new Vector2(0, S(shadow / 2f)); }
        return box;
    }

    /// <summary>A paper sheet floating over the field.</summary>
    public static StyleBoxFlat Sheet(float padX = 20, float padY = 18) =>
        Box(Paper, 6, new Color(0, 0, 0, 0.28f), 1, padX, padY, shadow: 14, shadowAlpha: 0.38f);

    /// <summary>A sheet's title with its one-line explanation.</summary>
    public static VBoxContainer PageHeading(string title, string lead)
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", Px(6));
        box.AddChild(Heading(title, 27));
        var words = Text(lead, 13.5f, InkMuted); words.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(words);
        return box;
    }

    /// <summary>A small-capitals section caption, optionally with an outlined tag such as "Required · choose one".</summary>
    public static HBoxContainer Section(string caption, string? tag = null)
    {
        var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", Px(8));
        var words = Caps(caption, InkMuted); words.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; line.AddChild(words);
        if (tag is not null)
        {
            var label = Caps(tag, Teal, 9.5f);
            label.AddThemeStyleboxOverride("normal", Box(new Color(0, 0, 0, 0), 4, Teal, 1, 5, 1));
            line.AddChild(label);
        }
        return line;
    }

    /// <summary>A thin paper-coloured vertical scrollbar.</summary>
    public static void SlimScrollbar(ScrollContainer scroll)
    {
        var bar = scroll.GetVScrollBar();
        bar.AddThemeStyleboxOverride("scroll", Box(new Color(0, 0, 0, 0.05f), 3));
        bar.AddThemeStyleboxOverride("grabber", Box(PaperEdge, 3));
        bar.AddThemeStyleboxOverride("grabber_highlight", Box(PaperEdge.Darkened(0.1f), 3));
        bar.AddThemeStyleboxOverride("grabber_pressed", Box(PaperEdge.Darkened(0.15f), 3));
        bar.CustomMinimumSize = new Vector2(S(6), 0);
    }

    /// <summary>Draws the clipboard clip above a sheet's top edge.</summary>
    public static void Clipboard(Control sheet, float mockupX = 150)
    {
        var clip = Box(new Color("2b2f2c"), 5);
        var lip = Box(new Color("474c47"), 5);
        sheet.Draw += () =>
        {
            var rect = new Rect2(S(mockupX), -S(9), S(64), S(20));
            sheet.DrawStyleBox(clip, rect);
            sheet.DrawStyleBox(lip, new Rect2(rect.Position + new Vector2(0, rect.Size.Y - S(5)), new Vector2(rect.Size.X, S(5))));
        };
    }

    public static Label Text(string text, float mockupSize, Color color, Font? font = null)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", Px(mockupSize));
        label.AddThemeColorOverride("font_color", color);
        if (font is not null) label.AddThemeFontOverride("font", font);
        return label;
    }
    public static Label Heading(string text, float mockupSize, Color? color = null) => Text(text, mockupSize, color ?? Ink, SlabBold);
    public static Label Caps(string text, Color color, float mockupSize = 10.5f) => Text(text.ToUpperInvariant(), mockupSize, color, CapsFont);

    public enum ButtonKind { Primary, Secondary, Accent, Bar, Quiet, Alert }

    /// <summary>Styles a button as one of the mockups' button kinds.</summary>
    public static T Style<T>(T button, ButtonKind kind, float mockupFont = 14, float radius = 6) where T : Button
    {
        var (background, text, border, hover) = kind switch
        {
            ButtonKind.Primary => (Gold, Bar, (Color?)null, Gold.Lightened(0.12f)),
            ButtonKind.Secondary => (GoldWash, GoldInk, (Color?)GoldShadow, new Color("fff9ec")),
            ButtonKind.Accent => (Teal, Colors.White, (Color?)null, Teal.Lightened(0.12f)),
            ButtonKind.Bar => (BarRaised, BarText, (Color?)new Color(0.96f, 0.92f, 0.84f, 0.2f), BarRaised.Lightened(0.1f)),
            ButtonKind.Alert => (Bar, BarText, (Color?)null, BarRaised),
            _ => (new Color(0, 0, 0, 0), InkMuted, (Color?)PaperEdge, new Color(0, 0, 0, 0.05f)),
        };
        var width = kind == ButtonKind.Secondary ? 1.5f : 1f;
        button.AddThemeStyleboxOverride("normal", Box(background, radius, border, width, 10, 4));
        button.AddThemeStyleboxOverride("hover", Box(hover, radius, border, width, 10, 4));
        button.AddThemeStyleboxOverride("pressed", Box(hover.Darkened(0.08f), radius, border, width, 10, 4));
        button.AddThemeStyleboxOverride("focus", Box(new Color(0, 0, 0, 0), radius, Gold, 2, 10, 4));
        button.AddThemeStyleboxOverride("disabled", Box(new Color(0, 0, 0, 0), radius, kind is ButtonKind.Bar or ButtonKind.Alert ? BarLine : TealLine, 1, 10, 4));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
            button.AddThemeColorOverride(state, text);
        foreach (var state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color", "icon_hover_pressed_color" })
            button.AddThemeColorOverride(state, text);
        button.AddThemeColorOverride("font_disabled_color", kind is ButtonKind.Bar or ButtonKind.Alert ? BarMuted : Teal);
        button.AddThemeColorOverride("icon_disabled_color", kind is ButtonKind.Bar or ButtonKind.Alert ? BarMuted : Teal);
        button.AddThemeFontOverride("font", BodyBold);
        button.AddThemeFontSizeOverride("font_size", Px(mockupFont));
        button.AddThemeConstantOverride("icon_max_width", Px(18));
        button.AddThemeConstantOverride("h_separation", Px(6));
        return button;
    }

    /// <summary>A button with a leading icon.</summary>
    public static Button IconButton(string text, string icon, ButtonKind kind, Action action, float mockupFont = 14)
    {
        var button = Style(new Button { Text = text, Icon = Icon(icon) }, kind, mockupFont);
        button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        button.Pressed += action;
        return button;
    }
}

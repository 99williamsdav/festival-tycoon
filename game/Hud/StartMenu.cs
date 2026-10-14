using Godot;
using System;

namespace Festival.Game;

/// <summary>The title screen: the festival logo, the three ways to play, and development tier starts.</summary>
internal sealed class StartMenu
{
    private CanvasLayer? _layer;

    public bool IsOpen => _layer is not null;

    public void Close() { _layer?.QueueFree(); _layer = null; }

    // The designer's mode buttons (assets/source/ui/hands-on-v1/INTEGRATION.md §3A): 244 × 58, 16 apart.
    private const float ModeWidth = 244, ModeHeight = 58;
    private static readonly Color Cream = new("f3e8c9"), HiVis = new("e8f23a"), Strap = new("b4bfb2");

    private static Label ModeLine(string text)
    {
        var line = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = Ui.S(ModeWidth, 0),
            AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeFontOverride("font", Ui.Body);
        line.AddThemeFontSizeOverride("font_size", Ui.Px(13));
        line.AddThemeColorOverride("font_color", Strap);
        // Lines 19 apart.
        line.AddThemeConstantOverride("line_spacing", Math.Max(0, Ui.Px(19) - (int)Ui.Body.GetHeight(Ui.Px(13))));
        return line;
    }

    private static VBoxContainer ModeColumn(Container modes)
    {
        var column = new VBoxContainer();
        // The strapline's first line is centred 72 below the button's top.
        column.AddThemeConstantOverride("separation", Ui.Px(4));
        modes.AddChild(column);
        return column;
    }

    /// <summary>
    /// A mode button: dark fill, a 2-unit edge (cream on the default choice, gold on hover), the icon 22 at +16 and the
    /// label in semibold 18 at +48, both in the mode's colour; keyboard focus adds a gold ring 4 outside.
    /// </summary>
    private static Button ModeButton(string name, string text, string icon, Color colour, string tip, bool chosen = false, bool fresh = false)
    {
        var button = new Button { Name = name, Text = text, Icon = Ui.Icon(icon), Alignment = HorizontalAlignment.Left, CustomMinimumSize = Ui.S(ModeWidth, ModeHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = tip };
        StyleBoxFlat Box(string fill, Color edge)
        {
            var box = Ui.Box(new Color(fill), 5, edge, 2);
            box.ContentMarginLeft = Ui.S(16); box.ContentMarginRight = Ui.S(12);
            return box;
        }
        button.AddThemeStyleboxOverride("normal", Box("15201f", chosen ? Cream : new Color("5d6a64")));
        button.AddThemeStyleboxOverride("hover", Box("1d2b29", Ui.Gold));
        button.AddThemeStyleboxOverride("pressed", Box("0f1716", Ui.Gold));
        button.AddThemeStyleboxOverride("hover_pressed", Box("0f1716", Ui.Gold));
        var focus = Ui.Box(new Color(0, 0, 0, 0), 8, new Color(Ui.Gold, .63f), 2);
        focus.SetExpandMarginAll(Ui.S(4));
        button.AddThemeStyleboxOverride("focus", focus);
        button.AddThemeFontOverride("font", Ui.BodySemi);
        button.AddThemeFontSizeOverride("font_size", Ui.Px(18));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color",
                     "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color", "icon_hover_pressed_color" })
            button.AddThemeColorOverride(state, colour);
        button.AddThemeConstantOverride("icon_max_width", Ui.Px(22));
        button.AddThemeConstantOverride("h_separation", Ui.Px(48 - 16 - 22));
        if (fresh)
        {
            // The NEW tag: a 40 × 18 gold pill at (+198, −9).
            var tag = new Control { Name = "NewTag", Position = Ui.S(198, -9), Size = Ui.S(40, 18), MouseFilter = Control.MouseFilterEnum.Ignore };
            tag.Draw += () =>
            {
                tag.DrawStyleBox(Ui.Box(Ui.Gold, 9), new Rect2(Vector2.Zero, tag.Size));
                var size = Ui.Px(10);
                var width = Ui.BodyBold.GetStringSize("NEW", HorizontalAlignment.Left, -1, size).X;
                tag.DrawString(Ui.BodyBold, new Vector2((tag.Size.X - width) / 2, (tag.Size.Y + Ui.BodyBold.GetAscent(size) - Ui.BodyBold.GetDescent(size)) / 2),
                    "NEW", HorizontalAlignment.Left, -1, size, Ui.Bar);
            };
            button.AddChild(tag);
        }
        return button;
    }

    private static Button Link(string name, string text, string colour, string tip, Action pressed)
    {
        var link = new Button { Name = name, Text = text, Flat = true, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = tip };
        link.AddThemeColorOverride("font_color", new Color(colour)); link.AddThemeFontSizeOverride("font_size", Ui.Px(14));
        link.Pressed += pressed;
        return link;
    }

    public void Open(Node parent, Action enter, Action fieldGuide, Action<int>? developmentTier = null, Action<int>? stewardShift = null,
        Action? continueShift = null, Action? frontOfHouse = null, Action? continueFrontOfHouse = null)
    {
        _layer = new CanvasLayer { Layer = 20 };
        parent.AddChild(_layer);
        var backdrop = new ColorRect { Color = new Color("142630") };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        _layer.AddChild(backdrop);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backdrop.AddChild(centre);
        var content = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        content.AddThemeConstantOverride("separation", Ui.Px(13));
        centre.AddChild(content);

        var visible = parent.GetViewport().GetVisibleRect().Size;
        var side = Mathf.Min(620f, Mathf.Min(visible.X * 0.55f, visible.Y * 0.69f));
        // The modes' straplines take a line under the buttons.
        side *= .85f;
        var logo = new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/branding/festival-tycoon-mosaic-logo-v2.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(side, side),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        content.AddChild(logo);
        var subtitle = new Label { Text = "LOWER WITTERING FARM  •  YOUR WEEKEND STARTS HERE",
            HorizontalAlignment = HorizontalAlignment.Center };
        subtitle.AddThemeFontSizeOverride("font_size", Ui.Px(14.5f));
        subtitle.AddThemeColorOverride("font_color", Cream);
        content.AddChild(subtitle);
        var button = ModeButton("EnterFestival", "MANAGEMENT ONLY", "map", Cream,
            "The campaign: plan, book and hire, then run the day from the manager's view.", chosen: true);
        button.Pressed += enter;
        // The three ways to play side by side, each with its line underneath.
        var modes = new HBoxContainer { Name = "Modes", Alignment = BoxContainer.AlignmentMode.Center };
        modes.AddThemeConstantOverride("separation", Ui.Px(16));
        content.AddChild(modes);
        var classic = ModeColumn(modes);
        classic.AddChild(button);
        classic.AddChild(ModeLine("Run the festival from the office."));
        if (frontOfHouse is not null)
        {
            // The campaign with the player on the ground too, until they can afford stewards of their own.
            var hands = ModeColumn(modes);
            var foh = ModeButton("FrontOfHouse", Main.HandsOnModeName.ToUpperInvariant(), "user", HiVis,
                "A new campaign where you manage the festival and work it as a steward too. V swaps views.", fresh: true);
            foh.Pressed += frontOfHouse;
            hands.AddChild(foh);
            hands.AddChild(ModeLine(Main.HandsOnStrapline));
            if (continueFrontOfHouse is not null) hands.AddChild(Link("ContinueFrontOfHouse", "Continue " + Main.HandsOnModeName, "e8f23a",
                "Back to your saved " + Main.HandsOnModeName + " campaign.", continueFrontOfHouse));
        }
        var guide = new Button { Name = "FieldGuide", Text = "Field guide", Flat = true, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        guide.AddThemeColorOverride("font_color", new Color("f3e8c9")); guide.AddThemeFontSizeOverride("font_size", Ui.Px(16));
        guide.Pressed += fieldGuide;
        // The trials share one row under the guide.
        var trials = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        trials.AddThemeConstantOverride("separation", Ui.Px(28));
        var developmentPicker = new VBoxContainer { Name = "DevelopmentPicker", Visible = false, Alignment = BoxContainer.AlignmentMode.Center };
        developmentPicker.AddThemeConstantOverride("separation", Ui.Px(8));
        if (developmentTier is not null)
        {
            var (guideShown, trialsShown) = (true, true);
            Button? developmentLink = null;
            developmentLink = Link("DevelopmentTiers", "Development · pick a tier", "9fd3c2",
                "Start a fresh Tier 1 or prepare Tier 2 after a recorded Tier 1 clear. Uses the management campaign saves.", () =>
                {
                    (guideShown, trialsShown) = (guide.Visible, trials.Visible);
                    modes.Visible = false; guide.Visible = false; trials.Visible = false; developmentPicker.Visible = true;
                    logo.CustomMinimumSize = new Vector2(side, side) * .62f;
                    developmentPicker.GetChild<Button>(1).GrabFocus();
                });
            trials.AddChild(developmentLink);
            var heading = new Label { Text = "PICK A DEVELOPMENT TIER", HorizontalAlignment = HorizontalAlignment.Center };
            heading.AddThemeFontSizeOverride("font_size", Ui.Px(13)); heading.AddThemeColorOverride("font_color", new Color("9fd3c2"));
            developmentPicker.AddChild(heading);
            foreach (var (tier, label, tip) in new[]
            {
                (1, "Tier 1 · Heatwave · fresh start", "A fresh management campaign with the usual starting money and no carried knowledge or relationships."),
                (2, "Tier 2 · Heavy rain · carried start", "Prepare both stages after a recorded Tier 1 clear, with its actual cash, knowledge and relationships."),
            })
            {
                var pick = new Button { Name = $"DevelopmentTier{tier}", Text = label, CustomMinimumSize = Ui.S(320, 36), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                    MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = tip };
                pick.AddThemeFontSizeOverride("font_size", Ui.Px(16));
                pick.Pressed += () => developmentTier(tier);
                developmentPicker.AddChild(pick);
            }
            var back = Link("DevelopmentBack", "Back", "f3e8c9", "Back to the mode choices.", () =>
            {
                developmentPicker.Visible = false; modes.Visible = true; guide.Visible = guideShown; trials.Visible = trialsShown;
                logo.CustomMinimumSize = new Vector2(side, side); developmentLink!.GrabFocus();
            });
            developmentPicker.AddChild(back);
        }
        // The shift picker, in place of the modes and trials while it's open.
        var picker = new VBoxContainer { Name = "ShiftPicker", Visible = false, Alignment = BoxContainer.AlignmentMode.Center };
        picker.AddThemeConstantOverride("separation", Ui.Px(8));
        if (stewardShift is not null)
        {
            // One steward on the ground at a festival that runs itself. Its saves keep to their own folder.
            var steward = ModeColumn(modes);
            var shift = ModeButton("StewardShift", "STEWARD ONLY", "radio", HiVis,
                "Work a festival day as one of its stewards, in first person. Kept apart from your campaigns.");
            steward.AddChild(shift);
            steward.AddChild(ModeLine("Pull on the hi-vis for a single shift."));
            // The logo shrinks while the picker is open, so its buttons fit under it.
            var (guideShown, trialsShown) = (true, true);
            shift.Pressed += () => { (guideShown, trialsShown) = (guide.Visible, trials.Visible);
                modes.Visible = false; guide.Visible = false; trials.Visible = false; picker.Visible = true;
                logo.CustomMinimumSize = new Vector2(side, side) * .62f; picker.GetChild<Button>(1).GrabFocus(); };
            if (continueShift is not null) steward.AddChild(Link("ContinueShift", "Continue shift", "e8f23a",
                "Back to your saved steward shift, where you left it.", continueShift));
            var heading = new Label { Text = "PICK A SHIFT", HorizontalAlignment = HorizontalAlignment.Center };
            heading.AddThemeFontSizeOverride("font_size", Ui.Px(13)); heading.AddThemeColorOverride("font_color", new Color("e8f23a"));
            picker.AddChild(heading);
            foreach (var (tier, label, tip) in new[]
            {
                (1, "Tier 1 · Heatwave · one stage", "A hot day by the trailer stage: keep the crowd watered, calm and in one piece."),
                (2, "Tier 2 · Rain · two stages", "A wet day across both stages: mud, straw, and twice the ground to cover."),
            })
            {
                var pick = new Button { Name = $"ShiftTier{tier}", Text = label, CustomMinimumSize = Ui.S(256, 36), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                    MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = tip };
                pick.AddThemeFontSizeOverride("font_size", Ui.Px(16));
                pick.Pressed += () => stewardShift(tier);
                picker.AddChild(pick);
            }
            var back = new Button { Name = "ShiftBack", Text = "Back", Flat = true, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            back.AddThemeColorOverride("font_color", new Color("f3e8c9")); back.AddThemeFontSizeOverride("font_size", Ui.Px(14));
            back.Pressed += () => { picker.Visible = false; modes.Visible = true; guide.Visible = guideShown; trials.Visible = trialsShown;
                logo.CustomMinimumSize = new Vector2(side, side); shift.GrabFocus(); };
            picker.AddChild(back);
        }
        content.AddChild(guide);
        content.AddChild(trials);
        content.AddChild(picker);
        content.AddChild(developmentPicker);
        button.GrabFocus();
    }
}

using Godot;
using System;

namespace Festival.Game;

/// <summary>The title screen: the festival logo, the three ways to play (Management Only, All-Rounder, Steward Only), and the Pond Stage trial.</summary>
internal sealed class StartMenu
{
    private CanvasLayer? _layer;

    public bool IsOpen => _layer is not null;

    public void Close() { _layer?.QueueFree(); _layer = null; }

    private static Label ModeLine(string text)
    {
        var line = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(250, 0),
            AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeFontSizeOverride("font_size", Ui.Px(13));
        line.AddThemeColorOverride("font_color", new Color("cbbf9f"));
        return line;
    }

    private static VBoxContainer ModeColumn(Container modes)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", Ui.Px(6));
        modes.AddChild(column);
        return column;
    }

    private static Button ModeButton(string name, string text, string colour, string tip)
    {
        var button = new Button { Name = name, Text = text, CustomMinimumSize = new Vector2(250, 54), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = tip };
        button.AddThemeFontSizeOverride("font_size", 21);
        button.AddThemeColorOverride("font_color", new Color(colour));
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

    public void Open(Node parent, Action enter, Action fieldGuide, Action? tryPondStage = null, Action<int>? stewardShift = null,
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
        content.AddThemeConstantOverride("separation", 16);
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
        subtitle.AddThemeFontSizeOverride("font_size", 18);
        subtitle.AddThemeColorOverride("font_color", new Color("f3e8c9"));
        content.AddChild(subtitle);
        var button = ModeButton("EnterFestival", "MANAGEMENT ONLY", "f3e8c9",
            "The campaign: plan, book and hire, then run the day from the manager's view.");
        button.Pressed += enter;
        // The three ways to play side by side, each with its line underneath.
        var modes = new HBoxContainer { Name = "Modes", Alignment = BoxContainer.AlignmentMode.Center };
        modes.AddThemeConstantOverride("separation", Ui.Px(18));
        content.AddChild(modes);
        var classic = ModeColumn(modes);
        classic.AddChild(button);
        classic.AddChild(ModeLine("Run the festival from the office."));
        if (frontOfHouse is not null)
        {
            // The campaign with the player on the ground too, until they can afford stewards of their own.
            var hands = ModeColumn(modes);
            var foh = ModeButton("FrontOfHouse", Main.HandsOnModeName.ToUpperInvariant(), "e8f23a",
                "A new campaign where you manage the festival and work it as a steward too. Tab swaps views.");
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
        if (tryPondStage is not null)
        {
            // A new campaign with the second stage open from the start; like Enter on a finished day, it takes over the autosaves.
            trials.AddChild(Link("TryPondStage", "Try the Pond Stage · trial", "9fd3c2",
                "Start a new trial festival with a second stage by the pond, before it would normally open.", tryPondStage));
        }
        // The shift picker, in place of the modes and trials while it's open.
        var picker = new VBoxContainer { Name = "ShiftPicker", Visible = false, Alignment = BoxContainer.AlignmentMode.Center };
        picker.AddThemeConstantOverride("separation", Ui.Px(8));
        if (stewardShift is not null)
        {
            // One steward on the ground at a festival that runs itself. Its saves keep to their own folder.
            var steward = ModeColumn(modes);
            var shift = ModeButton("StewardShift", "STEWARD ONLY", "e8f23a",
                "Work a festival day as one of its stewards, in first person. Kept apart from your campaigns.");
            steward.AddChild(shift);
            steward.AddChild(ModeLine("Pull on the hi-vis for a single shift."));
            // The logo shrinks while the picker is open, so its buttons fit under it.
            shift.Pressed += () => { modes.Visible = false; guide.Visible = false; trials.Visible = false; picker.Visible = true;
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
                var pick = new Button { Name = $"ShiftTier{tier}", Text = label, CustomMinimumSize = new Vector2(320, 44), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                    MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = tip };
                pick.AddThemeFontSizeOverride("font_size", Ui.Px(16));
                pick.Pressed += () => stewardShift(tier);
                picker.AddChild(pick);
            }
            var back = new Button { Name = "ShiftBack", Text = "Back", Flat = true, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            back.AddThemeColorOverride("font_color", new Color("f3e8c9")); back.AddThemeFontSizeOverride("font_size", Ui.Px(14));
            back.Pressed += () => { picker.Visible = false; modes.Visible = true; guide.Visible = true; trials.Visible = true;
                logo.CustomMinimumSize = new Vector2(side, side); shift.GrabFocus(); };
            picker.AddChild(back);
        }
        content.AddChild(guide);
        content.AddChild(trials);
        content.AddChild(picker);
        button.GrabFocus();
    }
}

using Godot;
using System;

namespace Festival.Game;

/// <summary>The title screen: the festival logo, the Enter button beside Front of House, and a way into the Pond Stage trial.</summary>
internal sealed class StartMenu
{
    private CanvasLayer? _layer;

    public bool IsOpen => _layer is not null;

    public void Close() { _layer?.QueueFree(); _layer = null; }

    private static Label ModeLine(string text)
    {
        var line = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(280, 0),
            AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeFontSizeOverride("font_size", Ui.Px(13));
        line.AddThemeColorOverride("font_color", new Color("cbbf9f"));
        return line;
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
        if (frontOfHouse is not null) side *= .88f;
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
        var button = new Button { Name = "EnterFestival", Text = "ENTER FESTIVAL", CustomMinimumSize = new Vector2(280, 54),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        button.AddThemeFontSizeOverride("font_size", 21);
        button.Pressed += enter;
        if (frontOfHouse is null) content.AddChild(button);
        else
        {
            // The two ways to play side by side: the manager's campaign, and Front of House with the player on the ground too.
            var modes = new HBoxContainer { Name = "Modes", Alignment = BoxContainer.AlignmentMode.Center };
            modes.AddThemeConstantOverride("separation", Ui.Px(18));
            content.AddChild(modes);
            var classic = new VBoxContainer();
            classic.AddThemeConstantOverride("separation", Ui.Px(6));
            modes.AddChild(classic);
            classic.AddChild(button);
            classic.AddChild(ModeLine("Run the festival from the office."));
            var hands = new VBoxContainer();
            hands.AddThemeConstantOverride("separation", Ui.Px(6));
            modes.AddChild(hands);
            var foh = new Button { Name = "FrontOfHouse", Text = Main.HandsOnModeName.ToUpperInvariant(), CustomMinimumSize = new Vector2(280, 54),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                TooltipText = "A new campaign where you manage the festival and work it as a steward too. Tab swaps views." };
            foh.AddThemeFontSizeOverride("font_size", 21);
            foh.AddThemeColorOverride("font_color", new Color("e8f23a"));
            foh.Pressed += frontOfHouse;
            hands.AddChild(foh);
            hands.AddChild(ModeLine(Main.HandsOnStrapline));
            if (continueFrontOfHouse is not null)
            {
                var resume = new Button { Name = "ContinueFrontOfHouse", Text = "Continue " + Main.HandsOnModeName, Flat = true,
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                    TooltipText = "Back to your saved Front of House campaign." };
                resume.AddThemeColorOverride("font_color", new Color("e8f23a")); resume.AddThemeFontSizeOverride("font_size", Ui.Px(14));
                resume.Pressed += continueFrontOfHouse;
                hands.AddChild(resume);
            }
        }
        var guide = new Button { Name = "FieldGuide", Text = "Field guide", Flat = true, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        guide.AddThemeColorOverride("font_color", new Color("f3e8c9")); guide.AddThemeFontSizeOverride("font_size", Ui.Px(16));
        guide.Pressed += fieldGuide;
        content.AddChild(guide);
        // The trials share one row under the guide.
        var trials = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        trials.AddThemeConstantOverride("separation", Ui.Px(28));
        content.AddChild(trials);
        if (tryPondStage is not null)
        {
            // A new campaign with the second stage open from the start; like Enter on a finished day, it takes over the autosaves.
            var pond = new Button { Name = "TryPondStage", Text = "Try the Pond Stage · trial", Flat = true, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                TooltipText = "Start a new trial festival with a second stage by the pond, before it would normally open." };
            pond.AddThemeColorOverride("font_color", new Color("9fd3c2")); pond.AddThemeFontSizeOverride("font_size", Ui.Px(16));
            pond.Pressed += tryPondStage;
            trials.AddChild(pond);
        }
        // The shift picker, in place of the trials row while it's open.
        var picker = new VBoxContainer { Name = "ShiftPicker", Visible = false, Alignment = BoxContainer.AlignmentMode.Center };
        picker.AddThemeConstantOverride("separation", Ui.Px(8));
        content.AddChild(picker);
        if (stewardShift is not null)
        {
            // A separate prototype: one steward on the ground at a festival that runs itself. Its saves keep to their own folder.
            var shift = new Button { Name = "StewardShift", Text = "Steward shift · prototype", Flat = true, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                TooltipText = "Work a festival day as one of its stewards, in first person. A prototype, kept apart from your campaign." };
            shift.AddThemeColorOverride("font_color", new Color("e8f23a")); shift.AddThemeFontSizeOverride("font_size", Ui.Px(16));
            // The logo shrinks while the picker is open, so its buttons fit under it.
            shift.Pressed += () => { trials.Visible = false; picker.Visible = true; logo.CustomMinimumSize = new Vector2(side, side) * .62f; picker.GetChild<Button>(1).GrabFocus(); };
            trials.AddChild(shift);
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
            back.Pressed += () => { picker.Visible = false; trials.Visible = true; logo.CustomMinimumSize = new Vector2(side, side); };
            picker.AddChild(back);
        }
        if (continueShift is not null)
        {
            // Only offered while a saved shift still has its steward on duty.
            var resume = new Button { Name = "ContinueShift", Text = "Continue shift", Flat = true, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = "Back to your saved steward shift, where you left it." };
            resume.AddThemeColorOverride("font_color", new Color("e8f23a")); resume.AddThemeFontSizeOverride("font_size", Ui.Px(16));
            resume.Pressed += continueShift;
            trials.AddChild(resume);
        }
        button.GrabFocus();
    }
}

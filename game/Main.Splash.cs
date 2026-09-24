using Godot;

namespace Festival.Game;

public partial class Main
{
    private CanvasLayer? _startSplash;
    private string? _startSplashCapturePath;
    private int _startSplashCaptureFrame;

    private void BuildStartSplash()
    {
        _startSplash = new CanvasLayer { Layer = 20 };
        AddChild(_startSplash);
        var backdrop = new ColorRect { Color = new Color("142630") };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        _startSplash.AddChild(backdrop);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backdrop.AddChild(centre);
        var content = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        content.AddThemeConstantOverride("separation", 16);
        centre.AddChild(content);

        var visible = GetViewport().GetVisibleRect().Size;
        var side = Mathf.Min(620f, Mathf.Min(visible.X * 0.55f, visible.Y * 0.69f));
        var logo = new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/branding/lwf-festival-tycoon-logo-v1.png"),
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
        var button = new Button { Text = "ENTER FESTIVAL", CustomMinimumSize = new Vector2(280, 54),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        button.AddThemeFontSizeOverride("font_size", 21);
        button.Pressed += () => { _startSplash?.QueueFree(); _startSplash = null; };
        content.AddChild(button);
        button.GrabFocus();
    }

    private void ProcessStartSplashCapture()
    {
        if (_startSplashCapturePath is null || ++_startSplashCaptureFrame < 4) return;
        GetViewport().GetTexture().GetImage().SavePng(_startSplashCapturePath);
        GD.Print($"START_SPLASH_CAPTURE path={_startSplashCapturePath}");
        GetTree().Quit();
    }
}

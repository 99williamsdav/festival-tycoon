using Festival.Simulation;
using Godot;
using System;

namespace Festival.Game;

/// <summary>A full, uncropped mosaic with separate accessible UI and no world input.</summary>
internal sealed class ChallengeReveal
{
    public CanvasLayer? Layer { get; private set; }
    public Button? Continue { get; private set; }
    public TextureRect? Artwork { get; private set; }
    public Control? Root { get; private set; }
    public bool IsOpen => Layer is not null;

    public void Close()
    {
        if (Layer is null) return;
        Layer.Hide(); Layer.QueueFree(); Layer = null; Root = null;
    }

    public void Open(Node parent, FestivalChallenge challenge, int tier, Action proceed, Action menu)
    {
        Close();
        Layer = new CanvasLayer { Layer = 25, Name = "ChallengeReveal" }; parent.AddChild(Layer);
        var root = new ColorRect { Color = Ui.BarDeep, MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect); Layer.AddChild(root); Root = root;
        Artwork = new TextureRect { Texture = GD.Load<Texture2D>($"res://assets/ui/challenges/{challenge.Artwork}"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(Artwork);
        var heading = new Label { Text = challenge.Title, MouseFilter = Control.MouseFilterEnum.Ignore };
        heading.AddThemeColorOverride("font_color", Ui.BarText); root.AddChild(heading);
        var detail = new Label { Text = challenge.Description, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        detail.AddThemeColorOverride("font_color", Ui.BarMuted); root.AddChild(detail);
        var tierLabel = new Label { Text = $"FESTIVAL CHALLENGE · TIER {tier}", MouseFilter = Control.MouseFilterEnum.Ignore };
        tierLabel.AddThemeColorOverride("font_color", Ui.Gold); root.AddChild(tierLabel);
        Continue = Ui.Style(new Button { Name = "PrepareFestival", Text = "Prepare festival", FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand }, Ui.ButtonKind.Primary);
        Continue.Pressed += proceed; root.AddChild(Continue);
        var back = Ui.Style(new Button { Name = "ChallengeMenu", Text = "Return to menu", FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand }, Ui.ButtonKind.Quiet);
        back.Pressed += menu; root.AddChild(back);
        Continue.FocusNeighborBottom = Continue.GetPathTo(back); Continue.FocusNext = Continue.GetPathTo(back);
        back.FocusNeighborTop = back.GetPathTo(Continue); back.FocusNext = back.GetPathTo(Continue);
        void Layout()
        {
            var size = parent.GetViewport().GetVisibleRect().Size;
            var scale = Math.Clamp(Math.Min(size.X / 1280f, size.Y / 720f), .65f, 2f);
            var margin = 16 * scale; var footer = 116 * scale;
            var imageHeight = Math.Min(size.Y - footer - margin * 2, (size.X - margin * 2) / 1.5f);
            var imageWidth = imageHeight * 1.5f; var left = (size.X - imageWidth) / 2;
            Artwork.Position = new(left, margin); Artwork.Size = new(imageWidth, imageHeight);
            var y = margin + imageHeight + 8 * scale;
            tierLabel.Position = new(left, y); tierLabel.Size = new(imageWidth, 18 * scale);
            tierLabel.AddThemeFontSizeOverride("font_size", (int)(12 * scale));
            heading.Position = new(left, y + 19 * scale); heading.Size = new(imageWidth - 250 * scale, 37 * scale);
            heading.AddThemeFontSizeOverride("font_size", (int)(30 * scale));
            detail.Position = new(left, y + 58 * scale); detail.Size = new(imageWidth - 255 * scale, 46 * scale);
            detail.AddThemeFontSizeOverride("font_size", (int)(16 * scale));
            Continue.Position = new(left + imageWidth - 235 * scale, y + 23 * scale); Continue.Size = new(235 * scale, 46 * scale);
            Continue.AddThemeFontSizeOverride("font_size", (int)(18 * scale));
            back.Position = new(left + imageWidth - 235 * scale, y + 74 * scale); back.Size = new(235 * scale, 28 * scale);
            back.AddThemeFontSizeOverride("font_size", (int)(14 * scale));
        }
        root.Resized += Layout; Layout(); Continue.CallDeferred(Control.MethodName.GrabFocus);
    }
}

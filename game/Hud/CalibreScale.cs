using Godot;

namespace Festival.Game;

/// <summary>Five calibre steps; steps below the crowd's are passed, theirs is gold, with a "Your crowd" marker above.</summary>
public partial class CalibreScale : Control
{
    private int _step;
    private float _marker;
    public int Step { get => _step; set { _step = value; QueueRedraw(); } }
    public float Marker { get => _marker; set { _marker = Mathf.Clamp(value, 0, 1); QueueRedraw(); } }

    public override void _Draw()
    {
        var gap = Ui.S(3); var width = (Size.X - gap * 4) / 5;
        for (var i = 0; i < 5; i++)
        {
            var colour = i < _step ? new Color("cfddbf") : i == _step ? Ui.Gold : Ui.PaperRule;
            var box = Ui.Box(colour, 0);
            if (i == 0) box.CornerRadiusTopLeft = box.CornerRadiusBottomLeft = Ui.Px(5);
            if (i == 4) box.CornerRadiusTopRight = box.CornerRadiusBottomRight = Ui.Px(5);
            DrawStyleBox(box, new Rect2(i * (width + gap), 0, width, Size.Y));
        }
        var x = Size.X * _marker;
        DrawRect(new Rect2(x - Ui.S(1), -Ui.S(22), Ui.S(2), Ui.S(22) + Size.Y), Ui.Bar);
        var font = Ui.CapsFont; var size = Ui.Px(9.5f); var text = "YOUR CROWD";
        var textWidth = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        var tag = new Rect2(x - textWidth / 2 - Ui.S(7), -Ui.S(40), textWidth + Ui.S(14), Ui.S(18));
        DrawStyleBox(Ui.Box(Ui.Bar, 4), tag);
        DrawString(font, new Vector2(tag.Position.X + Ui.S(7), tag.Position.Y + Ui.S(13)), text, HorizontalAlignment.Left, -1, size, Ui.BarText);
    }
}

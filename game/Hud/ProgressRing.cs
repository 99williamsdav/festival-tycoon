using Godot;

namespace Festival.Game;

/// <summary>A circular progress track with a centred caption, such as "5/7".</summary>
public partial class ProgressRing : Control
{
    private float _fraction;
    private string _text = "";

    public Color Track { get; set; }
    public Color Fill { get; set; }
    public Color TextColor { get; set; }
    public float Thickness { get; set; } = 5;
    public Font? Font { get; set; }
    public int FontSize { get; set; } = 16;

    public float Fraction { get => _fraction; set { _fraction = Mathf.Clamp(value, 0, 1); QueueRedraw(); } }
    public string Text { get => _text; set { _text = value; QueueRedraw(); } }

    public override void _Draw()
    {
        var centre = Size / 2;
        var radius = Mathf.Min(Size.X, Size.Y) / 2 - Thickness / 2;
        DrawArc(centre, radius, 0, Mathf.Tau, 64, Track, Thickness, true);
        if (_fraction > 0)
            DrawArc(centre, radius, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * _fraction, 64, Fill, Thickness, true);
        var font = Font ?? GetThemeDefaultFont();
        var size = font.GetStringSize(_text, HorizontalAlignment.Left, -1, FontSize);
        DrawString(font, new Vector2(centre.X - size.X / 2, centre.Y + (font.GetAscent(FontSize) - font.GetDescent(FontSize)) / 2),
            _text, HorizontalAlignment.Left, -1, FontSize, TextColor);
    }
}

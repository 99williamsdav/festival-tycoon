using Festival.Simulation;
using Godot;

namespace Festival.Game;

/// <summary>Five small bars filled in half steps from a 0–100 score; intensity, not quality.</summary>
public partial class RatingBars : Control
{
    private int _score;
    public int Score { get => _score; set { _score = value; QueueRedraw(); } }
    public Color Ink { get; set; } = Colors.Black;
    public Color Empty { get; set; } = new("e2d3b3");

    public override Vector2 _GetMinimumSize() => new(Ui.S(58), Ui.S(12));

    public override void _Draw()
    {
        var halves = BookingTableView.HalfStarUnits(_score);
        var width = Ui.S(10); var height = Ui.S(12); var gap = Ui.S(2);
        var top = (Size.Y - height) / 2;
        for (var i = 0; i < 5; i++)
        {
            var rect = new Rect2(i * (width + gap), top, width, height);
            var empty = Ui.Box(Empty, 2);
            DrawStyleBox(empty, rect);
            var filled = halves >= (i + 1) * 2 ? width : halves == i * 2 + 1 ? width / 2 : 0;
            if (filled > 0) DrawStyleBox(Ui.Box(Ink, 2), new Rect2(rect.Position, new Vector2(filled, height)));
        }
    }
}

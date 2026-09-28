using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;

namespace Festival.Game;

// Five code-drawn outlined stars. A half step fills exactly the left half of a star.
public partial class BookingStarRating : Control
{
    private int _score;
    public int Score { get => _score; set { _score = Math.Clamp(value, 0, 100); QueueRedraw(); } }
    public Color Ink { get; set; } = new("293b38");
    public BookingStarRating() => CustomMinimumSize = new Vector2(92, 20);

    public override void _Draw()
    {
        var halves = BookingTableView.HalfStarUnits(Score);
        for (var index = 0; index < 5; index++)
        {
            var centre = new Vector2(9 + index * 18, 10);
            var polygon = Star(centre);
            var fill = Math.Clamp(halves - index * 2, 0, 2);
            if (fill == 2) DrawColoredPolygon(polygon, Ink);
            if (fill == 1) DrawColoredPolygon(ClipLeft(polygon, centre.X), Ink);
            var outline = new Vector2[polygon.Length + 1];
            Array.Copy(polygon, outline, polygon.Length); outline[^1] = polygon[0];
            DrawPolyline(outline, Ink, 1.2f, true);
        }
    }

    private static Vector2[] Star(Vector2 centre)
    {
        var polygon = new Vector2[10];
        for (var i = 0; i < polygon.Length; i++)
        {
            var radius = i % 2 == 0 ? 8f : 3.5f;
            var angle = -MathF.PI / 2 + i * MathF.PI / 5;
            polygon[i] = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
        return polygon;
    }

    private static Vector2[] ClipLeft(Vector2[] polygon, float right)
    {
        var result = new List<Vector2>();
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length];
            var insideA = a.X <= right; var insideB = b.X <= right;
            if (insideA) result.Add(a);
            if (insideA != insideB)
            {
                var t = (right - a.X) / (b.X - a.X);
                result.Add(a.Lerp(b, t));
            }
        }
        return result.ToArray();
    }
}

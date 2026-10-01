using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>Opening cash, each movement as a floating bar, and closing cash.</summary>
public partial class CashWaterfall : Control
{
    public List<(string Label, long Change, bool Total)> Steps { get; set; } = [];
    public override void _Draw()
    {
        var font = Ui.BodyBold; var small = Ui.Body;
        var labelSize = Ui.Px(12.5f); var captionSize = Ui.Px(12);
        var running = 0L; var spans = new List<(long From, long To)>();
        foreach (var (_, change, total) in Steps)
        {
            if (total) { spans.Add((0, change)); running = change; }
            else { spans.Add((running, running + change)); running += change; }
        }
        var top = Math.Max(1, spans.Max(span => Math.Max(span.From, span.To)));
        var columns = Steps.Count; var gap = Ui.S(14);
        var width = (Size.X - gap * (columns - 1)) / columns;
        var floor = Size.Y - Ui.S(18); var ceiling = Ui.S(22);
        float Y(long value) => floor - (floor - ceiling) * value / top;
        for (var i = 0; i < columns; i++)
        {
            var (label, change, total) = Steps[i];
            var (from, to) = spans[i];
            var x = i * (width + gap);
            var y1 = Y(Math.Max(from, to)); var y2 = Y(Math.Min(from, to));
            var colour = total ? i == 0 ? Ui.InkMuted : Ui.Ink : change >= 0 ? new Color("2b6e66") : Ui.Alert;
            var rect = new Rect2(x, y1, width, Math.Max(Ui.S(3), y2 - y1));
            DrawStyleBox(Ui.Box(colour, 3), rect);
            var text = total ? FestivalCurrency.Format(change) : (change >= 0 ? "+" : "−") + FestivalCurrency.Format(Math.Abs(change));
            var ink = total ? Ui.Ink : change >= 0 ? Ui.TealDeep : Ui.Link;
            var textWidth = font.GetStringSize(text, HorizontalAlignment.Left, -1, labelSize).X;
            DrawString(font, new Vector2(x + (width - textWidth) / 2, rect.Position.Y - Ui.S(5)), text, HorizontalAlignment.Left, -1, labelSize, ink);
            var captionWidth = small.GetStringSize(label, HorizontalAlignment.Left, -1, captionSize).X;
            DrawString(small, new Vector2(x + (width - captionWidth) / 2, Size.Y - Ui.S(2)), label, HorizontalAlignment.Left, -1, captionSize, Ui.InkMuted);
        }
    }
}

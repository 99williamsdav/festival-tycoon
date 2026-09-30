using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// Floating +£/−£ labels for festival cash movements. Movements at one place within a moment
/// merge; at most eight show at once and the rest wait their turn, so no cash is dropped.
/// <c>resolveAnchor</c> maps a movement's anchor key to the place it shows now (a hidden
/// control falls back to a panel); <c>screenAnchor</c> gives that place's screen point, if visible.
/// </summary>
internal sealed class CashPopups(Node _parent, Func<string, string> _resolveAnchor, Func<string, Vector2?> _screenAnchor)
{
    private readonly FestivalCashFeedbackCursor _financeFeedbackCursor = new();
    private readonly List<CashPopup> _cashPopups = [];
    private readonly Dictionary<(string Anchor, bool Credit), long> _cashPopupPending = [];
    private CanvasLayer? _cashPopupLayer;

    private sealed class CashPopup(string anchor, long pennies, Label label)
    {
        public string Anchor = anchor;
        public long Pennies = pennies;
        public Label Label = label;
        public double Age;
    }

    public void Reset(GameSession session)
    {
        _financeFeedbackCursor.Reset(session.CaptureFestivalCashFeedbackEvents());
        foreach (var popup in _cashPopups) popup.Label.QueueFree();
        _cashPopups.Clear(); _cashPopupPending.Clear();
    }

    private static string CashPopupText(long pennies) => FestivalCurrency.Format(pennies, signed: true);

    public void Advance(GameSession session, double delta)
    {
        foreach (var popup in _cashPopups.ToArray())
        {
            popup.Age += delta;
            if (popup.Age >= 2) { popup.Label.QueueFree(); _cashPopups.Remove(popup); }
        }
        foreach (var item in _financeFeedbackCursor.Observe(session.CaptureFestivalCashFeedbackEvents()))
        {
            var anchor = _resolveAnchor(item.AnchorKey);
            var credit = item.FestivalCashPennies > 0;
            var recent = _cashPopups.FirstOrDefault(p => p.Anchor == anchor && (p.Pennies > 0) == credit && p.Age < .2);
            if (recent is not null) recent.Pennies += item.FestivalCashPennies;
            else
            {
                var key = (anchor, credit);
                _cashPopupPending[key] = _cashPopupPending.GetValueOrDefault(key) + item.FestivalCashPennies;
            }
        }
        // Overflow remains aggregated by the finite visible control/vendor keys
        // and sign, then drains when a display slot expires. No cash is discarded
        // and opposite signs never cancel each other.
        foreach (var entry in _cashPopupPending.ToArray())
        {
            if (_cashPopups.Count >= 8) break;
            _cashPopupLayer ??= new CanvasLayer { Layer = 30 };
            if (!_cashPopupLayer.IsInsideTree()) _parent.AddChild(_cashPopupLayer);
            var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center, Size = new Vector2(160, 42) };
            label.AddThemeFontSizeOverride("font_size", 26);
            label.AddThemeConstantOverride("outline_size", 5);
            label.AddThemeColorOverride("font_outline_color", new Color("243327"));
            label.AddThemeColorOverride("font_color", new Color(entry.Key.Credit ? "74f49a" : "ff857b"));
            _cashPopupLayer.AddChild(label);
            _cashPopups.Add(new(entry.Key.Anchor, entry.Value, label));
            _cashPopupPending.Remove(entry.Key);
        }
        var viewport = _parent.GetViewport().GetVisibleRect().Size;
        var anchorSlots = new Dictionary<string, int>();
        foreach (var popup in _cashPopups)
        {
            popup.Label.Text = CashPopupText(popup.Pennies);
            // Scrolling can change the visible anchor after a popup was created.
            // Stack at the resolved location, retaining each original cash sum.
            var effectiveAnchor = _resolveAnchor(popup.Anchor);
            var anchor = _screenAnchor(effectiveAnchor);
            popup.Label.Visible = anchor is not null;
            if (anchor is not { } position) continue;
            var slot = anchorSlots.GetValueOrDefault(effectiveAnchor);
            anchorSlots[effectiveAnchor] = slot + 1;
            var y = position.Y - 30 - (float)popup.Age * 30 - slot * 32;
            popup.Label.Position = new Vector2(Mathf.Clamp(position.X - 80, 4, Math.Max(4, viewport.X - 164)), Mathf.Clamp(y, 4, Math.Max(4, viewport.Y - 46)));
            popup.Label.Modulate = new Color(1, 1, 1, 1 - (float)popup.Age / 2);
        }
    }
}

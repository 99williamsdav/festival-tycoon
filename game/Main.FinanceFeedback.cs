using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private CashPopups? _cashPopupView;
    private CashPopups CashPopups => _cashPopupView ??= new(this, FinanceFeedbackAnchor, FinanceFeedbackScreenAnchor);

    private void ResetFinanceFeedback() => CashPopups.Reset(_session);
    private void AdvanceFinanceFeedback(double delta) => CashPopups.Advance(_session, delta);



    private string FinanceFeedbackAnchor(string key)
    {
        if (key is "vendor.food" or "vendor.drinks") return key;
        if (key == "stock") return FinanceFeedbackControlVisible(_immersionStockButton) ? key : "preparation";
        if (key == "programme") return FinanceFeedbackControlVisible(_programmeBook) ? key : "preparation";
        if (key.StartsWith("offer:", StringComparison.Ordinal))
        {
            var offer = key[6..];
            if (_offerButtons.TryGetValue(offer, out var button)) return FinanceFeedbackControlVisible(button) ? key : "preparation";
            if (offer.StartsWith("act.", StringComparison.Ordinal) && FinanceFeedbackControlVisible(_programmeBook)) return "programme";
        }
        return "preparation";
    }

    private bool FinanceFeedbackControlVisible(Control? control)
    {
        if (control is null || !control.IsVisibleInTree()) return false;
        var rect = control.GetGlobalRect();
        if (!GetViewport().GetVisibleRect().Encloses(rect)) return false;
        // IsVisibleInTree does not account for controls scrolled beneath a clip.
        // Resolve this before grouping so hidden offers share one panel anchor.
        for (Node? ancestor = control.GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
            if (ancestor is Control clip && (clip is ScrollContainer || clip.ClipContents) && !clip.GetGlobalRect().Encloses(rect))
                return false;
        return true;
    }

    private Vector2? FinanceFeedbackScreenAnchor(string key)
    {
        if (key.StartsWith("vendor.", StringComparison.Ordinal) && _immersionVendors.TryGetValue(key[7..], out var vendor))
        {
            var point = vendor.GlobalPosition + new Vector3(0, 2.8f, 0);
            if (_camera.IsPositionBehind(point)) return null;
            var screen = _camera.UnprojectPosition(point);
            return GetViewport().GetVisibleRect().HasPoint(screen) ? screen : null;
        }
        Control? control = key switch
        {
            "stock" => _immersionStockButton,
            "programme" => _programmeBook,
            _ => key.StartsWith("offer:", StringComparison.Ordinal) && _offerButtons.TryGetValue(key[6..], out var button) ? button : _hudMoney ?? _preparationSummary
        };
        if (control is null) return null;
        // Paid offers can disappear when opening. The finance summary remains
        // the relevant panel anchor when the original control is no longer shown.
        if (!FinanceFeedbackControlVisible(control)) control = _hudMoney ?? _preparationSummary;
        var rect = control.GetGlobalRect();
        if (control == _hudMoney)
            // The flush bar has no room above it for drift/stacking. Reserve
            // that cosmetic space immediately below its actual money chip.
            return new Vector2(rect.Position.X + rect.Size.X / 2, rect.End.Y + 110);
        if (control == _preparationSummary)
        {
            // Keep the shared expense anchor below the panel heading so its
            // two-second drift has room above it. If the summary itself has
            // scrolled out, use the visible preparation panel at the same depth.
            if (!FinanceFeedbackControlVisible(control))
                for (Node? ancestor = control.GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
                    if (ancestor is ScrollContainer scroll) { rect = scroll.GetGlobalRect(); break; }
            return new Vector2(rect.Position.X + rect.Size.X / 2, rect.Position.Y + Math.Min(100, rect.Size.Y - 12));
        }
        return new Vector2(rect.Position.X + rect.Size.X / 2, rect.Position.Y + Math.Min(24, rect.Size.Y / 2));
    }






}

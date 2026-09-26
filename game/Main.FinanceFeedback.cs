using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private readonly FestivalCashFeedbackCursor _financeFeedbackCursor = new();
    private readonly List<CashPopup> _cashPopups = [];
    private readonly Dictionary<(string Anchor, bool Credit), long> _cashPopupPending = [];
    private CanvasLayer? _cashPopupLayer;
    private long _cashFeedbackCredits;
    private long _cashFeedbackExpenses;
    private string? _financeCaptureDirectory;
    private int _financeCaptureStep;
    private double _financeCaptureElapsed;
    private long _financeCaptureSalesTick;
    private long _financeCaptureObservedCredit;
    private float _financeCaptureEarlyAlpha;
    private float _financeCaptureEarlyY;
    private string _financeCaptureAnimationHash = "";

    private sealed class CashPopup(string anchor, long pennies, Label label)
    {
        public string Anchor = anchor;
        public long Pennies = pennies;
        public Label Label = label;
        public double Age;
    }

    private void ResetFinanceFeedback()
    {
        _financeFeedbackCursor.Reset(_session.CaptureFestivalCashFeedbackEvents());
        foreach (var popup in _cashPopups) popup.Label.QueueFree();
        _cashPopups.Clear(); _cashPopupPending.Clear();
    }

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
            _ => key.StartsWith("offer:", StringComparison.Ordinal) && _offerButtons.TryGetValue(key[6..], out var button) ? button : _preparationSummary
        };
        if (control is null) return null;
        // Paid offers can disappear when opening. The finance summary remains
        // the relevant panel anchor when the original control is no longer shown.
        if (!FinanceFeedbackControlVisible(control)) control = _preparationSummary;
        var rect = control.GetGlobalRect();
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

    private static string CashPopupText(long pennies) => FestivalCurrency.Format(pennies, signed: true);

    private void AdvanceFinanceFeedback(double delta)
    {
        foreach (var popup in _cashPopups.ToArray())
        {
            popup.Age += delta;
            if (popup.Age >= 2) { popup.Label.QueueFree(); _cashPopups.Remove(popup); }
        }
        foreach (var item in _financeFeedbackCursor.Observe(_session.CaptureFestivalCashFeedbackEvents()))
        {
            if (item.FestivalCashPennies > 0) _cashFeedbackCredits += item.FestivalCashPennies;
            else _cashFeedbackExpenses += item.FestivalCashPennies;
            var anchor = FinanceFeedbackAnchor(item.AnchorKey);
            var credit = item.FestivalCashPennies > 0;
            var recent = _cashPopups.FirstOrDefault(p => p.Anchor == anchor && (p.Pennies > 0) == credit && p.Age < .2);
            if (recent is not null) recent.Pennies += item.FestivalCashPennies;
            else
            {
                var key = (anchor, credit);
                _cashPopupPending[key] = _cashPopupPending.GetValueOrDefault(key) + item.FestivalCashPennies;
            }
            if (_financeCaptureDirectory is not null)
                GD.Print($"FINANCE_FEEDBACK transaction={item.TransactionId} cash_pennies={item.FestivalCashPennies} anchor={anchor}");
        }
        // Overflow remains aggregated by the finite visible control/vendor keys
        // and sign, then drains when a display slot expires. No cash is discarded
        // and opposite signs never cancel each other.
        foreach (var entry in _cashPopupPending.ToArray())
        {
            if (_cashPopups.Count >= 8) break;
            _cashPopupLayer ??= new CanvasLayer { Layer = 30 };
            if (!_cashPopupLayer.IsInsideTree()) AddChild(_cashPopupLayer);
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
        var viewport = GetViewport().GetVisibleRect().Size;
        var anchorSlots = new Dictionary<string, int>();
        foreach (var popup in _cashPopups)
        {
            popup.Label.Text = CashPopupText(popup.Pennies);
            // Scrolling can change the visible anchor after a popup was created.
            // Stack at the resolved location, retaining each original cash sum.
            var effectiveAnchor = FinanceFeedbackAnchor(popup.Anchor);
            var anchor = FinanceFeedbackScreenAnchor(effectiveAnchor);
            popup.Label.Visible = anchor is not null;
            if (anchor is not { } position) continue;
            var slot = anchorSlots.GetValueOrDefault(effectiveAnchor);
            anchorSlots[effectiveAnchor] = slot + 1;
            var y = position.Y - 30 - (float)popup.Age * 30 - slot * 32;
            popup.Label.Position = new Vector2(Mathf.Clamp(position.X - 80, 4, Math.Max(4, viewport.X - 164)), Mathf.Clamp(y, 4, Math.Max(4, viewport.Y - 46)));
            popup.Label.Modulate = new Color(1, 1, 1, 1 - (float)popup.Age / 2);
        }
    }

    private void FinanceCaptureImage(string name) => GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_financeCaptureDirectory!, name + ".png"));

    private void ProcessFinanceFeedbackCapture(double delta)
    {
        if (_financeCaptureDirectory is null) return;
        _financeCaptureElapsed += delta;
        try
        {
            if (_financeCaptureStep == 0)
            {
                _session.Execute(CampaignEnvelope(new SetPausedCommand(true)));
                _immersionStockButton!.EmitSignal(Button.SignalName.Pressed);
                if (!_session.CaptureImmersion()!.StockPurchased) throw new InvalidOperationException("Actual stock command failed.");
                _financeCaptureStep = 1; _financeCaptureElapsed = 0;
            }
            else if (_financeCaptureStep == 1 && _financeCaptureElapsed >= .25)
            {
                if (_cashPopups.Single().Pennies != -9600 || _cashFeedbackExpenses != -9600 || _cashPopups.Single().Label.Text != "−£96") throw new InvalidOperationException("Stock expense feedback missing, duplicated or incorrectly formatted.");
                CaptureFinanceAnimationBaseline(_cashPopups.Single());
                FinanceCaptureImage("01-stock-expense-early"); _financeCaptureStep = 2;
            }
            else if (_financeCaptureStep == 2 && _financeCaptureElapsed >= 1)
            { AssertFinanceAnimation(_cashPopups.Single()); FinanceCaptureImage("02-stock-expense-mid"); _financeCaptureStep = 3; }
            else if (_financeCaptureStep == 3 && _financeCaptureElapsed >= 2.3)
            {
                if (_cashPopups.Count != 0) throw new InvalidOperationException("Expense popup did not expire.");
                AssertFinanceAnimationHash();
                FinanceCaptureImage("03-stock-expense-expired");
                PreparationSave(); PreparationLoad();
                if (_cashPopups.Count != 0 || _cashPopupPending.Count != 0) throw new InvalidOperationException("Load retained feedback.");
                _programmeDraft = ["act.meadow-lanterns", "act.neon-postcards", "act.field-frequency"];
                RefreshProgrammeControls(); _programmeBook!.EmitSignal(Button.SignalName.Pressed);
                foreach (var id in new[] { "staff.steward", "equipment.buy" }) _offerButtons[id].EmitSignal(Button.SignalName.Pressed);
                _financeCaptureStep = 4; _financeCaptureElapsed = 0;
            }
            else if (_financeCaptureStep == 4 && _financeCaptureElapsed >= .25)
            {
                var expected = _session.CaptureFestivalCashFeedbackEvents().Where(e => e.AnchorKey.StartsWith("offer:act.", StringComparison.Ordinal)).Sum(e => e.FestivalCashPennies);
                if (_cashPopups.Single(p => p.Anchor == "programme").Pennies != expected) throw new InvalidOperationException("Three paid acts did not aggregate at booking control.");
                var paidOffers = _session.CaptureFestivalCashFeedbackEvents().Where(e => e.AnchorKey.StartsWith("offer:", StringComparison.Ordinal)).Sum(e => e.FestivalCashPennies);
                if (_cashPopups.Single(p => p.Anchor == "preparation").Pennies != paidOffers - expected ||
                    _cashPopups.Sum(p => p.Pennies) != paidOffers || _cashPopups.Any(p => p.Label.Position.Y >= GetViewport().GetVisibleRect().Size.Y - 100))
                    throw new InvalidOperationException("Clipped staff/equipment expenses did not aggregate at the preparation summary.");
                FinanceCaptureImage("04-programme-expense-aggregated"); _financeCaptureStep = 10;
            }
            else if (_financeCaptureStep == 10 && _financeCaptureElapsed >= 2.3)
            {
                PreparationStart(); _financeCaptureStep = 5;
            }
            else if (_financeCaptureStep == 5)
            {
                if (_financeCaptureSalesTick == 0)
                {
                    SelectAttendee(_session.CaptureObservation().NavigationAgents.First().Id); AssertContextPanel(true);
                    ClearSelection(); AssertContextPanel(false);
                }
                _financeCaptureSalesTick += 160;
                TimetableAdvanceTo(_financeCaptureSalesTick);
                if (_session.CaptureImmersion()!.Purchases.Length > 0)
                { _financeCaptureStep = 6; _financeCaptureElapsed = 0; }
                else if (_financeCaptureSalesTick >= 6400) throw new InvalidOperationException("No ordinary sale within 80 simulated seconds.");
            }
            else if (_financeCaptureStep == 6 && _financeCaptureElapsed >= .25)
            {
                _financeCaptureObservedCredit = _session.CaptureImmersion()!.Purchases.Sum(p => (long)p.PricePennies);
                if (_cashFeedbackCredits != _financeCaptureObservedCredit || !_cashPopups.Any(p => p.Pennies > 0) ||
                    _cashPopups.Where(p => p.Pennies > 0).Any(p => p.Label.Text != FestivalCurrency.Format(p.Pennies, signed: true))) throw new InvalidOperationException("Sale credit missing, duplicated or incorrectly formatted.");
                CaptureFinanceAnimationBaseline(_cashPopups.First(p => p.Pennies > 0));
                FinanceCaptureImage("04-vendor-sale-early"); _financeCaptureStep = 7;
            }
            else if (_financeCaptureStep == 7 && _financeCaptureElapsed >= 1)
            { AssertFinanceAnimation(_cashPopups.First(p => p.Pennies > 0)); FinanceCaptureImage("05-vendor-sale-mid"); _financeCaptureStep = 8; }
            else if (_financeCaptureStep == 8 && _financeCaptureElapsed >= 2.3)
            {
                if (_cashPopups.Count != 0) throw new InvalidOperationException("Sale popup did not expire.");
                AssertFinanceAnimationHash();
                FinanceCaptureImage("06-vendor-sale-expired");
                PreparationSave(); PreparationLoad(); _financeCaptureStep = 9; _financeCaptureElapsed = 0;
            }
            else if (_financeCaptureStep == 9 && _financeCaptureElapsed >= .3)
            {
                if (_cashPopups.Count != 0 || _cashFeedbackCredits != _financeCaptureObservedCredit) throw new InvalidOperationException("Historical sales replayed after load.");
                FinanceCaptureImage("07-loaded-no-replay");
                AssertFinanceHiddenControlStack();
                // Labelled cosmetic capacity fixture: these are display-only
                // amounts, never authoritative cash, ledger or cursor events.
                _financeCaptureAnimationHash = _session.CaptureSnapshot().AuthoritativeHash;
                for (var index = 0; index < 10; index++) _cashPopupPending[("fixture:" + index, true)] = 100;
                _financeCaptureStep = 11; _financeCaptureElapsed = 0;
            }
            else if (_financeCaptureStep == 11 && _financeCaptureElapsed >= .1)
            {
                if (_cashPopups.Count != 8 || _cashPopupPending.Count != 2 || _cashPopups.Sum(p => p.Pennies) + _cashPopupPending.Values.Sum() != 1000)
                    throw new InvalidOperationException("Labelled cosmetic capacity fixture lost overflow amounts.");
                _financeCaptureStep = 12;
            }
            else if (_financeCaptureStep == 12 && _financeCaptureElapsed >= 2.2)
            {
                if (_cashPopups.Count != 2 || _cashPopupPending.Count != 0 || _cashPopups.Sum(p => p.Pennies) != 200)
                    throw new InvalidOperationException("Cosmetic overflow did not drain into bounded visible slots.");
                AssertFinanceAnimationHash();
                GD.Print($"FINANCE_CAPTURE_COMPLETE actual_expense=-9600 actual_sale_credit={_financeCaptureObservedCredit} dedup=True load_replay=False early_mid_expired=True alpha_drift_hash=True actual_programme_aggregation=True hidden_control_stack=True labelled_cosmetic_capacity=True max_visible=8 overflow_loss=False");
                GetTree().Quit();
            }
        }
        catch (Exception error) { GD.PushError("FINANCE_CAPTURE_FAILED " + error); GetTree().Quit(1); }
    }

    private void CaptureFinanceAnimationBaseline(CashPopup popup)
    {
        _financeCaptureEarlyAlpha = popup.Label.Modulate.A;
        _financeCaptureEarlyY = popup.Label.Position.Y;
        _financeCaptureAnimationHash = _session.CaptureSnapshot().AuthoritativeHash;
    }

    private void AssertFinanceHiddenControlStack()
    {
        // Labelled cosmetic fixture representing two popups whose original
        // offer controls become hidden after creation. No ledger/cursor input.
        var hash = _session.CaptureSnapshot().AuthoritativeHash;
        var controls = new[] { _offerButtons["staff.steward"], _offerButtons["equipment.buy"] };
        var oldVisibility = controls.Select(control => control.Visible).ToArray();
        foreach (var control in controls) control.Visible = true;
        _cashPopupPending[("offer:staff.steward", false)] = -100;
        _cashPopupPending[("offer:equipment.buy", false)] = -200;
        AdvanceFinanceFeedback(0);
        foreach (var control in controls) control.Visible = false;
        AdvanceFinanceFeedback(0);
        if (_cashPopups.Count != 2 || Math.Abs(Math.Abs(_cashPopups[0].Label.Position.Y - _cashPopups[1].Label.Position.Y) - 32) > .01 ||
            _cashPopups.Sum(popup => popup.Pennies) != -300 || _session.CaptureSnapshot().AuthoritativeHash != hash)
            throw new InvalidOperationException("Previously separate offer popups overlap after their controls become hidden.");
        for (var index = 0; index < controls.Length; index++) controls[index].Visible = oldVisibility[index];
        foreach (var popup in _cashPopups) popup.Label.QueueFree();
        _cashPopups.Clear();
    }

    private void AssertFinanceAnimationHash()
    {
        if (_session.CaptureSnapshot().AuthoritativeHash != _financeCaptureAnimationHash)
            throw new InvalidOperationException("Paused cosmetic animation changed authoritative hash.");
    }

    private void AssertFinanceAnimation(CashPopup popup)
    {
        if (popup.Label.Modulate.A >= _financeCaptureEarlyAlpha || popup.Label.Position.Y >= _financeCaptureEarlyY)
            throw new InvalidOperationException("Cash popup did not visibly drift and fade.");
        AssertFinanceAnimationHash();
    }
}

using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private ResultsPaper? _resultsPaperView;
    private ResultsPaper ResultsPaper => _resultsPaperView ??= new(this);

    private void RefreshFestivalPaper()
    {
        if (_newCampaignOnEnter) return;
        if (_session.CompletedFestivalResult is null)
        {
            ResultsPaper.Close();
            if (_session.PreparedStatus == PreparationStatus.Departing)
                _preparationSummary.Text += $"\nFestival finished · Guests leaving: {_session.CapturePreparation()!.People.Count(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed)}";
            return;
        }
        if (ResultsPaper.IsOpen) return;
        ResetFinanceFeedback();
        ResultsPaper.Open(this, () =>
        {
            if (!SaveMilestone("Return to menu")) return false;
            _newCampaignOnEnter = true; ResultsPaper.Close(); BuildStartSplash();
            return true;
        }, _session.CanStartNextFestival ? StartNextFestival : null);
    }

    /// <summary>The paper's "Next festival": save the completed festival, then open the next tier's preparation.</summary>
    private bool StartNextFestival()
    {
        if (!_session.CanStartNextFestival || !SaveMilestone("Next festival")) return false;
        var next = _session.CreateNextFestival();
        ResultsPaper.Close();
        SwitchToFestival(next);
        var p = _session.CapturePreparation()!;
        _preparationMessage = $"Tier {p.Tier}: {FestivalTickets.Sold(p.Tier)} guests at {FestivalCurrency.Format(FestivalTickets.PricePennies(p.Tier))}. " +
            $"{FestivalCurrency.Format(p.CarriedIn!.CashPennies)} carried forward; {FestivalCurrency.Format(p.CarriedIn.DebtPennies)} of loan still owed.";
        RefreshPreparationHud();
        GD.Print($"NEXT_FESTIVAL_STARTED id={_session.CampaignId.Value} tier={p.Tier} cash={p.OpeningCashPennies} debt={p.CarriedIn.DebtPennies}");
        return true;
    }
}

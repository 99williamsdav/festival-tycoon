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
        });
    }
}

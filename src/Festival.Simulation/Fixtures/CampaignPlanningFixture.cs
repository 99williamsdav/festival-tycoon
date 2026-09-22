namespace Festival.Simulation.Fixtures;

/// <summary>Development-only factory for financial failure-path verification.</summary>
public static class CampaignPlanningFixture
{
    public static GameSession CreateWithOpeningCash(long openingCashPennies, ulong seed = 20260922)
    {
        if (openingCashPennies < 0) throw new ArgumentOutOfRangeException(nameof(openingCashPennies));
        var session = GameSession.CreateCampaign(seed);
        var owner = session.CampaignPlanningState!.FinanceOwnerId;
        session.FestivalFinances[owner].CashPennies = openingCashPennies;
        return session;
    }
}

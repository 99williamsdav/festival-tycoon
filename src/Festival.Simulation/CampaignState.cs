namespace Festival.Simulation;

/// <summary>
/// Derived view of existing campaign holdings; no duplicate authoritative store or speculative land/ownership.
/// Stored act relationships/popularity/history are those this festival opened with; its new gigs settle at transition.
/// Bookings, hires, stock, rentals, layout and current performance remain the per-festival Preparation/programme state.
/// </summary>
public sealed record PersistentCampaignSnapshot(CampaignId CampaignId,ulong Seed,CampaignIdentity Identity,string FestivalName,
    FestivalPalette Palette,string SiteId,ulong SiteSeed,long CashPennies,LoanSnapshot Loan,int FavourBalance,
    bool CommunityWaterShareUsed,FestivalStanding Standing,string[] SeenActs,ActRelationship[] ActRelationships,
    PopularityShift[] ActPopularity,CarriedGig[] EarlierGigs,string[] OwnedEquipment);

public sealed partial class GameSession
{
    private CampaignRulesProfile RuleProfile=>CampaignRulesProfile.For(_identity.Ruleset);
    public IncidentTimingParameters IncidentTiming=>RuleProfile.Incidents;
    private static IncidentTimingParameters SavedIncidentTiming(SessionPersistenceSnapshot s)=>CampaignRulesProfile.For(s.Identity!.Ruleset).Incidents;
    public PersistentCampaignSnapshot? CapturePersistentCampaignState()
    {
        if(_preparation is not {} p||_campaignPlanning is not {} planning)return null;
        return new(CampaignId,CampaignSeed,_identity,planning.FestivalName,planning.Palette,planning.SiteId,planning.SiteSeed,
            _festivalFinances[new(p.FinanceOwnerId)].CashPennies,planning.Loan,_lifecycle?.FavourBalance??p.CarriedIn?.FavourBalance??1,
            p.CommunityShareAttempt!=0||p.CarriedIn?.CommunityWaterUsed==true,new(p.Reputation,p.SceneCredibility.ToArray()),
            p.SeenActs.ToArray(),p.ActRelationships?.ToArray()??[],p.ActPopularity?.ToArray()??[],
            p.RelationshipHistory?.Select(gig=>gig with{Hiccups=gig.Hiccups.ToArray()}).ToArray()??[],p.OwnedEquipment.ToArray());
    }
}

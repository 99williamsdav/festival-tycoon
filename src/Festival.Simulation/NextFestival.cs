namespace Festival.Simulation;

public sealed partial class GameSession
{
    /// <summary>The highest tier with its parameters set; a festival there has no next festival yet.</summary>
    public const int HighestTier = 2;

    /// <summary>
    /// Survival clearing rule (default, unconfirmed with the user): a festival that completes, with every guest
    /// gone home and no death or Council ending, unlocks the next tier. A failed festival keeps the hearing and retry.
    /// </summary>
    public bool CanStartNextFestival => _preparation is { Status: PreparationStatus.Finished, Result: not null } p &&
        p.Tier < HighestTier && !IsLifecycleEditionFrozen() && _campaignPlanning is not null;

    /// <summary>What this completed festival would carry into the next one.</summary>
    public FestivalCarryOver? NextFestivalCarryOver => !CanStartNextFestival ? null : CarryOverFrom(_preparation!);

    private FestivalCarryOver CarryOverFrom(PreparationSnapshot p) => new(p.Tier,
        _festivalFinances[new(p.FinanceOwnerId)].CashPennies,
        _campaignPlanning!.Loan.OutstandingPrincipalPennies,
        _lifecycle?.FavourBalance ?? p.CarriedIn?.FavourBalance ?? 1,
        p.CommunityShareAttempt != 0 || p.CarriedIn?.CommunityWaterUsed == true,
        p.OwnedEquipment.ToArray());

    /// <summary>
    /// The "Next festival" step: a fresh Preparation at the next tier on the same farm and campaign. Cash, debt,
    /// Council Favour, the spent water share, owned kit, reputation, scene credibility, seen acts and the festival's
    /// name and colours come forward; perks, staff, bookings, stock and the build start fresh.
    /// </summary>
    public GameSession CreateNextFestival()
    {
        if (!CanStartNextFestival) throw new InvalidOperationException("Only a completed festival below the highest tier can open the next one.");
        var p = _preparation!;
        return CreateLaterFestival(CampaignSeed, CampaignId, p.Tier + 1, CarryOverFrom(p),
            new FestivalStanding(p.Reputation, p.SceneCredibility.ToArray()), p.SeenActs, _campaignPlanning!);
    }

    private static GameSession CreateLaterFestival(ulong seed, CampaignId campaignId, int tier, FestivalCarryOver carry,
        FestivalStanding standing, string[] seenActs, CampaignPlanningState? identity)
    {
        var next = CreateBuildCampaign(seed, tier, carry, campaignId);
        next.PreparationView = next.PreparationView! with { Reputation = standing.Reputation,
            SceneCredibility = standing.SceneCredibility.ToArray(), SeenActs = seenActs.ToArray() };
        if (identity is not null)
        {
            var planning = next._campaignPlanning!;
            planning.FestivalName = identity.FestivalName;
            planning.Palette = identity.Palette;
            foreach (var tip in identity.DismissedTipIds) planning.DismissedTipIds.Add(tip);
        }
        return next;
    }

    /// <summary>
    /// Development and playtest route only, never a menu option: a campaign at <paramref name="tier"/> as if each
    /// tier below had just been completed, with a plausible carried state (a typical Tier 1 close, the starter loan
    /// still owed, the starting Favour, a three-star festival's standing).
    /// </summary>
    public static GameSession CreateDevelopmentFestival(ulong seed, int tier)
    {
        if (tier is < 1 or > HighestTier) throw new ArgumentOutOfRangeException(nameof(tier), $"Tiers run 1–{HighestTier}.");
        if (tier == 1) return CreateBuildCampaign(seed);
        var carry = DevelopmentCarryOver(tier);
        // A three-star Tier 1 in the folk, indie and pop scenes.
        var standing = ActCatalogue.AfterFestival(FestivalStanding.New, 3, [FestivalGenre.Folk, FestivalGenre.Indie, FestivalGenre.Pop]);
        return CreateLaterFestival(seed, new CampaignId(seed), tier, carry, standing, [], null);
    }

    /// <summary>
    /// A typical Tier 1 close: headless default-layout days with cheap acts closed on £235–£322 of the £450 they opened
    /// with, so £300. The starter loan is still owed.
    /// </summary>
    public static FestivalCarryOver DevelopmentCarryOver(int tier) =>
        new(tier - 1, 30_000, CampaignDefaults.OpeningLoanPrincipalPennies, 1, false, []);
}

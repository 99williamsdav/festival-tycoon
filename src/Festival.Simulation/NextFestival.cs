namespace Festival.Simulation;

public sealed partial class GameSession
{
    /// <summary>The highest tier with its parameters set; a festival there has no next festival yet.</summary>
    public const int HighestTier = 2;
    /// <summary>The tier from which a festival has a second medic slot without the Doctor's Orders perk (it isn't dealt there).</summary>
    public const int FreeExtraMedicFromTier = 2;
    /// <summary>The kit a festival can own and so carry forward: the bought sound rig.</summary>
    public static readonly string[] CarriableKit = ["sound-rig"];
    // Spending money a person brings (see NewImmersionPerson): a guest at most £25, crew and band £15.
    internal const int MostGuestBudgetPennies = 2_500, CrewBudgetPennies = 1_500;

    /// <summary>
    /// The most cash a festival opening on <paramref name="openingCashPennies"/> can close on: the bar only takes money
    /// people brought, so at most every guest's top budget and every other person's spending money (up to the people
    /// cap), plus the dearest food trader's pitch fee. Nothing else pays in; tickets are already in the opening cash.
    /// </summary>
    public static long MostClosingCashPennies(int tier, long openingCashPennies) =>
        openingCashPennies + FoodTraders.All.Max(trader => (long)trader.PitchFeePennies) +
        (long)FestivalTickets.Sold(tier) * MostGuestBudgetPennies + (long)(MaxActivePeople - FestivalTickets.Sold(tier)) * CrewBudgetPennies;

    /// <summary>
    /// The least: opening is refused past the overdraft, and Dav and free water are refused once they'd go past it,
    /// so no festival closes further in the red than the overdraft.
    /// </summary>
    public const long LeastClosingCashPennies = -CampaignDefaults.OverdraftPennies;

    /// <summary>Why a carried state couldn't have come from a real close of the tier below; null if it could.</summary>
    private static string? CarryIssue(FestivalCarryOver carry, int tier)
    {
        if (carry.FromTier != tier - 1 || carry.FromTier != 1)
            return "Only Tier 1 can carry into Tier 2 for now.";
        // Settlement is parked, so the starter loan's principal is owed in full at every later festival.
        if (carry.DebtPennies != CampaignDefaults.OpeningLoanPrincipalPennies) return "The loan still owed must be the starter loan.";
        if (carry.CashPennies < LeastClosingCashPennies || carry.CashPennies > MostClosingCashPennies(carry.FromTier, CampaignDefaults.OpeningCashPennies))
            return "Carried cash is more than Tier 1 could close on, or further into the red than the overdraft.";
        // One starting Favour, plus one if the water share was honoured; never more.
        if (carry.FavourBalance < 0 || carry.FavourBalance > 1 + (carry.CommunityWaterUsed ? 1 : 0)) return "Carried Favour is more than was ever granted.";
        if (carry.OwnedEquipment is null || carry.OwnedEquipment.Any(id => !CarriableKit.Contains(id)) ||
            !carry.OwnedEquipment.SequenceEqual(carry.OwnedEquipment.Distinct().Order(StringComparer.Ordinal)))
            return "Carried kit must be known, sorted and unique.";
        return null;
    }

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
            new FestivalStanding(p.Reputation, p.SceneCredibility.ToArray()), p.SeenActs, _campaignPlanning!, _pondStageTrial);
    }

    private static GameSession CreateLaterFestival(ulong seed, CampaignId campaignId, int tier, FestivalCarryOver carry,
        FestivalStanding standing, string[] seenActs, CampaignPlanningState? identity, bool pondStageTrial)
    {
        // A Pond Stage trial campaign stays one at the next tier.
        var next = CreateBuildCampaign(seed, tier, carry, campaignId, pondStageTrial: pondStageTrial);
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
    public static GameSession CreateDevelopmentFestival(ulong seed, int tier, bool pondStageTrial = false)
    {
        if (tier is < 1 or > HighestTier) throw new ArgumentOutOfRangeException(nameof(tier), $"Tiers run 1–{HighestTier}.");
        if (tier == 1) return CreateBuildCampaign(seed, pondStageTrial);
        var carry = DevelopmentCarryOver(tier);
        // A three-star Tier 1 in the folk, indie and pop scenes.
        var standing = ActCatalogue.AfterFestival(FestivalStanding.New, 3, [FestivalGenre.Folk, FestivalGenre.Indie, FestivalGenre.Pop]);
        return CreateLaterFestival(seed, new CampaignId(seed), tier, carry, standing, [], null, pondStageTrial);
    }

    /// <summary>
    /// A typical Tier 1 close: headless default-layout days with cheap acts closed on £235–£322 of the £450 they opened
    /// with, so £300. The starter loan is still owed.
    /// </summary>
    public static FestivalCarryOver DevelopmentCarryOver(int tier) =>
        new(tier - 1, 30_000, CampaignDefaults.OpeningLoanPrincipalPennies, 1, false, []);
}

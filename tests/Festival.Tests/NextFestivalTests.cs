using System.Reflection;
using Festival.Persistence;
using Festival.Simulation;

namespace Festival.Tests;

// Tier 2: the "Next festival" step after a completed Tier 1, carried cash and debt, and Tier 2's parameters.
[TestClass]
public sealed class NextFestivalTests
{
    /// <summary>A festival in preparation with a quiet perk, the default layout, three affordable acts, crew and stock.</summary>
    internal static GameSession Ready(GameSession s)
    {
        Drafted(s);
        var acts = s.GetFestivalActs().Where(act => s.ActStandingOf(act) == ActStanding.Available)
            .OrderBy(act => s.ActFee(act)).ThenBy(act => act.Id, StringComparer.Ordinal).Take(3).Select(act => act.Id).ToArray();
        Assert.AreEqual(3, acts.Length, "Three acts should play for the festival.");
        BuildSession.Accept(s, new SetProgrammeCommand(acts));
        BuildSession.Accept(s, new SetPreparationStockCommand(40, 32));
        foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        return s;
    }

    /// <summary>A quiet perk (a hiring slot no crew fills) drafted and the default layout placed.</summary>
    internal static GameSession Drafted(GameSession s)
    {
        var redraw = typeof(GameSession).GetMethod("OpenPerkDraft", BindingFlags.NonPublic | BindingFlags.Instance)!;
        string[] quiet = ["extra-pair-of-hands", "doctors-orders"];
        var perks = s.CapturePerks()!;
        for (var draws = 0; !perks.Hand.Any(quiet.Contains); draws++)
        {
            Assert.IsTrue(draws < 400, "No quiet perk turned up.");
            redraw.Invoke(s, []); perks = s.CapturePerks()!;
        }
        BuildSession.Accept(s, new ChoosePerkCommand(perks.DraftAttempt, perks.Cursor, perks.Hand.First(quiet.Contains)));
        BuildSession.Accept(s, new UseDefaultBuildLayoutCommand());
        return s;
    }

    /// <summary>
    /// Tier 2's second tap and third toilet, at the first sites that fit. The default layout alone (one tap, one medic)
    /// lost a guest on each of three probed Tier 2 days; with these all three finished.
    /// </summary>
    internal static GameSession WithTierTwoExtras(GameSession s)
    {
        foreach (var kind in new[] { BuildServiceKind.WaterTap, BuildServiceKind.Toilet })
            _ = (from x in Enumerable.Range(0, 23) from z in Enumerable.Range(0, 20) select new GridCell(100 + x * 4, 112 + z * 4))
                .First(cell => BuildSession.Send(s, new PlaceBuildServiceCommand(kind, cell)).IsAccepted);
        return s;
    }

    private static GameSession TierTwo(ulong seed = 20260922) => GameSession.CreateDevelopmentFestival(seed, 2);

    private static void RunToEnd(GameSession s)
    {
        for (var i = 0; i < 80 && s.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing; i++) s.AdvanceWithoutSnapshot(1_000);
    }

    [TestMethod]
    public void BuildLimitsGoUpByOneTapAndToiletAtTierTwo()
    {
        Assert.AreEqual(new TierBuildLimits(1, 2, 1, 1, 1, 1), GameSession.BuildLimits(1));
        // TODO(multi-vendor): bars and food vans become 2 here once vendors are no longer the single "food"/"drinks".
        Assert.AreEqual(new TierBuildLimits(2, 3, 1, 1, 1, 1), GameSession.BuildLimits(2));
        Assert.AreEqual(2, GameSession.BuildServiceLimit(BuildServiceKind.WaterTap, 1), "Tier 1: one tap, two with Another Round.");
        Assert.AreEqual(3, GameSession.BuildServiceLimit(BuildServiceKind.WaterTap, 2), "Tier 2: two taps, three with Another Round.");

        var one = GameSession.CreateBuildCampaign(20260922);
        var two = TierTwo();
        Assert.AreEqual(1, one.ServiceLimit(BuildServiceKind.WaterTap));
        Assert.AreEqual(2, two.ServiceLimit(BuildServiceKind.WaterTap));
        Assert.AreEqual(2, one.ServiceLimit(BuildServiceKind.Toilet));
        Assert.AreEqual(3, two.ServiceLimit(BuildServiceKind.Toilet));
        Assert.AreEqual(1, two.ServiceLimit(BuildServiceKind.Bar));
        Assert.AreEqual(1, two.ServiceLimit(BuildServiceKind.FoodVan));

        // A third toilet and a second tap stand at Tier 2 only, and save and restore.
        foreach (var s in new[] { one, two }) Drafted(s);
        GridCell? toiletSite = null, tapSite = null;
        for (var x = 145; x <= 190 && (toiletSite is null || tapSite is null); x += 5)
        for (var z = 115; z <= 190 && (toiletSite is null || tapSite is null); z += 5)
        {
            var cell = new GridCell(x, z);
            if (toiletSite is null && two.ValidateCommand(Envelope(two, new PlaceBuildServiceCommand(BuildServiceKind.Toilet, cell))) is null) toiletSite = cell;
            else if (tapSite is null && two.ValidateCommand(Envelope(two, new PlaceBuildServiceCommand(BuildServiceKind.WaterTap, cell))) is null) tapSite = cell;
        }
        Assert.IsNotNull(toiletSite); Assert.IsNotNull(tapSite);
        Assert.IsFalse(BuildSession.Send(one, new PlaceBuildServiceCommand(BuildServiceKind.Toilet, toiletSite.Value)).IsAccepted);
        Assert.IsFalse(BuildSession.Send(one, new PlaceBuildServiceCommand(BuildServiceKind.WaterTap, tapSite.Value)).IsAccepted);
        BuildSession.Accept(two, new PlaceBuildServiceCommand(BuildServiceKind.Toilet, toiletSite.Value));
        BuildSession.Accept(two, new PlaceBuildServiceCommand(BuildServiceKind.WaterTap, tapSite.Value));
        CollectionAssert.IsSubsetOf(new[] { "toilet.extra-2", "water.extra-1" }, two.CaptureBuildPlacements().Select(p => p.Id).ToArray());
        Assert.IsFalse(BuildSession.Send(two, new PlaceBuildServiceCommand(BuildServiceKind.Toilet, new(160, 180))).IsAccepted, "Three toilets is the Tier 2 limit.");
        Assert.AreEqual(3, BuildSession.Restored(two).CaptureToilets().Count);
    }

    private static CommandEnvelope Envelope(GameSession s, SessionCommand command) =>
        new(new(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase, s.CurrentTick, s.NextSubmissionSequence, null, command);

    [TestMethod]
    public void TierTwoSellsFiftyTicketsAtFifteenPounds()
    {
        Assert.AreEqual(1_500, FestivalTickets.PricePennies(2));
        Assert.AreEqual(50, FestivalTickets.Sold(2));
        Assert.AreEqual(75_000L, FestivalTickets.RevenuePennies(2));
        // Tier 1's books still open on its tickets plus the starter loan.
        Assert.AreEqual(CampaignDefaults.OpeningCashPennies - CampaignDefaults.OpeningLoanPrincipalPennies, FestivalTickets.RevenuePennies(1));
    }

    [TestMethod]
    public void TheDevelopmentRouteOpensTierTwoOnACarriedState()
    {
        var s = TierTwo();
        var p = s.CapturePreparation()!;
        var carry = GameSession.DevelopmentCarryOver(2);
        Assert.AreEqual(2, p.Tier);
        Assert.AreEqual(carry, p.CarriedIn);
        Assert.AreEqual(50, p.People.Count(person => person.Role == ProtectedPersonRole.Guest));
        Assert.AreEqual(carry.CashPennies + 75_000, p.OpeningCashPennies);
        Assert.AreEqual(p.OpeningCashPennies, s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        var campaign = s.CaptureCampaignPlanningSnapshot()!;
        Assert.AreEqual(carry.DebtPennies, campaign.Loan.OutstandingPrincipalPennies);
        Assert.AreEqual(2, campaign.EditionNumber);
        var opening = campaign.LedgerTransactions.Single();
        Assert.IsTrue(opening.IsBalanced);
        Assert.AreEqual(p.OpeningCashPennies, opening.Entries.Single(e => e.Account == LedgerAccountType.CashAsset).AmountPennies);
        Assert.AreEqual(-carry.DebtPennies, opening.Entries.Single(e => e.Account == LedgerAccountType.LoanPrincipalLiability).AmountPennies);
        Assert.AreEqual(-75_000L, opening.Entries.Single(e => e.Account == LedgerAccountType.SalesRevenue).AmountPennies);
        Assert.IsTrue(p.Reputation > 0, "A three-star Tier 1 left some reputation.");
        Assert.AreEqual(1_500, s.TicketPricePennies);
        Assert.IsTrue(s.GetFestivalActs().Count(act => s.ActStandingOf(act) == ActStanding.Available) >= 3, "Tier 2's offer has acts who'll play.");
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void TierOneHasNoCarryAndCannotSkipAhead()
    {
        var s = GameSession.CreateBuildCampaign(20260922);
        Assert.IsNull(s.CapturePreparation()!.CarriedIn);
        Assert.IsFalse(s.CanStartNextFestival, "Only a completed festival opens the next one.");
        Assert.ThrowsExactly<InvalidOperationException>(() => s.CreateNextFestival());
    }

    [TestMethod]
    public void CompletingTierOneOpensTierTwoWithItsCashDebtAndStanding()
    {
        var s = BuildSession.Started();
        RunToEnd(s);
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus);
        Assert.IsTrue(s.CanStartNextFestival);
        var closing = s.CaptureSnapshot().FestivalFinances.Single().CashPennies;
        var finished = s.CapturePreparation()!;
        var accounts = s.CompletedFestivalAccounts!;
        Assert.IsTrue(accounts.Reconciles);
        Assert.IsNull(accounts.CarriedInPennies);
        Assert.AreEqual(CampaignDefaults.OpeningLoanPrincipalPennies, accounts.DebtOwedPennies);
        s.RenameFestival("Pond Life");

        var next = s.CreateNextFestival();
        var p = next.CapturePreparation()!;
        Assert.AreEqual(2, p.Tier);
        Assert.AreEqual(PreparationStatus.Preparing, p.Status);
        Assert.AreEqual(1, p.Attempt);
        Assert.AreEqual(s.CampaignId, next.CampaignId);
        Assert.AreEqual(50, p.People.Count(person => person.Role == ProtectedPersonRole.Guest));
        Assert.AreEqual(1_500, next.TicketPricePennies);
        Assert.AreEqual(closing + 75_000, p.OpeningCashPennies);
        Assert.AreEqual(closing + 75_000, next.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        Assert.AreEqual(new FestivalCarryOver(1, closing, CampaignDefaults.OpeningLoanPrincipalPennies, 1, false, []), p.CarriedIn);
        Assert.AreEqual(CampaignDefaults.OpeningLoanPrincipalPennies, next.CaptureCampaignPlanningSnapshot()!.Loan.OutstandingPrincipalPennies);
        Assert.AreEqual("Pond Life", next.CaptureCampaignPlanningSnapshot()!.FestivalName);
        // Standing carries; perks, staff, bookings, stock and the build start fresh.
        Assert.AreEqual(finished.Reputation, p.Reputation);
        CollectionAssert.AreEqual(finished.SceneCredibility, p.SceneCredibility);
        CollectionAssert.AreEqual(finished.SeenActs, p.SeenActs);
        Assert.AreEqual(0, p.BuildPlacements.Length);
        Assert.AreEqual(0, p.AcceptedOffers.Length + p.WorkContracts.Length + p.Payments.Length);
        Assert.IsTrue(next.CapturePerks()!.Pending);
        Assert.AreEqual(0, next.CapturePerks()!.Equipped.Length);
        Assert.IsNull(p.Result);
        BuildSession.Restored(next);
    }

    [TestMethod]
    public void TierTwoPlaysToTheEndAndItsAccountsShowTheCarriedCashAndDebt()
    {
        var s = WithTierTwoExtras(Ready(TierTwo()));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        Assert.AreEqual(2, s.CaptureLifecycleSnapshot()!.TierOrdinal);
        Assert.AreEqual("tier-2", s.CaptureLifecycleSnapshot()!.CurrentTierId);
        s.AdvanceWithoutSnapshot(9_000);
        // A mid-day Tier 2 save restores, through the save file too.
        var middle = BuildSession.Restored(s);
        var directory = Path.Combine(Path.GetTempPath(), "festival-tier2-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var compatibility = new SaveCompatibility("tier2", "content", "tier2");
            var saved = SaveFileAdapter.SaveSlot(directory, "middle", new(s, compatibility, "test", DateTimeOffset.UtcNow));
            Assert.IsTrue(saved.IsSuccess, saved.Error);
            var loaded = SaveFileAdapter.LoadSlot(directory, "middle", compatibility);
            Assert.IsTrue(loaded.IsSuccess, loaded.Error);
            Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, loaded.Session!.CaptureSnapshot().AuthoritativeHash);
        }
        finally { Directory.Delete(directory, true); }
        RunToEnd(s);
        RunToEnd(middle);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, middle.CaptureSnapshot().AuthoritativeHash, "A restored day plays out the same.");
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus);
        var accounts = s.CompletedFestivalAccounts!;
        Assert.IsTrue(accounts.Reconciles);
        Assert.AreEqual(50, accounts.TicketsSold);
        Assert.AreEqual(1_500, accounts.TicketPricePennies);
        Assert.AreEqual(GameSession.DevelopmentCarryOver(2).CashPennies, accounts.CarriedInPennies);
        Assert.AreEqual(GameSession.DevelopmentCarryOver(2).CashPennies, accounts.OpeningCashPennies);
        Assert.AreEqual(CampaignDefaults.OpeningLoanPrincipalPennies, accounts.DebtOwedPennies);
        Assert.IsFalse(s.CanStartNextFestival, "Tier 2 is the highest tier with its parameters set.");
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void AFailedTierTwoRetriesFromTierTwosOpeningState()
    {
        var s = Ready(TierTwo());
        var opening = s.CapturePreparation()!.OpeningCashPennies;
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        Assert.IsTrue(s.CaptureSnapshot().FestivalFinances.Single().CashPennies < opening, "Opening spent some of the money.");
        s.AdvanceWithoutSnapshot(6_000);
        var performer = s.CapturePreparation()!.People.First(person =>
            person.Role == ProtectedPersonRole.Performer && s.CapturePerson(person.AgentId)!.Admitted);
        var tick = s.CurrentTick;
        typeof(GameSession).GetMethod("UpdatePerson", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(s,
            [performer.AgentId, (Func<Person, Person>)(p => p with
            {
                HealthStage = MedicalStage.Critical, Thirst = 10_000, HeatExposure = 10_000, Intent = MedicalIntent.Collapsed,
                HealthWarningTick = tick - 4_000, HealthCollapseTick = tick - GameSession.MedicalDeathDelayTicks, HealthCriticalTick = tick - 1,
            })]);
        for (var i = 0; i < 6_000 && s.PreparedStatus == PreparationStatus.Running; i++) s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(PreparationStatus.Failed, s.PreparedStatus);
        Assert.IsFalse(s.CanStartNextFestival, "A failed festival doesn't unlock the next tier.");
        BuildSession.Restored(s);

        BuildSession.Accept(s, new SpendCouncilFavourCommand());
        var p = s.CapturePreparation()!;
        Assert.AreEqual(2, p.Tier);
        Assert.AreEqual(2, p.Attempt);
        Assert.AreEqual(PreparationStatus.Preparing, p.Status);
        Assert.AreEqual(opening, s.CaptureSnapshot().FestivalFinances.Single().CashPennies, "The retry restarts from Tier 2's opening cash, not Tier 1's.");
        Assert.AreEqual(GameSession.DevelopmentCarryOver(2).DebtPennies, s.CaptureCampaignPlanningSnapshot()!.Loan.OutstandingPrincipalPennies);
        Assert.AreEqual(50, p.People.Count(person => person.Role == ProtectedPersonRole.Guest));
        Assert.AreEqual(0, s.CaptureLifecycleSnapshot()!.FavourBalance);
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void CarriedFavourAndTheSpentWaterShareComeForward()
    {
        var spent = new FestivalCarryOver(1, 30_000, 20_000, 0, true, []);
        var s = (GameSession)typeof(GameSession).GetMethod("CreateLaterFestival", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [20260922UL, new CampaignId(20260922), 2, spent, FestivalStanding.New, Array.Empty<string>(), null])!;
        Assert.IsFalse(BuildSession.Send(s, new CommitCommunityWaterShareCommand()).IsAccepted, "The water share is once per campaign.");
        Ready(s);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        Assert.AreEqual(0, s.CaptureLifecycleSnapshot()!.FavourBalance, "Favour spent at Tier 1 stays spent.");
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void TamperedCarriedStateIsRejectedOnRestore()
    {
        var saved = TierTwo().CapturePersistenceSnapshot();
        var richer = saved with { Preparation = saved.Preparation! with { CarriedIn = saved.Preparation.CarriedIn! with { CashPennies = 90_000 } } };
        Assert.IsFalse(GameSession.Restore(richer).IsSuccess, "Opening cash must follow from the carried cash.");
        var debtFree = saved with { Preparation = saved.Preparation! with { CarriedIn = saved.Preparation.CarriedIn! with { DebtPennies = 0 } } };
        Assert.IsFalse(GameSession.Restore(debtFree).IsSuccess, "The loan still owed must match what was carried.");
        var noCarry = saved with { Preparation = saved.Preparation! with { CarriedIn = null } };
        Assert.IsFalse(GameSession.Restore(noCarry).IsSuccess, "Tier 2 always opens on a carried state.");
    }
}

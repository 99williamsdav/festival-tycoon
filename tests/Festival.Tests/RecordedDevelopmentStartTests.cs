using Festival.Persistence;
using Festival.Simulation;
using System.Reflection;
using System.Text.Json;

namespace Festival.Tests;

[TestClass]
public sealed class RecordedDevelopmentStartTests
{
    private static GameSession Reference()
    {
        using var stream = typeof(GameSession).Assembly.GetManifestResourceStream("Festival.Simulation.recorded-tier-one-clear.json")!;
        var restored = GameSession.Restore(JsonSerializer.Deserialize<SessionPersistenceSnapshot>(stream)!);
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        return restored.Session!;
    }

    [TestMethod]
    public void RecordedClearIsEligibleAndMenuStartEqualsTheOrdinaryTransition()
    {
        var completed = Reference();
        Assert.AreEqual(PreparationStatus.Finished, completed.PreparedStatus);
        Assert.AreEqual(2, completed.CompletedFestivalResult!.Stars);
        Assert.IsTrue(completed.CanStartNextFestival);
        var ordinary = completed.CreateNextFestival();
        var development = GameSession.CreateRecordedTierTwoStart();
        Assert.AreEqual(ordinary.CaptureSnapshot().AuthoritativeHash, development.CaptureSnapshot().AuthoritativeHash);
        BuildSession.Restored(development);
    }

    [TestMethod]
    public void CashReconcilesWithoutAddingTierOneTicketsOrStartFundsTwice()
    {
        var completed = Reference();
        var accounts = completed.CompletedFestivalAccounts!;
        Assert.IsTrue(accounts.Reconciles);
        Assert.AreEqual(17_260L, completed.CompletedFestivalResult!.NetCashChangePennies);
        Assert.AreEqual(37_260L, accounts.ClosingCashPennies);
        var next = GameSession.CreateRecordedTierTwoStart();
        var p = next.CapturePreparation()!;
        Assert.AreEqual(37_260L, p.CarriedIn!.CashPennies);
        Assert.AreEqual(20_000L, p.CarriedIn.DebtPennies);
        Assert.AreEqual(112_260L, p.OpeningCashPennies);
        Assert.AreEqual(p.OpeningCashPennies, next.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        var ledger = next.CaptureCampaignPlanningSnapshot()!.LedgerTransactions.Single();
        Assert.IsTrue(ledger.IsBalanced);
        Assert.AreEqual(-75_000L, ledger.Entries.Single(e => e.Account == LedgerAccountType.SalesRevenue).AmountPennies);
        Assert.AreEqual(20_000L, next.CaptureCampaignPlanningSnapshot()!.Loan.OutstandingPrincipalPennies);
    }

    [TestMethod]
    public void ActualKnowledgeRelationshipsAndStandingCarryWhileBookingsCrewPerksAndStockReset()
    {
        var p = GameSession.CreateRecordedTierTwoStart().CapturePreparation()!;
        CollectionAssert.AreEqual(new[] { "act.daisy-diaries", "act.dj-spreadsheet", "act.two-men-harmonium" }, p.SeenActs);
        Assert.AreEqual(14, p.Reputation);
        CollectionAssert.AreEqual(new[] { 10, 0, 10, 10, 0, 0 }, p.SceneCredibility);
        // Two acts moved on the recorded day: DJ Spreadsheet up, Daisy Diaries down; the third carries no relationship.
        CollectionAssert.AreEqual(new[] { "act.daisy-diaries", "act.dj-spreadsheet" }, p.ActRelationships!.Select(a => a.ActId).ToArray());
        CollectionAssert.AreEqual(new[] { -5, 10 }, p.ActRelationships!.Select(a => a.Value).ToArray());
        Assert.AreEqual(3, p.RelationshipHistory!.Length);
        Assert.IsNull(p.ActPopularity);
        Assert.AreEqual(1, p.CarriedIn!.FavourBalance);
        Assert.AreEqual(0, p.CarriedIn.OwnedEquipment.Length);
        Assert.IsFalse(p.CarriedIn.CommunityWaterUsed);
        Assert.IsTrue(p.PreviousLayout!.Length > 0);
        Assert.AreEqual(0, p.BuildPlacements.Length);
        var next = GameSession.CreateRecordedTierTwoStart();
        Assert.IsTrue(next.CapturePerks()!.Pending);
        Assert.AreEqual(0, next.CapturePerks()!.Equipped.Length);
        Assert.AreEqual(0L, next.PreparationPlanCost);
        Assert.AreEqual(PreparationStatus.Preparing, next.PreparedStatus);
    }

    [TestMethod]
    public void TierTwoHasNormalUnlocksRainRevealAndAffordableTwoStagePreparation()
    {
        var s = GameSession.CreateRecordedTierTwoStart();
        Assert.IsTrue(s.BarnStageOpen);
        Assert.IsFalse(s.PondStageOpen);
        Assert.IsTrue(s.IsRainyFestival);
        Assert.IsFalse(s.CapturePreparation()!.ChallengeAcknowledged);
        Assert.AreEqual("heavy-rain-a.png", FestivalChallenges.ForTier(2)!.Artwork);
        Assert.IsTrue(s.CapturePreparation()!.ExtraMedicSlotOwned);
        CollectionAssert.AreEqual(new[] { "stage.main", "stage.barn" }, s.Stages.Select(a => a.Id).ToArray());
        BuildSession.Accept(s, new AcknowledgeFestivalChallengeCommand(2, 1));
        NextFestivalTests.Ready(s);
        Assert.AreEqual(0, s.GetPreparationStartBlockers().Count);
        Assert.IsTrue(s.PreparationPlanCost < s.CapturePreparation()!.OpeningCashPennies);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        Assert.AreEqual(PreparationStatus.Running, s.PreparedStatus);
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void RetryPreservesCarryAndLearnedStateAndFreshRestartDoesNotRetainEdits()
    {
        var s = GameSession.CreateRecordedTierTwoStart();
        var initial = s.CapturePreparation()!;
        BuildSession.Accept(s, new AcknowledgeFestivalChallengeCommand(2, 1));
        NextFestivalTests.Ready(s);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        s.AdvanceWithoutSnapshot(6_000);
        var performer = s.CapturePreparation()!.People.First(person =>
            person.Role == ProtectedPersonRole.Performer && s.CapturePerson(person.AgentId)!.Admitted);
        var tick = s.CurrentTick;
        // Labelled failure fixture, as in the ordinary Tier 2 Council regression: force one admitted casualty.
        typeof(GameSession).GetMethod("UpdatePerson", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(s,
            [performer.AgentId, (Func<Person, Person>)(p => p with
            {
                HealthStage = MedicalStage.Critical, Thirst = 10_000, HeatExposure = 10_000, Intent = MedicalIntent.Collapsed,
                HealthWarningTick = tick - 4_000, HealthCollapseTick = tick - GameSession.MedicalDeathDelayTicks, HealthCriticalTick = tick - 1,
            })]);
        for (var i = 0; i < 6_000 && s.PreparedStatus == PreparationStatus.Running; i++) s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(PreparationStatus.Failed, s.PreparedStatus);
        BuildSession.Restored(s);
        BuildSession.Accept(s, new SpendCouncilFavourCommand());
        var retry = s.CapturePreparation()!;
        Assert.AreEqual(2, retry.Attempt);
        Assert.IsFalse(retry.ChallengeAcknowledged);
        Assert.AreEqual(initial.CarriedIn, retry.CarriedIn);
        Assert.AreEqual(JsonSerializer.Serialize(initial.ActRelationships), JsonSerializer.Serialize(retry.ActRelationships));
        CollectionAssert.AreEqual(initial.SeenActs, retry.SeenActs);
        Assert.AreEqual(initial.OpeningCashPennies, s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        var fresh = GameSession.CreateRecordedTierTwoStart();
        Assert.AreEqual(1, fresh.CapturePreparation()!.Attempt);
        Assert.AreEqual(0, fresh.CaptureBuildPlacements().Count);
        Assert.AreEqual(112_260L, fresh.CapturePreparation()!.OpeningCashPennies);
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void ReferenceCanBeReproducedByOrdinaryPlayerCommandsWithoutGrantsOrPrivateMutations()
    {
        var s = GameSession.CreateBuildCampaign(20260922);
        BuildSession.Accept(s, new AcknowledgeFestivalChallengeCommand(1, 1));
        var perk = s.CapturePerks()!;
        BuildSession.Accept(s, new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
        BuildSession.Accept(s, new UseDefaultBuildLayoutCommand());
        var acts = s.GetFestivalActs().Where(a => s.ActStandingOf(a) == ActStanding.Available)
            .OrderBy(s.ActFee).ThenBy(a => a.Id, StringComparer.Ordinal).Take(3).Select(a => a.Id).ToArray();
        BuildSession.Accept(s, new SetProgrammeCommand(acts));
        BuildSession.Accept(s, new SetPreparationStockCommand(40, 32));
        foreach (var group in s.GetStaffCandidates().GroupBy(c => c.Role))
        {
            var candidate = group.OrderBy(c => c.Traits.Length > 0).ThenBy(c => c.Grade > 0)
                .ThenBy(c => Math.Abs(c.Grade)).ThenBy(c => c.Id, StringComparer.Ordinal).First();
            BuildSession.Accept(s, new AcceptPreparationOfferCommand(candidate.Id));
        }
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        for (var i = 0; i < 80 && s.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing; i++)
            s.AdvanceWithoutSnapshot(1_000);
        Assert.IsTrue(s.CanStartNextFestival);
        // Command IDs differ between the probe and BuildSession; compare resulting money, outcomes and next-tier state.
        Assert.AreEqual(Reference().CompletedFestivalResult, s.CompletedFestivalResult);
        Assert.AreEqual(GameSession.CreateRecordedTierTwoStart().CaptureSnapshot().AuthoritativeHash,
            s.CreateNextFestival().CaptureSnapshot().AuthoritativeHash);
    }
}

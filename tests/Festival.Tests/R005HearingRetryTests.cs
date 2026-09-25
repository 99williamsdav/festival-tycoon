using Festival.Simulation;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class R005HearingRetryTests
{
    private static CommandResult Send(GameSession session, SessionCommand command) => session.Execute(new(
        new CommandId(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase,
        session.CurrentTick, session.NextSubmissionSequence, null, command));

    private static GameSession Restored(GameSession session)
    {
        var result = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, result.Session!.CaptureSnapshot().AuthoritativeHash);
        return result.Session;
    }

    private static void Book(GameSession session, bool buyRig, bool buyStock, bool worker = false)
    {
        foreach (var id in new[] { "act.folk", "staff.steward" }
                     .Concat(buyRig ? ["equipment.buy"] : Array.Empty<string>())
                     .Concat(buyStock ? ["contract.stock"] : Array.Empty<string>())
                     .Concat(worker ? ["maintenance.worker"] : Array.Empty<string>()))
            Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand(id)).IsAccepted, id);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
    }

    [TestMethod]
    public void RealDeathSpendsOneFavourAndResetsCashStockWithoutFarmingProperty()
    {
        var session = GameSession.CreateEquipmentCampaign(2);
        Book(session, buyRig: true, buyStock: true);
        session.AdvanceWithoutSnapshot(7_200);
        Assert.AreEqual(PreparationStatus.Failed, session.PreparedStatus);
        Assert.AreEqual(1, session.CaptureLifecycleSnapshot()!.FixtureFavourBalance);
        Assert.AreEqual(1, session.CaptureLifecycleSnapshot()!.Casualties.Count);
        Assert.AreEqual(1, session.CaptureLifecycleSnapshot()!.Hearings.Count);
        session = Restored(session);

        Assert.IsTrue(Send(session, new SpendCouncilFavourCommand()).IsAccepted);
        var retry = session.CapturePreparation()!;
        Assert.AreEqual(2, retry.Attempt);
        Assert.AreEqual(PreparationStatus.Preparing, retry.Status);
        Assert.AreEqual(80_000L, session.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        Assert.AreEqual(40, session.CaptureSnapshot().OwnedStocks.Single().Quantity);
        Assert.IsTrue(retry.OwnedEquipment.Contains("sound-rig"));
        Assert.AreEqual(0, retry.Rentals.Length);
        Assert.AreEqual(0, retry.WorkContracts.Length);
        Assert.AreEqual(0, retry.AcceptedOffers.Length);
        Assert.AreEqual(1, session.CaptureLifecycleSnapshot()!.Casualties.Count);
        Assert.AreEqual(0, session.CaptureLifecycleSnapshot()!.FixtureFavourBalance);
        Assert.IsFalse(Send(session, new SpendCouncilFavourCommand()).IsAccepted);
        session = Restored(session);

        Book(session, buyRig: false, buyStock: true);
        session.AdvanceWithoutSnapshot(7_200);
        Assert.AreEqual(2, session.CaptureLifecycleSnapshot()!.Casualties.Count);
        Assert.AreEqual(2, session.CaptureLifecycleSnapshot()!.Hearings.Count);
        Assert.AreEqual(0, session.CaptureLifecycleSnapshot()!.FixtureFavourBalance);
        Assert.IsFalse(Send(session, new SpendCouncilFavourCommand()).IsAccepted);
        Restored(session);
    }

    [TestMethod]
    public void CommunitySharingCapsFastDrinkersAndCannotBeCommittedTwice()
    {
        var session = GameSession.CreateMedicalCampaign(20260922);
        StringAssert.Contains(session.CommunityWaterShareDisclosure!, "12 thirst units/tick");
        Assert.IsTrue(Send(session, new CommitCommunityWaterShareCommand()).IsAccepted);
        Assert.IsFalse(Send(session, new CommitCommunityWaterShareCommand()).IsAccepted);
        Assert.AreEqual(1, session.CapturePreparation()!.CommunityShareAttempt);
        Assert.AreEqual(1, session.CaptureLifecycleSnapshot()?.FixtureFavourBalance ?? 1);
        Book(session, buyRig: true, buyStock: false);
        Assert.IsTrue(session.CommunityWaterShareActive);
        for (ulong id = 1; id <= 8; id++)
            Assert.AreEqual(Math.Min(12, GameSession.MedicalDrinkThirstPerTickFor(id)), session.EffectiveMedicalDrinkThirstPerTickFor(id));
        Restored(session);
    }

    [TestMethod]
    public void FailedSharedWeekendCannotClaimFavourOrRepeatWaterChoiceOnRetry()
    {
        var session = GameSession.CreateMedicalCampaign(20260922);
        Assert.IsTrue(Send(session, new CommitCommunityWaterShareCommand()).IsAccepted);
        Book(session, buyRig: false, buyStock: true);
        session.AdvanceWithoutSnapshot(6_200);
        Assert.AreEqual(PreparationStatus.Failed, session.PreparedStatus);
        Assert.IsFalse(session.CapturePreparation()!.CommunityFavourClaimed);
        Assert.AreEqual(1, session.CaptureLifecycleSnapshot()!.FixtureFavourBalance);
        session = Restored(session);
        Assert.IsTrue(Send(session, new SpendCouncilFavourCommand()).IsAccepted);
        Assert.IsFalse(session.CommunityWaterShareActive);
        Assert.IsFalse(Send(session, new CommitCommunityWaterShareCommand()).IsAccepted);
        Assert.AreEqual(40, session.CaptureSnapshot().OwnedStocks.Single().Quantity);
        Restored(session);
    }

    [TestMethod]
    public void HeadlessSecondFavourFixtureProvesTwoRetryResetsWithoutStockAccumulation()
    {
        // The second Favour is fixture-only. Normal campaigns still begin with exactly one.
        var session = GameSession.CreateR005RetryEconomyFixture(2);
        ulong? workerId = null;
        int? walletCount = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            Book(session, buyRig: attempt == 1, buyStock: true, worker: true);
            workerId ??= session.CapturePreparation()!.MaintenanceWorkerId;
            walletCount ??= session.CaptureSnapshot().Wallets.Count;
            Assert.AreEqual(workerId, session.CapturePreparation()!.MaintenanceWorkerId);
            Assert.AreEqual(walletCount, session.CaptureSnapshot().Wallets.Count);
            session.AdvanceWithoutSnapshot(7_200);
            Assert.AreEqual(PreparationStatus.Failed, session.PreparedStatus);
            Assert.AreEqual(attempt, session.CaptureLifecycleSnapshot()!.Casualties.Count);
            session = Restored(session);
            Assert.IsTrue(Send(session, new SpendCouncilFavourCommand()).IsAccepted);
            Assert.AreEqual(attempt + 1, session.CapturePreparation()!.Attempt);
            Assert.AreEqual(80_000L, session.CaptureSnapshot().FestivalFinances.Single().CashPennies);
            Assert.AreEqual(40, session.CaptureSnapshot().OwnedStocks.Single().Quantity);
            Assert.AreEqual(1, session.CapturePreparation()!.OwnedEquipment.Length);
            Assert.AreEqual(2 - attempt, session.CaptureLifecycleSnapshot()!.FixtureFavourBalance);
            session = Restored(session);
        }
        Assert.AreEqual(2, session.CaptureLifecycleSnapshot()!.Hearings.Count);
        Assert.AreEqual(2, session.CapturePreparation()!.Payments.Count(item => item.OfferId == "contract.stock"));
        Assert.AreEqual(1, session.CapturePreparation()!.Payments.Count(item => item.OfferId == "equipment.buy"));
        Assert.IsFalse(Send(session, new SpendCouncilFavourCommand()).IsAccepted);
    }

    [TestMethod]
    public void CommunityFavourIsClaimedOnlyAfterFullHonouredWeekend()
    {
        var session = GameSession.CreateMedicalCampaign(20260922);
        Assert.IsTrue(Send(session, new CommitCommunityWaterShareCommand()).IsAccepted);
        Book(session, buyRig: true, buyStock: false);
        Assert.AreEqual(1, session.CaptureLifecycleSnapshot()!.FixtureFavourBalance);
        // Fixture-only suppression keeps this test focused on the honour/claim boundary;
        // lethality and timely medical prevention are covered by MedicalIncidentTests.
        var field = typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!;
        while (session.CurrentTick < 40_000 && session.PreparedStatus is (PreparationStatus.Running or PreparationStatus.Departing))
        {
            session.AdvanceWithoutSnapshot(500);
            var m = session.CaptureMedical()!;
            field.SetValue(session, m with { Needs = m.Needs.Select(item => item with
                { Thirst = Math.Min(item.Thirst, 1_000), HeatExposure = Math.Min(item.HeatExposure, 1_000) }).ToArray() });
        }
        Assert.AreEqual(PreparationStatus.Finished, session.PreparedStatus,
            $"tick={session.CurrentTick}; death={session.CaptureLifecycleSnapshot()!.Casualties.LastOrDefault()?.Cause}; medical={session.CaptureMedical()!.Stage}");
        Assert.IsTrue(session.CapturePreparation()!.CommunityFavourClaimed);
        Assert.AreEqual(2, session.CaptureLifecycleSnapshot()!.FixtureFavourBalance);
        Assert.AreEqual(1, session.CaptureLifecycleSnapshot()!.CompletedOutcomeTransactionIds.Count(id => id.StartsWith("community-water-favour:")));
        session = Restored(session);
        session.AdvanceWithoutSnapshot(10_000);
        Assert.AreEqual(2, session.CaptureLifecycleSnapshot()!.FixtureFavourBalance);
    }
}

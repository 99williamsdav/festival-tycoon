using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

// Death → Council hearing → Favour retry or concession, on the Build route. The collapse is
// forced on one performer (the only fixture); the death itself, the freeze and the hearing are
// produced by the simulation.
[TestClass]
public sealed class FatalHearingTests
{
    private static GameSession Fatal(ulong seed = 20260922) => Fatal(BuildSession.Started(seed));

    private static GameSession Fatal(GameSession s)
    {
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
        return s;
    }

    [TestMethod]
    public void FavourRetryAfterBuyingEquipmentKeepsItAndSaves()
    {
        var s = BuildSession.Ready(offers: "equipment.buy");
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        s = Fatal(s);
        BuildSession.Accept(s, new SpendCouncilFavourCommand());
        Assert.IsTrue(s.CapturePreparation()!.OwnedEquipment.Length > 0, "Bought equipment survives the retry.");
        Assert.IsFalse(s.CapturePreparation()!.Plan!.OfferIds.Any(id => id.StartsWith("equipment.")), "The retry plan must not buy it again.");
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void DeathFreezesTheEditionAndOpensOneHearing()
    {
        var s = Fatal();
        var lifecycle = s.CaptureLifecycleSnapshot()!;
        Assert.AreEqual(1, lifecycle.Casualties.Count);
        Assert.AreEqual(ProtectedPersonRole.Performer, lifecycle.Casualties.Single().Role);
        Assert.AreEqual(HearingStatus.Open, lifecycle.Hearings.Single().Status);
        Assert.AreEqual(1, lifecycle.FavourBalance);
        var frozen = s.CaptureSnapshot().AuthoritativeHash;
        s.AdvanceWithoutSnapshot(2_000);
        Assert.AreEqual(frozen, s.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(frozen, BuildSession.Restored(s).CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void SpendingFavourRetriesTheSameTierWithAFreshDraft()
    {
        var s = Fatal();
        var tier = s.CapturePreparation()!.Tier;
        BuildSession.Accept(s, new SpendCouncilFavourCommand());
        var lifecycle = s.CaptureLifecycleSnapshot()!;
        Assert.AreEqual(0, lifecycle.FavourBalance);
        Assert.AreEqual(HearingStatus.FavourSpent, lifecycle.Hearings.Single().Status);
        Assert.AreEqual(2UL, lifecycle.CurrentAttemptId);
        Assert.AreEqual(PreparationStatus.Preparing, s.PreparedStatus);
        Assert.AreEqual(tier, s.CapturePreparation()!.Tier);
        Assert.IsFalse(BuildSession.Send(s, new SpendCouncilFavourCommand()).IsAccepted);
        s = BuildSession.Restored(s);
        if (s.CapturePerks() is { Pending: true } perks)
            BuildSession.Accept(s, new ChoosePerkCommand(perks.DraftAttempt, perks.Cursor, perks.Hand[0]));
        BuildSession.Accept(s, new SetProgrammeCommand(BuildSession.Acts));
        BuildSession.Accept(s, new AcceptPreparationOfferCommand("staff.steward"));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        s.AdvanceWithoutSnapshot(2_000);
        Assert.AreEqual(PreparationStatus.Running, s.PreparedStatus);
    }

    [TestMethod]
    public void FavourRetryRebuildsEveryPlacedToiletFresh()
    {
        var s = BuildSession.Ready();
        GridCell? second = null;
        for (var x = 145; x <= 190 && second is null; x += 5)
        for (var z = 115; z <= 190 && second is null; z += 5)
        {
            var cell = new GridCell(x, z);
            if (s.ValidateCommand(new(new(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase, s.CurrentTick,
                s.NextSubmissionSequence, null, new PlaceBuildServiceCommand(BuildServiceKind.Toilet, cell))) is null) second = cell;
        }
        Assert.IsNotNull(second, "A second portaloo site should exist on the festival field.");
        BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Toilet, second.Value));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        s = Fatal(s);
        Assert.AreEqual(2, s.CaptureToilets().Count);
        BuildSession.Accept(s, new SpendCouncilFavourCommand());
        var placed = s.CapturePreparation()!.BuildPlacements.Where(item => item.Kind == BuildServiceKind.Toilet).ToArray();
        var toilets = s.CaptureToilets();
        CollectionAssert.AreEquivalent(placed.Select(item => item.Id).ToArray(), toilets.Select(item => item.Id).ToArray());
        Assert.AreEqual("toilet.main", toilets[0].Id);
        foreach (var toilet in toilets)
        {
            var site = placed.Single(item => item.Id == toilet.Id);
            Assert.AreEqual(site.Cell, toilet.Cell);
            Assert.AreEqual(site.QuarterTurns, toilet.QuarterTurns);
            Assert.AreEqual(0, toilet.Queue.Length);
            Assert.IsNull(toilet.OwnerId);
            Assert.AreEqual(0, toilet.ServiceTicks);
            Assert.AreEqual(0, toilet.WeeCount + toilet.PooCount);
        }
        s = BuildSession.Restored(s);
        Assert.AreEqual(2, s.CaptureToilets().Count);
    }

    [TestMethod]
    public void ConcedingEndsTheCampaignWithoutSpendingFavour()
    {
        var s = Fatal();
        BuildSession.Accept(s, new ConcedeCouncilHearingCommand());
        var lifecycle = s.CaptureLifecycleSnapshot()!;
        Assert.AreEqual(HearingStatus.Conceded, lifecycle.Hearings.Single().Status);
        Assert.AreEqual(1, lifecycle.FavourBalance);
        Assert.IsTrue(s.CapturePerks()!.Ended);
        Assert.IsFalse(BuildSession.Send(s, new SpendCouncilFavourCommand()).IsAccepted);
        Assert.IsFalse(BuildSession.Send(s, new ConcedeCouncilHearingCommand()).IsAccepted);
        BuildSession.Restored(s);
    }
}

using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

// Death → Council hearing → Favour retry or concession, on the Build route. The collapse is
// forced on one performer (the only fixture); the death itself, the freeze and the hearing are
// produced by the simulation.
[TestClass]
public sealed class FatalHearingTests
{
    private static GameSession Fatal(ulong seed = 20260922)
    {
        var s = BuildSession.Started(seed);
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

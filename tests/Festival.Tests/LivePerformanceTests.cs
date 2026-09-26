using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class LivePerformanceTests
{
    private static CommandResult Execute(GameSession session, SessionCommand command) => session.Execute(new(
        new CommandId(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase, session.CurrentTick,
        session.NextSubmissionSequence, null, command));

    private static GameSession Started(int tier = 1, string act = "act.folk")
    {
        var session = GameSession.CreateEquipmentCampaign(2, tier);
        foreach (var id in new[] { act, "staff.steward", "equipment.buy" })
            Assert.IsTrue(Execute(session, new AcceptPreparationOfferCommand(id)).IsAccepted);
        Assert.IsTrue(Execute(session, new StartPreparedEditionCommand()).IsAccepted);
        return session;
    }

    private static GameSession Restored(GameSession session)
    {
        var saved = session.CapturePersistenceSnapshot();
        var restored = GameSession.Restore(saved);
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
        return restored.Session;
    }

    [TestMethod]
    public void ThreeStablePerformersTravelAndListenersAccrueOnlyWhileWatching()
    {
        var session = Started();
        var first = session.CaptureLivePerformance()!;
        Assert.AreEqual(3, first.Performers.Length);
        Assert.IsTrue(first.Performers.All(item => !item.OnStage && !item.InstrumentAttached));
        Assert.IsTrue(first.Listeners.All(item => item.ListenedTicks == 0));
        session = Restored(session);
        session.AdvanceWithoutSnapshot(3_200);
        var show = session.CaptureLivePerformance()!;
        Assert.AreEqual(LiveSetStage.Live, show.Stage);
        Assert.IsTrue(show.Performers.All(item => item.OnStage));
        Assert.IsTrue(show.Performers.Take(2).All(item => item.InstrumentAttached));
        Assert.IsFalse(show.Performers[2].InstrumentAttached, "The drum kit is a fixed stage prop, not a body attachment.");
        Assert.IsTrue(show.Listeners.Count(item => item.AtPlace) > 5);
        Assert.AreEqual(show.Listeners.Count(item => item.Place is not null), show.Listeners.Select(item => item.Place).Where(item => item is not null).Distinct().Count());
        Assert.IsTrue(show.Listeners.Any(item => item.ListenedTicks > 0 && item.EnjoymentEarned > 0));
        Assert.IsTrue(show.Listeners.All(item => item.ListenedTicks > 0 || item.EnjoymentEarned == 0));
        session = Restored(session);
        var replay = Restored(session);
        session.AdvanceWithoutSnapshot(80); replay.AdvanceWithoutSnapshot(80);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, replay.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void BandHoldsOffStageUntilLeadThenEquipsAtMarksBeforeSetStart()
    {
        var session = Started();
        var planned = session.CaptureLivePerformance()!.PlannedTick;
        Assert.AreEqual(2, session.CaptureLivePerformance()!.Version);
        Assert.AreEqual(GameSession.LiveSetArrivalDelayTicks, planned);
        var entry = planned - GameSession.LiveSetStageEntryLeadTicks;
        session.AdvanceWithoutSnapshot(checked((int)entry - 1));
        var holding = session.CaptureLivePerformance()!;
        Assert.AreEqual(LiveSetStage.BeforeSet, holding.Stage);
        Assert.IsTrue(holding.Performers.All(item => item.AccessReached && !item.StairReached && !item.OnStage && !item.InstrumentAttached));
        Assert.IsTrue(holding.Performers.All(item =>
            session.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id.Value == item.AgentId).Destination == item.AccessCell));
        session = Restored(session);
        session.AdvanceWithoutSnapshot(1);
        var entering = session.CaptureLivePerformance()!;
        Assert.IsTrue(entering.Performers.All(item => item.AccessReached && !item.OnStage));
        Assert.IsTrue(entering.Performers.All(item =>
            session.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id.Value == item.AgentId).Destination == item.StairCell));
        while (session.CurrentTick < planned - 1 && !session.CaptureLivePerformance()!.Performers.All(item => item.OnStage))
            session.AdvanceWithoutSnapshot(1);
        var ready = session.CaptureLivePerformance()!;
        Assert.IsTrue(ready.Performers.All(item => item.AccessReached && item.StairReached && item.OnStage));
        Assert.AreEqual(LiveSetStage.BeforeSet, ready.Stage);
        Assert.IsTrue(ready.Performers.Take(2).All(item => item.InstrumentAttached), "Guitar and bass attach when their owners reach their marks.");
        Assert.IsFalse(ready.Performers[2].InstrumentAttached);
        var snapshot = session.CapturePersistenceSnapshot();
        var forged = ready with { Performers = ready.Performers.Select((item, index) =>
            index == 2 ? item with { InstrumentAttached = true } : item).ToArray() };
        Assert.IsFalse(GameSession.Restore(snapshot with { LivePerformance = forged }).IsSuccess);
        session = Restored(session);
        session.AdvanceWithoutSnapshot(checked((int)(planned - session.CurrentTick)));
        Assert.AreEqual(LiveSetStage.Live, session.CaptureLivePerformance()!.Stage);
        Assert.AreEqual(planned, session.CaptureLivePerformance()!.StartedTick);
    }

    [TestMethod]
    public void IsolationProducesImmediateSilenceThenOnlyDelayedBoo()
    {
        var session = Started();
        session.AdvanceWithoutSnapshot(3_200);
        Assert.AreEqual(LiveSetStage.Live, session.CaptureLivePerformance()!.Stage);
        Assert.IsTrue(Execute(session, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
        session.AdvanceWithoutSnapshot(1);
        var cut = session.CaptureLivePerformance()!;
        Assert.AreEqual(LiveSetStage.Interrupted, cut.Stage);
        Assert.AreEqual("silence", cut.LastReaction);
        var enjoyment = cut.Listeners.Sum(item => item.EnjoymentEarned);
        session = Restored(session);
        session.AdvanceWithoutSnapshot(GameSession.SustainedBooDelayTicks - 1);
        Assert.AreEqual("silence", session.CaptureLivePerformance()!.LastReaction);
        Assert.AreEqual(enjoyment, session.CaptureLivePerformance()!.Listeners.Sum(item => item.EnjoymentEarned));
        session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual("sustained-boo", session.CaptureLivePerformance()!.LastReaction);
    }

    [TestMethod]
    public void FullFrontageFallsBackAndFinishedSetDoesNotRewardTwice()
    {
        var trailer = LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.trailer-stage");
        Assert.AreEqual(-16.0, trailer.XMetres);
        Assert.AreEqual(11.0, trailer.ZMetres);
        Assert.AreEqual(1, trailer.YawQuarterTurns);
        var session = Started(tier: 2);
        Assert.IsTrue(Execute(session, new EquipmentCommand(EquipmentAction.ShedLoad)).IsAccepted);
        session.AdvanceWithoutSnapshot(3_200);
        var listeners = session.CaptureLivePerformance()!.Listeners;
        Assert.AreEqual(40, listeners.Count(item => item.Place is not null));
        Assert.IsTrue(listeners.All(item => item.Place is { } place && place.X is >= 103 and <= 122 &&
            session.TraversalGrid!.Get(place).IsWalkable), "All reserved places must face the moved stage and avoid track/generator obstacles.");
        Assert.IsTrue(listeners.Select(item => item.Place!.Value).Distinct().Count() == 40);
        // Interest changes local crowd tolerance, not a prescribed front/middle/rear row.
        Assert.IsTrue(listeners.Where(item => item.Enthusiasm == 35).Any(item => item.Place!.Value.X <= 110));
        Assert.IsTrue(listeners.Count(item => item.Place!.Value.X <= 105) < 40, "Full frontage must use farther walkable places.");
        session = Restored(session);
        session.AdvanceWithoutSnapshot(10_000);
        var finished = session.CaptureLivePerformance()!;
        Assert.AreEqual(LiveSetStage.Finished, finished.Stage);
        Assert.IsTrue(finished.Performers.All(item => !item.InstrumentAttached));
        var satisfaction = session.CapturePreparation()!.People.Select(item => item.Satisfaction).ToArray();
        session = Restored(session);
        session.AdvanceWithoutSnapshot(500);
        CollectionAssert.AreEqual(satisfaction, session.CapturePreparation()!.People.Select(item => item.Satisfaction).ToArray());
    }

    [TestMethod]
    public void BandUsesSavedSideAccessAndNeverReportsExitArrivalAsOnStage()
    {
        var session = Started();
        Assert.IsTrue(Execute(session, new EquipmentCommand(EquipmentAction.ShedLoad)).IsAccepted);
        var before = session.CaptureLivePerformance()!;
        Assert.IsTrue(before.Performers.All(item =>
            session.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id.Value == item.AgentId).Destination == item.AccessCell));
        Assert.IsFalse(session.TraversalGrid!.Get(new GridCell(91, 143)).IsWalkable);
        Assert.IsTrue(session.TraversalGrid.Get(before.Performers[0].StairCell).IsWalkable);
        Assert.IsTrue(session.TraversalGrid.Get(before.Performers[0].StageCell).IsWalkable);
        session.AdvanceWithoutSnapshot(700);
        session = Restored(session);
        session.AdvanceWithoutSnapshot(2_500);
        var live = session.CaptureLivePerformance()!;
        Assert.AreEqual(LiveSetStage.Live, live.Stage);
        Assert.IsTrue(live.Performers.All(item => item.AccessReached && item.StairReached && item.OnStage &&
            session.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id.Value == item.AgentId).Destination == item.StageCell));
        session.AdvanceWithoutSnapshot(10_000);
        Assert.AreEqual(LiveSetStage.Finished, session.CaptureLivePerformance()!.Stage);
        Assert.IsTrue(session.CaptureLivePerformance()!.Performers.All(item => !item.OnStage));
        session.AdvanceWithoutSnapshot(1_000);
        Assert.IsTrue(session.CaptureLivePerformance()!.Performers.All(item => !item.OnStage));
        Restored(session);
    }

    [TestMethod]
    public void ListeningCountsExactEligibleTicksAcrossSetStartAndPowerCut()
    {
        var probe = Started();
        probe.AdvanceWithoutSnapshot(3_200);
        var startTick = probe.CaptureLivePerformance()!.StartedTick;
        var session = Started();
        session.AdvanceWithoutSnapshot(checked((int)startTick));
        var start = session.CaptureLivePerformance()!;
        Assert.AreEqual(LiveSetStage.Live, start.Stage);
        Assert.IsTrue(start.Listeners.Where(item => item.AtPlace).All(item => item.ListenedTicks == 0 && item.EnjoymentEarned == 0));
        var listenerId = start.Listeners.First(item => item.AtPlace).AgentId;
        session = Restored(session);
        session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(1, session.CaptureLivePerformance()!.Listeners.Single(item => item.AgentId == listenerId).ListenedTicks);
        session.AdvanceWithoutSnapshot(78);
        var beforeBonus = session.CaptureLivePerformance()!.Listeners.Single(item => item.AgentId == listenerId);
        Assert.AreEqual(79, beforeBonus.ListenedTicks);
        Assert.AreEqual(0, beforeBonus.EnjoymentEarned);
        session.AdvanceWithoutSnapshot(1);
        var bonus = session.CaptureLivePerformance()!.Listeners.Single(item => item.AgentId == listenerId);
        Assert.AreEqual(80, bonus.ListenedTicks);
        Assert.IsTrue(bonus.EnjoymentEarned > 0);
        Assert.IsTrue(Execute(session, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
        session = Restored(session);
        session.AdvanceWithoutSnapshot(80);
        Assert.AreEqual(80, session.CaptureLivePerformance()!.Listeners.Single(item => item.AgentId == listenerId).ListenedTicks);
    }
}

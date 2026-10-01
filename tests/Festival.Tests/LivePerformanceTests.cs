using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class LivePerformanceTests
{
    private static CommandResult Execute(GameSession session, SessionCommand command) => session.Execute(new(
        new CommandId(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase, session.CurrentTick,
        session.NextSubmissionSequence, null, command));

    private static GameSession Started(int tier = 1)
    {
        var session = BuildSession.Planned(2);
        foreach (var id in BuildSession.CrewIds(session).Concat(new[] { "equipment.buy" }))
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
    public void ListeningCountsExactEligibleTicksAcrossSetStartAndPowerCut()
    {
        var probe = Started();
        probe.AdvanceWithoutSnapshot(4_800);
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

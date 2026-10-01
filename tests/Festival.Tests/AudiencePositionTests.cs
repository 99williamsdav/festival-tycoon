using System.Diagnostics;
using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class AudiencePositionTests
{
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static GameSession Started(int tier = 1)
    {
        var session = BuildSession.Planned(2);
        foreach (var command in BuildSession.Crew(session).Concat(new SessionCommand[] {
            new AcceptPreparationOfferCommand("equipment.buy"), new StartPreparedEditionCommand() }))
        {
            var result = session.Execute(new(new CommandId(session.NextSubmissionSequence + 1), session.CampaignId,
                session.Phase, session.CurrentTick, session.NextSubmissionSequence, null, command));
            Assert.IsTrue(result.IsAccepted, result.Message);
        }
        session.AdvanceWithoutSnapshot(4_800);
        return session;
    }
    private static GameSession Restore(GameSession session)
    {
        var result = GameSession.Restore(session.CapturePersistenceSnapshot()); Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, result.Session!.CaptureSnapshot().AuthoritativeHash);
        return result.Session;
    }
    private static void Live(GameSession session, LivePerformanceSnapshot live) => typeof(GameSession).GetField("_livePerformance", Hidden)!.SetValue(session, live);
    private static void Route(GameSession session, ulong id, GridCell cell, string intent) => typeof(GameSession).GetMethod("ApplyAgentDestination", Hidden)!.Invoke(session,
        [new EntityId(id), new SetAgentDestinationCommand(cell, intent), false]);
    // Labelled initial geometry only. Every subsequent displacement uses the shared walker.
    private static void Place(GameSession session, ulong id, GridCell cell)
    {
        var agents = typeof(GameSession).GetField("_navigationAgents", Hidden)!.GetValue(session)!;
        var agent = agents.GetType().GetProperty("Item")!.GetValue(agents, [new EntityId(id)])!;
        var centre = TraversalGrid.CellCentre(cell);
        foreach (var (name, value) in new[] { ("XMillimetres", centre.XMillimetres), ("ZMillimetres", centre.ZMillimetres),
            ("SegmentOriginXMillimetres", centre.XMillimetres), ("SegmentOriginZMillimetres", centre.ZMillimetres) })
            agent.GetType().GetProperty(name)!.SetValue(agent, value);
        Route(session, id, cell, "labelled.audience-initial-layout");
    }
    private static ulong Sparse(GameSession session, int enthusiasm, GridCell cell)
    {
        var live = session.CaptureLivePerformance()!; var id = live.Listeners[0].AgentId;
        foreach (var (listener, index) in live.Listeners.Select((item, index) => (item, index)))
            Place(session, listener.AgentId, listener.AgentId == id ? cell : new(180, 110 + index * 2));
        Live(session, live with { Listeners = live.Listeners.Select(item => item with { Place = item.AgentId == id ? cell : null,
            AtPlace = item.AgentId == id, Enthusiasm = item.AgentId == id ? enthusiasm : item.Enthusiasm,
            LastDecisionTick = session.CurrentTick - (item.AgentId == id ? 800 : 0) }).ToArray() });
        return id;
    }
    private static LiveListener Listener(GameSession session, ulong id) => session.CaptureLivePerformance()!.Listeners.Single(item => item.AgentId == id);

    [TestMethod]
    public void ExtremeSightlinePrefersCentralBehindFrontPeopleButCrowdedCenterStillLoses()
    {
        var session=Started();var id=Sparse(session,35,new(105,157));
        var live=session.CaptureLivePerformance()!;
        Place(session,live.Listeners[1].AgentId,new(103,149));
        Place(session,live.Listeners[2].AgentId,new(103,151));
        var centre=new GridCell(106,150);var side=new GridCell(103,164);
        int Score(GridCell cell)=>(int)typeof(GameSession).GetMethod("PlaceScore",Hidden)!.Invoke(session,
            [Listener(session,id),cell,new GridCell(105,157),session.CaptureLivePerformance()!.Listeners])!;
        var clearCenter=Score(centre);var clearSide=Score(side);
        Assert.IsTrue(clearCenter<clearSide,$"Behind-front central {clearCenter} versus extreme side {clearSide}");
        GridCell[] addedCrowd=[new(106,148),new(106,152),new(108,150),new(104,150)];
        for(var i=0;i<addedCrowd.Length;i++) Place(session,live.Listeners[i+3].AgentId,addedCrowd[i]);
        Assert.IsTrue(Score(centre)>Score(side),"Central preference must yield to actual over-comfort crowd pressure.");
        Console.WriteLine($"Sightline tradeoff clear behind-front={clearCenter} extreme-side={clearSide}; crowded center={Score(centre)} side={Score(side)}");
    }

}

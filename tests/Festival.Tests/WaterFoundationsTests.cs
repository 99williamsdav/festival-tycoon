using Festival.Simulation;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class WaterFoundationsTests
{
    private static CommandResult Send(GameSession session, SessionCommand command) => session.Execute(new(
        new CommandId(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase,
        session.CurrentTick, session.NextSubmissionSequence, null, command));

    private static GameSession Restore(GameSession session)
    {
        var result = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, result.Session!.CaptureSnapshot().AuthoritativeHash);
        return result.Session;
    }

    private static readonly GridCell ExtraSite = new(70, 120);

    private static GameSession StartWithExtra(bool tower = false, bool share = false)
    {
        var session = tower ? BuildSession.PlannedWith("high-pressure") : BuildSession.Planned();
        var placed = Send(session, new PlaceBuildServiceCommand(BuildServiceKind.WaterTap, ExtraSite));
        Assert.IsTrue(placed.IsAccepted, placed.Message);
        if (share) Assert.IsTrue(Send(session, new CommitCommunityWaterShareCommand()).IsAccepted);
        session = Restore(session);
        Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand("staff.steward")).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        return Restore(session);
    }

    private static void PutNear(GameSession session, ulong id, GridCell cell)
    {
        var field = typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var agents = field.GetValue(session)!;
        var agent = agents.GetType().GetProperty("Item")!.GetValue(agents, [new EntityId(id)])!;
        var centre = TraversalGrid.CellCentre(cell);
        foreach (var (name, value) in new[] { ("XMillimetres", centre.XMillimetres), ("ZMillimetres", centre.ZMillimetres),
                     ("SegmentOriginXMillimetres", centre.XMillimetres), ("SegmentOriginZMillimetres", centre.ZMillimetres) })
            agent.GetType().GetProperty(name)!.SetValue(agent, value);
    }

    private static void SetArrived(GameSession session, ulong id, GridCell cell)
    {
        PutNear(session, id, cell);
        var field = typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var agents = field.GetValue(session)!;
        var agent = agents.GetType().GetProperty("Item")!.GetValue(agents, [new EntityId(id)])!;
        agent.GetType().GetProperty("Destination")!.SetValue(agent, cell);
        agent.GetType().GetProperty("Action")!.SetValue(agent, AgentNavigationAction.Arrived);
        agent.GetType().GetProperty("Route")!.SetValue(agent, new List<GridCell>());
        agent.GetType().GetProperty("RouteIndex")!.SetValue(agent, 0);
    }

    private static int StartSharedRate(GameSession session, ulong id)
    {
        if (session.PreparedStatus == PreparationStatus.Preparing)
        {
            foreach (var offer in new[] { "staff.steward", "equipment.buy" })
                Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand(offer)).IsAccepted);
            Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        }
        return session.EffectiveMedicalDrinkThirstPerTickFor(id);
    }

    [TestMethod]
    public void QueuedGuestForfeitsPlaceForWorthwhileAlternativeButOwnerAndNearFrontStay()
    {
        var session = StartWithExtra();
        var ids = session.CaptureMedical()!.Needs.Take(3).Select(item => item.AgentId).ToArray();
        var field = typeof(GameSession).GetProperty("MedicalView", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var id in ids) typeof(GameSession).GetMethod("UpdatePerson", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(session, [id, (Func<Person, Person>)(person => person with { Admitted = true })]);
        var m = session.CaptureMedical()!;
        BuildSession.SetTap(session, BuildSession.MainTap(session) with { Queue = ids, OwnerId = ids[0] });
        field.SetValue(session, m with {
            Needs = m.Needs.Select(need => ids.Contains(need.AgentId) ? need with {
                Intent = need.AgentId == ids[0] ? MedicalIntent.Drinking : MedicalIntent.SeekWater,
                QueueSlot = Array.IndexOf(ids, need.AgentId), WaterPointId = "water.main", Thirst = need.AgentId == ids[0] ? 100 : 10000,
                LastWaterChoiceReviewTick = -160 } : need).ToArray() });
        for (var index = 0; index < ids.Length; index++) SetArrived(session, ids[index], GameSession.MedicalQueueSlot(index));
        typeof(GameSession).GetMethod("RetargetWaterSeekers", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, null);
        // Labelled near-alternative fixture; only position behind an actual slow person gains enough.
        PutNear(session, ids[2], new GridCell(ExtraSite.X, ExtraSite.Z + 5));
        // Each person re-plans once a second; one second covers all three.
        session.AdvanceWithoutSnapshot(GameSession.ActivityDecisionTicks);
        var now = session.CaptureMedical()!;
        Assert.AreEqual("water.main", now.Needs.Single(item => item.AgentId == ids[0]).WaterPointId, "Drinker never switches.");
        Assert.AreEqual("water.main", now.Needs.Single(item => item.AgentId == ids[1]).WaterPointId, "Own-position wait excludes people behind you.");
        Assert.AreEqual("water.extra-1", now.Needs.Single(item => item.AgentId == ids[2]).WaterPointId);
        Assert.IsFalse(BuildSession.MainTap(session).Queue.Contains(ids[2]));
        Assert.IsTrue(now.Evidence.Any(item => item.Id == "medical:water-rechoose" && item.Description.Contains("old place forfeited=True")));
        var switchedTick = now.Needs.Single(item => item.AgentId == ids[2]).LastWaterChoiceReviewTick;
        session.AdvanceWithoutSnapshot(10);
        Assert.AreEqual(switchedTick, session.CaptureMedical()!.Needs.Single(item => item.AgentId == ids[2]).LastWaterChoiceReviewTick);
        Restore(session);
    }

    [TestMethod]
    public void BlockedOrganicTailStopsWithoutReservingObstacleAndGrowthWorkIsBounded()
    {
        var session = StartWithExtra();
        var field = typeof(GameSession).GetProperty("MedicalView", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var m = session.CaptureMedical()!;
        var id = m.Needs[0].AgentId;
        var front = GameSession.WaterServiceCell(ExtraSite);
        BuildSession.SetTap(session, session.CaptureWaterPoints().First(point => point.Id != "water.main") with { Queue = [id], QueueCells = [front] });
        var gridField = typeof(GameSession).GetField("_traversalGrid", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var grid = (TraversalGrid)gridField.GetValue(session)!;
        var overrides = grid.Overrides.ToDictionary(item => item.Key, item => item.Value);
        for (var z = front.Z - 3; z <= front.Z + 3; z++)
        for (var x = front.X - 3; x <= front.X + 3; x++) if(x!=front.X||z!=front.Z)overrides[new(x, z)] = new(new(x, z), GroundSurface.Grass, false);
        gridField.SetValue(session, new TraversalGrid(overrides.Values));
        var grow = typeof(GameSession).GetMethod("GrowWaterQueue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var iteration = 0; iteration < 1000; iteration++) Assert.IsFalse((bool)grow.Invoke(session, ["water.extra-1"])!);
        watch.Stop();
        Assert.AreEqual(1, session.CaptureWaterQueueCells("water.extra-1").Count);
        Console.WriteLine($"WATER_GROWTH blocked_checks=1000 elapsed_ms={watch.Elapsed.TotalMilliseconds:F2} max_cells=20");
        Assert.IsTrue(watch.Elapsed.TotalSeconds < 10, "Bounded local growth should not become an unbounded path search.");
    }

}

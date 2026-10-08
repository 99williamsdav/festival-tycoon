using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>
/// Small groups in open ground must not knot together and jostle in place (the playtest "clustering").
/// Walkers crossing one another sidestep and get through; hot guests heading for first-aid rest each
/// get a spot of their own instead of all pressing on the one rest cell.
/// </summary>
[TestClass]
public sealed class CrowdCrossingTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int Centre = 128;

    [TestMethod]
    [DataRow("head-on pair")]
    [DataRow("perpendicular four")]
    [DataRow("diagonal four")]
    [DataRow("five-way knot")]
    [DataRow("six-way knot")]
    [DataRow("offset head-on three")]
    public void CrossingWalkersInOpenGroundAllGetThroughPromptly(string scenario)
    {
        var walks = Scenario(scenario);
        var session = OpenGround(walks, out var ids);
        // Unobstructed, the longest walk here is 11.3 m: about 630 ticks at the slowest guest pace.
        // A few seconds' allowance covers sidestepping round one another; a knot that jostles in
        // place for several seconds does not fit.
        var limit = walks.Max(walk => Ticks(walk)) + 3 * 80;
        var arrivedAt = new Dictionary<EntityId, int>();
        for (var tick = 1; tick <= limit && arrivedAt.Count < ids.Length; tick++)
        {
            session.AdvanceWithoutSnapshot(1);
            foreach (var agent in session.CaptureSnapshot().NavigationAgents)
                if (ids.Contains(agent.Id) && agent.Action == AgentNavigationAction.Arrived) arrivedAt.TryAdd(agent.Id, tick);
        }
        var stuck = session.CaptureSnapshot().NavigationAgents.Where(agent => ids.Contains(agent.Id) && !arrivedAt.ContainsKey(agent.Id))
            .Select(agent => $"{agent.Id.Value}@({agent.XMillimetres},{agent.ZMillimetres}) {agent.Action}").ToArray();
        Assert.AreEqual(0, stuck.Length, $"{scenario}: still walking after {limit} ticks: {string.Join("; ", stuck)}");
    }

    [TestMethod]
    public void HotGuestsHeadingForRestEachGetASpotAndRestPromptly()
    {
        var session = BuildSession.Planned(20260926);
        Assert.IsTrue(BuildSession.Send(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.glitter-rota", "act.low-battery"])).IsAccepted);
        foreach (var offer in BuildSession.CrewIds(session).Append("equipment.rent"))
            Assert.IsTrue(BuildSession.Send(session, new AcceptPreparationOfferCommand(offer)).IsAccepted);
        Assert.IsTrue(BuildSession.Send(session, new StartPreparedEditionCommand()).IsAccepted);
        SetMedical(session, need => need with { Thirst = 0, HeatExposure = 0 });
        session.AdvanceWithoutSnapshot(GameSession.FestivalSlotStarts[0] + 400);

        // Six guests overheat together: not thirsty, so the planner sends them all to first-aid rest.
        var hot = session.CapturePreparation()!.People.Where(person => person.Role == ProtectedPersonRole.Guest && person.Admitted && !person.Departed)
            .Select(person => person.AgentId).Take(6).ToArray();
        SetMedical(session, need => hot.Contains(need.AgentId) ? need with { Thirst = 0, HeatExposure = 9_000, LastDecisionTick = -240 } : need);
        session.AdvanceWithoutSnapshot(80);
        var resters = session.CaptureMedical()!.Needs.Where(need => hot.Contains(need.AgentId) && need.Intent == MedicalIntent.Rest)
            .Select(need => need.AgentId).ToArray();
        Assert.IsTrue(resters.Length >= 4, $"Only {resters.Length} of the hot guests chose rest.");
        var agents = session.CaptureSnapshot().NavigationAgents.Where(agent => resters.Contains(agent.Id.Value)).ToArray();
        CollectionAssert.AllItemsAreUnique(agents.Select(agent => agent.Destination).ToArray(), "Each rester heads for a spot of their own.");
        Assert.IsTrue(agents.All(agent => agent.Destination is { } spot && GameSession.IsRestSpot(spot)));

        // Each reaches their spot in about the time the walk takes, rather than circling an occupied
        // cell until whoever got there first has cooled off and gone (about five seconds each).
        var deadline = agents.ToDictionary(agent => agent.Id.Value, agent =>
        {
            var spot = TraversalGrid.CellCentre(agent.Destination!.Value);
            var dx = (double)spot.XMillimetres - agent.XMillimetres; var dz = (double)spot.ZMillimetres - agent.ZMillimetres;
            return session.CurrentTick + (long)(Math.Sqrt(dx * dx + dz * dz) * 1.5 / (30 * 0.7 * 0.85)) + 3 * 80;
        });
        var rested = new HashSet<ulong>();
        while (rested.Count < resters.Length && session.CurrentTick <= deadline.Values.Max())
        {
            session.AdvanceWithoutSnapshot(1);
            foreach (var agent in session.CaptureSnapshot().NavigationAgents.Where(agent => resters.Contains(agent.Id.Value)))
                if (agent.Action == AgentNavigationAction.Arrived && agent.Destination is { } spot && GameSession.IsRestSpot(spot) &&
                    session.CurrentTick <= deadline[agent.Id.Value]) rested.Add(agent.Id.Value);
        }
        var late = resters.Where(id => !rested.Contains(id)).Select(id =>
        {
            var agent = session.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == id);
            return $"{id}@({agent.XMillimetres},{agent.ZMillimetres}) {agent.Action} {agent.IntentId} to {agent.Destination}";
        }).ToArray();
        Assert.AreEqual(0, late.Length, $"Still not resting: {string.Join("; ", late)}");
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
    }

    private static void SetMedical(GameSession session, Func<MedicalNeed, MedicalNeed> change)
    {
        var state = session.CaptureMedical()!;
        typeof(GameSession).GetProperty("MedicalView", Private)!.SetValue(session, state with { Needs = state.Needs.Select(change).ToArray() });
    }

    private static (GridCell From, GridCell To)[] Scenario(string name)
    {
        static GridCell C(int dx, int dz) => new(Centre + dx, Centre + dz);
        return name switch
        {
            "head-on pair" => [(C(-10, 0), C(10, 0)), (C(10, 0), C(-10, 0))],
            "perpendicular four" => [(C(-10, 0), C(10, 0)), (C(10, 0), C(-10, 0)), (C(0, -10), C(0, 10)), (C(0, 10), C(0, -10))],
            "diagonal four" => [(C(-8, -8), C(8, 8)), (C(8, 8), C(-8, -8)), (C(8, -8), C(-8, 8)), (C(-8, 8), C(8, -8))],
            "five-way knot" => [(C(-10, 0), C(10, 0)), (C(10, 0), C(-10, 0)), (C(0, -10), C(0, 10)),
                (C(-7, 7), C(7, -7)), (C(7, 7), C(-7, -7))],
            "six-way knot" => [(C(-10, 0), C(10, 0)), (C(10, 0), C(-10, 0)), (C(0, -10), C(0, 10)), (C(0, 10), C(0, -10)),
                (C(-7, 7), C(7, -7)), (C(7, -7), C(-7, 7))],
            "offset head-on three" => [(C(-10, 0), C(10, 0)), (C(10, 1), C(-10, 1)), (C(10, -1), C(-10, -1))],
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    private static int Ticks((GridCell From, GridCell To) walk)
    {
        var dx = (walk.To.X - walk.From.X) * TraversalGrid.CellSizeMillimetres;
        var dz = (walk.To.Z - walk.From.Z) * TraversalGrid.CellSizeMillimetres;
        // 30 mm a tick at full pace; a guest strolls at 70% of it, and the slowest in the pattern at 85% of that.
        return (int)(Math.Sqrt((double)dx * dx + (double)dz * dz) / (30 * 0.7 * 0.85)) + 1;
    }

    /// <summary>An empty grid with one navigation agent per walk, all setting off on the same tick.</summary>
    private static GameSession OpenGround((GridCell From, GridCell To)[] walks, out EntityId[] ids)
    {
        var session = new GameSession(77);
        var created = session.Execute(new CommandEnvelope(new CommandId(1), session.CampaignId, session.Phase, session.CurrentTick,
            session.NextSubmissionSequence, null, new InitializeNavigationFixtureCommand(walks[0].From, [])));
        Assert.IsTrue(created.IsAccepted);
        var agents = (System.Collections.IDictionary)typeof(GameSession).GetField("_navigationAgents", Private)!.GetValue(session)!;
        var stateType = agents[created.TargetId!.Value]!.GetType();
        var list = new List<EntityId> { created.TargetId!.Value };
        for (var index = 1; index < walks.Length; index++)
        {
            var id = new EntityId(created.TargetId!.Value.Value + (ulong)index);
            var start = TraversalGrid.CellCentre(walks[index].From);
            var state = Activator.CreateInstance(stateType, nonPublic: true)!;
            void Set(string name, object value) => stateType.GetProperty(name)!.SetValue(state, value);
            Set("Id", id);
            Set("XMillimetres", start.XMillimetres); Set("ZMillimetres", start.ZMillimetres);
            Set("SegmentOriginXMillimetres", start.XMillimetres); Set("SegmentOriginZMillimetres", start.ZMillimetres);
            Set("Action", AgentNavigationAction.Idle);
            agents.Add(id, state);
            list.Add(id);
        }
        var apply = typeof(GameSession).GetMethod("ApplyAgentDestination", Private)!;
        for (var index = 0; index < walks.Length; index++)
        {
            var state = agents[list[index]]!;
            stateType.GetProperty("WalkingSpeedPermille")!.SetValue(state, GameSession.GetWalkingSpeedPermille(list[index]) * GameSession.GuestPacePermille / 1000);
            apply.Invoke(session, [list[index], new SetAgentDestinationCommand(walks[index].To, "test.cross"), false]);
        }
        ids = list.ToArray();
        return session;
    }
}

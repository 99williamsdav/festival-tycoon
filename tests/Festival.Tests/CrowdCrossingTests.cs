using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>
/// Small groups in open ground must not knot together and jostle in place (the playtest "clustering").
/// Walkers crossing one another sidestep and get through; hot guests heading for first-aid rest each
/// get a spot of their own beside first aid instead of all pressing on the one rest cell; and a walker
/// whose goal is under a standing person's feet stops instead of shuffling against them.
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
    public void HotGuestsHeadingForRestEachGetASpotAndRestPromptly() => RestScenario(RestSession(moveFirstAid: false));

    [TestMethod]
    public void TheRestAreaFollowsFirstAidWhereverItIsPlaced()
    {
        var standard = RestSession(moveFirstAid: false);
        var moved = RestSession(moveFirstAid: true);
        Assert.AreNotEqual(standard.CaptureRestCentre(), moved.CaptureRestCentre());
        // Out in front of the tent, four cells along from the medic's post, turned with the tent.
        var tent = moved.CaptureResponsePost(ResponseRole.Medic);
        var offset = GameSession.RotateWaterOffset(new(4, 6), tent.QuarterTurns);
        Assert.AreEqual(new GridCell(tent.Cell.X + offset.X, tent.Cell.Z + offset.Z), moved.CaptureRestCentre());
        Assert.AreEqual(GameSession.MedicalRestCell, GameSession.RestCentreFor(null), "With no first aid placed, the old rest cell stands.");
        // Judged where each rester actually rested: once cooled, a rester may already have moved on.
        var restedAt = RestScenario(moved);
        var centre = moved.CaptureRestCentre();
        foreach (var (id, spot) in restedAt)
            Assert.IsTrue(Math.Abs(spot.X - centre.X) <= 2 && Math.Abs(spot.Z - centre.Z) <= 2,
                $"{id} rested at {spot}, not beside the moved first aid at {tent.Cell} (rest centre {centre}).");
    }

    /// <summary>A started festival with a quiet crowd; first aid optionally moved away from its standard place first.</summary>
    private static GameSession RestSession(bool moveFirstAid)
    {
        var session = BuildSession.Planned(20260926);
        if (moveFirstAid)
        {
            var aid = session.CaptureBuildPlacements().Single(item => item.Kind == BuildServiceKind.FirstAid);
            GridCell[] sites = [new(150, 132), new(130, 120), new(150, 125), new(146, 136), new(125, 118), new(155, 140)];
            var site = sites.Where(cell => Math.Abs(cell.X - aid.Cell.X) + Math.Abs(cell.Z - aid.Cell.Z) >= 6)
                .FirstOrDefault(cell => BuildSession.Send(session, new MoveBuildServiceCommand(aid.Id, cell, 1)).IsAccepted);
            Assert.AreNotEqual(default, site, "Found nowhere to move first aid to.");
        }
        Assert.IsTrue(BuildSession.Send(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.glitter-rota", "act.low-battery"])).IsAccepted);
        foreach (var offer in BuildSession.CrewIds(session).Append("equipment.rent"))
            Assert.IsTrue(BuildSession.Send(session, new AcceptPreparationOfferCommand(offer)).IsAccepted);
        Assert.IsTrue(BuildSession.Send(session, new StartPreparedEditionCommand()).IsAccepted);
        SetMedical(session, need => need with { Thirst = 0, HeatExposure = 0 });
        session.AdvanceWithoutSnapshot(GameSession.FestivalSlotStarts[0] + 400);
        return session;
    }

    /// <summary>Six guests overheat at once; each heads for a rest spot of their own and gets there promptly. Returns where each rester rested.</summary>
    private static Dictionary<ulong, GridCell> RestScenario(GameSession session)
    {
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
        Assert.IsTrue(agents.All(agent => agent.Destination is { } spot && session.IsRestSpot(spot)));

        // Each reaches their spot in about the time the walk takes, rather than circling an occupied
        // cell until whoever got there first has cooled off and gone (about five seconds each).
        var deadline = agents.ToDictionary(agent => agent.Id.Value, agent =>
        {
            var spot = TraversalGrid.CellCentre(agent.Destination!.Value);
            var dx = (double)spot.XMillimetres - agent.XMillimetres; var dz = (double)spot.ZMillimetres - agent.ZMillimetres;
            return session.CurrentTick + (long)(Math.Sqrt(dx * dx + dz * dz) * 1.5 / (30 * 0.7 * 0.85)) + 3 * 80;
        });
        var rested = new Dictionary<ulong, GridCell>();
        while (rested.Count < resters.Length && session.CurrentTick <= deadline.Values.Max())
        {
            session.AdvanceWithoutSnapshot(1);
            foreach (var agent in session.CaptureSnapshot().NavigationAgents.Where(agent => resters.Contains(agent.Id.Value)))
                if (agent.Action == AgentNavigationAction.Arrived && agent.Destination is { } spot && session.IsRestSpot(spot) &&
                    session.CurrentTick <= deadline[agent.Id.Value]) rested.TryAdd(agent.Id.Value, spot);
        }
        var late = resters.Where(id => !rested.ContainsKey(id)).Select(id =>
        {
            var agent = session.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == id);
            return $"{id}@({agent.XMillimetres},{agent.ZMillimetres}) {agent.Action} {agent.IntentId} to {agent.Destination}";
        }).ToArray();
        Assert.AreEqual(0, late.Length, $"Still not resting: {string.Join("; ", late)}");
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        return rested;
    }

    [TestMethod]
    [DataRow("performance.listen")]
    [DataRow("litter.goody-pickup")]
    [DataRow("medical.return")]
    public void AWalkerWhoseSpotIsUnderSomeoneStandingStopsBesideThemRatherThanShuffling(string intent)
    {
        // Someone stands on the cell; a walker comes from 5 m away to that very cell.
        var spot = new GridCell(Centre, Centre);
        var session = OpenGround([(new(Centre - 10, Centre), spot), (spot, spot)], out var ids, [intent, "test.stand"]);
        var walker = ids[0];
        var (jumps, arrivedAt) = Watch(session, walker, 600);
        var final = session.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id == walker);
        Assert.AreEqual(AgentNavigationAction.Arrived, final.Action, $"{intent}: still walking at ({final.XMillimetres},{final.ZMillimetres}).");
        Assert.AreEqual(spot, final.Destination, "Arriving a step short keeps the goal the systems asked for.");
        Assert.IsTrue(arrivedAt <= Ticks((new(Centre - 10, Centre), spot)) + 40, $"Arrived late, at tick {arrivedAt}.");
        Assert.AreEqual(0, jumps, "No sidestep jumps against the stander.");
        var centre = TraversalGrid.CellCentre(spot);
        var gap = Math.Sqrt(Math.Pow(final.XMillimetres - centre.XMillimetres, 2) + Math.Pow(final.ZMillimetres - centre.ZMillimetres, 2));
        Assert.IsTrue(gap >= GameSession.SeparationRadiusMillimetres && gap <= GameSession.NearArrivalMillimetres, $"Stopped {gap:0} mm from the spot.");
    }

    [TestMethod]
    public void AWalkerNeedingTheExactCellWaitsBesideTheStanderThenStepsOnWhenTheyLeave()
    {
        var spot = new GridCell(Centre, Centre);
        var session = OpenGround([(new(Centre - 10, Centre), spot), (spot, spot)], out var ids, ["toilet.queue", "test.stand"]);
        var (jumps, _) = Watch(session, ids[0], 600);
        var waiting = session.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id == ids[0]);
        Assert.AreEqual(AgentNavigationAction.Travelling, waiting.Action, "A queue place needs the exact cell, so they wait.");
        Assert.AreEqual(0, jumps, "Waiting, not shuffling.");
        // The stander walks off; the waiter steps onto the exact cell.
        typeof(GameSession).GetMethod("ApplyAgentDestination", Private)!.Invoke(session,
            [ids[1], new SetAgentDestinationCommand(new(Centre, Centre + 10), "test.stand"), false]);
        Watch(session, ids[0], 200);
        var done = session.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id == ids[0]);
        var centre = TraversalGrid.CellCentre(spot);
        Assert.AreEqual(AgentNavigationAction.Arrived, done.Action);
        Assert.AreEqual((centre.XMillimetres, centre.ZMillimetres), (done.XMillimetres, done.ZMillimetres));
    }

    [TestMethod]
    public void TwoWalkersSentToTheSameSpotBothSettleWithoutAKnot()
    {
        var spot = new GridCell(Centre, Centre);
        var session = OpenGround([(new(Centre - 8, Centre), spot), (new(Centre + 8, Centre + 1), spot)], out var ids,
            ["performance.listen", "performance.listen"]);
        var jumps = 0;
        for (var tick = 0; tick < 600; tick++)
        {
            var before = session.CaptureSnapshot().NavigationAgents.Where(agent => ids.Contains(agent.Id)).ToDictionary(agent => agent.Id);
            session.AdvanceWithoutSnapshot(1);
            foreach (var agent in session.CaptureSnapshot().NavigationAgents.Where(agent => ids.Contains(agent.Id)))
                if (Math.Abs(agent.XMillimetres - before[agent.Id].XMillimetres) + Math.Abs(agent.ZMillimetres - before[agent.Id].ZMillimetres) > 80) jumps++;
        }
        var agents = session.CaptureSnapshot().NavigationAgents.Where(agent => ids.Contains(agent.Id)).ToArray();
        Assert.IsTrue(agents.All(agent => agent.Action == AgentNavigationAction.Arrived), string.Join("; ", agents.Select(a => $"{a.Id.Value} {a.Action}")));
        Assert.AreEqual(0, jumps, "Whoever gets there second stops beside the first, without sidestep jumps.");
    }

    /// <summary>Advances a number of ticks, counting the walker's sidestep jumps and when it first arrived.</summary>
    private static (int Jumps, int ArrivedAt) Watch(GameSession session, EntityId walker, int ticks)
    {
        var jumps = 0; var arrivedAt = int.MaxValue;
        var last = session.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id == walker);
        for (var tick = 1; tick <= ticks; tick++)
        {
            session.AdvanceWithoutSnapshot(1);
            var now = session.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id == walker);
            if (Math.Abs(now.XMillimetres - last.XMillimetres) + Math.Abs(now.ZMillimetres - last.ZMillimetres) > 80) jumps++;
            if (now.Action == AgentNavigationAction.Arrived && arrivedAt == int.MaxValue) arrivedAt = tick;
            last = now;
        }
        return (jumps, arrivedAt);
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
    private static GameSession OpenGround((GridCell From, GridCell To)[] walks, out EntityId[] ids, string[]? intents = null)
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
            apply.Invoke(session, [list[index], new SetAgentDestinationCommand(walks[index].To, intents?[index] ?? "test.cross"), false]);
        }
        ids = list.ToArray();
        return session;
    }
}

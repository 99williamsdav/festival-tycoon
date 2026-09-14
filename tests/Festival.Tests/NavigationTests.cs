using Festival.Persistence;
using Festival.Simulation;
using Festival.Simulation.Fixtures;

namespace Festival.Tests;

[TestClass]
public sealed class NavigationTests
{
    [TestMethod]
    public void PathAvoidsBlockedCellsAndCannotCutDiagonalCorners()
    {
        var blocked = new[]
        {
            new TerrainCellOverride(new GridCell(11, 10), GroundSurface.Grass, false),
            new TerrainCellOverride(new GridCell(10, 11), GroundSurface.Grass, false),
        };
        var result = DeterministicPathfinder.FindPath(new TraversalGrid(blocked), new GridCell(10, 10), new GridCell(12, 12));
        Assert.IsTrue(result.Found);
        Assert.IsFalse(result.Path.Contains(new GridCell(11, 11)), "The diagonal between two blocked orthogonal neighbours must not be used.");
        Assert.IsTrue(result.Path.All(cell => !blocked.Any(item => item.Cell == cell)));
    }

    [TestMethod]
    public void UnreachableTargetIsBoundedNoRouteAndDoesNotTeleport()
    {
        var fixture = NavigationFixture.CreateGateToServiceSession();
        Assert.IsTrue(NavigationFixture.IssueAutonomousServiceIntent(fixture).IsAccepted);
        fixture.Session.AdvanceTicks(20);
        var before = fixture.Session.CaptureSnapshot().NavigationAgents.Single();
        var blocked = fixture.Session.TraversalGrid!.Overrides.Values.First(item => !item.IsWalkable).Cell;
        var result = fixture.Session.Execute(Envelope(fixture.Session, new CommandId(3), fixture.AgentId,
            new SetAgentDestinationCommand(blocked, "fixture.blocked-target")));
        var after = fixture.Session.CaptureSnapshot().NavigationAgents.Single();
        Assert.IsTrue(result.IsAccepted);
        Assert.AreEqual(AgentNavigationAction.NoRoute, after.Action);
        Assert.AreEqual(0, after.LastSearchExpandedNodes);
        Assert.AreEqual(before.XMillimetres, after.XMillimetres);
        Assert.AreEqual(before.ZMillimetres, after.ZMillimetres);
        fixture.Session.AdvanceTicks(1000);
        Assert.AreEqual(after, fixture.Session.CaptureSnapshot().NavigationAgents.Single());
    }

    [TestMethod]
    public void EqualCostRouteAndFinalStateAreStableAcrossTickBatching()
    {
        var first = NavigationFixture.CreateGateToServiceSession();
        var second = NavigationFixture.CreateGateToServiceSession();
        Assert.IsTrue(NavigationFixture.IssueAutonomousServiceIntent(first).IsAccepted);
        Assert.IsTrue(NavigationFixture.IssueAutonomousServiceIntent(second).IsAccepted);
        CollectionAssert.AreEqual(first.Session.CaptureSnapshot().NavigationAgents.Single().Route.ToArray(),
            second.Session.CaptureSnapshot().NavigationAgents.Single().Route.ToArray());
        first.Session.AdvanceTicks(600);
        for (var tick = 0; tick < 600; tick++) second.Session.AdvanceTicks(1);
        Assert.AreEqual(first.Session.CaptureSnapshot().AuthoritativeHash, second.Session.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void SixtyMetresTakesTwentyFiveRealSecondsAtOneX()
    {
        var session = new GameSession(77);
        var start = new GridCell(60, 100);
        var target = new GridCell(180, 100);
        var created = session.Execute(Envelope(session, new CommandId(1), null,
            new InitializeNavigationFixtureCommand(start, Array.Empty<TerrainCellOverride>())));
        Assert.IsTrue(created.IsAccepted);
        Assert.IsTrue(session.Execute(Envelope(session, new CommandId(2), created.TargetId,
            new SetAgentDestinationCommand(target, "fixture.pace"))).IsAccepted);
        session.AdvanceTicks(1999);
        Assert.AreEqual(AgentNavigationAction.Travelling, session.CaptureSnapshot().NavigationAgents.Single().Action);
        session.AdvanceTicks(1);
        Assert.AreEqual(AgentNavigationAction.Arrived, session.CaptureSnapshot().NavigationAgents.Single().Action);
    }

    [TestMethod]
    public void HalfTravelPersistenceResumesToIdenticalArrival()
    {
        var uninterrupted = NavigationFixture.CreateGateToServiceSession();
        Assert.IsTrue(NavigationFixture.IssueAutonomousServiceIntent(uninterrupted).IsAccepted);
        uninterrupted.Session.AdvanceTicks(200);
        var persisted = uninterrupted.Session.CapturePersistenceSnapshot();
        var restored = GameSession.Restore(persisted);
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(uninterrupted.Session.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
        uninterrupted.Session.AdvanceTicks(3000);
        restored.Session.AdvanceTicks(3000);
        Assert.AreEqual(AgentNavigationAction.Arrived, uninterrupted.Session.CaptureSnapshot().NavigationAgents.Single().Action);
        Assert.AreEqual(uninterrupted.Session.CaptureSnapshot().AuthoritativeHash, restored.Session.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void TerrainCostPreservesFractionalMovementRemainder()
    {
        var start = new GridCell(20, 20);
        var target = new GridCell(21, 20);
        var terrain = new[] { new TerrainCellOverride(target, GroundSurface.Grass, true, 1300) };
        var session = new GameSession(88);
        var created = session.Execute(Envelope(session, new CommandId(1), null, new InitializeNavigationFixtureCommand(start, terrain)));
        session.Execute(Envelope(session, new CommandId(2), created.TargetId, new SetAgentDestinationCommand(target, "fixture.terrain")));
        session.AdvanceTicks(1);
        Assert.AreNotEqual(0, session.CaptureSnapshot().NavigationAgents.Single().MovementRemainder);
    }

    [TestMethod]
    public void SameTargetRetargetKeepsContinuousForwardMovement()
    {
        var fixture = NavigationFixture.CreateGateToServiceSession();
        Assert.IsTrue(NavigationFixture.IssueAutonomousServiceIntent(fixture).IsAccepted);
        fixture.Session.AdvanceTicks(8);
        var before = fixture.Session.CaptureSnapshot().NavigationAgents.Single();
        Assert.IsTrue(fixture.Session.Execute(Envelope(fixture.Session, new CommandId(3), fixture.AgentId,
            new SetAgentDestinationCommand(fixture.ServiceCell, NavigationFixture.ServiceIntentId))).IsAccepted);
        fixture.Session.AdvanceTicks(1);
        var after = fixture.Session.CaptureSnapshot().NavigationAgents.Single();
        Assert.IsTrue(SquaredDistance(before, after) <= 31 * 31, "Retargeting must not snap to the occupied cell centre.");
        var target = TraversalGrid.CellCentre(fixture.ServiceCell);
        var progressDot = (after.XMillimetres - before.XMillimetres) * (target.XMillimetres - before.XMillimetres) +
            (after.ZMillimetres - before.ZMillimetres) * (target.ZMillimetres - before.ZMillimetres);
        Assert.IsTrue(progressDot > 0, "Reissuing the same intent must continue generally toward its destination.");
    }

    [TestMethod]
    public void DifferentTargetRetargetStartsAtCurrentAuthoritativePosition()
    {
        var fixture = NavigationFixture.CreateGateToServiceSession();
        Assert.IsTrue(NavigationFixture.IssueAutonomousServiceIntent(fixture).IsAccepted);
        fixture.Session.AdvanceTicks(8);
        var before = fixture.Session.CaptureSnapshot().NavigationAgents.Single();
        var alternative = TraversalGrid.WorldToCell(-10_000, 20_000);
        Assert.IsTrue(fixture.Session.Execute(Envelope(fixture.Session, new CommandId(3), fixture.AgentId,
            new SetAgentDestinationCommand(alternative, "ai.seek-alternative"))).IsAccepted);
        fixture.Session.AdvanceTicks(1);
        var after = fixture.Session.CaptureSnapshot().NavigationAgents.Single();
        Assert.IsTrue(SquaredDistance(before, after) <= 31 * 31, "A changed AI intent must turn without a position discontinuity.");
        var target = TraversalGrid.CellCentre(alternative);
        var progressDot = (after.XMillimetres - before.XMillimetres) * (target.XMillimetres - before.XMillimetres) +
            (after.ZMillimetres - before.ZMillimetres) * (target.ZMillimetres - before.ZMillimetres);
        Assert.IsTrue(progressDot > 0, "Movement after the turn must follow the replacement route.");
    }

    [TestMethod]
    public void SaveResumeImmediatelyAfterRetargetPreservesSegmentOrigin()
    {
        var fixture = NavigationFixture.CreateGateToServiceSession();
        Assert.IsTrue(NavigationFixture.IssueAutonomousServiceIntent(fixture).IsAccepted);
        fixture.Session.AdvanceTicks(8);
        Assert.IsTrue(fixture.Session.Execute(Envelope(fixture.Session, new CommandId(3), fixture.AgentId,
            new SetAgentDestinationCommand(fixture.ServiceCell, NavigationFixture.ServiceIntentId))).IsAccepted);
        var directory = Path.Combine(Path.GetTempPath(), $"festival-navigation-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var compatibility = new SaveCompatibility("test-build", "content-v1", "rules-v1");
            var save = SaveFileAdapter.SaveSlot(directory, "retarget", new SaveWriteRequest(
                fixture.Session, compatibility, "navigation-test", new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero)));
            Assert.IsTrue(save.IsSuccess, save.Error);
            var restored = SaveFileAdapter.LoadSlot(directory, "retarget", compatibility);
            Assert.IsTrue(restored.IsSuccess, restored.Error);
            Assert.AreEqual(fixture.Session.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
            fixture.Session.AdvanceTicks(600);
            restored.Session.AdvanceTicks(600);
            Assert.AreEqual(fixture.Session.CaptureSnapshot().AuthoritativeHash, restored.Session.CaptureSnapshot().AuthoritativeHash);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void GateToServiceRouteStructurallyDetoursAroundLargeBarn()
    {
        var fixture = NavigationFixture.CreateGateToServiceSession();
        Assert.IsTrue(NavigationFixture.IssueAutonomousServiceIntent(fixture).IsAccepted);
        var route = fixture.Session.CaptureSnapshot().NavigationAgents.Single().Route;
        var barnMin = TraversalGrid.WorldToCell(7_000, 5_000);
        var barnMax = TraversalGrid.WorldToCell(29_000, 19_000);
        bool IsBarn(GridCell cell) => cell.X >= barnMin.X && cell.X <= barnMax.X && cell.Z >= barnMin.Z && cell.Z <= barnMax.Z;
        var unobstructed = DeterministicPathfinder.FindPath(new TraversalGrid(), fixture.GateCell, fixture.ServiceCell);
        Assert.IsTrue(unobstructed.Path.Any(IsBarn), "The straight optimal route must intersect the barn footprint for this fixture to prove a detour.");
        Assert.IsFalse(route.Any(IsBarn));
        Assert.IsTrue(route.Any(cell => cell.Z >= barnMin.Z && cell.Z <= barnMax.Z && cell.X < barnMin.X),
            "The authoritative route must pass the west-side clearance before turning south of the barn.");
    }

    [TestMethod]
    public void CheapTerrainDetourRemainsOptimal()
    {
        var overrides = new List<TerrainCellOverride>();
        for (var z = 11; z <= 15; z++) overrides.Add(new TerrainCellOverride(new GridCell(10, z), GroundSurface.VehicleTrack, true, 100));
        for (var x = 11; x <= 30; x++) overrides.Add(new TerrainCellOverride(new GridCell(x, 15), GroundSurface.VehicleTrack, true, 100));
        for (var z = 10; z <= 14; z++) overrides.Add(new TerrainCellOverride(new GridCell(30, z), GroundSurface.VehicleTrack, true, 100));
        var grid = new TraversalGrid(overrides);
        var result = DeterministicPathfinder.FindPath(grid, new GridCell(10, 10), new GridCell(30, 10));
        Assert.IsTrue(result.Found);
        Assert.IsTrue(result.Path.Any(cell => cell.Z == 15), "The cheapest route must use the low-cost corridor even though it initially moves away from the target.");
        Assert.IsTrue(RouteCost(grid, result.Path) < 20_000, "The selected path must cost less than the direct all-grass route.");
    }

    private static long SquaredDistance(NavigationAgentSnapshot left, NavigationAgentSnapshot right)
    {
        var dx = right.XMillimetres - left.XMillimetres;
        var dz = right.ZMillimetres - left.ZMillimetres;
        return (long)dx * dx + (long)dz * dz;
    }

    private static int RouteCost(TraversalGrid grid, IReadOnlyList<GridCell> route)
    {
        var total = 0;
        for (var index = 1; index < route.Count; index++)
        {
            var diagonal = route[index - 1].X != route[index].X && route[index - 1].Z != route[index].Z;
            total += (diagonal ? 1414 : 1000) * grid.Get(route[index]).CostPermille / 1000;
        }
        return total;
    }

    private static CommandEnvelope Envelope(GameSession session, CommandId id, EntityId? target, SessionCommand command) =>
        new(id, session.CampaignId, session.Phase, session.CurrentTick, session.NextSubmissionSequence, target, command);
}

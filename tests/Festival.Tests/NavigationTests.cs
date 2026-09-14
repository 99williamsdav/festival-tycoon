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
        uninterrupted.Session.AdvanceTicks(1000);
        restored.Session.AdvanceTicks(1000);
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

    private static CommandEnvelope Envelope(GameSession session, CommandId id, EntityId? target, SessionCommand command) =>
        new(id, session.CampaignId, session.Phase, session.CurrentTick, session.NextSubmissionSequence, target, command);
}

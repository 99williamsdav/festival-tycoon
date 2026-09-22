using Festival.Simulation;
using Festival.Simulation.Fixtures;

namespace Festival.Tests;

[TestClass]
public sealed class SharedWorldFeasibilityTests
{
    [TestMethod]
    public void FixtureUsesOneWorldThreePhysicalDestinationsAndExplicitRetargets()
    {
        var fixture = SharedWorldFeasibilityFixture.Create();
        var snapshot = fixture.Session.CaptureSnapshot();
        Assert.AreEqual(50, snapshot.NavigationAgents.Count);
        Assert.AreEqual(50, snapshot.Wallets.Count);
        Assert.AreEqual(1, snapshot.FestivalFinances.Count);
        Assert.AreEqual(3, snapshot.ServiceQueues.Count);
        Assert.AreEqual(3, snapshot.OwnedStocks.Count);
        Assert.AreEqual(50, snapshot.ServiceQueues.Sum(queue => queue.Agents.Count));
        Assert.AreEqual(4, fixture.RetargetCount);
        Assert.AreEqual(4, fixture.Session.CapturePersistenceSnapshot().AppliedCommands.Count(command => command.CommandType == nameof(RetargetServiceQueueAgentFixtureCommand)));
        Assert.IsTrue(snapshot.ServiceQueues.All(queue => queue.PhysicalArrivalAdmission && queue.Agents.Count > 0));
        Assert.IsTrue(snapshot.ServiceQueues.SelectMany(queue => queue.Agents).All(agent => agent.Action == ServiceQueueAgentAction.ApproachingQueue));
        Assert.AreEqual(50, snapshot.ServiceQueues.SelectMany(queue => queue.Agents).Select(agent => agent.AgentId).Distinct().Count());
    }

    [TestMethod]
    public void SameTickOneXFourXAndRepeatAreAuthoritativelyEquivalent()
    {
        var one = SharedWorldFeasibilityFixture.Create();
        var four = SharedWorldFeasibilityFixture.Create();
        var repeat = SharedWorldFeasibilityFixture.Create();
        var oneClock = new FoundationClock { RequestedSpeed = RequestedSpeed.OneX };
        var fourClock = new FoundationClock { RequestedSpeed = RequestedSpeed.FourX };
        AdvanceClockTo(one.Session, oneClock, 900);
        AdvanceClockTo(four.Session, fourClock, 900);
        repeat.Session.AdvanceTicks(900);
        Assert.AreEqual(one.Session.CaptureSnapshot().AuthoritativeHash, four.Session.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(one.Session.CaptureSnapshot().AuthoritativeHash, repeat.Session.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void MixedTravelAndQueueSaveContinuesExactly()
    {
        var fixture = SharedWorldFeasibilityFixture.Create();
        SessionSnapshot mixed;
        do
        {
            fixture.Session.AdvanceTicks(1);
            mixed = fixture.Session.CaptureSnapshot();
        }
        while (mixed.CurrentTick < 5_000 && !(mixed.NavigationAgents.Any(agent => agent.Action == AgentNavigationAction.Travelling) &&
            mixed.ServiceQueues.Any(queue => queue.OrderedMembers.Count > 0)));
        Assert.IsTrue(mixed.NavigationAgents.Any(agent => agent.Action == AgentNavigationAction.Travelling));
        Assert.IsTrue(mixed.ServiceQueues.Any(queue => queue.OrderedMembers.Count > 0));
        var restored = GameSession.Restore(fixture.Session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(mixed.AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
        for (var tick = 0; tick < 600; tick++)
        {
            fixture.Session.AdvanceTicks(1);
            restored.Session.AdvanceTicks(1);
        }
        Assert.AreEqual(fixture.Session.CaptureSnapshot().AuthoritativeHash, restored.Session.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void MalformedMultiQueueCollectionsAndGlobalDuplicatesRejectWithoutThrowing()
    {
        var persisted = SharedWorldFeasibilityFixture.Create().Session.CapturePersistenceSnapshot();
        foreach (var malformedQueue in new[]
        {
            persisted.ServiceQueues![0] with { Agents = null! },
            persisted.ServiceQueues[0] with { QueueSlots = null! },
            persisted.ServiceQueues[0] with { ExitCells = null! },
            persisted.ServiceQueues[0] with { OrderedMembers = null! },
        })
        {
            var queues = persisted.ServiceQueues.ToArray(); queues[0] = malformedQueue;
            var result = GameSession.Restore(persisted with { ServiceQueues = queues });
            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains(result.Error!, "invalid identity, configuration or collections");
        }

        var duplicateAgentQueues = persisted.ServiceQueues!.ToArray();
        duplicateAgentQueues[1] = duplicateAgentQueues[1] with
        {
            Agents = duplicateAgentQueues[1].Agents.Append(duplicateAgentQueues[0].Agents[0]).OrderBy(agent => agent.AgentId).ToArray(),
        };
        var duplicateAgent = GameSession.Restore(persisted with { ServiceQueues = duplicateAgentQueues });
        Assert.IsFalse(duplicateAgent.IsSuccess);
        StringAssert.Contains(duplicateAgent.Error!, "more than one service destination");

        foreach (var duplicateExit in new[] { false, true })
        {
            var queues = persisted.ServiceQueues.ToArray();
            queues[1] = duplicateExit
                ? queues[1] with { ExitCells = [queues[0].ExitCells[0], .. queues[1].ExitCells.Skip(1)] }
                : queues[1] with { QueueSlots = [queues[0].QueueSlots[0], .. queues[1].QueueSlots.Skip(1)] };
            var result = GameSession.Restore(persisted with { ServiceQueues = queues });
            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains(result.Error!, "globally distinct");
        }
    }

    [TestMethod]
    public void RetargetCapacityRequiresPairedSlotsAndExitsAndRoundTripsUnequalSafeShape()
    {
        var terrain = NavigationFixture.CreateLowerWitteringTerrain();
        var grid = new TraversalGrid(terrain);
        var cells = Enumerable.Range(0, TraversalGrid.Width * TraversalGrid.Depth)
            .Select(index => new GridCell(index % TraversalGrid.Width, index / TraversalGrid.Width))
            .Where(cell => grid.Get(cell).IsWalkable).Take(12).ToArray();
        var session = new GameSession(44, new CampaignId(44));
        CommandResult Initialize(CommandId id, GridCell start, GridCell[] slots, GridCell[] exits) => session.Execute(new CommandEnvelope(
            id, session.CampaignId, session.Phase, session.CurrentTick, session.NextSubmissionSequence, null,
            new InitializeServiceQueueFixtureCommand([start], slots, exits, terrain, [500], 3, 120, 300, 10, true)));

        var source = Initialize(new CommandId(1), cells[0], [cells[1]], [cells[2]]);
        Assert.IsTrue(source.IsAccepted, source.Message);
        var beforeInvalid = session.CaptureSnapshot().AuthoritativeHash;
        var invalid = Initialize(new CommandId(2), cells[3], [cells[4]], [cells[5], cells[6]]);
        Assert.IsFalse(invalid.IsAccepted);
        Assert.AreEqual(beforeInvalid, session.CaptureSnapshot().AuthoritativeHash, "Rejected incoherent capacity must not mutate state.");

        var destination = Initialize(new CommandId(3), cells[3], [cells[4], cells[7], cells[8]], [cells[5], cells[6]]);
        Assert.IsTrue(destination.IsAccepted, destination.Message);
        var sourceAgent = session.CaptureSnapshot().ServiceQueues.Single(queue => queue.Id == source.TargetId).Agents.Single().AgentId;
        var retarget = session.Execute(new CommandEnvelope(new CommandId(4), session.CampaignId, session.Phase,
            session.CurrentTick, session.NextSubmissionSequence, source.TargetId,
            new RetargetServiceQueueAgentFixtureCommand(sourceAgent, destination.TargetId!.Value)));
        Assert.IsTrue(retarget.IsAccepted, retarget.Message);
        var destinationSnapshot = session.CaptureSnapshot().ServiceQueues.Single(queue => queue.Id == destination.TargetId);
        var moved = destinationSnapshot.Agents.Single(agent => agent.AgentId == sourceAgent);
        Assert.IsTrue(moved.ExitIndex < destinationSnapshot.ExitCells.Count && moved.ExitIndex < destinationSnapshot.QueueSlots.Count);

        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void SharedWorldCompletesWithSafeSweepsAndReconciledOwnership()
    {
        var fixture = SharedWorldFeasibilityFixture.Create();
        var previous = fixture.Session.CaptureSnapshot().NavigationAgents.ToDictionary(agent => agent.Id);
        var recoveryEvents = 0;
        while (!SharedWorldFeasibilityFixture.AllCompleted(fixture))
        {
            var advanced = fixture.Session.AdvanceTicks(1);
            var snapshot = advanced.Snapshot;
            recoveryEvents += advanced.Events.Count(item => item.EventType.Contains("recover", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(snapshot.CurrentTick < 45_000, "Shared-world completion exceeded its deterministic budget.");
            Assert.AreEqual(snapshot.NavigationAgents.Count,
                snapshot.NavigationAgents.Select(agent => (agent.XMillimetres, agent.ZMillimetres)).Distinct().Count(),
                $"Exact attendee overlap at tick {snapshot.CurrentTick}.");
            foreach (var agent in snapshot.NavigationAgents)
            {
                var prior = previous[agent.Id];
                Assert.IsTrue(TraversalSweep.IsWalkable(fixture.Session.TraversalGrid!, prior.XMillimetres, prior.ZMillimetres,
                    agent.XMillimetres, agent.ZMillimetres), $"Illegal swept segment for {agent.Id} at tick {snapshot.CurrentTick}.");
                Assert.IsTrue(fixture.Session.TraversalGrid!.Get(TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres)).IsWalkable);
            }
            previous = snapshot.NavigationAgents.ToDictionary(agent => agent.Id);
        }
        var final = fixture.Session.CaptureSnapshot();
        Assert.AreEqual(0, recoveryEvents);
        Assert.AreEqual(50, final.Transactions.Count);
        Assert.AreEqual(15_000L, final.FestivalFinances.Single().CashPennies);
        Assert.AreEqual(100, final.OwnedStocks.Sum(stock => stock.Quantity));
        Assert.IsTrue(final.ServiceQueues.All(queue => queue.OrderedMembers.Count == 0 && queue.ActiveOwnerId is null &&
            queue.Agents.All(agent => agent.ReservedSlotIndex is null && !agent.OwnsExitReservation && agent.Action == ServiceQueueAgentAction.Completed)));
    }

    private static void AdvanceClockTo(GameSession session, FoundationClock clock, int targetTick)
    {
        while (session.CurrentTick < targetTick)
            session.AdvanceTicks(Math.Min(clock.Schedule(1.0 / 60), targetTick - (int)session.CurrentTick));
    }
}

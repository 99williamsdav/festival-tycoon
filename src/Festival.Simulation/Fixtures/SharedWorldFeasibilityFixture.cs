namespace Festival.Simulation.Fixtures;

public sealed record SharedWorldFeasibilityFixtureState(
    GameSession Session,
    IReadOnlyList<EntityId> QueueIds,
    EntityId FestivalId,
    IReadOnlyList<EntityId> AgentIds,
    IReadOnlyList<GridCell> SpawnCells,
    IReadOnlyList<GridCell> ServiceVisualCells,
    int RetargetCount);

/// <summary>
/// M1.00 development fixture. All people and destinations share one production navigation,
/// occupancy and queue world. Initial choices and retargets are deterministic attendee-AI
/// fixtures, not player controls or the later M1 needs model.
/// </summary>
public static class SharedWorldFeasibilityFixture
{
    public const int AgentCount = 50;
    public const int DestinationCount = 3;
    // Keeps every declared attendee active through the bounded representative render samples.
    public const int ServiceDurationTicks = 2_000;
    public const ulong Seed = 20260922;

    public static SharedWorldFeasibilityFixtureState Create()
    {
        var terrain = NavigationFixture.CreateLowerWitteringTerrain();
        var grid = new TraversalGrid(terrain);
        var used = new HashSet<GridCell>();
        // One entrance region immediately inside the inherited north gate.
        var allStarts = PickWalkable(grid, used, AgentCount,
            index => 112 + index % 17 * 2, index => 184 + index / 17 * 2);
        var serviceCells = new[] { new GridCell(186, 94), new GridCell(186, 118), new GridCell(186, 142) };
        var session = new GameSession(Seed, new CampaignId(Seed));
        var queueIds = new List<EntityId>();
        var offset = 0;
        for (var destination = 0; destination < DestinationCount; destination++)
        {
            var count = AgentCount / DestinationCount + (destination < AgentCount % DestinationCount ? 1 : 0);
            var starts = allStarts.Skip(offset).Take(count).ToArray();
            offset += count;
            var anchor = serviceCells[destination];
            // Capacity for all 50 permits deterministic pre-admission retargets while each
            // destination retains a distinct physical queue and exit allocation.
            var slots = PickWalkable(grid, used, AgentCount,
                index => anchor.X - 5 - (index / 10) * 2,
                index => anchor.Z - 9 + (index % 10) * 2);
            var exits = PickWalkable(grid, used, AgentCount,
                index => 106 + index % 25 * 2,
                index => 190 + destination * 5 + index / 25 * 2);
            var command = new InitializeServiceQueueFixtureCommand(
                starts, slots, exits, terrain, Enumerable.Repeat(500L, count).ToArray(),
                AgentCount, 120, ServiceQueueFixture.DefaultPricePennies, ServiceDurationTicks,
                PhysicalArrivalAdmission: true);
            var result = Execute(session, new CommandId((ulong)destination + 1), null, command);
            if (!result.IsAccepted || result.TargetId is null) throw new InvalidOperationException(result.Message);
            queueIds.Add(result.TargetId.Value);
        }

        // Explicit fixture-only reconsideration before physical admission. Production queue
        // ownership/navigation performs the actual transfer and subsequent service lifecycle.
        var firstQueueAgents = session.CaptureSnapshot().ServiceQueues.Single(queue => queue.Id == queueIds[0]).Agents
            .Select(agent => agent.AgentId).Take(4).ToArray();
        for (var index = 0; index < firstQueueAgents.Length; index++)
        {
            var destination = queueIds[1 + index % 2];
            var result = Execute(session, new CommandId((ulong)DestinationCount + 1UL + (ulong)index), queueIds[0],
                new RetargetServiceQueueAgentFixtureCommand(firstQueueAgents[index], destination));
            if (!result.IsAccepted) throw new InvalidOperationException(result.Message);
        }

        var snapshot = session.CaptureSnapshot();
        return new(session, queueIds, snapshot.FestivalFinances.Single().OwnerId,
            snapshot.NavigationAgents.Select(agent => agent.Id).ToArray(), allStarts, serviceCells, firstQueueAgents.Length);
    }

    public static bool AllCompleted(SharedWorldFeasibilityFixtureState fixture)
    {
        var queues = fixture.Session.CaptureSnapshot().ServiceQueues;
        return fixture.Session.Transactions.Count == AgentCount &&
            queues.Sum(queue => queue.Agents.Count) == AgentCount &&
            queues.All(queue => queue.Agents.All(agent => agent.Action == ServiceQueueAgentAction.Completed));
    }

    public static bool IsRepresentativeActiveState(SharedWorldFeasibilityFixtureState fixture)
    {
        var snapshot = fixture.Session.CaptureSnapshot();
        return snapshot.NavigationAgents.Count == AgentCount &&
            snapshot.ServiceQueues.Count == DestinationCount &&
            snapshot.ServiceQueues.All(queue => queue.OrderedMembers.Count > 0 || queue.ActiveOwnerId is not null) &&
            snapshot.ServiceQueues.SelectMany(queue => queue.Agents)
                .All(agent => agent.Action is not ServiceQueueAgentAction.Failed and not ServiceQueueAgentAction.Completed) &&
            snapshot.NavigationAgents.All(agent => agent.Action != AgentNavigationAction.Idle);
    }

    public static void AdvanceUntilCompleted(SharedWorldFeasibilityFixtureState fixture, int maximumTicks = 45_000)
    {
        for (var tick = 0; tick < maximumTicks && !AllCompleted(fixture); tick++) fixture.Session.AdvanceTicks(1);
        if (!AllCompleted(fixture)) throw new InvalidOperationException("Shared-world fixture did not complete within its deterministic budget.");
    }

    private static CommandResult Execute(GameSession session, CommandId id, EntityId? target, SessionCommand command) =>
        session.Execute(new CommandEnvelope(id, session.CampaignId, session.Phase, session.CurrentTick,
            session.NextSubmissionSequence, target, command));

    private static GridCell[] PickWalkable(TraversalGrid grid, HashSet<GridCell> used, int count,
        Func<int, int> preferredX, Func<int, int> preferredZ)
    {
        var result = new List<GridCell>(count);
        for (var index = 0; index < count; index++)
        {
            var preferred = new GridCell(preferredX(index), preferredZ(index));
            GridCell? selected = null;
            for (var radius = 0; radius < 48 && selected is null; radius++)
            for (var dz = -radius; dz <= radius && selected is null; dz++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius) continue;
                var candidate = new GridCell(preferred.X + dx, preferred.Z + dz);
                if (grid.Contains(candidate) && grid.Get(candidate).IsWalkable && used.Add(candidate))
                { selected = candidate; break; }
            }
            result.Add(selected ?? throw new InvalidOperationException("Could not allocate a unique shared-world fixture cell."));
        }
        return result.ToArray();
    }
}

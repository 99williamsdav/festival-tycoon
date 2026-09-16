namespace Festival.Simulation.Fixtures;

public sealed record FiftyAgentFoundationFixtureState(
    GameSession Session, EntityId QueueId, EntityId FestivalId, EntityId ServiceId,
    IReadOnlyList<EntityId> AgentIds, IReadOnlyList<GridCell> SpawnCells);

/// <summary>Development-only M0.09 stress scene. Intentions are attendee AI fixtures, never player controls.</summary>
public static class FiftyAgentFoundationFixture
{
    public const int AgentCount = 50;
    public const int ServiceDurationTicks = 24;

    public static FiftyAgentFoundationFixtureState Create()
    {
        var terrain = NavigationFixture.CreateLowerWitteringTerrain();
        var grid = new TraversalGrid(terrain);
        var used = new HashSet<GridCell>();
        var starts = PickWalkable(grid, used, 50, x => 108 + x % 20 * 2, x => 184 + x / 20 * 2);
        // The counter is centred at grid 186,118. Start 2.5 m west of it (just outside the
        // service-point footprint) and snake a compact, readable admission/queue line nearby.
        var slots = PickWalkable(grid, used, 50,
            x => 181 - 2 * (x / 10 % 2 == 0 ? x % 10 : 9 - x % 10),
            x => 118 + x / 10 * 2);
        var exits = PickWalkable(grid, used, 50, x => 104 + x % 25 * 2, x => 188 + x / 25 * 2);
        var command = new InitializeServiceQueueFixtureCommand(
            starts, slots, exits, terrain, Enumerable.Repeat(500L, AgentCount).ToArray(),
            AgentCount, 120, ServiceQueueFixture.DefaultPricePennies, ServiceDurationTicks, PhysicalArrivalAdmission: true);
        var session = new GameSession(20260915, new CampaignId(20260915));
        var result = session.Execute(new CommandEnvelope(new CommandId(1), session.CampaignId, session.Phase,
            session.CurrentTick, session.NextSubmissionSequence, null, command));
        if (!result.IsAccepted) throw new InvalidOperationException(result.Message);
        var queue = session.CaptureSnapshot().ServiceQueues.Single();
        return new(session, queue.Id, queue.FestivalId, queue.ServiceId, queue.Agents.Select(item => item.AgentId).ToArray(), starts);
    }

    public static bool AllCompleted(FiftyAgentFoundationFixtureState fixture)
    {
        var queue = fixture.Session.ServiceQueues[fixture.QueueId];
        return fixture.Session.Transactions.Count == AgentCount && queue.Agents.Values.All(item => item.Action == ServiceQueueAgentAction.Completed);
    }

    public static void AdvanceUntilCompleted(FiftyAgentFoundationFixtureState fixture, int maximumTicks = 30_000)
    {
        for (var tick = 0; tick < maximumTicks && !AllCompleted(fixture); tick++) fixture.Session.AdvanceTicks(1);
        if (!AllCompleted(fixture)) throw new InvalidOperationException("Fifty-agent fixture did not finish within its deterministic budget.");
    }

    private static GridCell[] PickWalkable(TraversalGrid grid, HashSet<GridCell> used, int count, Func<int,int> preferredX, Func<int,int> preferredZ)
    {
        var result = new List<GridCell>();
        for (var index = 0; index < count; index++)
        {
            var preferred = new GridCell(preferredX(index), preferredZ(index));
            GridCell? selected = null;
            for (var radius = 0; radius < 40 && selected is null; radius++)
            for (var dz = -radius; dz <= radius && selected is null; dz++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius) continue;
                var candidate = new GridCell(preferred.X + dx, preferred.Z + dz);
                if (grid.Contains(candidate) && grid.Get(candidate).IsWalkable && used.Add(candidate)) { selected = candidate; break; }
            }
            result.Add(selected ?? throw new InvalidOperationException("Could not allocate a unique walkable fixture cell."));
        }
        return result.ToArray();
    }
}

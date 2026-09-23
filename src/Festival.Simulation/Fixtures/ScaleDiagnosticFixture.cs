namespace Festival.Simulation.Fixtures;

public enum ScaleDiagnosticLayout { Representative, Congested }

public sealed record ScaleDiagnosticFixtureState(
    GameSession Session, int Population, ulong Seed, ScaleDiagnosticLayout Layout,
    IReadOnlyList<EntityId> QueueIds, IReadOnlyList<EntityId> AgentIds,
    IReadOnlyList<GridCell> ServiceVisualCells, int RetargetCount);

/// <summary>S0.00-only one-world scale fixture; not player-facing content or attendee AI.</summary>
public static class ScaleDiagnosticFixture
{
    public const int DestinationCount = 3;
    public const int ServiceDurationTicks = 500;

    public static ScaleDiagnosticFixtureState Create(int population, ulong seed, ScaleDiagnosticLayout layout,
        ScaleDiagnosticProbe? probe = null)
    {
        if (population is not (50 or 100 or 200))
            throw new ArgumentOutOfRangeException(nameof(population), "S0.00 is bounded to 50, 100 or 200 people.");

        var terrain = CreateTerrain(layout);
        var grid = new TraversalGrid(terrain);
        var used = new HashSet<GridCell>();
        var starts = PickWalkable(grid, used, population,
            index => 100 + index % 20, index => 176 + index / 20);
        var anchors = layout == ScaleDiagnosticLayout.Representative
            ? new[] { new GridCell(186, 92), new GridCell(186, 120), new GridCell(186, 148) }
            : new[] { new GridCell(174, 104), new GridCell(174, 120), new GridCell(174, 136) };
        var counts = Enumerable.Range(0, DestinationCount)
            .Select(destination => population / DestinationCount + (destination < population % DestinationCount ? 1 : 0)).ToArray();
        var retargetCount = Math.Min(6, counts[0] / 4);
        var session = new GameSession(seed, new CampaignId(seed));
        session.ScaleDiagnosticProbe = probe;
        var queueIds = new List<EntityId>();
        var offset = 0;
        for (var destination = 0; destination < DestinationCount; destination++)
        {
            var startsForQueue = starts.Skip(offset).Take(counts[destination]).ToArray();
            offset += counts[destination];
            var capacity = counts[destination] + (destination == 0 ? 0 : retargetCount);
            var anchor = anchors[destination];
            GridCell[] slots;
            GridCell[] exits;
            if (layout == ScaleDiagnosticLayout.Representative)
            {
                slots = PickWalkable(grid, used, capacity,
                    index => anchor.X - 4 - index / 10 * 2,
                    index => anchor.Z - 9 + index % 10 * 2);
                exits = PickWalkable(grid, used, capacity,
                    index => 240 + index % 7 * 2,
                    index => anchor.Z - 5 + index / 7 * 2);
            }
            else
            {
                // Three compact east-side holding pens reached through the same narrow opening.
                slots = PickWalkable(grid, used, capacity,
                    index => 154 + index % 9 * 2,
                    index => anchor.Z - 7 + index / 9 * 2);
                exits = PickWalkable(grid, used, capacity,
                    index => 240 + index % 7 * 2,
                    index => anchor.Z - 5 + index / 7 * 2);
            }
            var command = new InitializeServiceQueueFixtureCommand(
                startsForQueue, slots, exits, terrain, Enumerable.Repeat(500L, counts[destination]).ToArray(),
                population, 120, ServiceQueueFixture.DefaultPricePennies, ServiceDurationTicks,
                PhysicalArrivalAdmission: true);
            var result = Execute(session, new CommandId((ulong)destination + 1), null, command);
            if (!result.IsAccepted || result.TargetId is null) throw new InvalidOperationException(result.Message);
            queueIds.Add(result.TargetId.Value);
        }

        var firstQueueAgents = session.CaptureSnapshot().ServiceQueues.Single(queue => queue.Id == queueIds[0]).Agents
            .Select(agent => agent.AgentId).Take(retargetCount).ToArray();
        for (var index = 0; index < firstQueueAgents.Length; index++)
        {
            var destination = queueIds[1 + index % 2];
            var result = Execute(session, new CommandId((ulong)DestinationCount + 1UL + (ulong)index), queueIds[0],
                new RetargetServiceQueueAgentFixtureCommand(firstQueueAgents[index], destination));
            if (!result.IsAccepted) throw new InvalidOperationException(result.Message);
        }

        var snapshot = session.CaptureSnapshot();
        return new(session, population, seed, layout, queueIds,
            snapshot.NavigationAgents.Select(agent => agent.Id).ToArray(), anchors, retargetCount);
    }

    public static bool AllDeclaredAgentsPresentAndActive(ScaleDiagnosticFixtureState fixture)
    {
        var snapshot = fixture.Session.CaptureSnapshot();
        return snapshot.NavigationAgents.Count == fixture.Population && snapshot.Wallets.Count == fixture.Population &&
            snapshot.ServiceQueues.SelectMany(queue => queue.Agents).Select(agent => agent.AgentId).Distinct().Count() == fixture.Population &&
            snapshot.NavigationAgents.All(agent => agent.Action != AgentNavigationAction.Idle) &&
            snapshot.ServiceQueues.SelectMany(queue => queue.Agents).All(agent =>
                agent.Action is not ServiceQueueAgentAction.Failed and not ServiceQueueAgentAction.Completed);
    }

    public static bool HasCongestedServiceState(ScaleDiagnosticFixtureState fixture)
    {
        var snapshot = fixture.Session.CaptureSnapshot();
        return snapshot.Transactions.Count == 0 &&
            snapshot.ServiceQueues.All(queue => queue.OrderedMembers.Count > 0 || queue.ActiveOwnerId is not null);
    }

    private static IReadOnlyList<TerrainCellOverride> CreateTerrain(ScaleDiagnosticLayout layout)
    {
        var cells = NavigationFixture.CreateLowerWitteringTerrain().ToDictionary(item => item.Cell);
        if (layout == ScaleDiagnosticLayout.Congested)
        {
            // A two-cell gate in a north/south fence forces the entrance traffic into one corridor.
            for (var z = 70; z <= 170; z++)
            {
                if (z is 119 or 120) continue;
                var cell = new GridCell(150, z);
                cells[cell] = new TerrainCellOverride(cell, GroundSurface.Grass, false);
            }
        }
        return cells.Values.OrderBy(item => item.Cell).ToArray();
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
            for (var radius = 0; radius < 64 && selected is null; radius++)
            for (var dz = -radius; dz <= radius && selected is null; dz++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius) continue;
                var candidate = new GridCell(preferred.X + dx, preferred.Z + dz);
                if (grid.Contains(candidate) && grid.Get(candidate).IsWalkable && used.Add(candidate))
                { selected = candidate; break; }
            }
            result.Add(selected ?? throw new InvalidOperationException("Could not allocate a unique S0.00 fixture cell."));
        }
        return result.ToArray();
    }
}

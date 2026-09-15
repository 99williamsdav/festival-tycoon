namespace Festival.Simulation;

public sealed partial class GameSession
{
    public const int WalkingSpeedMillimetresPerFestivalSecond = 120;
    private const int RouteProgressMicrometresPerTick = 30_000;
    // Prototype centre clearance: below the 500 mm queue-slot spacing, but large enough to
    // prevent sustained near-superposition while still allowing compressed single-file flow.
    public const int SeparationRadiusMillimetres = 300;
    private readonly SortedDictionary<EntityId, NavigationAgentState> _navigationAgents = [];
    private TraversalGrid? _traversalGrid;

    public TraversalGrid? TraversalGrid => _traversalGrid;
    internal IReadOnlyDictionary<EntityId, NavigationAgentState> NavigationAgents => _navigationAgents;

    private CommandResult? ValidateInitializeNavigation(EntityId? targetId, InitializeNavigationFixtureCommand command)
    {
        if (targetId is not null) return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Navigation fixture creation does not accept a target.");
        if (_traversalGrid is not null) return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "A traversal grid already exists.");
        TraversalGrid grid;
        try { grid = new TraversalGrid(command.Terrain); }
        catch (ArgumentException exception) { return CommandResult.Rejected(CommandReasonCode.InvalidParameter, exception.Message); }
        if (!grid.Contains(command.Start) || !grid.Get(command.Start).IsWalkable)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Navigation agent start must be a walkable in-bounds cell.");
        return null;
    }

    private CommandResult? ValidateAgentDestination(EntityId? targetId, SetAgentDestinationCommand command)
    {
        if (targetId is null || !_navigationAgents.ContainsKey(targetId.Value))
            return CommandResult.Rejected(CommandReasonCode.UnknownTarget, "Navigation attendee does not exist.");
        if (_traversalGrid is null || !_traversalGrid.Contains(command.Destination) || string.IsNullOrWhiteSpace(command.IntentId))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "AI destination requires an in-bounds cell and intent ID.");
        return null;
    }

    private EntityId ApplyInitializeNavigation(InitializeNavigationFixtureCommand command)
    {
        _traversalGrid = new TraversalGrid(command.Terrain);
        var id = new EntityId(NextEntityId++);
        var position = TraversalGrid.CellCentre(command.Start);
        _navigationAgents.Add(id, new NavigationAgentState
        {
            Id = id, XMillimetres = position.XMillimetres, ZMillimetres = position.ZMillimetres,
            SegmentOriginXMillimetres = position.XMillimetres, SegmentOriginZMillimetres = position.ZMillimetres,
            Action = AgentNavigationAction.Idle,
        });
        return id;
    }

    private void ApplyAgentDestination(EntityId agentId, SetAgentDestinationCommand command)
    {
        var agent = _navigationAgents[agentId];
        var start = TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres);
        var search = DeterministicPathfinder.FindPath(_traversalGrid!, start, command.Destination);
        agent.Destination = command.Destination;
        agent.Route = search.Path.ToList();
        agent.RouteIndex = search.Found && search.Path.Count > 1 ? 1 : 0;
        agent.SegmentOriginXMillimetres = agent.XMillimetres;
        agent.SegmentOriginZMillimetres = agent.ZMillimetres;
        agent.SegmentProgressMicrometres = 0;
        agent.MovementRemainder = 0;
        agent.LastSearchExpandedNodes = search.ExpandedNodes;
        agent.IntentId = command.IntentId;
        agent.Action = !search.Found ? AgentNavigationAction.NoRoute
            : search.Path.Count == 1 ? AgentNavigationAction.Arrived : AgentNavigationAction.Travelling;
    }

    private void AdvanceNavigation(List<SessionEvent> events)
    {
        if (_traversalGrid is null) return;
        var moving = _navigationAgents.Values.Where(item => item.Action == AgentNavigationAction.Travelling).ToArray();
        var backups = moving.ToDictionary(item => item.Id, MovementBackup.Capture);
        var arrived = new HashSet<EntityId>();
        foreach (var agent in moving)
        {
            if (AdvanceAgentOneTick(agent)) arrived.Add(agent.Id);
        }

        // Resolve proposals together against stationary and already accepted occupancy through
        // the spatial index. Remaining-corridor progress then stable ID is the deterministic tie.
        // A deterministic lateral offset preserves route progress while giving proposals a
        // meaningful prototype centre clearance. This is steering/yielding, not body physics.
        var occupied = new SpatialNeighbourIndex();
        foreach (var agent in _navigationAgents.Values.Where(item => !backups.ContainsKey(item.Id)))
            occupied.Add(agent.Id, agent.XMillimetres, agent.ZMillimetres);
        foreach (var agent in moving.OrderBy(item => item.Route.Count - item.RouteIndex).ThenBy(item => item.Id))
        {
            if ((!TraversalSweep.IsWalkable(_traversalGrid, backups[agent.Id].X, backups[agent.Id].Z, agent.XMillimetres, agent.ZMillimetres) ||
                 HasConflict(occupied, agent.XMillimetres, agent.ZMillimetres)) &&
                !TryApplySeparationOffset(agent, backups[agent.Id], occupied))
            {
                backups[agent.Id].Restore(agent);
                arrived.Remove(agent.Id);
                if (HasConflict(occupied, agent.XMillimetres, agent.ZMillimetres) &&
                    !TryApplySeparationOffset(agent, backups[agent.Id], occupied))
                    throw new InvalidOperationException($"No deterministic non-overlapping movement position exists for attendee {agent.Id}.");
            }
            occupied.Add(agent.Id, agent.XMillimetres, agent.ZMillimetres);
        }
        foreach (var id in arrived.Order()) events.Add(new SessionEvent(CurrentTick, "navigation_arrived", id));
    }

    private bool AdvanceAgentOneTick(NavigationAgentState agent)
    {
            var terrainCost = _traversalGrid!.Get(agent.Route[agent.RouteIndex]).CostPermille;
            var numerator = checked(RouteProgressMicrometresPerTick * 1000 + agent.MovementRemainder);
            var allowance = numerator / terrainCost;
            agent.MovementRemainder = numerator % terrainCost;
            var arrived = false;

            while (allowance > 0 && agent.Action == AgentNavigationAction.Travelling)
            {
                var from = (XMillimetres: agent.SegmentOriginXMillimetres, ZMillimetres: agent.SegmentOriginZMillimetres);
                var to = TraversalGrid.CellCentre(agent.Route[agent.RouteIndex]);
                var dx = to.XMillimetres - from.XMillimetres;
                var dz = to.ZMillimetres - from.ZMillimetres;
                var segmentLength = checked((int)IntegerSquareRoot((long)dx * dx * 1_000_000L + (long)dz * dz * 1_000_000L));
                var remaining = segmentLength - agent.SegmentProgressMicrometres;
                var consumed = Math.Min(allowance, remaining);
                agent.SegmentProgressMicrometres += consumed;
                allowance -= consumed;

                if (agent.SegmentProgressMicrometres >= segmentLength)
                {
                    agent.XMillimetres = to.XMillimetres;
                    agent.ZMillimetres = to.ZMillimetres;
                    agent.SegmentOriginXMillimetres = to.XMillimetres;
                    agent.SegmentOriginZMillimetres = to.ZMillimetres;
                    agent.RouteIndex++;
                    agent.SegmentProgressMicrometres = 0;
                    if (agent.RouteIndex >= agent.Route.Count)
                    {
                        agent.RouteIndex = agent.Route.Count - 1;
                        agent.Action = AgentNavigationAction.Arrived;
                        arrived = true;
                    }
                }
                else
                {
                    agent.XMillimetres = from.XMillimetres + (int)((long)dx * agent.SegmentProgressMicrometres / segmentLength);
                    agent.ZMillimetres = from.ZMillimetres + (int)((long)dz * agent.SegmentProgressMicrometres / segmentLength);
                }
            }
            return arrived;
    }

    private static bool HasConflict(SpatialNeighbourIndex occupied, int x, int z) =>
        occupied.Query(x, z, SeparationRadiusMillimetres - 1).Count > 0;

    private bool TryApplySeparationOffset(NavigationAgentState agent, MovementBackup backup, SpatialNeighbourIndex occupied)
    {
        var dx = agent.XMillimetres - backup.X;
        var dz = agent.ZMillimetres - backup.Z;
        var lateralX = dz == 0 ? 0 : Math.Sign(dz);
        var lateralZ = dx == 0 ? (lateralX == 0 ? 1 : 0) : -Math.Sign(dx);
        var preferred = (agent.Id.Value & 1UL) == 0 ? 1 : -1;
        for (var distance = SeparationRadiusMillimetres; distance <= 1_200; distance += SeparationRadiusMillimetres)
        foreach (var side in new[] { preferred, -preferred })
        {
            var x = agent.XMillimetres + lateralX * distance * side;
            var z = agent.ZMillimetres + lateralZ * distance * side;
            var cell = TraversalGrid.WorldToCell(x, z);
            if (!_traversalGrid!.Contains(cell) || !_traversalGrid.Get(cell).IsWalkable ||
                !TraversalSweep.IsWalkable(_traversalGrid, backup.X, backup.Z, x, z) || HasConflict(occupied, x, z)) continue;
            agent.XMillimetres = x; agent.ZMillimetres = z;
            return true;
        }
        return false;
    }

    private sealed record MovementBackup(
        int X, int Z, AgentNavigationAction Action, int RouteIndex, int OriginX, int OriginZ,
        int Progress, int Remainder)
    {
        public static MovementBackup Capture(NavigationAgentState agent) => new(agent.XMillimetres, agent.ZMillimetres,
            agent.Action, agent.RouteIndex, agent.SegmentOriginXMillimetres, agent.SegmentOriginZMillimetres,
            agent.SegmentProgressMicrometres, agent.MovementRemainder);
        public void Restore(NavigationAgentState agent)
        {
            agent.XMillimetres = X; agent.ZMillimetres = Z; agent.Action = Action; agent.RouteIndex = RouteIndex;
            agent.SegmentOriginXMillimetres = OriginX; agent.SegmentOriginZMillimetres = OriginZ;
            agent.SegmentProgressMicrometres = Progress; agent.MovementRemainder = Remainder;
        }
    }

    private NavigationAgentSnapshot[] CaptureNavigationAgents() => _navigationAgents.Values.Select(agent => new NavigationAgentSnapshot(
        agent.Id, agent.XMillimetres, agent.ZMillimetres, agent.Action, agent.Destination,
        agent.Route.ToArray(), agent.RouteIndex, agent.SegmentOriginXMillimetres, agent.SegmentOriginZMillimetres,
        agent.SegmentProgressMicrometres,
        agent.MovementRemainder, agent.LastSearchExpandedNodes, agent.IntentId)).ToArray();

    private PersistedTraversalGrid? CaptureTraversalGrid() => _traversalGrid is null ? null : new PersistedTraversalGrid(
        TraversalGrid.Width, TraversalGrid.Depth, TraversalGrid.CellSizeMillimetres,
        _traversalGrid.Overrides.Values.Select(item => new PersistedTerrainCell(
            item.Cell.X, item.Cell.Z, (int)item.Surface, item.IsWalkable,
            item.CostPermille, item.ElevationMillimetres, item.SlopePermille)).ToArray());

    private PersistedNavigationAgent[]? CapturePersistedNavigationAgents() => _traversalGrid is null ? null :
        _navigationAgents.Values.Select(agent => new PersistedNavigationAgent(
            agent.Id.Value, agent.XMillimetres, agent.ZMillimetres, (int)agent.Action,
            agent.Destination?.X, agent.Destination?.Z,
            agent.Route.Select(cell => new PersistedGridCell(cell.X, cell.Z)).ToArray(),
            agent.RouteIndex, agent.SegmentOriginXMillimetres, agent.SegmentOriginZMillimetres,
            agent.SegmentProgressMicrometres, agent.MovementRemainder,
            agent.LastSearchExpandedNodes, agent.IntentId)).ToArray();

    private void RestoreNavigation(PersistedTraversalGrid? grid, PersistedNavigationAgent[]? agents)
    {
        _navigationAgents.Clear();
        if (grid is null) { _traversalGrid = null; return; }
        _traversalGrid = new TraversalGrid(grid.Cells.Select(item => new TerrainCellOverride(
            new GridCell(item.X, item.Z), (GroundSurface)item.Surface, item.IsWalkable,
            item.CostPermille, item.ElevationMillimetres, item.SlopePermille)));
        foreach (var item in agents ?? [])
        {
            var id = new EntityId(item.Id);
            _navigationAgents.Add(id, new NavigationAgentState
            {
                Id = id, XMillimetres = item.XMillimetres, ZMillimetres = item.ZMillimetres,
                Action = (AgentNavigationAction)item.Action,
                Destination = item.DestinationX is { } x && item.DestinationZ is { } z ? new GridCell(x, z) : null,
                Route = item.Route.Select(cell => new GridCell(cell.X, cell.Z)).ToList(),
                RouteIndex = item.RouteIndex, SegmentOriginXMillimetres = item.SegmentOriginXMillimetres,
                SegmentOriginZMillimetres = item.SegmentOriginZMillimetres, SegmentProgressMicrometres = item.SegmentProgressMicrometres,
                MovementRemainder = item.MovementRemainder, LastSearchExpandedNodes = item.LastSearchExpandedNodes, IntentId = item.IntentId,
            });
        }
    }

    private static string? ValidatePersistedNavigation(PersistedTraversalGrid? grid, PersistedNavigationAgent[]? agents)
    {
        if (grid is null && agents is null) return null;
        if (grid is null || agents is null) return "Traversal grid and navigation agents must both be present or absent.";
        if (grid.Width != TraversalGrid.Width || grid.Depth != TraversalGrid.Depth || grid.CellSizeMillimetres != TraversalGrid.CellSizeMillimetres || grid.Cells is null)
            return "Traversal grid dimensions or cells are invalid.";
        TraversalGrid restored;
        try { restored = new TraversalGrid(grid.Cells.Select(item => new TerrainCellOverride(new GridCell(item.X, item.Z), (GroundSurface)item.Surface, item.IsWalkable, item.CostPermille, item.ElevationMillimetres, item.SlopePermille))); }
        catch (ArgumentException exception) { return $"Traversal grid is invalid: {exception.Message}"; }
        if (!StrictlyIncreasing(agents.Select(item => item.Id)) || agents.Any(item => item.Route is null || item.Id == 0 || !Enum.IsDefined(typeof(AgentNavigationAction), item.Action)))
            return "Navigation agents must have sorted unique IDs, valid actions and routes.";
        foreach (var item in agents)
        {
            if (item.Route.Any(cell => !restored.Contains(new GridCell(cell.X, cell.Z)) || !restored.Get(new GridCell(cell.X, cell.Z)).IsWalkable))
                return $"Navigation agent {item.Id} route enters an invalid or blocked cell.";
            if (item.RouteIndex < 0 || (item.Route.Length > 0 && item.RouteIndex >= item.Route.Length) || item.SegmentProgressMicrometres < 0 || item.MovementRemainder < 0 || item.LastSearchExpandedNodes < 0)
                return $"Navigation agent {item.Id} movement progress is invalid.";
            if (item.Action == (int)AgentNavigationAction.Travelling && (item.Route.Length < 2 || item.RouteIndex < 1))
                return $"Navigation agent {item.Id} travelling route is incomplete.";
        }
        return null;
    }

    private static long IntegerSquareRoot(long value)
    {
        if (value <= 0) return 0;
        long low = 0;
        long high = 1;
        while (high <= value / high) high *= 2;
        while (low + 1 < high)
        {
            var middle = low + (high - low) / 2;
            if (middle <= value / middle) low = middle;
            else high = middle;
        }
        return low;
    }
}

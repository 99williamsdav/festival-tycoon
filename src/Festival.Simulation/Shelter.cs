namespace Festival.Simulation;

/// <summary>
/// Cover from the rain, from the footprints that are already there: the marquee's shelter, the bar's canopy, the food
/// van's counter awning and the first-aid porch. Offsets are in cells from each one's origin, front +Z, before turning.
/// </summary>
public static class ShelterRules
{
    /// <summary>The navigation intent of a guest walking to, or standing at, a sheltering place.</summary>
    public const string Intent = "weather.shelter";
    public static readonly GridCell[] BarCover = Rect(-3, 3, 3, 4);
    public static readonly GridCell[] BarSpots = [new(-3, 3), new(3, 3), new(-3, 4), new(3, 4)];
    public static readonly GridCell[] VanCover = Rect(-4, 4, 4, 4);
    public static readonly GridCell[] VanSpots = [new(-4, 4), new(4, 4)];
    public static readonly GridCell[] FirstAidCover = Rect(-3, 4, 1, 4);
    public static readonly GridCell[] FirstAidSpots = [new(-3, 4), new(-2, 4), new(-1, 4)];

    private static GridCell[] Rect(int minX, int minZ, int maxX, int maxZ) =>
        (from x in Enumerable.Range(minX, maxX - minX + 1) from z in Enumerable.Range(minZ, maxZ - minZ + 1) select new GridCell(x, z)).ToArray();
}

/// <summary>A sheltering place: a cell and the cover it belongs to.</summary>
public sealed record ShelterSpot(GridCell Cell, string CoverId);

public sealed partial class GameSession
{
    private static GridCell Turned(GridCell origin, int quarterTurns, GridCell offset)
    {
        var turned = RotateWaterOffset(offset, quarterTurns);
        return new(origin.X + turned.X, origin.Z + turned.Z);
    }

    // Derived from the layout, so never saved: rebuilt whenever the placements change.
    private BuildPlacement[]? _coverCacheSource;
    private GridCell[] _coveredCells = [];
    private bool[]? _coveredGrid;
    private ShelterSpot[] _shelterSpots = [];

    /// <summary>A layout's cover from the rain, and its sheltering places in order.</summary>
    private static (GridCell[] Covered, ShelterSpot[] Spots) ShelterLayout(IEnumerable<BuildPlacement> placements)
    {
        var layout = placements as IReadOnlyList<BuildPlacement> ?? placements.ToArray();
        var covered = new HashSet<GridCell>(BarnLayout.Coverage());
        var spots = new List<ShelterSpot>();
        foreach (var item in layout)
        {
            (IEnumerable<GridCell> cells, IEnumerable<GridCell> places) = item.Kind switch
            {
                BuildServiceKind.Marquee => (MarqueeShelterCells(item.Cell, item.QuarterTurns), MarqueeRestSpots(item.Cell, item.QuarterTurns, layout)),
                BuildServiceKind.Bar => (ShelterRules.BarCover.Select(offset => Turned(item.Cell, item.QuarterTurns, offset)),
                    ShelterRules.BarSpots.Select(offset => Turned(item.Cell, item.QuarterTurns, offset))),
                BuildServiceKind.FoodVan => (ShelterRules.VanCover.Select(offset => Turned(item.Cell, item.QuarterTurns, offset)),
                    ShelterRules.VanSpots.Select(offset => Turned(item.Cell, item.QuarterTurns, offset))),
                BuildServiceKind.FirstAid => (ShelterRules.FirstAidCover.Select(offset => Turned(item.Cell, item.QuarterTurns, offset)),
                    ShelterRules.FirstAidSpots.Select(offset => Turned(item.Cell, item.QuarterTurns, offset))),
                _ => ([], []),
            };
            covered.UnionWith(cells);
            spots.AddRange(places.Select(cell => new ShelterSpot(cell, item.Id)));
        }
        return (covered.ToArray(), spots.ToArray());
    }

    private void EnsureCoverCache()
    {
        var placements = _preparation?.BuildPlacements;
        if (_coveredGrid is not null && ReferenceEquals(placements, _coverCacheSource)) return;
        _coverCacheSource = placements;
        (_coveredCells, _shelterSpots) = ShelterLayout(placements ?? []);
        _coveredGrid = new bool[TraversalGrid.Width * TraversalGrid.Depth];
        foreach (var cell in _coveredCells)
            if (cell.X is >= 0 and < TraversalGrid.Width && cell.Z is >= 0 and < TraversalGrid.Depth) _coveredGrid[cell.Z * TraversalGrid.Width + cell.X] = true;
    }

    /// <summary>Whether this cell is out of the rain.</summary>
    public bool CoveredCell(GridCell cell)
    {
        EnsureCoverCache();
        return cell.X is >= 0 and < TraversalGrid.Width && cell.Z is >= 0 and < TraversalGrid.Depth && _coveredGrid![cell.Z * TraversalGrid.Width + cell.X];
    }

    /// <summary>Every cell out of the rain, for the view.</summary>
    public IReadOnlyCollection<GridCell> CaptureCoveredCells() { EnsureCoverCache(); return _coveredCells.ToArray(); }

    /// <summary>
    /// Everyone saved as sheltering is bound for a real sheltering place of the saved layout, one to a place, and no more
    /// at a marquee than its places allow (resters included, as the marquee's own check counts them).
    /// </summary>
    private static string? ValidatePersistedShelter(SessionPersistenceSnapshot s, PreparationSnapshot p)
    {
        var sheltering = (s.NavigationAgents ?? []).Where(agent => agent.IntentId == ShelterRules.Intent &&
            agent.Action is (int)AgentNavigationAction.Travelling or (int)AgentNavigationAction.Arrived).ToArray();
        if (sheltering.Length == 0) return null;
        var frozenFatal=p.Status==PreparationStatus.Failed&&s.Lifecycle is {} lifecycle&&lifecycle.CurrentAttemptId==(ulong)p.Attempt&&
            lifecycle.Attempts.Any(a=>a.AttemptId==lifecycle.CurrentAttemptId&&a.Status==(int)EditionAttemptStatus.Failed)&&
            lifecycle.Casualties.Count(c=>c.AttemptId==lifecycle.CurrentAttemptId)==1&&
            lifecycle.Casualties.Any(c=>c.AttemptId==lifecycle.CurrentAttemptId&&c.Tick==s.CurrentTick);
        if (p.Status != PreparationStatus.Running&&!frozenFatal || SavedWeatherKind(s) != WeatherKind.Rain) return "Sheltering outside a rainy festival day.";
        var spots = ShelterLayout(p.BuildPlacements ?? []).Spots.Select(spot => spot.Cell).ToHashSet();
        var places = new HashSet<GridCell>();
        foreach (var agent in sheltering)
        {
            if (agent.DestinationX is not { } x || agent.DestinationZ is not { } z || !spots.Contains(new(x, z))) return "Someone shelters where there's no shelter.";
            if (!places.Add(new(x, z))) return "Two people shelter at one place.";
            if (p.People.FirstOrDefault(person => person.AgentId == agent.Id) is not { Role: ProtectedPersonRole.Guest, Admitted: true, Departed: false })
                return "Only a guest on the field shelters.";
        }
        return null;
    }

    private static bool ShelterNavigation(NavigationAgentState agent) =>
        agent.IntentId == ShelterRules.Intent && agent.Destination is not null && agent.Action is AgentNavigationAction.Travelling or AgentNavigationAction.Arrived;

    /// <summary>Someone sheltering, or on the way to shelter.</summary>
    private bool Sheltering(ulong id) => _navigationAgents.TryGetValue(new(id), out var agent) && ShelterNavigation(agent);

    /// <summary>
    /// A place to shelter under this cover: the one they hold, else the free place nearest them. Null when every place is
    /// taken: a marquee holds 15 between those resting and sheltering, a bar four, a van two and first aid three.
    /// Places on a queue's line, or where someone stands, are skipped.
    /// </summary>
    private GridCell? ShelterSpotFor(ulong id, string coverId)
    {
        EnsureCoverCache();
        if (_traversalGrid is not { } grid) return null;
        var self = new EntityId(id);
        var agent = _navigationAgents[self];
        if (ShelterNavigation(agent) && _shelterSpots.Any(spot => spot.Cell == agent.Destination && spot.CoverId == coverId)) return agent.Destination;
        var scratch = _restScratch is { } own && own.Self == id ? own : null;
        var others = scratch?.Others ?? _navigationAgents.Values.Where(other => other.Id != self && MovementOccupant(other.Id.Value)).ToArray();
        if (scratch is not null) scratch.Others = others;
        HashSet<GridCell>? queues = scratch?.Queues;
        GridCell? best = null; var bestDistance = long.MaxValue;
        foreach (var spot in _shelterSpots)
        {
            if (spot.CoverId != coverId || !grid.Contains(spot.Cell) || !grid.Get(spot.Cell).IsWalkable || ShelterSpotTaken(spot.Cell, others)) continue;
            queues ??= AllQueueGround().Concat(EffectiveToilets(_facilities).SelectMany(toilet => toilet.QueueCells ?? [])).ToHashSet();
            if (queues.Contains(spot.Cell)) continue;
            var centre = TraversalGrid.CellCentre(spot.Cell);
            long dx = centre.XMillimetres - agent.XMillimetres, dz = centre.ZMillimetres - agent.ZMillimetres;
            if (dx * dx + dz * dz < bestDistance) { best = spot.Cell; bestDistance = dx * dx + dz * dz; }
        }
        if (scratch is not null) scratch.Queues = queues;
        return best;
    }

    /// <summary>A place someone else is resting or sheltering at (or heading for), or standing on.</summary>
    private static bool ShelterSpotTaken(GridCell spot, NavigationAgentState[] others) => RestSpotTaken(spot, others);

    /// <summary>Every cover's sheltering places, for the view and the checks.</summary>
    public IReadOnlyList<ShelterSpot> CaptureShelterSpots() { EnsureCoverCache(); return _shelterSpots.ToArray(); }

    /// <summary>How many are sheltering at, or heading for, each cover.</summary>
    public int ShelteringAt(string coverId)
    {
        EnsureCoverCache();
        var spots = _shelterSpots.Where(spot => spot.CoverId == coverId).Select(spot => spot.Cell).ToHashSet();
        return _navigationAgents.Values.Count(agent => ShelterNavigation(agent) && spots.Contains(agent.Destination!.Value));
    }

    private void StartShelter(ulong id, string coverId, string reason)
    {
        if (ShelterSpotFor(id, coverId) is not { } spot) { MutatePerson(id, item => { item.Reason = reason; item.NeedDecisionTick = CurrentTick; }); return; }
        MutatePerson(id, item => { item.Reason = reason; item.NeedDecisionTick = CurrentTick; });
        ApplyAgentDestination(new(id), new(spot, ShelterRules.Intent));
    }

    /// <summary>The cover someone is sheltering under, or null.</summary>
    private string? ShelterCoverOf(ulong id)
    {
        if (!_navigationAgents.TryGetValue(new(id), out var agent) || !ShelterNavigation(agent)) return null;
        EnsureCoverCache();
        return _shelterSpots.FirstOrDefault(spot => spot.Cell == agent.Destination)?.CoverId;
    }
}

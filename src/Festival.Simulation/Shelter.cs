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
    private readonly HashSet<GridCell> _coveredCells = [];
    private ShelterSpot[] _shelterSpots = [];

    private void EnsureCoverCache()
    {
        var placements = _preparation?.BuildPlacements;
        if (ReferenceEquals(placements, _coverCacheSource)) return;
        _coverCacheSource = placements;
        _coveredCells.Clear();
        var spots = new List<ShelterSpot>();
        foreach (var item in placements ?? [])
        {
            switch (item.Kind)
            {
                case BuildServiceKind.Marquee:
                    _coveredCells.UnionWith(MarqueeShelterCells(item.Cell, item.QuarterTurns));
                    spots.AddRange(MarqueeRestSpots(item.Cell, item.QuarterTurns).Select(cell => new ShelterSpot(cell, item.Id)));
                    break;
                case BuildServiceKind.Bar:
                    _coveredCells.UnionWith(ShelterRules.BarCover.Select(offset => Turned(item.Cell, item.QuarterTurns, offset)));
                    spots.AddRange(ShelterRules.BarSpots.Select(offset => new ShelterSpot(Turned(item.Cell, item.QuarterTurns, offset), item.Id)));
                    break;
                case BuildServiceKind.FoodVan:
                    _coveredCells.UnionWith(ShelterRules.VanCover.Select(offset => Turned(item.Cell, item.QuarterTurns, offset)));
                    spots.AddRange(ShelterRules.VanSpots.Select(offset => new ShelterSpot(Turned(item.Cell, item.QuarterTurns, offset), item.Id)));
                    break;
                case BuildServiceKind.FirstAid:
                    _coveredCells.UnionWith(ShelterRules.FirstAidCover.Select(offset => Turned(item.Cell, item.QuarterTurns, offset)));
                    spots.AddRange(ShelterRules.FirstAidSpots.Select(offset => new ShelterSpot(Turned(item.Cell, item.QuarterTurns, offset), item.Id)));
                    break;
            }
        }
        _shelterSpots = spots.ToArray();
    }

    /// <summary>Whether this cell is out of the rain.</summary>
    public bool CoveredCell(GridCell cell) { EnsureCoverCache(); return _coveredCells.Contains(cell); }

    /// <summary>Every cell out of the rain, for the view.</summary>
    public IReadOnlyCollection<GridCell> CaptureCoveredCells() { EnsureCoverCache(); return _coveredCells.ToArray(); }

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

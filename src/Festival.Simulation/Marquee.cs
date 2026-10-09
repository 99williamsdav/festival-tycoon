namespace Festival.Simulation;

/// <summary>A placed marquee as the HUD shows it: who is under it now, and how many of its rest places are taken.</summary>
public sealed record MarqueeReadModel(string Id, GridCell Cell, int QuarterTurns, int Sheltering, int Resting, int RestCapacity);

/// <summary>
/// The "Under My Umbrella" stretch tent, in cells from its middle (the model's origin), banner side +Z, before turning.
/// Only its poles and pegs block; the ground under the sail is open, so people and queues pass under it.
/// </summary>
public static class MarqueeRules
{
    public const int FeePennies = 4_000;
    /// <summary>Resting guests it holds at once: its 25 m² of shade at about 1.7 m² each.</summary>
    public const int RestCapacity = 15;
    /// <summary>The build footprint, guy-rope pegs included: 19 × 15 cells, 9.5 × 7.5 m.</summary>
    public const int HalfWidthCells = 9, HalfDepthCells = 7;
    /// <summary>The shade: 11 × 9 cells (5.5 × 4.5 m), inside the sail's scalloped edge.</summary>
    public const int ShelterHalfWidthCells = 5, ShelterHalfDepthCells = 4;
    /// <summary>The two main poles, four corner poles and four pegs: one cell each.</summary>
    public static readonly GridCell[] SolidOffsets =
    [
        new(-3, -1), new(4, 1),
        new(-7, -5), new(7, -5), new(-7, 5), new(7, 5),
        new(-9, -7), new(9, -7), new(-9, 7), new(9, 7),
    ];

    /// <summary>
    /// Where resting guests stand: shelter cells on a checkerboard (707 mm apart, so a walker can pass between two),
    /// nearest the middle first, poles left out.
    /// </summary>
    public static readonly GridCell[] RestOffsets =
        (from x in Enumerable.Range(-ShelterHalfWidthCells, ShelterHalfWidthCells * 2 + 1)
         from z in Enumerable.Range(-ShelterHalfDepthCells, ShelterHalfDepthCells * 2 + 1)
         where (x + z) % 2 == 0 && !SolidOffsets.Contains(new GridCell(x, z))
         orderby x * x + z * z, x, z
         select new GridCell(x, z)).Take(RestCapacity).ToArray();
}

public sealed partial class GameSession
{
    private static GridCell MarqueeCell(GridCell centre, int quarterTurns, GridCell offset)
    {
        var turned = RotateWaterOffset(offset, quarterTurns);
        return new(centre.X + turned.X, centre.Z + turned.Z);
    }

    private static GridCell[] MarqueeRect(GridCell centre, int quarterTurns, int halfWidth, int halfDepth) =>
        (from x in Enumerable.Range(-halfWidth, halfWidth * 2 + 1)
         from z in Enumerable.Range(-halfDepth, halfDepth * 2 + 1)
         select MarqueeCell(centre, quarterTurns, new(x, z))).ToArray();

    /// <summary>Everything the tent covers, pegs included. Nothing else may be built on it.</summary>
    public static GridCell[] MarqueeFootprintCells(GridCell centre, int quarterTurns) =>
        MarqueeRect(centre, quarterTurns, MarqueeRules.HalfWidthCells, MarqueeRules.HalfDepthCells);

    public static GridCell[] MarqueeSolidCells(GridCell centre, int quarterTurns) =>
        MarqueeRules.SolidOffsets.Select(offset => MarqueeCell(centre, quarterTurns, offset)).ToArray();

    public static GridCell[] MarqueeShelterCells(GridCell centre, int quarterTurns) =>
        MarqueeRect(centre, quarterTurns, MarqueeRules.ShelterHalfWidthCells, MarqueeRules.ShelterHalfDepthCells);

    public static GridCell[] MarqueeRestSpots(GridCell centre, int quarterTurns) =>
        MarqueeRules.RestOffsets.Select(offset => MarqueeCell(centre, quarterTurns, offset)).ToArray();

    private IEnumerable<BuildPlacement> Marquees() => (_preparation?.BuildPlacements ?? []).Where(item => item.Kind == BuildServiceKind.Marquee);

    // Derived from the layout, so never saved: rebuilt whenever the placements change.
    private BuildPlacement[]? _marqueeCacheSource;
    private readonly HashSet<GridCell> _marqueeShade = [];
    private readonly Dictionary<GridCell, string> _marqueeRestSpots = [];

    private void EnsureMarqueeCache()
    {
        var placements = _preparation?.BuildPlacements;
        if (ReferenceEquals(placements, _marqueeCacheSource)) return;
        _marqueeCacheSource = placements;
        _marqueeShade.Clear(); _marqueeRestSpots.Clear();
        foreach (var tent in Marquees())
        {
            _marqueeShade.UnionWith(MarqueeShelterCells(tent.Cell, tent.QuarterTurns));
            foreach (var spot in MarqueeRestSpots(tent.Cell, tent.QuarterTurns)) _marqueeRestSpots[spot] = tent.Id;
        }
    }

    /// <summary>Whether this cell is in the shade of a placed marquee.</summary>
    public bool InMarqueeShade(GridCell cell) { EnsureMarqueeCache(); return _marqueeShade.Count > 0 && _marqueeShade.Contains(cell); }

    /// <summary>Someone standing under a marquee: they build up no heat there.</summary>
    private bool InShade(ulong id)
    {
        EnsureMarqueeCache();
        return _marqueeShade.Count > 0 && _navigationAgents.TryGetValue(new(id), out var nav) &&
            _marqueeShade.Contains(TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres));
    }

    /// <summary>Whether this cell is one of a marquee's rest places.</summary>
    public bool IsMarqueeRestSpot(GridCell cell) { EnsureMarqueeCache(); return _marqueeRestSpots.ContainsKey(cell); }

    /// <summary>A rest place by first aid or under a marquee.</summary>
    private bool IsAnyRestSpot(GridCell cell) => IsRestSpot(cell) || IsMarqueeRestSpot(cell);

    /// <summary>Someone resting, or on their way to rest, at one of this marquee's places.</summary>
    private bool RestingAtMarquee(NavigationAgentState agent, string marqueeId) =>
        RestNavigationIntent(agent.IntentId) && agent.Destination is { } spot && agent.Action is AgentNavigationAction.Travelling or AgentNavigationAction.Arrived &&
        _marqueeRestSpots.TryGetValue(spot, out var owner) && owner == marqueeId;

    /// <summary>
    /// A rest place under this marquee: the one they hold, else the free place nearest the middle. Null when all
    /// <see cref="MarqueeRules.RestCapacity"/> are taken, so they rest by first aid instead. Places on a queue's
    /// line, or where someone stands, are skipped.
    /// </summary>
    private GridCell? MarqueeRestSpotFor(ulong id, string marqueeId)
    {
        EnsureMarqueeCache();
        if (Marquees().FirstOrDefault(item => item.Id == marqueeId) is not { } tent || _traversalGrid is not { } grid) return null;
        var self = new EntityId(id);
        var agent = _navigationAgents[self];
        if (RestingAtMarquee(agent, marqueeId)) return agent.Destination;
        var others = _navigationAgents.Values.Where(other => other.Id != self && MovementOccupant(other.Id.Value)).ToArray();
        if (others.Count(other => RestingAtMarquee(other, marqueeId)) >= MarqueeRules.RestCapacity) return null;
        HashSet<GridCell>? queues = null;
        foreach (var spot in MarqueeRestSpots(tent.Cell, tent.QuarterTurns))
        {
            if (!grid.Contains(spot) || !grid.Get(spot).IsWalkable || RestSpotTaken(spot, others)) continue;
            queues ??= AllQueueGround().Concat(EffectiveToilets(_facilities).SelectMany(toilet => toilet.QueueCells ?? [])).ToHashSet();
            if (!queues.Contains(spot)) return spot;
        }
        return null;
    }

    public IReadOnlyList<MarqueeReadModel> CaptureMarquees()
    {
        var tents = Marquees().ToArray();
        if (tents.Length == 0) return [];
        EnsureMarqueeCache();
        return tents.Select(tent =>
        {
            var shade = MarqueeShelterCells(tent.Cell, tent.QuarterTurns).ToHashSet();
            var sheltering = _navigationAgents.Values.Count(agent => MovementOccupant(agent.Id.Value) && PersonIn(PersonView.Roster, agent.Id.Value)?.Admitted == true &&
                shade.Contains(TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres)));
            var resting = _navigationAgents.Values.Count(agent => MovementOccupant(agent.Id.Value) && RestingAtMarquee(agent, tent.Id));
            return new MarqueeReadModel(tent.Id, tent.Cell, tent.QuarterTurns, sheltering, resting, MarqueeRules.RestCapacity);
        }).ToArray();
    }

    private void BlockMarquees()
    {
        if (_traversalGrid is null || !Marquees().Any()) return;
        var cells = _traversalGrid.Overrides.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var tent in Marquees())
            foreach (var cell in MarqueeSolidCells(tent.Cell, tent.QuarterTurns)) cells[cell] = new(cell, GroundSurface.Grass, false);
        _traversalGrid = new(cells.Values);
    }

    /// <summary>Once open, every pole and peg is solid in the saved ground, and no marquee holds more resters than it has places.</summary>
    private static string? ValidatePersistedMarquees(PreparationSnapshot p, SessionPersistenceSnapshot s)
    {
        var tents = p.BuildPlacements.Where(item => item.Kind == BuildServiceKind.Marquee).ToArray();
        if (tents.Length == 0 || p.Status == PreparationStatus.Preparing) return null;
        if (s.TraversalGrid is not { } grid) return "Marquee poles absent from saved traversal.";
        var solid = grid.Cells.Where(cell => !cell.IsWalkable).Select(cell => new GridCell(cell.X, cell.Z)).ToHashSet();
        if (tents.Any(tent => MarqueeSolidCells(tent.Cell, tent.QuarterTurns).Any(cell => !solid.Contains(cell))))
            return "Marquee poles absent from saved traversal.";
        foreach (var tent in tents)
        {
            var spots = MarqueeRestSpots(tent.Cell, tent.QuarterTurns).ToHashSet();
            if ((s.NavigationAgents ?? []).Count(agent => RestNavigationIntent(agent.IntentId) && agent.DestinationX is { } x && agent.DestinationZ is { } z &&
                    spots.Contains(new(x, z))) > MarqueeRules.RestCapacity)
                return "Marquee holds more resting guests than it has places.";
        }
        return null;
    }
}

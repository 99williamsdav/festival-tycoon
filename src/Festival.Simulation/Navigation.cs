namespace Festival.Simulation;

public readonly record struct GridCell(int X, int Z) : IComparable<GridCell>
{
    public int CompareTo(GridCell other)
    {
        var z = Z.CompareTo(other.Z);
        return z != 0 ? z : X.CompareTo(other.X);
    }
}

public enum GroundSurface { Grass = 1, VehicleTrack = 2 }
public enum AgentNavigationAction { Idle = 0, Travelling = 1, Arrived = 2, NoRoute = 3 }

public sealed record TerrainCellOverride(
    GridCell Cell, GroundSurface Surface, bool IsWalkable,
    int CostPermille = 1000, int ElevationMillimetres = 0, int SlopePermille = 0);

public sealed record TerrainCellReadModel(
    GroundSurface Surface, bool IsWalkable, int CostPermille,
    int ElevationMillimetres, int SlopePermille);

public sealed class TraversalGrid
{
    public const int Width = 256;
    public const int Depth = 256;
    public const int CellSizeMillimetres = 500;
    public const int OriginMillimetres = -64_000;
    private readonly SortedDictionary<GridCell, TerrainCellOverride> _overrides;

    public TraversalGrid(IEnumerable<TerrainCellOverride>? overrides = null)
    {
        _overrides = new SortedDictionary<GridCell, TerrainCellOverride>();
        foreach (var item in overrides ?? Array.Empty<TerrainCellOverride>())
        {
            if (!Contains(item.Cell)) throw new ArgumentOutOfRangeException(nameof(overrides), $"Cell {item.Cell} is outside the 256 x 256 grid.");
            if (item.CostPermille <= 0 || item.ElevationMillimetres < 0 || item.SlopePermille < 0)
                throw new ArgumentOutOfRangeException(nameof(overrides), "Terrain cost must be positive and elevation/slope nonnegative.");
            if (!_overrides.TryAdd(item.Cell, item)) throw new ArgumentException($"Duplicate terrain cell {item.Cell}.", nameof(overrides));
        }
    }

    public IReadOnlyDictionary<GridCell, TerrainCellOverride> Overrides => _overrides;
    public bool Contains(GridCell cell) => cell.X is >= 0 and < Width && cell.Z is >= 0 and < Depth;
    public TerrainCellReadModel Get(GridCell cell)
    {
        if (!Contains(cell)) return new TerrainCellReadModel(GroundSurface.Grass, false, int.MaxValue, 0, 0);
        return _overrides.TryGetValue(cell, out var item)
            ? new TerrainCellReadModel(item.Surface, item.IsWalkable, item.CostPermille, item.ElevationMillimetres, item.SlopePermille)
            : new TerrainCellReadModel(GroundSurface.Grass, true, 1000, 0, 0);
    }

    public static GridCell WorldToCell(int xMillimetres, int zMillimetres) =>
        new(Math.Clamp((xMillimetres - OriginMillimetres) / CellSizeMillimetres, 0, Width - 1),
            Math.Clamp((zMillimetres - OriginMillimetres) / CellSizeMillimetres, 0, Depth - 1));

    public static (int XMillimetres, int ZMillimetres) CellCentre(GridCell cell) =>
        (OriginMillimetres + cell.X * CellSizeMillimetres + CellSizeMillimetres / 2,
         OriginMillimetres + cell.Z * CellSizeMillimetres + CellSizeMillimetres / 2);
}

public sealed record PathSearchResult(bool Found, IReadOnlyList<GridCell> Path, int ExpandedNodes);

public static class DeterministicPathfinder
{
    private static readonly (int X, int Z, int Cost)[] Neighbours =
    [
        (0, -1, 1000), (1, 0, 1000), (0, 1, 1000), (-1, 0, 1000),
        (1, -1, 1414), (1, 1, 1414), (-1, 1, 1414), (-1, -1, 1414),
    ];

    public static PathSearchResult FindPath(TraversalGrid grid, GridCell start, GridCell target)
    {
        if (!grid.Contains(start) || !grid.Contains(target) || !grid.Get(start).IsWalkable || !grid.Get(target).IsWalkable)
            return new PathSearchResult(false, Array.Empty<GridCell>(), 0);

        var open = new List<GridCell> { start };
        var openSet = new HashSet<GridCell> { start };
        var closed = new HashSet<GridCell>();
        var cameFrom = new Dictionary<GridCell, GridCell>();
        var gScore = new Dictionary<GridCell, int> { [start] = 0 };
        var expanded = 0;

        while (open.Count > 0 && expanded < TraversalGrid.Width * TraversalGrid.Depth)
        {
            var bestIndex = 0;
            for (var index = 1; index < open.Count; index++)
                if (Compare(open[index], open[bestIndex], target, gScore) < 0) bestIndex = index;
            var current = open[bestIndex];
            open.RemoveAt(bestIndex);
            openSet.Remove(current);
            if (current == target) return new PathSearchResult(true, Reconstruct(cameFrom, current), expanded);
            closed.Add(current);
            expanded++;

            foreach (var (dx, dz, baseCost) in Neighbours)
            {
                var neighbour = new GridCell(current.X + dx, current.Z + dz);
                if (!grid.Contains(neighbour) || !grid.Get(neighbour).IsWalkable || closed.Contains(neighbour)) continue;
                if (dx != 0 && dz != 0 &&
                    (!grid.Get(new GridCell(current.X + dx, current.Z)).IsWalkable ||
                     !grid.Get(new GridCell(current.X, current.Z + dz)).IsWalkable)) continue;
                var terrain = grid.Get(neighbour);
                var stepCost = checked(baseCost * terrain.CostPermille / 1000);
                var tentative = checked(gScore[current] + stepCost);
                if (gScore.TryGetValue(neighbour, out var known) && tentative >= known) continue;
                cameFrom[neighbour] = current;
                gScore[neighbour] = tentative;
                if (openSet.Add(neighbour)) open.Add(neighbour);
            }
        }
        return new PathSearchResult(false, Array.Empty<GridCell>(), expanded);
    }

    private static int Compare(GridCell left, GridCell right, GridCell target, IReadOnlyDictionary<GridCell, int> scores)
    {
        var leftH = Heuristic(left, target); var rightH = Heuristic(right, target);
        var comparison = (scores[left] + leftH).CompareTo(scores[right] + rightH);
        if (comparison != 0) return comparison;
        comparison = leftH.CompareTo(rightH);
        if (comparison != 0) return comparison;
        comparison = left.Z.CompareTo(right.Z);
        return comparison != 0 ? comparison : left.X.CompareTo(right.X);
    }

    private static int Heuristic(GridCell from, GridCell to)
    {
        var dx = Math.Abs(from.X - to.X); var dz = Math.Abs(from.Z - to.Z);
        return 1414 * Math.Min(dx, dz) + 1000 * Math.Abs(dx - dz);
    }

    private static IReadOnlyList<GridCell> Reconstruct(IReadOnlyDictionary<GridCell, GridCell> cameFrom, GridCell current)
    {
        var path = new List<GridCell> { current };
        while (cameFrom.TryGetValue(current, out var previous)) { current = previous; path.Add(current); }
        path.Reverse();
        return path;
    }
}

public sealed record NavigationAgentSnapshot(
    EntityId Id, int XMillimetres, int ZMillimetres, AgentNavigationAction Action,
    GridCell? Destination, IReadOnlyList<GridCell> Route, int RouteIndex,
    int SegmentProgressMicrometres, int MovementRemainder, int LastSearchExpandedNodes);

internal sealed class NavigationAgentState
{
    public required EntityId Id { get; init; }
    public int XMillimetres { get; set; }
    public int ZMillimetres { get; set; }
    public AgentNavigationAction Action { get; set; }
    public GridCell? Destination { get; set; }
    public List<GridCell> Route { get; set; } = [];
    public int RouteIndex { get; set; }
    public int SegmentProgressMicrometres { get; set; }
    public int MovementRemainder { get; set; }
    public int LastSearchExpandedNodes { get; set; }
}

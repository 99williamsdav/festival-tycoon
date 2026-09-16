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
    public int MinimumWalkableCostPermille => Math.Min(1000,
        _overrides.Values.Where(item => item.IsWalkable).Select(item => item.CostPermille).DefaultIfEmpty(1000).Min());
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

/// <summary>Exact integer supercover validation for a world-space movement segment.</summary>
public static class TraversalSweep
{
    public static bool IsWalkable(TraversalGrid grid, int fromX, int fromZ, int toX, int toZ)
    {
        var size = TraversalGrid.CellSizeMillimetres;
        var origin = TraversalGrid.OriginMillimetres;
        var minCellX = FloorDiv(Math.Min(fromX, toX) - origin, size) - 1;
        var maxCellX = FloorDiv(Math.Max(fromX, toX) - origin, size) + 1;
        var minCellZ = FloorDiv(Math.Min(fromZ, toZ) - origin, size) - 1;
        var maxCellZ = FloorDiv(Math.Max(fromZ, toZ) - origin, size) + 1;
        for (var z = minCellZ; z <= maxCellZ; z++)
        for (var x = minCellX; x <= maxCellX; x++)
        {
            var cell = new GridCell(x, z);
            var left = origin + x * size;
            var top = origin + z * size;
            if (!IntersectsClosedRectangle(fromX, fromZ, toX, toZ, left, top, left + size, top + size)) continue;
            if (!grid.Contains(cell) || !grid.Get(cell).IsWalkable) return false;
        }
        return true;
    }

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
    }

    private static bool IntersectsClosedRectangle(int ax, int az, int bx, int bz, int left, int top, int right, int bottom)
    {
        if (Inside(ax, az, left, top, right, bottom) || Inside(bx, bz, left, top, right, bottom)) return true;
        return Intersects(ax, az, bx, bz, left, top, right, top) ||
            Intersects(ax, az, bx, bz, right, top, right, bottom) ||
            Intersects(ax, az, bx, bz, right, bottom, left, bottom) ||
            Intersects(ax, az, bx, bz, left, bottom, left, top);
    }

    private static bool Inside(int x, int z, int left, int top, int right, int bottom) =>
        x >= left && x <= right && z >= top && z <= bottom;

    private static bool Intersects(int ax, int az, int bx, int bz, int cx, int cz, int dx, int dz)
    {
        var abC = Cross(ax, az, bx, bz, cx, cz);
        var abD = Cross(ax, az, bx, bz, dx, dz);
        var cdA = Cross(cx, cz, dx, dz, ax, az);
        var cdB = Cross(cx, cz, dx, dz, bx, bz);
        if (((abC > 0 && abD < 0) || (abC < 0 && abD > 0)) && ((cdA > 0 && cdB < 0) || (cdA < 0 && cdB > 0))) return true;
        return abC == 0 && OnSegment(ax, az, bx, bz, cx, cz) ||
            abD == 0 && OnSegment(ax, az, bx, bz, dx, dz) ||
            cdA == 0 && OnSegment(cx, cz, dx, dz, ax, az) ||
            cdB == 0 && OnSegment(cx, cz, dx, dz, bx, bz);
    }

    private static long Cross(int ax, int az, int bx, int bz, int px, int pz) =>
        (long)(bx - ax) * (pz - az) - (long)(bz - az) * (px - ax);

    private static bool OnSegment(int ax, int az, int bx, int bz, int px, int pz) =>
        px >= Math.Min(ax, bx) && px <= Math.Max(ax, bx) && pz >= Math.Min(az, bz) && pz <= Math.Max(az, bz);
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
        var minimumCost = grid.MinimumWalkableCostPermille;

        while (open.Count > 0 && expanded < TraversalGrid.Width * TraversalGrid.Depth)
        {
            var bestIndex = 0;
            for (var index = 1; index < open.Count; index++)
                if (Compare(open[index], open[bestIndex], target, gScore, minimumCost) < 0) bestIndex = index;
            var current = open[bestIndex];
            open.RemoveAt(bestIndex);
            openSet.Remove(current);
            if (current == target) return new PathSearchResult(true, Reconstruct(cameFrom, current), expanded);
            closed.Add(current);
            expanded++;

            foreach (var (dx, dz, baseCost) in Neighbours)
            {
                var neighbour = new GridCell(current.X + dx, current.Z + dz);
                if (!grid.Contains(neighbour) || !grid.Get(neighbour).IsWalkable) continue;
                if (dx != 0 && dz != 0 &&
                    (!grid.Get(new GridCell(current.X + dx, current.Z)).IsWalkable ||
                     !grid.Get(new GridCell(current.X, current.Z + dz)).IsWalkable)) continue;
                var terrain = grid.Get(neighbour);
                var stepCost = checked(baseCost * terrain.CostPermille / 1000);
                var tentative = checked(gScore[current] + stepCost);
                if (gScore.TryGetValue(neighbour, out var known) && tentative >= known) continue;
                cameFrom[neighbour] = current;
                gScore[neighbour] = tentative;
                closed.Remove(neighbour);
                if (openSet.Add(neighbour)) open.Add(neighbour);
            }
        }
        return new PathSearchResult(false, Array.Empty<GridCell>(), expanded);
    }

    private static int Compare(GridCell left, GridCell right, GridCell target, IReadOnlyDictionary<GridCell, int> scores, int minimumCost)
    {
        var leftH = Heuristic(left, target, minimumCost); var rightH = Heuristic(right, target, minimumCost);
        var comparison = (scores[left] + leftH).CompareTo(scores[right] + rightH);
        if (comparison != 0) return comparison;
        comparison = leftH.CompareTo(rightH);
        if (comparison != 0) return comparison;
        comparison = left.Z.CompareTo(right.Z);
        return comparison != 0 ? comparison : left.X.CompareTo(right.X);
    }

    private static int Heuristic(GridCell from, GridCell to, int minimumCost)
    {
        var dx = Math.Abs(from.X - to.X); var dz = Math.Abs(from.Z - to.Z);
        var diagonalSteps = Math.Min(dx, dz);
        var orthogonalSteps = Math.Abs(dx - dz);
        var minimumDiagonalEdgeCost = 1414 * minimumCost / 1000;
        var minimumOrthogonalEdgeCost = 1000 * minimumCost / 1000;
        return diagonalSteps * minimumDiagonalEdgeCost + orthogonalSteps * minimumOrthogonalEdgeCost;
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
    int SegmentOriginXMillimetres, int SegmentOriginZMillimetres,
    int SegmentProgressMicrometres, int MovementRemainder, int LastSearchExpandedNodes, string? IntentId,
    int WalkingSpeedPermille = 1_000);

internal sealed class NavigationAgentState
{
    public required EntityId Id { get; init; }
    public int XMillimetres { get; set; }
    public int ZMillimetres { get; set; }
    public AgentNavigationAction Action { get; set; }
    public GridCell? Destination { get; set; }
    public List<GridCell> Route { get; set; } = [];
    public int RouteIndex { get; set; }
    public int SegmentOriginXMillimetres { get; set; }
    public int SegmentOriginZMillimetres { get; set; }
    public int SegmentProgressMicrometres { get; set; }
    public int MovementRemainder { get; set; }
    public int LastSearchExpandedNodes { get; set; }
    public string? IntentId { get; set; }
    public int WalkingSpeedPermille { get; set; } = 1_000;
}

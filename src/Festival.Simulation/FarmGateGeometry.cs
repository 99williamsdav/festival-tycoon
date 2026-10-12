namespace Festival.Simulation;

/// <summary>Footprints of the shipped timber gate, in millimetres. The open passage is not an obstacle.</summary>
public static class FarmGateGeometry
{
    public const float LeafHingeXMetres = -1.65f;
    public const float LeafYawDegrees = 72;
    public const int PersonClearanceMillimetres = 200;
    public const float HedgeEndXMetres = 3, HedgeEndZMetres = 32;
    public readonly record struct Point(double X, double Z);

    /// <summary>Only the two legacy waiting slots inside the rounded hedge ends move.
    /// The odd X cells are clear and unused by the regular six-column waiting rows.</summary>
    public static GridCell GuestWaitingCell(GridCell original) => original switch
    {
        { X: 122, Z: 192 } => new(125, 192),
        { X: 132, Z: 192 } => new(129, 192),
        _ => original
    };

    public static Point[][] ObstaclePolygons(FarmObjectReadModel? gate = null)
    {
        gate ??= LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.main-gate");
        Point World(Point p)
        {
            for (var i = 0; i < gate.YawQuarterTurns; i++) p = new(p.Z, -p.X);
            return new(p.X + gate.XMetres * 1000, p.Z + gate.ZMetres * 1000);
        }
        Point[] Rect(double minX, double maxX, double minZ, double maxZ, bool leaf = false)
        {
            var points = new[] { new Point(minX, minZ), new(maxX, minZ), new(maxX, maxZ), new(minX, maxZ) };
            var angle = LeafYawDegrees * Math.PI / 180; var c = Math.Cos(angle); var s = Math.Sin(angle);
            return points.Select(p => World(leaf ? new(p.X * c + p.Z * s + LeafHingeXMetres * 1000,
                -p.X * s + p.Z * c) : p)).ToArray();
        }
        // Exact X/Z mesh bounds, per post rather than the combined box across the entrance.
        return [Rect(-1990, -1615, -170, 215), Rect(1550, 1990, -170, 180), Rect(-30, 3420, -66, 230, true)];
    }

    public static GridCell[] SolidCells(FarmObjectReadModel? gate = null)
        => Raster(ObstaclePolygons(gate), 0);

    public static Point[][] HedgeEndPolygons() =>
    [
        [new(-3100, 31537), new(-2000, 31537), new(-2000, 32500), new(-3100, 32500)],
        [new(2000, 31500), new(3100, 31500), new(3100, 32463), new(2000, 32463)]
    ];

    // Sample walking centres with body clearance: adding another half-cell would
    // seal the existing arrival row 0.287m in front of the left hedge's visual edge.
    public static GridCell[] HedgeEndSolidCells() => Raster(HedgeEndPolygons(), 0);

    private static GridCell[] Raster(Point[][] polygons, int cellHalf)
    {
        var blocked = new SortedSet<GridCell>();
        foreach (var polygon in polygons)
        {
            var from = TraversalGrid.WorldToCell((int)Math.Floor(polygon.Min(p => p.X) - PersonClearanceMillimetres),
                (int)Math.Floor(polygon.Min(p => p.Z) - PersonClearanceMillimetres));
            var to = TraversalGrid.WorldToCell((int)Math.Ceiling(polygon.Max(p => p.X) + PersonClearanceMillimetres),
                (int)Math.Ceiling(polygon.Max(p => p.Z) + PersonClearanceMillimetres));
            for (var z = from.Z; z <= to.Z; z++)
            for (var x = from.X; x <= to.X; x++)
            {
                var cell = new GridCell(x, z); var centre = TraversalGrid.CellCentre(cell);
                var half = cellHalf + PersonClearanceMillimetres;
                // SAT between the actual oriented solid and a walking cell with body clearance.
                var axes = new List<Point> { new(1, 0), new(0, 1) };
                for (var i = 0; i < polygon.Length; i++)
                { var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length]; axes.Add(new(-(b.Z - a.Z), b.X - a.X)); }
                var intersects = axes.All(axis =>
                {
                    var projections = polygon.Select(p => p.X * axis.X + p.Z * axis.Z);
                    var mid = centre.XMillimetres * axis.X + centre.ZMillimetres * axis.Z;
                    var radius = half * (Math.Abs(axis.X) + Math.Abs(axis.Z));
                    return projections.Max() >= mid - radius && projections.Min() <= mid + radius;
                });
                if (intersects) blocked.Add(cell);
            }
        }
        return blocked.ToArray();
    }
}

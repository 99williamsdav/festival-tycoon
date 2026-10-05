namespace Festival.Simulation;

/// <summary>
/// The bands' backstage: the grass between the trailer stage and the farmhouse, kept apart from the crowd by a line of
/// crowd barriers on the audience side (with a gap at the house end, and a slight one by the front of the stage) and a
/// second line behind the stage to the hedge. Bands wait there, come back there after their set or an errand, and climb
/// to the deck by the trailer's stairs at the house end. The trailer's drawbar is at the south end.
/// </summary>
public static class Backstage
{
    /// <summary>Barrier runs, world millimetres end to end. Each blocks a one-cell line the pathfinder can't slip through.</summary>
    public static readonly (int FromX, int FromZ, int ToX, int ToZ)[] BarrierRuns =
    [
        (-14_000, 6_800, -14_000, -5_200),   // audience side, ending 1.5 m short of the stage's front corner
        (-14_000, -5_200, -15_875, -6_532),  // angled at the house end; the 2 m gap to the farmhouse wall is the way in
        (-18_300, 7_750, -31_400, 7_750),    // behind the stage, from its back corner to the west hedge
    ];

    /// <summary>The trailer's deck, walkable for performers on their marks.</summary>
    public static bool Deck(GridCell cell) => cell.X is >= 92 and <= 96 && cell.Z is >= 143 and <= 157;
    /// <summary>The stairs centred on the trailer's north end, from backstage up to the deck.</summary>
    public static bool Stairs(GridCell cell) => cell.X is >= 93 and <= 95 && cell.Z is >= 139 and <= 142;

    /// <summary>
    /// The trailer's cells and whether each can be walked: the chassis and drawbar are solid, the deck and stairs open,
    /// and the stair handrails close each side of the flight.
    /// </summary>
    public static IEnumerable<(GridCell Cell, bool Walkable)> TrailerCells()
    {
        for (var z = 139; z <= 163; z++)
        for (var x = 91; x <= 98; x++)
        {
            var cell = new GridCell(x, z);
            var chassis = z is >= 143 and <= 159 && x <= 97;
            var drawbar = z >= 160;
            var handrail = z <= 142 && x is 92 or 96;
            if (chassis || drawbar || handrail) yield return (cell, Deck(cell));
            else if (Stairs(cell)) yield return (cell, true);
        }
    }

    /// <summary>The stage and its approaches: nothing is built and no queue forms here.</summary>
    public static bool StageReserve(GridCell cell) => cell.X is >= 90 and <= 101 && cell.Z is >= 136 and <= 164;

    /// <summary>The trailer and the ground at the foot of its stairs, which an outgoing band must clear for the next.</summary>
    public static bool StageAccess(GridCell cell) => cell.X is >= 91 and <= 100 && cell.Z is >= 136 and <= 159;

    /// <summary>The backstage ground, kept clear of building.</summary>
    public static bool Area(GridCell cell) => cell.X is >= 66 and <= 100 && cell.Z is >= 112 and <= 143;

    /// <summary>Where a band member waits: a loose cluster on the stage side of backstage, by roster order among the performers.</summary>
    public static GridCell Place(int performerIndex) => new(76 + performerIndex % 4 * 3, 126 + performerIndex / 4 % 3 * 3);

    /// <summary>The garden gate in the west hedge, from the lane into backstage: two cells clear between its stone piers.</summary>
    public static bool HedgeGate(GridCell cell) => cell.X is >= 63 and <= 65 && cell.Z is 123 or 124;

    /// <summary>Where a band member steps off the lane, in a loose line along it outside the garden gate.</summary>
    public static GridCell LaneStart(int performerIndex) => new(56 + performerIndex % 2 * 2, 112 + performerIndex * 3);

    /// <summary>Where a member of staff comes out of the farmhouse front door.</summary>
    public static GridCell DoorStart(int staffIndex) => new(84 + staffIndex % 3, 112 + staffIndex / 3);

    /// <summary>The flight cases and crate pile, world millimetres: solid, so nobody walks through them.</summary>
    public static readonly (int X, int Z)[] Props = [(-18_600, 3_600), (-19_500, 2_200), (-20_500, -1_500)];

    /// <summary>Every cell the props stand on, with a little room round each.</summary>
    public static IEnumerable<GridCell> PropCells() => Props.SelectMany(prop =>
    {
        var centre = TraversalGrid.WorldToCell(prop.X, prop.Z);
        return Enumerable.Range(-1, 3).SelectMany(dz => Enumerable.Range(-1, 3).Select(dx => new GridCell(centre.X + dx, centre.Z + dz)));
    });

    /// <summary>Every cell the barrier runs block.</summary>
    public static IEnumerable<GridCell> BarrierCells() => BarrierRuns.SelectMany(run =>
        Line(TraversalGrid.WorldToCell(run.FromX, run.FromZ), TraversalGrid.WorldToCell(run.ToX, run.ToZ))).Distinct();

    private static IEnumerable<GridCell> Line(GridCell from, GridCell to)
    {
        // Eight-way steps: the pathfinder never cuts a corner between two blocked cells, so the line holds.
        int x = from.X, z = from.Z, dx = Math.Abs(to.X - x), dz = -Math.Abs(to.Z - z), sx = x < to.X ? 1 : -1, sz = z < to.Z ? 1 : -1, error = dx + dz;
        while (true)
        {
            yield return new(x, z);
            if (x == to.X && z == to.Z) yield break;
            var doubled = 2 * error;
            if (doubled >= dz) { error += dz; x += sx; }
            if (doubled <= dx) { error += dx; z += sz; }
        }
    }
}

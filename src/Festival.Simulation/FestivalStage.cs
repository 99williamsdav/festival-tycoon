namespace Festival.Simulation;

/// <summary>
/// One stage on the farm: its running order's fixed times, where its band stands and climbs up from, the ground its
/// crowd watches from, and the backstage its band waits in. Everything here is fixed; what happens on the day lives
/// in the programme and live performance kept for each stage.
/// </summary>
public sealed class FestivalStage
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Each set's start and end, in ticks after opening.</summary>
    public required IReadOnlyList<int> SlotStarts { get; init; }
    public required IReadOnlyList<int> SlotEnds { get; init; }
    public int SlotCount => SlotStarts.Count;
    /// <summary>Each player's mark on the deck, by role: lead, bass, drums.</summary>
    public required IReadOnlyList<GridCell> PerformerMarks { get; init; }
    /// <summary>Where each player waits at the foot of the stairs, then a step on the flight.</summary>
    public required IReadOnlyList<GridCell> AccessCells { get; init; }
    public required IReadOnlyList<GridCell> StairCells { get; init; }
    public required Func<GridCell, bool> Deck { get; init; }
    public required Func<GridCell, bool> Stairs { get; init; }
    /// <summary>The stage's cells and whether each can be walked, for the ground's blocking.</summary>
    public required Func<IEnumerable<(GridCell Cell, bool Walkable)>> Cells { get; init; }
    /// <summary>The stage and its approaches: nothing is built and no queue forms here.</summary>
    public required Func<GridCell, bool> Reserve { get; init; }
    /// <summary>A rectangle holding every reserved cell, for scanning the reserve.</summary>
    public required (int MinX, int MaxX, int MinZ, int MaxZ) ReserveBounds { get; init; }
    /// <summary>The ground an outgoing band must clear before the next set can start.</summary>
    public required Func<GridCell, bool> Access { get; init; }
    /// <summary>Where the stage's band members wait, and where they step off the lane, by their order in the stage's band list.</summary>
    public required Func<int, GridCell> BackstagePlace { get; init; }
    public required Func<int, GridCell> ArrivalStart { get; init; }
    /// <summary>Every place a listener can stand, in the order they're considered.</summary>
    public required IReadOnlyList<GridCell> ListeningPlaces { get; init; }
    /// <summary>The way the crowd faces away from the stage, one grid step: (1, 0) for a crowd out along +X.</summary>
    public required (int X, int Z) Facing { get; init; }
    /// <summary>The crowd's front row, along the facing axis, and the line straight out from the stage's middle across it.</summary>
    public required int Front { get; init; }
    public required int Sightline { get; init; }
    /// <summary>Close to the stage, where a listener walks at their own pace rather than the crowd's.</summary>
    public required (int MinX, int MaxX, int MinZ, int MaxZ) NearGround { get; init; }
    /// <summary>The widest a saved listener's place may be.</summary>
    public required (int MinX, int MaxX, int MinZ, int MaxZ) PlaceLimits { get; init; }

    /// <summary>The audience's ground: every listening place, as one rectangle.</summary>
    public required (int MinX, int MaxX, int MinZ, int MaxZ) AudienceBounds { get; init; }
    /// <summary>Where this stage's own sound engineer mixes; null where the engineer keeps the prepared place.</summary>
    public GridCell? MixingPlace { get; init; }
    /// <summary>Where the model stands for the presentation: its ground origin in world millimetres, yaw and deck height.</summary>
    public required StagePlacement Placement { get; init; }

    public static (int MinX, int MaxX, int MinZ, int MaxZ) BoundsOf(IReadOnlyList<GridCell> places) =>
        (places.Min(c => c.X), places.Max(c => c.X), places.Min(c => c.Z), places.Max(c => c.Z));

    public bool InAudienceArea(GridCell cell) =>
        cell.X >= AudienceBounds.MinX && cell.X <= AudienceBounds.MaxX && cell.Z >= AudienceBounds.MinZ && cell.Z <= AudienceBounds.MaxZ;

    /// <summary>How far back from the front row a cell is, in cells: negative in front of it.</summary>
    public int Depth(GridCell cell) => Facing.X != 0 ? (cell.X - Front) * Facing.X : (cell.Z - Front) * Facing.Z;
    /// <summary>How far to one side of the stage's middle line a cell is, in cells.</summary>
    public int Lateral(GridCell cell) => Math.Abs(Facing.X != 0 ? cell.Z - Sightline : cell.X - Sightline);
    /// <summary>The middle of the audience's ground, for estimating a walk there.</summary>
    public GridCell AudienceCentre => new((AudienceBounds.MinX + AudienceBounds.MaxX) / 2, (AudienceBounds.MinZ + AudienceBounds.MaxZ) / 2);
}

/// <summary>
/// Where a stage model stands: its ground origin in world millimetres, its yaw in degrees (0 when its audience is out
/// along +Z, as the model faces), and the height of its deck in millimetres.
/// </summary>
public sealed record StagePlacement(int XMillimetres, int ZMillimetres, int YawDegrees, int DeckHeightMillimetres);

/// <summary>The farm's stages, in the fixed order every per-stage pass runs in.</summary>
public static class FestivalStages
{
    public const string MainId = "stage.main";
    public const string PondId = "stage.pond";

    private static readonly GridCell[] TrailerPlaces = TrailerListeningPlaces().ToArray();
    private static readonly GridCell[] PondPlaces = PondListeningPlaces().ToArray();

    /// <summary>The trailer stage by the farmhouse: the festival's first stage.</summary>
    public static readonly FestivalStage Main = new()
    {
        Id = MainId,
        Name = "Trailer stage",
        SlotStarts = [4_800, 16_400, 28_000],
        SlotEnds = [13_200, 24_800, 37_600],
        PerformerMarks = [new(96, 150), new(94, 146), new(93, 152)],
        // Backstage at the foot of the trailer's north stairs, then a step on the flight, one column for each player.
        AccessCells = [new(93, 137), new(94, 137), new(95, 137)],
        StairCells = [new(93, 141), new(94, 141), new(95, 141)],
        Deck = Backstage.Deck,
        Stairs = Backstage.Stairs,
        Cells = Backstage.TrailerCells,
        Reserve = Backstage.StageReserve,
        ReserveBounds = (90, 101, 136, 164),
        Access = Backstage.StageAccess,
        BackstagePlace = Backstage.Place,
        ArrivalStart = Backstage.LaneStart,
        ListeningPlaces = TrailerPlaces,
        AudienceBounds = FestivalStage.BoundsOf(TrailerPlaces),
        Facing = (1, 0),
        Front = 103,
        Sightline = 150,
        NearGround = (103, 126, 131, 169),
        PlaceLimits = (103, 122, 135, 165),
        // The farm scene's trailer; its presentation keeps its own fixed contract (Main.LivePerformance.cs).
        Placement = new(LowerWitteringFarmScenario.TrailerStageXMillimetres, LowerWitteringFarmScenario.TrailerStageZMillimetres, 90, 1_190),
    };

    /// <summary>
    /// The Pond Stage: the modular riser in the pond corner of the field (+X, +Z), its back to the farm pond and its crowd
    /// on the open grass towards the barns (-Z), well away from the trailer's crowd and the farm track. Its band comes in
    /// off the lane by a small gate in the +X hedge, waits beside the riser and climbs its side stair at the +X end.
    /// </summary>
    public static readonly FestivalStage Pond = new()
    {
        Id = PondId,
        Name = "The Pond Stage",
        // Each set starts about halfway through the trailer's; the last is cut short to finish with the trailer's.
        SlotStarts = [9_000, 20_600, 32_800],
        SlotEnds = [17_400, 29_000, 37_600],
        // Front left, front right and the drums, as the riser's review marks.
        PerformerMarks = [new(170, 161), new(165, 161), new(168, 163)],
        AccessCells = [new(178, 159), new(178, 160), new(178, 161)],
        StairCells = [new(175, 159), new(175, 160), new(175, 161)],
        Deck = PondRiser.Deck,
        Stairs = PondRiser.Stairs,
        Cells = PondRiser.Cells,
        Reserve = PondRiser.Reserve,
        ReserveBounds = PondRiser.ReserveBounds,
        Access = PondRiser.Access,
        BackstagePlace = PondRiser.Place,
        ArrivalStart = PondRiser.LaneStart,
        ListeningPlaces = PondPlaces,
        AudienceBounds = FestivalStage.BoundsOf(PondPlaces),
        Facing = (0, -1),
        Front = 154,
        Sightline = 168,
        NearGround = (154, 183, 138, 157),
        PlaceLimits = (156, 180, 141, 155),
        MixingPlace = new(168, 138),
        Placement = new(PondRiser.OriginXMillimetres, PondRiser.OriginZMillimetres, 180, PondRiser.DeckHeightMillimetres),
    };

    /// <summary>The whole catalogue, in stage order.</summary>
    public static readonly IReadOnlyList<FestivalStage> All = [Main, Pond];
    /// <summary>The trailer stage alone, as a festival without the Pond Stage plays.</summary>
    public static readonly IReadOnlyList<FestivalStage> MainOnly = [Main];

    /// <summary>The stages a festival runs: the trailer, and the Pond Stage when it's open.</summary>
    public static IReadOnlyList<FestivalStage> For(bool pondOpen) => pondOpen ? All : MainOnly;

    public static FestivalStage? Find(string id) => All.FirstOrDefault(stage => stage.Id == id);
    /// <summary>A stage's place in the given stage list, or -1 when it isn't there.</summary>
    public static int IndexOf(IReadOnlyList<FestivalStage> stages, string id)
    {
        for (var index = 0; index < stages.Count; index++)
            if (stages[index].Id == id) return index;
        return -1;
    }

    /// <summary>Whether any of these stages keeps this cell clear of building and queues.</summary>
    public static bool InAnyReserve(IReadOnlyList<FestivalStage> stages, GridCell cell)
    {
        foreach (var stage in stages)
            if (stage.Reserve(cell)) return true;
        return false;
    }

    private static IEnumerable<GridCell> TrailerListeningPlaces()
    {
        // The rotated trailer faces increasing X. This irregular apron sits between
        // the front edge and vehicle track, leaving the visible south-end stairs
        // and generator footprint to terrain/route exclusions.
        for (var band = 0; band < 9; band++)
        for (var lane = -7; lane <= 7; lane++)
        {
            var x = 103 + band * 2 + Math.Abs((lane * 7 + band * 11) % 3);
            var z = 150 + lane * 2 + Math.Abs((band * 5 + lane * 3) % 3) - 1;
            yield return new GridCell(x, z);
        }
    }

    private static IEnumerable<GridCell> PondListeningPlaces()
    {
        // A smaller apron than the trailer's: six rows of eleven, out from the riser's front edge along -Z.
        for (var band = 0; band < 6; band++)
        for (var lane = -5; lane <= 5; lane++)
        {
            var z = 154 - band * 2 - Math.Abs((lane * 7 + band * 11) % 3);
            var x = 168 + lane * 2 + Math.Abs((band * 5 + lane * 3) % 3) - 1;
            yield return new GridCell(x, z);
        }
    }
}

/// <summary>
/// The Pond Stage's ground. The modular riser stands with its ground origin at world (20.0 m, 17.0 m), turned half round
/// so its audience is out along world -Z and its side stair, on the model's -X end, comes down at the world +X end.
/// In cells: the deck covers x 162..173, z 158..165 and stands 0.9 m high; the stair runs x 174..177 on rows 159..161,
/// with its foot at x 178. The pond's own generator stands off the deck's -X end; the band waits past the stair, between
/// the riser and the hedge, and comes in by a gate cut in the +X hedge at z 161..162.
/// </summary>
public static class PondRiser
{
    public const int OriginXMillimetres = 20_000, OriginZMillimetres = 17_000, DeckHeightMillimetres = 900;
    /// <summary>The pond generator, world millimetres; its body blocks a metre and a half either side along X, a metre along Z.</summary>
    public const int GeneratorXMillimetres = 14_250, GeneratorZMillimetres = 18_000;

    /// <summary>The deck's walkable boards: inside the rim, plus the entry column where the stair meets it.</summary>
    public static bool Deck(GridCell cell) => cell.X is >= 163 and <= 172 && cell.Z is >= 159 and <= 164 || cell.X == 173 && cell.Z is >= 159 and <= 161;
    /// <summary>The side stair, from its toe at x 177 to the top tread at x 174.</summary>
    public static bool Stairs(GridCell cell) => cell.X is >= 174 and <= 177 && cell.Z is >= 159 and <= 161;
    /// <summary>The pond gate: two cells clear through the +X hedge.</summary>
    public static bool Gate(GridCell cell) => cell.X is 191 or 192 && cell.Z is 161 or 162;

    /// <summary>The generator's body cells.</summary>
    public static bool Generator(GridCell cell)
    {
        var lower = TraversalGrid.WorldToCell(GeneratorXMillimetres - 1_500, GeneratorZMillimetres - 1_000);
        var upper = TraversalGrid.WorldToCell(GeneratorXMillimetres + 1_500, GeneratorZMillimetres + 1_000);
        return cell.X >= lower.X && cell.X <= upper.X && cell.Z >= lower.Z && cell.Z <= upper.Z;
    }

    /// <summary>
    /// The riser's cells and whether each can be walked: the deck's rim and the ground under it are solid (it stands
    /// 0.9 m up), the boards and stair are open, the stair's sides are closed, and the generator is solid. The pond
    /// gate is opened through the hedge.
    /// </summary>
    public static IEnumerable<(GridCell Cell, bool Walkable)> Cells()
    {
        for (var z = 158; z <= 165; z++)
        for (var x = 162; x <= 173; x++)
            yield return (new(x, z), Deck(new(x, z)));
        for (var x = 174; x <= 177; x++)
        {
            yield return (new(x, 158), false);
            for (var z = 159; z <= 161; z++) yield return (new(x, z), true);
            yield return (new(x, 162), false);
        }
        for (var z = 150; z <= 175; z++)
        for (var x = 150; x <= 161; x++)
            if (Generator(new(x, z))) yield return (new(x, z), false);
        for (var z = 161; z <= 162; z++)
        for (var x = 191; x <= 192; x++)
            yield return (new(x, z), true);
    }

    public static readonly (int MinX, int MaxX, int MinZ, int MaxZ) ReserveBounds = (150, 190, 156, 168);
    /// <summary>The riser, its stair foot, the generator, the band's waiting ground and the way in from the gate.</summary>
    public static bool Reserve(GridCell cell) => cell.X >= ReserveBounds.MinX && cell.X <= ReserveBounds.MaxX &&
        cell.Z >= ReserveBounds.MinZ && cell.Z <= ReserveBounds.MaxZ;
    /// <summary>The riser and the ground at the foot of its stair, which an outgoing band must clear for the next.</summary>
    public static bool Access(GridCell cell) => cell.X is >= 162 and <= 179 && cell.Z is >= 157 and <= 166;
    /// <summary>Where a band member waits: a loose cluster between the stair and the hedge, by order in the stage's band list.</summary>
    public static GridCell Place(int performerIndex) => new(182 + performerIndex % 3 * 3, 157 + performerIndex / 3 % 3 * 3);
    /// <summary>Where a band member steps off the lane outside the +X hedge, in a loose line along it.</summary>
    public static GridCell LaneStart(int performerIndex) => new(196 + performerIndex % 2 * 2, 150 + performerIndex * 2);
}

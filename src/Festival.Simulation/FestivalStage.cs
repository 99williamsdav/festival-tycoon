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
    /// <summary>The ground an outgoing band must clear before the next set can start.</summary>
    public required Func<GridCell, bool> Access { get; init; }
    /// <summary>Where the stage's band members wait, and where they step off the lane, by performer order.</summary>
    public required Func<int, GridCell> BackstagePlace { get; init; }
    public required Func<int, GridCell> ArrivalStart { get; init; }
    /// <summary>Every place a listener can stand, in the order they're considered.</summary>
    public required IReadOnlyList<GridCell> ListeningPlaces { get; init; }
    /// <summary>The crowd's front row and the line straight out from the stage's middle, for scoring places.</summary>
    public required int FrontX { get; init; }
    public required int SightlineZ { get; init; }
    /// <summary>Close to the stage, where a listener walks at their own pace rather than the crowd's.</summary>
    public required (int MinX, int MaxX, int MinZ, int MaxZ) NearGround { get; init; }
    /// <summary>The widest a saved listener's place may be.</summary>
    public required (int MinX, int MaxX, int MinZ, int MaxZ) PlaceLimits { get; init; }

    /// <summary>The audience's ground: every listening place, as one rectangle.</summary>
    public required (int MinX, int MaxX, int MinZ, int MaxZ) AudienceBounds { get; init; }

    public static (int MinX, int MaxX, int MinZ, int MaxZ) BoundsOf(IReadOnlyList<GridCell> places) =>
        (places.Min(c => c.X), places.Max(c => c.X), places.Min(c => c.Z), places.Max(c => c.Z));

    public bool InAudienceArea(GridCell cell) =>
        cell.X >= AudienceBounds.MinX && cell.X <= AudienceBounds.MaxX && cell.Z >= AudienceBounds.MinZ && cell.Z <= AudienceBounds.MaxZ;
}

/// <summary>The farm's stages, in the fixed order every per-stage pass runs in.</summary>
public static class FestivalStages
{
    public const string MainId = "stage.main";

    private static readonly GridCell[] TrailerPlaces = TrailerListeningPlaces().ToArray();

    /// <summary>The trailer stage by the farmhouse: the festival's first, and so far only, stage.</summary>
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
        Access = Backstage.StageAccess,
        BackstagePlace = Backstage.Place,
        ArrivalStart = Backstage.LaneStart,
        ListeningPlaces = TrailerPlaces,
        AudienceBounds = FestivalStage.BoundsOf(TrailerPlaces),
        FrontX = 103,
        SightlineZ = 150,
        NearGround = (103, 126, 131, 169),
        PlaceLimits = (103, 122, 135, 165),
    };

    public static readonly IReadOnlyList<FestivalStage> All = [Main];

    public static FestivalStage? Find(string id) => All.FirstOrDefault(stage => stage.Id == id);
    public static int IndexOf(string id)
    {
        for (var index = 0; index < All.Count; index++)
            if (All[index].Id == id) return index;
        return -1;
    }

    /// <summary>Whether any stage keeps this cell clear of building and queues.</summary>
    public static bool InAnyReserve(GridCell cell) => All.Any(stage => stage.Reserve(cell));

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
}

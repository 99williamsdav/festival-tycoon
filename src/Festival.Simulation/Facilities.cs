namespace Festival.Simulation;

/// <summary>
/// The physical service facilities on the festival field and their live queues. The Build
/// layout (<see cref="PreparationSnapshot.BuildPlacements"/>) is the plan; this is what is
/// actually standing and serving people.
/// </summary>
public sealed record FacilitiesSnapshot(int Version, WaterPointState[] Taps, ToiletFacility[] Toilets, ImmersionVendor[] Vendors);

public sealed partial class GameSession
{
    private FacilitiesSnapshot? _facilities;

    /// <summary>The current facilities read model; a shared immutable value.</summary>
    public FacilitiesSnapshot? CaptureFacilities() => _facilities;
    internal string? FacilitiesCanonicalJson => _facilities is null ? null : System.Text.Json.JsonSerializer.Serialize(_facilities);

    private static readonly FacilitiesSnapshot NoFacilities = new(1, [], [], []);
    private WaterPointState[] Taps => _facilities?.Taps ?? [];
    private ToiletFacility[] Toilets => _facilities?.Toilets ?? [];
    private ImmersionVendor[] Vendors => _facilities?.Vendors ?? [];
    private void SetTaps(WaterPointState[] taps) => _facilities = (_facilities ?? NoFacilities) with { Taps = taps };
    private void SetToilets(ToiletFacility[] toilets) => _facilities = (_facilities ?? NoFacilities) with { Toilets = toilets };
    private void SetVendors(ImmersionVendor[] vendors) => _facilities = (_facilities ?? NoFacilities) with { Vendors = vendors };

    /// <summary>The toilet a campaign opens with before any Build layout exists.</summary>
    private static ToiletFacility OpeningMainToilet() => new("toilet.main", new(172, 140), 0, [], null, false, 0, 0, 0,
        ToiletRules.CapacityMillilitres, ToiletRules.ContainmentPermille);

    /// <summary>The tap a campaign opens with before any Build layout exists.</summary>
    private static WaterPointState OpeningMainTap() => new("water.main", MedicalWaterCell, [], [], null, 0) { GeometryVersion = 1 };

    // Placement facts derived from the Build layout. Before a layout exists (the opening baseline)
    // the main tap stands at its fixed site and no extra taps or posts are placed.
    private static BuildPlacement? PlacedMainTap(PreparationSnapshot? p) => p?.BuildPlacements?.FirstOrDefault(item => item.Id == "water.main");
    private static GridCell PrimaryWaterCell(PreparationSnapshot? p) => PlacedMainTap(p)?.Cell ?? MedicalWaterCell;
    private static int PrimaryWaterQuarterTurns(PreparationSnapshot? p) => PlacedMainTap(p)?.QuarterTurns ?? 0;
    private static int PrimaryWaterGeometryVersion(PreparationSnapshot? p) => p?.BuildPlacements is null || PlacedMainTap(p) is not null ? 1 : 0;
    private static WaterPlacement[] EffectiveWaterPlacements(PreparationSnapshot p) => (p.BuildPlacements ?? [])
        .Where(item => item.Kind == BuildServiceKind.WaterTap && item.Id != "water.main").OrderBy(item => item.Id, StringComparer.Ordinal)
        .Select(item => new WaterPlacement(item.Id, item.Cell) { QuarterTurns = item.QuarterTurns, GeometryVersion = 1 }).ToArray();
    private static ResponsePostPlacement? FirstAidPlacement(PreparationSnapshot? p) => PlacedPost(p, BuildServiceKind.FirstAid);
    private static ResponsePostPlacement? StewardPostPlacement(PreparationSnapshot? p) => PlacedPost(p, BuildServiceKind.StewardPost);
    private static ResponsePostPlacement? PlacedPost(PreparationSnapshot? p, BuildServiceKind kind) =>
        p?.BuildPlacements?.FirstOrDefault(item => item.Kind == kind) is { } post ? new(post.Cell, post.QuarterTurns) : null;

    /// <summary>The main tap is standing unless a Build layout exists that does not place it.</summary>
    private static bool MainTapStanding(PreparationSnapshot? p) => p?.BuildPlacements?.Any(item => item.Id == "water.main") != false;
}

namespace Festival.Simulation;

/// <summary>
/// The physical service facilities on the festival field and their live queues. The Build
/// layout (<see cref="PreparationSnapshot.BuildPlacements"/>) is the plan; this is what is
/// actually standing and serving people.
/// </summary>
public sealed record FacilitiesSnapshot(int Version, WaterPointState[] Taps);

public sealed partial class GameSession
{
    private FacilitiesSnapshot? _facilities;

    /// <summary>The current facilities read model; a shared immutable value.</summary>
    public FacilitiesSnapshot? CaptureFacilities() => _facilities;
    internal string? FacilitiesCanonicalJson => _facilities is null ? null : System.Text.Json.JsonSerializer.Serialize(_facilities);

    private WaterPointState[] Taps => _facilities?.Taps ?? [];
    private void SetTaps(WaterPointState[] taps) => _facilities = (_facilities ?? new FacilitiesSnapshot(1, [])) with { Taps = taps };

    /// <summary>The tap a campaign opens with before any Build layout exists.</summary>
    private static WaterPointState OpeningMainTap() => new("water.main", MedicalWaterCell, [], [], null, 0) { GeometryVersion = 1 };

    /// <summary>The main tap is standing unless a Build layout exists that does not place it.</summary>
    private static bool MainTapStanding(PreparationSnapshot? p) => p?.BuildPlacements?.Any(item => item.Id == "water.main") != false;
}

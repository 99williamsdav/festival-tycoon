namespace Festival.Simulation;

public sealed partial class GameSession
{
    // Defensive save/input bound, not a building allowance. Even the smallest reserved footprints
    // cannot fit this many on the existing field. Never enumerate this value to create catalogue rows.
    public const int ClassicConstructionSafetyBound = 4_096;
    public bool ConstructionQuantitiesUnrestricted => IsClassicCampaign;
    private static long ClassicClosingCashBound(long opening,IReadOnlyList<BuildPlacement> layout,int capacity,int peopleBound=MaxActivePeople) => checked(opening+
        layout.Count(p=>p.Kind==BuildServiceKind.FoodVan)*(long)FoodTraders.All.Max(t=>t.PitchFeePennies)+
        (long)capacity*MostGuestBudgetPennies+(long)(peopleBound-capacity)*CrewBudgetPennies);
    private static int ConstructionLimit(BuildServiceKind kind, int tier, bool classic) =>
        classic ? ClassicConstructionSafetyBound : BuildServiceLimit(kind, tier);

    /// <summary>Existing vans and one next candidate. The catalogue grows only when the player places a van.</summary>
    public IReadOnlyList<string> FoodVanCatalogueIds => IsClassicCampaign
        ? PlacedVans.Append(Stalls.Next(BuildServiceKind.FoodVan, CaptureBuildPlacements().Select(p => p.Id)))
            .Distinct(StringComparer.Ordinal).OrderBy(Stalls.Number).ToArray()
        : Enumerable.Range(1, ServiceLimit(BuildServiceKind.FoodVan)).Select(n => Stalls.Id(BuildServiceKind.FoodVan, n)).ToArray();

    private static bool NumberedConstructionId(string id, string first, string extraPrefix, int maximum)
    {
        if (id == first) return true;
        if (!id.StartsWith(extraPrefix, StringComparison.Ordinal) ||
            !int.TryParse(id[extraPrefix.Length..], out var index) || index < 1 || index >= maximum) return false;
        return id == extraPrefix + index;
    }
}

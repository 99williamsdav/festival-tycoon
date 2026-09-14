namespace Festival.Simulation;

public enum FarmObjectKind { Farmhouse, SmallBarn, LargeBarn, TrailerStage, ServicePoint, Gate }
public enum FarmObjectState { Inherited, Open }

/// <summary>Immutable player-visible site data in authoritative scenario metres.</summary>
public sealed record FarmObjectReadModel(
    string StableId, string DisplayName, FarmObjectKind Kind,
    double XMetres, double ZMetres, int YawQuarterTurns,
    FarmObjectState State, bool IsPermanent, bool IsMovable, bool IsSelectable);

public sealed record FarmSceneReadModel(
    string ScenarioId, string DisplayName, double SiteWidthMetres, double SiteDepthMetres,
    IReadOnlyList<FarmObjectReadModel> Objects)
{
    public FarmObjectReadModel GetRequiredObject(string stableId) =>
        Objects.Single(item => string.Equals(item.StableId, stableId, StringComparison.Ordinal));
}

public static class LowerWitteringFarmScenario
{
    public const string ScenarioId = "scenario.lower-wittering-farm";

    private static readonly FarmSceneReadModel Model = new(
        ScenarioId, "Lower Wittering Farm", 128, 128, Array.AsReadOnly(new[]
        {
            new FarmObjectReadModel("farm.farmhouse", "Farmhouse", FarmObjectKind.Farmhouse, -22, -14, 0, FarmObjectState.Inherited, true, false, true),
            new FarmObjectReadModel("farm.small-barn", "Small Storage Barn", FarmObjectKind.SmallBarn, 19, -17, 3, FarmObjectState.Inherited, true, false, true),
            new FarmObjectReadModel("farm.large-barn", "Large Barn", FarmObjectKind.LargeBarn, 18, 12, 2, FarmObjectState.Inherited, true, false, true),
            new FarmObjectReadModel("farm.trailer-stage", "Trailer Stage", FarmObjectKind.TrailerStage, -10, 11, 1, FarmObjectState.Inherited, false, false, true),
            new FarmObjectReadModel("farm.service-point", "Generic Service Point", FarmObjectKind.ServicePoint, 4, 18, 2, FarmObjectState.Inherited, false, false, true),
            new FarmObjectReadModel("farm.main-gate", "Main Farm Gate", FarmObjectKind.Gate, 0, 30, 0, FarmObjectState.Open, true, false, true),
        }));

    public static FarmSceneReadModel CreateReadModel() => Model;
}

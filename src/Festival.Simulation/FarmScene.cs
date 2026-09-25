using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
    // The trailer is set back from the vehicle track. Its yaw turns the open
    // face toward increasing world X; the visible steps meet the south end.
    public const int TrailerStageXMillimetres = -16_000;
    public const int TrailerStageZMillimetres = 11_000;
    public const string LayoutRevision = "r0.04-rear-barn-front-post-back-aid-v3";
    private const string BaseAssetContentHash = "d7e7597670c2f9bc2552fa5df29f4afe294270e346643160e92feb1436bb1dd9";

    private static readonly FarmSceneReadModel Model = new(
        ScenarioId, "Lower Wittering Farm", 128, 128, Array.AsReadOnly(new[]
        {
            new FarmObjectReadModel("farm.farmhouse", "Farmhouse", FarmObjectKind.Farmhouse, -22, -14, 0, FarmObjectState.Inherited, true, false, true),
            new FarmObjectReadModel("farm.small-barn", "Small Storage Barn", FarmObjectKind.SmallBarn, 19, -17, 3, FarmObjectState.Inherited, true, false, true),
            new FarmObjectReadModel("farm.large-barn", "Large Barn", FarmObjectKind.LargeBarn, 0, -23, 0, FarmObjectState.Inherited, true, false, true),
            new FarmObjectReadModel("farm.trailer-stage", "Trailer Stage", FarmObjectKind.TrailerStage,
                TrailerStageXMillimetres / 1000.0, TrailerStageZMillimetres / 1000.0, 1, FarmObjectState.Inherited, false, false, true),
            new FarmObjectReadModel("farm.service-point", "Generic Service Point", FarmObjectKind.ServicePoint, 29, -5, 2, FarmObjectState.Inherited, false, false, true),
            new FarmObjectReadModel("farm.main-gate", "Main Farm Gate", FarmObjectKind.Gate, 0, 30, 0, FarmObjectState.Open, true, false, true),
        }));

    public static FarmSceneReadModel CreateReadModel() => Model;
    // This immutable scene is content, not mutable session state. Its fingerprint
    // changes when placements or the controlled stair/apron layout changes, so an
    // older save cannot silently render the same people at a different stage.
    public static string ContentCompatibilityHash => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        BaseAssetContentHash + "|" + LayoutRevision + "|" + JsonSerializer.Serialize(Model.Objects)))).ToLowerInvariant();
}

namespace Festival.Simulation;

/// <summary>
/// A food trader: they bring their own van and stock, keep what they take, and pay the festival a pitch fee to trade.
/// A trader who pays more is one who cuts corners: slower to serve, or with less to sell before running out.
/// </summary>
/// <param name="ServicePermille">How long they take to serve a portion, against the standard 4 seconds.</param>
/// <param name="Portions">How much they bring: once it's gone, they've sold out.</param>
public sealed record FoodTrader(string Id, string Name, string Blurb, int PitchFeePennies, int ServicePermille, int Portions);

public sealed record ChooseFoodTraderCommand(string TraderId) : SessionCommand;

public static class FoodTraders
{
    // Tier 1: three chip vans.
    public static readonly FoodTrader[] All =
    [
        new("trader.frying-tonight", "Frying Tonight", "A steady family chippy. Pays a fair pitch fee.", 3_000, 1_000, 60),
        new("trader.codfather", "The Codfather", "Pays handsomely for the pitch, but takes his time and brings too little.", 4_500, 1_500, 30),
        new("trader.chip-off-the-old-block", "Chip Off The Old Block", "Quick, generous portions, and a pitch fee to match: barely any.", 1_500, 750, 80),
    ];

    public static FoodTrader Default => All[0];
    public static FoodTrader? Find(string? id) => All.FirstOrDefault(t => t.Id == id);
}

public sealed partial class GameSession
{
    /// <summary>The trader at the food van: the one chosen this festival, the standard one until then.</summary>
    public FoodTrader FoodTrader => FoodTraders.Find(_preparation?.Plan?.TraderId) ?? FoodTraders.Default;

    /// <summary>Whether a food van is pitched at all: no van, no trader, no pitch fee.</summary>
    private bool FoodVanPitched => _preparation?.BuildPlacements.Any(p => p.Kind == BuildServiceKind.FoodVan) == true;

    /// <summary>The pitch fee the food trader pays at opening.</summary>
    public long PlannedPitchFeePennies => FoodVanPitched ? FoodTrader.PitchFeePennies : 0;

    /// <summary>What the festival itself took at its stalls: the bar's sales, not the food trader's.</summary>
    private static long FestivalTakings(ImmersionSnapshot? immersion) =>
        immersion?.Purchases.Where(purchase => purchase.Product != ImmersionProduct.Chips).Sum(purchase => (long)purchase.PricePennies) ?? 0;

    /// <summary>The pitch fee received for this attempt, once it opened.</summary>
    private static long ReceivedPitchFee(PreparationSnapshot p) =>
        p.Plan is { Committed: true } ? p.SetupPayments?.SingleOrDefault(setup => setup.Attempt == p.Attempt)?.PitchFeePennies ?? 0 : 0;

    private CommandResult? ValidateChooseFoodTrader(EntityId? target, ChooseFoodTraderCommand command) =>
        target is not null || _preparation is not { Status: PreparationStatus.Preparing, Plan: { Committed: false } }
            ? CommandResult.Rejected(CommandReasonCode.WrongPhase, "Choose the food trader while preparing.")
            : FoodTraders.Find(command.TraderId) is null ? CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Unknown food trader.") : null;

    private void ApplyChooseFoodTrader(ChooseFoodTraderCommand command) =>
        _preparation = _preparation! with { Plan = _preparation.Plan! with { TraderId = command.TraderId } };
}

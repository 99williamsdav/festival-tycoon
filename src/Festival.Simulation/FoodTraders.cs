namespace Festival.Simulation;

/// <summary>
/// A food trader: they bring their own van and stock, keep what they take, and pay the festival a pitch fee to trade.
/// Tier 1 has one, a chip van; later cuisines will differ by how fast they serve, how much they earn and how
/// satisfying they are (chips: quick, cheap, not very filling).
/// </summary>
/// <param name="Menu">What they sell, as the crowd and the player see it.</param>
/// <param name="Product">What a portion is: each trader sells their own food, and it fills and pleases by what it is.</param>
/// <param name="Art">The van's livery and sign artwork.</param>
/// <param name="ServicePermille">How long they take to serve a portion, against the standard 4 seconds.</param>
/// <param name="EnjoymentPercent">How satisfying a portion is, against chips. Not shown to the player.</param>
/// <param name="FillingPercent">How much hunger a portion takes away, against chips.</param>
/// <param name="PricePennies">What a portion costs a guest.</param>
/// <param name="PortionCostPennies">What a portion they sell costs them to make. They bring what they need, so nothing's wasted.
/// Not shown to the player.</param>
/// <param name="AppealPennies">How much more than plain chips the average guest would happily pay for it. Thriftier guests
/// weigh the price more and lean to cheap food, freer spenders to good food; quoted at the crowd's mean thrift, so with
/// appeal matching the price difference two vans side by side would split the crowd evenly. Pizza's is a little more than
/// that: side by side, it would take about three guests in four.</param>
public sealed record FoodTrader(string Id, string Name, string Menu, ImmersionProduct Product, string Art, string Blurb, int PitchFeePennies, int ServicePermille,
    int PricePennies, int PortionCostPennies, int AppealPennies, int EnjoymentPercent = 100, int FillingPercent = 100)
{
    /// <summary>Slower to serve than the standard chip van: the one thing the picker warns about.</summary>
    public bool SlowService => ServicePermille > 1_000;
}

public sealed record ChooseFoodTraderCommand(string TraderId) : SessionCommand;

/// <summary>
/// How a food trader's day is going, from their side of the counter: what they've sold and taken, what their stock and
/// pitch cost them, and so what they've made. Kept for the trader's own mood later (a van that loses money, because of
/// where it was put or a poor festival, isn't happy); never shown to the player.
/// </summary>
public sealed record FoodTraderAccount(string TraderId, int PortionsSold, long TakingsPennies, long IngredientCostPennies, long PitchFeePennies)
{
    public long ProfitPennies => TakingsPennies - IngredientCostPennies - PitchFeePennies;
    public bool MadeALoss => ProfitPennies < 0;
}

public static class FoodTraders
{
    // Tier 1: chips, quick and plain; or pizza, which pays more to pitch but is slow to serve. A queue that long has a
    // way of costing more than the extra pitch fee, though a well-run pizza van is a little more satisfying.
    public static readonly FoodTrader[] All =
    [
        new("trader.chip-off-the-old-block", "Chip Off The Old Block", "Chips", ImmersionProduct.Chips, "chip_block", "Quick, cheap and cheerful.", 4_000, 1_000, 300, 40, 0),
        new("trader.pizza-the-action", "Pizza the Action", "Pizza", ImmersionProduct.Pizza, "pizza", "Wood-fired pizza. Pays more to pitch, but slower to serve.", 6_000, 2_500, 450, 60, 175,
            EnjoymentPercent: 140, FillingPercent: 115),
    ];

    public static FoodTrader Default => All[0];
    public static FoodTrader? Find(string? id) => All.FirstOrDefault(t => t.Id == id);
    /// <summary>The trader whose food this is.</summary>
    public static FoodTrader Selling(ImmersionProduct product) => All.First(t => t.Product == product);
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
        immersion?.Purchases.Where(purchase => !purchase.Product.IsFood()).Sum(purchase => (long)purchase.PricePennies) ?? 0;

    /// <summary>The food van's trader's account for this festival, once they've pitched; null before opening or with no van.</summary>
    public FoodTraderAccount? FoodTraderAccount =>
        _immersion is null || _preparation is not { Plan: { Committed: true } } || !FoodVanPitched ? null : TraderAccount(FoodTrader, _immersion, ReceivedPitchFee(_preparation));

    // Derived from the day's sales, so there's nothing extra to save and it can't drift from them.
    private static FoodTraderAccount TraderAccount(FoodTrader trader, ImmersionSnapshot immersion, long pitchFee)
    {
        var sold = immersion.Purchases.Where(purchase => purchase.Product == trader.Product).ToArray();
        return new(trader.Id, sold.Length, sold.Sum(purchase => (long)purchase.PricePennies), (long)sold.Length * trader.PortionCostPennies, pitchFee);
    }

    /// <summary>The pitch fee received for this attempt, once it opened.</summary>
    private static long ReceivedPitchFee(PreparationSnapshot p) =>
        p.Plan is { Committed: true } ? p.SetupPayments?.LastOrDefault(setup => setup.Attempt == p.Attempt)?.PitchFeePennies ?? 0 : 0;

    private CommandResult? ValidateChooseFoodTrader(EntityId? target, ChooseFoodTraderCommand command) =>
        target is not null || _preparation is not { Status: PreparationStatus.Preparing, Plan: { Committed: false } }
            ? CommandResult.Rejected(CommandReasonCode.WrongPhase, "Choose the food trader while preparing.")
            : FoodTraders.Find(command.TraderId) is null ? CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Unknown food trader.") : null;

    private void ApplyChooseFoodTrader(ChooseFoodTraderCommand command) =>
        _preparation = _preparation! with { Plan = _preparation.Plan! with { TraderId = command.TraderId } };
}

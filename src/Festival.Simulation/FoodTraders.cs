namespace Festival.Simulation;

/// <summary>
/// A food trader: they bring their own van and stock, keep what they take, and pay the festival a pitch fee to trade.
/// Cuisines differ by how fast they serve, how much they earn and how satisfying they are (chips: quick, cheap, not
/// very filling). Each van has its own trader, and two vans never sell the same food.
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
/// <param name="FromTier">The first tier that can book them.</param>
public sealed record FoodTrader(string Id, string Name, string Menu, ImmersionProduct Product, string Art, string Blurb, int PitchFeePennies, int ServicePermille,
    int PricePennies, int PortionCostPennies, int AppealPennies, int EnjoymentPercent = 100, int FillingPercent = 100, int FromTier = 1)
{
    /// <summary>Slower to serve than the standard chip van: the one thing the picker warns about.</summary>
    public bool SlowService => ServicePermille > 1_000;
}

/// <param name="VanId">Which van: the first ("food") unless said otherwise.</param>
public sealed record ChooseFoodTraderCommand(string TraderId, string VanId = Stalls.FirstVan) : SessionCommand;

/// <summary>The trader chosen for a van after the first; the first van's is the plan's own TraderId.</summary>
public sealed record VanTrader(string VanId, string TraderId);

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
    // Tier 2 adds curry: slow from the pot and the dearest, but the most filling for the money.
    public static readonly FoodTrader[] All =
    [
        new("trader.chip-off-the-old-block", "Chip Off The Old Block", "Chips", ImmersionProduct.Chips, "chip_block", "Quick, cheap and cheerful.", 4_000, 1_000, 300, 40, 0),
        new("trader.pizza-the-action", "Pizza the Action", "Pizza", ImmersionProduct.Pizza, "pizza", "Wood-fired pizza. Pays more to pitch, but slower to serve.", 6_000, 2_500, 450, 60, 175,
            EnjoymentPercent: 140, FillingPercent: 115),
        new("trader.korma-chameleon", "Korma Chameleon", "Curry", ImmersionProduct.Curry, "korma",
            "Curry and rice. Slow from the pot, but it fills them up for the money. Hold the line: it's worth the wait.",
            CurryPitchFeePennies, CurryServicePermille, CurryPricePennies, CurryPortionCostPennies, CurryAppealPennies,
            EnjoymentPercent: CurryEnjoymentPercent, FillingPercent: CurryFillingPercent, FromTier: 2),
    ];

    // Curry's numbers, named so the probe and DECISIONS.md can point at them.
    internal const int CurryPitchFeePennies = 3_000, CurryServicePermille = 1_750, CurryPricePennies = 500, CurryPortionCostPennies = 90,
        CurryAppealPennies = 450, CurryEnjoymentPercent = 130, CurryFillingPercent = 175;

    public static FoodTrader Default => All[0];
    public static FoodTrader? Find(string? id) => All.FirstOrDefault(t => t.Id == id);
    /// <summary>The traders a festival at this tier can book, in the catalogue's order.</summary>
    public static IEnumerable<FoodTrader> AtTier(int tier) => All.Where(t => t.FromTier <= tier);
    /// <summary>The trader whose food this is.</summary>
    public static FoodTrader Selling(ImmersionProduct product) => All.First(t => t.Product == product);
}

public sealed partial class GameSession
{
    /// <summary>The trader at the first food van: the one chosen this festival, the standard one until then.</summary>
    public FoodTrader FoodTrader => TraderAt(Stalls.FirstVan);

    /// <summary>The trader at a van, placed or about to be.</summary>
    public FoodTrader TraderAt(string vanId)
    {
        // Asked for every stop a guest weighs: kept until the plan or the layout next changes (the snapshot is replaced then).
        if (!ReferenceEquals(_traderCache.Prep, _preparation)) _traderCache = (_preparation, new(StringComparer.Ordinal));
        if (!_traderCache.Traders.TryGetValue(vanId, out var trader)) _traderCache.Traders[vanId] = trader = TraderAt(_preparation, vanId);
        return trader;
    }
    private (PreparationSnapshot? Prep, Dictionary<string, FoodTrader> Traders) _traderCache = (null, new(StringComparer.Ordinal));

    /// <summary>
    /// Each van's trader, in van order: its own choice if the tier allows it and no earlier van has that food, else the
    /// first trader the tier allows that no earlier van has. So two vans never sell the same food, whatever's saved.
    /// </summary>
    private static FoodTrader TraderAt(PreparationSnapshot? p, string vanId)
    {
        var tier = p?.Tier ?? 1;
        var vans = PitchedVans(p).Append(vanId).Distinct().OrderBy(Stalls.Number).ToArray();
        // A van left to the default never takes a food another van's player picked, so a pick is never pushed aside.
        var picked = ChosenTraders(p?.Plan).Select(item => item.Trader).ToArray();
        var taken = new List<FoodTrader>();
        foreach (var van in vans)
        {
            var wanted = FoodTraders.Find(ChosenTraderId(p?.Plan, van));
            var trader = wanted is not null && wanted.FromTier <= tier && !taken.Contains(wanted) ? wanted
                : FoodTraders.AtTier(tier).Where(t => !taken.Contains(t)).OrderBy(t => picked.Contains(t)).FirstOrDefault() ?? FoodTraders.Default;
            if (van == vanId) return trader;
            taken.Add(trader);
        }
        return FoodTraders.Default;
    }

    /// <summary>Every van's pick in the plan, placed or not, in van order.</summary>
    private static IEnumerable<(string VanId, FoodTrader Trader)> ChosenTraders(PreparationPlan? plan) =>
        new[] { Stalls.FirstVan }.Concat((plan?.VanTraders ?? []).Select(item => item.VanId))
            .Select(van => (VanId: van, Trader: FoodTraders.Find(ChosenTraderId(plan, van))))
            .Where(item => item.Trader is not null).Select(item => (item.VanId, item.Trader!));

    /// <summary>What the plan says for a van: the plan's TraderId for the first, its van list for the rest.</summary>
    private static string? ChosenTraderId(PreparationPlan? plan, string vanId) => vanId == Stalls.FirstVan ? plan?.TraderId :
        plan?.VanTraders?.FirstOrDefault(item => item.VanId == vanId)?.TraderId;

    /// <summary>The vans placed, in van order.</summary>
    private static string[] PitchedVans(PreparationSnapshot? p) => (p?.BuildPlacements ?? []).Where(item => item.Kind == BuildServiceKind.FoodVan)
        .Select(item => item.Id).OrderBy(Stalls.Number).ToArray();
    public IReadOnlyList<string> PlacedVans => PitchedVans(_preparation);

    /// <summary>Whether a food van is pitched at all: no van, no trader, no pitch fee.</summary>
    private bool FoodVanPitched => PitchedVans(_preparation).Length > 0;

    /// <summary>The pitch fees the food traders pay at opening, one for each van.</summary>
    public long PlannedPitchFeePennies => PlannedPitchFee(_preparation);
    private static long PlannedPitchFee(PreparationSnapshot? p) => PitchedVans(p).Sum(van => (long)TraderAt(p, van).PitchFeePennies);

    /// <summary>Who's paying to pitch, in van order: "Chip Off The Old Block and Korma Chameleon".</summary>
    public string PitchingTraders => string.Join(" and ", PitchedVans(_preparation).Select(van => TraderAt(van).Name));

    /// <summary>What the festival itself took at its stalls: the bars' sales, not the food traders'.</summary>
    private static long FestivalTakings(ImmersionSnapshot? immersion) =>
        immersion?.Purchases.Where(purchase => !purchase.Product.IsFood()).Sum(purchase => (long)purchase.PricePennies) ?? 0;

    /// <summary>The first food van's trader's account for this festival, once they've pitched; null before opening or with no van.</summary>
    public FoodTraderAccount? FoodTraderAccount => TraderAccountAt(Stalls.FirstVan);

    /// <summary>A van's trader's account for this festival, once they've pitched; null before opening or with no such van.</summary>
    public FoodTraderAccount? TraderAccountAt(string vanId) =>
        _immersion is null || _preparation is not { Plan: { Committed: true } } p || !PitchedVans(p).Contains(vanId) ? null
            : TraderAccount(vanId, TraderAt(vanId), _immersion);

    /// <summary>Every pitched van's trader's account, in van order.</summary>
    public IReadOnlyList<(string VanId, FoodTraderAccount Account)> FoodTraderAccounts =>
        PitchedVans(_preparation).Select(van => (VanId: van, Account: TraderAccountAt(van))).Where(item => item.Account is not null)
            .Select(item => (item.VanId, item.Account!)).ToArray();

    // Derived from the day's sales, so there's nothing extra to save and it can't drift from them.
    private static FoodTraderAccount TraderAccount(string vanId, FoodTrader trader, ImmersionSnapshot immersion)
    {
        var sold = immersion.Purchases.Where(purchase => purchase.Product.IsFood() && Stalls.Of(purchase) == vanId).ToArray();
        return new(trader.Id, sold.Length, sold.Sum(purchase => (long)purchase.PricePennies), (long)sold.Length * trader.PortionCostPennies, trader.PitchFeePennies);
    }

    /// <summary>The pitch fee received for this attempt, once it opened.</summary>
    private static long ReceivedPitchFee(PreparationSnapshot p) =>
        p.Plan is { Committed: true } ? p.SetupPayments?.LastOrDefault(setup => setup.Attempt == p.Attempt)?.PitchFeePennies ?? 0 : 0;

    /// <summary>Why a van can't have this trader, or null if it can.</summary>
    public string? TraderUnavailable(string vanId, FoodTrader trader) =>
        trader.FromTier > (_preparation?.Tier ?? 1) ? $"{trader.Name} trade from Tier {trader.FromTier}."
        // Two vans selling the same food would just split one queue: one cuisine per van, for now. A pick for a van not yet
        // placed counts too, so placing it later never switches anyone's food.
        : PitchedVans(_preparation).Where(van => van != vanId).Select(van => (VanId: van, Trader: TraderAt(van)))
            .Concat(ChosenTraders(_preparation?.Plan).Where(item => item.VanId != vanId))
            .FirstOrDefault(item => item.Trader.Id == trader.Id) is { VanId: { } other }
            ? $"{trader.Name} are already at van {Stalls.Number(other)}: pick a different food, or change that van first."
        : null;

    private CommandResult? ValidateChooseFoodTrader(EntityId? target, ChooseFoodTraderCommand command)
    {
        if (target is not null || _preparation is not { Status: PreparationStatus.Preparing, Plan: { Committed: false } } p)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Choose the food trader while preparing.");
        if (FoodTraders.Find(command.TraderId) is not { } trader) return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Unknown food trader.");
        if (!Stalls.IsVan(command.VanId) || Stalls.Number(command.VanId) > BuildServiceLimit(BuildServiceKind.FoodVan, p.Tier))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Unknown food van.");
        return TraderUnavailable(command.VanId, trader) is { } issue ? CommandResult.Rejected(CommandReasonCode.InvalidParameter, issue) : null;
    }

    private void ApplyChooseFoodTrader(ChooseFoodTraderCommand command)
    {
        var plan = _preparation!.Plan!;
        _preparation = _preparation with { Plan = command.VanId == Stalls.FirstVan ? plan with { TraderId = command.TraderId } : plan with
        {
            VanTraders = (plan.VanTraders ?? []).Where(item => item.VanId != command.VanId).Append(new(command.VanId, command.TraderId))
                .OrderBy(item => Stalls.Number(item.VanId)).ToArray()
        } };
    }
}

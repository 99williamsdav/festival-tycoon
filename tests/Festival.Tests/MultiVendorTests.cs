using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>Two bars and two food vans at Tier 2, and Korma Chameleon's curry.</summary>
[TestClass]
public sealed class MultiVendorTests
{
    // Back to back with the first bar, serving the Pond Stage's crowd; and the curry van north of the main crowd, by the gate, facing it.
    internal static readonly GridCell SecondBarCell = new(152, 146), CurryVanCell = new(170, 128);
    internal const int SecondBarTurns = 1, CurryVanTurns = 0;
    internal static FoodTrader Curry => FoodTraders.Selling(ImmersionProduct.Curry);

    /// <summary>
    /// Tier 2's default layout and plan, plus a second bar, Korma Chameleon at a second van, the bigger generator they
    /// need and twice the starter stock, as two bars would sell.
    /// </summary>
    internal static GameSession TierTwoWithFourStalls(ulong seed = 20260922, bool generator = true, bool secondBar = true, bool curryVan = true, bool bins = false)
    {
        var s = NextFestivalTests.Ready(GameSession.CreateDevelopmentFestival(seed, 2));
        BuildSession.Accept(s, new SetPreparationStockCommand(80, 64));
        if (secondBar) BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Bar, SecondBarCell, SecondBarTurns));
        if (curryVan)
        {
            BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.FoodVan, CurryVanCell, CurryVanTurns));
            BuildSession.Accept(s, new ChooseFoodTraderCommand(Curry.Id, "food.2"));
        }
        if (generator) BuildSession.Accept(s, new AcceptPreparationOfferCommand(PowerRules.GeneratorOffer));
        // A bin by each new stall, for the litter they bring.
        if (bins) foreach (var bin in new GridCell[] { new(163, 136), new(178, 134) }) BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, bin));
        return s;
    }

    private static GameSession? _day;
    private static SessionPersistenceSnapshot? _midDay;

    /// <summary>One four-stall Tier 2 day, played once for the tests that only read it: kept mid-day and at the close.</summary>
    private static (GameSession MidDay, GameSession Close) Day()
    {
        lock (typeof(MultiVendorTests))
        {
            if (_day is null)
            {
                var s = TierTwoWithFourStalls();
                BuildSession.Accept(s, new StartPreparedEditionCommand());
                s.AdvanceWithoutSnapshot(16_000);
                _midDay = s.CapturePersistenceSnapshot();
                for (var i = 0; i < 80 && s.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing; i++) s.AdvanceWithoutSnapshot(1_000);
                _day = s;
            }
            // Each caller gets its own copy of the middle of the day to play on.
            return (GameSession.Restore(_midDay!).Session!, _day);
        }
    }

    [TestMethod]
    public void TierTwoAllowsOneMoreBarAndVanThanTierOne()
    {
        Assert.AreEqual(1, GameSession.BuildServiceLimit(BuildServiceKind.FoodVan, 1));
        Assert.AreEqual(1, GameSession.BuildServiceLimit(BuildServiceKind.Bar, 1));
        Assert.AreEqual(2, GameSession.BuildServiceLimit(BuildServiceKind.FoodVan, 2));
        Assert.AreEqual(2, GameSession.BuildServiceLimit(BuildServiceKind.Bar, 2));

        var one = BuildSession.Planned();
        Assert.IsFalse(BuildSession.Send(one, new PlaceBuildServiceCommand(BuildServiceKind.Bar, SecondBarCell, SecondBarTurns)).IsAccepted, "One bar at Tier 1.");
        Assert.IsFalse(BuildSession.Send(one, new PlaceBuildServiceCommand(BuildServiceKind.FoodVan, CurryVanCell, CurryVanTurns)).IsAccepted, "One van at Tier 1.");

        var two = TierTwoWithFourStalls(generator: false);
        CollectionAssert.AreEqual(new[] { "drinks", "drinks.2", "food", "food.2" },
            two.CaptureBuildPlacements().Where(item => Stalls.IsStall(item.Kind)).Select(item => item.Id).ToArray());
        Assert.IsFalse(BuildSession.Send(two, new PlaceBuildServiceCommand(BuildServiceKind.Bar, new(176, 122))).IsAccepted, "Two bars is the most.");
        // Take the first away and the next one placed fills its id again.
        BuildSession.Accept(two, new RemoveBuildServiceCommand("drinks"));
        BuildSession.Accept(two, new PlaceBuildServiceCommand(BuildServiceKind.Bar, new(140, 148), 3));
        Assert.IsTrue(two.CaptureBuildPlacements().Any(item => item.Id == "drinks"));
    }

    [TestMethod]
    public void StallIdsKeepTheFirstOfEachKindAsBefore()
    {
        Assert.AreEqual("food", Stalls.Id(BuildServiceKind.FoodVan, 1));
        Assert.AreEqual("drinks.2", Stalls.Id(BuildServiceKind.Bar, 2));
        Assert.AreEqual(2, Stalls.Number("food.2"));
        Assert.AreEqual(0, Stalls.Number("food.1"), "The first is plain \"food\".");
        Assert.AreEqual(0, Stalls.Number("food.02"));
        Assert.IsTrue(Stalls.IsVan("food.2") && Stalls.IsBar("drinks") && !Stalls.IsVan("drinks.2"));
        Assert.AreEqual("Bar 2", Stalls.Label("drinks.2"));
    }

    [TestMethod]
    public void TwoVansSellDifferentFoodAndCurryWaitsForTierTwo()
    {
        var one = BuildSession.Planned();
        Assert.IsFalse(BuildSession.Send(one, new ChooseFoodTraderCommand(Curry.Id)).IsAccepted, "Korma Chameleon trade from Tier 2.");

        var s = TierTwoWithFourStalls(generator: false);
        Assert.AreEqual(FoodTraders.Default, s.TraderAt("food"));
        Assert.AreEqual(Curry, s.TraderAt("food.2"));
        var refused = BuildSession.Send(s, new ChooseFoodTraderCommand(FoodTraders.Default.Id, "food.2"));
        Assert.IsFalse(refused.IsAccepted, "Chips are already at the first van.");
        Assert.IsFalse(BuildSession.Send(s, new ChooseFoodTraderCommand(Curry.Id)).IsAccepted, "Curry is already at the second.");
        BuildSession.Accept(s, new ChooseFoodTraderCommand("trader.pizza-the-action"));
        Assert.AreEqual(ImmersionProduct.Pizza, s.TraderAt("food").Product);
        Assert.AreEqual(FoodTraders.Find("trader.pizza-the-action")!.PitchFeePennies + Curry.PitchFeePennies, s.PlannedPitchFeePennies, "Each van's trader pays to pitch.");

        // A van placed fresh takes a food no other van has.
        var fresh = NextFestivalTests.Ready(GameSession.CreateDevelopmentFestival(20260922, 2));
        BuildSession.Accept(fresh, new PlaceBuildServiceCommand(BuildServiceKind.FoodVan, CurryVanCell, CurryVanTurns));
        Assert.AreNotEqual(fresh.TraderAt("food").Product, fresh.TraderAt("food.2").Product);
    }

    [TestMethod]
    public void CurryIsSlowToServeVeryFillingAndTheBestValueForMoney()
    {
        var chips = FoodTraders.Default; var pizza = FoodTraders.Selling(ImmersionProduct.Pizza);
        Assert.AreEqual("Korma Chameleon", Curry.Name);
        Assert.AreEqual("korma", Curry.Art);
        Assert.AreEqual(2, Curry.FromTier);
        Assert.IsTrue(GameSession.ImmersionServiceDuration(ImmersionProduct.Curry) > GameSession.ImmersionServiceDuration(ImmersionProduct.Chips));
        Assert.IsTrue(Curry.SlowService);
        Assert.IsTrue(Curry.FillingPercent > pizza.FillingPercent && pizza.FillingPercent > chips.FillingPercent, "The most filling.");
        foreach (var other in new[] { chips, pizza })
            Assert.IsTrue(Curry.FillingPercent * other.PricePennies > other.FillingPercent * Curry.PricePennies, $"More filling per pound than {other.Menu}.");
        Assert.AreEqual("Curry", GameSession.ProductName(ImmersionProduct.Curry));
        Assert.AreEqual(0, GameSession.ImmersionCost(ImmersionProduct.Curry), "The trader's stock, not the festival's.");
        // Worth its price to everyone; but side by side with chips, counting its slower service at the music's base worth
        // (2,000 a second, as between sets), the freer spenders take curry and the thriftiest still take chips.
        Assert.IsTrue(GameSession.FoodWorth(ImmersionProduct.Curry, 100) > GameSession.FoodWorth(ImmersionProduct.Chips, 100));
        var wait = (GameSession.ImmersionServiceDuration(ImmersionProduct.Curry) - GameSession.ImmersionServiceDuration(ImmersionProduct.Chips)) * 2_000L / 80;
        Assert.IsTrue(GameSession.FoodWorth(ImmersionProduct.Curry, 0) - wait > GameSession.FoodWorth(ImmersionProduct.Chips, 0));
        Assert.IsTrue(GameSession.FoodWorth(ImmersionProduct.Curry, 100) - wait < GameSession.FoodWorth(ImmersionProduct.Chips, 100));
    }

    [TestMethod]
    public void AGuestWeighsBothVansAndBothBarsByWalkQueueAndValue()
    {
        var s = Day().MidDay;
        var wallets = s.CaptureSnapshot().Wallets;
        var guest = s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed &&
            s.CaptureImmersion()!.People.Single(c => c.AgentId == p.AgentId) is { VendorId: null, Held: null } &&
            wallets.Single(w => w.OwnerId.Value == p.AgentId).CashPennies >= 1_000 && s.CaptureCarriedWaste(p.AgentId) is null && !s.Teetotal(p.AgentId) &&
            s.CaptureMedical()!.Needs.Single(n => n.AgentId == p.AgentId).Intent == MedicalIntent.WatchShow).AgentId;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(GameSession).GetMethod("MutatePerson", flags)!.Invoke(s, [guest, (Action<Person>)(p => { p.Hunger = 7_000; p.Thirst = 5_000; })]);
        var options = (List<ActivityOption>)typeof(GameSession).GetMethod("ActivityOptions", flags)!.Invoke(s, [guest, ActivityKind.Watch])!;
        var food = options.Where(o => o.Kind == ActivityKind.Food && o.Then is null).ToArray();
        CollectionAssert.AreEquivalent(new[] { "food", "food.2" }, food.Select(o => o.FacilityId).ToArray(), "Both vans are on offer.");
        Assert.AreNotEqual(food[0].Cost, food[1].Cost, "Each at its own price.");
        CollectionAssert.AreEquivalent(new[] { "drinks", "drinks.2" },
            options.Where(o => o.Kind is ActivityKind.SoftDrink or ActivityKind.Beer && o.Then is null).Select(o => o.FacilityId).Distinct().ToArray(), "Both bars too.");
        // The further van takes longer to reach.
        var nearer = food.MinBy(o => o.CompleteTicks)!;
        var vendor = s.CaptureVendors().Single(v => v.Id == nearer.FacilityId);
        var other = s.CaptureVendors().Single(v => v.Id != vendor.Id && Stalls.IsVan(v.Id));
        var at = s.CaptureSnapshot().NavigationAgents.Single(a => a.Id.Value == guest);
        var here = TraversalGrid.WorldToCell(at.XMillimetres, at.ZMillimetres);
        int Distance(GridCell cell) => Math.Max(Math.Abs(cell.X - here.X), Math.Abs(cell.Z - here.Z));
        Assert.IsTrue(Distance(vendor.Cell) <= Distance(other.Cell) + 4 || vendor.Queue.Length < other.Queue.Length, "The quicker one is nearer, or has the shorter queue.");
    }

    [TestMethod]
    public void TheCrowdSplitsBetweenTheVansAndBothBarsSell()
    {
        var s = Day().Close;
        var purchases = s.CaptureImmersion()!.Purchases;
        var chips = purchases.Count(p => p.Product == ImmersionProduct.Chips);
        var curry = purchases.Count(p => p.Product == ImmersionProduct.Curry);
        Assert.IsTrue(chips >= 5 && curry >= 5, $"Both vans sell: {chips} chips, {curry} curries.");
        Assert.IsTrue(purchases.Where(p => p.Product == ImmersionProduct.Curry).All(p => p.VendorId == "food.2"), "Curry is sold at the curry van.");
        Assert.IsTrue(purchases.Where(p => p.Product == ImmersionProduct.Chips).All(p => p.VendorId is null), "Chips at the first van, which a sale doesn't name.");
        var first = purchases.Count(p => !p.Product.IsFood() && Stalls.Of(p) == "drinks");
        var second = purchases.Count(p => !p.Product.IsFood() && Stalls.Of(p) == "drinks.2");
        Assert.IsTrue(first > 0 && second > 0, $"Both bars sell: {first} and {second}.");
    }

    [TestMethod]
    public void EachVanKeepsItsOwnAccountAndTheBarsReconcile()
    {
        var s = Day().Close;
        var purchases = s.CaptureImmersion()!.Purchases;
        Assert.AreEqual(2, s.FoodTraderAccounts.Count);
        foreach (var (van, account) in s.FoodTraderAccounts)
        {
            var trader = s.TraderAt(van);
            var sold = purchases.Where(p => p.Product.IsFood() && Stalls.Of(p) == van).ToArray();
            Assert.AreEqual(trader.Id, account.TraderId);
            Assert.AreEqual(sold.Length, account.PortionsSold);
            Assert.IsTrue(sold.All(p => p.Product == trader.Product));
            Assert.AreEqual(sold.Sum(p => (long)p.PricePennies), account.TakingsPennies);
            Assert.AreEqual((long)sold.Length * trader.PortionCostPennies, account.IngredientCostPennies);
            Assert.AreEqual(trader.PitchFeePennies, account.PitchFeePennies);
        }
        Assert.AreEqual(s.FoodTraderAccount, s.TraderAccountAt("food"));

        var accounts = s.CompletedFestivalAccounts!;
        Assert.IsTrue(accounts.Reconciles, "The books balance with two bars and two pitch fees.");
        Assert.AreEqual(FoodTraders.Default.PitchFeePennies + Curry.PitchFeePennies, accounts.PitchFeePennies);
        CollectionAssert.AreEqual(new[] { FoodTraders.Default.Name, Curry.Name }, accounts.PitchFees.Select(pitch => pitch.Trader).ToArray());
        Assert.IsTrue(accounts.Sales.Any(line => line.Stall == "drinks.2"), "A line for the second bar's sales.");
        Assert.AreEqual(purchases.Where(p => !p.Product.IsFood()).Sum(p => (long)p.PricePennies), accounts.Sales.Sum(line => line.AmountPennies));
        Assert.IsTrue(s.CaptureFestivalCashFeedbackEvents().Any(item => item.AnchorKey == "vendor.drinks.2"), "The second bar's takings pop up at the second bar.");
    }

    [TestMethod]
    public void ADayWithTwoVansAndTwoBarsRestoresMidDayAndPlaysOnTheSame()
    {
        var s = Day().MidDay;
        Assert.IsTrue(s.CaptureVendors().Any(v => v.Id == "food.2") && s.CaptureVendors().Any(v => v.Id == "drinks.2"));
        Assert.IsTrue(s.CaptureImmersion()!.Purchases.Any(p => p.VendorId is not null), "Something's been bought at a second stall.");
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        var copy = restored.Session!;
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, copy.CaptureSnapshot().AuthoritativeHash);
        s.AdvanceWithoutSnapshot(2_400); copy.AdvanceWithoutSnapshot(2_400);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, copy.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(s.TraderAccountAt("food.2"), copy.TraderAccountAt("food.2"));

        // A sale claiming a stall that isn't there is refused.
        var snapshot = copy.CapturePersistenceSnapshot();
        var immersion = snapshot.Immersion!;
        var bent = immersion with { Purchases = immersion.Purchases.Select(p => p.VendorId == "food.2" ? p with { VendorId = "food.3" } : p).ToArray() };
        Assert.IsFalse(GameSession.Restore(snapshot with { Immersion = bent }).IsSuccess);
    }

    [TestMethod]
    public void ASaleAtTheFirstBarIsRefusedOnRestoreWhenOnlyTheSecondBarStands()
    {
        // Tier 2 with the first bar taken away: only drinks.2 stands, so every drink is sold there and names it.
        var s = TierTwoWithFourStalls();
        BuildSession.Accept(s, new RemoveBuildServiceCommand("drinks"));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        for (var i = 0; i < 40 && !s.CaptureImmersion()!.Purchases.Any(p => !p.Product.IsFood()); i++) s.AdvanceWithoutSnapshot(400);
        var snapshot = s.CapturePersistenceSnapshot();
        Assert.IsTrue(snapshot.Immersion!.Purchases.Where(p => !p.Product.IsFood()).All(p => p.VendorId == "drinks.2"));
        Assert.IsTrue(GameSession.Restore(snapshot).IsSuccess);
        // Strip the stall from a drink sale: it would claim the first bar, which isn't there.
        var stripped = snapshot.Immersion with { Purchases = snapshot.Immersion.Purchases.Select(p => p.Product.IsFood() ? p : p with { VendorId = null }).ToArray() };
        Assert.IsFalse(GameSession.Restore(snapshot with { Immersion = stripped }).IsSuccess);
    }

    [TestMethod]
    public void APickForAVanNotYetPlacedIsNeverSwitchedLater()
    {
        var s = NextFestivalTests.Ready(GameSession.CreateDevelopmentFestival(20260922, 2));
        // Curry for van 2 before it's placed; van 1 can't then take curry, so placing van 2 keeps it.
        BuildSession.Accept(s, new ChooseFoodTraderCommand(Curry.Id, "food.2"));
        var refused = BuildSession.Send(s, new ChooseFoodTraderCommand(Curry.Id));
        Assert.IsFalse(refused.IsAccepted);
        StringAssert.Contains(refused.Message, "van 2");
        BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.FoodVan, CurryVanCell, CurryVanTurns));
        Assert.AreEqual(Curry, s.TraderAt("food.2"));
        Assert.AreEqual(FoodTraders.Default, s.TraderAt("food"));

        // Placed the other way round: van 2 first, then van 1 placed again, and still nobody's pick moves.
        BuildSession.Accept(s, new RemoveBuildServiceCommand("food"));
        BuildSession.Accept(s, new ChooseFoodTraderCommand("trader.pizza-the-action"));
        BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.FoodVan, new(140, 162), 3));
        Assert.AreEqual(ImmersionProduct.Pizza, s.TraderAt("food").Product);
        Assert.AreEqual(Curry, s.TraderAt("food.2"));
    }

    [TestMethod]
    public void AOneVanOneBarSaveWritesNoStallOrVanTraderFields()
    {
        var s = BuildSession.Started();
        s.AdvanceWithoutSnapshot(12_000);
        var snapshot = s.CapturePersistenceSnapshot();
        Assert.IsTrue(snapshot.Immersion!.Purchases.Length > 0);
        StringAssert.DoesNotMatch(System.Text.Json.JsonSerializer.Serialize(snapshot.Immersion.Purchases), new System.Text.RegularExpressions.Regex("VendorId"),
            "Sales at the only van and bar don't name it.");
        StringAssert.DoesNotMatch(System.Text.Json.JsonSerializer.Serialize(snapshot), new System.Text.RegularExpressions.Regex("VanTraders"));
    }
}

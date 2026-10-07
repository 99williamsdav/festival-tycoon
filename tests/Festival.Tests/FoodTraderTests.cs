using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class FoodTraderTests
{
    [TestMethod]
    public void EachTraderSellsTheirOwnFoodAndItIsThatFoodPeopleHoldAndDrop()
    {
        foreach (var trader in FoodTraders.All)
        {
            Assert.IsTrue(trader.Product.IsFood());
            Assert.AreSame(trader, FoodTraders.Selling(trader.Product), "One trader per food.");
        }
        var pizza = FoodTraders.Find("trader.pizza-the-action")!;
        Assert.AreEqual(ImmersionProduct.Pizza, pizza.Product);
        Assert.AreEqual(ImmersionProduct.Chips, FoodTraders.Default.Product);

        var s = BuildSession.Planned();
        BuildSession.Accept(s, new ChooseFoodTraderCommand(pizza.Id));
        foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        while (s.PreparedStatus == PreparationStatus.Running && s.CurrentTick < 24_000 &&
               s.CaptureLitter()?.Pieces.Any(p => p.Product.IsFood()) != true)
            s.AdvanceWithoutSnapshot(80);

        var food = s.CaptureImmersion()!.Purchases.Where(p => p.Product.IsFood()).ToArray();
        Assert.IsTrue(food.Length > 0, "Someone bought food.");
        Assert.IsTrue(food.All(p => p.Product == ImmersionProduct.Pizza), "A pizza van sells pizza, not chips.");
        Assert.IsTrue(s.CaptureLitter()!.Pieces.Where(p => p.Product.IsFood()).All(p => p.Product == ImmersionProduct.Pizza),
            "And what's left on the ground is a pizza plate.");
        Assert.IsTrue(food.All(p => p.Entries.Length == 2), "The trader keeps their takings: only the buyer's entries.");
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
    }

    [TestMethod]
    public void HowLongFoodTakesAndHowMuchItPleasesFollowWhatItIsNotTheVanPitchedToday()
    {
        Assert.AreEqual(320 * FoodTraders.Selling(ImmersionProduct.Pizza).ServicePermille / 1000, GameSession.ImmersionServiceDuration(ImmersionProduct.Pizza));
        Assert.AreEqual(320, GameSession.ImmersionServiceDuration(ImmersionProduct.Chips));
        Assert.AreEqual(GameSession.ImmersionConsumeTicks(ImmersionProduct.Chips), GameSession.ImmersionConsumeTicks(ImmersionProduct.Pizza));
        Assert.AreEqual(0, GameSession.ImmersionCost(ImmersionProduct.Pizza), "The trader's stock, not the festival's.");
        Assert.AreEqual("Pizza", GameSession.ProductName(ImmersionProduct.Pizza));
    }

    [TestMethod]
    public void SomeoneQueuingAtAPizzaVanWeighsStayingAtPizzasPaceNotAPhantomChipsQueue()
    {
        var s = BuildSession.Planned();
        BuildSession.Accept(s, new ChooseFoodTraderCommand("trader.pizza-the-action"));
        foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        s.AdvanceWithoutSnapshot(2_000);
        var guest = s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed).AgentId;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(GameSession).GetMethod("MutatePerson", flags)!.Invoke(s, [guest, (Action<Person>)(p => { p.VendorId = "food"; p.Order = ImmersionProduct.Pizza; })]);
        var options = (List<ActivityOption>)typeof(GameSession).GetMethod("ActivityOptions", flags)!.Invoke(s, [guest, ActivityKind.Food])!;
        var food = options.Where(o => o.Kind == ActivityKind.Food && o.Then is null).ToArray();
        Assert.AreEqual(1, food.Length, "One way to stay in the queue: the pizza one.");
    }

    [TestMethod]
    public void TheVanKeepsItsOwnAccountOfSalesIngredientsAndPitchFeeAndItSurvivesASave()
    {
        var s = BuildSession.Planned();
        Assert.IsNull(s.FoodTraderAccount, "Nothing to account for before opening.");
        foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        var trader = s.FoodTrader;
        var opening = s.FoodTraderAccount!;
        Assert.AreEqual(0, opening.PortionsSold);
        Assert.AreEqual(trader.PitchFeePennies, opening.PitchFeePennies);
        Assert.AreEqual(0, opening.IngredientCostPennies, "Nothing wasted: they only pay for what they sell.");
        Assert.IsTrue(opening.MadeALoss, "Out of pocket by the pitch fee until they sell something.");

        s.AdvanceWithoutSnapshot(20_000);
        var food = s.CaptureImmersion()!.Purchases.Where(p => p.Product == trader.Product).ToArray();
        var account = s.FoodTraderAccount!;
        Assert.IsTrue(food.Length > 0);
        Assert.AreEqual(food.Length, account.PortionsSold);
        Assert.AreEqual(food.Sum(p => (long)p.PricePennies), account.TakingsPennies);
        Assert.AreEqual((long)food.Length * trader.PortionCostPennies, account.IngredientCostPennies);
        Assert.AreEqual(account.TakingsPennies - account.IngredientCostPennies - account.PitchFeePennies, account.ProfitPennies);

        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(account, restored.Session!.FoodTraderAccount);
    }

    [TestMethod]
    public void SideBySideBetterOffGuestsLeanToPizzaThriftyOnesToChipsAndTheCrowdSplits()
    {
        int pizza = 0, chips = 0;
        for (var thrift = 0; thrift <= 100; thrift++)
        {
            var lean = GameSession.FoodWorth(ImmersionProduct.Pizza, thrift).CompareTo(GameSession.FoodWorth(ImmersionProduct.Chips, thrift));
            if (lean > 0) pizza++; else if (lean < 0) chips++;
        }
        Assert.IsTrue(GameSession.FoodWorth(ImmersionProduct.Pizza, 0) > GameSession.FoodWorth(ImmersionProduct.Chips, 0), "The well-off pay for good food.");
        Assert.IsTrue(GameSession.FoodWorth(ImmersionProduct.Pizza, 100) < GameSession.FoodWorth(ImmersionProduct.Chips, 100), "The thrifty buy cheap.");
        Assert.IsTrue(Math.Abs(pizza - chips) <= 30, $"A middling crowd splits: {pizza} lean to pizza, {chips} to chips.");
        Assert.IsTrue(GameSession.ImmersionPrice(ImmersionProduct.Pizza) > GameSession.ImmersionPrice(ImmersionProduct.Chips));
    }
}

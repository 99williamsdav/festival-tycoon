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
        Assert.AreEqual(pizza.Portions - food.Length, s.CaptureImmersion()!.FoodStock);
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
}

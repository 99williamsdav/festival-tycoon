using Festival.Simulation;

namespace Festival.Tests;

/// <summary>The trader flyer board's derived numbers (assets/source/ui/food-van-traders-v1/INTEGRATION.md).</summary>
[TestClass]
public sealed class TraderFlyerTests
{
    private static FoodTrader Chips => FoodTraders.Find("trader.chip-off-the-old-block")!;
    private static FoodTrader Pizza => FoodTraders.Find("trader.pizza-the-action")!;
    private static FoodTrader Curry => FoodTraders.Find("trader.korma-chameleon")!;

    [TestMethod]
    public void EachTraderHasTheApprovedStraplineAndCardColour()
    {
        Assert.AreEqual(("Fry Me to the Moon", "4fa3d9"), (Chips.Strapline, Chips.CardColour));
        Assert.AreEqual(("That's Amore", "c4573a"), (Pizza.Strapline, Pizza.CardColour));
        Assert.AreEqual(("Karma karma korma", "5b2a4f"), (Curry.Strapline, Curry.CardColour));
        foreach (var trader in FoodTraders.All)
            Assert.IsTrue(trader.Strapline.Length > 0 && System.Text.RegularExpressions.Regex.IsMatch(trader.CardColour, "^[0-9a-f]{6}$"), trader.Name);
        var coffee = TraderFlyers.BrewFighters;
        Assert.AreEqual(("Brew Fighters", "The Best of Brew", "1f5f5a", "Coffee"), (coffee.Name, coffee.Strapline, coffee.CardColour, coffee.Menu));
        Assert.IsFalse(FoodTraders.All.Any(trader => trader.Name == coffee.Name), "Coffee is coming soon: no bookable trader yet.");
    }

    [TestMethod]
    public void PipsReproduceTheDesignersBoard()
    {
        Assert.AreEqual(new TraderPips(5, 2, 2, 1), TraderFlyers.Pips(Chips));
        Assert.AreEqual(new TraderPips(2, 3, 5, 4), TraderFlyers.Pips(Pizza));
        Assert.AreEqual(new TraderPips(3, 5, 4, 2), TraderFlyers.Pips(Curry));
    }

    [TestMethod]
    public void APipRoundsHalvesUpAndStaysWithinNoneToFive()
    {
        Assert.AreEqual(3, TraderFlyers.Pip(2.5));
        Assert.AreEqual(2, TraderFlyers.Pip(2.49));
        Assert.AreEqual(0, TraderFlyers.Pip(-1.2));
        Assert.AreEqual(5, TraderFlyers.Pip(9));
        var slow = Chips with { ServicePermille = 10_000, FillingPercent = 20, EnjoymentPercent = 300, AppealPennies = 4_000 };
        Assert.AreEqual(new TraderPips(1, 0, 5, 5), TraderFlyers.Pips(slow));
    }

    [TestMethod]
    public void ChipsAgainstTheBookedTraderPutThePitchFeeFirstThenTheMetersInOrder()
    {
        CollectionAssert.AreEqual(new[] { new TraderChip("£20 less pitch", false), new TraderChip("quicker", true), new TraderChip("less filling", false) },
            TraderFlyers.Compare(Chips, Pizza).ToArray());
        CollectionAssert.AreEqual(new[] { new TraderChip("£20 more pitch", true), new TraderChip("slower", false), new TraderChip("more filling", true) },
            TraderFlyers.Compare(Pizza, Chips).ToArray());
        CollectionAssert.AreEqual(new[] { new TraderChip("£30 less pitch", false), new TraderChip("quicker", true), new TraderChip("more filling", true) },
            TraderFlyers.Compare(Curry, Pizza).ToArray());
        CollectionAssert.AreEqual(new[] { new TraderChip("£30 more pitch", true), new TraderChip("slower", false), new TraderChip("less filling", false),
                new TraderChip("bigger treat", true), new TraderChip("pulls more", true) },
            TraderFlyers.Compare(Pizza, Curry, most: 9).ToArray(), "Every difference, in order, when there's room.");
    }

    [TestMethod]
    public void NoChipsOnTheBookedFlyerOrWithNobodyBooked()
    {
        Assert.AreEqual(0, TraderFlyers.Compare(Pizza, Pizza).Count);
        Assert.AreEqual(0, TraderFlyers.Compare(Pizza, null).Count);
        // Same fee and meters: nothing to say.
        Assert.AreEqual(0, TraderFlyers.Compare(Chips with { Id = "trader.twin" }, Chips).Count);
        CollectionAssert.AreEqual(new[] { new TraderChip("bigger treat", true) },
            TraderFlyers.Compare(Chips with { Id = "trader.posh", EnjoymentPercent = 130 }, Chips).ToArray());
    }

    [TestMethod]
    public void ASecondVanOfTheSameFoodSaysItSharesThatCrowd()
    {
        Assert.IsNull(TraderFlyers.SameCuisineNote(Chips, 0));
        Assert.AreEqual("2nd chips van: shares the chips crowd", TraderFlyers.SameCuisineNote(Chips, 1));
        Assert.AreEqual("3rd curry van: shares the curry crowd", TraderFlyers.SameCuisineNote(Curry, 2));
        Assert.AreEqual("11th pizza van: shares the pizza crowd", TraderFlyers.SameCuisineNote(Pizza, 10));
    }

    [TestMethod]
    public void EveryFlyerHasItsArt()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "game", "project.godot"))) directory = directory.Parent;
        Assert.IsNotNull(directory, "The repository root.");
        var root = Path.Combine(directory.FullName, "game");
        foreach (var art in FoodTraders.All.Select(trader => trader.Art).Append(TraderFlyers.BrewFighters.Art))
        {
            var path = TraderFlyers.ArtPath(art);
            Assert.IsTrue(File.Exists(Path.Combine(root, path["res://".Length..])), path);
        }
    }

    [TestMethod]
    public void TradersCanBeChosenOnlyUntilThePlanIsPaidAndTheBoardLocksWithTheCommand()
    {
        var s = BuildSession.Planned();
        Assert.IsTrue(s.CanChooseFoodTraders);
        Assert.IsTrue(Validate(s, new ChooseFoodTraderCommand(Pizza.Id)) is null);
        foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        Assert.IsFalse(s.CanChooseFoodTraders, "Paid at Start: the flyers lock.");
        Assert.IsNotNull(Validate(s, new ChooseFoodTraderCommand(Pizza.Id)), "And the command agrees.");
    }

    private static CommandResult? Validate(GameSession s, SessionCommand command) => s.ValidateCommand(new(new(s.NextSubmissionSequence + 1), s.CampaignId,
        s.Phase, s.CurrentTick, s.NextSubmissionSequence, null, command));

    [TestMethod]
    public void ASurvivalTraderAtAnotherVanSaysWhichVan()
    {
        var s = BuildSession.Planned();
        Assert.IsNull(s.TraderBookedElsewhere("food.2", Pizza));
        BuildSession.Accept(s, new ChooseFoodTraderCommand(Pizza.Id));
        Assert.AreEqual(Stalls.FirstVan, s.TraderBookedElsewhere("food.2", Pizza));
        Assert.IsNull(s.TraderBookedElsewhere(Stalls.FirstVan, Pizza), "A van's own trader isn't elsewhere.");
        StringAssert.Contains(s.TraderUnavailable("food.2", Pizza), "already at van 1");
    }
}

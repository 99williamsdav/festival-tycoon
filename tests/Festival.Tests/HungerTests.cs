using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>Food is for the hungry: a gate on hunger, portions that fit it, and pleasure only in sating it.</summary>
[TestClass]
public sealed class HungerTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const long MusicPerSecond = 5_000;

    /// <summary>
    /// Two vans side by side, as the chooser scores them for a guest of this hunger and thrift: the same walk and one
    /// customer ahead at each (the probe's usual queue), each its own service time, price, appeal and filling, against the
    /// music of a well-liked act (5,000 a second, when most guests are listening).
    /// </summary>
    internal static ImmersionProduct Prefers(ImmersionProduct small, ImmersionProduct big, int hunger, int thrift)
    {
        const int walk = 800;
        ActivityOption Option(ImmersionProduct product)
        {
            var complete = walk + GameSession.ImmersionServiceDuration(product);
            return new ActivityOption(ActivityKind.Food, product.ToString(), complete, complete + walk, GameSession.FoodValue(product, hunger, complete),
                GameSession.ImmersionPrice(product) * (2L + thrift / 25) * GameSession.PurchaseValueScale) { FoodRelief = GameSession.FoodRelief(product) };
        }
        var music = Enumerable.Repeat(MusicPerSecond, ActivityChooser.Samples).ToArray();
        var ranked = ActivityChooser.Rank(new NeedLevels(2_000, 2_000, hunger, 2_000), new NeedGrowth(20, 20, 12, 10), music, [Option(small), Option(big)]);
        return Enum.Parse<ImmersionProduct>(ranked[0].Option.FacilityId!);
    }

    [TestMethod]
    public void NobodyIsOfferedFoodUntilTheyArePeckish()
    {
        var s = BuildSession.Started();
        s.AdvanceWithoutSnapshot(2_000);
        var guest = s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed &&
            s.CaptureImmersion()!.People.Single(c => c.AgentId == p.AgentId) is { VendorId: null, Held: null } &&
            s.CaptureMedical()!.Needs.Single(n => n.AgentId == p.AgentId).Intent == MedicalIntent.WatchShow &&
            s.CaptureSnapshot().Wallets.Single(w => w.OwnerId.Value == p.AgentId).CashPennies >= 1_000).AgentId;
        List<ActivityOption> Options(int hunger)
        {
            typeof(GameSession).GetMethod("MutatePerson", Private)!.Invoke(s, [guest, (Action<Person>)(p => { p.Hunger = hunger; p.Thirst = 2_000; })]);
            return (List<ActivityOption>)typeof(GameSession).GetMethod("ActivityOptions", Private)!.Invoke(s, [guest, ActivityKind.Watch])!;
        }
        Assert.IsFalse(Options(ActivityChooser.PeckishHunger - 1).Any(o => o.Kind == ActivityKind.Food || o.Then?.Kind == ActivityKind.Food), "Not hungry: no food.");
        Assert.IsTrue(Options(ActivityChooser.PeckishHunger).Any(o => o.Kind == ActivityKind.Food), "Peckish: food's on offer.");
        Assert.IsTrue(Options(ActivityChooser.PeckishHunger).Any(o => o.Kind == ActivityKind.SoftDrink), "Drinks aren't gated.");
    }

    [TestMethod]
    public void TheSlightlyHungryPickChipsAndTheVeryHungryTheFillingFood()
    {
        foreach (var big in new[] { ImmersionProduct.Pizza, ImmersionProduct.Curry })
        {
            Assert.AreEqual(ImmersionProduct.Chips, Prefers(ImmersionProduct.Chips, big, ActivityChooser.PeckishHunger, 50), $"Peckish: chips over {big}.");
            Assert.AreEqual(big, Prefers(ImmersionProduct.Chips, big, 9_000, 50), $"Starving: {big} over chips.");
        }
    }

    [TestMethod]
    public void ABigPortionIsWorthItsSizeOnlyToSomeoneHungryEnoughToFinishIt()
    {
        var curry = GameSession.FoodRelief(ImmersionProduct.Curry);
        Assert.IsTrue(curry > GameSession.FoodRelief(ImmersionProduct.Pizza) && GameSession.FoodRelief(ImmersionProduct.Pizza) > GameSession.FoodRelief(ImmersionProduct.Chips));
        Assert.AreEqual(GameSession.FoodValue(ImmersionProduct.Curry, curry, 0), GameSession.FoodValue(ImmersionProduct.Curry, 10_000, 0), "Overfilling is worth nothing extra.");
        Assert.AreEqual(GameSession.FoodValue(ImmersionProduct.Curry, curry, 0) / 2, GameSession.FoodValue(ImmersionProduct.Curry, curry / 2, 0), 100);
        Assert.AreEqual(0, GameSession.FoodValue(ImmersionProduct.Chips, 9_000, 0), "Chips are the plain baseline: no appeal over themselves.");
    }

    [TestMethod]
    public void EatingPleasesByTheHungerItSatesAndNotAtAllWhenFull()
    {
        Assert.AreEqual(0, GameSession.FoodSatisfaction(0, 140));
        Assert.AreEqual(100, GameSession.FoodSatisfaction(ActivityChooser.FoodHungerRelief, 100), "A chips portion's worth of hunger sated: chips' enjoyment in full.");
        Assert.AreEqual(50, GameSession.FoodSatisfaction(ActivityChooser.FoodHungerRelief / 2, 100));
        Assert.AreEqual(3_000 * 140 / ActivityChooser.FoodHungerRelief, GameSession.FoodSatisfaction(3_000, 140));

        // In play: someone full eats a tray of chips and sates nothing.
        var s = BuildSession.Started();
        s.AdvanceWithoutSnapshot(2_000);
        var guest = s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed &&
            s.CaptureImmersion()!.People.Single(c => c.AgentId == p.AgentId) is { VendorId: null, Held: null }).AgentId;
        typeof(GameSession).GetMethod("MutatePerson", Private)!.Invoke(s, [guest, (Action<Person>)(p => { p.Hunger = 0; p.Held = new("test-chips", ImmersionProduct.Chips, 0); })]);
        var from = s.CurrentTick;
        for (var i = 0; i < 40 && s.CaptureImmersion()!.People.Single(c => c.AgentId == guest).Held is { ConsumedTicks: < 1_000 }; i++) s.AdvanceWithoutSnapshot(80);
        var held = s.CaptureImmersion()!.People.Single(c => c.AgentId == guest).Held;
        Assert.IsNotNull(held);
        Assert.IsTrue(held.ConsumedTicks > 0, "They've been eating.");
        // Full: all it sates is the little hunger that grows while they eat, so all but nothing is enjoyed.
        Assert.IsTrue(held.Relieved <= (s.CurrentTick - from) * 12 / 80 + 1, $"{held.Relieved} sated in {s.CurrentTick - from} ticks.");
        Assert.IsTrue(GameSession.FoodSatisfaction(held.Relieved, 100) <= 5);
    }
}

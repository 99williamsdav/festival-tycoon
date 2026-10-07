using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class QueueTemperTests
{
    [TestMethod]
    public void PizzaPaysMoreToPitchButServesSlowlyAndLongQueuesWearOnPeople()
    {
        var chips = FoodTraders.Default; var pizza = FoodTraders.Find("trader.pizza-the-action")!;
        Assert.IsTrue(pizza.PitchFeePennies > chips.PitchFeePennies && pizza.SlowService && !chips.SlowService);
        Assert.IsTrue(GameSession.ImmersionServiceDuration(ImmersionProduct.Pizza) > GameSession.ImmersionServiceDuration(ImmersionProduct.Chips));
        var s = BuildSession.Planned();
        BuildSession.Accept(s, new ChooseFoodTraderCommand(pizza.Id));
        foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        Assert.AreEqual(pizza.PitchFeePennies, s.CapturePreparation()!.SetupPayments!.Last().PitchFeePennies);
        Assert.AreEqual(pizza.Portions, s.CaptureImmersion()!.FoodStock);
        // Somebody, somewhere, gets fed up waiting in a queue that isn't the water's.
        var fedUp = false;
        while (!fedUp && s.PreparedStatus == PreparationStatus.Running && s.CurrentTick < 40_000)
        {
            s.AdvanceWithoutSnapshot(80);
            fedUp = s.CaptureDisorder()!.People.Any(p => p.Grievance == DisorderGrievance.QueueWait);
        }
        Assert.IsTrue(fedUp, "A long queue made someone impatient.");
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
    }
}

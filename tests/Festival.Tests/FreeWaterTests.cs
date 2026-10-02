using System.Reflection;
using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class FreeWaterTests
{
    private static long Cash(GameSession s) => s.CaptureSnapshot().FestivalFinances.Single().CashPennies;
    private static void AssertRestores(GameSession s)
    {
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void FreeWaterIsOnlyForTheDayCostsTwentyPoundsEachTimeAndHasNoRefund()
    {
        var s = Ready();
        Assert.IsFalse(Send(s, new SetFreeWaterCommand(true)).IsAccepted, "Not before the gates open.");
        Accept(s, new StartPreparedEditionCommand());
        var before = Cash(s);
        Accept(s, new SetFreeWaterCommand(true));
        Assert.IsTrue(s.FreeWaterOn);
        Assert.AreEqual(before - GameSession.FreeWaterChargePennies, Cash(s));
        Assert.IsFalse(Send(s, new SetFreeWaterCommand(true)).IsAccepted, "Already on.");
        AssertRestores(s);
        s.AdvanceWithoutSnapshot(800);
        var running = Cash(s);
        Accept(s, new SetFreeWaterCommand(false));
        Assert.IsFalse(s.FreeWaterOn);
        Assert.AreEqual(running, Cash(s), "No refund.");
        Accept(s, new SetFreeWaterCommand(true));
        Assert.AreEqual(running - GameSession.FreeWaterChargePennies, Cash(s), "Switching back on costs again.");
        Assert.AreEqual(2, s.CaptureImmersion()!.FreeWaterChargeTicks.Length);
        AssertRestores(s);
    }

    [TestMethod]
    public void WithTheTapsShutAThirstyGuestGetsAFreeCupFromTheBar()
    {
        var s = WithoutFaults(Started());
        s.AdvanceWithoutSnapshot(3_000);
        Accept(s, new DisorderCommand(DisorderAction.CloseWater));
        Accept(s, new SetFreeWaterCommand(true));
        ulong? drinker = null;
        for (var guard = 0; guard < 300 && drinker is null; guard++)
        {
            s.AdvanceWithoutSnapshot(40);
            drinker = s.CaptureImmersion()!.Purchases.FirstOrDefault(p => p.Product == ImmersionProduct.Water)?.AgentId;
        }
        Assert.IsNotNull(drinker, "Someone thirsty queued at the bar for water.");
        var cup = s.CaptureImmersion()!.Purchases.First(p => p.Product == ImmersionProduct.Water);
        Assert.AreEqual(0, cup.PricePennies);
        Assert.AreEqual(0, cup.CostPennies);
        AssertRestores(s);
        // Drunk down, and the cup becomes litter like any other.
        for (var guard = 0; guard < 100 && s.CaptureLitter()!.Pieces.All(w => w.Id != cup.Id); guard++) s.AdvanceWithoutSnapshot(40);
        Assert.AreEqual(ImmersionProduct.Water, s.CaptureLitter()!.Pieces.Single(w => w.Id == cup.Id).Product);
        AssertRestores(s);
    }

    [TestMethod]
    public void TheChargeShowsInTheAccountsAndTheyStillReconcile()
    {
        var s = WithoutFaults(Started());
        s.AdvanceWithoutSnapshot(2_000);
        Accept(s, new SetFreeWaterCommand(true));
        for (var guard = 0; guard < 400 && s.PreparedStatus != PreparationStatus.Finished; guard++) s.AdvanceWithoutSnapshot(400);
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus);
        var accounts = s.CompletedFestivalAccounts!;
        Assert.IsTrue(accounts.OperatingExpenses.Any(e => e.Category == "Emergency measures" && e.AmountPennies == GameSession.FreeWaterChargePennies));
        Assert.IsTrue(accounts.Reconciles);
        AssertRestores(s);
    }
}

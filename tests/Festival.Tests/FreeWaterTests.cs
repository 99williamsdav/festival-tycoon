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

    private static void Mutate(GameSession s, ulong id, Action<Person> edit) =>
        typeof(GameSession).GetMethod("MutatePerson", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, [id, edit]);

    private static (GameSession Session, ulong Guest) QueuedForWater()
    {
        var s = WithoutFaults(Started());
        s.AdvanceWithoutSnapshot(3_000);
        Accept(s, new DisorderCommand(DisorderAction.CloseWater));
        Accept(s, new SetFreeWaterCommand(true));
        for (var guard = 0; guard < 300; guard++)
        {
            s.AdvanceWithoutSnapshot(20);
            if (s.CaptureImmersion()!.People.FirstOrDefault(p => p.Order == ImmersionProduct.Water && p.VendorId is not null) is { } queued) return (s, queued.AgentId);
        }
        throw new InvalidOperationException("Nobody queued for free water.");
    }

    [TestMethod]
    public void SwitchingOffStillServesThoseAlreadyQueuing()
    {
        var (s, guest) = QueuedForWater();
        Accept(s, new SetFreeWaterCommand(false));
        for (var guard = 0; guard < 200 && !s.CaptureImmersion()!.Purchases.Any(p => p.AgentId == guest && p.Product == ImmersionProduct.Water); guard++)
            s.AdvanceWithoutSnapshot(20);
        Assert.IsTrue(s.CaptureImmersion()!.Purchases.Any(p => p.AgentId == guest && p.Product == ImmersionProduct.Water), "Served after the switch-off.");
        Assert.IsFalse(s.CaptureImmersion()!.People.Any(p => p.Order == ImmersionProduct.Water && p.VendorId is null), "No new cups ordered.");
        AssertRestores(s);
    }

    [TestMethod]
    public void ADistressedGuestCanStillGetAFreeCup()
    {
        var s = WithoutFaults(Started());
        s.AdvanceWithoutSnapshot(3_000);
        Accept(s, new DisorderCommand(DisorderAction.CloseWater));
        Accept(s, new SetFreeWaterCommand(true));
        var guest = s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed &&
            s.CaptureMedical()!.Needs.Single(n => n.AgentId == p.AgentId).Stage == MedicalStage.Clear && s.CaptureImmersion()!.People.Single(c => c.AgentId == p.AgentId).Held is null).AgentId;
        Mutate(s, guest, n => { n.Thirst = 9_200; n.HeatExposure = 8_100; });
        var gotThere = false;
        for (var guard = 0; guard < 120 && !gotThere; guard++)
        {
            s.AdvanceWithoutSnapshot(20);
            var c = s.CaptureImmersion()!.People.Single(p => p.AgentId == guest);
            gotThere = c.Order == ImmersionProduct.Water || s.CaptureImmersion()!.Purchases.Any(p => p.AgentId == guest && p.Product == ImmersionProduct.Water);
        }
        Assert.IsTrue(gotThere, "In distress and heading for (or holding) a free cup.");
        AssertRestores(s);
    }

    [TestMethod]
    public void ACupCoolsOnlyInProportionToTheThirstItRelieves()
    {
        var s = WithoutFaults(Started());
        s.AdvanceWithoutSnapshot(3_000);
        Accept(s, new SetFreeWaterCommand(true));
        var guest = s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed &&
            s.CaptureImmersion()!.People.Single(c => c.AgentId == p.AgentId) is { Held: null, VendorId: null }).AgentId;
        typeof(GameSession).GetMethod("CompleteImmersionSale", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, [guest, ImmersionProduct.Water]);
        Mutate(s, guest, n => { n.Thirst = 2_000; n.HeatExposure = 6_000; });
        for (var guard = 0; guard < 200 && s.CaptureImmersion()!.People.Single(p => p.AgentId == guest).Held is not null; guard++) s.AdvanceWithoutSnapshot(20);
        Assert.IsNull(s.CaptureImmersion()!.People.Single(p => p.AgentId == guest).Held, "Drunk.");
        var need = s.CaptureMedical()!.Needs.Single(n => n.AgentId == guest);
        // Thirst was quenched (some has built up again since). Heat came down by at most a quarter of the
        // 2,000 relieved, not by a quarter of a whole cup's 10,000 (the gain while drinking only adds to it).
        Assert.IsTrue(need.Thirst < 800, $"Thirst {need.Thirst}");
        Assert.IsTrue(need.HeatExposure >= 6_000 - 2_000 / 4, $"Heat {need.HeatExposure}");
    }
}

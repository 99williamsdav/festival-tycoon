using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class PowerBudgetTests
{
    private static GameSession StartedWith(params string[] offers)
    {
        var s = Ready(offers: offers);
        Accept(s, new StartPreparedEditionCommand());
        return s;
    }

    private static void Restores(GameSession s)
    {
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void ThePlanShowsTheEveningPeakAgainstTheFarmDiesel()
    {
        var s = Ready();
        var power = s.CapturePower();
        Assert.AreEqual(new PowerDraw(PowerRules.BasicRigDraw, PowerRules.StallDraw, PowerRules.StallDraw, PowerRules.LightsDraw, PowerRules.FarmDieselCapacity), power);
        Assert.IsFalse(power.Over, "The included setup fits the farm's diesel.");
        Accept(s, new AcceptPreparationOfferCommand(PowerRules.ProRigOffer));
        Assert.IsTrue(s.CapturePower().Over, "A pro rig stretches it.");
        Accept(s, new AcceptPreparationOfferCommand(PowerRules.GeneratorOffer));
        Assert.AreEqual(PowerRules.HiredGeneratorCapacity, s.CapturePower().Capacity);
        Assert.IsFalse(s.CapturePower().Over, "A hired generator carries it.");
    }

    [TestMethod]
    public void TheIncludedSetupNeverStrainsTheGenerator()
    {
        var s = StartedWith();
        for (var k = 0; k < 48 && s.PreparedStatus == PreparationStatus.Running; k++)
        {
            s.AdvanceWithoutSnapshot(800);
            Assert.AreEqual(0, s.CaptureEquipment()!.Strain);
            Assert.AreEqual(EquipmentStage.Resolved, s.CaptureEquipment()!.Stage);
        }
        Assert.AreEqual(PowerRules.LightsDraw, s.CapturePower().Lights, "The festoons were on by the end.");
    }

    [TestMethod]
    public void AProRigOverloadsUntilTheBarIsSwitchedOff()
    {
        var s = StartedWith(PowerRules.ProRigOffer);
        // Idling between sets it's well within capacity; playing, 110 against 100, the strain builds to the warning.
        for (var guard = 0; guard < 6_000 && s.CaptureEquipment()!.Stage != EquipmentStage.Warning; guard++) s.AdvanceWithoutSnapshot(8);
        Assert.AreEqual(EquipmentStage.Warning, s.CaptureEquipment()!.Stage);
        Assert.AreEqual(LiveSetStage.Live, s.CaptureLivePerformance()!.Stage, "It's the music that stretches it.");
        Assert.IsTrue(s.CaptureEquipment()!.WarningTick - s.CaptureLivePerformance()!.StartedTick is >= PowerRules.StrainWarning / 10 and < PowerRules.StrainWarning / 10 + 100);
        Restores(s);
        Accept(s, new EquipmentCommand(EquipmentAction.ToggleBarPower));
        Assert.IsFalse(s.StallPowered("drinks"));
        Assert.AreEqual(95, s.CapturePower().Total);
        for (var guard = 0; guard < 2_000 && s.CaptureEquipment()!.Stage == EquipmentStage.Warning; guard++) s.AdvanceWithoutSnapshot(8);
        Assert.AreEqual(EquipmentStage.Resolved, s.CaptureEquipment()!.Stage, "Back under capacity, the generator settles.");
        Restores(s);
    }

    [TestMethod]
    public void ABarSwitchedOffSellsNothingButFreeWater()
    {
        var s = StartedWith();
        s.AdvanceWithoutSnapshot(2_400);
        Accept(s, new EquipmentCommand(EquipmentAction.ToggleBarPower));
        var sold = s.CaptureImmersion()!.Purchases.Length;
        Assert.IsFalse(s.CaptureVendors().Single(v => v.Id == "drinks").Queue.Any(id => s.CaptureImmersion()!.People.Single(p => p.AgentId == id).Order != ImmersionProduct.Water &&
            s.CaptureVendors().Single(v => v.Id == "drinks").OwnerId != id), "Its queue went back to their day.");
        s.AdvanceWithoutSnapshot(4_000);
        Assert.IsFalse(s.CaptureImmersion()!.Purchases.Skip(sold).Any(p => p.Product is ImmersionProduct.Beer or ImmersionProduct.SoftDrink &&
            p.Tick > s.CurrentTick - 4_000 + GameSession.ImmersionServiceDuration(p.Product)), "No drinks sold at a dark bar.");
        Restores(s);
        Accept(s, new EquipmentCommand(EquipmentAction.ToggleBarPower));
        Assert.IsTrue(s.StallPowered("drinks"));
    }

    [TestMethod]
    public void AGeneratorThatKeepsTippingOverStillSaves()
    {
        var s = StartedWith(PowerRules.ProRigOffer);
        var cycles = 0;
        for (var guard = 0; guard < 40_000 && cycles < 40 && s.PreparedStatus == PreparationStatus.Running; guard++)
        {
            s.AdvanceWithoutSnapshot(8);
            var e = s.CaptureEquipment()!;
            // Shed the bar the moment it warns, and put it back once it settles: it tips over again and again.
            if (e.Stage == EquipmentStage.Warning && e.BarPowered) { Accept(s, new EquipmentCommand(EquipmentAction.ToggleBarPower)); cycles++; }
            else if (e.Stage == EquipmentStage.Resolved && !e.BarPowered) Accept(s, new EquipmentCommand(EquipmentAction.ToggleBarPower));
        }
        Assert.IsTrue(cycles >= 3, $"{cycles} cycles.");
        // Far more than any day could log: the opening line stays first and only the latest are kept.
        var log = typeof(GameSession).GetMethod("EquipmentEvent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        for (var i = 0; i < 100; i++) log.Invoke(s, [$"equipment:test:{i}", "A test entry."]);
        Assert.AreEqual("equipment:load", s.CaptureEquipment()!.Evidence[0].Id);
        Assert.IsTrue(s.CaptureEquipment()!.Evidence.Length <= 64);
        Restores(s);
    }

    [TestMethod]
    public void AHiredGeneratorCarriesAProRigAllDay()
    {
        var s = StartedWith(PowerRules.ProRigOffer, PowerRules.GeneratorOffer);
        for (var k = 0; k < 48 && s.PreparedStatus == PreparationStatus.Running; k++)
        {
            s.AdvanceWithoutSnapshot(800);
            Assert.AreEqual(EquipmentStage.Resolved, s.CaptureEquipment()!.Stage);
        }
        Restores(s);
    }
}

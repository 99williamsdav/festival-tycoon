using Festival.Simulation;

namespace Festival.Tests;

/// <summary>With the Pond Stage open, the farm diesel and the Pond generator share one supply.</summary>
[TestClass]
public sealed class PooledPowerTests
{
    [TestMethod]
    public void TheTwoGeneratorsPoolAndTierTwosDefaultFitsOnTheBasicPa()
    {
        var s = NextFestivalTests.Ready(GameSession.CreateDevelopmentFestival(20260922, 2));
        Assert.IsTrue(s.PowerPooled);
        var plan = s.CapturePower();
        Assert.AreEqual(PowerRules.FarmDieselCapacity + StageGeneratorRules.PondCapacity, plan.Capacity, "100 from the farm and 70 from the pond.");
        Assert.AreEqual(PowerRules.BasicRigDraw, plan.OtherStages, "The Pond Stage's rig draws from the pool.");
        Assert.AreEqual(4 * PowerRules.StallDraw, plan.Bar + plan.Food, "Two bars and two vans in the default.");
        Assert.IsFalse(plan.Over, $"The default's evening peak, {plan.Total}, fits the pool's {plan.Capacity}.");
        Assert.AreEqual(plan, s.CaptureStagePower(FestivalStages.PondId), "The Pond generator shows the same pooled supply.");
        BuildSession.Accept(s, new AcceptPreparationOfferCommand(PowerRules.GeneratorOffer));
        Assert.AreEqual(PowerRules.HiredGeneratorCapacity + StageGeneratorRules.PondCapacity, s.CapturePower().Capacity);
    }

    [TestMethod]
    public void FourStallsOnTheBasicPaNeverStrainThePoolOnATierTwoDay()
    {
        var s = NextFestivalTests.Ready(GameSession.CreateDevelopmentFestival(20260922, 2));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        for (var i = 0; i < 100 && s.PreparedStatus == PreparationStatus.Running; i++)
        {
            s.AdvanceWithoutSnapshot(400);
            Assert.IsFalse(s.CapturePower().Over, $"Over at {s.CurrentTick}: {s.CapturePower()}");
            Assert.AreEqual(0, s.CaptureEquipment()!.Strain);
            Assert.AreEqual(0, s.CaptureStageGenerator(FestivalStages.PondId)!.Strain);
        }
    }

    [TestMethod]
    public void AGeneratorCutOffLeavesThePoolAndTheRestShareWhatsLeft()
    {
        var s = NextFestivalTests.Ready(GameSession.CreateDevelopmentFestival(20260922, 2));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        s.AdvanceWithoutSnapshot(2_000);
        BuildSession.Accept(s, new StageGeneratorCommand(FestivalStages.PondId, StageGeneratorAction.Isolate));
        var draw = s.CapturePower();
        Assert.AreEqual(PowerRules.FarmDieselCapacity, draw.Capacity, "The pond's 70 has gone.");
        Assert.AreEqual(0, draw.OtherStages, "Its stage is cut off with it.");
        // The four stalls and the trailer's rig now share the farm's 100: once the trailer's set is on, that's 110.
        var strained = false;
        for (var i = 0; i < 100 && !strained; i++)
        {
            s.AdvanceWithoutSnapshot(80);
            strained = s.CaptureEquipment()!.Strain > 0;
        }
        Assert.IsTrue(strained, "The farm generator strains on what's left.");
        Assert.AreEqual(0, s.CaptureStageGenerator(FestivalStages.PondId)!.Strain, "A generator out of the pool carries none of it.");
    }

    [TestMethod]
    public void ASingleStageKeepsTheFarmDieselAlone()
    {
        var s = BuildSession.Ready();
        Assert.IsFalse(s.PowerPooled);
        var plan = s.CapturePower();
        Assert.AreEqual(PowerRules.FarmDieselCapacity, plan.Capacity);
        Assert.AreEqual(0, plan.OtherStages);
        Assert.AreEqual(PowerRules.BasicRigDraw + 2 * PowerRules.StallDraw + PowerRules.LightsDraw, plan.Total);
    }
}

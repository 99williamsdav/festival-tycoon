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

    private static readonly System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    private static T Field<T>(GameSession s, string name) => (T)typeof(GameSession).GetField(name, Private)!.GetValue(s)!;
    private static void SetField(GameSession s, string name, object? value) => typeof(GameSession).GetField(name, Private)!.SetValue(s, value);

    /// <summary>The Pond Stage trial at Tier 1 on the pro rig: both rigs, the bar, the van and the lights on the pool of 170.</summary>
    private static GameSession ProRigOnBothStages()
    {
        var s = BuildSession.PondDrafted();
        BuildSession.Accept(s, new SetProgrammeCommand(BuildSession.Acts));
        BuildSession.Accept(s, new SetProgrammeCommand(["act.two-men-harmonium", "act.dj-spreadsheet", "act.kerry-co-op"]) { StageId = FestivalStages.PondId });
        BuildSession.Accept(s, new SetPreparationStockCommand(40, 32));
        foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        BuildSession.Accept(s, new AcceptPreparationOfferCommand(BuildSession.ExtraId(s, StaffRole.Sound)));
        BuildSession.Accept(s, new AcceptPreparationOfferCommand(PowerRules.ProRigOffer));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        return s;
    }

    /// <summary>Plays on until both sets are live and the pool is over: 80 + 80 + 30 = 190 of 170.</summary>
    private static GameSession OverloadedOnBothStages()
    {
        var s = ProRigOnBothStages();
        var start = s.CapturePreparation()!.StartedTick;
        s.AdvanceWithoutSnapshot((int)(start + FestivalStages.Pond.SlotStarts[0] + 200 - s.CurrentTick));
        Assert.IsTrue(s.CapturePower().Over, s.CapturePower().ToString());
        return s;
    }

    [TestMethod]
    public void CuttingAStageTakesOffOnlyItsRigSoItLightensTheOverload()
    {
        var s = OverloadedOnBothStages();
        var before = s.CapturePower();
        BuildSession.Accept(s, new EquipmentCommand(EquipmentAction.Isolate));
        var after = s.CapturePower();
        Assert.AreEqual(before.Capacity, after.Capacity, "The farm generator runs on for the pool.");
        Assert.AreEqual(0, after.Stage, "Only the trailer's rig comes off.");
        Assert.IsTrue(after.Total - after.Capacity < before.Total - before.Capacity, "The overage falls.");
        Assert.IsTrue(s.CaptureEquipment()!.StageCut && !s.StagePowered);
        Assert.AreNotEqual(EquipmentStage.Isolated, s.CaptureEquipment()!.Stage, "The generator itself isn't isolated: it can still warn, fault and hurt.");
        Assert.IsFalse(BuildSession.Send(s, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted, "Already cut.");
        // The Pond Stage's cut is the same: its rig off, its 70 still in the pool.
        BuildSession.Accept(s, new StageGeneratorCommand(FestivalStages.PondId, StageGeneratorAction.Isolate));
        Assert.AreEqual(before.Capacity, s.CapturePower().Capacity);
        Assert.AreEqual(0, s.CapturePower().OtherStages);
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.IsTrue(restored.Session!.CaptureEquipment()!.StageCut);
    }

    [TestMethod]
    public void WithEveryGeneratorInThePoolGoneTheStallsStopServing()
    {
        var s = ProRigOnBothStages();
        Assert.IsTrue(s.StallPowered("food") && s.StallPowered("drinks"));
        SetField(s, "_equipment", s.CaptureEquipment()! with { Stage = EquipmentStage.Terminal });
        Assert.IsTrue(s.StallPowered("food"), "The Pond generator still runs them.");
        var generators = Field<StageGeneratorSnapshot[]>(s, "_stageGenerators");
        SetField(s, "_stageGenerators", generators.Select(g => g with { Stage = EquipmentStage.Terminal }).ToArray());
        Assert.AreEqual(0, s.CapturePower().Capacity);
        Assert.IsFalse(s.StallPowered("food") || s.StallPowered("drinks"), "Nothing left to run them.");
    }

    [TestMethod]
    public void ARepairSettlesEveryGeneratorInThePoolSoTheyGoOnWarningTogether()
    {
        var s = OverloadedOnBothStages();
        while (s.CaptureEquipment()!.Stage != EquipmentStage.Warning) s.AdvanceWithoutSnapshot(40);
        Assert.AreEqual(EquipmentStage.Warning, s.CaptureStageGenerator(FestivalStages.PondId)!.Stage);
        // A repair finishing now (as if maintenance had been at it for its 20 seconds).
        SetField(s, "_equipment", s.CaptureEquipment()! with { JobStage = MaintenanceStage.Repairing, RepairStartedTick = s.CurrentTick - 10_000 });
        s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(0, s.CaptureEquipment()!.Strain);
        Assert.AreEqual(0, s.CaptureStageGenerator(FestivalStages.PondId)!.Strain);
        Assert.AreEqual(EquipmentStage.Resolved, s.CaptureStageGenerator(FestivalStages.PondId)!.Stage);
        s.AdvanceWithoutSnapshot(400);
        Assert.IsTrue(s.CaptureEquipment()!.Strain > 0);
        Assert.AreEqual(s.CaptureEquipment()!.Strain, s.CaptureStageGenerator(FestivalStages.PondId)!.Strain, "Still one strain.");
    }

    [TestMethod]
    public void TheNextTickPredictorCountsTheLightsComingOn()
    {
        // Tier 1 on the standard rig with its set playing as the lights come on: 65 + 30 = 95 before, 105 after.
        var s = BuildSession.Started(offers: "equipment.rent");
        var lightsOn = s.CapturePreparation()!.StartedTick + PowerRules.LightsOnTickFor(s.PreparedEditionDurationTicks);
        s.AdvanceWithoutSnapshot((int)(lightsOn - 1 - s.CurrentTick));
        var live = Field<LivePerformanceSnapshot?[]>(s, "_livePerformances");
        live[0] = live[0]! with { Stage = LiveSetStage.Live };
        Assert.AreEqual(PowerRules.StandardRigDraw + 2 * PowerRules.StallDraw, s.CapturePower().Total);
        // Five short of the warning: only the lights' 10 tip it over on the next tick.
        SetField(s, "_equipment", s.CaptureEquipment()! with { Stage = EquipmentStage.Resolved, Strain = PowerRules.StrainWarning - 5 });
        Assert.IsTrue(s.EquipmentBoundaryOnNextTick, "The boundary is next tick's, lights and all.");
        s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(EquipmentStage.Warning, s.CaptureEquipment()!.Stage);
    }

    [TestMethod]
    public void APooledSaveRefusesAnIsolatedFarmGeneratorAndAStrainThatHasDriftedApart()
    {
        var s = OverloadedOnBothStages();
        s.AdvanceWithoutSnapshot(400);
        var saved = s.CapturePersistenceSnapshot();
        Assert.IsTrue(saved.Equipment!.Strain > 0 && saved.StageGenerators![0].Strain == saved.Equipment.Strain);
        Assert.IsTrue(GameSession.Restore(saved).IsSuccess);
        // Isolated, the farm generator would keep its capacity in the pool yet never warn, fault or hurt anyone.
        Assert.IsFalse(GameSession.Restore(saved with { Equipment = saved.Equipment with { Stage = EquipmentStage.Isolated } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { Equipment = saved.Equipment with { Stage = EquipmentStage.Isolated, StageCut = true } }).IsSuccess);
        // One supply, one strain.
        Assert.IsFalse(GameSession.Restore(saved with { StageGenerators = [saved.StageGenerators[0] with { Strain = saved.Equipment.Strain + 1 }] }).IsSuccess);
    }

    [TestMethod]
    public void ACutTrailerStageOnAPooledSupplyCanBeRestoredAndTheMusicComesBack()
    {
        var s = NextFestivalTests.Ready(GameSession.CreateDevelopmentFestival(20260922, 2));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        var start = s.CapturePreparation()!.StartedTick;
        s.AdvanceWithoutSnapshot((int)(start + FestivalStages.Main.SlotStarts[0] + 400 - s.CurrentTick));
        Assert.AreEqual(LiveSetStage.Live, s.CaptureLivePerformance(FestivalStages.MainId)!.Stage);
        BuildSession.Accept(s, new EquipmentCommand(EquipmentAction.Isolate));
        s.AdvanceWithoutSnapshot(2);
        Assert.AreEqual(LiveSetStage.Interrupted, s.CaptureLivePerformance(FestivalStages.MainId)!.Stage);
        BuildSession.Accept(s, new DisorderCommand(DisorderAction.RestoreMusic));
        Assert.IsFalse(s.CaptureEquipment()!.StageCut);
        Assert.IsTrue(s.StagePowered);
        for (var i = 0; i < 40 && s.CaptureLivePerformance(FestivalStages.MainId)!.Stage != LiveSetStage.Live; i++) s.AdvanceWithoutSnapshot(40);
        Assert.AreEqual(LiveSetStage.Live, s.CaptureLivePerformance(FestivalStages.MainId)!.Stage, "The music's back.");
        Assert.IsTrue(BuildSession.Send(s, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted, "And it can be cut again.");
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

using System.Reflection;
using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class FacilityFaultTests
{
    private static T Call<T>(GameSession s, string method, params object[] args) =>
        (T)typeof(GameSession).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, args)!;

    private static void AssertRestores(GameSession s)
    {
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
    }

    /// <summary>An edition advanced, second by second, to the first fault of the kind; null if the day passes without one.</summary>
    private static (GameSession Session, FacilityFault Fault)? FirstFault(FacilityFaultKind kind, ulong seed, params string[] offers)
    {
        var s = Ready(seed, 0, offers);
        Accept(s, new StartPreparedEditionCommand());
        while (s.PreparedStatus == PreparationStatus.Running)
        {
            s.AdvanceWithoutSnapshot(80);
            if (s.CaptureFaults()!.Faults.FirstOrDefault(f => f.Kind == kind && f.Stage == FacilityFaultStage.Active) is { } fault) return (s, fault);
        }
        return null;
    }

    private static (GameSession Session, FacilityFault Fault) Find(FacilityFaultKind kind, params string[] offers)
    {
        for (var seed = 20260922UL; seed < 20260942UL; seed++)
            if (FirstFault(kind, seed, offers) is { } found) return found;
        throw new InvalidOperationException($"No {kind} in twenty days.");
    }

    [TestMethod]
    public void EachUseHasTheCalibratedSmallChance()
    {
        int Hits(int chance) => Enumerable.Range(0, 100_000).Count(i => FaultRules.Roll(20260922, "toilet.main", i, 7, chance));
        Assert.AreEqual(739, Hits(FaultRules.ToiletStuckChancePer10k) / 10.0, 40);
        Assert.AreEqual(377, Hits(FaultRules.TapBreakChancePer10k) / 10.0, 30);
        // About a 90% chance of at least one incident in a typical day's uses.
        Assert.AreEqual(0.9, 1 - Math.Pow(1 - FaultRules.ToiletStuckChancePer10k / 10_000.0, 30), 0.01);
        Assert.AreEqual(0.9, 1 - Math.Pow(1 - FaultRules.TapBreakChancePer10k / 10_000.0, 60), 0.01);
    }

    [TestMethod]
    public void AStewardFreesSomeoneStuckAndTheyPanicUntilThen()
    {
        var (s, fault) = Find(FacilityFaultKind.StuckInToilet);
        var victim = fault.VictimId;
        var toilet = s.CaptureToilets().Single(t => t.Id == fault.FacilityId);
        Assert.AreEqual(victim, toilet.OwnerId, "Still inside.");
        Assert.IsTrue(Call<int>(s, "FaultDelayTicks", fault.FacilityId) >= FaultRules.RescueTicks, "The queue expects a long wait.");
        var satisfaction = s.CapturePreparation()!.People.Single(p => p.AgentId == victim).Satisfaction;
        AssertRestores(s);
        long stuckFor = 0;
        for (var guard = 0; guard < 600 && s.CaptureFaults()!.Faults.Single(f => f.Id == fault.Id).Stage == FacilityFaultStage.Active; guard++)
        {
            s.AdvanceWithoutSnapshot(8);
            var open = s.CaptureFaults()!.Faults.Single(f => f.Id == fault.Id);
            if (open.Stage == FacilityFaultStage.Active)
            {
                stuckFor = s.CurrentTick - open.StartedTick;
                Assert.AreEqual(victim, s.CaptureToilets().Single(t => t.Id == fault.FacilityId).OwnerId);
                if (open.WorkerId is { } worker)
                {
                    Assert.IsTrue(s.GetStewardResponses().Any(r => r.WorkerId == worker), "Only stewards free people.");
                    if (guard % 10 == 0) AssertRestores(s);
                }
            }
        }
        var resolved = s.CaptureFaults()!.Faults.Single(f => f.Id == fault.Id);
        Assert.AreEqual(FacilityFaultStage.Fixed, resolved.Stage);
        Assert.IsTrue(stuckFor >= FaultRules.RescueTicks);
        Assert.IsTrue(s.CapturePreparation()!.People.Single(p => p.AgentId == victim).Satisfaction < satisfaction, "Panic cost them.");
        s.AdvanceWithoutSnapshot(800);
        Assert.AreNotEqual(victim, s.CaptureToilets().Single(t => t.Id == fault.FacilityId).OwnerId, "Out once freed.");
        AssertRestores(s);
    }

    [TestMethod]
    public void SomeoneWhoCollapsesWhileStuckStaysInsideUntilTheDoorOpensThenLiesOutside()
    {
        (GameSession Session, FacilityFault Fault)? found = null;
        for (var seed = 20260922UL; seed < 20260942UL && found is null; seed++)
            if (FirstFault(FacilityFaultKind.StuckInToilet, seed) is { } candidate &&
                candidate.Session.CapturePreparation()!.People.Single(p => p.AgentId == candidate.Fault.VictimId).Role == ProtectedPersonRole.Guest)
                found = candidate;
        var (s, fault) = found ?? throw new InvalidOperationException("No guest stuck in twenty days.");
        var victim = fault.VictimId;
        // Distressed long enough ago that the next tick is their collapse.
        var medical = s.CaptureMedical()!;
        typeof(GameSession).GetProperty("MedicalView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s, medical with { Needs = medical.Needs
            .Select(n => n.AgentId == victim ? n with { Stage = MedicalStage.Distress, WarningTick = s.CurrentTick - GameSession.MedicalCollapseDelayTicks, Thirst = 9_500, HeatExposure = 9_000 } : n).ToArray() });
        s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalStage.Collapsed, s.CaptureMedical()!.Needs.Single(n => n.AgentId == victim).Stage);
        Assert.AreEqual(victim, s.CaptureToilets().Single(t => t.Id == fault.FacilityId).OwnerId, "Collapsed behind the locked door.");
        Assert.IsTrue(s.StuckInToilet(victim));
        Assert.IsNull(FaultRules.Remark(s.CaptureFaults()!.Faults.Single(f => f.Id == fault.Id), s.CurrentTick - s.CurrentTick % FaultRules.ShoutEveryTicks, hot: true, conscious: false), "Silent.");
        StringAssert.Contains(s.FaultStatus(fault.FacilityId)!, "COLLAPSED INSIDE");
        Assert.IsFalse(Send(s, new MedicalCommand(victim, MedicalAction.DispatchMedic, s.GetMedicResponses()[0].WorkerId)).IsAccepted, "The medic can't get in.");
        AssertRestores(s);
        for (var guard = 0; guard < 600 && s.CaptureFaults()!.Faults.Single(f => f.Id == fault.Id).Stage == FacilityFaultStage.Active; guard++)
            s.AdvanceWithoutSnapshot(4);
        Assert.AreEqual(FacilityFaultStage.Fixed, s.CaptureFaults()!.Faults.Single(f => f.Id == fault.Id).Stage);
        var toilet = s.CaptureToilets().Single(t => t.Id == fault.FacilityId);
        Assert.AreNotEqual(victim, toilet.OwnerId, "The cubicle is free.");
        var body = s.CaptureObservation().NavigationAgents.Single(a => a.Id.Value == victim);
        var outside = TraversalGrid.CellCentre(GameSession.ToiletExitCell(toilet));
        Assert.AreEqual((outside.XMillimetres, outside.ZMillimetres), (body.XMillimetres, body.ZMillimetres), "Lying just outside the door.");
        Assert.IsTrue(s.CaptureMedical()!.Needs.Single(n => n.AgentId == victim).Stage is MedicalStage.Collapsed or MedicalStage.Critical);
        AssertRestores(s);
        Assert.IsNotNull(s.SelectRoleResponse(ResponseRole.Medic, victim, out var issue), $"A medic can reach them now: {issue}");
    }

    [TestMethod]
    public void ADeathBehindTheLockedDoorFreezesTheSceneAsFound()
    {
        (GameSession Session, FacilityFault Fault)? found = null;
        for (var seed = 20260922UL; seed < 20260942UL && found is null; seed++)
            if (FirstFault(FacilityFaultKind.StuckInToilet, seed) is { } candidate &&
                candidate.Session.CapturePreparation()!.People.Single(p => p.AgentId == candidate.Fault.VictimId).Role == ProtectedPersonRole.Guest)
                found = candidate;
        var (s, fault) = found ?? throw new InvalidOperationException("No guest stuck in twenty days.");
        var victim = fault.VictimId;
        // Collapsed inside and critical long enough that the next tick is fatal.
        var medical = s.CaptureMedical()!;
        typeof(GameSession).GetProperty("MedicalView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s, medical with { Needs = medical.Needs
            .Select(n => n.AgentId == victim ? n with { Stage = MedicalStage.Critical, Intent = MedicalIntent.Collapsed, Thirst = 9_500, HeatExposure = 9_000,
                WarningTick = s.CurrentTick - GameSession.MedicalDeathDelayTicks - 1_600, CollapseTick = s.CurrentTick - GameSession.MedicalDeathDelayTicks,
                CriticalTick = s.CurrentTick - GameSession.MedicalDeathDelayTicks + GameSession.MedicalCriticalDelayTicks } : n).ToArray() });
        for (var guard = 0; guard < 4 && s.PreparedStatus != PreparationStatus.Failed; guard++) s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(PreparationStatus.Failed, s.PreparedStatus);
        Assert.AreEqual(victim, s.CaptureToilets().Single(t => t.Id == fault.FacilityId).OwnerId, "Found behind the door, not carried out after the freeze.");
        Assert.AreEqual(FacilityFaultStage.Active, s.CaptureFaults()!.Faults.Single(f => f.Id == fault.Id).Stage);
        AssertRestores(s);
    }

    [TestMethod]
    public void NobodyCanGuideAStuckGuestOutAroundTheRescue()
    {
        var (s, fault) = Find(FacilityFaultKind.StuckInToilet);
        var steward = s.GetStewardResponses().First().WorkerId;
        var medic = s.GetMedicResponses().First().WorkerId;
        Assert.IsFalse(Send(s, new StaffInterventionCommand(fault.VictimId, steward, StaffInterventionAction.GuideToWater)).IsAccepted);
        Assert.IsFalse(Send(s, new StaffInterventionCommand(fault.VictimId, medic, StaffInterventionAction.GuideToRest)).IsAccepted);
        Assert.IsFalse(Send(s, new MedicalCommand(fault.VictimId, MedicalAction.DispatchMedic, medic)).IsAccepted);
        Assert.IsFalse(Send(s, new DisorderCommand(DisorderAction.DispatchSecurity, fault.VictimId, steward)).IsAccepted);
        Assert.AreEqual(fault.VictimId, s.CaptureToilets().Single(t => t.Id == fault.FacilityId).OwnerId);
    }

    [TestMethod]
    public void GuidanceOrderedBeforeTheJamStandsDownOnceTheyAreStuck()
    {
        var (s, fault) = Find(FacilityFaultKind.StuckInToilet);
        // As if issued while the visit was still ordinary: the command is applied without today's refusal.
        var steward = s.GetStewardResponses().Where(r => r.WorkerId != s.CaptureFaults()!.Faults.Single(f => f.Id == fault.Id).WorkerId).Select(r => r.WorkerId)
            .DefaultIfEmpty(s.GetStewardResponses()[0].WorkerId).First();
        typeof(GameSession).GetMethod("ApplyStaffIntervention", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(s, [new StaffInterventionCommand(fault.VictimId, steward, StaffInterventionAction.GuideToWater)]);
        s.AdvanceWithoutSnapshot(2);
        Assert.IsFalse(s.CaptureStaffInterventions().Any(j => j.GuestId == fault.VictimId && j.Stage is StaffInterventionStage.Travelling or StaffInterventionStage.Guiding or StaffInterventionStage.Escorting),
            "The guidance stood down.");
        Assert.AreEqual(fault.VictimId, s.CaptureToilets().Single(t => t.Id == fault.FacilityId).OwnerId, "Still inside until the rescue.");
        Assert.IsTrue(s.StuckInToilet(fault.VictimId));
        AssertRestores(s);
    }

    [TestMethod]
    public void AWorkerOnTheWayHeadsBackIfTheJamEndsAnotherWay()
    {
        var (s, fault) = Find(FacilityFaultKind.StuckInToilet);
        ulong? worker = null;
        for (var guard = 0; guard < 200 && worker is null; guard++)
        {
            s.AdvanceWithoutSnapshot(8);
            worker = s.CaptureFaults()!.Faults.Single(f => f.Id == fault.Id) is { Stage: FacilityFaultStage.Active, WorkStartedTick: < 0, WorkerId: { } w } ? w : null;
        }
        Assert.IsNotNull(worker, "A steward set off.");
        // Something else gets them out (as a collapse would): the toilet releases its occupant.
        typeof(GameSession).GetMethod("InterruptToiletOwner", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, [fault.VictimId]);
        s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(FacilityFaultStage.Fixed, s.CaptureFaults()!.Faults.Single(f => f.Id == fault.Id).Stage);
        var intent = s.CaptureObservation().NavigationAgents.Single(a => a.Id.Value == worker).IntentId;
        Assert.IsFalse(intent?.StartsWith("fault.", StringComparison.Ordinal) == true, $"Still heading to the toilet: {intent}");
        AssertRestores(s);
    }

    [TestMethod]
    public void WithoutMaintenanceAStewardBodgesABrokenTapToHalfFlow()
    {
        var (s, fault) = Find(FacilityFaultKind.BrokenTap);
        var tap = fault.FacilityId;
        Assert.IsNull(s.CaptureFacilities()!.Taps.Single(t => t.Id == tap).OwnerId, "Nobody drinks from a broken tap.");
        Assert.IsTrue(Call<int>(s, "FaultDelayTicks", tap) >= FaultRules.BodgeTicks);
        AssertRestores(s);
        for (var guard = 0; guard < 400 && s.CaptureFaults()!.Faults.Single(f => f.Id == fault.Id).Stage == FacilityFaultStage.Active; guard++)
        {
            Assert.IsNull(s.CaptureFacilities()!.Taps.Single(t => t.Id == tap).OwnerId);
            s.AdvanceWithoutSnapshot(16);
        }
        Assert.AreEqual(FacilityFaultStage.Bodged, s.CaptureFaults()!.Faults.Single(f => f.Id == fault.Id).Stage);
        Assert.IsTrue(s.TapBodged(tap));
        AssertRestores(s);
    }

    [TestMethod]
    public void MaintenanceMendsABrokenTapProperly()
    {
        var (s, fault) = Find(FacilityFaultKind.BrokenTap, "maintenance.worker");
        var maintenance = s.CaptureEquipment()!.WorkerId!.Value;
        for (var guard = 0; guard < 400 && s.CaptureFaults()!.Faults.Single(f => f.Id == fault.Id).Stage == FacilityFaultStage.Active; guard++)
        {
            if (s.CaptureFaults()!.Faults.Single(f => f.Id == fault.Id).WorkerId is { } worker) Assert.AreEqual(maintenance, worker, "Maintenance, not a steward, when hired.");
            s.AdvanceWithoutSnapshot(16);
        }
        Assert.AreEqual(FacilityFaultStage.Fixed, s.CaptureFaults()!.Faults.Single(f => f.Id == fault.Id).Stage);
        Assert.IsFalse(s.TapBodged(fault.FacilityId));
        AssertRestores(s);
    }

    [TestMethod]
    public void APortalooIsAHotBoxInHotWeather()
    {
        var (s, fault) = Find(FacilityFaultKind.StuckInToilet);
        var victim = fault.VictimId;
        int Heat() => s.CaptureMedical()!.Needs.Single(n => n.AgentId == victim).HeatExposure;
        var before = Heat(); s.AdvanceWithoutSnapshot(80);
        var guest = s.CapturePreparation()!.People.Single(p => p.AgentId == victim).Role == ProtectedPersonRole.Guest;
        // A guest normally gains one point every four ticks; inside, two.
        if (guest) Assert.AreEqual(Math.Min(10_000, before + 40), Heat());
    }

    [TestMethod]
    public void StuckPeopleShoutMoreDesperatelyAndABrokenTapGetsOneGrumble()
    {
        var stuck = new FacilityFault("stuck:toilet.main:3", FacilityFaultKind.StuckInToilet, "toilet.main", 7, 1_000, FacilityFaultStage.Active);
        Assert.IsNotNull(FaultRules.Remark(stuck, 1_000));
        Assert.IsNull(FaultRules.Remark(stuck, 1_000 + FaultRules.ShoutTicks), "Shouts come and go.");
        Assert.AreNotEqual(FaultRules.Remark(stuck, 1_000), FaultRules.Remark(stuck, 1_000 + 3_200), "Later shouts are more desperate.");
        Assert.IsNull(FaultRules.Remark(stuck with { Stage = FacilityFaultStage.Fixed }, 1_000), "Quiet once freed.");
        var shouts = Enumerable.Range(0, 30).Select(n => 1_000L + n * FaultRules.ShoutEveryTicks).ToArray();
        Assert.AreEqual(10, shouts.Count(tick => FaultRules.Remark(stuck, tick, hot: true) == FaultRules.HotShout), "Every third shout in a heatwave.");
        Assert.IsFalse(shouts.Any(tick => FaultRules.Remark(stuck, tick)!.Contains("hot", StringComparison.OrdinalIgnoreCase)), "No heat complaints in mild weather.");
        var tap = new FacilityFault("broken:water.main:500", FacilityFaultKind.BrokenTap, "water.main", 9, 500, FacilityFaultStage.Active);
        Assert.IsNotNull(FaultRules.Remark(tap, 600));
        Assert.IsNull(FaultRules.Remark(tap, 500 + FaultRules.GrumbleTicks), "One grumble, not a running commentary.");
    }

    [TestMethod]
    public void AFaultFreeFixtureStaysFaultFreeAcrossReload()
    {
        var s = WithoutFaults(Started());
        s.AdvanceWithoutSnapshot(20_000);
        Assert.AreEqual(0, s.CaptureFaults()!.Faults.Length);
        AssertRestores(s);
    }
}

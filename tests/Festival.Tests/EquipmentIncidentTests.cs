using Festival.Simulation;
using Festival.Persistence;

namespace Festival.Tests;

[TestClass]
public sealed class EquipmentIncidentTests
{
    private static readonly SaveCompatibility Compatibility = new("r0.02-tests", "equipment", "v1");
    private static CommandResult Execute(GameSession s, SessionCommand command) => s.Execute(new(new CommandId(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase, s.CurrentTick, s.NextSubmissionSequence, null, command));
    private static GameSession Started(ulong seed = 2, bool worker = false, int tier = 1)
    {
        var s = GameSession.CreateEquipmentCampaign(seed, tier);
        foreach (var id in new[] { "act.folk", "staff.steward", "equipment.buy" }.Concat(worker ? new[] { "maintenance.worker" } : []))
            Assert.IsTrue(Execute(s, new AcceptPreparationOfferCommand(id)).IsAccepted, id);
        Assert.IsTrue(Execute(s, new StartPreparedEditionCommand()).IsAccepted);
        return s;
    }
    private static GameSession Restore(GameSession s)
    {
        var r = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(r.IsSuccess, r.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, r.Session!.CaptureSnapshot().AuthoritativeHash);
        return r.Session;
    }

    [TestMethod]
    public void GeneratorSiteIsAuthoritativeBesideTrailerAndNotInAudienceApron()
    {
        var s = Started(worker: true);
        var equipment = s.CaptureEquipment()!;
        Assert.AreEqual(-20_250, equipment.XMillimetres);
        Assert.AreEqual(14_500, equipment.ZMillimetres);
        Assert.IsTrue(equipment.XMillimetres < LowerWitteringFarmScenario.TrailerStageXMillimetres);
        Assert.IsTrue(equipment.XMillimetres < -12_000, "Audience apron starts east of the trailer.");
        var restored = Restore(s);
        Assert.AreEqual(equipment.XMillimetres, restored.CaptureEquipment()!.XMillimetres);
        Assert.AreEqual(equipment.ZMillimetres, restored.CaptureEquipment()!.ZMillimetres);
        restored.AdvanceWithoutSnapshot(2_400);
        Assert.IsTrue(Execute(restored, new EquipmentCommand(EquipmentAction.DispatchMaintenance)).IsAccepted);
        restored.AdvanceWithoutSnapshot(1_000);
        Assert.AreEqual(MaintenanceStage.Repairing, restored.CaptureEquipment()!.JobStage);
    }

    [TestMethod]
    public void TimelyCountersPreventDeathAcrossSeedsAndSaveStages()
    {
        foreach (var action in new[] { EquipmentAction.ShedLoad, EquipmentAction.Isolate, EquipmentAction.DispatchMaintenance })
        for (ulong seed = 0; seed < 6; seed++)
        {
            var s = Started(seed, action == EquipmentAction.DispatchMaintenance, seed % 2 == 0 ? 2 : 1);
            s.AdvanceWithoutSnapshot(GameSession.EquipmentWarningDelayTicks);
            Assert.AreEqual(EquipmentStage.Warning, s.CaptureEquipment()!.Stage);
            s = Restore(s);
            Assert.IsTrue(Execute(s, new EquipmentCommand(action)).IsAccepted);
            s = Restore(s);
            var other = Restore(s);
            s.AdvanceWithoutSnapshot(6_000); other.AdvanceWithoutSnapshot(6_000);
            Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, other.CaptureSnapshot().AuthoritativeHash);
            Assert.AreEqual(0, s.CaptureLifecycleSnapshot()!.Casualties.Count);
            Assert.IsTrue(s.CaptureEquipment()!.Stage is EquipmentStage.Resolved or EquipmentStage.Isolated);
            if (action == EquipmentAction.DispatchMaintenance)
            {
                var e = s.CaptureEquipment()!;
                Assert.AreEqual(MaintenanceStage.Completed, e.JobStage);
                Assert.IsTrue(e.RepairStartedTick > e.JobDispatchedTick, "Must physically travel.");
                var completion = e.Evidence.Single(item => item.Id == "equipment:repair-complete").Tick;
                Assert.AreEqual(GameSession.EquipmentRepairTicks, completion - e.RepairStartedTick);
                Assert.IsTrue(e.WarningTick + GameSession.EquipmentDeathDelayTicks - completion >= 2_400,
                    $"At least 30 real seconds of reaction margin after measured immediate dispatch; seed={seed}, travel={e.RepairStartedTick-e.JobDispatchedTick}, margin={e.WarningTick+GameSession.EquipmentDeathDelayTicks-completion} ticks.");
                Console.WriteLine($"seed={seed} people={s.CapturePreparation()!.People.Length} travelTicks={e.RepairStartedTick-e.JobDispatchedTick} repairTicks=1600 marginTicks={e.WarningTick+4800-completion}");
            }
            Restore(s);
        }
    }

    [TestMethod]
    public void EscalationIsCausalPauseSafeNearbyAndExactlyOnce()
    {
        var s = Started();
        s.AdvanceWithoutSnapshot(2_399);
        Assert.AreEqual(EquipmentStage.Normal, s.CaptureEquipment()!.Stage);
        Execute(s, new SetPausedCommand(true)); var hash = s.CaptureSnapshot().AuthoritativeHash;
        s.AdvanceWithoutSnapshot(10_000); Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash);
        Execute(s, new SetPausedCommand(false)); s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(EquipmentStage.Warning, s.CaptureEquipment()!.Stage);
        Assert.IsTrue(Execute(s, new EquipmentCommand(EquipmentAction.Acknowledge)).IsAccepted);
        s.AdvanceWithoutSnapshot(3_600); s = Restore(s);
        Assert.AreEqual(EquipmentStage.DangerousFault, s.CaptureEquipment()!.Stage);
        Assert.AreEqual(0, s.CaptureLifecycleSnapshot()!.Casualties.Count);
        s.AdvanceWithoutSnapshot(1_199); s = Restore(s);
        Assert.AreEqual(0, s.CaptureLifecycleSnapshot()!.Casualties.Count);
        s.AdvanceWithoutSnapshot(1); s = Restore(s);
        var death = s.CaptureLifecycleSnapshot()!.Casualties.Single();
        Assert.AreEqual(7_200L, death.Tick);
        Assert.IsTrue(death.Cause.Contains("load 120%") && death.Cause.Contains("condition 30%") && death.Cause.Contains("warning tick 2400") && death.Cause.Contains("acknowledged True"));
        var victim = s.CapturePreparation()!.People.Single(item => item.Name == death.PersonId);
        var agent = s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == victim.AgentId);
        var e = s.CaptureEquipment()!;
        Assert.IsTrue(Math.Pow(agent.XMillimetres-e.XMillimetres,2)+Math.Pow(agent.ZMillimetres-e.ZMillimetres,2)<=16_000_000);
        Assert.AreEqual(1, s.CaptureLifecycleSnapshot()!.Hearings.Count);
        hash = s.CaptureSnapshot().AuthoritativeHash; s.AdvanceWithoutSnapshot(100_000);
        Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash);
        Assert.IsFalse(Execute(s, new SpendFixtureFavourCommand()).IsAccepted);
    }

    [TestMethod]
    public void LateMaintenanceDoesNotMagicallyCompleteAndCutoffCancelsOwnership()
    {
        var late = Started(worker: true); late.AdvanceWithoutSnapshot(6_900);
        Execute(late, new EquipmentCommand(EquipmentAction.DispatchMaintenance)); late.AdvanceWithoutSnapshot(301);
        Assert.AreEqual(PreparationStatus.Failed, late.PreparedStatus);
        Assert.IsTrue(late.CaptureLifecycleSnapshot()!.Casualties.Single().Cause.Contains("Maintenance dispatched"));
        var safe = Started(worker: true); safe.AdvanceWithoutSnapshot(6_900);
        Execute(safe, new EquipmentCommand(EquipmentAction.DispatchMaintenance)); safe.AdvanceWithoutSnapshot(100);
        Assert.IsTrue(Execute(safe, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
        Assert.AreEqual(MaintenanceStage.Cancelled, safe.CaptureEquipment()!.JobStage);
        safe = Restore(safe); safe.AdvanceWithoutSnapshot(2_000);
        Assert.AreEqual(0, safe.CaptureLifecycleSnapshot()!.Casualties.Count);
        Assert.IsFalse(Execute(safe, new EquipmentCommand(EquipmentAction.DispatchMaintenance)).IsAccepted);
    }

    [TestMethod]
    public void BaselineCutoffNeedsNoWorkerOrCashAndRemainsSafeAtLastResponseTick()
    {
        var s = Started(); s.AdvanceWithoutSnapshot(7_199);
        var cash = s.CaptureSnapshot().FestivalFinances.Single().CashPennies;
        Assert.IsTrue(Execute(s, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
        s = Restore(s); s.AdvanceWithoutSnapshot(200);
        Assert.AreEqual(0, s.CaptureLifecycleSnapshot()!.Casualties.Count);
        Assert.AreEqual(cash, s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        Assert.IsNull(s.CaptureEquipment()!.WorkerId);
        for (ulong seed = 0; seed < 100; seed++)
        {
            var prep = GameSession.CreateEquipmentCampaign(seed);
            var offers = prep.GetPreparationOffers();
            Assert.IsTrue(offers.Where(item => item.Id is "act.folk" or "staff.engineer" or "equipment.buy" or "contract.stock" or "maintenance.worker").Sum(item => item.PricePennies) < prep.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        }
    }

    [TestMethod]
    public void InvalidEquipmentOwnershipAndCausalStagesAreRejected()
    {
        var s = Started(worker: true); s.AdvanceWithoutSnapshot(2_400);
        var snapshot = s.CapturePersistenceSnapshot();
        foreach (var equipment in new[] { snapshot.Equipment! with { WorkerId = 999_999 }, snapshot.Equipment! with { WarningTick = 1 },
            snapshot.Equipment! with { Version = 1 }, snapshot.Equipment! with { XMillimetres = -5_750, ZMillimetres = 15_250 },
            snapshot.Equipment! with { LoadPercent = 0 }, snapshot.Equipment! with { Evidence = [] } })
            Assert.IsFalse(GameSession.Restore(snapshot with { Equipment = equipment }).IsSuccess);
    }

    [TestMethod]
    public void FortyThreePhysicalPeopleCompleteWithPaidWorkerAndNoDuplicateJob()
    {
        var s = Started(worker: true, tier: 2);
        s.AdvanceWithoutSnapshot(2_400);
        Execute(s, new EquipmentCommand(EquipmentAction.DispatchMaintenance));
        Assert.IsFalse(Execute(s, new EquipmentCommand(EquipmentAction.DispatchMaintenance)).IsAccepted);
        while (s.CaptureEquipment()!.JobStage == MaintenanceStage.Travelling) s.AdvanceWithoutSnapshot(1);
        s = Restore(s);
        var resumed = Restore(s);
        s.AdvanceWithoutSnapshot(40_000); resumed.AdvanceWithoutSnapshot(40_000);
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, resumed.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(45, s.CapturePreparation()!.People.Length);
        Assert.IsTrue(s.CapturePreparation()!.People.All(item => item.Admitted && item.Departed));
        Assert.AreEqual(0, s.CapturePreparation()!.WorkContracts.Length);
        Assert.IsTrue(s.CapturePreparation()!.Contacts.Contains("contact.morgan-finch"));
        Assert.AreEqual(1, s.CapturePreparation()!.Payments.Count(item => item.OfferId == "maintenance.worker"));
        Restore(s);
    }

    [TestMethod]
    public void RepairArrivalCompletionAndInterventionSaveFailuresRemainRetryable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "festival-repair-" + Guid.NewGuid());
        try
        {
            var s = Started(worker: true); s.AdvanceWithoutSnapshot(2_400);
            var hash = s.CaptureSnapshot().AuthoritativeHash;
            var failure = EquipmentCommandCoordinator.Execute(directory, s, new EquipmentCommand(EquipmentAction.DispatchMaintenance), Compatibility, DateTimeOffset.UtcNow, 1, _ => throw new IOException("injected"));
            Assert.IsFalse(failure.IsSuccess); Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash);
            Execute(s, new EquipmentCommand(EquipmentAction.DispatchMaintenance));
            while (!s.EquipmentBoundaryOnNextTick) s.AdvanceWithoutSnapshot(1);
            foreach (var boundary in new[] { "arrival", "repair" })
            {
                hash = s.CaptureSnapshot().AuthoritativeHash;
                failure = PreparationAdvanceCoordinator.AdvanceOne(directory, s, Compatibility, DateTimeOffset.UtcNow, s.CurrentTick, _ => throw new IOException("injected"));
                Assert.IsFalse(failure.IsSuccess); Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash);
                var success = PreparationAdvanceCoordinator.AdvanceOne(directory, s, Compatibility, DateTimeOffset.UtcNow, s.CurrentTick);
                Assert.IsTrue(success.IsSuccess, success.Error); s = success.Session;
                var loaded = AutosaveRotation.LoadNewestValid(directory, Compatibility);
                Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, loaded.Session!.CaptureSnapshot().AuthoritativeHash);
                if (boundary == "arrival") s.AdvanceWithoutSnapshot(GameSession.EquipmentRepairTicks - 1);
            }
            Assert.AreEqual(MaintenanceStage.Completed, s.CaptureEquipment()!.JobStage);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void EveryHazardBoundaryAndPaidCommandAreDurableOrRetainedOnFailure()
    {
        var directory = Path.Combine(Path.GetTempPath(), "festival-equipment-" + Guid.NewGuid());
        try
        {
            foreach (var tick in new[] { 2_399, 5_999, 7_199 })
            {
                var s = Started(); s.AdvanceWithoutSnapshot(tick);
                var hash = s.CaptureSnapshot().AuthoritativeHash;
                var failed = PreparationAdvanceCoordinator.AdvanceOne(directory, s, Compatibility, DateTimeOffset.UtcNow, tick,
                    _ => throw new IOException("injected"));
                Assert.IsFalse(failed.IsSuccess); Assert.AreSame(s, failed.Session);
                Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash);
                var success = PreparationAdvanceCoordinator.AdvanceOne(directory, s, Compatibility, DateTimeOffset.UtcNow, tick);
                Assert.IsTrue(success.IsSuccess, success.Error);
                var load = AutosaveRotation.LoadNewestValid(directory, Compatibility);
                Assert.IsTrue(load.IsSuccess, load.Error);
                Assert.AreEqual(success.Session.CaptureSnapshot().AuthoritativeHash, load.Session!.CaptureSnapshot().AuthoritativeHash);
            }
            var prep = GameSession.CreateEquipmentCampaign(2);
            var before = prep.CaptureSnapshot().AuthoritativeHash;
            var failedHire = EquipmentCommandCoordinator.Execute(directory, prep, new AcceptPreparationOfferCommand("maintenance.worker"), Compatibility, DateTimeOffset.UtcNow, 9_000, _ => throw new IOException("injected"));
            Assert.IsFalse(failedHire.IsSuccess); Assert.AreEqual(before, prep.CaptureSnapshot().AuthoritativeHash);
            var hire = EquipmentCommandCoordinator.Execute(directory, prep, new AcceptPreparationOfferCommand("maintenance.worker"), Compatibility, DateTimeOffset.UtcNow, 9_000);
            Assert.IsTrue(hire.IsSuccess, hire.Error);
            Assert.AreEqual(25, hire.Session.CapturePreparation()!.People.Length);
            Assert.IsFalse(Execute(hire.Session, new AcceptPreparationOfferCommand("maintenance.worker")).IsAccepted);
            Restore(hire.Session);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

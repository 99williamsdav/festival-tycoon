using System.Diagnostics;
using System.Reflection;
using Festival.Simulation;
using Festival.Persistence;

namespace Festival.Tests;

[TestClass]
public sealed class StaffFoundationsTests
{
    private static CommandResult Send(GameSession s, SessionCommand command) => s.Execute(new(new CommandId(s.NextSubmissionSequence + 1), s.CampaignId,
        s.Phase, s.CurrentTick, s.NextSubmissionSequence, null, LegacyInterventionFixture.For(s, command)));
    private static void Accept(GameSession s, SessionCommand command) { var result = Send(s, command); Assert.IsTrue(result.IsAccepted, result.Message); }
    private static GameSession Restore(GameSession s)
    {
        var loaded = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(loaded.IsSuccess, loaded.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, loaded.Session!.CaptureSnapshot().AuthoritativeHash);
        return loaded.Session;
    }
    private static GameSession Hired(bool train = false, ulong seed = 20260926)
    {
        var s = GameSession.CreateDisorderCampaign(seed);
        Accept(s, new ApplyStaffFoundationEffectCommand("staff.medic-slot"));
        Accept(s, new ApplyStaffFoundationEffectCommand("staff.steward-slot"));
        if (train) Accept(s, new ApplyStaffFoundationEffectCommand("staff.role-training"));
        foreach (var id in new[] { "staff.extra-medic", "staff.extra-steward", "maintenance.worker" }) Accept(s, new AcceptPreparationOfferCommand(id));
        return Restore(s);
    }
    private static GameSession Started(bool train = false, ulong seed = 20260926)
    {
        var s = Hired(train, seed);
        foreach (var id in new[] { "act.folk", "staff.steward", "equipment.buy" }) Accept(s, new AcceptPreparationOfferCommand(id));
        Accept(s, new StartPreparedEditionCommand());
        Accept(s, new MedicalCommand(s.CaptureMedical()!.AtRiskGuestId, MedicalAction.GuideToRest));
        while (!s.CapturePreparation()!.People.All(item => item.Admitted) && s.CurrentTick < 4000) s.AdvanceWithoutSnapshot(1);
        Assert.IsTrue(s.CapturePreparation()!.People.All(item => item.Admitted));
        return Restore(s);
    }
    private static void SetMedical(GameSession s, MedicalSnapshot m) => typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s, m);
    private static string? SemanticError(SessionPersistenceSnapshot snapshot) => (string?)typeof(GameSession)
        .GetMethod("ValidatePersistenceSnapshot", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [snapshot]);

    [TestMethod]
    public void SlotsAreNotLabourAndPaidHiresAreAtomicAndSeedStable()
    {
        var s = GameSession.CreateDisorderCampaign(17);
        var cash = s.CaptureSnapshot().FestivalFinances.Single().CashPennies;
        Assert.IsFalse(Send(s, new AcceptPreparationOfferCommand("staff.extra-medic")).IsAccepted);
        Assert.AreEqual(26, s.CapturePreparation()!.People.Length);
        Accept(s, new ApplyStaffFoundationEffectCommand("staff.medic-slot"));
        Assert.AreEqual(26, s.CapturePreparation()!.People.Length);
        Assert.AreEqual(cash, s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        Assert.IsFalse(Send(s, new ApplyStaffFoundationEffectCommand("staff.medic-slot")).IsAccepted);
        Assert.IsFalse(Send(s, new AcceptPreparationOfferCommand("staff.extra-steward")).IsAccepted);
        Accept(s, new AcceptPreparationOfferCommand("staff.extra-medic"));
        Assert.AreEqual(cash - 3000, s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        var profile = s.GetResponseStaff().Single(item => item.Name == "Avery Brooks");
        Assert.IsTrue(profile.TreatmentTicks is 360 or 480 or 600);
        Assert.IsFalse(Send(s, new AcceptPreparationOfferCommand("staff.extra-medic")).IsAccepted);
        Assert.AreEqual(1, s.CapturePreparation()!.Payments.Length);
        Assert.AreEqual(0L, s.GetPreparationLedgerEntries().Sum(item => item.AmountPennies));
        s = Restore(s);
        Assert.AreEqual(profile, s.GetResponseStaff().Single(item => item.Name == "Avery Brooks"));
    }

    [TestMethod]
    public void AllHireOrdersKeepDistinctStableIdsAndRestore()
    {
        foreach (var order in new[] { new[] { "maintenance.worker", "staff.extra-medic", "staff.extra-steward" },
            new[] { "staff.extra-medic", "maintenance.worker", "staff.extra-steward" }, new[] { "staff.extra-steward", "staff.extra-medic", "maintenance.worker" } })
        {
            var s = GameSession.CreateDisorderCampaign(17);
            Accept(s, new ApplyStaffFoundationEffectCommand("staff.medic-slot"));
            Accept(s, new ApplyStaffFoundationEffectCommand("staff.steward-slot"));
            foreach (var id in order) { Accept(s, new AcceptPreparationOfferCommand(id)); s = Restore(s); }
            Assert.AreEqual(29, s.CapturePreparation()!.People.Length);
            Assert.AreEqual(29, s.CapturePreparation()!.People.Select(item => item.AgentId).Distinct().Count());
        }
    }

    [TestMethod]
    public void RoleWideTrainingIsFreeClampedAndAppliesToActualRoutes()
    {
        var before = Hired(); var after = Hired(true);
        Assert.AreEqual(before.CaptureSnapshot().FestivalFinances.Single().CashPennies, after.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        foreach (var p in before.GetResponseStaff())
        {
            var trained = after.GetResponseStaff().Single(item => item.AgentId == p.AgentId);
            Assert.AreEqual(Math.Min(1150, p.WalkingSpeedPermille + 100), trained.WalkingSpeedPermille);
            if (p.Role == ResponseRole.Medic) Assert.AreEqual(Math.Max(360, p.TreatmentTicks - 120), trained.TreatmentTicks);
            else Assert.AreEqual(Math.Min(8000, p.CalmingSkill + 500), trained.CalmingSkill);
        }
        var live = Started(true);
        foreach (var p in live.GetResponseStaff()) Assert.AreEqual(p.WalkingSpeedPermille, live.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == p.AgentId).WalkingSpeedPermille);
        Assert.IsFalse(Send(live, new ApplyStaffFoundationEffectCommand("staff.role-training")).IsAccepted);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TwoMedicsTravelAndTreatIndependentPatientsAcrossReload(bool trained)
    {
        var s = Started(trained);
        var ids = s.CapturePreparation()!.People.Where(item => item.Role == ProtectedPersonRole.Guest).Take(2).Select(item => item.AgentId).ToArray();
        var m = s.CaptureMedical()!;
        SetMedical(s, m with { Needs = m.Needs.Select(item => ids.Contains(item.AgentId) ? item with { Stage = MedicalStage.Distress, WarningTick = s.CurrentTick } : item).ToArray() });
        var workers = s.GetResponseStaff().Where(item => item.Role == ResponseRole.Medic).ToArray();
        Accept(s, new MedicalCommand(ids[0], MedicalAction.DispatchMedic, workers[0].AgentId));
        Assert.IsFalse(Send(s, new MedicalCommand(ids[0], MedicalAction.DispatchMedic, workers[1].AgentId)).IsAccepted);
        Assert.IsFalse(Send(s, new MedicalCommand(ids[1], MedicalAction.DispatchMedic, workers[0].AgentId)).IsAccepted);
        Accept(s, new MedicalCommand(ids[1], MedicalAction.DispatchMedic, workers[1].AgentId));
        Assert.AreEqual(2, s.GetMedicResponses().Count(item => item.Stage == MedicalResponseStage.Travelling));
        s = Restore(s);
        var clone = Restore(s); var seen = new HashSet<ulong>(); var completed = new HashSet<ulong>();
        for (var tick = 0; tick < 5000 && s.GetMedicResponses().Any(item => item.Stage != MedicalResponseStage.Completed); tick++)
        {
            s.AdvanceWithoutSnapshot(1); clone.AdvanceWithoutSnapshot(1);
            foreach (var job in s.GetMedicResponses().Where(item => item.Stage == MedicalResponseStage.Treating))
                if (seen.Add(job.WorkerId)) s = Restore(s);
            foreach (var job in s.GetMedicResponses().Where(item => item.Stage == MedicalResponseStage.Completed))
                if (completed.Add(job.WorkerId)) Assert.AreEqual(s.GetResponseStaff().Single(item => item.AgentId == job.WorkerId).TreatmentTicks, (int)(s.CurrentTick - job.StartedTick));
        }
        Assert.AreEqual(2, seen.Count);
        Assert.AreEqual(2, s.GetMedicResponses().Count(item => item.Stage == MedicalResponseStage.Completed));
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, clone.CaptureSnapshot().AuthoritativeHash);
        foreach (var id in ids) Assert.AreEqual(MedicalStage.Treated, s.CaptureMedical()!.Needs.Single(item => item.AgentId == id).Stage);
        Restore(s);
    }

    [TestMethod]
    public void RetryExpiresBothPaidContractsButRetainsEffectsAndContacts()
    {
        var s = Hired(true);
        foreach (var id in new[] { "act.folk", "staff.steward" }) Accept(s, new AcceptPreparationOfferCommand(id));
        Accept(s, new StartPreparedEditionCommand());
        s.AdvanceWithoutSnapshot(7200);
        Assert.AreEqual(PreparationStatus.Failed, s.PreparedStatus);
        var profiles = s.CapturePreparation()!.StaffProfiles;
        Assert.AreEqual(0, s.CapturePreparation()!.WorkContracts.Length);
        s = Restore(s); Accept(s, new SpendCouncilFavourCommand()); s = Restore(s);
        Assert.AreEqual(26, s.CapturePreparation()!.People.Length);
        Assert.AreEqual(2, s.GetResponseStaff().Count);
        Assert.IsTrue(s.CapturePreparation()!.RespondersUpgraded);
        Assert.IsTrue(s.CapturePreparation()!.ExtraMedicSlotOwned && s.CapturePreparation()!.ExtraStewardSlotOwned);
        CollectionAssert.AreEqual(profiles, s.CapturePreparation()!.StaffProfiles);
        foreach (var id in new[] { "staff.extra-steward", "maintenance.worker", "staff.extra-medic" }) Accept(s, new AcceptPreparationOfferCommand(id));
        CollectionAssert.AreEqual(profiles, Restore(s).CapturePreparation()!.StaffProfiles);
        Assert.AreEqual(72500L, s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
    }

    [TestMethod]
    public void ProfileAndJobTamperingAreRejected()
    {
        var s = Hired(); var snap = s.CapturePersistenceSnapshot(); var p = snap.Preparation!;
        Assert.IsNotNull(SemanticError(snap with { Preparation = p with { StaffProfiles = p.StaffProfiles.Select(item => item with { WalkingSpeedPermille = 999 }).ToArray() } }));
        Assert.IsNotNull(SemanticError(snap with { Preparation = p with { ExtraMedicSlotOwned = false } }));
        Assert.IsNotNull(SemanticError(snap with { Medical = snap.Medical! with { ExtraResponses = [] } }));
        var live = Started(); var liveSnap = live.CapturePersistenceSnapshot(); var medic = liveSnap.Medical!;
        Assert.IsNull(SemanticError(liveSnap));
        Assert.IsNotNull(SemanticError(liveSnap with { Medical = medic with { ExtraResponses = medic.ExtraResponses.Select(item => item with {
            Stage = MedicalResponseStage.Removing, PatientId = medic.Needs[0].AgentId }).ToArray() } }));
        Assert.IsNotNull(SemanticError(liveSnap with { Medical = medic with { ExtraResponses = medic.ExtraResponses.Select(item => item with {
            Stage = MedicalResponseStage.Travelling, PatientId = medic.Needs[0].AgentId }).ToArray() } }));
    }

    [TestMethod]
    public void TwoStewardsOwnIndependentPhysicalResponsesAndRestore()
    {
        var s = Started();
        while (s.CaptureLivePerformance()!.Stage != LiveSetStage.Live && s.CurrentTick < 4000) s.AdvanceWithoutSnapshot(1);
        Accept(s, new EquipmentCommand(EquipmentAction.Isolate));
        while (s.CaptureDisorder()!.People.Count(item => item.Stage == DisorderStage.Complaint) < 2 && s.CurrentTick < 5000) s.AdvanceWithoutSnapshot(1);
        var ids = s.CaptureDisorder()!.People.Where(item => item.Stage == DisorderStage.Complaint).Take(2).Select(item => item.AgentId).ToArray();
        Assert.AreEqual(2, ids.Length);
        var workers = s.GetResponseStaff().Where(item => item.Role == ResponseRole.Steward).ToArray();
        Accept(s, new MedicalCommand(workers[1].AgentId, MedicalAction.GuideToWater));
        Assert.AreEqual(MedicalIntent.SeekWater, s.CaptureMedical()!.Needs.Single(item => item.AgentId == workers[1].AgentId).Intent);
        Accept(s, new DisorderCommand(DisorderAction.DispatchSecurity, ids[0], workers[0].AgentId));
        Assert.IsFalse(Send(s, new DisorderCommand(DisorderAction.DispatchSecurity, ids[0], workers[1].AgentId)).IsAccepted);
        Assert.IsFalse(Send(s, new DisorderCommand(DisorderAction.DispatchSecurity, ids[1], workers[0].AgentId)).IsAccepted);
        Accept(s, new DisorderCommand(DisorderAction.DispatchSecurity, ids[1], workers[1].AgentId));
        Assert.AreEqual(MedicalIntent.WatchShow, s.CaptureMedical()!.Needs.Single(item => item.AgentId == workers[1].AgentId).Intent);
        Assert.IsFalse(s.CaptureWaterPoints().Any(point => point.Queue.Concat(point.Overflow).Contains(workers[1].AgentId)));
        var interrupted = Send(s, new MedicalCommand(workers[1].AgentId, MedicalAction.GuideToWater));
        Assert.IsFalse(interrupted.IsAccepted); StringAssert.Contains(interrupted.Message, "owns a response");
        Assert.AreEqual(2, s.GetStewardResponses().Count(item => item.Stage == SecurityResponseStage.Travelling));
        s = Restore(s); var clone = Restore(s); var seen = new HashSet<ulong>();
        for (var tick = 0; tick < 2400 && seen.Count < 2; tick++)
        {
            s.AdvanceWithoutSnapshot(1); clone.AdvanceWithoutSnapshot(1);
            foreach (var job in s.GetStewardResponses().Where(item => item.Stage == SecurityResponseStage.Calming))
                if (seen.Add(job.WorkerId)) s = Restore(s);
        }
        Assert.AreEqual(2, seen.Count);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, clone.CaptureSnapshot().AuthoritativeHash);
    }

    // Explicit stress fixture: an already-confronting worker beside a maximally angry guest.
    // Normal production eligibility and injury/deadline processing execute from here.
    private static GameSession OptionalStewardInjuryFixture(ulong seed = 20260926, bool calming = false)
    {
        var s = Started(seed: seed);
        while (s.CaptureLivePerformance()!.Stage != LiveSetStage.Live && s.CurrentTick < 4000) s.AdvanceWithoutSnapshot(1);
        Accept(s, new EquipmentCommand(EquipmentAction.Isolate));
        s.AdvanceWithoutSnapshot(8);
        var d = s.CaptureDisorder()!;
        var target = s.CaptureLivePerformance()!.Listeners.First(item => item.AtPlace && item.Enthusiasm >= 65).AgentId;
        var worker = s.GetResponseStaff().Single(item => item.Name == "Sam Ellis").AgentId;
        var targetNav = s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == target);
        var targetCell = TraversalGrid.WorldToCell(targetNav.XMillimetres, targetNav.ZMillimetres);
        var agents = (System.Collections.IDictionary)typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(s)!;
        foreach (var (id, cell) in new[] { (worker, new GridCell(targetCell.X + 2, targetCell.Z)) })
        {
            var agent = agents[new EntityId(id)]!; var centre = TraversalGrid.CellCentre(cell);
            foreach (var (name, value) in new[] { ("XMillimetres", centre.XMillimetres), ("ZMillimetres", centre.ZMillimetres),
                ("SegmentOriginXMillimetres", centre.XMillimetres), ("SegmentOriginZMillimetres", centre.ZMillimetres) }) agent.GetType().GetProperty(name)!.SetValue(agent, value);
            typeof(GameSession).GetMethod("ApplyAgentDestination", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s,
                [new EntityId(id), new SetAgentDestinationCommand(cell, "staff.labelled-stress-fixture"), false]);
        }
        typeof(GameSession).GetField("_disorder", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s, d with {
            People = d.People.Select(item => item.AgentId == target ? item with { Temperament = calming ? 2000 : 8000, Pressure = calming ? 6500 : 10000,
                Grievance = DisorderGrievance.MusicCutoff, GrievanceTick = s.CurrentTick, Stage = DisorderStage.Argument, StageTick = s.CurrentTick, OpponentId = worker } : item).ToArray(),
            ExtraResponses = [new(worker, calming ? SecurityResponseStage.Calming : SecurityResponseStage.Confronting, target,
                s.CurrentTick - (calming ? 240 : 160), false, "Labelled stress fixture response", s.CurrentTick - (calming ? 240 : 160))] });
        s = Restore(s);
        if (calming) { s.AdvanceWithoutSnapshot(8); return Restore(s); }
        for (var tick = 0; tick < 1000 && !s.GetStewardResponses().Single(item => item.WorkerId == worker).Incapacitated; tick++) s.AdvanceWithoutSnapshot(1);
        Assert.IsTrue(s.GetStewardResponses().Single(item => item.WorkerId == worker).Incapacitated,
            System.Text.Json.JsonSerializer.Serialize(new { Jobs = s.GetStewardResponses(), Incidents = s.CaptureDisorder()!.Incidents, Evidence = s.CaptureDisorder()!.Evidence.TakeLast(6) }));
        return Restore(s);
    }

    [TestMethod]
    public void OptionalStewardIsProtectedAndTimelyFirstAidPreventsItsDeath()
    {
        var failed = OptionalStewardInjuryFixture(); var safe = Restore(failed);
        var worker = failed.GetResponseStaff().Single(item => item.Name == "Sam Ellis").AgentId;
        var collapse = failed.CaptureMedical()!.Needs.Single(item => item.AgentId == worker).CollapseTick;
        Accept(safe, new MedicalCommand(worker, MedicalAction.DispatchMedic));
        failed.AdvanceWithoutSnapshot((int)(collapse + GameSession.DisorderInjuryDeathTicks - failed.CurrentTick));
        Assert.AreEqual(PreparationStatus.Failed, failed.PreparedStatus);
        Assert.AreEqual("Sam Ellis", failed.CaptureLifecycleSnapshot()!.Casualties.Single().PersonId);
        Restore(failed);
        safe.AdvanceWithoutSnapshot(2400);
        Assert.AreEqual(MedicalStage.Treated, safe.CaptureMedical()!.Needs.Single(item => item.AgentId == worker).Stage);
        Assert.IsFalse(safe.GetStewardResponses().Single(item => item.WorkerId == worker).Incapacitated);
        Assert.AreEqual(PreparationStatus.Running, safe.PreparedStatus);
        Restore(safe);
        StringAssert.Contains(failed.CaptureLifecycleSnapshot()!.Casualties.Single().Cause, "Sam Ellis");
        StringAssert.Contains(failed.CaptureLifecycleSnapshot()!.Casualties.Single().Cause, "dispatch tick");
    }

    [TestMethod]
    public void UnaffordableAndFiftyBoundaryHiresDoNotMutateRosterOrLedger()
    {
        var s = GameSession.CreateDisorderCampaign(17);
        Accept(s, new ApplyStaffFoundationEffectCommand("staff.medic-slot"));
        var finances = (System.Collections.IDictionary)typeof(GameSession).GetField("_festivalFinances", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(s)!;
        var finance = finances[new EntityId(s.CapturePreparation()!.FinanceOwnerId)]!;
        finance.GetType().GetProperty("CashPennies")!.SetValue(finance, 2999L);
        Assert.IsFalse(Send(s, new AcceptPreparationOfferCommand("staff.extra-medic")).IsAccepted);
        Assert.AreEqual(2999L, s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        Assert.AreEqual(0, s.CapturePreparation()!.Payments.Length);
        Assert.AreEqual(26, s.CapturePreparation()!.People.Length);
        finance.GetType().GetProperty("CashPennies")!.SetValue(finance, 80000L);
        var p = s.CapturePreparation()!;
        // Deliberately overfilled command-boundary fixture, not a valid saved roster.
        typeof(GameSession).GetField("_preparation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s,
            p with { People = p.People.Concat(Enumerable.Range(0, 24).Select(index => new EditionPerson((ulong)(100 + index), "Capacity probe", ProtectedPersonRole.Staff, 0))).ToArray() });
        Assert.IsFalse(Send(s, new AcceptPreparationOfferCommand("staff.extra-medic")).IsAccepted);
        Assert.AreEqual(80000L, s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        Assert.AreEqual(0, s.CapturePreparation()!.Payments.Length);
        Assert.AreEqual(50, s.CapturePreparation()!.People.Length);
    }

    [TestMethod]
    public void ProportionalExistingTierTwoDiagnosticStaysBelowFifty()
    {
        // Measurement of the already-existing tier-two factory only; no tier progression or reward work.
        var s = GameSession.CreateDisorderCampaign(20260926, 2);
        foreach (var effect in new[] { "staff.medic-slot", "staff.steward-slot", "staff.role-training" }) Accept(s, new ApplyStaffFoundationEffectCommand(effect));
        foreach (var offer in new[] { "staff.extra-medic", "staff.extra-steward", "maintenance.worker", "act.folk", "staff.steward", "equipment.buy" }) Accept(s, new AcceptPreparationOfferCommand(offer));
        Assert.AreEqual(49, s.CapturePreparation()!.People.Length);
        Accept(s, new StartPreparedEditionCommand());
        Accept(s, new MedicalCommand(s.CaptureMedical()!.AtRiskGuestId, MedicalAction.GuideToRest));
        var watch = Stopwatch.StartNew(); s.AdvanceWithoutSnapshot(4000); watch.Stop();
        Assert.AreEqual(49, s.CaptureSnapshot().NavigationAgents.Count);
        Assert.AreEqual(PreparationStatus.Running, s.PreparedStatus);
        Console.WriteLine($"STAFF_HEADLESS_DIAGNOSTIC people=49 ticks=4000 elapsed_ms={watch.Elapsed.TotalMilliseconds:F2} simulated_real_seconds=50");
        Restore(s);
    }

    [TestMethod]
    public void StrongAndWeakOptionalStewardSkillsChangeCalmingOutcome()
    {
        ulong weakSeed = 0, strongSeed = 0;
        for (ulong seed = 1; seed < 200 && (weakSeed == 0 || strongSeed == 0); seed++)
        {
            var profile = GameSession.CreateDisorderCampaign(seed).GetOptionalStaffOfferProfile(ResponseRole.Steward)!;
            if (profile.CalmingSkill <= 4800) weakSeed = seed;
            if (profile.CalmingSkill >= 7500) strongSeed = seed;
        }
        Assert.IsTrue(weakSeed != 0 && strongSeed != 0);
        var weak = OptionalStewardInjuryFixture(weakSeed, calming: true);
        var strong = OptionalStewardInjuryFixture(strongSeed, calming: true);
        Assert.AreEqual(SecurityResponseStage.Confronting, weak.CaptureDisorder()!.ExtraResponses.Single().Stage);
        Assert.AreEqual(SecurityResponseStage.Completed, strong.CaptureDisorder()!.ExtraResponses.Single().Stage);
    }

    [TestMethod]
    public void HigherPersonalSpeedArrivesEarlierOnTheSamePhysicalRoute()
    {
        int ArrivalTicks(bool trained)
        {
            var s = Hired(trained);
            foreach (var id in new[] { "act.folk", "staff.steward" }) Accept(s, new AcceptPreparationOfferCommand(id));
            Accept(s, new StartPreparedEditionCommand());
            var worker = s.GetResponseStaff().Single(item => item.Name == "Sam Ellis");
            var agents = (System.Collections.IDictionary)typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(s)!;
            var agent = agents[new EntityId(worker.AgentId)]!;
            var advance = typeof(GameSession).GetMethod("AdvanceAgentOneTick", BindingFlags.Instance | BindingFlags.NonPublic)!;
            // Exact production route integrator, isolated from unrelated occupancy scheduling.
            for (var ticks = 1; ticks < 4000; ticks++) if ((bool)advance.Invoke(s, [agent])!) return ticks;
            Assert.Fail("Staff route did not finish."); return 0;
        }
        var ordinary = ArrivalTicks(false); var trained = ArrivalTicks(true);
        Assert.IsTrue(trained < ordinary, $"ordinary={ordinary}; trained={trained}");
    }

    [TestMethod]
    public void NamedUnreachableDispatchIsRejectedWithReasonAndNoOwnership()
    {
        var s = Started();
        var m = s.CaptureMedical()!; var id = m.Needs.First().AgentId;
        SetMedical(s, m with { Needs = m.Needs.Select(item => item.AgentId == id ? item with { Stage = MedicalStage.Distress } : item).ToArray() });
        var cells = s.CaptureSnapshot().NavigationAgents.Select(item => TraversalGrid.WorldToCell(item.XMillimetres, item.ZMillimetres)).ToHashSet();
        // A blocked finite map is a labelled route-failure fixture, not production terrain.
        var overrides = new List<TerrainCellOverride>();
        for (var z = 0; z < TraversalGrid.Depth; z++) for (var x = 0; x < TraversalGrid.Width; x++)
            overrides.Add(new(new GridCell(x, z), GroundSurface.Grass, cells.Contains(new GridCell(x, z))));
        typeof(GameSession).GetField("_traversalGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s, new TraversalGrid(overrides));
        var worker = s.GetResponseStaff().Single(item => item.Name == "Avery Brooks").AgentId;
        var rejected = Send(s, new MedicalCommand(id, MedicalAction.DispatchMedic, worker));
        Assert.IsFalse(rejected.IsAccepted); StringAssert.Contains(rejected.Message, "reach");
        Assert.AreEqual(MedicalResponseStage.None, s.CaptureMedical()!.ExtraResponses.Single().Stage);
        var d = s.CaptureDisorder()!;
        typeof(GameSession).GetField("_disorder", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s, d with {
            People = d.People.Select(item => item.AgentId == id ? item with { Stage = DisorderStage.Complaint, Pressure = 2500,
                Grievance = DisorderGrievance.MusicCutoff, StageTick = s.CurrentTick, GrievanceTick = s.CurrentTick } : item).ToArray() });
        var steward = s.GetResponseStaff().Single(item => item.Name == "Sam Ellis").AgentId;
        rejected = Send(s, new DisorderCommand(DisorderAction.DispatchSecurity, id, steward));
        Assert.IsFalse(rejected.IsAccepted); StringAssert.Contains(rejected.Message, "reach");
        Assert.AreEqual(SecurityResponseStage.None, s.CaptureDisorder()!.ExtraResponses.Single().Stage);
    }

    [TestMethod]
    public void LateOptionalMedicCannotEraseDeathAndIsNamedInHearing()
    {
        var s = Hired(true);
        foreach (var id in new[] { "act.folk", "staff.steward", "equipment.buy" }) Accept(s, new AcceptPreparationOfferCommand(id));
        Accept(s, new StartPreparedEditionCommand());
        while (s.CaptureMedical()!.Stage != MedicalStage.Critical && s.CurrentTick < 6200) s.AdvanceWithoutSnapshot(1);
        var m = s.CaptureMedical()!;
        s.AdvanceWithoutSnapshot((int)(m.CollapseTick + GameSession.MedicalDeathDelayTicks - s.CurrentTick - 1));
        var worker = s.GetResponseStaff().Single(item => item.Name == "Avery Brooks").AgentId;
        Accept(s, new MedicalCommand(m.AtRiskGuestId, MedicalAction.DispatchMedic, worker));
        s = Restore(s); s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(PreparationStatus.Failed, s.PreparedStatus);
        var cause = s.CaptureLifecycleSnapshot()!.Casualties.Single().Cause;
        StringAssert.Contains(cause, "Avery Brooks→Guest 20 Travelling");
        StringAssert.Contains(cause, "dispatch tick"); Restore(s);
    }

    [TestMethod]
    public void SafeSettlementExpiresOptionalContractsWithoutDeletingContacts()
    {
        var s = Started(true);
        // Settlement-focused fixture suppression, matching the existing R0.05 claim test.
        // Real incident prevention and optional-steward mortality are tested separately.
        while (s.CurrentTick < 40000 && s.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing)
        {
            var m = s.CaptureMedical()!;
            SetMedical(s, m with { Needs = m.Needs.Select(item => item with { Thirst = Math.Min(item.Thirst, 1000), HeatExposure = Math.Min(item.HeatExposure, 1000) }).ToArray() });
            s.AdvanceWithoutSnapshot(500);
        }
        for (var ticks = 0; ticks < 5000 && s.PreparedStatus == PreparationStatus.Departing; ticks++) s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus);
        Assert.AreEqual(0, s.CapturePreparation()!.WorkContracts.Length);
        Assert.AreEqual(2, s.CapturePreparation()!.StaffProfiles.Length);
        Assert.IsTrue(s.CapturePreparation()!.Contacts.Contains("contact.avery-brooks"));
        Restore(s);
    }

    [TestMethod]
    public void PreviouslyReviewedWaterSaveLoadsWithoutGrantingFreeStaff()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../reports/evidence/R0.05a/player-placement/same-id-layout-b.ftsave"));
        var loaded = SaveFileAdapter.LoadFile(path, new SaveCompatibility("0.0.1-r0.05-hearing-v1", LowerWitteringFarmScenario.ContentCompatibilityHash, "r0-disorder-layout-v13"));
        Assert.IsTrue(loaded.IsSuccess, loaded.Error);
        var p = loaded.Session!.CapturePreparation()!;
        Assert.IsFalse(loaded.Session.CaptureMedical()!.DevelopmentInterventionFixturesEnabled);
        Assert.IsFalse(p.ExtraMedicSlotOwned || p.ExtraStewardSlotOwned || p.RespondersUpgraded);
        Assert.AreEqual(0, p.StaffProfiles.Length);
        Assert.IsFalse(p.WorkContracts.Contains("staff.extra-medic") || p.WorkContracts.Contains("staff.extra-steward"));
    }
}

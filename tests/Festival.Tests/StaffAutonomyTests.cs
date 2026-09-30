using Festival.Simulation;
using Festival.Persistence;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class StaffAutonomyTests
{
    private static CommandResult Send(GameSession s, SessionCommand c) => s.Execute(new(new(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase, s.CurrentTick, s.NextSubmissionSequence, null, c));
    private static void Accept(GameSession s, SessionCommand c) { var result = Send(s, c); Assert.IsTrue(result.IsAccepted, result.Message); }
    private static void Set<T>(GameSession s, string field, T value) => SetMember(typeof(GameSession), field, BindingFlags.Instance | BindingFlags.NonPublic, s, value);
    private static GameSession Restore(GameSession s)
    {
        var loaded = GameSession.Restore(s.CapturePersistenceSnapshot()); Assert.IsTrue(loaded.IsSuccess, loaded.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, loaded.Session!.CaptureSnapshot().AuthoritativeHash); return loaded.Session;
    }
    private static GameSession Ready(bool extra = false)
    {
        // The extra medic slot comes from drafting Doctor's Orders; the medic is still hired for £30.
        var s = extra ? BuildSession.PlannedWith("doctors-orders") : BuildSession.Planned();
        if (extra) Accept(s, new AcceptPreparationOfferCommand("staff.extra-medic"));
        Accept(s, new AcceptPreparationOfferCommand("staff.steward"));
        return s;
    }
    private static GameSession Started(bool extra = false)
    {
        var s = Ready(extra); Accept(s, new StartPreparedEditionCommand()); s.AdvanceWithoutSnapshot(1500);
        while (!s.CapturePreparation()!.People.Where(person => person.Role != ProtectedPersonRole.Performer).All(person => person.Admitted) && s.CurrentTick < 12000)
            s.AdvanceWithoutSnapshot(80);
        Assert.AreEqual(PreparationStatus.Running, s.PreparedStatus);
        Assert.IsTrue(s.CapturePreparation()!.People.Where(person => person.Role != ProtectedPersonRole.Performer).All(person => person.Admitted)); return Restore(s);
    }
    private static ulong[] Guests(GameSession s) => s.CapturePreparation()!.People.Where(person => person.Role == ProtectedPersonRole.Guest && person.AgentId != BuildSession.LastGuest(s)).Take(4).Select(person => person.AgentId).ToArray();
    private static void Incidents(GameSession s, params (ulong Id, MedicalStage Stage, long Collapse)[] incidents)
    {
        var m = s.CaptureMedical()!;
        Set(s, "MedicalView", m with { Needs = m.Needs.Select(need => incidents.FirstOrDefault(item => item.Id == need.AgentId) is { Id: > 0 } incident
            ? need with { Stage = incident.Stage, CollapseTick = incident.Collapse, WarningTick = Math.Max(0, incident.Collapse - GameSession.MedicalCollapseDelayTicks),
                CriticalTick = incident.Stage == MedicalStage.Critical ? incident.Collapse + GameSession.MedicalCriticalDelayTicks : -1, Intent = MedicalIntent.Collapsed }
            : need).ToArray() });
    }
    private static void PositionFixture(GameSession s, ulong id, GridCell cell)
    {
        var agents = (System.Collections.IDictionary)typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(s)!;
        var nav = agents[new EntityId(id)]!; var centre = TraversalGrid.CellCentre(cell);
        void Property(string name, object? value) => nav.GetType().GetProperty(name)!.SetValue(nav, value);
        Property("XMillimetres", centre.XMillimetres); Property("ZMillimetres", centre.ZMillimetres);
        Property("SegmentOriginXMillimetres", centre.XMillimetres); Property("SegmentOriginZMillimetres", centre.ZMillimetres);
        Property("SegmentProgressMicrometres", 0); Property("RouteIndex", 0); Property("MovementRemainder", 0);
        Property("Route", new List<GridCell>()); Property("Action", AgentNavigationAction.Arrived); Property("Destination", cell);
    }
    private static (GameSession Session, ulong A, ulong B) FightFixture(bool success, bool extra = false, int age = 0)
    {
        var s = Started(extra); var ids = Guests(s); var d = s.CaptureDisorder()!;
        PositionFixture(s, d.SecurityId, new(119, 178)); PositionFixture(s, ids[0], new(121, 178)); PositionFixture(s, ids[1], new(121, 179));
        Set(s, "DisorderView", d with { ConfrontationSkill = 8000, People = d.People.Select(person => ids.Take(2).Contains(person.AgentId)
            ? person with { Stage = DisorderStage.Argument, Pressure = success ? 4000 : 10000, Temperament = success ? 2000 : 8000,
                Grievance = DisorderGrievance.MusicCutoff, GrievanceTick = s.CurrentTick - age, StageTick = s.CurrentTick - age }
            : person).ToArray() });
        typeof(GameSession).GetMethod("BeginDisorderFight", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, [ids[0], ids[1], "labelled:test-fight"]);
        if (age > 0)
        {
            d = s.CaptureDisorder()!;
            Set(s, "DisorderView", d with { People = d.People.Select(person => ids.Take(2).Contains(person.AgentId) ? person with { StageTick = s.CurrentTick - age } : person).ToArray(),
                Incidents = d.Incidents.Select(origin => origin with { FightTick = s.CurrentTick - age }).ToArray() });
        }
        return (Restore(s), ids[0], ids[1]);
    }
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void GuestFightPhysicalHandlingRollsOnceAndSavedContinuationPreservesOriginalDeadline(bool success)
    {
        var (s, a, b) = FightFixture(success); var fightTick = s.CaptureDisorder()!.Incidents.Single().FightTick;
        s.AdvanceWithoutSnapshot(1); Assert.AreEqual(SecurityResponseStage.Travelling, s.GetStewardResponses().Single().Stage);
        Assert.IsNull(s.CaptureDisorder()!.Incidents.Single().HandlingAttempt);
        Assert.AreEqual(DisorderStage.Fight, s.CaptureDisorder()!.People.Single(person => person.AgentId == a).Stage);
        for (var tick = 0; tick < 400 && s.CaptureDisorder()!.Incidents.Single().HandlingAttempt is null; tick++) s.AdvanceWithoutSnapshot(1);
        var origin = s.CaptureDisorder()!.Incidents.Single(); Assert.IsNotNull(origin.HandlingAttempt);
        Assert.AreEqual(FightHandlingOutcome.Handling, origin.HandlingAttempt.Outcome);
        Assert.AreEqual(fightTick, origin.FightTick);
        Assert.IsFalse(Send(s, new DisorderCommand(DisorderAction.DispatchSecurity, b)).IsAccepted);
        var clone = Restore(s);
        for (var tick = 0; tick < 168; tick++) { s.AdvanceWithoutSnapshot(1); clone.AdvanceWithoutSnapshot(1); }
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, clone.CaptureSnapshot().AuthoritativeHash);
        origin = s.CaptureDisorder()!.Incidents.Single(); Assert.AreEqual(success ? FightHandlingOutcome.Succeeded : FightHandlingOutcome.Failed, origin.HandlingAttempt!.Outcome);
        Assert.AreEqual(fightTick, origin.FightTick);
        Assert.AreEqual(success ? -2 : -1, origin.InjuryTick);
        if (!success)
        {
            Assert.IsFalse(Send(s, new DisorderCommand(DisorderAction.DispatchSecurity, a)).IsAccepted);
            s = Restore(s); s.AdvanceWithoutSnapshot(100);
            Assert.AreEqual(FightHandlingOutcome.Failed, s.CaptureDisorder()!.Incidents.Single().HandlingAttempt!.Outcome);
            var remaining = checked((int)(fightTick + GameSession.DisorderFightDurationTicks - s.CurrentTick));
            s.AdvanceWithoutSnapshot(remaining + 8);
            Assert.AreEqual((fightTick + GameSession.DisorderFightDurationTicks + 7) / 8 * 8, s.CaptureDisorder()!.Incidents.Single().InjuryTick);
        }
    }
    [TestMethod]
    public void CancelledFightTransitDoesNotConsumePhysicalAttemptAndNewIncidentHasItsOwnAttempt()
    {
        var (s, a, b) = FightFixture(true); s.AdvanceWithoutSnapshot(1);
        var d = s.CaptureDisorder()!;
        Set(s, "DisorderView", BuildSession.WithSecurity(d, SecurityResponseStage.Completed, null));
        Assert.IsNull(s.CaptureDisorder()!.Incidents.Single().HandlingAttempt);
        Accept(s, new DisorderCommand(DisorderAction.DispatchSecurity, a));
        for (var tick = 0; tick < 400 && s.CaptureDisorder()!.Incidents.Single().HandlingAttempt?.Outcome != FightHandlingOutcome.Succeeded; tick++) s.AdvanceWithoutSnapshot(1);
        Assert.IsNotNull(s.CaptureDisorder()!.Incidents.Single().HandlingAttempt, $"No physical attempt: {s.GetStewardResponses().Single()} navigation={s.CaptureSnapshot().NavigationAgents.Single(nav => nav.Id.Value == s.CaptureDisorder()!.SecurityId)}");
        Assert.AreEqual(FightHandlingOutcome.Succeeded, s.CaptureDisorder()!.Incidents.Single().HandlingAttempt!.Outcome);
        d = s.CaptureDisorder()!;
        Set(s, "DisorderView", d with { People = d.People.Select(person => person.AgentId == a || person.AgentId == b ? person with {
            Stage = DisorderStage.Argument, Pressure = 4000, Grievance = DisorderGrievance.MusicCutoff, StageTick = s.CurrentTick, GrievanceTick = s.CurrentTick } : person).ToArray() });
        typeof(GameSession).GetMethod("BeginDisorderFight", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, [a, b, "labelled:second-fight"]);
        s.AdvanceWithoutSnapshot(1); Assert.AreEqual(SecurityResponseStage.Travelling, s.GetStewardResponses().Single().Stage);
        Assert.AreEqual(2, s.CaptureDisorder()!.Incidents.Length); Assert.IsNull(s.CaptureDisorder()!.Incidents.Last().HandlingAttempt);
    }
    [TestMethod]
    public void HandlingCompletionOnOriginalFightBoundaryRetainsExistingResponseBeforeInjuryOrdering()
    {
        var (s, _, _) = FightFixture(true);
        for (var tick = 0; tick < 400 && s.CaptureDisorder()!.Incidents.Single().HandlingAttempt is null; tick++) s.AdvanceWithoutSnapshot(1);
        var d = s.CaptureDisorder()!; var attempt = d.Incidents.Single().HandlingAttempt!;
        Assert.IsNotNull(attempt);
        var originalTick = attempt.StartedTick + GameSession.DisorderConfrontationTicks - GameSession.DisorderFightDurationTicks;
        // Labelled deadline alignment; no production deadline or duration is changed.
        Set(s, "DisorderView", d with { Incidents = d.Incidents.Select(origin => origin with { ArgumentTick = originalTick, FightTick = originalTick }).ToArray(),
            People = d.People.Select(person => person.Stage == DisorderStage.Fight ? person with { StageTick = originalTick, GrievanceTick = originalTick } : person).ToArray() });
        s = Restore(s); s.AdvanceWithoutSnapshot(GameSession.DisorderConfrontationTicks);
        var result = s.CaptureDisorder()!.Incidents.Single(); Assert.AreEqual(-2, result.InjuryTick);
        Assert.AreEqual(FightHandlingOutcome.Succeeded, result.HandlingAttempt!.Outcome);
        Assert.AreEqual(originalTick + 800, result.HandlingAttempt.EndedTick);
    }
    [TestMethod]
    public void ClosingDuringPhysicalHandlingInterruptsSavedAttemptAndKeepsMedicAutonomyAvailable()
    {
        var (s, _, _) = FightFixture(true);
        for (var tick = 0; tick < 400 && s.CaptureDisorder()!.Incidents.Single().HandlingAttempt is null; tick++) s.AdvanceWithoutSnapshot(1);
        var original = s.CaptureDisorder()!.Incidents.Single(); Assert.IsNotNull(original.HandlingAttempt);
        // Labelled existing closing transition, avoiding a fabricated 24000-tick survival claim.
        typeof(GameSession).GetMethod("FinishLivePerformance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, []);
        Set(s, "_livePerformance", s.CaptureLivePerformance()! with { EndedTick = s.CaptureProgramme()!.SlotEndTick });
        typeof(GameSession).GetProperty(nameof(GameSession.CurrentTick))!.SetValue(s, (long)GameSession.PreparedDayTicks);
        typeof(GameSession).GetProperty(nameof(GameSession.Phase))!.SetValue(s, SessionPhase.Egress);
        Set(s, "PreparationView", s.CapturePreparation()! with { Status = PreparationStatus.Departing });
        typeof(GameSession).GetMethod("StartImmersionDeparture", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, []);
        var closed = s.CaptureDisorder()!.Incidents.Single(); Assert.AreEqual(FightHandlingOutcome.Interrupted, closed.HandlingAttempt!.Outcome);
        Assert.AreEqual(original.FightTick, closed.FightTick); Assert.AreEqual(original.InjuryTick, closed.InjuryTick);
        s = Restore(s); var id = Guests(s)[2]; Incidents(s, (id, MedicalStage.Collapsed, s.CurrentTick));
        s.AdvanceWithoutSnapshot(1); Assert.AreEqual(id, s.GetMedicResponses().Single().PatientId);
        s = Restore(s); Assert.AreEqual(FightHandlingOutcome.Interrupted, s.CaptureDisorder()!.Incidents.Single().HandlingAttempt!.Outcome);
    }
    [TestMethod]
    public void MedicalCollapseDuringFightHandlingInterruptsWithoutSkillRollOrFreshAttempt()
    {
        var (s,a,_) = FightFixture(true);
        for(var tick=0;tick<400 && s.CaptureDisorder()!.Incidents.Single().HandlingAttempt is null;tick++)s.AdvanceWithoutSnapshot(1);
        Assert.IsNotNull(s.CaptureDisorder()!.Incidents.Single().HandlingAttempt);
        Incidents(s,(a,MedicalStage.Collapsed,s.CurrentTick));
        s.AdvanceWithoutSnapshot(8);
        Assert.AreEqual(FightHandlingOutcome.Interrupted,s.CaptureDisorder()!.Incidents.Single().HandlingAttempt!.Outcome);
        Assert.AreEqual(-1,s.CaptureDisorder()!.Incidents.Single().InjuryTick);
        s=Restore(s); Assert.IsFalse(Send(s,new DisorderCommand(DisorderAction.DispatchSecurity,a)).IsAccepted);
    }
    [TestMethod]
    public void AutoMedicTreatmentPhysicallyStartsAndContinuesExactlyAcrossReload()
    {
        var s = Started(); var id = Guests(s)[2]; PositionFixture(s,id,new(118,125));
        Incidents(s,(id,MedicalStage.Collapsed,s.CurrentTick)); s.AdvanceWithoutSnapshot(1);
        for(var tick=0;tick<700 && s.GetMedicResponses().Single().Stage!=MedicalResponseStage.Treating;tick++)s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalResponseStage.Treating,s.GetMedicResponses().Single().Stage);
        Assert.AreEqual(MedicalStage.Collapsed,s.CaptureMedical()!.Needs.Single(need=>need.AgentId==id).Stage);
        var clone=Restore(s);var duration=s.GetResponseStaff().Single(worker=>worker.Role==ResponseRole.Medic).TreatmentTicks;
        s.AdvanceWithoutSnapshot(duration);clone.AdvanceWithoutSnapshot(duration);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,clone.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(MedicalStage.Treated,s.CaptureMedical()!.Needs.Single(need=>need.AgentId==id).Stage);
    }
    [TestMethod]
    public void CriticalBeforeCollapsedThenDeadlineAndStableIdWithoutRemoteTreatment()
    {
        var s = Started(); var ids = Guests(s);
        var origin = s.CurrentTick - 100; Incidents(s, (ids[0], MedicalStage.Collapsed, origin + 20), (ids[1], MedicalStage.Critical, origin + 90), (ids[2], MedicalStage.Critical, origin + 80));
        s.AdvanceWithoutSnapshot(1);
        var job = s.GetMedicResponses().Single(); Assert.AreEqual(ids[2], job.PatientId); Assert.AreEqual(MedicalResponseStage.Travelling, job.Stage);
        Assert.AreEqual(MedicalStage.Critical, s.CaptureMedical()!.Needs.Single(need => need.AgentId == ids[2]).Stage);
        var command = s.SelectRoleResponse(ResponseRole.Medic, ids[1], out var reason); Assert.IsNull(command); StringAssert.Contains(reason!, "assigned");
        s.AdvanceWithoutSnapshot(1); Assert.AreEqual(job.PatientId, s.GetMedicResponses().Single().PatientId);
    }
    [TestMethod]
    public void MultipleMedicsOwnDistinctPatientsAndExactContinuationAcrossTravel()
    {
        var s = Started(true); var ids = Guests(s);
        Incidents(s, (ids[1], MedicalStage.Collapsed, s.CurrentTick), (ids[0], MedicalStage.Collapsed, s.CurrentTick));
        s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(2, s.GetMedicResponses().Count(job => job.Stage == MedicalResponseStage.Travelling));
        CollectionAssert.AreEquivalent(ids.Take(2).ToArray(), s.GetMedicResponses().Select(job => job.PatientId!.Value).ToArray());
        var clone = Restore(s);
        for (var tick = 0; tick < 160; tick++) { s.AdvanceWithoutSnapshot(1); clone.AdvanceWithoutSnapshot(1); }
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, clone.CaptureSnapshot().AuthoritativeHash);
    }
    [TestMethod]
    public void ManualRoleDispatchPrioritizesSelectedTargetBeforeAutomaticCritical()
    {
        var s = Started(); var ids = Guests(s);
        Incidents(s, (ids[0], MedicalStage.Collapsed, s.CurrentTick), (ids[1], MedicalStage.Critical, s.CurrentTick));
        Accept(s, new MedicalCommand(ids[0], MedicalAction.DispatchMedic));
        s.AdvanceWithoutSnapshot(1); Assert.AreEqual(ids[0], s.GetMedicResponses().Single().PatientId);
        Assert.IsFalse(Send(s, new MedicalCommand(ids[1], MedicalAction.DispatchMedic)).IsAccepted);
    }
    [TestMethod]
    public void RoleSelectionUsesNearestLegalWorkerAndStableWorkerIdTieBreak()
    {
        var s = Started(true); var id = Guests(s)[0]; Incidents(s, (id, MedicalStage.Collapsed, s.CurrentTick));
        var target = s.CaptureSnapshot().NavigationAgents.Single(nav => nav.Id.Value == id);
        var workers = s.GetResponseStaff().Where(worker => worker.Role == ResponseRole.Medic).ToArray();
        var command = (MedicalCommand)s.SelectRoleResponse(ResponseRole.Medic, id, out var issue)!; Assert.IsNull(issue);
        long Distance(ulong worker) { var nav = s.CaptureSnapshot().NavigationAgents.Single(nav => nav.Id.Value == worker); var dx = (long)nav.XMillimetres - target.XMillimetres; var dz = (long)nav.ZMillimetres - target.ZMillimetres; return dx * dx + dz * dz; }
        Assert.AreEqual(workers.OrderBy(worker => Distance(worker.AgentId)).ThenBy(worker => worker.AgentId).First().AgentId, command.WorkerId);
        // Equal physical positions are a labelled arbitration fixture, not a normal spawn.
        var snapshot = s.CapturePersistenceSnapshot(); var baseline = snapshot.NavigationAgents!.Single(nav => nav.Id == workers[0].AgentId);
        // Hash validation deliberately disallows arbitrary persisted coordinate edits; mutate live navigators only here.
        var agents = (System.Collections.IDictionary)typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(s)!;
        var navObject = agents[new EntityId(workers[1].AgentId)]!;
        navObject.GetType().GetProperty("XMillimetres")!.SetValue(navObject, baseline.XMillimetres);
        navObject.GetType().GetProperty("ZMillimetres")!.SetValue(navObject, baseline.ZMillimetres);
        command = (MedicalCommand)s.SelectRoleResponse(ResponseRole.Medic, id, out _)!; Assert.AreEqual(workers.Min(worker => worker.AgentId), command.WorkerId);
    }
    [TestMethod]
    public void RoleSelectionSkipsNearestBusyMedicForAvailableFartherWorker()
    {
        var s = Started(true); var ids = Guests(s);
        var workers = s.GetResponseStaff().Where(worker => worker.Role == ResponseRole.Medic).ToArray();
        PositionFixture(s, ids[0], new(118,125)); PositionFixture(s, workers[0].AgentId, new(116,125));
        PositionFixture(s, workers[1].AgentId, new(118,131));
        Incidents(s,(ids[0],MedicalStage.Collapsed,s.CurrentTick),(ids[1],MedicalStage.Collapsed,s.CurrentTick));
        Accept(s,new MedicalCommand(ids[1],MedicalAction.DispatchMedic,workers[0].AgentId));
        Assert.IsFalse(Send(s,new MedicalCommand(ids[0],MedicalAction.DispatchMedic,workers[0].AgentId)).IsAccepted);
        var command=(MedicalCommand)s.SelectRoleResponse(ResponseRole.Medic,ids[0],out var reason)!;
        Assert.IsNull(reason); Assert.AreEqual(workers[1].AgentId,command.WorkerId);
        Accept(s,command); Assert.AreEqual(ids[1],s.GetMedicResponses().Single(job=>job.WorkerId==workers[0].AgentId).PatientId);
        Assert.AreEqual(ids[0],s.GetMedicResponses().Single(job=>job.WorkerId==workers[1].AgentId).PatientId);
    }
    [TestMethod]
    public void RoleSelectionSkipsNearestUnreachableMedicForLegalFartherRoute()
    {
        var s=Started(true);var id=Guests(s)[0];
        var workers=s.GetResponseStaff().Where(worker=>worker.Role==ResponseRole.Medic).ToArray();
        PositionFixture(s,id,new(118,125));PositionFixture(s,workers[0].AgentId,new(116,125));PositionFixture(s,workers[1].AgentId,new(118,131));
        Incidents(s,(id,MedicalStage.Collapsed,s.CurrentTick));
        // Labelled finite blocked-terrain fixture: nearest worker is isolated,
        // farther worker has a continuous legal corridor to the real bedside.
        var walkable=Enumerable.Range(125,7).Select(z=>new GridCell(118,z)).Append(new(116,125)).ToHashSet();
        var overrides=new List<TerrainCellOverride>();
        for(var z=0;z<TraversalGrid.Depth;z++)for(var x=0;x<TraversalGrid.Width;x++)
            overrides.Add(new(new(x,z),GroundSurface.Grass,walkable.Contains(new(x,z))));
        Set(s,"_traversalGrid",new TraversalGrid(overrides));
        var rejected=Send(s,new MedicalCommand(id,MedicalAction.DispatchMedic,workers[0].AgentId));
        Assert.IsFalse(rejected.IsAccepted);StringAssert.Contains(rejected.Message,"reach");
        var command=(MedicalCommand)s.SelectRoleResponse(ResponseRole.Medic,id,out var reason)!;
        Assert.IsNull(reason);Assert.AreEqual(workers[1].AgentId,command.WorkerId);
        Accept(s,command);Assert.AreEqual(MedicalResponseStage.None,s.GetMedicResponses().Single(job=>job.WorkerId==workers[0].AgentId).Stage);
    }
    [TestMethod]
    public void AllUnavailableMedicsExplainEachWorkerWithoutCreatingClaims()
    {
        var s=Started(true);var id=Guests(s)[0];Incidents(s,(id,MedicalStage.Collapsed,s.CurrentTick));
        var workers=s.GetResponseStaff().Where(worker=>worker.Role==ResponseRole.Medic).ToArray();
        var medical=s.CaptureMedical()!;
        Set(s,"MedicalView",medical with {Needs=medical.Needs.Select(need=>workers.Any(worker=>worker.AgentId==need.AgentId)?need with {Intent=MedicalIntent.Rest}:need).ToArray()});
        var before=s.CaptureSnapshot().AuthoritativeHash;
        Assert.IsNull(s.SelectRoleResponse(ResponseRole.Medic,id,out var reason));
        foreach(var worker in workers)StringAssert.Contains(reason!,worker.Name);
        StringAssert.Contains(reason!,"rest");
        Assert.IsFalse(Send(s,new MedicalCommand(id,MedicalAction.DispatchMedic)).IsAccepted);
        Assert.AreEqual(before,s.CaptureSnapshot().AuthoritativeHash);
        Assert.IsTrue(s.GetMedicResponses().All(job=>job.Stage==MedicalResponseStage.None));
    }
    [TestMethod]
    public void NewCriticalPatientDoesNotPreemptPhysicallyActiveMedicTreatment()
    {
        var s=Started();var ids=Guests(s);PositionFixture(s,ids[0],new(118,125));
        Incidents(s,(ids[0],MedicalStage.Collapsed,s.CurrentTick));s.AdvanceWithoutSnapshot(1);
        for(var tick=0;tick<700&&s.GetMedicResponses().Single().Stage!=MedicalResponseStage.Treating;tick++)s.AdvanceWithoutSnapshot(1);
        var active=s.GetMedicResponses().Single();Assert.AreEqual(MedicalResponseStage.Treating,active.Stage);
        // Labelled elapsed critical clock; never persist a future CriticalTick.
        // Match ordinary collapse's release of any previously owned water place.
        typeof(GameSession).GetMethod("LeaveWater",BindingFlags.Instance|BindingFlags.NonPublic)!
            .Invoke(s,[ids[1],"Labelled critical initialization releases prior water ownership",false,true]);
        Incidents(s,(ids[1],MedicalStage.Critical,s.CurrentTick-GameSession.MedicalCriticalDelayTicks));
        Assert.IsNull(s.SelectRoleResponse(ResponseRole.Medic,ids[1],out var reason));StringAssert.Contains(reason!,"assigned");
        Assert.IsFalse(Send(s,new MedicalCommand(ids[1],MedicalAction.DispatchMedic)).IsAccepted);
        var clone=Restore(s);s.AdvanceWithoutSnapshot(16);clone.AdvanceWithoutSnapshot(16);
        var after=s.GetMedicResponses().Single();Assert.AreEqual(MedicalResponseStage.Treating,after.Stage);
        Assert.AreEqual(active.PatientId,after.PatientId);Assert.AreEqual(active.StartedTick,after.StartedTick);
        Assert.AreEqual(MedicalStage.Critical,s.CaptureMedical()!.Needs.Single(need=>need.AgentId==ids[1]).Stage);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,clone.CaptureSnapshot().AuthoritativeHash);
    }
    [TestMethod]
    public void OrdinaryDistressDoesNotAutomaticallyGuideOrDispatchAndManualGuidanceIsNotPreempted()
    {
        var s = Started(); var ids = Guests(s); var m = s.CaptureMedical()!;
        Set(s, "MedicalView", m with { Needs = m.Needs.Select(need => need.AgentId == ids[0] ? need with { Stage = MedicalStage.Distress, WarningTick = s.CurrentTick, Thirst = 10_000, HeatExposure = 10_000 } : need).ToArray() });
        s.AdvanceWithoutSnapshot(1); Assert.AreEqual(MedicalResponseStage.None, s.GetMedicResponses().Single().Stage); Assert.AreEqual(0, s.CaptureStaffInterventions().Count);
        var medic = s.GetResponseStaff().Single(worker => worker.Role == ResponseRole.Medic).AgentId;
        Accept(s, new StaffInterventionCommand(ids[0], medic, StaffInterventionAction.EscortOut));
        Incidents(s, (ids[1], MedicalStage.Collapsed, s.CurrentTick));
        s.AdvanceWithoutSnapshot(1); Assert.AreEqual(MedicalResponseStage.None, s.GetMedicResponses().Single().Stage);
        Assert.AreEqual(ids[0], s.CaptureStaffInterventions().Single().GuestId);
        Assert.IsNull(s.SelectRoleResponse(ResponseRole.Medic, ids[1], out var reason)); StringAssert.Contains(reason!, "intervention");
    }
    [TestMethod]
    public void IncapacitatedMedicAndClaimedPatientCannotReceiveNewResponse()
    {
        var s = Started(true); var ids = Guests(s);
        var medic = s.GetResponseStaff().Where(worker => worker.Role == ResponseRole.Medic).First().AgentId;
        Incidents(s, (ids[0], MedicalStage.Collapsed, s.CurrentTick));
        Accept(s, new MedicalCommand(ids[0], MedicalAction.DispatchMedic, medic));
        Incidents(s, (medic, MedicalStage.Collapsed, s.CurrentTick));
        Assert.IsFalse(Send(s, new MedicalCommand(ids[1], MedicalAction.DispatchMedic, medic)).IsAccepted);
        s.AdvanceWithoutSnapshot(1); Assert.IsFalse(s.GetMedicResponses().Single(job => job.WorkerId == medic).Stage is MedicalResponseStage.Travelling or MedicalResponseStage.Treating);
        StringAssert.Contains(s.ResponseStaffStatus(medic), "Unavailable");
    }
    [TestMethod]
    public void RoleDispatchAutosaveFailurePreservesSourceSessionAtomically()
    {
        var s = Started(); var id = Guests(s)[0]; Incidents(s, (id, MedicalStage.Collapsed, s.CurrentTick));
        var before = s.CaptureSnapshot().AuthoritativeHash; var directory = Path.Combine(Path.GetTempPath(), "festival-staff-autonomy-" + Guid.NewGuid().ToString("N"));
        var result = MedicalCommandCoordinator.Execute(directory, s, new MedicalCommand(id, MedicalAction.DispatchMedic), new("r005l", "test", "staff-auto"), DateTimeOffset.UnixEpoch, 0,
            point => { throw new IOException("Labelled injected staff save failure"); });
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(before, s.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(MedicalResponseStage.None, s.GetMedicResponses().Single().Stage);
    }
}

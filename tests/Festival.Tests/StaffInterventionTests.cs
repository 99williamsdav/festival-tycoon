using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class StaffInterventionTests
{
    private static CommandResult Send(GameSession session, SessionCommand command) => session.Execute(new(
        new CommandId(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase, session.CurrentTick, session.NextSubmissionSequence, null, command));
    private static void Accept(GameSession session, SessionCommand command)
    { var result = Send(session, command); Assert.IsTrue(result.IsAccepted, result.Message); }
    private static void Medical(GameSession session, MedicalSnapshot value) => typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, value);
    private static void SuppressUnrelatedNeeds(GameSession session, ulong? except = null)
    {
        var m = session.CaptureMedical()!;
        Medical(session, m with { Needs = m.Needs.Select(need => need.AgentId == except ? need : need with {
            Thirst = Math.Min(need.Thirst, 1000), HeatExposure = Math.Min(need.HeatExposure, 1000) }).ToArray() });
    }
    private static void Step(GameSession session, int ticks, ulong? except = null)
    {
        for (var elapsed = 0; elapsed < ticks; elapsed += 40)
        { SuppressUnrelatedNeeds(session, except); session.AdvanceWithoutSnapshot(Math.Min(40, ticks - elapsed)); }
    }
    private static GameSession Started(bool extra = true)
    {
        var session = GameSession.CreateDisorderCampaign(20260926);
        if (extra)
        {
            foreach (var effect in new[] { "staff.medic-slot", "staff.steward-slot" }) Accept(session, new ApplyStaffFoundationEffectCommand(effect));
            foreach (var offer in new[] { "staff.extra-medic", "staff.extra-steward" }) Accept(session, new AcceptPreparationOfferCommand(offer));
        }
        foreach (var offer in new[] { "act.folk", "staff.steward", "equipment.buy" }) Accept(session, new AcceptPreparationOfferCommand(offer));
        Accept(session, new StartPreparedEditionCommand());
        while (!session.CapturePreparation()!.People.All(person => person.Admitted) && session.CurrentTick < 4000) Step(session, 40);
        Assert.IsTrue(session.CapturePreparation()!.People.All(person => person.Admitted));
        Assert.IsFalse(session.CaptureMedical()!.DevelopmentInterventionFixturesEnabled);
        return session;
    }
    private static GameSession Restore(GameSession session)
    {
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
        return restored.Session;
    }
    private static void Route(GameSession session, ulong id, GridCell destination) => typeof(GameSession)
        .GetMethod("ApplyAgentDestination", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session,
            [new EntityId(id), new SetAgentDestinationCommand(destination, "labelled.intervention-fixture-route"), false]);
    private static void PlaceFixture(GameSession session, ulong id, GridCell cell)
    {
        var agents = typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var agent = agents.GetType().GetProperty("Item")!.GetValue(agents, [new EntityId(id)])!;
        var centre = TraversalGrid.CellCentre(cell);
        foreach (var (name, value) in new[] { ("XMillimetres", centre.XMillimetres), ("ZMillimetres", centre.ZMillimetres),
                     ("SegmentOriginXMillimetres", centre.XMillimetres), ("SegmentOriginZMillimetres", centre.ZMillimetres) }) agent.GetType().GetProperty(name)!.SetValue(agent, value);
        Route(session, id, cell);
    }
    private static void DistressFixture(GameSession session, ulong id)
    {
        var m = session.CaptureMedical()!;
        Medical(session, m with { Stage = id == m.AtRiskGuestId ? MedicalStage.Distress : m.Stage,
            WarningTick = id == m.AtRiskGuestId ? session.CurrentTick : m.WarningTick,
            Needs = m.Needs.Select(need => need.AgentId == id ? need with { Stage = MedicalStage.Distress,
                Thirst = 10000, HeatExposure = 10000, WarningTick = session.CurrentTick, Reason = "Labelled ambulatory-distress intervention fixture" } : need).ToArray() });
    }

    [TestMethod]
    public void ProductionLegacyGuidanceDispatchesPhysicalWorkerAndFixtureGateIsDisabled()
    {
        var session = Started(false); var id = session.CaptureMedical()!.Needs[0].AgentId;
        var hash = session.CaptureSnapshot().AuthoritativeHash;
        Assert.IsFalse(Send(session, new DevelopmentMedicalFixtureCommand(id, MedicalAction.GuideToWater)).IsAccepted);
        Assert.IsFalse(Send(session, new DevelopmentDisorderEgressFixtureCommand(id)).IsAccepted);
        Assert.AreEqual(hash, session.CaptureSnapshot().AuthoritativeHash);
        Accept(session, new MedicalCommand(id, MedicalAction.GuideToWater));
        Assert.AreEqual(MedicalIntent.WatchShow, session.CaptureMedical()!.Needs.Single(need => need.AgentId == id).Intent);
        var job = session.CaptureStaffInterventions().Single();
        Assert.AreEqual(session.CaptureDisorder()!.SecurityId, job.WorkerId);
        Assert.AreEqual(StaffInterventionStage.Travelling, job.Stage);
        session = Restore(session);
        Step(session, 40);
        Assert.AreEqual(MedicalIntent.WatchShow, session.CaptureMedical()!.Needs.Single(need => need.AgentId == id).Intent);
        for (var elapsed = 0; elapsed < 5000 && session.CaptureStaffInterventions().Single().Stage == StaffInterventionStage.Travelling; elapsed += 40) Step(session, 40);
        Assert.IsTrue(session.CaptureMedical()!.Evidence.Any(item => item.Id == "staff:intervention-arrival"));
        Assert.IsTrue(session.CaptureStaffInterventions().Single().StartedTick > job.DispatchedTick);
        Step(session, 8); Restore(session);
    }

    [TestMethod]
    public void IndependentWorkersTrackMovingTargetsAndSaveTheirApproachAndGuidance()
    {
        var session = Started(); var m = session.CaptureMedical()!; var guest = m.Needs[0].AgentId;
        var sam = session.GetResponseStaff().Single(profile => profile.Name == "Sam Ellis").AgentId;
        Accept(session, new StaffInterventionCommand(guest, sam, StaffInterventionAction.GuideToWater));
        Accept(session, new StaffInterventionCommand(m.AtRiskGuestId, m.MedicId, StaffInterventionAction.GuideToRest));
        Route(session, guest, new GridCell(80, 170)); // labelled moving-target route, no teleport
        session = Restore(session);
        Assert.AreEqual(2, session.CaptureStaffInterventions().Count);
        Assert.IsFalse(Send(session, new StaffInterventionCommand(m.AtRiskGuestId, sam, StaffInterventionAction.EscortOut)).IsAccepted);
        Assert.IsFalse(Send(session, new MedicalCommand(m.AtRiskGuestId, MedicalAction.DispatchMedic)).IsAccepted);
        var performanceStart = session.CurrentTick;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var elapsed = 0; elapsed < 6500 && session.CaptureStaffInterventions().Any(job => job.Stage is StaffInterventionStage.Travelling or StaffInterventionStage.Guiding); elapsed += 40) Step(session, 40);
        watch.Stop();
        Console.WriteLine($"STAFF_INTERVENTION_DIAGNOSTIC people={session.CapturePreparation()!.People.Length} jobs=2 ticks={session.CurrentTick - performanceStart} elapsed_ms={watch.Elapsed.TotalMilliseconds:F2} fixture_need_suppression=true");
        Assert.IsTrue(session.CaptureStaffInterventions().All(job => job.Stage == StaffInterventionStage.Completed),
            string.Join("; ", session.CaptureStaffInterventions().Select(job => job.Description)));
        Assert.AreEqual(2, session.CaptureMedical()!.Evidence.Count(item => item.Id == "staff:intervention-arrival"));
        Assert.AreEqual(MedicalIntent.Rest, session.CaptureMedical()!.Needs.Single(need => need.AgentId == m.AtRiskGuestId).Intent);
        Restore(session);
    }

    [TestMethod]
    public void EscortWalksBothPeopleToGateBeforeOnlyAffectedPersonDepartsAndReloadMatches()
    {
        var session = Started(); var m = session.CaptureMedical()!; var guest = m.Needs[0].AgentId;
        DistressFixture(session, guest);
        var worker = session.CaptureDisorder()!.SecurityId;
        PlaceFixture(session, guest, new GridCell(124, 174)); PlaceFixture(session, worker, new GridCell(128, 174));
        Accept(session, new StaffInterventionCommand(guest, worker, StaffInterventionAction.EscortOut));
        session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(StaffInterventionStage.Escorting, session.CaptureStaffInterventions().Single().Stage);
        Assert.IsFalse(session.CapturePreparation()!.People.Single(person => person.AgentId == guest).Departed);
        var resumed = Restore(session);
        for (var elapsed = 0; elapsed < 2500 && session.CaptureStaffInterventions().Single().Stage == StaffInterventionStage.Escorting; elapsed += 20)
        { Step(session, 20, guest); Step(resumed, 20, guest); }
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, resumed.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(StaffInterventionStage.Completed, session.CaptureStaffInterventions().Single().Stage);
        Assert.IsTrue(session.CapturePreparation()!.People.Single(person => person.AgentId == guest).Departed);
        Assert.AreEqual(1, session.CapturePreparation()!.People.Count(person => person.Departed));
        Assert.AreEqual(MedicalStage.Removed, session.CaptureMedical()!.Needs.Single(need => need.AgentId == guest).Stage);
        Assert.AreEqual(GameSession.MedicalExitCell, session.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id.Value == guest).Destination);
        Restore(session);
    }

    [TestMethod]
    public void NoWarningClosureWrongRoleAndCollapsedGuidanceAreRejectedWithoutMutation()
    {
        var session = Started(); var m = session.CaptureMedical()!; var guest = m.Needs[0].AgentId; var steward = session.CaptureDisorder()!.SecurityId;
        foreach (var command in new StaffInterventionCommand[] {
            new(guest, steward, StaffInterventionAction.EscortOut), new(guest, m.MedicId, StaffInterventionAction.GuideToWater),
            new(m.AtRiskGuestId, steward, StaffInterventionAction.GuideToRest) })
        {
            var hash = session.CaptureSnapshot().AuthoritativeHash;
            Assert.IsFalse(Send(session, command).IsAccepted); Assert.AreEqual(hash, session.CaptureSnapshot().AuthoritativeHash);
        }
        m = session.CaptureMedical()!;
        Medical(session, m with { Needs = m.Needs.Select(need => need.AgentId == guest ? need with { Stage = MedicalStage.Collapsed, Intent = MedicalIntent.Collapsed, CollapseTick = session.CurrentTick } : need).ToArray() });
        var before = session.CaptureSnapshot().AuthoritativeHash;
        Assert.IsFalse(Send(session, new StaffInterventionCommand(guest, steward, StaffInterventionAction.GuideToWater)).IsAccepted);
        Assert.IsFalse(Send(session, new StaffInterventionCommand(guest, m.MedicId, StaffInterventionAction.EscortOut)).IsAccepted);
        Assert.AreEqual(before, session.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void LateApproachDoesNotStopCollapseDeathAndReleasesJobsAcrossRetry()
    {
        var session = Started(false); var m = session.CaptureMedical()!; var id = m.AtRiskGuestId;
        DistressFixture(session, id);
        m = session.CaptureMedical()!;
        Medical(session, m with { WarningTick = session.CurrentTick - GameSession.MedicalCollapseDelayTicks + 1 });
        Accept(session, new StaffInterventionCommand(id, m.MedicId, StaffInterventionAction.EscortOut));
        session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalStage.Collapsed, session.CaptureMedical()!.Stage);
        Assert.AreEqual(StaffInterventionStage.Failed, session.CaptureStaffInterventions().Single().Stage);
        session = Restore(session);
        Step(session, GameSession.MedicalDeathDelayTicks + 1, id);
        Assert.AreEqual(PreparationStatus.Failed, session.PreparedStatus);
        Assert.AreEqual(1, session.CaptureLifecycleSnapshot()!.Casualties.Count);
        session = Restore(session); Accept(session, new SpendCouncilFavourCommand());
        Assert.AreEqual(0, session.CaptureStaffInterventions().Count); Restore(session);
    }

    [TestMethod]
    public void CollapsedBodiesDoNotBlockExactMovementButMedicalStateAndDeadlineRemain()
    {
        var session = Started(false); var m = session.CaptureMedical()!; var id = m.AtRiskGuestId; var walker = m.Needs[0].AgentId;
        var body = new GridCell(80, 175); PlaceFixture(session, id, body); PlaceFixture(session, walker, new GridCell(80, 171));
        Medical(session, m with { Stage = MedicalStage.Collapsed, WarningTick = session.CurrentTick - 1, CollapseTick = session.CurrentTick,
            Needs = m.Needs.Select(need => need.AgentId == id ? need with { Thirst = 10000, HeatExposure = 10000, Intent = MedicalIntent.Collapsed } : need).ToArray() });
        Route(session, walker, body);
        for (var elapsed = 0; elapsed < 500 && session.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id.Value == walker).Action != AgentNavigationAction.Arrived; elapsed += 20) Step(session, 20, id);
        Assert.AreEqual(AgentNavigationAction.Arrived, session.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id.Value == walker).Action);
        Assert.IsTrue(session.CaptureSnapshot().NavigationAgents.Any(agent => agent.Id.Value == id));
        Assert.AreEqual(MedicalIntent.Collapsed, session.CaptureMedical()!.Needs.Single(need => need.AgentId == id).Intent);
        Restore(session);
    }

    [TestMethod]
    public void BaselinePhysicalRestGuidancePreventsHighNeedsWithoutFixtureCommandsOrClampingTarget()
    {
        var session = Started(false); var m = session.CaptureMedical()!; var id = m.AtRiskGuestId;
        Medical(session, m with { Needs = m.Needs.Select(need => need.AgentId == id ? need with {
            Thirst = 8800, HeatExposure = 7800, Reason = "Labelled early high-needs counterfactual; target not suppressed" } : need).ToArray() });
        Accept(session, new MedicalCommand(id, MedicalAction.GuideToRest));
        Assert.AreEqual(MedicalIntent.WatchShow, session.CaptureMedical()!.Needs.Single(need => need.AgentId == id).Intent);
        for (var elapsed = 0; elapsed < 6200 && session.PreparedStatus == PreparationStatus.Running; elapsed += 40) Step(session, 40, id);
        var job = session.CaptureStaffInterventions().Single();
        Assert.AreEqual(StaffInterventionStage.Completed, job.Stage, job.Description);
        Assert.IsTrue(job.StartedTick > job.DispatchedTick);
        Assert.AreEqual(PreparationStatus.Running, session.PreparedStatus);
        Assert.AreEqual(0, session.CaptureLifecycleSnapshot()!.Casualties.Count);
        Assert.AreEqual(MedicalStage.Treated, session.CaptureMedical()!.Stage);
        Assert.IsTrue(session.CaptureMedical()!.Evidence.Any(item => item.Id == "medical:prevented"));
        Assert.IsFalse(session.CaptureMedical()!.DevelopmentInterventionFixturesEnabled); Restore(session);
    }

    [TestMethod]
    public void CollapseDuringSavedEscortCancelsInsteadOfProtectingItsDeadline()
    {
        var session = Started(false); var m = session.CaptureMedical()!; var id = m.AtRiskGuestId; var worker = m.MedicId;
        PlaceFixture(session, id, new GridCell(80, 175)); PlaceFixture(session, worker, new GridCell(84, 175));
        DistressFixture(session, id); m = session.CaptureMedical()!;
        Medical(session, m with { WarningTick = session.CurrentTick - GameSession.MedicalCollapseDelayTicks + 2 });
        Accept(session, new StaffInterventionCommand(id, worker, StaffInterventionAction.EscortOut));
        session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(StaffInterventionStage.Escorting, session.CaptureStaffInterventions().Single().Stage);
        session = Restore(session); session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalStage.Collapsed, session.CaptureMedical()!.Stage);
        Assert.AreEqual(StaffInterventionStage.Failed, session.CaptureStaffInterventions().Single().Stage);
        Assert.IsFalse(session.CapturePreparation()!.People.Single(person => person.AgentId == id).Departed);
        session = Restore(session); Step(session, GameSession.MedicalDeathDelayTicks + 1, id);
        Assert.AreEqual(PreparationStatus.Failed, session.PreparedStatus); Restore(session);
    }

    [TestMethod]
    public void JobTamperingWrongRolesOwnershipClocksAndFakeGateCompletionAreRejected()
    {
        var session = Started(); var m = session.CaptureMedical()!; var id = m.Needs[0].AgentId;
        var worker = session.CaptureDisorder()!.SecurityId;
        Accept(session, new StaffInterventionCommand(id, worker, StaffInterventionAction.GuideToWater));
        var snapshot = session.CapturePersistenceSnapshot(); var job = session.CaptureStaffInterventions().Single();
        var check = typeof(GameSession).GetMethod("ValidatePersistedInterventions", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var bad in new[] { job with { DispatchedTick = session.CurrentTick + 1 }, job with { StartedTick = session.CurrentTick },
                     job with { Action = StaffInterventionAction.GuideToRest }, job with { Stage = StaffInterventionStage.Completed, Action = StaffInterventionAction.EscortOut, EndedTick = session.CurrentTick, Waypoint = GameSession.MedicalExitCell },
                     job with { Stage = StaffInterventionStage.Guiding, Action = StaffInterventionAction.EscortOut, StartedTick = session.CurrentTick } })
            Assert.IsNotNull(check.Invoke(null, [snapshot with { Medical = snapshot.Medical! with { StaffInterventions = [bad] } }]), bad.ToString());
        var other = job with { WorkerId = session.GetResponseStaff().Single(profile => profile.Name == "Sam Ellis").AgentId };
        Assert.IsNotNull(check.Invoke(null, [snapshot with { Medical = snapshot.Medical! with { StaffInterventions = new[] { job, other }.OrderBy(item => item.WorkerId).ToArray() } }]));
        Restore(session);
    }

    [TestMethod]
    public void UnreachableInterventionDoesNotChangeOwnershipAndExplicitFixtureGateRoundTrips()
    {
        var session = Started(false); var m = session.CaptureMedical()!; var id = m.Needs[0].AgentId;
        var gridField = typeof(GameSession).GetField("_traversalGrid", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var grid = (TraversalGrid)gridField.GetValue(session)!;
        var nav = session.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id.Value == id);
        var origin = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
        var terrain = grid.Overrides.ToDictionary(item => item.Key, item => item.Value);
        for (var z = origin.Z - 6; z <= origin.Z + 6; z++)
        for (var x = origin.X - 6; x <= origin.X + 6; x++)
            if (Math.Abs(x - origin.X) == 6 || Math.Abs(z - origin.Z) == 6) terrain[new(x, z)] = new(new(x, z), GroundSurface.Grass, false);
        gridField.SetValue(session, new TraversalGrid(terrain.Values));
        var hash = session.CaptureSnapshot().AuthoritativeHash;
        Assert.IsFalse(Send(session, new StaffInterventionCommand(id, session.CaptureDisorder()!.SecurityId, StaffInterventionAction.GuideToWater)).IsAccepted);
        Assert.AreEqual(hash, session.CaptureSnapshot().AuthoritativeHash);
        gridField.SetValue(session, grid); // undo the labelled unreachable-route setup before save-gate proof
        hash = session.CaptureSnapshot().AuthoritativeHash;
        Medical(session, session.CaptureMedical()! with { DevelopmentInterventionFixturesEnabled = true });
        Assert.AreNotEqual(hash, session.CaptureSnapshot().AuthoritativeHash);
        Assert.IsTrue(Restore(session).CaptureMedical()!.DevelopmentInterventionFixturesEnabled);
    }

    [TestMethod]
    public void LeaveQueueHasNoRemoteEffectAndKeepsOtherPersonsServiceOwner()
    {
        var session = Started(); var m = session.CaptureMedical()!;
        var owner = m.Needs.First(need => need.Profile == MedicalNeedProfile.Guest && need.AgentId % 4 == 0).AgentId;
        var guest = m.Needs.First(need => need.Profile == MedicalNeedProfile.Guest && need.AgentId != owner && need.AgentId != m.AtRiskGuestId).AgentId;
        var front = GameSession.WaterServiceCell(m.MainWaterCell);
        var cells = new GridCell[] { front, new(front.X, front.Z + 2), new(front.X, front.Z + 4) };
        // Labelled coherent physical two-person queue; production fixture gate remains false.
        PlaceFixture(session, owner, cells[0]); PlaceFixture(session, guest, cells[1]);
        Medical(session, m with { WaterQueue = [owner, guest], MainWaterQueueCells = cells, WaterOwnerId = owner,
            Needs = m.Needs.Select(need => need.AgentId == owner ? need with { Intent = MedicalIntent.Drinking, QueueSlot = 0, Thirst = 10000 }
                : need.AgentId == guest ? need with { Intent = MedicalIntent.SeekWater, QueueSlot = 1, Thirst = 10000 } : need).ToArray() });
        Route(session, owner, cells[0]); Route(session, guest, cells[1]);
        var worker = session.GetResponseStaff().Single(profile => profile.Name == "Sam Ellis").AgentId;
        PlaceFixture(session, worker, new(cells[1].X + 4, cells[1].Z));
        Accept(session, new StaffInterventionCommand(guest, worker, StaffInterventionAction.LeaveWaterQueue));
        Assert.AreEqual(1, session.CaptureMedical()!.Needs.Single(need => need.AgentId == guest).QueueSlot);
        session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(StaffInterventionStage.Guiding, session.CaptureStaffInterventions().Single().Stage);
        Assert.IsNull(session.CaptureMedical()!.Needs.Single(need => need.AgentId == guest).QueueSlot);
        Assert.AreEqual(owner, session.CaptureMedical()!.WaterOwnerId);
        CollectionAssert.AreEqual(new[] { owner }, session.CaptureMedical()!.WaterQueue);
        session = Restore(session); session.AdvanceWithoutSnapshot(4);
        Assert.AreEqual(StaffInterventionStage.Completed, session.CaptureStaffInterventions().Single().Stage); Restore(session);
    }

    [TestMethod]
    public void ClosingWaterBeforePhysicalArrivalDoesNotGiveStewardRemoteRestOrQueueEffect()
    {
        var session = Started(false); var m = session.CaptureMedical()!; var id = m.AtRiskGuestId; var worker = session.CaptureDisorder()!.SecurityId;
        PlaceFixture(session, id, new GridCell(80, 175)); PlaceFixture(session, worker, new GridCell(84, 175));
        Accept(session, new StaffInterventionCommand(id, worker, StaffInterventionAction.GuideToWater));
        Accept(session, new DisorderCommand(DisorderAction.CloseWater));
        Assert.AreEqual(MedicalIntent.WatchShow, session.CaptureMedical()!.Needs.Single(need => need.AgentId == id).Intent);
        session = Restore(session); session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(StaffInterventionStage.Failed, session.CaptureStaffInterventions().Single().Stage);
        StringAssert.Contains(session.CaptureStaffInterventions().Single().Description, "no substitute rest");
        Assert.AreEqual(MedicalIntent.WatchShow, session.CaptureMedical()!.Needs.Single(need => need.AgentId == id).Intent);
        Assert.AreEqual(0, session.CaptureMedical()!.WaterQueue.Length); Restore(session);
    }
}

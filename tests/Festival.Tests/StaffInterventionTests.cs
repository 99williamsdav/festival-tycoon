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
    private static void Medical(GameSession session, MedicalSnapshot value) => typeof(GameSession).GetProperty("MedicalView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, value);
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
        var session = extra ? BuildSession.PlannedWith("doctors-orders", 20260926) : BuildSession.Planned(20260926);
        if (extra) Accept(session, new AcceptPreparationOfferCommand(BuildSession.ExtraId(session, StaffRole.Medic)));
        foreach (var hire in BuildSession.Crew(session)) Accept(session, hire);
        Accept(session, new StartPreparedEditionCommand());
        while (!session.CapturePreparation()!.People.All(person => person.Admitted) && session.CurrentTick < 12000) Step(session, 40);
        Assert.IsTrue(session.CapturePreparation()!.People.All(person => person.Admitted));
        // The steward is the worker these tests send: let them finish any break of their own (the bar is on their way in) first.
        var steward = session.CaptureDisorder()!.SecurityId;
        bool OnABreak() => session.CapturePerson(steward) is { } p && (p.Intent is MedicalIntent.Drinking or MedicalIntent.Rest ||
            p.ToiletStage is not (ToiletVisitStage.None or ToiletVisitStage.Approaching or ToiletVisitStage.Queued) ||
            session.CaptureVendors().Any(v => v.OwnerId == steward) || session.CaptureWaterPoints().Any(w => w.OwnerId == steward));
        while (OnABreak() && session.CurrentTick < 16000) Step(session, 40);
        Assert.IsFalse(OnABreak());
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
        Medical(session, m with { Needs = m.Needs.Select(need => need.AgentId == id ? need with { Stage = MedicalStage.Distress,
                Thirst = 10000, HeatExposure = 10000, WarningTick = session.CurrentTick, Reason = "Labelled ambulatory-distress intervention fixture" } : need).ToArray() });
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
            new(BuildSession.LastGuest(session), steward, StaffInterventionAction.GuideToRest) })
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
    public void CollapsedBodiesDoNotBlockExactMovementButMedicalStateAndDeadlineRemain()
    {
        var session = Started(false); var m = session.CaptureMedical()!; var id = BuildSession.LastGuest(session); var walker = m.Needs[0].AgentId;
        var body = new GridCell(80, 175); PlaceFixture(session, id, body); PlaceFixture(session, walker, new GridCell(80, 171));
        Medical(session, m with {
            Needs = m.Needs.Select(need => need.AgentId == id ? need with { Thirst = 10000, HeatExposure = 10000, Intent = MedicalIntent.Collapsed, Stage = MedicalStage.Collapsed, CollapseTick = session.CurrentTick } : need).ToArray() });
        Route(session, walker, body);
        for (var elapsed = 0; elapsed < 500 && session.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id.Value == walker).Action != AgentNavigationAction.Arrived; elapsed += 20) Step(session, 20, id);
        Assert.AreEqual(AgentNavigationAction.Arrived, session.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id.Value == walker).Action);
        Assert.IsTrue(session.CaptureSnapshot().NavigationAgents.Any(agent => agent.Id.Value == id));
        Assert.AreEqual(MedicalIntent.Collapsed, session.CaptureMedical()!.Needs.Single(need => need.AgentId == id).Intent);
        Restore(session);
    }

    [TestMethod]
    public void ClosingWaterBeforePhysicalArrivalDoesNotGiveStewardRemoteRestOrQueueEffect()
    {
        var session = Started(false); var m = session.CaptureMedical()!; var id = BuildSession.LastGuest(session); var worker = session.CaptureDisorder()!.SecurityId;
        PlaceFixture(session, id, new GridCell(80, 175)); PlaceFixture(session, worker, new GridCell(84, 175));
        Accept(session, new StaffInterventionCommand(id, worker, StaffInterventionAction.GuideToWater));
        Accept(session, new DisorderCommand(DisorderAction.CloseWater));
        Assert.AreEqual(MedicalIntent.WatchShow, session.CaptureMedical()!.Needs.Single(need => need.AgentId == id).Intent);
        session = Restore(session); session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(StaffInterventionStage.Failed, session.CaptureStaffInterventions().Single().Stage);
        StringAssert.Contains(session.CaptureStaffInterventions().Single().Description, "no substitute rest");
        Assert.AreEqual(MedicalIntent.WatchShow, session.CaptureMedical()!.Needs.Single(need => need.AgentId == id).Intent);
        Assert.AreEqual(0, BuildSession.MainTap(session).Queue.Length); Restore(session);
    }
}

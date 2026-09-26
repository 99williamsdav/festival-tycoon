using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class TimetableLateBandTests
{
    private static CommandResult Send(GameSession session, SessionCommand command) => session.Execute(new(
        new CommandId(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase,
        session.CurrentTick, session.NextSubmissionSequence, null, command));

    private static void Medical(GameSession session, Func<MedicalNeed, MedicalNeed> change)
    {
        var medical = session.CaptureMedical()!;
        typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session, medical with { Needs = medical.Needs.Select(change).ToArray() });
    }

    private static GameSession Started(bool holdFirstPerformer)
    {
        var session = GameSession.CreateTimetableCampaign(20260926);
        Assert.IsTrue(Send(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.neon-postcards", "act.field-frequency"])).IsAccepted);
        foreach (var id in new[] { "staff.steward", "equipment.buy" })
            Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand(id)).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        var performer = session.CaptureProgramme()!.Performers[0].AgentId;
        Medical(session, need => need with { Thirst = 0, HeatExposure = 0,
            Intent = holdFirstPerformer && need.AgentId == performer ? MedicalIntent.AwaitMedic : need.Intent });
        return session;
    }

    private static GameSession Restored(GameSession session)
    {
        var loaded = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(loaded.IsSuccess, loaded.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, loaded.Session!.CaptureSnapshot().AuthoritativeHash);
        return loaded.Session;
    }

    [TestMethod]
    public void LateReadinessStartsAtScheduledBoundaryAndExcludesPreShowAndChangeover()
    {
        var session = Started(holdFirstPerformer: true);
        session.AdvanceWithoutSnapshot(GameSession.FestivalSlotStarts[0] - 1);
        Assert.IsFalse(session.FestivalBandLate);
        Assert.IsTrue(session.CaptureDisorder()!.People.All(person => person.Grievance != DisorderGrievance.BandDelayed));
        session.AdvanceWithoutSnapshot(1);
        Assert.IsTrue(session.FestivalBandLate);
        Assert.AreEqual((long)GameSession.FestivalSlotStarts[0], session.LateReadyScheduledTick);
        session = Restored(session);
        var performer = session.CaptureProgramme()!.Performers[0].AgentId;
        Medical(session, need => need.AgentId == performer ? need with { Intent = MedicalIntent.WatchShow } : need);
        session.AdvanceWithoutSnapshot(GameSession.FestivalSlotEnds[0] - (int)session.CurrentTick);
        Assert.IsFalse(session.FestivalBandLate);
        var changeover = session.CaptureDisorder()!;
        foreach (var person in changeover.People.Where(person => person.Grievance == DisorderGrievance.BandDelayed))
            Console.WriteLine($"changeover tick={session.CurrentTick} retained person={person.AgentId} stage={person.Stage} pressure={person.Pressure} onset={person.GrievanceTick}");
        // A fight/injury retains its causal origin and deadline. One existing social decision settles ordinary grievances.
        session.AdvanceWithoutSnapshot(8);
        Assert.IsTrue(session.CaptureDisorder()!.People.Where(person => person.Stage is not (DisorderStage.Fight or DisorderStage.Injured))
            .All(person => person.Grievance != DisorderGrievance.BandDelayed));
        Assert.IsTrue(session.CaptureDisorder()!.People.Where(person => person.Grievance == DisorderGrievance.BandDelayed)
            .All(person => person.GrievanceTick < GameSession.FestivalSlotEnds[0]));
        session.AdvanceWithoutSnapshot(GameSession.FestivalSlotStarts[1] - (int)session.CurrentTick - 1);
        Assert.IsFalse(session.FestivalBandLate);
    }

    [TestMethod]
    public void ActualMedicalDelayWarnsGraduallyThenRelievesWhenMusicReallyBeginsAndRestoresExactly()
    {
        var session = Started(holdFirstPerformer: true);
        session.AdvanceWithoutSnapshot(GameSession.FestivalSlotStarts[0]);
        while (!session.CaptureLivePerformance()!.Listeners.Any(listener => listener.AtPlace && listener.Enthusiasm >= 65) &&
               session.CurrentTick < GameSession.FestivalSlotStarts[0] + 600) session.AdvanceWithoutSnapshot(8);
        var listener = session.CaptureLivePerformance()!.Listeners.Where(item => item.AtPlace).OrderByDescending(item => item.Enthusiasm).First();
        var id = listener.AgentId;
        var planner = new DisorderCuePlanner();
        planner.Reset(session.CaptureDisorder(), session.CurrentTick);
        var warningSeen = session.CaptureDisorder()!.Evidence.Any(item => item.PersonId == id && item.Id.StartsWith("disorder:band-late:"));
        while (session.CaptureDisorder()!.People.Single(person => person.AgentId == id).Stage is DisorderStage.Calm or DisorderStage.Complaint &&
               session.CurrentTick < GameSession.FestivalSlotStarts[0] + 1_800)
        {
            session.AdvanceWithoutSnapshot(8);
            warningSeen |= session.CaptureDisorder()!.Evidence.Any(item => item.PersonId == id && item.Id.StartsWith("disorder:band-late:"));
            planner.Observe(session.CaptureDisorder()!, session.CaptureMedical(), session.CurrentTick);
        }
        var agitated = session.CaptureDisorder()!.People.Single(person => person.AgentId == id);
        Assert.IsTrue(warningSeen);
        Assert.AreEqual(DisorderGrievance.BandDelayed, agitated.Grievance);
        Assert.AreEqual(DisorderStage.Agitated, agitated.Stage);
        Assert.IsTrue(agitated.Pressure >= GameSession.DisorderAgitatedPressure && agitated.Pressure < GameSession.DisorderArgumentPressure);
        Assert.AreEqual(0, session.CaptureDisorder()!.Incidents.Length, "A causal warning and visible agitation precede confrontation harm.");
        Console.WriteLine($"band warning {agitated.GrievanceTick}; agitation {session.CurrentTick}; pressure {agitated.Pressure}; enthusiasm {listener.Enthusiasm}; temperament {agitated.Temperament}");
        var restored = Restored(session);
        session.AdvanceWithoutSnapshot(80);
        restored.AdvanceWithoutSnapshot(80);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.CaptureSnapshot().AuthoritativeHash);
        var performer = session.CaptureProgramme()!.Performers[0].AgentId;
        Medical(session, need => need.AgentId == performer ? need with { Intent = MedicalIntent.WatchShow } : need);
        while (session.CaptureLivePerformance()!.Stage != LiveSetStage.Live && session.CurrentTick < GameSession.FestivalSlotEnds[0] - 160)
            session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(LiveSetStage.Live, session.CaptureLivePerformance()!.Stage);
        Assert.IsFalse(session.FestivalBandLate);
        var before = session.CaptureDisorder()!.People.Single(person => person.AgentId == id).Pressure;
        session.AdvanceWithoutSnapshot(80);
        var relieved = session.CaptureDisorder()!.People.Single(person => person.AgentId == id);
        Assert.AreEqual(DisorderGrievance.None, relieved.Grievance);
        Assert.IsTrue(relieved.Pressure < before);
        Restored(session);
    }

    [TestMethod]
    public void BlockedOutgoingPhysicalPerformerMakesNextActualSlotLateWhileOldSlotRemainsFinished()
    {
        var session = Started(holdFirstPerformer: false);
        while (session.CaptureLivePerformance()!.Stage != LiveSetStage.Live && session.CurrentTick < 3_000)
            session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(LiveSetStage.Live, session.CaptureLivePerformance()!.Stage);
        var performer = session.CaptureProgramme()!.Performers[0].AgentId;
        Medical(session, need => need.AgentId == performer ? need with { Intent = MedicalIntent.AwaitMedic } : need);
        session.AdvanceWithoutSnapshot(GameSession.FestivalSlotStarts[1] - (int)session.CurrentTick);
        Assert.AreEqual(0, session.CaptureProgramme()!.CurrentSlot);
        Assert.AreEqual(LiveSetStage.Finished, session.CaptureLivePerformance()!.Stage);
        Assert.IsTrue(session.FestivalBandLate);
        Assert.AreEqual((long)GameSession.FestivalSlotStarts[1], session.LateReadyScheduledTick);
        session.AdvanceWithoutSnapshot(8);
        Assert.IsTrue(session.CaptureDisorder()!.People.Any(person => person.Grievance == DisorderGrievance.BandDelayed));
        Restored(session);
    }

    [TestMethod]
    public void BandDelayCannotReplaceUrgentMedicalOwnershipOrDoubleCountAnActualPowerCut()
    {
        var session = Started(holdFirstPerformer: true);
        session.AdvanceWithoutSnapshot(1_600);
        var id = session.CaptureLivePerformance()!.Listeners.First(listener => listener.AtPlace && listener.Enthusiasm >= 35).AgentId;
        Medical(session, need => need.AgentId == id ? need with { Stage = MedicalStage.Distress, Intent = MedicalIntent.AwaitMedic,
            Thirst = 9_500, HeatExposure = 8_500, WarningTick = session.CurrentTick } : need);
        session.AdvanceWithoutSnapshot(8);
        Assert.AreNotEqual(DisorderGrievance.BandDelayed, session.CaptureDisorder()!.People.Single(person => person.AgentId == id).Grievance);
        Assert.AreEqual(MedicalIntent.AwaitMedic, session.CaptureMedical()!.Needs.Single(need => need.AgentId == id).Intent);

        session = Started(holdFirstPerformer: false);
        while (session.CaptureLivePerformance()!.Stage != LiveSetStage.Live && session.CurrentTick < 3_000)
            session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(LiveSetStage.Live, session.CaptureLivePerformance()!.Stage);
        Assert.IsTrue(Send(session, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
        session.AdvanceWithoutSnapshot(80);
        Assert.IsFalse(session.FestivalBandLate);
        Assert.IsTrue(session.CaptureDisorder()!.People.Any(person => person.Grievance == DisorderGrievance.MusicCutoff));
        Assert.IsFalse(session.CaptureDisorder()!.People.Any(person => person.Grievance == DisorderGrievance.BandDelayed));
    }

    [TestMethod]
    public void PhysicalWaterTripCanDelayPerformerAndOnlyNormalDrinkingReleasesEntry()
    {
        var session = Started(holdFirstPerformer: false);
        var performer = session.CaptureProgramme()!.Performers[0].AgentId;
        // Development need initialization; the existing water navigation, physical admission and drinking execute normally.
        Medical(session, need => need.AgentId == performer ? need with { Thirst = 9_500, HeatExposure = 6_500 } : need);
        typeof(GameSession).GetMethod("SeekWater", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(session, [performer, "Development fixture: performer needs water before the scheduled set"]);
        session.AdvanceWithoutSnapshot(GameSession.FestivalSlotStarts[0]);
        Assert.IsTrue(session.FestivalBandLate);
        Assert.AreEqual(LiveSetStage.BeforeSet, session.CaptureLivePerformance()!.Stage);
        Assert.IsTrue(session.CaptureMedical()!.Needs.Single(need => need.AgentId == performer).Intent is MedicalIntent.SeekWater or MedicalIntent.Drinking);
        Assert.IsFalse(session.CaptureLivePerformance()!.Performers.Single(person => person.AgentId == performer).OnStage);
        session = Restored(session);
        var drinkingSeen = false;
        while (session.CurrentTick < GameSession.FestivalSlotEnds[0] && session.CaptureLivePerformance()!.Stage != LiveSetStage.Live)
        {
            session.AdvanceWithoutSnapshot(1);
            drinkingSeen |= session.CaptureMedical()!.Needs.Single(need => need.AgentId == performer).Intent == MedicalIntent.Drinking;
        }
        Assert.IsTrue(drinkingSeen, "The performer must physically arrive and drink, not have the need reset remotely.");
        Assert.IsTrue(session.CaptureMedical()!.Needs.Single(need => need.AgentId == performer).LastWaterTick >= 0);
        Assert.IsTrue(session.CaptureMedical()!.Evidence.Any(item => item.Id == "medical:water" && item.Description.Contains($"Person {performer} ")));
        Console.WriteLine($"water-delayed performer={performer} drank={drinkingSeen} waterFinished={session.CaptureMedical()!.Needs.Single(need => need.AgentId == performer).LastWaterTick} outcome={session.CaptureLivePerformance()!.Stage} actualStarted={session.CaptureLivePerformance()!.StartedTick} fixedEnd={GameSession.FestivalSlotEnds[0]}");
        if (session.CaptureLivePerformance()!.Stage == LiveSetStage.Live)
        {
            Assert.IsTrue(session.CaptureLivePerformance()!.Performers.All(person => person.OnStage));
            Assert.IsFalse(session.FestivalBandLate);
        }
        else Assert.AreEqual(LiveSetStage.Finished, session.CaptureLivePerformance()!.Stage, "Physical lateness must respect the fixed deadline.");
        Restored(session);
    }

    [TestMethod]
    public void BandDelayGrievanceRejectsLegacyOrPreScheduledOnsetTamper()
    {
        var legacy = GameSession.CreateDisorderCampaign(20260926).CapturePersistenceSnapshot();
        var fake = legacy.Disorder! with { People = legacy.Disorder!.People.Select((person, index) => index == 0
            ? person with { Grievance = DisorderGrievance.BandDelayed, GrievanceTick = 0 } : person).ToArray() };
        Assert.IsFalse(GameSession.Restore(legacy with { Disorder = fake }).IsSuccess);
        var session = Started(holdFirstPerformer: true);
        session.AdvanceWithoutSnapshot(1_600);
        var snapshot = session.CapturePersistenceSnapshot();
        var id = snapshot.Disorder!.People.First(person => person.Grievance == DisorderGrievance.BandDelayed).AgentId;
        Assert.IsFalse(GameSession.Restore(snapshot with { Disorder = snapshot.Disorder with { People = snapshot.Disorder.People.Select(person => person.AgentId == id
            ? person with { GrievanceTick = GameSession.FestivalSlotStarts[0] - 1 } : person).ToArray() } }).IsSuccess);
    }
}

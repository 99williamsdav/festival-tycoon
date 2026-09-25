using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class DisorderIncidentTests
{
    private static CommandResult Send(GameSession session, SessionCommand command) => session.Execute(new(
        new CommandId(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase,
        session.CurrentTick, session.NextSubmissionSequence, null, command));

    private static GameSession Restored(GameSession session)
    {
        var loaded = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(loaded.IsSuccess, loaded.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, loaded.Session!.CaptureSnapshot().AuthoritativeHash);
        return loaded.Session;
    }

    private static GameSession Started(ulong seed = 20260925)
    {
        var session = GameSession.CreateDisorderCampaign(seed);
        foreach (var id in new[] { "act.folk", "staff.steward", "equipment.buy" })
            Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand(id)).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        return session;
    }

    private static GameSession SecurityInjuryFixture()
    {
        for (ulong seed = 41; seed < 65; seed++)
        {
            var session = Started(seed);
            Assert.IsTrue(Send(session, new MedicalCommand(session.CaptureMedical()!.AtRiskGuestId, MedicalAction.GuideToRest)).IsAccepted);
            while (session.CaptureLivePerformance()!.Stage != LiveSetStage.Live && session.CurrentTick < 4_000)
                session.AdvanceWithoutSnapshot(1);
            Assert.IsTrue(Send(session, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
            while (!session.CaptureDisorder()!.People.Any(item => item.Grievance == DisorderGrievance.MusicCutoff &&
                   item.Stage == DisorderStage.Complaint) && session.CurrentTick < 4_000 &&
                   session.CapturePreparation()!.Status == PreparationStatus.Running)
                session.AdvanceWithoutSnapshot(1);
            if (session.CapturePreparation()!.Status != PreparationStatus.Running) continue;
            var target = session.CaptureDisorder()!.People.First(item => item.Grievance == DisorderGrievance.MusicCutoff &&
                item.Stage == DisorderStage.Complaint).AgentId;
            var field = typeof(GameSession).GetField("_disorder", BindingFlags.Instance | BindingFlags.NonPublic)!;
            field.SetValue(session, session.CaptureDisorder()! with { CalmingSkill = 3_500, ConfrontationSkill = 3_500 });
            if (!Send(session, new DisorderCommand(DisorderAction.DispatchSecurity, target)).IsAccepted) continue;
            while (!session.CaptureDisorder()!.SecurityIncapacitated && session.CurrentTick < 6_500 &&
                   session.CapturePreparation()!.Status == PreparationStatus.Running)
                session.AdvanceWithoutSnapshot(1);
            if (session.CaptureDisorder()!.SecurityIncapacitated)
            {
                Console.WriteLine($"security injury fixture seed={seed} tick={session.CurrentTick}");
                return session;
            }
        }
        Assert.Fail("No security injury route found in bounded natural seed sweep.");
        return null!;
    }

    [TestMethod]
    public void SecurityTraitsAndRosterRestoreBeforeAndAfterStart()
    {
        var session = GameSession.CreateDisorderCampaign(20260925);
        var state = session.CaptureDisorder()!;
        Assert.AreEqual(20, state.People.Length);
        Assert.IsTrue(state.People.Select(item => item.Temperament).Distinct().Count() > 1);
        Assert.AreEqual(1, session.CaptureMedical()!.Needs.Count(item => item.Profile == MedicalNeedProfile.Staff));
        session = Restored(session);
        foreach (var id in new[] { "act.folk", "staff.steward", "equipment.buy" })
            Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand(id)).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        session.AdvanceWithoutSnapshot(120);
        Restored(session);
    }

    [TestMethod]
    public void SafeMusicResetRemovesCutoffGrievanceWithoutRestartingOverload()
    {
        var session = Started();
        while (session.CaptureLivePerformance()!.Stage != LiveSetStage.Live && session.CurrentTick < 4_000)
            session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(LiveSetStage.Live, session.CaptureLivePerformance()!.Stage);
        Assert.IsTrue(Send(session, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
        session.AdvanceWithoutSnapshot(8);
        Assert.AreEqual(LiveSetStage.Interrupted, session.CaptureLivePerformance()!.Stage);
        session = Restored(session);
        session.AdvanceWithoutSnapshot(700);
        Assert.IsTrue(session.CaptureDisorder()!.People.Any(item => item.Grievance == DisorderGrievance.MusicCutoff &&
            item.Stage is DisorderStage.Complaint or DisorderStage.Agitated or DisorderStage.Argument));
        Assert.IsTrue(Send(session, new DisorderCommand(DisorderAction.RestoreMusic)).IsAccepted);
        Assert.AreEqual(80, session.CaptureEquipment()!.LoadPercent);
        session = Restored(session);
        session.AdvanceWithoutSnapshot(8);
        Assert.AreEqual(LiveSetStage.Live, session.CaptureLivePerformance()!.Stage);
        Assert.IsFalse(session.CaptureDisorder()!.People.Any(item => item.Grievance == DisorderGrievance.MusicCutoff));
        Restored(session);
    }

    [TestMethod]
    public void PhysicalThirstyWaterWaitCanCauseVisibleComplaintAndClosurePreservesEgress()
    {
        var session = Started();
        var atRisk = session.CaptureMedical()!.AtRiskGuestId;
        Assert.IsTrue(Send(session, new MedicalCommand(atRisk, MedicalAction.GuideToRest)).IsAccepted);
        while (!session.CaptureDisorder()!.Evidence.Any(item => item.Description.Contains("Hurry up!")) &&
               session.CurrentTick < 5_000)
            session.AdvanceWithoutSnapshot(1);
        var before = session.CaptureDisorder()!;
        Console.WriteLine($"water tick={session.CurrentTick} queue={session.CaptureMedical()!.WaterQueue.Length} complaints={before.Evidence.Count(item => item.Description.Contains("Hurry up!"))}");
        Assert.IsTrue(before.Evidence.Any(item => item.Description.Contains("Hurry up!")));
        var complainer = before.Evidence.First(item => item.Description.Contains("Hurry up!")).PersonId;
        session = Restored(session);
        Assert.IsTrue(Send(session, new DisorderCommand(DisorderAction.CloseWater)).IsAccepted);
        Assert.AreEqual(0, session.CaptureMedical()!.WaterQueue.Length);
        Assert.IsTrue(session.CaptureDisorder()!.WaterClosed);
        session = Restored(session);
        session.AdvanceWithoutSnapshot(8);
        Assert.IsFalse(session.CaptureDisorder()!.People.Any(item => item.Grievance == DisorderGrievance.WaterWait));
        Assert.IsTrue(Send(session, new DisorderCommand(DisorderAction.SafeEgress, complainer)).IsAccepted);
        session = Restored(session);
        while (!session.CapturePreparation()!.People.Single(item => item.AgentId == complainer).Departed && session.CurrentTick < 5_000)
            session.AdvanceWithoutSnapshot(1);
        Assert.IsTrue(session.CapturePreparation()!.People.Single(item => item.AgentId == complainer).Departed);
        Assert.IsTrue(Send(session, new DisorderCommand(DisorderAction.ReopenWater)).IsAccepted);
        Restored(session);
    }

    [TestMethod]
    public void SecurityDispatchTravelsAndEitherCalmsOrHonestlyEscalates()
    {
        var session = Started();
        var atRisk = session.CaptureMedical()!.AtRiskGuestId;
        Assert.IsTrue(Send(session, new MedicalCommand(atRisk, MedicalAction.GuideToRest)).IsAccepted);
        while (session.CaptureLivePerformance()!.Stage != LiveSetStage.Live && session.CurrentTick < 4_000)
            session.AdvanceWithoutSnapshot(1);
        Assert.IsTrue(Send(session, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
        while (!session.CaptureDisorder()!.People.Any(item => item.Grievance == DisorderGrievance.MusicCutoff &&
               item.Stage is DisorderStage.Complaint or DisorderStage.Agitated or DisorderStage.Argument) &&
               session.CurrentTick < 4_000)
            session.AdvanceWithoutSnapshot(1);
        var target = session.CaptureDisorder()!.People.First(item => item.Grievance == DisorderGrievance.MusicCutoff &&
            item.Stage is DisorderStage.Complaint or DisorderStage.Agitated or DisorderStage.Argument);
        var dispatch = Send(session, new DisorderCommand(DisorderAction.DispatchSecurity, target.AgentId));
        Assert.IsTrue(dispatch.IsAccepted, dispatch.Message);
        Assert.AreEqual(SecurityResponseStage.Travelling, session.CaptureDisorder()!.ResponseStage);
        Assert.AreEqual("disorder.security-dispatch", session.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == session.CaptureDisorder()!.SecurityId).IntentId);
        session = Restored(session);
        while (session.CaptureDisorder()!.ResponseStage is SecurityResponseStage.Travelling or SecurityResponseStage.Calming or SecurityResponseStage.Confronting &&
               session.CurrentTick < 6_000)
            session.AdvanceWithoutSnapshot(1);
        var response = session.CaptureDisorder()!;
        Console.WriteLine($"security={response.ResponseStage} {response.Response} tick={session.CurrentTick} skills={response.CalmingSkill}/{response.ConfrontationSkill}");
        Assert.IsTrue(response.Evidence.Any(item => item.Id == "security:dispatch"));
        Assert.IsTrue(response.ResponseStage is SecurityResponseStage.Completed or SecurityResponseStage.Failed);
        Restored(session);
    }

    [TestMethod]
    public void LowSkillSecurityFixtureCanBeInjuredAndPhysicallyTreated()
    {
        var session = SecurityInjuryFixture();
        session = Restored(session);
        while (!session.CaptureDisorder()!.SecurityIncapacitated && session.CurrentTick < 6_500 &&
               session.CapturePreparation()!.Status == PreparationStatus.Running)
            session.AdvanceWithoutSnapshot(1);
        var d = session.CaptureDisorder()!;
        Console.WriteLine($"injury tick={session.CurrentTick} response={d.ResponseStage} securityInjured={d.SecurityIncapacitated} evidence={string.Join(',', d.Evidence.TakeLast(6).Select(item => item.Id))}");
        Assert.IsTrue(d.SecurityIncapacitated);
        Assert.AreEqual(MedicalStage.Collapsed, session.CaptureMedical()!.Needs.Single(item => item.AgentId == d.SecurityId).Stage);
        session = Restored(session);
        var dispatch = Send(session, new MedicalCommand(d.SecurityId, MedicalAction.DispatchMedic));
        Assert.IsTrue(dispatch.IsAccepted, dispatch.Message);
        Assert.AreEqual(MedicalIntent.Collapsed, session.CaptureMedical()!.Needs.Single(item => item.AgentId == d.SecurityId).Intent);
        session = Restored(session);
        while (session.CaptureMedical()!.Needs.Single(item => item.AgentId == d.SecurityId).Stage != MedicalStage.Treated &&
               session.CurrentTick < 9_000 && session.CapturePreparation()!.Status == PreparationStatus.Running)
            session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalStage.Treated, session.CaptureMedical()!.Needs.Single(item => item.AgentId == d.SecurityId).Stage);
        Assert.IsFalse(session.CaptureDisorder()!.SecurityIncapacitated);
        Assert.AreEqual(0, session.CaptureLifecycleSnapshot()!.Casualties.Count);
        Restored(session);
    }

    [TestMethod]
    public void TimelySkilledSecurityCalmsAfterPhysicalArrival()
    {
        var session = Started();
        Assert.IsTrue(Send(session, new MedicalCommand(session.CaptureMedical()!.AtRiskGuestId, MedicalAction.GuideToRest)).IsAccepted);
        while (session.CaptureLivePerformance()!.Stage != LiveSetStage.Live && session.CurrentTick < 4_000)
            session.AdvanceWithoutSnapshot(1);
        Assert.IsTrue(Send(session, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
        while (!session.CaptureDisorder()!.People.Any(item => item.Grievance == DisorderGrievance.MusicCutoff &&
               item.Stage == DisorderStage.Complaint) && session.CurrentTick < 4_000)
            session.AdvanceWithoutSnapshot(1);
        var target = session.CaptureDisorder()!.People.First(item => item.Grievance == DisorderGrievance.MusicCutoff &&
            item.Stage == DisorderStage.Complaint).AgentId;
        var field = typeof(GameSession).GetField("_disorder", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(session, session.CaptureDisorder()! with { CalmingSkill = 8_000 });
        Assert.IsTrue(Send(session, new DisorderCommand(DisorderAction.DispatchSecurity, target)).IsAccepted);
        session = Restored(session);
        while (session.CaptureDisorder()!.ResponseStage is SecurityResponseStage.Travelling or SecurityResponseStage.Calming &&
               session.CurrentTick < 5_000)
            session.AdvanceWithoutSnapshot(1);
        var d = session.CaptureDisorder()!;
        Assert.AreEqual(SecurityResponseStage.Completed, d.ResponseStage, d.Response);
        Assert.IsTrue(d.Evidence.Any(item => item.Id == "security:calming"));
        Assert.IsTrue(d.Evidence.Any(item => item.Id == "security:calmed"));
        Assert.IsFalse(d.Evidence.Any(item => item.Id == "security:confrontation"));
        Restored(session);
    }

    [TestMethod]
    public void UnassistedSecurityInjuryProducesAttributedTerminalFixture()
    {
        var session = SecurityInjuryFixture();
        Assert.IsTrue(session.CaptureDisorder()!.SecurityIncapacitated);
        session = Restored(session);
        var injuryTick = session.CaptureMedical()!.Needs.Single(item => item.AgentId == session.CaptureDisorder()!.SecurityId).CollapseTick;
        while (session.CapturePreparation()!.Status == PreparationStatus.Running && session.CurrentTick < injuryTick + GameSession.DisorderInjuryDeathTicks + 1)
            session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(PreparationStatus.Failed, session.CapturePreparation()!.Status);
        Assert.AreEqual(1, session.CaptureLifecycleSnapshot()!.Casualties.Count);
        StringAssert.Contains(session.CaptureLifecycleSnapshot()!.Casualties[0].Cause, "Confrontation injury");
        StringAssert.Contains(session.CaptureLifecycleSnapshot()!.Casualties[0].Cause, "medic");
        Restored(session);
    }

    [TestMethod]
    public void CounteredMusicAndWaterAcrossSeedsHaveNoForcedDisorder()
    {
        var uncounteredComplaints = 0;
        var spontaneousDiffusions = 0;
        for (ulong seed = 51; seed < 57; seed++)
        {
            var session = Started(seed);
            Assert.IsTrue(Send(session, new MedicalCommand(session.CaptureMedical()!.AtRiskGuestId, MedicalAction.GuideToRest)).IsAccepted);
            Assert.IsTrue(Send(session, new DisorderCommand(DisorderAction.CloseWater)).IsAccepted);
            while (session.CaptureLivePerformance()!.Stage != LiveSetStage.Live && session.CurrentTick < 4_000)
                session.AdvanceWithoutSnapshot(1);
            Assert.IsTrue(Send(session, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
            session.AdvanceWithoutSnapshot(8);
            Assert.IsTrue(Send(session, new DisorderCommand(DisorderAction.RestoreMusic)).IsAccepted);
            session.AdvanceWithoutSnapshot(1_800);
            var d = session.CaptureDisorder()!;
            Assert.IsFalse(d.Evidence.Any(item => item.Id == "disorder:fight"), $"seed={seed}");
            Assert.IsFalse(d.Evidence.Any(item => item.Id.Contains("complaint")), $"seed={seed}");
            Assert.AreEqual(0, session.CaptureLifecycleSnapshot()!.Casualties.Count, $"seed={seed}");
            Restored(session);

            var uncountered = Started(seed);
            Assert.IsTrue(Send(uncountered, new MedicalCommand(uncountered.CaptureMedical()!.AtRiskGuestId, MedicalAction.GuideToRest)).IsAccepted);
            while (uncountered.CaptureLivePerformance()!.Stage != LiveSetStage.Live && uncountered.CurrentTick < 4_000)
                uncountered.AdvanceWithoutSnapshot(1);
            Assert.IsTrue(Send(uncountered, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
            uncountered.AdvanceWithoutSnapshot(1_800);
            uncounteredComplaints += uncountered.CaptureDisorder()!.Evidence.Count(item => item.Id.Contains("complaint"));
            spontaneousDiffusions += uncountered.CaptureDisorder()!.Evidence.Count(item => item.Id == "disorder:diffused");
            Console.WriteLine($"seed={seed} countered=0 uncounteredComplaints={uncountered.CaptureDisorder()!.Evidence.Count(item => item.Id.Contains("complaint"))} diffusions={uncountered.CaptureDisorder()!.Evidence.Count(item => item.Id == "disorder:diffused")}");
        }
        Assert.IsTrue(uncounteredComplaints > 0);
        Assert.IsTrue(spontaneousDiffusions > 0, "At least one ongoing argument should diffuse naturally in the sweep.");
    }
}

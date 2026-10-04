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
        // High Pressure is the perk these worlds were tuned with, before the catalogue grew and reshuffled the hands.
        var session = BuildSession.Planned(seed, "high-pressure");
        foreach (var id in BuildSession.CrewIds(session).Concat(new[] { "equipment.rent" }))
            Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand(id)).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        return session;
    }

    private static GameSession SecurityInjuryFixture()
    {
        for (ulong seed = 41; seed < 65; seed++)
        {
            var session = Started(seed);
            while (session.CaptureLivePerformance()!.Stage != LiveSetStage.Live && session.CurrentTick < 8_000)
                session.AdvanceWithoutSnapshot(1);
            Assert.IsTrue(Send(session, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
            while (!session.CaptureDisorder()!.People.Any(item => item.Grievance == DisorderGrievance.MusicCutoff &&
                   item.Stage == DisorderStage.Complaint) && session.CurrentTick < 8_000 &&
                   session.CapturePreparation()!.Status == PreparationStatus.Running)
                session.AdvanceWithoutSnapshot(1);
            if (session.CapturePreparation()!.Status != PreparationStatus.Running) continue;
            var target = session.CaptureDisorder()!.People.First(item => item.Grievance == DisorderGrievance.MusicCutoff &&
                item.Stage == DisorderStage.Complaint).AgentId;
            var field = typeof(GameSession).GetProperty("DisorderView", BindingFlags.Instance | BindingFlags.NonPublic)!;
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
    public void SafeMusicResetRemovesCutoffGrievanceWithoutRestartingOverload()
    {
        var session = Started();
        while (session.CaptureLivePerformance()!.Stage != LiveSetStage.Live && session.CurrentTick < 8_000)
            session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(LiveSetStage.Live, session.CaptureLivePerformance()!.Stage);
        Assert.IsTrue(Send(session, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
        session.AdvanceWithoutSnapshot(8);
        Assert.AreEqual(LiveSetStage.Interrupted, session.CaptureLivePerformance()!.Stage);
        session = Restored(session);
        // Keen listeners left in silence start to grumble; how soon depends on who's at the front.
        bool Grumbling() => session.CaptureDisorder()!.People.Any(item => item.Grievance == DisorderGrievance.MusicCutoff &&
            item.Stage is DisorderStage.Complaint or DisorderStage.Agitated or DisorderStage.Argument);
        for (var guard = 0; guard < 300 && !Grumbling(); guard++) session.AdvanceWithoutSnapshot(8);
        Assert.IsTrue(Grumbling());
        Assert.IsTrue(Send(session, new DisorderCommand(DisorderAction.RestoreMusic)).IsAccepted);
        Assert.AreEqual(EquipmentStage.Resolved, session.CaptureEquipment()!.Stage);
        session = Restored(session);
        session.AdvanceWithoutSnapshot(8);
        Assert.AreEqual(LiveSetStage.Live, session.CaptureLivePerformance()!.Stage);
        Assert.IsFalse(session.CaptureDisorder()!.People.Any(item => item.Grievance == DisorderGrievance.MusicCutoff));
        Restored(session);
    }

    [TestMethod]
    public void SecurityDispatchTravelsAndEitherCalmsOrHonestlyEscalates()
    {
        var session = Started();
        var atRisk = BuildSession.LastGuest(session);
        while (session.CaptureLivePerformance()!.Stage != LiveSetStage.Live && session.CurrentTick < 8_000)
            session.AdvanceWithoutSnapshot(1);
        Assert.IsTrue(Send(session, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
        while (!session.CaptureDisorder()!.People.Any(item => item.Grievance == DisorderGrievance.MusicCutoff &&
               item.Stage is DisorderStage.Complaint or DisorderStage.Agitated or DisorderStage.Argument) &&
               session.CurrentTick < 8_000)
            session.AdvanceWithoutSnapshot(1);
        var target = session.CaptureDisorder()!.People.First(item => item.Grievance == DisorderGrievance.MusicCutoff &&
            item.Stage is DisorderStage.Complaint or DisorderStage.Agitated or DisorderStage.Argument);
        var dispatch = Send(session, new DisorderCommand(DisorderAction.DispatchSecurity, target.AgentId));
        Assert.IsTrue(dispatch.IsAccepted, dispatch.Message);
        Assert.AreEqual(SecurityResponseStage.Travelling, session.CaptureDisorder()!.Stewards[0].Stage);
        Assert.AreEqual("disorder.security-dispatch", session.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == session.CaptureDisorder()!.SecurityId).IntentId);
        session = Restored(session);
        while (session.CaptureDisorder()!.Stewards[0].Stage is SecurityResponseStage.Travelling or SecurityResponseStage.Calming or SecurityResponseStage.Confronting &&
               session.CurrentTick < 8_000)
            session.AdvanceWithoutSnapshot(1);
        var response = session.CaptureDisorder()!;
        Console.WriteLine($"security={response.Stewards[0].Stage} {response.Stewards[0].Description} tick={session.CurrentTick} skills={response.CalmingSkill}/{response.ConfrontationSkill}");
        Assert.IsTrue(response.Evidence.Any(item => item.Id == "security:dispatch"));
        Assert.IsTrue(response.Stewards[0].Stage is SecurityResponseStage.Completed or SecurityResponseStage.Failed);
        Restored(session);
    }

    [TestCategory("Slow")]
    [TestMethod]
    public void CounteredMusicAndWaterAcrossSeedsHaveNoForcedDisorder()
    {
        var uncounteredComplaints = 0;
        var spontaneousDiffusions = 0;
        var uncounteredConfrontations = 0;
        for (ulong seed = 51; seed < 57; seed++)
        {
            var session = Started(seed);
            Assert.IsTrue(Send(session, new DisorderCommand(DisorderAction.CloseWater)).IsAccepted);
            while (session.CaptureLivePerformance()!.Stage != LiveSetStage.Live && session.CurrentTick < 8_000)
                session.AdvanceWithoutSnapshot(1);
            Assert.IsTrue(Send(session, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
            session.AdvanceWithoutSnapshot(8);
            Assert.IsTrue(Send(session, new DisorderCommand(DisorderAction.RestoreMusic)).IsAccepted);
            session.AdvanceWithoutSnapshot(2_600);
            var d = session.CaptureDisorder()!;
            Assert.IsFalse(d.Evidence.Any(item => item.Id == "disorder:fight"), $"seed={seed}");
            Assert.AreEqual(0, d.Incidents.Length, $"seed={seed}");
            Assert.IsFalse(d.Evidence.Any(item => item.Id.Contains("complaint")), $"seed={seed}");
            Assert.AreEqual(0, session.CaptureLifecycleSnapshot()!.Casualties.Count, $"seed={seed}");
            Restored(session);

            var uncountered = Started(seed);
            while (uncountered.CaptureLivePerformance()!.Stage != LiveSetStage.Live && uncountered.CurrentTick < 8_000)
                uncountered.AdvanceWithoutSnapshot(1);
            Assert.IsTrue(Send(uncountered, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
            uncountered.AdvanceWithoutSnapshot(2_600);
            uncounteredComplaints += uncountered.CaptureDisorder()!.Evidence.Count(item => item.Id.Contains("complaint"));
            spontaneousDiffusions += uncountered.CaptureDisorder()!.Evidence.Count(item => item.Id == "disorder:diffused");
            uncounteredConfrontations += uncountered.CaptureDisorder()!.Incidents.Length;
            Console.WriteLine($"seed={seed} counteredFights=0 uncounteredFights={uncountered.CaptureDisorder()!.Incidents.Length} complaints={uncountered.CaptureDisorder()!.Evidence.Count(item => item.Id.Contains("complaint"))} diffusions={uncountered.CaptureDisorder()!.Evidence.Count(item => item.Id == "disorder:diffused")}");
        }
        Assert.IsTrue(uncounteredComplaints > 0);
        Assert.IsTrue(spontaneousDiffusions > 0, "At least one ongoing argument should diffuse naturally in the sweep.");
        Assert.IsTrue(uncounteredConfrontations > 0, "Unresolved grievances must show a materially higher fight rate than the countered zero-fight states.");
    }

}

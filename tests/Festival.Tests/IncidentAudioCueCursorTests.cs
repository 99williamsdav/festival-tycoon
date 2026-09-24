using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class IncidentAudioCueCursorTests
{
    private static CommandResult Send(GameSession session, SessionCommand command) => session.Execute(new(
        new CommandId(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase,
        session.CurrentTick, session.NextSubmissionSequence, null, command));

    private static GameSession Started(bool medical)
    {
        var session = medical ? GameSession.CreateMedicalCampaign(20260922) : GameSession.CreateEquipmentCampaign(2);
        foreach (var offer in new[] { "act.folk", "staff.steward", "equipment.buy" })
            Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand(offer)).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        return session;
    }

    [TestMethod]
    public void EquipmentExplosionAndWitnessScreamAreOnceOnlyAndNeverReplayOnLoad()
    {
        var session = Started(medical: false);
        var cursor = new IncidentAudioCueCursor();
        cursor.Reset(session.CaptureEquipment(), session.CaptureMedical());
        session.AdvanceWithoutSnapshot(7_200);
        Assert.AreEqual(EquipmentStage.Terminal, session.CaptureEquipment()!.Stage);
        var cues = cursor.Observe(session.CaptureEquipment(), session.CaptureMedical(), session.CurrentTick);
        CollectionAssert.AreEqual(new[] { IncidentAudioCueKind.GeneratorExplosion, IncidentAudioCueKind.DeathScream },
            cues.Select(item => item.Kind).ToArray());
        Assert.AreEqual(0, cursor.Observe(session.CaptureEquipment(), session.CaptureMedical(), session.CurrentTick).Count);
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        cursor.Reset(restored.Session!.CaptureEquipment(), restored.Session.CaptureMedical());
        Assert.AreEqual(0, cursor.Observe(restored.Session.CaptureEquipment(), restored.Session.CaptureMedical(), restored.Session.CurrentTick).Count);
        restored.Session.AdvanceWithoutSnapshot(100);
        Assert.AreEqual(0, cursor.Observe(restored.Session.CaptureEquipment(), restored.Session.CaptureMedical(), restored.Session.CurrentTick).Count);
    }

    [TestMethod]
    public void MedicalDeathScreamIsOnceOnlyAndOldHistoricalEventsStaySilent()
    {
        var session = Started(medical: true);
        var cursor = new IncidentAudioCueCursor();
        cursor.Reset(session.CaptureEquipment(), session.CaptureMedical());
        session.AdvanceWithoutSnapshot(6_200);
        Assert.AreEqual(MedicalStage.Terminal, session.CaptureMedical()!.Stage);
        var cues = cursor.Observe(session.CaptureEquipment(), session.CaptureMedical(), session.CurrentTick);
        CollectionAssert.AreEqual(new[] { IncidentAudioCueKind.DeathScream }, cues.Select(item => item.Kind).ToArray());
        Assert.AreEqual(0, cursor.Observe(session.CaptureEquipment(), session.CaptureMedical(), session.CurrentTick).Count);
        cursor.Reset(session.CaptureEquipment(), session.CaptureMedical());
        Assert.AreEqual(0, cursor.Observe(session.CaptureEquipment(), session.CaptureMedical(), session.CurrentTick).Count);
    }

    [TestMethod]
    public void StaleEventsDoNotFireAfterFastForward()
    {
        var session = Started(medical: true);
        var cursor = new IncidentAudioCueCursor();
        cursor.Reset(session.CaptureEquipment(), session.CaptureMedical());
        session.AdvanceWithoutSnapshot(6_200);
        Assert.AreEqual(0, cursor.Observe(session.CaptureEquipment(), session.CaptureMedical(), session.CurrentTick + 81).Count);
    }
}

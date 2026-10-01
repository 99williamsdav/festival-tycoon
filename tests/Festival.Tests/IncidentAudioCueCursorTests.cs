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
        var session = medical ? BuildSession.Planned(20260922) : BuildSession.Planned(2);
        foreach (var offer in new[] { "staff.sound.1", "staff.medic.1", "staff.steward.1", "equipment.buy" })
            Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand(offer)).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        return session;
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

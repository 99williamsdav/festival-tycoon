using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class PerformerIdentityTests
{
    [TestMethod]
    public void BookedBandRoleTitlesRetainIdentityAndFollowReorderAndRestore()
    {
        var session = GameSession.CreateTimetableCampaign(20260922);
        var people = session.CapturePreparation()!.People;
        var roles = session.CaptureProgramme()!.Performers;
        Assert.AreEqual("Unbooked Set 1 Lead", session.FestivalPerformerTitle(roles[0].AgentId));
        CommandResult Send(string[] acts) => session.Execute(new(new CommandId(session.NextSubmissionSequence + 1),
            session.CampaignId, session.Phase, session.CurrentTick, session.NextSubmissionSequence, null, new SetProgrammeCommand(acts)));
        Assert.IsTrue(Send(["act.orchard-chorus", "act.barnstorm-circuit", "act.neon-postcards"]).IsAccepted);
        Assert.AreEqual("Orchard Chorus Drummer", session.FestivalPerformerTitle(roles[2].AgentId));
        Assert.AreEqual("Barnstorm Circuit Lead", session.FestivalPerformerTitle(roles[3].AgentId));
        Assert.IsTrue(Send(["act.barnstorm-circuit", "act.orchard-chorus", "act.neon-postcards"]).IsAccepted);
        Assert.AreEqual("Barnstorm Circuit Drummer", session.FestivalPerformerTitle(roles[2].AgentId));
        CollectionAssert.AreEqual(people.Select(person => (person.AgentId, person.Name, person.Role)).ToArray(),
            session.CapturePreparation()!.People.Select(person => (person.AgentId, person.Name, person.Role)).ToArray());
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual("Barnstorm Circuit Drummer", restored.Session.FestivalPerformerTitle(roles[2].AgentId));
        Assert.IsNull(GameSession.CreateDisorderCampaign(20260922).FestivalPerformerTitle(roles[0].AgentId));
    }
}

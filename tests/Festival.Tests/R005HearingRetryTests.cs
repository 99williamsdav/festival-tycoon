using Festival.Simulation;
using Festival.Persistence;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class R005HearingRetryTests
{
    private static CommandResult Send(GameSession session, SessionCommand command) => session.Execute(new(
        new CommandId(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase,
        session.CurrentTick, session.NextSubmissionSequence, null, command));

    private static GameSession Restored(GameSession session)
    {
        var result = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, result.Session!.CaptureSnapshot().AuthoritativeHash);
        return result.Session;
    }

    private static void Book(GameSession session, bool buyRig, bool buyStock, bool worker = false)
    {
        foreach (var id in BuildSession.CrewIds(session)
                     .Concat(buyRig ? ["equipment.buy"] : Array.Empty<string>())
                     .Concat(buyStock ? ["contract.stock"] : Array.Empty<string>())
                     .Concat(worker ? ["maintenance.worker"] : Array.Empty<string>()))
            Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand(id)).IsAccepted, id);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
    }

    [TestMethod]
    public void CommunitySharingCapsFastDrinkersAndCannotBeCommittedTwice()
    {
        var session = BuildSession.Planned(20260922);
        StringAssert.Contains(session.CommunityWaterShareDisclosure!, "12 thirst units/tick");
        Assert.IsTrue(Send(session, new CommitCommunityWaterShareCommand()).IsAccepted);
        Assert.IsFalse(Send(session, new CommitCommunityWaterShareCommand()).IsAccepted);
        Assert.AreEqual(1, session.CapturePreparation()!.CommunityShareAttempt);
        Assert.AreEqual(1, session.CaptureLifecycleSnapshot()?.FavourBalance ?? 1);
        Book(session, buyRig: true, buyStock: false);
        Assert.IsTrue(session.CommunityWaterShareActive);
        for (ulong id = 1; id <= 8; id++)
            Assert.AreEqual(Math.Min(12, GameSession.MedicalDrinkThirstPerTickFor(id)), session.EffectiveMedicalDrinkThirstPerTickFor(id));
        Restored(session);
    }

}

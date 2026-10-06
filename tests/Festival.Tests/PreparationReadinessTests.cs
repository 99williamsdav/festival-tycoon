using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class PreparationReadinessTests
{
    private static CommandEnvelope Envelope(GameSession session, SessionCommand command) => new(
        new CommandId(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase,
        session.CurrentTick, session.NextSubmissionSequence, null, command);

    private static void Accept(GameSession session, SessionCommand command)
    {
        var result = session.Execute(Envelope(session, command));
        Assert.IsTrue(result.IsAccepted, result.Message);
    }

    private static GameSession VerifyReadModel(GameSession session, params PreparationStartOwner[] owners)
    {
        var hash = session.CaptureSnapshot().AuthoritativeHash;
        var blockers = session.GetPreparationStartBlockers();
        CollectionAssert.AreEqual(owners, blockers.Select(blocker => blocker.Owner).ToArray());
        Assert.IsTrue(blockers.All(blocker => !string.IsNullOrWhiteSpace(blocker.Message)));
        var issue = session.ValidateCommand(Envelope(session, new StartPreparedEditionCommand()));
        Assert.AreEqual(owners.Length == 0, issue is null);
        if (owners.Length != 0)
        {
            Assert.AreEqual(CommandReasonCode.InvalidParameter, issue!.ReasonCode);
            Assert.AreEqual("Book three acts and hire the required staff before opening.", issue.Message);
        }
        Assert.AreEqual(hash, session.CaptureSnapshot().AuthoritativeHash);
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(hash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
        CollectionAssert.AreEqual(blockers.ToArray(), restored.Session.GetPreparationStartBlockers().ToArray());
        return restored.Session;
    }

    [TestMethod]
    public void TimetableShowsBothOwningTabsThenOnlyUnresolvedTabAndClearsWhenReady()
    {
        var session = BuildSession.Drafted(20260927);
        session = VerifyReadModel(session, PreparationStartOwner.Programme, PreparationStartOwner.Staff, PreparationStartOwner.Staff, PreparationStartOwner.Staff);
        Accept(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.overdue-library-books", "act.glitter-rota"]));
        session = VerifyReadModel(session, PreparationStartOwner.Staff, PreparationStartOwner.Staff, PreparationStartOwner.Staff);
        foreach (var hire in BuildSession.Crew(session)) Accept(session, hire);
        session = VerifyReadModel(session);
        Assert.AreEqual(0, session.CapturePreparation()!.OwnedEquipment.Length);
        Assert.AreEqual(0, session.CapturePreparation()!.Rentals.Length);
        Assert.IsFalse(session.CapturePreparation()!.AcceptedOffers.Contains("contract.stock"));
        Accept(session, new StartPreparedEditionCommand());
        Assert.AreEqual(0, session.GetPreparationStartBlockers().Count);
    }

    [TestMethod]
    public void MaintenanceAndOptionalEquipmentDoNotHideTheActualStaffBlocker()
    {
        var session = BuildSession.Planned(20260927);
        Accept(session, new AcceptPreparationOfferCommand("maintenance.worker"));
        Accept(session, new AcceptPreparationOfferCommand("equipment.rent"));
        VerifyReadModel(session, PreparationStartOwner.Staff, PreparationStartOwner.Staff, PreparationStartOwner.Staff);
    }

    [TestMethod]
    public void BuildChecklistKeepsEveryRequirementVisibleAcrossCompletionRemovalAndReload()
    {
        var session = GameSession.CreateBuildCampaign(20260929, FestivalStanding.Established);
        var originalHash = session.CaptureSnapshot().AuthoritativeHash;
        var initial = session.GetPreparationStartRequirements();
        CollectionAssert.AreEqual(new[] { "water", "toilet", "first-aid", "steward-post", "programme", "staff", "medic", "steward", "budget" },
            initial.Select(item => item.Id).ToArray());
        Assert.AreEqual(8, initial.Count(item => !item.Complete));
        Assert.IsTrue(initial.Single(item => item.Id == "budget").Complete);
        Assert.AreEqual(originalHash, session.CaptureSnapshot().AuthoritativeHash);

        var perk = session.CapturePerks()!;
        Accept(session, new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
        Accept(session, new UseDefaultBuildLayoutCommand());
        foreach (var id in new[] { "water", "toilet", "first-aid", "steward-post" })
            Assert.IsTrue(session.GetPreparationStartRequirements().Single(item => item.Id == id).Complete);
        Accept(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.overdue-library-books", "act.glitter-rota"]));
        foreach (var hire in BuildSession.Crew(session)) Accept(session, hire);
        Assert.IsTrue(session.GetPreparationStartRequirements().All(item => item.Complete));
        Assert.AreEqual(0, session.GetPreparationStartBlockers().Count);

        Accept(session, new RemoveBuildServiceCommand("water.main"));
        var removed = session.GetPreparationStartRequirements();
        Assert.AreEqual(9, removed.Count);
        Assert.IsFalse(removed.Single(item => item.Id == "water").Complete);
        Assert.AreEqual(1, session.GetPreparationStartBlockers().Count);
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        CollectionAssert.AreEqual(removed.ToArray(), restored.Session!.GetPreparationStartRequirements().ToArray());
    }
}

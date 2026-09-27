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
            Assert.AreEqual("Book one act and one worker for the fixed protected roster.", issue.Message);
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
        var session = GameSession.CreateTimetableCampaign(20260927);
        session = VerifyReadModel(session, PreparationStartOwner.Programme, PreparationStartOwner.Staff);
        Accept(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"]));
        session = VerifyReadModel(session, PreparationStartOwner.Staff);
        Accept(session, new AcceptPreparationOfferCommand("staff.steward"));
        session = VerifyReadModel(session);
        Assert.AreEqual(0, session.CapturePreparation()!.OwnedEquipment.Length);
        Assert.AreEqual(0, session.CapturePreparation()!.Rentals.Length);
        Assert.IsFalse(session.CapturePreparation()!.AcceptedOffers.Contains("contract.stock"));
        Accept(session, new StartPreparedEditionCommand());
        Assert.AreEqual(0, session.GetPreparationStartBlockers().Count);
    }

    [TestMethod]
    public void LegacyOneActGateAndAnyStaffContractSemanticsArePreserved()
    {
        var session = GameSession.CreateDisorderCampaign(20260927);
        session = VerifyReadModel(session, PreparationStartOwner.Programme, PreparationStartOwner.Staff);
        Accept(session, new ApplyStaffFoundationEffectCommand("staff.medic-slot"));
        Accept(session, new AcceptPreparationOfferCommand("staff.extra-medic"));
        session = VerifyReadModel(session, PreparationStartOwner.Programme);
        Accept(session, new AcceptPreparationOfferCommand("act.folk"));
        session = VerifyReadModel(session);
        Assert.IsFalse(session.CapturePreparation()!.AcceptedOffers.Contains("staff.steward"));
        Accept(session, new StartPreparedEditionCommand());
    }

    [TestMethod]
    public void MaintenanceAndOptionalEquipmentDoNotHideTheActualStaffBlocker()
    {
        var session = GameSession.CreateDisorderCampaign(20260927);
        Accept(session, new AcceptPreparationOfferCommand("maintenance.worker"));
        Accept(session, new AcceptPreparationOfferCommand("equipment.buy"));
        Accept(session, new AcceptPreparationOfferCommand("act.folk"));
        VerifyReadModel(session, PreparationStartOwner.Staff);
    }

    [TestMethod]
    public void PendingPerkUsesItsGlobalGuardAndDoesNotInventAnotherOwningTab()
    {
        var session = GameSession.CreatePerkCampaign(20260927);
        var hash = session.CaptureSnapshot().AuthoritativeHash;
        var blockers = session.GetPreparationStartBlockers();
        CollectionAssert.AreEqual(new[] { PreparationStartOwner.Programme, PreparationStartOwner.Staff },
            blockers.Select(blocker => blocker.Owner).ToArray());
        var issue = session.ValidateCommand(Envelope(session, new StartPreparedEditionCommand()));
        Assert.AreEqual("Choose a festival perk before preparation.", issue!.Message);
        Assert.AreEqual(CommandReasonCode.WrongPhase, issue.ReasonCode);
        Assert.AreEqual(hash, session.CaptureSnapshot().AuthoritativeHash);
        var perks = session.CapturePerks()!;
        Accept(session, new ChoosePerkCommand(perks.DraftAttempt, perks.Cursor, perks.Hand[0]));
        VerifyReadModel(session, PreparationStartOwner.Programme, PreparationStartOwner.Staff);
    }
}

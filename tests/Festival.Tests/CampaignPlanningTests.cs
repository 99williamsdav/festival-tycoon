using Festival.Persistence;
using Festival.Simulation;
using Festival.Simulation.Fixtures;

namespace Festival.Tests;

[TestClass]
public sealed class CampaignPlanningTests
{
    private static readonly SaveCompatibility Compatibility = new("m1-test-build", "content-catalogue-v1-d7e759", "m1-rules-v1");

    [TestMethod]
    public void CampaignCreationUsesFixedFarmLoanForecastAndDeterministicCosmetics()
    {
        var first = GameSession.CreateCampaign(42);
        var second = GameSession.CreateCampaign(42);
        var campaign = first.CaptureSnapshot().Campaign!;

        Assert.AreEqual(SessionPhase.Planning, first.Phase);
        Assert.AreEqual(8, campaign.PlanningWeek);
        Assert.AreEqual(CampaignDefaults.SiteId, campaign.SiteId);
        Assert.AreEqual(campaign.FestivalName, second.CaptureSnapshot().Campaign!.FestivalName);
        Assert.AreEqual(campaign.Palette, second.CaptureSnapshot().Campaign!.Palette);
        Assert.AreEqual(80_000, first.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        Assert.AreEqual(80_000, campaign.Loan.OutstandingPrincipalPennies);
        Assert.AreEqual(16_000, campaign.Loan.PrincipalDueAtSettlementPennies);
        Assert.AreEqual(6_400, campaign.Loan.InterestDueAtSettlementPennies);
        Assert.AreEqual(5, campaign.Loan.RemainingEditions);
        Assert.IsTrue(campaign.LedgerTransactions.Single().IsBalanced);
    }

    [TestMethod]
    public void RenameAndPaletteAffectSaveChecksumButNotGameplayHash()
    {
        var session = GameSession.CreateCampaign(73);
        var gameplayBefore = session.CaptureSnapshot().AuthoritativeHash;
        var checksumBefore = SaveFileAdapter.ComputePayloadChecksum(session.CapturePersistenceSnapshot());

        session.RenameFestival("Acre & Ink");
        session.SelectPalette(FestivalPalette.Berry);

        Assert.AreEqual(gameplayBefore, session.CaptureSnapshot().AuthoritativeHash);
        Assert.AreNotEqual(checksumBefore, SaveFileAdapter.ComputePayloadChecksum(session.CapturePersistenceSnapshot()));
        Assert.AreEqual("Acre & Ink", session.CaptureSnapshot().Campaign!.FestivalName);
        Assert.AreEqual(FestivalPalette.Berry, session.CaptureSnapshot().Campaign!.Palette);
    }

    [TestMethod]
    public void CommitmentConfirmsOnceAndInsufficientFundsRejectsAtomically()
    {
        var session = GameSession.CreateCampaign(11);
        var confirm = Envelope(session, 1, new ConfirmPlanningCommitmentCommand(CampaignDefaults.BasicAdministrationCommitmentId));
        Assert.IsTrue(session.Execute(confirm).IsAccepted);
        var confirmedHash = session.CaptureSnapshot().AuthoritativeHash;

        var duplicateId = session.Execute(confirm);
        Assert.AreEqual(CommandReasonCode.DuplicateCommand, duplicateId.ReasonCode);
        var duplicateUi = session.Execute(Envelope(session, 2, new ConfirmPlanningCommitmentCommand(CampaignDefaults.BasicAdministrationCommitmentId)));
        Assert.AreEqual(CommandReasonCode.AlreadyCommitted, duplicateUi.ReasonCode);
        Assert.AreEqual(confirmedHash, session.CaptureSnapshot().AuthoritativeHash);

        var poor = CampaignPlanningFixture.CreateWithOpeningCash(3_999);
        var poorBefore = poor.CaptureSnapshot().AuthoritativeHash;
        var rejected = poor.Execute(Envelope(poor, 1, new ConfirmPlanningCommitmentCommand(CampaignDefaults.BasicAdministrationCommitmentId)));
        Assert.AreEqual(CommandReasonCode.InsufficientFunds, rejected.ReasonCode);
        Assert.AreEqual(poorBefore, poor.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(3_999, poor.CaptureSnapshot().FestivalFinances.Single().CashPennies);
    }

    [TestMethod]
    public void ExactlyEightAutosavedManualAdvancesReachOpeningCheckAndPayOnce()
    {
        WithTemporaryDirectory(directory =>
        {
            var session = GameSession.CreateCampaign(20260922);
            Assert.IsTrue(session.Execute(Envelope(session, 1,
                new ConfirmPlanningCommitmentCommand(CampaignDefaults.BasicAdministrationCommitmentId))).IsAccepted);
            for (var index = 0; index < 8; index++)
            {
                var result = PlanningAdvanceCoordinator.Advance(
                    directory, session, Compatibility, DateTimeOffset.UnixEpoch.AddMinutes(index), index,
                    Envelope(session, (ulong)(index + 2), new AdvancePlanningWeekCommand()));
                Assert.IsTrue(result.IsSuccess, result.Message);
                Assert.IsNotNull(result.Digest);
            }

            var snapshot = session.CaptureSnapshot();
            Assert.AreEqual(SessionPhase.OpeningCheck, snapshot.Phase);
            Assert.AreEqual(0, snapshot.Campaign!.PlanningWeek);
            Assert.AreEqual(0, snapshot.CurrentTick, "Planning does not advance fixed simulation ticks.");
            Assert.AreEqual(76_000, snapshot.FestivalFinances.Single().CashPennies);
            Assert.AreEqual(8, snapshot.Campaign.WeeklyDigests.Count);
            Assert.AreEqual(1, snapshot.Campaign.LedgerTransactions.Count(item => item.Reason == "Basic administration and cover"));
            Assert.IsTrue(snapshot.Campaign.LedgerTransactions.All(item => item.IsBalanced));
            Assert.AreEqual(80_000, snapshot.Campaign.Loan.OutstandingPrincipalPennies);
            Assert.AreEqual(CommandReasonCode.WrongPhase,
                session.Execute(Envelope(session, 10, new AdvancePlanningWeekCommand())).ReasonCode);
            Assert.AreEqual(3, Directory.GetFiles(directory, "autosave-*.ftsave").Length);
        });
    }

    [TestMethod]
    public void AutosaveFailurePreservesPriorGoodSaveAndDoesNotAdvance()
    {
        WithTemporaryDirectory(directory =>
        {
            var session = GameSession.CreateCampaign(99);
            var before = session.CaptureSnapshot();
            var first = AutosaveRotation.Save(directory, session, Compatibility, DateTimeOffset.UnixEpoch, 0);
            Assert.IsTrue(first.IsSuccess, first.Error);
            var result = PlanningAdvanceCoordinator.Advance(
                directory, session, Compatibility, DateTimeOffset.UnixEpoch.AddMinutes(1), 0,
                Envelope(session, 1, new AdvancePlanningWeekCommand()),
                point => throw new IOException($"Injected {point}"));

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains(result.Message, "mandatory autosave failed");
            Assert.AreEqual(before.AuthoritativeHash, session.CaptureSnapshot().AuthoritativeHash);
            Assert.AreEqual(8, session.CaptureSnapshot().Campaign!.PlanningWeek);
            var prior = AutosaveRotation.LoadNewestValid(directory, Compatibility);
            Assert.IsTrue(prior.IsSuccess, prior.Error);
            Assert.AreEqual(before.AuthoritativeHash, prior.Session!.CaptureSnapshot().AuthoritativeHash);
        });
    }

    [TestMethod]
    public void SaveReloadKeepsCampaignCosmeticsDismissalCommitmentsDigestsAndFutureDeterminism()
    {
        WithTemporaryDirectory(directory =>
        {
            var uninterrupted = GameSession.CreateCampaign(551);
            uninterrupted.RenameFestival("The Quiet Acre");
            uninterrupted.SelectPalette(FestivalPalette.River);
            Assert.IsTrue(uninterrupted.Execute(Envelope(uninterrupted, 1,
                new DismissCampaignTipCommand("tip.finance.opening-loan"))).IsAccepted);
            Assert.IsTrue(uninterrupted.Execute(Envelope(uninterrupted, 2,
                new ConfirmPlanningCommitmentCommand(CampaignDefaults.BasicAdministrationCommitmentId))).IsAccepted);
            Assert.IsTrue(PlanningAdvanceCoordinator.Advance(
                directory, uninterrupted, Compatibility, DateTimeOffset.UnixEpoch, 0,
                Envelope(uninterrupted, 3, new AdvancePlanningWeekCommand())).IsSuccess);
            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "campaign", new SaveWriteRequest(
                uninterrupted, Compatibility, "test", DateTimeOffset.UnixEpoch.AddMinutes(1))).IsSuccess);
            var loaded = SaveFileAdapter.LoadSlot(directory, "campaign", Compatibility);
            Assert.IsTrue(loaded.IsSuccess, loaded.Error);

            var restored = loaded.Session!;
            var campaign = restored.CaptureSnapshot().Campaign!;
            Assert.AreEqual("The Quiet Acre", campaign.FestivalName);
            Assert.AreEqual(FestivalPalette.River, campaign.Palette);
            CollectionAssert.Contains(campaign.DismissedTipIds.ToArray(), "tip.finance.opening-loan");
            Assert.AreEqual(uninterrupted.CaptureSnapshot().AuthoritativeHash, restored.CaptureSnapshot().AuthoritativeHash);
            Assert.AreEqual(uninterrupted.CaptureCampaignPlanningSnapshot()!.WeeklyDigests[0].Summary, campaign.WeeklyDigests[0].Summary);

            for (var index = 1; index < 8; index++)
            {
                var commandId = (ulong)(index + 3);
                var a = PlanningAdvanceCoordinator.Advance(directory, uninterrupted, Compatibility,
                    DateTimeOffset.UnixEpoch.AddMinutes(index + 1), index,
                    Envelope(uninterrupted, commandId, new AdvancePlanningWeekCommand()));
                var b = PlanningAdvanceCoordinator.Advance(directory, restored, Compatibility,
                    DateTimeOffset.UnixEpoch.AddMinutes(index + 20), index + 20,
                    Envelope(restored, commandId, new AdvancePlanningWeekCommand()));
                Assert.IsTrue(a.IsSuccess, a.Message);
                Assert.IsTrue(b.IsSuccess, b.Message);
                Assert.AreEqual(a.Digest!.Summary, b.Digest!.Summary);
                Assert.AreEqual(uninterrupted.CaptureSnapshot().AuthoritativeHash, restored.CaptureSnapshot().AuthoritativeHash);
            }
        });
    }

    private static CommandEnvelope Envelope(GameSession session, ulong commandId, SessionCommand command) =>
        new(new CommandId(commandId), session.CampaignId, session.Phase, session.CurrentTick,
            session.NextSubmissionSequence, null, command);

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"festival-m101-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}

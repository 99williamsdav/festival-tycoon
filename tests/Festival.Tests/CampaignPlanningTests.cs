using System.IO.Compression;
using System.Text.Json;
using Festival.Persistence;
using Festival.Simulation;
using Festival.Simulation.Fixtures;

namespace Festival.Tests;

[TestClass]
public sealed class CampaignPlanningTests
{
    private static readonly SaveCompatibility Compatibility = new("m1-test-build", "content-catalogue-v1-d7e759", "m1-rules-v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

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

    [TestMethod]
    public void CommitmentConfirmedAtW7IsDueNextAdvancePersistsAndCannotRemainConfirmedAtOpening()
    {
        WithTemporaryDirectory(directory =>
        {
            var session = GameSession.CreateCampaign(7007);
            Assert.IsTrue(Advance(directory, session, 0, 1).IsSuccess); // W8 -> W7 with no commitment.
            Assert.AreEqual(7, session.CaptureSnapshot().Campaign!.PlanningWeek);
            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "before-confirm", Request(session, 1)).IsSuccess);
            session = SaveFileAdapter.LoadSlot(directory, "before-confirm", Compatibility).Session!;

            var confirm = Envelope(session, 2, new ConfirmPlanningCommitmentCommand(CampaignDefaults.BasicAdministrationCommitmentId));
            Assert.IsTrue(session.Execute(confirm).IsAccepted);
            var commitment = session.CaptureSnapshot().Campaign!.Commitments.Single();
            Assert.AreEqual(7, commitment.ConfirmedInWeek);
            Assert.AreEqual(7, commitment.DueOnAdvanceFromWeek);
            Assert.AreEqual(4_000, session.GetWeekAdvancePreview().DuePayments.Single().AmountPennies);
            Assert.AreEqual(CommandReasonCode.DuplicateCommand, session.Execute(confirm).ReasonCode);
            Assert.AreEqual(CommandReasonCode.AlreadyCommitted,
                session.Execute(Envelope(session, 3, new ConfirmPlanningCommitmentCommand(CampaignDefaults.BasicAdministrationCommitmentId))).ReasonCode);

            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "confirmed", Request(session, 2)).IsSuccess);
            session = SaveFileAdapter.LoadSlot(directory, "confirmed", Compatibility).Session!;
            var payment = Advance(directory, session, 3, 3);
            Assert.IsTrue(payment.IsSuccess, payment.Message);
            Assert.AreEqual(4_000, payment.Digest!.Payments.Single().AmountPennies);
            Assert.AreEqual(7, payment.Digest.FromWeek);
            Assert.AreEqual(PlanningCommitmentStatus.Paid, session.CaptureSnapshot().Campaign!.Commitments.Single().Status);
            Assert.AreEqual(76_000, session.CaptureSnapshot().FestivalFinances.Single().CashPennies);
            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "after-payment", Request(session, 3)).IsSuccess);
            session = SaveFileAdapter.LoadSlot(directory, "after-payment", Compatibility).Session!;

            for (var generation = 4; session.Phase == SessionPhase.Planning; generation++)
                Assert.IsTrue(Advance(directory, session, generation, (ulong)generation).IsSuccess);
            Assert.AreEqual(SessionPhase.OpeningCheck, session.Phase);
            Assert.AreEqual(PlanningCommitmentStatus.Paid, session.CaptureSnapshot().Campaign!.Commitments.Single().Status);
            Assert.AreEqual(1, session.CaptureSnapshot().Campaign!.LedgerTransactions.Count(item => item.Reason == "Basic administration and cover"));
        });
    }

    [TestMethod]
    public void CommitmentConfirmedAtW1PreviewsAndPaysOnOpeningAdvance()
    {
        WithTemporaryDirectory(directory =>
        {
            var session = GameSession.CreateCampaign(1001);
            for (var generation = 0; generation < 7; generation++)
                Assert.IsTrue(Advance(directory, session, generation, (ulong)(generation + 1)).IsSuccess);
            Assert.AreEqual(1, session.CaptureSnapshot().Campaign!.PlanningWeek);
            Assert.IsTrue(session.Execute(Envelope(session, 8,
                new ConfirmPlanningCommitmentCommand(CampaignDefaults.BasicAdministrationCommitmentId))).IsAccepted);
            var preview = session.GetWeekAdvancePreview();
            Assert.AreEqual(1, preview.FromWeek);
            Assert.AreEqual(SessionPhase.OpeningCheck, preview.PhaseAfter);
            Assert.AreEqual(4_000, preview.DuePayments.Single().AmountPennies);
            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "w1-confirmed", Request(session, 8)).IsSuccess);
            session = SaveFileAdapter.LoadSlot(directory, "w1-confirmed", Compatibility).Session!;

            var result = Advance(directory, session, 9, 9);
            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.AreEqual(SessionPhase.OpeningCheck, session.Phase);
            Assert.AreEqual(1, result.Digest!.FromWeek);
            Assert.AreEqual(4_000, result.Digest.Payments.Single().AmountPennies);
            Assert.AreEqual(PlanningCommitmentStatus.Paid, session.CaptureSnapshot().Campaign!.Commitments.Single().Status);
        });
    }

    [TestMethod]
    public void PlanningAndOpeningCheckRejectAuthoritativeTickProgressButStillAcceptBoundaryCommandsAndSave()
    {
        WithTemporaryDirectory(directory =>
        {
            var session = GameSession.CreateCampaign(8800);
            var planningHash = session.CaptureSnapshot().AuthoritativeHash;
            var result = session.AdvanceTicks(10_000);
            Assert.AreEqual(0, result.Events.Count);
            Assert.AreEqual(0, session.CurrentTick);
            Assert.AreEqual(planningHash, session.CaptureSnapshot().AuthoritativeHash);
            Assert.IsTrue(session.Execute(Envelope(session, 1,
                new ConfirmPlanningCommitmentCommand(CampaignDefaults.BasicAdministrationCommitmentId))).IsAccepted);
            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "stopped-boundary", Request(session, 0)).IsSuccess);

            for (var generation = 0; session.Phase == SessionPhase.Planning; generation++)
                Assert.IsTrue(Advance(directory, session, generation, (ulong)(generation + 2)).IsSuccess);
            var openingHash = session.CaptureSnapshot().AuthoritativeHash;
            session.AdvanceTicks(10_000);
            Assert.AreEqual(0, session.CurrentTick);
            Assert.AreEqual(openingHash, session.CaptureSnapshot().AuthoritativeHash);
        });
    }

    [TestMethod]
    public void MalformedNestedCampaignSaveRecordsReturnStructuredFailuresWithoutMutatingSource()
    {
        WithTemporaryDirectory(directory =>
        {
            var session = GameSession.CreateCampaign(3030);
            Assert.IsTrue(session.Execute(Envelope(session, 1,
                new ConfirmPlanningCommitmentCommand(CampaignDefaults.BasicAdministrationCommitmentId))).IsAccepted);
            Assert.IsTrue(Advance(directory, session, 0, 2).IsSuccess);
            var sourceHash = session.CaptureSnapshot().AuthoritativeHash;
            var good = session.CapturePersistenceSnapshot();
            var campaign = good.CampaignPlanning!;
            var digest = campaign.WeeklyDigests.Single();

            var malformed = new[]
            {
                good with { CampaignPlanning = campaign with { WeeklyDigests = [digest with { Payments = [null!] }] } },
                good with { CampaignPlanning = campaign with { WeeklyDigests = [digest with { Warnings = [null!] }] } },
                good with { CampaignPlanning = campaign with { WeeklyDigests = [digest with { Payments = [new PersistedWeeklyPayment("", "Bad", 4_000)] }] } },
                good with { CampaignPlanning = campaign with { LedgerTransactions = [campaign.LedgerTransactions[0] with { Entries = [null!] }] } },
            };

            foreach (var snapshot in malformed)
            {
                var restored = GameSession.Restore(snapshot);
                Assert.IsFalse(restored.IsSuccess);
                Assert.IsFalse(string.IsNullOrWhiteSpace(restored.Error));
            }
            Assert.AreEqual(sourceHash, session.CaptureSnapshot().AuthoritativeHash);

            var path = Path.Combine(directory, "malformed-nested.ftsave");
            WriteEnvelope(path, malformed[0]);
            var loaded = SaveFileAdapter.LoadFile(path, Compatibility);
            Assert.IsFalse(loaded.IsSuccess);
            StringAssert.Contains(loaded.Error!, "Authoritative payload validation failed");
            StringAssert.Contains(loaded.Error!, "weekly digest");
            Assert.AreEqual(sourceHash, session.CaptureSnapshot().AuthoritativeHash);
        });
    }

    private static CommandEnvelope Envelope(GameSession session, ulong commandId, SessionCommand command) =>
        new(new CommandId(commandId), session.CampaignId, session.Phase, session.CurrentTick,
            session.NextSubmissionSequence, null, command);

    private static PlanningAdvanceResult Advance(string directory, GameSession session, long generation, ulong commandId) =>
        PlanningAdvanceCoordinator.Advance(directory, session, Compatibility, DateTimeOffset.UnixEpoch.AddMinutes(generation), generation,
            Envelope(session, commandId, new AdvancePlanningWeekCommand()));

    private static SaveWriteRequest Request(GameSession session, int minute) =>
        new(session, Compatibility, "test", DateTimeOffset.UnixEpoch.AddMinutes(minute));

    private static void WriteEnvelope(string path, SessionPersistenceSnapshot payload)
    {
        var header = new SaveHeaderV1(
            SaveFileAdapter.FormatId,
            SaveMigrationPipeline.CurrentSchemaVersion,
            Compatibility.BuildId,
            Compatibility.ContentHash,
            Compatibility.RulesetHash,
            payload.CampaignId,
            DateTimeOffset.UnixEpoch.ToString("O"),
            payload.Phase,
            "malformed-test",
            SaveFileAdapter.ComputePayloadChecksum(payload));
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        JsonSerializer.Serialize(gzip, new SaveEnvelopeV1(header, payload), JsonOptions);
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"festival-m101-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}

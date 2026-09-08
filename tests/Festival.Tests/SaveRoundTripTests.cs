using System.IO.Compression;
using System.Text.Json;
using Festival.Persistence;
using Festival.Simulation;
using Festival.Simulation.Fixtures;

namespace Festival.Tests;

[TestClass]
public sealed class SaveRoundTripTests
{
    private static readonly SaveCompatibility Compatibility = new("test-build", "content-v1", "rules-v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [TestMethod]
    public void SaveResumeContinuationMatchesUninterruptedHashTransactionsIdsAndRandom()
    {
        WithTemporaryDirectory(directory =>
        {
            var fixture = AtomicPurchaseFixture.Create(stockQuantity: 3);
            var scheduled = fixture.Session.Execute(Envelope(fixture.Session, new CommandId(6), null, new CreateFixtureRecordCommand(9, 20)));
            Assert.IsTrue(scheduled.IsAccepted);
            fixture.Session.AdvanceTicks(7);
            Assert.IsTrue(fixture.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
                fixture, new CommandId(7), new TransactionId(10), fixture.PrimaryBuyerId)).IsAccepted);
            fixture.Session.NextRandom(RandomStreamId.Demand);

            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "roundtrip", Request(fixture.Session)).IsSuccess);
            var loaded = SaveFileAdapter.LoadSlot(directory, "roundtrip", Compatibility);
            Assert.IsTrue(loaded.IsSuccess, loaded.Error);
            Assert.AreEqual(fixture.Session.CaptureSnapshot().AuthoritativeHash, loaded.Session!.CaptureSnapshot().AuthoritativeHash);

            var loadedFixture = fixture with { Session = loaded.Session };
            Assert.AreEqual(fixture.Session.NextRandom(RandomStreamId.Weather), loaded.Session.NextRandom(RandomStreamId.Weather));
            fixture.Session.AdvanceTicks(20);
            loaded.Session.AdvanceTicks(20);
            Assert.IsTrue(fixture.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
                fixture, new CommandId(8), new TransactionId(11), fixture.ExactCashBuyerId)).IsAccepted);
            Assert.IsTrue(loaded.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
                loadedFixture, new CommandId(8), new TransactionId(11), fixture.ExactCashBuyerId)).IsAccepted);
            var originalCreated = fixture.Session.Execute(Envelope(fixture.Session, new CommandId(9), null, new CreateGuestWalletCommand(50)));
            var loadedCreated = loaded.Session.Execute(Envelope(loaded.Session, new CommandId(9), null, new CreateGuestWalletCommand(50)));

            Assert.AreEqual(originalCreated.TargetId, loadedCreated.TargetId);
            Assert.AreEqual(fixture.Session.CaptureSnapshot().AuthoritativeHash, loaded.Session.CaptureSnapshot().AuthoritativeHash);
            CollectionAssert.AreEqual(
                fixture.Session.Transactions.Select(item => item.Id.Value).ToArray(),
                loaded.Session.Transactions.Select(item => item.Id.Value).ToArray());
        });
    }

    [TestMethod]
    public void PausedCommandSavesImmediatelyAndRemainsAppliedOnce()
    {
        WithTemporaryDirectory(directory =>
        {
            var fixture = AtomicPurchaseFixture.Create();
            var pause = Envelope(fixture.Session, new CommandId(6), null, new SetPausedCommand(true));
            Assert.IsTrue(fixture.Session.Execute(pause).IsAccepted);
            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "paused", Request(fixture.Session)).IsSuccess);
            var loaded = SaveFileAdapter.LoadSlot(directory, "paused", Compatibility);

            Assert.IsTrue(loaded.IsSuccess, loaded.Error);
            Assert.IsTrue(loaded.Session!.IsPaused);
            var hash = loaded.Session.CaptureSnapshot().AuthoritativeHash;
            loaded.Session.AdvanceTicks(100);
            Assert.AreEqual(hash, loaded.Session.CaptureSnapshot().AuthoritativeHash);
            var replay = loaded.Session.Execute(pause);
            Assert.AreEqual(CommandReasonCode.DuplicateCommand, replay.ReasonCode);
            Assert.AreEqual(hash, loaded.Session.CaptureSnapshot().AuthoritativeHash);
        });
    }

    [TestMethod]
    public void CorruptionTruncationUnknownSchemaAndContentMismatchAreActionable()
    {
        WithTemporaryDirectory(directory =>
        {
            var fixture = AtomicPurchaseFixture.Create();
            var good = SaveFileAdapter.ResolveSlotPath(directory, "good");
            Assert.IsTrue(SaveFileAdapter.SaveFile(good, Request(fixture.Session)).IsSuccess);

            var corrupt = Path.Combine(directory, "corrupt.ftsave");
            var bytes = File.ReadAllBytes(good);
            var corruptBytes = bytes.ToArray();
            corruptBytes[corruptBytes.Length / 2] ^= 0x5A;
            File.WriteAllBytes(corrupt, corruptBytes);
            AssertFailureContains(SaveFileAdapter.LoadFile(corrupt, Compatibility), "load");

            var truncated = Path.Combine(directory, "truncated.ftsave");
            File.WriteAllBytes(truncated, bytes[..Math.Max(1, bytes.Length / 3)]);
            AssertFailureContains(SaveFileAdapter.LoadFile(truncated, Compatibility), "load");

            var missingFooterByte = Path.Combine(directory, "missing-footer-byte.ftsave");
            File.WriteAllBytes(missingFooterByte, bytes[..^1]);
            AssertFailureContains(SaveFileAdapter.LoadFile(missingFooterByte, Compatibility), "gzip");
            var missingFooter = Path.Combine(directory, "missing-footer.ftsave");
            File.WriteAllBytes(missingFooter, bytes[..^8]);
            AssertFailureContains(SaveFileAdapter.LoadFile(missingFooter, Compatibility), "gzip");

            var unknown = Path.Combine(directory, "unknown.ftsave");
            var envelope = ReadEnvelope(good);
            WriteEnvelope(unknown, envelope with { Header = envelope.Header with { SchemaVersion = 99 } });
            AssertFailureContains(SaveFileAdapter.LoadFile(unknown, Compatibility), "schema version 99");

            var checksumMismatch = Path.Combine(directory, "checksum-mismatch.ftsave");
            WriteEnvelope(checksumMismatch, envelope with { Payload = envelope.Payload with { CurrentTick = envelope.Payload.CurrentTick + 1 } });
            AssertFailureContains(SaveFileAdapter.LoadFile(checksumMismatch, Compatibility), "Payload checksum mismatch");

            AssertFailureContains(
                SaveFileAdapter.LoadFile(good, Compatibility with { ContentHash = "different-content" }),
                "Content hash mismatch");
            AssertFailureContains(
                SaveFileAdapter.LoadFile(good, Compatibility with { RulesetHash = "different-rules" }),
                "Ruleset hash mismatch");
        });
    }

    [TestMethod]
    public void InjectedFailurePreservesPriorGoodSlotAndSuccessfulReplaceKeepsBackup()
    {
        WithTemporaryDirectory(directory =>
        {
            var fixture = AtomicPurchaseFixture.Create(stockQuantity: 2);
            var firstHash = fixture.Session.CaptureSnapshot().AuthoritativeHash;
            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "atomic", Request(fixture.Session)).IsSuccess);
            Assert.IsTrue(fixture.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
                fixture, new CommandId(6), new TransactionId(1), fixture.PrimaryBuyerId)).IsAccepted);

            var failed = SaveFileAdapter.SaveSlot(directory, "atomic", Request(fixture.Session),
                point => throw new IOException($"Injected at {point}."));
            Assert.IsFalse(failed.IsSuccess);
            StringAssert.Contains(failed.Error!, "previous slot was preserved");
            var afterFailure = SaveFileAdapter.LoadSlot(directory, "atomic", Compatibility);
            Assert.IsTrue(afterFailure.IsSuccess, afterFailure.Error);
            Assert.AreEqual(firstHash, afterFailure.Session!.CaptureSnapshot().AuthoritativeHash);

            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "atomic", Request(fixture.Session)).IsSuccess);
            var backup = SaveFileAdapter.LoadFile(SaveFileAdapter.ResolveSlotPath(directory, "atomic") + ".bak", Compatibility);
            Assert.IsTrue(backup.IsSuccess, backup.Error);
            Assert.AreEqual(firstHash, backup.Session!.CaptureSnapshot().AuthoritativeHash);

            Assert.IsTrue(fixture.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
                fixture, new CommandId(7), new TransactionId(2), fixture.ExactCashBuyerId)).IsAccepted);
            var slotPath = SaveFileAdapter.ResolveSlotPath(directory, "atomic");
            var corruptPrimary = File.ReadAllBytes(slotPath);
            corruptPrimary[corruptPrimary.Length / 2] ^= 0x5A;
            File.WriteAllBytes(slotPath, corruptPrimary);
            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "atomic", Request(fixture.Session)).IsSuccess);
            var preservedBackup = SaveFileAdapter.LoadFile(slotPath + ".bak", Compatibility);
            Assert.IsTrue(preservedBackup.IsSuccess, preservedBackup.Error);
            Assert.AreEqual(firstHash, preservedBackup.Session!.CaptureSnapshot().AuthoritativeHash);
            var repairedPrimary = SaveFileAdapter.LoadFile(slotPath, Compatibility);
            Assert.IsTrue(repairedPrimary.IsSuccess, repairedPrimary.Error);
            Assert.AreEqual(fixture.Session.CaptureSnapshot().AuthoritativeHash, repairedPrimary.Session!.CaptureSnapshot().AuthoritativeHash);
        });
    }

    [TestMethod]
    public void AcceptedPurchaseCannotReplayAfterLoading()
    {
        WithTemporaryDirectory(directory =>
        {
            var fixture = AtomicPurchaseFixture.Create(stockQuantity: 2);
            var purchase = AtomicPurchaseFixture.PurchaseEnvelope(
                fixture, new CommandId(6), new TransactionId(1), fixture.PrimaryBuyerId);
            Assert.IsTrue(fixture.Session.Execute(purchase).IsAccepted);
            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "idempotent", Request(fixture.Session)).IsSuccess);
            var loaded = SaveFileAdapter.LoadSlot(directory, "idempotent", Compatibility).Session!;
            var hash = loaded.CaptureSnapshot().AuthoritativeHash;

            Assert.AreEqual(CommandReasonCode.DuplicateCommand, loaded.Execute(purchase).ReasonCode);
            var loadedFixture = fixture with { Session = loaded };
            var newCommandSameTransaction = AtomicPurchaseFixture.PurchaseEnvelope(
                loadedFixture, new CommandId(7), new TransactionId(1), fixture.CompetingBuyerId);
            Assert.AreEqual(CommandReasonCode.DuplicateTransaction, loaded.Execute(newCommandSameTransaction).ReasonCode);
            Assert.AreEqual(hash, loaded.CaptureSnapshot().AuthoritativeHash);
            Assert.AreEqual(1, loaded.Transactions.Count);
        });
    }

    [TestMethod]
    public void BuildMismatchIsWarningAndSlotNamesCannotEscapeDirectory()
    {
        WithTemporaryDirectory(directory =>
        {
            var session = AtomicPurchaseFixture.Create().Session;
            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "Slot_01-good", Request(session)).IsSuccess);
            var loaded = SaveFileAdapter.LoadSlot(directory, "Slot_01-good", Compatibility with { BuildId = "new-build" });
            Assert.IsTrue(loaded.IsSuccess, loaded.Error);
            Assert.IsTrue(loaded.BuildMismatch);
            StringAssert.Contains(loaded.Warning!, "differs");

            Assert.AreEqual(
                SaveFileAdapter.ResolveSlotPath(directory, "Slot_01-good"),
                SaveFileAdapter.ResolveSlotPath(directory + Path.DirectorySeparatorChar, "Slot_01-good"));
            Assert.AreEqual(
                SaveFileAdapter.ResolveSlotPath(directory, "Slot_01-good"),
                SaveFileAdapter.ResolveSlotPath(directory + Path.AltDirectorySeparatorChar, "Slot_01-good"));

            foreach (var slot in new[] { "../escape", "..", "bad/name", "bad\\name", "", new string('a', 65) })
                Assert.ThrowsExactly<ArgumentException>(() => SaveFileAdapter.ResolveSlotPath(directory, slot));
        });
    }

    [TestMethod]
    public void CheckedInNonpersonalV1FixtureLoads()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "FestivalTycoon.sln")))
            directory = Directory.GetParent(directory)?.FullName;
        Assert.IsNotNull(directory, "Could not locate repository root.");
        var path = Path.Combine(directory, "tests", "fixtures", "saves", "m0-v1-nonpersonal.ftsave");

        var loaded = SaveFileAdapter.LoadFile(path, new SaveCompatibility(
            "0.0.1-m0.05",
            "d7e7597670c2f9bc2552fa5df29f4afe294270e346643160e92feb1436bb1dd9",
            "m0-rules-v1"));
        Assert.IsTrue(loaded.IsSuccess, loaded.Error);
        Assert.IsTrue(loaded.Session!.IsPaused);
        Assert.AreEqual(new TransactionId(1), loaded.Session.Transactions.Single().Id);
        Assert.AreEqual("6bd820988de37a9c669dd4e5a71c26b3c781a5be1e77ab9e9f80f47fbabcff37", loaded.Session.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void ZeroCommandIdRejectsWithoutMutationAndCannotEnterSaveState()
    {
        var fixture = AtomicPurchaseFixture.Create();
        var before = fixture.Session.CaptureSnapshot();
        var result = fixture.Session.Execute(Envelope(
            fixture.Session, new CommandId(0), null, new SetPausedCommand(true)));

        Assert.IsFalse(result.IsAccepted);
        Assert.AreEqual(CommandReasonCode.InvalidParameter, result.ReasonCode);
        Assert.AreEqual(before.AuthoritativeHash, fixture.Session.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(before.NextSubmissionSequence, fixture.Session.NextSubmissionSequence);
        Assert.IsFalse(fixture.Session.CapturePersistenceSnapshot().AcceptedCommandIds.Contains(0UL));
    }

    private static SaveWriteRequest Request(GameSession session) => new(session, Compatibility, "test", new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));

    private static CommandEnvelope Envelope(GameSession session, CommandId commandId, EntityId? targetId, SessionCommand command) =>
        new(commandId, session.CampaignId, session.Phase, session.CurrentTick, session.NextSubmissionSequence, targetId, command);

    private static void AssertFailureContains(SaveLoadResult result, string text)
    {
        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Error!, text, StringComparison.OrdinalIgnoreCase);
    }

    private static SaveEnvelopeV1 ReadEnvelope(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<SaveEnvelopeV1>(gzip, JsonOptions)!;
    }

    private static void WriteEnvelope(string path, SaveEnvelopeV1 envelope)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        JsonSerializer.Serialize(gzip, envelope, JsonOptions);
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"festival-save-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}

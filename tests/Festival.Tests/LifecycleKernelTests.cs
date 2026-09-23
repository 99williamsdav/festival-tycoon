using Festival.Persistence;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class LifecycleKernelTests
{
    private static readonly SaveCompatibility Compatibility = new("r0.00-test", "content-v1", "r0.00-fixture-rules-v1");

    [TestMethod]
    public void FullFixtureTraceIsDeterministicPersistsEveryBoundaryAndAdvancesOnce()
    {
        WithTemporaryDirectory(directory =>
        {
            var first = RunTrace(Path.Combine(directory, "first"), 20260923, reloadEachBoundary: true);
            var second = RunTrace(Path.Combine(directory, "second"), 20260923, reloadEachBoundary: false);

            CollectionAssert.AreEqual(first.Hashes, second.Hashes);
            CollectionAssert.AreEqual(first.TransactionIds, second.TransactionIds);
            var lifecycle = first.Session.CaptureSnapshot().Lifecycle!;
            Assert.AreEqual("fixture-tier-2", lifecycle.CurrentTierId);
            Assert.AreEqual(2, lifecycle.FixtureTierOrdinal);
            Assert.AreEqual(0, lifecycle.FixtureFavourBalance);
            Assert.AreEqual(2, lifecycle.Attempts.Count);
            Assert.AreEqual(1, lifecycle.Casualties.Count);
            Assert.AreEqual(1, lifecycle.Hearings.Count);
            Assert.AreEqual(EditionAttemptStatus.Failed, lifecycle.Attempts[0].Status);
            Assert.AreEqual(EditionAttemptStatus.Safe, lifecycle.Attempts[1].Status);
            Assert.AreEqual(lifecycle.Attempts[0].TierId, lifecycle.Attempts[1].TierId, "Favour retry stays on the same fixture tier.");
        });
    }

    [TestMethod]
    public void SimultaneousDeathsChooseFirstProtectedPersonAndFreezeTicksAndCommands()
    {
        WithTemporaryDirectory(directory =>
        {
            var session = GameSession.CreateR000LifecycleFixture(41);
            var result = Apply(directory, session, 0, 1,
                new ForceFixtureDeathsCommand(["fixture-staff", "fixture-guest"]));
            Assert.IsTrue(result.IsSuccess, result.Message);
            session = result.Session;
            var afterDeath = session.CaptureSnapshot();

            Assert.AreEqual(1, afterDeath.Lifecycle!.Casualties.Count);
            Assert.AreEqual("fixture-staff", afterDeath.Lifecycle.Casualties[0].PersonId);
            Assert.AreEqual(ProtectedPersonRole.Staff, afterDeath.Lifecycle.Casualties[0].Role);
            Assert.AreEqual(1, afterDeath.Lifecycle.Hearings.Count);
            session.AdvanceTicks(100);
            Assert.AreEqual(afterDeath.AuthoritativeHash, session.CaptureSnapshot().AuthoritativeHash);
            var later = session.Execute(Envelope(session, 2, new SetPausedCommand(true)));
            Assert.AreEqual(CommandReasonCode.EditionFrozen, later.ReasonCode);
            Assert.AreEqual(afterDeath.AuthoritativeHash, session.CaptureSnapshot().AuthoritativeHash);
            var secondDeath = Apply(directory, session, 1, 2, new ForceFixtureDeathsCommand(["fixture-performer"]));
            Assert.IsFalse(secondDeath.IsSuccess);
            Assert.AreEqual(CommandReasonCode.EditionFrozen, secondDeath.ReasonCode);
            Assert.AreEqual(afterDeath.AuthoritativeHash, secondDeath.Session.CaptureSnapshot().AuthoritativeHash);
        });
    }

    [TestMethod]
    public void EveryProtectedRoleCanBeTerminalWhileUnknownSubjectRejectsWithoutMutation()
    {
        foreach (var (personId, role) in new[]
        {
            ("fixture-guest", ProtectedPersonRole.Guest),
            ("fixture-staff", ProtectedPersonRole.Staff),
            ("fixture-performer", ProtectedPersonRole.Performer),
        })
        {
            WithTemporaryDirectory(directory =>
            {
                var session = GameSession.CreateR000LifecycleFixture(52);
                var result = Apply(directory, session, 0, 1, new ForceFixtureDeathsCommand([personId]));
                Assert.IsTrue(result.IsSuccess, result.Message);
                Assert.AreEqual(role, result.Session.CaptureSnapshot().Lifecycle!.Casualties.Single().Role);
            });
        }

        WithTemporaryDirectory(directory =>
        {
            var session = GameSession.CreateR000LifecycleFixture(53);
            var before = session.CaptureSnapshot().AuthoritativeHash;
            var result = Apply(directory, session, 0, 1, new ForceFixtureDeathsCommand(["unknown-person"]));
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(CommandReasonCode.UnprotectedSubject, result.ReasonCode);
            Assert.AreEqual(before, session.CaptureSnapshot().AuthoritativeHash);
            Assert.AreEqual(0, Directory.GetFiles(directory).Length);
        });
    }

    [TestMethod]
    public void DuplicateFavourSettlementCasualtyAndAdvanceRejectWithoutMutation()
    {
        WithTemporaryDirectory(directory =>
        {
            var session = GameSession.CreateR000LifecycleFixture(64);
            var death = Envelope(session, 1, new ForceFixtureDeathsCommand(["fixture-guest"]));
            var failed = LifecycleTransitionCoordinator.Apply(directory, session, Compatibility, DateTimeOffset.UnixEpoch, 0, death);
            Assert.IsTrue(failed.IsSuccess, failed.Message);
            session = failed.Session;
            var failedHash = session.CaptureSnapshot().AuthoritativeHash;
            var duplicateDeath = LifecycleTransitionCoordinator.Apply(directory, session, Compatibility, DateTimeOffset.UnixEpoch, 1, death);
            Assert.AreEqual(CommandReasonCode.DuplicateCommand, duplicateDeath.ReasonCode);
            Assert.AreEqual(failedHash, duplicateDeath.Session.CaptureSnapshot().AuthoritativeHash);

            var spend = Envelope(session, 2, new SpendFixtureFavourCommand());
            var retried = LifecycleTransitionCoordinator.Apply(directory, session, Compatibility, DateTimeOffset.UnixEpoch, 1, spend);
            Assert.IsTrue(retried.IsSuccess, retried.Message);
            session = retried.Session;
            var retryHash = session.CaptureSnapshot().AuthoritativeHash;
            Assert.AreEqual(CommandReasonCode.DuplicateCommand,
                LifecycleTransitionCoordinator.Apply(directory, session, Compatibility, DateTimeOffset.UnixEpoch, 2, spend).ReasonCode);
            Assert.AreEqual(CommandReasonCode.AlreadySettled,
                Apply(directory, session, 2, 3, new SpendFixtureFavourCommand()).ReasonCode);
            Assert.AreEqual(retryHash, session.CaptureSnapshot().AuthoritativeHash);

            var safe = Envelope(session, 3, new ForceFixtureSafeCompletionCommand());
            var advanced = LifecycleTransitionCoordinator.Apply(directory, session, Compatibility, DateTimeOffset.UnixEpoch, 2, safe);
            Assert.IsTrue(advanced.IsSuccess, advanced.Message);
            session = advanced.Session;
            var advancedHash = session.CaptureSnapshot().AuthoritativeHash;
            Assert.AreEqual(CommandReasonCode.DuplicateCommand,
                LifecycleTransitionCoordinator.Apply(directory, session, Compatibility, DateTimeOffset.UnixEpoch, 3, safe).ReasonCode);
            Assert.AreEqual(CommandReasonCode.EditionFrozen,
                Apply(directory, session, 3, 4, new ForceFixtureSafeCompletionCommand()).ReasonCode);
            Assert.AreEqual(advancedHash, session.CaptureSnapshot().AuthoritativeHash);
        });
    }

    [TestMethod]
    public void InjectedSaveFailureAtEachBoundaryLeavesMemoryAndPriorSlotAtPreviousValidState()
    {
        WithTemporaryDirectory(directory =>
        {
            var states = new List<(GameSession Session, SessionCommand Command)>
            {
                (GameSession.CreateR000LifecycleFixture(75), new ForceFixtureDeathsCommand(["fixture-guest"])),
            };
            var afterDeath = Apply(Path.Combine(directory, "setup-death"), states[0].Session, 0, 1, states[0].Command).Session;
            states.Add((afterDeath, new SpendFixtureFavourCommand()));
            var afterSpend = Apply(Path.Combine(directory, "setup-spend"), afterDeath, 0, 2, states[1].Command).Session;
            states.Add((afterSpend, new ForceFixtureSafeCompletionCommand()));

            for (var index = 0; index < states.Count; index++)
            {
                var boundaryDirectory = Path.Combine(directory, $"boundary-{index}");
                var (session, command) = states[index];
                var beforeHash = session.CaptureSnapshot().AuthoritativeHash;
                Assert.IsTrue(AutosaveRotation.Save(boundaryDirectory, session, Compatibility, DateTimeOffset.UnixEpoch, 0).IsSuccess);
                var failed = LifecycleTransitionCoordinator.Apply(
                    boundaryDirectory, session, Compatibility, DateTimeOffset.UnixEpoch.AddMinutes(1), 0,
                    Envelope(session, (ulong)(index + 10), command),
                    point => throw new IOException($"Injected at {point}."));

                Assert.IsFalse(failed.IsSuccess);
                StringAssert.Contains(failed.Message, "not exposed");
                Assert.AreSame(session, failed.Session);
                Assert.AreEqual(beforeHash, session.CaptureSnapshot().AuthoritativeHash);
                var prior = AutosaveRotation.LoadNewestValid(boundaryDirectory, Compatibility);
                Assert.IsTrue(prior.IsSuccess, prior.Error);
                Assert.AreEqual(beforeHash, prior.Session!.CaptureSnapshot().AuthoritativeHash);
            }
        });
    }

    private static (GameSession Session, string[] Hashes, string[] TransactionIds) RunTrace(string directory, ulong seed, bool reloadEachBoundary)
    {
        Directory.CreateDirectory(directory);
        var session = GameSession.CreateR000LifecycleFixture(seed);
        var hashes = new List<string> { session.CaptureSnapshot().AuthoritativeHash };
        Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "before-death", Request(session)).IsSuccess);
        var beforeDeath = SaveFileAdapter.LoadSlot(directory, "before-death", Compatibility);
        Assert.IsTrue(beforeDeath.IsSuccess, beforeDeath.Error);
        Assert.AreEqual(hashes[0], beforeDeath.Session!.CaptureSnapshot().AuthoritativeHash);
        if (reloadEachBoundary) session = beforeDeath.Session;

        foreach (var (generation, command) in new (long, SessionCommand)[]
        {
            (0, new ForceFixtureDeathsCommand(["fixture-guest", "fixture-performer"])),
            (1, new SpendFixtureFavourCommand()),
            (2, new ForceFixtureSafeCompletionCommand()),
        })
        {
            var result = Apply(directory, session, generation, (ulong)(generation + 1), command);
            Assert.IsTrue(result.IsSuccess, result.Message);
            session = result.Session;
            var boundaryHash = session.CaptureSnapshot().AuthoritativeHash;
            hashes.Add(boundaryHash);
            var loaded = AutosaveRotation.LoadNewestValid(directory, Compatibility);
            Assert.IsTrue(loaded.IsSuccess, loaded.Error);
            Assert.AreEqual(boundaryHash, loaded.Session!.CaptureSnapshot().AuthoritativeHash);
            if (reloadEachBoundary) session = loaded.Session;
        }

        return (session, hashes.ToArray(), session.CaptureSnapshot().Lifecycle!.CompletedOutcomeTransactionIds.ToArray());
    }

    private static LifecycleTransitionResult Apply(string directory, GameSession session, long generation, ulong commandId, SessionCommand command) =>
        LifecycleTransitionCoordinator.Apply(directory, session, Compatibility, DateTimeOffset.UnixEpoch.AddMinutes(generation), generation,
            Envelope(session, commandId, command));

    private static CommandEnvelope Envelope(GameSession session, ulong commandId, SessionCommand command) =>
        new(new CommandId(commandId), session.CampaignId, session.Phase, session.CurrentTick,
            session.NextSubmissionSequence, null, command);

    private static SaveWriteRequest Request(GameSession session) =>
        new(session, Compatibility, "r0.00-boundary-test", DateTimeOffset.UnixEpoch);

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"festival-r000-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}

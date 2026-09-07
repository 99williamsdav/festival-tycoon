using Festival.Simulation;
using Festival.Simulation.Fixtures;

namespace Festival.Tests;

[TestClass]
public sealed class DeterministicSessionTests
{
    [TestMethod]
    public void SameSeedAndCommandsProduceIdenticalIntermediateAndFinalHashes()
    {
        var first = DeterministicSessionFixture.CreateInitializedSession();
        var second = DeterministicSessionFixture.CreateInitializedSession();

        Assert.AreEqual(first.CaptureSnapshot().AuthoritativeHash, second.CaptureSnapshot().AuthoritativeHash);
        first.AdvanceTicks(100);
        second.AdvanceTicks(100);
        Assert.AreEqual(first.CaptureSnapshot().AuthoritativeHash, second.CaptureSnapshot().AuthoritativeHash);
        first.AdvanceTicks(300);
        second.AdvanceTicks(300);
        Assert.AreEqual(first.CaptureSnapshot().AuthoritativeHash, second.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void FourHundredTicksAreIndependentOfBatchSizes()
    {
        var singleBatch = DeterministicSessionFixture.Run(400);
        var mixedBatches = DeterministicSessionFixture.Run(1, 7, 32);

        Assert.AreEqual(singleBatch.FinalHash, mixedBatches.FinalHash);
        Assert.AreEqual(400L, mixedBatches.FinalSnapshot.CurrentTick);
        Assert.IsTrue(mixedBatches.FinalSnapshot.FixtureRecords.Single().HasExpired);
    }

    [TestMethod]
    public void PauseStopsTicksAndExpiryButAllowsValidCommands()
    {
        var session = DeterministicSessionFixture.CreateInitializedSession();
        var targetId = session.CaptureSnapshot().FixtureRecords.Single().Id;
        var pause = session.Execute(DeterministicSessionFixture.Envelope(
            session,
            new CommandId(3),
            null,
            new SetPausedCommand(true)));
        Assert.IsTrue(pause.IsAccepted);

        var beforeAdvance = session.CaptureSnapshot();
        session.AdvanceTicks(200);
        var afterAdvance = session.CaptureSnapshot();
        Assert.AreEqual(beforeAdvance.AuthoritativeHash, afterAdvance.AuthoritativeHash);
        Assert.AreEqual(0L, afterAdvance.CurrentTick);
        Assert.AreEqual(350, afterAdvance.FixtureRecords.Single().RemainingTicks);

        var changed = session.Execute(DeterministicSessionFixture.Envelope(
            session,
            new CommandId(4),
            targetId,
            new ChangeFixtureValueCommand(99)));
        var afterCommand = session.CaptureSnapshot();
        Assert.IsTrue(changed.IsAccepted);
        Assert.AreEqual(99, afterCommand.FixtureRecords.Single().Value);
        Assert.AreEqual(0L, afterCommand.CurrentTick);
        Assert.AreEqual(350, afterCommand.FixtureRecords.Single().RemainingTicks);
    }

    [TestMethod]
    public void WrongPhaseAndUnknownTargetRejectWithoutMutation()
    {
        var session = DeterministicSessionFixture.CreateInitializedSession();
        var beforeWrongPhase = session.CaptureSnapshot().AuthoritativeHash;
        var wrongPhase = session.Execute(DeterministicSessionFixture.Envelope(
            session,
            new CommandId(3),
            null,
            new SetPausedCommand(true),
            SessionPhase.Egress));
        Assert.IsFalse(wrongPhase.IsAccepted);
        Assert.AreEqual(CommandReasonCode.WrongPhase, wrongPhase.ReasonCode);
        Assert.AreEqual(beforeWrongPhase, session.CaptureSnapshot().AuthoritativeHash);

        var beforeUnknownTarget = session.CaptureSnapshot().AuthoritativeHash;
        var unknownTarget = session.Execute(DeterministicSessionFixture.Envelope(
            session,
            new CommandId(4),
            new EntityId(999),
            new ChangeFixtureValueCommand(88)));
        Assert.IsFalse(unknownTarget.IsAccepted);
        Assert.AreEqual(CommandReasonCode.UnknownTarget, unknownTarget.ReasonCode);
        Assert.AreEqual(beforeUnknownTarget, session.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void EqualTickCommandsUseSubmissionOrderAndAcceptedIdsAreIdempotent()
    {
        var session = DeterministicSessionFixture.CreateInitializedSession();
        var targetId = session.CaptureSnapshot().FixtureRecords.Single().Id;
        var first = DeterministicSessionFixture.Envelope(
            session,
            new CommandId(3),
            targetId,
            new ChangeFixtureValueCommand(100));
        Assert.IsTrue(session.Execute(first).IsAccepted);

        var second = DeterministicSessionFixture.Envelope(
            session,
            new CommandId(4),
            targetId,
            new ChangeFixtureValueCommand(200));
        Assert.IsTrue(session.Execute(second).IsAccepted);
        Assert.AreEqual(200, session.CaptureSnapshot().FixtureRecords.Single().Value);
        CollectionAssert.AreEqual(
            new ulong[] { 0, 1, 2, 3 },
            session.AppliedCommands.Select(command => command.SubmissionSequence).ToArray());

        var beforeDuplicate = session.CaptureSnapshot().AuthoritativeHash;
        var duplicate = session.Execute(second);
        Assert.IsFalse(duplicate.IsAccepted);
        Assert.AreEqual(CommandReasonCode.DuplicateCommand, duplicate.ReasonCode);
        Assert.AreEqual(beforeDuplicate, session.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(200, session.CaptureSnapshot().FixtureRecords.Single().Value);
    }

    [TestMethod]
    public void RequestedSpeedIsNonAuthoritativeAndDoesNotAdvanceTime()
    {
        var session = DeterministicSessionFixture.CreateInitializedSession();
        var before = session.CaptureSnapshot();
        session.RequestSpeed(RequestedSpeed.FourX);
        var after = session.CaptureSnapshot();

        Assert.AreEqual(RequestedSpeed.FourX, after.RequestedSpeed);
        Assert.AreEqual(before.CurrentTick, after.CurrentTick);
        Assert.AreEqual(before.AuthoritativeHash, after.AuthoritativeHash);
    }

    [TestMethod]
    public void EntityIdsAllocateMonotonicallyAndAllocationStateIsHashed()
    {
        var session = DeterministicSessionFixture.CreateInitializedSession();
        var before = session.CaptureSnapshot();
        var created = session.Execute(DeterministicSessionFixture.Envelope(
            session,
            new CommandId(3),
            null,
            new CreateFixtureRecordCommand(7, 10)));
        var after = session.CaptureSnapshot();

        Assert.IsTrue(created.IsAccepted);
        Assert.AreEqual(new EntityId(2), created.TargetId);
        Assert.AreEqual(3UL, after.NextEntityId);
        Assert.AreNotEqual(before.AuthoritativeHash, after.AuthoritativeHash);
    }

    [TestMethod]
    public void CosmeticAndUnrelatedStreamsDoNotPerturbBehaviourStream()
    {
        var baseline = new GameSession(1234);
        var perturbed = new GameSession(1234);
        var hashBeforeCosmetic = perturbed.CaptureSnapshot().AuthoritativeHash;

        for (var index = 0; index < 20; index++)
        {
            perturbed.NextRandom(RandomStreamId.Cosmetic);
        }

        Assert.AreEqual(hashBeforeCosmetic, perturbed.CaptureSnapshot().AuthoritativeHash);
        var hashBeforeWeather = perturbed.CaptureSnapshot().AuthoritativeHash;
        for (var index = 0; index < 10; index++)
        {
            perturbed.NextRandom(RandomStreamId.Weather);
        }
        Assert.AreNotEqual(hashBeforeWeather, perturbed.CaptureSnapshot().AuthoritativeHash);

        var baselineBehaviour = Enumerable.Range(0, 8)
            .Select(_ => baseline.NextRandom(RandomStreamId.IndividualBehaviour))
            .ToArray();
        var perturbedBehaviour = Enumerable.Range(0, 8)
            .Select(_ => perturbed.NextRandom(RandomStreamId.IndividualBehaviour))
            .ToArray();
        CollectionAssert.AreEqual(baselineBehaviour, perturbedBehaviour);
    }
}

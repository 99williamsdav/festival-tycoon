using Festival.Simulation;
using Festival.Simulation.Fixtures;

namespace Festival.Tests;

[TestClass]
public sealed class ObservationPublicationTests
{
    [TestMethod]
    public void MixedBatchPublicationIsExactForVariableSpeedRetargetFixtureAndObservationsAreDetached()
    {
        var legacy = ScaleDiagnosticFixture.Create(50, 20260924, ScaleDiagnosticLayout.Representative);
        var compact = ScaleDiagnosticFixture.Create(50, 20260924, ScaleDiagnosticLayout.Representative);
        var initial = legacy.Session.CaptureSnapshot();
        Assert.IsTrue(initial.NavigationAgents.Select(agent => agent.WalkingSpeedPermille).Distinct().Count() > 1);
        Assert.IsTrue(legacy.RetargetCount > 0);

        foreach (var count in new[] { 7, 1, 19, 3, 32, 2, 11, 5 })
        {
            var published = legacy.Session.AdvanceTicks(count);
            var events = compact.Session.AdvanceWithoutSnapshot(count);
            CollectionAssert.AreEqual(published.Events.ToArray(), events.ToArray());
            AssertObservationEquals(legacy.Session.CaptureObservation(), compact.Session.CaptureObservation());
            Assert.AreEqual(published.Snapshot.AuthoritativeHash, compact.Session.CaptureSnapshot().AuthoritativeHash);
        }

        var beforeMutation = compact.Session.CaptureSnapshot().AuthoritativeHash;
        var detached = compact.Session.CaptureObservation();
        var navigation = (NavigationObservation[])detached.NavigationAgents;
        navigation[0] = navigation[0] with { XMillimetres = int.MinValue };
        var queues = (QueueObservation[])detached.ServiceQueues;
        var members = (EntityId[])queues[0].OrderedMembers;
        if (members.Length > 0) members[0] = new EntityId(ulong.MaxValue);
        Assert.AreEqual(beforeMutation, compact.Session.CaptureSnapshot().AuthoritativeHash);
        Assert.AreNotEqual(int.MinValue, compact.Session.CaptureObservation().NavigationAgents[0].XMillimetres);
    }

    [TestMethod]
    public void LegacyCaptureAndCompactProductionPresenterRemainEquivalent()
    {
        var captureFixture = ServiceQueueFixture.Create(durationTicks: 2);
        var normalFixture = ServiceQueueFixture.Create(durationTicks: 2);
        var capturePresenter = new FoundationPresentationInterpolator();
        var normalPresenter = new FoundationPresentationInterpolator();
        capturePresenter.Reset(captureFixture.Session.CaptureSnapshot());
        normalPresenter.Reset(normalFixture.Session.CaptureObservation());

        for (var tick = 0; tick < 250; tick++)
        {
            capturePresenter.Advance(captureFixture.Session.AdvanceTicks(1).Snapshot);
            normalFixture.Session.AdvanceWithoutSnapshot(1);
            normalPresenter.Advance(normalFixture.Session.CaptureObservation());
            foreach (var id in captureFixture.AgentIds)
            {
                Assert.AreEqual(capturePresenter.Sample(id, .25), normalPresenter.Sample(id, .25));
                Assert.AreEqual(capturePresenter.Sample(id, .75), normalPresenter.Sample(id, .75));
            }
        }
        Assert.AreEqual(captureFixture.Session.CaptureSnapshot().AuthoritativeHash,
            normalFixture.Session.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void SnapshotFreeAndLegacyPublicationSchedulesRemainAuthoritativelyEquivalent()
    {
        var legacy = ServiceQueueFixture.Create(durationTicks: 2);
        var compact = ServiceQueueFixture.Create(durationTicks: 2);
        var persistedAfterPurchase = false;

        for (var step = 0; step < 10_000; step++)
        {
            if (step == 40)
                AssertSameCommand(ServiceQueueFixture.Abandon(legacy, legacy.AgentIds[^1], 2),
                    ServiceQueueFixture.Abandon(compact, compact.AgentIds[^1], 2));
            if (step == 80)
            {
                AssertSameCommand(SetPaused(legacy.Session, true, 3), SetPaused(compact.Session, true, 3));
                var pausedHash = compact.Session.CaptureSnapshot().AuthoritativeHash;
                Assert.AreEqual(0, legacy.Session.AdvanceTicks(4).Events.Count);
                Assert.AreEqual(0, compact.Session.AdvanceWithoutSnapshot(4).Count);
                Assert.AreEqual(pausedHash, compact.Session.CaptureSnapshot().AuthoritativeHash);
                AssertSameCommand(SetPaused(legacy.Session, false, 4), SetPaused(compact.Session, false, 4));
            }

            var published = legacy.Session.AdvanceTicks(1);
            var events = compact.Session.AdvanceWithoutSnapshot(1);
            CollectionAssert.AreEqual(published.Events.ToArray(), events.ToArray(), $"Event divergence at step {step}.");
            if (step % 3 == 0) AssertObservationEquals(legacy.Session.CaptureObservation(), compact.Session.CaptureObservation());
            if (step % 47 == 0)
                Assert.AreEqual(published.Snapshot.AuthoritativeHash, compact.Session.CaptureSnapshot().AuthoritativeHash,
                    $"Canonical divergence at step {step}.");

            if (!persistedAfterPurchase && compact.Session.CaptureObservation().TransactionCount > 0)
            {
                persistedAfterPurchase = true;
                var restore = GameSession.Restore(compact.Session.CapturePersistenceSnapshot());
                Assert.IsTrue(restore.IsSuccess, restore.Error);
                for (var continuation = 0; continuation < 173; continuation++)
                {
                    var originalEvents = compact.Session.AdvanceWithoutSnapshot(1);
                    var restoredEvents = restore.Session!.AdvanceWithoutSnapshot(1);
                    CollectionAssert.AreEqual(originalEvents.ToArray(), restoredEvents.ToArray());
                    if (continuation % 11 == 0)
                        AssertObservationEquals(compact.Session.CaptureObservation(), restore.Session.CaptureObservation());
                }
                Assert.AreEqual(compact.Session.CaptureSnapshot().AuthoritativeHash,
                    restore.Session!.CaptureSnapshot().AuthoritativeHash);
                legacy.Session.AdvanceTicks(173);
            }

            var compactObservation = compact.Session.CaptureObservation();
            if (compactObservation.TransactionCount == 4 &&
                compactObservation.ServiceQueues.Single().OrderedMembers.Count == 0) break;
        }

        Assert.IsTrue(persistedAfterPurchase, "The snapshot-free interval must cover a purchase.");
        var legacyFinal = legacy.Session.CaptureSnapshot();
        var compactFinal = compact.Session.CaptureSnapshot();
        Assert.AreEqual(4, compactFinal.Transactions.Count);
        Assert.AreEqual(legacyFinal.AuthoritativeHash, compactFinal.AuthoritativeHash);
        Assert.AreEqual(compactFinal.AuthoritativeHash, compact.Session.CaptureSnapshot().AuthoritativeHash,
            "Explicit snapshot capture must be stable without authoritative progress.");
    }

    [TestMethod]
    public void CompactObservationPreservesEveryPersonQueueOwnershipAndPresentationIdentityWithoutHashing()
    {
        var probe = new ScaleDiagnosticProbe();
        var fixture = ScaleDiagnosticFixture.Create(100, 20260924, ScaleDiagnosticLayout.Representative, probe);
        var before = probe.Capture();
        var presentation = new FoundationPresentationInterpolator();
        presentation.Reset(fixture.Session.CaptureObservation());

        for (var tick = 0; tick < 25; tick++)
        {
            fixture.Session.AdvanceWithoutSnapshot(1);
            presentation.Advance(fixture.Session.CaptureObservation());
        }

        var observation = fixture.Session.CaptureObservation();
        var after = probe.Capture();
        Assert.AreEqual(100, observation.NavigationAgents.Count);
        Assert.AreEqual(100, observation.WalletCount);
        Assert.AreEqual(100, observation.NavigationAgents.Select(item => item.Id).Distinct().Count());
        var queueAgents = observation.ServiceQueues.SelectMany(item => item.Agents).ToArray();
        Assert.AreEqual(100, queueAgents.Length);
        Assert.AreEqual(100, queueAgents.Select(item => item.AgentId).Distinct().Count());
        Assert.IsTrue(observation.ServiceQueues.All(queue => queue.OrderedMembers.All(id =>
            queue.Agents.Any(agent => agent.AgentId == id))));
        Assert.IsTrue(observation.ServiceQueues.All(queue => queue.Agents
            .Where(agent => agent.ReservedSlotIndex is not null)
            .Select(agent => agent.ReservedSlotIndex).Distinct().Count() ==
            queue.Agents.Count(agent => agent.ReservedSlotIndex is not null)));
        Assert.IsTrue(observation.ServiceQueues.All(queue => queue.Agents
            .Where(agent => agent.OwnsExitReservation).Select(agent => agent.ExitIndex).Distinct().Count() ==
            queue.Agents.Count(agent => agent.OwnsExitReservation)));
        Assert.IsTrue(observation.ServiceQueues.All(queue => queue.ActiveOwnerId is null ||
            queue.Agents.Single(agent => agent.AgentId == queue.ActiveOwnerId).Action == ServiceQueueAgentAction.InService));
        foreach (var navigation in observation.NavigationAgents)
            Assert.AreEqual((navigation.XMillimetres, navigation.ZMillimetres), presentation.Sample(navigation.Id, 1));
        Assert.AreEqual(before.SnapshotInclusiveMs, after.SnapshotInclusiveMs);
        Assert.AreEqual(before.HashMs, after.HashMs);
        Assert.IsTrue(after.ObservationMs > before.ObservationMs);
    }

    private static CommandResult SetPaused(GameSession session, bool paused, ulong commandId) =>
        session.Execute(new CommandEnvelope(new CommandId(commandId), session.CampaignId, session.Phase,
            session.CurrentTick, session.NextSubmissionSequence, null, new SetPausedCommand(paused)));

    private static void AssertSameCommand(CommandResult expected, CommandResult actual)
    {
        Assert.AreEqual(expected.IsAccepted, actual.IsAccepted);
        Assert.AreEqual(expected.ReasonCode, actual.ReasonCode);
        Assert.AreEqual(expected.TargetId, actual.TargetId);
    }

    private static void AssertObservationEquals(SessionObservation expected, SessionObservation actual)
    {
        Assert.AreEqual(expected.CurrentTick, actual.CurrentTick);
        Assert.AreEqual(expected.WalletCount, actual.WalletCount);
        Assert.AreEqual(expected.TransactionCount, actual.TransactionCount);
        CollectionAssert.AreEqual(expected.NavigationAgents.ToArray(), actual.NavigationAgents.ToArray());
        Assert.AreEqual(expected.ServiceQueues.Count, actual.ServiceQueues.Count);
        for (var index = 0; index < expected.ServiceQueues.Count; index++)
        {
            var left = expected.ServiceQueues[index];
            var right = actual.ServiceQueues[index];
            Assert.AreEqual(left.Id, right.Id);
            Assert.AreEqual(left.ActiveOwnerId, right.ActiveOwnerId);
            Assert.AreEqual(left.RemainingServiceTicks, right.RemainingServiceTicks);
            CollectionAssert.AreEqual(left.OrderedMembers.ToArray(), right.OrderedMembers.ToArray());
            CollectionAssert.AreEqual(left.Agents.ToArray(), right.Agents.ToArray());
        }
    }
}

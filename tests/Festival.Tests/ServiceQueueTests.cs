using Festival.Persistence;
using Festival.Simulation;
using Festival.Simulation.Fixtures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Festival.Tests;

[TestClass]
public sealed class ServiceQueueTests
{
    [TestMethod]
    public void FiveAutonomousGuestsPhysicallyQueueAndPayExactlyOnceInOrder()
    {
        var fixture = ServiceQueueFixture.Create();
        var initial = fixture.Session.CaptureSnapshot().ServiceQueues.Single();
        CollectionAssert.AreEqual(fixture.AgentIds.ToArray(), initial.OrderedMembers.ToArray());
        Assert.AreEqual(5, initial.Agents.Select(item => item.ReservedSlotIndex).Distinct().Count());

        ServiceQueueFixture.AdvanceUntilResolved(fixture.Session);
        var final = fixture.Session.CaptureSnapshot();
        Assert.AreEqual(5, final.Transactions.Count);
        CollectionAssert.AreEqual(fixture.AgentIds.ToArray(), final.Transactions.Select(item => item.BuyerId).ToArray());
        Assert.AreEqual(1_500L, final.FestivalFinances.Single().CashPennies);
        Assert.AreEqual(0, final.OwnedStocks.Single().Quantity);
        Assert.IsTrue(final.Transactions.All(item => item.IsBalanced));
        Assert.AreEqual(5, final.Transactions.Select(item => item.Id).Distinct().Count());
        Assert.IsNull(final.ServiceQueues.Single().ActiveOwnerId);
    }

    [TestMethod]
    public void DelayedFrontGuestCannotBeServedRemotely()
    {
        var service = TraversalGrid.WorldToCell(29_000, -5_000);
        var starts = new[] { new GridCell(5, 240), new GridCell(service.X - 3, service.Z), new GridCell(128, 180), new GridCell(126, 181), new GridCell(130, 182) };
        var fixture = ServiceQueueFixture.Create(starts: starts);
        fixture.Session.AdvanceTicks(100);
        var snapshot = fixture.Session.CaptureSnapshot();
        Assert.AreEqual(0, snapshot.Transactions.Count);
        Assert.IsNull(snapshot.ServiceQueues.Single().ActiveOwnerId);
        Assert.AreNotEqual(AgentNavigationAction.Arrived, snapshot.NavigationAgents.Single(item => item.Id == fixture.AgentIds[0]).Action);
    }

    [TestMethod]
    public void InsufficientCashAndStockoutReleaseOwnersWithoutDoubleCharging()
    {
        var funds = ServiceQueueFixture.Create(new long[] { 0, 500, 500, 500, 500 }, stock: 4);
        ServiceQueueFixture.AdvanceUntilResolved(funds.Session);
        var fundsFinal = funds.Session.CaptureSnapshot();
        Assert.AreEqual(4, fundsFinal.Transactions.Count);
        Assert.AreEqual(ServiceQueueAgentAction.Failed, fundsFinal.ServiceQueues.Single().Agents.Single(item => item.AgentId == funds.AgentIds[0]).Action);
        Assert.AreEqual(0, fundsFinal.OwnedStocks.Single().Quantity);

        var stock = ServiceQueueFixture.Create(stock: 2);
        ServiceQueueFixture.AdvanceUntilResolved(stock.Session);
        var stockFinal = stock.Session.CaptureSnapshot();
        Assert.AreEqual(2, stockFinal.Transactions.Count);
        Assert.AreEqual(3, stockFinal.ServiceQueues.Single().Agents.Count(item => item.Action == ServiceQueueAgentAction.Failed));
        Assert.IsNull(stockFinal.ServiceQueues.Single().ActiveOwnerId);
    }

    [TestMethod]
    public void ClosureAndAbandonmentReleaseAllReservationsAndReopenClean()
    {
        var fixture = ServiceQueueFixture.Create();
        fixture.Session.AdvanceTicks(300);
        Assert.IsTrue(ServiceQueueFixture.Abandon(fixture, fixture.AgentIds[2], 2).IsAccepted);
        Assert.IsTrue(ServiceQueueFixture.SetOpen(fixture, false, 3).IsAccepted);
        var closed = fixture.Session.CaptureSnapshot().ServiceQueues.Single();
        Assert.AreEqual(0, closed.OrderedMembers.Count); Assert.IsNull(closed.ActiveOwnerId); Assert.AreEqual(0, closed.RemainingServiceTicks);
        Assert.IsTrue(closed.Agents.All(item => item.ReservedSlotIndex is null));
        Assert.IsTrue(fixture.Session.CaptureSnapshot().NavigationAgents.All(item => item.Destination is null));

        Assert.IsTrue(ServiceQueueFixture.SetOpen(fixture, true, 4).IsAccepted);
        var reverse = fixture.AgentIds.Reverse().ToArray();
        for (var index = 0; index < reverse.Length; index++) Assert.IsTrue(ServiceQueueFixture.Enqueue(fixture, reverse[index], 7, (ulong)(5 + index)).IsAccepted);
        var reopened = fixture.Session.CaptureSnapshot().ServiceQueues.Single();
        CollectionAssert.AreEqual(fixture.AgentIds.OrderBy(id => id).ToArray(), reopened.OrderedMembers.ToArray(), "Same-tick sequence ties use stable agent ID.");
        Assert.AreEqual(5, reopened.Agents.Select(item => item.ReservedSlotIndex).Distinct().Count());
    }

    [TestMethod]
    public void SaveResumeMidServiceAndCompletionBoundaryMatchUninterrupted()
    {
        foreach (var remaining in new[] { 8, 1 })
        {
            var fixture = ServiceQueueFixture.Create();
            while (fixture.Session.CaptureSnapshot().ServiceQueues.Single().RemainingServiceTicks != remaining) fixture.Session.AdvanceTicks(1);
            var persisted = fixture.Session.CapturePersistenceSnapshot();
            var restored = GameSession.Restore(persisted);
            Assert.IsTrue(restored.IsSuccess, restored.Error);
            fixture.Session.AdvanceTicks(100);
            restored.Session!.AdvanceTicks(100);
            Assert.AreEqual(fixture.Session.CaptureSnapshot().AuthoritativeHash, restored.Session.CaptureSnapshot().AuthoritativeHash);
            Assert.AreEqual(fixture.Session.Transactions.Count, restored.Session.Transactions.Count);
        }
    }

    [TestMethod]
    public void FileSaveRoundTripPreservesMidServiceQueueAndExactContinuation()
    {
        var fixture = ServiceQueueFixture.Create();
        while (fixture.Session.CaptureSnapshot().ServiceQueues.Single().RemainingServiceTicks != 1) fixture.Session.AdvanceTicks(1);
        var directory = Path.Combine(Path.GetTempPath(), "festival-m008-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "queue.ftsave");
            var compatibility = new SaveCompatibility("test-build", "content-v1", "rules-v1");
            var saved = SaveFileAdapter.SaveFile(path, new SaveWriteRequest(fixture.Session, compatibility, "m008-test", DateTimeOffset.UnixEpoch));
            Assert.IsTrue(saved.IsSuccess, saved.Error);
            var loaded = SaveFileAdapter.LoadFile(path, compatibility);
            Assert.IsTrue(loaded.IsSuccess, loaded.Error);
            Assert.AreEqual(fixture.Session.CaptureSnapshot().AuthoritativeHash, loaded.Session!.CaptureSnapshot().AuthoritativeHash);
            fixture.Session.AdvanceTicks(1);
            loaded.Session.AdvanceTicks(1);
            Assert.AreEqual(fixture.Session.CaptureSnapshot().AuthoritativeHash, loaded.Session.CaptureSnapshot().AuthoritativeHash);
            Assert.AreEqual(fixture.Session.Transactions.Count, loaded.Session.Transactions.Count);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void OnlyFrontOwnsServiceAndRepeatedTicksCannotRepeatCompletion()
    {
        var fixture = ServiceQueueFixture.Create(durationTicks: 1);
        while (fixture.Session.Transactions.Count == 0)
        {
            fixture.Session.AdvanceTicks(1);
            var queue = fixture.Session.CaptureSnapshot().ServiceQueues.Single();
            if (queue.ActiveOwnerId is { } owner) Assert.AreEqual(queue.OrderedMembers[0], owner);
        }
        var transaction = fixture.Session.Transactions.Single();
        fixture.Session.AdvanceTicks(10);
        Assert.AreEqual(1, fixture.Session.Transactions.Count(item => item.Id == transaction.Id));
    }

    [TestMethod]
    public void NoRouteCannotLeaveStaleLogicalOrPhysicalReservation()
    {
        var fixture = ServiceQueueFixture.Create();
        var blocked = fixture.Session.TraversalGrid!.Overrides.Values.First(item => !item.IsWalkable).Cell;
        var agentId = fixture.AgentIds[2];
        var result = fixture.Session.Execute(new CommandEnvelope(
            new CommandId(2), fixture.Session.CampaignId, fixture.Session.Phase, fixture.Session.CurrentTick,
            fixture.Session.NextSubmissionSequence, agentId, new SetAgentDestinationCommand(blocked, "fixture.route-failure")));
        Assert.IsTrue(result.IsAccepted);
        fixture.Session.AdvanceTicks(1);
        var queue = fixture.Session.CaptureSnapshot().ServiceQueues.Single();
        Assert.IsFalse(queue.OrderedMembers.Contains(agentId));
        var agent = queue.Agents.Single(item => item.AgentId == agentId);
        Assert.AreEqual(ServiceQueueAgentAction.Failed, agent.Action);
        Assert.IsNull(agent.ReservedSlotIndex);
        Assert.IsNull(fixture.Session.CaptureSnapshot().NavigationAgents.Single(item => item.Id == agentId).Destination);
    }

    [TestMethod]
    public void SameBuildTickBatchingProducesIdenticalQueueState()
    {
        var single = ServiceQueueFixture.Create();
        var batches = ServiceQueueFixture.Create();
        single.Session.AdvanceTicks(1_600);
        for (var index = 0; index < 160; index++) batches.Session.AdvanceTicks(10);
        Assert.AreEqual(single.Session.CaptureSnapshot().AuthoritativeHash, batches.Session.CaptureSnapshot().AuthoritativeHash);
    }
}

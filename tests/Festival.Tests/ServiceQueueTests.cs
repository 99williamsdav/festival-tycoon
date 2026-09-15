using System.Collections;
using System.Reflection;
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
        ServiceQueueFixture.AdvanceUntilDeparted(fixture);
        Assert.IsTrue(ServiceQueueFixture.AllAtExit(fixture));
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
        var closedNavigation = fixture.Session.CaptureSnapshot().NavigationAgents;
        Assert.AreEqual(5, closedNavigation.Select(item => item.Destination).Distinct().Count());
        Assert.IsTrue(closedNavigation.All(item => item.IntentId == "ai.service-exit"));

        Assert.IsTrue(ServiceQueueFixture.SetOpen(fixture, true, 4).IsAccepted);
        var reverse = fixture.AgentIds.Reverse().ToArray();
        for (var index = 0; index < reverse.Length; index++)
            Assert.IsTrue(ServiceQueueFixture.Enqueue(fixture, reverse[index], fixture.Session.NextSubmissionSequence, (ulong)(5 + index)).IsAccepted);
        var reopened = fixture.Session.CaptureSnapshot().ServiceQueues.Single();
        CollectionAssert.AreEqual(reverse, reopened.OrderedMembers.ToArray(), "Valid same-tick admissions retain authoritative submission order.");
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
        var service = TraversalGrid.WorldToCell(29_000, -5_000);
        var slots = Enumerable.Range(0, 5).Select(index => new GridCell(service.X - index * 3, service.Z + index * 2)).ToArray();
        var isolated = slots[2];
        var terrain = NavigationFixture.CreateLowerWitteringTerrain().ToList();
        for (var z = -1; z <= 1; z++)
        for (var x = -1; x <= 1; x++)
            if (x != 0 || z != 0) terrain.Add(new TerrainCellOverride(new GridCell(isolated.X + x, isolated.Z + z), GroundSurface.Grass, false));
        var fixture = ServiceQueueFixture.Create(terrain: terrain, slots: slots);
        var agentId = fixture.AgentIds[2];
        fixture.Session.AdvanceTicks(1);
        var queue = fixture.Session.CaptureSnapshot().ServiceQueues.Single();
        Assert.IsFalse(queue.OrderedMembers.Contains(agentId));
        var agent = queue.Agents.Single(item => item.AgentId == agentId);
        Assert.AreEqual(ServiceQueueAgentAction.Failed, agent.Action);
        Assert.IsNull(agent.ReservedSlotIndex);
        Assert.AreEqual(queue.ExitCells[agent.ExitIndex], fixture.Session.CaptureSnapshot().NavigationAgents.Single(item => item.Id == agentId).Destination);
    }

    [TestMethod]
    public void StaleRejoinCannotDisplaceActiveOwnerAndValidLateAdmissionAppends()
    {
        var fixture = ServiceQueueFixture.Create(durationTicks: 20);
        Assert.IsTrue(ServiceQueueFixture.Abandon(fixture, fixture.AgentIds[0], 2).IsAccepted);
        while (fixture.Session.CaptureSnapshot().ServiceQueues.Single().ActiveOwnerId != fixture.AgentIds[1]) fixture.Session.AdvanceTicks(1);
        var before = fixture.Session.CaptureSnapshot().ServiceQueues.Single();
        var stale = ServiceQueueFixture.Enqueue(fixture, fixture.AgentIds[0], 0, 3);
        Assert.IsFalse(stale.IsAccepted);
        Assert.AreEqual(CommandReasonCode.InvalidParameter, stale.ReasonCode);
        var valid = ServiceQueueFixture.Enqueue(fixture, fixture.AgentIds[0], fixture.Session.NextSubmissionSequence, 4);
        Assert.IsTrue(valid.IsAccepted);
        var after = fixture.Session.CaptureSnapshot().ServiceQueues.Single();
        Assert.AreEqual(fixture.AgentIds[1], after.ActiveOwnerId);
        Assert.AreEqual(fixture.AgentIds[1], after.OrderedMembers[0]);
        Assert.AreEqual(fixture.AgentIds[0], after.OrderedMembers[^1]);
        fixture.Session.AdvanceTicks(1); // exact former crash boundary
        Assert.AreEqual(fixture.AgentIds[1], fixture.Session.CaptureSnapshot().ServiceQueues.Single().ActiveOwnerId);
    }

    [TestMethod]
    public void FailedAndAbandonedCustomersClearCounterAndFinishAtDistinctExits()
    {
        var fixture = ServiceQueueFixture.Create(new long[] { 0, 500, 500, 500, 500 }, stock: 4, durationTicks: 1);
        var previousTransactions = 0;
        while (fixture.Session.CaptureSnapshot().ServiceQueues.Single().OrderedMembers.Count > 0)
        {
            fixture.Session.AdvanceTicks(1);
            var current = fixture.Session.CaptureSnapshot();
            if (current.Transactions.Count <= previousTransactions) continue;
            var failedPosition = current.NavigationAgents.Single(item => item.Id == fixture.AgentIds[0]);
            var buyerPosition = current.NavigationAgents.Single(item => item.Id == current.Transactions[^1].BuyerId);
            Assert.AreNotEqual((failedPosition.XMillimetres, failedPosition.ZMillimetres),
                (buyerPosition.XMillimetres, buyerPosition.ZMillimetres), "Failed front customer must clear the counter before a later completion.");
            previousTransactions = current.Transactions.Count;
        }
        ServiceQueueFixture.AdvanceUntilDeparted(fixture);
        var snapshot = fixture.Session.CaptureSnapshot();
        var queue = snapshot.ServiceQueues.Single();
        var positions = snapshot.NavigationAgents.Select(item => (item.XMillimetres, item.ZMillimetres)).ToArray();
        Assert.AreEqual(positions.Length, positions.Distinct().Count());
        var failed = queue.Agents.Single(item => item.AgentId == fixture.AgentIds[0]);
        var failedNavigation = snapshot.NavigationAgents.Single(item => item.Id == failed.AgentId);
        Assert.AreEqual(queue.ExitCells[failed.ExitIndex], failedNavigation.Destination);
        Assert.AreNotEqual(TraversalGrid.CellCentre(queue.QueueSlots[0]), (failedNavigation.XMillimetres, failedNavigation.ZMillimetres));
        Assert.AreEqual(4, snapshot.Transactions.Count);

        var abandoned = ServiceQueueFixture.Create(durationTicks: 1);
        while (abandoned.Session.CaptureSnapshot().ServiceQueues.Single().ActiveOwnerId is null) abandoned.Session.AdvanceTicks(1);
        var abandonedOwner = abandoned.Session.CaptureSnapshot().ServiceQueues.Single().ActiveOwnerId!.Value;
        Assert.IsTrue(ServiceQueueFixture.Abandon(abandoned, abandonedOwner, 2).IsAccepted);
        while (abandoned.Session.CaptureSnapshot().ServiceQueues.Single().ActiveOwnerId is null) abandoned.Session.AdvanceTicks(1);
        var afterAbandon = abandoned.Session.CaptureSnapshot();
        var formerPosition = afterAbandon.NavigationAgents.Single(item => item.Id == abandonedOwner);
        var newOwnerPosition = afterAbandon.NavigationAgents.Single(item => item.Id == afterAbandon.ServiceQueues.Single().ActiveOwnerId);
        Assert.AreNotEqual((formerPosition.XMillimetres, formerPosition.ZMillimetres), (newOwnerPosition.XMillimetres, newOwnerPosition.ZMillimetres));
        ServiceQueueFixture.AdvanceUntilResolved(abandoned.Session);
        ServiceQueueFixture.AdvanceUntilDeparted(abandoned);
        Assert.IsTrue(ServiceQueueFixture.AllAtExit(abandoned));
        Assert.IsFalse(abandoned.Session.Transactions.Any(item => item.BuyerId == abandonedOwner),
            "An active customer who abandons must never be charged at the completion boundary.");
    }

    [TestMethod]
    public void InServiceDestinationIsProtectedAndCompletionRechecksPresence()
    {
        var fixture = ServiceQueueFixture.Create();
        while (fixture.Session.CaptureSnapshot().ServiceQueues.Single().ActiveOwnerId is null) fixture.Session.AdvanceTicks(1);
        var queue = fixture.Session.CaptureSnapshot().ServiceQueues.Single();
        var owner = queue.ActiveOwnerId!.Value;
        var retarget = fixture.Session.Execute(new CommandEnvelope(new CommandId(2), fixture.Session.CampaignId,
            fixture.Session.Phase, fixture.Session.CurrentTick, fixture.Session.NextSubmissionSequence, owner,
            new SetAgentDestinationCommand(new GridCell(100, 180), "fixture.walk-away")));
        Assert.IsFalse(retarget.IsAccepted);
        fixture.Session.AdvanceTicks(ServiceQueueFixture.DefaultServiceDurationTicks);
        Assert.AreEqual(owner, fixture.Session.Transactions.Single().BuyerId);
        var completedAt = fixture.Session.CaptureSnapshot().NavigationAgents.Single(item => item.Id == owner);
        Assert.AreEqual(queue.QueueSlots[0], TraversalGrid.WorldToCell(completedAt.SegmentOriginXMillimetres, completedAt.SegmentOriginZMillimetres));

        var boundary = ServiceQueueFixture.Create(durationTicks: 1);
        while (boundary.Session.CaptureSnapshot().ServiceQueues.Single().RemainingServiceTicks != 1) boundary.Session.AdvanceTicks(1);
        Assert.IsTrue(ServiceQueueFixture.SetOpen(boundary, false, 2).IsAccepted);
        boundary.Session.AdvanceTicks(1);
        Assert.AreEqual(0, boundary.Session.Transactions.Count, "Closure at the exact completion boundary cancels without payment.");
    }

    [TestMethod]
    public void MalformedQueueCrossFieldsAreRejectedBeforeHashReconstruction()
    {
        var closedFixture = ActiveFixture();
        SetProperty(PrivateEntry(closedFixture.Session, "_serviceQueues"), "IsOpen", false);
        var closedSnapshot = closedFixture.Session.CapturePersistenceSnapshot();
        var closedResult = GameSession.Restore(closedSnapshot);
        Assert.IsFalse(closedResult.IsSuccess);
        StringAssert.Contains(closedResult.Error!, "Closed service queue");

        var walkingFixture = ActiveFixture();
        var owner = walkingFixture.Session.CaptureSnapshot().ServiceQueues.Single().ActiveOwnerId!.Value;
        var navigation = PrivateEntry(walkingFixture.Session, "_navigationAgents", owner);
        SetProperty(navigation, "Action", AgentNavigationAction.Travelling);
        var x = (int)navigation.GetType().GetProperty("XMillimetres")!.GetValue(navigation)!;
        SetProperty(navigation, "XMillimetres", x - 10_000);
        var walkingSnapshot = walkingFixture.Session.CapturePersistenceSnapshot();
        var walkingResult = GameSession.Restore(walkingSnapshot);
        Assert.IsFalse(walkingResult.IsSuccess);
        StringAssert.Contains(walkingResult.Error!, "active owner is not physically present");

        var duplicateFixture = ActiveFixture();
        var duplicateQueue = PrivateEntry(duplicateFixture.Session, "_serviceQueues");
        var slots = (IList)duplicateQueue.GetType().GetProperty("QueueSlots")!.GetValue(duplicateQueue)!;
        slots[1] = slots[0];
        var duplicateSnapshot = duplicateFixture.Session.CapturePersistenceSnapshot();
        var duplicateResult = GameSession.Restore(duplicateSnapshot);
        Assert.IsFalse(duplicateResult.IsSuccess);
        StringAssert.Contains(duplicateResult.Error!, "distinct walkable physical queue and exit cells");
    }

    private static ServiceQueueFixtureState ActiveFixture()
    {
        var fixture = ServiceQueueFixture.Create();
        while (fixture.Session.CaptureSnapshot().ServiceQueues.Single().ActiveOwnerId is null) fixture.Session.AdvanceTicks(1);
        return fixture;
    }

    private static object PrivateEntry(GameSession session, string fieldName, EntityId? id = null)
    {
        var field = typeof(GameSession).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var entry in (IEnumerable)field.GetValue(session)!)
        {
            if (id is null || (EntityId)entry.GetType().GetProperty("Key")!.GetValue(entry)! == id.Value)
                return entry.GetType().GetProperty("Value")!.GetValue(entry)!;
        }
        throw new AssertFailedException($"No entry found in {fieldName}.");
    }

    private static void SetProperty(object target, string propertyName, object value) =>
        target.GetType().GetProperty(propertyName)!.SetValue(target, value);

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

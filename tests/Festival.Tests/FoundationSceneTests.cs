using Festival.Persistence;
using Festival.Simulation;
using Festival.Simulation.Fixtures;

namespace Festival.Tests;

[TestClass]
public sealed class FoundationSceneTests
{
    private static readonly SaveCompatibility Compatibility = new("m0.09-tests", "content", "rules");

    [TestMethod]
    public void FiftyAutonomousAttendees_AllPurchaseAndDepartWithoutReservations()
    {
        var fixture = FiftyAgentFoundationFixture.Create();
        var consecutiveBelowTwoHundred = new Dictionary<(EntityId, EntityId), int>();
        var maximumConsecutiveBelowTwoHundred = 0;
        var reproPairMaximum = 0;
        var previousPositions = fixture.Session.CaptureSnapshot().NavigationAgents.ToDictionary(item => item.Id);
        while (!FiftyAgentFoundationFixture.AllCompleted(fixture))
        {
            fixture.Session.AdvanceTicks(1);
            var tick = fixture.Session.CaptureSnapshot();
            Assert.AreEqual(tick.NavigationAgents.Count, tick.NavigationAgents.Select(item => (item.XMillimetres, item.ZMillimetres)).Distinct().Count(),
                $"Exact attendee overlap at tick {tick.CurrentTick}.");
            Assert.IsTrue(tick.NavigationAgents.All(item => fixture.Session.TraversalGrid!.Get(TraversalGrid.WorldToCell(item.XMillimetres, item.ZMillimetres)).IsWalkable),
                $"Blocked-cell crossing at tick {tick.CurrentTick}.");
            foreach (var agent in tick.NavigationAgents)
            {
                var previous = previousPositions[agent.Id];
                Assert.IsTrue(TraversalSweep.IsWalkable(fixture.Session.TraversalGrid!, previous.XMillimetres, previous.ZMillimetres,
                    agent.XMillimetres, agent.ZMillimetres), $"Blocked swept segment at tick {tick.CurrentTick} for attendee {agent.Id}.");
                if (tick.CurrentTick == 792 && agent.Id == new EntityId(21))
                {
                    Assert.IsTrue(TraversalSweep.IsWalkable(fixture.Session.TraversalGrid!, previous.XMillimetres, previous.ZMillimetres,
                        agent.XMillimetres, agent.ZMillimetres), "Exact tick-792 attendee-21 avoidance regression crossed blocked cell (142,138).");
                    Assert.AreNotEqual(((6_450, 5_028), (7_050, 4_998)),
                        ((previous.XMillimetres, previous.ZMillimetres), (agent.XMillimetres, agent.ZMillimetres)),
                        "The exact formerly illegal tick-792 attendee-21 segment reappeared.");
                }
            }
            foreach (var left in tick.NavigationAgents)
            foreach (var right in tick.NavigationAgents.Where(item => item.Id.CompareTo(left.Id) > 0))
            {
                var dx = (long)right.XMillimetres - left.XMillimetres;
                var dz = (long)right.ZMillimetres - left.ZMillimetres;
                var pair = (left.Id, right.Id);
                var consecutive = dx * dx + dz * dz < 40_000 ? consecutiveBelowTwoHundred.GetValueOrDefault(pair) + 1 : 0;
                consecutiveBelowTwoHundred[pair] = consecutive;
                maximumConsecutiveBelowTwoHundred = Math.Max(maximumConsecutiveBelowTwoHundred, consecutive);
                if (left.Id == new EntityId(20) && right.Id == new EntityId(21)) reproPairMaximum = Math.Max(reproPairMaximum, consecutive);
            }
            Assert.IsTrue(tick.CurrentTick < 30_000, "Persistent stacking/deadlock exceeded the fixture budget.");
            previousPositions = tick.NavigationAgents.ToDictionary(item => item.Id);
        }
        Assert.IsTrue(maximumConsecutiveBelowTwoHundred <= 2, $"Near-superposition persisted for {maximumConsecutiveBelowTwoHundred} ticks.");
        Assert.IsTrue(reproPairMaximum <= 2, $"IDs 20/21 remained compressed for {reproPairMaximum} ticks.");
        var snapshot = fixture.Session.CaptureSnapshot();
        Assert.AreEqual(50, snapshot.Transactions.Count);
        Assert.AreEqual(15_000L, snapshot.FestivalFinances.Single().CashPennies);
        Assert.AreEqual(0, snapshot.OwnedStocks.Single().Quantity);
        Assert.AreEqual(0, snapshot.ServiceQueues.Single().OrderedMembers.Count);
        Assert.IsNull(snapshot.ServiceQueues.Single().ActiveOwnerId);
        Assert.IsTrue(snapshot.ServiceQueues.Single().Agents.All(item => item.ReservedSlotIndex is null && !item.OwnsExitReservation));
        Assert.IsTrue(snapshot.NavigationAgents.All(item => fixture.Session.TraversalGrid!.Get(TraversalGrid.WorldToCell(item.XMillimetres, item.ZMillimetres)).IsWalkable));
    }

    [TestMethod]
    public void FiftyAgentFixture_IsOneXFourXAndSaveResumeEquivalent()
    {
        var one = FiftyAgentFoundationFixture.Create();
        var four = FiftyAgentFoundationFixture.Create();
        var oneClock = new FoundationClock { RequestedSpeed = RequestedSpeed.OneX };
        var fourClock = new FoundationClock { RequestedSpeed = RequestedSpeed.FourX };
        while (one.Session.CurrentTick < 1_000)
            one.Session.AdvanceTicks(Math.Min(oneClock.Schedule(1.0 / 60), 1_000 - (int)one.Session.CurrentTick));
        while (four.Session.CurrentTick < 1_000)
            four.Session.AdvanceTicks(Math.Min(fourClock.Schedule(1.0 / 60), 1_000 - (int)four.Session.CurrentTick));
        Assert.AreEqual(one.Session.CaptureSnapshot().AuthoritativeHash, four.Session.CaptureSnapshot().AuthoritativeHash);

        var persisted = one.Session.CapturePersistenceSnapshot();
        var restored = GameSession.Restore(persisted);
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        var before = one.Session.CaptureSnapshot();
        var after = restored.Session!.CaptureSnapshot();
        CollectionAssert.AreEqual(before.ServiceQueues.Single().OrderedMembers.ToArray(), after.ServiceQueues.Single().OrderedMembers.ToArray());
        Assert.AreEqual(before.ServiceQueues.Single().ActiveOwnerId, after.ServiceQueues.Single().ActiveOwnerId);
        Assert.AreEqual(before.ServiceQueues.Single().RemainingServiceTicks, after.ServiceQueues.Single().RemainingServiceTicks);
        CollectionAssert.AreEqual(before.NavigationAgents.Select(item => (item.Id, item.XMillimetres, item.ZMillimetres, item.Action)).ToArray(),
            after.NavigationAgents.Select(item => (item.Id, item.XMillimetres, item.ZMillimetres, item.Action)).ToArray());
        while (!FiftyAgentFoundationFixture.AllCompleted(one))
        {
            one.Session.AdvanceTicks(1);
            restored.Session!.AdvanceTicks(1);
        }
        Assert.AreEqual(one.Session.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void PauseFreezesCompletionBoundaryAndClockReportsOverloadWithoutSkippingDebt()
    {
        var fixture = FiftyAgentFoundationFixture.Create();
        while (fixture.Session.CaptureSnapshot().ServiceQueues.Single().RemainingServiceTicks != 1) fixture.Session.AdvanceTicks(1);
        fixture.Session.Execute(new CommandEnvelope(new CommandId(2), fixture.Session.CampaignId, fixture.Session.Phase,
            fixture.Session.CurrentTick, fixture.Session.NextSubmissionSequence, null, new SetPausedCommand(true)));
        var before = fixture.Session.CaptureSnapshot().AuthoritativeHash;
        fixture.Session.AdvanceTicks(100);
        Assert.AreEqual(before, fixture.Session.CaptureSnapshot().AuthoritativeHash);

        var clock = new FoundationClock { RequestedSpeed = RequestedSpeed.FourX };
        var processed = clock.Schedule(1, 3);
        Assert.AreEqual(3, processed);
        Assert.IsTrue(clock.IsOverloaded);
        Assert.IsTrue(clock.DebtTicks > 0);
        Assert.IsTrue(clock.AttainedSpeed < 4);
    }

    [TestMethod]
    public void NeighbourIndexAndPaletteAssignmentAreStable()
    {
        var fixture = FiftyAgentFoundationFixture.Create();
        var snapshot = fixture.Session.CaptureSnapshot();
        var reversed = new SpatialNeighbourIndex(snapshot.NavigationAgents.Reverse());
        var normal = new SpatialNeighbourIndex(snapshot.NavigationAgents);
        var first = snapshot.NavigationAgents.First();
        CollectionAssert.AreEqual(normal.Query(first.XMillimetres, first.ZMillimetres, 2_000).ToArray(),
            reversed.Query(first.XMillimetres, first.ZMillimetres, 2_000).ToArray());
        CollectionAssert.AreEqual(Enumerable.Range(0, 50).Select(AttendeePaletteAssignment.FromOrdinal).ToArray(),
            Enumerable.Range(0, 5).SelectMany(_ => Enumerable.Range(0, 10)).ToArray());

        var boundary = new SpatialNeighbourIndex(new[] { Agent(1, 999, 999), Agent(2, 1_001, 1_001), Agent(3, -1, -1) });
        CollectionAssert.AreEqual(new[] { new EntityId(1), new EntityId(2) }, boundary.Query(1_000, 1_000, 2).ToArray());
        CollectionAssert.AreEqual(new[] { new EntityId(3) }, boundary.Query(-1, -1, 0).ToArray());
    }

    [TestMethod]
    public void DiagnosticsCountPhysicalActionsAndInterpolationIsCosmeticAndResettable()
    {
        var fixture = FiftyAgentFoundationFixture.Create();
        var initial = fixture.Session.CaptureSnapshot();
        var counts = FoundationDiagnostics.Count(initial);
        Assert.AreEqual(50, counts.Travelling);
        Assert.AreEqual(50, counts.QueueMembers);
        Assert.AreEqual(0, counts.Waiting);
        Assert.AreEqual(0, counts.InService);
        Assert.AreEqual(0, counts.Served);

        FoundationDiagnosticCounts physical;
        do
        {
            fixture.Session.AdvanceTicks(1);
            physical = FoundationDiagnostics.Count(fixture.Session.CaptureSnapshot());
        } while (physical.Waiting == 0 && fixture.Session.CurrentTick < 2_000);
        Assert.IsTrue(physical.Waiting > 0);
        Assert.IsTrue(physical.QueueMembers >= physical.Waiting + physical.InService);
        Assert.IsTrue(physical.Travelling < 50);

        var interpolator = new FoundationPresentationInterpolator();
        var start = fixture.Session.CaptureSnapshot();
        interpolator.Reset(start);
        var hash = start.AuthoritativeHash;
        SessionSnapshot previous = start;
        SessionSnapshot current = start;
        for (var tick = 0; tick < 4; tick++)
        {
            fixture.Session.AdvanceTicks(1);
            previous = current;
            current = fixture.Session.CaptureSnapshot();
            interpolator.Advance(current);
        }
        var id = current.NavigationAgents.First(item =>
        {
            var prior = previous.NavigationAgents.Single(value => value.Id == item.Id);
            return prior.XMillimetres != item.XMillimetres || prior.ZMillimetres != item.ZMillimetres;
        }).Id;
        var midpoint = interpolator.Sample(id, 0.5);
        Assert.AreNotEqual(hash, current.AuthoritativeHash);
        Assert.AreEqual(current.AuthoritativeHash, fixture.Session.CaptureSnapshot().AuthoritativeHash, "Presentation sampling changed authoritative state.");
        var previousAgent = previous.NavigationAgents.Single(item => item.Id == id);
        var currentAgent = current.NavigationAgents.Single(item => item.Id == id);
        Assert.AreEqual((previousAgent.XMillimetres + currentAgent.XMillimetres) / 2.0, midpoint.XMillimetres, 0.001);
        Assert.AreEqual((previousAgent.ZMillimetres + currentAgent.ZMillimetres) / 2.0, midpoint.ZMillimetres, 0.001);
        interpolator.Reset(current);
        Assert.AreEqual((double)current.NavigationAgents[0].XMillimetres, interpolator.Sample(id, 0).XMillimetres);
        Assert.IsTrue(midpoint != interpolator.Sample(id, 0));

        fixture.Session.AdvanceTicks(3);
        var discontinuous = fixture.Session.CaptureSnapshot();
        interpolator.Advance(discontinuous);
        Assert.AreEqual((double)discontinuous.NavigationAgents.Single(item => item.Id == id).XMillimetres, interpolator.Sample(id, 0.5).XMillimetres);

        var turning = NavigationFixture.CreateGateToServiceSession();
        Assert.IsTrue(NavigationFixture.IssueAutonomousServiceIntent(turning).IsAccepted);
        var turningInterpolator = new FoundationPresentationInterpolator();
        var turnPrevious = turning.Session.CaptureSnapshot();
        turningInterpolator.Reset(turnPrevious);
        (int X, int Z)? priorVector = null;
        var observedTurn = false;
        for (var tick = 0; tick < 2_000 && !observedTurn; tick++)
        {
            turning.Session.AdvanceTicks(1);
            var turnCurrent = turning.Session.CaptureSnapshot();
            turningInterpolator.Advance(turnCurrent);
            var beforeAgent = turnPrevious.NavigationAgents.Single();
            var afterAgent = turnCurrent.NavigationAgents.Single();
            var vector = (afterAgent.XMillimetres - beforeAgent.XMillimetres, afterAgent.ZMillimetres - beforeAgent.ZMillimetres);
            if (priorVector is { } prior && vector != (0, 0) && prior != (0, 0) &&
                (long)prior.X * vector.Item2 != (long)prior.Z * vector.Item1)
            {
                var atTurn = turningInterpolator.Sample(afterAgent.Id, 0.5);
                Assert.AreEqual((beforeAgent.XMillimetres + afterAgent.XMillimetres) / 2.0, atTurn.XMillimetres, 0.001);
                Assert.AreEqual((beforeAgent.ZMillimetres + afterAgent.ZMillimetres) / 2.0, atTurn.ZMillimetres, 0.001);
                observedTurn = true;
            }
            if (vector != (0, 0)) priorVector = vector;
            turnPrevious = turnCurrent;
        }
        Assert.IsTrue(observedTurn, "The presentation regression did not exercise a route turn.");
    }

    [TestMethod]
    public void AutosaveRotatesThreeSlotsAndRecoversNewestValid()
    {
        var directory = Path.Combine(Path.GetTempPath(), "festival-m009-" + Guid.NewGuid().ToString("N"));
        try
        {
            var olderHighTick = FiftyAgentFoundationFixture.Create();
            olderHighTick.Session.AdvanceTicks(1_000);
            var time = new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
            Assert.IsTrue(AutosaveRotation.Save(directory, olderHighTick.Session, Compatibility, time, 0).IsSuccess);
            var rolledBack = FiftyAgentFoundationFixture.Create();
            rolledBack.Session.AdvanceTicks(10);
            Assert.IsTrue(AutosaveRotation.Save(directory, rolledBack.Session, Compatibility, time.AddSeconds(1), 1).IsSuccess);
            rolledBack.Session.AdvanceTicks(10);
            Assert.IsTrue(AutosaveRotation.Save(directory, rolledBack.Session, Compatibility, time.AddSeconds(1), 2).IsSuccess);
            Assert.AreEqual(3, Directory.GetFiles(directory, "autosave-*.ftsave").Length);
            var newestAfterRestart = AutosaveRotation.LoadNewestValid(directory, Compatibility);
            Assert.IsTrue(newestAfterRestart.IsSuccess, newestAfterRestart.Error);
            Assert.AreEqual(20, newestAfterRestart.Session!.CurrentTick, "Rollback recovery must follow autosave chronology, not highest simulation tick.");
            Assert.AreEqual(3, AutosaveRotation.NextGeneration(directory, Compatibility));

            rolledBack.Session.AdvanceTicks(10);
            Assert.IsTrue(AutosaveRotation.Save(directory, rolledBack.Session, Compatibility, time.AddSeconds(2), 3).IsSuccess);
            File.WriteAllBytes(SaveFileAdapter.ResolveSlotPath(directory, AutosaveRotation.SlotForGeneration(3)), [1,2,3]);
            var recovered = AutosaveRotation.LoadNewestValid(directory, Compatibility);
            Assert.IsTrue(recovered.IsSuccess, recovered.Error);
            Assert.AreEqual(20, recovered.Session!.CurrentTick, "Corrupt newest generation must fall back to the prior successful autosave.");

            var legacyDirectory = Path.Combine(directory, "legacy");
            Assert.IsTrue(SaveFileAdapter.SaveSlot(legacyDirectory, "autosave-0", new SaveWriteRequest(
                olderHighTick.Session, Compatibility, "autosave", time)).IsSuccess);
            Assert.IsTrue(SaveFileAdapter.SaveSlot(legacyDirectory, "autosave-1", new SaveWriteRequest(
                rolledBack.Session, Compatibility, "autosave", time.AddSeconds(1))).IsSuccess);
            Assert.AreEqual(30, AutosaveRotation.LoadNewestValid(legacyDirectory, Compatibility).Session!.CurrentTick,
                "Legacy slots without generation metadata fall back to persisted timestamp, not simulation tick.");
            Assert.IsTrue(SaveFileAdapter.SaveSlot(legacyDirectory, "autosave-2", new SaveWriteRequest(
                olderHighTick.Session, Compatibility, "autosave", time.AddSeconds(1))).IsSuccess);
            Assert.AreEqual(1_000, AutosaveRotation.LoadNewestValid(legacyDirectory, Compatibility).Session!.CurrentTick,
                "Equal-time legacy ties use descending slot path deterministically.");

            var oneX = new RealTimeAutosaveScheduler();
            var fourX = new RealTimeAutosaveScheduler();
            var paused = new RealTimeAutosaveScheduler();
            var overloaded = new RealTimeAutosaveScheduler();
            Assert.IsFalse(oneX.Advance(299)); Assert.IsTrue(oneX.Advance(1));
            Assert.IsFalse(fourX.Advance(150)); Assert.IsTrue(fourX.Advance(150));
            Assert.IsTrue(paused.Advance(300), "Pause must not suppress real-time autosave.");
            Assert.IsTrue(overloaded.Advance(1_200), "A delayed/overloaded frame must schedule one safe write.");
            Assert.IsFalse(overloaded.Advance(0), "A delayed frame must not burst multiple saves.");
            overloaded.Rebase();
            Assert.IsFalse(overloaded.Advance(299), "Load rebase must prevent immediate slot churn.");
            Assert.IsTrue(overloaded.Advance(1));
            var forwardLoad = new RealTimeAutosaveScheduler(10);
            var backwardLoad = new RealTimeAutosaveScheduler(10);
            Assert.IsFalse(forwardLoad.Advance(9)); forwardLoad.Rebase(); // loaded session tick may be ahead
            Assert.IsFalse(backwardLoad.Advance(9)); backwardLoad.Rebase(); // or behind
            Assert.IsFalse(forwardLoad.Advance(9)); Assert.IsTrue(forwardLoad.Advance(1));
            Assert.IsFalse(backwardLoad.Advance(9)); Assert.IsTrue(backwardLoad.Advance(1));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static NavigationAgentSnapshot Agent(ulong id, int x, int z) => new(new EntityId(id), x, z,
        AgentNavigationAction.Idle, null, Array.Empty<GridCell>(), 0, x, z, 0, 0, 0, null);
}

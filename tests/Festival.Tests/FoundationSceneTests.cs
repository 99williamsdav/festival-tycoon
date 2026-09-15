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
        while (!FiftyAgentFoundationFixture.AllCompleted(fixture))
        {
            fixture.Session.AdvanceTicks(1);
            var tick = fixture.Session.CaptureSnapshot();
            Assert.AreEqual(tick.NavigationAgents.Count, tick.NavigationAgents.Select(item => (item.XMillimetres, item.ZMillimetres)).Distinct().Count(),
                $"Exact attendee overlap at tick {tick.CurrentTick}.");
            Assert.IsTrue(tick.NavigationAgents.All(item => fixture.Session.TraversalGrid!.Get(TraversalGrid.WorldToCell(item.XMillimetres, item.ZMillimetres)).IsWalkable),
                $"Blocked-cell crossing at tick {tick.CurrentTick}.");
            Assert.IsTrue(tick.CurrentTick < 30_000, "Persistent stacking/deadlock exceeded the fixture budget.");
        }
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
        Assert.AreEqual(49, counts.Waiting);
        Assert.AreEqual(0, counts.InService);
        Assert.AreEqual(0, counts.Served);

        var interpolator = new FoundationPresentationInterpolator();
        interpolator.Reset(initial);
        var hash = initial.AuthoritativeHash;
        fixture.Session.AdvanceTicks(4);
        var current = fixture.Session.CaptureSnapshot();
        interpolator.Advance(current);
        var id = current.NavigationAgents[0].Id;
        var midpoint = interpolator.Sample(id, 0.5);
        Assert.AreNotEqual(hash, current.AuthoritativeHash);
        Assert.AreEqual(current.AuthoritativeHash, fixture.Session.CaptureSnapshot().AuthoritativeHash, "Presentation sampling changed authoritative state.");
        interpolator.Reset(current);
        Assert.AreEqual((double)current.NavigationAgents[0].XMillimetres, interpolator.Sample(id, 0).XMillimetres);
        Assert.IsTrue(midpoint != interpolator.Sample(id, 0));
    }

    [TestMethod]
    public void AutosaveRotatesThreeSlotsAndRecoversNewestValid()
    {
        var directory = Path.Combine(Path.GetTempPath(), "festival-m009-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fixture = FiftyAgentFoundationFixture.Create();
            for (var index = 0; index < 4; index++)
            {
                fixture.Session.AdvanceTicks((int)AutosaveRotation.CaptureFixtureCadenceTicks);
                Assert.IsTrue(AutosaveRotation.Save(directory, fixture.Session, Compatibility, DateTimeOffset.UtcNow, AutosaveRotation.CaptureFixtureCadenceTicks).IsSuccess);
            }
            Assert.AreEqual(3, Directory.GetFiles(directory, "autosave-*.ftsave").Length);
            File.WriteAllBytes(SaveFileAdapter.ResolveSlotPath(directory, AutosaveRotation.SlotForTick(fixture.Session.CurrentTick, AutosaveRotation.CaptureFixtureCadenceTicks)), [1,2,3]);
            var recovered = AutosaveRotation.LoadNewestValid(directory, Compatibility);
            Assert.IsTrue(recovered.IsSuccess, recovered.Error);
            Assert.IsTrue(recovered.Session!.CurrentTick < fixture.Session.CurrentTick);
            Assert.AreEqual(24_000, AutosaveRotation.NextDeadline(0));
            Assert.AreEqual(48_000, AutosaveRotation.NextDeadline(24_001));
            Assert.AreEqual(800, AutosaveRotation.NextDeadline(100, AutosaveRotation.CaptureFixtureCadenceTicks));
            Assert.AreEqual(3_200, AutosaveRotation.NextDeadline(2_401, AutosaveRotation.CaptureFixtureCadenceTicks));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static NavigationAgentSnapshot Agent(ulong id, int x, int z) => new(new EntityId(id), x, z,
        AgentNavigationAction.Idle, null, Array.Empty<GridCell>(), 0, x, z, 0, 0, 0, null);
}

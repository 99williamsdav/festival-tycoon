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
        FiftyAgentFoundationFixture.AdvanceUntilCompleted(fixture);
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
                fixture.Session.AdvanceTicks((int)AutosaveRotation.CadenceTicks);
                Assert.IsTrue(AutosaveRotation.Save(directory, fixture.Session, Compatibility, DateTimeOffset.UtcNow).IsSuccess);
            }
            Assert.AreEqual(3, Directory.GetFiles(directory, "autosave-*.ftsave").Length);
            File.WriteAllBytes(SaveFileAdapter.ResolveSlotPath(directory, AutosaveRotation.SlotForTick(fixture.Session.CurrentTick)), [1,2,3]);
            var recovered = AutosaveRotation.LoadNewestValid(directory, Compatibility);
            Assert.IsTrue(recovered.IsSuccess, recovered.Error);
            Assert.IsTrue(recovered.Session!.CurrentTick < fixture.Session.CurrentTick);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

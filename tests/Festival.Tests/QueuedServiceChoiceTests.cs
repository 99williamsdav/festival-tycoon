using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class QueuedServiceChoiceTests
{
    private static QueuedServiceChoice.Candidate Option(string id, int walk, int own = 240,
        QueuedServiceChoice.Member[]? physical = null, ulong? owner = null, int ownerRemaining = 0,
        QueuedServiceChoice.Approacher[]? approaching = null, bool canJoin = true, bool canServe = true) =>
        new(id, walk, own, canServe, canJoin, physical ?? [], owner, ownerRemaining, approaching ?? []);

    [TestMethod]
    public void ShorterLineCanBeatNearerServiceButLongTravelCanReverseIt()
    {
        var longNear = Option("near", 40, physical:
            [new(10, 400), new(11, 400), new(12, 400)], owner: 10, ownerRemaining: 300);
        var shortFar = Option("far", 300, physical: [new(20, 240)]);
        Assert.AreEqual("far", QueuedServiceChoice.Choose(90, null, [longNear, shortFar])!.Id);
        var muchFarther = shortFar with { WalkTicks = 1_200 };
        Assert.AreEqual("near", QueuedServiceChoice.Choose(90, null, [longNear, muchFarther])!.Id);
    }

    [TestMethod]
    public void ActualRemainingOwnerAndCurrentPositionControlComparison()
    {
        var current = Option("current", 5, physical: [new(10, 400), new(90, 240)],
            owner: 10, ownerRemaining: 12);
        var alternative = Option("alternative", 65);
        Assert.AreEqual(257, QueuedServiceChoice.EstimateTicks(90, current));
        Assert.IsFalse(QueuedServiceChoice.Choose(90, "current", [current, alternative])!.Switched);
        Assert.IsTrue(QueuedServiceChoice.Choose(90, "current", [current with { OwnerRemainingTicks = 400 }, alternative])!.Switched);
    }

    [TestMethod]
    public void EarlierApproachersAddExpectedDemandWithoutTakingPhysicalPlaces()
    {
        var busy = Option("busy", 180, approaching:
            [new(10, 30, 400), new(11, 100, 240), new(12, 300, 400)]);
        var clear = Option("clear", 310);
        Assert.AreEqual(1_060, QueuedServiceChoice.EstimateTicks(90, busy));
        Assert.AreEqual("clear", QueuedServiceChoice.Choose(90, null, [busy, clear])!.Id);
        // Once physically in front, approaching demand cannot jump the queue.
        var physicallyQueued = busy with { PhysicalOrder = [new(90, 240)], WalkTicks = 0 };
        Assert.AreEqual(240, QueuedServiceChoice.EstimateTicks(90, physicallyQueued));
    }

    [TestMethod]
    public void ReviewCooldownMeaningfulGainAndStableTieAvoidOscillation()
    {
        Assert.IsFalse(QueuedServiceChoice.ReviewDue(159, 0, 0));
        Assert.IsTrue(QueuedServiceChoice.ReviewDue(160, 0, 0));
        Assert.IsFalse(QueuedServiceChoice.ReviewDue(161, 0, 160));
        var current = Option("b", 200);
        Assert.IsFalse(QueuedServiceChoice.Choose(90, "b", [current, Option("a", 81)])!.Switched);
        Assert.IsTrue(QueuedServiceChoice.Choose(90, "b", [current, Option("a", 79)])!.Switched);
        Assert.IsFalse(QueuedServiceChoice.Choose(90, "a", [Option("a", 79), Option("b", 80)])!.Switched);
        Assert.AreEqual("a", QueuedServiceChoice.Choose(90, null, [Option("b", 80), Option("a", 80)])!.Id);
    }

    [TestMethod]
    public void FullAndUnreachableAlternativesAreExcludedButExistingPhysicalPlaceCounts()
    {
        var full = Option("full", 1, canJoin: false);
        var unreachable = Option("unreachable", int.MaxValue);
        var open = Option("open", 200);
        Assert.AreEqual("open", QueuedServiceChoice.Choose(90, null, [full, unreachable, open])!.Id);
        Assert.IsNull(QueuedServiceChoice.Choose(90, null, [full, unreachable]));
        var ownsPlace = full with { PhysicalOrder = [new(90, 240)] };
        Assert.AreEqual(241, QueuedServiceChoice.EstimateTicks(90, ownsPlace));
        Assert.AreEqual(int.MaxValue, QueuedServiceChoice.EstimateTicks(90, ownsPlace with { CanServe = false }));
    }

    [TestMethod]
    public void LabelledBuildFixtureReassessesBothPhysicalServicesAndKeepsOwners()
    {
        var evidence = GameSession.CreateQueuedServiceChoiceFixture();
        Assert.AreNotEqual(evidence.ToiletFrom, evidence.ToiletTo);
        Assert.AreNotEqual(evidence.WaterFrom, evidence.WaterTo);
        Assert.IsTrue(evidence.ToiletOwnerRetained && evidence.OldToiletPlaceForfeited);
        Assert.IsTrue(evidence.WaterOwnerRetained && evidence.OldWaterPlaceForfeited);
        var restored = GameSession.Restore(evidence.Session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(evidence.AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
    }
}

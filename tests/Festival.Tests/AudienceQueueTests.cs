using System.Reflection;
using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class AudienceQueueTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static Func<GridCell, bool> Growth(GameSession s) =>
        (Func<GridCell, bool>)typeof(GameSession).GetMethod("QueueGrowthAllowed", Private)!.Invoke(s, [])!;

    private static bool NearQueue(GameSession s, GridCell cell) =>
        (bool)typeof(GameSession).GetMethod("NearQueue", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [cell, typeof(GameSession).GetMethod("AllQueueGround", Private)!.Invoke(s, [])!])!;

    [TestMethod]
    public void QueuesMayGrowOntoEmptyAudienceGroundButNeverBesideAListener()
    {
        var s = Started();
        var middle = new GridCell(112, 150);
        Assert.IsTrue(GameSession.InAudienceArea(middle));
        Assert.IsTrue(Growth(s)(middle), "Nobody's watching yet, so the audience's ground is free for a queue.");

        LiveListener? placed = null;
        for (var step = 0; step < 200 && placed is null; step++)
        {
            s.AdvanceWithoutSnapshot(80);
            placed = s.CaptureLivePerformance()?.Listeners.FirstOrDefault(l => l.Place is { } p && GameSession.InAudienceArea(p));
        }
        Assert.IsNotNull(placed, "Someone takes a place to watch the first set.");
        var place = placed.Place!.Value;
        var allowed = Growth(s);
        Assert.IsFalse(allowed(place), "Not on a listener's place.");
        Assert.IsFalse(allowed(new(place.X + 2, place.Z + 2)), "Nor within two cells of it.");
    }

    [TestMethod]
    public void ListenersKeepClearOfEveryQueueNotJustWater()
    {
        var s = Started();
        var bar = s.CaptureVendors().Single(v => v.Id == "drinks");
        var inCrowd = new GridCell(112, 150);
        Assert.IsFalse(NearQueue(s, inCrowd), "Nothing queues in the audience area at the start.");
        // A short stretch of bar queue standing in the audience area (only its cells matter here).
        SetVendor(s, bar with { QueueCells = [inCrowd, new(inCrowd.X, inCrowd.Z + 2)] });
        Assert.IsTrue(NearQueue(s, inCrowd), "A bar queue's cell is off limits to listeners.");
        Assert.IsTrue(NearQueue(s, new(inCrowd.X + 2, inCrowd.Z - 2)), "And so is the ground two cells round it.");
        Assert.IsFalse(NearQueue(s, new(inCrowd.X + 3, inCrowd.Z)), "Three cells away is fine.");
    }
}

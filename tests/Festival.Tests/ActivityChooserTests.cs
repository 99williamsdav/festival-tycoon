using Festival.Simulation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Festival.Tests;

[TestClass]
public sealed class ActivityChooserTests
{
    private static readonly NeedGrowth Growth = new(20, 20, 12, 20);
    private static long[] Music(long perSecond) => Enumerable.Repeat(perSecond, ActivityChooser.Samples).ToArray();
    private static long[] MusicFrom(int second, long before, long after) =>
        Enumerable.Range(0, ActivityChooser.Samples).Select(sample => sample < second ? before : after).ToArray();
    private static ActivityOption Watch => new(ActivityKind.Watch, null, 0, 0);
    private static ActivityOption Toilet(int complete = 1_600, int away = 2_000) => new(ActivityKind.Toilet, "toilet.main", complete, away);
    private static ActivityKind Best(NeedLevels now, long[] music, params ActivityOption[] options) =>
        ActivityChooser.Rank(now, Growth, music, options)[0].Option.Kind;

    [TestMethod]
    public void MildNeedsKeepWatchingAndUrgentNeedsLeave()
    {
        var music = Music(2_000);
        Assert.AreEqual(ActivityKind.Watch, Best(new(2_000, 3_000, 2_000, 4_000), music, Watch, Toilet()));
        Assert.AreEqual(ActivityKind.Toilet, Best(new(2_000, 3_000, 2_000, 8_500), music, Watch, Toilet()));
    }

    [TestMethod]
    public void BetterMusicHoldsPeopleLonger()
    {
        var needs = new NeedLevels(2_000, 3_000, 2_000, 6_500);
        Assert.AreEqual(ActivityKind.Toilet, Best(needs, Music(1_000), Watch, Toilet()));
        Assert.AreEqual(ActivityKind.Watch, Best(needs, Music(8_000), Watch, Toilet()));
    }

    [TestMethod]
    public void GoesBeforeAFavouriteSetRatherThanDuringIt()
    {
        // The same need goes now when the set starts in 40 seconds, but not once it has begun.
        var needs = new NeedLevels(2_000, 3_000, 2_000, 6_000);
        Assert.AreEqual(ActivityKind.Toilet, Best(needs, MusicFrom(40, 500, 8_000), Watch, Toilet()));
        Assert.AreEqual(ActivityKind.Watch, Best(needs, Music(8_000), Watch, Toilet()));
    }

    [TestMethod]
    public void HeldQueuePlaceOutscoresStartingOver()
    {
        var needs = new NeedLevels(8_000, 6_000, 2_000, 3_000);
        var ranked = ActivityChooser.Rank(needs, Growth, Music(2_000),
            [new(ActivityKind.Water, "water.far", 2_400, 3_000), new(ActivityKind.Water, "water.main", 800, 1_400)]);
        Assert.AreEqual("water.main", ranked[0].Option.FacilityId);
    }

    [TestMethod]
    public void ReliefThatLandsLateStillCounts()
    {
        // Parched, behind a long queue: water still beats staying put.
        var needs = new NeedLevels(9_500, 8_500, 2_000, 3_000);
        Assert.AreEqual(ActivityKind.Water, Best(needs, Music(2_000), Watch, new(ActivityKind.Water, "water.main", 7_000, 7_600)));
    }

    [TestMethod]
    public void WaterRelievesThirstAndSomeHeat()
    {
        var after = ActivityChooser.Relieve(ActivityKind.Water, new(8_000, 7_000, 0, 0));
        Assert.AreEqual(0, after.Thirst);
        Assert.AreEqual(5_000, after.Heat);
        Assert.AreEqual(ActivityChooser.RestHeatTarget, ActivityChooser.Relieve(ActivityKind.Rest, new(0, 9_000, 0, 0)).Heat);
    }

    [TestMethod]
    public void TiesKeepTheCallersOrder()
    {
        var first = new ActivityOption(ActivityKind.Toilet, "a", 800, 800);
        var second = new ActivityOption(ActivityKind.Toilet, "b", 800, 800);
        Assert.AreEqual("a", ActivityChooser.Rank(new(0, 0, 0, 6_000), Growth, Music(0), [first, second])[0].Option.FacilityId);
    }
}

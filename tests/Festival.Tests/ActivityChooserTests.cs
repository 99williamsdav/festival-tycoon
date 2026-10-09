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

    private static ActivityOption Plan(ActivityKind first, int firstDone, ActivityKind second, int secondDone, int away, long enjoyment = 0) =>
        new(first, first.ToString(), firstDone, away, enjoyment) { Then = new(second, second.ToString(), secondDone) };

    [TestMethod]
    public void AToiletOnTheWayToTheBarIsWorthTheStop()
    {
        // Beer is wanted; the toilet adds four seconds on the way. Going via the toilet wins.
        var needs = new NeedLevels(4_000, 4_000, 2_000, 6_500);
        var beerOnly = new ActivityOption(ActivityKind.Beer, "drinks", 1_200, 1_800, 60_000);
        var viaToilet = Plan(ActivityKind.Toilet, 900, ActivityKind.Beer, 1_500, 2_100, 60_000);
        Assert.AreSame(viaToilet, ActivityChooser.Rank(needs, Growth, Music(2_000), [beerOnly, viaToilet])[0].Option);
    }

    [TestMethod]
    public void TheToiletGoesFirstWhenItWillBeMoreUrgentByTheBar()
    {
        var needs = new NeedLevels(4_000, 4_000, 2_000, 7_500);
        var beerFirst = Plan(ActivityKind.Beer, 1_200, ActivityKind.Toilet, 2_400, 3_000, 60_000);
        var toiletFirst = Plan(ActivityKind.Toilet, 1_200, ActivityKind.Beer, 2_400, 3_000, 60_000);
        Assert.AreEqual(ActivityKind.Toilet, ActivityChooser.Rank(needs, Growth, Music(2_000), [beerFirst, toiletFirst])[0].Option.Kind);
    }

    [TestMethod]
    public void AFartherTapWinsWhenItIsOnTheWayToTheNextStop()
    {
        // Tap A is nearer, but chips are twenty seconds' walk beyond it; tap B sits beside the stall.
        var needs = new NeedLevels(7_500, 6_000, 7_000, 3_000);
        var viaA = new ActivityOption(ActivityKind.Water, "water.a", 800, 4_400) { Then = new(ActivityKind.Food, "food", 3_200) };
        var viaB = new ActivityOption(ActivityKind.Water, "water.b", 1_200, 2_400) { Then = new(ActivityKind.Food, "food", 1_800) };
        Assert.AreEqual("water.b", ActivityChooser.Rank(needs, Growth, Music(2_000), [viaA, viaB])[0].Option.FacilityId);
    }

    [TestMethod]
    public void TiesKeepTheCallersOrder()
    {
        var first = new ActivityOption(ActivityKind.Toilet, "a", 800, 800);
        var second = new ActivityOption(ActivityKind.Toilet, "b", 800, 800);
        Assert.AreEqual("a", ActivityChooser.Rank(new(0, 0, 0, 6_000), Growth, Music(0), [first, second])[0].Option.FacilityId);
    }

    [TestMethod]
    public void ABandMemberAboutToPlayALongSetDrinksFirstRatherThanCollapseOnStage()
    {
        // Parched but not yet hot: inside the two-minute look-ahead heat stays under distress, so a long trip to the tap
        // looks worse than staying for the music. The set they're committed to runs on past the look-ahead, though,
        // and with no water they'd be in distress for most of it. Counting that, the tap wins.
        var now = new NeedLevels(9_500, 5_000, 0, 0);
        var growth = new NeedGrowth(20, 20, 0, 0);
        var water = new ActivityOption(ActivityKind.Water, "water.main", 3_000, 6_000);
        var stay = new ActivityOption(ActivityKind.Watch, null, 0, 0);
        Assert.AreEqual(ActivityKind.Watch, ActivityChooser.Rank(now, growth, Music(4_000), [stay, water])[0].Option.Kind,
            "Without the set in view, staying looks better: the flaw this guards.");
        Assert.AreEqual(ActivityKind.Water, ActivityChooser.Rank(now, growth, Music(4_000), [stay, water], committedTicks: 8_400)[0].Option.Kind);
        // Committed time only counts distress: an ordinary need past the look-ahead doesn't tip a choice.
        var cool = new NeedLevels(5_000, 0, 0, 0);
        Assert.AreEqual(ActivityChooser.Score(cool, growth, Music(4_000), stay), ActivityChooser.Score(cool, growth, Music(4_000), stay, 8_400));
    }
}

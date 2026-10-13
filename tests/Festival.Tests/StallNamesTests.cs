using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>
/// Bars and vans named from where they stand (the user: "give bars useful names"), the power leads that follow each
/// stall, "Copy last festival's layout" from Tier 2, and the Pond Stage's crowd matching the trailer's.
/// </summary>
[TestClass]
public sealed class StallNamesTests
{
    private static GameSession TierTwo(ulong seed = 20260922) => GameSession.CreateDevelopmentFestival(seed, 2);

    private static void SetPreparation(GameSession s, Func<PreparationSnapshot, PreparationSnapshot> change)
    {
        var view = typeof(GameSession).GetProperty("PreparationView", BindingFlags.Instance | BindingFlags.NonPublic)!;
        view.SetValue(s, change((PreparationSnapshot)view.GetValue(s)!));
    }

    // ---- Names ----

    [TestMethod]
    public void TheDefaultLayoutsNameTheirBarsByCompassAndStageAndTheirVansByTrader()
    {
        var one = BuildSession.Drafted();
        Assert.AreEqual("East Bar", one.StallName(Stalls.FirstBar), "Tier 1's bar stands east of the track, well clear of the trailer's crowd.");
        Assert.AreEqual(one.TraderAt(Stalls.FirstVan).Name, one.StallName(Stalls.FirstVan));
        Assert.AreEqual("Chip Off The Old Block", one.StallName(Stalls.FirstVan));

        var two = NextFestivalTests.Drafted(TierTwo());
        Assert.AreEqual("East Bar", two.StallName(Stalls.FirstBar));
        Assert.AreEqual("Pond Stage Bar", two.StallName(Stalls.Id(BuildServiceKind.Bar, 2)), "The second bar serves the Pond Stage's crowd.");
        Assert.AreEqual("Chip Off The Old Block", two.StallName(Stalls.FirstVan));
        Assert.AreEqual("Korma Chameleon", two.StallName(Stalls.Id(BuildServiceKind.FoodVan, 2)));
        // Every bar and van has a name, and no two share one.
        var names = two.StallNamesNow();
        Assert.AreEqual(4, names.Count);
        Assert.AreEqual(names.Count, names.Values.Distinct(StringComparer.Ordinal).Count());
        // A stall that isn't on the field keeps the old label.
        Assert.AreEqual("Bar 2", one.StallName(Stalls.Id(BuildServiceKind.Bar, 2)));
    }

    [TestMethod]
    public void MovingABarRenamesItAndOnlyIt()
    {
        var two = NextFestivalTests.Drafted(TierTwo());
        var second = Stalls.Id(BuildServiceKind.Bar, 2);
        // Out of the Pond Stage's reach, north of the field's middle: named by the compass, and the first bar keeps its name.
        BuildSession.Accept(two, new MoveBuildServiceCommand(second, new(150, 114), 1));
        Assert.AreEqual("North Bar", two.StallName(second));
        Assert.AreEqual("East Bar", two.StallName(Stalls.FirstBar));
        // The site check and the named spot agree: it's 22 cells from the pond's crowd.
        Assert.IsNull(StallNames.StageNear(new(150, 114), two.Stages));
    }

    [TestMethod]
    public void BarsSharingAStageTakeABearingFromItsCrowd()
    {
        var pond = StallNames.For([("drinks", new(147, 146)), ("drinks.2", new(172, 128))], FestivalStages.All);
        Assert.AreEqual("West Pond Stage Bar", pond["drinks"]);
        Assert.AreEqual("North Pond Stage Bar", pond["drinks.2"]);
        var trailer = StallNames.For([("drinks", new(112, 128)), ("drinks.2", new(112, 172))], FestivalStages.All);
        Assert.AreEqual("North Trailer Stage Bar", trailer["drinks"]);
        Assert.AreEqual("South Trailer Stage Bar", trailer["drinks.2"]);
        // One by each stage takes each stage's name; the Pond Stage counts only when it's open.
        var each = StallNames.For([("drinks", new(112, 128)), ("drinks.2", new(147, 146))], FestivalStages.All);
        CollectionAssert.AreEqual(new[] { "Trailer Stage Bar", "Pond Stage Bar" }, new[] { each["drinks"], each["drinks.2"] });
        Assert.AreEqual("East Bar", StallNames.For([("drinks.2", new(147, 146))], FestivalStages.MainOnly)["drinks.2"]);
    }

    [TestMethod]
    public void NamesNeverRepeatAndDontDependOnOrder()
    {
        // Both north of the middle on four points; eight points tell them apart.
        var eight = StallNames.For([("drinks", new(150, 112)), ("drinks.2", new(135, 110))], FestivalStages.MainOnly);
        Assert.AreEqual("North-East Bar", eight["drinks"]);
        Assert.AreEqual("North Bar", eight["drinks.2"]);
        // Still the same on eight points: a number, in stall order.
        var numbered = StallNames.For([("drinks.2", new(150, 120)), ("drinks", new(150, 115))], FestivalStages.MainOnly);
        Assert.AreEqual("North-East Bar", numbered["drinks"]);
        Assert.AreEqual("North-East Bar 2", numbered["drinks.2"]);
        // Vans with no trader to go by are named as bars are, and never clash with a bar.
        var vans = StallNames.For([("drinks", new(140, 148)), ("food", new(140, 150)), ("food.2", new(150, 130))], FestivalStages.MainOnly, _ => null);
        CollectionAssert.AreEqual(new[] { "East Bar", "East Van", "North-East Van" }, new[] { vans["drinks"], vans["food"], vans["food.2"] });
        // Two vans claiming one trader can't both have it.
        var sameTrader = StallNames.For([("food", new(140, 162)), ("food.2", new(160, 120))], FestivalStages.MainOnly, _ => "Chip Off The Old Block");
        Assert.AreEqual(2, sameTrader.Values.Distinct().Count());
        // The same layout given in any order names the same.
        var stalls = new (string, GridCell)[] { ("drinks", new(147, 146)), ("drinks.2", new(172, 128)), ("food", new(140, 162)), ("food.2", new(170, 124)) };
        var forwards = StallNames.For(stalls, FestivalStages.All, id => id);
        var backwards = StallNames.For(stalls.Reverse(), FestivalStages.All, id => id);
        CollectionAssert.AreEqual(forwards.ToArray(), backwards.ToArray());
    }

    [TestMethod]
    public void BearingsPointNorthUpTheSiteMap()
    {
        var middle = StallNames.FieldCentre;
        Assert.AreEqual("North", StallNames.Bearing(new(middle.X, middle.Z - 5), middle, eight: false), "North is -Z, up the site map.");
        Assert.AreEqual("East", StallNames.Bearing(new(middle.X + 5, middle.Z), middle, eight: false));
        Assert.AreEqual("South-West", StallNames.Bearing(new(middle.X - 5, middle.Z + 5), middle, eight: false), "An exact diagonal.");
        Assert.AreEqual("South", StallNames.Bearing(new(middle.X - 2, middle.Z + 5), middle, eight: true));
        Assert.AreEqual("South-West", StallNames.Bearing(new(middle.X - 3, middle.Z + 5), middle, eight: true));
    }

    // ---- Power leads ----

    [TestMethod]
    public void EveryStallsCableRunsBehindItAndMovesWithIt()
    {
        var two = NextFestivalTests.Drafted(TierTwo());
        var spots = two.CableSpots();
        foreach (var vendor in two.CaptureVendors())
        {
            var spot = spots.Single(item => item.Utility == vendor.Id).Spot;
            Assert.IsTrue(Math.Max(Math.Abs(spot.X - vendor.Cell.X), Math.Abs(spot.Z - vendor.Cell.Z)) <= 7, $"{vendor.Id}'s cable is at {spot}, away from {vendor.Cell}.");
        }
        Assert.AreEqual(5, spots.Count, "The farm generator and four stalls.");
        var before = spots.Single(item => item.Utility == Stalls.FirstVan).Spot;
        BuildSession.Accept(two, new MoveBuildServiceCommand(Stalls.FirstVan, new(140, 175), 3));
        var after = two.CableSpots().Single(item => item.Utility == Stalls.FirstVan).Spot;
        Assert.AreEqual(before.X, after.X);
        Assert.AreEqual(before.Z + 13, after.Z);
    }

    // ---- Copy last festival's layout ----

    [TestMethod]
    public void LastFestivalsLayoutIsCopiedWhereItStillFitsAndSaysHowManyDidnt()
    {
        // Tier 2's default layout placed first; copying replaces it.
        var s = NextFestivalTests.Drafted(TierTwo());
        Assert.IsNull(s.LastFestivalLayoutFit(), "A development Tier 2 has no last festival.");
        Assert.IsFalse(BuildSession.Send(s, new UseLastFestivalLayoutCommand()).IsAccepted);
        // Tier 1's default with its second loo on what's now the Pond Stage's crowd, and a bin on its riser's ground.
        var previous = GameSession.StandardBuildLayout(1)
            .Select(item => item.Id == "toilet.extra-1" ? item with { Cell = new(168, 140) } : item)
            .Append(new BuildPlacement("bin.2", BuildServiceKind.Bin, new(155, 158), 0))
            .Append(new BuildPlacement("bin.3", BuildServiceKind.Bin, new(160, 120), 0))
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        SetPreparation(s, p => p with { PreviousLayout = previous });
        var fit = s.LastFestivalLayoutFit()!.Value;
        Assert.AreEqual(2, fit.Skipped);
        CollectionAssert.AreEqual(previous.Where(item => item.Id is not ("toilet.extra-1" or "bin.2")).ToArray(), fit.Placed);
        BuildSession.Accept(s, new UseLastFestivalLayoutCommand());
        CollectionAssert.AreEqual(fit.Placed, s.CaptureBuildPlacements().ToArray());
        // Tier 2's extra bar, van, tap and toilet are left for the player.
        Assert.AreEqual(1, s.CaptureBuildPlacements().Count(item => item.Kind == BuildServiceKind.Bar));
        Assert.AreEqual(1, s.CaptureToilets().Count);
        // The copied layout stands, saves and restores, and the carried layout comes back with it.
        var restored = BuildSession.Restored(s);
        CollectionAssert.AreEqual(previous, restored.CapturePreparation()!.PreviousLayout);
    }

    [TestMethod]
    public void ACarriedLayoutTheFestivalBeforeCouldntHavePlacedIsRejectedOnRestore()
    {
        var s = TierTwo();
        var previous = GameSession.StandardBuildLayout(1).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        SetPreparation(s, p => p with { PreviousLayout = previous });
        var saved = s.CapturePersistenceSnapshot();
        Assert.IsTrue(GameSession.Restore(saved).IsSuccess);
        SessionPersistenceSnapshot With(BuildPlacement[]? layout) => saved with { Preparation = saved.Preparation! with { PreviousLayout = layout } };
        // Tier 1 had one bar, so a second can't have come from it.
        var twoBars = previous.Append(new BuildPlacement("drinks.2", BuildServiceKind.Bar, new(160, 120), 0)).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        Assert.IsFalse(GameSession.Restore(With(twoBars)).IsSuccess);
        Assert.IsFalse(GameSession.Restore(With(previous.Reverse().ToArray())).IsSuccess, "Unsorted.");
        Assert.IsFalse(GameSession.Restore(With([])).IsSuccess, "Empty is saved as none.");
        var overlapping = previous.Select(item => item.Id == "bin.1" ? item with { Cell = previous.Single(other => other.Id == Stalls.FirstBar).Cell } : item).ToArray();
        Assert.IsFalse(GameSession.Restore(With(overlapping)).IsSuccess);
        // Tier 1 never has a last festival.
        var one = BuildSession.Drafted();
        var first = one.CapturePersistenceSnapshot();
        Assert.IsFalse(GameSession.Restore(first with { Preparation = first.Preparation! with { PreviousLayout = previous } }).IsSuccess);
    }

    // ---- The Pond Stage's crowd ----

    [TestMethod]
    public void ThePondStagesCrowdIsAsBigAsTheTrailers()
    {
        var pond = FestivalStages.Pond;
        var trailer = FestivalStages.Main;
        Assert.AreEqual(trailer.ListeningPlaces.Count, pond.ListeningPlaces.Count);
        Assert.AreEqual(135, pond.ListeningPlaces.Count);
        Assert.AreEqual(trailer.ListeningPlaces.Distinct().Count(), pond.ListeningPlaces.Distinct().Count());
        static int Area((int MinX, int MaxX, int MinZ, int MaxZ) b) => (b.MaxX - b.MinX + 1) * (b.MaxZ - b.MinZ + 1);
        Assert.AreEqual(Area(trailer.AudienceBounds), Area(pond.AudienceBounds));
        Assert.AreEqual((153, 183, 136, 154), pond.AudienceBounds);
        // Nothing in Tier 2's default stands in it, bins aside, and the pond's engineer mixes behind it.
        foreach (var item in GameSession.StandardBuildLayout(2).Where(item => item.Kind != BuildServiceKind.Bin))
            Assert.IsFalse(GameSession.BuildFootprint(item).Any(pond.InAudienceArea), item.Id);
        Assert.IsFalse(GameSession.StandardBuildLayout(2).Any(item => GameSession.BuildFootprint(item).Any(pond.ListeningPlaces.Contains)));
        Assert.IsTrue(pond.MixingPlace is { } mixing && !pond.InAudienceArea(mixing) && mixing.Z < pond.AudienceBounds.MinZ);
    }
}

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

    private static SortedDictionary<string, string> Bars(IReadOnlyList<FestivalStage> stages, params GridCell[] cells) =>
        StallNames.For(cells.Select((cell, index) => (Stalls.Id(BuildServiceKind.Bar, index + 1), cell)), stages);

    private static string[] Named(SortedDictionary<string, string> names, int count) =>
        Enumerable.Range(1, count).Select(number => names[Stalls.Id(BuildServiceKind.Bar, number)]).ToArray();

    [TestMethod]
    public void TheDefaultLayoutsNameTheirBarsInRelationToEachOtherAndTheirVansByTrader()
    {
        var one = BuildSession.Drafted();
        Assert.AreEqual("Bar", one.StallName(Stalls.FirstBar), "Tier 1's only bar stands well clear of the trailer's crowd.");
        Assert.AreEqual(one.TraderAt(Stalls.FirstVan).Name, one.StallName(Stalls.FirstVan));
        Assert.AreEqual("Chip Off The Old Block", one.StallName(Stalls.FirstVan));

        var two = NextFestivalTests.Drafted(TierTwo());
        // Tier 2's second stage is the Barn, so neither bar is by a stage: each takes its bearing from the other.
        Assert.AreEqual("West Bar", two.StallName(Stalls.FirstBar));
        Assert.AreEqual("East Bar", two.StallName(Stalls.Id(BuildServiceKind.Bar, 2)));
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
    public void ABarBesideThePondStageBarIsNamedFromIt()
    {
        // The user's playthrough: a Pond Stage Bar and one other bar to its west, which used to be "East Bar" (east of
        // the field's middle). Named in relation to each other, it's the west one.
        var pond = Bars(FestivalStages.All, new(140, 148), new(147, 146));
        Assert.IsNull(StallNames.StageNear(new(140, 148), FestivalStages.All));
        CollectionAssert.AreEqual(new[] { "West Bar", "Pond Stage Bar" }, Named(pond, 2));
        // Numbered the other way round, the same.
        var swapped = Bars(FestivalStages.All, new(147, 146), new(140, 148));
        CollectionAssert.AreEqual(new[] { "Pond Stage Bar", "West Bar" }, Named(swapped, 2));
    }

    [TestMethod]
    public void ALoneBarIsJustBarUnlessItsByAStage()
    {
        foreach (var cell in new GridCell[] { new(140, 148), new(150, 114), new(175, 185), new(100, 185) })
        {
            Assert.IsNull(StallNames.StageNear(cell, FestivalStages.All), $"{cell}");
            Assert.AreEqual("Bar", Bars(FestivalStages.All, cell)["drinks"]);
            Assert.AreEqual("Bar", StallNames.For([("drinks.3", cell)], FestivalStages.All)["drinks.3"], "Whatever its number.");
        }
        Assert.AreEqual("Pond Stage Bar", Bars(FestivalStages.All, new GridCell(147, 146))["drinks"]);
        Assert.AreEqual("Trailer Stage Bar", Bars(FestivalStages.All, new GridCell(112, 128))["drinks"]);
        Assert.AreEqual("Bar", Bars(FestivalStages.MainOnly, new GridCell(147, 146))["drinks"], "The Pond Stage counts only when it's open.");
    }

    [TestMethod]
    public void TwoBarsByNoStageAreNamedEastAndWestOfEachOther()
    {
        CollectionAssert.AreEqual(new[] { "West Bar", "East Bar" }, Named(Bars(FestivalStages.MainOnly, new(140, 180), new(160, 182)), 2));
        CollectionAssert.AreEqual(new[] { "East Bar", "West Bar" }, Named(Bars(FestivalStages.MainOnly, new(160, 182), new(140, 180)), 2));
        CollectionAssert.AreEqual(new[] { "North Bar", "South Bar" }, Named(Bars(FestivalStages.MainOnly, new(150, 114), new(140, 148)), 2));
        // Placing a second bar renames the first: the trade for names that relate.
        Assert.AreEqual("Bar", Bars(FestivalStages.MainOnly, new GridCell(160, 182))["drinks"]);
    }

    [TestMethod]
    public void BearingsThatClashGoToEightPointsThenNumbers()
    {
        // Three bars: two north of their middle on four points go to eight; the south one, alone, stays on four.
        CollectionAssert.AreEqual(new[] { "North-West Bar", "North Bar", "South Bar" },
            Named(Bars(FestivalStages.MainOnly, new(140, 160), new(150, 158), new(145, 190)), 3));
        // Still alike on eight points: numbered west to east.
        CollectionAssert.AreEqual(new[] { "North Bar 1", "North Bar 2", "South Bar" },
            Named(Bars(FestivalStages.MainOnly, new(140, 160), new(142, 150), new(141, 190)), 3));
        CollectionAssert.AreEqual(new[] { "North Bar 2", "North Bar 1", "South Bar" },
            Named(Bars(FestivalStages.MainOnly, new(142, 150), new(140, 160), new(141, 190)), 3));
        // One exactly at the middle of them all.
        CollectionAssert.AreEqual(new[] { "West Bar", "Central Bar", "East Bar" },
            Named(Bars(FestivalStages.MainOnly, new(140, 180), new(150, 180), new(160, 180)), 3));
    }

    [TestMethod]
    public void SeveralBarsByOneStageTakeBearingsFromEachOther()
    {
        CollectionAssert.AreEqual(new[] { "West Pond Stage Bar", "East Pond Stage Bar" }, Named(Bars(FestivalStages.All, new(147, 146), new(172, 128)), 2));
        CollectionAssert.AreEqual(new[] { "North Trailer Stage Bar", "South Trailer Stage Bar" }, Named(Bars(FestivalStages.All, new(112, 128), new(112, 172)), 2));
        // One by each stage takes each stage's name.
        CollectionAssert.AreEqual(new[] { "Trailer Stage Bar", "Pond Stage Bar" }, Named(Bars(FestivalStages.All, new(112, 128), new(147, 146)), 2));
        // Two by the pond and one by no stage: the pond pair go by each other, the other by all three.
        CollectionAssert.AreEqual(new[] { "West Pond Stage Bar", "East Pond Stage Bar", "South Bar" },
            Named(Bars(FestivalStages.All, new(147, 146), new(172, 128), new(130, 180)), 3));
    }

    [TestMethod]
    public void NamesNeverRepeatAndDontDependOnOrderOrNumbering()
    {
        // Vans with no trader to go by are named as bars are, among themselves, and never clash with a bar.
        var vans = StallNames.For([("drinks", new(140, 148)), ("food", new(140, 150)), ("food.2", new(150, 130))], FestivalStages.MainOnly, _ => null);
        CollectionAssert.AreEqual(new[] { "Bar", "South Van", "North Van" }, new[] { vans["drinks"], vans["food"], vans["food.2"] });
        Assert.AreEqual("Van", StallNames.For([("food.2", new(140, 150))], FestivalStages.MainOnly, _ => null)["food.2"]);
        // Two vans claiming one trader can't both have it: the western keeps it.
        var sameTrader = StallNames.For([("food.2", new(160, 120)), ("food", new(140, 162))], FestivalStages.MainOnly, _ => "Chip Off The Old Block");
        Assert.AreEqual("Chip Off The Old Block", sameTrader["food"]);
        Assert.AreEqual("Chip Off The Old Block 2", sameTrader["food.2"]);

        var spots = (from x in Enumerable.Range(0, 9) from z in Enumerable.Range(0, 7) select new GridCell(70 + x * 15, 110 + z * 13)).ToArray();
        var some = spots.Where((_, index) => index % 3 == 0).ToArray();
        foreach (var stages in new[] { FestivalStages.MainOnly, FestivalStages.All })
            foreach (var a in some)
                foreach (var b in some)
                    foreach (var c in some)
                    {
                        if (a == b || b == c || a == c) continue;
                        var cells = new[] { a, b, c };
                        var names = Bars(stages, cells);
                        Assert.AreEqual(3, names.Values.Distinct(StringComparer.Ordinal).Count(), $"{a}, {b}, {c}: {string.Join(", ", names.Values)}");
                        // Any order and any numbering: each cell keeps its name.
                        var shuffled = StallNames.For(new[] { ("drinks.7", c), ("drinks.2", a), ("drinks", b) }, stages);
                        CollectionAssert.AreEqual(Named(names, 3), new[] { shuffled["drinks.2"], shuffled["drinks"], shuffled["drinks.7"] }, $"{a}, {b}, {c}");
                        // With trader-less vans on the same spots and a trader van too: still no two alike.
                        var withVans = StallNames.For(cells.Select((cell, index) => (Stalls.Id(BuildServiceKind.Bar, index + 1), cell))
                            .Concat(cells.Select((cell, index) => (Stalls.Id(BuildServiceKind.FoodVan, index + 1), cell))), stages,
                            id => id == "food.3" ? "Bar" : null);
                        Assert.AreEqual(6, withVans.Values.Distinct(StringComparer.Ordinal).Count(), string.Join(", ", withVans.Values));
                    }
        // Pairs everywhere on the field: two bars never share a name.
        foreach (var stages in new[] { FestivalStages.MainOnly, FestivalStages.All })
            foreach (var a in spots)
                foreach (var b in spots.Where(b => b != a))
                    Assert.AreEqual(2, Bars(stages, a, b).Values.Distinct(StringComparer.Ordinal).Count(), $"{a}, {b}");
    }

    [TestMethod]
    public void MovingABarRenamesItAndTheOthers()
    {
        var two = NextFestivalTests.Drafted(TierTwo());
        var second = Stalls.Id(BuildServiceKind.Bar, 2);
        // Out of the Pond Stage's reach, north of the first bar: the two are north and south of each other.
        BuildSession.Accept(two, new MoveBuildServiceCommand(second, new(150, 114), 1));
        Assert.AreEqual("North Bar", two.StallName(second));
        Assert.AreEqual("South Bar", two.StallName(Stalls.FirstBar));
        // The site check and the named spot agree: it's 22 cells from the pond's crowd.
        Assert.IsNull(StallNames.StageNear(new(150, 114), two.Stages));
        // Removing it leaves a lone "Bar".
        BuildSession.Accept(two, new RemoveBuildServiceCommand(second));
        Assert.AreEqual("Bar", two.StallName(Stalls.FirstBar));
    }

    [TestMethod]
    public void TheNamesFollowTheStagesThatAreOpen()
    {
        // The cache is keyed on the stages too: a bar by the pond is "Pond Stage Bar" only while that stage is open.
        var s = NextFestivalTests.Drafted(TierTwo());
        var second = Stalls.Id(BuildServiceKind.Bar, 2);
        var stages = typeof(GameSession).GetField("_diagnosticStages", BindingFlags.Instance | BindingFlags.NonPublic)!;
        stages.SetValue(s, FestivalStages.For(pondOpen: true));
        Assert.AreEqual("Pond Stage Bar", s.StallName(second));
        Assert.AreEqual("West Bar", s.StallName(Stalls.FirstBar), "The user's case.");
        stages.SetValue(s, FestivalStages.MainOnly);
        Assert.AreEqual("East Bar", s.StallName(second), "Without the pond it's east of the first bar.");
        Assert.AreEqual("West Bar", s.StallName(Stalls.FirstBar));
    }

    [TestMethod]
    public void BearingsPointNorthUpTheSiteMap()
    {
        var middle = new GridCell(129, 149);
        Assert.AreEqual("North", StallNames.Bearing(new(middle.X, middle.Z - 5), middle, eight: false), "North is -Z, up the site map.");
        Assert.AreEqual("East", StallNames.Bearing(new(middle.X + 5, middle.Z), middle, eight: false));
        Assert.AreEqual("South-West", StallNames.Bearing(new(middle.X - 5, middle.Z + 5), middle, eight: false), "An exact diagonal.");
        Assert.AreEqual("South", StallNames.Bearing(new(middle.X - 2, middle.Z + 5), middle, eight: true));
        Assert.AreEqual("South-West", StallNames.Bearing(new(middle.X - 3, middle.Z + 5), middle, eight: true));
        Assert.AreEqual("Central", StallNames.Bearing(middle, middle, eight: true));
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
        // Tier 1's default with its second loo and a bin on the generator's clear ground, which this festival refuses.
        // Not a layout real Tier 1 play makes: the carried layout is checked without the generator, the copy with it,
        // which is the "still fits, says how many didn't" path. Keep it a mechanism test.
        var previous = GameSession.StandardBuildLayout(1)
            .Select(item => item.Id == "toilet.extra-1" ? item with { Cell = new(82, 153) } : item)
            .Append(new BuildPlacement("bin.2", BuildServiceKind.Bin, new(82, 161), 0))
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

    /// <summary>The copy's rule, spelled out: one at a time, firsts of each kind before seconds, each placed if its limit has room and it fits.</summary>
    private static (BuildPlacement[] Placed, int Skipped) OneAtATime(GameSession s, IEnumerable<BuildPlacement> previous)
    {
        var validate = typeof(GameSession).GetMethod("ValidateBuildLayout", BindingFlags.Static | BindingFlags.NonPublic)!;
        var ordinal = typeof(GameSession).GetMethod("OrdinalOfKind", BindingFlags.Static | BindingFlags.NonPublic)!;
        var p = s.CapturePreparation()!;
        var placed = new List<BuildPlacement>();
        var skipped = 0;
        foreach (var item in previous.OrderBy(item => (int)ordinal.Invoke(null, [item])!).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            var next = placed.Append(item).OrderBy(other => other.Id, StringComparer.Ordinal).ToArray();
            if (placed.Count(other => other.Kind == item.Kind) >= s.ServiceLimit(item.Kind) ||
                validate.Invoke(null, [next, s.Stages, s.CaptureEquipment(), p.WaterTowerOwned, p.Tier, false, s.BarnGeometryVersion]) is not null) skipped++;
            else placed = [.. next];
        }
        return (placed.ToArray(), skipped);
    }

    [TestMethod]
    public void TheCopyTriesMainsBeforeExtrasAndMatchesPlacingOneAtATime()
    {
        // Tier 2's layout carried into a Tier 1 field (one tap, two loos, one bar and van): the mains are kept and the
        // extras left off, though "water.extra-1" sorts before "water.main".
        var one = BuildSession.Drafted();
        var tierTwo = GameSession.StandardBuildLayout(2).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        SetPreparation(one, p => p with { PreviousLayout = tierTwo });
        var fit = one.LastFestivalLayoutFit()!.Value;
        var kept = fit.Placed.Select(item => item.Id).ToArray();
        CollectionAssert.IsSubsetOf(new[] { "water.main", "toilet.main", "toilet.extra-1", Stalls.FirstBar, Stalls.FirstVan }, kept);
        CollectionAssert.IsSubsetOf(new[] { "water.extra-1", "toilet.extra-2", Stalls.Id(BuildServiceKind.Bar, 2), Stalls.Id(BuildServiceKind.FoodVan, 2) },
            tierTwo.Select(item => item.Id).Except(kept).ToArray());
        Assert.AreEqual(OneAtATime(one, tierTwo), (fit.Placed, fit.Skipped), new FitComparer());
        // The main tap no longer fitting: the extra is placed in its stead, at its own spot.
        var two = NextFestivalTests.Drafted(TierTwo());
        var trailerDeck = new GridCell(80, 140);
        var layouts = new[]
        {
            tierTwo,
            tierTwo.Select(item => item.Id == "water.main" ? item with { Cell = trailerDeck } : item).ToArray(),
            // Clashes early, late and several at once: the toilet in the pond's crowd, the second bar on the first,
            // a bin on the trailer.
            tierTwo.Select(item => item.Id == "toilet.extra-1" ? item with { Cell = new(168, 140) } : item).ToArray(),
            tierTwo.Select(item => item.Id == Stalls.Id(BuildServiceKind.Bar, 2) ? item with { Cell = new(140, 148) } : item).ToArray(),
            tierTwo.Select(item => item.Id switch
            {
                "toilet.main" => item with { Cell = new(168, 140) },
                "bin.2" => item with { Cell = trailerDeck },
                "first-aid" => item with { Cell = new(150, 150) },
                _ => item
            }).ToArray(),
            tierTwo.Select(item => item with { Cell = trailerDeck }).ToArray(),
        };
        foreach (var (layout, index) in layouts.Select((layout, index) => (layout, index)))
        {
            SetPreparation(two, p => p with { PreviousLayout = layout });
            var copied = two.LastFestivalLayoutFit()!.Value;
            Assert.AreEqual(OneAtATime(two, layout), (copied.Placed, copied.Skipped), new FitComparer(), $"Layout {index}.");
            Assert.AreEqual(layout.Length, copied.Placed.Length + copied.Skipped);
            if (index == 1) CollectionAssert.Contains(copied.Placed.Select(item => item.Id).ToArray(), "water.extra-1");
        }
    }

    private sealed class FitComparer : IEqualityComparer<(BuildPlacement[] Placed, int Skipped)>
    {
        public bool Equals((BuildPlacement[] Placed, int Skipped) x, (BuildPlacement[] Placed, int Skipped) y) => x.Skipped == y.Skipped && x.Placed.SequenceEqual(y.Placed);
        public int GetHashCode((BuildPlacement[] Placed, int Skipped) obj) => obj.Skipped;
    }

    [TestMethod]
    public void TheCopyIsWorkedOutOnceAndPricedAsItLands()
    {
        var s = NextFestivalTests.Drafted(TierTwo());
        var previous = GameSession.StandardBuildLayout(2).Select(item => item.Id == "toilet.extra-1" ? item with { Cell = new(82, 153) } : item)
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        SetPreparation(s, p => p with { PreviousLayout = previous });
        var first = s.LastFestivalLayoutFit()!.Value;
        // Moving something placed now doesn't change what a copy would lay out, so it's remembered, not redone.
        var cache = typeof(GameSession).GetField("_lastLayoutFit", BindingFlags.Static | BindingFlags.NonPublic)!;
        BuildSession.Accept(s, new MoveBuildServiceCommand("bin.1", new(121, 150), 0));
        var again = s.LastFestivalLayoutFit()!.Value;
        CollectionAssert.AreEqual(first.Placed, again.Placed);
        Assert.AreNotSame(first.Placed, again.Placed, "Each caller gets its own array.");
        // The button's price is the fitted layout's, as the confirmation shows: the loo that no longer fits isn't charged.
        var fitted = again.Placed.Sum(item => GameSession.BuildServiceFeePennies(item.Kind));
        Assert.AreEqual(previous.Sum(item => GameSession.BuildServiceFeePennies(item.Kind)) - GameSession.BuildServiceFeePennies(BuildServiceKind.Toilet), fitted);
        BuildSession.Accept(s, new UseLastFestivalLayoutCommand());
        Assert.AreEqual(fitted, s.CaptureBuildPlacements().Sum(item => (long)GameSession.BuildServiceFeePennies(item.Kind)));
        Assert.IsNotNull(cache.GetValue(null));
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
        Assert.AreEqual(trailer.AudienceCells().Count(), pond.AudienceCells().Count());
        Assert.AreEqual(589, pond.AudienceCells().Distinct().Count());
        static int Area((int MinX, int MaxX, int MinZ, int MaxZ) b) => (b.MaxX - b.MinX + 1) * (b.MaxZ - b.MinZ + 1);
        Assert.AreEqual(Area(trailer.AudienceBounds), Area(pond.AudienceBounds));
        Assert.AreEqual((153, 183, 136, 154), pond.AudienceBounds);
        // Nothing in Tier 2's default stands in it, bins aside, and the pond's engineer mixes behind it.
        foreach (var item in GameSession.StandardBuildLayout(2).Where(item => item.Kind != BuildServiceKind.Bin))
            Assert.IsFalse(GameSession.BuildFootprint(item).Any(pond.InAudienceArea), item.Id);
        Assert.IsTrue(pond.MixingPlace is { } mixing && !pond.InAudienceArea(mixing) && mixing.Z < pond.AudienceBounds.MinZ);
    }

    [TestMethod]
    public void TheOpeningToiletStandsClearOfBothCrowds()
    {
        // The toilet a campaign opens with stands until Build lays out its own; the food-and-drink baseline a retry or
        // check is built from keeps it, Pond Stage open or not. It must stand outside both crowds, where Build would
        // let a toilet stand.
        var toilet = (ToiletFacility)typeof(GameSession).GetMethod("OpeningMainToilet", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [])!;
        Assert.AreEqual("toilet.main", toilet.Id);
        var placement = new BuildPlacement(toilet.Id, BuildServiceKind.Toilet, toilet.Cell, toilet.QuarterTurns);
        foreach (var stage in FestivalStages.All)
            Assert.IsFalse(GameSession.BuildFootprint(placement).Concat(GameSession.ToiletSolidCells(toilet)).Any(stage.InAudienceArea), stage.Id);
        var validate = typeof(GameSession).GetMethod("ValidateBuildLayout", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.IsNull(validate.Invoke(null, [new[] { placement }, FestivalStages.All, null, false, 2, false, 0]));
    }
}

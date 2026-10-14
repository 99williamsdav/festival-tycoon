using System.Reflection;
using System.Text.Json;
using Festival.Simulation;

namespace Festival.Tests;

// Band relationships: each set's record, the gig score, quotes from its crowd, the fee it moves, and the carry to Tier 2.
[TestClass]
public sealed class BandRelationshipTests
{
    private static void Send(GameSession s, SessionCommand command) => BuildSession.Accept(s, command);

    /// <summary>A Tier 1 set that went as well as one can: a full field clapping it out, enthusiastically.</summary>
    private static CarriedGig Triumph(string actId) => new(1, actId, true, false, 25, 25, 25 * 1_000, []) { AverageCrowdTenths = 250 };
    /// <summary>A Tier 1 set whose band never made it on, and one of them collapsed.</summary>
    private static CarriedGig NoShow(string actId) => new(1, actId, false, false, 0, 0, 0, [GigRules.Collapse]);

    /// <summary>Gives a Tier 2 festival a Tier 1 behind it: those sets, and the relationships they work out to.</summary>
    private static void SetHistory(GameSession s, params CarriedGig[] history)
    {
        var field = typeof(GameSession).GetField("_preparation", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var p = (PreparationSnapshot)field.GetValue(s)!;
        var (relationships, popularity) = Replay(history);
        field.SetValue(s, p with { RelationshipHistory = history, ActRelationships = relationships, ActPopularity = popularity });
    }

    /// <summary>The relationships and popularity a campaign's sets work out to.</summary>
    private static (ActRelationship[]?, PopularityShift[]?) Replay(CarriedGig[] history) =>
        ((ActRelationship[]?, PopularityShift[]?))typeof(GameSession).GetMethod("Replay", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [history, false])!;

    /// <summary>A Tier 1 set that held its crowd and won over <paramref name="newcomers"/> guests who weren't fans going in.</summary>
    private static CarriedGig WonOver(string actId, int newcomers) => new(1, actId, true, false, 20, 18, 18 * 300, []) { WonOver = newcomers, AverageCrowdTenths = 160 };

    /// <summary>A Tier 1 day played until its first set has ended and been written down.</summary>
    private static GameSession AfterFirstSet(ulong seed = 20260922)
    {
        var s = BuildSession.Started(seed);
        while (s.PerformanceRecords.Count == 0) s.AdvanceWithoutSnapshot(80);
        return s;
    }

    /// <summary>Facts for a played-out set with this average crowd (in listeners), peak and end.</summary>
    private static GigFacts Played(double average, int peak, int end, int enjoyment) =>
        new(true, false, peak, end, end * enjoyment, []) { AverageCrowdTenths = (int)Math.Round(average * 10) };

    [TestMethod]
    public void ACrowdThatDriftsOffMovesTheRelationshipLessThanOneThatStays()
    {
        // Against what's normal: a typical set's average, end and peak score nothing.
        Assert.AreEqual(0, GigRules.CrowdScore(15, 98, 14, 14), "A typical set reads as neutral: about 70% of the expected crowd on average, 95% at the end, 105% at its peak.");
        // The user's old example: a peak of 15 against about 14 expected, 4 still there at the end, 9 on average.
        var example = GigRules.Judge(Played(9, 15, 4, 250), 14);
        Assert.AreEqual(GigRules.Smattering, example.Reaction);
        CollectionAssert.Contains(example.Reasons, "the crowd drifted off before the end");
        // Drained to one: worse still.
        var drained = Played(7, 15, 1, 250);
        var (reaction, delta, reasons) = GigRules.Judge(drained, expected: 14);
        Assert.AreEqual(GigRules.Smattering, reaction);
        Assert.IsTrue(delta < example.Delta && example.Delta < 0, $"drained {delta}, example {example.Delta}");
        CollectionAssert.Contains(reasons, "the crowd drifted off before the end");
        // A set that holds more than the expected crowd throughout and to the end: a big crowd, and a much better gig.
        var (heldReaction, heldDelta, heldReasons) = GigRules.Judge(Played(17, 20, 19, 250), 14);
        Assert.AreEqual(GigRules.Warm, heldReaction);
        Assert.IsTrue(heldDelta > 0, $"held {heldDelta}");
        CollectionAssert.Contains(heldReasons, "a big crowd");
        // The end counts for more than the peak: ten there throughout beats a peak of 14 with five left.
        Assert.IsTrue(GigRules.CrowdScore(10, 100, 10, 14) > GigRules.CrowdScore(14, 100, 5, 14));
        // The fewer stay, the lower it goes, all the way down; and the same for the average.
        for (var end = 15; end > 0; end--) Assert.IsTrue(GigRules.CrowdScore(15, 100, end - 1, 14) <= GigRules.CrowdScore(15, 100, end, 14), $"end {end}");
        for (var average = 150; average > 0; average -= 5) Assert.IsTrue(GigRules.CrowdScore(15, average - 5, 12, 14) <= GigRules.CrowdScore(15, average, 12, 14), $"average {average}");
        // A thin crowd that was never big doesn't read as drifting off.
        CollectionAssert.DoesNotContain(GigRules.Judge(Played(4, 6, 2, 200), 16).Reasons, "the crowd drifted off before the end");
    }

    /// <summary>
    /// The user's Tier 2 case (13 Oct): nobody watched while the other stage played, then the whole field came over for the
    /// last few seconds because nothing else was on. The average is what it really drew, so it scores poorly whatever the end.
    /// </summary>
    [TestMethod]
    public void ALateSurgeAfterAnEmptySetScoresPoorly()
    {
        var surge = Played(0.5, 14, 14, 30);
        Assert.IsTrue(GigRules.CrowdScore(surge.PeakCrowd, surge.AverageCrowdTenths, surge.SetEndCrowd, 14) <= -8,
            $"Surge crowd score {GigRules.CrowdScore(surge.PeakCrowd, surge.AverageCrowdTenths, surge.SetEndCrowd, 14)}");
        var (reaction, delta, reasons) = GigRules.Judge(surge, 14);
        Assert.AreEqual(GigRules.Smattering, reaction);
        Assert.IsTrue(delta < 0, $"delta {delta}");
        CollectionAssert.DoesNotContain(reasons, "a big crowd");
        // The same end crowd after a set they watched throughout reads as a typical, neutral-or-better crowd.
        var held = Played(12, 16, 14, 200);
        Assert.IsTrue(GigRules.CrowdScore(held.PeakCrowd, held.AverageCrowdTenths, held.SetEndCrowd, 14) >= 0);
        Assert.IsTrue(GigRules.Judge(held, 14).Delta > delta + 8, "Watched throughout beats a late surge to the same end by a clear margin.");
        // Nor can a late surge win the act newcomers: none of them heard a quarter of the set.
        Assert.IsFalse(GigRules.IsWonOver(30, true, 800, 240), "Ten seconds heard, however enjoyable, wins nobody over.");
        Assert.IsTrue(GigRules.IsWonOver(30, true, GigRules.WonOverHeardTicks, 240));
    }

    [TestMethod]
    public void TheLabelReadsTheSetAsTheScoreDoes()
    {
        var steady = Played(14, 18, 16, 200);
        Assert.AreEqual(GigRules.Polite, GigRules.Judge(steady, 16).Reaction);
        Assert.AreEqual(GigRules.Warm, GigRules.Judge(Played(20, 24, 22, 200), 16).Reaction);
        var thin = Played(6, 8, 4, 200);
        Assert.AreEqual(GigRules.Smattering, GigRules.Judge(thin, 16).Reaction);
        Assert.IsTrue(GigRules.Judge(thin, 16).Delta <= 0, "A smattering never reads as a gain from the crowd.");
        Assert.AreEqual(GigRules.Smattering, GigRules.Judge(Played(8, 19, 1, 200), 16).Reaction,
            "A big peak that drifted off reads as the smattering left at the end.");
        Assert.AreEqual(GigRules.EmptyField, GigRules.Judge(Played(2, 4, 0, 0), 16).Reaction);
        Assert.AreEqual(GigRules.Enthusiastic, GigRules.Judge(Played(14, 20, 12, 500), 16).Reaction);
        Assert.AreEqual(GigRules.CutShort, GigRules.Judge(new GigFacts(true, true, 20, 0, 0, [GigRules.PowerCut]), 16).Reaction);
        Assert.AreEqual(GigRules.NoShow, GigRules.Judge(new GigFacts(false, false, 0, 0, 0, []), 16).Reaction);
    }

    [TestMethod]
    public void AGoodGigRaisesTheRelationshipAndABadOneLowersItBelowZero()
    {
        var (good, goodReasons) = GigRules.Score(GigRules.Warm, [], peak: 24, setEnd: 24, expected: 18, averageEnjoyment: 420, averageCrowdTenths: 220);
        Assert.IsTrue(good > 0, $"good gig {good}");
        CollectionAssert.Contains(goodReasons, "a big crowd");
        var (bad, badReasons) = GigRules.Score(GigRules.CutShort, [GigRules.PowerCut, GigRules.Boos], peak: 15, setEnd: 0, expected: 18, averageEnjoyment: 0, averageCrowdTenths: 80);
        Assert.IsTrue(bad < 0, $"bad gig {bad}");
        CollectionAssert.Contains(badReasons, "power cut");
        Assert.IsTrue(GigRules.Apply(3, bad) < 0, "A bad gig can take a relationship below zero.");
        Assert.AreEqual(GigRules.Highest, GigRules.Apply(95, 25));
        Assert.AreEqual(GigRules.Lowest, GigRules.Apply(-95, -30));
    }

    [TestMethod]
    public void TheBestAndWorstGigsAreHeldToTheirRange()
    {
        var (best, _) = GigRules.Score(GigRules.Enthusiastic, [], peak: 40, setEnd: 40, expected: 18, averageEnjoyment: 1_500, averageCrowdTenths: 400);
        Assert.AreEqual(GigRules.BestGig, best);
        var (worst, reasons) = GigRules.Score(GigRules.NoShow, [GigRules.Collapse], peak: 0, setEnd: 0, expected: 18, averageEnjoyment: 0, averageCrowdTenths: 0);
        Assert.AreEqual(GigRules.WorstGig, worst);
        Assert.AreEqual("never made it on stage", reasons[0]);
        // A set that died gets nothing for the crowd that waited for it.
        var (dead, _) = GigRules.Score(GigRules.CutShort, [GigRules.PowerCut], peak: 25, setEnd: 0, expected: 18, averageEnjoyment: 0, averageCrowdTenths: 120);
        Assert.AreEqual(-GigRules.CutShortPenalty - GigRules.PowerCutPenalty, dead);
    }

    [TestMethod]
    public void EachHiccupLowersTheScore()
    {
        var (clean, _) = GigRules.Score(GigRules.Polite, [], peak: 14, setEnd: 10, expected: 18, averageEnjoyment: 220, averageCrowdTenths: 110);
        foreach (var hiccup in GigRules.HiccupOrder)
        {
            var (with, reasons) = GigRules.Score(GigRules.Polite, [hiccup], peak: 14, setEnd: 10, expected: 18, averageEnjoyment: 220, averageCrowdTenths: 110);
            Assert.IsTrue(with < clean, $"{hiccup}: {with} vs {clean}");
            Assert.AreEqual(clean - GigRules.Penalty(hiccup), with, hiccup);
            // A gig that still came out ahead gives its good reasons; one that went under names what went wrong.
            if (with < 0) CollectionAssert.Contains(reasons, GigRules.HiccupWord(hiccup).ToLowerInvariant(), hiccup);
        }
        var (cut, _) = GigRules.Score(GigRules.CutShort, [], peak: 14, setEnd: 0, expected: 18, averageEnjoyment: 0, averageCrowdTenths: 100);
        var (finished, _) = GigRules.Score(GigRules.Polite, [], peak: 14, setEnd: 14, expected: 18, averageEnjoyment: 200, averageCrowdTenths: 120);
        Assert.IsTrue(cut < finished, "Being cut short costs more than playing out to the same crowd.");
        var (walkedOut, _) = GigRules.Score(GigRules.EmptyField, [], peak: 14, setEnd: 0, expected: 18, averageEnjoyment: 0, averageCrowdTenths: 30);
        Assert.IsTrue(walkedOut < finished, "A crowd that walked out on a set is remembered.");
    }

    [TestMethod]
    public void TheFeeHalvesAtBestAndDoublesAtWorstToTheNearestPound()
    {
        Assert.AreEqual(4_000, GigRules.Fee(4_000, 0));
        Assert.AreEqual(2_000, GigRules.Fee(4_000, 100));
        Assert.AreEqual(8_000, GigRules.Fee(4_000, -100));
        Assert.AreEqual(3_700, GigRules.Fee(4_000, 14), "£40 less 7% is £37.20, so £37.");
        Assert.AreEqual(5_200, GigRules.Fee(4_000, -30), "£40 and 30% more is £52.");
        Assert.AreEqual(1_300, GigRules.Fee(1_350, 1), "A £13.50 stretch fee less 0.5% is £13.43, so £13.");
        Assert.AreEqual(1_350, GigRules.Fee(1_350, 0), "A stretch fee in pence is untouched at no relationship.");
        Assert.AreEqual(-7m, GigRules.FeePercent(14));
        Assert.AreEqual(30m, GigRules.FeePercent(-30));
        Assert.AreEqual(-50m, GigRules.FeePercent(100));
        Assert.AreEqual(100m, GigRules.FeePercent(-100));
        var standing = FestivalStanding.Established;
        var act = ActCatalogue.Find("act.meadow-lanterns")!;
        Assert.AreEqual(GigRules.Fee(act.PricePennies, -40), ActCatalogue.Fee(standing, act, -40));
    }

    [TestMethod]
    public void TheRelationshipFeeIsWhatTheBookingThePlanThePaymentAndTheSaveAllUse()
    {
        var s = NextFestivalTests.Drafted(GameSession.CreateDevelopmentFestival(20260922, 2));
        var acts = s.GetFestivalActs().Where(act => s.ActStandingOf(act) == ActStanding.Available)
            .OrderBy(act => act.PricePennies).ThenBy(act => act.Id, StringComparer.Ordinal).ToArray();
        var liked = acts[0];
        var disliked = acts[1];
        var basePlan = s.PreparationPlanCost;
        SetHistory(s, Triumph(liked.Id), NoShow(disliked.Id));
        Assert.AreEqual(GigRules.BestGig, s.ActRelationship(liked.Id));
        Assert.AreEqual(GigRules.WorstGig, s.ActRelationship(disliked.Id));
        Assert.AreEqual(GigRules.Fee(ActCatalogue.Fee(s.Standing, liked), GigRules.BestGig), s.ActFee(liked));
        Assert.AreEqual(GigRules.Fee(ActCatalogue.Fee(s.Standing, disliked), GigRules.WorstGig), s.ActFee(disliked));
        Assert.AreNotEqual(ActCatalogue.Fee(s.Standing, liked), s.ActFee(liked));
        Assert.AreEqual(s.ActFee(liked), s.GetPreparationOffers().Single(offer => offer.Id == liked.Id).PricePennies);
        Send(s, new SetProgrammeCommand([liked.Id, disliked.Id, acts[2].Id]));
        Send(s, new SetProgrammeCommand(acts[3..6].Select(act => act.Id).ToArray()) { StageId = s.Stages[1].Id });
        Assert.AreEqual(basePlan + new[] { liked, disliked }.Concat(acts[2..6]).Sum(act => (long)s.ActFee(act)), s.PreparationPlanCost);
        Send(s, new SetPreparationStockCommand(40, 32));
        foreach (var hire in BuildSession.Crew(s)) Send(s, hire);
        Send(s, new AcceptPreparationOfferCommand(BuildSession.ExtraId(s, StaffRole.Sound)));
        Send(s, new StartPreparedEditionCommand());
        var payments = s.CapturePreparation()!.Payments;
        Assert.AreEqual(s.ActFee(liked), payments.Single(payment => payment.OfferId == liked.Id).AmountPennies);
        Assert.AreEqual(s.ActFee(disliked), payments.Single(payment => payment.OfferId == disliked.Id).AmountPennies);
        // The save's payment check prices the acts the same way.
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void RelationshipsThatTheEarlierSetsDontGiveAreRefusedOnRestore()
    {
        const string refused = "Act relationships invalid.";
        var s = BuildSession.Started();
        var saved = s.CapturePersistenceSnapshot();
        var tampered = saved with { Preparation = saved.Preparation! with { ActRelationships = [new("act.meadow-lanterns", 5)] } };
        Assert.AreEqual(refused, GameSession.Restore(tampered).Error, "Tier 1 has no festival before it.");
        var tier2 = NextFestivalTests.Drafted(GameSession.CreateDevelopmentFestival(20260922, 2));
        SetHistory(tier2, Triumph("act.meadow-lanterns"), NoShow("act.parish-ceilidh"));
        var two = tier2.CapturePersistenceSnapshot();
        Assert.IsTrue(GameSession.Restore(two).IsSuccess, "Relationships that follow from their sets restore.");
        var p = two.Preparation!;
        // Every act in the catalogue at +100: no Tier 1 could have done that.
        var everyone = ActCatalogue.All.Select(act => new ActRelationship(act.Id, 100)).OrderBy(item => item.ActId, StringComparer.Ordinal).ToArray();
        Assert.AreEqual(refused, GameSession.Restore(two with { Preparation = p with { ActRelationships = everyone } }).Error);
        Assert.AreEqual(refused, GameSession.Restore(two with { Preparation = p with { ActRelationships = everyone, RelationshipHistory = null } }).Error);
        // Nor a history that a Tier 1 couldn't hold: more sets than slots, an act twice, a crowd bigger than the field.
        var many = ActCatalogue.All.Take(4).Select(act => Triumph(act.Id)).ToArray();
        foreach (var history in new CarriedGig[][] { many, [Triumph("act.meadow-lanterns"), Triumph("act.meadow-lanterns")],
                     [Triumph("act.meadow-lanterns") with { PeakCrowd = 26, SetEndCrowd = 26 }], [Triumph("act.meadow-lanterns") with { Tier = 2 }] })
        {
            var (forged, _) = Replay(history);
            Assert.AreEqual(refused, GameSession.Restore(two with { Preparation = p with { RelationshipHistory = history, ActRelationships = forged } }).Error);
        }
        Assert.AreEqual(refused, GameSession.Restore(two with { Preparation = p with { ActRelationships = [new("act.meadow-lanterns", 24), new("act.parish-ceilidh", -30)] } }).Error,
            "Off by one from what the sets give.");
    }

    [TestMethod]
    public void ThePeakCrowdIsTheMostListenersAtTheirPlacesWhileTheSetPlayed()
    {
        var s = BuildSession.Started();
        var most = 0;
        while (s.PerformanceRecords.Count == 0)
        {
            s.AdvanceWithoutSnapshot(1);
            var live = s.CaptureLivePerformance()!;
            if (live.Stage is LiveSetStage.Live or LiveSetStage.Interrupted)
            {
                most = Math.Max(most, live.Listeners.Count(listener => listener.AtPlace));
                Assert.AreEqual(most, live.PeakListeners, $"tick {s.CurrentTick}");
            }
            else if (live.Stage == LiveSetStage.BeforeSet) Assert.AreEqual(0, live.PeakListeners);
        }
        var record = s.PerformanceRecords.Single();
        Assert.IsTrue(most > 0);
        Assert.AreEqual(Math.Max(most, record.SetEndCrowd), record.PeakCrowd, "The most at their places, and never fewer than clapped it out.");
        Assert.IsTrue(record.PeakCrowd >= record.SetEndCrowd);
        // The record outlives the set: the next set starts its own count.
        while (s.CaptureProgramme()!.CurrentSlot == 0) s.AdvanceWithoutSnapshot(80);
        Assert.AreEqual(0, s.CaptureLivePerformance()!.PeakListeners);
        Assert.AreEqual(record.PeakCrowd, s.PerformanceRecords[0].PeakCrowd);
    }

    [TestMethod]
    public void TheAverageCrowdIsListenersAtTheirPlacesOverEveryTickTheSetWasOn()
    {
        var s = BuildSession.Started();
        long sum = 0;
        while (s.PerformanceRecords.Count == 0)
        {
            s.AdvanceWithoutSnapshot(1);
            var live = s.CaptureLivePerformance()!;
            // Counted on the ticks the peak is: from the one the set started to the one it ended.
            if (live.Stage is LiveSetStage.Live or LiveSetStage.Interrupted || live.Stage == LiveSetStage.Finished && live.EndedTick == s.CurrentTick)
                sum += live.Listeners.Count(listener => listener.AtPlace);
            else Assert.AreEqual(0L, live.ListenerTicks);
            Assert.AreEqual(sum, live.ListenerTicks, $"tick {s.CurrentTick}");
        }
        var set = s.CaptureLivePerformance()!;
        var record = s.PerformanceRecords.Single();
        Assert.AreEqual((int)(sum * 10 / (set.EndedTick - set.StartedTick + 1)), record.AverageCrowdTenths);
        Assert.IsTrue(record.AverageCrowdTenths > 0 && record.AverageCrowdTenths <= 10 * record.PeakCrowd);
        Assert.AreEqual(record.AverageCrowdTenths, record.Facts.AverageCrowdTenths);
        // The score reads it: the same set with nobody there on average scores worse.
        Assert.IsTrue(GigRules.Judge(record.Facts with { AverageCrowdTenths = 0 }, record.ExpectedCrowd).Delta < record.Delta);
        BuildSession.Restored(s);
        // The next set starts its own count.
        while (s.CaptureProgramme()!.CurrentSlot == 0) s.AdvanceWithoutSnapshot(80);
        Assert.AreEqual(0L, s.CaptureLivePerformance()!.ListenerTicks);
    }

    [TestMethod]
    public void ForgedAveragesAreRefusedOnRestore()
    {
        const string invalid = "Performance records invalid.";
        var s = AfterFirstSet();
        var saved = s.CapturePersistenceSnapshot();
        var record = saved.PerformanceRecords![0];
        // The set just finished: its record must match the listener-ticks its live set counted.
        foreach (var forged in new[] { record.AverageCrowdTenths + 1, record.AverageCrowdTenths - 1, 10 * record.PeakCrowd })
            Assert.AreEqual(invalid, GameSession.Restore(saved with { PerformanceRecords = [record with { AverageCrowdTenths = forged }] }).Error, $"{forged}");
        // And the live set's count can't be more than its peak on every tick it was on, nor anything before it started.
        var live = saved.LivePerformances![0]!;
        var ticks = live.EndedTick - live.StartedTick + 1;
        Assert.AreEqual("Live performance state invalid.", GameSession.Restore(saved with
            { LivePerformances = [live with { ListenerTicks = live.PeakListeners * ticks + 1 }] }).Error);
        Assert.AreEqual("Live performance state invalid.", GameSession.Restore(saved with
            { LivePerformances = [live with { ListenerTicks = -1 }] }).Error);
        // An earlier set (no longer the live one) is held to what a set could have: never more on average than its peak.
        while (s.CaptureProgramme()!.CurrentSlot == 0) s.AdvanceWithoutSnapshot(80);
        var later = s.CapturePersistenceSnapshot();
        var past = later.PerformanceRecords![0];
        Assert.IsTrue(GameSession.Restore(later).IsSuccess);
        Assert.AreEqual(invalid, GameSession.Restore(later with { PerformanceRecords = [past with { AverageCrowdTenths = 10 * past.PeakCrowd + 1 }] }).Error);
        Assert.AreEqual(invalid, GameSession.Restore(later with { PerformanceRecords = [past with { AverageCrowdTenths = -1 }] }).Error);
        // A forged average within the peak still has to give the score the record carries.
        Assert.AreEqual(invalid, GameSession.Restore(later with { PerformanceRecords = [past with { AverageCrowdTenths = 0 }] }).Error);
    }

    [TestMethod]
    public void ARecordFollowsFromItsSetAndQuotesOnlyItsOwnCrowd()
    {
        var s = AfterFirstSet();
        var record = s.PerformanceRecords.Single();
        var live = s.CaptureLivePerformance()!;
        var p = s.CapturePreparation()!;
        Assert.AreEqual(FestivalStages.MainId, record.StageId);
        Assert.AreEqual(0, record.Slot);
        Assert.AreEqual(BuildSession.Acts[0], record.ActId);
        Assert.AreEqual(p.StartedTick + FestivalStages.Main.SlotStarts[0], record.ScheduledStartTick);
        Assert.AreEqual(p.StartedTick + FestivalStages.Main.SlotEnds[0], record.ScheduledEndTick);
        Assert.AreEqual(live.StartedTick, record.StartedTick);
        Assert.AreEqual(live.EndedTick, record.EndedTick);
        Assert.AreEqual(live.SetEndAudienceCount, record.SetEndCrowd);
        Assert.AreEqual(live.SetEndEnjoymentTotal / Math.Max(1, live.SetEndAudienceCount), record.AverageEnjoyment);
        Assert.AreEqual(0, record.RelationshipBefore, "Tier 1 opens with every relationship at zero.");
        Assert.AreEqual(GigRules.Apply(0, record.Delta), record.RelationshipAfter);
        Assert.IsTrue(record.Delta is >= GigRules.WorstGig and <= GigRules.BestGig);
        Assert.IsTrue(record.Reasons.Length is >= 1 and <= 3);
        var heard = live.Listeners.Where(listener => listener.ListenedTicks > 0).Select(listener => listener.AgentId).ToHashSet();
        CollectionAssert.AreEqual(heard.Order().ToArray(), record.CrowdIds);
        Assert.IsTrue(record.Quotes.Length is 2 or 3);
        var guests = p.People.Where(person => person.Role == ProtectedPersonRole.Guest).ToDictionary(person => person.AgentId);
        foreach (var quote in record.Quotes)
        {
            Assert.IsTrue(heard.Contains(quote.GuestId), "Only someone who heard the set is quoted.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(guests[quote.GuestId].Name));
            Assert.AreEqual(s.FestivalAffinity(quote.GuestId, ActCatalogue.Find(record.ActId)!) >= 60, quote.Fan);
            Assert.IsFalse(string.IsNullOrWhiteSpace(GigQuotes.Line(record.Reaction, quote)));
        }
        Assert.AreEqual(record.Quotes.Length, record.Quotes.Select(quote => quote.GuestId).Distinct().Count());
    }

    [TestMethod]
    public void RecordsAndQuotesAreTheSameEveryRunAndRestoreAfterASave()
    {
        var first = AfterFirstSet();
        var second = AfterFirstSet();
        Assert.AreEqual(JsonSerializer.Serialize(first.PerformanceRecords), JsonSerializer.Serialize(second.PerformanceRecords));
        var restored = BuildSession.Restored(first);
        Assert.AreEqual(JsonSerializer.Serialize(first.PerformanceRecords), JsonSerializer.Serialize(restored.PerformanceRecords));
        NextFestivalTests_RunToEnd(first);
        NextFestivalTests_RunToEnd(restored);
        Assert.AreEqual(3, first.PerformanceRecords.Count);
        Assert.AreEqual(JsonSerializer.Serialize(first.PerformanceRecords), JsonSerializer.Serialize(restored.PerformanceRecords));
        Assert.AreEqual(first.CaptureSnapshot().AuthoritativeHash, restored.CaptureSnapshot().AuthoritativeHash);
        BuildSession.Restored(first);
    }

    [TestMethod]
    public void ATamperedRecordIsRefusedOnRestore()
    {
        var s = AfterFirstSet();
        var saved = s.CapturePersistenceSnapshot();
        var record = saved.PerformanceRecords![0];
        var stranger = saved.Preparation!.People.First(person => person.Role == ProtectedPersonRole.Guest && !record.CrowdIds.Contains(person.AgentId) ||
            person.Role == ProtectedPersonRole.Staff).AgentId;
        PerformanceRecord[][] tampered =
        [
            [record with { Delta = record.Delta + 1, RelationshipAfter = record.RelationshipAfter + 1 }],
            [record with { Quotes = [record.Quotes[0] with { GuestId = stranger }, .. record.Quotes[1..]] }],
            [record with { Quotes = [record.Quotes[0] with { Line = record.Quotes[0].Line + 1 }, .. record.Quotes[1..]] }],
            [record with { PeakCrowd = record.PeakCrowd + 1 }],
            [record with { AverageCrowdTenths = record.AverageCrowdTenths + 1 }],
            [record with { ActId = BuildSession.Acts[1] }],
            [record with { Hiccups = [GigRules.Boos] }],
            [record with { Reaction = GigRules.Polite == record.Reaction ? GigRules.Warm : GigRules.Polite }],
            [record with { SetEndEnjoyment = record.SetEndEnjoyment + 1 }],
            [record with { CutShort = !record.CutShort }],
            [record with { RelationshipBefore = 5, RelationshipAfter = GigRules.Apply(5, record.Delta) }],
            [record, record with { Slot = 1 }],
        ];
        foreach (var records in tampered)
            Assert.AreEqual("Performance records invalid.", GameSession.Restore(saved with { PerformanceRecords = records }).Error);
        Assert.AreEqual("Performance records invalid.", GameSession.Restore(saved with { PerformanceRecords = null }).Error,
            "A finished set must have its record.");
    }

    [TestMethod]
    public void ACutShortSetCannotBeRelabelledEnthusiastic()
    {
        var s = BuildSession.Started();
        while (s.CaptureLivePerformance()!.Stage != LiveSetStage.Live) s.AdvanceWithoutSnapshot(80);
        s.AdvanceWithoutSnapshot(2_400);
        Send(s, new EquipmentCommand(EquipmentAction.Isolate));
        while (s.PerformanceRecords.Count == 0) s.AdvanceWithoutSnapshot(80);
        // Move on to the next set, so the cut-short one is no longer the live set it could be checked against.
        while (s.CaptureProgramme()!.CurrentSlot == 0) s.AdvanceWithoutSnapshot(80);
        var saved = s.CapturePersistenceSnapshot();
        var record = saved.PerformanceRecords![0];
        Assert.AreEqual(GigRules.CutShort, record.Reaction);
        // Still cut short but labelled enthusiastic, with its score, relationship and reasons made to match.
        var relabelled = record with { Reaction = GigRules.Enthusiastic };
        var (relabelledDelta, relabelledReasons) = GigRules.Score(GigRules.Enthusiastic, record.Hiccups, record.PeakCrowd, record.SetEndCrowd, record.ExpectedCrowd, record.AverageEnjoyment, record.AverageCrowdTenths);
        relabelled = relabelled with { Delta = relabelledDelta, RelationshipAfter = GigRules.Apply(record.RelationshipBefore, relabelledDelta), Reasons = relabelledReasons };
        Assert.AreEqual("Performance records invalid.", GameSession.Restore(saved with { PerformanceRecords = [relabelled, .. saved.PerformanceRecords[1..]] }).Error);
        // Nor by saying it wasn't cut short: its own numbers don't make an enthusiastic set.
        Assert.AreEqual("Performance records invalid.", GameSession.Restore(saved with { PerformanceRecords = [relabelled with { CutShort = false }, .. saved.PerformanceRecords[1..]] }).Error);
    }

    [TestMethod]
    public void AMissedFirstSetIsRecordedAndItsRecordCannotBeDropped()
    {
        var s = BuildSession.Started();
        // Labelled fixture: the clock jumps past the first set without its band ever getting on, as a day that ran away would.
        typeof(GameSession).GetProperty(nameof(GameSession.CurrentTick))!.SetValue(s, 16_000L);
        typeof(GameSession).GetMethod("FinishLivePerformance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, null);
        BuildSession.SetMainProgramme(s, s.CaptureProgramme()! with { CurrentSlot = 1 });
        typeof(GameSession).GetMethod("StartLivePerformance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, null);
        var record = s.PerformanceRecords.Single();
        Assert.AreEqual(0, record.Slot);
        Assert.AreEqual(GigRules.NoShow, record.Reaction);
        Assert.AreEqual(GigRules.WorstGig, record.Delta, "Nobody heard a note and the band never came: the worst a gig gives.");
        var saved = s.CapturePersistenceSnapshot();
        Assert.IsTrue(GameSession.Restore(saved).IsSuccess, GameSession.Restore(saved).Error);
        Assert.AreEqual("Performance records invalid.", GameSession.Restore(saved with { PerformanceRecords = null }).Error, "The missed set must be on record.");
    }

    [TestMethod]
    public void APowerCutMidSetIsAHiccupThatCostsTheAct()
    {
        var s = BuildSession.Started();
        while (s.CaptureLivePerformance()!.Stage != LiveSetStage.Live) s.AdvanceWithoutSnapshot(80);
        s.AdvanceWithoutSnapshot(2_400);
        Send(s, new EquipmentCommand(EquipmentAction.Isolate));
        while (s.PerformanceRecords.Count == 0) s.AdvanceWithoutSnapshot(80);
        var record = s.PerformanceRecords.Single();
        CollectionAssert.Contains(record.Hiccups, GigRules.PowerCut);
        CollectionAssert.Contains(record.Hiccups, GigRules.Boos);
        Assert.AreEqual(GigRules.CutShort, record.Reaction);
        Assert.IsTrue(record.Delta < 0, $"delta {record.Delta}");
        Assert.IsTrue(record.RelationshipAfter < 0);
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void ARetryForgetsTheFailedFestivalsSets()
    {
        var s = AfterFirstSet();
        typeof(GameSession).GetMethod("RetryPreparedWeekend", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, []);
        Assert.AreEqual(0, s.PerformanceRecords.Count);
        Assert.IsNull(s.RelationshipsAfterFestival);
        Assert.IsNull(s.PopularityAfterFestival);
    }

    [TestMethod]
    public void NewcomersWonOverRaisePopularityWithinTheCap()
    {
        // Newcomers needed for a point: 2 for an obscure act, more the more popular it already is.
        CollectionAssert.AreEqual(new[] { 2, 2, 2, 5, 8, 11, 14, 17, 20, 23 },
            new[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 }.Select(GigRules.NewcomersPerPoint).ToArray());
        GigFacts Set(int won, int walked = 0) => new(true, false, 25, 20, 20 * 300, []) { WonOver = won, WalkedOff = walked };
        Assert.AreEqual(0, GigRules.PopularityChange(Set(1), 10));
        Assert.AreEqual(1, GigRules.PopularityChange(Set(2), 10), "Two converts lift an obscure act.");
        Assert.AreEqual(2, GigRules.PopularityChange(Set(4), 10));
        Assert.AreEqual(GigRules.MostPopularityGain, GigRules.PopularityChange(Set(6), 10));
        Assert.AreEqual(GigRules.MostPopularityGain, GigRules.PopularityChange(Set(20), 10), "Held to +2 a set.");
        Assert.AreEqual(0, GigRules.PopularityChange(Set(2), 90), "Two converts do nothing for a famous act.");
        Assert.AreEqual(0, GigRules.PopularityChange(Set(19), 90));
        Assert.AreEqual(1, GigRules.PopularityChange(Set(20), 90));
        // Losing a point takes four times a point's worth walking off, and many more than were won over.
        Assert.AreEqual(0, GigRules.PopularityChange(Set(0, 7), 10));
        Assert.AreEqual(-1, GigRules.PopularityChange(Set(0, 8), 10));
        Assert.AreEqual(-1, GigRules.PopularityChange(Set(1, 8), 10));
        Assert.AreEqual(1, GigRules.PopularityChange(Set(3, 12), 10), "Enough won over outweighs those who walked.");
        Assert.AreEqual(0, GigRules.PopularityChange(Set(0, 8), 90), "A famous act shrugs off a handful walking off.");
        Assert.AreEqual(GigRules.MostPopularity, GigRules.ApplyPopularity(97, 99, 3));
        Assert.AreEqual(25, GigRules.ApplyPopularity(40, 25, -1), "Never more than 15 below the catalogue.");
        Assert.AreEqual(GigRules.LeastPopularity, GigRules.ApplyPopularity(6, 1, -1), "Nor below 1.");
        var unknown = ActCatalogue.All.MinBy(act => act.Popularity)!;
        Assert.IsTrue(ActCatalogue.WithPopularityShift(unknown, -GigRules.MostPopularityLost).PricePennies >= 100, "Never a free act.");
        Assert.AreEqual(unknown.Popularity - 1 < GigRules.MostPopularityLost ? GigRules.LeastPopularity : unknown.Popularity - GigRules.MostPopularityLost,
            ActCatalogue.WithPopularityShift(unknown, -40).Popularity);
        // Who counts: not a fan going in (keenness under 60), clapped it out, heard a quarter of the set and had at least 200 from it.
        Assert.IsTrue(GigRules.IsWonOver(59, true, 2_400, 200));
        Assert.IsFalse(GigRules.IsWonOver(60, true, 4_000, 900), "A fan already knew them.");
        Assert.IsFalse(GigRules.IsWonOver(40, false, 4_000, 900), "Left before the end.");
        Assert.IsFalse(GigRules.IsWonOver(40, true, 4_000, 199), "Stayed but didn't get much from it.");
        Assert.IsFalse(GigRules.IsWonOver(40, true, 2_399, 900), "Came over too late in the set to be won over by it.");
        Assert.IsTrue(GigRules.IsWalkOff(40, true, false, 2_400, 89));
        Assert.IsFalse(GigRules.IsWalkOff(40, true, false, 2_400, 90), "Enjoying it at 3 a second isn't unimpressed.");
        Assert.IsFalse(GigRules.IsWalkOff(40, true, false, 2_320, 0), "Under 30 s heard.");
        Assert.IsFalse(GigRules.IsWalkOff(40, false, false, 4_000, 0), "Nobody walks off a set that was cut short.");
        Assert.IsFalse(GigRules.IsWalkOff(70, true, false, 4_000, 0), "A fan leaving isn't a newcomer walking off.");
    }

    [TestMethod]
    public void ARecordsNewcomersAreItsCrowdsNonFansWhoClappedItOutHavingEnjoyedIt()
    {
        var s = AfterFirstSet();
        var record = s.PerformanceRecords.Single();
        var live = s.CaptureLivePerformance()!;
        var act = ActCatalogue.Find(record.ActId)!;
        var expected = live.Listeners.Where(item => s.FestivalAffinity(item.AgentId, act) < 60 && live.SetEndAudienceIds.Contains(item.AgentId) &&
            item.ListenedTicks >= GigRules.WonOverHeardTicks && item.EnjoymentEarned >= GigRules.WonOverEnjoyment).Select(item => item.AgentId).Order().ToArray();
        CollectionAssert.AreEqual(expected, record.WonOverIds ?? []);
        Assert.IsTrue(record.WonOverIds is null || record.WonOverIds.Length > 0, "Saved as null when nobody was won over.");
        Assert.AreEqual(GigRules.PopularityChange(record.Facts, act.Popularity), s.PopularityChange(record));
        Assert.IsTrue(s.PopularityChange(record) is >= -1 and <= GigRules.MostPopularityGain);
        // The festival it's in keeps the popularity it opened with: nothing moves mid-day, and a classic Tier 1 plays the catalogue as listed.
        Assert.IsNull(s.CapturePreparation()!.ActPopularity);
        Assert.AreEqual(0, s.PopularityShifts.Count);
        Assert.IsTrue(s.GetFestivalActs().All(item => ReferenceEquals(item, ActCatalogue.Find(item.Id))));
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void ForgedNewcomersOnAnEarlierSetAreRefusedOnRestore()
    {
        const string invalid = "Performance records invalid.";
        var s = AfterFirstSet();
        // On to the next set, so the first is no longer the live set it could be checked against.
        while (s.CaptureProgramme()!.CurrentSlot == 0) s.AdvanceWithoutSnapshot(80);
        var saved = s.CapturePersistenceSnapshot();
        Assert.IsTrue(GameSession.Restore(saved).IsSuccess);
        var record = saved.PerformanceRecords![0];
        var act = ActCatalogue.Find(record.ActId)!;
        var setEnd = record.SetEndIds ?? [];
        Assert.AreEqual(record.SetEndCrowd, setEnd.Length, "The record keeps who clapped it out.");
        var outsider = record.CrowdIds.FirstOrDefault(id => s.FestivalAffinity(id, act) < 60 && !setEnd.Contains(id) && record.WalkedOffIds?.Contains(id) != true);
        var forged = new List<PerformanceRecord> { record with { SetEndIds = [] } };
        if (setEnd.Length > 0) forged.Add(record with { SetEndIds = null });
        if (outsider != 0)
        {
            // Won over though they weren't there at the end, with or without claiming they were.
            forged.Add(record with { WonOverIds = [.. (record.WonOverIds ?? []).Append(outsider).Order()] });
            forged.Add(record with { WonOverIds = [.. (record.WonOverIds ?? []).Append(outsider).Order()], SetEndIds = [.. setEnd.Append(outsider).Order()] });
        }
        if (setEnd.Length > 0)
        {
            forged.Add(record with { WalkedOffIds = [.. (record.WalkedOffIds ?? []).Append(setEnd[0]).Order()] });
            forged.Add(record with { SetEndIds = setEnd.Reverse().ToArray() is var backwards && setEnd.Length > 1 ? backwards : [setEnd[0], setEnd[0]] });
        }
        foreach (var item in forged)
            Assert.AreEqual(invalid, GameSession.Restore(saved with { PerformanceRecords = [item, .. saved.PerformanceRecords[1..]] }).Error);
    }

    [TestMethod]
    public void ForgedNewcomersAreRefusedOnRestore()
    {
        const string invalid = "Performance records invalid.";
        var s = AfterFirstSet();
        var saved = s.CapturePersistenceSnapshot();
        var record = saved.PerformanceRecords![0];
        var act = ActCatalogue.Find(record.ActId)!;
        var guests = saved.Preparation!.People.Where(person => person.Role == ProtectedPersonRole.Guest).ToArray();
        var fan = record.CrowdIds.FirstOrDefault(id => s.FestivalAffinity(id, act) >= 60);
        var newcomer = record.CrowdIds.First(id => s.FestivalAffinity(id, act) < 60 && record.WonOverIds?.Contains(id) != true && record.WalkedOffIds?.Contains(id) != true);
        var forged = new List<PerformanceRecord>
        {
            record with { WonOverIds = [.. (record.WonOverIds ?? []).Append(newcomer).Order()] },
            record with { WonOverIds = [] },
            record with { WalkedOffIds = [newcomer] },
        };
        if (guests.FirstOrDefault(person => !record.CrowdIds.Contains(person.AgentId) && s.FestivalAffinity(person.AgentId, act) < 60) is { } stranger)
            forged.Add(record with { WonOverIds = [stranger.AgentId] });
        if (fan != 0) forged.Add(record with { WonOverIds = [.. (record.WonOverIds ?? []).Append(fan).Order()] });
        if (record.WonOverIds is { Length: > 0 } won)
        {
            forged.Add(record with { WonOverIds = won.Length == 1 ? null : won[1..] });
            forged.Add(record with { WalkedOffIds = [won[0]] });
            forged.Add(record with { WonOverIds = won.Length > 1 ? won.Reverse().ToArray() : [won[0], won[0]] });
        }
        foreach (var item in forged)
            Assert.AreEqual(invalid, GameSession.Restore(saved with { PerformanceRecords = [item, .. saved.PerformanceRecords[1..]] }).Error);
        Assert.IsTrue(GameSession.Restore(saved).IsSuccess);
    }

    [TestMethod]
    public void PopularityCarriesIntoTheNextFestivalAndThroughARetry()
    {
        var s = NextFestivalTests.Drafted(GameSession.CreateDevelopmentFestival(20260922, 2));
        var acts = s.GetFestivalActs().Where(act => s.ActStandingOf(act) == ActStanding.Available)
            .OrderBy(act => act.PricePennies).ThenBy(act => act.Id, StringComparer.Ordinal).ToArray();
        var winner = ActCatalogue.Find(acts[0].Id)!;
        var plain = ActCatalogue.Find(acts[1].Id)!;
        var need = GigRules.NewcomersPerPoint(winner.Popularity);
        SetHistory(s, WonOver(winner.Id, 2 * need), WonOver(plain.Id, need - 1));
        CollectionAssert.AreEqual(new[] { new PopularityShift(winner.Id, 2) }, s.CapturePreparation()!.ActPopularity, "One short of a point moves nothing.");
        var campaign = s.CampaignAct(winner);
        Assert.AreEqual(winner.Popularity + 2, campaign.Popularity);
        Assert.AreEqual(campaign, s.GetFestivalActs().Single(act => act.Id == winner.Id), "The booking table shows the act as it is now.");
        Assert.AreEqual(plain, s.CampaignAct(plain));
        // Fee: the catalogue's own curve, so a little dearer; and the same whichever copy of the act asks.
        Assert.IsTrue(campaign.PricePennies > winner.PricePennies, $"{campaign.PricePennies} vs {winner.PricePennies}");
        Assert.AreEqual(GigRules.Fee(ActCatalogue.Fee(s.Standing, campaign), s.ActRelationship(winner.Id)), s.ActFee(winner));
        Assert.AreEqual(s.ActFee(winner), s.ActFee(campaign));
        Assert.AreEqual(s.ActFee(winner), s.GetPreparationOffers().Single(offer => offer.Id == winner.Id).PricePennies);
        Assert.AreEqual(PerformanceRules.Talent(winner with { Popularity = winner.Popularity + 2 }), PerformanceRules.Talent(campaign), "Talent is popularity plus its swing, so it follows.");
        // It restores, and survives a retry.
        var restored = BuildSession.Restored(s);
        Assert.AreEqual(campaign, restored.CampaignAct(winner));
        typeof(GameSession).GetMethod("RetryPreparedWeekend", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, []);
        Assert.AreEqual(campaign, s.CampaignAct(winner), "A retry keeps the popularity the festival opened with.");
        CollectionAssert.AreEqual(new[] { new PopularityShift(winner.Id, 2) }, s.CapturePreparation()!.ActPopularity);
    }

    [TestMethod]
    public void PopularityThatTheEarlierSetsDontGiveIsRefusedOnRestore()
    {
        const string refused = "Act relationships invalid.";
        var tier2 = NextFestivalTests.Drafted(GameSession.CreateDevelopmentFestival(20260922, 2));
        SetHistory(tier2, WonOver("act.meadow-lanterns", 15), Triumph("act.parish-ceilidh"));
        var two = tier2.CapturePersistenceSnapshot();
        Assert.IsTrue(GameSession.Restore(two).IsSuccess, GameSession.Restore(two).Error);
        var p = two.Preparation!;
        CollectionAssert.AreEqual(new[] { new PopularityShift("act.meadow-lanterns", 2) }, p.ActPopularity, "Fifteen won over, held to +2 a set.");
        foreach (var forged in new PopularityShift[]?[] { null, [new("act.meadow-lanterns", 3)], [new("act.meadow-lanterns", 2), new("act.parish-ceilidh", 1)], [] })
            Assert.AreEqual(refused, GameSession.Restore(two with { Preparation = p with { ActPopularity = forged } }).Error);
        // Nor newcomers a set couldn't have had: more won over than clapped it out.
        var greedy = new CarriedGig[] { WonOver("act.meadow-lanterns", 19), Triumph("act.parish-ceilidh") };
        var (relationships, popularity) = Replay(greedy);
        Assert.AreEqual(refused, GameSession.Restore(two with { Preparation = p with { RelationshipHistory = greedy, ActRelationships = relationships, ActPopularity = popularity } }).Error);
        // And a Tier 1 has no festival before it to have moved anyone.
        var one = BuildSession.Started().CapturePersistenceSnapshot();
        Assert.AreEqual(refused, GameSession.Restore(one with { Preparation = one.Preparation! with { ActPopularity = [new("act.meadow-lanterns", 1)] } }).Error);
    }

    [TestMethod]
    public void BandsWhoLikedPlayingForYouAreAlwaysOfferedButBandsYouUpsetAreNot()
    {
        var s = GameSession.CreateDevelopmentFestival(20260922, 2);
        var drawn = s.GetFestivalActs().Select(act => act.Id).ToHashSet();
        var outside = ActCatalogue.All.Where(act => !drawn.Contains(act.Id) && s.ActStandingOf(act) == ActStanding.Available)
            .Select(act => act.Id).Order(StringComparer.Ordinal).Take(2).ToArray();
        Assert.AreEqual(2, outside.Length, "Two playable acts the shortlist didn't draw.");
        SetHistory(s, Triumph(outside[0]), NoShow(outside[1]));
        var offered = s.GetFestivalActs().Select(act => act.Id).ToArray();
        CollectionAssert.Contains(offered, outside[0], "A band that enjoyed the last festival comes back to you.");
        CollectionAssert.DoesNotContain(offered, outside[1], "A band you let down only turns up if the shortlist happens to draw them.");
    }

    [TestMethod]
    public void TheRelationshipCarriesToTierTwoAndItsFeeThere()
    {
        var one = BuildSession.Started();
        NextFestivalTests_RunToEnd(one);
        Assert.AreEqual(PreparationStatus.Finished, one.PreparedStatus);
        var records = one.PerformanceRecords;
        Assert.AreEqual(3, records.Count);
        var restored = BuildSession.Restored(one);
        Assert.AreEqual(JsonSerializer.Serialize(records), JsonSerializer.Serialize(restored.PerformanceRecords), "The finished day's records restore.");
        // Every set's record must be there: losing the first one is refused.
        var saved = one.CapturePersistenceSnapshot();
        Assert.AreEqual("Performance records invalid.", GameSession.Restore(saved with { PerformanceRecords = saved.PerformanceRecords!.Where(record => record.Slot != 0).ToArray() }).Error);
        var two = one.CreateNextFestival();
        Assert.AreEqual(0, two.PerformanceRecords.Count, "A new festival starts with no sets played.");
        foreach (var record in records)
        {
            Assert.AreEqual(record.RelationshipAfter, two.ActRelationship(record.ActId), record.ActId);
            Assert.IsTrue(two.CapturePreparation()!.RelationshipHistory!.Any(gig => gig.Tier == 1 && gig.ActId == record.ActId), "The set comes forward as its facts.");
            var act = ActCatalogue.Find(record.ActId)!;
            Assert.AreEqual(GigRules.Fee(ActCatalogue.Fee(two.Standing, two.CampaignAct(act)), record.RelationshipAfter), two.ActFee(act));
            // An act you've history with, who'll play for you, is always on the offer.
            if (record.RelationshipAfter > 0 && two.ActStandingOf(act) != ActStanding.Locked)
                Assert.IsTrue(two.GetFestivalActs().Contains(two.CampaignAct(act)), record.ActId);
            // Its popularity moved by the newcomers it won over, from this festival on.
            Assert.AreEqual(GigRules.ApplyPopularity(act.Popularity, act.Popularity, one.PopularityChange(record)), two.CampaignAct(act).Popularity, record.ActId);
            Assert.AreEqual(act.Popularity, one.CampaignAct(act).Popularity, "Never mid-festival.");
        }
        Assert.IsTrue(records.Any(record => record.RelationshipAfter != 0), "A real day moves someone.");
        BuildSession.Restored(two);
    }

    private static void NextFestivalTests_RunToEnd(GameSession s)
    {
        for (var i = 0; i < 80 && s.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing; i++) s.AdvanceWithoutSnapshot(1_000);
    }
}

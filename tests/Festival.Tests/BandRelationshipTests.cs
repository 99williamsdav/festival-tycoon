using System.Reflection;
using System.Text.Json;
using Festival.Simulation;

namespace Festival.Tests;

// Band relationships: each set's record, the gig score, quotes from its crowd, the fee it moves, and the carry to Tier 2.
[TestClass]
public sealed class BandRelationshipTests
{
    private static void Send(GameSession s, SessionCommand command) => BuildSession.Accept(s, command);

    private static void SetRelationships(GameSession s, params ActRelationship[] relationships)
    {
        var field = typeof(GameSession).GetField("_preparation", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var p = (PreparationSnapshot)field.GetValue(s)!;
        field.SetValue(s, p with { ActRelationships = relationships.OrderBy(item => item.ActId, StringComparer.Ordinal).ToArray() });
    }

    /// <summary>A Tier 1 day played until its first set has ended and been written down.</summary>
    private static GameSession AfterFirstSet(ulong seed = 20260922)
    {
        var s = BuildSession.Started(seed);
        while (s.PerformanceRecords.Count == 0) s.AdvanceWithoutSnapshot(80);
        return s;
    }

    [TestMethod]
    public void AGoodGigRaisesTheRelationshipAndABadOneLowersItBelowZero()
    {
        var (good, goodReasons) = GigRules.Score(GigRules.Warm, [], peak: 20, setEnd: 16, expected: 18, averageEnjoyment: 420);
        Assert.IsTrue(good > 0, $"good gig {good}");
        CollectionAssert.Contains(goodReasons, "a big crowd");
        var (bad, badReasons) = GigRules.Score(GigRules.CutShort, [GigRules.PowerCut, GigRules.Boos], peak: 15, setEnd: 0, expected: 18, averageEnjoyment: 0);
        Assert.IsTrue(bad < 0, $"bad gig {bad}");
        CollectionAssert.Contains(badReasons, "power cut");
        Assert.IsTrue(GigRules.Apply(3, bad) < 0, "A bad gig can take a relationship below zero.");
        Assert.AreEqual(GigRules.Highest, GigRules.Apply(95, 25));
        Assert.AreEqual(GigRules.Lowest, GigRules.Apply(-95, -30));
    }

    [TestMethod]
    public void TheBestAndWorstGigsAreHeldToTheirRange()
    {
        var (best, _) = GigRules.Score(GigRules.Enthusiastic, [], peak: 40, setEnd: 40, expected: 18, averageEnjoyment: 1_500);
        Assert.AreEqual(GigRules.BestGig, best);
        var (worst, reasons) = GigRules.Score(GigRules.NoShow, [GigRules.Collapse], peak: 0, setEnd: 0, expected: 18, averageEnjoyment: 0);
        Assert.AreEqual(GigRules.WorstGig, worst);
        Assert.AreEqual("never made it on stage", reasons[0]);
        // A set that died gets nothing for the crowd that waited for it.
        var (dead, _) = GigRules.Score(GigRules.CutShort, [GigRules.PowerCut], peak: 25, setEnd: 0, expected: 18, averageEnjoyment: 0);
        Assert.AreEqual(-GigRules.CutShortPenalty - GigRules.PowerCutPenalty, dead);
    }

    [TestMethod]
    public void EachHiccupLowersTheScore()
    {
        var (clean, _) = GigRules.Score(GigRules.Polite, [], peak: 14, setEnd: 10, expected: 18, averageEnjoyment: 220);
        foreach (var hiccup in GigRules.HiccupOrder)
        {
            var (with, reasons) = GigRules.Score(GigRules.Polite, [hiccup], peak: 14, setEnd: 10, expected: 18, averageEnjoyment: 220);
            Assert.IsTrue(with < clean, $"{hiccup}: {with} vs {clean}");
            Assert.AreEqual(clean - GigRules.Penalty(hiccup), with, hiccup);
            // A gig that still came out ahead gives its good reasons; one that went under names what went wrong.
            if (with < 0) CollectionAssert.Contains(reasons, GigRules.HiccupWord(hiccup).ToLowerInvariant(), hiccup);
        }
        var (cut, _) = GigRules.Score(GigRules.CutShort, [], peak: 14, setEnd: 0, expected: 18, averageEnjoyment: 0);
        var (finished, _) = GigRules.Score(GigRules.EmptyField, [], peak: 14, setEnd: 0, expected: 18, averageEnjoyment: 0);
        Assert.IsTrue(cut < finished, "Being cut short costs more than playing out to nobody.");
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
        SetRelationships(s, new(liked.Id, 100), new(disliked.Id, -100));
        Assert.AreEqual(100, s.ActRelationship(liked.Id));
        Assert.AreEqual(GigRules.Fee(ActCatalogue.Fee(s.Standing, liked), 100), s.ActFee(liked));
        Assert.AreEqual(GigRules.Fee(ActCatalogue.Fee(s.Standing, disliked), -100), s.ActFee(disliked));
        Assert.AreNotEqual(ActCatalogue.Fee(s.Standing, liked), s.ActFee(liked));
        Assert.AreEqual(s.ActFee(liked), s.GetPreparationOffers().Single(offer => offer.Id == liked.Id).PricePennies);
        Send(s, new SetProgrammeCommand([liked.Id, disliked.Id, acts[2].Id]));
        Send(s, new SetProgrammeCommand(acts[3..6].Select(act => act.Id).ToArray()) { StageId = FestivalStages.PondId });
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
    public void ARelationshipOutOfRangeOrAtTierOneIsRefusedOnRestore()
    {
        var s = BuildSession.Started();
        var saved = s.CapturePersistenceSnapshot();
        var tampered = saved with { Preparation = saved.Preparation! with { ActRelationships = [new("act.meadow-lanterns", 5)] } };
        Assert.AreEqual("Act relationships invalid.", GameSession.Restore(tampered).Error, "Tier 1 has no festival before it.");
        var tier2 = NextFestivalTests.Drafted(GameSession.CreateDevelopmentFestival(20260922, 2));
        var two = tier2.CapturePersistenceSnapshot();
        foreach (var bad in new ActRelationship[][] { [new("act.meadow-lanterns", 101)], [new("act.meadow-lanterns", 0)], [new("act.nobody", 4)],
                     [new("act.parish-ceilidh", 4), new("act.meadow-lanterns", 4)] })
            Assert.AreEqual("Act relationships invalid.", GameSession.Restore(two with { Preparation = two.Preparation! with { ActRelationships = bad } }).Error);
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
        Assert.AreEqual(most, record.PeakCrowd);
        Assert.IsTrue(record.PeakCrowd >= record.SetEndCrowd);
        // The record outlives the set: the next set starts its own count.
        while (s.CaptureProgramme()!.CurrentSlot == 0) s.AdvanceWithoutSnapshot(80);
        Assert.AreEqual(0, s.CaptureLivePerformance()!.PeakListeners);
        Assert.AreEqual(most, s.PerformanceRecords[0].PeakCrowd);
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
            [record with { ActId = BuildSession.Acts[1] }],
            [record with { Hiccups = [GigRules.Boos] }],
            [record with { RelationshipBefore = 5, RelationshipAfter = GigRules.Apply(5, record.Delta) }],
            [record, record with { Slot = 1 }],
        ];
        foreach (var records in tampered)
            Assert.AreEqual("Performance records invalid.", GameSession.Restore(saved with { PerformanceRecords = records }).Error);
        Assert.AreEqual("Performance records invalid.", GameSession.Restore(saved with { PerformanceRecords = null }).Error,
            "A finished set must have its record.");
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
        var two = one.CreateNextFestival();
        Assert.AreEqual(0, two.PerformanceRecords.Count, "A new festival starts with no sets played.");
        foreach (var record in records)
        {
            Assert.AreEqual(record.RelationshipAfter, two.ActRelationship(record.ActId), record.ActId);
            var act = ActCatalogue.Find(record.ActId)!;
            Assert.AreEqual(GigRules.Fee(ActCatalogue.Fee(two.Standing, act), record.RelationshipAfter), two.ActFee(act));
            // An act you've history with, who'll play for you, is always on the offer.
            if (record.RelationshipAfter != 0 && two.ActStandingOf(act) != ActStanding.Locked)
                Assert.IsTrue(two.GetFestivalActs().Contains(act), record.ActId);
        }
        Assert.IsTrue(records.Any(record => record.RelationshipAfter != 0), "A real day moves someone.");
        BuildSession.Restored(two);
    }

    private static void NextFestivalTests_RunToEnd(GameSession s)
    {
        for (var i = 0; i < 80 && s.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing; i++) s.AdvanceWithoutSnapshot(1_000);
    }
}

using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class ActCatalogueTests
{
    private static CommandResult Send(GameSession s, SessionCommand c) =>
        s.Execute(new(new(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase, s.CurrentTick, s.NextSubmissionSequence, null, c));

    /// <summary>A fresh festival at reputation 0, past its perk draft.</summary>
    private static GameSession Fresh(ulong seed = 20260922)
    {
        var s = GameSession.CreateBuildCampaign(seed);
        var perks = s.CapturePerks()!;
        Assert.IsTrue(Send(s, new ChoosePerkCommand(perks.DraftAttempt, perks.Cursor, perks.Hand[0])).IsAccepted);
        return s;
    }

    private static FestivalAct Act(string id) => ActCatalogue.Find(id)!;

    [TestMethod]
    public void CatalogueHasUniqueActsInEveryGenreWithLocalsInEach()
    {
        Assert.AreEqual(53, ActCatalogue.All.Length);
        Assert.AreEqual(53, ActCatalogue.All.Select(act => act.Id).Distinct().Count());
        for (var genre = 0; genre < FestivalGenre.Count; genre++)
            Assert.IsTrue(ActCatalogue.All.Count(act => act.Genre == genre && act.Popularity <= ActCatalogue.AvailableReach) >= 2, FestivalGenre.Name(genre));
        Assert.IsTrue(ActCatalogue.All.All(act => act.Popularity is >= 1 and <= 100 && act.PricePennies > 0 && act.Ego is >= 0 and <= 100 && act.Professionalism is >= 0 and <= 100));
    }

    [TestMethod]
    public void ReputationZeroBooksLocalsFreelyStretchesToFiftyAndRefusesTheRest()
    {
        var standing = FestivalStanding.New;
        Assert.AreEqual(ActStanding.Available, ActCatalogue.StandingOf(standing, Act("act.muddy-wellies")));     // 26
        Assert.AreEqual(ActStanding.Available, ActCatalogue.StandingOf(standing, Act("act.tractor-pull")));      // 30
        Assert.AreEqual(ActStanding.Stretch, ActCatalogue.StandingOf(standing, Act("act.meadow-lanterns")));     // 40
        Assert.AreEqual(ActStanding.Stretch, ActCatalogue.StandingOf(standing, Act("act.pitchfork-uprising")));  // 50
        Assert.AreEqual(ActStanding.Locked, ActCatalogue.StandingOf(standing, Act("act.bin-strike")));           // 52
        Assert.AreEqual(ActStanding.Locked, ActCatalogue.StandingOf(standing, Act("act.neon-postcards")));       // 90
        Assert.AreEqual(2200, ActCatalogue.Fee(standing, Act("act.muddy-wellies")));
        Assert.AreEqual(6000, ActCatalogue.Fee(standing, Act("act.meadow-lanterns")), "Stretch bookings cost 1.5×.");
        Assert.AreEqual(40, ActCatalogue.ReputationNeeded(standing, Act("act.neon-postcards")));
        Assert.IsTrue(ActCatalogue.All.All(act => ActCatalogue.StandingOf(FestivalStanding.Established, act) == ActStanding.Available));
    }

    [TestMethod]
    public void SceneCredibilityHelpsItsOwnGenreAndHurtsClashingOnes()
    {
        var pop = FestivalStanding.New with { SceneCredibility = [0, 0, 80, 0, 0, 0] };
        Assert.AreEqual(40, ActCatalogue.EffectiveReputation(pop, Act("act.sugar-tax")), "Own scene adds half.");
        Assert.AreEqual(0, ActCatalogue.EffectiveReputation(pop with { Reputation = 20 }, Act("act.council-tax")), "Pop costs punk a quarter.");
        Assert.AreEqual(20, ActCatalogue.EffectiveReputation(pop with { Reputation = 20 }, Act("act.whittled-spoons")), "Pop is neutral to folk.");
        Assert.AreEqual(ActStanding.Available, ActCatalogue.StandingOf(pop, Act("act.sugar-tax")));
        Assert.AreEqual(ActStanding.Locked, ActCatalogue.StandingOf(pop with { Reputation = 20 }, Act("act.council-tax")));
    }

    [TestMethod]
    public void OfferIsSeededCoversEveryGenreAndShowsFiveNearMisses()
    {
        var a = Fresh(20260922).GetFestivalActs();
        var again = Fresh(20260922).GetFestivalActs();
        CollectionAssert.AreEqual(a.Select(act => act.Id).ToArray(), again.Select(act => act.Id).ToArray());
        var bookable = a.Take(ActCatalogue.ShortlistSize).ToArray();
        Assert.IsTrue(bookable.All(act => ActCatalogue.StandingOf(FestivalStanding.New, act) != ActStanding.Locked));
        for (var genre = 0; genre < FestivalGenre.Count; genre++)
            Assert.IsTrue(bookable.Any(act => act.Genre == genre && ActCatalogue.StandingOf(FestivalStanding.New, act) == ActStanding.Available), FestivalGenre.Name(genre));
        var locked = a.Skip(ActCatalogue.ShortlistSize).ToArray();
        Assert.AreEqual(ActCatalogue.OutOfReachShown, locked.Length);
        Assert.IsTrue(locked.All(act => ActCatalogue.StandingOf(FestivalStanding.New, act) == ActStanding.Locked && act.Popularity <= 60), "Near misses are just out of reach.");
        var other = Enumerable.Range(1, 10).Select(i => string.Join(",", Fresh(20260922 + (ulong)i).GetFestivalActs().Take(10).Select(act => act.Id))).Distinct().Count();
        Assert.IsTrue(other > 1, "Different seeds offer different acts.");
    }

    [TestMethod]
    public void LockedActsAreRefusedAndStretchFeesReachThePlan()
    {
        var s = Fresh();
        var refused = Send(s, new SetProgrammeCommand(["act.neon-postcards", "", ""]));
        Assert.IsFalse(refused.IsAccepted); StringAssert.Contains(refused.Message, "won't play");
        Assert.IsFalse(s.PreviewLineupEdit("act.neon-postcards", -1, 0).IsValid);
        Assert.IsTrue(Send(s, new SetProgrammeCommand(["act.muddy-wellies", "act.meadow-lanterns", "act.doom-fete"])).IsAccepted);
        Assert.AreEqual(6000, s.GetPreparationOffers().Single(offer => offer.Id == "act.meadow-lanterns").PricePennies);
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void ReputationAndBookedScenesMoveTowardTheResult()
    {
        var after = ActCatalogue.AfterFestival(FestivalStanding.New, 4, [FestivalGenre.Folk, FestivalGenre.Folk, FestivalGenre.Punk]);
        Assert.AreEqual(28, after.Reputation);
        CollectionAssert.AreEqual(new[] { 40, 0, 0, 0, 20, 0 }, after.SceneCredibility);
        Assert.AreEqual(after, after with { }, "Standing is a value.");
        var down = ActCatalogue.AfterFestival(new FestivalStanding(60, [60, 0, 0, 0, 0, 0]), 1, [FestivalGenre.Folk]);
        Assert.AreEqual(46, down.Reputation); Assert.AreEqual(50, down.SceneCredibility[FestivalGenre.Folk]);
    }

    [TestMethod]
    public void TicketPriceSetsExpectationsThatShapeEnjoyment()
    {
        var s = Fresh();
        Assert.AreEqual(800, s.TicketPricePennies);
        Assert.AreEqual(24, s.ExpectedPopularity);
        Assert.AreEqual(0, GameSession.ExpectationAdjustment(24, 24));
        Assert.AreEqual(660, GameSession.ExpectationAdjustment(90, 24));
        Assert.AreEqual(800, GameSession.ExpectationAdjustment(100, 0));
        Assert.AreEqual(-800, GameSession.ExpectationAdjustment(0, 100));
        Assert.AreEqual(1000, GameSession.MusicExpectationPermille(24, 24));
        Assert.AreEqual(1300, GameSession.MusicExpectationPermille(90, 24));
        Assert.AreEqual(700, GameSession.MusicExpectationPermille(10, 90));
    }
}

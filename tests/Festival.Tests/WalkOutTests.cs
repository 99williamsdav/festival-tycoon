using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>
/// The walk-out after the close (13 Oct): the day finishes when the last guest is off site. Stewards (the player's
/// own included) and the medic stay on duty until then; whoever is still on site at the finish goes with the results.
/// </summary>
[TestClass]
public sealed class WalkOutTests
{
    /// <summary>The walk-out runs about 2,500-4,000 ticks now guests drift off and stop on the way; well inside the 8,000-tick limit.</summary>
    private const int DepartureBudgetTicks = 4_500;

    private static void Invoke(GameSession s, string name, params object[] args) =>
        typeof(GameSession).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(s, args);

    /// <summary>A Tier 1 day in each way to play, started: Management Only, All-Rounder or Steward Only.</summary>
    private static GameSession Started(string mode, ulong seed)
    {
        if (mode == "steward-only") return GameSession.CreateStewardShift(seed);
        GameSession s;
        if (mode == "all-rounder")
        {
            s = GameSession.CreateHandsOnCampaign(seed);
            var perks = s.CapturePerks()!;
            var redraw = typeof(GameSession).GetMethod("OpenPerkDraft", BindingFlags.NonPublic | BindingFlags.Instance)!;
            string[] quiet = ["extra-pair-of-hands", "doctors-orders"];
            for (var draws = 0; !perks.Hand.Any(quiet.Contains); draws++)
            {
                Assert.IsTrue(draws < 400);
                redraw.Invoke(s, []); perks = s.CapturePerks()!;
            }
            BuildSession.Accept(s, new ChoosePerkCommand(perks.DraftAttempt, perks.Cursor, perks.Hand.First(quiet.Contains)));
            BuildSession.Accept(s, new UseDefaultBuildLayoutCommand());
            BuildSession.Accept(s, new SetProgrammeCommand(BuildSession.Acts));
            BuildSession.Accept(s, new SetPreparationStockCommand(40, 32));
            foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        }
        else
        {
            s = BuildSession.Planned(seed);
            foreach (var id in BuildSession.CrewIds(s).Append("equipment.rent")) BuildSession.Accept(s, new AcceptPreparationOfferCommand(id));
        }
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        return s;
    }

    private static long RunToClose(GameSession s)
    {
        s.AdvanceWithoutSnapshot((int)(s.CapturePreparation()!.StartedTick + s.PreparedEditionDurationTicks - s.CurrentTick));
        Assert.AreEqual(PreparationStatus.Departing, s.PreparedStatus);
        return s.CurrentTick;
    }

    private static bool GuestsOnSite(GameSession s) =>
        s.CapturePreparation()!.People.Any(person => person.Role == ProtectedPersonRole.Guest && !person.Departed);

    [TestCategory("Slow")]
    [TestMethod]
    [DataRow("management-only")]
    [DataRow("all-rounder")]
    [DataRow("steward-only")]
    public void StewardsAndTheMedicStayUntilTheLastGuestIsOut(string mode)
    {
        var s = Started(mode, 20260922);
        var player = mode == "all-rounder" ? s.HandsOnStewardId : s.PlayerStewardId;
        if (mode != "management-only") Assert.IsNotNull(player);
        var close = RunToClose(s);
        var stewards = s.GetStewardResponses().Select(job => job.WorkerId).ToArray();
        var onDuty = stewards.Concat(s.GetMedicResponses().Select(job => job.WorkerId)).Concat(player is { } me ? [me] : []).Distinct().ToArray();
        if (mode == "steward-only") CollectionAssert.Contains(stewards, player!.Value, "Steward Only's player is one of the hired stewards.");
        Assert.IsTrue(onDuty.Length >= 2);
        if (player is not null)
        {
            // The shift itself still ends at the close: control goes back, the report reads the close.
            var shift = s.CaptureStewardShift()!;
            Assert.IsNull(shift.PlayerId);
            Assert.AreEqual(close, shift.EndedTick);
            Assert.IsNotNull(shift.ClosingMood);
        }
        var standing = player is { } p0 ? s.CaptureSnapshot().NavigationAgents.Single(n => n.Id.Value == p0).Destination : null;
        while (s.CurrentTick - close < DepartureBudgetTicks && s.PreparedStatus == PreparationStatus.Departing)
        {
            s.AdvanceWithoutSnapshot(1);
            if (s.PreparedStatus != PreparationStatus.Departing) break;
            var people = s.CapturePreparation()!.People;
            var navs = s.CaptureSnapshot().NavigationAgents;
            foreach (var id in onDuty)
            {
                Assert.IsFalse(people.Single(person => person.AgentId == id).Departed, $"Staff {id} left at +{s.CurrentTick - close} with guests still on site.");
                var intent = navs.Single(n => n.Id.Value == id).IntentId;
                Assert.IsTrue(intent is "disorder.departure-standby" or "medical.departure-standby", $"Staff {id} is on '{intent}' at +{s.CurrentTick - close}.");
            }
            // The player's own steward stands where control went back.
            if (mode == "all-rounder" && s.CurrentTick - close > 1)
                Assert.AreEqual(standing ??= navs.Single(n => n.Id.Value == player!.Value).Destination, navs.Single(n => n.Id.Value == player!.Value).Destination);
        }
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus);
        Assert.IsTrue(s.CapturePreparation()!.People.All(person => person.Departed));
        Assert.AreEqual(close, s.CaptureStewardShift()?.EndedTick ?? close);
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
    }

    /// <summary>Walks out until every guest is off site, on the tick before the day would finish.</summary>
    private static void UntilTheLastGuestIsOut(GameSession s, long close)
    {
        while (GuestsOnSite(s))
        {
            Assert.IsTrue(s.CurrentTick - close < DepartureBudgetTicks);
            s.AdvanceWithoutSnapshot(1);
        }
        Assert.AreEqual(PreparationStatus.Departing, s.PreparedStatus);
        Assert.IsTrue(s.PreparationBoundaryOnNextTick, "With nobody down, the day finishes on the next tick.");
    }

    [TestCategory("Slow")]
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void AStewardDownAtTheFinishHoldsTheDayOpenUntilTreatedOrDead(bool treated)
    {
        var s = Started("management-only", 2);
        var close = RunToClose(s);
        UntilTheLastGuestIsOut(s, close);
        var steward = s.GetStewardResponses()[0].WorkerId;
        var tick = s.CurrentTick;
        // Down at their post as the last guest goes: with time for the medic to come, or past saving.
        Invoke(s, "UpdatePerson", steward, (Func<Person, Person>)(need => treated
            ? need with { HealthStage = MedicalStage.Collapsed, Intent = MedicalIntent.Collapsed, HealthWarningTick = tick, HealthCollapseTick = tick }
            : need with { HealthStage = MedicalStage.Critical, Intent = MedicalIntent.Collapsed, HealthWarningTick = tick - 4_000,
                HealthCollapseTick = tick - GameSession.MedicalDeathDelayTicks + 40, HealthCriticalTick = tick - 1 }));
        Assert.IsFalse(s.PreparationBoundaryOnNextTick, "A steward down holds the day open.");
        for (var step = 0; step < DepartureBudgetTicks && s.PreparedStatus == PreparationStatus.Departing; step++)
        {
            s.AdvanceWithoutSnapshot(1);
            if (s.PreparedStatus == PreparationStatus.Finished) break;
            Assert.IsFalse(s.CapturePreparation()!.People.Single(person => person.AgentId == steward).Departed);
        }
        if (treated)
        {
            Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus);
            Assert.AreEqual(MedicalStage.Treated, s.CapturePerson(steward)!.HealthStage);
            Assert.IsTrue(s.CurrentTick > tick + 80, "The day waited for the medic.");
            var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
            Assert.IsTrue(restored.IsSuccess, restored.Error);
        }
        else
        {
            Assert.AreEqual(PreparationStatus.Failed, s.PreparedStatus, "A death on the way out fails the edition, as at any other time.");
            Assert.IsNull(s.CompletedFestivalResult);
        }
    }

    [TestCategory("Slow")]
    [TestMethod]
    public void StaffInALooAtTheFinishGiveItUp()
    {
        var s = Started("management-only", 2);
        var close = RunToClose(s);
        UntilTheLastGuestIsOut(s, close);
        var steward = s.GetStewardResponses()[0].WorkerId;
        var medic = s.GetMedicResponses()[0].WorkerId;
        var toilets = (ToiletFacility[])typeof(GameSession).GetProperty("Toilets", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
        Invoke(s, "SetToilets", (object)toilets.Select((toilet, index) => index == 0 ? toilet with { OwnerId = steward, DoorOpen = false, Queue = [medic] } : toilet).ToArray());
        s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus);
        var after = (ToiletFacility[])typeof(GameSession).GetProperty("Toilets", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
        Assert.IsTrue(after.All(toilet => toilet.OwnerId is null && toilet.Queue.Length == 0));
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
    }

    /// <summary>
    /// The user (13 Oct): "Feels a bit abrupt when everyone leaves after the music stops - we could maybe have people move out
    /// slowly. Different speeds, some stopping on the way to the toilet or water." Guests linger a keyed while, walk home at
    /// their own pace, some by way of a loo or a tap, and the last is off site 2,500-4,000 ticks after the close.
    /// </summary>
    [TestCategory("Slow")]
    [TestMethod]
    public void GuestsDriftOutSomeByWayOfALooOrATapAndTheDayFinishesInBound()
    {
        var s = Started("management-only", 20260922);
        // Dav empties the loos before the headliner, as a player would: a default day fills both, and a full loo takes nobody.
        var walk = WalkOutProbe.Watch(s, callDav: true);
        Assert.AreEqual(PreparationStatus.Finished, walk.Status);
        Assert.IsTrue(walk.LastOut is >= 2_500 and <= 4_000, $"Last guest out at +{walk.LastOut}.");
        Assert.AreEqual(walk.LastOut + 1, walk.Finished, "The day finishes the tick after the last guest is off site.");
        // Staggered: not everyone sets off on the last note, and the last to go waited a good while.
        Assert.IsTrue(walk.SetOffs.Count(tick => tick <= 1) < walk.Guests / 3, $"Set off at once: {walk.SetOffs.Count(tick => tick <= 1)} of {walk.Guests}.");
        Assert.IsTrue(walk.SetOffs.Max() >= 500, $"Latest set-off +{walk.SetOffs.Max()}.");
        Assert.IsTrue(walk.SetOffs.Distinct().Count() >= walk.Guests / 2, "Each in their own time.");
        Assert.IsTrue(walk.HalfwayTick >= 1_000, $"Half the guests gone by +{walk.HalfwayTick}.");
        // Some stop on the way: the loos and the taps stay open for the walk-out.
        Assert.IsTrue(walk.LooStops >= 2, $"{walk.LooStops} stopped at a loo.");
        Assert.IsTrue(walk.WaterStops >= 1, $"{walk.WaterStops} stopped for water.");
        Assert.IsTrue(s.CapturePreparation()!.People.All(person => person.Departed));
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
    }

    [TestMethod]
    public void LingeringPaceAndStopsAreKeyedAndBounded()
    {
        var lingers = Enumerable.Range(1, 400).Select(id => GameSession.DepartureLingerTicks(20260922, (ulong)id)).ToArray();
        Assert.IsTrue(lingers.All(t => t is >= 0 and <= GameSession.DepartureLingerMaxTicks));
        Assert.IsTrue(lingers.Average() is > 200 and < 340, $"Mean linger {lingers.Average():0}: most go early, a few late.");
        Assert.IsTrue(lingers.Count(t => t > 600) is > 20 and < 120, "A few hang on to the end of the window.");
        CollectionAssert.AreEqual(lingers, Enumerable.Range(1, 400).Select(id => GameSession.DepartureLingerTicks(20260922, (ulong)id)).ToArray(), "Keyed: the same every time.");
        CollectionAssert.AreNotEqual(lingers, Enumerable.Range(1, 400).Select(id => GameSession.DepartureLingerTicks(20260923, (ulong)id)).ToArray(), "And by seed.");
        var paces = Enumerable.Range(1, 400).Select(id => GameSession.DeparturePacePermille(20260922, (ulong)id)).ToArray();
        Assert.IsTrue(paces.All(p => p is >= 900 and <= 1_000) && paces.Distinct().Count() > 80);
        // Stops follow need: nobody comfortable stops, and the bursting mostly do.
        Assert.AreEqual(0, GameSession.DepartureToiletChancePercent(3_000));
        Assert.AreEqual(30, GameSession.DepartureToiletChancePercent(9_000));
        Assert.AreEqual(0, GameSession.DepartureWaterChancePercent(3_000));
        Assert.AreEqual(30, GameSession.DepartureWaterChancePercent(9_000));
        var ids = Enumerable.Range(1, 400).Select(id => (ulong)id).ToArray();
        Assert.IsTrue(ids.All(id => GameSession.DepartureStop(20260922, id, 1_000, 1_000) is null));
        var bursting = ids.Count(id => GameSession.DepartureStop(20260922, id, 9_000, 1_000) == ActivityKind.Toilet);
        Assert.IsTrue(bursting is > 80 and < 160, $"{bursting} of 400 bursting guests stop at a loo.");
        var parched = ids.Count(id => GameSession.DepartureStop(20260922, id, 1_000, 9_000) == ActivityKind.Water);
        Assert.IsTrue(parched is > 80 and < 160, $"{parched} of 400 parched guests stop for water.");
    }

    [TestCategory("Slow")]
    [TestMethod]
    public void AWalkOutSavedHalfwayWithGuestsAtTheLoosAndTapsRestoresAndFinishesTheSame()
    {
        var s = Started("management-only", 20260922);
        // Dav empties the loos before the headliner (a default day fills both, and a full loo takes nobody).
        s.AdvanceWithoutSnapshot((int)(s.CapturePreparation()!.StartedTick + s.PreparedEditionDurationTicks - 4_800 - s.CurrentTick));
        foreach (var toilet in s.CaptureToilets().Where(t => s.LavSuckerUnavailable(t.Id) is null)) BuildSession.Accept(s, new CallLavSuckerCommand(toilet.Id));
        var close = RunToClose(s);
        // Partway out, with someone in a loo line and someone at a tap on the way, both at once (on this seed they are, for
        // 544 ticks of the walk-out, 14 Oct).
        GameSession? saved = null;
        while (s.PreparedStatus == PreparationStatus.Departing && saved is null)
        {
            s.AdvanceWithoutSnapshot(1);
            var people = s.CapturePreparation()!.People.Where(person => person.Role == ProtectedPersonRole.Guest && !person.Departed).Select(person => s.CapturePerson(person.AgentId)!).ToArray();
            if (s.CurrentTick - close >= 400 && people.Any(person => person.ToiletStage is ToiletVisitStage.Queued or ToiletVisitStage.Using) &&
                people.Any(person => person.Intent is MedicalIntent.SeekWater or MedicalIntent.Drinking))
            {
                var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
                Assert.IsTrue(restored.IsSuccess, restored.Error);
                Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
                saved = restored.Session;
            }
        }
        Assert.IsNotNull(saved, "The walk-out had people at a loo and a tap at once.");
        while (s.PreparedStatus == PreparationStatus.Departing) s.AdvanceWithoutSnapshot(1);
        while (saved.PreparedStatus == PreparationStatus.Departing) saved.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(PreparationStatus.Finished, saved.PreparedStatus);
        Assert.AreEqual(s.CurrentTick, saved.CurrentTick);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, saved.CaptureSnapshot().AuthoritativeHash, "The restored walk-out plays out the same.");
        Assert.IsTrue(s.CurrentTick - close < DepartureBudgetTicks);
    }

    [TestMethod]
    public void RestoreRefusesSomeoneOffSiteWhoIsStillDown()
    {
        var s = Started("management-only", 2);
        s.AdvanceWithoutSnapshot(2_000);
        var saved = s.CapturePersistenceSnapshot();
        var guest = saved.Preparation!.People.First(person => person.Role == ProtectedPersonRole.Guest && person.Admitted).AgentId;
        var forged = saved with
        {
            Preparation = saved.Preparation with { People = saved.Preparation.People.Select(person => person.AgentId == guest ? person with { Departed = true } : person).ToArray() },
            Medical = saved.Medical! with { Needs = saved.Medical.Needs.Select(need => need.AgentId == guest ? need with { Stage = MedicalStage.Collapsed, CollapseTick = s.CurrentTick } : need).ToArray() },
        };
        var result = GameSession.Restore(forged);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("A person off site is still collapsed, critical or injured.", result.Error);
    }

    [TestCategory("Slow")]
    [TestMethod]
    public void AGuestLeftWithNoRouteTriesAgainAndAStuckOneIsSentHomeAtTheLimit()
    {
        var s = Started("management-only", 2);
        var close = RunToClose(s);
        var guests = s.CapturePreparation()!.People.Where(person => person.Role == ProtectedPersonRole.Guest && !person.Departed).Select(person => person.AgentId).ToArray();
        var (lost, stuck) = (guests[0], guests[^1]);
        var agents = (System.Collections.IDictionary)typeof(GameSession).GetField("_navigationAgents", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
        void NoRoute(ulong id)
        {
            var nav = agents[new EntityId(id)]!;
            nav.GetType().GetProperty("Action")!.SetValue(nav, AgentNavigationAction.NoRoute);
            nav.GetType().GetProperty("Route")!.SetValue(nav, new List<GridCell>());
            nav.GetType().GetProperty("RouteIndex")!.SetValue(nav, 0);
        }
        // One guest loses their route once and must find it again; another keeps losing it every tick.
        s.AdvanceWithoutSnapshot(1);
        NoRoute(lost);
        long lostOut = -1;
        while (s.PreparedStatus == PreparationStatus.Departing && s.CurrentTick - close <= GameSession.DepartureCapTicks + 1)
        {
            NoRoute(stuck);
            s.AdvanceWithoutSnapshot(1);
            if (lostOut < 0 && s.CapturePreparation()!.People.Single(person => person.AgentId == lost).Departed) lostOut = s.CurrentTick;
            if (s.PreparedStatus == PreparationStatus.Departing)
                Assert.IsFalse(s.CapturePreparation()!.People.Single(person => person.AgentId == stuck).Departed);
        }
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus);
        Assert.AreEqual(close + GameSession.DepartureCapTicks, s.CurrentTick, "The walk-out's limit sends the stuck guest home.");
        Assert.AreEqual(FestivalTickets.Sold(1), s.CompletedFestivalResult!.GuestCount);
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.IsTrue(lostOut > 0 && lostOut - close < DepartureBudgetTicks, $"The guest left with no route should find it again and walk out; out at {lostOut - close}.");
    }

    private static void ForceNoRoute(GameSession s, ulong id)
    {
        var agents = (System.Collections.IDictionary)typeof(GameSession).GetField("_navigationAgents", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
        var nav = agents[new EntityId(id)]!;
        nav.GetType().GetProperty("Action")!.SetValue(nav, AgentNavigationAction.NoRoute);
        nav.GetType().GetProperty("Route")!.SetValue(nav, new List<GridCell>());
        nav.GetType().GetProperty("RouteIndex")!.SetValue(nav, 0);
    }

    private static string? IntentOf(GameSession s, ulong id) => s.CaptureSnapshot().NavigationAgents.Single(n => n.Id.Value == id).IntentId;

    /// <summary>
    /// Reviewer (14 Oct): a guest leaving by way of a loo or a tap whose approach ends in no route was never handed back to
    /// the walk home, and held the day open to the 8,000-tick limit. Now one left with no route on the way to a loo or a
    /// tap gives it up on the next tick and heads for the gate, and so does one still on the way after
    /// <see cref="GameSession.DepartureStopApproachTicks"/>; the walk-out finishes in bound and the save restores.
    /// </summary>
    [TestCategory("Slow")]
    [TestMethod]
    public void AGuestWhoCantReachTheirLooOrTapOnTheWayOutGoesHomeInstead()
    {
        var s = Started("management-only", 20260922);
        s.AdvanceWithoutSnapshot((int)(s.CapturePreparation()!.StartedTick + s.PreparedEditionDurationTicks - 4_800 - s.CurrentTick));
        foreach (var toilet in s.CaptureToilets().Where(t => s.LavSuckerUnavailable(t.Id) is null)) BuildSession.Accept(s, new CallLavSuckerCommand(toilet.Id));
        var close = RunToClose(s);
        ulong? lostToLoo = null, lostToTap = null, slowToTap = null;
        while (s.PreparedStatus == PreparationStatus.Departing)
        {
            Assert.IsTrue(s.CurrentTick - close < DepartureBudgetTicks, "The walk-out finishes in bound.");
            s.AdvanceWithoutSnapshot(1);
            var guests = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && !p.Departed).Select(p => s.CapturePerson(p.AgentId)!).ToArray();
            if (lostToLoo is null && guests.FirstOrDefault(p => p.ToiletStage == ToiletVisitStage.Approaching) is { } loo)
            {
                lostToLoo = loo.Id;
                ForceNoRoute(s, loo.Id);
                s.AdvanceWithoutSnapshot(1);
                Assert.AreEqual(ToiletVisitStage.None, s.CapturePerson(loo.Id)!.ToiletStage, "No route to the loo: they give it up.");
                Assert.AreEqual("edition.departure", IntentOf(s, loo.Id), "And head for the gate.");
                Assert.IsFalse(s.CaptureToilets().Any(t => t.Queue.Contains(loo.Id) || t.OwnerId == loo.Id));
            }
            else if (guests.FirstOrDefault(p => p is { Intent: MedicalIntent.SeekWater, WaterQueueSlot: null } &&
                         !s.CaptureWaterPoints().Any(w => w.Overflow.Contains(p.Id))) is { } tap && (lostToTap is null || slowToTap is null && tap.Id != lostToTap))
            {
                if (lostToTap is null)
                {
                    lostToTap = tap.Id;
                    ForceNoRoute(s, tap.Id);
                }
                else
                {
                    // Still on the way after the limit (forced: real approaches take far less).
                    slowToTap = tap.Id;
                    Invoke(s, "UpdatePerson", tap.Id, (Func<Person, Person>)(p => p with { LastWaterChoiceReviewTick = s.CurrentTick - GameSession.DepartureStopApproachTicks + 1 }));
                }
                s.AdvanceWithoutSnapshot(1);
                Assert.AreEqual(MedicalIntent.Leaving, s.CapturePerson(tap.Id)!.Intent, "They give up the tap.");
                Assert.AreEqual("edition.departure", IntentOf(s, tap.Id), "And head for the gate.");
            }
        }
        Assert.IsNotNull(lostToLoo, "Someone set off for a loo on the way out.");
        Assert.IsNotNull(lostToTap, "Someone set off for a tap on the way out.");
        Assert.IsNotNull(slowToTap, "A second guest set off for a tap on the way out.");
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus);
        Assert.IsTrue(s.CurrentTick - close < DepartureBudgetTicks, $"Finished at +{s.CurrentTick - close}.");
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
    }

    /// <summary>
    /// A quick drink on the way out ends after <see cref="GameSession.DepartureDrinkTicks"/>: the tick it ends on is a
    /// medical boundary (flagged the tick before), and the tick before that is not flagged for it (14 Oct: it was one early).
    /// </summary>
    [TestCategory("Slow")]
    [TestMethod]
    public void AQuickDrinkOnTheWayOutIsABoundaryOnTheTickItEnds()
    {
        var s = Started("management-only", 20260922);
        var close = RunToClose(s);
        var checkedDrinks = 0;
        while (s.PreparedStatus == PreparationStatus.Departing && checkedDrinks < 2)
        {
            Assert.IsTrue(s.CurrentTick - close < DepartureBudgetTicks);
            var drinking = s.CaptureWaterPoints().FirstOrDefault(w => w.OwnerId is { } id && s.CapturePerson(id)!.Thirst > 200 &&
                w.DrinkTicks == GameSession.DepartureDrinkTicks - 2);
            if (drinking is null) { s.AdvanceWithoutSnapshot(1); continue; }
            var drinker = drinking.OwnerId!.Value;
            // Two ticks out: the next tick isn't the end, so not flagged for this drink (nothing else is due either).
            Assert.IsFalse(s.MedicalBoundaryOnNextTick, $"Flagged a tick early at +{s.CurrentTick - close}.");
            s.AdvanceWithoutSnapshot(1);
            Assert.AreEqual(drinker, s.CaptureWaterPoints().Single(w => w.Id == drinking.Id).OwnerId, "Still drinking.");
            Assert.IsTrue(s.MedicalBoundaryOnNextTick, "The tick the drink ends is a boundary.");
            s.AdvanceWithoutSnapshot(1);
            Assert.AreNotEqual(drinker, s.CaptureWaterPoints().Single(w => w.Id == drinking.Id).OwnerId, "A quick one, then on they go.");
            checkedDrinks++;
        }
        Assert.IsTrue(checkedDrinks >= 1, "Someone had a quick drink on the way out.");
    }

    [TestCategory("Slow")]
    [TestMethod]
    public void AGuestCollapsingOnTheWayOutHoldsTheDayOpenUntilTheyAreSeenTo()
    {
        var s = Started("management-only", 2);
        var close = RunToClose(s);
        // Late in the walk-out, with only a few guests left: one of them goes down.
        while (s.CapturePreparation()!.People.Count(person => person.Role == ProtectedPersonRole.Guest && !person.Departed) > 3)
        {
            Assert.IsTrue(s.CurrentTick - close < DepartureBudgetTicks);
            s.AdvanceWithoutSnapshot(1);
        }
        var guest = s.CapturePreparation()!.People.Last(person => person.Role == ProtectedPersonRole.Guest && !person.Departed).AgentId;
        Invoke(s, "UpdatePerson", guest, (Func<Person, Person>)(need => need with
            { HealthStage = MedicalStage.Distress, HealthWarningTick = s.CurrentTick - GameSession.MedicalCollapseDelayTicks }));
        var collapses = s.CapturePreparation()!.GuestMedicalCollapses;
        s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalStage.Collapsed, s.CapturePerson(guest)!.HealthStage);
        Assert.AreEqual(collapses + 1, s.CapturePreparation()!.GuestMedicalCollapses);
        var medicCame = false;
        var othersOutWhileDown = false;
        long guestOut = -1;
        for (var tick = 0; tick < DepartureBudgetTicks && s.PreparedStatus == PreparationStatus.Departing; tick++)
        {
            s.AdvanceWithoutSnapshot(1);
            medicCame |= s.GetMedicResponses().Any(job => job.PatientId == guest);
            var people = s.CapturePreparation()!.People;
            var down = !people.Single(person => person.AgentId == guest).Departed;
            othersOutWhileDown |= down && people.All(person => person.Role != ProtectedPersonRole.Guest || person.AgentId == guest || person.Departed);
            if (down) Assert.AreEqual(PreparationStatus.Departing, s.PreparedStatus, "The day can't finish with a guest down on site.");
            else if (guestOut < 0) guestOut = s.CurrentTick;
        }
        Assert.IsTrue(medicCame, "The medic should come to the collapsed guest.");
        Assert.IsTrue(othersOutWhileDown, "The other guests should be out while this one was still being seen to.");
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus);
        Assert.IsTrue(s.CapturePerson(guest)!.HealthStage is MedicalStage.Treated or MedicalStage.Removed, $"{s.CapturePerson(guest)!.HealthStage}");
        Assert.AreEqual(guestOut + 1, s.CurrentTick, "The day finishes the tick after the last guest is off site.");
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
    }
}

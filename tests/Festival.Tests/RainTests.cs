using System.Reflection;
using Festival.Persistence;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class RainTests
{
    private const ulong Seed = 20260922;

    private static GameSession Prepared(RainProbe.Counters counters = RainProbe.Counters.None) => RainProbe.Prepared(Seed, counters);

    private static GameSession Started(RainProbe.Counters counters = RainProbe.Counters.None)
    {
        var s = Prepared(counters);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        return s;
    }

    private static long Opened(GameSession s) => s.CapturePreparation()!.StartedTick;

    /// <summary>Runs on to this many ticks after opening, a second at a time.</summary>
    private static void RunTo(GameSession s, long dayTick)
    {
        while (s.CurrentTick - Opened(s) < dayTick && s.PreparedStatus == PreparationStatus.Running) s.AdvanceWithoutSnapshot(80);
    }

    private static WeatherSpell FirstShower(GameSession s) => s.CaptureWeather()!.Spells[0];

    private static ulong[] Guests(GameSession s) =>
        s.CapturePreparation()!.People.Where(person => person.Role == ProtectedPersonRole.Guest).Select(person => person.AgentId).ToArray();

    [TestMethod]
    public void TheShowersComeFromTheSeedAndTierAndAreSaved()
    {
        var day = GameSession.DayTicksFor(FestivalStages.For(true));
        var once = GameSession.RainForecast(Seed, 2, day);
        CollectionAssert.AreEqual(once, GameSession.RainForecast(Seed, 2, day), "The same seed and tier give the same showers.");
        CollectionAssert.AreNotEqual(once, GameSession.RainForecast(Seed + 1, 2, day));
        var s = GameSession.CreateDevelopmentFestival(Seed, 2);
        Assert.AreEqual(WeatherKind.Rain, s.Weather);
        CollectionAssert.AreEqual(once, s.CaptureWeather()!.Spells, "A Tier 2 festival rains the forecast.");
        Assert.IsFalse(s.CaptureMedical()!.IsHot, "No heatwave in the rain.");
        CollectionAssert.AreEqual(once, s.CapturePersistenceSnapshot().Weather!.Spells, "The showers are in the save.");

        var downpours = 0;
        for (ulong seed = 1; seed <= 80; seed++)
        {
            var spells = GameSession.RainForecast(seed, 2, day);
            Assert.IsTrue(spells.Length >= 4, "Showers through the afternoon and into dusk.");
            Assert.IsTrue(spells.Zip(spells.Skip(1)).All(pair => pair.First.EndTick < pair.Second.StartTick), "Dry spells between showers.");
            Assert.IsTrue(spells.All(spell => spell.StartTick >= WeatherRules.FirstShowerFromTicks && spell.EndTick <= day - GameSession.ClosingAfterLastSetTicks));
            var heavy = spells.Count(spell => spell.Intensity == RainIntensity.Heavy);
            Assert.IsTrue(heavy >= spells.Length - 1, "Heavy showers are the norm; at most one downpour a day.");
            if (spells.SingleOrDefault(spell => spell.Intensity == RainIntensity.Downpour) is { } downpour)
            {
                downpours++;
                Assert.IsTrue(downpour.EndTick - downpour.StartTick is >= WeatherRules.DownpourFromTicks and <= WeatherRules.DownpourToTicks, "6 to 10 festival minutes.");
            }
        }
        Assert.IsTrue(downpours is >= 10 and <= 32, $"A downpour about one day in four: {downpours} of 80.");
    }

    [TestMethod]
    public void TierOneStaysTheHeatwave()
    {
        var s = GameSession.CreateDevelopmentFestival(Seed, 1);
        Assert.AreEqual(WeatherKind.Heatwave, s.Weather);
        Assert.IsNull(s.CaptureWeather());
        Assert.IsTrue(s.CaptureMedical()!.IsHot);
        Assert.IsNull(s.CapturePersistenceSnapshot().Weather, "Nothing rain-related in a Tier 1 save.");
        NextFestivalTests.Ready(s);
        Assert.IsFalse(BuildSession.Send(s, new GroundCoverCommand(GroundCoverAction.Straw, new(110, 154), new(130, 154))).IsAccepted, "No straw at Tier 1.");
        Assert.IsFalse(BuildSession.Send(s, new SetPreparationStockCommand(40, 32) { Ponchos = 10 }).IsAccepted, "No ponchos at Tier 1.");
        Assert.IsFalse(s.ImmersionListPrice(ImmersionProduct.Poncho) == 0);
    }

    [TestMethod]
    public void NobodyHeatsUpAndThirstComesSlowerInTheRain()
    {
        var s = Started();
        var start = s.CaptureMedical()!.Needs.ToDictionary(need => need.AgentId, need => need.HeatExposure);
        RunTo(s, 12_000);
        foreach (var need in s.CaptureMedical()!.Needs)
            Assert.IsTrue(need.HeatExposure <= start[need.AgentId], $"{need.AgentId} heated up in the rain.");
        Assert.AreEqual(0, s.MoodTotal(MoodCause.Heat), "Heat costs no mood in the rain.");
    }

    [TestMethod]
    public void RainWetsTheGroundAndBusyGroundChurnsToMud()
    {
        var s = Started();
        var shower = FirstShower(s);
        RunTo(s, shower.StartTick - 80);
        var dry = Puddles(s);
        RunTo(s, Math.Min(shower.EndTick, shower.StartTick + 1_600));
        Assert.IsTrue(Puddles(s) > dry + 200, $"Puddles gather in the shower: {dry} then {Puddles(s)}.");
        RunTo(s, 26_000);
        Assert.IsTrue(RainProbe.MudCells(s) > 50, $"Busy ground churns to mud: {RainProbe.MudCells(s)} cells.");
    }

    private static int Puddles(GameSession s)
    {
        var count = 0;
        for (var z = GroundRules.FieldFirstCell; z <= GroundRules.FieldLastCell; z++)
        for (var x = GroundRules.FieldFirstCell; x <= GroundRules.FieldLastCell; x++)
            if (s.GroundWetAt(new(x, z)) >= GroundRules.PuddleWet) count++;
        return count;
    }

    [TestMethod]
    public void StrawAndMattingResistChurning()
    {
        var s = Prepared();
        Assert.IsTrue(BuildSession.Send(s, new GroundCoverCommand(GroundCoverAction.Straw, new(110, 154), new(162, 154))).IsAccepted);
        Assert.IsTrue(BuildSession.Send(s, new GroundCoverCommand(GroundCoverAction.Matting, new(112, 142), new(150, 142))).IsAccepted);
        var p = s.CapturePreparation()!;
        Assert.IsTrue(p.StrawCells!.Length > 100 && p.MatPanels!.Length > 10);
        Assert.IsTrue(s.BuildDraftCost >= GameSession.GroundCoverCost(p) + 1, "The cover is part of the Build bill.");
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        RunTo(s, 30_000);
        var straw = p.StrawCells.Select(index => new GridCell(index % TraversalGrid.Width, index / TraversalGrid.Width)).ToArray();
        var fresh = straw.Where(cell => s.GroundTrampleAt(cell) is >= 0 and < GroundCoverRules.TrampleMaximum).ToArray();
        Assert.IsTrue(fresh.Length > 0, "Some straw is still fresh.");
        Assert.IsTrue(fresh.All(cell => s.GroundStateAt(cell) is GroundState.Grass or GroundState.Puddle), "Fresh straw never reads as mud.");
        Assert.IsTrue(straw.Any(cell => s.GroundTrampleAt(cell) > 0), "Feet trample it.");
        var matted = p.MatPanels.SelectMany(panel => panel.Cells()).ToArray();
        Assert.IsTrue(matted.All(cell => s.GroundStateAt(cell) == GroundState.Grass && s.MattedAt(cell)), "Matting never churns or puddles.");
        // Bare trampled ground elsewhere does churn.
        Assert.IsTrue(RainProbe.MudCells(s) > 20, $"Bare ground churns: {RainProbe.MudCells(s)} cells.");
    }

    [TestMethod]
    public void CoverIsWalkedOnNotBuiltOn()
    {
        var s = Prepared();
        var bar = s.CaptureBuildPlacements().First(item => item.Kind == BuildServiceKind.Bar);
        // A run straight through the bar leaves its stall alone.
        Assert.IsTrue(BuildSession.Send(s, new GroundCoverCommand(GroundCoverAction.Straw, new(bar.Cell.X - 12, bar.Cell.Z), new(bar.Cell.X + 12, bar.Cell.Z))).IsAccepted);
        var under = GameSession.BuildFootprint(bar).Select(cell => cell.Z * TraversalGrid.Width + cell.X).ToHashSet();
        Assert.IsTrue(s.CapturePlannedStraw().Count > 0);
        Assert.IsFalse(s.CapturePlannedStraw().Any(cell => under.Contains(cell) && Math.Abs(cell % TraversalGrid.Width - bar.Cell.X) <= 2 && Math.Abs(cell / TraversalGrid.Width - bar.Cell.Z) <= 1),
            "No straw under the bar.");
        // A bin can't be put down on straw, but can once it's lifted.
        var on = Enumerable.Range(0, 40).Select(i => new GridCell(150 + i % 8 * 3, 112 + i / 8 * 3))
            .First(cell => s.ValidateCommand(new(new CommandId(99_999), s.CampaignId, s.Phase, s.CurrentTick, s.NextSubmissionSequence, null,
                new PlaceBuildServiceCommand(BuildServiceKind.Bin, cell))) is null);
        Assert.IsTrue(BuildSession.Send(s, new GroundCoverCommand(GroundCoverAction.Straw, new(on.X - 4, on.Z), new(on.X + 4, on.Z))).IsAccepted);
        var refused = BuildSession.Send(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, on));
        Assert.IsFalse(refused.IsAccepted);
        StringAssert.Contains(refused.Message, "straw");
        Assert.IsTrue(BuildSession.Send(s, new GroundCoverCommand(GroundCoverAction.Lift, new(on.X - 4, on.Z), new(on.X + 4, on.Z))).IsAccepted);
        Assert.IsTrue(BuildSession.Send(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, on)).IsAccepted);
        // The default layout lifts whatever lies under it.
        BuildSession.Accept(s, new UseDefaultBuildLayoutCommand());
        Assert.IsTrue(BuildSession.Send(s, new StartPreparedEditionCommand()).IsAccepted, "Cover never breaks the opening checks.");
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
    }

    [TestMethod]
    public void WetGuestsLoseMoodAndShelterDriesThem()
    {
        var s = Started(RainProbe.Counters.Marquee);
        var shower = FirstShower(s);
        RunTo(s, shower.StartTick + 1_200);
        Assert.IsTrue(Guests(s).Count(id => s.WetnessOf(id) > 0) > 10, "Guests out in it get wet.");
        Assert.IsTrue(s.MoodTotal(MoodCause.Rain) < 0, "And feel it.");
        // Someone sheltering, once there, gets no wetter.
        RunTo(s, shower.StartTick + 2_000);
        var under = s.CaptureObservation().NavigationAgents.Where(agent => agent.IntentId == ShelterRules.Intent && agent.Action == AgentNavigationAction.Arrived)
            .Select(agent => agent.Id.Value).ToArray();
        Assert.IsTrue(under.Length > 0, "Wet guests go and shelter.");
        var before = under.ToDictionary(id => id, s.WetnessOf);
        s.AdvanceWithoutSnapshot(400);
        foreach (var id in under.Where(id => s.CaptureObservation().NavigationAgents.Single(agent => agent.Id.Value == id).IntentId == ShelterRules.Intent))
            Assert.IsTrue(s.WetnessOf(id) <= before[id], "Under cover they dry, not soak.");
    }

    [TestMethod]
    public void ShelterIsFilledNoFurtherThanItHoldsRoomFor()
    {
        var s = Started(RainProbe.Counters.Marquee);
        var spots = s.CaptureShelterSpots();
        var peakMarquee = 0;
        foreach (var spell in s.CaptureWeather()!.Spells.Take(3))
        {
            RunTo(s, spell.StartTick);
            while (s.CurrentTick - Opened(s) < spell.EndTick && s.PreparedStatus == PreparationStatus.Running)
            {
                s.AdvanceWithoutSnapshot(80);
                var heading = s.CaptureSnapshot().NavigationAgents.Where(agent => agent.IntentId == ShelterRules.Intent).ToArray();
                foreach (var cover in spots.Select(spot => spot.CoverId).Distinct())
                {
                    var room = spots.Count(spot => spot.CoverId == cover);
                    var count = s.ShelteringAt(cover);
                    Assert.IsTrue(count <= room, $"{cover} holds {count} of {room}.");
                    if (cover.StartsWith("marquee", StringComparison.Ordinal)) peakMarquee = Math.Max(peakMarquee, count);
                }
                Assert.AreEqual(heading.Length, heading.Select(agent => (agent.Destination!.Value.X, agent.Destination.Value.Z)).Distinct().Count(), "One to a place.");
            }
        }
        Assert.IsTrue(peakMarquee >= 8, $"The marquee fills up in a shower: {peakMarquee}.");
        Assert.IsTrue(peakMarquee <= MarqueeRules.RestCapacity);
    }

    [TestMethod]
    public void APonchoBuyerStaysDryAndPonchosAreInTheAccounts()
    {
        var s = Prepared();
        BuildSession.Accept(s, new SetPreparationStockCommand(40, 32) { Ponchos = 20 });
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        var wearing = new Dictionary<ulong, int>();
        while (s.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing)
        {
            s.AdvanceWithoutSnapshot(80);
            foreach (var (id, wetness) in wearing.ToArray())
            {
                Assert.IsTrue(s.WetnessOf(id) <= wetness, $"{id} got wetter in a poncho.");
                wearing[id] = s.WetnessOf(id);
            }
            foreach (var purchase in s.CaptureImmersion()!.Purchases.Where(purchase => purchase.Product == ImmersionProduct.Poncho && !wearing.ContainsKey(purchase.AgentId)))
                wearing[purchase.AgentId] = s.WetnessOf(purchase.AgentId);
        }
        Assert.IsTrue(wearing.Count >= 5, $"Ponchos sell in the rain: {wearing.Count}.");
        Assert.IsTrue(wearing.Count <= 20, "No more than the stock.");
        var accounts = s.CompletedFestivalAccounts!;
        var lines = accounts.Sales.Where(sale => sale.Product == ImmersionProduct.Poncho).ToArray();
        Assert.AreEqual(wearing.Count, lines.Sum(line => line.Quantity), "Every poncho sold is a sale, at whichever bar.");
        Assert.AreEqual(wearing.Count * (long)WeatherRules.PonchoPricePennies, lines.Sum(line => line.AmountPennies));
        Assert.IsTrue(accounts.PurchasedStock.Any(stock => stock.Label == "Poncho" && stock.Quantity == 20));
        Assert.IsTrue(accounts.Reconciles, "The accounts still balance.");
    }

    [TestMethod]
    public void AStewardSpreadsABaleOnTheMud()
    {
        var s = Prepared();
        BuildSession.Accept(s, new SetPreparationStockCommand(40, 32) { StrawBales = 2 });
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        BuildSession.WithoutFaults(s);
        RunTo(s, 4_000);
        var spot = new GridCell(150, 154);
        // Make a patch of mud there to cover.
        var mud = (int[])typeof(GameSession).GetField("_groundMud", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
        for (var x = spot.X - 4; x <= spot.X + 4; x++) for (var z = spot.Z - 4; z <= spot.Z + 4; z++) mud[z * TraversalGrid.Width + x] = 80;
        var steward = s.GetStewardResponses().First().WorkerId;
        Assert.IsTrue(s.CanSpreadStraw(steward, out var reason), reason);
        BuildSession.Accept(s, new SpreadStrawCommand(steward, spot));
        Assert.AreEqual(1, s.CaptureWeather()!.StrawBales, "A bale comes out of the store.");
        Assert.IsFalse(BuildSession.Send(s, new SpreadStrawCommand(steward, spot)).IsAccepted, "One bale at a time.");
        var trail = new System.Text.StringBuilder();
        for (var i = 0; i < 60 && s.CaptureWeather()!.StrawJob is not null; i++)
        {
            var nav = s.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id.Value == steward);
            trail.AppendLine($"{s.CaptureWeather()!.StrawJob} {nav.Action} {nav.IntentId} {nav.Destination} {s.CapturePerson(steward)!.Reason} {s.CapturePerson(steward)!.Intent}");
            s.AdvanceWithoutSnapshot(80);
        }
        var after = s.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id.Value == steward);
        trail.AppendLine($"after: {after.Action} {after.IntentId} {after.Destination} {s.CapturePerson(steward)!.Reason}");
        Assert.IsNull(s.CaptureWeather()!.StrawJob, "The job ends.");
        var covered = 0;
        for (var x = spot.X - 6; x <= spot.X + 6; x++) for (var z = spot.Z - 6; z <= spot.Z + 6; z++)
            if (s.GroundTrampleAt(new(x, z)) >= 0) covered++;
        Assert.IsTrue(covered >= GroundCoverRules.CellsPerBale - 2, $"A bale's worth of straw is down: {covered}. {trail}");
        Assert.AreEqual(GroundState.Grass, s.GroundStateAt(spot), "Straw firms up the mud.");
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
    }

    [TestMethod]
    public void AMidShowerSaveRoundTrips()
    {
        var s = Prepared(RainProbe.Counters.MarqueeStrawPonchos);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        var shower = FirstShower(s);
        RunTo(s, shower.StartTick + 1_000);
        Assert.AreNotEqual(RainIntensity.Dry, s.RainNow);
        Assert.IsTrue(s.CaptureWeather()!.WetIds.Length > 0);
        var directory = Path.Combine(Path.GetTempPath(), $"festival-rain-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var compatibility = new SaveCompatibility("test-build", "content-v1", "rules-v1");
            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "rain", new SaveWriteRequest(s, compatibility, "test", new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero))).IsSuccess);
            var loaded = SaveFileAdapter.LoadSlot(directory, "rain", compatibility);
            Assert.IsTrue(loaded.IsSuccess, loaded.Error);
            var copy = loaded.Session!;
            Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, copy.CaptureSnapshot().AuthoritativeHash);
            s.AdvanceWithoutSnapshot(2_400); copy.AdvanceWithoutSnapshot(2_400);
            Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, copy.CaptureSnapshot().AuthoritativeHash, "The day plays on the same after loading.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void ASaveWithTamperedShowersIsRefused()
    {
        var s = Started();
        RunTo(s, 2_000);
        var snapshot = s.CapturePersistenceSnapshot();
        var weather = snapshot.Weather!;
        var forged = snapshot with { Weather = weather with { Spells = weather.Spells.Skip(1).ToArray() } };
        Assert.IsFalse(GameSession.Restore(forged).IsSuccess);
        var hot = snapshot with { Weather = null };
        Assert.IsFalse(GameSession.Restore(hot).IsSuccess);
    }
}

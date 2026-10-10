using System.Collections;
using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class MarqueeTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    /// <summary>South of the trailer stage's crowd, west of the gate lane: nearer the crowd than first aid's rest area.</summary>
    internal static readonly GridCell ByTheCrowd = new(104, 174);

    private static CommandResult Place(GameSession s, GridCell cell, int quarterTurns = 0) =>
        BuildSession.Send(s, new PlaceBuildServiceCommand(BuildServiceKind.Marquee, cell, quarterTurns));

    private static void SetMedical(GameSession s, Func<MedicalNeed, MedicalNeed> change)
    {
        var state = s.CaptureMedical()!;
        typeof(GameSession).GetProperty("MedicalView", Private)!.SetValue(s, state with { Needs = state.Needs.Select(change).ToArray() });
    }

    private static void Calm(GameSession s, ulong id) => typeof(GameSession).GetMethod("MutatePerson", Private)!.Invoke(s,
        [id, (Action<Person>)(person => { person.ToiletNeed = 0; person.Hunger = 0; })]);

    /// <summary>Test fixture: stands someone still on a cell, as if they'd walked there.</summary>
    private static void StandAt(GameSession s, ulong id, GridCell cell)
    {
        var agents = (IDictionary)typeof(GameSession).GetField("_navigationAgents", Private)!.GetValue(s)!;
        var agent = agents[new EntityId(id)]!;
        var centre = TraversalGrid.CellCentre(cell);
        void Set(string name, object? value) => agent.GetType().GetProperty(name)!.SetValue(agent, value);
        Set("XMillimetres", centre.XMillimetres); Set("ZMillimetres", centre.ZMillimetres);
        Set("SegmentOriginXMillimetres", centre.XMillimetres); Set("SegmentOriginZMillimetres", centre.ZMillimetres);
        Set("SegmentProgressMicrometres", 0); Set("Destination", cell); Set("Route", new List<GridCell> { cell }); Set("RouteIndex", 0);
        Set("Action", AgentNavigationAction.Arrived);
    }

    /// <summary>A crewed Tier 1 day with guests on site, a marquee by the crowd if asked for, and no random faults.</summary>
    private static GameSession Live(bool marquee)
    {
        var s = BuildSession.Ready();
        if (marquee) BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Marquee, ByTheCrowd));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        BuildSession.WithoutFaults(s);
        s.AdvanceWithoutSnapshot(GameSession.FestivalSlotStarts[0] + 400);
        return s;
    }

    /// <summary>Guests on site, listening, and in no queue: free for the chooser to send anywhere.</summary>
    private static ulong[] FreeGuests(GameSession s, bool bands = false) => s.CaptureMedical()!.Needs
        .Where(need => (need.Profile == MedicalNeedProfile.Guest || bands && need.Profile == MedicalNeedProfile.Performer && !s.IsCurrentProgrammePerformer(need.AgentId)) &&
            need.Intent == MedicalIntent.WatchShow && need.Stage == MedicalStage.Clear &&
            s.CapturePerson(need.AgentId) is { Admitted: true, Departed: false, ToiletStage: ToiletVisitStage.None, VendorId: null })
        .Select(need => need.AgentId).ToArray();

    [TestMethod]
    public void FootprintTurnsWithTheTentAndOnlyThePolesAndPegsBlock()
    {
        var placed = new BuildPlacement("marquee.main", BuildServiceKind.Marquee, new(150, 140), 0);
        var footprint = GameSession.BuildFootprint(placed);
        Assert.AreEqual(19 * 15, footprint.Length, "9.5 × 7.5 m, guy-rope pegs included.");
        Assert.AreEqual(19, footprint.Select(cell => cell.X).Distinct().Count());
        var turned = GameSession.BuildFootprint(placed with { QuarterTurns = 1 });
        Assert.AreEqual(15, turned.Select(cell => cell.X).Distinct().Count(), "A quarter turn swaps its width and depth.");
        Assert.AreEqual(19, turned.Select(cell => cell.Z).Distinct().Count());
        foreach (var turns in Enumerable.Range(0, 4))
        {
            var solid = GameSession.MarqueeSolidCells(placed.Cell, turns);
            Assert.AreEqual(10, solid.Distinct().Count(), "Two main poles, four corner poles and four pegs.");
            Assert.IsTrue(solid.All(GameSession.BuildFootprint(placed with { QuarterTurns = turns }).Contains));
            var shade = GameSession.MarqueeShelterCells(placed.Cell, turns);
            Assert.AreEqual(11 * 9, shade.Length);
            var spots = GameSession.MarqueeRestSpots(placed.Cell, turns);
            Assert.AreEqual(MarqueeRules.RestCapacity, spots.Distinct().Count());
            Assert.IsTrue(spots.All(shade.Contains) && !spots.Any(solid.Contains), "Rest places are in the shade and off the poles.");
        }

        // Once open, the poles and pegs are solid and the rest of the ground under it stays walkable.
        var s = BuildSession.Ready();
        BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Marquee, ByTheCrowd));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        var grid = s.TraversalGrid!;
        var poles = GameSession.MarqueeSolidCells(ByTheCrowd, 0);
        Assert.IsTrue(poles.All(cell => !grid.Get(cell).IsWalkable));
        Assert.IsTrue(GameSession.BuildFootprint(s.CaptureBuildPlacements().Single(item => item.Kind == BuildServiceKind.Marquee))
            .Where(cell => !poles.Contains(cell)).All(cell => grid.Get(cell).IsWalkable), "Everything under the sail is open ground.");
    }

    [TestMethod]
    public void MarqueeKeepsOffAudiencesStageReservesTheRestAreaAndOtherServices()
    {
        var s = BuildSession.Ready();
        var before = s.BuildDraftCost;
        Assert.IsTrue(Place(s, ByTheCrowd).IsAccepted);
        Assert.AreEqual(before + 4_000, s.BuildDraftCost, "£40 hire for the day.");
        BuildSession.Accept(s, new RemoveBuildServiceCommand("marquee.main"));

        var audience = FestivalStages.Main.AudienceCentre;
        Assert.AreEqual("Keep the audience area in front of the stage clear.", Place(s, audience).Message);
        Assert.IsFalse(Place(s, new(ByTheCrowd.X, ByTheCrowd.Z - 6)).IsAccepted, "Its back edge would reach into the crowd.");
        var rest = s.CaptureRestCentre();
        Assert.IsFalse(Place(s, new(rest.X + 8, rest.Z)).IsAccepted, "Its pegs would stand in first aid's rest area.");
        Assert.IsFalse(Place(s, s.CaptureBuildPlacements().Single(item => item.Id == "drinks").Cell).IsAccepted);
        // Nothing else goes up under it, not even a bin.
        BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Marquee, ByTheCrowd));
        Assert.IsFalse(BuildSession.Send(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, new(ByTheCrowd.X + 2, ByTheCrowd.Z + 2))).IsAccepted);
        Assert.IsFalse(BuildSession.Send(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, new(ByTheCrowd.X + 8, ByTheCrowd.Z + 6))).IsAccepted,
            "The guy-rope pegs are part of it too.");

        // Tier 2: the Pond Stage's audience and reserve are as off limits as the trailer stage's.
        var two = NextFestivalTests.Drafted(GameSession.CreateDevelopmentFestival(20260922, 2));
        var pond = two.Stages[1];
        Assert.IsFalse(Place(two, pond.AudienceCentre).IsAccepted);
        var riser = new GridCell((pond.ReserveBounds.MinX + pond.ReserveBounds.MaxX) / 2, (pond.ReserveBounds.MinZ + pond.ReserveBounds.MaxZ) / 2);
        Assert.IsFalse(Place(two, riser).IsAccepted);
        Assert.IsFalse(GameSession.MarqueeFootprintCells(ByTheCrowd, 0).Any(cell => FestivalStages.InAnyReserve(two.Stages, cell) || GameSession.InAudienceArea(two.Stages, cell)));
        Assert.AreEqual("Keep the audience area in front of the stage clear.", Place(two, ByTheCrowd, 1).Message,
            "Turned, it would reach into the trailer stage's crowd.");
        Assert.IsTrue(Place(two, ByTheCrowd).IsAccepted);
    }

    [TestMethod]
    public void OneMarqueeAtTierOneTwoAtTierTwoAndItIsNeverRequired()
    {
        Assert.AreEqual(1, GameSession.BuildServiceLimit(BuildServiceKind.Marquee, 1));
        Assert.AreEqual(2, GameSession.BuildServiceLimit(BuildServiceKind.Marquee, 2));
        var one = BuildSession.Ready();
        Assert.AreEqual(0, one.GetPreparationStartBlockers().Count, "Optional: the default plan opens without one.");
        Assert.IsFalse(one.CaptureBuildPlacements().Any(item => item.Kind == BuildServiceKind.Marquee), "Not in the default layout.");
        Assert.IsTrue(Place(one, ByTheCrowd).IsAccepted);
        Assert.IsFalse(Place(one, new(160, 140)).IsAccepted, "Tier 1 hires one.");

        var two = NextFestivalTests.Drafted(GameSession.CreateDevelopmentFestival(20260922, 2));
        Assert.IsTrue(Place(two, ByTheCrowd).IsAccepted);
        Assert.IsTrue(Place(two, new(172, 116)).IsAccepted);
        CollectionAssert.AreEqual(new[] { "marquee.extra-1", "marquee.main" },
            two.CaptureBuildPlacements().Where(item => item.Kind == BuildServiceKind.Marquee).Select(item => item.Id).ToArray());
        Assert.IsFalse(Place(two, new(165, 118)).IsAccepted, "Tier 2 hires two.");
    }

    [TestMethod]
    public void NobodyHeatsUpInTheShade()
    {
        var s = Live(marquee: true);
        var guests = FreeGuests(s);
        var (shaded, sunny) = (guests[0], guests[1]);
        SetMedical(s, need => need.AgentId == shaded || need.AgentId == sunny ? need with { Thirst = 0, HeatExposure = 3_000 } : need);
        StandAt(s, shaded, new(ByTheCrowd.X + 1, ByTheCrowd.Z + 1));
        StandAt(s, sunny, new(ByTheCrowd.X + 1, ByTheCrowd.Z - 12));
        Assert.IsTrue(s.InMarqueeShade(new(ByTheCrowd.X + 1, ByTheCrowd.Z + 1)));
        Assert.IsFalse(s.InMarqueeShade(new(ByTheCrowd.X + 1, ByTheCrowd.Z - 12)));
        s.AdvanceWithoutSnapshot(16);
        int Heat(ulong id) => s.CaptureMedical()!.Needs.Single(need => need.AgentId == id).HeatExposure;
        Assert.AreEqual(3_000, Heat(shaded), "No heat builds up under the sail.");
        Assert.AreEqual(3_004, Heat(sunny), "Out in the sun it builds as ever.");
        Assert.AreEqual(1, s.CaptureMarquees().Single().Sheltering);
    }

    /// <summary>An overheated guest at <paramref name="from"/>, left to choose; where they go to rest.</summary>
    private static (MedicalNeed Need, GridCell? Destination, GameSession Session) HotGuestChooses(bool marquee, GridCell from)
    {
        var s = Live(marquee);
        var guest = FreeGuests(s)[0];
        SetMedical(s, need => need.AgentId == guest ? need with { Thirst = 0, HeatExposure = 8_500 } : need);
        Calm(s, guest);
        StandAt(s, guest, from);
        s.AdvanceWithoutSnapshot(80);
        var agent = s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == guest);
        return (s.CaptureMedical()!.Needs.Single(need => need.AgentId == guest), agent.Destination, s);
    }

    [TestMethod]
    public void AnOverheatedGuestNearTheMarqueeRestsInItsShadeAndOneNearFirstAidRestsThere()
    {
        var nearTent = new GridCell(ByTheCrowd.X + 4, ByTheCrowd.Z - 10);
        var (need, destination, s) = HotGuestChooses(true, nearTent);
        Assert.AreEqual(MedicalIntent.Rest, need.Intent, need.Reason);
        StringAssert.Contains(need.Reason, "rest in the shade");
        Assert.IsTrue(destination is { } spot && s.IsMarqueeRestSpot(spot), $"Rested at {destination}, not under the marquee.");
        Assert.AreEqual(1, s.CaptureMarquees().Single().Resting);
        // Resting there cools them as it would by first aid, and then they go back to the music.
        var deadline = s.CurrentTick + 4_000;
        while (s.CaptureMedical()!.Needs.Single(item => item.AgentId == need.AgentId).Intent == MedicalIntent.Rest && s.CurrentTick < deadline)
            s.AdvanceWithoutSnapshot(8);
        var after = s.CaptureMedical()!.Needs.Single(item => item.AgentId == need.AgentId);
        Assert.AreNotEqual(MedicalIntent.Rest, after.Intent);
        Assert.IsTrue(after.HeatExposure <= ActivityChooser.RestHeatTarget, $"Heat {after.HeatExposure}");

        // Without the feature the same guest walks all the way to first aid.
        var (without, plain, bare) = HotGuestChooses(false, nearTent);
        Assert.AreEqual(MedicalIntent.Rest, without.Intent, without.Reason);
        Assert.IsTrue(plain is { } aid && bare.IsRestSpot(aid));

        // Someone already by first aid still rests there, marquee or not.
        var rest = s.CaptureRestCentre();
        var (far, farSpot, farSession) = HotGuestChooses(true, new(rest.X + 3, rest.Z + 5));
        Assert.AreEqual(MedicalIntent.Rest, far.Intent, far.Reason);
        Assert.IsTrue(farSpot is { } farCell && farSession.IsRestSpot(farCell), $"Rested at {farSpot}, not by first aid.");
    }

    [TestMethod]
    public void AFullMarqueeSendsTheNextHotGuestToFirstAid()
    {
        var s = Live(marquee: true);
        // A Tier 1 crowd is small, so band members off stage make up the numbers.
        var guests = FreeGuests(s, bands: true);
        // A few more than it holds: a band member due on stage soon may go and play instead.
        Assert.IsTrue(guests.Length >= MarqueeRules.RestCapacity + 3, $"{guests.Length} free guests");
        var nearTent = new GridCell(ByTheCrowd.X + 4, ByTheCrowd.Z - 10);
        var hot = guests.Take(MarqueeRules.RestCapacity + 3).ToArray();
        SetMedical(s, need => hot.Contains(need.AgentId) ? need with { Thirst = 0, HeatExposure = 9_500 } : need);
        foreach (var id in hot) { Calm(s, id); StandAt(s, id, nearTent); }
        s.AdvanceWithoutSnapshot(80);
        var agents = s.CaptureSnapshot().NavigationAgents.Where(agent => hot.Contains(agent.Id.Value)).ToArray();
        var resting = s.CaptureMedical()!.Needs.Where(need => hot.Contains(need.AgentId) && need.Intent == MedicalIntent.Rest).Select(need => need.AgentId).ToHashSet();
        var underTent = agents.Count(agent => resting.Contains(agent.Id.Value) && agent.Destination is { } spot && s.IsMarqueeRestSpot(spot));
        Assert.AreEqual(MarqueeRules.RestCapacity, underTent, "It holds fifteen.");
        Assert.AreEqual(MarqueeRules.RestCapacity, agents.Where(agent => resting.Contains(agent.Id.Value) && s.IsMarqueeRestSpot(agent.Destination!.Value))
            .Select(agent => agent.Destination).Distinct().Count(), "Each at a place of their own.");
        Assert.IsTrue(agents.Any(agent => resting.Contains(agent.Id.Value) && agent.Destination is { } spot && s.IsRestSpot(spot)),
            "Once it's full, the next goes to first aid.");
    }

    [TestMethod]
    public void AStrandedResterDoesNotCountAgainstAFullMarqueeOnRestore()
    {
        var s = Live(marquee: true);
        var guests = FreeGuests(s, bands: true);
        var nearTent = new GridCell(ByTheCrowd.X + 4, ByTheCrowd.Z - 10);
        var hot = guests.Take(MarqueeRules.RestCapacity + 3).ToArray();
        SetMedical(s, need => hot.Contains(need.AgentId) ? need with { Thirst = 0, HeatExposure = 9_500 } : need);
        foreach (var id in hot) { Calm(s, id); StandAt(s, id, nearTent); }
        s.AdvanceWithoutSnapshot(80);
        Assert.AreEqual(MarqueeRules.RestCapacity, s.CaptureMarquees().Single().Resting);
        // Someone resting by first aid finds no way back to a tent place they'd once headed for: the runtime cap ignores
        // them, so a sixteenth tent destination can be saved and must load.
        var agents = s.CaptureSnapshot().NavigationAgents;
        var stranded = agents.First(agent => hot.Contains(agent.Id.Value) && agent.Destination is { } spot && s.IsRestSpot(spot)).Id;
        var held = agents.First(agent => agent.Destination is { } spot && s.IsMarqueeRestSpot(spot)).Destination!.Value;
        var all = (IDictionary)typeof(GameSession).GetField("_navigationAgents", Private)!.GetValue(s)!;
        var state = all[stranded]!;
        void Set(string name, object? value) => state.GetType().GetProperty(name)!.SetValue(state, value);
        Set("Destination", held); Set("Route", new List<GridCell>()); Set("RouteIndex", 0); Set("Action", AgentNavigationAction.NoRoute);
        var saved = s.CapturePersistenceSnapshot();
        Assert.AreEqual(MarqueeRules.RestCapacity + 1, saved.NavigationAgents!.Count(agent => agent.IntentId == "medical.rest" &&
            agent.DestinationX is { } x && agent.DestinationZ is { } z && s.IsMarqueeRestSpot(new(x, z))));
        BuildSession.Restored(s);
        // A sixteenth on their way (or there) is more than the tent ever admits, and is refused.
        var crowded = saved with { NavigationAgents = saved.NavigationAgents!.Select(agent => agent.Id == stranded.Value
            ? Arrived(agent) : agent).ToArray() };
        static PersistedNavigationAgent Arrived(PersistedNavigationAgent agent)
        {
            var centre = TraversalGrid.CellCentre(new(agent.DestinationX!.Value, agent.DestinationZ!.Value));
            return agent with { Action = (int)AgentNavigationAction.Arrived, XMillimetres = centre.XMillimetres, ZMillimetres = centre.ZMillimetres,
                SegmentOriginXMillimetres = centre.XMillimetres, SegmentOriginZMillimetres = centre.ZMillimetres,
                Route = [new(agent.DestinationX.Value, agent.DestinationZ.Value)], RouteIndex = 0 };
        }
        var refused = GameSession.Restore(crowded);
        Assert.IsFalse(refused.IsSuccess);
        StringAssert.Contains(refused.Error, "Marquee holds more resting guests");
    }

    [TestMethod]
    public void ASaveRestoresWithAMarqueeAndGuestsShelteringUnderIt()
    {
        var (need, _, s) = HotGuestChooses(true, new(ByTheCrowd.X + 4, ByTheCrowd.Z - 10));
        Assert.AreEqual(MedicalIntent.Rest, need.Intent);
        while (s.CaptureMarquees().Single().Sheltering == 0 && s.CurrentTick < 40_000) s.AdvanceWithoutSnapshot(8);
        var restored = BuildSession.Restored(s);
        Assert.AreEqual(s.CaptureMarquees().Single(), restored.CaptureMarquees().Single());
        s.AdvanceWithoutSnapshot(400); restored.AdvanceWithoutSnapshot(400);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.CaptureSnapshot().AuthoritativeHash);

        // A save whose ground has lost a tent pole is refused.
        var saved = s.CapturePersistenceSnapshot();
        var pole = GameSession.MarqueeSolidCells(ByTheCrowd, 0)[0];
        var tampered = saved with { TraversalGrid = saved.TraversalGrid! with { Cells = saved.TraversalGrid.Cells
            .Select(cell => cell.X == pole.X && cell.Z == pole.Z ? cell with { IsWalkable = true } : cell).ToArray() } };
        Assert.IsFalse(GameSession.Restore(tampered).IsSuccess);
        // So is one whose marquee stands in the crowd.
        var moved = saved with { Preparation = saved.Preparation! with { BuildPlacements = saved.Preparation.BuildPlacements
            .Select(item => item.Kind == BuildServiceKind.Marquee ? item with { Cell = FestivalStages.Main.AudienceCentre } : item).ToArray() } };
        Assert.IsFalse(GameSession.Restore(moved).IsSuccess);
    }
}

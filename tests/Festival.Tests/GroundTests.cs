using System.Reflection;
using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class GroundTests
{
    private static int TotalWear(GameSession s) => s.CaptureGround()!.Wear.Sum();

    [TestMethod]
    public void FeetWearTheFieldOnlyOnceTheFestivalIsOn()
    {
        var s = Ready();
        s.AdvanceWithoutSnapshot(400);
        Assert.AreEqual(0, TotalWear(s), "Nobody wears the grass while the festival is being prepared.");
        Accept(s, new StartPreparedEditionCommand());
        s.AdvanceWithoutSnapshot(4_000);
        var ground = s.CaptureGround()!;
        Assert.IsTrue(ground.Cells.Length > 0, "Guests walking in have worn the grass.");
        Assert.IsTrue(ground.Cells.All(index => GroundRules.InField(new GridCell(index % TraversalGrid.Width, index / TraversalGrid.Width))),
            "Only the field inside the hedges wears.");
        CollectionAssert.AreEqual(ground.Cells.Order().ToArray(), ground.Cells, "Saved in ascending cell order.");
        var cell = new GridCell(ground.Cells[0] % TraversalGrid.Width, ground.Cells[0] / TraversalGrid.Width);
        Assert.AreEqual(ground.Wear[0], s.GroundWearAt(cell));
        Restored(s);
    }

    [TestMethod]
    public void StandingCrowdsWearDeeperThanPassingFeet()
    {
        var s = Started();
        s.AdvanceWithoutSnapshot(20_000);
        var wear = s.CaptureGround()!.Wear;
        Assert.IsTrue(wear.Max() >= GroundRules.BareEarthWear, "Somewhere people stand has gone to bare earth by mid-afternoon.");
        Assert.IsTrue(wear.Count(w => w < 10) > wear.Length / 2, "Most worn cells are only lightly passed over.");
    }

    [TestMethod]
    public void NobodyWaitingOutsideOrGoneHomeWearsTheGateway()
    {
        var s = Started();
        GridCell Cell(ulong id) { var a = s.CaptureSnapshot().NavigationAgents.Single(x => x.Id.Value == id); return TraversalGrid.WorldToCell(a.XMillimetres, a.ZMillimetres); }
        var waiting = s.CapturePreparation()!.People.First(p => s.GuestWaitingForRelease(p.AgentId)).AgentId;
        var held = Cell(waiting);
        var before = s.GroundWearAt(held);
        for (var guard = 0; guard < 20 && s.GuestWaitingForRelease(waiting); guard++) s.AdvanceWithoutSnapshot(40);
        Assert.IsTrue(s.GroundWearAt(held) - before <= 2, "A guest held at the gate doesn't wear their spot while they wait.");
        for (var guard = 0; guard < 2_000 && s.PreparedStatus != PreparationStatus.Departing; guard++) s.AdvanceWithoutSnapshot(40);
        ulong? home = null;
        for (var guard = 0; guard < 2_000 && home is null && s.PreparedStatus == PreparationStatus.Departing; guard++)
        {
            s.AdvanceWithoutSnapshot(40);
            home = s.CapturePreparation()!.People.FirstOrDefault(p => p.Departed)?.AgentId;
        }
        Assert.IsNotNull(home, "Someone has gone home while others are still leaving.");
        var spot = Cell(home.Value);
        var parked = s.GroundWearAt(spot);
        for (var i = 0; i < 10 && s.PreparedStatus == PreparationStatus.Departing; i++) s.AdvanceWithoutSnapshot(40);
        Assert.IsTrue(s.GroundWearAt(spot) - parked <= 2, "Someone gone home no longer wears the ground where they left.");
    }

    [TestMethod]
    public void ARetriedWeekendStartsOnFreshGrass()
    {
        var s = Started();
        s.AdvanceWithoutSnapshot(4_000);
        Assert.IsTrue(TotalWear(s) > 0);
        typeof(GameSession).GetMethod("RetryPreparedWeekend", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, null);
        Assert.AreEqual(0, TotalWear(s));
    }

    [TestMethod]
    public void ASaveWithImpossibleWearIsRejected()
    {
        var s = Started();
        s.AdvanceWithoutSnapshot(4_000);
        var snapshot = s.CapturePersistenceSnapshot();
        var ground = snapshot.Ground!;
        string? Error(GroundSnapshot bad) => GameSession.Restore(snapshot with { Ground = bad }).Error;
        StringAssert.Contains(Error(ground with { Cells = ground.Cells.Reverse().ToArray() }), "ascending");
        StringAssert.Contains(Error(ground with { Cells = [0], Wear = [1] }), "outside the field");
        StringAssert.Contains(Error(ground with { Wear = ground.Wear.Select(_ => GroundRules.MaximumWear + 1).ToArray() }), "out of range");
        StringAssert.Contains(Error(ground with { Wear = [] }), "shape");
        StringAssert.Contains(GameSession.Restore(snapshot with { Ground = null }).Error, "requires ground state");
    }

    private static readonly FieldInfo FaultsField = typeof(GameSession).GetField("_faults", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>
    /// Breaks the main tap now, replacing any earlier fault there, so it can be kept broken all day. A save can't
    /// hold a fault under the fault-free fixture, so this lifts it; chance faults may then happen too.
    /// </summary>
    private static void BreakMainTap(GameSession s)
    {
        var faults = s.CaptureFaults()!;
        if (faults.Faults.Any(f => f.FacilityId == "water.main" && f.Stage == FacilityFaultStage.Active)) return;
        FaultsField.SetValue(s, faults with { Disabled = false, Faults = faults.Faults.Where(f => f.FacilityId != "water.main").Append(new FacilityFault(
            $"broken:water.main:{s.CurrentTick}", FacilityFaultKind.BrokenTap, "water.main", LastGuest(s), s.CurrentTick, FacilityFaultStage.Active)).ToArray() });
    }

    /// <summary>Mends the tap and restores the fault-free fixture, so nothing breaks it again by chance.</summary>
    private static void MendMainTap(GameSession s) => FaultsField.SetValue(s, s.CaptureFaults()! with { Disabled = true, Faults = [] });

    private static GridCell[] Cells(Func<GridCell, bool> where)
    {
        var cells = new List<GridCell>();
        for (var z = GroundRules.FieldFirstCell; z <= GroundRules.FieldLastCell; z++)
        for (var x = GroundRules.FieldFirstCell; x <= GroundRules.FieldLastCell; x++)
            if (where(new GridCell(x, z))) cells.Add(new GridCell(x, z));
        return cells.ToArray();
    }

    private static double Reach(GameSession s, GridCell from) =>
        Cells(c => s.GroundWetAt(c) >= GroundRules.PuddleWet)
            .Select(c => Math.Sqrt((c.X - from.X) * (c.X - from.X) + (c.Z - from.Z) * (c.Z - from.Z))).DefaultIfEmpty(-1).Max();

    [TestMethod]
    public void ABrokenTapsPuddleStartsSmallAtTheTapAndGrowsOutwards()
    {
        var s = WithoutFaults(Started());
        s.AdvanceWithoutSnapshot(3_000);
        var tap = MainTap(s).Cell;
        BreakMainTap(s);
        s.AdvanceWithoutSnapshot(GroundRules.FootfallEveryTicks * 3);
        var early = Reach(s, tap);
        Assert.IsTrue(early is >= 0 and <= 1.5, $"Early on the water lies right at the tap (reach {early}).");
        var reaches = new List<double>();
        for (var i = 0; i < 6; i++) { BreakMainTap(s); s.AdvanceWithoutSnapshot(1_500); reaches.Add(Reach(s, tap)); }
        Assert.IsTrue(reaches.Last() >= early + 4, $"The puddle spreads cell by cell from the tap: {early} then {string.Join(", ", reaches)}.");
        Assert.IsTrue(reaches.Zip(reaches.Skip(1)).All(pair => pair.Second >= pair.First - 0.5), "It grows steadily while the tap stays broken.");
    }

    [TestMethod]
    public void AQuickRepairLeavesWetGrassThatDriesWhileATapLeftAllDayMakesASwamp()
    {
        var quick = WithoutFaults(Started());
        quick.AdvanceWithoutSnapshot(3_000);
        BreakMainTap(quick);
        quick.AdvanceWithoutSnapshot(2_000);
        MendMainTap(quick);
        var wetAtRepair = Cells(c => quick.GroundWetAt(c) >= GroundRules.PuddleWet).Length;
        Assert.IsTrue(wetAtRepair > 0);
        for (var i = 0; i < 12; i++)
        {
            quick.AdvanceWithoutSnapshot(1_000);
            Assert.IsTrue(Cells(c => quick.GroundStateAt(c) == GroundState.Swamp).Length <= 4,
                "A quick repair makes no swamp, at most a trampled patch right by the tap where people stood.");
        }
        Assert.IsTrue(Cells(c => quick.GroundWetAt(c) >= GroundRules.PuddleWet).Length < wetAtRepair / 2, "The puddle dries back once the tap is mended.");

        var neglected = WithoutFaults(Started());
        neglected.AdvanceWithoutSnapshot(3_000);
        for (var i = 0; i < 16; i++) { BreakMainTap(neglected); neglected.AdvanceWithoutSnapshot(1_000); }
        var swamp = Cells(c => neglected.GroundStateAt(c) == GroundState.Swamp).Length;
        Assert.IsTrue(swamp >= 20, $"A tap left broken most of the day turns its puddle's heart to swamp ({swamp} cells).");
    }

    [TestMethod]
    public void PeopleRouteRoundASwampAndWadeSlowlyThroughOne()
    {
        var grid = new TraversalGrid();
        var extra = new int[TraversalGrid.Width * TraversalGrid.Depth];
        for (var z = 95; z <= 105; z++)
        for (var x = 98; x <= 102; x++)
            extra[z * TraversalGrid.Width + x] = GroundRules.RouteCostPermille(GroundState.Swamp);
        var straight = DeterministicPathfinder.FindPath(grid, new(90, 100), new(110, 100));
        var around = DeterministicPathfinder.FindPath(grid, new(90, 100), new(110, 100), extra);
        Assert.IsTrue(straight.Path.Contains(new GridCell(100, 100)), "Dry ground: the straight line.");
        Assert.IsFalse(around.Path.Any(c => extra[c.Z * TraversalGrid.Width + c.X] > 0), "With a swamp in the way, the route skirts it.");
        Assert.IsTrue(GroundRules.SlowPermille(GroundState.Swamp) > GroundRules.SlowPermille(GroundState.Mud) &&
            GroundRules.SlowPermille(GroundState.Mud) > GroundRules.SlowPermille(GroundState.Grass));
    }

    [TestMethod]
    public void WadingThroughASwampUpsetsAGuestAndMudsTheirBoots()
    {
        GameSession Day() { var day = WithoutFaults(Started()); day.AdvanceWithoutSnapshot(6_000); return day; }
        var s = Day(); var dry = Day();
        var guest = s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed).AgentId;
        var agent = s.CaptureSnapshot().NavigationAgents.Single(a => a.Id.Value == guest);
        var cell = TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres);
        int[] Layer(string name) => (int[])typeof(GameSession).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(s)!;
        var wet = Layer("_groundWet"); var mud = Layer("_groundMud");
        for (var dz = -3; dz <= 3; dz++)
        for (var dx = -3; dx <= 3; dx++)
        { var i = (cell.Z + dz) * TraversalGrid.Width + cell.X + dx; wet[i] = 400; mud[i] = 100; }
        s.AdvanceWithoutSnapshot(GroundRules.FootfallEveryTicks * 2);
        dry.AdvanceWithoutSnapshot(GroundRules.FootfallEveryTicks * 2);
        int Satisfaction(GameSession x) => x.CapturePreparation()!.People.Single(p => p.AgentId == guest).Satisfaction;
        Assert.IsTrue(Satisfaction(s) < Satisfaction(dry), "Standing in a swamp is unpleasant.");
        Assert.IsTrue(s.CaptureGround()!.MuddyBoots.Contains(guest), "And they come out with muddy boots.");
    }

    [TestMethod]
    public void ASaveWithImpossibleWaterOrMudIsRejected()
    {
        var s = WithoutFaults(Started());
        s.AdvanceWithoutSnapshot(3_000);
        BreakMainTap(s);
        s.AdvanceWithoutSnapshot(3_000);
        Restored(s);
        var snapshot = s.CapturePersistenceSnapshot();
        var g = snapshot.Ground!;
        Assert.IsTrue(g.WetCells.Length > 0);
        string? Error(GroundSnapshot bad) => GameSession.Restore(snapshot with { Ground = bad }).Error;
        StringAssert.Contains(Error(g with { WetCells = g.WetCells.Reverse().ToArray() }), "ascending");
        StringAssert.Contains(Error(g with { Wet = g.Wet.Select(_ => GroundRules.MaximumWet + 1).ToArray() }), "out of range");
        StringAssert.Contains(Error(g with { MudCells = [0], Mud = [1] }), "outside the field");
        StringAssert.Contains(Error(g with { MuddyBoots = [999_999], MuddySteps = [1] }), "not on the farm");
        StringAssert.Contains(Error(g with { MuddyBoots = [LastGuest(s)], MuddySteps = [GroundRules.MuddyBootSteps + 1] }), "out of range");
    }
}

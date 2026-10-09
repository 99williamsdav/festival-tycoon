using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class ToiletQueueGrowthTests
{
    private static string? Place(GameSession s, BuildServiceKind kind, GridCell cell, int turns) =>
        s.ValidateCommand(new CommandEnvelope(new CommandId(9_999), s.CampaignId, s.Phase, s.CurrentTick, s.NextSubmissionSequence, null,
            new PlaceBuildServiceCommand(kind, cell, turns)))?.Message;

    [TestMethod]
    public void AToiletOnlyNeedsRoomForItselfAndItsDoorstepNotAWholeQueue()
    {
        var s = WithOneToilet(Drafted());
        // The bar out on the east grass, square on, with room around it.
        var bar = new GridCell(160, 120);
        Accept(s, new MoveBuildServiceCommand("drinks", bar, 0));
        // Beside the bar, with the door facing it: the old ten-place lane ran straight through the bar.
        var beside = new GridCell(bar.X + 8, bar.Z - 4);
        Assert.IsNull(Place(s, BuildServiceKind.Toilet, beside, 2));
        // The cubicle itself still can't overlap anything.
        StringAssert.Contains(Place(s, BuildServiceKind.Toilet, bar, 2), "overlaps");
    }

    [TestMethod]
    public void TheAudienceAreaInFrontOfTheStageStaysClearOfServicesButNotBins()
    {
        var (seed, index) = SeedOffering("another-round");
        var s = WithOneToilet(Drafted(seed, index));
        var crowd = TraversalGrid.WorldToCell(-7_000, 11_000);
        Assert.IsTrue(GameSession.InAudienceArea(crowd));
        foreach (var kind in new[] { BuildServiceKind.WaterTap, BuildServiceKind.Toilet })
            StringAssert.Contains(Place(s, kind, crowd, 0), "audience area");
        Assert.IsNull(Place(s, BuildServiceKind.Bin, crowd, 0), "A bin can stand among the crowd.");
        Assert.IsNull(Place(s, BuildServiceKind.WaterTap, TraversalGrid.WorldToCell(-7_000, 21_000), 0), "Just beyond it is fine.");
    }

    [TestMethod]
    public void NothingIsKeptClearForRestUntilFirstAidIsPlaced()
    {
        var s = WithOneToilet(Drafted());
        Assert.IsTrue(Send(s, new RemoveBuildServiceCommand("first-aid")).IsAccepted);
        // With no tent there's no rest area, so the old fallback spot round MedicalRestCell is ordinary grass to build on.
        var legacy = GameSession.MedicalRestCell;
        bool OnLegacy(GridCell cell) => Math.Abs(cell.X - legacy.X) <= 1 && Math.Abs(cell.Z - legacy.Z) <= 1;
        var placed = (from x in Enumerable.Range(legacy.X - 3, 7) from z in Enumerable.Range(legacy.Z - 3, 7) from turns in Enumerable.Range(0, 4)
                      let cell = new GridCell(x, z)
                      where GameSession.BuildFootprint(new("probe", BuildServiceKind.Toilet, cell, turns)).Any(OnLegacy)
                      select Place(s, BuildServiceKind.Toilet, cell, turns)).FirstOrDefault(result => result is null, "none");
        Assert.IsNull(placed, "A toilet can stand where the rest area would be while there's no first aid.");
    }

    [TestMethod]
    public void TapsNeedMuchTheSameRoomWhicheverWayTheyAreSpaced()
    {
        var (seed, index) = SeedOffering("another-round");
        var s = Drafted(seed, index);
        // The main tap on its old spot, just east of backstage's barriers, square on, with open grass east of it
        // (the default's loos stand there).
        Accept(s, new RemoveBuildServiceCommand("toilet.main"));
        Accept(s, new RemoveBuildServiceCommand("toilet.extra-1"));
        Accept(s, new MoveBuildServiceCommand("water.main", GameSession.MedicalWaterCell, 0));
        var main = s.CaptureBuildPlacements().Single(p => p.Id == "water.main").Cell;
        string? At(int dx, int dz) => Place(s, BuildServiceKind.WaterTap, new GridCell(main.X + dx, main.Z + dz), 0);
        // Along a grid axis (a screen diagonal) and along a grid diagonal (screen up-down or left-right):
        // 2.5 m and about 2.8 m apart are both fine, where the square berth wanted 3.5 m on the diagonal.
        // Eastward: backstage's barriers stand just west of the main tap.
        Assert.IsNull(At(5, 0));
        Assert.IsNull(At(4, 4));
        Assert.IsNotNull(At(3, 3), "Still not on top of each other.");
    }

    [TestMethod]
    public void AStallCantOverlapTheCornerOfTheBigWaterMain()
    {
        var s = Ready();
        // The main tap on its old spot by backstage, square on, clear of the audience area.
        Accept(s, new MoveBuildServiceCommand("water.main", GameSession.MedicalWaterCell, 0));
        Accept(s, new StartPreparedEditionCommand());
        var tap = MainTap(s);
        SetTap(s, tap with { GeometryVersion = 0 }); // The big water main: solid over 7×7.
        var vendor = s.CaptureVendors().Single(v => v.Id == "drinks");
        // The bar's footprint corner (-3, -2) lands exactly on the main's corner cell (+3, +3).
        var proposed = vendor with { Cell = new GridCell(tap.Cell.X + 6, tap.Cell.Z + 5), QuarterTurns = 0 };
        var error = (string?)typeof(GameSession).GetMethod("ImmersionPlacementError", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(s, [proposed]);
        StringAssert.Contains(error, "obstacle");
    }

    [TestMethod]
    public void NobodyNewPicksAToiletWhoseQueueHasNoRoomToGrow()
    {
        var s = WithoutFaults(Started());
        s.AdvanceWithoutSnapshot(2_000);
        var guests = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed)
            .Select(p => p.AgentId).Where(id => s.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletStage == ToiletVisitStage.None).Take(3).ToArray();
        Assert.AreEqual(3, guests.Length);
        var toilet = s.CaptureToilets().First();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var mutate = typeof(GameSession).GetMethod("MutatePerson", flags)!;
        // One already queuing, one on the way, and a queue boxed in after two places.
        mutate.Invoke(s, [guests[0], (Action<Person>)(p => { p.ToiletStage = ToiletVisitStage.Queued; p.ToiletId = toilet.Id; p.ToiletChoice = ToiletVisitKind.Wee; })]);
        mutate.Invoke(s, [guests[1], (Action<Person>)(p => { p.ToiletStage = ToiletVisitStage.Approaching; p.ToiletId = toilet.Id; p.ToiletChoice = ToiletVisitKind.Wee; })]);
        var doorstep = GameSession.ToiletQueueCell(toilet, 0);
        var second = GameSession.ToiletQueueCell(toilet with { QueueCells = null }, 1);
        int Ticks(ToiletFacility t) => (int)typeof(GameSession).GetMethod("LightToiletTicks", flags)!
            .Invoke(s, [guests[2], ToiletVisitKind.Wee, t, doorstep, true])!;
        var boxedIn = toilet with { Queue = [guests[0]], QueueCells = [doorstep, second] };
        Assert.AreEqual(int.MaxValue, Ticks(boxedIn), "Two places, both spoken for: no room for a third.");
        // Room to spare for everyone already on their way there, whoever else that may be on this day.
        var roomy = boxedIn with { QueueCells = [doorstep, second, .. Enumerable.Range(1, 8).Select(i => second with { X = second.X + 2 * i })] };
        Assert.AreNotEqual(int.MaxValue, Ticks(roomy), "With a spare place it can be chosen.");
    }

    [TestMethod]
    public void WhoeverIsNearestTheEndOfTheLineTakesTheNextPlace()
    {
        var s = WithoutFaults(Started());
        s.AdvanceWithoutSnapshot(2_000);
        // A free loo and a pair where the later arrival on the list stands far nearer it than the earlier one. The loos stand
        // by the stage, where the first arrivals gather, so wait for the field to fill out enough to have such a pair.
        ToiletFacility toilet = null!; (ulong, ulong)? pair = null;
        for (var wait = 0; wait < 40 && pair is null; wait++)
        {
            if (wait > 0) s.AdvanceWithoutSnapshot(200);
            var nav = s.CaptureSnapshot().NavigationAgents.ToDictionary(a => a.Id.Value);
            var idle = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed)
                .Select(p => p.AgentId).Where(id => s.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletStage == ToiletVisitStage.None).ToArray();
            // Free means nobody else on their way there either: with a bigger crowd someone often is, and they'd take a place first.
            var heading = s.CaptureImmersion()!.People.Where(p => p.ToiletStage != ToiletVisitStage.None).Select(p => p.ToiletId).ToHashSet();
            foreach (var free in s.CaptureToilets().Where(t => t.Queue.Length == 0 && t.OwnerId is null && !heading.Contains(t.Id)))
            {
                var door = TraversalGrid.CellCentre(GameSession.ToiletQueueCell(free, 0));
                long Distance(ulong id) => Math.Abs((long)nav[id].XMillimetres - door.XMillimetres) + Math.Abs((long)nav[id].ZMillimetres - door.ZMillimetres);
                pair = (from a in idle from b in idle where a < b && Distance(a) > Distance(b) + 8_000 select ((ulong, ulong)?)(a, b)).FirstOrDefault();
                if (pair is not null) { toilet = free; break; }
            }
        }
        Assert.IsNotNull(pair, "A far and a near guest for a free loo.");
        var (far, near) = pair.Value;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var mutate = typeof(GameSession).GetMethod("MutatePerson", flags)!;
        var destination = typeof(GameSession).GetMethod("ApplyAgentDestination", flags)!;
        foreach (var id in new[] { far, near })
        {
            mutate.Invoke(s, [id, (Action<Person>)(p => { p.ToiletStage = ToiletVisitStage.Approaching; p.ToiletId = toilet.Id; p.ToiletChoice = ToiletVisitKind.Wee; })]);
            destination.Invoke(s, [new EntityId(id), new SetAgentDestinationCommand(GameSession.ToiletQueueCell(toilet, 0), "toilet.approach"), false]);
        }
        s.AdvanceWithoutSnapshot(1);
        var after = s.CaptureSnapshot().NavigationAgents.ToDictionary(a => a.Id.Value);
        var grown = s.CaptureToilets().Single(t => t.Id == toilet.Id);
        Assert.AreEqual(GameSession.ToiletQueueCell(grown, 0), after[near].Destination, "The one nearly there goes to the door...");
        Assert.AreEqual(GameSession.ToiletQueueCell(grown, 1), after[far].Destination, "...not past the queue to its far end and back.");
    }

    [TestMethod]
    public void AToiletQueueGrowsFromItsDoorstepAsPeopleArriveAndSurvivesASave()
    {
        var s = Started();
        Assert.IsTrue(s.CaptureToilets().All(t => t.QueueCells is { Length: 1 } cells && cells[0] == GameSession.ToiletQueueCell(t, 0)),
            "Each toilet starts with just its doorstep.");
        var longest = 1;
        for (var guard = 0; guard < 400 && longest < 3; guard++)
        {
            s.AdvanceWithoutSnapshot(80);
            foreach (var toilet in s.CaptureToilets()) longest = Math.Max(longest, toilet.QueueCells!.Length);
        }
        Assert.IsTrue(longest >= 3, $"A queue formed and grew out from the door ({longest} places).");
        Restored(s);
    }
}

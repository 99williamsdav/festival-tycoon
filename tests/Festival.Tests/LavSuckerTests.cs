using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class LavSuckerTests
{
    private static (GameSession Session, string ToiletId) WithAFullToilet()
    {
        var s = WithoutFaults(Started());
        s.AdvanceWithoutSnapshot(2_000);
        var toilet = s.CaptureToilets().First(t => t.OwnerId is null && t.Queue.Length == 0);
        var wees = (toilet.CapacityMillilitres - toilet.UsedMillilitres) / ToiletRules.WeeMillilitres + toilet.WeeCount;
        SetToilet(s, toilet with { WeeCount = wees });
        Assert.IsTrue(s.CaptureToilets().Single(t => t.Id == toilet.Id).IsFull);
        return (s, toilet.Id);
    }

    [TestMethod]
    public void DavComesForTwentyFivePoundsEmptiesAFullLooAndDrivesOffAgainAcrossASave()
    {
        var (s, id) = WithAFullToilet();
        var cash = s.CaptureSnapshot().FestivalFinances.Single().CashPennies;
        var sent = Send(s, new CallLavSuckerCommand(id));
        Assert.IsTrue(sent.IsAccepted, sent.Message);
        Assert.AreEqual(cash - LavSuckerRules.FeePennies, s.CaptureSnapshot().FestivalFinances.Single().CashPennies, "Paid up front.");
        Assert.IsFalse(Send(s, new CallLavSuckerCommand(id)).IsAccepted, "Already on the way.");
        s.AdvanceWithoutSnapshot(400);
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        s = restored.Session!;
        var pumped = false;
        for (var guard = 0; guard < 600 && s.CaptureLavSucker()!.Calls.Single().Stage != LavSuckerStage.Gone; guard++)
        {
            s.AdvanceWithoutSnapshot(80);
            if (s.CaptureLavSucker()!.Calls.Single().Stage == LavSuckerStage.Pumping)
            {
                pumped = true;
                Assert.IsTrue(s.ToiletBeingEmptied(id), "Nobody goes in while the hose is on.");
            }
        }
        Assert.IsTrue(pumped, "The tanker got there and pumped.");
        Assert.AreEqual(LavSuckerStage.Gone, s.CaptureLavSucker()!.Calls.Single().Stage, "And drove off again.");
        var after = s.CaptureToilets().Single(t => t.Id == id);
        Assert.IsFalse(after.IsFull, "Back in service.");
        Assert.IsTrue(after.UsedMillilitres < after.CapacityMillilitres / 4, "Nearly empty: just what's been used since.");
        var restoredAfter = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restoredAfter.IsSuccess, "The paid call reconciles in the books: " + restoredAfter.Error);
    }

    [TestMethod]
    public void DavKeepsToTheFarmTrackUntilHeHasToTurnOffForTheLoo()
    {
        var (s, id) = WithAFullToilet();
        Assert.IsTrue(Send(s, new CallLavSuckerCommand(id)).IsAccepted);
        var route = s.CaptureLavSucker()!.Calls.Single().Route;
        var grid = (TraversalGrid)typeof(GameSession).GetField("_traversalGrid", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(s)!;
        // On the track from the gate, then one turn off across the grass: never back onto the track once he's left it.
        var onTrack = route.TakeWhile(cell => grid.Get(cell).Surface == GroundSurface.VehicleTrack).Count();
        Assert.IsTrue(onTrack >= route.Length / 2, $"{onTrack} of {route.Length} cells on the track.");
        Assert.IsFalse(route.Skip(onTrack).Any(cell => grid.Get(cell).Surface == GroundSurface.VehicleTrack && cell != route[^1]), "Left the track once.");
        AssertUpTheMiddle(route.Take(onTrack).ToArray(), "On the way in");

        // And back down the middle on the way out.
        for (var guard = 0; guard < 600 && s.CaptureLavSucker()!.Calls.Single().Stage != LavSuckerStage.Leaving; guard++) s.AdvanceWithoutSnapshot(80);
        var home = s.CaptureLavSucker()!.Calls.Single().Route;
        Assert.AreEqual(LavSuckerRules.Gate, home[^1], "Heading for the gate.");
        var homeOnTrack = home.Reverse().TakeWhile(cell => grid.Get(cell).Surface == GroundSurface.VehicleTrack).Count();
        AssertUpTheMiddle(home.Reverse().Take(homeOnTrack).ToArray(), "On the way out");
    }

    /// <summary>
    /// A leg of track from the gate, gate first: within a cell of the centre line (x 127/128 on the 124–131 track)
    /// until it turns off, then only the few cells of a straight run across to the verge.
    /// </summary>
    private static void AssertUpTheMiddle(GridCell[] fromGate, string leg)
    {
        static bool Middle(GridCell cell) => cell.X is >= 126 and <= 129;
        var middle = fromGate.TakeWhile(Middle).Count();
        Assert.IsTrue(middle >= fromGate.Length - 3, $"{leg}: {middle} of {fromGate.Length} track cells by the centre line: " +
            string.Join(" ", fromGate.Select(c => $"{c.X},{c.Z}")));
        var across = fromGate.Skip(middle).Select(c => c.X).ToArray();
        Assert.IsTrue(across.Zip(across.Skip(1)).All(p => Math.Abs(p.Second - p.First) == 1 && Math.Sign(p.Second - p.First) == Math.Sign(across[^1] - across[0])),
            $"{leg}: straight across to the verge.");
    }

    [TestMethod]
    public void DavStopsForSomeoneInFrontOfTheCabAndEdgesOnIfTheyWontMove()
    {
        var (s, id) = WithAFullToilet();
        Assert.IsTrue(Send(s, new CallLavSuckerCommand(id)).IsAccepted);
        s.AdvanceWithoutSnapshot(160);
        var call = s.CaptureLavSucker()!.Calls.Single();
        Assert.IsTrue(call.RouteIndex < call.Route.Length - 4, "Still on the way in.");
        // Plant a guest just ahead of the cab, along the way it's driving, and hold them there.
        var next = TraversalGrid.CellCentre(call.Route[call.RouteIndex + 2]);
        double hx = next.XMillimetres - call.XMillimetres, hz = next.ZMillimetres - call.ZMillimetres, length = Math.Sqrt(hx * hx + hz * hz);
        int ax = call.XMillimetres + (int)(hx / length * (LavSuckerRules.CabFrontMillimetres + 600)), az = call.ZMillimetres + (int)(hz / length * (LavSuckerRules.CabFrontMillimetres + 600));
        var guest = s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed).AgentId;
        var agents = (System.Collections.IDictionary)typeof(GameSession).GetField("_navigationAgents", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(s)!;
        var agent = agents[new EntityId(guest)]!;
        void Hold() { agent.GetType().GetProperty("XMillimetres")!.SetValue(agent, ax); agent.GetType().GetProperty("ZMillimetres")!.SetValue(agent, az); }
        var at = (call.XMillimetres, call.ZMillimetres);
        for (var tick = 0; tick < 400; tick++) { Hold(); s.AdvanceWithoutSnapshot(1); }
        var waiting = s.CaptureLavSucker()!.Calls.Single();
        // Unhindered it would have covered 15 m in that time; held up, it gets no further than a pace before stopping.
        var crept = Math.Sqrt(Math.Pow(waiting.XMillimetres - at.XMillimetres, 2) + Math.Pow(waiting.ZMillimetres - at.ZMillimetres, 2));
        Assert.IsTrue(crept < 1_000, $"Stopped short of them, not {crept:0} mm on.");
        Assert.IsTrue(waiting.BlockedTicks > 300, $"Held up {waiting.BlockedTicks} ticks.");
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, "A held-up tanker saves: " + restored.Error);
        for (var tick = 0; tick < LavSuckerRules.PatienceTicks; tick++) { Hold(); s.AdvanceWithoutSnapshot(1); }
        Assert.AreNotEqual((waiting.XMillimetres, waiting.ZMillimetres), (s.CaptureLavSucker()!.Calls.Single().XMillimetres, s.CaptureLavSucker()!.Calls.Single().ZMillimetres), "Edges on after ten seconds.");
    }

    private static T Private<T>(GameSession s, string name, params object[] args) => (T)typeof(GameSession)
        .GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(s, args)!;

    /// <summary>
    /// The user (13 Oct): Dav always went to the same spot by the loo, its door side, so a loo facing away from the track had
    /// him drive round to park behind it. He now parks in whichever of its eight bays he fits in and is the shortest drive from
    /// the gate (ties to the lower cell), and sucks from that side. Checked on the default layout's loos (doors facing the gate)
    /// and with the main loo turned to face away from it.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DavParksOnTheNearSideOfEachLooWithHisHoseOnIt(bool turnedAway)
    {
        var s = Ready();
        // Turned round where it stands: its door to the south, away from the gate at the north end of the track.
        if (turnedAway)
        {
            var main = s.CapturePreparation()!.BuildPlacements.Single(item => item.Id == "toilet.main");
            Accept(s, new MoveBuildServiceCommand("toilet.main", main.Cell, 0));
        }
        Accept(s, new StartPreparedEditionCommand());
        s = WithoutFaults(s);
        s.AdvanceWithoutSnapshot(2_000);
        var grid = (TraversalGrid)typeof(GameSession).GetField("_traversalGrid", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(s)!;
        var cost = Private<int[]>(s, "LavRoadCost", grid);
        var facingAway = 0;
        foreach (var toilet in s.CaptureToilets())
        {
            SetToilet(s, s.CaptureToilets().Single(t => t.Id == toilet.Id) with { WeeCount = toilet.WeeCount + 1 });
            Assert.IsTrue(Send(s, new CallLavSuckerCommand(toilet.Id)).IsAccepted);
            var call = s.CaptureLavSucker()!.Calls.Single(c => c.ToiletId == toilet.Id);
            var bay = GameSession.LavSuckerBayCell(toilet, call.BaySide);
            Assert.AreEqual(bay, call.Route[^1], $"{toilet.Id}: drives to its bay.");
            Assert.IsTrue(Private<bool>(s, "LavSuckerBayFits", toilet, call.BaySide), $"{toilet.Id}: the lorry fits there.");
            // No bay it fits in is a shorter drive from the gate.
            long Drive(int side)
            {
                var path = DeterministicPathfinder.FindPath(grid, LavSuckerRules.Gate, GameSession.LavSuckerBayCell(toilet, side), cost);
                return path.Found ? Private<long>(s, "DrivingDistance", (object)path.Path.ToArray()) : long.MaxValue;
            }
            var chosen = Drive(call.BaySide);
            for (var side = 0; side < LavSuckerRules.BaySides; side++)
                if (side != call.BaySide && Private<bool>(s, "LavSuckerBayFits", toilet, side))
                    Assert.IsTrue(Drive(side) > chosen || Drive(side) == chosen && GameSession.LavSuckerBayCell(toilet, side).CompareTo(bay) > 0,
                        $"{toilet.Id}: bay {side} ({Drive(side)} mm) beats bay {call.BaySide} ({chosen} mm).");
            // A loo whose door faces away from the gate (to the south) is parked behind it, on the gate's side, not at its door.
            var door = GameSession.RotateWaterOffset(new(0, -1), toilet.QuarterTurns);
            if (door.Z < 0)
            {
                facingAway++;
                Assert.IsTrue(bay.Z > toilet.Cell.Z, $"{toilet.Id} faces away from the gate: parked at {bay}, loo at {toilet.Cell}.");
                Assert.IsTrue(GameSession.LavSuckerBayCell(toilet, 0).Z < toilet.Cell.Z, "Where it always used to park: the far, door side.");
            }
        }
        Assert.AreEqual(turnedAway ? 1 : 0, facingAway, "The default layout's loos face the gate; the turned one doesn't.");
        // Parked and pumping: the hose runs from the lorry's back end to the side of the loo facing it.
        for (var guard = 0; guard < 600 && s.CaptureLavSucker()!.Calls.Any(c => c.Stage != LavSuckerStage.Pumping); guard++) s.AdvanceWithoutSnapshot(20);
        foreach (var call in s.CaptureLavSucker()!.Calls)
        {
            Assert.AreEqual(LavSuckerStage.Pumping, call.Stage);
            var toilet = s.CaptureToilets().Single(t => t.Id == call.ToiletId);
            var (fromX, fromZ, toX, toZ) = s.LavSuckerHose(call)!.Value;
            var loo = TraversalGrid.CellCentre(toilet.Cell);
            var onLoo = TraversalGrid.WorldToCell(toX, toZ);
            Assert.AreEqual(1, Math.Max(Math.Abs(onLoo.X - toilet.Cell.X), Math.Abs(onLoo.Z - toilet.Cell.Z)), "The hose ends on the loo's wall.");
            Assert.IsTrue(Math.Sqrt(Math.Pow(fromX - toX, 2) + Math.Pow(fromZ - toZ, 2)) < 6_000, "Within the hose's reach.");
            Assert.IsTrue(Math.Pow(toX - loo.XMillimetres, 2) + Math.Pow(toZ - loo.ZMillimetres, 2) < Math.Pow(fromX - loo.XMillimetres, 2) + Math.Pow(fromZ - loo.ZMillimetres, 2),
                "It points from the lorry at the loo.");
            Assert.AreEqual((TraversalGrid.CellCentre(call.Route[^1]).XMillimetres, TraversalGrid.CellCentre(call.Route[^1]).ZMillimetres), (call.XMillimetres, call.ZMillimetres));
            // The turned loo's hose comes off its back wall, toward the gate, where the lorry stands.
            if (GameSession.RotateWaterOffset(new(0, -1), toilet.QuarterTurns).Z < 0) Assert.IsTrue(onLoo.Z >= toilet.Cell.Z, $"Hose on {onLoo}, loo {toilet.Cell}.");
        }
        // Restore keeps it strict: a parked lorry in a bay its route doesn't end at, or a bay that doesn't exist, is refused.
        var saved = s.CapturePersistenceSnapshot();
        Assert.IsTrue(GameSession.Restore(saved).IsSuccess, GameSession.Restore(saved).Error);
        var parked = saved.LavSucker!.Calls[0];
        foreach (var side in new[] { parked.BaySide % 7 + 1, LavSuckerRules.BaySides, -1 })
            Assert.AreEqual("Emptying service call invalid.", GameSession.Restore(saved with
                { LavSucker = saved.LavSucker with { Calls = [parked with { BaySide = side }, .. saved.LavSucker.Calls[1..]] } }).Error, $"bay {side}");
    }

    [TestMethod]
    public void AnEmptyLooNeedsNoCallAndDavWantsPayingUpFront()
    {
        var s = WithoutFaults(Started());
        s.AdvanceWithoutSnapshot(800);
        var empty = s.CaptureToilets().First(t => t.UsedMillilitres == 0);
        Assert.IsFalse(Send(s, new CallLavSuckerCommand(empty.Id)).IsAccepted, "Nothing to empty.");
        StringAssert.Contains(s.LavSuckerUnavailable(empty.Id), "empty");
    }
}

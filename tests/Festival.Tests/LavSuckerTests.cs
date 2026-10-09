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

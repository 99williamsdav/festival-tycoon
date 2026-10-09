using System.Reflection;
using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class CowTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static GameSession WithCowsLoose()
    {
        var s = WithoutFaults(Started());
        s.AdvanceWithoutSnapshot(2_000);
        // Break the gate now rather than wait on the dice; the fault fixture otherwise holds every fault back.
        var cows = (CowsSnapshot)typeof(GameSession).GetField("_cows", Private)!.GetValue(s)!;
        var broken = typeof(GameSession).GetMethod("BreakGate", Private)!.Invoke(s, [cows, s.CapturePreparation()!])!;
        typeof(GameSession).GetField("_cows", Private)!.SetValue(s, broken);
        return Faulty(s);
    }

    /// <summary>The fault-free fixture can't hold a fault in a save, so once one is forced, ordinary faults resume.</summary>
    private static GameSession Faulty(GameSession s)
    {
        var field = typeof(GameSession).GetField("_faults", Private)!;
        field.SetValue(s, ((FaultsSnapshot)field.GetValue(s)!) with { Disabled = false });
        return s;
    }

    [TestMethod]
    public void TheGateGivesWayAndAFewCowsAmbleAboutTheSiteAndSurviveASave()
    {
        var s = WithCowsLoose();
        var cows = s.CaptureCows()!;
        Assert.AreEqual(PastureGateState.Broken, cows.Gate);
        Assert.IsTrue(cows.Loose.Length is >= CowRules.EscapeMin and <= CowRules.EscapeMax, $"{cows.Loose.Length} cows out.");
        var start = cows.Loose.ToDictionary(c => c.Id, c => (c.XMillimetres, c.ZMillimetres));
        s.AdvanceWithoutSnapshot(8_000);
        var later = s.CaptureCows()!.Loose;
        Assert.IsTrue(later.Any(c => (c.XMillimetres, c.ZMillimetres) != start[c.Id]), "They wander.");
        foreach (var cow in later)
        {
            var cell = TraversalGrid.WorldToCell(cow.XMillimetres, cow.ZMillimetres);
            Assert.IsTrue(cell.X is >= CowRules.SiteMinCell - 1 and <= CowRules.SiteMaxCell + 1 && cell.Z is >= CowRules.SiteMinCell - 1 and <= CowRules.SiteMaxCell + 1, $"{cow.Id} stays on site.");
        }
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, "RESTORE: " + restored.Error);
        s.AdvanceWithoutSnapshot(400); restored.Session!.AdvanceWithoutSnapshot(400);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.Session.CaptureSnapshot().AuthoritativeHash, "Deterministic across a load.");
    }

    [TestMethod]
    public void AChewedBarCableShutsTheBarUntilMaintenanceSplicesIt()
    {
        var s = WithoutFaults(Started(20260922, QuietPerk, "maintenance.worker"));
        s.AdvanceWithoutSnapshot(2_000);
        Assert.IsTrue(s.StallPowered("drinks"));
        typeof(GameSession).GetMethod("AddFault", Private)!.Invoke(s, ["cable:test", FacilityFaultKind.ChewedCable, "cable.drinks", 0UL]);
        Faulty(s);
        Assert.IsTrue(s.CableCut("drinks"));
        Assert.IsFalse(s.StallPowered("drinks"), "No power, no pints.");
        Assert.IsTrue(s.StallPowered("food") && s.StagePowered, "Only the bar's lead.");
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, "RESTORE: " + restored.Error);
        for (var guard = 0; guard < 400 && s.CableCut("drinks"); guard++) s.AdvanceWithoutSnapshot(80);
        Assert.IsFalse(s.CableCut("drinks"), "The maintenance worker spliced it.");
        Assert.IsTrue(s.StallPowered("drinks"));
    }

    [TestMethod]
    public void WithoutAMaintenanceWorkerAChewedCableStaysCut()
    {
        var s = WithoutFaults(Started());
        s.AdvanceWithoutSnapshot(2_000);
        Assert.IsNull(s.CaptureEquipment()!.WorkerId);
        typeof(GameSession).GetMethod("AddFault", Private)!.Invoke(s, ["cable:test", FacilityFaultKind.ChewedCable, "cable.food", 0UL]);
        Faulty(s);
        s.AdvanceWithoutSnapshot(8_000);
        Assert.IsTrue(s.CableCut("food"), "Nobody else can splice it.");
        StringAssert.Contains(s.FaultStatus("cable.food"), "needs a maintenance worker");
    }

    [TestMethod]
    public void AChewedGeneratorLeadCutsTheStageAndBothStalls()
    {
        var s = WithoutFaults(Started());
        s.AdvanceWithoutSnapshot(2_000);
        typeof(GameSession).GetMethod("AddFault", Private)!.Invoke(s, ["cable:gen", FacilityFaultKind.ChewedCable, "cable.generator", 0UL]);
        Assert.IsFalse(s.StagePowered);
        Assert.IsFalse(s.StallPowered("drinks") || s.StallPowered("food"));
    }

    [TestMethod]
    public void AStewardSentByThePlayerDrivesACowBackThroughTheGate()
    {
        var s = WithCowsLoose();
        var cow = s.CaptureCows()!.Loose.First();
        var before = s.CaptureCows()!.Loose.Length;
        s.AdvanceWithoutSnapshot(800);
        var sent = Send(s, new HerdCowCommand(cow.Id));
        Assert.IsTrue(sent.IsAccepted, sent.Message);
        Assert.IsFalse(Send(s, new HerdCowCommand(cow.Id)).IsAccepted, "Already on it.");
        s.AdvanceWithoutSnapshot(160);
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, "RESTORE: " + restored.Error);
        s = restored.Session!;
        for (var guard = 0; guard < 600 && s.CaptureCows()!.Loose.Any(c => c.Id == cow.Id); guard++) s.AdvanceWithoutSnapshot(80);
        Assert.IsFalse(s.CaptureCows()!.Loose.Any(c => c.Id == cow.Id), "Back in its field.");
        Assert.AreEqual(before - 1, s.CaptureCows()!.Loose.Length);
    }

    [TestMethod]
    public void NoCowIsHerdedUnlessThePlayerAsks()
    {
        var s = WithCowsLoose();
        var count = s.CaptureCows()!.Loose.Length;
        s.AdvanceWithoutSnapshot(16_000);
        Assert.AreEqual(count, s.CaptureCows()!.Loose.Length, "Stewards leave cows alone until told.");
        Assert.IsTrue(s.CaptureCows()!.Loose.All(c => c.HerderId is null));
    }

    [TestMethod]
    public void AHerdThatRunsFarTooLongGivesUpAndFreesTheSteward()
    {
        var s = WithCowsLoose();
        var cow = s.CaptureCows()!.Loose.First();
        Assert.IsTrue(Send(s, new HerdCowCommand(cow.Id)).IsAccepted);
        var field = typeof(GameSession).GetField("_cows", Private)!;
        var cows = (CowsSnapshot)field.GetValue(s)!;
        var steward = cows.Loose.Single(c => c.Id == cow.Id).HerderId!.Value;
        // As if the steward had been trying, and failing, to reach it for longer than any herd should take.
        field.SetValue(s, cows with { Loose = cows.Loose.Select(c => c.Id == cow.Id ? c with { HerdStartedTick = s.CurrentTick - CowRules.HerdDeadlineTicks - 1 } : c).ToArray() });
        s.AdvanceWithoutSnapshot(1);
        var after = s.CaptureCows()!.Loose.Single(c => c.Id == cow.Id);
        Assert.IsNull(after.HerderId, "The herd's called off.");
        Assert.AreEqual(CowActivity.Grazing, after.Activity);
        Assert.IsNull(typeof(GameSession).GetMethod("StaffUnavailableReason", Private)!.Invoke(s, [steward]), "The steward's free for other work.");
    }

    [TestMethod]
    public void PeopleWalkRoundACowRatherThanThroughIt()
    {
        var s = WithCowsLoose();
        var field = typeof(GameSession).GetField("_cows", Private)!;
        var cows = (CowsSnapshot)field.GetValue(s)!;
        // One cow grazing squarely on open ground, the others back in the field.
        var middle = TraversalGrid.CellCentre(new GridCell(112, 150));
        var cow = cows.Loose[0] with { XMillimetres = middle.XMillimetres, ZMillimetres = middle.ZMillimetres, Activity = CowActivity.Grazing, Route = [], RouteIndex = 0, UntilTick = s.CurrentTick + 100_000 };
        field.SetValue(s, cows with { Loose = [cow] });
        // A guest with nothing else on: not in or heading for a water line, loo or stall, whose place there would call them off
        // the walk; and already holding a listening place, so the audience doesn't send them off to find one mid-walk.
        var medical = s.CaptureMedical()!.Needs.ToDictionary(n => n.AgentId);
        var immersion = s.CaptureImmersion()!.People.ToDictionary(p => p.AgentId);
        var placed = s.CaptureLivePerformance()!.Listeners.Where(l => l.Place is not null).Select(l => l.AgentId).ToHashSet();
        var walker = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed).Select(p => p.AgentId)
            .First(id => placed.Contains(id) && medical[id] is { Intent: MedicalIntent.WatchShow, QueueSlot: null } &&
                immersion[id] is { ToiletStage: ToiletVisitStage.None, VendorId: null });
        // Content and skint for the walk's length, so the activity chooser doesn't call them off to the tap, the loo or a stall.
        typeof(GameSession).GetMethod("MutatePerson", Private)!.Invoke(s, [walker, (Action<Person>)(p => { p.Thirst = 0; p.HeatExposure = 0; p.Hunger = 0; p.ToiletNeed = 0; })]);
        var wallet = ((System.Collections.IDictionary)typeof(GameSession).GetField("_wallets", Private)!.GetValue(s)!)[new EntityId(walker)]!;
        wallet.GetType().GetProperty("CashPennies")!.SetValue(wallet, 0L);
        var agents = (System.Collections.IDictionary)typeof(GameSession).GetField("_navigationAgents", Private)!.GetValue(s)!;
        var agent = agents[new EntityId(walker)]!;
        void Set(string name, object value) => agent.GetType().GetProperty(name)!.SetValue(agent, value);
        var start = TraversalGrid.CellCentre(new GridCell(104, 150));
        Set("XMillimetres", start.XMillimetres); Set("ZMillimetres", start.ZMillimetres);
        Set("SegmentOriginXMillimetres", start.XMillimetres); Set("SegmentOriginZMillimetres", start.ZMillimetres);
        Set("Route", new List<GridCell>()); Set("RouteIndex", 0); Set("SegmentProgressMicrometres", 0);
        typeof(GameSession).GetMethod("ApplyAgentDestination", Private)!.Invoke(s, [new EntityId(walker), new SetAgentDestinationCommand(new GridCell(121, 150), "test.walk"), false]);
        var closest = long.MaxValue; var passed = false; var last = "";
        for (var tick = 0; tick < 1_600; tick++)
        {
            s.AdvanceWithoutSnapshot(1);
            var at = s.CaptureSnapshot().NavigationAgents.Single(a => a.Id.Value == walker);
            last = $"tick {tick}: {at.IntentId} {at.Action} at ({at.XMillimetres},{at.ZMillimetres}) to {at.Destination}";
            if (at.IntentId != "test.walk") break;
            passed |= at.XMillimetres > middle.XMillimetres + 1_000;
            long dx = at.XMillimetres - middle.XMillimetres, dz = at.ZMillimetres - middle.ZMillimetres;
            closest = Math.Min(closest, (long)Math.Sqrt(dx * dx + dz * dz));
        }
        Assert.IsTrue(passed, $"The walk got past the cow ({last}).");
        Assert.IsTrue(closest >= GameSession.SeparationRadiusMillimetres, $"Kept clear of the cow's middle (closest {closest} mm).");
    }
}

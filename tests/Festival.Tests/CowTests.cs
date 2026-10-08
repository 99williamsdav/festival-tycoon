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
}

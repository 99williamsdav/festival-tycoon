using Festival.ContentAdapter;
using Festival.Simulation;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class StewardCleanupPresentationTests
{
    private static void Position(GameSession s, ulong id, GridCell cell)
    {
        var agents = (System.Collections.IDictionary)typeof(GameSession).GetField("_navigationAgents", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
        var nav = agents[new EntityId(id)]!; var point = TraversalGrid.CellCentre(cell);
        void Set(string name, object value) => nav.GetType().GetProperty(name)!.SetValue(nav, value);
        Set("XMillimetres", point.XMillimetres); Set("ZMillimetres", point.ZMillimetres);
        Set("SegmentOriginXMillimetres", point.XMillimetres); Set("SegmentOriginZMillimetres", point.ZMillimetres);
        Set("Route", new List<GridCell>()); Set("RouteIndex", 0); Set("SegmentProgressMicrometres", 0);
        Set("Action", AgentNavigationAction.Arrived); Set("Destination", cell); Set("IntentId", "litter.test-position");
    }
    private static void Invoke(GameSession s, string name, params object[] args) => typeof(GameSession)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, args);
    private static void Mutate(GameSession s, ulong id, Action<Person> edit) => Invoke(s, "MutatePerson", id, edit);
    private static void Time(GameSession s, long tick) => typeof(GameSession).GetProperty("CurrentTick")!.SetValue(s, tick);
    private static (GameSession Session, ulong Worker) Fixture()
    {
        var s = BuildSession.Started(); var worker = s.CaptureDisorder()!.SecurityId;
        Mutate(s, worker, p => { p.Admitted = true; p.Thirst = p.Hunger = p.HeatExposure = p.ToiletNeed = 2000; });
        var cell = s.StaffAssignedPost(worker)!.Value; var pos = TraversalGrid.CellCentre(cell);
        var piece = new WastePiece("labelled-visual-target", 1, ImmersionProduct.Chips, 0,
            WasteLocation.Ground, pos.XMillimetres, pos.ZMillimetres);
        typeof(GameSession).GetField("_litter", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(s,
            new LitterSnapshot(1, [piece], [new(worker, cell, LitterRules.LocalRadiusCells, 6, 2400, false,
                piece.Id, false, cell, 0)]));
        return (s, worker);
    }
    [TestMethod]
    public void PhaseComesOnlyFromAuthoritativeTicksAndDoesNotConsumeWaste()
    {
        var (s, worker) = Fixture();
        Assert.AreEqual(StewardCleanupMode.GroundPickup, StewardCleanupPresentation.Read(s, worker).Mode);
        Time(s, 35); Assert.IsFalse(StewardCleanupPresentation.Read(s, worker).LiftedWaste);
        Time(s, 36); var pose = StewardCleanupPresentation.Read(s, worker); Assert.IsTrue(pose.LiftedWaste);
        Assert.AreEqual(.45, pose.Progress);
        Time(s, 64); Assert.AreEqual(.8, StewardCleanupPresentation.Read(s, worker).Progress);
        BuildSession.Accept(s, new SetPausedCommand(true)); pose = StewardCleanupPresentation.Read(s, worker);
        var hash = s.CaptureSnapshot().AuthoritativeHash; s.AdvanceWithoutSnapshot(400);
        Assert.AreEqual(pose, StewardCleanupPresentation.Read(s, worker)); Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(WasteLocation.Ground, s.CaptureLitter()!.Pieces.Single().Location);
        BuildSession.Accept(s, new SetPausedCommand(false)); Assert.AreEqual(pose, StewardCleanupPresentation.Read(s, worker));
    }
    [TestMethod]
    public void SafetyRetainedFoodCollapseAndDepartureStowWithoutWaitingForCleanupCadence()
    {
        foreach (var reason in new[] { "food", "collapse", "departed", "water", "thirst" })
        {
            var (s, worker) = Fixture(); Time(s, 40);
            Mutate(s, worker, p => { if (reason == "food") p.Held = new("retained", ImmersionProduct.Chips, 0);
                if (reason == "collapse") p.HealthStage = MedicalStage.Collapsed;
                if (reason == "departed") p.Departed = true;
                if (reason == "water") p.Intent = MedicalIntent.SeekWater;
                if (reason == "thirst") p.Thirst = 7000; });
            Assert.AreEqual(StewardCleanupMode.Stowed, StewardCleanupPresentation.Read(s, worker).Mode, reason);
            Assert.AreEqual(1, s.CaptureLitter()!.Pieces.Length); Assert.AreEqual(6, s.CaptureLitter()!.Sweeps.Single().Remaining);
        }
        var (session, id) = Fixture(); Invoke(session, "RecallWorker", id, "Labelled safety response");
        Assert.AreEqual(StewardCleanupMode.Stowed, StewardCleanupPresentation.Read(session, id).Mode);
    }
    [TestMethod]
    public void RealSavedPickupReconstructsTheSamePhaseAndTargetWithoutCosmeticFields()
    {
        var s = BuildSession.WithLitterCorner(BuildSession.Ready());
        BuildSession.Accept(s, new StartPreparedEditionCommand()); var worker = s.CaptureDisorder()!.SecurityId;
        var guest = s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest).AgentId;
        foreach (var id in new[] { guest, worker }) Mutate(s, id, p => { p.Admitted = true; p.Thirst = p.Hunger = p.HeatExposure = p.ToiletNeed = 2000; });
        var post = s.StaffAssignedPost(worker)!.Value; Position(s, guest, new(post.X, post.Z + 4));
        Invoke(s, "CompleteImmersionSale", guest, ImmersionProduct.Chips);
        var duration = GameSession.ImmersionConsumeTicks(ImmersionProduct.Chips); Time(s, duration);
        Mutate(s, guest, p => p.Held = p.Held! with { ConsumedTicks = duration - 1 }); Invoke(s, "AdvanceImmersion");
        Invoke(s, "DropWaste", s.CaptureLitter()!.Pieces.Single(), false);
        BuildSession.Accept(s, new CleanUpCommand(worker));
        Time(s, (s.CurrentTick / 80 + 1) * 80); Invoke(s, "AdvanceLitter");
        Position(s, worker, s.CaptureCleanupSweep(worker)!.Approach!.Value);
        Time(s, s.CurrentTick + 80); Invoke(s, "AdvanceLitter"); Time(s, s.CurrentTick + 40);
        var visual = StewardCleanupPresentation.Read(s, worker); Assert.AreEqual(.5, visual.Progress); Assert.IsTrue(visual.LiftedWaste);
        var restored = BuildSession.Restored(s); Assert.AreEqual(visual, StewardCleanupPresentation.Read(restored, worker));
        Assert.AreEqual(s.CaptureCleanupFooting(worker), restored.CaptureCleanupFooting(worker));
        Assert.AreEqual(1, restored.CaptureLitter()!.Pieces.Count(w => w.Location == WasteLocation.Ground));
        Invoke(restored, "RecallWorker", worker, "Saved cleanup preempted by safety");
        Assert.AreEqual(StewardCleanupMode.Stowed, StewardCleanupPresentation.Read(restored, worker).Mode);
        Assert.AreEqual(WasteLocation.Ground, restored.CaptureWastePiece(visual.TargetId!)!.Location);
    }
    [TestMethod]
    public void ApproachEquipsAndBinEmptyUsesItsExistingThreeSecondClock()
    {
        var (s, worker) = Fixture(); var state = s.CaptureLitter()!;
        void Set(CleanupSweep job) => typeof(GameSession).GetField("_litter", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(s, state with { Sweeps = [job] });
        Set(state.Sweeps[0] with { ActionTick = -1 }); Assert.AreEqual(StewardCleanupMode.Equipped, StewardCleanupPresentation.Read(s, worker).Mode);
        Set(state.Sweeps[0] with { TargetIsBin = true, TargetId = "labelled-bin" }); Time(s, 120);
        var pose = StewardCleanupPresentation.Read(s, worker); Assert.AreEqual(StewardCleanupMode.EmptyingBin, pose.Mode);
        Assert.AreEqual(.5, pose.Progress); Assert.IsFalse(pose.LiftedWaste);
        Set(state.Sweeps[0] with { Remaining = 0 }); Assert.AreEqual(StewardCleanupMode.Stowed, StewardCleanupPresentation.Read(s, worker).Mode);
    }
}

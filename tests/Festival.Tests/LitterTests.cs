using Festival.Simulation;
using System.Collections;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class LitterTests
{
    private static void Invoke(GameSession s, string method, params object[] args) => typeof(GameSession).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, args);
    private static void Mutate(GameSession s, ulong id, Action<Person> edit) => Invoke(s, "MutatePerson", id, edit);
    private static void SetTime(GameSession s, long tick) => typeof(GameSession).GetProperty("CurrentTick")!.SetValue(s, tick);
    private static void SetLitter(GameSession s, LitterSnapshot state) => typeof(GameSession).GetField("_litter", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s, state);
    private static void Position(GameSession s, ulong id, GridCell cell)
    {
        var agents = (IDictionary)typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(s)!;
        var nav = agents[new EntityId(id)]!; var centre = TraversalGrid.CellCentre(cell);
        void Set(string name, object value) => nav.GetType().GetProperty(name)!.SetValue(nav, value);
        Set("XMillimetres", centre.XMillimetres); Set("ZMillimetres", centre.ZMillimetres);
        Set("SegmentOriginXMillimetres", centre.XMillimetres); Set("SegmentOriginZMillimetres", centre.ZMillimetres);
        Set("Route", new List<GridCell>()); Set("RouteIndex", 0); Set("SegmentProgressMicrometres", 0);
        Set("Action", AgentNavigationAction.Arrived); Set("Destination", cell); Set("IntentId", "litter.test-position");
    }
    private static GameSession Open(bool bin = true)
    {
        var s = BuildSession.Ready();
        if (bin) BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, new(118, 166)));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        // Labelled isolated-system fixture: mark actors physically present with quiet needs.
        foreach (var p in s.CapturePreparation()!.People) Mutate(s, p.AgentId, n =>
        { n.Admitted = true; n.Thirst = 2000; n.HeatExposure = 2000; n.Hunger = 2000; n.ToiletNeed = 2000; });
        return s;
    }
    private static ulong Guest(GameSession s) => s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest).AgentId;
    private static void Complete(GameSession s, ulong id, ImmersionProduct product)
    {
        Position(s, id, s.StaffAssignedPost(id) is not null
            ? s.StaffAssignedPost(id)!.Value : new(116, 166));
        Invoke(s, "CompleteImmersionSale", id, product);
        var duration = GameSession.ImmersionConsumeTicks(product);
        SetTime(s, s.CurrentTick + duration);
        Mutate(s, id, p => p.Held = p.Held! with { ConsumedTicks = duration - 1 });
        Invoke(s, "AdvanceImmersion");
        Assert.IsNull(s.CapturePerson(id)!.Held);
    }
    private static void Step(GameSession s)
    {
        SetTime(s, (s.CurrentTick / 80 + 1) * 80); Invoke(s, "AdvanceLitter");
    }
    private static WastePiece Ground(string id, GridCell cell)
    {
        var c = TraversalGrid.CellCentre(cell); return new(id, 1, ImmersionProduct.Chips, 0, WasteLocation.Ground, c.XMillimetres, c.ZMillimetres);
    }
    private static void Fill(GameSession s, int count)
    {
        var bin = s.CaptureBins().Single();
        SetLitter(s, new(1, Enumerable.Range(1, count).Select(i => Ground("fixture:" + i, bin.Cell) with { Location = WasteLocation.Bin, BinId = bin.Id }).ToArray(), []));
    }
    [TestMethod]
    public void PersonalityConvenienceDetourUrgencyAndRolesProduceDifferentDecisions()
    {
        Assert.IsTrue(LitterRules.WillUseBin(5, 80, 0, false, ProtectedPersonRole.Guest));
        Assert.IsFalse(LitterRules.WillUseBin(95, 320, 160, false, ProtectedPersonRole.Guest));
        Assert.IsFalse(LitterRules.WillUseBin(0, 700, 0, false, ProtectedPersonRole.Guest));
        Assert.IsFalse(LitterRules.WillUseBin(5, 80, 1800, false, ProtectedPersonRole.Guest));
        Assert.IsFalse(LitterRules.WillUseBin(0, 0, 0, true, ProtectedPersonRole.Guest));
        Assert.IsTrue(LitterRules.WillUseBin(100, 80, 80, false, ProtectedPersonRole.Staff));
        Assert.AreEqual(LitterRules.Dickishness(1, 3), LitterRules.Dickishness(1, 3));
        Assert.IsTrue(Enumerable.Range(1, 20).Select(i => LitterRules.Dickishness(123, (ulong)i)).Distinct().Count() > 8);
    }
    [TestMethod]
    public void ActualCompletionCreatesOnePieceForEveryConsumingRoleAndPartialCreatesNone()
    {
        var s = Open(false); var guest = Guest(s);
        Invoke(s, "CompleteImmersionSale", guest, ImmersionProduct.SoftDrink); Position(s, guest, new(116, 166));
        Invoke(s, "AdvanceImmersion"); Assert.AreEqual(0, s.CaptureLitter()!.Pieces.Length);
        foreach (var role in Enum.GetValues<ProtectedPersonRole>())
        {
            var id = s.CapturePreparation()!.People.Last(p => p.Role == role).AgentId;
            if (id == guest) Mutate(s, id, p => p.Held = null); // Isolated incomplete item fixture.
            Complete(s, id, ImmersionProduct.Chips);
        }
        Assert.AreEqual(3, s.CaptureLitter()!.Pieces.Length);
        var last = s.CaptureLitter()!.Pieces.Last(); Invoke(s, "RecordCompletedWaste", last.ProducerId, new ImmersionHeldItem(last.Id, last.Product, 0));
        Assert.AreEqual(3, s.CaptureLitter()!.Pieces.Length);
    }
    [TestMethod]
    public void DisposalRequiresArrivalAndActionAndOverflowStillAccepts()
    {
        var s = Open(); var id = s.GetResponseStaff().First(w => w.Role == ResponseRole.Medic).AgentId;
        Complete(s, id, ImmersionProduct.SoftDrink); Position(s, id, new(114, 166));
        Step(s); Step(s); var waste = s.CaptureLitter()!.Pieces.Single(); Assert.IsNotNull(waste.Approach);
        Assert.AreEqual(WasteLocation.Carried, waste.Location); Step(s); Assert.AreEqual(0, s.CaptureBins().Single().Pieces);
        Position(s, id, waste.Approach!.Value); Step(s); Assert.AreEqual(0, s.CaptureBins().Single().Pieces);
        Step(s); Assert.AreEqual(1, s.CaptureBins().Single().Pieces);
        var pieces = s.CaptureLitter()!.Pieces;
        SetLitter(s, new(1, pieces.Concat(Enumerable.Range(1, 25).Select(i => pieces[0] with { Id = "volume:" + i })).ToArray(), []));
        var bin = s.CaptureBins().Single(); Assert.AreEqual(130, bin.FullPercent); Assert.AreEqual(6, bin.ExtraPieces); Assert.IsTrue(bin.Wasps);
        Assert.IsTrue(LitterRules.WillUseBin(0, 80, 0, false, ProtectedPersonRole.Staff));
    }
    [TestMethod]
    public void NoBinAndUrgentDropKeepActualPositionAndReleaseClaim()
    {
        var s = Open(false); var id = Guest(s); Complete(s, id, ImmersionProduct.Chips); Position(s, id, new(133, 123));
        Step(s); Step(s); Step(s); var piece = s.CaptureLitter()!.Pieces.Single();
        Assert.AreEqual(WasteLocation.Ground, piece.Location); Assert.AreEqual(TraversalGrid.CellCentre(new(133, 123)), (piece.XMillimetres, piece.ZMillimetres));
        Assert.IsFalse(s.CaptureClaims(id).HasFlag(PersonClaim.WasteDisposal));
        Complete(s, id, ImmersionProduct.SoftDrink); Mutate(s, id, p => p.ToiletNeed = 9000); Step(s);
        Assert.AreEqual(2, s.CaptureLitter()!.Pieces.Count(w => w.Location == WasteLocation.Ground));
    }
    [TestMethod]
    public void EmptyingThresholdIsExactly90PercentEvenManualAndClearsOverflowWasps()
    {
        var s = Open(); var worker = s.CaptureDisorder()!.SecurityId;
        Fill(s, 17); BuildSession.Accept(s, new CleanUpCommand(worker)); Step(s);
        Assert.AreEqual(17, s.CaptureBins().Single().Pieces); Assert.IsNull(s.CaptureLitter()!.Sweeps.Single().TargetId);
        foreach (var count in new[] { 18, 20, 24 })
        {
            Fill(s, count); BuildSession.Accept(s, new CleanUpCommand(worker)); Step(s);
            var job = s.CaptureLitter()!.Sweeps.Single(); Assert.IsTrue(job.TargetIsBin); Position(s, worker, job.Approach!.Value);
            Step(s); Step(s); Step(s); Assert.AreEqual(count, s.CaptureBins().Single().Pieces);
            Step(s); Assert.AreEqual(0, s.CaptureBins().Single().Pieces); Assert.IsFalse(s.CaptureBins().Single().Wasps);
        }
    }
    [TestMethod]
    public void CleanupInterruptedForSafetyPreservesWasteAndCannotDuplicateTarget()
    {
        var s = Open(); var worker = s.CaptureDisorder()!.SecurityId; var centre = s.StaffAssignedPost(worker)!.Value;
        SetLitter(s, new(1, [Ground("one", new(centre.X, centre.Z + 4))], []));
        BuildSession.Accept(s, new CleanUpCommand(worker)); Step(s); var job = s.CaptureLitter()!.Sweeps.Single(); Assert.AreEqual("one", job.TargetId);
        Invoke(s, "RecallWorker", worker, "test safety response");
        Assert.AreEqual(WasteLocation.Ground, s.CaptureLitter()!.Pieces.Single().Location); Assert.IsFalse(s.CaptureClaims(worker).HasFlag(PersonClaim.Cleanup));
        Assert.IsNull(s.CaptureLitter()!.Sweeps.Single().TargetId);
    }
    [TestMethod]
    public void SweepsStayBoundedAtCurrentPostAndReturnAfterTargetBudget()
    {
        var s = Open(false); var worker = s.CaptureDisorder()!.SecurityId; var centre = s.StaffAssignedPost(worker)!.Value;
        var near = new GridCell(centre.X, centre.Z + 4); var far = new GridCell(centre.X + 40, centre.Z);
        SetLitter(s, new(1, Enumerable.Range(0, 8).Select(i => Ground("near:" + i, near)).Append(Ground("far", far)).ToArray(), []));
        Step(s); Assert.AreEqual(centre, s.CaptureLitter()!.Sweeps.Single().Centre);
        Step(s);
        for (var i = 0; i < 6; i++) { var job = s.CaptureLitter()!.Sweeps.Single(); Assert.IsNotNull(job.Approach, $"Iteration {i}: {job}; pieces {s.CaptureLitter()!.Pieces.Count(w => w.Location == WasteLocation.Ground)}"); Position(s, worker, job.Approach!.Value); Step(s); Step(s); }
        Assert.AreEqual(3, s.CaptureLitter()!.Pieces.Count(w => w.Location == WasteLocation.Ground));
        Assert.AreEqual(0, s.CaptureLitter()!.Sweeps.Single().Remaining);
        Assert.AreEqual(centre, s.CaptureSnapshot().NavigationAgents.Single(n => n.Id.Value == worker).Destination);
    }
    [TestMethod]
    public void GroundAndWaspsGiveBoundedLossAndSafeAvoidancePauseFreezes()
    {
        var s = Open(); var id = Guest(s); var bin = s.CaptureBins().Single(); Position(s, id, new(bin.Cell.X + 3, bin.Cell.Z));
        Fill(s, 20); var p = s.CapturePerson(id)!; Step(s);
        Assert.AreEqual(p.Satisfaction - LitterRules.WaspLoss, s.CapturePerson(id)!.Satisfaction);
        Assert.AreEqual("litter.avoid-wasps", s.CaptureObservation().NavigationAgents.Single(n => n.Id.Value == id).IntentId);
        var c = new GridCell(135, 126); Position(s, id, c);
        SetLitter(s, new(1, Enumerable.Range(0, 1000).Select(i => Ground("large:" + i, c)).ToArray(), []));
        var before = s.CapturePerson(id)!.Satisfaction; Step(s); Assert.AreEqual(before - LitterRules.GroundLossCap, s.CapturePerson(id)!.Satisfaction);
        Assert.AreEqual(1000, s.CaptureLitter()!.Pieces.Count(w => w.Location == WasteLocation.Ground));
        BuildSession.Accept(s, new SetPausedCommand(true)); var hash = s.CaptureSnapshot().AuthoritativeHash; s.AdvanceWithoutSnapshot(200);
        Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash);
    }
    [TestMethod]
    public void SaveDuringPhysicalDisposalAndPickupReplaysAndRejectsMalformedRoutes()
    {
        var s = Open(); var id = s.GetResponseStaff().First(w => w.Role == ResponseRole.Medic).AgentId;
        Complete(s, id, ImmersionProduct.SoftDrink); Position(s, id, new(114, 166));
        Step(s); Step(s); Position(s, id, s.CaptureLitter()!.Pieces.Single().Approach!.Value); Step(s);
        var r = BuildSession.Restored(s); Step(s); Step(r);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, r.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(WasteLocation.Bin, s.CaptureLitter()!.Pieces.Single().Location);
        var saved = s.CapturePersistenceSnapshot();
        Assert.IsFalse(GameSession.Restore(saved with { Litter = saved.Litter! with { Pieces = [saved.Litter.Pieces[0] with { XMillimetres = int.MaxValue }] } }).IsSuccess);

        s = Open(false); id = Guest(s); Complete(s, id, ImmersionProduct.Chips);
        var worker = s.CaptureDisorder()!.SecurityId; var centre = s.StaffAssignedPost(worker)!.Value;
        Position(s, id, new(centre.X, centre.Z + 4)); Invoke(s, "DropWaste", s.CaptureLitter()!.Pieces.Single(), false);
        BuildSession.Accept(s, new CleanUpCommand(worker)); Step(s);
        Position(s, worker, s.CaptureLitter()!.Sweeps.Single().Approach!.Value); Step(s);
        saved = s.CapturePersistenceSnapshot(); r = BuildSession.Restored(s);
        Assert.IsFalse(GameSession.Restore(saved with { Litter = saved.Litter! with { Sweeps = [saved.Litter.Sweeps[0] with { Approach = new(0, 0) }] } }).IsSuccess);
        Step(s); Step(r); Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, r.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(WasteLocation.Removed, s.CaptureLitter()!.Pieces.Single().Location);
    }
    [TestMethod]
    public void TwoStewardsCannotClaimTheSameWaste()
    {
        var s = BuildSession.PlannedWith("extra-pair-of-hands");
        foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        BuildSession.Accept(s, new AcceptPreparationOfferCommand(BuildSession.ExtraId(s, StaffRole.Steward)));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        foreach (var p in s.CapturePreparation()!.People) Mutate(s, p.AgentId, n => { n.Admitted = true; n.Thirst = n.Hunger = n.HeatExposure = n.ToiletNeed = 2000; });
        var workers = s.GetStewardResponses().Select(w => w.WorkerId).ToArray(); var centre = s.StaffAssignedPost(workers[0])!.Value;
        SetLitter(s, new(1, [Ground("one", new(centre.X, centre.Z + 4)), Ground("two", new(centre.X, centre.Z + 5))], []));
        foreach (var worker in workers) BuildSession.Accept(s, new CleanUpCommand(worker));
        Step(s); var targets = s.CaptureLitter()!.Sweeps.Select(j => j.TargetId).ToArray();
        Assert.AreEqual(2, targets.Length); Assert.IsTrue(targets.All(t => t is not null)); Assert.AreEqual(2, targets.Distinct().Count());
    }
    [TestMethod]
    public void CompletedPackagingDropsAtomicallyAtPhysicalExitAndSaveStillLoads()
    {
        var s = Open(); var id = Guest(s); Complete(s, id, ImmersionProduct.Chips);
        var prep = s.CapturePreparation()!;
        SetTime(s, prep.StartedTick + s.PreparedEditionDurationTicks);
        typeof(GameSession).GetProperty("Phase")!.SetValue(s, SessionPhase.Egress);
        typeof(GameSession).GetProperty("PreparationView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s, prep with { Status = PreparationStatus.Departing });
        var index = Array.FindIndex(prep.People, p => p.AgentId == id);
        var exit = (GridCell)typeof(GameSession).GetMethod("PreparedStart", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [index])!;
        Position(s, id, exit);
        var navs = (IDictionary)typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(s)!;
        var nav = navs[new EntityId(id)]!; nav.GetType().GetProperty("IntentId")!.SetValue(nav, "edition.departure");
        foreach (var person in prep.People.Where(p => p.AgentId != id)) Mutate(s, person.AgentId, p => p.Departed = true);
        Invoke(s, "FinalizeFestivalDeparture");
        Assert.IsTrue(s.CapturePerson(id)!.Departed); var waste = s.CaptureLitter()!.Pieces.Single();
        Assert.AreEqual(WasteLocation.Ground, waste.Location); Assert.AreEqual(TraversalGrid.CellCentre(exit), (waste.XMillimetres, waste.ZMillimetres));
        BuildSession.Restored(s);
        SetTime(s, s.CurrentTick + 1); Invoke(s, "FinalizeFestivalDeparture");
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus); BuildSession.Restored(s);
        Assert.AreEqual(1500L, s.CompletedFestivalAccounts!.OperatingExpenses.Single(e => e.Label == "Litter bin × 1").AmountPennies);
        Assert.IsTrue(s.CompletedFestivalAccounts.Reconciles);
    }
    [TestMethod]
    public void FatalRetryKeepsBinsAndClearsAllWasteAndSweepClaims()
    {
        var s = Open(); Complete(s, Guest(s), ImmersionProduct.Chips);
        var layout = s.CaptureBuildPlacements().ToArray();
        var id = s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Performer).AgentId;
        Mutate(s, id, p => { p.HealthStage = MedicalStage.Critical; p.Thirst = 10000; p.HeatExposure = 10000;
            p.Intent = MedicalIntent.Collapsed; p.HealthWarningTick = s.CurrentTick - 4000;
            p.HealthCollapseTick = s.CurrentTick - GameSession.MedicalDeathDelayTicks; p.HealthCriticalTick = s.CurrentTick - 1; });
        for (var i = 0; i < 6000 && s.PreparedStatus == PreparationStatus.Running; i++) s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(PreparationStatus.Failed, s.PreparedStatus);
        BuildSession.Accept(s, new SpendCouncilFavourCommand());
        CollectionAssert.AreEqual(layout, s.CaptureBuildPlacements().ToArray());
        Assert.AreEqual(0, s.CaptureLitter()!.Pieces.Length); Assert.AreEqual(0, s.CaptureLitter()!.Sweeps.Length);
        BuildSession.Restored(s);
    }
    [TestMethod]
    public void BinDraftChargeAndCurrentVersionRestoreAreExact()
    {
        var s = BuildSession.Ready(); var before = s.PreparationPlanCost;
        BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, new(118, 130)));
        BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, new(140, 130)));
        Assert.AreEqual(before + 3000, s.PreparationPlanCost); BuildSession.Restored(s);
        BuildSession.Accept(s, new StartPreparedEditionCommand()); BuildSession.Restored(s);
        Assert.AreEqual(33000L, s.CapturePreparation()!.SetupPayments!.Single().BuildCostPennies);
        Assert.IsFalse(BuildSession.Send(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, new(142, 120))).IsAccepted);
        // Real completion fixture records a ledger-backed purchase and enough elapsed time.
        var id = Guest(s); Mutate(s, id, p => { p.Admitted = true; p.ToiletNeed = 2000; });
        Complete(s, id, ImmersionProduct.Chips); var r = BuildSession.Restored(s);
        Step(s); Step(r); Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, r.CaptureSnapshot().AuthoritativeHash);
        var saved = s.CapturePersistenceSnapshot(); Assert.IsFalse(GameSession.Restore(saved with { Litter = null }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { Litter = saved.Litter! with { Pieces = [saved.Litter.Pieces[0], saved.Litter.Pieces[0]] } }).IsSuccess);
    }

    [TestMethod]
    public void AManualSweepStartedMidSecondEndsOnALitterSecondAndAlwaysSaves()
    {
        var s = Open(); var worker = s.GetStewardResponses()[0].WorkerId;
        s.AdvanceWithoutSnapshot(41 - (int)(s.CurrentTick % LitterRules.SecondTicks) + LitterRules.SecondTicks);
        Assert.AreNotEqual(0, s.CurrentTick % LitterRules.SecondTicks, "The command lands mid-second.");
        var issued = s.CurrentTick;
        BuildSession.Accept(s, new CleanUpCommand(worker));
        var until = s.CaptureLitter()!.Sweeps.Single(j => j.WorkerId == worker).UntilTick;
        Assert.AreEqual(0, until % LitterRules.SecondTicks);
        Assert.IsTrue(until >= issued + LitterRules.ManualDurationTicks && until < issued + LitterRules.ManualDurationTicks + LitterRules.SecondTicks);
        // Skip to just before the deadline with the sweep still working, then save on each tick across it.
        SetTime(s, until - 3);
        for (var tick = 0; tick < 6; tick++)
        {
            s.AdvanceWithoutSnapshot(1);
            var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
            Assert.IsTrue(restored.IsSuccess, $"tick {s.CurrentTick}: {restored.Error}");
        }
        Assert.AreEqual(0, s.CaptureLitter()!.Sweeps.Single(j => j.WorkerId == worker).Remaining, "The sweep ended at its deadline.");
    }
}

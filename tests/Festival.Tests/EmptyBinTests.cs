using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class EmptyBinTests
{
    [TestMethod]
    public void EmptyNowSendsAStewardToEmptyABinWhateverItsLevel()
    {
        var s = BuildSession.WithoutBins(BuildSession.Ready(20261007));
        BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, new(110, 140)));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        // No burst tap to call the steward away mid-job (a fault outranks litter).
        BuildSession.WithoutFaults(s);
        Assert.IsFalse(BuildSession.Send(s, new EmptyBinCommand("bin.1")).IsAccepted, "Nothing to empty yet.");
        while (s.CaptureBins().Single().Pieces == 0 && s.CurrentTick < 40_000) s.AdvanceWithoutSnapshot(80);
        var bin = s.CaptureBins().Single();
        Assert.IsTrue(bin.Pieces > 0 && !bin.CanEmpty, "A bin well short of 90%, which stewards would otherwise leave.");
        var sent = BuildSession.Send(s, new EmptyBinCommand(bin.Id));
        Assert.IsTrue(sent.IsAccepted, sent.Message);
        Assert.IsFalse(BuildSession.Send(s, new EmptyBinCommand(bin.Id)).IsAccepted, "Already on their way.");
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.IsTrue(restored.Session!.CaptureLitter()!.Sweeps.Single(j => j.TargetId == bin.Id).Ordered, "The order survives a save.");
        // Carry on from the save, so the emptying itself is proven after a load.
        s = restored.Session;
        // Watch for the moment the steward finishes: the bin is empty then (a busy crowd soon starts refilling it).
        var emptied = false;
        for (var guard = 0; guard < 3_000 && !emptied; guard++)
        {
            s.AdvanceWithoutSnapshot(1);
            emptied = s.CaptureBins().Single().Pieces == 0;
        }
        Assert.IsTrue(emptied, "Emptied.");
        // The finished trip keeps its long ordered deadline after it stops being an order; a save then still loads.
        var after = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(after.IsSuccess, after.Error);
    }

    [TestMethod]
    public void AFullBinFarFromTheStewardPostStillGetsEmptied()
    {
        var s = BuildSession.WithoutBins(BuildSession.Ready(20261007));
        var far = new GridCell(150, 160);
        BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, far));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        BuildSession.WithoutFaults(s);
        var post = s.CaptureResponsePost(ResponseRole.Steward).Cell;
        Assert.IsTrue((post.X - far.X) * (post.X - far.X) + (post.Z - far.Z) * (post.Z - far.Z) > LitterRules.LocalRadiusCells * LitterRules.LocalRadiusCells,
            "Out of the post's local sweep.");
        s.AdvanceWithoutSnapshot(1_600);
        // Fill it past full, as a busy crowd far from the post would.
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var field = typeof(GameSession).GetField("_litter", flags)!;
        var litter = (LitterSnapshot)field.GetValue(s)!;
        var centre = TraversalGrid.CellCentre(far);
        var producer = s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest).AgentId;
        var pieces = Enumerable.Range(0, 26).Select(i => new WastePiece($"test-waste:{i}", producer, ImmersionProduct.SoftDrink, s.CurrentTick,
            WasteLocation.Bin, centre.XMillimetres, centre.ZMillimetres, "bin.1")).ToArray();
        field.SetValue(s, litter with { Pieces = [.. litter.Pieces, .. pieces] });
        Assert.IsTrue(s.CaptureBins().Single().Pieces * 100 >= 130 * LitterRules.BinCapacity, "130% full.");
        var emptied = false;
        for (var guard = 0; guard < 12_000 && !emptied; guard += 80)
        {
            s.AdvanceWithoutSnapshot(80);
            emptied = s.CaptureBins().Single().Pieces < LitterRules.BinCapacity / 2;
        }
        Assert.IsTrue(emptied, "A steward came and emptied it without being asked.");
    }

    [TestMethod]
    public void EmptyNowWaitsForTheFirstStewardToComeFreeAndSurvivesASave()
    {
        var s = BuildSession.WithoutBins(BuildSession.Ready(20261007));
        BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, new(110, 140)));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        BuildSession.WithoutFaults(s);
        while (s.CaptureBins().Single().Pieces == 0 && s.CurrentTick < 40_000) s.AdvanceWithoutSnapshot(80);
        var bin = s.CaptureBins().Single();
        // Every steward out on a sweep: nobody is free to go now.
        foreach (var steward in s.GetStewardResponses()) BuildSession.Accept(s, new CleanUpCommand(steward.WorkerId));
        Assert.IsNull(s.BinEmptier(bin.Id, out var why), "All busy.");
        Assert.IsTrue(s.BinEmptyQueueable(bin.Id, out _), why);
        BuildSession.Accept(s, new EmptyBinCommand(bin.Id));
        Assert.IsTrue(s.BinEmptyRequested(bin.Id), "The order waits.");
        Assert.IsFalse(BuildSession.Send(s, new EmptyBinCommand(bin.Id)).IsAccepted, "Already waiting.");

        // The waiting order survives a save, and the restored game carries on with it.
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.IsTrue(restored.Session!.BinEmptyRequested(bin.Id), "Still waiting after a load.");
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.Session.CaptureSnapshot().AuthoritativeHash);

        // It can be cancelled, and asked for again.
        BuildSession.Accept(s, new EmptyBinCommand(bin.Id, Cancel: true));
        Assert.IsFalse(s.BinEmptyRequested(bin.Id), "Cancelled.");
        Assert.IsFalse(BuildSession.Send(s, new EmptyBinCommand(bin.Id, Cancel: true)).IsAccepted, "Nothing left to cancel.");
        s = restored.Session;

        // The first steward to come free takes it, ahead of any routine sweep, and empties it.
        var taken = false;
        for (var guard = 0; guard < 12_000 && !taken; guard++)
        {
            s.AdvanceWithoutSnapshot(1);
            taken = s.CaptureLitter()!.Sweeps.Any(j => j.Remaining > 0 && j.Ordered && j.TargetId == bin.Id);
        }
        Assert.IsTrue(taken, "A steward took the waiting order once free.");
        Assert.IsFalse(s.BinEmptyRequested(bin.Id), "No longer waiting once taken.");
        var emptied = false;
        for (var guard = 0; guard < 6_000 && !emptied; guard++)
        {
            s.AdvanceWithoutSnapshot(1);
            emptied = s.CaptureBins().Single().Pieces == 0;
        }
        Assert.IsTrue(emptied, "Emptied.");
    }
    [TestMethod]
    public void EmptyNowOnAWaitingBinBetweenLitterSecondsServesTheOrderAndStillSaves()
    {
        var s = BuildSession.WithoutBins(BuildSession.Ready(20261007));
        BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, new(110, 140)));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        BuildSession.WithoutFaults(s);
        while (s.CaptureBins().Single().Pieces == 0 && s.CurrentTick < 40_000) s.AdvanceWithoutSnapshot(80);
        var bin = s.CaptureBins().Single();
        foreach (var steward in s.GetStewardResponses()) BuildSession.Accept(s, new CleanUpCommand(steward.WorkerId));
        BuildSession.Accept(s, new EmptyBinCommand(bin.Id));
        Assert.IsTrue(s.BinEmptyRequested(bin.Id), "Queued.");
        // Off a litter second, so the queue isn't served before the player acts.
        while (s.CurrentTick % LitterRules.SecondTicks == 0) s.AdvanceWithoutSnapshot(1);
        // A steward comes free mid-second: their sweep ends.
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var endSweep = typeof(GameSession).GetMethod("EndSweep", flags)!;
        foreach (var sweep in s.CaptureLitter()!.Sweeps.Where(j => j.Remaining > 0).ToArray()) endSweep.Invoke(s, [sweep, false]);
        Assert.IsNotNull(s.BinEmptier(bin.Id, out var why), why);
        Assert.IsTrue(s.BinEmptyRequested(bin.Id), "Still waiting: no litter second has passed.");
        // The player clicks Empty now on it before the queue gets a look in.
        BuildSession.Accept(s, new EmptyBinCommand(bin.Id));
        Assert.IsFalse(s.BinEmptyRequested(bin.Id), "The steward going now serves the waiting order.");
        Assert.IsTrue(s.CaptureLitter()!.Sweeps.Any(j => j.Remaining > 0 && j.Ordered && j.TargetId == bin.Id), "On their way.");
        Assert.AreNotEqual(0, s.CurrentTick % LitterRules.SecondTicks, "Saved inside the window before the next litter second.");
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
    }

    [TestMethod]
    public void AWaitingOrderIsServedTheSameWayOnReplay()
    {
        string Run()
        {
            var s = BuildSession.WithoutBins(BuildSession.Ready(20261007));
            BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, new(110, 140)));
            BuildSession.Accept(s, new StartPreparedEditionCommand());
            BuildSession.WithoutFaults(s);
            while (s.CaptureBins().Single().Pieces == 0 && s.CurrentTick < 40_000) s.AdvanceWithoutSnapshot(80);
            foreach (var steward in s.GetStewardResponses()) BuildSession.Accept(s, new CleanUpCommand(steward.WorkerId));
            BuildSession.Accept(s, new EmptyBinCommand("bin.1"));
            s.AdvanceWithoutSnapshot(2_400);
            return s.CaptureSnapshot().AuthoritativeHash;
        }
        Assert.AreEqual(Run(), Run());
    }
}

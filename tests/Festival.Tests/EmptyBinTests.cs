using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class EmptyBinTests
{
    [TestMethod]
    public void EmptyNowSendsAStewardToEmptyABinWhateverItsLevel()
    {
        var s = BuildSession.Ready(20261007);
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
    }

    [TestMethod]
    public void AFullBinFarFromTheStewardPostStillGetsEmptied()
    {
        var s = BuildSession.Ready(20261007);
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
}

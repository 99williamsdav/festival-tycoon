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
        // Watch for the moment the steward finishes: the bin is empty then (a busy crowd soon starts refilling it).
        var emptied = false;
        for (var guard = 0; guard < 3_000 && !emptied; guard++)
        {
            s.AdvanceWithoutSnapshot(1);
            emptied = s.CaptureBins().Single().Pieces == 0;
        }
        Assert.IsTrue(emptied, "Emptied.");
    }
}

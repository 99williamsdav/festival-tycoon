using System.Reflection;
using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class GroundTests
{
    private static int TotalWear(GameSession s) => s.CaptureGround()!.Wear.Sum();

    [TestMethod]
    public void FeetWearTheFieldOnlyOnceTheFestivalIsOn()
    {
        var s = Ready();
        s.AdvanceWithoutSnapshot(400);
        Assert.AreEqual(0, TotalWear(s), "Nobody wears the grass while the festival is being prepared.");
        Accept(s, new StartPreparedEditionCommand());
        s.AdvanceWithoutSnapshot(4_000);
        var ground = s.CaptureGround()!;
        Assert.IsTrue(ground.Cells.Length > 0, "Guests walking in have worn the grass.");
        Assert.IsTrue(ground.Cells.All(index => GroundRules.InField(new GridCell(index % TraversalGrid.Width, index / TraversalGrid.Width))),
            "Only the field inside the hedges wears.");
        CollectionAssert.AreEqual(ground.Cells.Order().ToArray(), ground.Cells, "Saved in ascending cell order.");
        var cell = new GridCell(ground.Cells[0] % TraversalGrid.Width, ground.Cells[0] / TraversalGrid.Width);
        Assert.AreEqual(ground.Wear[0], s.GroundWearAt(cell));
        Restored(s);
    }

    [TestMethod]
    public void StandingCrowdsWearDeeperThanPassingFeet()
    {
        var s = Started();
        s.AdvanceWithoutSnapshot(20_000);
        var wear = s.CaptureGround()!.Wear;
        Assert.IsTrue(wear.Max() >= GroundRules.BareEarthWear, "Somewhere people stand has gone to bare earth by mid-afternoon.");
        Assert.IsTrue(wear.Count(w => w < 10) > wear.Length / 2, "Most worn cells are only lightly passed over.");
    }

    [TestMethod]
    public void ARetriedWeekendStartsOnFreshGrass()
    {
        var s = Started();
        s.AdvanceWithoutSnapshot(4_000);
        Assert.IsTrue(TotalWear(s) > 0);
        typeof(GameSession).GetMethod("RetryPreparedWeekend", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, null);
        Assert.AreEqual(0, TotalWear(s));
    }

    [TestMethod]
    public void ASaveWithImpossibleWearIsRejected()
    {
        var s = Started();
        s.AdvanceWithoutSnapshot(4_000);
        var snapshot = s.CapturePersistenceSnapshot();
        var ground = snapshot.Ground!;
        string? Error(GroundSnapshot bad) => GameSession.Restore(snapshot with { Ground = bad }).Error;
        StringAssert.Contains(Error(ground with { Cells = ground.Cells.Reverse().ToArray() }), "ascending");
        StringAssert.Contains(Error(ground with { Cells = [0], Wear = [1] }), "outside the field");
        StringAssert.Contains(Error(ground with { Wear = ground.Wear.Select(_ => GroundRules.MaximumWear + 1).ToArray() }), "out of range");
        StringAssert.Contains(Error(ground with { Wear = [] }), "shape");
        StringAssert.Contains(GameSession.Restore(snapshot with { Ground = null }).Error, "requires ground state");
    }
}

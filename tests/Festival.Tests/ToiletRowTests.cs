using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class ToiletRowTests
{
    [TestMethod]
    public void ToiletsCanStandSideBySideAndBothServeTheDay()
    {
        var s = Planned();
        var main = s.CapturePreparation()!.BuildPlacements.Single(p => p.Id == "toilet.main");
        // Cubicles touching, sharing the walkway in front of their doors.
        var placed = Send(s, new PlaceBuildServiceCommand(BuildServiceKind.Toilet, new(main.Cell.X + 3, main.Cell.Z), main.QuarterTurns));
        Assert.IsTrue(placed.IsAccepted, placed.Message);
        var second = s.CapturePreparation()!.BuildPlacements.Single(p => p.Kind == BuildServiceKind.Toilet && p.Id != "toilet.main");
        Assert.IsFalse(Send(s, new MoveBuildServiceCommand(second.Id, new(main.Cell.X + 2, main.Cell.Z), main.QuarterTurns)).IsAccepted, "Cubicles can't overlap.");
        Assert.IsFalse(Send(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, new(main.Cell.X + 1, main.Cell.Z - 3))).IsAccepted,
            "Nothing else may stand in front of a loo door.");
        foreach (var hire in Crew(s)) Accept(s, hire);
        Accept(s, new StartPreparedEditionCommand());
        var used = new HashSet<string>();
        for (var k = 0; k < 30; k++)
        {
            s.AdvanceWithoutSnapshot(800);
            foreach (var toilet in s.CaptureToilets()) if (toilet.OwnerId is not null) used.Add(toilet.Id);
        }
        Assert.AreEqual(2, used.Count, "Both loos in the row are used.");
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
    }
}

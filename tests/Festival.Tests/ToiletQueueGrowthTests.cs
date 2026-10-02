using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class ToiletQueueGrowthTests
{
    private static string? Place(GameSession s, BuildServiceKind kind, GridCell cell, int turns) =>
        s.ValidateCommand(new CommandEnvelope(new CommandId(9_999), s.CampaignId, s.Phase, s.CurrentTick, s.NextSubmissionSequence, null,
            new PlaceBuildServiceCommand(kind, cell, turns)))?.Message;

    [TestMethod]
    public void AToiletOnlyNeedsRoomForItselfAndItsDoorstepNotAWholeQueue()
    {
        var s = Drafted();
        Accept(s, new UseDefaultBuildLayoutCommand());
        // Beside the bar, with the door facing it: the old ten-place lane ran straight through the bar.
        var beside = TraversalGrid.WorldToCell(20_000, -6_000);
        Assert.IsNull(Place(s, BuildServiceKind.Toilet, beside, 2));
        // The cubicle itself still can't overlap anything.
        StringAssert.Contains(Place(s, BuildServiceKind.Toilet, TraversalGrid.WorldToCell(16_000, -4_000), 2), "overlaps");
    }

    [TestMethod]
    public void AToiletQueueGrowsFromItsDoorstepAsPeopleArriveAndSurvivesASave()
    {
        var s = Started();
        Assert.IsTrue(s.CaptureToilets().All(t => t.QueueCells is { Length: 1 } cells && cells[0] == GameSession.ToiletQueueCell(t, 0)),
            "Each toilet starts with just its doorstep.");
        var longest = 1;
        for (var guard = 0; guard < 400 && longest < 3; guard++)
        {
            s.AdvanceWithoutSnapshot(80);
            foreach (var toilet in s.CaptureToilets()) longest = Math.Max(longest, toilet.QueueCells!.Length);
        }
        Assert.IsTrue(longest >= 3, $"A queue formed and grew out from the door ({longest} places).");
        Restored(s);
    }
}

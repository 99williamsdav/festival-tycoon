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
    public void TheAudienceAreaInFrontOfTheStageStaysClearOfServicesButNotBins()
    {
        var s = Drafted();
        Accept(s, new UseDefaultBuildLayoutCommand());
        var crowd = TraversalGrid.WorldToCell(-7_000, 11_000);
        Assert.IsTrue(GameSession.InAudienceArea(crowd));
        foreach (var kind in new[] { BuildServiceKind.WaterTap, BuildServiceKind.Toilet })
            StringAssert.Contains(Place(s, kind, crowd, 0), "audience area");
        Assert.IsNull(Place(s, BuildServiceKind.Bin, crowd, 0), "A bin can stand among the crowd.");
        Assert.IsNull(Place(s, BuildServiceKind.WaterTap, TraversalGrid.WorldToCell(-7_000, 21_000), 0), "Just beyond it is fine.");
    }

    [TestMethod]
    public void NobodyNewPicksAToiletWhoseQueueHasNoRoomToGrow()
    {
        var s = WithoutFaults(Started());
        s.AdvanceWithoutSnapshot(2_000);
        var guests = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed)
            .Select(p => p.AgentId).Where(id => s.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletStage == ToiletVisitStage.None).Take(3).ToArray();
        Assert.AreEqual(3, guests.Length);
        var toilet = s.CaptureToilets().First();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var mutate = typeof(GameSession).GetMethod("MutatePerson", flags)!;
        // One already queuing, one on the way, and a queue boxed in after two places.
        mutate.Invoke(s, [guests[0], (Action<Person>)(p => { p.ToiletStage = ToiletVisitStage.Queued; p.ToiletId = toilet.Id; p.ToiletChoice = ToiletVisitKind.Wee; })]);
        mutate.Invoke(s, [guests[1], (Action<Person>)(p => { p.ToiletStage = ToiletVisitStage.Approaching; p.ToiletId = toilet.Id; p.ToiletChoice = ToiletVisitKind.Wee; })]);
        var doorstep = GameSession.ToiletQueueCell(toilet, 0);
        var second = GameSession.ToiletQueueCell(toilet with { QueueCells = null }, 1);
        int Ticks(ToiletFacility t) => (int)typeof(GameSession).GetMethod("LightToiletTicks", flags)!
            .Invoke(s, [guests[2], ToiletVisitKind.Wee, t, doorstep, true])!;
        var boxedIn = toilet with { Queue = [guests[0]], QueueCells = [doorstep, second] };
        Assert.AreEqual(int.MaxValue, Ticks(boxedIn), "Two places, both spoken for: no room for a third.");
        var roomy = boxedIn with { QueueCells = [doorstep, second, second with { X = second.X + 2 }] };
        Assert.AreNotEqual(int.MaxValue, Ticks(roomy), "With a spare place it can be chosen.");
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

using Festival.Simulation;
using System.Diagnostics;

namespace Festival.Tests;

[TestClass]
public sealed class MedicalCuePlannerTests
{
    private static MedicalSnapshot Baseline() => GameSession.CreateMedicalCampaign(20260925).CaptureMedical()!;

    [TestMethod]
    public void RoutineChoicesAreTransitionTriggeredGloballySpacedAndNotRepeatedPerTick()
    {
        var baseline = Baseline();
        var waterId = baseline.Needs[0].AgentId;
        var tradeoffId = baseline.AtRiskGuestId;
        var planner = new MedicalCuePlanner();
        planner.Reset(baseline, 0);
        var choices = baseline with { Needs = baseline.Needs.Select(item => item.AgentId == waterId
            ? item with { Intent = MedicalIntent.SeekWater, LastDecisionTick = 80 }
            : item.AgentId == tradeoffId
                ? item with { Thirst = 9_100, Reason = "Watching band: music 19000 vs water 10900 incl. travel/wait", LastDecisionTick = 80 }
                : item).ToArray() };
        var first = planner.Observe(choices, 80);
        Assert.AreEqual(1, first.Count(item => !item.Urgent));
        Assert.AreEqual(tradeoffId, first.Single().AgentId);
        StringAssert.Contains(first.Single().Text, "want to miss this band");
        for (var tick = 81; tick < 200; tick++)
            Assert.AreEqual(1, planner.Observe(choices, tick).Count(item => !item.Urgent));
        var second = planner.Observe(choices, 200);
        Assert.AreEqual(1, second.Count(item => !item.Urgent));
        for (var tick = 201; tick < 320; tick++)
            Assert.IsTrue(planner.Observe(choices, tick).Count(item => !item.Urgent) <= MedicalCuePlanner.MaximumVisibleRoutine);
        var water = planner.Observe(choices, 320);
        Assert.AreEqual(1, water.Count);
        Assert.AreEqual(waterId, water.Single().AgentId);
        Assert.AreEqual("I'm going to get water", water.Single().Text);
        Assert.AreEqual(0, planner.Observe(choices, 560).Count(item => !item.Urgent));
        Assert.AreEqual(0, planner.Observe(choices, 600).Count(item => !item.Urgent));
        var noTradeoff = choices with { Needs = choices.Needs.Select(item => item.AgentId == tradeoffId
            ? item with { Reason = "Watching happily", LastDecisionTick = 610 } : item).ToArray() };
        planner.Observe(noTradeoff, 610);
        var repeatedTradeoff = choices with { Needs = choices.Needs.Select(item => item.AgentId == tradeoffId
            ? item with { LastDecisionTick = 611 } : item).ToArray() };
        Assert.IsFalse(planner.Observe(repeatedTradeoff, 611).Any(item => item.AgentId == tradeoffId));
        Assert.IsFalse(planner.Observe(repeatedTradeoff, 719).Any(item => item.AgentId == tradeoffId));
        Assert.IsTrue(planner.Observe(repeatedTradeoff, 720).Any(item => item.AgentId == tradeoffId),
            "A later real choice may bark once the per-person cooldown expires.");
        planner.Reset(repeatedTradeoff, 721);
        Assert.AreEqual(0, planner.Observe(repeatedTradeoff, 721).Count,
            "Loading the current choice must not replay routine dialogue.");

        var thresholdOnly = baseline with { Needs = baseline.Needs.Select(item => item.AgentId == tradeoffId
            ? item with { Thirst = 6_499, Reason = "Watching band: music 19000 vs water 10900 incl. travel/wait" }
            : item).ToArray() };
        planner.Reset(thresholdOnly, 0);
        thresholdOnly = thresholdOnly with { Needs = thresholdOnly.Needs.Select(item => item.AgentId == tradeoffId
            ? item with { Thirst = 6_500 } : item).ToArray() };
        Assert.AreEqual(0, planner.Observe(thresholdOnly, 1).Count,
            "Thirst crossing the presentation threshold without a new decision must not bark.");
    }

    [TestMethod]
    public void UrgentPersonCuePreemptsRoutineBudgetPersistsAndReappearsAfterRestore()
    {
        var baseline = Baseline();
        var id = baseline.AtRiskGuestId;
        var planner = new MedicalCuePlanner();
        planner.Reset(baseline, 0);
        var tradeoff = baseline with { Needs = baseline.Needs.Select(item => item.AgentId == id
                ? item with { Thirst = 9_100, Reason = "Watching band: music 19000 vs water 10900 incl. travel/wait", LastDecisionTick = 80 }
                : item).ToArray() };
        Assert.IsTrue(planner.Observe(tradeoff, 80).Any(item => item.AgentId == id && !item.Urgent));
        var distress = tradeoff with { Stage = MedicalStage.Distress };
        var cue = planner.Observe(distress, 100).Single(item => item.AgentId == id);
        Assert.IsTrue(cue.Urgent);
        StringAssert.Contains(cue.Text, "collapse");
        Assert.AreEqual(0, planner.Observe(distress, 101).Count(item => !item.Urgent));
        var performer = distress.Needs.First(item => item.Profile == MedicalNeedProfile.Performer);
        distress = distress with { Needs = distress.Needs.Select(item => item.AgentId == performer.AgentId
            ? item with { Stage = MedicalStage.Distress } : item).ToArray() };
        Assert.AreEqual(2, planner.Observe(distress, 102).Count(item => item.Urgent));
        Assert.IsTrue(planner.Observe(distress, 1_600).Any(item => item.AgentId == id && item.Urgent));
        planner.Reset(distress, 1_600); // A load seeds routine transitions, but urgency reconstructs from state.
        Assert.IsTrue(planner.Observe(distress, 1_600).Any(item => item.AgentId == id && item.Urgent));
        Assert.AreEqual(0, planner.Observe(distress with { Stage = MedicalStage.Treated }, 1_601)
            .Count(item => item.AgentId == id));

        var started = GameSession.CreateMedicalCampaign(20260925);
        foreach (var offer in new[] { "act.folk", "staff.steward", "equipment.buy" })
            Assert.IsTrue(Send(started, new AcceptPreparationOfferCommand(offer)).IsAccepted);
        Assert.IsTrue(Send(started, new StartPreparedEditionCommand()).IsAccepted);
        while (started.CaptureMedical()!.Stage != MedicalStage.Distress && started.CurrentTick < 3_000)
            started.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalStage.Distress, started.CaptureMedical()!.Stage);
        var restored = GameSession.Restore(started.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        planner.Reset(restored.Session!.CaptureMedical(), restored.Session.CurrentTick);
        Assert.IsTrue(planner.Observe(restored.Session.CaptureMedical()!, restored.Session.CurrentTick)
            .Any(item => item.AgentId == id && item.Urgent));
    }

    [TestMethod]
    public void FiftyProtectedPeopleStayWithinRoutineBudgetDuringRepeatedObservation()
    {
        var baseline = Baseline();
        var fifty = baseline with { Needs = Enumerable.Range(0, 50)
            .Select(index => baseline.Needs[0] with { AgentId = (ulong)(10_000 + index), Intent = MedicalIntent.WatchShow })
            .ToArray() };
        var planner = new MedicalCuePlanner();
        planner.Reset(fifty, 0);
        fifty = fifty with { Needs = fifty.Needs.Select(item => item with { Intent = MedicalIntent.SeekWater,
            LastDecisionTick = 1 }).ToArray() };
        var watch = Stopwatch.StartNew();
        for (var tick = 1; tick <= 1_000; tick++)
            Assert.IsTrue(planner.Observe(fifty, tick).Count(item => !item.Urgent) <= MedicalCuePlanner.MaximumVisibleRoutine);
        watch.Stop();
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(2), $"50-person cue observation was too slow: {watch.Elapsed}.");
        Console.WriteLine($"medical_cue_planner_50_people_1000_observations_ms={watch.Elapsed.TotalMilliseconds:0.###}");
    }

    private static CommandResult Send(GameSession session, SessionCommand command) => session.Execute(new(
        new CommandId(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase,
        session.CurrentTick, session.NextSubmissionSequence, null, command));
}

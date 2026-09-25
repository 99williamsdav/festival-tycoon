using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class DisorderCuePlannerTests
{
    private static (DisorderSnapshot Disorder, MedicalSnapshot Medical) Baseline()
    {
        var session = GameSession.CreateDisorderCampaign(20260925);
        return (session.CaptureDisorder()!, session.CaptureMedical()!);
    }

    [TestMethod]
    public void ComplaintShoutsAreTransitionTriggeredThrottledAndNotReplayedOnLoad()
    {
        var (baseline, medical) = Baseline();
        var firstId = baseline.People[0].AgentId;
        var secondId = baseline.People[1].AgentId;
        var planner = new DisorderCuePlanner();
        planner.Reset(baseline, 0);
        var complaining = baseline with { People = baseline.People.Select(item => item.AgentId == firstId
            ? item with { Stage = DisorderStage.Complaint, Grievance = DisorderGrievance.MusicCutoff, StageTick = 100 }
            : item.AgentId == secondId
                ? item with { Stage = DisorderStage.Complaint, Grievance = DisorderGrievance.WaterWait, StageTick = 100 }
                : item).ToArray() };
        var first = planner.Observe(complaining, medical, 100);
        Assert.AreEqual(1, first.Count);
        Assert.IsTrue(first.Single().Text is "What the hell?!" or "This is ridiculous!" or "Hurry up!" or "This queue is ridiculous!");
        Assert.AreEqual(first.Single(), planner.Observe(complaining, medical, 101).Single());
        Assert.IsTrue(planner.Observe(complaining, medical, 220).Count <= 1);
        Assert.AreEqual(secondId, planner.Observe(complaining, medical, 500).Single().AgentId,
            "The queued water complaint follows the first shout instead of overlapping it.");
        Assert.AreEqual(0, planner.Observe(complaining, medical, 701).Count);
        planner.Reset(complaining, 701);
        Assert.AreEqual(0, planner.Observe(complaining, medical, 701).Count,
            "Loading a current complaint must not replay its transition shout.");
    }

    [TestMethod]
    public void FightMarksBothRecordedParticipantsAndMedicalUrgencyWinsAfterRestore()
    {
        var (baseline, medical) = Baseline();
        var a = baseline.People[0].AgentId;
        var b = baseline.People[1].AgentId;
        var fight = baseline with { People = baseline.People.Select(item => item.AgentId == a
            ? item with { Stage = DisorderStage.Fight, OpponentId = b, StageTick = 900 }
            : item.AgentId == b
                ? item with { Stage = DisorderStage.Fight, OpponentId = a, StageTick = 900 }
                : item).ToArray() };
        var planner = new DisorderCuePlanner();
        planner.Reset(fight, 900);
        CollectionAssert.AreEquivalent(new[] { a, b }, planner.Observe(fight, medical, 900)
            .Where(item => item.Kind == DisorderCueKind.Fight && item.Text == "FIGHT").Select(item => item.AgentId).ToArray());
        planner.Reset(fight, 901);
        Assert.AreEqual(2, planner.Observe(fight, medical, 901).Count);
        var urgent = medical with { Needs = medical.Needs.Select(item => item.AgentId == a
            ? item with { Stage = MedicalStage.Distress } : item).ToArray() };
        Assert.AreEqual(b, planner.Observe(fight, urgent, 902).Single().AgentId,
            "An urgent medical cue owns the injured participant's overhead label.");

        var failedCalming = baseline with { People = baseline.People.Select(item => item.AgentId == a
            ? item with { Stage = DisorderStage.Argument, OpponentId = baseline.SecurityId, StageTick = 1_000 }
            : item).ToArray() };
        planner.Reset(failedCalming, 1_000);
        CollectionAssert.AreEquivalent(new[] { a, baseline.SecurityId }, planner.Observe(failedCalming, medical, 1_000)
            .Where(item => item.Kind == DisorderCueKind.Argument).Select(item => item.AgentId).ToArray(),
            "An argument with a recorded steward counterpart must label both sides.");
    }

    [TestMethod]
    public void FiftyPeopleBoundOrdinaryTextWhileActualPairsRemainVisible()
    {
        var (baseline, medical) = Baseline();
        var people = Enumerable.Range(0, 50).Select(index => baseline.People[0] with
        {
            AgentId = (ulong)(10_000 + index), Stage = DisorderStage.Complaint,
            Grievance = DisorderGrievance.WaterWait, StageTick = 1
        }).ToArray();
        var fifty = baseline with { People = people };
        var planner = new DisorderCuePlanner();
        planner.Reset(baseline, 0);
        for (var tick = 1; tick <= 1_000; tick++)
            Assert.IsTrue(planner.Observe(fifty, medical, tick).Count(item => item.Kind == DisorderCueKind.Shout)
                <= DisorderCuePlanner.MaximumVisibleShouts);
        var arguing = fifty with { People = people.Select(item => item with { Stage = DisorderStage.Argument }).ToArray() };
        planner.Reset(arguing, 1_001);
        Assert.AreEqual(DisorderCuePlanner.MaximumUnpairedArguments,
            planner.Observe(arguing, medical, 1_001).Count(item => item.Kind == DisorderCueKind.Argument));
        var a = people[0].AgentId;
        var b = people[1].AgentId;
        var paired = arguing with { People = arguing.People.Select(item => item.AgentId == a
            ? item with { Stage = DisorderStage.Fight, OpponentId = b }
            : item.AgentId == b ? item with { Stage = DisorderStage.Fight, OpponentId = a } : item).ToArray() };
        var cues = planner.Observe(paired, medical, 1_002);
        CollectionAssert.AreEquivalent(new[] { a, b }, cues.Where(item => item.Kind == DisorderCueKind.Fight)
            .Select(item => item.AgentId).ToArray());
        Assert.AreEqual(2, cues.Count, "An active fight must take precedence over nearby arguments and ordinary shouts.");
    }

    [TestMethod]
    public void NaturalSavedFightReconstructsBothOverheadCuesWithoutReplayState()
    {
        var session = GameSession.CreateDisorderCampaign(20260922);
        foreach (var offer in new[] { "act.folk", "staff.steward", "equipment.buy" })
            Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand(offer)).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        Assert.IsTrue(Send(session, new MedicalCommand(session.CaptureMedical()!.AtRiskGuestId,
            MedicalAction.GuideToRest)).IsAccepted);
        while (session.CaptureLivePerformance()!.Stage != LiveSetStage.Live && session.CurrentTick < 4_000)
            session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(LiveSetStage.Live, session.CaptureLivePerformance()!.Stage);
        Assert.IsTrue(Send(session, new EquipmentCommand(EquipmentAction.Isolate)).IsAccepted);
        while (!session.CaptureDisorder()!.People.Any(item => item.Stage == DisorderStage.Fight &&
               item.OpponentId != session.CaptureDisorder()!.SecurityId) && session.CurrentTick < 7_000)
            session.AdvanceWithoutSnapshot(1);
        var fighter = session.CaptureDisorder()!.People.First(item => item.Stage == DisorderStage.Fight &&
            item.OpponentId != session.CaptureDisorder()!.SecurityId);
        var hash = session.CaptureSnapshot().AuthoritativeHash;
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(hash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
        var planner = new DisorderCuePlanner();
        planner.Reset(restored.Session.CaptureDisorder(), restored.Session.CurrentTick);
        var cues = planner.Observe(restored.Session.CaptureDisorder()!, restored.Session.CaptureMedical(),
            restored.Session.CurrentTick);
        CollectionAssert.AreEquivalent(new[] { fighter.AgentId, fighter.OpponentId!.Value },
            cues.Select(item => item.AgentId).ToArray());
        Assert.IsTrue(cues.All(item => item.Kind == DisorderCueKind.Fight && item.Text == "FIGHT"));
    }

    private static CommandResult Send(GameSession session, SessionCommand command) => session.Execute(new(
        new CommandId(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase,
        session.CurrentTick, session.NextSubmissionSequence, null, command));
}

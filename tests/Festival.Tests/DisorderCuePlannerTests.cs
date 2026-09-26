using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class DisorderCuePlannerTests
{
    [TestMethod]
    public void DelayedBandQuestionsBecomeAngryWithExistingThrottlesAndNeverReplayAfterLoad()
    {
        var (baseline, medical) = Baseline();
        var id = baseline.People[0].AgentId;
        var planner = new DisorderCuePlanner();
        planner.Reset(baseline, 799);
        var waiting = baseline with { People = baseline.People.Select(person => person with
            { Grievance = DisorderGrievance.BandDelayed, GrievanceTick = 800 }).ToArray() };
        var mild = planner.Observe(waiting, medical, 800);
        Assert.AreEqual(1, mild.Count);
        Assert.IsTrue(mild.Single().Text is "Where is the band?" or "When are they starting?" or "Are they ready yet?" or "What's the hold-up?" or "Shouldn't the band be on?");
        Assert.IsTrue(planner.Observe(waiting, medical, 920).Count <= DisorderCuePlanner.MaximumVisibleShouts);
        var angry = waiting with { People = waiting.People.Select(person => person.AgentId == id
            ? person with { Stage = DisorderStage.Agitated, StageTick = 1_440, Pressure = 3_000 } : person).ToArray() };
        var stronger = planner.Observe(angry, medical, 1_440);
        Assert.AreEqual(id, stronger.Single().AgentId);
        Assert.IsTrue(stronger.Single().Text is "Start the music!" or "We've waited long enough!" or "Where the hell is the band?!" or "This delay is ridiculous!" or "Get on with it!");
        var musicStarted = angry with { People = angry.People.Select(person => person with { Grievance = DisorderGrievance.None }).ToArray() };
        Assert.AreEqual(0, planner.Observe(musicStarted, medical, 1_441).Count,
            "A late-band phrase ends when actual music resolves its cause, even while pressure is still calming.");
        planner.Reset(angry, 1_441);
        Assert.AreEqual(0, planner.Observe(angry, medical, 1_441).Count);
    }

    [TestMethod]
    public void DelayedBandQuestionYieldsToUrgentMedicalAndEndsWithResolvedGrievance()
    {
        var (baseline, medical) = Baseline();
        var id = baseline.People[0].AgentId;
        var planner = new DisorderCuePlanner();
        planner.Reset(baseline, 799);
        var waiting = baseline with { People = baseline.People.Select(person => person.AgentId == id
            ? person with { Grievance = DisorderGrievance.BandDelayed, GrievanceTick = 800 } : person).ToArray() };
        Assert.AreEqual(1, planner.Observe(waiting, medical, 800).Count);
        var urgent = medical with { Needs = medical.Needs.Select(need => need.AgentId == id
            ? need with { Stage = MedicalStage.Distress } : need).ToArray() };
        Assert.AreEqual(0, planner.Observe(waiting, urgent, 801).Count);
        planner.Reset(baseline, 799);
        Assert.AreEqual(1, planner.Observe(waiting, medical, 800).Count);
        Assert.AreEqual(0, planner.Observe(baseline, medical, 801).Count);
    }

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
        Assert.IsTrue(first.Single().Text is "What the hell?!" or "This is ridiculous!" or "It's an outrage!" or "FFS!" or "Grrrr!" or "Hurry up!" or "This queue is ridiculous!");
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

        var failedCalming = baseline with { ResponseStage = SecurityResponseStage.Confronting, ResponseTargetId = a,
            People = baseline.People.Select(item => item.AgentId == a
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
        var allFighters = restored.Session.CaptureDisorder()!.People.Where(item => item.Stage == DisorderStage.Fight &&
            DisorderCuePlanner.CurrentOpponentId(restored.Session.CaptureDisorder()!, item) is not null).Select(item => item.AgentId).ToArray();
        Assert.IsTrue(allFighters.Contains(fighter.AgentId) && allFighters.Contains(fighter.OpponentId!.Value));
        CollectionAssert.AreEquivalent(allFighters, cues.Select(item => item.AgentId).ToArray(),
            "Changed real queue geometry may produce several simultaneous pairs; every actual fighter must retain its cue after restore.");
        Assert.IsTrue(cues.All(item => item.Kind == DisorderCueKind.Fight && item.Text == "FIGHT"));
    }

    [TestMethod]
    public void ResolvedStewardResponseDoesNotRePairLaterUnrelatedArgumentOrInspector()
    {
        var (baseline, medical) = Baseline();
        var id = baseline.People[0].AgentId;
        var active = baseline with
        {
            ResponseStage = SecurityResponseStage.Confronting,
            ResponseTargetId = id,
            People = baseline.People.Select(item => item.AgentId == id
                ? item with { Stage = DisorderStage.Argument, OpponentId = baseline.SecurityId,
                    Grievance = DisorderGrievance.MusicCutoff, StageTick = 100 }
                : item).ToArray()
        };
        var person = active.People.Single(item => item.AgentId == id);
        var planner = new DisorderCuePlanner();
        planner.Reset(active, 100);
        CollectionAssert.AreEquivalent(new[] { id, baseline.SecurityId }, planner.Observe(active, medical, 100)
            .Select(item => item.AgentId).ToArray());
        StringAssert.Contains(DisorderCuePlanner.CurrentCounterpartInspectorLine(active, person,
            _ => "Jordan Hale", _ => "-6.8, 27.3 m"), "Jordan Hale");

        var resolved = active with
        {
            ResponseStage = SecurityResponseStage.Completed,
            ResponseTargetId = null,
            People = active.People.Select(item => item.AgentId == id
                ? item with { Stage = DisorderStage.Resolved, StageTick = 200, Pressure = 0 }
                : item).ToArray()
        };
        Assert.AreEqual(0, planner.Observe(resolved, medical, 200).Count);
        var renewed = resolved with { People = resolved.People.Select(item => item.AgentId == id
            ? item with { Stage = DisorderStage.Argument, StageTick = 1_100, Pressure = 4_200 }
            : item).ToArray() };
        var renewedPerson = renewed.People.Single(item => item.AgentId == id);
        Assert.AreEqual(baseline.SecurityId, renewedPerson.OpponentId,
            "This regression deliberately preserves the stale saved opponent field.");
        Assert.IsNull(DisorderCuePlanner.CurrentOpponentId(renewed, renewedPerson));
        var cues = planner.Observe(renewed, medical, 1_100);
        Assert.AreEqual(id, cues.Single().AgentId);
        Assert.AreEqual("ARGUMENT", cues.Single().Text);
        Assert.AreEqual("COUNTERPART not established\n",
            DisorderCuePlanner.CurrentCounterpartInspectorLine(renewed, renewedPerson,
                _ => "Jordan Hale", _ => "-6.8, 27.3 m"));
        planner.Reset(renewed, 1_100);
        Assert.AreEqual(id, planner.Observe(renewed, medical, 1_100).Single().AgentId,
            "Loading the renewed unpaired argument must not resurrect Jordan's old cue.");

        var otherId = baseline.People[1].AgentId;
        var oldGuestFight = baseline with { People = baseline.People.Select(item => item.AgentId == id
            ? item with { Stage = DisorderStage.Argument, OpponentId = otherId, StageTick = 1_100 }
            : item.AgentId == otherId
                ? item with { Stage = DisorderStage.Argument, OpponentId = id, StageTick = 1_100 }
                : item).ToArray() };
        Assert.IsNull(DisorderCuePlanner.CurrentOpponentId(oldGuestFight,
            oldGuestFight.People.Single(item => item.AgentId == id)),
            "Old reciprocal fight IDs do not establish a new guest argument pair.");
    }

    private static CommandResult Send(GameSession session, SessionCommand command) => session.Execute(new(
        new CommandId(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase,
        session.CurrentTick, session.NextSubmissionSequence, null, LegacyInterventionFixture.For(session, command)));
}

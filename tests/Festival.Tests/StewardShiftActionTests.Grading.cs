using Festival.Simulation;

namespace Festival.Tests;

/// <summary>Who dealt with an urgent call, standing by after the medic, and the mood the player adds.</summary>
public sealed partial class StewardShiftActionTests
{
    /// <summary>Starts a fight between a standing guest (picked by <paramref name="where"/>) and the guest nearest them; returns the initiator.</summary>
    private static ulong StartFight(GameSession s, Func<ulong, bool> where)
    {
        var initiator = StandingGuest(s, where);
        var me = At(s, initiator);
        var opponent = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.AgentId != initiator && p.Admitted && !p.Departed)
            .Select(p => p.AgentId).OrderBy(id => { var at = At(s, id); return (long)(at.X - me.X) * (at.X - me.X) + (long)(at.Z - me.Z) * (at.Z - me.Z); }).First();
        Agitate(s, initiator, DisorderStage.Argument, 4_500);
        Call(s, "BeginDisorderFight", initiator, opponent, "test:fight");
        Assert.AreEqual(DisorderStage.Fight, s.CapturePerson(initiator)!.ConductStage);
        return initiator;
    }

    private static long DistanceSquared(GameSession s, ulong a, ulong b)
    {
        var x = At(s, a); var y = At(s, b);
        return (long)(x.X - y.X) * (x.X - y.X) + (long)(x.Z - y.Z) * (x.Z - y.Z);
    }

    [TestMethod]
    public void AnUrgentCallTheCrewHandledIsUnansweredButDoesNotCapTheGrade()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(9_000);
        var crew = s.GetStewardResponses()[0].WorkerId;
        // Near the crew's steward and well away from the player, who stays put.
        var initiator = StartFight(s, id => DistanceSquared(s, id, crew) < 8_000L * 8_000 && DistanceSquared(s, id, Player(s)) > 6_000L * 6_000);
        var before = Score(s);
        s.AdvanceWithoutSnapshot(1);
        var call = s.CaptureStewardShift()!.Calls!.Single(c => c.Open && c.Kind == StewardCallKind.Fight && c.Subject == initiator.ToString());
        for (var i = 0; i < 8_000 && s.CaptureStewardShift()!.Calls!.Any(c => c.Id == call.Id && c.Open); i++) s.AdvanceWithoutSnapshot(1);
        var closed = s.CaptureStewardShift()!.Calls!.Single(c => c.Id == call.Id);
        Assert.IsFalse(closed.Open, "The fight ended.");
        Assert.IsTrue(closed.ClosedTick >= closed.HeardTick, "It went out over the radio.");
        var attempt = s.CaptureDisorder()!.Incidents.Last(item => item.InitiatorId == initiator).HandlingAttempt;
        Assert.IsNotNull(attempt, "The crew's steward made the handling attempt.");
        Assert.AreEqual(crew, attempt.WorkerId);
        var score = Score(s);
        Assert.AreEqual(before.Calls + 1, score.Calls);
        Assert.AreEqual(before.Answered, score.Answered, "The player didn't answer it: those points are lost.");
        Assert.AreEqual(before.UrgentUnhandled, score.UrgentUnhandled, "Somebody dealt with it: no cap.");
        Assert.AreEqual(StewardShiftGrade.Encore, StewardShiftGrading.Grade(100, false, score.UrgentUnhandled));
        Restores(s, "after the crew's fight");
    }

    [TestMethod]
    public void AnUrgentCallNobodyDealtWithCapsTheGradeAtBSide()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(9_000);
        var crew = s.GetStewardResponses()[0].WorkerId;
        // Far from the crew's steward, so nobody reaches it before it's over.
        var initiator = StartFight(s, id => DistanceSquared(s, id, crew) > 12_000L * 12_000);
        s.AdvanceWithoutSnapshot(1);
        var call = s.CaptureStewardShift()!.Calls!.Single(c => c.Open && c.Kind == StewardCallKind.Fight && c.Subject == initiator.ToString());
        while (s.CurrentTick < call.HeardTick + 8) s.AdvanceWithoutSnapshot(1);
        Assert.IsNull(s.CaptureDisorder()!.Incidents.Last(item => item.InitiatorId == initiator).HandlingAttempt, "Nobody's got there yet.");
        // It ends of itself, untouched.
        Forge(s, initiator, p => p with { ConductStage = DisorderStage.Resolved, Pressure = 0, ConductStageTick = s.CurrentTick, CooldownUntilTick = s.CurrentTick + 800 });
        var before = Score(s);
        s.AdvanceWithoutSnapshot(1);
        Assert.IsFalse(s.CaptureStewardShift()!.Calls!.Single(c => c.Id == call.Id).Open);
        var score = Score(s);
        Assert.AreEqual(before.Calls + 1, score.Calls);
        Assert.AreEqual(before.UrgentUnhandled + 1, score.UrgentUnhandled, "Nobody dealt with it.");
        Assert.AreEqual(StewardShiftGrade.BSide, StewardShiftGrading.Grade(100, false, score.UrgentUnhandled));
        Assert.IsTrue(s.CaptureStewardShiftReport()!.Grade <= StewardShiftGrade.BSide, "The report is capped too.");
        Restores(s, "after an unhandled fight");
    }

    [TestMethod]
    public void StandingByAfterTheMedicArrivesLeavesTheCallUnanswered()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(9_000);
        var guest = StandingGuest(s);
        var tick = s.CurrentTick;
        Forge(s, guest, p => p with { HealthStage = MedicalStage.Collapsed, Intent = MedicalIntent.Collapsed, HealthWarningTick = tick - 1, HealthCollapseTick = tick });
        s.AdvanceWithoutSnapshot(1);
        var call = s.CaptureStewardShift()!.Calls!.Single(c => c.Open && c.Kind == StewardCallKind.Collapse && c.Subject == guest.ToString());
        // Control puts it out; the crew's medic goes and gets to work.
        for (var i = 0; i < 6_000 && !s.GetMedicResponses().Any(m => m.PatientId == guest && m.Stage == MedicalResponseStage.Treating); i++) s.AdvanceWithoutSnapshot(1);
        Assert.IsTrue(s.GetMedicResponses().Any(m => m.PatientId == guest && m.Stage == MedicalResponseStage.Treating), "The medic reached them.");
        var at = At(s, guest);
        var facing = StandBeside(s, at.X, at.Z);
        Assert.AreEqual(StewardActionKind.StandBy, s.StewardReach(facing).Single(t => t.PersonId == guest).Action);
        Assert.IsTrue(Hold(s, StewardActionKind.StandBy, Who(guest)), Outcome(s));
        Assert.AreEqual(1, Score(s).StoodBy, "Standing by still counts.");
        Assert.AreEqual(-1, s.CaptureStewardShift()!.Calls!.Single(c => c.Id == call.Id).AnsweredTick, "But the call was the medic's.");
        Restores(s, "standing by with the medic");
    }

    [TestMethod]
    public void StandingByBeforeTheMedicAnswersTheCall()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(9_000);
        var guest = StandingGuest(s);
        var tick = s.CurrentTick;
        Forge(s, guest, p => p with { HealthStage = MedicalStage.Collapsed, Intent = MedicalIntent.Collapsed, HealthWarningTick = tick - 1, HealthCollapseTick = tick });
        s.AdvanceWithoutSnapshot(1);
        var call = s.CaptureStewardShift()!.Calls!.Single(c => c.Open && c.Kind == StewardCallKind.Collapse && c.Subject == guest.ToString());
        var at = At(s, guest);
        StandBeside(s, at.X, at.Z);
        Assert.IsTrue(Hold(s, StewardActionKind.StandBy, Who(guest)), Outcome(s));
        Assert.IsTrue(s.CaptureStewardShift()!.Calls!.Single(c => c.Id == call.Id).AnsweredTick >= 0, "First on the scene.");
    }

    [TestMethod]
    public void ThePlayersActionsAddMoodToTheScore()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(9_000);
        Assert.AreEqual(0, Score(s).MoodAdded, "Nothing done, nothing added, whatever the heat does to the crowd.");
        var points = s.CaptureStewardShiftReport()!.Points;
        var guest = StandingGuest(s);
        Agitate(s, guest, DisorderStage.Agitated, 3_200);
        var at = At(s, guest);
        StandBeside(s, at.X, at.Z);
        Assert.IsTrue(Hold(s, StewardActionKind.Calm, Who(guest), follow: true), Outcome(s));
        WastePiece? piece = null;
        for (var i = 0; i < 800 && piece is null; i++)
        {
            piece = s.CaptureLitter()?.Pieces.FirstOrDefault(w => s.BestStewardAction($"litter:{w.Id}") == StewardActionKind.PickLitter &&
                StewardShiftRules.InField(TraversalGrid.WorldToCell(w.XMillimetres, w.ZMillimetres)));
            if (piece is null) s.AdvanceWithoutSnapshot(40);
        }
        Assert.IsNotNull(piece);
        StandBeside(s, piece.XMillimetres, piece.ZMillimetres);
        Assert.IsTrue(Hold(s, StewardActionKind.PickLitter, $"litter:{piece.Id}"), Outcome(s));
        var expected = StewardShiftRules.MoodCredit(StewardActionKind.Calm) + StewardShiftRules.MoodCredit(StewardActionKind.PickLitter);
        Assert.AreEqual(expected, Score(s).MoodAdded);
        var report = s.CaptureStewardShiftReport()!;
        Assert.AreEqual(expected, report.MoodAdded);
        Assert.IsTrue(report.Points > points, $"Points {points} -> {report.Points}.");
        // It's saved with the score and survives a load.
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(expected, restored.Session!.CaptureStewardShift()!.Score!.MoodAdded);
    }
}

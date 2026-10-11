using Festival.Persistence;
using Festival.Simulation;

namespace Festival.Tests;

public sealed partial class StewardShiftActionTests
{
    private static void Agitate(GameSession s, ulong id, DisorderStage stage, int pressure, int temperament = 2_000) =>
        Forge(s, id, p => p with { ConductStage = stage, Pressure = pressure, Temperament = temperament, Grievance = DisorderGrievance.QueueWait,
            ConductStageTick = s.CurrentTick, CooldownUntilTick = 0 });

    private static StewardShiftScore Score(GameSession s) => s.CaptureStewardShift()!.Score!;
    private static string Outcome(GameSession s) => s.CaptureStewardShift()!.LastOutcome?.Text ?? "(no outcome)";

    [TestMethod]
    public void ACalmSucceedsWhenHeldAndOffersTheWalkOut()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(9_000);
        var guest = StandingGuest(s);
        Agitate(s, guest, DisorderStage.Agitated, 3_200);
        var at = At(s, guest);
        var facing = StandBeside(s, at.X, at.Z);
        Assert.AreEqual(StewardActionKind.Calm, s.StewardReach(facing).Single(t => t.PersonId == guest).Action, "An agitated guest in front is one to calm.");
        Assert.IsTrue(Hold(s, StewardActionKind.Calm, Who(guest), follow: true), Outcome(s));
        var person = s.CapturePerson(guest)!;
        Assert.AreEqual(DisorderStage.Resolved, person.ConductStage);
        Assert.AreEqual(0, person.Pressure);
        Assert.AreEqual(1, Score(s).Calmed);
        Assert.IsTrue(s.CaptureStewardShift()!.LastOutcome!.Success);
        Assert.AreEqual(guest, s.CaptureStewardShift()!.EscortOffer!.GuestId, "The one calmed may be walked out.");
        Restores(s, "after a calm");
    }

    [TestMethod]
    public void WalkingAwayOrLettingGoCancelsTheHold()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(9_000);
        var start = At(s, Player(s));
        var guest = StandingGuest(s);
        Agitate(s, guest, DisorderStage.Agitated, 3_200);
        var at = At(s, guest);
        StandBeside(s, at.X, at.Z);
        Assert.IsTrue(Press(s, StewardActionKind.Calm, Who(guest)).IsAccepted);
        s.AdvanceWithoutSnapshot(40);
        Assert.IsNotNull(s.CaptureStewardShift()!.Action, "Held while in reach.");
        Restores(s, "mid-hold");
        // Off out of reach.
        Teleport(s, start.X, start.Z);
        s.AdvanceWithoutSnapshot(1);
        var shift = s.CaptureStewardShift()!;
        Assert.IsNull(shift.Action);
        Assert.IsFalse(shift.LastOutcome!.Completed);
        Assert.AreEqual("Out of reach.", shift.LastOutcome.Text);
        Assert.AreNotEqual(DisorderStage.Resolved, s.CapturePerson(guest)!.ConductStage, "Nothing came of it.");
        Assert.AreEqual(0, Score(s).Calmed);
        // Back again, then letting go of the key.
        at = At(s, guest);
        StandBeside(s, at.X, at.Z);
        Assert.IsTrue(Press(s, StewardActionKind.Calm, Who(guest)).IsAccepted);
        s.AdvanceWithoutSnapshot(20);
        BuildSession.Accept(s, new StewardActionCommand(StewardActionKind.Release, "", 0));
        Assert.IsNull(s.CaptureStewardShift()!.Action);
        s.AdvanceWithoutSnapshot(200);
        Assert.AreEqual(0, Score(s).Calmed, "Let go early, nothing's done.");
    }

    [TestMethod]
    public void AFailedCalmCanTipAnArgumentIntoAFight()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(9_000);
        while (s.CurrentTick % 8 != 3) s.AdvanceWithoutSnapshot(1);
        // Wound up past anyone's calming: the roll can't reach it.
        var guest = StandingGuest(s);
        Agitate(s, guest, DisorderStage.Argument, 7_900, 8_000);
        var at = At(s, guest);
        StandBeside(s, at.X, at.Z);
        Assert.IsTrue(Press(s, StewardActionKind.Calm, Who(guest)).IsAccepted);
        for (var i = 0; i < StewardShiftRules.DurationTicks(StewardActionKind.Calm) - 1; i++)
        {
            s.AdvanceWithoutSnapshot(1);
            Assert.IsNotNull(s.CaptureStewardShift()!.Action, $"Dropped at {i}: {Outcome(s)}");
        }
        // Still arguing, still aggrieved, as the hold runs out (between the crowd's assessments).
        Forge(s, guest, p => p with { ConductStage = DisorderStage.Argument, Pressure = 9_000, Grievance = DisorderGrievance.QueueWait });
        s.AdvanceWithoutSnapshot(1);
        var outcome = s.CaptureStewardShift()!.LastOutcome!;
        Assert.IsTrue(outcome.Completed && !outcome.Success, outcome.Text);
        Assert.AreEqual(1, Score(s).CalmsFailed);
        Assert.AreEqual(DisorderStage.Fight, s.CapturePerson(guest)!.ConductStage, outcome.Text);
        s.AdvanceWithoutSnapshot(1);
        Assert.IsTrue(s.CaptureStewardShift()!.Calls!.Any(call => call.Open && call.Kind == StewardCallKind.Fight && call.Subject == guest.ToString()), "The fight goes out on the radio.");
        Restores(s, "after a failed calm");
    }

    [TestMethod]
    public void AFightIsBrokenUpAndTheInstigatorWalkedToTheGate()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(9_000);
        var steward = At(s, s.GetStewardResponses()[0].WorkerId);
        bool FarFromTheCrew(ulong id) { var at = At(s, id); long dx = at.X - steward.X, dz = at.Z - steward.Z; return dx * dx + dz * dz > 12_000L * 12_000; }
        var initiator = StandingGuest(s, FarFromTheCrew);
        var me = At(s, initiator);
        var opponent = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.AgentId != initiator && p.Admitted && !p.Departed)
            .Select(p => p.AgentId).OrderBy(id => { var at = At(s, id); return (long)(at.X - me.X) * (at.X - me.X) + (long)(at.Z - me.Z) * (at.Z - me.Z); }).First();
        Agitate(s, initiator, DisorderStage.Argument, 4_500);
        Call(s, "BeginDisorderFight", initiator, opponent, "test:fight");
        Assert.AreEqual(DisorderStage.Fight, s.CapturePerson(opponent)!.ConductStage);
        var at = At(s, initiator);
        var facing = StandBeside(s, at.X, at.Z);
        Assert.AreEqual(StewardActionKind.BreakUp, s.StewardReach(facing).Single(t => t.PersonId == initiator).Action);
        Assert.IsTrue(Hold(s, StewardActionKind.BreakUp, Who(initiator), follow: true), Outcome(s));
        Assert.IsTrue(s.CaptureStewardShift()!.LastOutcome!.Success, "Skill against a mild temper: it always works.");
        var origin = s.CaptureDisorder()!.Incidents.Last(item => item.InitiatorId == initiator);
        Assert.AreEqual(-2, origin.InjuryTick, "Stopped without injury.");
        Assert.AreEqual(Player(s), origin.HandlingAttempt!.WorkerId, "The fight's one handling attempt was the player's.");
        Assert.AreEqual(FightHandlingOutcome.Succeeded, origin.HandlingAttempt.Outcome);
        Assert.AreEqual(DisorderStage.Resolved, s.CapturePerson(opponent)!.ConductStage);
        Assert.AreEqual(1, Score(s).FightsBrokenUp);
        Restores(s, "after a break-up");
        // A moment for the crew's steward, on their way, to stand down.
        s.AdvanceWithoutSnapshot(8);

        Assert.IsTrue(Hold(s, StewardActionKind.Escort, Who(initiator), follow: true), Outcome(s));
        Assert.AreEqual(initiator, s.CaptureStewardShift()!.EscortId);
        Assert.AreEqual(MedicalIntent.Leaving, s.CapturePerson(initiator)!.Intent);
        // Walk to the gate at a walking pace; they follow.
        var from = TraversalGrid.WorldToCell(At(s, Player(s)).X, At(s, Player(s)).Z);
        var path = DeterministicPathfinder.FindPath(s.TraversalGrid!, from, GameSession.MedicalExitCell).Path;
        var halfway = path[path.Count / 2];
        foreach (var cell in path.Where(StewardShiftRules.InField))
        {
            var (x, z) = TraversalGrid.CellCentre(cell);
            Teleport(s, x, z);
            s.AdvanceWithoutSnapshot(32);
            if (s.CaptureStewardShift()!.EscortId is null) break;
            if (cell == halfway) Restores(s, "mid-escort");
        }
        for (var i = 0; i < 40 && s.CaptureStewardShift()!.EscortId is not null; i++) s.AdvanceWithoutSnapshot(40);
        Assert.IsNull(s.CaptureStewardShift()!.EscortId, Outcome(s));
        Assert.AreEqual(1, Score(s).Escorted, Outcome(s));
        for (var i = 0; i < 100 && !s.CapturePerson(initiator)!.Departed; i++) s.AdvanceWithoutSnapshot(40);
        Assert.IsTrue(s.CapturePerson(initiator)!.Departed, "Out of the gate and gone.");
    }

    [TestMethod]
    public void RadioingTheMedicSendsThemBeforeControlWouldAndStandingByCounts()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(9_000);
        var guest = StandingGuest(s);
        var tick = s.CurrentTick;
        Forge(s, guest, p => p with { HealthStage = MedicalStage.Collapsed, Intent = MedicalIntent.Collapsed, HealthWarningTick = tick - 1, HealthCollapseTick = tick });
        s.AdvanceWithoutSnapshot(1);
        var call = s.CaptureStewardShift()!.Calls!.Single(c => c.Open && c.Kind == StewardCallKind.Collapse);
        Assert.IsTrue(call.Urgent);
        Assert.AreEqual(guest.ToString(), call.Subject);
        s.AdvanceWithoutSnapshot(20);
        Assert.IsFalse(s.GetMedicResponses().Any(m => m.PatientId == guest), "The crew's medic waits for Control's call.");
        var at = At(s, guest);
        var facing = StandBeside(s, at.X, at.Z);
        Assert.AreEqual(StewardActionKind.RadioMedic, s.StewardReach(facing).Single(t => t.PersonId == guest).Action);
        Assert.IsTrue(Hold(s, StewardActionKind.RadioMedic, Who(guest)), Outcome(s));
        Assert.IsTrue(s.CurrentTick < call.HeardTick, "Got there before the radio.");
        var medic = s.GetMedicResponses().Single(m => m.PatientId == guest);
        Assert.AreEqual(MedicalResponseStage.Travelling, medic.Stage, "Dispatched as a manual send is.");
        Assert.AreEqual(1, Score(s).MedicRadioed);
        Assert.IsTrue(s.CaptureStewardShift()!.Calls!.Single(c => c.Id == call.Id).AnsweredTick >= 0);
        Assert.AreEqual(StewardActionKind.StandBy, s.StewardReach(facing).Single(t => t.PersonId == guest).Action, "Then stand by.");
        Assert.IsTrue(Hold(s, StewardActionKind.StandBy, Who(guest)), Outcome(s));
        Assert.AreEqual(1, Score(s).StoodBy);
        Restores(s, "standing by");
    }

    [TestMethod]
    public void SomeoneStuckInALooIsFreed()
    {
        var s = GameSession.CreateStewardShift(Seed);
        ToiletFacility? loo = null;
        for (var i = 0; i < 400 && loo is null; i++)
        {
            s.AdvanceWithoutSnapshot(40);
            loo = s.CaptureToilets().FirstOrDefault(t => t.OwnerId is { } owner && s.CapturePerson(owner)!.ToiletStage == ToiletVisitStage.Using && s.ActiveFault(t.Id) is null);
        }
        Assert.IsNotNull(loo);
        var victim = loo.OwnerId!.Value;
        Call(s, "AddFault", "stuck:test", FacilityFaultKind.StuckInToilet, loo.Id, victim);
        s.AdvanceWithoutSnapshot(1);
        Assert.IsTrue(s.CaptureStewardShift()!.Calls!.Any(c => c.Open && c.Kind == StewardCallKind.StuckInLoo && c.Subject == loo.Id));
        var door = s.StewardTargetPoint($"toilet:{loo.Id}")!.Value;
        StandBeside(s, door.X, door.Z);
        Assert.IsTrue(Hold(s, StewardActionKind.FreeLoo, $"toilet:{loo.Id}"), Outcome(s));
        Assert.IsNull(s.ActiveFault(loo.Id));
        Assert.IsFalse(s.StuckInToilet(victim));
        Assert.AreEqual(1, Score(s).LoosFreed);
        s.AdvanceWithoutSnapshot(1);
        Assert.IsFalse(s.CaptureStewardShift()!.Calls!.Any(c => c.Open && c.Kind == StewardCallKind.StuckInLoo), "The call's closed.");
        Restores(s, "after freeing a loo");
    }

    [TestMethod]
    public void ABinIsEmptiedAndLitterPickedUp()
    {
        var s = GameSession.CreateStewardShift(Seed);
        string? binId = null;
        for (var i = 0; i < 800 && binId is null; i++)
        {
            s.AdvanceWithoutSnapshot(40);
            binId = s.CaptureBins().FirstOrDefault(b => b.Pieces >= 2)?.Id;
        }
        Assert.IsNotNull(binId, "Something went in a bin.");
        var point = s.StewardTargetPoint($"bin:{binId}")!.Value;
        StandBeside(s, point.X, point.Z);
        Assert.IsTrue(Hold(s, StewardActionKind.EmptyBin, $"bin:{binId}"), Outcome(s));
        Assert.AreEqual(0, s.CaptureBins().Single(b => b.Id == binId).Pieces);
        Assert.AreEqual(1, Score(s).BinsEmptied);

        WastePiece? piece = null;
        for (var i = 0; i < 800 && piece is null; i++)
        {
            piece = s.CaptureLitter()?.Pieces.FirstOrDefault(w => s.BestStewardAction($"litter:{w.Id}") == StewardActionKind.PickLitter &&
                StewardShiftRules.InField(TraversalGrid.WorldToCell(w.XMillimetres, w.ZMillimetres)));
            if (piece is null) s.AdvanceWithoutSnapshot(40);
        }
        Assert.IsNotNull(piece, "Something was dropped.");
        var target = $"litter:{piece.Id}";
        StandBeside(s, piece.XMillimetres, piece.ZMillimetres);
        Assert.IsTrue(Hold(s, StewardActionKind.PickLitter, target), Outcome(s));
        Assert.AreEqual(WasteLocation.Removed, s.CaptureLitter()!.Pieces.Single(w => w.Id == piece.Id).Location);
        Assert.AreEqual(1, Score(s).LitterPicked);
        Restores(s, "after tidying");
    }

    [TestMethod]
    public void ALooseCowIsDrivenHomeWithThePlayerBehindIt()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(1_000);
        SetField(s, "_cows", Call(s, "BreakGate", s.CaptureCows()!, s.CapturePreparation()!));
        s.AdvanceWithoutSnapshot(1);
        Assert.IsTrue(s.CaptureStewardShift()!.Calls!.Any(c => c.Open && c.Kind == StewardCallKind.LooseCow));
        // Let them wander in a little first.
        s.AdvanceWithoutSnapshot(2_000);
        var cow = s.CaptureCows()!.Loose.OrderBy(c => c.Id, StringComparer.Ordinal).First();
        StandBeside(s, cow.XMillimetres, cow.ZMillimetres);
        Assert.IsTrue(Hold(s, StewardActionKind.HerdCow, $"cow:{cow.Id}", follow: true), Outcome(s));
        Assert.AreEqual(Player(s), s.CaptureCows()!.Loose.Single(c => c.Id == cow.Id).HerderId);
        Restores(s, "driving a cow");
        // Keep a few metres behind it until it's home.
        for (var step = 0; step < 12_000 && s.CaptureCows()!.Loose.Any(c => c.Id == cow.Id); step++)
        {
            var now = s.CaptureCows()!.Loose.Single(c => c.Id == cow.Id);
            var me = At(s, Player(s));
            long dx = now.XMillimetres - me.X, dz = now.ZMillimetres - me.Z;
            if (dx * dx + dz * dz > 4_000L * 4_000) StandBeside(s, now.XMillimetres, now.ZMillimetres);
            s.AdvanceWithoutSnapshot(1);
        }
        Assert.IsFalse(s.CaptureCows()!.Loose.Any(c => c.Id == cow.Id), Outcome(s));
        Assert.AreEqual(1, Score(s).CowsHerded);
        s.AdvanceWithoutSnapshot(1);
        Assert.IsTrue(s.CaptureStewardShift()!.Calls!.Any(c => !c.Open && c.Id == $"cow:{cow.Id}" && c.AnsweredTick >= 0), "The cow's call was the player's.");
    }

    [TestMethod]
    public void ALostGuestIsPointedTheRightWay()
    {
        var s = GameSession.CreateStewardShift(Seed);
        ulong guest = 0;
        for (var i = 0; i < 600 && guest == 0; i++)
        {
            s.AdvanceWithoutSnapshot(8);
            var agents = s.CaptureSnapshot().NavigationAgents.ToDictionary(a => a.Id.Value);
            guest = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed)
                .Select(p => p.AgentId).Where(id => s.BestStewardAction(Who(id)) == StewardActionKind.Directions && agents[id].Destination is { } d &&
                    Math.Abs(TraversalGrid.CellCentre(d).XMillimetres - agents[id].XMillimetres) + Math.Abs(TraversalGrid.CellCentre(d).ZMillimetres - agents[id].ZMillimetres) > 8_000)
                .OrderBy(id => id).FirstOrDefault();
        }
        Assert.AreNotEqual(0UL, guest, "Someone set off for a stall, the loos or the water.");
        var before = s.CapturePerson(guest)!.Satisfaction;
        var at = At(s, guest);
        StandBeside(s, at.X, at.Z);
        Assert.IsTrue(Hold(s, StewardActionKind.Directions, Who(guest), follow: true), Outcome(s));
        Assert.IsTrue(s.CapturePerson(guest)!.Satisfaction >= before + StewardShiftRules.DirectionsSatisfaction - 20, "A little happier for it.");
        CollectionAssert.Contains(s.CaptureStewardShift()!.Directed, guest);
        Assert.AreEqual(1, Score(s).Directions);
        Assert.AreNotEqual(StewardActionKind.Directions, s.BestStewardAction(Who(guest)), "Once each.");
        Restores(s, "after directions");
    }

    [TestMethod]
    public void CallsOpenCloseAndTimeTheResponse()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(9_000);
        var start = Score(s);
        // Got to before Control called it: answered, nothing on the clock.
        var first = StandingGuest(s);
        Agitate(s, first, DisorderStage.Argument, 6_000);
        s.AdvanceWithoutSnapshot(1);
        var call = s.CaptureStewardShift()!.Calls!.Single(c => c.Open && c.Kind == StewardCallKind.Argument && c.Subject == first.ToString());
        Assert.AreEqual(call.OpenedTick + StewardShiftRules.RadioDelayTicks(StewardCallKind.Argument), call.HeardTick);
        Assert.IsFalse(call.Urgent);
        var at = At(s, first);
        StandBeside(s, at.X, at.Z);
        Assert.IsTrue(Hold(s, StewardActionKind.Calm, Who(first), follow: true), Outcome(s));
        s.AdvanceWithoutSnapshot(1);
        var closed = s.CaptureStewardShift()!.Calls!.Single(c => c.Id == call.Id);
        Assert.IsFalse(closed.Open);
        Assert.IsTrue(closed.AnsweredTick >= 0 && closed.AnsweredTick < closed.HeardTick);
        var score = Score(s);
        Assert.AreEqual(start.Calls + 1, score.Calls);
        Assert.AreEqual(start.Answered + 1, score.Answered);
        Assert.AreEqual(start.ResponseTicks, score.ResponseTicks, "Spotted first: no wait.");

        // Answered after the radio: the wait from the call counts.
        var second = StandingGuest(s, id => id != first);
        Agitate(s, second, DisorderStage.Argument, 6_000);
        s.AdvanceWithoutSnapshot(1);
        call = s.CaptureStewardShift()!.Calls!.Single(c => c.Open && c.Kind == StewardCallKind.Argument && c.Subject == second.ToString());
        while (s.CurrentTick < call.HeardTick + 40)
        {
            s.AdvanceWithoutSnapshot(8);
            Forge(s, second, p => p with { Pressure = 6_000 });
        }
        at = At(s, second);
        StandBeside(s, at.X, at.Z);
        Assert.IsTrue(Hold(s, StewardActionKind.Calm, Who(second), follow: true), Outcome(s));
        s.AdvanceWithoutSnapshot(1);
        closed = s.CaptureStewardShift()!.Calls!.Single(c => c.Id == call.Id);
        Assert.IsTrue(closed.AnsweredTick > closed.HeardTick);
        Assert.AreEqual(start.ResponseTicks + closed.AnsweredTick - closed.HeardTick, Score(s).ResponseTicks,
            $"answered {closed.AnsweredTick} heard {closed.HeardTick} start {start.ResponseTicks}/{start.Answered} now {Score(s).ResponseTicks}/{Score(s).Answered}");

        // Left alone past the radio, settled by itself: counted, not answered.
        var third = StandingGuest(s, id => id != first && id != second);
        var before = Score(s);
        Agitate(s, third, DisorderStage.Argument, 6_000);
        s.AdvanceWithoutSnapshot(1);
        call = s.CaptureStewardShift()!.Calls!.Single(c => c.Open && c.Subject == third.ToString());
        while (s.CurrentTick < call.HeardTick + 8) { s.AdvanceWithoutSnapshot(8); Forge(s, third, p => p with { Pressure = 6_000 }); }
        Forge(s, third, p => p with { ConductStage = DisorderStage.Calm, Pressure = 0, Grievance = DisorderGrievance.None, ConductStageTick = s.CurrentTick });
        s.AdvanceWithoutSnapshot(1);
        Assert.IsFalse(s.CaptureStewardShift()!.Calls!.Single(c => c.Id == call.Id).Open);
        Assert.AreEqual(before.Calls + 1, Score(s).Calls);
        Assert.AreEqual(before.Answered, Score(s).Answered);
        Restores(s, "after the calls");
    }

    [TestMethod]
    public void ThirstAndHeatBuildAndADrinkOrShadeRelievesThem()
    {
        var s = GameSession.CreateStewardShift(Seed);
        var id = Player(s);
        s.AdvanceWithoutSnapshot(9_000);
        var me = s.CapturePerson(id)!;
        Assert.IsTrue(Math.Abs(me.Thirst - 9_000 / 4) <= 100, $"Thirst {me.Thirst}: a point every four ticks.");
        Assert.IsTrue(Math.Abs(me.HeatExposure - 9_000 / 4) <= 100, $"Heat {me.HeatExposure}: in the sun of a heatwave.");
        Assert.AreEqual(MedicalIntent.WatchShow, me.Intent, "Thirsty or not, they go nowhere by themselves.");
        // A drink at the tap.
        var tap = s.StewardTargetPoint("tap:water.main")!.Value;
        StandBeside(s, tap.X, tap.Z);
        Assert.IsTrue(Hold(s, StewardActionKind.Drink, "tap:water.main"), Outcome(s));
        Assert.AreEqual(0, s.CapturePerson(id)!.Thirst);
        Assert.AreEqual(1, Score(s).Drinks);
        // Under the marquee, heat falls fast.
        Forge(s, id, p => p with { HeatExposure = 6_000 });
        var shade = Enumerable.Range(80, 50).SelectMany(x => Enumerable.Range(150, 40).Select(z => new GridCell(x, z)))
            .First(cell => s.InMarqueeShade(cell) && s.TraversalGrid!.Get(cell).IsWalkable && StewardShiftRules.InField(cell));
        var (sx, sz) = TraversalGrid.CellCentre(shade);
        Teleport(s, sx, sz);
        s.AdvanceWithoutSnapshot(400);
        Assert.IsTrue(s.CapturePerson(id)!.HeatExposure <= 6_000 - 400 / 4 * StewardShiftRules.ShadeCoolPerStep + 10, $"Heat {s.CapturePerson(id)!.HeatExposure}");
        // Parched, they slow down; they never collapse.
        long Walked(int thirst)
        {
            var g = GameSession.CreateStewardShift(Seed);
            Forge(g, Player(g), p => p with { Thirst = thirst });
            var from = At(g, Player(g));
            BuildSession.Accept(g, new SetPlayerMoveCommand(0, -1000, false, 180));
            g.AdvanceWithoutSnapshot(40);
            return from.Z - At(g, Player(g)).Z;
        }
        var fresh = Walked(0); var parched = Walked(9_500);
        Assert.IsTrue(parched < fresh && parched >= fresh * 7 / 10, $"Fresh {fresh} mm, parched {parched} mm.");
        // Rain: thirst at half the pace and no heat.
        var wet = GameSession.CreateStewardShift(Seed, 2);
        wet.AdvanceWithoutSnapshot(9_000);
        var soaked = wet.CapturePerson(Player(wet))!;
        Assert.IsTrue(Math.Abs(soaked.Thirst - 9_000 / 8) <= 100 && soaked.HeatExposure == 0, $"Thirst {soaked.Thirst}, heat {soaked.HeatExposure}.");
    }

    [TestMethod]
    public void StrawIsSpreadOnTheMudFromTheStore()
    {
        var s = GameSession.CreateStewardShift(Seed, 2);
        s.AdvanceWithoutSnapshot(2_000);
        var me = At(s, Player(s));
        var facing = s.CaptureStewardShift()!.FacingDegrees;
        var (fx, fz) = StewardShiftRules.Facing(facing);
        var ahead = TraversalGrid.WorldToCell(me.X + fx * StewardShiftRules.MudAheadMillimetres / 1000, me.Z + fz * StewardShiftRules.MudAheadMillimetres / 1000);
        var mud = Field<int[]>(s, "_groundMud");
        for (var x = -2; x <= 2; x++) for (var z = -2; z <= 2; z++) mud[(ahead.Z + z) * TraversalGrid.Width + ahead.X + x] = 60;
        var target = $"mud:{ahead.X}:{ahead.Z}";
        var reach = s.StewardReach(facing);
        Assert.IsTrue(reach.Any(t => t.TargetId == target && t.Action == StewardActionKind.SpreadStraw), string.Join(", ", reach.Select(t => t.TargetId)));
        var bales = s.CaptureWeather()!.StrawBales;
        Assert.IsTrue(BuildSession.Send(s, new StewardActionCommand(StewardActionKind.SpreadStraw, target, facing)).IsAccepted);
        s.AdvanceWithoutSnapshot(StewardShiftRules.DurationTicks(StewardActionKind.SpreadStraw) + 1);
        Assert.IsTrue(s.CaptureStewardShift()!.LastOutcome!.Success, Outcome(s));
        Assert.AreEqual(bales - 1, s.CaptureWeather()!.StrawBales);
        Assert.IsTrue(s.GroundTrampleAt(ahead) >= 0, "Straw down where they stood.");
        Assert.AreEqual(1, Score(s).StrawSpread);
        Restores(s, "after straw");
    }

    [TestMethod]
    public void ADeathFailsTheShiftWithAReportAndTheSaveLoads()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(6_000);
        var performer = s.CapturePreparation()!.People.First(person => person.Role == ProtectedPersonRole.Performer && s.CapturePerson(person.AgentId)!.Admitted);
        var tick = s.CurrentTick;
        Forge(s, performer.AgentId, p => p with
        {
            HealthStage = MedicalStage.Critical, Thirst = 10_000, HeatExposure = 10_000, Intent = MedicalIntent.Collapsed,
            HealthWarningTick = tick - 4_000, HealthCollapseTick = tick - GameSession.MedicalDeathDelayTicks, HealthCriticalTick = tick - 1,
        });
        for (var i = 0; i < 6_000 && s.PreparedStatus == PreparationStatus.Running; i++) s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(PreparationStatus.Failed, s.PreparedStatus);
        var report = s.CaptureStewardShiftReport()!;
        Assert.IsTrue(report.Over && report.Failed);
        Assert.AreEqual(1, report.Deaths);
        Assert.AreEqual(StewardShiftGrade.OffKey, report.Grade, "A death is Off-key, whatever else went well.");
        var shift = s.CaptureStewardShift()!;
        Assert.IsNotNull(shift.ClosingMood);
        Assert.IsFalse(shift.Calls!.Any(c => c.Open), "Every call closed with the shift.");
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(report, restored.Session!.CaptureStewardShiftReport()! with { Score = report.Score });
    }

    [TestMethod]
    public void TheGradeScale()
    {
        StewardShiftGrade Grade(int calls, int answered, int seconds, int prevented, int broken, int mood, int litter, int missed = 0) =>
            StewardShiftGrading.Grade(StewardShiftGrading.Points(calls, answered, seconds, prevented, broken, mood, litter), false, missed);
        Assert.AreEqual(100, StewardShiftGrading.Points(10, 10, 20, 3, 0, 10, 15));
        Assert.AreEqual(StewardShiftGrade.Encore, Grade(10, 10, 20, 3, 0, 10, 15));
        Assert.AreEqual(StewardShiftGrade.Headliner, Grade(10, 9, 45, 2, 1, 8, 10));
        Assert.AreEqual(StewardShiftGrade.CrowdPleaser, Grade(10, 6, 90, 2, 1, 5, 6));
        Assert.AreEqual(StewardShiftGrade.BSide, Grade(10, 4, 100, 1, 1, 4, 5));
        Assert.AreEqual(StewardShiftGrade.OffKey, Grade(10, 0, -1, 0, 0, 0, 0), "Did nothing.");
        Assert.AreEqual(StewardShiftGrade.BSide, Grade(10, 10, 20, 3, 0, 10, 15, missed: 1), "A missed urgent call caps it.");
        Assert.AreEqual(StewardShiftGrade.OffKey, StewardShiftGrading.Grade(100, true), "A death.");
        Assert.AreEqual("Encore!", StewardShiftGrading.Name(StewardShiftGrade.Encore));
        Assert.AreEqual("B-side", StewardShiftGrading.Name(StewardShiftGrade.BSide));
    }

    [TestMethod]
    public void AMidActionSaveRestoresAndReplaysIdentically()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(9_000);
        var guest = StandingGuest(s);
        Agitate(s, guest, DisorderStage.Agitated, 3_200);
        var at = At(s, guest);
        StandBeside(s, at.X, at.Z);
        Assert.IsTrue(Press(s, StewardActionKind.Calm, Who(guest)).IsAccepted);
        s.AdvanceWithoutSnapshot(60);
        Assert.IsNotNull(s.CaptureStewardShift()!.Action);
        var directory = Path.Combine(Path.GetTempPath(), "festival-steward-action-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var compatibility = new SaveCompatibility("shift", "content", "shift");
            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "holding", new(s, compatibility, "test", DateTimeOffset.UtcNow)).IsSuccess);
            var loaded = SaveFileAdapter.LoadSlot(directory, "holding", compatibility);
            Assert.IsTrue(loaded.IsSuccess, loaded.Error);
            var copy = loaded.Session!;
            foreach (var game in new[] { s, copy })
            {
                game.AdvanceWithoutSnapshot(200);
                BuildSession.Accept(game, new SetPlayerMoveCommand(1000, 0, false, 90));
                game.AdvanceWithoutSnapshot(600);
            }
            Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, copy.CaptureSnapshot().AuthoritativeHash);
            Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(s.CaptureStewardShift()), System.Text.Json.JsonSerializer.Serialize(copy.CaptureStewardShift()));
            Assert.AreEqual(1, Score(copy).Calmed, $"The hold carried on through the load: {Outcome(copy)}");
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void ForgedShiftWorkIsRefused()
    {
        var s = GameSession.CreateStewardShift(Seed);
        s.AdvanceWithoutSnapshot(9_000);
        var guest = StandingGuest(s);
        Agitate(s, guest, DisorderStage.Argument, 7_900);
        var at = At(s, guest);
        StandBeside(s, at.X, at.Z);
        Assert.IsTrue(Press(s, StewardActionKind.Calm, Who(guest)).IsAccepted);
        s.AdvanceWithoutSnapshot(20);
        var saved = s.CapturePersistenceSnapshot();
        var ok = GameSession.Restore(saved);
        Assert.IsTrue(ok.IsSuccess, ok.Error);
        var shift = saved.StewardShift!;
        var held = shift.Action!;
        var medic = s.GetMedicResponses()[0].WorkerId;
        var open = shift.Calls!.First(c => c.Open);
        var far = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed).Select(p => p.AgentId)
            .OrderByDescending(id => { var g = At(s, id); return Math.Abs((long)g.X - at.X) + Math.Abs((long)g.Z - at.Z); }).First();
        var cases = new (string Why, StewardShiftSnapshot Shift)[]
        {
            ("a hold on nobody", shift with { Action = held with { TargetId = "person:999999" } }),
            ("a hold on someone out of reach", shift with { Action = held with { TargetId = Who(far) } }),
            ("a hold of the wrong length", shift with { Action = held with { DurationTicks = 1 } }),
            ("a hold that should have finished", shift with { Action = held with { StartedTick = s.CurrentTick - 1_000 } }),
            ("letting go as an action", shift with { Action = held with { Kind = StewardActionKind.Release } }),
            ("a call for nothing", shift with { Calls = [.. shift.Calls!, new StewardCall("fight:1:1", StewardCallKind.Fight, "1", s.CurrentTick - 10)] }),
            ("a live incident with no call", shift with { Calls = shift.Calls!.Where(c => c != open).ToArray() }),
            ("a call answered before it began", shift with { Calls = shift.Calls!.Select(c => c == open ? c with { AnsweredTick = c.OpenedTick - 1 } : c).ToArray() }),
            ("more answered than called", shift with { Score = shift.Score! with { Answered = shift.Score.Calls + 1 } }),
            ("a negative tally", shift with { Score = shift.Score! with { Calmed = -1 } }),
            ("directions to the medic", shift with { Directed = [medic], Score = shift.Score! with { Directions = 1 } }),
            ("directions uncounted", shift with { Directed = [guest] }),
            ("the medic walked out", shift with { EscortId = medic }),
            ("a guest walked out who isn't following", shift with { EscortId = guest }),
            ("an escort offer for ever", shift with { EscortOffer = new(guest, s.CurrentTick + 100_000) }),
            ("a mood at close while on", shift with { ClosingMood = 5_000 }),
        };
        var failures = new List<string>();
        foreach (var (why, forged) in cases)
        {
            var result = GameSession.Restore(saved with { StewardShift = forged });
            if (result.IsSuccess) failures.Add($"Restored {why}.");
            else if (!result.Error!.Contains("steward", StringComparison.OrdinalIgnoreCase)) failures.Add($"{why}: refused for something else ({result.Error}).");
        }
        // A cow on the player's books that isn't being driven.
        var cows = saved.Cows! with { Gate = PastureGateState.Broken, Escaped = 1,
            Loose = [new LooseCow("cow.1", at.X, at.Z, CowActivity.Herded, [], 0, s.CurrentTick) { HerderId = Player(s), HerdStartedTick = s.CurrentTick }] };
        if (GameSession.Restore(saved with { Cows = cows }).IsSuccess) failures.Add("Restored a cow the player holds but doesn't drive.");
        Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
    }

    [TestMethod]
    public void TheSameInputsGiveTheSameShift()
    {
        GameSession Play()
        {
            var s = GameSession.CreateStewardShift(Seed);
            s.AdvanceWithoutSnapshot(9_000);
            var guest = StandingGuest(s);
            Agitate(s, guest, DisorderStage.Argument, 6_000, 6_000);
            var at = At(s, guest);
            StandBeside(s, at.X, at.Z);
            Hold(s, StewardActionKind.Calm, Who(guest), follow: true);
            s.AdvanceWithoutSnapshot(3_000);
            return s;
        }
        var one = Play(); var two = Play();
        Assert.AreEqual(one.CaptureSnapshot().AuthoritativeHash, two.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(one.CaptureStewardShift()), System.Text.Json.JsonSerializer.Serialize(two.CaptureStewardShift()));
    }
}

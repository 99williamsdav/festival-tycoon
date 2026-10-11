using System.Collections;
using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>
/// Hands-on step 2: the player's steward works by themselves while the player is in the manager's view (a weak
/// autopilot, sim-driven and saved), Control routes the radio's calls to the hired crew first, and calls the crew
/// covered don't count against the player. Classic campaigns and the standalone shift have none of it.
/// </summary>
[TestClass]
public sealed class HandsOnAutopilotTests
{
    private const ulong Seed = 20260922;
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static object Call(GameSession s, string method, params object[] args) => typeof(GameSession).GetMethod(method, Private)!.Invoke(s, args)!;
    private static void Forge(GameSession s, ulong id, Func<Person, Person> change) => Call(s, "UpdatePerson", id, change);

    /// <summary>A hands-on (or classic) Tier 1, planned and crewed as the classic tests' campaign is.</summary>
    private static GameSession Started(bool handsOn = true, bool faults = true)
    {
        var s = handsOn ? GameSession.CreateHandsOnCampaign(Seed) : GameSession.CreateBuildCampaign(Seed);
        var perks = s.CapturePerks()!;
        string[] quiet = ["extra-pair-of-hands", "doctors-orders"];
        for (var draws = 0; !perks.Hand.Any(quiet.Contains); draws++)
        {
            Assert.IsTrue(draws < 400);
            Call(s, "OpenPerkDraft"); perks = s.CapturePerks()!;
        }
        BuildSession.Accept(s, new ChoosePerkCommand(perks.DraftAttempt, perks.Cursor, perks.Hand.First(quiet.Contains)));
        BuildSession.Accept(s, new UseDefaultBuildLayoutCommand());
        BuildSession.Accept(s, new SetProgrammeCommand(BuildSession.Acts));
        BuildSession.Accept(s, new SetPreparationStockCommand(40, 32));
        foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        if (!faults) BuildSession.WithoutFaults(s);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        return s;
    }

    private static ulong Me(GameSession s) => s.PlayerStewardId!.Value;
    private static StewardShiftSnapshot Shift(GameSession s) => s.CaptureStewardShift()!;

    private static (int X, int Z) At(GameSession s, ulong id)
    {
        var agent = s.CaptureObservation().NavigationAgents.Single(item => item.Id.Value == id);
        return (agent.XMillimetres, agent.ZMillimetres);
    }

    private static void Teleport(GameSession s, int x, int z)
    {
        var agents = (IDictionary)typeof(GameSession).GetField("_navigationAgents", Private)!.GetValue(s)!;
        var nav = agents[new EntityId(Me(s))]!;
        void Set(string name, object value) => nav.GetType().GetProperty(name)!.SetValue(nav, value);
        Set("XMillimetres", x); Set("ZMillimetres", z); Set("SegmentOriginXMillimetres", x); Set("SegmentOriginZMillimetres", z);
    }

    /// <summary>The player a metre or so from a point, on open ground; returns the facing at it.</summary>
    private static int StandBeside(GameSession s, int x, int z)
    {
        var grid = s.TraversalGrid!;
        var target = TraversalGrid.WorldToCell(x, z);
        var spot = Enumerable.Range(-3, 7).SelectMany(dx => Enumerable.Range(-3, 7).Select(dz => new GridCell(target.X + dx, target.Z + dz)))
            .Where(cell => Math.Max(Math.Abs(cell.X - target.X), Math.Abs(cell.Z - target.Z)) >= 2 && grid.Get(cell).IsWalkable && StewardShiftRules.InField(cell))
            .OrderBy(cell => { var c = TraversalGrid.CellCentre(cell); return Math.Abs((long)(c.XMillimetres - x) * (c.XMillimetres - x) + (long)(c.ZMillimetres - z) * (c.ZMillimetres - z) - 1_200_000L); })
            .ThenBy(cell => cell.X).ThenBy(cell => cell.Z).First();
        var (sx, sz) = TraversalGrid.CellCentre(spot);
        Teleport(s, sx, sz);
        return StewardShiftRules.FacingTowards(x - sx, z - sz);
    }

    /// <summary>The first loo someone is using.</summary>
    private static ToiletFacility LooInUse(GameSession s)
    {
        ToiletFacility? loo = null;
        for (var i = 0; i < 400 && loo is null; i++)
        {
            s.AdvanceWithoutSnapshot(40);
            loo = s.CaptureToilets().FirstOrDefault(t => t.OwnerId is { } owner && s.CapturePerson(owner)!.ToiletStage == ToiletVisitStage.Using && s.ActiveFault(t.Id) is null);
        }
        Assert.IsNotNull(loo, "Someone used a loo.");
        return loo;
    }

    /// <summary>Jams a loo with whoever is in it, as a random fault would.</summary>
    private static void Jam(GameSession s, ToiletFacility loo)
    {
        Call(s, "AddFault", "stuck:test", FacilityFaultKind.StuckInToilet, loo.Id, loo.OwnerId!.Value);
        s.AdvanceWithoutSnapshot(1);
    }

    /// <summary>
    /// Nobody hired free: a tap breaks as far as can be from (x, z) and, with no maintenance at Tier 1, the hired
    /// steward goes to bodge it, which takes them 20 s and the walk.
    /// </summary>
    private static void BusyTheCrew(GameSession s, int x, int z)
    {
        var tap = s.CaptureWaterPoints().OrderByDescending(t =>
            {
                var c = TraversalGrid.CellCentre(t.Cell);
                return (long)(c.XMillimetres - x) * (c.XMillimetres - x) + (long)(c.ZMillimetres - z) * (c.ZMillimetres - z);
            }).ThenBy(t => t.Id, StringComparer.Ordinal).First();
        var someone = s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed).AgentId;
        Call(s, "AddFault", "broken:test", FacilityFaultKind.BrokenTap, tap.Id, someone);
        s.AdvanceWithoutSnapshot(16);
        var crew = s.GetStewardResponses().Select(job => job.WorkerId).ToArray();
        Assert.IsTrue(crew.All(id => s.FaultWorkOf(id) is not null), "Every hired steward is busy: " + string.Join("; ", crew.Select(s.ResponseStaffStatus)));
    }

    /// <summary>An admitted guest standing still in the field, calm and watching, far from anyone hired.</summary>
    private static ulong StandingGuest(GameSession s, Func<ulong, bool>? also = null)
    {
        var agents = s.CaptureObservation().NavigationAgents.ToDictionary(a => a.Id.Value);
        return s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed).Select(p => p.AgentId)
            .Where(id => agents[id].Action != AgentNavigationAction.Travelling && StewardShiftRules.InField(TraversalGrid.WorldToCell(agents[id].XMillimetres, agents[id].ZMillimetres)) &&
                s.CapturePerson(id) is { ConductStage: DisorderStage.Calm, Intent: MedicalIntent.WatchShow, VendorId: null, ToiletStage: ToiletVisitStage.None, HealthStage: MedicalStage.Clear } &&
                (also?.Invoke(id) ?? true))
            .OrderBy(id => id).First();
    }

    private static void Argue(GameSession s, ulong id, int pressure = 6_000) =>
        Forge(s, id, p => p with { ConductStage = DisorderStage.Argument, Pressure = pressure, Temperament = 2_000, Grievance = DisorderGrievance.QueueWait,
            ConductStageTick = s.CurrentTick, CooldownUntilTick = 0 });

    [TestMethod]
    public void TheAutopilotAnswersACallAndActsButLaterAndSlowerThanAPlayer()
    {
        // Nobody hired free: the call is the player's own. Two identical days, one left to itself, one played, each with
        // the player's steward standing by the loo's door when it jams.
        var auto = Started();
        var played = Started();
        Assert.IsNotNull(Shift(auto).Autopilot);
        var loos = new[] { auto, played }.Select(LooInUse).ToArray();
        Assert.AreEqual(loos[0].Id, loos[1].Id);
        var loo = loos[0].Id;
        var door = auto.StewardTargetPoint($"toilet:{loo}")!.Value;
        BuildSession.Accept(played, new SetPlayerStewardControlCommand(false));
        foreach (var (session, toilet) in new[] { (auto, loos[0]), (played, loos[1]) })
        {
            BusyTheCrew(session, door.X, door.Z);
            Jam(session, toilet);
        }
        var facing = StandBeside(played, door.X, door.Z);
        StandBeside(auto, door.X, door.Z);
        var call = Shift(auto).Calls!.Single(c => c.Open && c.Kind == StewardCallKind.StuckInLoo);
        Assert.IsNull(call.CoveredBy, "Nobody hired is free to cover it.");

        // The player sees it and holds E.
        BuildSession.Accept(played, new StewardActionCommand(StewardActionKind.FreeLoo, $"toilet:{loo}", facing));
        while (Shift(played).Action is not null) played.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(1, Shift(played).Score!.LoosFreed);
        var playedAnswer = Shift(played).Calls!.Single(c => c.Id == call.Id).AnsweredTick;

        // Left to themselves, the steward waits for the radio and a few seconds more, walks over and holds half as long again.
        long started = -1;
        for (var i = 0; i < 6_000 && Shift(auto).Score!.LoosFreed == 0; i++)
        {
            auto.AdvanceWithoutSnapshot(1);
            if (started < 0 && Shift(auto).Action is { } held)
            {
                started = held.StartedTick;
                Assert.IsTrue(held.Auto);
                Assert.AreEqual(StewardActionKind.FreeLoo, held.Kind);
                Assert.AreEqual(StewardShiftRules.DurationTicks(StewardActionKind.FreeLoo) * 3 / 2, held.DurationTicks, "Half as long again.");
                Assert.IsTrue(held.StartedTick >= Math.Min(call.HeardTick, call.OpenedTick) + StewardAutopilotRules.ReactTicks, "Not before it's noticed.");
                Assert.IsTrue(BuildSession.Restored(auto) is not null);
            }
        }
        Assert.IsTrue(started >= 0, "The autopilot took the call.");
        Assert.AreEqual(1, Shift(auto).Score!.LoosFreed, Shift(auto).LastOutcome?.Text);
        Assert.IsNull(auto.ActiveFault(loo));
        auto.AdvanceWithoutSnapshot(1);
        var answered = Shift(auto).Calls!.Single(c => c.Id == call.Id);
        Assert.IsTrue(answered.Auto && answered.AnsweredTick > playedAnswer + 400, $"autopilot {answered.AnsweredTick} vs player {playedAnswer}");
        Assert.AreEqual(1, Shift(auto).Score!.Answered, "Answered by you.");
        Assert.AreEqual(1, Shift(auto).Score!.AutoAnswered, "...by yourself working alone.");
        Assert.AreEqual(0, Shift(played).Score!.AutoAnswered);
        Assert.IsTrue(Shift(auto).Score!.ResponseTicks > Shift(played).Score!.ResponseTicks);
        BuildSession.Restored(auto);
    }

    [TestMethod]
    public void TheAutopilotCalmsWithALowerSkillThanThePlayers()
    {
        // Wound up exactly as far as the player's own calming skill: the player's calm always works, the autopilot's only on a good roll.
        var s = Started();
        s.AdvanceWithoutSnapshot(9_000);
        var skill = ((StaffProfile)Call(s, "PlayerStewardProfile", Me(s))).CalmingSkill;
        var guest = StandingGuest(s);
        var spot = At(s, guest);
        BusyTheCrew(s, spot.X, spot.Z);
        var pressure = skill - 2_000 / 4;
        Argue(s, guest, pressure);
        for (var i = 0; i < 9_000 && Shift(s).Score!.Calmed + Shift(s).Score!.CalmsFailed == 0; i++)
        {
            s.AdvanceWithoutSnapshot(1);
            if (Shift(s).Score!.Calmed + Shift(s).Score!.CalmsFailed > 0) break;
            if (Shift(s).Action is { Kind: StewardActionKind.Calm } held) Assert.AreEqual(StewardAutopilotRules.HoldTicks(StewardActionKind.Calm), held.DurationTicks);
            Forge(s, guest, p => p with { Pressure = pressure, Temperament = 2_000, Grievance = DisorderGrievance.QueueWait });
        }
        var score = Shift(s).Score!;
        Assert.AreEqual(1, score.Calmed + score.CalmsFailed, "The autopilot went and tried to calm them.");
        var roll = CowRules.Draw(s.CampaignSeed, "steward-shift.calm", s.CurrentTick, guest, 2_001);
        Assert.AreEqual(skill - StewardAutopilotRules.SkillPenalty + roll >= skill, score.Calmed == 1, $"roll {roll}: calmed at the autopilot's skill, not the player's");
    }

    [TestMethod]
    public void SwitchingBetweenManualAndAutoIsSavedAndReplaysIdentically()
    {
        GameSession Play(GameSession s, bool restoreMidway)
        {
            s.AdvanceWithoutSnapshot(6_000);
            BuildSession.Accept(s, new SetPlayerStewardControlCommand(false));
            Assert.IsNull(Shift(s).Autopilot);
            Assert.AreEqual(CommandReasonCode.AlreadyCommitted, BuildSession.Send(s, new SetPlayerStewardControlCommand(false)).ReasonCode);
            BuildSession.Accept(s, new SetPlayerMoveCommand(1000, 0, false, 90));
            s.AdvanceWithoutSnapshot(300);
            BuildSession.Accept(s, new SetPlayerStewardControlCommand(true));
            Assert.AreEqual(0, Shift(s).MoveX, "Handing over stops the walk.");
            Assert.IsFalse(BuildSession.Send(s, new SetPlayerMoveCommand(1000, 0, false, 90)).IsAccepted, "Not the player's to move.");
            s.AdvanceWithoutSnapshot(3_000);
            if (restoreMidway) s = BuildSession.Restored(s);
            s.AdvanceWithoutSnapshot(3_000);
            BuildSession.Accept(s, new SetPlayerStewardControlCommand(false));
            s.AdvanceWithoutSnapshot(200);
            BuildSession.Accept(s, new SetPlayerStewardControlCommand(true));
            s.AdvanceWithoutSnapshot(3_000);
            return s;
        }
        var one = Play(Started(), false);
        var two = Play(Started(), true);
        Assert.AreEqual(one.CaptureSnapshot().AuthoritativeHash, two.CaptureSnapshot().AuthoritativeHash, "The same inputs, with a save and load in the middle, give the same day.");
        var saved = one.CapturePersistenceSnapshot();
        Assert.IsNotNull(saved.StewardShift!.Autopilot);
        var json = System.Text.Json.JsonSerializer.Serialize(saved.StewardShift);
        Assert.IsTrue(json.Contains("\"Autopilot\"", StringComparison.Ordinal));

        // Strict on restore.
        Assert.IsFalse(GameSession.Restore(saved with { StewardShift = saved.StewardShift with { Autopilot = new() { RouteIndex = 3 } } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { StewardShift = saved.StewardShift with { Autopilot = new() { TakenTick = saved.CurrentTick + 1 } } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { StewardShift = saved.StewardShift with { Autopilot = new() { Route = new GridCell[30], TargetId = "bin:x" } } }).IsSuccess);
        var held = new StewardActionProgress(StewardActionKind.PickLitter, "litter:x", saved.CurrentTick, StewardShiftRules.DurationTicks(StewardActionKind.PickLitter));
        Assert.IsFalse(GameSession.Restore(saved with { StewardShift = saved.StewardShift with { Action = held } }).IsSuccess, "Left to themselves, nothing the player started is held.");
        var forgedCall = saved.StewardShift.Calls!.Length == 0 ? null : saved.StewardShift.Calls[0];
        if (forgedCall is not null)
            Assert.IsFalse(GameSession.Restore(saved with { StewardShift = saved.StewardShift with { Calls = [forgedCall with { Auto = true, AnsweredTick = -1 }, .. saved.StewardShift.Calls[1..]] } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { StewardShift = saved.StewardShift with { Score = saved.StewardShift.Score! with { AutoAnswered = saved.StewardShift.Score.Answered + 1 } } }).IsSuccess);
    }

    [TestMethod]
    public void ControlSendsAHiredStewardBeforeThePlayersOwn()
    {
        // The pasture gate gives: loose cows wander in. Nobody herds a cow by the crew's own rules (it's a manager's
        // order), but on a hands-on day Control sends the free hired steward to the first one radioed.
        var s = Started();
        s.AdvanceWithoutSnapshot(6_000);
        var crew = s.GetStewardResponses().Select(job => job.WorkerId).ToArray();
        Assert.IsTrue(crew.Length >= 1 && !crew.Contains(Me(s)));
        Assert.IsTrue(crew.All(id => s.ResponseStaffStatus(id).StartsWith("Idle", StringComparison.Ordinal)), string.Join("; ", crew.Select(s.ResponseStaffStatus)));
        typeof(GameSession).GetField("_cows", Private)!.SetValue(s, Call(s, "BreakGate", s.CaptureCows()!, s.CapturePreparation()!));
        s.AdvanceWithoutSnapshot(1);
        var calls = Shift(s).Calls!.Where(c => c.Open && c.Kind == StewardCallKind.LooseCow).ToArray();
        Assert.IsTrue(calls.Length >= 2, "More than one loose cow.");
        Assert.IsTrue(calls.All(c => c.CoveredBy is null), "Not yet radioed, nobody's sent.");
        while (s.CurrentTick < calls.Max(c => c.HeardTick) + 16) s.AdvanceWithoutSnapshot(1);
        var now = Shift(s).Calls!.Where(c => c.Open && c.Kind == StewardCallKind.LooseCow).ToArray();
        var covered = now.Where(c => c.CoveredBy is not null).ToArray();
        Assert.AreEqual(crew.Length, covered.Length, "Each hired steward was sent to a cow.");
        foreach (var call in covered)
        {
            Assert.IsTrue(crew.Contains(call.CoveredBy!.Value));
            Assert.AreEqual(call.CoveredBy, s.CaptureCows()!.Loose.Single(cow => cow.Id == call.Subject).HerderId, "Herding it.");
            Assert.AreNotEqual(call.Id, Shift(s).Autopilot?.CallId, "The player's steward leaves it to them.");
        }
        Assert.IsTrue(now.Any(c => c.CoveredBy is null), "The rest are the player's.");
        BuildSession.Restored(s);
        // The player's steward goes after something the crew isn't on.
        for (var i = 0; i < 2_000 && Shift(s).Autopilot?.CallId is null; i++) s.AdvanceWithoutSnapshot(1);
        var mine = Shift(s).Calls!.Single(c => c.Id == Shift(s).Autopilot!.CallId);
        Assert.IsNull(mine.CoveredBy);
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void CallsTheCrewCoversDontCountAgainstThePlayer()
    {
        // A fight the hired steward handles, then the same fight while they're busy elsewhere; the player stands by both.
        void Fight(bool busy)
        {
            var s = Started();
            BuildSession.Accept(s, new SetPlayerStewardControlCommand(false));
            s.AdvanceWithoutSnapshot(9_000);
            var initiator = StandingGuest(s);
            var me = At(s, initiator);
            if (busy) BusyTheCrew(s, me.X, me.Z);
            var opponent = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.AgentId != initiator && p.Admitted && !p.Departed)
                .Select(p => p.AgentId).OrderBy(id => { var at = At(s, id); return (long)(at.X - me.X) * (at.X - me.X) + (long)(at.Z - me.Z) * (at.Z - me.Z); }).First();
            Argue(s, initiator, 4_500);
            Call(s, "BeginDisorderFight", initiator, opponent, "test:fight");
            s.AdvanceWithoutSnapshot(1);
            var call = Shift(s).Calls!.Single(c => c.Open && c.Kind == StewardCallKind.Fight);
            var score = Shift(s).Score!;
            for (var i = 0; i < 6_000 && Shift(s).Calls!.Single(c => c.Id == call.Id).Open; i++) s.AdvanceWithoutSnapshot(1);
            var closed = Shift(s).Calls!.Single(c => c.Id == call.Id);
            Assert.IsFalse(closed.Open);
            // How this one call was scored as it closed: the score's rule applied to it alone.
            var dealt = (bool)typeof(GameSession).GetMethod("UrgentDealtWith", Private)!.Invoke(s, [closed])!;
            var counted = (StewardShiftScore)typeof(GameSession).GetMethod("Counted", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [score, closed, dealt])!;
            if (busy)
            {
                Assert.IsNull(closed.CoveredBy, "Nobody hired was free: the player's call.");
                Assert.AreEqual(score.Calls + 1, counted.Calls, "Left unanswered, it counts against them.");
                Assert.AreEqual(score.UrgentUnhandled + (dealt ? 0 : 1), counted.UrgentUnhandled);
            }
            else
            {
                Assert.AreEqual(s.GetStewardResponses()[0].WorkerId, closed.CoveredBy, "Covered by the hired steward.");
                Assert.AreEqual(score with { Covered = score.Covered + 1 }, counted, "Not the player's call: neither unanswered nor an urgent call nobody dealt with, only counted as covered.");
                Assert.AreEqual(0, s.CaptureStewardShiftReport()!.UrgentUnhandled, "No B-side cap from the crew's fight.");
            }
            BuildSession.Restored(s);
        }
        Fight(busy: false);
        Fight(busy: true);
    }

    [TestMethod]
    public void AClassicCampaignIsUnchanged()
    {
        var s = Started(handsOn: false, faults: false);
        Assert.IsNull(s.CaptureStewardShift());
        Assert.AreEqual(CommandReasonCode.WrongPhase, BuildSession.Send(s, new SetPlayerStewardControlCommand(true)).ReasonCode);
        s.AdvanceWithoutSnapshot(1_000);
        typeof(GameSession).GetField("_cows", Private)!.SetValue(s, Call(s, "BreakGate", s.CaptureCows()!, s.CapturePreparation()!));
        s.AdvanceWithoutSnapshot(1_000);
        Assert.IsTrue(s.CaptureCows()!.Loose.Length >= 2);
        Assert.IsTrue(s.CaptureCows()!.Loose.All(cow => cow.HerderId is null), "No Control in the classic game: a cow is herded when the manager says so.");
        Assert.IsNull(s.CaptureStewardShift());
        var json = System.Text.Json.JsonSerializer.Serialize(s.CapturePersistenceSnapshot());
        Assert.IsFalse(json.Contains("Autopilot", StringComparison.Ordinal) || json.Contains("CoveredBy", StringComparison.Ordinal));
    }

    [TestMethod]
    public void LeftUpOnAStageTheStewardComesDownAndNeverGoesBackUp()
    {
        var s = Started();
        BuildSession.Accept(s, new SetPlayerStewardControlCommand(false));
        s.AdvanceWithoutSnapshot(6_000);
        bool OnStage() { var (x, z) = At(s, Me(s)); var cell = TraversalGrid.WorldToCell(x, z); return s.Stages.Any(stage => stage.Deck(cell) || stage.Stairs(cell)); }
        // The player walks up the main stage's steps, then steps back into the manager's view up there.
        var stage = s.Stages[0];
        var foot = stage.AccessCells[1]; var stair = stage.StairCells[1];
        var (fx, fz) = TraversalGrid.CellCentre(foot);
        Teleport(s, fx, fz);
        var dx = Math.Sign(stair.X - foot.X) * 1000; var dz = Math.Sign(stair.Z - foot.Z) * 1000;
        BuildSession.Accept(s, new SetPlayerMoveCommand(dx, dz, false, StewardShiftRules.FacingTowards(dx, dz)));
        s.AdvanceWithoutSnapshot(400);
        BuildSession.Accept(s, new SetPlayerMoveCommand(0, 0, false, 0));
        s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(stage.Id, s.PlayerStageId, "Up on the deck.");
        BuildSession.Accept(s, new SetPlayerStewardControlCommand(true));
        for (var i = 0; i < 2_400 && OnStage(); i++) s.AdvanceWithoutSnapshot(1);
        Assert.IsFalse(OnStage(), "Left to themselves, they come down the steps.");
        Assert.IsNull(s.PlayerStageId);
        BuildSession.Restored(s);
        // Working the field from there on, with the stage in the way of the gate and the bars: never back up it.
        for (var i = 0; i < 12_000; i++)
        {
            s.AdvanceWithoutSnapshot(1);
            Assert.IsFalse(OnStage(), $"Back up a stage at tick {s.CurrentTick}: {s.CaptureStewardShift()!.Autopilot?.TargetId}");
        }
        // The way round: from the foot of the steps to the far side of the deck, the autopilot's route keeps off it.
        var avoid = (int[])Call(s, "StageAvoidance");
        var deck = Enumerable.Range(0, TraversalGrid.Width).SelectMany(x => Enumerable.Range(0, TraversalGrid.Depth).Select(z => new GridCell(x, z))).Where(stage.Deck).ToArray();
        var grid = s.TraversalGrid!;
        var beyond = deck.Max(c => c.Z) + 3;
        var far = Enumerable.Range(beyond, 12).SelectMany(z => Enumerable.Range(deck.Min(c => c.X), deck.Max(c => c.X) - deck.Min(c => c.X) + 1).Select(x => new GridCell(x, z)))
            .First(c => grid.Get(c).IsWalkable && StewardShiftRules.InField(c));
        var path = DeterministicPathfinder.FindPath(s.TraversalGrid!, foot, far, avoid);
        Assert.IsTrue(path.Found && path.Path.All(cell => !stage.Deck(cell) && !stage.Stairs(cell)), "Routed round the stage.");
    }

    [TestMethod]
    public void StandingStillInFirstPersonWithAFullCrewEarnsNothing()
    {
        var s = Started();
        BuildSession.Accept(s, new SetPlayerStewardControlCommand(false));
        for (var i = 0; i < 80 && s.PreparedStatus is PreparationStatus.Running; i++) s.AdvanceWithoutSnapshot(1_000);
        var report = s.CaptureStewardShiftReport()!;
        Assert.AreEqual(0, report.Answered);
        Assert.IsTrue(report.Calls > 0 || report.Score.Covered > 0, "The day had calls.");
        Assert.AreEqual(StewardShiftGrade.OffKey, report.Grade, $"points {report.Points}: calls {report.Calls}, covered {report.Score.Covered}, fights {report.DayFights}");
    }

    [TestMethod]
    public void AGivenUpCallIsLeftAloneForAWhileAndTheGiveUpIsSaved()
    {
        var s = Started();
        var loo = LooInUse(s);
        var door = s.StewardTargetPoint($"toilet:{loo.Id}")!.Value;
        BusyTheCrew(s, door.X, door.Z);
        Jam(s, loo);
        var call = Shift(s).Calls!.Single(c => c.Open && c.Kind == StewardCallKind.StuckInLoo);
        Assert.IsNull(call.CoveredBy);
        // Already a minute on it without getting there.
        var field = typeof(GameSession).GetField("_stewardShift", Private)!;
        field.SetValue(s, Shift(s) with { Autopilot = new() { TargetId = $"toilet:{loo.Id}", CallId = call.Id, TakenTick = s.CurrentTick - StewardAutopilotRules.GiveUpTicks - 1 } });
        s.AdvanceWithoutSnapshot(1);
        var gaveUp = Shift(s).Autopilot!.GaveUp!.Single();
        Assert.AreEqual(call.Id, gaveUp.Key);
        Assert.AreEqual(s.CurrentTick + StewardAutopilotRules.GiveUpCooldownTicks, gaveUp.UntilTick);
        BuildSession.Restored(s);
        var saved = s.CapturePersistenceSnapshot();
        Assert.IsFalse(GameSession.Restore(saved with { StewardShift = saved.StewardShift! with { Autopilot = saved.StewardShift.Autopilot! with
            { GaveUp = [gaveUp with { UntilTick = s.CurrentTick + StewardAutopilotRules.GiveUpCooldownTicks + 1 }] } } }).IsSuccess, "No longer than the cooldown.");
        Assert.IsFalse(GameSession.Restore(saved with { StewardShift = saved.StewardShift! with { Autopilot = saved.StewardShift.Autopilot! with
            { GaveUp = [gaveUp, gaveUp] } } }).IsSuccess, "Once each.");
        // It doesn't go straight back to it, though it's the oldest call it knows of.
        for (var i = 0; i < StewardAutopilotRules.GiveUpCooldownTicks - 10 && s.ActiveFault(loo.Id) is not null; i++)
        {
            s.AdvanceWithoutSnapshot(1);
            Assert.AreNotEqual(call.Id, Shift(s).Autopilot?.CallId, $"Back on it at tick {s.CurrentTick}.");
        }
    }

    [TestMethod]
    public void HandingOverEndsAWalkOutAndNeverLetsTheStewardOntoAStage()
    {
        var s = Started();
        BuildSession.Accept(s, new SetPlayerStewardControlCommand(false));
        s.AdvanceWithoutSnapshot(6_000);
        var guest = StandingGuest(s);
        var field = typeof(GameSession).GetField("_stewardShift", Private)!;
        field.SetValue(s, Shift(s) with { EscortOffer = new(guest, s.CurrentTick + 100) });
        BuildSession.Accept(s, new SetPlayerStewardControlCommand(true));
        Assert.IsNull(Shift(s).EscortOffer, "The autopilot never walks anyone out.");
        // From the foot of the steps, a step onto them is refused while they work by themselves, and allowed in the player's hands.
        var stage = s.Stages[0];
        var (fx, fz) = TraversalGrid.CellCentre(stage.AccessCells[1]);
        var (tx, tz) = TraversalGrid.CellCentre(stage.StairCells[1]);
        var step = typeof(GameSession).GetMethod("PlayerStepAllowed", Private)!;
        bool OnStage(int x, int z) { var c = TraversalGrid.WorldToCell(x, z); return stage.Stairs(c) || stage.Deck(c); }
        long lx = tx - fx, lz = tz - fz; var length = (long)Math.Sqrt(lx * lx + lz * lz);
        var d = Enumerable.Range(1, (int)(length / 50)).Select(k => k * 50).First(k => OnStage(fx + (int)(lx * k / length), fz + (int)(lz * k / length)));
        int ax = fx + (int)(lx * (d - 50) / length), az = fz + (int)(lz * (d - 50) / length), bx = fx + (int)(lx * d / length), bz = fz + (int)(lz * d / length);
        Assert.IsFalse(OnStage(ax, az));
        bool Allowed() => (bool)step.Invoke(s, [Me(s), ax, az, bx, bz, new SpatialNeighbourIndex()])!;
        Assert.IsFalse(Allowed(), "Working by themselves: never a step from the ground onto the steps.");
        BuildSession.Accept(s, new SetPlayerStewardControlCommand(false));
        Assert.IsNull(Shift(s).Autopilot);
        Assert.IsTrue(Allowed(), "In the player's hands the same step is fine.");
    }

    [TestMethod]
    public void LeftToThemselvesAllDayTheStewardAnswersSomeOfTheirCalls()
    {
        var s = Started();
        for (var i = 0; i < 80 && s.PreparedStatus is PreparationStatus.Running; i++)
        {
            s.AdvanceWithoutSnapshot(1_000);
            if (i == 20) BuildSession.Restored(s);
        }
        var score = s.CaptureStewardShift()!.Score!;
        var report = s.CaptureStewardShiftReport()!;
        Assert.IsTrue(score.Answered > 0 && score.AutoAnswered == score.Answered && report.Grade < StewardShiftGrade.Headliner,
            $"calls {score.Calls} answered {score.Answered} auto {score.AutoAnswered} unhandled {score.UrgentUnhandled} calmed {score.Calmed}/{score.CalmsFailed} " +
            $"broken {score.FightsBrokenUp}/{score.BreakUpsFailed} loos {score.LoosFreed} taps {score.TapsBodged} bins {score.BinsEmptied} litter {score.LitterPicked} " +
            $"cows {score.CowsHerded} drinks {score.Drinks} avg {report.AverageResponseSeconds}s points {report.Points} {report.Grade} covered {score.Covered} fights {report.DayFights}");
    }

    [TestMethod]
    public void TheStandaloneShiftHasNoAutopilot()
    {
        var s = GameSession.CreateStewardShift(Seed);
        Assert.IsNull(Shift(s).Autopilot, "Always the player's own hands.");
        Assert.AreEqual(CommandReasonCode.WrongPhase, BuildSession.Send(s, new SetPlayerStewardControlCommand(true)).ReasonCode);
        var from = At(s, Me(s));
        s.AdvanceWithoutSnapshot(12_000);
        Assert.AreEqual(from, At(s, Me(s)), "Left alone, they stand still.");
        Assert.AreEqual(0, Shift(s).Score!.Answered);
        Assert.AreEqual(0, Shift(s).Score!.AutoAnswered);
        Assert.IsTrue((Shift(s).Calls ?? []).All(c => c.CoveredBy is null && !c.Auto), "No covering on the standalone shift: its grading is unchanged.");
        var saved = s.CapturePersistenceSnapshot();
        Assert.AreEqual("The standalone steward shift has no autopilot.", GameSession.Restore(saved with { StewardShift = saved.StewardShift! with { Autopilot = new() } }).Error);
    }
}

using System.Reflection;
using Festival.Persistence;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>
/// The player steward up on a stage: only they can get up its steps, the crowd and the band shout at them (angry
/// during a set, milder between), the watching crowd sours, the act's set takes a "stage invader" hiccup, the shift's
/// score counts it, and a save made up there restores and replays the same, with forged positions refused.
/// </summary>
[TestClass]
public sealed class StewardShiftStageTests
{
    private const ulong Seed = 20260922;
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static ulong Player(GameSession s) => s.PlayerStewardId!.Value;

    private static (int X, int Z) At(GameSession s, ulong id)
    {
        var agent = s.CaptureObservation().NavigationAgents.Single(item => item.Id.Value == id);
        return (agent.XMillimetres, agent.ZMillimetres);
    }

    private static GridCell CellOf(GameSession s, ulong id) { var (x, z) = At(s, id); return TraversalGrid.WorldToCell(x, z); }

    /// <summary>Test setup: stands the player on open ground at a cell's centre, as if they'd walked there.</summary>
    private static void Stand(GameSession s, GridCell cell)
    {
        var navs = (System.Collections.IDictionary)typeof(GameSession).GetField("_navigationAgents", Private)!.GetValue(s)!;
        var nav = navs[new EntityId(Player(s))]!;
        var (x, z) = TraversalGrid.CellCentre(cell);
        foreach (var (name, value) in new[] { ("XMillimetres", x), ("ZMillimetres", z), ("SegmentOriginXMillimetres", x), ("SegmentOriginZMillimetres", z) })
            nav.GetType().GetProperty(name)!.SetValue(nav, value);
    }

    private static void Move(GameSession s, int dx, int dz) =>
        BuildSession.Accept(s, new SetPlayerMoveCommand(dx, dz, false, StewardShiftRules.FacingTowards(dx == 0 && dz == 0 ? 1 : dx, dz)));

    /// <summary>From the foot of a stage's steps, up them and on along the deck until something stops them.</summary>
    private static void ClimbOnto(GameSession s, FestivalStage stage, int ticks = 400)
    {
        var foot = stage.AccessCells[1]; var stair = stage.StairCells[1];
        Stand(s, foot);
        Move(s, Math.Sign(stair.X - foot.X) * 1000, Math.Sign(stair.Z - foot.Z) * 1000);
        s.AdvanceWithoutSnapshot(ticks);
        Move(s, 0, 0);
    }

    private static void RunUntil(GameSession s, Func<bool> done, int limit = 60_000)
    {
        for (var i = 0; i < limit && !done(); i++) s.AdvanceWithoutSnapshot(1);
        Assert.IsTrue(done(), $"Never got there by tick {s.CurrentTick}.");
    }

    private static LiveSetStage MainSet(GameSession s) => s.CaptureLivePerformance()!.Stage;

    private static bool OnStageCells(GameSession s, GridCell cell) => s.Stages.Any(stage => stage.Deck(cell) || stage.Stairs(cell));

    [TestMethod]
    public void ThePlayerClimbsEachStagesStepsButNotItsEdgeAndNoGuestEverGetsUp()
    {
        var s = GameSession.CreateStewardShift(Seed, 2);
        foreach (var stage in s.Stages)
        {
            ClimbOnto(s, stage);
            Assert.IsTrue(stage.Deck(CellOf(s, Player(s))), $"Up {stage.Name}'s steps onto its deck: at {CellOf(s, Player(s))}.");
            s.AdvanceWithoutSnapshot(1);
            Assert.AreEqual(stage.Id, s.PlayerStageId);
        }
        // Never up the front or the end: the trailer's chassis and drawbar and the riser's rim are solid.
        foreach (var (stage, from, dx, dz) in new[] { (FestivalStages.Main, new GridCell(100, 148), -1000, 0), (FestivalStages.Pond, new GridCell(168, 155), 0, 1000),
                     (FestivalStages.Main, new GridCell(94, 166), 0, -1000) })
        {
            Stand(s, from);
            Move(s, dx, dz);
            s.AdvanceWithoutSnapshot(400);
            Move(s, 0, 0);
            s.AdvanceWithoutSnapshot(1);
            Assert.IsFalse(OnStageCells(s, CellOf(s, Player(s))), $"Climbed {stage.Name}'s edge from {from}.");
            Assert.IsNull(s.PlayerStageId);
        }
        // A whole day of the crowd: nobody but the band and the player ever sets foot on a deck or its steps.
        var band = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Performer).Select(p => p.AgentId).ToHashSet();
        while (s.PreparedStatus == PreparationStatus.Running)
        {
            s.AdvanceWithoutSnapshot(80);
            foreach (var agent in s.CaptureObservation().NavigationAgents)
                if (!band.Contains(agent.Id.Value) && agent.Id.Value != (s.PlayerStewardId ?? 0))
                    Assert.IsFalse(OnStageCells(s, TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres)), $"Tick {s.CurrentTick}: {agent.Id.Value} on a stage.");
        }
    }

    [TestMethod]
    public void TheDrumKitIsSolidToThePlayer()
    {
        var s = GameSession.CreateStewardShift(Seed);
        var stage = FestivalStages.Main;
        ClimbOnto(s, stage);
        var (dx, dz) = TraversalGrid.CellCentre(stage.PerformerMarks[2]);
        var (x, z) = At(s, Player(s));
        Move(s, Math.Sign(dx - x) * 707, Math.Sign(dz - z) * 707);
        for (var i = 0; i < 600; i++)
        {
            s.AdvanceWithoutSnapshot(1);
            (x, z) = At(s, Player(s));
            Assert.IsTrue((long)(x - dx) * (x - dx) + (long)(z - dz) * (z - dz) >= (long)StageInvasionRules.DrumKitClearanceMillimetres * StageInvasionRules.DrumKitClearanceMillimetres,
                "Walked into the drums.");
        }
    }

    [TestMethod]
    public void TheCrowdAndBandShoutDuringASetAndMorePolitelyBetweenSets()
    {
        List<StageShout> Heard(GameSession s, int ticks)
        {
            var heard = new List<StageShout>();
            for (var i = 0; i < ticks; i++)
            {
                s.AdvanceWithoutSnapshot(1);
                var now = s.StageShouts();
                Assert.IsTrue(now.Count(x => !x.Band) <= 1 && now.Count(x => x.Band) <= 1, "One crowd shout and one band shout at a time.");
                foreach (var shout in now) if (!heard.Any(h => h.Band == shout.Band && h.StartedTick == shout.StartedTick)) heard.Add(shout);
            }
            return heard;
        }
        GameSession Day(out List<StageShout> during, out List<StageShout> between)
        {
            var s = GameSession.CreateStewardShift(Seed);
            RunUntil(s, () => MainSet(s) == LiveSetStage.Live);
            s.AdvanceWithoutSnapshot(400);
            Assert.AreEqual(0, s.StageShouts().Count, "Nobody shouts at a steward on the grass.");
            ClimbOnto(s, FestivalStages.Main, 160);
            Assert.AreEqual(FestivalStages.MainId, s.PlayerStageId);
            during = Heard(s, 2_400);
            RunUntil(s, () => MainSet(s) == LiveSetStage.Finished);
            between = Heard(s, 2_000);
            return s;
        }
        var s = Day(out var during, out var between);
        var crowd = during.Where(x => !x.Band).ToArray();
        var band = during.Where(x => x.Band).ToArray();
        Assert.IsTrue(crowd.Length >= 10, $"The crowd shouts every couple of seconds: {crowd.Length}.");
        Assert.IsTrue(band.Length >= 5, $"The band too: {band.Length}.");
        Assert.IsTrue(during.All(x => x.Angry));
        CollectionAssert.IsSubsetOf(crowd.Select(x => x.Text).Distinct().ToArray(), StageInvasionRules.CrowdLines);
        CollectionAssert.IsSubsetOf(band.Select(x => x.Text).Distinct().ToArray(), StageInvasionRules.BandLines);
        Assert.AreEqual(StageInvasionRules.CrowdLines.Length, crowd.Take(StageInvasionRules.CrowdLines.Length).Select(x => x.Text).Distinct().Count(), "Every line before any repeats.");
        Assert.IsTrue(crowd.Zip(crowd.Skip(1)).All(pair => pair.Second.StartedTick - pair.First.StartedTick == StageInvasionRules.CrowdEveryTicks), "Rate-limited to one each slot.");
        var performers = s.CaptureLivePerformances()[0].Performers.Select(p => p.AgentId).ToHashSet();
        var guests = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest).Select(p => p.AgentId).ToHashSet();
        Assert.IsTrue(crowd.All(x => guests.Contains(x.SpeakerId)) && band.All(x => !guests.Contains(x.SpeakerId)));
        Assert.IsTrue(crowd.Select(x => x.SpeakerId).Distinct().Count() > 3, "Different people in the crowd.");

        var mildCrowd = between.Where(x => !x.Band).ToArray();
        Assert.IsTrue(mildCrowd.Length >= 2, $"Between sets they still wonder: {mildCrowd.Length}.");
        Assert.IsTrue(between.All(x => !x.Angry));
        CollectionAssert.IsSubsetOf(mildCrowd.Select(x => x.Text).Distinct().ToArray(), StageInvasionRules.CrowdMildLines);
        CollectionAssert.IsSubsetOf(between.Where(x => x.Band).Select(x => x.Text).Distinct().ToArray(), StageInvasionRules.BandMildLines);

        // Keyed: the same day shouts the same.
        Day(out var again, out var againBetween);
        CollectionAssert.AreEqual(during.Concat(between).ToArray(), again.Concat(againBetween).ToArray());
        var score = s.CaptureStewardShift()!.Score!;
        Assert.AreEqual(1, score.StageInvasions, "One invasion of the set.");
        Assert.IsTrue(score.StageTicks >= 2_400);
        Assert.AreEqual(StageInvasionRules.GradeMaxPenalty, StageInvasionRules.GradePenalty(score.StageTicks));
        var report = s.CaptureStewardShiftReport()!;
        Assert.AreEqual(StewardShiftGrading.Points(report.Calls, report.Answered, report.AverageResponseSeconds, report.FightsPrevented, report.FightsBrokenUp,
            report.MoodAdded, report.LitterPicked) - StageInvasionRules.GradeMaxPenalty is var expected && expected < 0 ? 0 : expected, report.Points);
    }

    private static GameSession HandsOnStarted()
    {
        var s = GameSession.CreateHandsOnCampaign(Seed);
        var perks = s.CapturePerks()!;
        string[] quiet = ["extra-pair-of-hands", "doctors-orders"];
        for (var draws = 0; !perks.Hand.Any(quiet.Contains); draws++)
        {
            Assert.IsTrue(draws < 400);
            typeof(GameSession).GetMethod("OpenPerkDraft", Private)!.Invoke(s, []); perks = s.CapturePerks()!;
        }
        BuildSession.Accept(s, new ChoosePerkCommand(perks.DraftAttempt, perks.Cursor, perks.Hand.First(quiet.Contains)));
        BuildSession.Accept(s, new UseDefaultBuildLayoutCommand());
        BuildSession.Accept(s, new SetProgrammeCommand(BuildSession.Acts));
        BuildSession.Accept(s, new SetPreparationStockCommand(40, 32));
        foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        return s;
    }

    [TestMethod]
    public void InAHandsOnCampaignTheCrowdSoursAndTheBandHoldsItAgainstYou()
    {
        var s = HandsOnStarted();
        RunUntil(s, () => MainSet(s) == LiveSetStage.Live);
        s.AdvanceWithoutSnapshot(400);
        Assert.AreEqual(0, s.MoodTotal(MoodCause.StageInvader));
        ClimbOnto(s, FestivalStages.Main, 160);
        Assert.AreEqual(FestivalStages.MainId, s.PlayerStageId);
        var watching = s.CaptureLivePerformance()!.Listeners.Where(l => l.AtPlace).Select(l => l.AgentId).ToArray();
        Assert.IsTrue(watching.Length > 5);
        // A second's loss now, and one after lingering a minute: it escalates.
        long Second() { var before = s.MoodTotal(MoodCause.StageInvader); s.AdvanceWithoutSnapshot(80); return before - s.MoodTotal(MoodCause.StageInvader); }
        var early = Second();
        s.AdvanceWithoutSnapshot(3_600);
        var late = Second();
        Assert.IsTrue(early > 0 && late > early * 2, $"The watching crowd sours, faster the longer they stay: {early} then {late}.");
        // Back down the steps: it stops.
        var stair = FestivalStages.Main.StairCells[1];
        var foot = FestivalStages.Main.AccessCells[1];
        Move(s, 0, -1000);
        RunUntil(s, () => CellOf(s, Player(s)).Z <= foot.Z, 2_000);
        Move(s, 0, 0);
        Assert.IsNull(s.PlayerStageId);
        var settled = s.MoodTotal(MoodCause.StageInvader);
        s.AdvanceWithoutSnapshot(400);
        Assert.AreEqual(settled, s.MoodTotal(MoodCause.StageInvader), "Off the stage, no more.");
        Assert.IsTrue(stair.Z > foot.Z);

        // The set's record carries the hiccup: a small one-off hit to the act.
        RunUntil(s, () => s.PerformanceRecords.Count > 0);
        var record = s.PerformanceRecords[0];
        CollectionAssert.Contains(record.Hiccups, GigRules.StageInvader);
        var without = GigRules.Judge(record.Facts with { Hiccups = record.Hiccups.Where(h => h != GigRules.StageInvader).ToArray() }, record.ExpectedCrowd).Delta;
        Assert.AreEqual(Math.Max(GigRules.WorstGig, without - GigRules.StageInvaderPenalty), record.Delta, "Three points off the act's relationship.");
        Assert.AreEqual(1, s.CaptureStewardShift()!.Score!.StageInvasions);
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void ASaveMadeOnStageRestoresAndReplaysIdentically()
    {
        var s = GameSession.CreateStewardShift(Seed);
        RunUntil(s, () => MainSet(s) == LiveSetStage.Live);
        ClimbOnto(s, FestivalStages.Main, 160);
        s.AdvanceWithoutSnapshot(300);
        Assert.IsNotNull(s.CaptureStewardShift()!.OnStage!.InvadedTick);
        var directory = Path.Combine(Path.GetTempPath(), "festival-stage-invader-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var compatibility = new SaveCompatibility("shift", "content", "shift");
            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory, "stage", new(s, compatibility, "test", DateTimeOffset.UtcNow)).IsSuccess);
            var loaded = SaveFileAdapter.LoadSlot(directory, "stage", compatibility);
            Assert.IsTrue(loaded.IsSuccess, loaded.Error);
            var copy = loaded.Session!;
            Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, copy.CaptureSnapshot().AuthoritativeHash);
            CollectionAssert.AreEqual(s.StageShouts().ToArray(), copy.StageShouts().ToArray());
            var heard = new List<StageShout>[] { [], [] };
            var games = new[] { s, copy };
            for (var g = 0; g < 2; g++)
            {
                var game = games[g];
                for (var i = 0; i < 800; i++) { game.AdvanceWithoutSnapshot(1); heard[g].AddRange(game.StageShouts()); }
                Move(game, 0, -1000); game.AdvanceWithoutSnapshot(600);
                Move(game, 0, 0); game.AdvanceWithoutSnapshot(1_000);
            }
            Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, copy.CaptureSnapshot().AuthoritativeHash);
            CollectionAssert.AreEqual(heard[0], heard[1]);
            Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(s.CaptureStewardShift()), System.Text.Json.JsonSerializer.Serialize(copy.CaptureStewardShift()));
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void RestoreRefusesForgedStagePositionsAndRecords()
    {
        var s = GameSession.CreateStewardShift(Seed);
        RunUntil(s, () => MainSet(s) == LiveSetStage.Live);
        ClimbOnto(s, FestivalStages.Main, 160);
        s.AdvanceWithoutSnapshot(200);
        var saved = s.CapturePersistenceSnapshot();
        var restored = GameSession.Restore(saved);
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(FestivalStages.MainId, restored.Session!.PlayerStageId);
        var shift = saved.StewardShift!;
        var id = shift.PlayerId!.Value;
        SessionPersistenceSnapshot PlayerAt(GridCell cell, int offsetX = 0) =>
            saved with { NavigationAgents = saved.NavigationAgents!.Select(a => a.Id == id ? a with
                { XMillimetres = TraversalGrid.CellCentre(cell).XMillimetres + offsetX, ZMillimetres = TraversalGrid.CellCentre(cell).ZMillimetres } : a).ToArray() };
        var drums = FestivalStages.Main.PerformerMarks[2];
        var cases = new (string Why, SessionPersistenceSnapshot Snapshot)[]
        {
            ("on the deck with no stage record", saved with { StewardShift = shift with { OnStage = null } }),
            ("a record for a stage that isn't open", saved with { StewardShift = shift with { OnStage = shift.OnStage! with { StageId = FestivalStages.PondId } } }),
            ("on the stage mid-set but not invading", saved with { StewardShift = shift with { OnStage = shift.OnStage! with { InvadedTick = null } } }),
            ("an invasion from the future", saved with { StewardShift = shift with { OnStage = shift.OnStage! with { InvadedTick = saved.CurrentTick + 5 } } }),
            ("a stage record on the grass", PlayerAt(new GridCell(110, 150))),
            ("standing in the drum kit", PlayerAt(drums)),
            ("standing on the trailer's chassis", PlayerAt(new GridCell(97, 150))),
            ("the set not knowing it was invaded", BuildSession.WithMainLive(saved, live => live with { StageInvasions = 0 })),
            ("a score with no invasion", saved with { StewardShift = shift with { Score = shift.Score! with { StageInvasions = 0, StageTicks = 0 } } }),
            ("negative stage time", saved with { StewardShift = shift with { Score = shift.Score! with { StageTicks = -1 } } }),
        };
        foreach (var (why, snapshot) in cases)
            Assert.IsFalse(GameSession.Restore(snapshot).IsSuccess, $"Restored a save with {why}.");

        // A classic campaign has nobody to invade a stage.
        var classic = BuildSession.Started();
        RunUntil(classic, () => MainSet(classic) == LiveSetStage.Live);
        var plain = classic.CapturePersistenceSnapshot();
        Assert.IsTrue(GameSession.Restore(plain).IsSuccess);
        Assert.IsFalse(GameSession.Restore(BuildSession.WithMainLive(plain, live => live with { StageInvasions = 1 })).IsSuccess, "A classic set invaded by nobody.");
        Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(plain).Contains("StageInvasions", StringComparison.Ordinal), "No new JSON in a classic save.");
        Assert.AreEqual(0, classic.StageShouts().Count);
    }

    [TestMethod]
    public void TheGradeLosesAPointPerTenSecondsOnStageUpToFive()
    {
        var clean = StewardShiftGrading.Points(4, 4, 20, 2, 0, 2_000, 15);
        Assert.AreEqual(100, clean);
        Assert.AreEqual(100, StewardShiftGrading.Points(4, 4, 20, 2, 0, 2_000, 15, 799));
        Assert.AreEqual(99, StewardShiftGrading.Points(4, 4, 20, 2, 0, 2_000, 15, 800));
        Assert.AreEqual(95, StewardShiftGrading.Points(4, 4, 20, 2, 0, 2_000, 15, 80_000));
        Assert.AreEqual(0, StewardShiftGrading.Points(1, 0, -1, 0, 1, 0, 0, 80_000), "Never below nothing.");
    }

    private static long Apart(GameSession s, GridCell mark)
    {
        var (x, z) = At(s, Player(s)); var (mx, mz) = TraversalGrid.CellCentre(mark);
        return (long)Math.Sqrt((double)(x - mx) * (x - mx) + (double)(z - mz) * (z - mz));
    }

    [TestMethod]
    public void StandingOnABandMarkCanNeitherStopNorHoldUpTheSet()
    {
        var s = GameSession.CreateStewardShift(Seed);
        var stage = FestivalStages.Main;
        var mark = stage.PerformerMarks[0];
        // Up on the deck well before the set, then leaning on the lead's mark through its start.
        RunUntil(s, () => s.CaptureLivePerformance() is { } live && s.CurrentTick >= live.PlannedTick - 1_600);
        ClimbOnto(s, stage);
        Assert.IsTrue(stage.Deck(CellOf(s, Player(s))));
        var (x, z) = At(s, Player(s)); var (mx, mz) = TraversalGrid.CellCentre(mark);
        Move(s, Math.Sign(mx - x) * 707, Math.Sign(mz - z) * 707);
        var planned = s.CaptureLivePerformance()!.PlannedTick;
        while (s.CurrentTick < planned + 2_000)
        {
            s.AdvanceWithoutSnapshot(1);
            Assert.IsTrue(Apart(s, mark) >= StageInvasionRules.MarkClearanceMillimetres, $"Tick {s.CurrentTick}: stood on the lead's mark ({Apart(s, mark)} mm).");
            if (MainSet(s) == LiveSetStage.Live) { (x, z) = At(s, Player(s)); Move(s, Math.Sign(mx - x) * 707, Math.Sign(mz - z) * 707); }
        }
        var set = s.CaptureLivePerformance()!;
        Assert.AreEqual(LiveSetStage.Live, set.Stage, set.LastReaction);
        Assert.IsTrue(set.StartedTick - set.PlannedTick <= GigRules.LateGraceTicks, $"Started {set.StartedTick - set.PlannedTick} ticks late.");
        Assert.AreEqual(0, set.Stoppages, "Never stopped by the steward on a mark.");
        Assert.AreEqual(FestivalStages.MainId, s.PlayerStageId);
        Assert.AreEqual(1, s.CaptureStewardShift()!.Score!.StageInvasions, "The set starting under them counts.");
        CollectionAssert.AreEqual(new[] { new InvadedSet(FestivalStages.MainId, 0, 1) }, s.CaptureStewardShift()!.InvadedSets);
        BuildSession.Restored(s);
    }

    private static string? InvasionCheck(SessionPersistenceSnapshot saved) => (string?)typeof(GameSession)
        .GetMethod("ValidatePersistedStageInvasions", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [saved]);

    private static void Refused(SessionPersistenceSnapshot snapshot, string why, bool byInvasionCheck = true)
    {
        Assert.IsFalse(GameSession.Restore(snapshot).IsSuccess, $"Restored a save with {why}.");
        if (byInvasionCheck) Assert.IsNotNull(InvasionCheck(snapshot), $"The invasion check missed {why}.");
    }

    [TestMethod]
    public void TheRelationshipHitCannotBeEditedOutOrForgedIn()
    {
        var s = GameSession.CreateStewardShift(Seed);
        RunUntil(s, () => MainSet(s) == LiveSetStage.Live);
        ClimbOnto(s, FestivalStages.Main, 160);
        s.AdvanceWithoutSnapshot(200);
        // Mid-invasion: the tick bounds.
        var during = s.CapturePersistenceSnapshot();
        var shift = during.StewardShift!;
        Refused(during with { StewardShift = shift with { Score = shift.Score! with { StageTicks = during.CurrentTick } } }, "more stage time than the day", false);
        Refused(during with { StewardShift = shift with { Score = shift.Score! with { StageTicks = shift.Score.StageTicks - 1 } } }, "less stage time than the invasion", false);
        Refused(during with { StewardShift = shift with { InvadedSets = null } }, "no invaded sets");
        Refused(during with { StewardShift = shift with { InvadedSets = [new(FestivalStages.MainId, 0, 2)] } }, "a count the set doesn't have");
        Refused(during with { StewardShift = shift with { InvadedSets = [new(FestivalStages.MainId, 1, 1)] } }, "the wrong slot");

        // Back down, the set still on: the set's count and the shift's list must still agree.
        Move(s, 0, -1000);
        RunUntil(s, () => s.PlayerStageId is null && !OnStageCells(s, CellOf(s, Player(s))), 2_000);
        Move(s, 0, 0);
        var down = s.CapturePersistenceSnapshot();
        Assert.IsTrue(GameSession.Restore(down).IsSuccess);
        Refused(BuildSession.WithMainLive(down, live => live with { StageInvasions = 0 }), "the set's invasion zeroed");
        Refused(BuildSession.WithMainLive(down, live => live with { StageInvasions = 0 }) with { StewardShift = down.StewardShift! with { InvadedSets = null } },
            "the set's invasion and the list both cleared");

        // The set over: its record carries the hiccup, and keeps it.
        SessionPersistenceSnapshot Record(SessionPersistenceSnapshot saved, Func<PerformanceRecord, PerformanceRecord> change) =>
            saved with { PerformanceRecords = [change(saved.PerformanceRecords![0]), .. saved.PerformanceRecords.Skip(1)] };
        RunUntil(s, () => s.PerformanceRecords.Count > 0);
        s.AdvanceWithoutSnapshot(1);
        Refused(Record(s.CapturePersistenceSnapshot(), r => r with { Hiccups = r.Hiccups.Where(h => h != GigRules.StageInvader).ToArray() }),
            "the hiccup deleted from the record of the set just finished", false);
        // On to the next set, so only the record remembers it.
        RunUntil(s, () => s.CaptureProgramme()!.CurrentSlot >= 1);
        s.AdvanceWithoutSnapshot(1);
        var after = s.CapturePersistenceSnapshot();
        Assert.IsTrue(GameSession.Restore(after).IsSuccess);
        CollectionAssert.Contains(after.PerformanceRecords![0].Hiccups, GigRules.StageInvader);
        Refused(Record(after, r => r with { Hiccups = r.Hiccups.Where(h => h != GigRules.StageInvader).ToArray() }), "the hiccup deleted from the record");

        // A classic campaign had nobody to do it: no hiccup in a record, nor in a carried set.
        var classic = BuildSession.Started();
        RunUntil(classic, () => classic.PerformanceRecords.Count > 0);
        var plain = classic.CapturePersistenceSnapshot();
        Assert.IsNull(InvasionCheck(plain));
        Refused(Record(plain, r => r with { Hiccups = [.. r.Hiccups, GigRules.StageInvader] }), "a classic record with the hiccup");
        var gig = new CarriedGig(1, BuildSession.Acts[0], true, false, 10, 10, 1_000, [GigRules.StageInvader]);
        var carried = plain with { Preparation = plain.Preparation! with { RelationshipHistory = [gig] } };
        Assert.IsNotNull(InvasionCheck(carried), "A classic carried set with the hiccup.");
        Assert.IsNull(InvasionCheck(carried with { Mode = CampaignMode.HandsOn }), "A hands-on campaign's earlier day may have had one.");
    }

    [TestMethod]
    public void AHandsOnInvasionCarriesIntoTheNextFestival()
    {
        var s = HandsOnStarted();
        RunUntil(s, () => MainSet(s) == LiveSetStage.Live);
        ClimbOnto(s, FestivalStages.Main, 160);
        s.AdvanceWithoutSnapshot(200);
        Move(s, 0, -1000);
        RunUntil(s, () => !OnStageCells(s, CellOf(s, Player(s))), 2_000);
        Move(s, 0, 0);
        for (var i = 0; i < 80 && s.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing; i++) s.AdvanceWithoutSnapshot(1_000);
        Assert.IsTrue(s.CanStartNextFestival, $"{s.PreparedStatus}");
        var record = s.PerformanceRecords.Single(r => r.StageId == FestivalStages.MainId && r.Slot == 0);
        var next = s.CreateNextFestival();
        var history = next.CapturePreparation()!.RelationshipHistory!;
        CollectionAssert.Contains(history.Single(g => g.ActId == record.ActId).Hiccups, GigRules.StageInvader);
        Assert.AreEqual(record.RelationshipAfter, next.ActRelationship(record.ActId), "The hit is in the act's relationship next time.");
        BuildSession.Restored(next);
    }
}

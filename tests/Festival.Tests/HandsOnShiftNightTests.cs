using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>
/// The results paper's "Your shift" tab: a hands-on day keeps every call of the night (saved, strictly checked),
/// who went to each is derived from it, and the press line and the listed calls are pure picks from the report.
/// </summary>
[TestClass]
public sealed class HandsOnShiftNightTests
{
    private const ulong Seed = 20260922;
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static GameSession Started(bool handsOn = true)
    {
        var s = handsOn ? GameSession.CreateHandsOnCampaign(Seed) : GameSession.CreateBuildCampaign(Seed);
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
    public void AHandsOnDayLogsEveryCallOfTheNightWithWhoWent()
    {
        var s = Started();
        for (var i = 0; i < 80 && s.PreparedStatus is PreparationStatus.Running; i++)
        {
            s.AdvanceWithoutSnapshot(1_000);
            if (i == 20) s = BuildSession.Restored(s);
        }
        Assert.AreNotEqual(PreparationStatus.Running, s.PreparedStatus, "The day closed.");
        var shift = s.CaptureStewardShift()!;
        var log = shift.Log!;
        var score = shift.Score!;
        Assert.IsTrue(log.Length > (shift.Calls ?? []).Length, $"The log keeps more than the radio's last few: {log.Length}.");
        Assert.AreEqual(score.Calls, log.Count(c => c.AnsweredTick >= 0 || c.CoveredBy is null));
        Assert.AreEqual(score.Answered, log.Count(c => c.AnsweredTick >= 0));
        Assert.IsTrue(log.All(c => c.Where is { Length: > 0 }), "Control named a place for every call.");

        var night = s.StewardShiftNight();
        Assert.AreEqual(log.Length, night.Count);
        Assert.AreEqual(score.AutoAnswered, night.Count(c => c.Who == StewardCallResponder.Autopilot), "Left to themselves all day: every answer is the autopilot's.");
        Assert.AreEqual(0, night.Count(c => c.Who == StewardCallResponder.You));
        Assert.AreEqual(log.Count(c => c.AnsweredTick < 0 && c.CoveredBy is not null), night.Count(c => c.Who is StewardCallResponder.Medic or StewardCallResponder.Steward or StewardCallResponder.Maintenance));
        Assert.IsTrue(night.Where(c => c.Who is StewardCallResponder.Medic or StewardCallResponder.Steward or StewardCallResponder.Maintenance).All(c => c.Crew is { Length: > 0 }));
        Assert.IsTrue(night.Zip(night.Skip(1)).All(pair => pair.First.Tick <= pair.Second.Tick), "Oldest first.");
        var report = s.CaptureStewardShiftReport()!;
        Assert.AreEqual(StewardShiftPress.Quote(report, night), StewardShiftPress.Quote(BuildSession.Restored(s).CaptureStewardShiftReport()!, BuildSession.Restored(s).StewardShiftNight()),
            "A reload prints the same line.");

        // Strict on restore: the log matches the score, closed calls only, oldest closed first.
        var saved = s.CapturePersistenceSnapshot();
        Assert.IsTrue(GameSession.Restore(saved).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { StewardShift = saved.StewardShift! with { Log = log[1..] } }).IsSuccess, "A call dropped from the log.");
        Assert.IsFalse(GameSession.Restore(saved with { StewardShift = saved.StewardShift! with { Log = [log[0] with { ClosedTick = -1 }, .. log[1..]] } }).IsSuccess, "An open call in the log.");
        Assert.IsFalse(GameSession.Restore(saved with { StewardShift = saved.StewardShift! with { Log = [log[0] with { Where = new string('x', 65) }, .. log[1..]] } }).IsSuccess);
        if (log.Length > 1 && log[0].ClosedTick != log[^1].ClosedTick)
            Assert.IsFalse(GameSession.Restore(saved with { StewardShift = saved.StewardShift! with { Log = [log[^1], .. log[1..^1], log[0]] } }).IsSuccess, "Out of order.");
        Assert.IsFalse(GameSession.Restore(saved with { StewardShift = saved.StewardShift! with { Log = [] } }).IsSuccess, "Present means at least one call.");
    }

    [TestMethod]
    public void TheStandaloneShiftAndClassicCampaignsKeepNoLog()
    {
        var shift = GameSession.CreateStewardShift(Seed);
        shift.AdvanceWithoutSnapshot(12_000);
        Assert.IsNull(shift.CaptureStewardShift()!.Log);
        Assert.IsTrue((shift.CaptureStewardShift()!.Calls ?? []).All(c => c.Where is null));
        Assert.AreEqual(0, shift.StewardShiftNight().Count, "Steward Only keeps its own clipboard report.");
        var saved = shift.CapturePersistenceSnapshot();
        var call = new StewardCall("bin:x", StewardCallKind.FullBin, "x", saved.Preparation!.StartedTick, saved.Preparation.StartedTick, saved.Preparation.StartedTick);
        Assert.IsFalse(GameSession.Restore(saved with { StewardShift = saved.StewardShift! with { Log = [call] } }).IsSuccess, "No log on the standalone shift.");

        var classic = Started(handsOn: false);
        classic.AdvanceWithoutSnapshot(4_000);
        Assert.IsNull(classic.CaptureStewardShift());
        Assert.AreEqual(0, classic.StewardShiftNight().Count);
    }

    private static StewardShiftReport Report(StewardShiftScore score, int deaths = 0) =>
        new(true, deaths > 0, score.Calls, score.Answered, 40, score.Calmed, score.FightsBrokenUp, 60, 55, score.MoodAdded, score.LitterPicked,
            score.UrgentUnhandled, deaths, 70, StewardShiftGrade.CrowdPleaser, score);

    private static StewardNightCall Night(StewardCallKind kind, StewardCallResponder who, string where = "Bar 2", long tick = 100) => new(kind, where, tick, who, null);

    [TestMethod]
    public void ThePressLineGoesToTheBiggestMomentOfTheShift()
    {
        var quiet = new StewardShiftScore();
        Assert.AreEqual("A quiet night on the radio for the organiser", StewardShiftPress.Quote(Report(quiet), []));
        Assert.AreEqual("Organiser nowhere to be seen as the radio crackles", StewardShiftPress.Quote(Report(quiet with { Calls = 3 }), []));
        Assert.AreEqual("Organiser mucks in with the stewards", StewardShiftPress.Quote(Report(quiet with { Calls = 3, Answered = 1 }), []));

        var bins = quiet with { Calls = 2, Answered = 1, BinsEmptied = 1 };
        Assert.AreEqual("Boss mops up at Bar 2", StewardShiftPress.Quote(Report(bins), [Night(StewardCallKind.FullBin, StewardCallResponder.Autopilot)]));
        Assert.AreEqual("Boss mops up after the crowd", StewardShiftPress.Quote(Report(bins), [Night(StewardCallKind.FullBin, StewardCallResponder.Steward)]),
            "The crew's bin isn't the boss's.");
        Assert.AreEqual("Boss mops up after the crowd", StewardShiftPress.Quote(Report(quiet with { LitterPicked = 10 }), []));

        var fight = bins with { FightsBrokenUp = 1, Calmed = 2, LoosFreed = 1, CowsHerded = 1 };
        Assert.AreEqual("Organiser seen breaking up a scuffle", StewardShiftPress.Quote(Report(fight), []));
        Assert.AreEqual("Organiser breaks up two scuffles", StewardShiftPress.Quote(Report(fight with { FightsBrokenUp = 2 }), []));
        Assert.AreEqual("Organiser keeps the peace, rows nipped in the bud", StewardShiftPress.Quote(Report(fight with { FightsBrokenUp = 0 }), []));
        Assert.AreEqual("Where was the organiser? Urgent call goes unanswered", StewardShiftPress.Quote(Report(fight with { FightsBrokenUp = 0, UrgentUnhandled = 1 }), []));

        Assert.AreEqual("Organiser first on the scene of a collapse",
            StewardShiftPress.Quote(Report(fight), [Night(StewardCallKind.Collapse, StewardCallResponder.You, "the main stage")]));
        Assert.AreEqual("Organiser seen breaking up a scuffle",
            StewardShiftPress.Quote(Report(fight), [Night(StewardCallKind.Collapse, StewardCallResponder.Medic, "the main stage")]), "The medic's collapse isn't the organiser's.");
        Assert.AreEqual("Organiser takes the stage, crowd unimpressed",
            StewardShiftPress.Quote(Report(fight with { StageInvasions = 1, StageTicks = 400 }), [Night(StewardCallKind.Collapse, StewardCallResponder.You)]));
        Assert.AreEqual("A sombre night on the field for the organiser", StewardShiftPress.Quote(Report(fight with { StageInvasions = 1, StageTicks = 400 }, deaths: 1), []));
    }

    [TestMethod]
    public void TheListKeepsTheMostTellingCallsInTheOrderTheyCameIn()
    {
        StewardNightCall[] night =
        [
            Night(StewardCallKind.FullBin, StewardCallResponder.Steward, tick: 1),
            Night(StewardCallKind.Argument, StewardCallResponder.You, tick: 2),
            Night(StewardCallKind.Fight, StewardCallResponder.Missed, tick: 3),
            Night(StewardCallKind.BrokenTap, StewardCallResponder.Missed, tick: 4),
            Night(StewardCallKind.Collapse, StewardCallResponder.Medic, tick: 5),
            Night(StewardCallKind.Fight, StewardCallResponder.Autopilot, tick: 6),
            Night(StewardCallKind.LooseCow, StewardCallResponder.Steward, tick: 7),
        ];
        CollectionAssert.AreEqual(night, StewardShiftPress.Highlights(night, 7).ToArray(), "All of them when they fit.");
        CollectionAssert.AreEqual(new long[] { 1, 2, 3, 4, 6 }, StewardShiftPress.Highlights(night, 5).Select(c => c.Tick).ToArray());
        CollectionAssert.AreEqual(new long[] { 2, 3, 6 }, StewardShiftPress.Highlights(night, 3).Select(c => c.Tick).ToArray());
    }
}

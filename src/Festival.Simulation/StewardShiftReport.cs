namespace Festival.Simulation;

/// <summary>The shift's grade, worst to best.</summary>
public enum StewardShiftGrade { OffKey, BSide, CrowdPleaser, Headliner, Encore }

/// <summary>
/// The shift's report card, worked out from the saved score. <paramref name="AverageResponseSeconds"/> is real
/// seconds at 1× (80 ticks), -1 with nothing answered. Crowd mood, for information, is the crowd's average satisfaction
/// as a percentage: as they arrived (<paramref name="MoodFromPercent"/>) and at close, or now while the shift's on.
/// <paramref name="MoodAdded"/> is what the player's own actions added (<see cref="StewardShiftScore.MoodAdded"/>), which
/// the grade scores. <paramref name="UrgentUnhandled"/> counts urgent calls nobody dealt with.
/// </summary>
public sealed record StewardShiftReport(bool Over, bool Failed, int Calls, int Answered, int AverageResponseSeconds, int FightsPrevented,
    int FightsBrokenUp, int CrowdMoodPercent, int MoodFromPercent, int MoodAdded, int LitterPicked, int UrgentUnhandled, int Deaths, int Points,
    StewardShiftGrade Grade, StewardShiftScore Score)
{
    /// <summary>Fights anywhere on the site today, whoever dealt with them: with none the player did, the fights share is 0.</summary>
    public int DayFights { get; init; }
}

/// <summary>
/// The designer's suggested score, out of 100: calls answered (35: with no calls of the player's own, none if the crew
/// covered some and half if nobody had any), response time (20: full marks within 30 s, none from 3 minutes), fights
/// prevented of all the player dealt with (20: with none dealt with, none if the day had fights, half if it had none), the mood the player's own
/// actions added (15: <see cref="StewardShiftRules.MoodForFullMarks"/> for full marks), and litter picked up (10:
/// fifteen pieces). Encore! from 90, Headliner 75, Crowd-pleaser 55, B-side 35, Off-key below. An urgent call (a fight
/// or a collapse) that went out and closed with nobody dealing with it caps the grade at B-side; one the crew handled
/// only costs its calls-answered share. Time up on a stage while its set plays costs a point per 10 s, at most 5. A death is Off-key.
/// </summary>
public static class StewardShiftGrading
{
    public const int EncorePoints = 90, HeadlinerPoints = 75, CrowdPleaserPoints = 55, BSidePoints = 35;

    public static int Points(int calls, int answered, int averageSeconds, int prevented, int brokenUp, int moodAdded, int litter, long stageTicks = 0,
        int covered = 0, int dayFights = 0, int dispatched = 0, int fightsDispatched = 0)
    {
        // Standing still while the crew does everything earns nothing; a day with nothing to do is neither good nor bad.
        // A call the player radioed the crew onto counts half.
        var share = calls > 0 ? (answered + dispatched / 2.0) / calls : covered > 0 ? 0 : 0.5;
        var response = answered == 0 || averageSeconds < 0 ? 0 : Math.Clamp((180 - averageSeconds) / 150.0, 0, 1);
        // A fight radioed to the crew was dealt with, but neither prevented nor stopped by the player: as one broken up.
        var dealt = prevented + brokenUp + fightsDispatched;
        var fights = dealt > 0 ? (double)prevented / dealt : dayFights > 0 ? 0 : 0.5;
        var mood = Math.Clamp(moodAdded / (double)StewardShiftRules.MoodForFullMarks, 0, 1);
        var tidy = Math.Min(1.0, litter / 15.0);
        // Time up on a stage mid-set costs a point per 10 s, at most 5: the crowd's mood it costs isn't scored anywhere else.
        return Math.Max(0, (int)Math.Round(35 * share + 20 * response + 20 * fights + 15 * mood + 10 * tidy, MidpointRounding.AwayFromZero) -
            StageInvasionRules.GradePenalty(stageTicks));
    }

    public static StewardShiftGrade Grade(int points, bool failed, int urgentUnhandled = 0)
    {
        if (failed) return StewardShiftGrade.OffKey;
        var grade = points >= EncorePoints ? StewardShiftGrade.Encore : points >= HeadlinerPoints ? StewardShiftGrade.Headliner :
            points >= CrowdPleaserPoints ? StewardShiftGrade.CrowdPleaser : points >= BSidePoints ? StewardShiftGrade.BSide : StewardShiftGrade.OffKey;
        return urgentUnhandled > 0 && grade > StewardShiftGrade.BSide ? StewardShiftGrade.BSide : grade;
    }

    public static string Name(StewardShiftGrade grade) => grade switch
    {
        StewardShiftGrade.Encore => "Encore!",
        StewardShiftGrade.Headliner => "Headliner",
        StewardShiftGrade.CrowdPleaser => "Crowd-pleaser",
        StewardShiftGrade.BSide => "B-side",
        _ => "Off-key",
    };
}

public sealed partial class GameSession
{
    /// <summary>The report card for the shift, so far or final; null outside a shift.</summary>
    public StewardShiftReport? CaptureStewardShiftReport()
    {
        if (_stewardShift is not { } shift) return null;
        var score = shift.Score ?? new();
        var guests = PeopleIn(PersonView.Roster).Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted).ToArray();
        var mood = shift.ClosingMood ?? (guests.Length == 0 ? 0 : (int)(guests.Sum(p => (long)p.Satisfaction) / guests.Length));
        // Every guest arrives at the same mood for the day's line-up: where the shift's crowd started from.
        var all = PeopleIn(PersonView.Roster).Where(p => p.Role == ProtectedPersonRole.Guest).ToArray();
        var from = all.Length == 0 ? 5_000 : (int)(all.Sum(p => (long)Math.Clamp(5_000 + AdmissionLineupAdjustment(p), 0, 10_000)) / all.Length);
        var failed = _preparation?.Status == PreparationStatus.Failed;
        var deaths = _lifecycle is null ? 0 : _lifecycle.Casualties.Count(c => c.AttemptId == _lifecycle.CurrentAttemptId);
        var average = score.Answered == 0 ? -1 : (int)(score.ResponseTicks / score.Answered / 80);
        var since = _preparation?.StartedTick ?? 0;
        var fights = _disorder?.Incidents.Count(item => item.FightTick >= since) ?? 0;
        var points = StewardShiftGrading.Points(score.Calls, score.Answered, average, score.Calmed, score.FightsBrokenUp, score.MoodAdded, score.LitterPicked, score.StageTicks,
            score.Covered, fights, score.Dispatched, score.FightsDispatched);
        return new(shift.PlayerId is null, failed || deaths > 0, score.Calls, score.Answered, average, score.Calmed, score.FightsBrokenUp, mood / 100, from / 100,
            score.MoodAdded, score.LitterPicked, score.UrgentUnhandled, deaths, points, StewardShiftGrading.Grade(points, failed || deaths > 0, score.UrgentUnhandled), score) { DayFights = fights };
    }
}

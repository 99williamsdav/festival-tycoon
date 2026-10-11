namespace Festival.Simulation;

/// <summary>The shift's grade, worst to best.</summary>
public enum StewardShiftGrade { OffKey, BSide, CrowdPleaser, Headliner, Encore }

/// <summary>
/// The shift's report card, worked out from the saved score. <paramref name="AverageResponseSeconds"/> is real
/// seconds at 1× (80 ticks), -1 with nothing answered. Mood is the crowd's average satisfaction as a percentage: as
/// they arrived (<paramref name="MoodFromPercent"/>) and at close, or now while the shift's on.
/// </summary>
public sealed record StewardShiftReport(bool Over, bool Failed, int Calls, int Answered, int AverageResponseSeconds, int FightsPrevented,
    int FightsBrokenUp, int CrowdMoodPercent, int MoodFromPercent, int LitterPicked, int UrgentMissed, int Deaths, int Points,
    StewardShiftGrade Grade, StewardShiftScore Score);

/// <summary>
/// The designer's suggested score, out of 100: calls answered (35), response time (20: full marks within 30 s, none
/// from 3 minutes), fights prevented of all the player dealt with (20; full marks with none), the crowd's mood gained
/// over the shift (15: ten points of mood for full marks), and litter picked up (10: fifteen pieces). Encore! from 90,
/// Headliner 75, Crowd-pleaser 55, B-side 35, Off-key below. An urgent call (a fight or a collapse) that went out and
/// was never answered caps the grade at B-side; a death is Off-key.
/// </summary>
public static class StewardShiftGrading
{
    public const int EncorePoints = 90, HeadlinerPoints = 75, CrowdPleaserPoints = 55, BSidePoints = 35;

    public static int Points(int calls, int answered, int averageSeconds, int prevented, int brokenUp, int moodGain, int litter)
    {
        var share = calls == 0 ? 1.0 : (double)answered / calls;
        var response = answered == 0 || averageSeconds < 0 ? 0 : Math.Clamp((180 - averageSeconds) / 150.0, 0, 1);
        var fights = prevented + brokenUp == 0 ? 1.0 : (double)prevented / (prevented + brokenUp);
        var mood = Math.Clamp(moodGain / 10.0, 0, 1);
        var tidy = Math.Min(1.0, litter / 15.0);
        return (int)Math.Round(35 * share + 20 * response + 20 * fights + 15 * mood + 10 * tidy, MidpointRounding.AwayFromZero);
    }

    public static StewardShiftGrade Grade(int points, bool failed, int urgentMissed = 0)
    {
        if (failed) return StewardShiftGrade.OffKey;
        var grade = points >= EncorePoints ? StewardShiftGrade.Encore : points >= HeadlinerPoints ? StewardShiftGrade.Headliner :
            points >= CrowdPleaserPoints ? StewardShiftGrade.CrowdPleaser : points >= BSidePoints ? StewardShiftGrade.BSide : StewardShiftGrade.OffKey;
        return urgentMissed > 0 && grade > StewardShiftGrade.BSide ? StewardShiftGrade.BSide : grade;
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
        var points = StewardShiftGrading.Points(score.Calls, score.Answered, average, score.Calmed, score.FightsBrokenUp, (mood - from) / 100, score.LitterPicked);
        return new(shift.PlayerId is null, failed || deaths > 0, score.Calls, score.Answered, average, score.Calmed, score.FightsBrokenUp, mood / 100, from / 100,
            score.LitterPicked, score.UrgentMissed, deaths, points, StewardShiftGrading.Grade(points, failed || deaths > 0, score.UrgentMissed), score);
    }
}

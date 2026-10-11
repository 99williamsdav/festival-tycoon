namespace Festival.Simulation;

/// <summary>Who went to a call on a hands-on day, as the results paper's "Your shift" lists it.</summary>
public enum StewardCallResponder
{
    /// <summary>The player, by their own hands.</summary>
    You,
    /// <summary>The player's steward working by themselves while the player was in the manager's view.</summary>
    Autopilot,
    Medic,
    /// <summary>A hired steward (or another hired hand who isn't the medic or maintenance).</summary>
    Steward,
    Maintenance,
    /// <summary>Nobody: the player didn't answer it and nobody hired was on it.</summary>
    Missed,
    /// <summary>A hired crew member the player radioed out (<see cref="StewardCall.Radioed"/>); <see cref="StewardNightCall.Crew"/> names them.</summary>
    Sent,
}

/// <summary>
/// One call of the night: what, where (Control's landmark, "the field" when it had none), when it came in (the radio
/// call, or the player's action if they got there first), and who went. <paramref name="Crew"/> is the hired crew
/// member's full name when the crew went.
/// </summary>
public sealed record StewardNightCall(StewardCallKind Kind, string Where, long Tick, StewardCallResponder Who, string? Crew)
{
    public bool Urgent => StewardShiftRules.Urgent(Kind);
}

public sealed partial class GameSession
{
    /// <summary>
    /// A hands-on day's calls, oldest first, with who went: the player (by hand or working by themselves) when they
    /// answered it, whoever they beat to it notwithstanding; otherwise the hired crew member Control had on it, by
    /// their role; otherwise missed. Empty outside a hands-on day.
    /// </summary>
    public IReadOnlyList<StewardNightCall> StewardShiftNight()
    {
        if (!IsHandsOn || _stewardShift?.Log is not { } log) return [];
        var staff = GetResponseStaff();
        var maintenance = _preparation?.MaintenanceWorkerId;
        return log.Select(call =>
        {
            var answered = call.AnsweredTick >= 0;
            var tick = answered ? Math.Min(call.HeardTick, call.AnsweredTick) : call.HeardTick;
            var (who, crew) = answered ? (call.Auto ? StewardCallResponder.Autopilot : StewardCallResponder.You, (string?)null)
                : call is { Radioed: true, CoveredBy: { } sent } ? (StewardCallResponder.Sent,
                    staff.FirstOrDefault(member => member.AgentId == sent)?.Role == ResponseRole.Medic ? "the medic" : _persons.TryGet(sent, out var radioed) ? radioed.Name : null)
                : call.CoveredBy is { } id ? (id == maintenance ? StewardCallResponder.Maintenance :
                    staff.FirstOrDefault(member => member.AgentId == id)?.Role == ResponseRole.Medic ? StewardCallResponder.Medic : StewardCallResponder.Steward,
                    _persons.TryGet(id, out var person) ? person.Name : null)
                : (StewardCallResponder.Missed, null);
            return new StewardNightCall(call.Kind, call.Where ?? "the field", tick, who, crew);
        }).OrderBy(call => call.Tick).ToArray();
    }
}

/// <summary>
/// What the paper says about the organiser's own shift, and which calls the results paper has room for. Pure
/// functions of the report and the night, so a reload prints the same.
/// </summary>
public static class StewardShiftPress
{
    /// <summary>
    /// The press line, from the biggest moment of the shift, first match wins: a death (a sombre line, never a joke);
    /// getting up on stage; first on the scene of a collapse; breaking up a fight; an urgent call nobody got to;
    /// calming rows before they turned; freeing someone from a jammed loo; taking a cow home; emptying a bin ("Boss
    /// mops up at Bar 2", where the first bin was) or picking up ten or more pieces of litter; mending a tap; giving
    /// directions three or more times; every call of yours missed; any call answered; every call left to the crew
    /// (<see cref="StewardShiftScore.Covered"/>); a quiet night.
    /// </summary>
    public static string Quote(StewardShiftReport report, IReadOnlyList<StewardNightCall> night)
    {
        var s = report.Score;
        string? Mine(StewardCallKind kind) => night.FirstOrDefault(call => call.Kind == kind && call.Who is StewardCallResponder.You or StewardCallResponder.Autopilot)?.Where;
        if (report.Deaths > 0) return "A sombre night on the field for the organiser";
        if (s.StageInvasions > 0) return s.StageInvasions == 1 ? "Organiser takes the stage, crowd unimpressed" : "Organiser keeps taking the stage, crowd still unimpressed";
        if (Mine(StewardCallKind.Collapse) is not null) return "Organiser first on the scene of a collapse";
        if (report.FightsBrokenUp > 0) return report.FightsBrokenUp == 1 ? "Organiser seen breaking up a scuffle" : $"Organiser breaks up {Count(report.FightsBrokenUp)} scuffles";
        if (report.UrgentUnhandled > 0) return "Where was the organiser? Urgent call goes unanswered";
        if (report.FightsPrevented > 0) return report.FightsPrevented == 1 ? "Organiser talks down a row before it boils over" : "Organiser keeps the peace, rows nipped in the bud";
        if (s.LoosFreed > 0) return "Organiser to the rescue at a jammed portaloo";
        if (s.CowsHerded > 0) return "Organiser rounds up a runaway cow";
        if (s.BinsEmptied > 0) return Mine(StewardCallKind.FullBin) is { } bin && bin != "the field" ? $"Boss mops up at {bin}" : "Boss mops up after the crowd";
        if (s.LitterPicked >= 10) return "Boss mops up after the crowd";
        if (s.TapsBodged > 0) return "Organiser mends a tap with a bit of string";
        if (s.Directions >= 3) return "Organiser plays tour guide on the field";
        if (report.Calls > 0 && report.Answered == 0 && s.Dispatched > 0) return "Organiser runs the night from the radio";
        if (report.Calls > 0 && report.Answered == 0) return "Organiser nowhere to be seen as the radio crackles";
        if (report.Answered > 0) return "Organiser mucks in with the stewards";
        if (s.Covered > 0) return "Organiser leaves the radio to the crew";
        return "A quiet night on the radio for the organiser";
    }

    private static string Count(int n) => n switch { 2 => "two", 3 => "three", 4 => "four", 5 => "five", _ => n.ToString() };

    /// <summary>
    /// The calls to list, up to <paramref name="room"/>, oldest first: all of them when they fit, otherwise the most
    /// telling (urgent calls nobody went to, urgent calls the player took, the player's other calls, other missed
    /// calls, then the crew's), earliest first within each.
    /// </summary>
    public static IReadOnlyList<StewardNightCall> Highlights(IReadOnlyList<StewardNightCall> night, int room)
    {
        if (night.Count <= room) return night;
        int Rank(StewardNightCall call) => (call.Who, call.Urgent) switch
        {
            (StewardCallResponder.Missed, true) => 0,
            (StewardCallResponder.You or StewardCallResponder.Autopilot or StewardCallResponder.Sent, true) => 1,
            (StewardCallResponder.You or StewardCallResponder.Autopilot or StewardCallResponder.Sent, false) => 2,
            (StewardCallResponder.Missed, false) => 3,
            _ => 4,
        };
        return night.Select((call, index) => (call, index)).OrderBy(item => Rank(item.call)).ThenBy(item => item.index).Take(room)
            .OrderBy(item => item.index).Select(item => item.call).ToArray();
    }
}

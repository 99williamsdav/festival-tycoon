using System.Text.Json.Serialization;

namespace Festival.Simulation;

public enum StewardCallKind { Fight = 1, Argument, Collapse, StuckInLoo, BrokenTap, FullBin, LooseCow }

/// <summary>
/// One radio call: an incident the festival already runs, from the tick it began. Control puts it out over the radio
/// <see cref="StewardShiftRules.RadioDelayTicks"/> later (<see cref="HeardTick"/>); the player getting there first counts
/// the same. It closes when the incident does, whoever saw to it; <see cref="AnsweredTick"/> is when the player acted on it.
/// </summary>
/// <param name="Subject">The person (initiator, for a fight), facility, bin or cow it's about.</param>
public sealed record StewardCall(string Id, StewardCallKind Kind, string Subject, long OpenedTick, long ClosedTick = -1, long AnsweredTick = -1)
{
    [JsonIgnore] public bool Open => ClosedTick < 0;
    [JsonIgnore] public long HeardTick => OpenedTick + StewardShiftRules.RadioDelayTicks(Kind);
    [JsonIgnore] public bool Urgent => StewardShiftRules.Urgent(Kind);
}

/// <summary>
/// The radio. Calls are saved rather than derived: whether the player answered, and when, can't be worked out
/// afterwards. They follow the incidents at the end of every tick and after every command, so a save's open calls are
/// exactly its live incidents, which the restore checks.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The incidents that make radio calls now, by call id: fights, arguments, collapses, jammed loos, broken taps, full bins, loose cows.</summary>
    private List<(string Id, StewardCallKind Kind, string Subject)> LiveStewardIncidents()
    {
        var live = new List<(string, StewardCallKind, string)>();
        if (_disorder is { } d)
        {
            foreach (var origin in d.Incidents)
                if (origin.InjuryTick == -1 && PersonIn(PersonView.Disorder, origin.InitiatorId) is { ConductStage: DisorderStage.Fight } initiator &&
                    initiator.ConductStageTick == origin.FightTick)
                    live.Add(($"fight:{origin.InitiatorId}:{origin.FightTick}", StewardCallKind.Fight, origin.InitiatorId.ToString()));
            foreach (var person in PeopleIn(PersonView.Disorder))
                if (person.ConductStage == DisorderStage.Argument && person.Admitted && !person.Departed)
                    live.Add(($"argument:{person.Id}:{person.ConductStageTick}", StewardCallKind.Argument, person.Id.ToString()));
        }
        foreach (var person in PeopleIn(PersonView.Medical))
            if (person.HealthStage is MedicalStage.Collapsed or MedicalStage.Critical && person.Admitted && !person.Departed && !IsPlayerSteward(person.Id))
                live.Add(($"collapse:{person.Id}:{person.HealthCollapseTick}", StewardCallKind.Collapse, person.Id.ToString()));
        foreach (var fault in OpenFaults)
            if (fault.Stage == FacilityFaultStage.Active && fault.Kind is FacilityFaultKind.StuckInToilet or FacilityFaultKind.BrokenTap)
                live.Add((fault.Id, fault.Kind == FacilityFaultKind.StuckInToilet ? StewardCallKind.StuckInLoo : StewardCallKind.BrokenTap, fault.FacilityId));
        foreach (var bin in CaptureBins())
            if (bin.CanEmpty) live.Add(($"bin:{bin.Id}", StewardCallKind.FullBin, bin.Id));
        foreach (var cow in _cows?.Loose ?? [])
            live.Add(($"cow:{cow.Id}", StewardCallKind.LooseCow, cow.Id));
        return live;
    }

    /// <summary>Opens a call for each new incident and closes those whose incident is over, scoring them; old closed calls drop off.</summary>
    private void RefreshStewardCalls()
    {
        if (_stewardShift is not { PlayerId: not null } shift) return;
        var live = LiveStewardIncidents();
        var calls = shift.Calls ?? [];
        var liveIds = live.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var openIds = calls.Where(call => call.Open).Select(call => call.Id).ToHashSet(StringComparer.Ordinal);
        var closing = calls.Any(call => call.Open && !liveIds.Contains(call.Id));
        var opening = live.Any(item => !openIds.Contains(item.Id));
        var expiring = calls.Any(call => !call.Open && call.ClosedTick < CurrentTick - StewardShiftRules.ClosedCallTicks);
        if (!closing && !opening && !expiring) return;
        var score = shift.Score ?? new();
        var next = new List<StewardCall>();
        foreach (var call in calls)
        {
            if (!call.Open || liveIds.Contains(call.Id)) { next.Add(call); continue; }
            var closed = call with { ClosedTick = CurrentTick };
            score = Counted(score, closed);
            next.Add(closed);
        }
        foreach (var (id, kind, subject) in live)
            if (!openIds.Contains(id)) next.Add(new(id, kind, subject, CurrentTick));
        _stewardShift = shift with { Calls = Pruned(next), Score = score };
    }

    /// <summary>The last few closed calls stay, briefly, for the radio strip.</summary>
    private StewardCall[] Pruned(List<StewardCall> calls)
    {
        var keep = calls.Where(call => !call.Open && call.ClosedTick >= CurrentTick - StewardShiftRules.ClosedCallTicks)
            .TakeLast(StewardShiftRules.ClosedCallsKept).ToHashSet();
        return calls.Where(call => call.Open || keep.Contains(call)).ToArray();
    }

    /// <summary>A closed call counts once it went out over the radio, or the player got to it first; answered, with the time it took.</summary>
    private static StewardShiftScore Counted(StewardShiftScore score, StewardCall call)
    {
        var answered = call.AnsweredTick >= 0;
        if (!answered && call.ClosedTick < call.HeardTick) return score;
        return score with
        {
            Calls = score.Calls + 1, Answered = score.Answered + (answered ? 1 : 0),
            ResponseTicks = score.ResponseTicks + (answered ? Math.Max(0, call.AnsweredTick - call.HeardTick) : 0),
            UrgentMissed = score.UrgentMissed + (!answered && call.Urgent ? 1 : 0),
        };
    }

    /// <summary>Marks the open calls this action saw to as answered by the player.</summary>
    private void AnswerStewardCalls(StewardActionProgress held, ulong person, string facility)
    {
        var (kind, subject) = held.Kind switch
        {
            StewardActionKind.Calm => (StewardCallKind.Argument, person.ToString()),
            StewardActionKind.BreakUp => (StewardCallKind.Fight, (GuestFightOrigin(person)?.InitiatorId ?? person).ToString()),
            StewardActionKind.RadioMedic or StewardActionKind.StandBy => (StewardCallKind.Collapse, person.ToString()),
            StewardActionKind.FreeLoo => (StewardCallKind.StuckInLoo, facility),
            StewardActionKind.BodgeTap => (StewardCallKind.BrokenTap, facility),
            StewardActionKind.EmptyBin => (StewardCallKind.FullBin, facility),
            StewardActionKind.HerdCow => (StewardCallKind.LooseCow, facility),
            _ => ((StewardCallKind)0, ""),
        };
        if (kind == 0 || _stewardShift?.Calls is not { } calls || !calls.Any(call => call.Open && call.Kind == kind && call.Subject == subject && call.AnsweredTick < 0)) return;
        _stewardShift = _stewardShift with { Calls = calls.Select(call => call.Open && call.Kind == kind && call.Subject == subject && call.AnsweredTick < 0
            ? call with { AnsweredTick = CurrentTick } : call).ToArray() };
    }

    /// <summary>
    /// On a shift the crew's medic hears of a collapse over the radio: they set off once Control has put the call out,
    /// unless the player radios it in first. Outside a shift, never.
    /// </summary>
    /// <remarks>Bounded by the collapse itself, so a collapse that somehow made no call still gets the medic.</remarks>
    private bool CollapseAwaitsTheRadio(ulong id) => _stewardShift is { PlayerId: not null } shift &&
        CurrentTick < _persons[id].HealthCollapseTick + StewardShiftRules.RadioDelayTicks(StewardCallKind.Collapse) &&
        !(shift.Calls ?? []).Any(call => call.Open && call.Kind == StewardCallKind.Collapse && call.Subject == id.ToString() && call.HeardTick <= CurrentTick);

    /// <summary>The shift is over: every call still open closes now and is scored.</summary>
    private void CloseStewardCalls()
    {
        if (_stewardShift?.Calls is not { } calls) return;
        var score = _stewardShift.Score ?? new();
        var closed = calls.Select(call =>
        {
            if (!call.Open) return call;
            var done = call with { ClosedTick = CurrentTick };
            score = Counted(score, done);
            return done;
        }).ToList();
        _stewardShift = _stewardShift with { Calls = Pruned(closed), Score = score };
    }

    /// <summary>Whether the saved open calls are exactly the live incidents.</summary>
    private string? StewardCallsIssue()
    {
        if (_stewardShift is not { PlayerId: not null, Calls: { } calls }) return null;
        var open = calls.Where(call => call.Open).Select(call => (call.Id, call.Kind, call.Subject)).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var live = LiveStewardIncidents().OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        return open.SequenceEqual(live) ? null : "The steward shift's radio calls don't match what's going on.";
    }

    /// <summary>Where a call's incident is now: the person, the loo door, the tap, the bin or the cow. Null once it's gone.</summary>
    public (int X, int Z)? StewardCallPoint(StewardCall call)
    {
        var target = call.Kind switch
        {
            StewardCallKind.Fight or StewardCallKind.Argument or StewardCallKind.Collapse => $"person:{call.Subject}",
            StewardCallKind.StuckInLoo => $"toilet:{call.Subject}",
            StewardCallKind.BrokenTap => $"tap:{call.Subject}",
            StewardCallKind.FullBin => $"bin:{call.Subject}",
            _ => $"cow:{call.Subject}",
        };
        return StewardTargetPoint(target) is { } point ? (point.X, point.Z) : null;
    }

    /// <summary>The nearest landmark to a point, as Control would say it: a stage, a bar or van, the loos, the water, first aid or the gate.</summary>
    public string StewardLandmark(int x, int z)
    {
        var marks = new List<(string Name, GridCell Cell)>();
        foreach (var stage in Stages) marks.Add((stage.Id == FestivalStages.PondId ? "the Pond Stage" : "the main stage", stage.PerformerMarks[0]));
        foreach (var vendor in Vendors) marks.Add((Stalls.Label(vendor.Id), vendor.Cell));
        foreach (var toilet in EffectiveToilets(_facilities)) marks.Add(("the loos", toilet.Cell));
        foreach (var tap in WaterPoints()) marks.Add(("the water", tap.Cell));
        marks.Add(("first aid", CaptureRestCentre()));
        marks.Add(("the gate", MedicalExitCell));
        return marks.OrderBy(mark =>
        {
            var (mx, mz) = TraversalGrid.CellCentre(mark.Cell);
            return (long)(mx - x) * (mx - x) + (long)(mz - z) * (mz - z);
        }).ThenBy(mark => mark.Name, StringComparer.Ordinal).First().Name;
    }
}

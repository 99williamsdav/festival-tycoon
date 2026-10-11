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
    /// <summary>
    /// A hands-on day only: the hired crew member Control has on it (a steward, the medic, maintenance), kept as it was
    /// when the call closed. A covered call isn't the player's: it shows as covered and, unless they answered it anyway,
    /// is left out of their score.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ulong? CoveredBy { get; init; }
    /// <summary>Answered by the player's steward working by themselves (the autopilot) rather than by the player's hands.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Auto { get; init; }
    /// <summary>
    /// A hands-on day only: the player radioed the crew member covering it (<see cref="StewardRadioCommand"/>). It
    /// stays the player's call and, left to that crew member, counts half (<see cref="StewardShiftScore.Dispatched"/>).
    /// Held only while someone hired covers it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Radioed { get; init; }
    /// <summary>A hands-on day only: the landmark Control named as the call opened ("Bar 2", "the main stage"), for the results paper.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Where { get; init; }
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
        // A hands-on day: who in the hired crew holds each open call follows the incidents too.
        var covering = IsHandsOn && calls.Any(call => call.Open && liveIds.Contains(call.Id) && call.CoveredBy != CrewHolder(call));
        if (!closing && !opening && !expiring && !covering) return;
        var score = shift.Score ?? new();
        var log = shift.Log;
        var next = new List<StewardCall>();
        foreach (var call in calls)
        {
            if (!call.Open) { next.Add(call); continue; }
            if (liveIds.Contains(call.Id))
            {
                // A radioed call stays radioed while someone hired is on it.
                if (IsHandsOn && CrewHolder(call) is var holder) next.Add(call with { CoveredBy = holder, Radioed = call.Radioed && holder is not null });
                else next.Add(call);
                continue;
            }
            var closed = call with { ClosedTick = CurrentTick };
            score = Counted(score, closed, UrgentDealtWith(closed));
            log = Logged(log, closed);
            next.Add(closed);
        }
        foreach (var (id, kind, subject) in live)
            if (!openIds.Contains(id))
            {
                StewardCall call = new(id, kind, subject, CurrentTick);
                next.Add(IsHandsOn ? call with { CoveredBy = CrewHolder(call), Where = CallPlace(call) } : call);
            }
        _stewardShift = shift with { Calls = Pruned(next), Score = score, Log = log };
    }

    /// <summary>Where Control says a call is, by the nearest landmark; null once its incident has gone.</summary>
    private string? CallPlace(StewardCall call) => StewardCallPoint(call) is { } point ? StewardLandmark(point.X, point.Z) : null;

    /// <summary>
    /// A hands-on day keeps every call of the night for the results paper: each closed call that went out over the
    /// radio or that the player answered, in the order they closed. Exactly the calls the score counted, plus those
    /// the hired crew covered and the player left to them.
    /// </summary>
    private StewardCall[]? Logged(StewardCall[]? log, StewardCall closed) =>
        !IsHandsOn || !InStewardLog(closed) ? log : [.. log ?? [], closed];

    private static bool InStewardLog(StewardCall closed) => closed.AnsweredTick >= 0 || closed.ClosedTick >= closed.HeardTick;

    /// <summary>The last few closed calls stay, briefly, for the radio strip.</summary>
    private StewardCall[] Pruned(List<StewardCall> calls)
    {
        var keep = calls.Where(call => !call.Open && call.ClosedTick >= CurrentTick - StewardShiftRules.ClosedCallTicks)
            .TakeLast(StewardShiftRules.ClosedCallsKept).ToHashSet();
        return calls.Where(call => call.Open || keep.Contains(call)).ToArray();
    }

    /// <summary>
    /// A closed call counts once it went out over the radio, or the player got to it first; answered, with the time it
    /// took. An urgent one left unanswered counts against the grade only when nobody dealt with it either.
    /// </summary>
    private static StewardShiftScore Counted(StewardShiftScore score, StewardCall call, bool dealtWith)
    {
        var answered = call.AnsweredTick >= 0;
        if (!answered && call.ClosedTick < call.HeardTick) return score;
        // On a hands-on day Control gave it to the hired crew: not the player's call, unless they answered it anyway.
        if (!answered && call.CoveredBy is not null && !call.Radioed) return score with { Covered = score.Covered + 1 };
        // The player radioed the crew onto it and left it to them: their call, half answered, with no response time;
        // a fight counts among those they dealt with, with no credit for preventing or stopping it.
        if (!answered && call.Radioed)
            return score with { Calls = score.Calls + 1, Dispatched = score.Dispatched + 1,
                FightsDispatched = score.FightsDispatched + (call.Kind == StewardCallKind.Fight ? 1 : 0) };
        return score with
        {
            Calls = score.Calls + 1, Answered = score.Answered + (answered ? 1 : 0),
            AutoAnswered = score.AutoAnswered + (answered && call.Auto ? 1 : 0),
            ResponseTicks = score.ResponseTicks + (answered ? Math.Max(0, call.AnsweredTick - call.HeardTick) : 0),
            UrgentUnhandled = score.UrgentUnhandled + (!answered && call.Urgent && !dealtWith ? 1 : 0),
        };
    }

    /// <summary>
    /// Whether someone dealt with an urgent call's incident, as it closes: for a fight, a steward (the crew's or the
    /// player's) made the fight's one handling attempt, whatever came of it; for a collapse, the patient was treated or
    /// taken out, or a medic is on the way or with them (a shift ending mid-response). A fight that ran on to an injury
    /// untouched, or a collapse that ended in a death no medic was sent to, was nobody's. Other calls: always.
    /// </summary>
    private bool UrgentDealtWith(StewardCall call)
    {
        switch (call.Kind)
        {
            case StewardCallKind.Fight:
            {
                var parts = call.Id.Split(':');
                if (parts.Length != 3 || !ulong.TryParse(parts[1], out var initiator) || !long.TryParse(parts[2], out var fightTick)) return false;
                return _disorder?.Incidents.LastOrDefault(item => item.InitiatorId == initiator && item.FightTick == fightTick)?.HandlingAttempt is not null;
            }
            case StewardCallKind.Collapse:
            {
                if (!ulong.TryParse(call.Subject, out var patient)) return false;
                return PersonIn(PersonView.Medical, patient)?.HealthStage is MedicalStage.Treated or MedicalStage.Removed ||
                    GetMedicResponses().Any(medic => medic.PatientId == patient && medic.Stage is MedicalResponseStage.Travelling or MedicalResponseStage.Treating or MedicalResponseStage.Removing);
            }
            default:
                return true;
        }
    }

    /// <summary>A medic is with the patient: treating them, taking them out, or arrived at their side.</summary>
    private bool MedicReached(ulong patient) => GetMedicResponses().Any(medic => medic.PatientId == patient &&
        (medic.Stage is MedicalResponseStage.Treating or MedicalResponseStage.Removing ||
         medic.Stage == MedicalResponseStage.Travelling && MedicalTreatmentPositionValid(medic.WorkerId, patient)));

    /// <summary>
    /// Marks the open calls this action saw to as answered by the player. Standing by a collapsed guest answers their
    /// call only before a medic has reached them; after that it's the medic's.
    /// </summary>
    private void AnswerStewardCalls(StewardActionProgress held, ulong person, string facility)
    {
        if (held.Kind == StewardActionKind.StandBy && MedicReached(person)) return;
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
            ? call with { AnsweredTick = CurrentTick, Auto = held.Auto } : call).ToArray() };
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
        var log = _stewardShift.Log;
        var closed = calls.Select(call =>
        {
            if (!call.Open) return call;
            var done = call with { ClosedTick = CurrentTick };
            score = Counted(score, done, UrgentDealtWith(done));
            log = Logged(log, done);
            return done;
        }).ToList();
        _stewardShift = _stewardShift with { Calls = Pruned(closed), Score = score, Log = log };
    }

    /// <summary>Whether the saved open calls are exactly the live incidents.</summary>
    private string? StewardCallsIssue()
    {
        if (_stewardShift is not { PlayerId: not null, Calls: { } calls }) return null;
        var open = calls.Where(call => call.Open).Select(call => (call.Id, call.Kind, call.Subject)).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var live = LiveStewardIncidents().OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (!open.SequenceEqual(live)) return "The steward shift's radio calls don't match what's going on.";
        return calls.Any(call => call.Open && call.CoveredBy != (IsHandsOn ? CrewHolder(call) : null))
            ? "The radio's covered calls don't match who in the crew is on them." : null;
    }

    /// <summary>
    /// The hired crew member holding a call's incident now, never the player's own steward: the steward walking to or
    /// handling a fight or argument (or who made the fight's one attempt), the medic on the way to or with a collapse,
    /// whoever is freeing the loo or mending the tap, the steward off to empty the bin, the one herding the cow.
    /// </summary>
    private ulong? CrewHolder(StewardCall call)
    {
        var me = _stewardShift?.PlayerId;
        ulong? Crew(ulong? id) => id is { } worker && worker != me ? worker : null;
        ulong? Steward(ulong person) => Crew(GetStewardResponses().FirstOrDefault(job => job.TargetId == person && StewardBusy(job))?.WorkerId ??
            CaptureStaffInterventions().FirstOrDefault(job => job.GuestId == person && InterventionBusy(job))?.WorkerId);
        switch (call.Kind)
        {
            case StewardCallKind.Fight:
            {
                var parts = call.Id.Split(':');
                if (parts.Length != 3 || !ulong.TryParse(parts[1], out var initiator) || !long.TryParse(parts[2], out var fightTick)) return null;
                var origin = _disorder?.Incidents.LastOrDefault(item => item.InitiatorId == initiator && item.FightTick == fightTick);
                if (origin?.HandlingAttempt is { } attempt) return Crew(attempt.WorkerId);
                return Steward(initiator) ?? (origin is null ? null : Steward(origin.OpponentId));
            }
            case StewardCallKind.Argument:
                return ulong.TryParse(call.Subject, out var arguing) ? Steward(arguing) : null;
            case StewardCallKind.Collapse:
                return ulong.TryParse(call.Subject, out var patient) ? Crew(GetMedicResponses().FirstOrDefault(medic => medic.PatientId == patient &&
                    medic.Stage is MedicalResponseStage.Travelling or MedicalResponseStage.Treating or MedicalResponseStage.Removing)?.WorkerId) : null;
            case StewardCallKind.StuckInLoo or StewardCallKind.BrokenTap:
                return Crew(OpenFaults.FirstOrDefault(fault => fault.Id == call.Id)?.WorkerId);
            case StewardCallKind.FullBin:
                return Crew(_litter?.Sweeps.FirstOrDefault(sweep => sweep.Remaining > 0 && sweep.TargetIsBin && sweep.TargetId == call.Subject)?.WorkerId);
            default:
                return Crew(_cows?.Loose.FirstOrDefault(cow => cow.Id == call.Subject)?.HerderId);
        }
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

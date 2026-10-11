namespace Festival.Simulation;

/// <summary>What the player can radio Control for from first person: the medic, Dirty Henry (Dav's Lav-Sucker) or one of the hired crew.</summary>
public enum StewardRadioJob { Medic = 1, Henry, Crew }

/// <summary>
/// The player's steward, in first person, radios a job out (the radial radio menu): the medic to a collapse, Dirty
/// Henry to a loo, or a named hired steward or maintenance worker to a fight, argument, full bin, jammed loo, broken
/// tap or loose cow. <paramref name="TargetId"/> is a reach-style target (<c>person:</c>, <c>toilet:</c>, <c>tap:</c>,
/// <c>bin:</c>, <c>cow:</c>); <paramref name="WorkerId"/> names the crew member, for <see cref="StewardRadioJob.Crew"/> only.
/// Each goes through the manager's own dispatch for it, so the same rules hold; nothing queues behind a busy worker
/// but a bin, which waits for the first steward free as the manager's "Empty now" does.
/// </summary>
public sealed record StewardRadioCommand(StewardRadioJob Job, string TargetId, ulong? WorkerId = null) : SessionCommand;

/// <summary>The player raises (or puts away) the laminated site plan in their right hand.</summary>
public sealed record SetSitePlanCommand(bool Up) : SessionCommand;

/// <summary>
/// One of the hired crew the player could radio: a steward or the maintenance worker, where they are, and what they're
/// busy with (null when free). Data only, for the radio menu's crew fan.
/// </summary>
public sealed record StewardRadioCrewMember(ulong WorkerId, string Name, bool Maintenance, string? Busy, int XMillimetres, int ZMillimetres);

public static class StewardRadioRules
{
    /// <summary>With the site plan up, a walk is this much of a walk, and there's no running.</summary>
    public const int PlanWalkPermille = 600;
    /// <summary>"Under the crosshair": within this far, and this close to the line of sight (cos 12°).</summary>
    public const int CrosshairMillimetres = 40_000, CrosshairConeCosPermille = 978;
}

public sealed partial class GameSession
{
    // ---- The site plan ----------------------------------------------------------------------------------------

    private CommandResult? ValidateSitePlan(EntityId? target, SetSitePlanCommand command)
    {
        if (target is not null || _stewardShift?.PlayerId is null || _preparation?.Status != PreparationStatus.Running)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Nobody is on shift.");
        if (_stewardShift.Autopilot is not null)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Your steward is working by themselves: take them over first.");
        return command.Up == _stewardShift.PlanUp
            ? CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, command.Up ? "The plan's already up." : "The plan's already away.") : null;
    }

    /// <summary>Up, the right hand holds the plan: anything it was doing is let go.</summary>
    private void ApplySitePlan(SetSitePlanCommand command)
    {
        if (command.Up && _stewardShift!.Action is { } held) DropStewardAction(held, "You got the plan out.");
        _stewardShift = _stewardShift! with { PlanUp = command.Up };
    }

    // ---- The radio --------------------------------------------------------------------------------------------

    /// <summary>
    /// The hired crew the player can radio on a hands-on day, stewards (perk extras included) then maintenance, by id:
    /// never the player's own steward. Empty on the standalone shift, where Control runs the crew.
    /// </summary>
    public IReadOnlyList<StewardRadioCrewMember> StewardRadioCrew()
    {
        if (!IsHandsOn || _stewardShift?.PlayerId is not { } me || _preparation?.Status != PreparationStatus.Running) return [];
        var crew = new List<StewardRadioCrewMember>();
        void Add(ulong id, bool maintenance)
        {
            if (id == me || PersonIn(PersonView.Roster, id) is not { Admitted: true, Departed: false } person || !_navigationAgents.TryGetValue(new(id), out var nav)) return;
            crew.Add(new(id, person.Name, maintenance, RadioCrewBusy(id, maintenance), nav.XMillimetres, nav.ZMillimetres));
        }
        foreach (var job in GetStewardResponses().OrderBy(job => job.WorkerId)) Add(job.WorkerId, false);
        if (_equipment?.WorkerId is { } mender && !GetStewardResponses().Any(job => job.WorkerId == mender)) Add(mender, true);
        return crew;
    }

    /// <summary>What a crew member is busy with, in a few words; null when they're free to be sent.</summary>
    private string? RadioCrewBusy(ulong id, bool maintenance)
    {
        if (FaultWorkOf(id) is { } fault)
            return CowWorkOwns(id) ? "herding a cow" : fault.Kind switch
            {
                FacilityFaultKind.StuckInToilet => "freeing a loo", FacilityFaultKind.BrokenTap => "fixing a tap", _ => "on a repair",
            };
        if (CowWorkOwns(id)) return "herding a cow";
        if (_litter?.Sweeps.FirstOrDefault(sweep => sweep.WorkerId == id && sweep.Remaining > 0) is { } sweep) return sweep.TargetIsBin ? "emptying a bin" : "sweeping up";
        if (CleanupOwnsNavigation(id) || WasteOwnsNavigation(id)) return "sweeping up";
        if (maintenance && _equipment is { JobStage: not MaintenanceStage.None }) return "on a maintenance job";
        if (!maintenance && GetStewardResponses().FirstOrDefault(job => job.WorkerId == id) is { } response && (StewardBusy(response) || response.Incapacitated))
            return response.Incapacitated ? "hurt" : "on a call";
        return StaffUnavailableReason(id) is null ? null : "busy";
    }

    /// <summary>Whether a target is the kind of thing this job goes to (whether it can go now aside).</summary>
    private bool StewardRadioSuits(StewardRadioJob job, string target)
    {
        var (prefix, key) = SplitTarget(target);
        ulong.TryParse(key, out var id);
        return job switch
        {
            StewardRadioJob.Medic => prefix == "person" && $"person:{id}" == target && CollapsedHere(id),
            StewardRadioJob.Henry => prefix == "toilet" && EffectiveToilets(_facilities).Any(t => t.Id == key),
            StewardRadioJob.Crew => prefix switch
            {
                "person" => $"person:{id}" == target && !IsPlayerSteward(id) && PersonIn(PersonView.Disorder, id) is { ConductStage: DisorderStage.Fight or DisorderStage.Argument },
                "bin" => CaptureBins().FirstOrDefault(b => b.Id == key) is { Pieces: > 0 },
                "toilet" => ActiveFault(key) is { Kind: FacilityFaultKind.StuckInToilet },
                "tap" => ActiveFault(key) is { Kind: FacilityFaultKind.BrokenTap },
                "cow" => _cows?.Loose.Any(c => c.Id == key) == true,
                _ => false,
            },
            _ => false,
        };
    }

    /// <summary>
    /// Where a radio job would go, as the menu's target line has it: the current call (<paramref name="callId"/>) if the
    /// job can take it; otherwise whatever suits it under the crosshair (within 40 m, about 12° of the facing); otherwise
    /// the nearest to the player: for Henry, a loo; for the medic and crew, an open call heard on the radio. One the job
    /// can go to now (or is only busy for) is preferred at each step, so a call the medic is already at doesn't hide a
    /// second collapse; failing any, the first that suits, so the menu can say why not. Null when nothing suits. Data only.
    /// </summary>
    public string? StewardRadioTarget(StewardRadioJob job, int facingDegrees, string? callId = null)
    {
        if (_stewardShift is not { PlayerId: { } me } shift || !_navigationAgents.TryGetValue(new(me), out var nav) || !Enum.IsDefined(job)) return null;
        long Distance(int x, int z) => (long)(x - nav.XMillimetres) * (x - nav.XMillimetres) + (long)(z - nav.ZMillimetres) * (z - nav.ZMillimetres);
        var candidates = new List<string>();
        switch (job)
        {
            case StewardRadioJob.Medic:
                foreach (var person in PeopleIn(PersonView.Medical)) if (CollapsedHere(person.Id)) candidates.Add($"person:{person.Id}");
                break;
            case StewardRadioJob.Henry:
                foreach (var toilet in EffectiveToilets(_facilities)) candidates.Add($"toilet:{toilet.Id}");
                break;
            default:
                foreach (var person in PeopleIn(PersonView.Disorder)) candidates.Add($"person:{person.Id}");
                foreach (var target in StewardFacilityTargets()) candidates.Add(target);
                foreach (var cow in _cows?.Loose ?? []) candidates.Add($"cow:{cow.Id}");
                break;
        }
        var suited = candidates.Where(target => StewardRadioSuits(job, target) && StewardTargetPoint(target) is not null)
            .Select(target => (Target: target, Point: StewardTargetPoint(target)!.Value))
            .OrderBy(item => Distance(item.Point.X, item.Point.Z)).ThenBy(item => item.Target, StringComparer.Ordinal).ToArray();
        var current = callId is not null && (shift.Calls ?? []).FirstOrDefault(c => c.Open && c.Id == callId) is { } call && CallTargetId(call) is var called &&
            StewardRadioSuits(job, called) ? called : null;
        var (fx, fz) = StewardShiftRules.Facing(facingDegrees);
        var sighted = suited.Where(item =>
            {
                long dx = item.Point.X - nav.XMillimetres, dz = item.Point.Z - nav.ZMillimetres;
                var length = IntegerSquareRoot(dx * dx + dz * dz);
                return length <= StewardRadioRules.CrosshairMillimetres && fx * dx + fz * dz >= StewardRadioRules.CrosshairConeCosPermille * length;
            }).Select(item => item.Target).ToArray();
        var heard = (shift.Calls ?? []).Where(c => c.Open && c.HeardTick <= CurrentTick).Select(CallTargetId).ToHashSet(StringComparer.Ordinal);
        var nearest = suited.Where(item => job == StewardRadioJob.Henry || heard.Contains(item.Target)).Select(item => item.Target).ToArray();
        // Can go (or would go, but busy): someone could be sent there now.
        bool Goes(string target)
        {
            if (job != StewardRadioJob.Crew) return StewardRadioPlan(job, target, null) is var plan && (plan.Apply is not null || plan.Busy);
            return StewardRadioCrew().Any(member => StewardRadioPlan(job, target, member.WorkerId) is var crew && (crew.Apply is not null || crew.Busy));
        }
        var order = (current is null ? [] : new[] { current }).Concat(sighted).Concat(nearest).ToArray();
        return order.FirstOrDefault(Goes) ?? order.FirstOrDefault();
    }

    /// <summary>Why this radio job can't go now (busy, nobody to send, the wrong target); null when it can. Data only.</summary>
    public string? StewardRadioIssue(StewardRadioJob job, string target, ulong? workerId = null) => StewardRadioPlan(job, target, workerId).Reason;

    /// <summary>
    /// As <see cref="StewardRadioIssue"/>, with whether who'd go is busy (or Henry already booked there). A busy bin order
    /// has no issue: it waits on the list for the first steward free. Data only.
    /// </summary>
    public (string? Reason, bool Busy) StewardRadioCheck(StewardRadioJob job, string target, ulong? workerId = null)
    {
        var plan = StewardRadioPlan(job, target, workerId);
        return (plan.Reason, plan.Busy);
    }

    /// <summary>
    /// What a radio job does, through the manager's own path for it, or why it can't. <c>Busy</c> marks a refusal
    /// because who'd go is busy (or Henry is already booked there).
    /// </summary>
    private (string? Reason, bool Busy, Action? Apply) StewardRadioPlan(StewardRadioJob job, string target, ulong? workerId)
    {
        if (_preparation?.Status != PreparationStatus.Running || _stewardShift?.PlayerId is null) return ("Nobody is on shift.", false, null);
        if (target is null || target.Length is 0 or > 64) return ("Nothing to send them to.", false, null);
        var (prefix, key) = SplitTarget(target);
        ulong.TryParse(key, out var id);
        if (job != StewardRadioJob.Crew && workerId is not null) return ("Only the crew are radioed by name.", false, null);
        switch (job)
        {
            case StewardRadioJob.Medic:
            {
                if (GetMedicResponses().Count == 0) return ("No medic hired.", false, null);
                if (!StewardRadioSuits(job, target)) return ("Nobody's down there.", false, null);
                if (HasClaim(id, PersonClaim.MedicPatient)) return ("The medic's already with them.", false, null);
                if (SelectRoleResponse(ResponseRole.Medic, id, out _) is not MedicalCommand dispatch)
                    return (GetMedicResponses().All(medic => MedicBusy(medic) || StaffUnavailableReason(medic.WorkerId) is not null)
                        ? ("The medic's busy: stay with them.", true, null) : ("The medic can't get to them.", false, null));
                return (null, false, () => ApplyMedicalCommand(dispatch));
            }
            case StewardRadioJob.Henry:
            {
                if (!StewardRadioSuits(job, target)) return ("Henry only empties the loos.", false, null);
                if (LavSuckerUnavailable(key) is { } why) return (why, LavSuckerBooked(key), null);
                return (null, false, () => ApplyCallLavSucker(new(key)));
            }
            case StewardRadioJob.Crew:
            {
                if (!IsHandsOn) return ("Control runs the crew on this shift.", false, null);
                if (workerId is not { } worker || StewardRadioCrew().FirstOrDefault(member => member.WorkerId == worker) is not { } member)
                    return ("Pick someone from the crew.", false, null);
                if (!StewardRadioSuits(job, target)) return ("Nothing for the crew there.", false, null);
                var name = member.Name.Split(' ')[0];
                var busy = member.Busy is not null;
                (string?, bool, Action?) Busy() => ($"{name}'s busy {member.Busy}.", true, null);
                if (member.Maintenance && prefix != "tap") return ($"That's a job for a steward, not {name}.", false, null);
                switch (prefix)
                {
                    case "person":
                    {
                        if (busy) return Busy();
                        var dispatch = new DisorderCommand(DisorderAction.DispatchSecurity, id, worker);
                        return ValidateDisorderCommand(null, dispatch) is { } refused ? (refused.Message, false, null) : (null, false, () => ApplyDisorderCommand(dispatch));
                    }
                    case "bin":
                    {
                        if (BinEmptier(key, worker, out var reason) is { } pick)
                            return (null, false, () =>
                            {
                                if (BinEmptyRequested(key)) SetEmptyRequests(_litter!.EmptyRequests!.Where(bin => bin != key));
                                SendToEmptyBin(key, pick, "Steward sent to empty a bin");
                            });
                        // All the stewards busy: the order waits for the first one free, as the manager's does.
                        if (busy && BinEmptyQueueable(key, out _)) return (null, true, () => SetEmptyRequests((_litter!.EmptyRequests ?? []).Append(key)));
                        return busy ? Busy() : (reason ?? "They can't get to that bin.", false, null);
                    }
                    case "cow":
                    {
                        if (_cows!.Loose.First(c => c.Id == key).HerderId is not null) return ("A steward is already on that cow.", false, null);
                        if (busy) return Busy();
                        return (null, false, () => SendToHerdCow(key, worker));
                    }
                    default:
                    {
                        // A jammed loo is a steward's; a broken tap is maintenance's if there is one, else a steward bodges it.
                        var fault = ActiveFault(key)!;
                        if (fault.WorkerId is { } on) return ($"{CrewName(on)}'s already on it.", false, null);
                        var maintenanceJob = fault.Kind == FacilityFaultKind.BrokenTap && _equipment?.WorkerId is not null;
                        if (maintenanceJob != member.Maintenance) return (maintenanceJob ? "That tap's a job for maintenance." : $"That's a job for a steward, not {name}.", false, null);
                        if (busy) return Busy();
                        if (FaultWorkCell(fault, worker) is not { } cell) return ($"{name} can't get to it.", false, null);
                        return (null, false, () => AssignFaultWorker(fault, worker, cell));
                    }
                }
            }
            default:
                return ("Unknown radio job.", false, null);
        }
    }

    private string CrewName(ulong id) => _persons.TryGet(id, out var person) ? person.Name.Split(' ')[0] : "Someone";

    private CommandResult? ValidateStewardRadio(EntityId? target, StewardRadioCommand command)
    {
        if (target is not null || _stewardShift?.PlayerId is null || _preparation?.Status != PreparationStatus.Running)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Nobody is on shift.");
        if (_stewardShift.Autopilot is not null)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Your steward is working by themselves: take them over first.");
        if (_stewardShift.PlanUp) return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Put the site plan away first.");
        if (!Enum.IsDefined(command.Job)) return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Unknown radio job.");
        return StewardRadioPlan(command.Job, command.TargetId, command.WorkerId) is { Apply: null } plan
            ? CommandResult.Rejected(plan.Busy ? CommandReasonCode.AlreadyCommitted : CommandReasonCode.InvalidParameter, plan.Reason!) : null;
    }

    private void ApplyStewardRadio(StewardRadioCommand command)
    {
        StewardRadioPlan(command.Job, command.TargetId, command.WorkerId).Apply!();
        Score(s => s with { RadioJobs = s.RadioJobs + 1 });
    }
}

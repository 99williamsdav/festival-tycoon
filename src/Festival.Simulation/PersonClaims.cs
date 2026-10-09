namespace Festival.Simulation;

/// <summary>
/// Everything that currently has a hold on a person. Systems ask this one table
/// whether a person is free for them instead of consulting each other's state.
/// Each claim is derived from authoritative state, so it can never drift.
/// </summary>
[Flags]
public enum PersonClaim
{
    None = 0,
    /// <summary>Holds a place in a physical free-water line.</summary>
    WaterPlace = 1 << 0,
    /// <summary>Walking to, queuing for or drinking free water.</summary>
    SeekingWater = 1 << 1,
    Resting = 1 << 2,
    AwaitingMedic = 1 << 3,
    Leaving = 1 << 4,
    Collapsed = 1 << 5,
    /// <summary>Walking to or queuing at a food or drinks vendor.</summary>
    Shopping = 1 << 6,
    /// <summary>Anywhere in a toilet visit, from approach to walking away.</summary>
    ToiletVisit = 1 << 7,
    /// <summary>In a confrontation, either as a guest or as the steward opponent.</summary>
    Fighting = 1 << 8,
    /// <summary>On stage or holding an attached instrument.</summary>
    Performing = 1 << 9,
    /// <summary>The subject of a named staff guide, rest or escort job.</summary>
    InterventionTarget = 1 << 10,
    /// <summary>Physically being guided or escorted by staff right now.</summary>
    BeingEscorted = 1 << 11,
    InterventionWorker = 1 << 12,
    MedicResponding = 1 << 13,
    MedicPatient = 1 << 14,
    StewardResponding = 1 << 15,
    StewardTarget = 1 << 16,
    Maintaining = 1 << 17,
    WasteDisposal = 1 << 18,
    Cleanup = 1 << 19,
    /// <summary>A steward or maintenance worker assigned to a stuck toilet or broken tap.</summary>
    FaultWork = 1 << 20,
}

public static class PersonClaims
{
    /// <summary>Medical care and the water line steer this person's movement.</summary>
    public const PersonClaim MedicalNavigation = PersonClaim.WaterPlace | PersonClaim.SeekingWater | PersonClaim.Resting |
        PersonClaim.AwaitingMedic | PersonClaim.Leaving | PersonClaim.Collapsed;
    /// <summary>A staff member's hands and route belong to a job.</summary>
    public const PersonClaim StaffJob = PersonClaim.InterventionWorker | PersonClaim.MedicResponding |
        PersonClaim.StewardResponding | PersonClaim.Maintaining | PersonClaim.Cleanup | PersonClaim.FaultWork;
    /// <summary>Claims that stop a person carrying, buying or consuming food and drink.</summary>
    public const PersonClaim HandsBusy = PersonClaim.Performing | PersonClaim.Fighting | PersonClaim.InterventionTarget |
        StaffJob | PersonClaim.WasteDisposal;
    /// <summary>Claims that move a listener away from their audience place.</summary>
    public const PersonClaim AwayFromAudience = PersonClaim.Shopping | PersonClaim.ToiletVisit | MedicalNavigation |
        PersonClaim.Fighting | PersonClaim.BeingEscorted | PersonClaim.WasteDisposal | PersonClaim.Cleanup;
    /// <summary>A staff response already owns this person, as worker or subject.</summary>
    public const PersonClaim ResponseAssigned = PersonClaim.InterventionWorker | PersonClaim.InterventionTarget |
        PersonClaim.MedicResponding | PersonClaim.MedicPatient | PersonClaim.StewardResponding | PersonClaim.StewardTarget;
}

public sealed partial class GameSession
{
    /// <summary>Every claim currently held on one person.</summary>
    public PersonClaim CaptureClaims(ulong id)
    {
        var claims = PersonClaim.None;
        foreach (var claim in Enum.GetValues<PersonClaim>())
            if (claim != PersonClaim.None && HasClaim(id, claim)) claims |= claim;
        return claims;
    }

    // Ids named as the opponent of someone fighting, rebuilt whenever any person changes.
    private (long Version, HashSet<ulong>? Ids) _fightOpponents;
    private bool IsFightOpponent(ulong id)
    {
        if (_fightOpponents.Ids is null || _fightOpponents.Version != _persons.Version)
        {
            var ids = new HashSet<ulong>();
            foreach (var person in PeopleIn(PersonView.Disorder))
                if (person.ConductStage == DisorderStage.Fight && person.OpponentId is { } opponent) ids.Add(opponent);
            _fightOpponents = (_persons.Version, ids);
        }
        return _fightOpponents.Ids.Contains(id);
    }

    /// <summary>True when any of the requested claims currently holds; only requested claims are evaluated.</summary>
    private bool HasClaim(ulong id, PersonClaim claims)
    {
        if (claims.HasFlag(PersonClaim.WasteDisposal) && WasteOwnsNavigation(id)) return true;
        if (claims.HasFlag(PersonClaim.Cleanup) && CleanupOwnsNavigation(id)) return true;
        if (claims.HasFlag(PersonClaim.FaultWork) && FaultWorkOwns(id)) return true;
        if (claims.HasFlag(PersonClaim.WaterPlace) && WaterPoints().Any(point => point.Queue.Contains(id))) return true;
        if ((claims & (PersonClaim.SeekingWater | PersonClaim.Resting | PersonClaim.AwaitingMedic | PersonClaim.Leaving |
                PersonClaim.Collapsed)) != 0 && PersonIn(PersonView.Medical, id) is { } need &&
            (claims.HasFlag(PersonClaim.SeekingWater) && need.Intent == MedicalIntent.SeekWater ||
             claims.HasFlag(PersonClaim.Resting) && need.Intent == MedicalIntent.Rest ||
             claims.HasFlag(PersonClaim.AwaitingMedic) && need.Intent == MedicalIntent.AwaitMedic ||
             claims.HasFlag(PersonClaim.Leaving) && need.Intent == MedicalIntent.Leaving ||
             claims.HasFlag(PersonClaim.Collapsed) && need.Intent == MedicalIntent.Collapsed)) return true;
        if (claims.HasFlag(PersonClaim.Shopping) && PersonIn(PersonView.Consumption, id)?.VendorId is not null) return true;
        if (claims.HasFlag(PersonClaim.ToiletVisit) &&
            PersonIn(PersonView.Consumption, id) is { ToiletStage: not ToiletVisitStage.None }) return true;
        if (claims.HasFlag(PersonClaim.Fighting) &&
            (PersonIn(PersonView.Disorder, id)?.ConductStage == DisorderStage.Fight || IsFightOpponent(id))) return true;
        if (claims.HasFlag(PersonClaim.Performing) &&
            LivePerformerStage(id) is var stage and >= 0 && _livePerformances[stage]?.Performers.Any(performer => performer.AgentId == id && (performer.OnStage || performer.InstrumentAttached ||
                // Still on the way off: across the deck and down the band stairs, before anything else can call them away.
                _navigationAgents.TryGetValue(new(id), out var exit) && exit.IntentId is "performance.stage-exit-stair" or "performance.stage-exit-access" &&
                // A blocked way down lets go, so a stranded performer can still be called off by anything else.
                exit.Action is AgentNavigationAction.Travelling or AgentNavigationAction.Arrived)) == true) return true;
        if ((claims & (PersonClaim.InterventionTarget | PersonClaim.BeingEscorted | PersonClaim.InterventionWorker)) != 0 &&
            (_medical?.StaffInterventions ?? []).Any(job =>
                claims.HasFlag(PersonClaim.InterventionTarget) && InterventionBusy(job) && job.GuestId == id ||
                claims.HasFlag(PersonClaim.BeingEscorted) && job.GuestId == id && job.Stage is StaffInterventionStage.Guiding or StaffInterventionStage.Escorting ||
                claims.HasFlag(PersonClaim.InterventionWorker) && InterventionBusy(job) && job.WorkerId == id)) return true;
        if ((claims & (PersonClaim.MedicResponding | PersonClaim.MedicPatient)) != 0 && GetMedicResponses().Any(job => MedicBusy(job) &&
                (claims.HasFlag(PersonClaim.MedicResponding) && job.WorkerId == id || claims.HasFlag(PersonClaim.MedicPatient) && job.PatientId == id))) return true;
        if ((claims & (PersonClaim.StewardResponding | PersonClaim.StewardTarget)) != 0 && GetStewardResponses().Any(job => StewardBusy(job) &&
                (claims.HasFlag(PersonClaim.StewardResponding) && job.WorkerId == id || claims.HasFlag(PersonClaim.StewardTarget) && job.TargetId == id))) return true;
        if (claims.HasFlag(PersonClaim.Maintaining) &&
            _equipment is { WorkerId: { } worker, JobStage: MaintenanceStage.Travelling or MaintenanceStage.Repairing } && worker == id) return true;
        return false;
    }

    private bool MedicalOwnsNavigation(ulong id) => HasClaim(id, PersonClaims.MedicalNavigation);
    private bool ImmersionOwnsNavigation(ulong id) => HasClaim(id, PersonClaim.Shopping);
    private bool ToiletOwnsNavigation(ulong id) => HasClaim(id, PersonClaim.ToiletVisit);
    private bool DisorderOwnsNavigation(ulong id) => HasClaim(id, PersonClaim.Fighting);
    private bool InterventionOwnsWorker(ulong id) => HasClaim(id, PersonClaim.InterventionWorker);
    private bool InterventionOwnsTarget(ulong id) => HasClaim(id, PersonClaim.InterventionTarget);
    private bool AudienceNavigationOwned(ulong id) => HasClaim(id, PersonClaims.AwayFromAudience);

    /// <summary>
    /// Present and not held by a stage, fight, staff job or medical route. While the
    /// site is emptying, a person simply walking out keeps free hands.
    /// </summary>
    public bool ImmersionHandsAvailable(ulong id) => _immersion is not null &&
        PersonIn(PersonView.Roster, id) is { Admitted: true, Departed: false } && !HasClaim(id, PersonClaims.HandsBusy) &&
        !(HasClaim(id, PersonClaims.MedicalNavigation) &&
          !(ImmersionDepartureActive && !ImmersionDepartureJobOwns(id) && _persons[id].Intent == MedicalIntent.Leaving));

    private bool ResponseTargetClaimed(ulong id)
    {
        var opponent = PersonIn(PersonView.Disorder, id) is { ConductStage: DisorderStage.Fight } fighter ? fighter.OpponentId : null;
        return HasClaim(id, PersonClaims.ResponseAssigned) || opponent is { } other && HasClaim(other, PersonClaims.ResponseAssigned);
    }
}

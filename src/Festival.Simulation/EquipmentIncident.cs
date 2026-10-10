using System.Text.Json;

namespace Festival.Simulation;

public enum EquipmentStage { Normal, Warning, DangerousFault, Resolved, Isolated, Terminal }
public enum MaintenanceStage { None, Travelling, Repairing, Completed, Cancelled }
public enum EquipmentAction { Acknowledge, ShedLoad, Isolate, DispatchMaintenance, ToggleBarPower, ToggleFoodPower, ToggleLights }
public sealed record EquipmentCommand(EquipmentAction Action) : SessionCommand;
public sealed record EquipmentEvidence(string Id, long Tick, string Description);
public sealed record EquipmentSnapshot(int Version, int XMillimetres, int ZMillimetres, int LoadPercent,
    int Condition, EquipmentStage Stage, long WarningTick, bool WarningAcknowledged, string Response,
    ulong? WorkerId, MaintenanceStage JobStage, long JobDispatchedTick, long RepairStartedTick,
    EquipmentEvidence[] Evidence)
{
    // Version 3 runs on the power budget (see PowerRules); version 2 is the older scripted overload.
    public int Capacity { get; init; } = PowerRules.FarmDieselCapacity;
    public int Strain { get; init; }
    public bool BarPowered { get; init; } = true;
    public bool FoodPowered { get; init; } = true;
    public bool LightsPowered { get; init; } = true;
    /// <summary>
    /// With the generators pooled, the trailer stage's power is cut while the generator runs on for everything else (on a
    /// single stage the cutoff is the Isolated stage instead).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool StageCut { get; init; }
}

public sealed partial class GameSession
{
    public const int EquipmentWarningDelayTicks = 2_400;
    public const int EquipmentDangerDelayTicks = 3_600;
    public const int EquipmentDeathDelayTicks = 4_800;
    public const int EquipmentRepairTicks = 1_600;
    public const int EquipmentHazardRadiusMillimetres = 4_000;
    public const int EquipmentXMillimetres = -20_250;
    public const int EquipmentZMillimetres = 14_500;
    private EquipmentSnapshot? _equipment;
    public EquipmentSnapshot? CaptureEquipment() => _equipment is null ? null : _equipment with { Evidence = _equipment.Evidence.ToArray() };
    internal string? EquipmentCanonicalJson => _equipment is null ? null : JsonSerializer.Serialize(_equipment);


    /// <summary>Headless economy fixture only: grants a second test Favour to exercise two reset cycles.</summary>

    // Panel access is on the outer side of the relocated unit, away from the
    // trailer deck/stairs and the audience-facing apron.
    private GridCell EquipmentWorkCell => TraversalGrid.WorldToCell(_equipment!.XMillimetres - 2_000, _equipment.ZMillimetres);
    private Person? NearbyEquipmentPerson() => PeopleIn(PersonView.Roster).OrderBy(item => item.Id).FirstOrDefault(person =>
        _navigationAgents.TryGetValue(new(person.Id), out var agent) &&
        (long)(agent.XMillimetres - _equipment!.XMillimetres) * (agent.XMillimetres - _equipment.XMillimetres) +
        (long)(agent.ZMillimetres - _equipment.ZMillimetres) * (agent.ZMillimetres - _equipment.ZMillimetres) <=
        (long)EquipmentHazardRadiusMillimetres * EquipmentHazardRadiusMillimetres);

    public bool EquipmentBoundaryOnNextTick => !IsPaused && (StageGeneratorBoundaryOnNextTick || _equipment is { } e && _preparation is { Status: PreparationStatus.Running } p &&
        (e.Version != 3 && e.Stage == EquipmentStage.Normal && CurrentTick + 1 >= p.StartedTick + EquipmentWarningDelayTicks ||
         e.Version == 3 && PowerTransitionOnNextTick(e) ||
         e.Stage == EquipmentStage.Warning && CurrentTick + 1 >= e.WarningTick + EquipmentDangerDelayTicks ||
         e.Stage == EquipmentStage.DangerousFault && CurrentTick + 1 >= e.WarningTick + EquipmentDeathDelayTicks && NearbyEquipmentPerson() is not null ||
         e.JobStage == MaintenanceStage.Travelling && _navigationAgents[new(e.WorkerId!.Value)].Action == AgentNavigationAction.Arrived ||
         e.JobStage == MaintenanceStage.Repairing && CurrentTick + 1 >= e.RepairStartedTick + EquipmentRepairTicks));

    private CommandResult? ValidateEquipmentCommand(EntityId? target, EquipmentCommand command)
    {
        if (target is not null || _equipment is not { } e || _preparation?.Status is not (PreparationStatus.Preparing or PreparationStatus.Running) || !Enum.IsDefined(command.Action))
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Equipment controls require a preparing or live edition.");
        if (e.Version == 3)
        {
            // The power budget: switches and the stage cutoff while the festival runs; nothing once a death has ended it.
            if (command.Action == EquipmentAction.ShedLoad || command.Action != EquipmentAction.DispatchMaintenance && command.Action != EquipmentAction.Acknowledge &&
                _preparation!.Status != PreparationStatus.Running || e.Stage == EquipmentStage.Terminal)
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Switch the bar, food van or lights off, or cut the stage, while the festival runs.");
            if (command.Action == EquipmentAction.Isolate && (e.Stage == EquipmentStage.Isolated || e.StageCut))
                return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "The stage is already cut off.");
            if (command.Action is EquipmentAction.ToggleBarPower or EquipmentAction.ToggleFoodPower && _immersion is null)
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "There are no stalls on this generator.");
        }
        else if (command.Action is EquipmentAction.ToggleBarPower or EquipmentAction.ToggleFoodPower or EquipmentAction.ToggleLights)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "This generator has no separate switches.");
        else
        if (e.Stage is EquipmentStage.Isolated or EquipmentStage.Resolved or EquipmentStage.Terminal &&
            !(_disorder is not null && e.Stage == EquipmentStage.Resolved && command.Action == EquipmentAction.Isolate))
            return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "The unit is already safe or terminal.");
        if (command.Action == EquipmentAction.Acknowledge && (e.WarningTick < 0 || e.WarningAcknowledged))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "No unseen overload warning.");
        if (command.Action == EquipmentAction.DispatchMaintenance && (e.WorkerId is null || e.JobStage != MaintenanceStage.None || _preparation!.Status != PreparationStatus.Running || !_persons[e.WorkerId.Value].Admitted))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Hire maintenance before opening and wait for their physical arrival. Only one repair job is available.");
        return null;
    }

    // On the power budget warnings can come and go all day, so only the latest are kept, after the opening load line.
    private void EquipmentEvent(string id, string description) => _equipment = _equipment! with
    { Evidence = _equipment.Version == 3
        ? [_equipment.Evidence[0], .. _equipment.Evidence.Skip(1).Append(new EquipmentEvidence(id, CurrentTick, description)).TakeLast(63)]
        : _equipment.Evidence.Append(new EquipmentEvidence(id, CurrentTick, description)).ToArray() };

    private void ApplyEquipmentCommand(EquipmentCommand command)
    {
        var e = _equipment!;
        if (command.Action == EquipmentAction.Acknowledge)
        {
            _equipment = e with { WarningAcknowledged = true };
            EquipmentEvent("equipment:ack", "Overload warning acknowledged; acknowledgement alone does not make equipment safe.");
            return;
        }
        if (command.Action == EquipmentAction.DispatchMaintenance)
        {
            ApplyAgentDestination(new(e.WorkerId!.Value), new(EquipmentWorkCell, "equipment.maintenance"));
            _equipment = e with { JobStage = MaintenanceStage.Travelling, JobDispatchedTick = CurrentTick, Response = "Maintenance dispatched; travel and 20-second repair must finish before escalation" };
            EquipmentEvent("equipment:dispatch", _equipment.Response);
            return;
        }
        if (command.Action is EquipmentAction.ToggleBarPower or EquipmentAction.ToggleFoodPower or EquipmentAction.ToggleLights)
        {
            TogglePowerSwitch(command.Action);
            return;
        }
        if (e.Version == 3)
        {
            // Cutting the stage takes the rig off the generator; the stalls and lights stay on. Pooled, the generator keeps
            // running for the pool, strain and all, so the cut only ever lightens the load.
            _equipment = PowerPooled ? e with { StageCut = true, Response = "Emergency cutoff: trailer stage power cut; the generator runs on for the pool" }
                : e with { Stage = EquipmentStage.Isolated, Response = "Emergency cutoff: stage power isolated" };
            EquipmentEvent($"equipment:isolated:{CurrentTick}", _equipment.Response);
            return;
        }
        if (e.JobStage is MaintenanceStage.Travelling or MaintenanceStage.Repairing)
        {
            var index = Array.FindIndex(PeopleIn(PersonView.Roster), item => item.Id == e.WorkerId);
            ReturnToListening(e.WorkerId!.Value);
        }
        _equipment = e with { Stage = command.Action == EquipmentAction.Isolate ? EquipmentStage.Isolated : EquipmentStage.Resolved,
            LoadPercent = command.Action == EquipmentAction.Isolate ? 0 : 80,
            Response = command.Action == EquipmentAction.Isolate ? "Emergency cutoff: stage power isolated" : "Stage lighting load shed to 80%",
            JobStage = e.JobStage is MaintenanceStage.Travelling or MaintenanceStage.Repairing ? MaintenanceStage.Cancelled : e.JobStage };
        EquipmentEvent("equipment:safe", _equipment.Response + "; maintenance ownership released.");
    }

    private void StartEquipmentLifecycle()
    {
        if (_equipment is null) return;
        var p = _preparation!;
        if (_lifecycle is null)
        {
            _lifecycle = new LifecycleState {
                CurrentTierId = $"tier-{p.Tier}", TierOrdinal = p.Tier, CurrentAttemptId = 1, NextAttemptId = 2,
                NextCasualtyId = 1, NextHearingId = 1, FavourBalance = p.CarriedIn?.FavourBalance ?? 1 };
            _lifecycle.Attempts.Add(new(1, _lifecycle.CurrentTierId, EditionAttemptStatus.Active, null));
        }
        foreach (var person in PeopleIn(PersonView.Roster))
            _lifecycle.ProtectedPeople.TryAdd(person.Name, new(person.Name, person.Role));
    }

    private void AdvanceEquipment()
    {
        if (_equipment is not { } e || _preparation is not { Status: PreparationStatus.Running } p) return;
        if (e.JobStage == MaintenanceStage.Travelling && _navigationAgents[new(e.WorkerId!.Value)].Action == AgentNavigationAction.Arrived)
        {
            _equipment = e = e with { JobStage = MaintenanceStage.Repairing, RepairStartedTick = CurrentTick };
            EquipmentEvent("equipment:repair-start", "Morgan arrived at the breaker panel; 20 real seconds of repair work required at 1×.");
            e = _equipment;
        }
        if (e.JobStage == MaintenanceStage.Repairing && CurrentTick >= e.RepairStartedTick + EquipmentRepairTicks)
        {
            _equipment = e with { JobStage = MaintenanceStage.Completed, Stage = e.Version == 3 && e.Stage == EquipmentStage.Isolated ? EquipmentStage.Isolated : EquipmentStage.Resolved,
                LoadPercent = e.Version == 3 ? e.LoadPercent : 80, Condition = 9_500, Strain = 0,
                Response = e.Version == 3 ? "Physical repair completed; the generator is sound again" : "Physical repair completed and load balanced to 80%" };
            // The pool's strain is one strain: a repair settles every generator in it, so they go on warning together.
            if (PowerPooled)
                for (var stage = 1; stage < Stages.Count; stage++)
                    if (StageGenerator(stage) is { } g)
                        SetStageGenerator(stage, g with { Strain = 0, Stage = g.Stage is EquipmentStage.Warning or EquipmentStage.DangerousFault ? EquipmentStage.Resolved : g.Stage,
                            Response = g.Stage is EquipmentStage.Warning or EquipmentStage.DangerousFault ? "Settled with the farm generator's repair" : g.Response });
            EquipmentEvent("equipment:repair-complete", _equipment.Response);
            return;
        }
        if (e.Version == 3) { AdvancePowerBudget(e); return; }
        if (e.Stage == EquipmentStage.Normal && CurrentTick >= p.StartedTick + EquipmentWarningDelayTicks)
        {
            _equipment = e with { Stage = EquipmentStage.Warning, WarningTick = CurrentTick, Condition = 7_000 };
            EquipmentEvent("equipment:warning", "GENERATOR OVERLOAD: 120% load, condition 70%. Shed load, isolate or complete maintenance. Dangerous fault in 45 seconds; lethal eligibility no sooner than 60 seconds at 1×.");
        }
        else if (e.Stage == EquipmentStage.Warning && CurrentTick >= e.WarningTick + EquipmentDangerDelayTicks)
        {
            _equipment = e with { Stage = EquipmentStage.DangerousFault, Condition = 3_000 };
            EquipmentEvent("equipment:fault", "Dangerous generator fault: 120% load, condition 30%. Cutoff or shedding still prevents death. Unfinished maintenance is not protection.");
        }
        else if (e.Stage == EquipmentStage.DangerousFault && CurrentTick >= e.WarningTick + EquipmentDeathDelayTicks && NearbyEquipmentPerson() is { } victim)
            EquipmentDeath(e, victim);
    }

    /// <summary>A dangerous fault kills someone standing by the unit: the day fails and the Council hearing opens.</summary>
    private void EquipmentDeath(EquipmentSnapshot e, Person victim)
    {
        var p = _preparation!;
        {
            var agent = _navigationAgents[new(victim.Id)];
            var cause = $"Generator overload killed {victim.Name} ({victim.Role}) at ({agent.XMillimetres},{agent.ZMillimetres}) near unit ({e.XMillimetres},{e.ZMillimetres}); load {e.LoadPercent}%; condition {e.Condition / 100}%; warning tick {e.WarningTick}, acknowledged {e.WarningAcknowledged}; response: {e.Response}; job {e.JobStage}.";
            _equipment = e with { Stage = EquipmentStage.Terminal };
            EquipmentEvent("equipment:death", cause);
            var lifecycle = _lifecycle!;
            var attempt = CurrentAttempt();
            var transaction = $"equipment-death:{CampaignId.Value}:{attempt.AttemptId}";
            lifecycle.Casualties.Add(new(lifecycle.NextCasualtyId++, attempt.AttemptId, victim.Name, victim.Role, cause, CurrentTick, transaction));
            lifecycle.CompletedOutcomeTransactionIds.Add(transaction);
            ReplaceAttempt(attempt with { Status = EditionAttemptStatus.Failed, OutcomeTransactionId = transaction });
            var hearing = $"equipment-hearing:{CampaignId.Value}:{attempt.AttemptId}";
            lifecycle.Hearings.Add(new(lifecycle.NextHearingId++, attempt.AttemptId, HearingStatus.Open, hearing, null));
            lifecycle.CompletedOutcomeTransactionIds.Add(hearing);
            ResolveNoFavourHearing();
        _preparation = p with { Status = PreparationStatus.Failed, Rentals = [], WorkContracts = [] };
        ReleaseInterventionsForBoundary("First death froze the edition; intervention released");
            FinishLivePerformance();
        }
    }

    private static string? ValidatePersistedEquipment(EquipmentSnapshot? e, SessionPersistenceSnapshot s)
    {
        if (e is null) return null;
        var budget = e.Version == 3;
        if (s.Preparation is not { } p || e.Version is not (2 or 3) || e.XMillimetres != EquipmentXMillimetres || e.ZMillimetres != EquipmentZMillimetres ||
            !Enum.IsDefined(e.Stage) || !Enum.IsDefined(e.JobStage) || (budget ? e.LoadPercent is < 0 or > 300 : e.LoadPercent is not (0 or 80 or 120)) ||
            e.Condition is not (3_000 or 7_000 or 8_000 or 9_500) ||
            budget && (e.Capacity is not (PowerRules.FarmDieselCapacity or PowerRules.HiredGeneratorCapacity) || e.Strain is < 0 or > PowerRules.StrainMaximum ||
                e.Stage == EquipmentStage.Normal) ||
            e.Evidence is null || e.Evidence.Length < 1 || e.Evidence.Length > (budget ? 64 : 8) || e.Evidence.Any(item => item is null || item.Tick < 0 || item.Tick > s.CurrentTick || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Description)) ||
            e.Evidence.Select(item => item.Id).Distinct().Count() != e.Evidence.Length || !e.Evidence.Select(item => item.Tick).SequenceEqual(e.Evidence.Select(item => item.Tick).Order()) || string.IsNullOrWhiteSpace(e.Response) ||
            // A cut that leaves the generator running only exists with a pooled supply, once the festival has opened.
            e.StageCut && (!budget || s.StageGenerators is not { Length: > 0 } || p.Status == PreparationStatus.Preparing || e.Stage == EquipmentStage.Isolated) ||
            // Pooled, the trailer's cut is StageCut: an isolated farm generator would keep its capacity yet never warn or fault.
            budget && s.StageGenerators is { Length: > 0 } && e.Stage == EquipmentStage.Isolated)
            return "Equipment identity, stage or evidence invalid.";
        if ((e.WorkerId is not null) != p.AcceptedOffers.Contains("maintenance.worker") ||
            e.WorkerId is { } worker && !p.People.Any(item => item.AgentId == worker && item.Name == "Morgan Finch" && item.Role == ProtectedPersonRole.Staff) ||
            e.JobStage != MaintenanceStage.None && (e.WorkerId is null || e.JobDispatchedTick < p.StartedTick || e.JobDispatchedTick > s.CurrentTick) ||
            e.JobStage is MaintenanceStage.Repairing or MaintenanceStage.Completed && (e.RepairStartedTick < e.JobDispatchedTick || e.RepairStartedTick > s.CurrentTick) ||
            e.WarningTick < -1 || e.WarningTick > s.CurrentTick || e.WarningAcknowledged && e.WarningTick < 0 ||
            !budget && e.Stage is EquipmentStage.Warning or EquipmentStage.DangerousFault or EquipmentStage.Terminal && e.WarningTick != p.StartedTick + EquipmentWarningDelayTicks ||
            budget && e.Stage is EquipmentStage.Warning or EquipmentStage.DangerousFault or EquipmentStage.Terminal && e.WarningTick < p.StartedTick ||
            (e.Stage == EquipmentStage.Terminal) != (p.Status == PreparationStatus.Failed && s.Medical?.Fatal != true && s.Disorder?.Evidence.LastOrDefault()?.Id != "disorder:death") ||
            p.Status != PreparationStatus.Preparing && s.Lifecycle is null)
            return "Equipment warning, response ownership or lifecycle invalid.";
        if (budget ? e.Evidence[0].Id != "equipment:load" || e.Evidence[0].Tick != 0 ||
                e.Stage is EquipmentStage.DangerousFault or EquipmentStage.Terminal && s.CurrentTick < e.WarningTick + EquipmentDangerDelayTicks ||
                e.Stage == EquipmentStage.Terminal && s.CurrentTick < e.WarningTick + EquipmentDeathDelayTicks ||
                e.JobStage == MaintenanceStage.Completed && (e.Condition != 9_500 && e.Stage is not (EquipmentStage.Warning or EquipmentStage.DangerousFault or EquipmentStage.Terminal) ||
                    s.CurrentTick < e.RepairStartedTick + EquipmentRepairTicks)
            : e.Evidence[0].Id != "equipment:load" || e.Evidence[0].Tick != 0 ||
            (e.WarningTick >= 0) != e.Evidence.Any(item => item.Id == "equipment:warning" && item.Tick == e.WarningTick) ||
            e.WarningAcknowledged != e.Evidence.Any(item => item.Id == "equipment:ack") ||
            e.Stage is EquipmentStage.Normal or EquipmentStage.Warning or EquipmentStage.DangerousFault or EquipmentStage.Terminal && e.LoadPercent != 120 ||
            e.Stage == EquipmentStage.Isolated && e.LoadPercent != 0 || e.Stage == EquipmentStage.Resolved && e.LoadPercent != 80 ||
            e.JobStage == MaintenanceStage.Completed && (e.Stage != EquipmentStage.Resolved || e.Condition != 9_500 || s.CurrentTick < e.RepairStartedTick + EquipmentRepairTicks) ||
            e.Stage is EquipmentStage.DangerousFault or EquipmentStage.Terminal && s.CurrentTick < e.WarningTick + EquipmentDangerDelayTicks ||
            e.Stage == EquipmentStage.Terminal && s.CurrentTick < e.WarningTick + EquipmentDeathDelayTicks)
            return "Equipment causal stages do not reconcile.";
        if (p.Status == PreparationStatus.Preparing && p.Attempt == 1 && s.Lifecycle is not null ||
            // Hired staff name their slots only at opening, so a retry's unfilled slots are the only names not yet protected.
            s.Lifecycle is { } lifecycle && (p.People.Any(person => !StaffCatalogue.IsVacancy(person.Name) && !lifecycle.ProtectedPeople.Any(item => item.PersonId == person.Name && item.Role == (int)person.Role)) ||
                (lifecycle.Casualties.LastOrDefault()?.AttemptId == (ulong)p.Attempt) !=
                    (e.Stage == EquipmentStage.Terminal || s.Medical?.Fatal == true || s.Disorder?.Evidence.LastOrDefault()?.Id == "disorder:death")))
            return "Equipment lifecycle must protect the exact physical roster.";
        return null;
    }
}

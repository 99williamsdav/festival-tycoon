using System.Text.Json;

namespace Festival.Simulation;

public enum EquipmentStage { Normal, Warning, DangerousFault, Resolved, Isolated, Terminal }
public enum MaintenanceStage { None, Travelling, Repairing, Completed, Cancelled }
public enum EquipmentAction { Acknowledge, ShedLoad, Isolate, DispatchMaintenance }
public sealed record EquipmentCommand(EquipmentAction Action) : SessionCommand;
public sealed record EquipmentEvidence(string Id, long Tick, string Description);
public sealed record EquipmentSnapshot(int Version, int XMillimetres, int ZMillimetres, int LoadPercent,
    int Condition, EquipmentStage Stage, long WarningTick, bool WarningAcknowledged, string Response,
    ulong? WorkerId, MaintenanceStage JobStage, long JobDispatchedTick, long RepairStartedTick,
    EquipmentEvidence[] Evidence);

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

    public static GameSession CreateEquipmentCampaign(ulong seed, int tier = 1)
    {
        var session = CreatePreparedCampaign(seed, tier);
        session._equipment = new(2, EquipmentXMillimetres, EquipmentZMillimetres, 120, 8_000, EquipmentStage.Normal, -1, false,
            "No response", null, MaintenanceStage.None, -1, -1,
            [new("equipment:load", 0, "Stage sound and lights demand 120% of safe capacity; condition 80%. Free load shedding and emergency cutoff available; maintenance contract offered before opening.")]);
        return session;
    }

    // Panel access is on the outer side of the relocated unit, away from the
    // trailer deck/stairs and the audience-facing apron.
    private GridCell EquipmentWorkCell => TraversalGrid.WorldToCell(_equipment!.XMillimetres - 2_000, _equipment.ZMillimetres);
    private EditionPerson? NearbyEquipmentPerson() => _preparation!.People.OrderBy(item => item.AgentId).FirstOrDefault(person =>
        _navigationAgents.TryGetValue(new(person.AgentId), out var agent) &&
        (long)(agent.XMillimetres - _equipment!.XMillimetres) * (agent.XMillimetres - _equipment.XMillimetres) +
        (long)(agent.ZMillimetres - _equipment.ZMillimetres) * (agent.ZMillimetres - _equipment.ZMillimetres) <=
        (long)EquipmentHazardRadiusMillimetres * EquipmentHazardRadiusMillimetres);

    public bool EquipmentBoundaryOnNextTick => !IsPaused && _equipment is { } e && _preparation is { Status: PreparationStatus.Running } p &&
        (e.Stage == EquipmentStage.Normal && CurrentTick + 1 >= p.StartedTick + EquipmentWarningDelayTicks ||
         e.Stage == EquipmentStage.Warning && CurrentTick + 1 >= e.WarningTick + EquipmentDangerDelayTicks ||
         e.Stage == EquipmentStage.DangerousFault && CurrentTick + 1 >= e.WarningTick + EquipmentDeathDelayTicks && NearbyEquipmentPerson() is not null ||
         e.JobStage == MaintenanceStage.Travelling && _navigationAgents[new(e.WorkerId!.Value)].Action == AgentNavigationAction.Arrived ||
         e.JobStage == MaintenanceStage.Repairing && CurrentTick + 1 >= e.RepairStartedTick + EquipmentRepairTicks);

    private CommandResult? ValidateEquipmentCommand(EntityId? target, EquipmentCommand command)
    {
        if (target is not null || _equipment is not { } e || _preparation?.Status is not (PreparationStatus.Preparing or PreparationStatus.Running) || !Enum.IsDefined(command.Action))
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Equipment controls require a preparing or live edition.");
        if (e.Stage is EquipmentStage.Isolated or EquipmentStage.Resolved or EquipmentStage.Terminal)
            return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "The unit is already safe or terminal.");
        if (command.Action == EquipmentAction.Acknowledge && (e.WarningTick < 0 || e.WarningAcknowledged))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "No unseen overload warning.");
        if (command.Action == EquipmentAction.DispatchMaintenance && (e.WorkerId is null || e.JobStage != MaintenanceStage.None || _preparation!.Status != PreparationStatus.Running || !_preparation.People.Single(item => item.AgentId == e.WorkerId).Admitted))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Hire maintenance before opening and wait for their physical arrival. Only one repair job is available.");
        return null;
    }

    private void EquipmentEvent(string id, string description) => _equipment = _equipment! with
    { Evidence = _equipment.Evidence.Append(new EquipmentEvidence(id, CurrentTick, description)).ToArray() };

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
        if (e.JobStage is MaintenanceStage.Travelling or MaintenanceStage.Repairing)
        {
            var index = Array.FindIndex(_preparation!.People, item => item.AgentId == e.WorkerId);
            ApplyAgentDestination(new(e.WorkerId!.Value), new(PreparedPlace(index), "equipment.job-cancelled"));
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
        _lifecycle = new LifecycleState { FixtureLabel = "R0.02 equipment lifecycle; hearing only, no Favour economy",
            CurrentTierId = $"tier-{p.Tier}", FixtureTierOrdinal = p.Tier, CurrentAttemptId = 1, NextAttemptId = 2,
            NextCasualtyId = 1, NextHearingId = 1, FixtureFavourBalance = 0 };
        foreach (var person in p.People) _lifecycle.ProtectedPeople.Add(person.Name, new(person.Name, person.Role));
        _lifecycle.Attempts.Add(new(1, _lifecycle.CurrentTierId, EditionAttemptStatus.Active, null));
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
            _equipment = e with { JobStage = MaintenanceStage.Completed, Stage = EquipmentStage.Resolved, LoadPercent = 80, Condition = 9_500,
                Response = "Physical repair completed and load balanced to 80%" };
            EquipmentEvent("equipment:repair-complete", _equipment.Response);
            return;
        }
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
        {
            var agent = _navigationAgents[new(victim.AgentId)];
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
            _preparation = p with { Status = PreparationStatus.Failed, Rentals = [], WorkContracts = [] };
            FinishLivePerformance();
        }
    }

    private static string? ValidatePersistedEquipment(EquipmentSnapshot? e, SessionPersistenceSnapshot s)
    {
        if (e is null) return null;
        if (s.Preparation is not { } p || e.Version != 2 || e.XMillimetres != EquipmentXMillimetres || e.ZMillimetres != EquipmentZMillimetres ||
            !Enum.IsDefined(e.Stage) || !Enum.IsDefined(e.JobStage) || e.LoadPercent is not (0 or 80 or 120) || e.Condition is not (3_000 or 7_000 or 8_000 or 9_500) ||
            e.Evidence is null || e.Evidence.Length is < 1 or > 8 || e.Evidence.Any(item => item is null || item.Tick < 0 || item.Tick > s.CurrentTick || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Description)) ||
            e.Evidence.Select(item => item.Id).Distinct().Count() != e.Evidence.Length || !e.Evidence.Select(item => item.Tick).SequenceEqual(e.Evidence.Select(item => item.Tick).Order()) || string.IsNullOrWhiteSpace(e.Response))
            return "Equipment identity, stage or evidence invalid.";
        if ((e.WorkerId is not null) != p.AcceptedOffers.Contains("maintenance.worker") ||
            e.WorkerId is { } worker && !p.People.Any(item => item.AgentId == worker && item.Name == "Morgan Finch" && item.Role == ProtectedPersonRole.Staff) ||
            e.JobStage != MaintenanceStage.None && (e.WorkerId is null || e.JobDispatchedTick < p.StartedTick || e.JobDispatchedTick > s.CurrentTick) ||
            e.JobStage is MaintenanceStage.Repairing or MaintenanceStage.Completed && (e.RepairStartedTick < e.JobDispatchedTick || e.RepairStartedTick > s.CurrentTick) ||
            e.WarningTick < -1 || e.WarningTick > s.CurrentTick || e.WarningAcknowledged && e.WarningTick < 0 ||
            e.Stage is EquipmentStage.Warning or EquipmentStage.DangerousFault or EquipmentStage.Terminal && e.WarningTick != p.StartedTick + EquipmentWarningDelayTicks ||
            (e.Stage == EquipmentStage.Terminal) != (p.Status == PreparationStatus.Failed && s.Medical?.Stage != MedicalStage.Terminal) ||
            p.Status != PreparationStatus.Preparing && s.Lifecycle is null)
            return "Equipment warning, response ownership or lifecycle invalid.";
        if (e.Evidence[0].Id != "equipment:load" || e.Evidence[0].Tick != 0 ||
            (e.WarningTick >= 0) != e.Evidence.Any(item => item.Id == "equipment:warning" && item.Tick == e.WarningTick) ||
            e.WarningAcknowledged != e.Evidence.Any(item => item.Id == "equipment:ack") ||
            e.Stage is EquipmentStage.Normal or EquipmentStage.Warning or EquipmentStage.DangerousFault or EquipmentStage.Terminal && e.LoadPercent != 120 ||
            e.Stage == EquipmentStage.Isolated && e.LoadPercent != 0 || e.Stage == EquipmentStage.Resolved && e.LoadPercent != 80 ||
            e.JobStage == MaintenanceStage.Completed && (e.Stage != EquipmentStage.Resolved || e.Condition != 9_500 || s.CurrentTick < e.RepairStartedTick + EquipmentRepairTicks) ||
            e.Stage is EquipmentStage.DangerousFault or EquipmentStage.Terminal && s.CurrentTick < e.WarningTick + EquipmentDangerDelayTicks ||
            e.Stage == EquipmentStage.Terminal && s.CurrentTick < e.WarningTick + EquipmentDeathDelayTicks)
            return "Equipment causal stages do not reconcile.";
        if (p.Status == PreparationStatus.Preparing && s.Lifecycle is not null ||
            s.Lifecycle is { } lifecycle && (lifecycle.FixtureLabel != "R0.02 equipment lifecycle; hearing only, no Favour economy" ||
                !lifecycle.ProtectedPeople.Select(item => (item.PersonId, item.Role)).SequenceEqual(p.People.OrderBy(item => item.Name, StringComparer.Ordinal).Select(item => (item.Name, (int)item.Role))) ||
                lifecycle.Casualties.Length != (e.Stage == EquipmentStage.Terminal || s.Medical?.Stage == MedicalStage.Terminal ? 1 : 0)))
            return "Equipment lifecycle must protect the exact physical roster.";
        return null;
    }
}

using System.Text.Json;

namespace Festival.Simulation;

public enum DisorderGrievance { None, MusicCutoff, WaterWait, BandDelayed }
public enum DisorderStage { Calm, Complaint, Agitated, Argument, Fight, Injured, Resolved }
public enum SecurityResponseStage { None, Travelling, Calming, Confronting, Completed, Failed }
public enum DisorderAction { DispatchSecurity, SafeEgress, CloseWater, ReopenWater, RestoreMusic }
public sealed record DisorderCommand(DisorderAction Action, ulong? PersonId = null, ulong? WorkerId = null) : SessionCommand;
public sealed record StewardResponse(ulong WorkerId, SecurityResponseStage Stage, ulong? TargetId, long StartedTick, bool Incapacitated, string Description, long DispatchedTick = -1);
public sealed record DisorderPerson(ulong AgentId, int Temperament, int QueueToleranceTicks, int Pressure,
    DisorderGrievance Grievance, DisorderStage Stage, long GrievanceTick, long StageTick,
    long QueueJoinedTick, long InjuryTick, long CooldownUntilTick, ulong? OpponentId);
public sealed record DisorderEvidence(string Id, long Tick, ulong PersonId, ulong? OtherId,
    int XMillimetres, int ZMillimetres, int Pressure, string Description);
public enum FightHandlingOutcome { Handling, Succeeded, Failed, Interrupted }
public sealed record FightHandlingAttempt(ulong WorkerId, long StartedTick, long EndedTick, FightHandlingOutcome Outcome);
public sealed record DisorderIncidentOrigin(ulong InitiatorId, ulong OpponentId, DisorderGrievance Grievance,
    int Pressure, long ArgumentTick, long FightTick, long InjuryTick, ulong? VictimId)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public FightHandlingAttempt? HandlingAttempt { get; init; }
}
/// <summary>Stewards[0] is the baseline steward; any hired steward follows.</summary>
public sealed record DisorderSnapshot(int Version, StewardResponse[] Stewards, int CalmingSkill, int ConfrontationSkill,
    bool WaterClosed, DisorderPerson[] People, DisorderEvidence[] Evidence)
{
    public DisorderIncidentOrigin[] Incidents { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public ulong SecurityId => Stewards[0].WorkerId;
    [System.Text.Json.Serialization.JsonIgnore] public bool SecurityIncapacitated => Stewards[0].Incapacitated;
}

public sealed partial class GameSession
{
    // Fixed-scale pressure; these are prototype calibration values, not elapsed-stage clocks.
    public const int DisorderComplaintPressure = 2_000;
    public const int DisorderAgitatedPressure = 3_000;
    public const int DisorderArgumentPressure = 4_000;
    public const int DisorderFightEligiblePressure = 8_000;
    public const int DisorderCalmingTicks = 240;
    public const int DisorderConfrontationTicks = 160;
    public const int DisorderFightDurationTicks = 800;   // 10 seconds of visible confrontation at 1×.
    public const int DisorderInjuryDeathTicks = 2_400;
    // Rotated open-sided visual post faces east toward the path. Its walkable
    // duty position sits in front; neither post nor approach closes the gate.
    public static readonly GridCell DisorderSecurityPostCell = new(114, 178); // (-6.75, 25.25) m.
    public static readonly GridCell DisorderSecurityBaseCell = new(119, 178); // (-4.25, 25.25) m; front of post facing the path.

    /// <summary>The current read model. Snapshots are shared immutable values: never write into their arrays.</summary>
    public DisorderSnapshot? CaptureDisorder() => DisorderView;
    internal string? DisorderCanonicalJson => DisorderView is not { } d ? null : System.Text.Json.JsonSerializer.Serialize(d);


    private void DisorderEvent(string id, ulong personId, ulong? otherId, int pressure, string description)
    {
        var nav = _navigationAgents.GetValueOrDefault(new(personId));
        var item = new DisorderEvidence(id, CurrentTick, personId, otherId,
            nav?.XMillimetres ?? 0, nav?.ZMillimetres ?? 0, pressure, description);
        _disorder = _disorder! with { Evidence = _disorder.Evidence.Append(item).TakeLast(96).ToArray() };
    }

    private CommandResult? ValidateDisorderCommand(EntityId? target, DisorderCommand command)
    {
        if (command.Action == DisorderAction.DispatchSecurity && command.WorkerId is null && command.PersonId is { } roleTarget)
            return target is not null ? CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Steward dispatch has no envelope target.") :
                SelectRoleResponse(ResponseRole.Steward, roleTarget, out var roleIssue) is null ? CommandResult.Rejected(CommandReasonCode.InvalidParameter, roleIssue!) : null;
        if (command.Action == DisorderAction.SafeEgress && command.PersonId is { } escortId)
            return ValidateStaffIntervention(target, new(escortId, command.WorkerId ?? _disorder?.SecurityId ?? 0, StaffInterventionAction.EscortOut));
        if (target is not null || _disorder is not { } d || _preparation?.Status != PreparationStatus.Running ||
            !Enum.IsDefined(command.Action))
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Disorder actions require a live edition.");
        var workerId = command.WorkerId ?? d.SecurityId;
        if (command.Action == DisorderAction.DispatchSecurity && (InterventionOwnsWorker(workerId) || InterventionOwnsTarget(workerId) || command.PersonId is { } targetId && (InterventionOwnsTarget(targetId) || InterventionOwnsWorker(targetId))))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "A physical staff intervention already owns this worker or target.");
        var job = GetStewardResponses().SingleOrDefault(item => item.WorkerId == workerId);
        if (command.WorkerId is not null && (command.Action != DisorderAction.DispatchSecurity || job is null))
            return CommandResult.Rejected(CommandReasonCode.UnknownTarget, "Choose a contracted steward for dispatch.");
        if (command.Action is DisorderAction.DispatchSecurity or DisorderAction.SafeEgress)
        {
            if (command.PersonId is not { } id || !InView(PersonView.Disorder, id))
                return CommandResult.Rejected(CommandReasonCode.UnknownTarget, "Select an affected guest.");
            var person = _persons[id];
            if (HasClaim(id, PersonClaim.MedicPatient) ||
                _persons[id].HealthStage is MedicalStage.Collapsed or MedicalStage.Critical)
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "This person is owned by medical response or needs first aid, not steward reassignment.");
            if (command.Action == DisorderAction.DispatchSecurity)
            {
                if (person.ConductStage == DisorderStage.Fight && GuestFightOrigin(id) is { } fight && !GuestFightParticipantsAvailable(fight))
                    return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "A fight participant is medically or physically unavailable; no steward reassignment.");
                if ((StaffUnavailableReason(workerId) is not null || ResponseTargetClaimed(id)))
                    return CommandResult.Rejected(CommandReasonCode.InvalidParameter, StaffUnavailableReason(workerId) ?? "This person or reciprocal fight is already assigned; finish that response first.");
                if (person.ConductStage == DisorderStage.Fight && GuestFightOrigin(id)?.HandlingAttempt is not null)
                    return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "This fight has already had its one physical handling attempt; no retry or reroll.");
                if (!_persons[id].Admitted || _persons[id].Departed)
                    return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "The affected person is not physically on site.");
                if (person.ConductStage is not (DisorderStage.Complaint or DisorderStage.Agitated or DisorderStage.Argument or DisorderStage.Fight))
                    return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "No active disturbance to attend.");
                if (job!.Incapacitated || !_persons[workerId].Admitted || _persons[workerId].Departed)
                    return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Security is not physically available.");
                if (StewardBusy(job) || HasClaim(id, PersonClaim.StewardTarget))
                    return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Security is already responding; this request is backlogged.");
                if (StewardResponseCell(workerId, id) is null)
                    return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Security cannot reach this person.");
            }
            else if (person.ConductStage is DisorderStage.Injured or DisorderStage.Fight ||
                     !MedicalRouteExists(id, MedicalExitCell))
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Safe egress requires an ambulatory person and a walkable exit.");
        }
        else if (command.PersonId is not null)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "This area action has no person target.");
        if (command.Action == DisorderAction.CloseWater && d.WaterClosed ||
            command.Action == DisorderAction.ReopenWater && !d.WaterClosed)
            return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "Water closure state is unchanged.");
        if (command.Action == DisorderAction.RestoreMusic &&
            (_equipment is not { Stage: EquipmentStage.Isolated, LoadPercent: 0 } e || e.Condition < 7_000 ||
             _livePerformance?.Stage != LiveSetStage.Interrupted))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter,
                "Safe reset needs an isolated, non-faulted generator and interrupted set; unresolved overload stays off.");
        return null;
    }

    private void ApplyDisorderCommand(DisorderCommand command)
    {
        if (command.Action == DisorderAction.DispatchSecurity && command.WorkerId is null)
            command = (DisorderCommand)SelectRoleResponse(ResponseRole.Steward, command.PersonId!.Value, out _)!;
        if (command.Action == DisorderAction.SafeEgress)
        { ApplyStaffIntervention(new(command.PersonId!.Value, command.WorkerId ?? _disorder!.SecurityId, StaffInterventionAction.EscortOut)); return; }
        var d = _disorder!;
        if (command.Action == DisorderAction.DispatchSecurity)
        {
            var id = command.PersonId!.Value;
            var workerId = command.WorkerId ?? d.SecurityId;
            RecallWorker(workerId, "Steward recalled from a personal errand for an assigned response");
            MutatePerson(workerId, item => { item.Intent = MedicalIntent.WatchShow; item.Reason = "Steward responding to assigned target"; item.WaterQueueSlot = null; });
            ApplyAgentDestination(new(workerId), new(StewardResponseCell(workerId, id)!.Value, "disorder.security-dispatch"));
            var description = $"Steward {workerId} walking to person {id}; not yet calming";
            SetStewardResponse(new(workerId, SecurityResponseStage.Travelling, id, CurrentTick, false, description, CurrentTick));
            DisorderEvent("security:dispatch", id, workerId, _persons[id].Pressure, description);
            return;
        }
        if (command.Action == DisorderAction.SafeEgress)
        {
            var id = command.PersonId!.Value;
            LeaveWater(id, "Safe egress chosen", reroute: false);
            MutatePerson(id, item => { item.Intent = MedicalIntent.Leaving; item.Reason = "Safe exit via the gate"; });
            ApplyAgentDestination(new(id), new(MedicalExitCell, "disorder.safe-egress"));
            MutatePerson(id, item => { item.ConductStage = DisorderStage.Resolved; item.Pressure = 0; item.Grievance = DisorderGrievance.None; item.ConductStageTick = CurrentTick; item.CooldownUntilTick = long.MaxValue; });
            DisorderEvent("disorder:egress", id, null, 0, "Person walking to the safe gate; egress completes only on arrival.");
            return;
        }
        if (command.Action == DisorderAction.CloseWater)
        {
            _disorder = d with { WaterClosed = true };
            foreach (var id in WaterPoints().SelectMany(point => point.Queue.Concat(point.Overflow))
                         .Concat(PeopleIn(PersonView.Medical).Where(item => item.Intent == MedicalIntent.SeekWater).Select(item => item.Id))
                         .Distinct().ToArray())
            {
                LeaveWater(id, "Water service closed safely", reroute: false);
                var need = _persons[id];
                if (need.NeedProfile == MedicalNeedProfile.Performer || need.NeedProfile == MedicalNeedProfile.Guest && need.HeatExposure >= MedicalDistressHeat)
                {
                    MutatePerson(id, item => { item.Intent = MedicalIntent.Rest; item.Reason = "Closed-water relief at first-aid rest"; });
                    ApplyAgentDestination(new(id), new(MedicalRestCell, "disorder.water-closure-rest"));
                }
                else ReturnToListening(id);
            }
            DisorderEvent("disorder:water-closed", PeopleIn(PersonView.Disorder)[0].Id, null, 0,
                "Water service closed; physical queue released, rest and gate remain reachable.");
            return;
        }
        if (command.Action == DisorderAction.ReopenWater)
        {
            _disorder = d with { WaterClosed = false };
            DisorderEvent("disorder:water-opened", PeopleIn(PersonView.Disorder)[0].Id, null, 0, "Free water reopened.");
            return;
        }
        // Isolation already removed the dangerous load. A reset is permitted only
        // above the safe condition floor; it restores the existing 80% baseline.
        _equipment = _equipment! with { Stage = EquipmentStage.Resolved, LoadPercent = 80,
            Response = "Explicit safe reset at 80% after isolation" };
        EquipmentEvent("equipment:safe-reset", _equipment.Response);
        DisorderEvent("disorder:music-safe-reset", PeopleIn(PersonView.Disorder)[0].Id, null, 0,
            "Stage power safely restored at 80%; music resumes on the next set update.");
    }

    public bool DisorderBoundaryOnNextTick => !IsPaused && _disorder is { } d &&
        _preparation is { Status: PreparationStatus.Running } && PeopleIn(PersonView.Disorder).Any(item =>
            item.ConductStage == DisorderStage.Injured && CurrentTick + 1 >= item.InjuryTick + DisorderInjuryDeathTicks &&
            _persons[item.Id].HealthStage != MedicalStage.Treated) ||
        !IsPaused && _preparation is { Status: PreparationStatus.Running } && GetStewardResponses().Any(response => response.Incapacitated &&
        _persons[response.WorkerId] is { HealthStage: MedicalStage.Collapsed } securityNeed &&
        CurrentTick + 1 >= securityNeed.HealthCollapseTick + DisorderInjuryDeathTicks);

    private void AdvanceDisorder()
    {
        if (_disorder is not { } d || _preparation is not { Status: PreparationStatus.Running } p) return;
        // Injury deadlines are checked every authoritative tick; ordinary social
        // assessment is batched at a deterministic tenth of a real second.
        foreach (var injured in PeopleIn(PersonView.Disorder).Where(item => item.ConductStage == DisorderStage.Injured).ToArray())
        {
            if (_persons[injured.Id].HealthStage == MedicalStage.Treated)
            {
                MutatePerson(injured.Id, item => { item.ConductStage = DisorderStage.Resolved; item.Pressure = 0; item.ConductStageTick = CurrentTick; item.CooldownUntilTick = CurrentTick + 800; });
                DisorderEvent("disorder:treated", injured.Id, injured.OpponentId, 0,
                    "Physical first aid completed after confrontation injury.");
            }
            else if (CurrentTick >= injured.InjuryTick + DisorderInjuryDeathTicks)
            {
                ApplyDisorderDeath(injured.Id);
                return;
            }
        }
        d = _disorder!;
        foreach (var response in GetStewardResponses().Where(item => item.Incapacitated))
        {
            var securityNeed = _persons[response.WorkerId];
            if (securityNeed.HealthStage == MedicalStage.Treated)
            {
                SetStewardResponse(response with { Incapacitated = false, Stage = SecurityResponseStage.Completed, TargetId = null,
                    Description = "Steward treated after confrontation" });
                DisorderEvent("security:treated", response.WorkerId, response.TargetId, 0, "Steward treated after confrontation");
            }
            else if (CurrentTick >= securityNeed.HealthCollapseTick + DisorderInjuryDeathTicks)
            {
                ApplyDisorderDeath(response.WorkerId);
                return;
            }
        }
        if (CurrentTick % 8 != 0) return;
        d = _disorder!;
        // Each person is judged against their state at the start of this pass; fights begun
        // earlier in the pass do not re-classify later people until the next pass.
        foreach (var person in PeopleIn(PersonView.Disorder).Select(item => item with { }).ToArray())
        {
            if (_persons[person.Id].ConductStage == DisorderStage.Fight) continue;
            if (person.ConductStage == DisorderStage.Injured || person.ConductStage == DisorderStage.Fight) continue;
            var protectedPerson = _persons[person.Id];
            if (!protectedPerson.Admitted || protectedPerson.Departed) continue;
            if (_persons[person.Id].Intent == MedicalIntent.Leaving)
            {
                if (InterventionOwnsTarget(person.Id)) continue;
                var nav = _navigationAgents[new(person.Id)];
                if (nav.Action == AgentNavigationAction.Arrived && nav.Destination == MedicalExitCell)
                {
                    ReleaseWasteAtExit(person.Id);
                    MutatePerson(person.Id, item => item.Departed = true);
                    DisorderEvent("disorder:egress-complete", person.Id, null, 0,
                        "Person reached the safe gate; no teleport or casualty.");
                }
                continue;
            }
            var inWaterLine = !d.WaterClosed && WaterPoints().Any(point =>
                point.Queue.Contains(person.Id) || point.Overflow.Contains(person.Id));
            var joined = inWaterLine ? person.QueueJoinedTick < 0 ? CurrentTick : person.QueueJoinedTick : -1;
            var need = _persons[person.Id];
            var listener = _livePerformance?.Listeners.SingleOrDefault(item => item.AgentId == person.Id);
            var lateAct = LateReadyFestivalAct;
            var lateEnthusiasm = lateAct is null ? 0 : FestivalAffinity(person.Id, lateAct);
            var waitingForBand = BandDelayRemarkEligible && listener is { AtPlace: true } && lateEnthusiasm >= 35 &&
                need.Intent == MedicalIntent.WatchShow && need.HealthStage is MedicalStage.Clear or MedicalStage.Treated &&
                need.Thirst < MedicalDistressThirst && need.HeatExposure < MedicalDistressHeat &&
                !InterventionOwnsTarget(person.Id) && !InterventionOwnsWorker(person.Id);
            var grievance = _livePerformance?.Stage == LiveSetStage.Interrupted && !ScheduledSilence &&
                (CurrentTick >= _livePerformance.PlannedTick && CurrentTick < _programme!.SlotEndTick &&
                    _equipment?.Stage is EquipmentStage.Isolated or EquipmentStage.Terminal) &&
                listener is { AtPlace: true, Enthusiasm: >= 65 }
                ? DisorderGrievance.MusicCutoff
                : inWaterLine && need.Thirst >= 6_000 && CurrentTick - joined >= person.QueueToleranceTicks
                    ? DisorderGrievance.WaterWait : waitingForBand ? DisorderGrievance.BandDelayed : DisorderGrievance.None;
            var pressure = person.Pressure;
            if (grievance != DisorderGrievance.None && CurrentTick >= person.CooldownUntilTick)
            {
                var rate = grievance == DisorderGrievance.BandDelayed
                    ? CurrentTick - LateReadyScheduledTick < 800
                        ? Math.Clamp(1 + lateEnthusiasm / 50 + person.Temperament / 4_000, 1, 3)
                        : Math.Clamp(2 + lateEnthusiasm / 50 + person.Temperament / 2_500, 2, 5)
                    : grievance == DisorderGrievance.MusicCutoff
                    ? 2 + (listener?.Enthusiasm ?? 0) / 100 + person.Temperament / 2_500
                    : 2 + need.Thirst / 3_500 + person.Temperament / 2_500;
                // A sustained strongest grievance reaches fight eligibility no
                // earlier than 1,600 ticks (20 real seconds), without a timer gate.
                rate = Math.Min(5, rate);
                pressure = Math.Min(10_000, pressure + (rate + ImmersionAggression(person.Id,person.Temperament)) * 8);
            }
            else pressure = Math.Max(0, pressure - 48);
            var stage = pressure >= DisorderArgumentPressure ? DisorderStage.Argument :
                pressure >= DisorderAgitatedPressure ? DisorderStage.Agitated :
                pressure >= DisorderComplaintPressure ? DisorderStage.Complaint : DisorderStage.Calm;
            if (person.ConductStage == DisorderStage.Resolved && CurrentTick < person.CooldownUntilTick) stage = DisorderStage.Resolved;
            if (grievance == DisorderGrievance.None && pressure == 0) stage = DisorderStage.Calm;
            var changed = person with { Pressure = pressure, Grievance = grievance,
                GrievanceTick = grievance == DisorderGrievance.None ? -1 :
                    grievance != person.Grievance ? CurrentTick : person.GrievanceTick,
                QueueJoinedTick = joined, ConductStage = stage,
                ConductStageTick = stage == person.ConductStage ? person.ConductStageTick : CurrentTick };
            SetConduct(changed);
            d = _disorder!;
            if (grievance == DisorderGrievance.BandDelayed && person.Grievance != DisorderGrievance.BandDelayed)
                DisorderEvent($"disorder:band-late:{person.Id}:{CurrentTick}", person.Id, null, pressure,
                    $"Where is {lateAct?.Name}? Scheduled start tick {LateReadyScheduledTick}; physical performer readiness still prevents music; pressure {pressure}/10000.");
            if (stage != person.ConductStage)
            {
                var label = stage switch
                {
                    DisorderStage.Complaint => grievance == DisorderGrievance.WaterWait ? "Hurry up!" :
                        grievance == DisorderGrievance.BandDelayed ? "When is the band starting?" : "Why did the music stop?",
                    DisorderStage.Agitated => "Visibly agitated",
                    DisorderStage.Argument => "Argument forming",
                    DisorderStage.Calm => "Pressure subsided",
                    _ => stage.ToString()
                };
                DisorderEvent($"disorder:{stage.ToString().ToLowerInvariant()}:{person.Id}:{CurrentTick}",
                    person.Id, null, pressure, $"{label}; cause {grievance}; pressure {pressure}/10000; temperament hidden.");
            }
            if (stage != DisorderStage.Argument || grievance == DisorderGrievance.None || CurrentTick % 80 != 0) continue;
            if (NextRandom(RandomStreamId.Incidents) % 20 == 0)
            {
                MutatePerson(person.Id, item => { item.ConductStage = DisorderStage.Resolved; item.Pressure = 0; item.ConductStageTick = CurrentTick; item.CooldownUntilTick = CurrentTick + 800; });
                DisorderEvent("disorder:diffused", person.Id, null, pressure,
                    "Argument subsided without a security intervention despite a continuing grievance.");
                continue;
            }
            if (pressure < DisorderFightEligiblePressure) continue;
            var opponent = FindDisorderOpponent(person.Id);
            if (opponent is null || NextRandom(RandomStreamId.Incidents) % 4 != 0) continue;
            BeginDisorderFight(person.Id, opponent.Value, "disorder:fight");
        }
        ReconcileMergedFightClaims();
        foreach (var response in GetStewardResponses()) AdvanceSecurityResponse(response);
        foreach (var fighter in PeopleIn(PersonView.Disorder).Where(item => item.ConductStage == DisorderStage.Fight &&
                     _disorder.Incidents.Any(origin => origin.InitiatorId == item.Id &&
                         origin.FightTick == item.ConductStageTick && origin.InjuryTick == -1) &&
                     CurrentTick >= item.ConductStageTick + DisorderFightDurationTicks).Select(item => item with { }).ToArray())
            ResolveDisorderFight(fighter); // State as collected, before other fights in this batch resolve.
        FinishInterruptedFightAttempts();
    }

    private void BeginDisorderFight(ulong initiatorId, ulong opponentId, string eventId)
    {
        var d = _disorder!;
        var initiator = _persons[initiatorId];
        var origin = new DisorderIncidentOrigin(initiatorId, opponentId, initiator.Grievance,
            initiator.Pressure, initiator.ConductStageTick, CurrentTick, -1, null);
        _disorder = d with { Incidents = d.Incidents.Append(origin).ToArray() };
        MutatePerson(initiatorId, item => { item.ConductStage = DisorderStage.Fight; item.OpponentId = opponentId; item.ConductStageTick = CurrentTick; });
        if (!IsSteward(opponentId))
            MutatePerson(opponentId, item => { item.ConductStage = DisorderStage.Fight; item.OpponentId = initiatorId; item.ConductStageTick = CurrentTick; });
        foreach (var id in new[] { initiatorId, opponentId })
        {
            LeaveWater(id, "Left the water line during confrontation", reroute: false);
            if (IsSteward(id)) continue;
            var nav = _navigationAgents[new(id)];
            ApplyAgentDestination(new(id), new(TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres),
                "disorder.confrontation"));
        }
        DisorderEvent(eventId, initiatorId, opponentId, origin.Pressure,
            "Abstract confrontation after visible complaint, agitation and argument; both participants reserved in place.");
    }

    private ulong? FindDisorderOpponent(ulong actorId)
    {
        var actor = _navigationAgents[new(actorId)];
        return PeopleIn(PersonView.Disorder).Where(item => item.Id != actorId &&
                item.ConductStage is not (DisorderStage.Injured or DisorderStage.Fight) &&
                !PeopleIn(PersonView.Disorder).Any(other => other.ConductStage == DisorderStage.Fight && other.OpponentId == item.Id) &&
                PersonIn(PersonView.Roster, item.Id) is { Admitted: true, Departed: false })
            .Select(item => (item.Id, Nav: _navigationAgents[new(item.Id)]))
            .Select(item => (item.Id, DistanceSquared:
                (long)(item.Nav.XMillimetres - actor.XMillimetres) * (item.Nav.XMillimetres - actor.XMillimetres) +
                (long)(item.Nav.ZMillimetres - actor.ZMillimetres) * (item.Nav.ZMillimetres - actor.ZMillimetres)))
            .Where(item => item.DistanceSquared <= 4_000_000)
            .OrderBy(item => item.DistanceSquared).ThenBy(item => item.Id)
            .Select(item => (ulong?)item.Id).FirstOrDefault();
    }

    private void AdvanceSecurityResponse(StewardResponse response)
    {
        var d = _disorder!;
        var workerId = response.WorkerId;
        var profile = GetResponseStaff().Single(item => item.AgentId == workerId);
        if (!ImmersionOwnsNavigation(workerId) && !MedicalOwnsNavigation(workerId) && !InterventionOwnsWorker(workerId) && !CleanupOwnsNavigation(workerId) && !WasteOwnsNavigation(workerId) && response.Stage == SecurityResponseStage.Completed && !response.Incapacitated &&
            _navigationAgents[new(workerId)].Destination != StaffDutyCell(workerId, ResponseRole.Steward))
            ApplyAgentDestination(new(workerId), new(StaffDutyCell(workerId, ResponseRole.Steward), "disorder.return-to-post"));
        if (response.TargetId is not { } targetId || response.Incapacitated) return;
        // The response step decides from the target's state as it began.
        var target = _persons[targetId] with { };
        var targetNeed = target;
        if (GuestFightOrigin(targetId) is { } guestFight &&
            (target.ConductStage == DisorderStage.Fight || guestFight.HandlingAttempt is { Outcome: FightHandlingOutcome.Handling, WorkerId: var handler } && handler == workerId))
        { AdvanceGuestFightResponse(response, guestFight); return; }
        if (target.ConductStage == DisorderStage.Injured || targetNeed.HealthStage is MedicalStage.Collapsed or MedicalStage.Critical)
        {
            SetStewardResponse(response with { Stage = SecurityResponseStage.Completed, TargetId = null,
                Description = "Steward response ended; injured person is now owned by medical response" });
            DisorderEvent("security:medical-handoff", targetId, workerId, target.Pressure, "Medical handoff");
            return;
        }
        if (target.ConductStage == DisorderStage.Fight &&
            (response.Stage is SecurityResponseStage.Travelling or SecurityResponseStage.Calming ||
             response.Stage == SecurityResponseStage.Confronting && target.OpponentId != workerId))
        {
            SetStewardResponse(response with { Stage = SecurityResponseStage.Completed, TargetId = null,
                Description = "Target entered a separate confrontation before steward could calm them" });
            DisorderEvent("security:too-late", targetId, workerId, target.Pressure, "Separate confrontation; response ended");
            return;
        }
        if (StewardBusy(response) &&
            (target.Grievance == DisorderGrievance.None || target.ConductStage is DisorderStage.Resolved or DisorderStage.Calm))
        {
            SetStewardResponse(response with { Stage = SecurityResponseStage.Completed, TargetId = null,
                Description = "Grievance resolved before further confrontation" });
            DisorderEvent("security:stand-down", targetId, workerId, target.Pressure, "Grievance resolved");
            return;
        }
        if (response.Stage == SecurityResponseStage.Travelling)
        {
            var security = _navigationAgents[new(workerId)];
            var patient = _navigationAgents[new(targetId)];
            var dx = (long)security.XMillimetres - patient.XMillimetres;
            var dz = (long)security.ZMillimetres - patient.ZMillimetres;
            if (StewardAttending(workerId, targetId))
            {
                SetStewardResponse(response with { Stage = SecurityResponseStage.Calming,
                    StartedTick = CurrentTick, Description = "Steward arrived; calming attempt underway" });
                DisorderEvent("security:calming", targetId, workerId, target.Pressure, "Steward arrived; calming attempt underway");
            }
            else if (CurrentTick % 80 == 0 && StewardResponseCell(workerId, targetId) is { } cell && security.Destination != cell)
                ApplyAgentDestination(new(workerId), new(cell, "disorder.security-retarget"));
            return;
        }
        d = _disorder!;
        if (response.Stage == SecurityResponseStage.Calming)
        {
            var worker = _navigationAgents[new(workerId)]; var patient = _navigationAgents[new(targetId)];
            var dx = (long)worker.XMillimetres - patient.XMillimetres; var dz = (long)worker.ZMillimetres - patient.ZMillimetres;
            if (!StewardAttending(workerId, targetId))
            {
                SetStewardResponse(response with { Stage = SecurityResponseStage.Travelling, Description = "Target moved; steward must physically re-approach before calming" });
                if (StewardResponseCell(workerId, targetId) is { } cell) ApplyAgentDestination(new(workerId), new(cell, "disorder.security-retarget"));
                return;
            }
        }
        if (response.Stage == SecurityResponseStage.Calming && CurrentTick >= response.StartedTick + DisorderCalmingTicks)
        {
            if (profile.CalmingSkill + NextRandom(RandomStreamId.Incidents) % 2_001 >= target.Pressure + target.Temperament / 4)
            {
                MutatePerson(targetId, item => { item.ConductStage = DisorderStage.Resolved; item.Pressure = 0; item.ConductStageTick = CurrentTick; item.CooldownUntilTick = CurrentTick + 800; });
                SetStewardResponse(response with { Stage = SecurityResponseStage.Completed,
                    TargetId = null, Description = "Steward calmed the argument after arriving" });
                DisorderEvent("security:calmed", targetId, workerId, target.Pressure, "Steward calmed the argument after arriving");
            }
            else
            {
                MutatePerson(targetId, item => { item.ConductStage = DisorderStage.Argument; item.Pressure = Math.Max(item.Pressure, DisorderArgumentPressure); item.ConductStageTick = CurrentTick; item.OpponentId = workerId; });
                SetStewardResponse(response with { Stage = SecurityResponseStage.Confronting,
                    StartedTick = CurrentTick, Description = "Calming failed; aggression redirected toward steward" });
                DisorderEvent("security:calm-failed", targetId, workerId, target.Pressure, "Calming failed; aggression redirected toward steward");
            }
            return;
        }
        d = _disorder!;
        if (response.Stage == SecurityResponseStage.Confronting && response.TargetId is { } aggressorId)
        {
            var aggressor = _persons[aggressorId];
            if (aggressor.ConductStage == DisorderStage.Argument && !StewardAttending(workerId, aggressorId))
            {
                if (CurrentTick % 80 == 0 && StewardResponseCell(workerId, aggressorId) is { } near &&
                    _navigationAgents[new(workerId)].Destination != near)
                    ApplyAgentDestination(new(workerId), new(near, "disorder.confrontation-retarget"));
                return;
            }
            var person = _persons[aggressorId];
            if (aggressor.ConductStage == DisorderStage.Argument && aggressor.OpponentId == workerId &&
                person.Admitted && !person.Departed &&
                _persons[aggressorId].HealthStage is not (MedicalStage.Collapsed or MedicalStage.Critical or MedicalStage.Treated) &&
                aggressor.Pressure >= DisorderFightEligiblePressure &&
                CurrentTick >= response.StartedTick + DisorderConfrontationTicks)
            {
                BeginDisorderFight(aggressorId, workerId, "security:confrontation");
            }
            else if (aggressor.ConductStage is not (DisorderStage.Argument or DisorderStage.Fight) ||
                     aggressor.ConductStage == DisorderStage.Argument && aggressor.OpponentId != workerId)
            {
                SetStewardResponse(response with { Stage = SecurityResponseStage.Completed, TargetId = null,
                    Description = "Steward confrontation ownership ended; target is no longer in its eligible argument" });
                DisorderEvent("security:stand-down", aggressorId, workerId, aggressor.Pressure, "Confrontation ownership ended");
            }
        }
    }

    private void ResolveDisorderFight(Person fighter)
    {
        if (fighter.OpponentId is not { } opponentId) return;
        var d = _disorder!;
        var origin = d.Incidents.Last(item => item.InitiatorId == fighter.Id &&
            item.FightTick == fighter.ConductStageTick && item.InjuryTick == -1);
        var securityFight = IsSteward(opponentId);
        var initiatorPerson = _persons[fighter.Id];
        var opponentPerson = _persons[opponentId];
        var initiatorNav = _navigationAgents[new(fighter.Id)];
        var opponentNav = _navigationAgents[new(opponentId)];
        var dx = (long)initiatorNav.XMillimetres - opponentNav.XMillimetres;
        var dz = (long)initiatorNav.ZMillimetres - opponentNav.ZMillimetres;
        var opponentReserved = securityFight || PersonIn(PersonView.Disorder, opponentId) is { ConductStage: DisorderStage.Fight } opponent && opponent.OpponentId == fighter.Id;
        if (!initiatorPerson.Admitted || initiatorPerson.Departed || !opponentPerson.Admitted ||
            opponentPerson.Departed || !opponentReserved || dx * dx + dz * dz > 9_000_000)
        {
            _disorder = d with { Incidents = d.Incidents.Select(item => item == origin ? item with { InjuryTick = -2 } : item).ToArray() };
            MutatePerson(fighter.Id, item => { item.ConductStage = DisorderStage.Resolved; item.Pressure = 0; item.CooldownUntilTick = CurrentTick + 800; });
            if (!securityFight && _persons[opponentId].ConductStage == DisorderStage.Fight)
                MutatePerson(opponentId, item => { item.ConductStage = DisorderStage.Resolved; item.Pressure = 0; item.CooldownUntilTick = CurrentTick + 800; });
            DisorderEvent("disorder:confrontation-broken", fighter.Id, opponentId, origin.Pressure,
                "Confrontation ended without injury because participants were no longer together and available.");
            return;
        }
        var securityLoses = securityFight && GetResponseStaff().Single(item => item.AgentId == opponentId).ConfrontationSkill + NextRandom(RandomStreamId.Incidents) % 2_001 <
            fighter.Pressure + fighter.Temperament / 4;
        var victimId = securityLoses ? opponentId : securityFight ? fighter.Id :
            NextRandom(RandomStreamId.Incidents) % 2 == 0 ? fighter.Id : opponentId;
        var victimNeed = _persons[victimId];
        if (victimNeed.HealthStage is MedicalStage.Collapsed or MedicalStage.Critical or MedicalStage.Treated)
        {
            _disorder = d with { Incidents = d.Incidents.Select(item => item == origin ? item with { InjuryTick = -2 } : item).ToArray() };
            MutatePerson(fighter.Id, item => { item.ConductStage = DisorderStage.Resolved; item.Pressure = 0; item.CooldownUntilTick = CurrentTick + 800; });
            if (!securityFight) MutatePerson(opponentId, item => { item.ConductStage = DisorderStage.Resolved; item.Pressure = 0; item.CooldownUntilTick = CurrentTick + 800; });
            return;
        }
        _disorder = d with { Incidents = d.Incidents.Select(item => item == origin ? item with { InjuryTick = CurrentTick,
            VictimId = victimId } : item).ToArray() };
        LeaveWater(victimId, "Injured during confrontation", reroute: false);
        var nav = _navigationAgents[new(victimId)];
        ApplyAgentDestination(new(victimId), new(TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres),
            "disorder.injured"));
        MutatePerson(victimId, item => { item.HealthStage = MedicalStage.Collapsed; item.Intent = MedicalIntent.Collapsed; item.HealthCollapseTick = CurrentTick; item.Reason = "Generic serious confrontation injury; physical first aid needed"; });
        if (securityLoses)
            SetStewardResponse(GetStewardResponses().Single(item => item.WorkerId == opponentId) with {
                Incapacitated = true, Stage = SecurityResponseStage.Failed, Description = "Steward incapacitated; medical help needed" });
        else if (securityFight)
            SetStewardResponse(GetStewardResponses().Single(item => item.WorkerId == opponentId) with {
                Stage = SecurityResponseStage.Completed, TargetId = null, Description = "Confrontation ended; injured guest needs medical help" });
        if (!IsSteward(victimId))
            MutatePerson(victimId, item => { item.ConductStage = DisorderStage.Injured; item.InjuryTick = CurrentTick; item.ConductStageTick = CurrentTick; item.OpponentId = fighter.Id; });
        foreach (var participant in new[] { fighter.Id, opponentId }.Where(id => id != victimId && !IsSteward(id)))
            MutatePerson(participant, item => { item.ConductStage = DisorderStage.Resolved; item.Pressure = 0; item.CooldownUntilTick = CurrentTick + 800; });
        DisorderEvent("disorder:injury", fighter.Id, victimId, fighter.Pressure,
            $"Generic serious injury to person {victimId}; security {d.Stewards[0].Stage}; first aid needed before tick {CurrentTick + DisorderInjuryDeathTicks}.");
    }

    private void ApplyDisorderDeath(ulong victimId)
    {
        var p = _preparation!;
        var victim = _persons[victimId];
        var d = _disorder!;
        var origin = d.Incidents.Last(item => item.VictimId == victimId && item.InjuryTick >= 0);
        var cause = $"Confrontation injury to {victim.Name} ({victim.Role}); initiating person {origin.InitiatorId}, opponent {origin.OpponentId}, " +
            $"grievance {origin.Grievance}, pressure {origin.Pressure}/10000, argument tick {origin.ArgumentTick}, " +
            $"fight tick {origin.FightTick}, injury tick {origin.InjuryTick}, " +
            $"{StaffResponseCausalSummary()}.";
        SetStewardResponse(d.Stewards[0] with { Description = cause });
        DisorderEvent("disorder:death", origin.InitiatorId, victimId, origin.Pressure, cause);
        var lifecycle = _lifecycle!;
        var attempt = CurrentAttempt();
        var transaction = $"disorder-death:{CampaignId.Value}:{attempt.AttemptId}";
        lifecycle.Casualties.Add(new(lifecycle.NextCasualtyId++, attempt.AttemptId, victim.Name, victim.Role,
            cause, CurrentTick, transaction));
        lifecycle.CompletedOutcomeTransactionIds.Add(transaction);
        ReplaceAttempt(attempt with { Status = EditionAttemptStatus.Failed, OutcomeTransactionId = transaction });
        var hearing = $"disorder-hearing:{CampaignId.Value}:{attempt.AttemptId}";
        lifecycle.Hearings.Add(new(lifecycle.NextHearingId++, attempt.AttemptId, HearingStatus.Open, hearing, null));
        lifecycle.CompletedOutcomeTransactionIds.Add(hearing);
        ResolveNoFavourHearing();
        _preparation = p with { Status = PreparationStatus.Failed, Rentals = [], WorkContracts = [] };
        ReleaseInterventionsForBoundary("First death froze the edition; intervention released");
        FinishLivePerformance();
    }

    private static string? ValidatePersistedDisorder(DisorderSnapshot? d, SessionPersistenceSnapshot s)
    {
        if (d is null) return null;
        if (s.Preparation is not { } p || s.Medical is not { Version: 6 } medical ||
            d.Version != 1 || d.People is null || d.Evidence is null || d.Incidents is null || d.Stewards is not [{ } security, ..] ||
            d.People.Length != p.Tier * 20 ||
            !d.People.Select(item => item.AgentId).SequenceEqual(p.People.Where(item => item.Role == ProtectedPersonRole.Guest).Select(item => item.AgentId)) ||
            !p.People.Any(item => item.AgentId == d.SecurityId && item.Role == ProtectedPersonRole.Staff) ||
            !medical.Needs.Any(item => item.AgentId == d.SecurityId && item.Profile == MedicalNeedProfile.Staff) ||
            d.CalmingSkill is < 3_500 or > 8_000 || d.ConfrontationSkill is < 3_500 or > 8_000 ||
            !Enum.IsDefined(security.Stage) || string.IsNullOrWhiteSpace(security.Description) ||
            security.StartedTick > s.CurrentTick ||
            security.Stage is SecurityResponseStage.Travelling or SecurityResponseStage.Calming or SecurityResponseStage.Confronting &&
                (security.TargetId is null || !d.People.Any(item => item.AgentId == security.TargetId)) ||
            !StewardCollapseConsistent(s, security) ||
            d.WaterClosed && (s.Facilities?.Taps ?? []).Any(point => point.Queue.Length > 0 || point.Overflow.Length > 0 || point.OwnerId is not null) ||
            d.People.Any(item => item is null || item.Temperament is < 2_000 or > 8_000 ||
                item.QueueToleranceTicks is < 320 or > 1_120 || item.Pressure is < 0 or > 10_000 ||
                !Enum.IsDefined(item.Grievance) || !Enum.IsDefined(item.Stage) ||
                item.Grievance == DisorderGrievance.BandDelayed && (s.Programme is null || item.GrievanceTick < 0 ||
                    !FestivalSlotStarts.Where((start, index) => item.GrievanceTick >= p.StartedTick + start &&
                        item.GrievanceTick < p.StartedTick + FestivalSlotEnds[index]).Any()) ||
                item.GrievanceTick > s.CurrentTick || item.StageTick > s.CurrentTick ||
                item.QueueJoinedTick > s.CurrentTick || item.InjuryTick > s.CurrentTick ||
                item.OpponentId is { } opponent && !p.People.Any(person => person.AgentId == opponent) ||
                item.Stage == DisorderStage.Injured && (item.InjuryTick < 0 ||
                    medical.Needs.Single(need => need.AgentId == item.AgentId).Stage != MedicalStage.Collapsed)) ||
            d.Evidence.Length > 96 || d.Evidence.Any(item => item is null || item.Tick < 0 || item.Tick > s.CurrentTick ||
                !p.People.Any(person => person.AgentId == item.PersonId) || string.IsNullOrWhiteSpace(item.Id) ||
                string.IsNullOrWhiteSpace(item.Description) || item.Pressure is < 0 or > 10_000) ||
            d.Incidents.Any(item => item is null || !d.People.Any(person => person.AgentId == item.InitiatorId) ||
                !p.People.Any(person => person.AgentId == item.OpponentId) || !Enum.IsDefined(item.Grievance) ||
                item.Grievance == DisorderGrievance.BandDelayed && s.Programme is null ||
                item.Grievance == DisorderGrievance.None || item.Pressure < DisorderArgumentPressure || item.Pressure > 10_000 ||
                item.ArgumentTick < 0 || item.ArgumentTick > item.FightTick || item.FightTick > s.CurrentTick ||
                item.InjuryTick < -2 || item.InjuryTick > s.CurrentTick ||
                item.InjuryTick >= 0 != (item.VictimId is not null) ||
                item.VictimId is { } injuredId && injuredId != item.InitiatorId && injuredId != item.OpponentId) ||
            !d.Evidence.Select(item => item.Tick).SequenceEqual(d.Evidence.Select(item => item.Tick).Order()) ||
            d.Evidence.LastOrDefault()?.Id == "disorder:death" &&
                (p.Status != PreparationStatus.Failed || s.Lifecycle?.Casualties.Count(casualty => casualty.AttemptId == (ulong)p.Attempt) != 1))
            return "Disorder pressure, response ownership, evidence or protected-person state invalid.";
        return ValidatePersistedFightHandling(d, s);
    }
}

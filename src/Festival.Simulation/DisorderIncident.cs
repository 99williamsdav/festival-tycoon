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
public sealed record DisorderIncidentOrigin(ulong InitiatorId, ulong OpponentId, DisorderGrievance Grievance,
    int Pressure, long ArgumentTick, long FightTick, long InjuryTick, ulong? VictimId);
public sealed record DisorderSnapshot(int Version, ulong SecurityId, int CalmingSkill, int ConfrontationSkill,
    bool SecurityIncapacitated, bool WaterClosed, DisorderPerson[] People, SecurityResponseStage ResponseStage,
    ulong? ResponseTargetId, long ResponseStartedTick, string Response, DisorderEvidence[] Evidence)
{
    public DisorderIncidentOrigin[] Incidents { get; init; } = [];
    public StewardResponse[] ExtraResponses { get; init; } = [];
    public long ResponseDispatchedTick { get; init; } = -1;
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
    private DisorderSnapshot? _disorder;
    private bool DisorderOwnsNavigation(ulong id) => _disorder is { } d &&
        (d.People.Any(item => item.AgentId == id && item.Stage == DisorderStage.Fight) ||
         d.People.Any(item => item.Stage == DisorderStage.Fight && item.OpponentId == id));
    public DisorderSnapshot? CaptureDisorder() => _disorder is null ? null :
        JsonSerializer.Deserialize<DisorderSnapshot>(JsonSerializer.Serialize(_disorder));
    internal string? DisorderCanonicalJson => _disorder is not { } d ? null : StaffCompatibleCanonicalJson(d,
        d.ExtraResponses.Length > 0 ? "" : nameof(d.ExtraResponses), d.ResponseDispatchedTick >= 0 ? "" : nameof(d.ResponseDispatchedTick));

    public static GameSession CreateDisorderCampaign(ulong seed, int tier = 1)
    {
        var session = CreateMedicalCampaign(seed, tier);
        var securityId = session.NextEntityId++;
        session._wallets.Add(new(securityId), new WalletState { OwnerId = new(securityId), CashPennies = 500 });
        session._preparation = session._preparation! with { People = session._preparation.People.Append(
            new EditionPerson(securityId, "Jordan Hale", ProtectedPersonRole.Staff, 0)).ToArray() };
        session._medical = session._medical! with { Version = 6, Needs = session._medical.Needs.Append(
            new MedicalNeed(securityId, 0, 0, MedicalIntent.WatchShow, "Security on duty", -MedicalDecisionCooldownTicks,
                null, -1, MedicalNeedProfile.Staff)).ToArray() };
        var skills = RandomStreamFactory.Create(seed ^ securityId, RandomStreamId.IndividualBehaviour);
        var people = session._preparation.People.Where(item => item.Role == ProtectedPersonRole.Guest).Select(person =>
        {
            var random = RandomStreamFactory.Create(seed ^ person.AgentId, RandomStreamId.IndividualBehaviour);
            return new DisorderPerson(person.AgentId, 2_000 + (int)(random.NextUInt32() % 6_001),
                320 + (int)(random.NextUInt32() % 801), 0, DisorderGrievance.None, DisorderStage.Calm,
                -1, -1, -1, -1, -1, null);
        }).ToArray();
        session._disorder = new(1, securityId, 3_500 + (int)(skills.NextUInt32() % 4_501),
            3_500 + (int)(skills.NextUInt32() % 4_501), false, false, people,
            SecurityResponseStage.None, null, -1, "Security available", []);
        return session;
    }

    private void DisorderEvent(string id, ulong personId, ulong? otherId, int pressure, string description)
    {
        var nav = _navigationAgents.GetValueOrDefault(new(personId));
        var item = new DisorderEvidence(id, CurrentTick, personId, otherId,
            nav?.XMillimetres ?? 0, nav?.ZMillimetres ?? 0, pressure, description);
        _disorder = _disorder! with { Evidence = _disorder.Evidence.Append(item).TakeLast(96).ToArray() };
    }

    private void SetDisorderPerson(ulong id, Func<DisorderPerson, DisorderPerson> change)
    {
        var d = _disorder!;
        _disorder = d with { People = d.People.Select(item => item.AgentId == id ? change(item) : item).ToArray() };
    }

    private CommandResult? ValidateDisorderCommand(EntityId? target, DisorderCommand command, bool developmentFixture = false)
    {
        if (!developmentFixture && command.Action == DisorderAction.SafeEgress && command.PersonId is { } escortId)
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
            if (command.PersonId is not { } id || !d.People.Any(item => item.AgentId == id))
                return CommandResult.Rejected(CommandReasonCode.UnknownTarget, "Select an affected guest.");
            var person = d.People.Single(item => item.AgentId == id);
            if (GetMedicResponses().Any(item => MedicBusy(item) && item.PatientId == id) ||
                _medical!.Needs.Single(item => item.AgentId == id).Stage is MedicalStage.Collapsed or MedicalStage.Critical)
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "This person is owned by medical response or needs first aid, not steward reassignment.");
            if (command.Action == DisorderAction.DispatchSecurity)
            {
                if (!_preparation.People.Single(item => item.AgentId == id).Admitted || _preparation.People.Single(item => item.AgentId == id).Departed)
                    return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "The affected person is not physically on site.");
                if (person.Stage is not (DisorderStage.Complaint or DisorderStage.Agitated or DisorderStage.Argument or DisorderStage.Fight))
                    return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "No active disturbance to attend.");
                if (job!.Incapacitated || !_preparation.People.Single(item => item.AgentId == workerId).Admitted || _preparation.People.Single(item => item.AgentId == workerId).Departed)
                    return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Security is not physically available.");
                if (StewardBusy(job) || GetStewardResponses().Any(item => StewardBusy(item) && item.TargetId == id))
                    return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Security is already responding; this request is backlogged.");
                if (MedicalResponseCell(workerId, id) is null)
                    return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Security cannot reach this person.");
            }
            else if (person.Stage is DisorderStage.Injured or DisorderStage.Fight ||
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

    private void ApplyDisorderCommand(DisorderCommand command, bool developmentFixture = false)
    {
        if (!developmentFixture && command.Action == DisorderAction.SafeEgress)
        { ApplyStaffIntervention(new(command.PersonId!.Value, command.WorkerId ?? _disorder!.SecurityId, StaffInterventionAction.EscortOut)); return; }
        var d = _disorder!;
        if (command.Action == DisorderAction.DispatchSecurity)
        {
            var id = command.PersonId!.Value;
            var workerId = command.WorkerId ?? d.SecurityId;
            LeaveWater(workerId, "Steward explicitly recalled from water for assigned response", reroute: false);
            SetNeed(workerId, item => item with { Intent = MedicalIntent.WatchShow, Reason = "Steward responding to assigned target", QueueSlot = null });
            ApplyAgentDestination(new(workerId), new(MedicalResponseCell(workerId, id)!.Value, "disorder.security-dispatch"));
            var description = $"Steward {workerId} walking to person {id}; not yet calming";
            SetStewardResponse(new(workerId, SecurityResponseStage.Travelling, id, CurrentTick, false, description, CurrentTick));
            DisorderEvent("security:dispatch", id, workerId, d.People.Single(item => item.AgentId == id).Pressure, description);
            return;
        }
        if (command.Action == DisorderAction.SafeEgress)
        {
            var id = command.PersonId!.Value;
            LeaveWater(id, "Safe egress chosen", reroute: false);
            SetNeed(id, item => item with { Intent = MedicalIntent.Leaving, Reason = "Safe exit via the gate" });
            ApplyAgentDestination(new(id), new(MedicalExitCell, "disorder.safe-egress"));
            SetDisorderPerson(id, item => item with { Stage = DisorderStage.Resolved, Pressure = 0,
                Grievance = DisorderGrievance.None, StageTick = CurrentTick, CooldownUntilTick = long.MaxValue });
            DisorderEvent("disorder:egress", id, null, 0, "Person walking to the safe gate; egress completes only on arrival.");
            return;
        }
        if (command.Action == DisorderAction.CloseWater)
        {
            _disorder = d with { WaterClosed = true };
            foreach (var id in WaterPoints().SelectMany(point => point.Queue.Concat(point.Overflow))
                         .Concat(_medical!.Needs.Where(item => item.Intent == MedicalIntent.SeekWater).Select(item => item.AgentId))
                         .Distinct().ToArray())
            {
                LeaveWater(id, "Water service closed safely", reroute: false);
                var need = _medical!.Needs.Single(item => item.AgentId == id);
                if (id == _medical.AtRiskGuestId || need.Profile == MedicalNeedProfile.Performer)
                {
                    SetNeed(id, item => item with { Intent = MedicalIntent.Rest, Reason = "Closed-water relief at first-aid rest" });
                    ApplyAgentDestination(new(id), new(MedicalRestCell, "disorder.water-closure-rest"));
                }
                else ReturnToListening(id);
            }
            DisorderEvent("disorder:water-closed", d.People[0].AgentId, null, 0,
                "Water service closed; physical queue released, rest and gate remain reachable.");
            return;
        }
        if (command.Action == DisorderAction.ReopenWater)
        {
            _disorder = d with { WaterClosed = false };
            DisorderEvent("disorder:water-opened", d.People[0].AgentId, null, 0, "Free water reopened.");
            return;
        }
        // Isolation already removed the dangerous load. A reset is permitted only
        // above the safe condition floor; it restores the existing 80% baseline.
        _equipment = _equipment! with { Stage = EquipmentStage.Resolved, LoadPercent = 80,
            Response = "Explicit safe reset at 80% after isolation" };
        EquipmentEvent("equipment:safe-reset", _equipment.Response);
        DisorderEvent("disorder:music-safe-reset", d.People[0].AgentId, null, 0,
            "Stage power safely restored at 80%; music resumes on the next set update.");
    }

    public bool DisorderBoundaryOnNextTick => !IsPaused && _disorder is { } d &&
        _preparation is { Status: PreparationStatus.Running } && d.People.Any(item =>
            item.Stage == DisorderStage.Injured && CurrentTick + 1 >= item.InjuryTick + DisorderInjuryDeathTicks &&
            _medical!.Needs.Single(need => need.AgentId == item.AgentId).Stage != MedicalStage.Treated) ||
        !IsPaused && _preparation is { Status: PreparationStatus.Running } && GetStewardResponses().Any(response => response.Incapacitated &&
        _medical!.Needs.Single(need => need.AgentId == response.WorkerId) is { Stage: MedicalStage.Collapsed } securityNeed &&
        CurrentTick + 1 >= securityNeed.CollapseTick + DisorderInjuryDeathTicks);

    private void AdvanceDisorder()
    {
        if (_disorder is not { } d || _preparation is not { Status: PreparationStatus.Running } p) return;
        // Injury deadlines are checked every authoritative tick; ordinary social
        // assessment is batched at a deterministic tenth of a real second.
        foreach (var injured in d.People.Where(item => item.Stage == DisorderStage.Injured).ToArray())
        {
            if (_medical!.Needs.Single(item => item.AgentId == injured.AgentId).Stage == MedicalStage.Treated)
            {
                SetDisorderPerson(injured.AgentId, item => item with { Stage = DisorderStage.Resolved,
                    Pressure = 0, StageTick = CurrentTick, CooldownUntilTick = CurrentTick + 800 });
                DisorderEvent("disorder:treated", injured.AgentId, injured.OpponentId, 0,
                    "Physical first aid completed after confrontation injury.");
            }
            else if (CurrentTick >= injured.InjuryTick + DisorderInjuryDeathTicks)
            {
                ApplyDisorderDeath(injured.AgentId);
                return;
            }
        }
        d = _disorder!;
        foreach (var response in GetStewardResponses().Where(item => item.Incapacitated))
        {
            var securityNeed = _medical!.Needs.Single(item => item.AgentId == response.WorkerId);
            if (securityNeed.Stage == MedicalStage.Treated)
            {
                SetStewardResponse(response with { Incapacitated = false, Stage = SecurityResponseStage.Completed, TargetId = null,
                    Description = "Steward treated after confrontation" });
                DisorderEvent("security:treated", response.WorkerId, response.TargetId, 0, "Steward treated after confrontation");
            }
            else if (CurrentTick >= securityNeed.CollapseTick + DisorderInjuryDeathTicks)
            {
                ApplyDisorderDeath(response.WorkerId);
                return;
            }
        }
        if (CurrentTick % 8 != 0) return;
        d = _disorder!;
        foreach (var person in d.People.ToArray())
        {
            if (_disorder!.People.Single(item => item.AgentId == person.AgentId).Stage == DisorderStage.Fight) continue;
            if (person.Stage == DisorderStage.Injured || person.Stage == DisorderStage.Fight) continue;
            var protectedPerson = p.People.Single(item => item.AgentId == person.AgentId);
            if (!protectedPerson.Admitted || protectedPerson.Departed) continue;
            if (_medical!.Needs.Single(item => item.AgentId == person.AgentId).Intent == MedicalIntent.Leaving)
            {
                if (InterventionOwnsTarget(person.AgentId)) continue;
                var nav = _navigationAgents[new(person.AgentId)];
                if (nav.Action == AgentNavigationAction.Arrived && nav.Destination == MedicalExitCell)
                {
                    _preparation = p = p with { People = p.People.Select(item => item.AgentId == person.AgentId
                        ? item with { Departed = true } : item).ToArray() };
                    DisorderEvent("disorder:egress-complete", person.AgentId, null, 0,
                        "Person reached the safe gate; no teleport or casualty.");
                }
                continue;
            }
            var inWaterLine = !d.WaterClosed && WaterPoints().Any(point =>
                point.Queue.Contains(person.AgentId) || point.Overflow.Contains(person.AgentId));
            var joined = inWaterLine ? person.QueueJoinedTick < 0 ? CurrentTick : person.QueueJoinedTick : -1;
            var need = _medical.Needs.Single(item => item.AgentId == person.AgentId);
            var listener = _livePerformance?.Listeners.SingleOrDefault(item => item.AgentId == person.AgentId);
            var lateAct = LateReadyFestivalAct;
            var lateEnthusiasm = lateAct is null ? 0 : FestivalAffinity(person.AgentId, lateAct);
            var waitingForBand = FestivalBandLate && listener is { AtPlace: true } && lateEnthusiasm >= 35 &&
                need.Intent == MedicalIntent.WatchShow && need.Stage is MedicalStage.Clear or MedicalStage.Treated &&
                need.Thirst < MedicalDistressThirst && need.HeatExposure < MedicalDistressHeat &&
                !(need.AgentId == _medical.AtRiskGuestId && _medical.Stage is MedicalStage.Distress or MedicalStage.Collapsed or MedicalStage.Critical) &&
                !InterventionOwnsTarget(person.AgentId) && !InterventionOwnsWorker(person.AgentId);
            var grievance = _livePerformance?.Stage == LiveSetStage.Interrupted && !ScheduledSilence &&
                (_programme is null || CurrentTick >= _livePerformance.PlannedTick && CurrentTick < _programme.SlotEndTick &&
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
                pressure = Math.Min(10_000, pressure + (rate + ImmersionAggression(person.AgentId,person.Temperament)) * 8);
            }
            else pressure = Math.Max(0, pressure - 48);
            var stage = pressure >= DisorderArgumentPressure ? DisorderStage.Argument :
                pressure >= DisorderAgitatedPressure ? DisorderStage.Agitated :
                pressure >= DisorderComplaintPressure ? DisorderStage.Complaint : DisorderStage.Calm;
            if (person.Stage == DisorderStage.Resolved && CurrentTick < person.CooldownUntilTick) stage = DisorderStage.Resolved;
            if (grievance == DisorderGrievance.None && pressure == 0) stage = DisorderStage.Calm;
            var changed = person with { Pressure = pressure, Grievance = grievance,
                GrievanceTick = grievance == DisorderGrievance.None ? -1 :
                    grievance != person.Grievance ? CurrentTick : person.GrievanceTick,
                QueueJoinedTick = joined, Stage = stage,
                StageTick = stage == person.Stage ? person.StageTick : CurrentTick };
            SetDisorderPerson(person.AgentId, _ => changed);
            d = _disorder!;
            if (grievance == DisorderGrievance.BandDelayed && person.Grievance != DisorderGrievance.BandDelayed)
                DisorderEvent($"disorder:band-late:{person.AgentId}:{CurrentTick}", person.AgentId, null, pressure,
                    $"Where is {lateAct?.Name}? Scheduled start tick {LateReadyScheduledTick}; physical performer readiness still prevents music; pressure {pressure}/10000.");
            if (stage != person.Stage)
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
                DisorderEvent($"disorder:{stage.ToString().ToLowerInvariant()}:{person.AgentId}:{CurrentTick}",
                    person.AgentId, null, pressure, $"{label}; cause {grievance}; pressure {pressure}/10000; temperament hidden.");
            }
            if (stage != DisorderStage.Argument || grievance == DisorderGrievance.None || CurrentTick % 80 != 0) continue;
            if (NextRandom(RandomStreamId.Incidents) % 20 == 0)
            {
                SetDisorderPerson(person.AgentId, item => item with { Stage = DisorderStage.Resolved,
                    Pressure = 0, StageTick = CurrentTick, CooldownUntilTick = CurrentTick + 800 });
                DisorderEvent("disorder:diffused", person.AgentId, null, pressure,
                    "Argument subsided without a security intervention despite a continuing grievance.");
                continue;
            }
            if (pressure < DisorderFightEligiblePressure) continue;
            var opponent = FindDisorderOpponent(person.AgentId);
            if (opponent is null || NextRandom(RandomStreamId.Incidents) % 4 != 0) continue;
            BeginDisorderFight(person.AgentId, opponent.Value, "disorder:fight");
        }
        foreach (var response in GetStewardResponses()) AdvanceSecurityResponse(response);
        foreach (var fighter in _disorder!.People.Where(item => item.Stage == DisorderStage.Fight &&
                     _disorder.Incidents.Any(origin => origin.InitiatorId == item.AgentId &&
                         origin.FightTick == item.StageTick && origin.InjuryTick == -1) &&
                     CurrentTick >= item.StageTick + DisorderFightDurationTicks).ToArray())
            ResolveDisorderFight(fighter);
    }

    private void BeginDisorderFight(ulong initiatorId, ulong opponentId, string eventId)
    {
        var d = _disorder!;
        var initiator = d.People.Single(item => item.AgentId == initiatorId);
        var origin = new DisorderIncidentOrigin(initiatorId, opponentId, initiator.Grievance,
            initiator.Pressure, initiator.StageTick, CurrentTick, -1, null);
        _disorder = d with { Incidents = d.Incidents.Append(origin).ToArray() };
        SetDisorderPerson(initiatorId, item => item with { Stage = DisorderStage.Fight,
            OpponentId = opponentId, StageTick = CurrentTick });
        if (!IsSteward(opponentId))
            SetDisorderPerson(opponentId, item => item with { Stage = DisorderStage.Fight,
                OpponentId = initiatorId, StageTick = CurrentTick });
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
        return _disorder!.People.Where(item => item.AgentId != actorId &&
                item.Stage is not (DisorderStage.Injured or DisorderStage.Fight) &&
                !_disorder.People.Any(other => other.Stage == DisorderStage.Fight && other.OpponentId == item.AgentId) &&
                _preparation!.People.Any(person => person.AgentId == item.AgentId && person.Admitted && !person.Departed))
            .Select(item => (item.AgentId, Nav: _navigationAgents[new(item.AgentId)]))
            .Select(item => (item.AgentId, DistanceSquared:
                (long)(item.Nav.XMillimetres - actor.XMillimetres) * (item.Nav.XMillimetres - actor.XMillimetres) +
                (long)(item.Nav.ZMillimetres - actor.ZMillimetres) * (item.Nav.ZMillimetres - actor.ZMillimetres)))
            .Where(item => item.DistanceSquared <= 4_000_000)
            .OrderBy(item => item.DistanceSquared).ThenBy(item => item.AgentId)
            .Select(item => (ulong?)item.AgentId).FirstOrDefault();
    }

    private void AdvanceSecurityResponse(StewardResponse response)
    {
        var d = _disorder!;
        var workerId = response.WorkerId;
        var profile = GetResponseStaff().Single(item => item.AgentId == workerId);
        if (!ImmersionOwnsNavigation(workerId) && !MedicalOwnsNavigation(workerId) && !InterventionOwnsWorker(workerId) && response.Stage == SecurityResponseStage.Completed && !response.Incapacitated &&
            _navigationAgents[new(workerId)].Destination != StaffDutyCell(workerId, ResponseRole.Steward))
            ApplyAgentDestination(new(workerId), new(StaffDutyCell(workerId, ResponseRole.Steward), "disorder.return-to-post"));
        if (response.TargetId is not { } targetId || response.Incapacitated) return;
        var target = d.People.Single(item => item.AgentId == targetId);
        var targetNeed = _medical!.Needs.Single(item => item.AgentId == targetId);
        if (target.Stage == DisorderStage.Injured || targetNeed.Stage is MedicalStage.Collapsed or MedicalStage.Critical)
        {
            SetStewardResponse(response with { Stage = SecurityResponseStage.Completed, TargetId = null,
                Description = "Steward response ended; injured person is now owned by medical response" });
            DisorderEvent("security:medical-handoff", targetId, workerId, target.Pressure, "Medical handoff");
            return;
        }
        if (target.Stage == DisorderStage.Fight &&
            (response.Stage is SecurityResponseStage.Travelling or SecurityResponseStage.Calming ||
             response.Stage == SecurityResponseStage.Confronting && target.OpponentId != workerId))
        {
            SetStewardResponse(response with { Stage = SecurityResponseStage.Completed, TargetId = null,
                Description = "Target entered a separate confrontation before steward could calm them" });
            DisorderEvent("security:too-late", targetId, workerId, target.Pressure, "Separate confrontation; response ended");
            return;
        }
        if (StewardBusy(response) &&
            (target.Grievance == DisorderGrievance.None || target.Stage is DisorderStage.Resolved or DisorderStage.Calm))
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
            if (security.Action == AgentNavigationAction.Arrived && dx * dx + dz * dz <= 6_250_000)
            {
                SetStewardResponse(response with { Stage = SecurityResponseStage.Calming,
                    StartedTick = CurrentTick, Description = "Steward arrived; calming attempt underway" });
                DisorderEvent("security:calming", targetId, workerId, target.Pressure, "Steward arrived; calming attempt underway");
            }
            else if (CurrentTick % 80 == 0 && MedicalResponseCell(workerId, targetId) is { } cell && security.Destination != cell)
                ApplyAgentDestination(new(workerId), new(cell, "disorder.security-retarget"));
            return;
        }
        d = _disorder!;
        if (response.Stage == SecurityResponseStage.Calming)
        {
            var worker = _navigationAgents[new(workerId)]; var patient = _navigationAgents[new(targetId)];
            var dx = (long)worker.XMillimetres - patient.XMillimetres; var dz = (long)worker.ZMillimetres - patient.ZMillimetres;
            if (worker.Action != AgentNavigationAction.Arrived || dx * dx + dz * dz > 6_250_000)
            {
                SetStewardResponse(response with { Stage = SecurityResponseStage.Travelling, Description = "Target moved; steward must physically re-approach before calming" });
                if (MedicalResponseCell(workerId, targetId) is { } cell) ApplyAgentDestination(new(workerId), new(cell, "disorder.security-retarget"));
                return;
            }
        }
        if (response.Stage == SecurityResponseStage.Calming && CurrentTick >= response.StartedTick + DisorderCalmingTicks)
        {
            if (profile.CalmingSkill + NextRandom(RandomStreamId.Incidents) % 2_001 >= target.Pressure + target.Temperament / 4)
            {
                SetDisorderPerson(targetId, item => item with { Stage = DisorderStage.Resolved, Pressure = 0,
                    StageTick = CurrentTick, CooldownUntilTick = CurrentTick + 800 });
                SetStewardResponse(response with { Stage = SecurityResponseStage.Completed,
                    TargetId = null, Description = "Steward calmed the argument after arriving" });
                DisorderEvent("security:calmed", targetId, workerId, target.Pressure, "Steward calmed the argument after arriving");
            }
            else
            {
                SetDisorderPerson(targetId, item => item with { Stage = DisorderStage.Argument,
                    Pressure = Math.Max(item.Pressure, DisorderArgumentPressure), StageTick = CurrentTick,
                    OpponentId = workerId });
                SetStewardResponse(response with { Stage = SecurityResponseStage.Confronting,
                    StartedTick = CurrentTick, Description = "Calming failed; aggression redirected toward steward" });
                DisorderEvent("security:calm-failed", targetId, workerId, target.Pressure, "Calming failed; aggression redirected toward steward");
            }
            return;
        }
        d = _disorder!;
        if (response.Stage == SecurityResponseStage.Confronting && response.TargetId is { } aggressorId)
        {
            var aggressor = d.People.Single(item => item.AgentId == aggressorId);
            var person = _preparation!.People.Single(item => item.AgentId == aggressorId);
            if (aggressor.Stage == DisorderStage.Argument && aggressor.OpponentId == workerId &&
                person.Admitted && !person.Departed &&
                _medical!.Needs.Single(item => item.AgentId == aggressorId).Stage is not (MedicalStage.Collapsed or MedicalStage.Critical or MedicalStage.Treated) &&
                aggressor.Pressure >= DisorderFightEligiblePressure &&
                CurrentTick >= response.StartedTick + DisorderConfrontationTicks)
            {
                BeginDisorderFight(aggressorId, workerId, "security:confrontation");
            }
            else if (aggressor.Stage is not (DisorderStage.Argument or DisorderStage.Fight) ||
                     aggressor.Stage == DisorderStage.Argument && aggressor.OpponentId != workerId)
            {
                SetStewardResponse(response with { Stage = SecurityResponseStage.Completed, TargetId = null,
                    Description = "Steward confrontation ownership ended; target is no longer in its eligible argument" });
                DisorderEvent("security:stand-down", aggressorId, workerId, aggressor.Pressure, "Confrontation ownership ended");
            }
        }
    }

    private void ResolveDisorderFight(DisorderPerson fighter)
    {
        if (fighter.OpponentId is not { } opponentId) return;
        var d = _disorder!;
        var origin = d.Incidents.Last(item => item.InitiatorId == fighter.AgentId &&
            item.FightTick == fighter.StageTick && item.InjuryTick == -1);
        var securityFight = IsSteward(opponentId);
        var initiatorPerson = _preparation!.People.Single(item => item.AgentId == fighter.AgentId);
        var opponentPerson = _preparation.People.Single(item => item.AgentId == opponentId);
        var initiatorNav = _navigationAgents[new(fighter.AgentId)];
        var opponentNav = _navigationAgents[new(opponentId)];
        var dx = (long)initiatorNav.XMillimetres - opponentNav.XMillimetres;
        var dz = (long)initiatorNav.ZMillimetres - opponentNav.ZMillimetres;
        var opponentReserved = securityFight || d.People.Any(item => item.AgentId == opponentId &&
            item.Stage == DisorderStage.Fight && item.OpponentId == fighter.AgentId);
        if (!initiatorPerson.Admitted || initiatorPerson.Departed || !opponentPerson.Admitted ||
            opponentPerson.Departed || !opponentReserved || dx * dx + dz * dz > 9_000_000)
        {
            _disorder = d with { Incidents = d.Incidents.Select(item => item == origin ? item with { InjuryTick = -2 } : item).ToArray() };
            SetDisorderPerson(fighter.AgentId, item => item with { Stage = DisorderStage.Resolved,
                Pressure = 0, CooldownUntilTick = CurrentTick + 800 });
            if (!securityFight && d.People.Single(item => item.AgentId == opponentId).Stage == DisorderStage.Fight)
                SetDisorderPerson(opponentId, item => item with { Stage = DisorderStage.Resolved,
                    Pressure = 0, CooldownUntilTick = CurrentTick + 800 });
            DisorderEvent("disorder:confrontation-broken", fighter.AgentId, opponentId, origin.Pressure,
                "Confrontation ended without injury because participants were no longer together and available.");
            return;
        }
        var securityLoses = securityFight && GetResponseStaff().Single(item => item.AgentId == opponentId).ConfrontationSkill + NextRandom(RandomStreamId.Incidents) % 2_001 <
            fighter.Pressure + fighter.Temperament / 4;
        var victimId = securityLoses ? opponentId : securityFight ? fighter.AgentId :
            NextRandom(RandomStreamId.Incidents) % 2 == 0 ? fighter.AgentId : opponentId;
        var victimNeed = _medical!.Needs.Single(item => item.AgentId == victimId);
        if (victimNeed.Stage is MedicalStage.Collapsed or MedicalStage.Critical or MedicalStage.Treated)
        {
            _disorder = d with { Incidents = d.Incidents.Select(item => item == origin ? item with { InjuryTick = -2 } : item).ToArray() };
            SetDisorderPerson(fighter.AgentId, item => item with { Stage = DisorderStage.Resolved,
                Pressure = 0, CooldownUntilTick = CurrentTick + 800 });
            if (!securityFight) SetDisorderPerson(opponentId, item => item with { Stage = DisorderStage.Resolved,
                Pressure = 0, CooldownUntilTick = CurrentTick + 800 });
            return;
        }
        _disorder = d with { Incidents = d.Incidents.Select(item => item == origin ? item with { InjuryTick = CurrentTick,
            VictimId = victimId } : item).ToArray() };
        LeaveWater(victimId, "Injured during confrontation", reroute: false);
        var nav = _navigationAgents[new(victimId)];
        ApplyAgentDestination(new(victimId), new(TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres),
            "disorder.injured"));
        SetNeed(victimId, item => item with { Stage = MedicalStage.Collapsed, Intent = MedicalIntent.Collapsed,
            CollapseTick = CurrentTick, Reason = "Generic serious confrontation injury; physical first aid needed" });
        if (securityLoses)
            SetStewardResponse(GetStewardResponses().Single(item => item.WorkerId == opponentId) with {
                Incapacitated = true, Stage = SecurityResponseStage.Failed, Description = "Steward incapacitated; medical help needed" });
        else if (securityFight)
            SetStewardResponse(GetStewardResponses().Single(item => item.WorkerId == opponentId) with {
                Stage = SecurityResponseStage.Completed, TargetId = null, Description = "Confrontation ended; injured guest needs medical help" });
        if (!IsSteward(victimId))
            SetDisorderPerson(victimId, item => item with { Stage = DisorderStage.Injured,
                InjuryTick = CurrentTick, StageTick = CurrentTick, OpponentId = fighter.AgentId });
        foreach (var participant in new[] { fighter.AgentId, opponentId }.Where(id => id != victimId && !IsSteward(id)))
            SetDisorderPerson(participant, item => item with { Stage = DisorderStage.Resolved,
                Pressure = 0, CooldownUntilTick = CurrentTick + 800 });
        DisorderEvent("disorder:injury", fighter.AgentId, victimId, fighter.Pressure,
            $"Generic serious injury to person {victimId}; security {d.ResponseStage}; first aid needed before tick {CurrentTick + DisorderInjuryDeathTicks}.");
    }

    private void ApplyDisorderDeath(ulong victimId)
    {
        var p = _preparation!;
        var victim = p.People.Single(item => item.AgentId == victimId);
        var d = _disorder!;
        var origin = d.Incidents.Last(item => item.VictimId == victimId && item.InjuryTick >= 0);
        var cause = $"Confrontation injury to {victim.Name} ({victim.Role}); initiating person {origin.InitiatorId}, opponent {origin.OpponentId}, " +
            $"grievance {origin.Grievance}, pressure {origin.Pressure}/10000, argument tick {origin.ArgumentTick}, " +
            $"fight tick {origin.FightTick}, injury tick {origin.InjuryTick}, " +
            $"{StaffResponseCausalSummary()}.";
        _disorder = d with { Response = cause };
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
            d.Version != 1 || d.People is null || d.Evidence is null || d.Incidents is null ||
            d.People.Length != p.Tier * 20 ||
            !d.People.Select(item => item.AgentId).SequenceEqual(p.People.Where(item => item.Role == ProtectedPersonRole.Guest).Select(item => item.AgentId)) ||
            !p.People.Any(item => item.AgentId == d.SecurityId && item.Name == "Jordan Hale" && item.Role == ProtectedPersonRole.Staff) ||
            !medical.Needs.Any(item => item.AgentId == d.SecurityId && item.Profile == MedicalNeedProfile.Staff) ||
            d.CalmingSkill is < 3_500 or > 8_000 || d.ConfrontationSkill is < 3_500 or > 8_000 ||
            !Enum.IsDefined(d.ResponseStage) || string.IsNullOrWhiteSpace(d.Response) ||
            d.ResponseStartedTick > s.CurrentTick ||
            d.ResponseStage is SecurityResponseStage.Travelling or SecurityResponseStage.Calming or SecurityResponseStage.Confronting &&
                (d.ResponseTargetId is null || !d.People.Any(item => item.AgentId == d.ResponseTargetId)) ||
            d.SecurityIncapacitated != (medical.Needs.Single(item => item.AgentId == d.SecurityId).Stage == MedicalStage.Collapsed) ||
            d.WaterClosed && (medical.WaterQueue.Length > 0 || medical.WaterOverflow.Length > 0 || medical.WaterOwnerId is not null ||
                medical.ExtraWaterPoints.Any(point => point.Queue.Length > 0 || point.Overflow.Length > 0 || point.OwnerId is not null)) ||
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
                (p.Status != PreparationStatus.Failed || s.Lifecycle?.Casualties.Length != 1))
            return "Disorder pressure, response ownership, evidence or protected-person state invalid.";
        return null;
    }
}

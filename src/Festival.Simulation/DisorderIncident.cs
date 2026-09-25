using System.Text.Json;

namespace Festival.Simulation;

public enum DisorderGrievance { None, MusicCutoff, WaterWait }
public enum DisorderStage { Calm, Complaint, Agitated, Argument, Fight, Injured, Resolved }
public enum SecurityResponseStage { None, Travelling, Calming, Confronting, Completed, Failed }
public enum DisorderAction { DispatchSecurity, SafeEgress, CloseWater, ReopenWater, RestoreMusic }
public sealed record DisorderCommand(DisorderAction Action, ulong? PersonId = null) : SessionCommand;
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
    public const int DisorderInjuryDeathTicks = 2_400;
    // Open-sided visual post sits just south of this walkable duty position;
    // neither the post nor the approach closes the gate/medical corridor.
    public static readonly GridCell DisorderSecurityPostCell = new(114, 178); // (-6.75, 25.25) m.
    public static readonly GridCell DisorderSecurityBaseCell = new(114, 182); // (-6.75, 27.25) m.
    private DisorderSnapshot? _disorder;
    public DisorderSnapshot? CaptureDisorder() => _disorder is null ? null :
        JsonSerializer.Deserialize<DisorderSnapshot>(JsonSerializer.Serialize(_disorder));
    internal string? DisorderCanonicalJson => _disorder is null ? null : JsonSerializer.Serialize(_disorder);

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

    private CommandResult? ValidateDisorderCommand(EntityId? target, DisorderCommand command)
    {
        if (target is not null || _disorder is not { } d || _preparation?.Status != PreparationStatus.Running ||
            !Enum.IsDefined(command.Action))
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Disorder actions require a live edition.");
        if (command.Action is DisorderAction.DispatchSecurity or DisorderAction.SafeEgress)
        {
            if (command.PersonId is not { } id || !d.People.Any(item => item.AgentId == id))
                return CommandResult.Rejected(CommandReasonCode.UnknownTarget, "Select an affected guest.");
            var person = d.People.Single(item => item.AgentId == id);
            if (command.Action == DisorderAction.DispatchSecurity)
            {
                if (person.Stage is not (DisorderStage.Complaint or DisorderStage.Agitated or DisorderStage.Argument or DisorderStage.Fight))
                    return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "No active disturbance to attend.");
                if (d.SecurityIncapacitated || !_preparation.People.Single(item => item.AgentId == d.SecurityId).Admitted)
                    return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Security is not physically available.");
                if (d.ResponseStage is SecurityResponseStage.Travelling or SecurityResponseStage.Calming or SecurityResponseStage.Confronting)
                    return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Security is already responding; this request is backlogged.");
                if (MedicalResponseCell(d.SecurityId, id) is null)
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

    private void ApplyDisorderCommand(DisorderCommand command)
    {
        var d = _disorder!;
        if (command.Action == DisorderAction.DispatchSecurity)
        {
            var id = command.PersonId!.Value;
            ApplyAgentDestination(new(d.SecurityId), new(MedicalResponseCell(d.SecurityId, id)!.Value, "disorder.security-dispatch"));
            _disorder = d with { ResponseStage = SecurityResponseStage.Travelling, ResponseTargetId = id,
                ResponseStartedTick = CurrentTick, Response = $"Security walking to person {id}; not yet calming" };
            DisorderEvent("security:dispatch", id, d.SecurityId, d.People.Single(item => item.AgentId == id).Pressure,
                _disorder.Response);
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
            foreach (var id in _medical!.WaterQueue.Concat(_medical.WaterOverflow)
                         .Concat(_medical.Needs.Where(item => item.Intent == MedicalIntent.SeekWater).Select(item => item.AgentId))
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
        !IsPaused && _disorder is { SecurityIncapacitated: true } injured &&
        _medical!.Needs.Single(need => need.AgentId == injured.SecurityId) is { Stage: MedicalStage.Collapsed } securityNeed &&
        CurrentTick + 1 >= securityNeed.CollapseTick + DisorderInjuryDeathTicks;

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
        if (d.SecurityIncapacitated)
        {
            var securityNeed = _medical!.Needs.Single(item => item.AgentId == d.SecurityId);
            if (securityNeed.Stage == MedicalStage.Treated)
            {
                _disorder = d = d with { SecurityIncapacitated = false, ResponseStage = SecurityResponseStage.Completed,
                    Response = "Security treated after confrontation" };
                DisorderEvent("security:treated", d.SecurityId, d.ResponseTargetId, 0, d.Response);
            }
            else if (CurrentTick >= securityNeed.CollapseTick + DisorderInjuryDeathTicks)
            {
                ApplyDisorderDeath(d.SecurityId);
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
            var inWaterLine = !d.WaterClosed && (_medical.WaterQueue.Contains(person.AgentId) ||
                _medical.WaterOverflow.Contains(person.AgentId));
            var joined = inWaterLine ? person.QueueJoinedTick < 0 ? CurrentTick : person.QueueJoinedTick : -1;
            var need = _medical.Needs.Single(item => item.AgentId == person.AgentId);
            var listener = _livePerformance?.Listeners.SingleOrDefault(item => item.AgentId == person.AgentId);
            var grievance = _livePerformance?.Stage == LiveSetStage.Interrupted && listener is { AtPlace: true, Enthusiasm: >= 65 }
                ? DisorderGrievance.MusicCutoff
                : inWaterLine && need.Thirst >= 6_000 && CurrentTick - joined >= person.QueueToleranceTicks
                    ? DisorderGrievance.WaterWait : DisorderGrievance.None;
            var pressure = person.Pressure;
            if (grievance != DisorderGrievance.None && CurrentTick >= person.CooldownUntilTick)
            {
                var rate = grievance == DisorderGrievance.MusicCutoff
                    ? 2 + (listener?.Enthusiasm ?? 0) / 100 + person.Temperament / 2_500
                    : 2 + need.Thirst / 3_500 + person.Temperament / 2_500;
                // A sustained strongest grievance reaches fight eligibility no
                // earlier than 1,600 ticks (20 real seconds), without a timer gate.
                rate = Math.Min(5, rate);
                pressure = Math.Min(10_000, pressure + rate * 8);
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
            if (stage != person.Stage)
            {
                var label = stage switch
                {
                    DisorderStage.Complaint => grievance == DisorderGrievance.WaterWait ? "Hurry up!" : "Why did the music stop?",
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
        AdvanceSecurityResponse();
        foreach (var fighter in _disorder!.People.Where(item => item.Stage == DisorderStage.Fight &&
                     _disorder.Incidents.Any(origin => origin.InitiatorId == item.AgentId &&
                         origin.FightTick == item.StageTick && origin.InjuryTick == -1) &&
                     CurrentTick >= item.StageTick + DisorderConfrontationTicks).ToArray())
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
        if (opponentId != d.SecurityId)
            SetDisorderPerson(opponentId, item => item with { Stage = DisorderStage.Fight,
                OpponentId = initiatorId, StageTick = CurrentTick });
        foreach (var id in new[] { initiatorId, opponentId })
        {
            if (id == d.SecurityId) continue;
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
            .Where(item => (long)(item.Nav.XMillimetres - actor.XMillimetres) * (item.Nav.XMillimetres - actor.XMillimetres) +
                (long)(item.Nav.ZMillimetres - actor.ZMillimetres) * (item.Nav.ZMillimetres - actor.ZMillimetres) <= 9_000_000)
            .OrderBy(item => item.AgentId).Select(item => (ulong?)item.AgentId).FirstOrDefault();
    }

    private void AdvanceSecurityResponse()
    {
        var d = _disorder!;
        if (d.ResponseStage == SecurityResponseStage.Completed && !d.SecurityIncapacitated &&
            _navigationAgents[new(d.SecurityId)].Destination != DisorderSecurityBaseCell)
            ApplyAgentDestination(new(d.SecurityId), new(DisorderSecurityBaseCell, "disorder.return-to-post"));
        if (d.ResponseTargetId is not { } targetId || d.SecurityIncapacitated) return;
        var target = d.People.Single(item => item.AgentId == targetId);
        var targetNeed = _medical!.Needs.Single(item => item.AgentId == targetId);
        if (target.Stage == DisorderStage.Injured || targetNeed.Stage is MedicalStage.Collapsed or MedicalStage.Critical)
        {
            _disorder = d with { ResponseStage = SecurityResponseStage.Completed, ResponseTargetId = null,
                Response = "Security response ended; injured person is now owned by medical response" };
            DisorderEvent("security:medical-handoff", targetId, d.SecurityId, target.Pressure, _disorder.Response);
            return;
        }
        if (target.Stage == DisorderStage.Fight &&
            (d.ResponseStage is SecurityResponseStage.Travelling or SecurityResponseStage.Calming ||
             d.ResponseStage == SecurityResponseStage.Confronting && target.OpponentId != d.SecurityId))
        {
            _disorder = d with { ResponseStage = SecurityResponseStage.Completed, ResponseTargetId = null,
                Response = "Target entered a separate confrontation before security could calm them" };
            DisorderEvent("security:too-late", targetId, d.SecurityId, target.Pressure, _disorder.Response);
            return;
        }
        if (d.ResponseStage is SecurityResponseStage.Travelling or SecurityResponseStage.Calming or SecurityResponseStage.Confronting &&
            (target.Grievance == DisorderGrievance.None || target.Stage is DisorderStage.Resolved or DisorderStage.Calm))
        {
            _disorder = d with { ResponseStage = SecurityResponseStage.Completed, ResponseTargetId = null,
                Response = "Grievance resolved before further confrontation" };
            DisorderEvent("security:stand-down", targetId, d.SecurityId, target.Pressure, _disorder.Response);
            return;
        }
        if (d.ResponseStage == SecurityResponseStage.Travelling)
        {
            var security = _navigationAgents[new(d.SecurityId)];
            var patient = _navigationAgents[new(targetId)];
            var dx = (long)security.XMillimetres - patient.XMillimetres;
            var dz = (long)security.ZMillimetres - patient.ZMillimetres;
            if (security.Action == AgentNavigationAction.Arrived && dx * dx + dz * dz <= 6_250_000)
            {
                _disorder = d with { ResponseStage = SecurityResponseStage.Calming,
                    ResponseStartedTick = CurrentTick, Response = "Security arrived; calming attempt underway" };
                DisorderEvent("security:calming", targetId, d.SecurityId, target.Pressure, _disorder.Response);
            }
            else if (CurrentTick % 80 == 0 && MedicalResponseCell(d.SecurityId, targetId) is { } cell && security.Destination != cell)
                ApplyAgentDestination(new(d.SecurityId), new(cell, "disorder.security-retarget"));
            return;
        }
        d = _disorder!;
        if (d.ResponseStage == SecurityResponseStage.Calming && CurrentTick >= d.ResponseStartedTick + DisorderCalmingTicks)
        {
            if (d.CalmingSkill + NextRandom(RandomStreamId.Incidents) % 2_001 >= target.Pressure + target.Temperament / 4)
            {
                SetDisorderPerson(targetId, item => item with { Stage = DisorderStage.Resolved, Pressure = 0,
                    StageTick = CurrentTick, CooldownUntilTick = CurrentTick + 800 });
                _disorder = _disorder! with { ResponseStage = SecurityResponseStage.Completed,
                    ResponseTargetId = null, Response = "Security calmed the argument after arriving" };
                DisorderEvent("security:calmed", targetId, d.SecurityId, target.Pressure, _disorder.Response);
            }
            else
            {
                SetDisorderPerson(targetId, item => item with { Stage = DisorderStage.Argument,
                    Pressure = Math.Max(item.Pressure, DisorderArgumentPressure), StageTick = CurrentTick,
                    OpponentId = d.SecurityId });
                _disorder = _disorder! with { ResponseStage = SecurityResponseStage.Confronting,
                    ResponseStartedTick = CurrentTick, Response = "Calming failed; aggression redirected toward security" };
                DisorderEvent("security:calm-failed", targetId, d.SecurityId, target.Pressure, _disorder.Response);
            }
        }
        d = _disorder!;
        if (d.ResponseStage == SecurityResponseStage.Confronting && d.ResponseTargetId is { } aggressorId)
        {
            var aggressor = d.People.Single(item => item.AgentId == aggressorId);
            var person = _preparation!.People.Single(item => item.AgentId == aggressorId);
            if (aggressor.Stage == DisorderStage.Argument && aggressor.OpponentId == d.SecurityId &&
                person.Admitted && !person.Departed &&
                _medical!.Needs.Single(item => item.AgentId == aggressorId).Stage is not (MedicalStage.Collapsed or MedicalStage.Critical or MedicalStage.Treated) &&
                aggressor.Pressure >= DisorderFightEligiblePressure &&
                CurrentTick >= d.ResponseStartedTick + DisorderConfrontationTicks)
            {
                BeginDisorderFight(aggressorId, d.SecurityId, "security:confrontation");
            }
            else if (aggressor.Stage is not (DisorderStage.Argument or DisorderStage.Fight) ||
                     aggressor.Stage == DisorderStage.Argument && aggressor.OpponentId != d.SecurityId)
            {
                _disorder = d with { ResponseStage = SecurityResponseStage.Completed, ResponseTargetId = null,
                    Response = "Security confrontation ownership ended; target is no longer in its eligible argument" };
                DisorderEvent("security:stand-down", aggressorId, d.SecurityId, aggressor.Pressure, _disorder.Response);
            }
        }
    }

    private void ResolveDisorderFight(DisorderPerson fighter)
    {
        if (fighter.OpponentId is not { } opponentId) return;
        var d = _disorder!;
        var origin = d.Incidents.Last(item => item.InitiatorId == fighter.AgentId &&
            item.FightTick == fighter.StageTick && item.InjuryTick == -1);
        var securityFight = opponentId == d.SecurityId;
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
        var securityLoses = securityFight && d.ConfrontationSkill + NextRandom(RandomStreamId.Incidents) % 2_001 <
            fighter.Pressure + fighter.Temperament / 4;
        var victimId = securityLoses ? d.SecurityId : securityFight ? fighter.AgentId :
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
            _disorder = _disorder! with { SecurityIncapacitated = true, ResponseStage = SecurityResponseStage.Failed,
                Response = "Security worker incapacitated; medical help needed" };
        else if (securityFight)
            _disorder = _disorder! with { ResponseStage = SecurityResponseStage.Completed, ResponseTargetId = null,
                Response = "Confrontation ended; injured guest needs medical help" };
        if (victimId != d.SecurityId)
            SetDisorderPerson(victimId, item => item with { Stage = DisorderStage.Injured,
                InjuryTick = CurrentTick, StageTick = CurrentTick, OpponentId = fighter.AgentId });
        foreach (var participant in new[] { fighter.AgentId, opponentId }.Where(id => id != victimId && id != d.SecurityId))
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
            $"response {d.ResponseStage}/{d.Response}; medic {_medical!.ResponseStage}/{_medical.Response}.";
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
        _preparation = p with { Status = PreparationStatus.Failed, Rentals = [], WorkContracts = [] };
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
            d.WaterClosed && (medical.WaterQueue.Length > 0 || medical.WaterOverflow.Length > 0 || medical.WaterOwnerId is not null) ||
            d.People.Any(item => item is null || item.Temperament is < 2_000 or > 8_000 ||
                item.QueueToleranceTicks is < 320 or > 1_120 || item.Pressure is < 0 or > 10_000 ||
                !Enum.IsDefined(item.Grievance) || !Enum.IsDefined(item.Stage) ||
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

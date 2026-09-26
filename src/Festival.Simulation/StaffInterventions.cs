namespace Festival.Simulation;

public enum StaffInterventionAction { GuideToWater, LeaveWaterQueue, GuideToRest, EscortOut }
public enum StaffInterventionStage { Travelling, Guiding, Escorting, Completed, Failed }
public sealed record StaffInterventionCommand(ulong GuestId, ulong WorkerId, StaffInterventionAction Action) : SessionCommand;
// Isolated, explicitly enabled regression/capture fixture. No normal UI enables its gate.
public sealed record DevelopmentMedicalFixtureCommand(ulong GuestId, MedicalAction Action) : SessionCommand;
public sealed record DevelopmentDisorderEgressFixtureCommand(ulong GuestId) : SessionCommand;
public sealed record StaffInterventionJob(ulong WorkerId, ulong GuestId, StaffInterventionAction Action,
    StaffInterventionStage Stage, long DispatchedTick, long StartedTick, long EndedTick,
    long LastReviewTick, GridCell? Waypoint, string Description);

public sealed partial class GameSession
{
    private const int InterventionReviewTicks = 80;
    private const int InterventionMaximumTicks = 9600;
    public IReadOnlyList<StaffInterventionJob> CaptureStaffInterventions() => _medical?.StaffInterventions.ToArray() ?? [];
    private static bool InterventionBusy(StaffInterventionJob job) => job.Stage is StaffInterventionStage.Travelling or StaffInterventionStage.Guiding or StaffInterventionStage.Escorting;
    private bool InterventionOwnsWorker(ulong id) => CaptureStaffInterventions().Any(job => InterventionBusy(job) && job.WorkerId == id);
    private bool InterventionOwnsTarget(ulong id) => CaptureStaffInterventions().Any(job => InterventionBusy(job) && job.GuestId == id);
    private bool PersonCollapsed(ulong id) => _medical is { } m &&
        (m.Needs.Any(need => need.AgentId == id && need.Stage is MedicalStage.Collapsed or MedicalStage.Critical or MedicalStage.Terminal) ||
         id == m.AtRiskGuestId && m.Stage is MedicalStage.Collapsed or MedicalStage.Critical or MedicalStage.Terminal);
    private bool MovementOccupant(ulong id) => !PersonCollapsed(id) && _preparation?.People.Any(person => person.AgentId == id && person.Departed) != true;
    private void SetIntervention(StaffInterventionJob job) => _medical = _medical! with
    {
        StaffInterventions = _medical.StaffInterventions.Where(item => item.WorkerId != job.WorkerId).Append(job).OrderBy(item => item.WorkerId).ToArray()
    };
    private StaffInterventionCommand LegacyMedicalIntervention(MedicalCommand command) => new(command.GuestId,
        command.WorkerId ?? (command.Action is MedicalAction.GuideToWater or MedicalAction.ReturnToShow ? _disorder?.SecurityId ?? 0 : _medical?.MedicId ?? 0),
        command.Action switch { MedicalAction.GuideToWater => StaffInterventionAction.GuideToWater,
            MedicalAction.ReturnToShow => StaffInterventionAction.LeaveWaterQueue, MedicalAction.GuideToRest => StaffInterventionAction.GuideToRest,
            _ => StaffInterventionAction.EscortOut });
    private CommandResult? ValidateStaffIntervention(EntityId? target, StaffInterventionCommand command)
    {
        if (target is not null || _preparation is not { Status: PreparationStatus.Running } p || _medical is not { } m || !Enum.IsDefined(command.Action))
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Staff interventions require a live edition.");
        var need = m.Needs.SingleOrDefault(item => item.AgentId == command.GuestId);
        var worker = GetResponseStaff().SingleOrDefault(item => item.AgentId == command.WorkerId);
        if (need is null || worker is null || command.GuestId == command.WorkerId)
            return CommandResult.Rejected(CommandReasonCode.UnknownTarget, "Choose an affected person and a contracted responder.");
        if (!p.People.Any(item => item.AgentId == command.GuestId && item.Admitted && !item.Departed) ||
            !p.People.Any(item => item.AgentId == command.WorkerId && item.Admitted && !item.Departed) || PersonCollapsed(command.WorkerId))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Both people must be physically on site and the worker available.");
        if (InterventionOwnsWorker(command.WorkerId) || InterventionOwnsTarget(command.GuestId) || InterventionOwnsTarget(command.WorkerId) || InterventionOwnsWorker(command.GuestId) ||
            GetMedicResponses().Any(job => MedicBusy(job) && (job.WorkerId == command.WorkerId || job.WorkerId == command.GuestId || job.PatientId == command.GuestId || job.PatientId == command.WorkerId)) ||
            GetStewardResponses().Any(job => StewardBusy(job) && (job.WorkerId == command.WorkerId || job.WorkerId == command.GuestId || job.TargetId == command.GuestId || job.TargetId == command.WorkerId)))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "This worker or target already owns an independent response.");
        if (PersonCollapsed(command.GuestId) || need.Stage == MedicalStage.Removed || m.Stage == MedicalStage.Terminal ||
            _disorder?.People.Any(item => item.AgentId == command.GuestId && item.Stage is DisorderStage.Fight or DisorderStage.Injured) == true)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Resolve active confrontation or give physical first aid before guidance or escort.");
        if ((command.Action is StaffInterventionAction.GuideToWater or StaffInterventionAction.LeaveWaterQueue) && worker.Role != ResponseRole.Steward ||
            command.Action == StaffInterventionAction.GuideToRest && worker.Role != ResponseRole.Medic)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Water/queue guidance needs a steward; first-aid rest guidance needs a medic.");
        if (command.Action == StaffInterventionAction.GuideToWater && (_disorder?.WaterClosed == true ||
            !MedicalRouteExists(command.GuestId, WaterApproach(ChooseWaterPoint(command.GuestId)))))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Free water is closed or unreachable.");
        if (command.Action == StaffInterventionAction.LeaveWaterQueue && need.Intent is not (MedicalIntent.SeekWater or MedicalIntent.Drinking))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Only an actual water visitor can be asked to leave its queue.");
        if (command.Action == StaffInterventionAction.GuideToRest && command.GuestId != m.AtRiskGuestId && need.Profile != MedicalNeedProfile.Performer)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "This bounded rest point supports the at-risk guest and performers.");
        if (command.Action == StaffInterventionAction.GuideToRest && !MedicalRouteExists(command.GuestId, MedicalRestCell) ||
            command.Action == StaffInterventionAction.EscortOut && !MedicalRouteExists(command.GuestId, MedicalExitCell))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "The person's destination is not reachable.");
        if (command.Action == StaffInterventionAction.EscortOut && need.Stage != MedicalStage.Distress &&
            !(command.GuestId == m.AtRiskGuestId && m.Stage == MedicalStage.Distress) &&
            _disorder?.People.Any(item => item.AgentId == command.GuestId && item.Stage is DisorderStage.Complaint or DisorderStage.Agitated or DisorderStage.Argument) != true)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Escort is an affected-person intervention, not a no-warning festival closure.");
        if (MedicalResponseCell(command.WorkerId, command.GuestId) is null)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "The worker cannot physically reach this person.");
        return null;
    }
    private void ApplyStaffIntervention(StaffInterventionCommand command)
    {
        if (IsSteward(command.WorkerId))
        {
            LeaveWater(command.WorkerId, "Steward recalled for a physical intervention", reroute: false);
            SetNeed(command.WorkerId, need => need with { Intent = MedicalIntent.WatchShow, QueueSlot = null, Reason = "Named physical intervention" });
        }
        ApplyAgentDestination(new(command.WorkerId), new(MedicalResponseCell(command.WorkerId, command.GuestId)!.Value, "staff.intervention-approach"));
        SetIntervention(new(command.WorkerId, command.GuestId, command.Action, StaffInterventionStage.Travelling,
            CurrentTick, -1, -1, CurrentTick, null, "Walking to the person; no remote guidance or safety effect"));
        MedicalEvent("staff:intervention-dispatch", $"Worker {command.WorkerId} assigned {command.Action} to person {command.GuestId}; physical arrival required.");
    }
    private bool InterventionProximity(ulong workerId, ulong guestId)
    {
        var worker = _navigationAgents[new(workerId)]; var guest = _navigationAgents[new(guestId)];
        var dx = (long)worker.XMillimetres - guest.XMillimetres; var dz = (long)worker.ZMillimetres - guest.ZMillimetres;
        return worker.Action == AgentNavigationAction.Arrived && dx * dx + dz * dz <= 6_250_000;
    }
    private GridCell? CompanionCell(ulong workerId, GridCell destination)
    {
        foreach (var offset in new GridCell[] { new(2, 0), new(-2, 0), new(0, 2), new(0, -2), new(2, 2), new(-2, 2), new(2, -2), new(-2, -2) })
        {
            var cell = new GridCell(destination.X + offset.X, destination.Z + offset.Z);
            if (!_traversalGrid!.Get(cell).IsWalkable ||
                CaptureStaffInterventions().Any(job => InterventionBusy(job) && job.WorkerId != workerId && _navigationAgents[new(job.WorkerId)].Destination == cell) ||
                !MedicalRouteExists(workerId, cell)) continue;
            return cell;
        }
        return null;
    }
    private bool StartEscortLeg(StaffInterventionJob job)
    {
        var guest = _navigationAgents[new(job.GuestId)];
        var origin = TraversalGrid.WorldToCell(guest.XMillimetres, guest.ZMillimetres);
        var path = DeterministicPathfinder.FindPath(_traversalGrid!, origin, MedicalExitCell);
        if (!path.Found) return false;
        var waypoint = path.Path[Math.Min(path.Path.Count - 1, 8)];
        var companion = CompanionCell(job.WorkerId, waypoint);
        if (companion is null) return false;
        ApplyAgentDestination(new(job.GuestId), new(waypoint, "staff.escorted-exit"));
        ApplyAgentDestination(new(job.WorkerId), new(companion.Value, "staff.escort-companion"));
        SetIntervention(job with { Stage = StaffInterventionStage.Escorting, Waypoint = waypoint, LastReviewTick = CurrentTick,
            Description = "Walking together through bounded waypoints; no safety until both reach the gate" });
        return true;
    }
    private void EndIntervention(StaffInterventionJob job, bool completed, string description)
    {
        SetIntervention(job with { Stage = completed ? StaffInterventionStage.Completed : StaffInterventionStage.Failed, EndedTick = CurrentTick, Description = description });
        MedicalEvent(completed ? "staff:intervention-complete" : "staff:intervention-failed", $"Worker {job.WorkerId}, person {job.GuestId}: {description}");
        if (_preparation?.Status == PreparationStatus.Running && !PersonCollapsed(job.WorkerId))
            ApplyAgentDestination(new(job.WorkerId), new(StaffDutyCell(job.WorkerId, GetResponseStaff().Single(item => item.AgentId == job.WorkerId).Role), "staff.intervention-return"));
        if (!completed && job.Action == StaffInterventionAction.EscortOut &&
            _medical!.Needs.Single(need => need.AgentId == job.GuestId).Intent == MedicalIntent.Leaving && !PersonCollapsed(job.GuestId))
        {
            SetNeed(job.GuestId, need => need with { Intent = MedicalIntent.WatchShow, Reason = description });
            ReturnToListening(job.GuestId);
        }
    }
    private void AdvanceStaffInterventions()
    {
        if (_medical is null || _preparation is not { Status: PreparationStatus.Running }) return;
        foreach (var original in CaptureStaffInterventions().Where(InterventionBusy))
        {
            var job = original;
            if (PersonCollapsed(job.GuestId) || PersonCollapsed(job.WorkerId) ||
                _disorder?.People.Any(person => (person.AgentId == job.GuestId || person.AgentId == job.WorkerId) && person.Stage is DisorderStage.Fight or DisorderStage.Injured) == true)
            { EndIntervention(job, false, "Stopped for actual collapse/confrontation; physical first aid or resolution required"); continue; }
            if (CurrentTick >= job.DispatchedTick + InterventionMaximumTicks ||
                _navigationAgents[new(job.WorkerId)].Action == AgentNavigationAction.NoRoute ||
                job.Stage == StaffInterventionStage.Escorting && _navigationAgents[new(job.GuestId)].Action == AgentNavigationAction.NoRoute)
            { EndIntervention(job, false, "Bounded route failed or timed out; clear the path and redispatch, no assisted teleport"); continue; }
            if (job.Stage == StaffInterventionStage.Travelling)
            {
                if (!InterventionProximity(job.WorkerId, job.GuestId))
                {
                    if (CurrentTick - job.LastReviewTick >= InterventionReviewTicks)
                    {
                        var cell = MedicalResponseCell(job.WorkerId, job.GuestId);
                        if (cell is null) { EndIntervention(job, false, "Moving target became unreachable"); continue; }
                        if (_navigationAgents[new(job.WorkerId)].Destination != cell) ApplyAgentDestination(new(job.WorkerId), new(cell.Value, "staff.intervention-approach"));
                        SetIntervention(job with { LastReviewTick = CurrentTick });
                    }
                    continue;
                }
                if (job.Action == StaffInterventionAction.GuideToWater && (_disorder?.WaterClosed == true ||
                    !MedicalRouteExists(job.GuestId, WaterApproach(ChooseWaterPoint(job.GuestId)))))
                { EndIntervention(job, false, "Water closed or became unreachable before physical arrival; no substitute rest guidance"); continue; }
                if (job.Action == StaffInterventionAction.GuideToRest && !MedicalRouteExists(job.GuestId, MedicalRestCell))
                { EndIntervention(job, false, "Rest became unreachable before physical arrival; no remote reroute"); continue; }
                job = job with { StartedTick = CurrentTick, Stage = StaffInterventionStage.Guiding, Description = "Physically reached the person; guidance underway" };
                SetIntervention(job);
                MedicalEvent("staff:intervention-arrival", $"Worker {job.WorkerId} physically reached person {job.GuestId} before {job.Action}.");
                if (job.Action == StaffInterventionAction.EscortOut)
                {
                    LeaveWater(job.GuestId, "Staff arrived to escort this affected person", reroute: false);
                    MedicalRelinquishPerformerStage(job.GuestId);
                    SetNeed(job.GuestId, need => need with { Intent = MedicalIntent.Leaving, QueueSlot = null, Reason = "Physically escorted exit; still exposed until the gate" });
                    if (!StartEscortLeg(job)) EndIntervention(job, false, "No reachable escorted leg; no remote removal");
                    continue;
                }
                if (job.Action == StaffInterventionAction.GuideToWater) SeekWater(job.GuestId, "A named steward physically arrived and guided this person to water");
                else if (job.Action == StaffInterventionAction.LeaveWaterQueue)
                {
                    if (_medical!.Needs.Single(need => need.AgentId == job.GuestId).Intent is not (MedicalIntent.SeekWater or MedicalIntent.Drinking))
                    { EndIntervention(job, true, "Person already left the water queue before the worker arrived; no remote action was applied"); continue; }
                    LeaveWater(job.GuestId, "A named steward physically arrived and asked this person to leave the queue");
                }
                else
                {
                    LeaveWater(job.GuestId, "Medic physically arrived to guide first-aid rest", reroute: false);
                    SetNeed(job.GuestId, need => need with { Intent = MedicalIntent.Rest, QueueSlot = null, Reason = "A named medic physically guided first-aid rest" });
                    MedicalRelinquishPerformerStage(job.GuestId);
                    ApplyAgentDestination(new(job.GuestId), new(MedicalRestCell, "medical.rest"));
                }
                continue;
            }
            if (job.Stage == StaffInterventionStage.Guiding && CurrentTick >= job.StartedTick + 4)
            { EndIntervention(job, true, "Physical guidance delivered; ordinary destination/service remains physical"); continue; }
            if (job.Stage != StaffInterventionStage.Escorting || _navigationAgents[new(job.GuestId)].Action != AgentNavigationAction.Arrived ||
                _navigationAgents[new(job.WorkerId)].Action != AgentNavigationAction.Arrived || !InterventionProximity(job.WorkerId, job.GuestId)) continue;
            if (job.Waypoint != MedicalExitCell)
            {
                if (!StartEscortLeg(job)) EndIntervention(job, false, "Next escorted leg blocked; person remains on site");
                continue;
            }
            SetNeed(job.GuestId, need => need with { Stage = MedicalStage.Removed, Reason = "Named escort physically completed at the gate" });
            if (job.GuestId == _medical.AtRiskGuestId) _medical = _medical with { Stage = MedicalStage.Removed };
            _preparation = _preparation! with { People = _preparation.People.Select(person => person.AgentId == job.GuestId ? person with { Departed = true } : person).ToArray() };
            if (_disorder?.People.Any(person => person.AgentId == job.GuestId) == true)
                SetDisorderPerson(job.GuestId, person => person with { Stage = DisorderStage.Resolved, Pressure = 0, OpponentId = null, Grievance = DisorderGrievance.None });
            EndIntervention(job, true, "Both people physically reached the gate; only this affected person departed");
        }
    }
    private void ReleaseInterventionsForBoundary(string reason)
    {
        if (_medical is null) return;
        _medical = _medical with { StaffInterventions = _medical.StaffInterventions.Select(job => InterventionBusy(job)
            ? job with { Stage = StaffInterventionStage.Failed, EndedTick = CurrentTick, Description = reason } : job).ToArray() };
    }

    private static string? ValidatePersistedInterventions(SessionPersistenceSnapshot snapshot)
    {
        if (snapshot.Medical is not { } m) return null;
        if (m.StaffInterventions is null || m.StaffInterventions.Any(job => job is null) || snapshot.Preparation is not { } p)
            return "Staff intervention collection missing.";
        var jobs = m.StaffInterventions;
        var medicJobs = new[] { new MedicResponse(m.MedicId, m.ResponseStage, m.ResponsePatientId, m.ResponseStartedTick, m.Response, m.ResponseDispatchedTick) }.Concat(m.ExtraResponses ?? []).ToArray();
        var stewardJobs = snapshot.Disorder is not { } d ? [] : new[] { new StewardResponse(d.SecurityId, d.ResponseStage, d.ResponseTargetId, d.ResponseStartedTick, d.SecurityIncapacitated, d.Response, d.ResponseDispatchedTick) }.Concat(d.ExtraResponses ?? []).ToArray();
        ResponseRole? Role(ulong id) => id == m.MedicId ? ResponseRole.Medic : id == snapshot.Disorder?.SecurityId ? ResponseRole.Steward :
            p.StaffProfiles.SingleOrDefault(profile => profile.AgentId == id && p.People.Any(person => person.AgentId == id))?.Role;
        if (jobs.Length > 4 || !jobs.Select(job => job.WorkerId).SequenceEqual(jobs.Select(job => job.WorkerId).Distinct().Order()) ||
            jobs.Any(job => !Enum.IsDefined(job.Action) || !Enum.IsDefined(job.Stage) || Role(job.WorkerId) is null || job.WorkerId == job.GuestId ||
                !m.Needs.Any(need => need.AgentId == job.GuestId) || string.IsNullOrWhiteSpace(job.Description) ||
                job.DispatchedTick < 0 || job.DispatchedTick > snapshot.CurrentTick || job.LastReviewTick < job.DispatchedTick || job.LastReviewTick > snapshot.CurrentTick ||
                job.StartedTick < -1 || job.StartedTick > snapshot.CurrentTick || job.StartedTick >= 0 && job.StartedTick < job.DispatchedTick ||
                job.EndedTick < -1 || job.EndedTick > snapshot.CurrentTick || job.EndedTick >= 0 && job.EndedTick < Math.Max(job.StartedTick, job.DispatchedTick) ||
                job.Stage == StaffInterventionStage.Travelling && (job.StartedTick != -1 || job.Waypoint is not null) ||
                job.Stage == StaffInterventionStage.Guiding && (job.Action == StaffInterventionAction.EscortOut || job.Waypoint is not null) ||
                job.Stage is StaffInterventionStage.Guiding or StaffInterventionStage.Escorting && job.StartedTick < 0 ||
                InterventionBusy(job) && job.EndedTick != -1 || !InterventionBusy(job) && job.EndedTick < 0 ||
                job.Stage == StaffInterventionStage.Escorting && (job.Action != StaffInterventionAction.EscortOut || job.Waypoint is null) ||
                job.Waypoint is { } waypoint && (waypoint.X is < 0 or >= TraversalGrid.Width || waypoint.Z is < 0 or >= TraversalGrid.Depth) ||
                job.Stage == StaffInterventionStage.Completed && job.Action == StaffInterventionAction.EscortOut &&
                    (!p.People.Any(person => person.AgentId == job.GuestId && person.Departed) ||
                     m.Needs.Single(need => need.AgentId == job.GuestId).Stage != MedicalStage.Removed || job.Waypoint != MedicalExitCell ||
                     snapshot.NavigationAgents?.SingleOrDefault(agent => agent.Id == job.GuestId) is not { Action: (int)AgentNavigationAction.Arrived } gate ||
                     gate.DestinationX != MedicalExitCell.X || gate.DestinationZ != MedicalExitCell.Z) ||
                job.Action is StaffInterventionAction.GuideToWater or StaffInterventionAction.LeaveWaterQueue && Role(job.WorkerId) != ResponseRole.Steward ||
                job.Action == StaffInterventionAction.GuideToRest && Role(job.WorkerId) != ResponseRole.Medic))
            return "Staff intervention role, ownership or clocks invalid.";
        var active = jobs.Where(InterventionBusy).ToArray();
        if (active.Select(job => job.GuestId).Distinct().Count() != active.Length ||
            active.Any(job => p.Status != PreparationStatus.Running ||
                !p.People.Any(person => person.AgentId == job.WorkerId && person.Admitted && !person.Departed) ||
                !p.People.Any(person => person.AgentId == job.GuestId && person.Admitted && !person.Departed) ||
                active.Any(other => other != job && (other.WorkerId == job.GuestId || other.GuestId == job.WorkerId)) ||
                medicJobs.Any(other => MedicBusy(other) && (other.WorkerId == job.WorkerId || other.WorkerId == job.GuestId || other.PatientId == job.WorkerId || other.PatientId == job.GuestId)) ||
                stewardJobs.Any(other => StewardBusy(other) && (other.WorkerId == job.WorkerId || other.WorkerId == job.GuestId || other.TargetId == job.WorkerId || other.TargetId == job.GuestId)) ||
                snapshot.NavigationAgents?.SingleOrDefault(agent => agent.Id == job.WorkerId) is not { } worker ||
                job.Stage == StaffInterventionStage.Travelling && worker.IntentId != "staff.intervention-approach" ||
                job.Stage == StaffInterventionStage.Escorting && (worker.IntentId != "staff.escort-companion" ||
                    snapshot.NavigationAgents?.SingleOrDefault(agent => agent.Id == job.GuestId) is not { IntentId: "staff.escorted-exit" } guest ||
                    guest.DestinationX != job.Waypoint!.Value.X || guest.DestinationZ != job.Waypoint.Value.Z ||
                    m.Needs.Single(need => need.AgentId == job.GuestId).Intent != MedicalIntent.Leaving)))
            return "Active staff interventions require admitted, exclusively owned physical routes.";
        return null;
    }
}

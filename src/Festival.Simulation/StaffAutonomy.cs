namespace Festival.Simulation;

public sealed partial class GameSession
{
    public bool StaffAutonomyEnabled => _preparation?.StaffAutonomyEnabled == true;

    // Availability is shared by automatic arbitration and role-based manual dispatch.
    // Completed jobs own no response; their ordinary return route can safely be replaced.
    private string? StaffUnavailableReason(ulong id)
    {
        if (_preparation?.People.SingleOrDefault(person => person.AgentId == id) is not { Admitted: true, Departed: false })
            return "Not physically on duty.";
        if (PersonCollapsed(id) || GetStewardResponses().Any(job => job.WorkerId == id && job.Incapacitated) || DisorderOwnsNavigation(id))
            return "Incapacitated or in an active confrontation.";
        if (InterventionOwnsWorker(id) || InterventionOwnsTarget(id) ||
            GetMedicResponses().Any(job => MedicBusy(job) && (job.WorkerId == id || job.PatientId == id)) ||
            GetStewardResponses().Any(job => StewardBusy(job) && (job.WorkerId == id || job.TargetId == id)))
            return "Already assigned; finish the current response or manual guidance.";
        if (MedicalOwnsNavigation(id) || ImmersionOwnsNavigation(id)) return "Busy with physical water, rest or departure.";
        return null;
    }

    private bool ResponseTargetClaimed(ulong id)
    {
        var opponent = _disorder?.People.SingleOrDefault(person => person.AgentId == id && person.Stage == DisorderStage.Fight)?.OpponentId;
        bool Claimed(ulong value) => InterventionOwnsWorker(value) || InterventionOwnsTarget(value) ||
            GetMedicResponses().Any(job => MedicBusy(job) && (job.WorkerId == value || job.PatientId == value)) ||
            GetStewardResponses().Any(job => StewardBusy(job) && (job.WorkerId == value || job.TargetId == value));
        return Claimed(id) || opponent is { } other && Claimed(other);
    }

    public SessionCommand? SelectRoleResponse(ResponseRole role, ulong targetId, out string? reason)
    {
        SessionCommand Command(ulong workerId) => role == ResponseRole.Medic
            ? new MedicalCommand(targetId, MedicalAction.DispatchMedic, workerId)
            : new DisorderCommand(DisorderAction.DispatchSecurity, targetId, workerId);
        var workers = GetResponseStaff().Where(worker => worker.Role == role).ToArray();
        if (workers.Length == 0) { reason = $"No contracted {role.ToString().ToLowerInvariant()}."; return null; }
        if (!_navigationAgents.TryGetValue(new(targetId), out var target))
        { reason = "The selected person is not physically on site."; return null; }
        long Distance(ulong id)
        {
            var worker = _navigationAgents[new(id)];
            var dx = (long)worker.XMillimetres - target.XMillimetres; var dz = (long)worker.ZMillimetres - target.ZMillimetres;
            return dx * dx + dz * dz;
        }
        var ordered = workers.OrderBy(worker => _navigationAgents.ContainsKey(new(worker.AgentId)) ? Distance(worker.AgentId) : long.MaxValue)
            .ThenBy(worker => worker.AgentId).ToArray();
        var issues = new List<string>();
        foreach (var worker in ordered)
        {
            var command = Command(worker.AgentId);
            var issue = role == ResponseRole.Medic ? ValidateMedicalCommand(null, (MedicalCommand)command) : ValidateDisorderCommand(null, (DisorderCommand)command);
            if (issue is null) { reason = null; return command; }
            issues.Add($"{worker.Name}: {issue.Message}");
        }
        reason = string.Join("\n", issues);
        return null;
    }

    private MedicalStage EffectiveMedicalStage(MedicalNeed need) => need.Stage is MedicalStage.Collapsed or MedicalStage.Critical
        ? need.Stage : need.AgentId == _medical!.AtRiskGuestId ? _medical.Stage : need.Stage;
    private long MedicalResponseDeadline(MedicalNeed need)
    {
        var collapse = need.AgentId == _medical!.AtRiskGuestId ? _medical.CollapseTick : need.CollapseTick;
        return _disorder?.Incidents.LastOrDefault(incident => incident.VictimId == need.AgentId && incident.InjuryTick >= 0) is { } injury
            ? injury.InjuryTick + DisorderInjuryDeathTicks : collapse + MedicalDeathDelayTicks;
    }

    private void AdvanceStaffAutonomy()
    {
        if (!StaffAutonomyEnabled || !MedicalOperationsActive || _medical is null) return;
        foreach (var need in _medical.Needs.Where(need => EffectiveMedicalStage(need) is MedicalStage.Critical or MedicalStage.Collapsed)
                     .OrderBy(need => EffectiveMedicalStage(need) == MedicalStage.Critical ? 0 : 1)
                     .ThenBy(MedicalResponseDeadline).ThenBy(need => need.AgentId).ToArray())
            if (!ResponseTargetClaimed(need.AgentId) && SelectRoleResponse(ResponseRole.Medic, need.AgentId, out _) is MedicalCommand command)
                ApplyMedicalCommand(command);
        if (_disorder is null || _preparation?.Status != PreparationStatus.Running) return;
        foreach (var person in _disorder.People.Where(person => person.Stage == DisorderStage.Fight && GuestFightOrigin(person.AgentId)?.HandlingAttempt is null ||
                         person.Stage == DisorderStage.Argument && person.Pressure >= DisorderFightEligiblePressure)
                     .OrderBy(person => person.Stage == DisorderStage.Fight ? 0 : 1).ThenBy(person => person.StageTick).ThenBy(person => person.AgentId).ToArray())
            if (!ResponseTargetClaimed(person.AgentId) && SelectRoleResponse(ResponseRole.Steward, person.AgentId, out _) is DisorderCommand command)
                ApplyDisorderCommand(command);
    }

    public string ResponseStaffStatus(ulong id)
    {
        if (PersonCollapsed(id) || GetStewardResponses().Any(job => job.WorkerId == id && job.Incapacitated)) return "Unavailable · incapacitated; first aid needed";
        var intervention = CaptureStaffInterventions().SingleOrDefault(job => job.WorkerId == id && InterventionBusy(job));
        if (intervention is not null) return intervention.Stage == StaffInterventionStage.Travelling ? "Responding · manual guidance" : "Handling · manual guidance";
        var medic = GetMedicResponses().SingleOrDefault(job => job.WorkerId == id);
        if (medic?.Stage == MedicalResponseStage.Treating) return "Treating";
        if (medic is not null && MedicBusy(medic)) return "Responding";
        var steward = GetStewardResponses().SingleOrDefault(job => job.WorkerId == id);
        if (steward?.Stage is SecurityResponseStage.Calming or SecurityResponseStage.Confronting) return "Handling";
        if (steward is not null && StewardBusy(steward)) return "Responding";
        return StaffUnavailableReason(id) is { } issue ? "Unavailable · " + issue : "Idle · available (safe return routes may be reassigned)";
    }
}

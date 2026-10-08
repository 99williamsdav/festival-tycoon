namespace Festival.Simulation;

public sealed partial class GameSession
{

    // Availability is shared by automatic arbitration and role-based manual dispatch.
    // Completed jobs own no response; their ordinary return route can safely be replaced.
    private string? StaffUnavailableReason(ulong id)
    {
        if (PersonIn(PersonView.Roster, id) is not { Admitted: true, Departed: false })
            return "Not physically on duty.";
        if (PersonCollapsed(id) || GetStewardResponses().Any(job => job.WorkerId == id && job.Incapacitated) || DisorderOwnsNavigation(id))
            return "Incapacitated or in an active confrontation.";
        if (HasClaim(id, PersonClaims.ResponseAssigned))
            return "Already assigned; finish the current response or manual guidance.";
        if (PersonalActivityInService(id)) return "Finishing a drink, purchase, toilet visit or rest.";
        if (FaultWorkOwns(id)) return CowWorkOwns(id) ? "Busy herding a cow." : "Busy with a repair or freeing someone from a toilet.";
        if (_persons[id].Intent is MedicalIntent.AwaitMedic or MedicalIntent.Leaving or MedicalIntent.Collapsed)
            return "Busy with medical care or departure.";
        return null;
    }


    public SessionCommand? SelectRoleResponse(ResponseRole role, ulong targetId, out string? reason) =>
        SelectRoleResponse(role, targetId, null, out reason);

    /// <summary>Nearest legal worker first; <paramref name="accepts"/> lets an automatic job be declined.</summary>
    private SessionCommand? SelectRoleResponse(ResponseRole role, ulong targetId, Func<ulong, bool>? accepts, out string? reason)
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
            if (issue is null && accepts?.Invoke(worker.AgentId) != false) { reason = null; return command; }
            issues.Add($"{worker.Name}: {issue?.Message ?? "declined for a more pressing need"}");
        }
        reason = string.Join("\n", issues);
        return null;
    }

    private MedicalStage EffectiveMedicalStage(Person need) => need.HealthStage;
    private long MedicalResponseDeadline(Person need)
    {
        var collapse = need.HealthCollapseTick;
        return _disorder?.Incidents.LastOrDefault(incident => incident.VictimId == need.Id && incident.InjuryTick >= 0) is { } injury
            ? injury.InjuryTick + DisorderInjuryDeathTicks : collapse + MedicalDeathDelayTicks;
    }

    private void AdvanceStaffAutonomy()
    {
        if (!MedicalOperationsActive || _medical is null) return;
        foreach (var need in PeopleIn(PersonView.Medical).Where(need => EffectiveMedicalStage(need) is MedicalStage.Critical or MedicalStage.Collapsed)
                     .OrderBy(need => EffectiveMedicalStage(need) == MedicalStage.Critical ? 0 : 1)
                     .ThenBy(MedicalResponseDeadline).ThenBy(need => need.Id).ToArray())
            if (!ResponseTargetClaimed(need.Id) && SelectRoleResponse(ResponseRole.Medic, need.Id,
                    worker => AcceptsAutomaticResponse(worker, need.Id,
                        EffectiveMedicalStage(need) == MedicalStage.Critical ? CriticalResponseValue : CollapsedResponseValue,
                        GetResponseStaff().Single(item => item.AgentId == worker).TreatmentTicks), out _) is MedicalCommand command)
                ApplyMedicalCommand(command);
        if (_disorder is null || _preparation?.Status != PreparationStatus.Running) return;
        foreach (var person in PeopleIn(PersonView.Disorder).Where(person => person.ConductStage == DisorderStage.Fight && GuestFightOrigin(person.Id)?.HandlingAttempt is null ||
                         person.ConductStage == DisorderStage.Argument && person.Pressure >= DisorderFightEligiblePressure)
                     .OrderBy(person => person.ConductStage == DisorderStage.Fight ? 0 : 1).ThenBy(person => person.ConductStageTick).ThenBy(person => person.Id).ToArray())
            if (!ResponseTargetClaimed(person.Id) && SelectRoleResponse(ResponseRole.Steward, person.Id,
                    worker => AcceptsAutomaticResponse(worker, person.Id,
                        person.ConductStage == DisorderStage.Fight ? FightResponseValue : ArgumentResponseValue, DisorderCalmingTicks), out _) is DisorderCommand command)
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
        if (CleanupOwnsNavigation(id)) return "Cleaning · bounded sweep";
        if (FaultWorkOf(id) is { } fault) return fault.Kind == FacilityFaultKind.StuckInToilet ? "Freeing someone stuck in a toilet" :
            id == _equipment?.WorkerId ? "Mending a broken tap" : "Bodging a broken tap";
        return StaffUnavailableReason(id) is { } issue ? "Unavailable · " + issue : "Idle · available (safe return routes may be reassigned)";
    }
}

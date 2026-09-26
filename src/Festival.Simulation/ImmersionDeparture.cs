namespace Festival.Simulation;

public sealed partial class GameSession
{
    public bool ImmersionDepartureActive => _immersion is not null && _preparation?.Status == PreparationStatus.Departing;
    private bool MedicalOperationsActive => _preparation?.Status == PreparationStatus.Running || ImmersionDepartureActive;
    private bool ImmersionDepartureMedicalBoundaryOnNextTick => !IsPaused && ImmersionDepartureActive && _medical!.Needs.Any(need =>
    {
        if (_preparation!.People.Single(person=>person.AgentId==need.AgentId).Departed) return false;
        var primary=need.AgentId==_medical.AtRiskGuestId;
        var stage=primary?_medical.Stage:need.Stage;
        var warning=primary?_medical.WarningTick:need.WarningTick;
        var collapse=primary?_medical.CollapseTick:need.CollapseTick;
        return stage==MedicalStage.Distress && CurrentTick+1>=warning+MedicalCollapseDelayTicks ||
            stage==MedicalStage.Collapsed && CurrentTick+1>=collapse+MedicalCriticalDelayTicks ||
            stage==MedicalStage.Critical && CurrentTick+1>=collapse+MedicalDeathDelayTicks ||
            _disorder!.People.Any(person=>person.AgentId==need.AgentId && person.Stage==DisorderStage.Injured && CurrentTick+1>=person.InjuryTick+DisorderInjuryDeathTicks);
    });
    private bool IsImmersionMedic(ulong id) => GetMedicResponses().Any(job => job.WorkerId == id);
    private bool ImmersionDepartureJobOwns(ulong id) => PersonCollapsed(id) ||
        GetMedicResponses().Any(job => MedicBusy(job) && (job.WorkerId == id || job.PatientId == id)) ||
        _disorder?.People.Any(person => person.AgentId == id && person.Stage == DisorderStage.Injured) == true;
    private bool ImmersionCanMarkDeparted(ulong id, int index)
    {
        if (!ImmersionDepartureActive) return true;
        var nav = _navigationAgents[new(id)];
        return !ImmersionDepartureJobOwns(id) && nav.Destination == PreparedStart(index) &&
            nav.IntentId == "edition.departure" &&
            (!IsImmersionMedic(id) || _preparation!.People.All(person => person.Departed || IsImmersionMedic(person.AgentId)));
    }
    private void StartImmersionDeparture()
    {
        ReleaseInterventionsForBoundary("Closing released ordinary staff interventions; physical medical responses remain active");
        foreach (var job in GetStewardResponses().Where(StewardBusy))
            SetStewardResponse(job with { Stage = SecurityResponseStage.Completed, TargetId = null, Description = "Closing; physical departure" });
        var medical = _medical!;
        _medical = medical with { WaterQueue = [], WaterOverflow = [], WaterOwnerId = null, WaterDrinkTicks = 0,
            MainWaterQueueCells = [], ExtraWaterPoints = medical.ExtraWaterPoints.Select(point => point with
                { Queue = [], Overflow = [], OwnerId = null, DrinkTicks = 0, QueueCells = [] }).ToArray(),
            Needs = medical.Needs.Select(need => ImmersionDepartureJobOwns(need.AgentId) ? need with { QueueSlot = null } :
                need with { Intent = IsImmersionMedic(need.AgentId) ? MedicalIntent.WatchShow : MedicalIntent.Leaving,
                    QueueSlot = null, Reason = IsImmersionMedic(need.AgentId) ? "Medic remains available until other people physically exit" : "Closing; physically leaving while exposure continues" }).ToArray() };
        AdvanceImmersionDepartureRoutes();
        CleanupImmersionDeparture();
    }
    private void AdvanceImmersionDepartureRoutes()
    {
        if (!ImmersionDepartureActive) return;
        var p = _preparation!;
        for (var index = 0; index < p.People.Length; index++)
        {
            var person = p.People[index];
            if (person.Departed || ImmersionDepartureJobOwns(person.AgentId)) continue;
            var holdMedic = IsImmersionMedic(person.AgentId) && p.People.Any(other => !other.Departed && !IsImmersionMedic(other.AgentId));
            var cell = holdMedic ? StaffDutyCell(person.AgentId, ResponseRole.Medic) : PreparedStart(index);
            var intent = holdMedic ? "medical.departure-standby" : "edition.departure";
            var nav = _navigationAgents[new(person.AgentId)];
            if (nav.Destination != cell || nav.IntentId != intent) ApplyAgentDestination(new(person.AgentId), new(cell, intent));
            SetNeed(person.AgentId, need => need with { Intent = holdMedic ? MedicalIntent.WatchShow : MedicalIntent.Leaving,
                Reason = holdMedic ? "Medic available while protected people remain on site" : "Physically leaving; ingestion and exposure continue until exit" });
        }
    }
    private void AdvanceImmersionDepartureMedicine()
    {
        AdvanceMedicResponses();
        foreach (var person in _preparation!.People.Where(person => !person.Departed).ToArray())
        {
            var need = _medical!.Needs.Single(need => need.AgentId == person.AgentId);
            var injury = _disorder!.People.SingleOrDefault(item => item.AgentId == person.AgentId && item.Stage == DisorderStage.Injured);
            if (injury is not null)
            {
                if (need.Stage == MedicalStage.Treated)
                    SetDisorderPerson(person.AgentId, item => item with { Stage = DisorderStage.Resolved, Pressure = 0, StageTick = CurrentTick, CooldownUntilTick = CurrentTick + 800 });
                else if (CurrentTick >= injury.InjuryTick + DisorderInjuryDeathTicks) { ApplyDisorderDeath(person.AgentId); return; }
                continue;
            }
            var primary = person.AgentId == _medical.AtRiskGuestId;
            var stage = primary ? _medical.Stage : need.Stage;
            var warning = primary ? _medical.WarningTick : need.WarningTick;
            var collapse = primary ? _medical.CollapseTick : need.CollapseTick;
            if (stage == MedicalStage.Distress && CurrentTick >= warning + MedicalCollapseDelayTicks)
            {
                var nav = _navigationAgents[new(person.AgentId)];
                ApplyAgentDestination(new(person.AgentId), new(TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres), "medical.departure-collapse"));
                SetNeed(person.AgentId, item => item with { Stage = MedicalStage.Collapsed, Intent = MedicalIntent.Collapsed, CollapseTick = CurrentTick });
                if (primary) _medical = _medical with { Stage = MedicalStage.Collapsed, CollapseTick = CurrentTick };
                MedicalEvent("medical:collapse", $"Person {person.AgentId}: existing medical warning progressed during physical departure");
                stage = MedicalStage.Collapsed; collapse = CurrentTick;
            }
            if (stage == MedicalStage.Collapsed && CurrentTick >= collapse + MedicalCriticalDelayTicks)
            {
                SetNeed(person.AgentId, item => item with { Stage = MedicalStage.Critical, CriticalTick = CurrentTick });
                if (primary) _medical = _medical with { Stage = MedicalStage.Critical, CriticalTick = CurrentTick };
                MedicalEvent("medical:critical", $"Person {person.AgentId}: medical deadline continues until physical exit");
                stage = MedicalStage.Critical;
            }
            if (stage == MedicalStage.Critical && CurrentTick >= collapse + MedicalDeathDelayTicks)
            { ApplyMedicalDeath(person.AgentId, warning, collapse, primary?_medical.CriticalTick:_medical.Needs.Single(item => item.AgentId == person.AgentId).CriticalTick); return; }
        }
        AdvanceImmersionDepartureRoutes();
    }
}

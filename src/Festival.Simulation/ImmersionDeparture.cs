namespace Festival.Simulation;

public sealed partial class GameSession
{
    public bool ImmersionDepartureActive => _immersion is not null && _preparation?.Status == PreparationStatus.Departing;
    private bool MedicalOperationsActive => _preparation?.Status == PreparationStatus.Running || ImmersionDepartureActive;
    private bool ImmersionDepartureMedicalBoundaryOnNextTick => !IsPaused && ImmersionDepartureActive && PeopleIn(PersonView.Medical).Any(need =>
    {
        if (_persons[need.Id].Departed) return false;
        var primary=need.Id==_medical!.AtRiskGuestId;
        var stage=primary?_medical.Stage:need.HealthStage;
        var warning=primary?_medical.WarningTick:need.HealthWarningTick;
        var collapse=primary?_medical.CollapseTick:need.HealthCollapseTick;
        return stage==MedicalStage.Distress && CurrentTick+1>=warning+MedicalCollapseDelayTicks ||
            stage==MedicalStage.Collapsed && CurrentTick+1>=collapse+MedicalCriticalDelayTicks ||
            stage==MedicalStage.Critical && CurrentTick+1>=collapse+MedicalDeathDelayTicks ||
            (PersonIn(PersonView.Disorder, need.Id) is { ConductStage: DisorderStage.Injured } injured && CurrentTick+1>=injured.InjuryTick+DisorderInjuryDeathTicks);
    });
    private bool IsImmersionMedic(ulong id) => GetMedicResponses().Any(job => job.WorkerId == id);
    private bool ImmersionDepartureJobOwns(ulong id) => PersonCollapsed(id) ||
        EffectiveToilets(_immersion).Any(toilet => toilet.OwnerId == id) ||
        HasClaim(id, PersonClaim.MedicResponding | PersonClaim.MedicPatient) ||
        PersonIn(PersonView.Disorder, id)?.ConductStage == DisorderStage.Injured;
    private bool ImmersionCanMarkDeparted(ulong id, int index)
    {
        if (!ImmersionDepartureActive) return true;
        var nav = _navigationAgents[new(id)];
        return !ImmersionDepartureJobOwns(id) && nav.Destination == PreparedStart(index) &&
            nav.IntentId == "edition.departure" &&
            (!IsImmersionMedic(id) || PeopleIn(PersonView.Roster).All(person => person.Departed || IsImmersionMedic(person.Id)));
    }
    private void StartImmersionDeparture()
    {
        ReleaseFightHandlingForBoundary("Closing released physical fight handling; the single attempt is spent without a new roll");
        ReleaseInterventionsForBoundary("Closing released ordinary staff interventions; physical medical responses remain active");
        foreach (var job in GetStewardResponses().Where(StewardBusy))
            SetStewardResponse(job with { Stage = SecurityResponseStage.Completed, TargetId = null, Description = "Closing; physical departure" });
        SetTaps(Taps.Select(point => point with { Queue = [], Overflow = [], OwnerId = null, DrinkTicks = 0, QueueCells = [] }).ToArray());
        foreach (var need in PeopleIn(PersonView.Medical).Select(need => ImmersionDepartureJobOwns(need.Id) ? need with { WaterQueueSlot = null } :
                need with { Intent = IsImmersionMedic(need.Id) ? MedicalIntent.WatchShow : MedicalIntent.Leaving,
                    WaterQueueSlot = null, Reason = IsImmersionMedic(need.Id) ? "Medic remains available until other people physically exit" : "Closing; physically leaving while exposure continues" }).ToArray())
            _persons.Set(need);
        AdvanceImmersionDepartureRoutes();
        CleanupImmersionDeparture();
    }
    private void AdvanceImmersionDepartureRoutes()
    {
        if (!ImmersionDepartureActive) return;
        var p = _preparation!;
        for (var index = 0; index < PeopleIn(PersonView.Roster).Length; index++)
        {
            var person = PeopleIn(PersonView.Roster)[index];
            if (person.Departed || ImmersionDepartureJobOwns(person.Id)) continue;
            var holdMedic = IsImmersionMedic(person.Id) && PeopleIn(PersonView.Roster).Any(other => !other.Departed && !IsImmersionMedic(other.Id));
            var cell = holdMedic ? StaffDutyCell(person.Id, ResponseRole.Medic) : PreparedStart(index);
            var intent = holdMedic ? "medical.departure-standby" : "edition.departure";
            var nav = _navigationAgents[new(person.Id)];
            if (nav.Destination != cell || nav.IntentId != intent) ApplyAgentDestination(new(person.Id), new(cell, intent));
            MutatePerson(person.Id, need => { need.Intent = holdMedic ? MedicalIntent.WatchShow : MedicalIntent.Leaving; need.Reason = holdMedic ? "Medic available while protected people remain on site" : "Physically leaving; ingestion and exposure continue until exit"; });
        }
    }
    private void AdvanceImmersionDepartureMedicine()
    {
        AdvanceMedicResponses();
        foreach (var person in PeopleIn(PersonView.Roster).Where(person => !person.Departed).ToArray())
        {
            var need = _persons[person.Id];
            var injury = PeopleIn(PersonView.Disorder).SingleOrDefault(item => item.Id == person.Id && item.ConductStage == DisorderStage.Injured);
            if (injury is not null)
            {
                if (need.HealthStage == MedicalStage.Treated)
                    MutatePerson(person.Id, item => { item.ConductStage = DisorderStage.Resolved; item.Pressure = 0; item.ConductStageTick = CurrentTick; item.CooldownUntilTick = CurrentTick + 800; });
                else if (CurrentTick >= injury.InjuryTick + DisorderInjuryDeathTicks) { ApplyDisorderDeath(person.Id); return; }
                continue;
            }
            var primary = person.Id == _medical!.AtRiskGuestId;
            var stage = primary ? _medical.Stage : need.HealthStage;
            var warning = primary ? _medical.WarningTick : need.HealthWarningTick;
            var collapse = primary ? _medical.CollapseTick : need.HealthCollapseTick;
            if (stage == MedicalStage.Distress && CurrentTick >= warning + MedicalCollapseDelayTicks)
            {
                var nav = _navigationAgents[new(person.Id)];
                ApplyAgentDestination(new(person.Id), new(TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres), "medical.departure-collapse"));
                MutatePerson(person.Id, item => { item.HealthStage = MedicalStage.Collapsed; item.Intent = MedicalIntent.Collapsed; item.HealthCollapseTick = CurrentTick; });
                if (primary) _medical = _medical with { Stage = MedicalStage.Collapsed, CollapseTick = CurrentTick };
                MedicalEvent("medical:collapse", $"Person {person.Id}: existing medical warning progressed during physical departure");
                RecordGuestMedicalCollapse(person.Id);
                stage = MedicalStage.Collapsed; collapse = CurrentTick;
            }
            if (stage == MedicalStage.Collapsed && CurrentTick >= collapse + MedicalCriticalDelayTicks)
            {
                MutatePerson(person.Id, item => { item.HealthStage = MedicalStage.Critical; item.HealthCriticalTick = CurrentTick; });
                if (primary) _medical = _medical with { Stage = MedicalStage.Critical, CriticalTick = CurrentTick };
                MedicalEvent("medical:critical", $"Person {person.Id}: medical deadline continues until physical exit");
                stage = MedicalStage.Critical;
            }
            if (stage == MedicalStage.Critical && CurrentTick >= collapse + MedicalDeathDelayTicks)
            { ApplyMedicalDeath(person.Id, warning, collapse, primary?_medical.CriticalTick:_persons[person.Id].HealthCriticalTick); return; }
        }
        AdvanceImmersionDepartureRoutes();
    }
}

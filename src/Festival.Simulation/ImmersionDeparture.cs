namespace Festival.Simulation;

public sealed partial class GameSession
{
    public bool ImmersionDepartureActive => _immersion is not null && _preparation?.Status == PreparationStatus.Departing;
    private bool MedicalOperationsActive => _preparation?.Status == PreparationStatus.Running || ImmersionDepartureActive;
    private bool ImmersionDepartureMedicalBoundaryOnNextTick => !IsPaused && ImmersionDepartureActive && PeopleIn(PersonView.Medical).Any(need =>
    {
        if (_persons[need.Id].Departed) return false;
        var stage=need.HealthStage;
        var warning=need.HealthWarningTick;
        var collapse=need.HealthCollapseTick;
        return stage==MedicalStage.Distress && CurrentTick+1>=warning+MedicalCollapseDelayTicks ||
            stage==MedicalStage.Collapsed && CurrentTick+1>=collapse+MedicalCriticalDelayTicks ||
            stage==MedicalStage.Critical && CurrentTick+1>=collapse+MedicalDeathDelayTicks ||
            (PersonIn(PersonView.Disorder, need.Id) is { ConductStage: DisorderStage.Injured } injured && CurrentTick+1>=injured.InjuryTick+DisorderInjuryDeathTicks);
    });
    private bool IsImmersionMedic(ulong id) => GetMedicResponses().Any(job => job.WorkerId == id);
    /// <summary>Stewards and medics stay on duty through the walk-out: the festival finishes when the last guest is off site.</summary>
    /// <summary>The player's own steward (All-Rounder) counts: control goes back to the manager at the close, and they stand where they were.</summary>
    private bool HoldsForGuests(ulong id) => IsImmersionMedic(id) || GetStewardResponses().Any(job => job.WorkerId == id) || id == HandsOnStewardId;
    private ResponseRole HoldRole(ulong id) => IsImmersionMedic(id) ? ResponseRole.Medic : ResponseRole.Steward;
    private bool GuestsRemainOnSite => PeopleIn(PersonView.Roster).Any(person => person.Role == ProtectedPersonRole.Guest && !person.Departed);
    private bool ImmersionDepartureJobOwns(ulong id) => PersonCollapsed(id) ||
        EffectiveToilets(_facilities).Any(toilet => toilet.OwnerId == id) ||
        HasClaim(id, PersonClaim.MedicResponding | PersonClaim.MedicPatient) ||
        PersonIn(PersonView.Disorder, id)?.ConductStage == DisorderStage.Injured;
    private bool ImmersionCanMarkDeparted(ulong id, int index)
    {
        if (!ImmersionDepartureActive) return true;
        var nav = _navigationAgents[new(id)];
        return !ImmersionDepartureJobOwns(id) && nav.Destination == PreparedStart(index) &&
            nav.IntentId == "edition.departure" &&
            (!HoldsForGuests(id) || !GuestsRemainOnSite);
    }
    private void StartImmersionDeparture()
    {
        ReleaseFightHandlingForBoundary("Closing released physical fight handling; the single attempt is spent without a new roll");
        ReleaseInterventionsForBoundary("Closing released ordinary staff interventions; physical medical responses remain active");
        foreach (var job in GetStewardResponses().Where(StewardBusy))
            SetStewardResponse(job with { Stage = SecurityResponseStage.Completed, TargetId = null, Description = "Closing; physical departure" });
        SetTaps(Taps.Select(point => point with { Queue = [], Overflow = [], OwnerId = null, DrinkTicks = 0, QueueCells = [] }).ToArray());
        foreach (var need in PeopleIn(PersonView.Medical).Select(need => ImmersionDepartureJobOwns(need.Id) ? need with { WaterQueueSlot = null } :
                need with { Intent = HoldsForGuests(need.Id) ? MedicalIntent.WatchShow : MedicalIntent.Leaving,
                    WaterQueueSlot = null, Reason = HoldsForGuests(need.Id) ? "On duty until the last guest is off site" : "Closing; physically leaving while exposure continues" }).ToArray())
            _persons.Set(need);
        AdvanceImmersionDepartureRoutes();
        CleanupImmersionDeparture();
    }
    private void AdvanceImmersionDepartureRoutes()
    {
        if (!ImmersionDepartureActive) return;
        var p = _preparation!;
        var guestsRemain = GuestsRemainOnSite;
        for (var index = 0; index < PeopleIn(PersonView.Roster).Length; index++)
        {
            var person = PeopleIn(PersonView.Roster)[index];
            if (person.Departed || ImmersionDepartureJobOwns(person.Id)) continue;
            var hold = guestsRemain && HoldsForGuests(person.Id);
            var intent = !hold ? "edition.departure" : HoldRole(person.Id) == ResponseRole.Medic ? "medical.departure-standby" : "disorder.departure-standby";
            var nav = _navigationAgents[new(person.Id)];
            var cell = !hold ? PreparedStart(index) : person.Id != HandsOnStewardId ? StaffDutyCell(person.Id, HoldRole(person.Id)) :
                nav.IntentId == intent && nav.Destination is { } standing ? standing : TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
            if (nav.Destination != cell || nav.IntentId != intent) ApplyAgentDestination(new(person.Id), new(cell, intent));
            MutatePerson(person.Id, need => { need.Intent = hold ? MedicalIntent.WatchShow : MedicalIntent.Leaving; need.Reason = hold ? "On duty while guests remain on site" : "Physically leaving; ingestion and exposure continue until exit"; });
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
            var stage = need.HealthStage;
            var warning = need.HealthWarningTick;
            var collapse = need.HealthCollapseTick;
            if (stage == MedicalStage.Distress && CurrentTick >= warning + MedicalCollapseDelayTicks)
            {
                var nav = _navigationAgents[new(person.Id)];
                ApplyAgentDestination(new(person.Id), new(TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres), "medical.departure-collapse"));
                MutatePerson(person.Id, item => { item.HealthStage = MedicalStage.Collapsed; item.Intent = MedicalIntent.Collapsed; item.HealthCollapseTick = CurrentTick; });
                MedicalEvent("medical:collapse", $"Person {person.Id}: existing medical warning progressed during physical departure");
                RecordGuestMedicalCollapse(person.Id);
                stage = MedicalStage.Collapsed; collapse = CurrentTick;
            }
            if (stage == MedicalStage.Collapsed && CurrentTick >= collapse + MedicalCriticalDelayTicks)
            {
                MutatePerson(person.Id, item => { item.HealthStage = MedicalStage.Critical; item.HealthCriticalTick = CurrentTick; });
                MedicalEvent("medical:critical", $"Person {person.Id}: medical deadline continues until physical exit");
                stage = MedicalStage.Critical;
            }
            if (stage == MedicalStage.Critical && CurrentTick >= collapse + MedicalDeathDelayTicks)
            { ApplyMedicalDeath(person.Id, warning, collapse, _persons[person.Id].HealthCriticalTick); return; }
        }
        AdvanceImmersionDepartureRoutes();
    }
}

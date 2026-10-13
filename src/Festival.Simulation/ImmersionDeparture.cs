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
    /// <summary>
    /// Stewards and medics stay on duty through the walk-out, until the festival can finish. The player's own steward
    /// (All-Rounder) counts: control goes back to the manager at the close, and they stand where they were.
    /// </summary>
    private bool HoldsForGuests(ulong id) => IsImmersionMedic(id) || GetStewardResponses().Any(job => job.WorkerId == id) || id == HandsOnStewardId;
    private ResponseRole HoldRole(ulong id) => IsImmersionMedic(id) ? ResponseRole.Medic : ResponseRole.Steward;
    private bool GuestsRemainOnSite => PeopleIn(PersonView.Roster).Any(person => person.Role == ProtectedPersonRole.Guest && !person.Departed);
    /// <summary>Down on a death clock: collapsed, critical or fight-injured. Anyone like this, guest or not, holds the day open.</summary>
    private bool OnDeathClock(ulong id) => PersonCollapsed(id) || PersonIn(PersonView.Disorder, id)?.ConductStage == DisorderStage.Injured;
    private bool CasualtyOnSite(bool includePatients) => PeopleIn(PersonView.Roster).Any(person => !person.Departed &&
        (OnDeathClock(person.Id) || includePatients && HasClaim(person.Id, PersonClaim.MedicPatient)));
    /// <summary>Stewards and the medic are still needed while a guest or a casualty is on site.</summary>
    private bool StaffNeededOnSite => GuestsRemainOnSite || CasualtyOnSite(true);
    /// <summary>
    /// Something other than the walk home has this person: down, being seen to, or a loo or tap on the way out (from the
    /// approach to walking away), which hands them back to the walk home when it's done.
    /// </summary>
    private bool ImmersionDepartureJobOwns(ulong id) => PersonCollapsed(id) ||
        EffectiveToilets(_facilities).Any(toilet => toilet.OwnerId == id) ||
        PersonIn(PersonView.Consumption, id) is { ToiletStage: not ToiletVisitStage.None } ||
        HasClaim(id, PersonClaim.WaterPlace | PersonClaim.SeekingWater) || _persons[id].Intent == MedicalIntent.Drinking ||
        HasClaim(id, PersonClaim.MedicResponding | PersonClaim.MedicPatient) ||
        PersonIn(PersonView.Disorder, id)?.ConductStage == DisorderStage.Injured;

    /// <summary>The walk-out: guests set off home over the first <see cref="DepartureLingerMaxTicks"/> after the close, not all at once.</summary>
    public const int DepartureLingerMaxTicks = 800;
    /// <summary>
    /// How long after the close this guest lingers before setting off (chatting, a last look at the stage): a keyed roll,
    /// squared so most go within the first few minutes and a few hang on to the end of the window. 0 to
    /// <see cref="DepartureLingerMaxTicks"/>.
    /// </summary>
    public static int DepartureLingerTicks(ulong seed, ulong id)
    {
        var roll = (long)(GuestInitialRoll(seed, id, 0x4C494E474552UL) % 1_001);
        return (int)(roll * roll * DepartureLingerMaxTicks / 1_000_000);
    }
    /// <summary>A guest's pace on the way home, in permille of their usual stroll: a keyed 900-1000, some tired after a long day.</summary>
    public static int DeparturePacePermille(ulong seed, ulong id) => 900 + (int)(GuestInitialRoll(seed, id, 0x54495245445055UL) % 101);
    /// <summary>
    /// The chance, in percent, a guest leaving stops at a loo on the way: none under 3,000 need, rising to 30% at 6,000 (where
    /// they'd go during the day anyway).
    /// </summary>
    public static int DepartureToiletChancePercent(int toiletNeed) => Math.Clamp((toiletNeed - 3_000) / 100, 0, 30);
    /// <summary>Nobody stops for a loo or a tap on the way home with this many already in its line or heading for it: they go home.</summary>
    public const int DepartureMostInLine = 2;
    /// <summary>The chance, in percent, a guest leaving stops at a tap on the way: none under 3,500 thirst, rising to 30% at 6,500.</summary>
    public static int DepartureWaterChancePercent(int thirst) => Math.Clamp((thirst - 3_500) / 100, 0, 30);
    /// <summary>On the way home a drink at the tap is a quick one: four festival minutes at most, then on they go.</summary>
    public const int DepartureDrinkTicks = 240;
    /// <summary>
    /// Where a guest stops on the way out, if anywhere, by need and a keyed roll for each: the loo first, then a tap. Their
    /// needs don't grow once the music's over, so this is the same whenever it's asked until they've been.
    /// </summary>
    public static ActivityKind? DepartureStop(ulong seed, ulong id, int toiletNeed, int thirst) =>
        (int)(GuestInitialRoll(seed, id, 0x44455054504F4FUL) % 100) < DepartureToiletChancePercent(toiletNeed) ? ActivityKind.Toilet :
        (int)(GuestInitialRoll(seed, id, 0x44455054574154UL) % 100) < DepartureWaterChancePercent(thirst) ? ActivityKind.Water : null;
    /// <summary>The planned close: when the walk-out starts, and what the lingering counts from.</summary>
    private long DepartureCloseTick => _preparation!.StartedTick + PreparedEditionDurationTicks;
    /// <summary>A guest still lingering after the close, not yet on their way.</summary>
    private const string LingerIntent = "edition.linger";
    private bool ImmersionCanMarkDeparted(ulong id, int index)
    {
        if (!ImmersionDepartureActive) return true;
        var nav = _navigationAgents[new(id)];
        return !ImmersionDepartureJobOwns(id) && nav.Destination == PreparedStart(index) &&
            nav.IntentId == "edition.departure" &&
            (!HoldsForGuests(id) || !StaffNeededOnSite);
    }
    private void StartImmersionDeparture()
    {
        ReleaseFightHandlingForBoundary("Closing released physical fight handling; the single attempt is spent without a new roll");
        ReleaseInterventionsForBoundary("Closing released ordinary staff interventions; physical medical responses remain active");
        foreach (var job in GetStewardResponses().Where(StewardBusy))
            SetStewardResponse(job with { Stage = SecurityResponseStage.Completed, TargetId = null, Description = "Closing; physical departure" });
        // Bars and vans shut at the close; loos and taps stay open for the walk-out. The front of each line keeps its place;
        // anyone further back, or still on their way to it, gives up and heads home with everyone else.
        foreach (var toilet in EffectiveToilets(_facilities).ToArray())
            foreach (var id in toilet.Queue.Where((id, index) => id != toilet.OwnerId && index >= DepartureMostInLine)
                         .Concat(PeopleIn(PersonView.Consumption).Where(p => p.ToiletId == toilet.Id && p.ToiletStage == ToiletVisitStage.Approaching).Select(p => p.Id)).ToArray())
                ReleaseToiletPerson(id, false);
        foreach (var point in WaterPoints().ToArray())
            foreach (var id in point.Queue.Skip(DepartureMostInLine).Concat(point.Overflow).Concat(PeopleIn(PersonView.Medical)
                         .Where(need => need.Intent == MedicalIntent.SeekWater && need.WaterPointId == point.Id && need.WaterQueueSlot is null).Select(need => need.Id))
                         .Distinct().Reverse().ToArray())
                LeaveWater(id, "Closing: too far back in the line, heading home", reroute: false);
        foreach (var need in PeopleIn(PersonView.Medical).Where(need => !ImmersionDepartureJobOwns(need.Id)).Select(need =>
                need with { Intent = HoldsForGuests(need.Id) ? MedicalIntent.WatchShow : MedicalIntent.Leaving,
                    WaterQueueSlot = null, Reason = HoldsForGuests(need.Id) ? "On duty until the last guest is off site" : "Closing; physically leaving while exposure continues" }).ToArray())
            _persons.Set(need);
        AdvanceImmersionDepartureRoutes(closing: true);
        CleanupImmersionDeparture();
    }

    /// <summary>
    /// A lingering guest sets off: by way of a loo or a tap if their need and their roll say so and there's one to use,
    /// else straight for the gate. Returns false while they stay put.
    /// </summary>
    private bool SetOffFromLinger(ulong id, int index)
    {
        var close = DepartureCloseTick;
        var waited = CurrentTick - close;
        // Finishing a drink holds them, but not past the end of the lingering window.
        if (waited < DepartureLingerTicks(CampaignSeed, id) ||
            PersonIn(PersonView.Consumption, id)?.Held is not null && waited < DepartureLingerMaxTicks) return false;
        var stop = DepartureStop(CampaignSeed, id, _persons[id].ToiletNeed, _persons[id].Thirst);
        if (stop == ActivityKind.Toilet && StartDepartureToilet(id)) return true;
        if (stop == ActivityKind.Water && StartDepartureWater(id)) return true;
        ApplyAgentDestination(new(id), new(PreparedStart(index), "edition.departure"));
        return true;
    }

    /// <summary>The loo that would serve them soonest, if any will take them; they join its line as during the day.</summary>
    private bool StartDepartureToilet(ulong id)
    {
        var person = _persons[id];
        var kind = ChooseToiletVisit(person);
        if (BestToiletFor(id, kind) is not { } toilet || toilet.IsFull || toilet.InterruptedOccupantId is not null ||
            ToiletBeingEmptied(toilet.Id) || !toilet.CanAccept(kind) ||
            toilet.Queue.Length + PeopleIn(PersonView.Consumption).Count(p => p.ToiletId == toilet.Id && p.ToiletStage == ToiletVisitStage.Approaching) >= DepartureMostInLine)
            return false;
        SetConsumption(person with { ToiletStage = ToiletVisitStage.Approaching, ToiletChoice = kind, ToiletId = toilet.Id, LastToiletChoiceReviewTick = CurrentTick });
        MutatePerson(id, item => item.Reason = "A last trip to the loo on the way out");
        ApplyAgentDestination(new(id), new(ToiletQueueCell(toilet, toilet.Queue.Length), "toilet.approach"));
        return true;
    }

    /// <summary>A working tap with room in its line, the soonest to serve them; nobody is sent to a broken one.</summary>
    private bool StartDepartureWater(ulong id)
    {
        if (_disorder?.WaterClosed == true || _medical is null) return false;
        var working = WaterPoints().Where(point => ActiveFault(point.Id) is null).ToArray();
        var decision = QueuedServiceChoice.Choose(id, null, working.Select(point => WaterChoiceCandidate(id, point)).ToArray());
        if (decision is null) return false;
        var tap = working.Single(point => point.Id == decision.Id);
        if (tap.Queue.Length + tap.Overflow.Length + PeopleIn(PersonView.Medical).Count(need => need.Intent == MedicalIntent.SeekWater &&
                need.WaterPointId == tap.Id && need.WaterQueueSlot is null && !tap.Overflow.Contains(need.Id)) >= DepartureMostInLine) return false;
        SeekWater(id, "A last drink of water on the way out", decision.Id);
        if (_persons[id].Intent == MedicalIntent.SeekWater) return true;
        MutatePerson(id, item => item.Intent = MedicalIntent.Leaving);
        return false;
    }

    /// <summary>
    /// The taps through the walk-out: arrivals join the line, and whoever's at the front drinks until they've had enough,
    /// then walks on home. A tap that's broken stays broken, so its line gives up and goes.
    /// </summary>
    private void AdvanceDepartureWater()
    {
        if (_medical is null) return;
        foreach (var point in WaterPoints().Where(point => ActiveFault(point.Id) is not null).ToArray())
            foreach (var id in point.Queue.Concat(point.Overflow).Concat(PeopleIn(PersonView.Medical)
                         .Where(need => need.Intent == MedicalIntent.SeekWater && need.WaterPointId == point.Id).Select(need => need.Id)).Distinct().ToArray())
                LeaveWater(id, "The tap is broken; heading home", reroute: true);
        AdmitWaterArrivals();
        AdvanceTaps();
    }

    private void AdvanceImmersionDepartureRoutes(bool closing = false)
    {
        if (!ImmersionDepartureActive) return;
        var p = _preparation!;
        var guestsRemain = StaffNeededOnSite;
        for (var index = 0; index < PeopleIn(PersonView.Roster).Length; index++)
        {
            var person = PeopleIn(PersonView.Roster)[index];
            if (person.Departed || ImmersionDepartureJobOwns(person.Id)) continue;
            // Guests don't all leave on the last note: each lingers a while where they are, then sets off.
            if (person.Role == ProtectedPersonRole.Guest && (closing || _navigationAgents[new(person.Id)].IntentId == LingerIntent))
            {
                var at = _navigationAgents[new(person.Id)];
                if (closing)
                {
                    // Someone on their way to their spot at the stage carries on there; anyone else stops where they are.
                    var spot = at.IntentId?.StartsWith("performance.listen", StringComparison.Ordinal) == true && at.Destination is { } place
                        ? place : TraversalGrid.WorldToCell(at.XMillimetres, at.ZMillimetres);
                    ApplyAgentDestination(new(person.Id), new(spot, LingerIntent));
                    MutatePerson(person.Id, need => { need.Intent = MedicalIntent.Leaving; need.Reason = "Lingering after the music; ingestion and exposure continue until exit"; });
                }
                if (!SetOffFromLinger(person.Id, index)) continue;
                if (ImmersionDepartureJobOwns(person.Id)) continue;
            }
            var hold = guestsRemain && HoldsForGuests(person.Id);
            var intent = !hold ? "edition.departure" : HoldRole(person.Id) == ResponseRole.Medic ? "medical.departure-standby" : "disorder.departure-standby";
            var nav = _navigationAgents[new(person.Id)];
            var cell = !hold ? PreparedStart(index) : person.Id != HandsOnStewardId ? StaffDutyCell(person.Id, HoldRole(person.Id)) :
                nav.IntentId == intent && nav.Destination is { } standing ? standing : TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
            // A walker left with no route tries again every festival minute, so nobody is stranded holding the day open.
            if (nav.Destination != cell || nav.IntentId != intent || nav.Action == AgentNavigationAction.NoRoute && CurrentTick % 80 == 0)
                ApplyAgentDestination(new(person.Id), new(cell, intent));
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
        ReleaseStalledDepartureStops();
        AdvanceDepartureWater();
        AdvanceImmersionDepartureRoutes();
    }

    /// <summary>
    /// The longest a guest leaving keeps heading for a loo or a tap before giving it up and going straight home:
    /// 2,400 ticks, about twice the longest real approach (1,251 ticks over sixteen Tier 1 walk-outs and two Tier 2, 14 Oct).
    /// </summary>
    public const int DepartureStopApproachTicks = 2_400;

    /// <summary>
    /// A guest on their way out who can't reach the loo or tap they set off for (no route) or hasn't got there in
    /// <see cref="DepartureStopApproachTicks"/> gives it up and walks home; otherwise nothing hands them back to the walk
    /// home and they'd hold the day open until the walk-out's limit. Only the approach: once in a line, the line moves.
    /// </summary>
    private void ReleaseStalledDepartureStops()
    {
        foreach (var person in PeopleIn(PersonView.Consumption).Where(p => p.ToiletStage == ToiletVisitStage.Approaching).ToArray())
            if (!_persons[person.Id].Departed && (_navigationAgents[new(person.Id)].Action == AgentNavigationAction.NoRoute ||
                    CurrentTick - (person.LastToiletChoiceReviewTick ?? CurrentTick) >= DepartureStopApproachTicks))
            {
                ReleaseToiletPerson(person.Id, returnToListening: true);
                MutatePerson(person.Id, need => need.Reason = "Couldn't get to the loo; heading home");
            }
        if (_medical is null) return;
        foreach (var need in PeopleIn(PersonView.Medical).Where(need => need.Intent == MedicalIntent.SeekWater && need.WaterQueueSlot is null).ToArray())
        {
            if (_persons[need.Id].Departed || WaterPoints().Any(point => point.Overflow.Contains(need.Id))) continue;
            if (_navigationAgents[new(need.Id)].Action == AgentNavigationAction.NoRoute ||
                CurrentTick - need.LastWaterChoiceReviewTick >= DepartureStopApproachTicks)
                LeaveWater(need.Id, "Couldn't get to the tap; heading home", reroute: true);
        }
    }
}

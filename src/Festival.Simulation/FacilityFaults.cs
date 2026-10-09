namespace Festival.Simulation;

public enum FacilityFaultKind { StuckInToilet, BrokenTap, ChewedCable, BrokenGate }
/// <summary>Active blocks the facility; a bodged tap works again at reduced flow; fixed is done with.</summary>
public enum FacilityFaultStage { Active, Bodged, Fixed }

/// <param name="VictimId">Who is stuck, or who was using the tap when it broke.</param>
/// <param name="WorkerId">The steward or maintenance worker assigned, while they are on their way or working.</param>
public sealed record FacilityFault(string Id, FacilityFaultKind Kind, string FacilityId, ulong VictimId, long StartedTick,
    FacilityFaultStage Stage, ulong? WorkerId = null, long WorkStartedTick = -1, long ResolvedTick = -1)
{
    /// <summary>When the fumes of a nearly full toilet overcame the person stuck in it; -1 if they never did.</summary>
    public long PoisonedTick { get; init; } = -1;
}
/// <param name="Disabled">Labelled test fixture: no new faults. Never set in play.</param>
public sealed record FaultsSnapshot(int Version, FacilityFault[] Faults, bool Disabled = false);

public static class FaultRules
{
    // A small chance on each use. A Tier 1 toilet (~30 visits) has about an even chance of one jam in a day,
    // 1 - 0.5^(1/30), after playtesting found two or more a day too many; a tap (~60 uses) about a 90% chance of
    // breaking, 1 - 0.1^(1/60).
    public const int ToiletStuckChancePer10k = 228;
    public const int TapBreakChancePer10k = 377;
    public const int RescueTicks = 480, RepairTicks = 960, BodgeTicks = 1_600;
    /// <summary>What someone weighing the queue expects to wait before help is even on its way.</summary>
    public const int UnassignedWaitTicks = 4_800;
    /// <summary>A bodged tap gives water at half the normal flow.</summary>
    public const int BodgedFlowDivisor = 2;
    public const int BrokenTapSatisfactionLoss = 150;

    /// <summary>Deterministic per-use roll from the campaign seed, the facility and the use.</summary>
    public static bool Roll(ulong seed, string facilityId, long a, ulong b, int chancePer10k)
    {
        var value = seed;
        foreach (var ch in facilityId) value = unchecked((value ^ ch) * 0x100000001B3UL);
        value = unchecked(value + (ulong)a * 0x9E3779B97F4A7C15UL + b * 0xD1B54A32D192ED03UL);
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return (int)((value ^ (value >> 31)) % 10_000) < chancePer10k;
    }

    /// <summary>Satisfaction a stuck person loses each second: one more every 15 seconds as panic grows, up to 8.</summary>
    public static int PanicLossPerSecond(long stuckTicks) => Math.Min(8, 1 + (int)(stuckTicks / 1_200));
    public const int ShoutEveryTicks = 400, ShoutTicks = 200, GrumbleTicks = 240;
    /// <summary>
    /// A toilet this full is toxic to anyone jammed inside it: they lose heart faster, and after this long they're
    /// overcome by the fumes and collapse, needing the door opened and a medic before they die.
    /// </summary>
    public const int ToxicFullPercent = 90, PoisonCollapseTicks = 3_600, PoisonLossPerSecond = 3;
    public static readonly string[] ToxicShouts = ["It stinks in here!", "I feel sick…", "I can't breathe in here!"];
    public static readonly string[][] StuckShouts = [
        ["Hello? The door's stuck!", "Er... the lock's jammed.", "Erm… hello?", "Can someone get a steward?"],
        ["Help! I can't get out!", "Is anyone out there?!", "I've been in here for ages!", "Not funny! Open up!"],
        ["HELP! GET ME OUT!", "Why won't it open?!", "I CAN HEAR YOU OUT THERE!"]];
    public static readonly string[] HotShouts = ["It's so hot in here!", "I'm cooking in here!"];
    public static readonly string[] TapGrumbles = ["Oh, come on!", "It's just spitting at me!", "Brilliant. Broken.",
        "Great, now my shoes are wet", "Water everywhere!", "Who broke the tap?"];

    /// <summary>
    /// What the person at the centre of a fault is saying at this tick, if anything: a stuck person shouts
    /// every few seconds, more desperately the longer they wait (and, in hot weather, every third shout is about
    /// the heat); a tap's last user grumbles once as it breaks.
    /// Presentation only, and a pure function of the fault, the tick and the weather, so a reload needs no catching up.
    /// </summary>
    public static string? Remark(FacilityFault fault, long tick, bool hot = false, bool conscious = true, bool toxic = false)
    {
        if (!conscious) return null;
        var elapsed = tick - fault.StartedTick;
        if (elapsed < 0 || fault.Stage != FacilityFaultStage.Active && !(fault.Kind == FacilityFaultKind.BrokenTap && elapsed < GrumbleTicks)) return null;
        if (fault.Kind == FacilityFaultKind.BrokenTap)
            return elapsed < GrumbleTicks ? TapGrumbles[(int)(fault.VictimId % (ulong)TapGrumbles.Length)] : null;
        if (elapsed % ShoutEveryTicks >= ShoutTicks) return null;
        var shout = elapsed / ShoutEveryTicks;
        if (toxic && shout % 2 == 1) return ToxicShouts[(int)(shout / 2 % ToxicShouts.Length)];
        if (hot && shout % 3 == 2) return HotShouts[(int)(shout / 3 % HotShouts.Length)];
        var band = StuckShouts[Math.Min(StuckShouts.Length - 1, (int)(elapsed / 1_600))];
        return band[(int)((shout + (long)(fault.VictimId % (ulong)band.Length)) % band.Length)];
    }

    /// <summary>In hot weather a portaloo cubicle adds this much heat every four ticks on top of the usual gain.</summary>
    public const int PortalooExtraHeat = 1;
}

public sealed partial class GameSession
{
    private FaultsSnapshot? _faults;
    private static readonly FaultsSnapshot EmptyFaults = new(1, []);
    public FaultsSnapshot? CaptureFaults() => _faults;
    internal string? FaultsCanonicalJson => _faults is null ? null : System.Text.Json.JsonSerializer.Serialize(_faults);

    private IEnumerable<FacilityFault> OpenFaults => (_faults?.Faults ?? []).Where(f => f.Stage != FacilityFaultStage.Fixed);
    public FacilityFault? ActiveFault(string facilityId) => OpenFaults.FirstOrDefault(f => f.FacilityId == facilityId && f.Stage == FacilityFaultStage.Active);
    public bool TapBodged(string tapId) => OpenFaults.Any(f => f.FacilityId == tapId && f.Stage == FacilityFaultStage.Bodged);
    private bool FaultWorkOwns(ulong id) => OpenFaults.Any(f => f.WorkerId == id) || CowWorkOwns(id);
    /// <summary>A cow's doing: a chewed cable or the broken pasture gate, which only the maintenance worker can mend.</summary>
    private static bool CowFault(FacilityFaultKind kind) => kind is FacilityFaultKind.ChewedCable or FacilityFaultKind.BrokenGate;
    private bool Unconscious(ulong id) => _persons[id].HealthStage is MedicalStage.Collapsed or MedicalStage.Critical;

    /// <summary>Whether a toilet is full enough to poison anyone jammed inside it.</summary>
    public bool ToiletToxic(string toiletId) => EffectiveToilets(_facilities).FirstOrDefault(t => t.Id == toiletId)?.FullPercent >= FaultRules.ToxicFullPercent;

    /// <summary>Whether someone collapsed from a toxic toilet's fumes, which outlasts the medic rewriting their status.</summary>
    private bool PoisonedByFumes(ulong id) => _faults?.Faults.Any(f => f.VictimId == id && f.PoisonedTick >= 0 && f.PoisonedTick == _persons[id].HealthCollapseTick) == true;

    /// <summary>The fumes overcome someone jammed in a nearly full toilet: they collapse inside, behind the locked door.</summary>
    private void OvercomeByFumes(FacilityFault fault)
    {
        var id = fault.VictimId;
        SetFault(fault with { PoisonedTick = CurrentTick });
        MutatePerson(id, item => { item.HealthStage = MedicalStage.Collapsed; item.HealthWarningTick = CurrentTick; item.HealthCollapseTick = CurrentTick;
            item.Intent = MedicalIntent.Collapsed; item.Reason = "Overcome by the fumes in a jammed, nearly full toilet; needs the door opened and a medic"; });
        MedicalEvent("medical:collapse", $"{_persons[id].Name} ({id}) collapsed from the fumes while stuck in {fault.FacilityId}.");
        if (IsGuest(id)) RecordGuestMedicalCollapse(id);
    }
    /// <summary>Locked in a cubicle: only a steward's rescue at the door gets them out.</summary>
    public bool StuckInToilet(ulong id) => OpenFaults.Any(f => f.Kind == FacilityFaultKind.StuckInToilet && f.Stage == FacilityFaultStage.Active && f.VictimId == id);
    public FacilityFault? FaultWorkOf(ulong id) => OpenFaults.FirstOrDefault(f => f.WorkerId == id);

    /// <summary>A line for the toilet or tap inspector while something is wrong with it.</summary>
    public string? FaultStatus(string facilityId)
    {
        if (OpenFaults.FirstOrDefault(f => f.FacilityId == facilityId) is not { } fault) return null;
        string Who(ulong id) => PersonIn(PersonView.Roster, id)?.Name ?? "someone";
        var help = fault.WorkerId is { } worker ? $"{Who(worker)} {(fault.WorkStartedTick >= 0 ? "is on it" : "is on the way")}" : "waiting for help";
        return fault switch
        {
            { Kind: FacilityFaultKind.StuckInToilet } when Unconscious(fault.VictimId) => $"COLLAPSED INSIDE • {Who(fault.VictimId)} needs the door opened for first aid • {help}",
            { Kind: FacilityFaultKind.StuckInToilet } when ToiletToxic(fault.FacilityId) => $"TOXIC • {Who(fault.VictimId)} is choking on the fumes ({(CurrentTick - fault.StartedTick) / 80}s of {FaultRules.PoisonCollapseTicks / 80}s) • {help}",
            { Kind: FacilityFaultKind.StuckInToilet } => $"STUCK • {Who(fault.VictimId)} can't get out ({(CurrentTick - fault.StartedTick) / 80}s) • {help}",
            { Kind: FacilityFaultKind.ChewedCable } => $"CABLE CHEWED • no power until it's spliced • {(_equipment?.WorkerId is null ? "needs a maintenance worker" : help)}",
            { Kind: FacilityFaultKind.BrokenGate } => $"GATE BROKEN • cows can get out • {(_equipment?.WorkerId is null ? "needs a maintenance worker" : help)}",
            { Stage: FacilityFaultStage.Active } => $"BROKEN • no water until mended • {(_equipment?.WorkerId is null && fault.WorkerId is null ? "a steward can bodge it" : help)}",
            _ => $"BODGED • half flow{(_equipment?.WorkerId is null ? "" : fault.WorkerId is null ? " • maintenance will mend it" : $" • {help}")}",
        };
    }

    private void SetFault(FacilityFault fault) => _faults = _faults! with
        { Faults = _faults.Faults.Select(f => f.Id == fault.Id ? fault : f).ToArray() };

    /// <param name="id">Names the use that caused it, so the same use can never fault twice.</param>
    private FacilityFault AddFault(string id, FacilityFaultKind kind, string facilityId, ulong victimId)
    {
        _faults ??= EmptyFaults;
        var fault = new FacilityFault(id, kind, facilityId, victimId, CurrentTick, FacilityFaultStage.Active);
        // A tap that breaks again loses its earlier bodge: the new break needs mending from scratch.
        foreach (var bodge in _faults.Faults.Where(f => f.FacilityId == facilityId && f.Stage == FacilityFaultStage.Bodged && f.WorkerId is not null))
            SendWorkerBack(bodge.WorkerId!.Value);
        _faults = _faults with { Faults = _faults.Faults
            .Select(f => f.FacilityId == facilityId && f.Stage == FacilityFaultStage.Bodged ? f with { Stage = FacilityFaultStage.Fixed, WorkerId = null, WorkStartedTick = -1 } : f)
            .Append(fault).ToArray() };
        return fault;
    }

    /// <summary>
    /// Called as a toilet visit finishes. True while the occupant cannot get out: an existing jam,
    /// or a fresh one from this visit's roll.
    /// </summary>
    private bool ToiletDoorJammed(ToiletFacility toilet, ulong occupant)
    {
        if (ActiveFault(toilet.Id) is { Kind: FacilityFaultKind.StuckInToilet } jam) return jam.VictimId == occupant;
        // The visit count is unchanged until they leave, so a freed occupant's own visit is not rolled again.
        var visit = toilet.WeeCount + toilet.PooCount;
        var id = $"stuck:{toilet.Id}:{visit}";
        if (_faults is null or { Disabled: true } || _faults.Faults.Any(f => f.Id == id) ||
            !FaultRules.Roll(CampaignSeed, toilet.Id, visit, (ulong)_preparation!.Attempt, FaultRules.ToiletStuckChancePer10k)) return false;
        AddFault(id, FacilityFaultKind.StuckInToilet, toilet.Id, occupant);
        MutatePerson(occupant, person => person.Reason = "Stuck in the toilet: the lock has jammed");
        MedicalEvent("fault:stuck", $"Person {occupant} is stuck in {toilet.Id}; a steward is needed to free them.");
        return true;
    }

    /// <summary>Called as someone starts drinking. True when the tap breaks in their hands.</summary>
    private bool TapBreaksOnUse(WaterPointState point, ulong drinker)
    {
        if (_faults is null or { Disabled: true } || !FaultRules.Roll(CampaignSeed, point.Id, CurrentTick, drinker, FaultRules.TapBreakChancePer10k)) return false;
        AddFault($"broken:{point.Id}:{CurrentTick}", FacilityFaultKind.BrokenTap, point.Id, drinker);
        var loss = UnpleasantFor(drinker, FaultRules.BrokenTapSatisfactionLoss);
        ChangeSatisfaction(drinker, -loss, MoodCause.BrokenTap);
        MedicalEvent("fault:broken-tap", $"{point.Id} broke as person {drinker} used it; it needs mending before anyone else can drink there.");
        return true;
    }

    /// <summary>Extra time someone weighing this facility expects before it serves again, if it is out of action.</summary>
    private int FaultDelayTicks(string facilityId)
    {
        if (ActiveFault(facilityId) is not { } fault) return 0;
        var work = fault.Kind == FacilityFaultKind.StuckInToilet ? FaultRules.RescueTicks :
            _equipment?.WorkerId is not null ? FaultRules.RepairTicks : FaultRules.BodgeTicks;
        if (fault.WorkerId is not { } worker) return FaultRules.UnassignedWaitTicks + work;
        if (fault.WorkStartedTick >= 0) return (int)Math.Max(0, work - (CurrentTick - fault.WorkStartedTick));
        return (EstimateStaffTravelTicks(worker) ?? FaultRules.UnassignedWaitTicks) + work;
    }

    /// <summary>Where a worker stands to mend a tap: a walkable side away from its queue; or right at a jammed toilet's door.</summary>
    private GridCell? FaultWorkCell(FacilityFault fault, ulong worker)
    {
        var here = TraversalGrid.WorldToCell(_navigationAgents[new(worker)].XMillimetres, _navigationAgents[new(worker)].ZMillimetres);
        if (CowFault(fault.Kind))
        {
            var spot = CowFaultCell(fault);
            return DeterministicPathfinder.FindPath(_traversalGrid!, here, spot).Found ? spot : null;
        }
        if (fault.Kind == FacilityFaultKind.StuckInToilet)
        {
            // At the door if it can be reached, else the exit spot beside it, so a steward never loops on no route.
            var toilet = GetToilet(fault.FacilityId);
            var door = ToiletDoorFrontCell(toilet);
            return DeterministicPathfinder.FindPath(_traversalGrid!, here, door).Found ? door : ToiletExitCell(toilet);
        }
        if (WaterPoints().SingleOrDefault(p => p.Id == fault.FacilityId) is not { } point) return null;
        var avoid = point.QueueCells.Append(WaterSlot(point, 0)).Append(WaterApproach(point)).ToHashSet();
        return new[] { new GridCell(point.Cell.X, point.Cell.Z + 2), new(point.Cell.X + 2, point.Cell.Z), new(point.Cell.X, point.Cell.Z - 2), new(point.Cell.X - 2, point.Cell.Z) }
            .Where(c => !avoid.Contains(c) && _traversalGrid!.Contains(c) && _traversalGrid.Get(c).IsWalkable)
            .OrderBy(c => (long)(c.X - here.X) * (c.X - here.X) + (long)(c.Z - here.Z) * (c.Z - here.Z)).ThenBy(c => c.X).ThenBy(c => c.Z)
            .Where(c => DeterministicPathfinder.FindPath(_traversalGrid!, here, c).Found).Select(c => (GridCell?)c).FirstOrDefault();
    }

    private GridCell CowFaultCell(FacilityFault fault) => fault.Kind == FacilityFaultKind.BrokenGate ? CowRules.GateInside :
        CableSpots().Where(spot => "cable." + spot.Utility == fault.FacilityId).Select(spot => spot.Spot).DefaultIfEmpty(CowRules.GateInside).First();

    private static string FaultIntent(FacilityFault fault, bool maintenance) =>
        fault.Kind == FacilityFaultKind.StuckInToilet ? "fault.rescue" : maintenance ? "fault.repair" : "fault.bodge";

    private void AdvanceFacilityFaults()
    {
        if (_faults is null || _preparation is null) return;
        if (_preparation.Status != PreparationStatus.Running)
        {
            // Departure takes everyone's route: workers are released, and what is broken stays broken.
            foreach (var held in OpenFaults.Where(f => f.WorkerId is not null).ToArray()) SetFault(held with { WorkerId = null, WorkStartedTick = -1 });
            // A death froze the edition: the scene stays as found, door shut, for the hearing.
            if (_preparation.Status == PreparationStatus.Failed) return;
            // The end of the day opens every jammed door: the toilet sends every occupant out.
            foreach (var jam in OpenFaults.Where(f => f.Kind == FacilityFaultKind.StuckInToilet && f.Stage == FacilityFaultStage.Active).ToArray())
                ResolveFault(jam, FacilityFaultStage.Fixed);
            return;
        }
        foreach (var original in OpenFaults.ToArray())
        {
            var fault = original;
            if (fault.Kind == FacilityFaultKind.StuckInToilet && fault.Stage == FacilityFaultStage.Active)
            {
                // The jam ends if anything else has got them out (another emergency, the day ending).
                var toilet = GetToilet(fault.FacilityId);
                if (toilet.OwnerId != fault.VictimId || _persons[fault.VictimId].ToiletStage != ToiletVisitStage.Using)
                { ResolveFault(fault, FacilityFaultStage.Fixed); continue; }
                // Panic costs satisfaction, more in a toxic cubicle. A collapse comes from heat and thirst (a hot cubicle
                // helps), or from the fumes of a nearly full one.
                var stuck = CurrentTick - fault.StartedTick;
                var toxic = ToiletToxic(fault.FacilityId);
                if (stuck > 0 && stuck % 80 == 0 && !Unconscious(fault.VictimId))
                {
                    var loss = UnpleasantFor(fault.VictimId, FaultRules.PanicLossPerSecond(stuck) + (toxic ? FaultRules.PoisonLossPerSecond : 0));
                    ChangeSatisfaction(fault.VictimId, -loss, MoodCause.StuckInToilet);
                }
                // Guests and performers collapse; on-duty staff ride it out, as their own health track only rests them.
                if (toxic && stuck >= FaultRules.PoisonCollapseTicks && fault.PoisonedTick < 0 && !Unconscious(fault.VictimId) &&
                    _persons[fault.VictimId].NeedProfile != MedicalNeedProfile.Staff)
                    OvercomeByFumes(fault);
            }
            if (fault.Stage == FacilityFaultStage.Bodged && _equipment?.WorkerId is null) continue;
            AdvanceFaultWorker(fault);
        }
    }

    private void AdvanceFaultWorker(FacilityFault fault)
    {
        var maintenance = _equipment?.WorkerId;
        if (fault.WorkerId is { } worker)
        {
            var nav = _navigationAgents[new(worker)];
            // Anything else that takes the worker's route (a fight, a generator call) releases the job.
            if (nav.IntentId?.StartsWith("fault.", StringComparison.Ordinal) != true || PersonCollapsed(worker) ||
                PersonIn(PersonView.Roster, worker) is not { Admitted: true, Departed: false } || nav.Action == AgentNavigationAction.NoRoute)
            { SetFault(fault with { WorkerId = null, WorkStartedTick = -1 }); return; }
            if (nav.Action != AgentNavigationAction.Arrived || nav.Destination is not { } cell ||
                (nav.XMillimetres, nav.ZMillimetres) != TraversalGrid.CellCentre(cell)) return;
            if (fault.WorkStartedTick < 0) { SetFault(fault with { WorkStartedTick = CurrentTick }); return; }
            var mending = worker == maintenance;
            var duration = fault.Kind == FacilityFaultKind.StuckInToilet ? FaultRules.RescueTicks : mending ? FaultRules.RepairTicks : FaultRules.BodgeTicks;
            if (CurrentTick - fault.WorkStartedTick < duration) return;
            ResolveFault(fault, fault.Kind == FacilityFaultKind.BrokenTap && !mending ? FacilityFaultStage.Bodged : FacilityFaultStage.Fixed);
            return;
        }
        if (CurrentTick % 8 != 0) return;
        // Freeing someone is for stewards. A broken tap is mended by maintenance; without a maintenance
        // worker, a steward bodges it. A bodged tap waits for maintenance to mend it properly.
        IEnumerable<ulong> candidates = CowFault(fault.Kind) ? maintenance is { } mender && _equipment!.JobStage == MaintenanceStage.None ? [mender] : []
            : fault.Kind == FacilityFaultKind.StuckInToilet || fault.Stage == FacilityFaultStage.Active && maintenance is null
            ? GetStewardResponses().Select(s => s.WorkerId)
            : maintenance is { } m && _equipment!.JobStage == MaintenanceStage.None ? [m] : [];
        var facilityCell = CowFault(fault.Kind) ? CowFaultCell(fault) : fault.Kind == FacilityFaultKind.StuckInToilet ? GetToilet(fault.FacilityId).Cell : WaterPoints().Single(p => p.Id == fault.FacilityId).Cell;
        foreach (var id in candidates.Where(id => StaffUnavailableReason(id) is null && !FaultWorkOwns(id) && !WasteOwnsNavigation(id))
                     .OrderBy(id => { var n = _navigationAgents[new(id)]; var c = TraversalGrid.CellCentre(facilityCell);
                         long dx = n.XMillimetres - c.XMillimetres, dz = n.ZMillimetres - c.ZMillimetres; return dx * dx + dz * dz; })
                     .ThenBy(id => id))
        {
            if (FaultWorkCell(fault, id) is not { } cell) continue;
            RecallWorker(id, "Called to a facility fault");
            SetFault(fault with { WorkerId = id, WorkStartedTick = -1 });
            ApplyAgentDestination(new(id), new(cell, FaultIntent(fault, id == maintenance)));
            return;
        }
    }

    /// <summary>A worker whose fault is over, however it ended, heads back rather than standing at the door or tap.</summary>
    private void SendWorkerBack(ulong worker)
    {
        if (_navigationAgents[new(worker)].IntentId?.StartsWith("fault.", StringComparison.Ordinal) == true) ReturnToListening(worker);
    }

    /// <summary>
    /// The door opens on someone collapsed inside. Their visit counts, the cubicle frees up, and the body
    /// is moved just outside the door, where the medic can reach them.
    /// </summary>
    private void CarryOutCollapsed(FacilityFault fault)
    {
        var id = fault.VictimId;
        var toilet = GetToilet(fault.FacilityId);
        var person = _persons[id];
        var poo = person.ToiletChoice == ToiletVisitKind.Poo;
        SetToilet(toilet with { WeeCount = toilet.WeeCount + (poo ? 0 : 1), PooCount = toilet.PooCount + (poo ? 1 : 0) });
        SetConsumption(person with { ToiletVisits = person.ToiletVisits + 1, ToiletNeed = 1_000 });
        ReleaseToiletPerson(id, false);
        var outside = ToiletExitCell(GetToilet(fault.FacilityId));
        var centre = TraversalGrid.CellCentre(outside);
        var nav = _navigationAgents[new(id)];
        nav.XMillimetres = centre.XMillimetres; nav.ZMillimetres = centre.ZMillimetres;
        nav.SegmentOriginXMillimetres = centre.XMillimetres; nav.SegmentOriginZMillimetres = centre.ZMillimetres;
        nav.Route = []; nav.RouteIndex = 0; nav.SegmentProgressMicrometres = 0; nav.MovementRemainder = 0;
        nav.Action = AgentNavigationAction.Arrived; nav.Destination = outside; nav.IntentId = "medical.collapsed";
        MedicalEvent("fault:carried-out", $"Person {id} found collapsed in {fault.FacilityId} and moved just outside for first aid.");
    }

    private void ResolveFault(FacilityFault fault, FacilityFaultStage stage)
    {
        if (fault.WorkerId is { } worker) { SetFault(fault with { WorkerId = null, WorkStartedTick = -1 }); SendWorkerBack(worker); }
        var resolved = fault with { Stage = stage, ResolvedTick = CurrentTick, WorkerId = null, WorkStartedTick = -1 };
        SetFault(resolved);
        if (fault.Kind == FacilityFaultKind.BrokenGate && _cows is { } cows) _cows = cows with { Gate = PastureGateState.Repaired };
        if (fault.Kind == FacilityFaultKind.StuckInToilet && GetToilet(fault.FacilityId).OwnerId == fault.VictimId && Unconscious(fault.VictimId))
            CarryOutCollapsed(fault);
        MedicalEvent(stage == FacilityFaultStage.Bodged ? "fault:bodged" : "fault:fixed",
            stage == FacilityFaultStage.Bodged ? $"{fault.FacilityId} bodged back into service at reduced flow." : $"{fault.FacilityId}: {fault.Kind} resolved.");
    }

    private static string? ValidatePersistedFaults(SessionPersistenceSnapshot s)
    {
        if (s.Faults is null) return s.Preparation?.Plan is null ? null : "Current Build save requires facility fault state.";
        if (s.Faults.Version != 1 || s.Faults.Faults is null || s.Faults.Faults.Any(f => f is null) || s.Preparation is not { } prep)
            return "Facility fault state shape invalid.";
        var faults = s.Faults.Faults;
        if (prep.Status == PreparationStatus.Preparing && faults.Length != 0) return "Draft/retry must clear facility faults.";
        // Disabled is a labelled test fixture. A save can carry it only with no faults recorded; play never sets it.
        if (s.Faults.Disabled && faults.Length != 0) return "A fault-free fixture holds no faults.";
        var toilets = (s.Facilities?.Toilets ?? []).ToDictionary(t => t.Id);
        var taps = (s.Facilities?.Taps ?? []).Select(t => t.Id).ToHashSet();
        var stewards = (s.Disorder?.Stewards ?? []).Select(w => w.WorkerId).ToHashSet();
        var maintenance = s.Equipment?.WorkerId;
        var occupants = (s.Immersion?.People ?? []).ToDictionary(p => p.AgentId);
        var tapOwners = (s.Facilities?.Taps ?? []).ToDictionary(t => t.Id, t => t.OwnerId);
        PersistedNavigationAgent? Nav(ulong id) => s.NavigationAgents?.SingleOrDefault(n => n.Id == id);
        bool Working(ulong id) => Nav(id)?.IntentId?.StartsWith("fault.", StringComparison.Ordinal) == true;
        // The worker's route names the job, and only the right role does it: stewards free people and bodge
        // taps when no maintenance worker is hired; maintenance mends taps.
        string ExpectedIntent(FacilityFault f, ulong worker) => f.Kind == FacilityFaultKind.StuckInToilet ? "fault.rescue" : worker == maintenance ? "fault.repair" : "fault.bodge";
        bool RightWorker(FacilityFault f, ulong worker) => CowFault(f.Kind) ? worker == maintenance : f.Kind == FacilityFaultKind.StuckInToilet ? stewards.Contains(worker) :
            f.Stage == FacilityFaultStage.Bodged || maintenance is not null ? worker == maintenance : stewards.Contains(worker);
        bool Standing(ulong id) => Nav(id) is { Action: (int)AgentNavigationAction.Arrived, DestinationX: { } x, DestinationZ: { } z } n &&
            (n.XMillimetres, n.ZMillimetres) == TraversalGrid.CellCentre(new GridCell(x, z));
        if (faults.Select(f => f.Id).Distinct().Count() != faults.Length ||
            faults.Any(f => !Enum.IsDefined(f.Kind) || !Enum.IsDefined(f.Stage) || f.StartedTick < prep.StartedTick || f.StartedTick > s.CurrentTick ||
                (CowFault(f.Kind) ? f.VictimId != 0 || f.Stage == FacilityFaultStage.Bodged ||
                    !(f.Kind == FacilityFaultKind.BrokenGate ? f.FacilityId == "gate.pasture" : (f.FacilityId == "cable.generator" || f.FacilityId.StartsWith("cable.", StringComparison.Ordinal) && s.Facilities?.Vendors?.Any(v => v.Id == f.FacilityId["cable.".Length..]) == true))
                : !prep.People.Any(p => p.AgentId == f.VictimId) ||
                (f.Kind == FacilityFaultKind.StuckInToilet ? !toilets.ContainsKey(f.FacilityId) || f.Stage == FacilityFaultStage.Bodged : !taps.Contains(f.FacilityId))) ||
                (f.Stage == FacilityFaultStage.Active) != (f.ResolvedTick < 0) || f.ResolvedTick >= 0 && (f.ResolvedTick < f.StartedTick || f.ResolvedTick > s.CurrentTick) ||
                f.Stage == FacilityFaultStage.Fixed && (f.WorkerId is not null || f.WorkStartedTick != -1) ||
                f.WorkerId is { } worker && (!RightWorker(f, worker) || !Working(worker) || Nav(worker)!.IntentId != ExpectedIntent(f, worker) ||
                    f.WorkStartedTick >= 0 && !Standing(worker)) ||
                f.WorkerId is null && f.WorkStartedTick != -1 || f.WorkStartedTick > s.CurrentTick || f.WorkStartedTick >= 0 && f.WorkStartedTick < f.StartedTick ||
                f.PoisonedTick != -1 && (f.Kind != FacilityFaultKind.StuckInToilet || f.PoisonedTick < f.StartedTick + FaultRules.PoisonCollapseTicks || f.PoisonedTick > s.CurrentTick) ||
                f.Stage == FacilityFaultStage.Bodged && f.WorkerId is { } mender && mender != maintenance ||
                f.Stage == FacilityFaultStage.Active && f.Kind == FacilityFaultKind.StuckInToilet &&
                    (toilets[f.FacilityId].OwnerId != f.VictimId || occupants.GetValueOrDefault(f.VictimId)?.ToiletStage != ToiletVisitStage.Using) ||
                f.Stage == FacilityFaultStage.Active && f.Kind == FacilityFaultKind.BrokenTap && tapOwners.GetValueOrDefault(f.FacilityId) is not null) ||
            faults.Where(f => f.Stage != FacilityFaultStage.Fixed).GroupBy(f => f.FacilityId).Any(g => g.Count() > 1) ||
            faults.Where(f => f.WorkerId is not null).GroupBy(f => f.WorkerId).Any(g => g.Count() > 1))
            return "Facility fault identity, stage or worker invalid.";
        return null;
    }
}

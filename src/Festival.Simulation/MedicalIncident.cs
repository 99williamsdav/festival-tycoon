using System.Text.Json;

namespace Festival.Simulation;

public enum MedicalStage { Clear, Distress, Collapsed, Critical, Treated, Removed, Terminal }
public enum MedicalIntent { WatchShow, SeekWater, Rest, AwaitMedic, Leaving, Collapsed, Drinking }
public enum MedicalResponseStage { None, Travelling, Treating, Removing, Completed }
public enum MedicalAction { GuideToWater, GuideToRest, DispatchMedic, SafeRemove, ReturnToShow }
public enum MedicalNeedProfile { Guest, Performer, Staff }
public sealed record MedicalCommand(ulong GuestId, MedicalAction Action, ulong? WorkerId = null) : SessionCommand;
public sealed record MedicResponse(ulong WorkerId, MedicalResponseStage Stage, ulong? PatientId, long StartedTick, string Description, long DispatchedTick = -1);
public sealed record MedicalNeed(ulong AgentId, int Thirst, int HeatExposure, MedicalIntent Intent,
    string Reason, long LastDecisionTick, int? QueueSlot, long LastWaterTick,
    MedicalNeedProfile Profile = MedicalNeedProfile.Guest, MedicalStage Stage = MedicalStage.Clear,
    long WarningTick = -1, long CollapseTick = -1, long CriticalTick = -1)
{
    public string WaterPointId { get; init; } = "water.main";
    public long LastWaterChoiceReviewTick { get; init; } = -160;
}
public sealed record MedicalEvidence(string Id, long Tick, string Description);
public sealed record WaterPointState(string Id, GridCell Cell, ulong[] Queue, ulong[] Overflow, ulong? OwnerId, int DrinkTicks)
{
    public int QuarterTurns { get; init; }
    public int GeometryVersion { get; init; }
    public GridCell[] QueueCells { get; init; } = [];
}
public sealed record MedicalSnapshot(int Version, bool IsHot, ulong MedicId, ulong AtRiskGuestId,
    MedicalNeed[] Needs, ulong[] WaterQueue, ulong[] WaterOverflow, ulong? WaterOwnerId, int WaterDrinkTicks,
    MedicalStage Stage, MedicalResponseStage ResponseStage, ulong? ResponsePatientId, long WarningTick, long CollapseTick,
    long CriticalTick, long ResponseStartedTick, string Response, MedicalEvidence[] Evidence)
{
    public GridCell MainWaterCell { get; init; } = GameSession.MedicalWaterCell;
    public int MainWaterQuarterTurns { get; init; }
    public int MainWaterGeometryVersion { get; init; }
    public GridCell[] MainWaterQueueCells { get; init; } = [];
    public WaterPointState[] ExtraWaterPoints { get; init; } = [];
    public MedicResponse[] ExtraResponses { get; init; } = [];
    public long ResponseDispatchedTick { get; init; } = -1;
    public StaffInterventionJob[] StaffInterventions { get; init; } = [];
    public bool DevelopmentInterventionFixturesEnabled { get; init; }
}

public sealed partial class GameSession
{
    // Prototype Hot scenario, not clinical thresholds or a general weather model.
    public const int MedicalDrinkThirstPerTick = 16;      // Continuous relief, not a fixed service timer.
    public const int MedicalDrinkHeatPerTick = 4;
    // Stable per-person service pace. Tap count never divides or reduces this rate.
    public static int MedicalDrinkThirstPerTickFor(ulong agentId) => 8 + (int)(agentId % 4) * 4;
    public static int MedicalDrinkHeatPerTickFor(ulong agentId) => MedicalDrinkThirstPerTickFor(agentId) / 4;
    public int EffectiveMedicalDrinkThirstPerTickFor(ulong agentId) =>
        (CommunityWaterShareActive ? Math.Min(12, MedicalDrinkThirstPerTickFor(agentId)) : MedicalDrinkThirstPerTickFor(agentId)) +
        (_preparation?.WaterTowerOwned == true ? 4 : 0);
    public int EffectiveMedicalDrinkHeatPerTickFor(ulong agentId) => EffectiveMedicalDrinkThirstPerTickFor(agentId) / 4;
    public const int MedicalDecisionCooldownTicks = 240;   // 3 real seconds at 1×.
    public const int WaterChoiceReviewTicks = 160;
    public const int WaterChoiceSwitchMarginTicks = 120;
    public const int MedicalCollapseDelayTicks = 1_600;   // 20 real seconds after distress.
    public const int MedicalCriticalDelayTicks = 800;     // 10 real seconds after collapse.
    public const int MedicalDeathDelayTicks = 2_400;      // 30 real seconds after collapse.
    public const int MedicalTreatmentTicks = 480;        // 6 real seconds after physical arrival.
    public const int MedicalDistressThirst = 9_000;
    public const int MedicalDistressHeat = 8_000;
    public static readonly GridCell MedicalWaterCell = new(95, 123);    // (-16.25, -2.25) m; upper-right overview, away from the audience.
    public static readonly GridCell WaterTowerCell = TraversalGrid.WorldToCell(-12_300, -14_000);
    public static readonly (string Id, GridCell Cell)[] ExtraWaterSites =
    [
        ("water.west", new GridCell(74, 123)),
        ("water.east", new GridCell(116, 143))
    ];
    public static readonly GridCell MedicalTentCell = new(116, 119);    // (-5.75, -4.25) m; tent frontage aligns with the water point.
    public static readonly GridCell MedicalMedicCell = new(116, 125);   // (-5.75, -1.25) m; Riley stands in front of the tent.
    public static readonly GridCell MedicalRestCell = new(120, 125);    // (-3.75, -1.25) m; beside the tent's new front approach.
    public static readonly GridCell MedicalExitCell = new(128, 186);    // (0.25, 29.25) m.
    // One compact line behind the single tap, with a slight human offset and no branches.
    // Slot zero alone owns the tap. Approaching the tail does not reserve a slot.
    private static readonly GridCell[] WaterSlots =
    [
        new(95, 128), new(95, 130), new(96, 132), new(97, 134), new(98, 136),
        new(99, 138), new(101, 140), new(102, 142), new(103, 144), new(104, 146)
    ];
    private static readonly GridCell[] WaterOverflowSlots =
    [
        new(105, 148), new(106, 150), new(107, 152), new(108, 154), new(109, 156),
        new(110, 158), new(111, 160), new(112, 162), new(113, 164), new(114, 166)
    ];
    private IReadOnlyList<WaterPointState> WaterPoints() => _medical is not { } m ? [] :
        [new WaterPointState("water.main", m.MainWaterCell, m.WaterQueue, m.WaterOverflow, m.WaterOwnerId, m.WaterDrinkTicks)
            { QuarterTurns = m.MainWaterQuarterTurns, GeometryVersion = m.MainWaterGeometryVersion, QueueCells = m.MainWaterQueueCells }, .. m.ExtraWaterPoints];
    public IReadOnlyList<WaterPointState> CaptureWaterPoints() => WaterPoints().Select(point =>
        point with { Queue = point.Queue.ToArray(), Overflow = point.Overflow.ToArray(), QueueCells = point.QueueCells.ToArray() }).ToArray();
    public static GridCell RotateWaterOffset(GridCell offset, int quarterTurns) => quarterTurns switch
    { 0 => offset, 1 => new(offset.Z, -offset.X), 2 => new(-offset.X, -offset.Z), 3 => new(-offset.Z, offset.X), _ => throw new ArgumentOutOfRangeException(nameof(quarterTurns)) };
    public static GridCell WaterServiceCell(GridCell centre, int quarterTurns = 0, int geometryVersion = 1)
    {
        var offset = RotateWaterOffset(new(0, geometryVersion == 1 ? 2 : 5), quarterTurns);
        return new(centre.X + offset.X, centre.Z + offset.Z);
    }
    public static GridCell WaterPointServiceCell(WaterPointState point) => WaterServiceCell(point.Cell, point.QuarterTurns, point.GeometryVersion);
    private static int WaterFootprintRadius(WaterPointState point) => point.GeometryVersion == 1 ? 1 : 3;
    public IReadOnlyList<GridCell> CaptureWaterQueueCells(string pointId)
    {
        var point = WaterPoints().Single(item => item.Id == pointId);
        return point.QueueCells.Length > 0 ? point.QueueCells.ToArray() :
            Enumerable.Range(0, Math.Min(20, point.Queue.Length + point.Overflow.Length + 1)).Select(index => index < 10 ? WaterSlot(point, index) : WaterOverflowSlot(point, index - 10)).ToArray();
    }
    private static GridCell WaterSlot(WaterPointState point, int index) =>
        index < point.QueueCells.Length ? point.QueueCells[index] : LegacyWaterSlot(point, index);
    private static GridCell LegacyWaterSlot(WaterPointState point, int index)
    {
        var offset = point.Id == "water.main" && point.Cell == MedicalWaterCell && point.QuarterTurns == 0
            ? new GridCell(WaterSlots[index].X - point.Cell.X, WaterSlots[index].Z - point.Cell.Z) : new GridCell(index / 2, 5 + index * 2);
        if (point.GeometryVersion == 1) offset = new(offset.X, offset.Z - 3);
        offset = RotateWaterOffset(offset, point.QuarterTurns);
        return new(point.Cell.X + offset.X, point.Cell.Z + offset.Z);
    }
    private static GridCell WaterOverflowSlot(WaterPointState point, int index) =>
        index + 10 < point.QueueCells.Length ? point.QueueCells[index + 10] : LegacyWaterOverflowSlot(point, index);
    private static GridCell LegacyWaterOverflowSlot(WaterPointState point, int index)
    {
        var offset = point.Id == "water.main" && point.Cell == MedicalWaterCell && point.QuarterTurns == 0
            ? new GridCell(WaterOverflowSlots[index].X - point.Cell.X, WaterOverflowSlots[index].Z - point.Cell.Z) : new GridCell(5 + index / 2, 25 + index * 2);
        if (point.GeometryVersion == 1) offset = new(offset.X, offset.Z - 3);
        offset = RotateWaterOffset(offset, point.QuarterTurns);
        return new(point.Cell.X + offset.X, point.Cell.Z + offset.Z);
    }
    private static GridCell WaterApproach(WaterPointState point) => point.Queue.Length < 10
        ? WaterSlot(point, point.Queue.Length) : WaterOverflowSlot(point, Math.Min(point.Overflow.Length, 9));
    private void SetWaterPoint(WaterPointState point)
    {
        var m = _medical!;
        _medical = point.Id == "water.main"
            ? m with { WaterQueue = point.Queue, WaterOverflow = point.Overflow, WaterOwnerId = point.OwnerId, WaterDrinkTicks = point.DrinkTicks, MainWaterQueueCells = point.QueueCells }
            : m with { ExtraWaterPoints = m.ExtraWaterPoints.Select(item => item.Id == point.Id ? point : item).ToArray() };
    }
    private WaterPointState WaterPointFor(ulong id) => WaterPoints().Single(point => point.Id ==
        _medical!.Needs.Single(item => item.AgentId == id).WaterPointId);
    private int EstimateWaterWalkTicks(ulong id, WaterPointState point)
    {
        var nav = _navigationAgents[new(id)];
        var from = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
        var destination = WaterApproach(point);
        IReadOnlyList<GridCell> remaining;
        if (nav.Destination == destination && nav.Action == AgentNavigationAction.Arrived) return 0;
        if (nav.Destination == destination && nav.Action == AgentNavigationAction.Travelling &&
            nav.RouteIndex >= 0 && nav.RouteIndex < nav.Route.Count)
            remaining = nav.Route.Skip(nav.RouteIndex).ToArray();
        else
        {
            var search = DeterministicPathfinder.FindPath(_traversalGrid!, from, destination);
            if (!search.Found) return int.MaxValue;
            remaining = search.Path.Skip(1).ToArray();
        }
        var x = nav.XMillimetres; var z = nav.ZMillimetres;
        long weightedMicrometres = 0;
        foreach (var cell in remaining)
        {
            var centre = TraversalGrid.CellCentre(cell);
            var dx = centre.XMillimetres - x; var dz = centre.ZMillimetres - z;
            var segment = IntegerSquareRoot((long)dx * dx * 1_000_000L + (long)dz * dz * 1_000_000L);
            weightedMicrometres += segment * _traversalGrid!.Get(cell).CostPermille / 1000;
            x = centre.XMillimetres; z = centre.ZMillimetres;
        }
        var perTick = (long)RouteProgressMicrometresPerTick * nav.WalkingSpeedPermille / 1000;
        return checked((int)((weightedMicrometres + perTick - 1) / perTick));
    }

    private int EstimateWaterWaitTicks(WaterPointState point) => point.Queue.Concat(point.Overflow).Sum(id =>
    {
        var thirst = _medical!.Needs.Single(need => need.AgentId == id).Thirst;
        var rate = EffectiveMedicalDrinkThirstPerTickFor(id);
        return (thirst + rate - 1) / rate;
    });

    private int EstimateWaterTotalTicks(ulong id, WaterPointState point)
    {
        if (point.QueueCells.Length > 0 && point.QueueCells.Length <= point.Queue.Length + point.Overflow.Length) return int.MaxValue;
        var walk = EstimateWaterWalkTicks(id, point);
        if (walk == int.MaxValue) return walk;
        var own = _medical!.Needs.Single(need => need.AgentId == id).Thirst;
        var rate = EffectiveMedicalDrinkThirstPerTickFor(id);
        return checked(walk + EstimateWaterWaitTicks(point) + (own + rate - 1) / rate);
    }

    private WaterPointState ChooseWaterPoint(ulong id)
    {
        var available = WaterPoints().Where(point => (point.Queue.Length < 10 || point.Overflow.Length < 10) &&
            (point.QueueCells.Length == 0 || point.QueueCells.Length > point.Queue.Length + point.Overflow.Length)).ToArray();
        return (available.Length > 0 ? available : WaterPoints().ToArray())
            .OrderBy(point => EstimateWaterTotalTicks(id, point))
            .ThenBy(point => point.Id, StringComparer.Ordinal).First();
    }
    public static GridCell MedicalQueueSlot(int index) => WaterSlots[index];
    public static GridCell MedicalQueueApproach(int queuedCount, int overflowCount = 0) =>
        queuedCount < WaterSlots.Length ? WaterSlots[queuedCount] : WaterOverflowSlots[Math.Min(overflowCount, WaterOverflowSlots.Length - 1)];
    private bool MedicalQueueExcludesListening(GridCell cell) => _medical is not null &&
        WaterPoints().Any(point => CaptureWaterQueueCells(point.Id)
            .Any(slot => Math.Abs(cell.X - slot.X) <= 2 && Math.Abs(cell.Z - slot.Z) <= 2));

    private MedicalSnapshot? _medical;
    private bool MedicalOwnsNavigation(ulong id) => _medical is { } m &&
        (WaterPoints().Any(point => point.Queue.Contains(id)) || m.Needs.Any(item => item.AgentId == id &&
            item.Intent is MedicalIntent.SeekWater or MedicalIntent.Rest or MedicalIntent.AwaitMedic or MedicalIntent.Leaving or MedicalIntent.Collapsed));
    public MedicalSnapshot? CaptureMedical() => _medical is null ? null : JsonSerializer.Deserialize<MedicalSnapshot>(JsonSerializer.Serialize(_medical));
    internal string? MedicalCanonicalJson => _medical is not { } m ? null : StaffCompatibleCanonicalJson(m,
        m.ExtraResponses.Length > 0 ? "" : nameof(m.ExtraResponses), m.ResponseDispatchedTick >= 0 ? "" : nameof(m.ResponseDispatchedTick),
        m.MainWaterQuarterTurns != 0 ? "" : nameof(m.MainWaterQuarterTurns), m.MainWaterQueueCells.Length > 0 ? "" : nameof(m.MainWaterQueueCells),
        m.MainWaterGeometryVersion != 0 ? "" : nameof(m.MainWaterGeometryVersion), m.StaffInterventions.Length > 0 ? "" : nameof(m.StaffInterventions),
        m.DevelopmentInterventionFixturesEnabled ? "" : nameof(m.DevelopmentInterventionFixturesEnabled));
    public bool MedicalBoundaryOnNextTick => ImmersionDepartureMedicalBoundaryOnNextTick || StaffMedicalBoundaryOnNextTick || !IsPaused && _medical is { } m && _preparation is { Status: PreparationStatus.Running } &&
        (m.Stage == MedicalStage.Clear && m.Needs.Single(item => item.AgentId == m.AtRiskGuestId).Thirst >= MedicalDistressThirst - 1 ||
         m.Stage == MedicalStage.Distress && CurrentTick + 1 >= m.WarningTick + MedicalCollapseDelayTicks ||
         m.Stage == MedicalStage.Collapsed && CurrentTick + 1 >= m.CollapseTick + MedicalCriticalDelayTicks ||
         m.Stage == MedicalStage.Critical && CurrentTick + 1 >= m.CollapseTick + MedicalDeathDelayTicks ||
         WaterPoints().Any(point => point.OwnerId is { } waterOwner &&
             m.Needs.Single(item => item.AgentId == waterOwner).Thirst <= EffectiveMedicalDrinkThirstPerTickFor(waterOwner)) ||
         m.Needs.Any(item => item.Profile == MedicalNeedProfile.Performer &&
             (item.Stage == MedicalStage.Clear && item.Thirst >= MedicalDistressThirst - 1 && item.HeatExposure >= MedicalDistressHeat - 1 ||
              item.Stage == MedicalStage.Distress && CurrentTick + 1 >= item.WarningTick + MedicalCollapseDelayTicks ||
              item.Stage == MedicalStage.Collapsed && CurrentTick + 1 >= item.CollapseTick + MedicalCriticalDelayTicks ||
              item.Stage == MedicalStage.Critical && CurrentTick + 1 >= item.CollapseTick + MedicalDeathDelayTicks)) ||
         m.ResponseStage == MedicalResponseStage.Travelling && _navigationAgents[new(m.MedicId)].Action == AgentNavigationAction.Arrived ||
         m.ResponseStage == MedicalResponseStage.Treating && (!IntoxicationCareOwns(GetMedicResponses().Single(j=>j.WorkerId==m.MedicId)) && CurrentTick + 1 >= m.ResponseStartedTick + MedicalTreatmentTicks || IntoxicationCareBoundary(GetMedicResponses().Single(j=>j.WorkerId==m.MedicId))));

    public static GameSession CreateMedicalCampaign(ulong seed, int tier = 1)
    {
        var session = CreateEquipmentCampaign(seed, tier);
        session._equipment = session._equipment! with { Stage = EquipmentStage.Resolved, LoadPercent = 80,
            Response = "Hot scenario baseline: generator load balanced before opening" };
        var medicId = session.NextEntityId++;
        session._wallets.Add(new(medicId), new WalletState { OwnerId = new(medicId), CashPennies = 500 });
        session._preparation = session._preparation! with { People = session._preparation.People.Append(
            new EditionPerson(medicId, "Riley Hart", ProtectedPersonRole.Staff, 0)).ToArray() };
        var guests = session._preparation.People.Where(item => item.Role == ProtectedPersonRole.Guest).ToArray();
        var atRisk = guests[19].AgentId;
        var needs = guests.Select((guest, index) => new MedicalNeed(guest.AgentId,
            index == 19 ? 8_500 : index < 8 ? 7_600 : 2_000 + (index * 43) % 500,
            index == 19 ? 7_500 : 2_500, MedicalIntent.WatchShow,
            index == 19 ? "Strong act interest outweighs early water trip" : "Water need below show preference",
            -MedicalDecisionCooldownTicks, null, -1))
            .Concat(session._preparation.People.Where(item => item.Role == ProtectedPersonRole.Performer)
                .Select((person, index) => new MedicalNeed(person.AgentId, 6_900 + index * 100, 6_000 + index * 100,
                    MedicalIntent.WatchShow, "Performing; free water and first aid remain available",
                    -MedicalDecisionCooldownTicks, null, -1, MedicalNeedProfile.Performer))).ToArray();
        session._medical = new(5, true, medicId, atRisk, needs, [], [], null, 0,
            MedicalStage.Clear, MedicalResponseStage.None, null, -1, -1, -1, -1, "No response",
            [new("medical:hot", 0, "Fixed Hot scenario; free water and a baseline medic are available before opening.")]) { MainWaterGeometryVersion = 1 };
        session._preparation = session._preparation! with { PrimaryWaterGeometryVersion = 1 };
        return session;
    }

    private void MedicalEvent(string id, string description) => _medical = _medical! with
    { Evidence = _medical.Evidence.Append(new MedicalEvidence(id, CurrentTick, description)).ToArray() };

    private bool MedicalRouteExists(ulong agentId, GridCell destination)
    {
        var agent = _navigationAgents[new(agentId)];
        var origin = TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres);
        return DeterministicPathfinder.FindPath(_traversalGrid!, origin, destination).Found;
    }

    private GridCell? MedicalResponseCell(ulong medicId, ulong patientId)
    {
        var patient = _navigationAgents[new(patientId)];
        var cell = TraversalGrid.WorldToCell(patient.XMillimetres, patient.ZMillimetres);
        var bedside=PersonCollapsed(patientId);
        var offsets=bedside ? new (int X,int Z)[] { (1,0),(-1,0),(0,1),(0,-1),(0,0),(1,1),(-1,1),(1,-1),(-1,-1) }
            : [(4,0),(-4,0),(0,4),(0,-4),(3,2),(-3,2),(3,-2),(-3,-2),(2,3),(-2,3),(2,-3),(-2,-3)];
        if(bedside)offsets=offsets.OrderBy(offset=>{var centre=TraversalGrid.CellCentre(new(cell.X+offset.X,cell.Z+offset.Z));var dx=(long)centre.XMillimetres-patient.XMillimetres;var dz=(long)centre.ZMillimetres-patient.ZMillimetres;return Math.Abs(dx*dx+dz*dz-250_000);}).ToArray();
        foreach (var (dx, dz) in offsets)
        {
            var candidate = new GridCell(cell.X + dx, cell.Z + dz);
            if (!_traversalGrid!.Contains(candidate) || WaterPoints().Any(point =>
                    CaptureWaterQueueCells(point.Id).Contains(candidate)) ||
                !_traversalGrid.Get(candidate).IsWalkable || !MedicalRouteExists(medicId, candidate)) continue;
            var centre = TraversalGrid.CellCentre(candidate);
            var px = (long)centre.XMillimetres - patient.XMillimetres;
            var pz = (long)centre.ZMillimetres - patient.ZMillimetres;
            if (px * px + pz * pz <= (bedside?562_500:6_250_000) && (!bedside || px*px+pz*pz>=90_000) &&
                (!bedside || TraversalSweep.IsWalkable(_traversalGrid,centre.XMillimetres,centre.ZMillimetres,patient.XMillimetres,patient.ZMillimetres))) return candidate;
        }
        return null;
    }

    private bool MedicalTreatmentPositionValid(MedicalSnapshot m)
    {
        var medic = _navigationAgents[new(m.MedicId)];
        if (m.ResponsePatientId is not { } patientId) return false;
        var patient = _navigationAgents[new(patientId)];
        var dx = (long)medic.XMillimetres - patient.XMillimetres;
        var dz = (long)medic.ZMillimetres - patient.ZMillimetres;
        return medic.Action == AgentNavigationAction.Arrived && patient.Action == AgentNavigationAction.Arrived &&
            dx * dx + dz * dz <= (PersonCollapsed(patientId)?562_500:6_250_000) &&
            (!PersonCollapsed(patientId) || TraversalSweep.IsWalkable(_traversalGrid!,medic.XMillimetres,medic.ZMillimetres,patient.XMillimetres,patient.ZMillimetres));
    }
    private bool MedicHasValidBedsideDestination(ulong medicId,ulong patientId)
    {
        var medic=_navigationAgents[new(medicId)];var patient=_navigationAgents[new(patientId)];
        if(medic.Destination is not { } cell || !_traversalGrid!.Get(cell).IsWalkable || medic.Action==AgentNavigationAction.NoRoute)return false;
        var centre=TraversalGrid.CellCentre(cell);var dx=(long)centre.XMillimetres-patient.XMillimetres;var dz=(long)centre.ZMillimetres-patient.ZMillimetres;
        return dx*dx+dz*dz is >=90_000 and <=562_500 && TraversalSweep.IsWalkable(_traversalGrid,centre.XMillimetres,centre.ZMillimetres,patient.XMillimetres,patient.ZMillimetres);
    }

    private CommandResult? ValidateMedicalCommand(EntityId? target, MedicalCommand command, bool developmentFixture = false)
    {
        if (!Enum.IsDefined(command.Action)) return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Unknown medical action.");
        if (!developmentFixture && command.Action != MedicalAction.DispatchMedic)
            return ValidateStaffIntervention(target, LegacyMedicalIntervention(command));
        if (target is not null || _medical is not { } m || _preparation is null || !MedicalOperationsActive || !Enum.IsDefined(command.Action))
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Medical actions require a live Hot edition.");
        var need = m.Needs.SingleOrDefault(item => item.AgentId == command.GuestId);
        if (InterventionOwnsWorker(command.WorkerId ?? m.MedicId) || InterventionOwnsTarget(command.WorkerId ?? m.MedicId) || (InterventionOwnsTarget(command.GuestId) || InterventionOwnsWorker(command.GuestId)) && !PersonCollapsed(command.GuestId))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "A physical staff intervention owns this worker or target.");
        if (command.Action is MedicalAction.GuideToWater or MedicalAction.GuideToRest or MedicalAction.ReturnToShow or MedicalAction.SafeRemove &&
            GetStewardResponses().Any(item => item.WorkerId == command.GuestId && StewardBusy(item)))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "This steward owns a response; finish it before personal water/rest actions.");
        var workerId = command.WorkerId ?? m.MedicId;
        var response = GetMedicResponses().SingleOrDefault(item => item.WorkerId == workerId);
        if (command.WorkerId is not null && (command.Action != MedicalAction.DispatchMedic || response is null))
            return CommandResult.Rejected(CommandReasonCode.UnknownTarget, "Choose a contracted medic for dispatch.");
        if (need is null)
            return CommandResult.Rejected(CommandReasonCode.UnknownTarget, "The selected person has no medical needs in this edition.");
        if (_disorder?.People.Any(item => item.AgentId == command.GuestId && item.Stage == DisorderStage.Fight) == true)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter,
                "This person is in an active confrontation; first aid and egress follow its safe resolution.");
        var patientStage = IntoxicationWarning(command.GuestId) && need.Stage is MedicalStage.Clear or MedicalStage.Treated ? MedicalStage.Distress : need.Stage is MedicalStage.Collapsed or MedicalStage.Critical ? need.Stage :
            need.Profile == MedicalNeedProfile.Guest && command.GuestId == m.AtRiskGuestId ? m.Stage : need.Stage;
        if (m.Stage == MedicalStage.Terminal || patientStage is MedicalStage.Treated or MedicalStage.Removed or MedicalStage.Terminal)
            return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "This medical incident has settled.");
        if (GetMedicResponses().Any(item => MedicBusy(item) && item.PatientId == command.GuestId))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter,
                command.Action == MedicalAction.DispatchMedic ? "A medic already owns this response; allow travel and treatment to finish." :
                "An active medical response owns this guest; allow physical travel and treatment or safe removal to finish.");
        if (command.Action == MedicalAction.GuideToRest && command.GuestId != m.AtRiskGuestId && need.Profile != MedicalNeedProfile.Performer)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter,
                "This prototype's first-aid rest route is reserved for the at-risk guest; guide other guests to free water.");
        if (command.Action == MedicalAction.DispatchMedic)
        {
            if (!_preparation.People.Single(item => item.AgentId == command.GuestId).Admitted || _preparation.People.Single(item => item.AgentId == command.GuestId).Departed)
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "The patient is not physically on site.");
            if (GetStewardResponses().Any(item => StewardBusy(item) && item.TargetId == command.GuestId) &&
                patientStage is not (MedicalStage.Collapsed or MedicalStage.Critical))
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "A steward owns this target; finish the response before medical dispatch.");
            if (patientStage == MedicalStage.Clear)
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "No medical warning yet; guide the guest to free water or rest.");
            if (MedicBusy(response!))
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "This medic is busy; choose another worker or wait.");
            if (!_preparation.People.Single(item => item.AgentId == workerId).Admitted || _preparation.People.Single(item => item.AgentId == workerId).Departed)
                return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "This medic is not physically on duty; use another counter.");
        }
        if (command.Action == MedicalAction.SafeRemove && (command.GuestId != m.AtRiskGuestId ||
            m.Stage != MedicalStage.Distress || m.ResponseStage != MedicalResponseStage.None))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Safe removal requires an ambulatory distressed guest and an unowned response.");
        if (command.Action is MedicalAction.GuideToWater or MedicalAction.GuideToRest or MedicalAction.ReturnToShow &&
            patientStage is MedicalStage.Collapsed or MedicalStage.Critical)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "A collapsed guest needs physical first aid, not an ordinary destination.");
        if (command.Action == MedicalAction.ReturnToShow && need.Intent != MedicalIntent.SeekWater &&
            !WaterPoints().Any(point => point.Queue.Contains(command.GuestId)))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Only a water visitor can leave this queue or approach.");
        if (command.Action == MedicalAction.GuideToWater && need.Intent != MedicalIntent.SeekWater &&
            !WaterPoints().Any(point => point.Queue.Contains(command.GuestId)) &&
            WaterPoints().All(point => point.Queue.Length == 10 && point.Overflow.Length == 10))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "The free-water line and its visible overflow tail are full.");
        if (command.Action == MedicalAction.GuideToWater && _disorder?.WaterClosed == true)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Free water is closed; use rest, medical help or safe egress.");
        if (command.Action == MedicalAction.GuideToWater && need.Intent != MedicalIntent.SeekWater &&
            !WaterPoints().Any(point => point.Queue.Contains(command.GuestId)) &&
            !MedicalRouteExists(command.GuestId, WaterApproach(ChooseWaterPoint(command.GuestId))))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "No walkable route to free water; clear the path or choose first aid/rest.");
        if (command.Action == MedicalAction.GuideToRest && !MedicalRouteExists(command.GuestId, MedicalRestCell))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "No walkable route to the rest point; clear the path or dispatch aid.");
        if (command.Action == MedicalAction.SafeRemove && !MedicalRouteExists(command.GuestId, MedicalExitCell))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "No walkable route to the gate; clear the path or dispatch aid.");
        if (command.Action == MedicalAction.DispatchMedic && MedicalResponseCell(workerId, command.GuestId) is null)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Medic cannot reach a walkable position beside this guest; clear the route before dispatch.");
        return null;
    }

    private void ApplyMedicalCommand(MedicalCommand command, bool developmentFixture = false)
    {
        if (!developmentFixture && command.Action != MedicalAction.DispatchMedic)
        { ApplyStaffIntervention(LegacyMedicalIntervention(command)); return; }
        var m = _medical!;
        var id = new EntityId(command.GuestId);
        if (command.Action == MedicalAction.GuideToWater) { SeekWater(command.GuestId, "Player guided to free water despite the show/wait tradeoff"); return; }
        if (command.Action == MedicalAction.ReturnToShow) { LeaveWater(command.GuestId, "Left the water queue to watch the band"); return; }
        if (command.Action == MedicalAction.GuideToRest)
        {
            LeaveWater(command.GuestId, "Rest chosen", reroute: false);
            SetNeed(command.GuestId, item => item with { Intent = MedicalIntent.Rest, Reason = "Rest chosen to reduce Hot exposure", QueueSlot = null });
            MedicalRelinquishPerformerStage(command.GuestId);
            ApplyAgentDestination(id, new(MedicalRestCell, "medical.rest"));
            MedicalEvent("medical:rest", "Guest routed physically to the shaded first-aid rest point.");
            return;
        }
        if (command.Action == MedicalAction.SafeRemove)
        {
            LeaveWater(command.GuestId, "Safe removal chosen", reroute: false);
            SetNeed(command.GuestId, item => item with { Intent = MedicalIntent.Leaving, Reason = "Safe removal via the main gate", QueueSlot = null });
            ApplyAgentDestination(id, new(MedicalExitCell, "medical.safe-removal"));
            _medical = _medical! with { ResponseStage = MedicalResponseStage.Removing, ResponsePatientId = command.GuestId,
                Response = "Safe removal en route; not protection until the guest reaches the gate" };
            MedicalEvent("medical:remove-dispatch", _medical.Response);
            return;
        }
        foreach (var job in GetStewardResponses().Where(item => StewardBusy(item) && item.TargetId == command.GuestId))
        {
            SetStewardResponse(job with { Stage = SecurityResponseStage.Completed, TargetId = null, Description = "Injured person handed to medical response" });
            DisorderEvent("security:medical-handoff", command.GuestId, job.WorkerId, 0, "Explicit injury handoff to named medic");
        }
        foreach (var job in CaptureStaffInterventions().Where(item => InterventionBusy(item) && (item.GuestId == command.GuestId || item.WorkerId == command.GuestId)))
            EndIntervention(job, false, "Explicit injury handoff to a named physical medic");
        LeaveWater(command.GuestId, "Medic now owns response", reroute: false);
        MedicalRelinquishPerformerStage(command.GuestId);
        var patient = _navigationAgents[id];
        var patientCell = TraversalGrid.WorldToCell(patient.XMillimetres, patient.ZMillimetres);
        ApplyAgentDestination(id, new(patientCell, "medical.await-medic"));
        SetNeed(command.GuestId, item => item with {
            Intent = (item.Stage is MedicalStage.Collapsed or MedicalStage.Critical ||
                item.AgentId == m.AtRiskGuestId && m.Stage is MedicalStage.Collapsed or MedicalStage.Critical)
                ? MedicalIntent.Collapsed : MedicalIntent.AwaitMedic,
            Reason = "Awaiting physically dispatched medic", QueueSlot = null });
        // A medic cannot occupy the patient's cell. Reserve a walkable response
        // position beside it rather than waiting forever on collision avoidance.
        var workerId = command.WorkerId ?? m.MedicId;
        var responseCell = MedicalResponseCell(workerId, command.GuestId)!.Value;
        ApplyAgentDestination(new(workerId), new(responseCell, "medical.dispatch"));
        SetMedicResponse(new(workerId, MedicalResponseStage.Travelling, command.GuestId, -1, "Medic dispatched; physical travel and treatment required", CurrentTick));
        MedicalEvent("medical:dispatch", $"Worker {workerId} dispatched to patient {command.GuestId}.");
    }

    private void SetNeed(ulong id, Func<MedicalNeed, MedicalNeed> change)
    {
        var m = _medical!; var needs = m.Needs.ToArray();
        var index = Array.FindIndex(needs, item => item.AgentId == id);
        needs[index] = change(needs[index]);
        _medical = m with { Needs = needs };
    }

    private void SeekWater(ulong id, string reason)
    {
        if (_disorder?.WaterClosed == true)
        {
            var need = _medical!.Needs.Single(item => item.AgentId == id);
            if (id == _medical.AtRiskGuestId || need.Profile == MedicalNeedProfile.Performer)
            {
                SetNeed(id, item => item with { Intent = MedicalIntent.Rest,
                    Reason = "Water service closed; physically seeking first-aid rest" });
                ApplyAgentDestination(new(id), new(MedicalRestCell, "disorder.water-closure-rest"));
            }
            return;
        }
        foreach (var item in WaterPoints()) GrowWaterQueue(item.Id);
        var m = _medical!;
        if (WaterPoints().Any(point => point.Queue.Contains(id)) ||
            m.Needs.Any(item => item.AgentId == id && item.Intent == MedicalIntent.SeekWater)) return;
        if (WaterPoints().All(point => point.Queue.Length == 10 && point.Overflow.Length == 10))
        {
            SetNeed(id, item => item with { Reason = "The physical free-water line is full", LastDecisionTick = CurrentTick });
            return;
        }
        var point = ChooseWaterPoint(id);
        if (EstimateWaterTotalTicks(id, point) == int.MaxValue)
        {
            SetNeed(id, item => item with { Reason = "No reachable space at a physical water line", LastDecisionTick = CurrentTick });
            return;
        }
        SetNeed(id, item => item with { Intent = MedicalIntent.SeekWater, Reason = reason,
            QueueSlot = null, LastDecisionTick = CurrentTick, WaterPointId = point.Id,
            LastWaterChoiceReviewTick = CurrentTick });
        MedicalRelinquishPerformerStage(id);
        ApplyAgentDestination(new(id), new(WaterApproach(point), "medical.free-water-approach"));
        MedicalEvent("medical:water-seek", $"Person {id} walked toward {point.Id} without reserving a place.");
    }

    private void RetargetWaterSeekers()
    {
        foreach (var selected in WaterPoints())
        {
            var hasSpace = GrowWaterQueue(selected.Id);
            var point = WaterPoints().Single(item => item.Id == selected.Id);
            for (var index = 0; index < point.Queue.Length; index++)
                if (_navigationAgents[new(point.Queue[index])].Destination != WaterSlot(point, index))
                    ApplyAgentDestination(new(point.Queue[index]), new(WaterSlot(point, index), "medical.free-water-queue"));
            if (!hasSpace || point.Queue.Length == 10 && point.Overflow.Length == 10)
            {
                foreach (var need in _medical!.Needs.Where(item => item.Intent == MedicalIntent.SeekWater &&
                    item.WaterPointId == point.Id && item.QueueSlot is null && !point.Overflow.Contains(item.AgentId)).ToArray())
                    LeaveWater(need.AgentId, "The physical water line is full", retargetSeekers: false);
                continue;
            }
            var approach = WaterApproach(point);
            foreach (var need in _medical!.Needs.Where(item => item.Intent == MedicalIntent.SeekWater &&
                         item.WaterPointId == point.Id && item.QueueSlot is null))
            {
                if (point.Overflow.Contains(need.AgentId)) continue;
                var nav = _navigationAgents[new(need.AgentId)];
                if (nav.Destination != approach)
                    ApplyAgentDestination(new(need.AgentId), new(approach, "medical.free-water-approach"));
            }
        }
    }

    private void ReassessWaterSeekers()
    {
        if (_disorder?.WaterClosed == true) return;
        foreach (var need in _medical!.Needs.Where(item => item.Intent == MedicalIntent.SeekWater &&
                     CurrentTick - item.LastWaterChoiceReviewTick >= WaterChoiceReviewTicks &&
                     CurrentTick % 8 == (long)(item.AgentId % 8)).ToArray())
        {
            var current = WaterPointFor(need.AgentId);
            if (current.OwnerId == need.AgentId) continue;
            var best = ChooseWaterPoint(need.AgentId);
            var currentTicks = RemainingOwnWaterWaitTicks(need.AgentId, current);
            var bestTicks = EstimateWaterTotalTicks(need.AgentId, best);
            SetNeed(need.AgentId, item => item with { LastWaterChoiceReviewTick = CurrentTick });
            if (best.Id == current.Id || bestTicks == int.MaxValue || bestTicks + WaterChoiceSwitchMarginTicks >= currentTicks)
                continue;
            var wasQueued = current.Queue.Contains(need.AgentId) || current.Overflow.Contains(need.AgentId);
            if (wasQueued) LeaveWater(need.AgentId, "Left old place for a clearly shorter physical water line", reroute: false);
            SetNeed(need.AgentId, item => item with { WaterPointId = best.Id, Intent = MedicalIntent.SeekWater, QueueSlot = null,
                LastWaterChoiceReviewTick = CurrentTick,
                Reason = $"Rechosen {best.Id}; old place forfeited: {bestTicks} vs {currentTicks} remaining ticks" });
            ApplyAgentDestination(new(need.AgentId), new(WaterApproach(best), "medical.free-water-approach"));
            MedicalEvent("medical:water-rechoose", $"Person {need.AgentId} switched to {best.Id}; no advance reservation, old place forfeited={wasQueued}.");
        }
    }

    private void AdmitWaterArrivals()
    {
        if (_disorder?.WaterClosed == true) return;
        foreach (var current in WaterPoints()) AdmitWaterArrivalsAt(current.Id);
    }

    private void AdmitWaterArrivalsAt(string pointId)
    {
        var point = WaterPoints().Single(item => item.Id == pointId);
        if (point.Queue.Length == 10 && point.Overflow.Length == 10 || point.QueueCells.Length > 0 &&
            point.QueueCells.Length <= point.Queue.Length + point.Overflow.Length) return;
        var approach = WaterApproach(point);
        var centre = TraversalGrid.CellCentre(approach);
        var arrived = _medical!.Needs.Where(item => item.Intent == MedicalIntent.SeekWater && item.WaterPointId == pointId &&
                         item.QueueSlot is null && !point.Overflow.Contains(item.AgentId))
            .Select(item => (item.AgentId, Nav: _navigationAgents[new(item.AgentId)]))
            .Where(item => item.Nav.Destination == approach &&
                item.Nav.Action is AgentNavigationAction.Travelling or AgentNavigationAction.Arrived &&
                (long)(item.Nav.XMillimetres - centre.XMillimetres) * (item.Nav.XMillimetres - centre.XMillimetres) +
                (long)(item.Nav.ZMillimetres - centre.ZMillimetres) * (item.Nav.ZMillimetres - centre.ZMillimetres) <= 650L * 650)
            .OrderBy(item => item.AgentId).ToArray();
        foreach (var (id, _) in arrived)
        {
            GrowWaterQueue(pointId);
            point = WaterPoints().Single(item => item.Id == pointId);
            if (point.QueueCells.Length > 0 && point.QueueCells.Length <= point.Queue.Length + point.Overflow.Length) break;
            if (point.Queue.Length < 10)
            {
                var slot = point.Queue.Length;
                SetWaterPoint(point with { Queue = point.Queue.Append(id).ToArray() });
                SetNeed(id, item => item with { QueueSlot = slot, Reason = $"Joined the free-water line on physical arrival at tick {CurrentTick}" });
                ApplyAgentDestination(new(id), new(WaterSlot(point, slot), "medical.free-water-queue"));
                MedicalEvent("medical:queue-join", $"Person {id} arrived physically and took {point.Id} place {slot}; no payment or stock transfer.");
            }
            else if (point.Overflow.Length < 10)
            {
                var slot = point.Overflow.Length;
                SetWaterPoint(point with { Overflow = point.Overflow.Append(id).ToArray() });
                SetNeed(id, item => item with { Reason = $"Reached visible water overflow place {slot} at tick {CurrentTick}" });
                ApplyAgentDestination(new(id), new(WaterOverflowSlot(point, slot), "medical.free-water-overflow"));
                MedicalEvent("medical:overflow-join", $"Person {id} reached {point.Id} overflow place {slot} after physical arrival.");
            }
            else break;
        }
        if (arrived.Length > 0) RetargetWaterSeekers();
    }

    private void LeaveWater(ulong id, string reason, bool reroute = true, bool retargetSeekers = true)
    {
        var point = WaterPointFor(id);
        if (!point.Queue.Contains(id))
        {
            if (!_medical!.Needs.Any(item => item.AgentId == id && item.Intent == MedicalIntent.SeekWater)) return;
            if (point.Overflow.Contains(id))
            {
                SetWaterPoint(point with { Overflow = point.Overflow.Where(member => member != id).ToArray() });
                point = WaterPointFor(id);
                for (var index = 0; index < point.Overflow.Length; index++)
                    ApplyAgentDestination(new(point.Overflow[index]), new(WaterOverflowSlot(point, index), "medical.free-water-overflow"));
            }
            SetNeed(id, item => item with { Intent = MedicalIntent.WatchShow, Reason = reason, QueueSlot = null,
                LastDecisionTick = CurrentTick });
            if (reroute) ReturnToListening(id);
            if (retargetSeekers) RetargetWaterSeekers();
            MedicalEvent("medical:queue-leave", $"Person {id} left the water approach before taking a place: {reason}.");
            return;
        }
        var ordered = point.Queue.Where(item => item != id).ToArray();
        SetWaterPoint(point with { Queue = ordered, OwnerId = point.OwnerId == id ? null : point.OwnerId,
            DrinkTicks = point.OwnerId == id ? 0 : point.DrinkTicks });
        SetNeed(id, item => item with { Intent = MedicalIntent.WatchShow, Reason = reason, QueueSlot = null,
            LastDecisionTick = CurrentTick });
        for (var index = 0; index < ordered.Length; index++)
        {
            var member = ordered[index];
            var old = _medical!.Needs.Single(item => item.AgentId == member);
            if (old.QueueSlot == index) continue;
            SetNeed(member, item => item with { QueueSlot = index });
            ApplyAgentDestination(new(member), new(WaterSlot(point, index), "medical.free-water-queue"));
        }
        point = WaterPointFor(id);
        if (point.Overflow.Length > 0)
        {
            var promoted = point.Overflow[0];
            var slot = point.Queue.Length;
            SetWaterPoint(point with { Queue = point.Queue.Append(promoted).ToArray(), Overflow = point.Overflow.Skip(1).ToArray() });
            SetNeed(promoted, item => item with { QueueSlot = slot, Reason = "Advanced from the visible overflow tail" });
            ApplyAgentDestination(new(promoted), new(WaterSlot(point, slot), "medical.free-water-queue"));
            point = WaterPointFor(id);
            for (var index = 0; index < point.Overflow.Length; index++)
                ApplyAgentDestination(new(point.Overflow[index]), new(WaterOverflowSlot(point, index), "medical.free-water-overflow"));
        }
        if (reroute) ReturnToListening(id);
        if (retargetSeekers) RetargetWaterSeekers();
        MedicalEvent("medical:queue-leave", $"Guest {id} released their free-water reservation: {reason}.");
    }

    private void ReturnToListening(ulong id)
    {
        if (ImmersionDepartureActive)
        {
            var index = Array.FindIndex(_preparation!.People, person => person.AgentId == id);
            SetNeed(id, need => need with { Intent = MedicalIntent.Leaving, Reason = "Care completed; physically leaving" });
            ApplyAgentDestination(new(id), new(PreparedStart(index), "edition.departure"));
            return;
        }
        var place = _livePerformance?.Listeners.SingleOrDefault(item => item.AgentId == id)?.Place;
        if (place is { } cell) ApplyAgentDestination(new(id), new(cell, "performance.listen"));
        else if (_livePerformance?.Performers.SingleOrDefault(item => item.AgentId == id) is { } performer &&
                 (_programme is null || IsCurrentProgrammePerformer(id)) &&
                 _livePerformance.Stage is LiveSetStage.BeforeSet or LiveSetStage.Live or LiveSetStage.Interrupted)
            ApplyAgentDestination(new(id), new(performer.AccessCell, "medical.return-to-stage-access"));
        else
        {
            var index = Array.FindIndex(_preparation!.People, item => item.AgentId == id);
            ApplyAgentDestination(new(id), new(PreparedPlace(index), "medical.return"));
        }
    }

    private void MedicalRelinquishPerformerStage(ulong id)
    {
        if (_livePerformance is not { } live) return;
        var index = Array.FindIndex(live.Performers, item => item.AgentId == id);
        if (index < 0) return;
        var performers = live.Performers.ToArray();
        performers[index] = performers[index] with
        { AccessReached = false, StairReached = false, OnStage = false, InstrumentAttached = false };
        _livePerformance = live with { Performers = performers };
        if (_programme is not null && live.Stage == LiveSetStage.Live)
            _livePerformance = _livePerformance with { Stage = LiveSetStage.Interrupted, InterruptedTick = CurrentTick,
                LastReaction = "performer-unavailable", ReactionSequence = live.ReactionSequence + 1 };
    }

    // Derived only from saved programme, identity and time; no random draws or hidden anticipation state.
    public int FestivalNeedMusicAppeal(ulong id)
    {
        if (_programme is null) return 0;
        var score = _livePerformance?.Stage == LiveSetStage.Live && CurrentFestivalAct is { } current
            ? 2_500 + FestivalAffinity(id, current) * 50 + current.Popularity * 10 : 2_500;
        var until = UpcomingFestivalTick - CurrentTick;
        if (UpcomingFestivalAct is { } upcoming && until is >= 0 and <= 1_600)
            score += FestivalAnticipationAppeal(FestivalAffinity(id, upcoming), until);
        return score;
    }

    public static int FestivalAnticipationAppeal(int affinity, long ticksUntil) => ticksUntil is < 0 or > 1_600
        ? 0 : (int)((1_600 - ticksUntil) * Math.Clamp(affinity, 0, 100) * 1_500 / 160_000L);

    private void FinishProgrammeMedicalNeedsForDeparture()
    {
        if (_programme is null || _medical is not { } medical) return;
        _medical = medical with { WaterQueue = [], WaterOverflow = [], WaterOwnerId = null, WaterDrinkTicks = 0,
            MainWaterQueueCells = [], ExtraWaterPoints = medical.ExtraWaterPoints.Select(point => point with
                { Queue = [], Overflow = [], OwnerId = null, DrinkTicks = 0, QueueCells = [] }).ToArray(),
            Needs = medical.Needs.Select(need => need with { Intent = MedicalIntent.Leaving, QueueSlot = null,
                Reason = "Festival complete; leaving physically through the gate", LastDecisionTick = CurrentTick }).ToArray() };
    }

    private void AdvanceMedical()
    {
        if (ImmersionDepartureActive) { AdvanceImmersionDepartureMedicine(); return; }
        if (_medical is not { } m || _preparation is not { Status: PreparationStatus.Running } p) return;
        if (CurrentTick % 4 == 0)
        {
            var needs = m.Needs.Select(item => item with
            {
                Thirst = Math.Min(10_000, item.Thirst + 1),
                HeatExposure = Math.Min(10_000, item.HeatExposure + (item.AgentId == m.AtRiskGuestId || item.Profile == MedicalNeedProfile.Performer ? 1 : CurrentTick % 32 == 0 ? 1 : 0))
            }).ToArray();
            _medical = m = m with { Needs = needs };
        }
        if (CurrentTick % 80 == 0)
        {
            foreach (var need in m.Needs)
            {
                var person = p.People.Single(item => item.AgentId == need.AgentId);
                if (!person.Admitted || person.Departed || need.Profile == MedicalNeedProfile.Staff || DisorderOwnsNavigation(need.AgentId) ||
                    InterventionOwnsTarget(need.AgentId) || InterventionOwnsWorker(need.AgentId) ||
                    need.Intent is MedicalIntent.Rest or MedicalIntent.AwaitMedic or MedicalIntent.Leaving or MedicalIntent.Collapsed or MedicalIntent.Drinking ||
                    (m.Stage is MedicalStage.Collapsed or MedicalStage.Critical && need.AgentId == m.AtRiskGuestId) ||
                    need.Stage is MedicalStage.Collapsed or MedicalStage.Critical ||
                    CurrentTick - need.LastDecisionTick < MedicalDecisionCooldownTicks &&
                    !(_programme is not null && (need.Thirst >= MedicalDistressThirst || need.HeatExposure >= MedicalDistressHeat))) continue;
                var nav = _navigationAgents[new(need.AgentId)];
                var from = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
                var bestPoint = need.Intent == MedicalIntent.SeekWater ? WaterPointFor(need.AgentId) : ChooseWaterPoint(need.AgentId);
                var travel = Math.Abs(from.X - bestPoint.Cell.X) + Math.Abs(from.Z - bestPoint.Cell.Z);
                var enthusiasm = _livePerformance?.Listeners.SingleOrDefault(item => item.AgentId == need.AgentId)?.Enthusiasm ?? 35;
                var waterScore = need.Thirst + need.HeatExposure / 3 + (need.Thirst >= MedicalDistressThirst ? 3_000 : 0) -
                    travel * 15 - bestPoint.Queue.Length * 120;
                var showScore = 5_000 + enthusiasm * 40 + (_livePerformance?.Stage == LiveSetStage.Live ? 300 : 0) +
                    (need.Profile == MedicalNeedProfile.Performer ? 5_000 : 0) +
                    (need.AgentId == m.AtRiskGuestId ? 10_000 : 0);
                if (_programme is not null)
                    showScore = FestivalNeedMusicAppeal(need.AgentId) +
                        (need.Profile == MedicalNeedProfile.Performer && IsCurrentProgrammePerformer(need.AgentId) ? 5_000 : 0);
                var urgent = _programme is not null && (need.Thirst >= MedicalDistressThirst || need.HeatExposure >= MedicalDistressHeat);
                if (_programme is not null && need.Intent != MedicalIntent.SeekWater &&
                    (need.Profile == MedicalNeedProfile.Performer || need.AgentId == m.AtRiskGuestId) &&
                    need.HeatExposure >= MedicalDistressHeat && need.Thirst < MedicalDistressThirst &&
                    MedicalRouteExists(need.AgentId, MedicalRestCell))
                {
                    MedicalRelinquishPerformerStage(need.AgentId);
                    SetNeed(need.AgentId, item => item with { Intent = MedicalIntent.Rest,
                        Reason = "Hot exposure takes priority over current and upcoming music; physically seeking rest",
                        LastDecisionTick = CurrentTick });
                    ApplyAgentDestination(new(need.AgentId), new(MedicalRestCell, "medical.rest"));
                }
                else if (need.Intent == MedicalIntent.SeekWater)
                {
                    if (!urgent && bestPoint.OwnerId != need.AgentId && need.Thirst < 8_500 && showScore > waterScore + 1_200)
                        LeaveWater(need.AgentId, $"Band appeal {showScore} exceeded water utility {waterScore}; queue place released");
                }
                else if (urgent || waterScore > showScore)
                    SeekWater(need.AgentId, $"Hot thirst {need.Thirst}/10000 outweighed band {showScore}; estimated walk + wait + drink {EstimateWaterTotalTicks(need.AgentId, bestPoint)} ticks");
                else SetNeed(need.AgentId, item => item with { Reason = _programme is null
                    ? $"Watching band: music {showScore} vs water {waterScore} incl. travel/wait"
                    : $"Current/upcoming act appeal {showScore} vs water {waterScore} incl. travel/wait",
                    LastDecisionTick = CurrentTick });
                m = _medical!;
            }
        }
        m = _medical!;
        ReassessWaterSeekers();
        AdmitWaterArrivals();
        m = _medical!;
        foreach (var selected in WaterPoints())
        {
            var point = WaterPoints().Single(item => item.Id == selected.Id);
            if (point.Queue.Length == 0) continue;
            var first = point.Queue[0];
            var atTap = _navigationAgents[new(first)] is { Action: AgentNavigationAction.Arrived, Destination: { } destination } && destination == WaterSlot(point, 0);
            if (point.OwnerId is null && atTap)
            {
                SetWaterPoint(point with { OwnerId = first, DrinkTicks = 0 });
                SetNeed(first, item => item with { Intent = MedicalIntent.Drinking,
                    Reason = $"Drinking at {point.Id} ({EffectiveMedicalDrinkThirstPerTickFor(first)} thirst/tick); thirst and heat improve continuously" });
                MedicalEvent("medical:drink-start", $"Person {first} started drinking at {point.Id} after physical arrival.");
                point = WaterPoints().Single(item => item.Id == selected.Id);
            }
            if (point.OwnerId == first && atTap)
            {
                SetNeed(first, item => item with { Thirst = Math.Max(0, item.Thirst - EffectiveMedicalDrinkThirstPerTickFor(first)),
                    HeatExposure = Math.Max(0, item.HeatExposure - EffectiveMedicalDrinkHeatPerTickFor(first)) });
                SetWaterPoint(point with { DrinkTicks = point.DrinkTicks + 1 });
                if (_medical!.Needs.Single(item => item.AgentId == first).Thirst == 0)
                {
                    var drankTicks = point.DrinkTicks + 1;
                    LeaveWater(first, "Thirst reached zero after drinking", reroute: true);
                    SetNeed(first, item => item with { LastWaterTick = CurrentTick,
                        Reason = "Drank free water until thirst reached zero" });
                    MedicalEvent("medical:water", $"Person {first} finished at {point.Id} after {drankTicks} ticks; thirst zero, no payment or stock transfer.");
                }
            }
        }
        m = _medical!;
        foreach (var resting in m.Needs.Where(item => item.Profile == MedicalNeedProfile.Performer && item.Intent == MedicalIntent.Rest).ToArray())
        {
            if (_navigationAgents[new(resting.AgentId)] is { Action: AgentNavigationAction.Arrived, Destination: { } restCell } && restCell == MedicalRestCell)
            {
                SetNeed(resting.AgentId, item => item with { HeatExposure = Math.Max(0, item.HeatExposure - 8) });
                if (_programme is not null && (resting.Stage is MedicalStage.Clear or MedicalStage.Treated) && resting.HeatExposure <= 6_000)
                {
                    SetNeed(resting.AgentId, item => item with { Intent = MedicalIntent.WatchShow,
                        Reason = "Rest relieved Hot exposure; ordinary needs decisions resume", LastDecisionTick = CurrentTick });
                    ReturnToListening(resting.AgentId);
                }
            }
        }
        AdvanceMedicResponses();
        m = _medical!;
        var target = m.Needs.Single(item => item.AgentId == m.AtRiskGuestId);
        if (target.Intent == MedicalIntent.Rest && _navigationAgents[new(target.AgentId)] is { Action: AgentNavigationAction.Arrived, Destination: { } rest } && rest == MedicalRestCell)
        {
            SetNeed(target.AgentId, item => item with { HeatExposure = Math.Max(0, item.HeatExposure - 8) });
            m = _medical!; target = m.Needs.Single(item => item.AgentId == m.AtRiskGuestId);
        }
        if (m.Stage == MedicalStage.Clear && target.Thirst >= MedicalDistressThirst && target.HeatExposure >= MedicalDistressHeat)
        {
            _medical = m = m with { Stage = MedicalStage.Distress, WarningTick = CurrentTick };
            MedicalEvent("medical:distress", $"Guest {target.AgentId} distressed in Hot conditions: thirst {target.Thirst}, heat {target.HeatExposure}; free water, rest, safe removal and medic dispatch are available.");
        }
        m = _medical!; target = m.Needs.Single(item => item.AgentId == m.AtRiskGuestId);
        if (m.Stage == MedicalStage.Distress && (target.Thirst < MedicalDistressThirst || target.HeatExposure < MedicalDistressHeat))
        {
            const string relief = "Need relieved through free water or rest before collapse";
            var otherPatientActive = m.ResponsePatientId != target.AgentId &&
                m.ResponseStage is MedicalResponseStage.Travelling or MedicalResponseStage.Treating or MedicalResponseStage.Removing;
            _medical = m with { Stage = MedicalStage.Treated,
                ResponseStage = otherPatientActive ? m.ResponseStage : MedicalResponseStage.Completed,
                Response = otherPatientActive ? m.Response : relief };
            if (target.Intent == MedicalIntent.Rest)
            {
                SetNeed(target.AgentId, item => item with { Intent = MedicalIntent.WatchShow,
                    Reason = "Rest relieved Hot exposure; free to return to the show" });
                ReturnToListening(target.AgentId);
            }
            MedicalEvent("medical:prevented", relief); return;
        }
        if (m.ResponseStage == MedicalResponseStage.Removing && _navigationAgents[new(target.AgentId)] is { Action: AgentNavigationAction.Arrived, Destination: { } exit } && exit == MedicalExitCell)
        {
            _medical = m with { Stage = MedicalStage.Removed, ResponseStage = MedicalResponseStage.Completed,
                Response = "Guest safely reached the main gate" };
            var people = p.People.Select(item => item.AgentId == target.AgentId ? item with { Departed = true } : item).ToArray();
            _preparation = p with { People = people };
            MedicalEvent("medical:removed", _medical.Response); return;
        }
        m = _medical!;
        if (m.Stage == MedicalStage.Distress && CurrentTick >= m.WarningTick + MedicalCollapseDelayTicks)
        {
            LeaveWater(m.AtRiskGuestId, "Collapsed before drinking", reroute: false);
            var patient = _navigationAgents[new(m.AtRiskGuestId)];
            ApplyAgentDestination(new(m.AtRiskGuestId), new(TraversalGrid.WorldToCell(patient.XMillimetres, patient.ZMillimetres), "medical.collapsed"));
            SetNeed(m.AtRiskGuestId, item => item with { Intent = MedicalIntent.Collapsed, Reason = "Collapsed; needs physical medic response", QueueSlot = null });
            _medical = _medical! with { Stage = MedicalStage.Collapsed, CollapseTick = CurrentTick,
                ResponseStage = m.ResponseStage == MedicalResponseStage.Removing ? MedicalResponseStage.None : m.ResponseStage };
            MedicalEvent("medical:collapse", "Guest collapsed after visible distress; untreated response window remains.");
        }
        m = _medical!;
        if (m.Stage == MedicalStage.Collapsed && CurrentTick >= m.CollapseTick + MedicalCriticalDelayTicks)
        {
            _medical = m with { Stage = MedicalStage.Critical, CriticalTick = CurrentTick };
            MedicalEvent("medical:critical", "Guest remains untreated after collapse; dispatch can still prevent death.");
        }
        m = _medical!;
        if (m.Stage == MedicalStage.Critical && CurrentTick >= m.CollapseTick + MedicalDeathDelayTicks)
        {
            ApplyMedicalDeath(m.AtRiskGuestId, m.WarningTick, m.CollapseTick, m.CriticalTick);
            return;
        }
        foreach (var performer in _medical!.Needs.Where(item => item.Profile == MedicalNeedProfile.Performer).ToArray())
        {
            m = _medical!;
            var need = m.Needs.Single(item => item.AgentId == performer.AgentId);
            if (need.Stage is MedicalStage.Treated or MedicalStage.Removed) continue;
            if (need.Stage == MedicalStage.Clear && need.Thirst >= MedicalDistressThirst && need.HeatExposure >= MedicalDistressHeat)
            {
                SetNeed(need.AgentId, item => item with { Stage = MedicalStage.Distress, WarningTick = CurrentTick });
                MedicalEvent("medical:distress", $"Performer {need.AgentId} distressed in Hot conditions; free water, rest and first aid are available.");
            }
            need = _medical!.Needs.Single(item => item.AgentId == performer.AgentId);
            if (need.Stage == MedicalStage.Distress && (need.Thirst < MedicalDistressThirst || need.HeatExposure < MedicalDistressHeat))
            {
                SetNeed(need.AgentId, item => item with { Stage = MedicalStage.Treated, Reason = "Need relieved before collapse" });
                if (need.Intent == MedicalIntent.Rest)
                {
                    SetNeed(need.AgentId, item => item with { Intent = MedicalIntent.WatchShow });
                    ReturnToListening(need.AgentId);
                }
                MedicalEvent("medical:prevented", $"Performer {need.AgentId} relieved through water or rest.");
                continue;
            }
            if (need.Stage == MedicalStage.Distress && CurrentTick >= need.WarningTick + MedicalCollapseDelayTicks)
            {
                LeaveWater(need.AgentId, "Collapsed before drinking", reroute: false);
                MedicalRelinquishPerformerStage(need.AgentId);
                var patient = _navigationAgents[new(need.AgentId)];
                ApplyAgentDestination(new(need.AgentId), new(TraversalGrid.WorldToCell(patient.XMillimetres, patient.ZMillimetres), "medical.collapsed"));
                SetNeed(need.AgentId, item => item with { Stage = MedicalStage.Collapsed, CollapseTick = CurrentTick,
                    Intent = MedicalIntent.Collapsed, Reason = "Collapsed; needs physical medic response", QueueSlot = null });
                MedicalEvent("medical:collapse", $"Performer {need.AgentId} collapsed after visible distress.");
            }
            need = _medical!.Needs.Single(item => item.AgentId == performer.AgentId);
            if (need.Stage == MedicalStage.Collapsed && CurrentTick >= need.CollapseTick + MedicalCriticalDelayTicks)
            {
                SetNeed(need.AgentId, item => item with { Stage = MedicalStage.Critical, CriticalTick = CurrentTick });
                MedicalEvent("medical:critical", $"Performer {need.AgentId} remains untreated after collapse.");
            }
            need = _medical!.Needs.Single(item => item.AgentId == performer.AgentId);
            if (need.Stage == MedicalStage.Critical && CurrentTick >= need.CollapseTick + MedicalDeathDelayTicks)
            {
                ApplyMedicalDeath(need.AgentId, need.WarningTick, need.CollapseTick, need.CriticalTick);
                return;
            }
        }
    }

    private void ApplyMedicalDeath(ulong victimId, long warningTick, long collapseTick, long criticalTick)
    {
        var m = _medical!; var p = _preparation!;
        var victim = p.People.Single(item => item.AgentId == victimId);
        var cause = _immersion?.People.SingleOrDefault(item=>item.AgentId==victimId) is { CollapseTick: >=0 } alcohol
            ? $"{victim.Name} died after sustained intoxication {alcohol.Intoxication}/10000; visible intoxication warning tick {alcohol.WarningTick}, collapse tick {collapseTick}, critical tick {criticalTick}; {StaffResponseCausalSummary()}."
            : $"In fixed Hot conditions {victim.Name} dried up after thirst {m.Needs.Single(item => item.AgentId == victim.AgentId).Thirst}/10000 and heat exposure; distress tick {warningTick}, collapse tick {collapseTick}, critical tick {criticalTick}; {StaffResponseCausalSummary()}.";
        _medical = m with { Stage = MedicalStage.Terminal };
        MedicalEvent("medical:death", cause);
        var lifecycle = _lifecycle!; var attempt = CurrentAttempt();
        var transaction = $"medical-death:{CampaignId.Value}:{attempt.AttemptId}";
        lifecycle.Casualties.Add(new(lifecycle.NextCasualtyId++, attempt.AttemptId, victim.Name, victim.Role, cause, CurrentTick, transaction));
        lifecycle.CompletedOutcomeTransactionIds.Add(transaction);
        ReplaceAttempt(attempt with { Status = EditionAttemptStatus.Failed, OutcomeTransactionId = transaction });
        var hearing = $"medical-hearing:{CampaignId.Value}:{attempt.AttemptId}";
        lifecycle.Hearings.Add(new(lifecycle.NextHearingId++, attempt.AttemptId, HearingStatus.Open, hearing, null));
        lifecycle.CompletedOutcomeTransactionIds.Add(hearing);
        ResolveNoFavourHearing();
        _preparation = p with { Status = PreparationStatus.Failed, Rentals = [], WorkContracts = [] };
        ReleaseInterventionsForBoundary("First death froze the edition; intervention released");
        FinishLivePerformance();
    }

    private static string? ValidatePersistedMedical(MedicalSnapshot? m, SessionPersistenceSnapshot s)
    {
        if (m is null) return null;
        var savedGrid = s.TraversalGrid is { } savedTerrain ? new TraversalGrid(savedTerrain.Cells.Select(cell =>
            new TerrainCellOverride(new(cell.X, cell.Z), (GroundSurface)cell.Surface, cell.IsWalkable, cell.CostPermille, cell.ElevationMillimetres, cell.SlopePermille))) : null;
        var points = new[] { new WaterPointState("water.main", m.MainWaterCell, m.WaterQueue, m.WaterOverflow, m.WaterOwnerId, m.WaterDrinkTicks) { QuarterTurns = m.MainWaterQuarterTurns, GeometryVersion = m.MainWaterGeometryVersion, QueueCells = m.MainWaterQueueCells } }
            .Concat(m.ExtraWaterPoints ?? []).ToArray();
        if (s.Preparation is not { } p || m.Version != (s.Disorder is null ? 5 : 6) || !m.IsHot || m.Needs is null || m.WaterQueue is null || m.WaterOverflow is null ||
            m.Evidence is null || m.Needs.Length != (s.Immersion is not null ? p.People.Length : p.Tier * 20 + p.People.Count(item => item.Role == ProtectedPersonRole.Performer) + (s.Disorder is null ? 0 : 1) + p.AcceptedOffers.Count(id => id == "staff.extra-steward")) ||
            !m.Needs.Select(item => item.AgentId).SequenceEqual(p.People.Where(item => item.Role is ProtectedPersonRole.Guest or ProtectedPersonRole.Performer ||
                item.AgentId == s.Disorder?.SecurityId || s.Immersion is not null || p.StaffProfiles.Any(profile => profile.Role == ResponseRole.Steward && profile.AgentId == item.AgentId)).Select(item => item.AgentId)) ||
            m.Needs.Any(item => item.Profile != (p.People.Single(person => person.AgentId == item.AgentId).Role == ProtectedPersonRole.Performer ? MedicalNeedProfile.Performer :
                item.AgentId == s.Disorder?.SecurityId || s.Immersion is not null && p.People.Single(person=>person.AgentId==item.AgentId).Role==ProtectedPersonRole.Staff || p.StaffProfiles.Any(profile => profile.Role == ResponseRole.Steward && profile.AgentId == item.AgentId) ? MedicalNeedProfile.Staff : MedicalNeedProfile.Guest)) ||
            !p.People.Any(item => item.AgentId == m.MedicId && item.Name == "Riley Hart" && item.Role == ProtectedPersonRole.Staff) ||
            m.AtRiskGuestId != m.Needs[19].AgentId || m.Needs.Any(item => item.Thirst is < 0 or > 10_000 || item.HeatExposure is < 0 or > 10_000 ||
                !Enum.IsDefined(item.Intent) || !Enum.IsDefined(item.Profile) || !Enum.IsDefined(item.Stage) ||
                item.QueueSlot is < 0 or >= 10 || !points.Any(point => point.Id == item.WaterPointId) ||
                item.LastDecisionTick > s.CurrentTick || item.LastWaterChoiceReviewTick > s.CurrentTick ||
                item.WarningTick > s.CurrentTick || item.CollapseTick > s.CurrentTick || item.CriticalTick > s.CurrentTick) ||
            m.MainWaterCell != p.PrimaryWaterCell || m.MainWaterQuarterTurns != p.PrimaryWaterQuarterTurns || m.MainWaterGeometryVersion != p.PrimaryWaterGeometryVersion || m.ExtraWaterPoints is null || p.ExtraWaterSiteIds is null || p.WaterPlacements is null ||
            !m.ExtraWaterPoints.Select(point => point.Id).SequenceEqual(p.ExtraWaterSiteIds) ||
            m.ExtraWaterPoints.Any(point => !EffectiveWaterPlacements(p).Any(site => site.Id == point.Id && site.Cell == point.Cell && site.QuarterTurns == point.QuarterTurns && site.GeometryVersion == point.GeometryVersion)) ||
            points.Any(point => point.Queue is null || point.Overflow is null || point.Queue.Length > 10 || point.Overflow.Length > 10 ||
                point.QuarterTurns is < 0 or > 3 || point.GeometryVersion is < 0 or > 1 || point.QueueCells is null || point.QueueCells.Length > 20 ||
                point.QueueCells.Distinct().Count() != point.QueueCells.Length ||
                point.QueueCells.Length > 0 && (point.QueueCells.Length < point.Queue.Length + point.Overflow.Length ||
                    point.QueueCells.Length > point.Queue.Length + point.Overflow.Length + 1 || point.QueueCells[0] != WaterPointServiceCell(point) ||
                    point.QueueCells.Any(cell => cell.X is < 0 or >= TraversalGrid.Width || cell.Z is < 0 or >= TraversalGrid.Depth || savedGrid is not null && !savedGrid.Get(cell).IsWalkable) ||
                    point.QueueCells.Skip(1).Where((cell, index) => Math.Abs(cell.X - point.QueueCells[index].X) > 3 || Math.Abs(cell.Z - point.QueueCells[index].Z) > 3).Any()) ||
                point.Queue.Distinct().Count() != point.Queue.Length || point.Overflow.Distinct().Count() != point.Overflow.Length ||
                point.Overflow.Length > 0 && point.Queue.Length != 10 ||
                point.Queue.Where((id, index) => !m.Needs.Any(item => item.AgentId == id && item.WaterPointId == point.Id && item.QueueSlot == index) ||
                    s.NavigationAgents?.SingleOrDefault(agent => agent.Id == id) is not { } queuedNav ||
                    queuedNav.DestinationX != WaterSlot(point, index).X || queuedNav.DestinationZ != WaterSlot(point, index).Z).Any() ||
                point.Overflow.Where((id, index) => !m.Needs.Any(item => item.AgentId == id && item.WaterPointId == point.Id &&
                    item.Intent == MedicalIntent.SeekWater && item.QueueSlot is null) ||
                    s.NavigationAgents?.SingleOrDefault(agent => agent.Id == id) is not { } nav ||
                    nav.DestinationX != WaterOverflowSlot(point, index).X || nav.DestinationZ != WaterOverflowSlot(point, index).Z).Any() ||
                point.OwnerId is { } owner && (point.Queue.Length == 0 || point.Queue[0] != owner ||
                    m.Needs.Single(item => item.AgentId == owner).Intent != MedicalIntent.Drinking) ||
                point.DrinkTicks < 0 || point.DrinkTicks > s.CurrentTick || point.OwnerId is null && point.DrinkTicks != 0) ||
            points.SelectMany(point => point.Queue.Concat(point.Overflow)).Distinct().Count() !=
                points.Sum(point => point.Queue.Length + point.Overflow.Length) ||
            !ValidSavedWaterGeometry(points, savedGrid) ||
            m.Needs.Any(item => item.QueueSlot is not null && !points.Any(point => point.Id == item.WaterPointId && point.Queue.Contains(item.AgentId))) ||
            m.Needs.Any(item => item.QueueSlot is not null && item.Intent is not (MedicalIntent.SeekWater or MedicalIntent.Drinking)) ||
            m.Needs.Any(item => item.Intent == MedicalIntent.SeekWater && item.QueueSlot is null &&
                !points.Any(point => point.Id == item.WaterPointId && point.Overflow.Contains(item.AgentId)) &&
                (s.NavigationAgents?.SingleOrDefault(agent => agent.Id == item.AgentId) is not { } nav ||
                 nav.DestinationX != WaterApproach(points.Single(point => point.Id == item.WaterPointId)).X ||
                 nav.DestinationZ != WaterApproach(points.Single(point => point.Id == item.WaterPointId)).Z)) ||
            m.Needs.Any(item => item.Intent == MedicalIntent.Drinking && !points.Any(point => point.OwnerId == item.AgentId)) ||
            m.ResponsePatientId is { } responsePatient && !m.Needs.Any(item => item.AgentId == responsePatient) ||
            m.ResponseStage is MedicalResponseStage.Travelling or MedicalResponseStage.Treating or MedicalResponseStage.Removing && m.ResponsePatientId is null ||
            !Enum.IsDefined(m.Stage) || !Enum.IsDefined(m.ResponseStage) ||
            m.WarningTick > s.CurrentTick || m.CollapseTick > s.CurrentTick || m.CriticalTick > s.CurrentTick ||
            m.Stage == MedicalStage.Terminal && (p.Status != PreparationStatus.Failed || s.Lifecycle?.Casualties.Length != 1) ||
            p.Status == PreparationStatus.Failed && m.Stage != MedicalStage.Terminal && s.Disorder?.Evidence.LastOrDefault()?.Id != "disorder:death")
            return "Medical Hot state, queue ownership or causal stage invalid.";
        return null;
    }
}

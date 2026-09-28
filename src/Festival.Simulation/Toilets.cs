namespace Festival.Simulation;

/// <summary>Fictional prototype balance values, not measured human waste volumes or health advice.</summary>
public static class ToiletRules
{
    public const int CapacityMillilitres = 12_000;
    public const int WeeMillilitres = 300;
    public const int PooMillilitres = 700;
    public const int NeedMaximum = 10_000;
    public const int NeedThreshold = 6_000;
    public const int NeedGainEveryTicks = 4;
    public const int DecisionEveryTicks = 80;
    public const int WeeServiceTicks = 240;
    public const int PooServiceTicks = 400;
    public const int SmellStartsPermille = 250;
    public const int SmellRadiusMillimetres = 8_000;
    public const int SmellMaximumPenaltyPerSecond = 24;
    public const int ContainmentPermille = 1_000;
    public const int MaximumQueue = 10;
}

public enum ToiletVisitKind { Wee, Poo }
public enum ToiletVisitStage { None, Approaching, Queued, Entering, Using, Leaving, InterruptedLeaving }

public sealed record ToiletFacility(string Id, GridCell Cell, int QuarterTurns, ulong[] Queue,
    ulong? OwnerId, bool DoorOpen, int ServiceTicks, int WeeCount, int PooCount,
    int CapacityMillilitres, int ContainmentPermille, ulong? InterruptedOccupantId = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int UsedMillilitres => WeeCount * ToiletRules.WeeMillilitres + PooCount * ToiletRules.PooMillilitres;
    [System.Text.Json.Serialization.JsonIgnore]
    public int FullPercent => Math.Clamp((UsedMillilitres * 100 + CapacityMillilitres - 1) / CapacityMillilitres, 0, 100);
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsFull => UsedMillilitres + ToiletRules.WeeMillilitres > CapacityMillilitres;
    public bool CanAccept(ToiletVisitKind kind) => UsedMillilitres +
        (kind == ToiletVisitKind.Poo ? ToiletRules.PooMillilitres : ToiletRules.WeeMillilitres) <= CapacityMillilitres;
}

public sealed record MoveToiletCommand(GridCell Cell, int QuarterTurns = 0) : SessionCommand;

public sealed partial class GameSession
{
    private void SetToilet(ToiletFacility toilet) => _immersion = _immersion! with { Toilet = toilet };
    private bool ToiletOwnsNavigation(ulong id) => _immersion?.People.Any(p => p.AgentId == id && p.ToiletStage != ToiletVisitStage.None) == true;
    public ToiletFacility? CaptureToilet() => _immersion?.Toilet is { } toilet ? toilet with { Queue = toilet.Queue.ToArray() } : null;

    public static GridCell ToiletInsideCell(ToiletFacility toilet) => toilet.Cell;
    public static GridCell ToiletQueueCell(ToiletFacility toilet, int index)
    {
        var offset = RotateWaterOffset(new(0, -5 - Math.Min(index, ToiletRules.MaximumQueue - 1) * 2), toilet.QuarterTurns);
        return new(toilet.Cell.X + offset.X, toilet.Cell.Z + offset.Z);
    }
    public static GridCell ToiletExitCell(ToiletFacility toilet)
    {
        var offset = RotateWaterOffset(new(4, -5), toilet.QuarterTurns);
        return new(toilet.Cell.X + offset.X, toilet.Cell.Z + offset.Z);
    }
    public static GridCell[] ToiletSolidCells(ToiletFacility toilet)
    {
        var cells = new List<GridCell>(7);
        foreach (var x in new[] { -1, 1 })
            foreach (var z in new[] { -1, 0, 1 })
            {
                var offset = RotateWaterOffset(new(x, z), toilet.QuarterTurns);
                cells.Add(new(toilet.Cell.X + offset.X, toilet.Cell.Z + offset.Z));
            }
        var back = RotateWaterOffset(new(0, 1), toilet.QuarterTurns);
        cells.Add(new(toilet.Cell.X + back.X, toilet.Cell.Z + back.Z));
        return cells.ToArray();
    }
    public static GridCell[] ToiletReservedCells(ToiletFacility toilet)
    {
        var cells = new HashSet<GridCell>();
        for (var x = -3; x <= 3; x++)
            for (var z = -4; z <= 2; z++)
            {
                var offset = RotateWaterOffset(new(x, z), toilet.QuarterTurns);
                cells.Add(new(toilet.Cell.X + offset.X, toilet.Cell.Z + offset.Z));
            }
        // Reserve every possible physical queue slot and the cells between them,
        // not merely the first waiting position near the door.
        for (var z = -5 - (ToiletRules.MaximumQueue - 1) * 2; z <= -5; z++)
            for (var x = -1; x <= 1; x++)
            {
                var offset = RotateWaterOffset(new(x, z), toilet.QuarterTurns);
                cells.Add(new(toilet.Cell.X + offset.X, toilet.Cell.Z + offset.Z));
            }
        return cells.OrderBy(cell => cell.X).ThenBy(cell => cell.Z).ToArray();
    }

    private string? ToiletPlacementError(ToiletFacility proposed)
    {
        if (_immersion is null || _preparation is null || _medical is null) return "A prepared weekend is required.";
        var terrain = new TraversalGrid(Fixtures.NavigationFixture.CreateLowerWitteringTerrain());
        var reserved = new HashSet<GridCell>();
        foreach (var vendor in _immersion.Vendors)
            foreach (var cell in ImmersionFootprint(vendor).Concat(LooseQueueGeometry.Corridor(VendorQueueCells(vendor, _immersion)))) reserved.Add(cell);
        foreach (var water in WaterPoints())
        {
            for (var x = water.Cell.X - 3; x <= water.Cell.X + 3; x++)
                for (var z = water.Cell.Z - 3; z <= water.Cell.Z + 3; z++) reserved.Add(new(x, z));
            foreach (var cell in LooseQueueGeometry.Corridor(CaptureWaterQueueCells(water.Id))) reserved.Add(cell);
        }
        foreach (var cell in ResponsePostReserved(_preparation)) reserved.Add(cell);
        for (var x = 90; x <= 101; x++) for (var z = 140; z <= 159; z++) reserved.Add(new(x, z));
        for (var x = MedicalRestCell.X - 1; x <= MedicalRestCell.X + 1; x++)
            for (var z = MedicalRestCell.Z - 1; z <= MedicalRestCell.Z + 1; z++) reserved.Add(new(x, z));
        if (_equipment is { } unit)
        {
            var centre = TraversalGrid.WorldToCell(unit.XMillimetres, unit.ZMillimetres);
            for (var x = centre.X - 5; x <= centre.X + 5; x++)
                for (var z = centre.Z - 5; z <= centre.Z + 5; z++) reserved.Add(new(x, z));
        }
        if (_preparation.WaterTowerOwned)
            for (var x = WaterTowerCell.X - 4; x <= WaterTowerCell.X + 4; x++)
                for (var z = WaterTowerCell.Z - 4; z <= WaterTowerCell.Z + 4; z++) reserved.Add(new(x, z));
        foreach (var cell in ToiletReservedCells(proposed).Append(ToiletQueueCell(proposed, 0)).Append(ToiletExitCell(proposed)))
            if (!terrain.Contains(cell) || !terrain.Get(cell).IsWalkable || reserved.Contains(cell))
                return "Toilet footprint, door sweep or approach overlaps terrain, a service or a protected route.";
        var blocked = terrain.Overrides.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var cell in _immersion.Vendors.SelectMany(ImmersionFootprint).Concat(ToiletSolidCells(proposed)))
            blocked[cell] = new(cell, GroundSurface.Grass, false);
        var grid = new TraversalGrid(blocked.Values);
        foreach (var destination in _immersion.Vendors.Select(ImmersionServiceCell).Concat(WaterPoints().Select(WaterPointServiceCell))
                     .Append(MedicalRestCell).Append(ToiletInsideCell(proposed)).Append(ToiletQueueCell(proposed, 0)))
            if (!DeterministicPathfinder.FindPath(grid, MedicalExitCell, destination).Found)
                return "Toilet would block an essential route or its own open entrance.";
        return null;
    }

    private CommandResult? ValidateToiletCommand(EntityId? target, MoveToiletCommand command)
    {
        if (target is not null || _immersion?.Toilet is not { } toilet || _preparation?.Status != PreparationStatus.Preparing)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Move the owned toilet before opening.");
        if (command.QuarterTurns is < 0 or > 3 || !new TraversalGrid().Contains(command.Cell))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Invalid toilet placement or rotation.");
        var proposed = toilet with { Cell = command.Cell, QuarterTurns = command.QuarterTurns };
        return ToiletPlacementError(proposed) is { } error ? CommandResult.Rejected(CommandReasonCode.InvalidParameter, error) : null;
    }

    private void ApplyToiletCommand(MoveToiletCommand command) => SetToilet(_immersion!.Toilet! with
    { Cell = command.Cell, QuarterTurns = command.QuarterTurns });

    private void BlockToilet()
    {
        if (_immersion?.Toilet is not { } toilet || _traversalGrid is null) return;
        var terrain = _traversalGrid.Overrides.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var cell in ToiletSolidCells(toilet)) terrain[cell] = new(cell, GroundSurface.Grass, false);
        _traversalGrid = new TraversalGrid(terrain.Values);
    }

    private static ToiletVisitKind ChooseToiletVisit(ImmersionPerson person) =>
        (person.AgentId + (ulong)person.ToiletVisits) % 4 == 0 ? ToiletVisitKind.Poo : ToiletVisitKind.Wee;

    private void ReleaseToiletPerson(ulong id, bool returnToListening)
    {
        var toilet = _immersion!.Toilet!;
        SetToilet(toilet with { Queue = toilet.Queue.Where(member => member != id).ToArray(),
            OwnerId = toilet.OwnerId == id ? null : toilet.OwnerId, DoorOpen = toilet.OwnerId == id ? false : toilet.DoorOpen,
            ServiceTicks = toilet.OwnerId == id ? 0 : toilet.ServiceTicks });
        var person = _immersion.People.Single(p => p.AgentId == id);
        SetImmersionPerson(person with { ToiletStage = ToiletVisitStage.None, ToiletChoice = null });
        if (returnToListening) ReturnToListening(id);
    }

    private void AdvanceToilet()
    {
        if (_immersion?.Toilet is not { } toilet || _preparation is not { } prep || !MedicalOperationsActive) return;
        if (toilet.InterruptedOccupantId is { } interrupted)
        {
            var nav = _navigationAgents[new(interrupted)];
            var centre = TraversalGrid.CellCentre(toilet.Cell);
            var dx = (long)nav.XMillimetres - centre.XMillimetres;
            var dz = (long)nav.ZMillimetres - centre.ZMillimetres;
            if (prep.People.Single(person => person.AgentId == interrupted).Departed ||
                dx * dx + dz * dz > 2_500_000)
            {
                SetToilet(toilet with { InterruptedOccupantId = null, DoorOpen = false });
                toilet = _immersion.Toilet!;
            }
        }
        var running = prep.Status == PreparationStatus.Running;
        for (var personIndex = 0; personIndex < _immersion.People.Length; personIndex++)
        {
            if (prep.People[personIndex].Departed) continue;
            var person = _immersion.People[personIndex];
            if (person.ToiletStage == ToiletVisitStage.None)
            {
                if (!running || toilet.IsFull || toilet.InterruptedOccupantId is not null ||
                    person.ToiletNeed < ToiletRules.NeedThreshold ||
                    CurrentTick % ToiletRules.DecisionEveryTicks != (long)(person.AgentId % ToiletRules.DecisionEveryTicks) ||
                    prep.People[personIndex].Role != ProtectedPersonRole.Guest ||
                    !ImmersionHandsAvailable(person.AgentId) || ImmersionOwnsNavigation(person.AgentId) ||
                    _medical!.Needs.Single(p => p.AgentId == person.AgentId).Intent != MedicalIntent.WatchShow ||
                    _livePerformance?.Performers.Any(p => p.AgentId == person.AgentId && (p.OnStage || p.InstrumentAttached)) == true)
                    continue;
                if (_immersion.People.Count(p => p.ToiletStage != ToiletVisitStage.None) >= ToiletRules.MaximumQueue ||
                    !toilet.CanAccept(ChooseToiletVisit(person))) continue;
                var queueCell = ToiletQueueCell(toilet, toilet.Queue.Length);
                if (!MedicalRouteExists(person.AgentId, queueCell)) continue;
                SetImmersionPerson(person with { ToiletStage = ToiletVisitStage.Approaching,
                    ToiletChoice = ChooseToiletVisit(person) });
                ApplyAgentDestination(new(person.AgentId), new(queueCell, "toilet.approach"));
                continue;
            }
            var nav = _navigationAgents[new(person.AgentId)];
            if (person.ToiletStage is ToiletVisitStage.Approaching or ToiletVisitStage.Queued &&
                (!running || toilet.IsFull || nav.IntentId?.StartsWith("toilet.", StringComparison.Ordinal) != true))
            { ReleaseToiletPerson(person.AgentId, running && nav.IntentId?.StartsWith("toilet.", StringComparison.Ordinal) == true); toilet = _immersion.Toilet!; continue; }
            if (person.ToiletStage == ToiletVisitStage.Approaching && nav.Action == AgentNavigationAction.Arrived &&
                nav.IntentId == "toilet.approach" && toilet.Queue.Length < ToiletRules.MaximumQueue)
            {
                toilet = toilet with { Queue = toilet.Queue.Append(person.AgentId).ToArray() };
                SetToilet(toilet); SetImmersionPerson(person with { ToiletStage = ToiletVisitStage.Queued });
            }
        }
        toilet = _immersion.Toilet!;
        if (toilet.OwnerId is null && toilet.Queue.Length == 0 &&
            !_immersion.People.Any(p => p.ToiletStage == ToiletVisitStage.Approaching))
        { ApplyToiletSmell(); return; }
        if (!running)
        {
            foreach (var id in toilet.Queue.Where(id => id != toilet.OwnerId).ToArray()) ReleaseToiletPerson(id, false);
            toilet = _immersion.Toilet!;
        }
        foreach (var person in _immersion.People.Where(p => p.ToiletStage == ToiletVisitStage.Approaching).OrderBy(p => p.AgentId))
        {
            var index = Math.Min(toilet.Queue.Length + _immersion.People.Count(p => p.ToiletStage == ToiletVisitStage.Approaching && p.AgentId < person.AgentId), ToiletRules.MaximumQueue - 1);
            var cell = ToiletQueueCell(toilet, index);
            if (_navigationAgents[new(person.AgentId)].Destination != cell)
                ApplyAgentDestination(new(person.AgentId), new(cell, "toilet.approach"));
        }
        for (var index = 0; index < toilet.Queue.Length; index++)
        {
            var id = toilet.Queue[index];
            if (id == toilet.OwnerId) continue;
            var cell = ToiletQueueCell(toilet, index);
            if (_navigationAgents[new(id)].Destination != cell)
                ApplyAgentDestination(new(id), new(cell, "toilet.queue"));
        }
        toilet = _immersion.Toilet!;
        if (running && !toilet.IsFull && toilet.InterruptedOccupantId is null &&
            toilet.OwnerId is null && toilet.Queue.Length > 0)
        {
            var id = toilet.Queue[0]; var nav = _navigationAgents[new(id)];
            if (!toilet.CanAccept(_immersion.People.Single(p => p.AgentId == id).ToiletChoice!.Value))
            { ReleaseToiletPerson(id, true); ApplyToiletSmell(); return; }
            if (nav.Action == AgentNavigationAction.Arrived && nav.Destination == ToiletQueueCell(toilet, 0))
            {
                SetToilet(toilet with { OwnerId = id, DoorOpen = true });
                SetImmersionPerson(_immersion.People.Single(p => p.AgentId == id) with { ToiletStage = ToiletVisitStage.Entering });
                ApplyAgentDestination(new(id), new(ToiletInsideCell(toilet), "toilet.enter"));
            }
        }
        toilet = _immersion.Toilet!;
        if (toilet.OwnerId is not { } owner) { ApplyToiletSmell(); return; }
        var active = _immersion.People.Single(p => p.AgentId == owner);
        var activeNav = _navigationAgents[new(owner)];
        if (running && active.ToiletStage is ToiletVisitStage.Entering or ToiletVisitStage.Leaving &&
            activeNav.IntentId is not ("toilet.enter" or "toilet.exit"))
        {
            InterruptToiletOwner(owner);
        }
        else if (!running && active.ToiletStage is ToiletVisitStage.Entering or ToiletVisitStage.Using)
        {
            SetToilet(toilet with { DoorOpen = true, ServiceTicks = 0 });
            SetImmersionPerson(active with { ToiletStage = ToiletVisitStage.Leaving });
            ApplyAgentDestination(new(owner), new(ToiletExitCell(toilet), "toilet.exit"));
        }
        else if (active.ToiletStage == ToiletVisitStage.Entering && activeNav.Action == AgentNavigationAction.Arrived && activeNav.IntentId == "toilet.enter")
        {
            SetToilet(toilet with { DoorOpen = false, ServiceTicks = active.ToiletChoice == ToiletVisitKind.Poo ? ToiletRules.PooServiceTicks : ToiletRules.WeeServiceTicks });
            SetImmersionPerson(active with { ToiletStage = ToiletVisitStage.Using });
        }
        else if (active.ToiletStage == ToiletVisitStage.Using)
        {
            if (activeNav.IntentId != "toilet.enter") InterruptToiletOwner(owner);
            else if (toilet.ServiceTicks > 1) SetToilet(toilet with { ServiceTicks = toilet.ServiceTicks - 1 });
            else
            {
                var poo = active.ToiletChoice == ToiletVisitKind.Poo;
                SetToilet(toilet with { ServiceTicks = 0, DoorOpen = true, WeeCount = toilet.WeeCount + (poo ? 0 : 1), PooCount = toilet.PooCount + (poo ? 1 : 0) });
                SetImmersionPerson(active with { ToiletStage = ToiletVisitStage.Leaving,
                    ToiletVisits = active.ToiletVisits + 1, ToiletNeed = 1_000 });
                ApplyAgentDestination(new(owner), new(ToiletExitCell(toilet), "toilet.exit"));
            }
        }
        else if (active.ToiletStage == ToiletVisitStage.Leaving && activeNav.Action == AgentNavigationAction.Arrived && activeNav.IntentId == "toilet.exit")
            ReleaseToiletPerson(owner, running);
        else if (active.ToiletStage == ToiletVisitStage.InterruptedLeaving)
        {
            var centre = TraversalGrid.CellCentre(toilet.Cell);
            var dx = (long)activeNav.XMillimetres - centre.XMillimetres;
            var dz = (long)activeNav.ZMillimetres - centre.ZMillimetres;
            if (dx * dx + dz * dz > 2_500_000) ReleaseToiletPerson(owner, false);
        }
        ApplyToiletSmell();
    }

    private void InterruptToiletOwner(ulong id)
    {
        var toilet = _immersion!.Toilet!;
        SetToilet(toilet with { Queue = toilet.Queue.Where(member => member != id).ToArray(),
            OwnerId = null, InterruptedOccupantId = id, DoorOpen = true, ServiceTicks = 0 });
        var person = _immersion.People.Single(item => item.AgentId == id);
        SetImmersionPerson(person with { ToiletStage = ToiletVisitStage.None, ToiletChoice = null });
    }

    private void ApplyToiletSmell()
    {
        if (CurrentTick % 80 != 0 || _immersion?.Toilet is not { } toilet || _preparation?.Status != PreparationStatus.Running) return;
        if (toilet.UsedMillilitres * 1_000 / toilet.CapacityMillilitres <= ToiletRules.SmellStartsPermille) return;
        _preparation = _preparation with { People = _preparation.People.Select(person =>
        {
            if (person.Role != ProtectedPersonRole.Guest || !person.Admitted || person.Departed ||
                !_navigationAgents.ContainsKey(new(person.AgentId))) return person;
            var penalty = ToiletSmellPenaltyPerSecond(person.AgentId);
            return person with { Satisfaction = Math.Max(0, person.Satisfaction - penalty) };
        }).ToArray() };
    }

    public int ToiletSmellPenaltyPerSecond(ulong agentId)
    {
        if (_immersion?.Toilet is not { } toilet || !_navigationAgents.TryGetValue(new(agentId), out var nav)) return 0;
        var fillPermille = Math.Min(1_000, toilet.UsedMillilitres * 1_000 / toilet.CapacityMillilitres);
        if (fillPermille <= ToiletRules.SmellStartsPermille) return 0;
        var strength = Math.Min(ToiletRules.SmellMaximumPenaltyPerSecond,
            (fillPermille - ToiletRules.SmellStartsPermille) * ToiletRules.SmellMaximumPenaltyPerSecond * 1_000 /
            ((1_000 - ToiletRules.SmellStartsPermille) * toilet.ContainmentPermille));
        var centre = TraversalGrid.CellCentre(toilet.Cell);
        var dx = (long)nav.XMillimetres - centre.XMillimetres;
        var dz = (long)nav.ZMillimetres - centre.ZMillimetres;
        var distanceSquared = dx * dx + dz * dz;
        if (distanceSquared >= (long)ToiletRules.SmellRadiusMillimetres * ToiletRules.SmellRadiusMillimetres) return 0;
        var distance = (int)Math.Sqrt(distanceSquared);
        return strength * (ToiletRules.SmellRadiusMillimetres - distance) / ToiletRules.SmellRadiusMillimetres;
    }

    private static string? ValidatePersistedToilet(SessionPersistenceSnapshot snapshot, ImmersionSnapshot immersion, GameSession geometry)
    {
        var toilet = immersion.Toilet!;
        if (toilet.Id != "toilet.main" || toilet.QuarterTurns is < 0 or > 3 ||
            toilet.CapacityMillilitres != ToiletRules.CapacityMillilitres ||
            toilet.ContainmentPermille != ToiletRules.ContainmentPermille ||
            toilet.WeeCount < 0 || toilet.PooCount < 0 || toilet.UsedMillilitres > toilet.CapacityMillilitres ||
            toilet.Queue is null || toilet.Queue.Length > ToiletRules.MaximumQueue ||
            toilet.Queue.Distinct().Count() != toilet.Queue.Length ||
            toilet.OwnerId is { } owner && (toilet.Queue.Length == 0 || toilet.Queue[0] != owner) ||
            toilet.OwnerId is null && (toilet.DoorOpen != (toilet.InterruptedOccupantId is not null) || toilet.ServiceTicks != 0) ||
            toilet.OwnerId is not null && toilet.InterruptedOccupantId is not null ||
            toilet.InterruptedOccupantId is { } interrupted &&
            (toilet.Queue.Contains(interrupted) || !immersion.People.Any(person => person.AgentId == interrupted)))
            return "Toilet identity, tank or exclusive owner invalid.";
        if (geometry.ToiletPlacementError(toilet) is { } issue) return issue;
        if (immersion.People.Any(person => person.ToiletNeed is < 0 or > ToiletRules.NeedMaximum ||
            person.ToiletVisits < 0 || !Enum.IsDefined(person.ToiletStage) ||
            (person.ToiletStage == ToiletVisitStage.None) != (person.ToiletChoice is null) ||
            person.ToiletChoice is { } choice && !Enum.IsDefined(choice) ||
            person.ToiletStage is ToiletVisitStage.Queued or ToiletVisitStage.Entering or ToiletVisitStage.Using or ToiletVisitStage.Leaving or ToiletVisitStage.InterruptedLeaving &&
            !toilet.Queue.Contains(person.AgentId)))
            return "Toilet person need, choice or queue ownership invalid.";
        if (toilet.Queue.Any(id => !immersion.People.Any(person => person.AgentId == id &&
            person.ToiletStage is not (ToiletVisitStage.None or ToiletVisitStage.Approaching))) ||
            immersion.People.Count(person => person.ToiletStage != ToiletVisitStage.None) > ToiletRules.MaximumQueue)
            return "Toilet physical queue membership invalid.";
        if (toilet.OwnerId is { } current)
        {
            var person = immersion.People.Single(p => p.AgentId == current);
            var maxTicks = person.ToiletChoice == ToiletVisitKind.Poo ? ToiletRules.PooServiceTicks : ToiletRules.WeeServiceTicks;
            if (person.ToiletStage is not (ToiletVisitStage.Entering or ToiletVisitStage.Using or ToiletVisitStage.Leaving or ToiletVisitStage.InterruptedLeaving) ||
                toilet.DoorOpen != (person.ToiletStage != ToiletVisitStage.Using) ||
                person.ToiletStage == ToiletVisitStage.Using && toilet.ServiceTicks is < 1 ||
                toilet.ServiceTicks > maxTicks ||
                person.ToiletStage != ToiletVisitStage.Using && toilet.ServiceTicks != 0)
                return "Toilet door or service owner stage invalid.";
        }
        if (snapshot.Preparation is { Status: not PreparationStatus.Preparing } &&
            (snapshot.TraversalGrid is null || ToiletSolidCells(toilet).Any(cell =>
                !snapshot.TraversalGrid.Cells.Any(saved => saved.X == cell.X && saved.Z == cell.Z && !saved.IsWalkable))))
            return "Toilet solid wall cells absent from saved traversal.";
        return null;
    }
}

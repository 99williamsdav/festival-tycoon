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
    // While a held beer is actually being consumed, add one extra need unit
    // every four ticks. No purchase, carrying or lasting intoxication modifier.
    public const int BeerConsumptionExtraGainEveryTicks = 4;
    public const int DecisionEveryTicks = 80;
    public const int WeeServiceTicks = 240;
    public const int PooServiceTicks = 400;
    // If another person's visit kind is not yet known, use the public 3:1
    // expected wee/poo mix rather than predicting a future private decision.
    public const int UnknownServiceTicks = (3 * WeeServiceTicks + PooServiceTicks) / 4;
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
    // Door signal only; ownership/fullness still independently controls admission.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool OccupiedIndicator => !DoorOpen && (OwnerId is not null || InterruptedOccupantId is not null);
    [System.Text.Json.Serialization.JsonIgnore]
    public int UsedMillilitres => WeeCount * ToiletRules.WeeMillilitres + PooCount * ToiletRules.PooMillilitres;
    [System.Text.Json.Serialization.JsonIgnore]
    public int FullPercent => Math.Clamp((UsedMillilitres * 100 + CapacityMillilitres - 1) / CapacityMillilitres, 0, 100);
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsFull => UsedMillilitres + ToiletRules.WeeMillilitres > CapacityMillilitres;
    public bool CanAccept(ToiletVisitKind kind) => UsedMillilitres +
        (kind == ToiletVisitKind.Poo ? ToiletRules.PooMillilitres : ToiletRules.WeeMillilitres) <= CapacityMillilitres;
    /// <summary>
    /// The queue's places, from the doorstep out, grown one at a time into free ground as people arrive, like the
    /// taps' and the stalls'. Nothing is set aside for a queue that hasn't formed.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public GridCell[]? QueueCells { get; init; }
}


public sealed partial class GameSession
{
    private static IEnumerable<ToiletFacility> EffectiveToilets(FacilitiesSnapshot? facilities) => facilities?.Toilets ?? [];
    private ToiletFacility GetToilet(string id) => Toilets.Single(item => item.Id == id);
    private void SetToilet(ToiletFacility toilet) => SetToilets(Toilets.Select(item => item.Id == toilet.Id ? toilet : item).ToArray());
    public ToiletFacility? CaptureToilet() => CaptureToilets().FirstOrDefault();
    public IReadOnlyList<ToiletFacility> CaptureToilets() => EffectiveToilets(_facilities)
        .Select(toilet => toilet with { Queue = toilet.Queue.ToArray() }).ToArray();

    public static GridCell ToiletInsideCell(ToiletFacility toilet) => toilet.Cell;
    public static GridCell ToiletQueueCell(ToiletFacility toilet, int index)
    {
        // Anyone past the queue's grown end waits at its tail until there's room.
        if (toilet.QueueCells is { Length: > 0 } cells) return cells[Math.Clamp(index, 0, cells.Length - 1)];
        return ToiletDoorstepCell(toilet, index);
    }

    /// <summary>The first place, at the door; and the old straight line, kept for saves without grown geometry.</summary>
    private static GridCell ToiletDoorstepCell(ToiletFacility toilet, int index = 0)
    {
        var offset = RotateWaterOffset(new(0, -5 - Math.Min(index, ToiletRules.MaximumQueue - 1) * 2), toilet.QuarterTurns);
        return new(toilet.Cell.X + offset.X, toilet.Cell.Z + offset.Z);
    }
    /// <summary>Just outside the door, where a steward works at a jammed lock.</summary>
    public static GridCell ToiletDoorFrontCell(ToiletFacility toilet)
    {
        var offset = RotateWaterOffset(new(0, -2), toilet.QuarterTurns);
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
    /// <summary>
    /// A toilet cubicle (doorway and interior) belongs to its occupant. Sidestepping a crowd must
    /// never push anyone else into it: they would meet the next user head-on in a one-cell doorway.
    /// </summary>
    private bool CubicleClosedTo(ulong agentId, GridCell cell)
    {
        if (_immersion is null) return false;
        foreach (var toilet in EffectiveToilets(_facilities))
        {
            var door = RotateWaterOffset(new(0, -1), toilet.QuarterTurns);
            if ((cell == toilet.Cell || cell == new GridCell(toilet.Cell.X + door.X, toilet.Cell.Z + door.Z)) &&
                toilet.OwnerId != agentId && toilet.InterruptedOccupantId != agentId) return true;
        }
        return false;
    }
    /// <summary>
    /// The open ground a toilet needs in front of its door: the apron out to the doorstep, where its queue starts, and
    /// the way round to the exit spot beside it. Toilets in a row share this walkway, so they can stand side by side.
    /// </summary>
    public static GridCell[] ToiletAccessCells(ToiletFacility toilet)
    {
        // The doorway itself, between the cubicle's side walls, then the open ground in front.
        var door = RotateWaterOffset(new(0, -1), toilet.QuarterTurns);
        var cells = new List<GridCell> { new(toilet.Cell.X + door.X, toilet.Cell.Z + door.Z) };
        for (var x = -1; x <= 4; x++)
            for (var z = -5; z <= -2; z++)
            {
                var offset = RotateWaterOffset(new(x, z), toilet.QuarterTurns);
                cells.Add(new(toilet.Cell.X + offset.X, toilet.Cell.Z + offset.Z));
            }
        return cells.ToArray();
    }

    /// <summary>The cubicle and the ground in front of it; the rest of the queue grows into free space once people arrive.</summary>
    public static GridCell[] ToiletReservedCells(ToiletFacility toilet) =>
        ToiletSolidCells(toilet).Concat(ToiletAccessCells(toilet)).Distinct().OrderBy(cell => cell.X).ThenBy(cell => cell.Z).ToArray();

    private void BlockToilet()
    {
        if (_immersion is null || _traversalGrid is null) return;
        var terrain = _traversalGrid.Overrides.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var cell in EffectiveToilets(_facilities).SelectMany(ToiletSolidCells)) terrain[cell] = new(cell, GroundSurface.Grass, false);
        _traversalGrid = new TraversalGrid(terrain.Values);
    }

    // Irritable bowels make every other visit a long one rather than one in four.
    private ToiletVisitKind ChooseToiletVisit(Person person) =>
        (person.Id + (ulong)person.ToiletVisits) % (HasIbs(person.Id) ? 2UL : 4UL) == 0 ? ToiletVisitKind.Poo : ToiletVisitKind.Wee;

    private void ReleaseToiletPerson(ulong id, bool returnToListening)
    {
        var person = _persons[id];
        var toilet = GetToilet(person.ToiletId ?? "toilet.main");
        SetToilet(toilet with { Queue = toilet.Queue.Where(member => member != id).ToArray(),
            OwnerId = toilet.OwnerId == id ? null : toilet.OwnerId, DoorOpen = toilet.OwnerId == id ? false : toilet.DoorOpen,
            ServiceTicks = toilet.OwnerId == id ? 0 : toilet.ServiceTicks });
        SetConsumption(person with { ToiletStage = ToiletVisitStage.None, ToiletChoice = null, ToiletId = null });
        if (returnToListening) ReturnToListening(id);
    }

    private void AdvanceToilet()
    {
        if (_immersion is null || _preparation is null || !MedicalOperationsActive) return;
        GrowToiletQueues();
        foreach (var facility in EffectiveToilets(_facilities).ToArray())
            AdvanceSingleToilet(GetToilet(facility.Id));
        ApplyToiletSmell();
    }

    private static int ToiletServiceDuration(ToiletVisitKind? kind) => kind switch
    { ToiletVisitKind.Wee => ToiletRules.WeeServiceTicks, ToiletVisitKind.Poo => ToiletRules.PooServiceTicks,
        _ => ToiletRules.UnknownServiceTicks };

    private QueuedServiceChoice.Candidate ToiletChoiceCandidate(ulong agentId, ToiletVisitKind kind, ToiletFacility toilet)
    {
        var people = PeopleIn(PersonView.Consumption);
        var position = Array.IndexOf(toilet.Queue, agentId);
        var currentApproach = people.Any(person => person.Id == agentId && person.ToiletId == toilet.Id &&
            person.ToiletStage == ToiletVisitStage.Approaching);
        var nav = _navigationAgents[new(agentId)];
        var destination = position >= 0 ? ToiletQueueCell(toilet, position) :
            currentApproach && nav.IntentId == "toilet.approach" && nav.Destination is { } assigned ? assigned :
            ToiletQueueCell(toilet, Math.Min(toilet.Queue.Length, ToiletRules.MaximumQueue - 1));
        var approaching = people.Where(person => person.ToiletId == toilet.Id &&
                person.ToiletStage == ToiletVisitStage.Approaching)
            .Select(person =>
            {
                var approachNav = _navigationAgents[new(person.Id)];
                var approachCell = approachNav.IntentId == "toilet.approach" && approachNav.Destination is { } assigned
                    ? assigned : ToiletQueueCell(toilet, Math.Min(toilet.Queue.Length, ToiletRules.MaximumQueue - 1));
                return new QueuedServiceChoice.Approacher(person.Id,
                    EstimateQueuedServiceWalkTicks(person.Id, approachCell), ToiletServiceDuration(person.ToiletChoice));
            })
            .Where(item => item.ArrivalTicks != int.MaxValue).ToArray();
        var active = toilet.OwnerId is { } owner ? people.Single(person => person.Id == owner) : null;
        var ownerRemaining = active?.ToiletStage switch
        { ToiletVisitStage.Using => toilet.ServiceTicks, ToiletVisitStage.Leaving => 0,
            ToiletVisitStage.Entering => ToiletServiceDuration(active.ToiletChoice), _ => 0 } + FaultDelayTicks(toilet.Id);
        return new(toilet.Id, EstimateQueuedServiceWalkTicks(agentId, destination), ToiletServiceDuration(kind),
            !toilet.IsFull && toilet.InterruptedOccupantId is null && toilet.CanAccept(kind),
            toilet.Queue.Length < ToiletRules.MaximumQueue && ToiletQueueHasRoom(toilet, agentId),
            toilet.Queue.Select(id => new QueuedServiceChoice.Member(id,
                ToiletServiceDuration(people.Single(person => person.Id == id).ToiletChoice))).ToArray(),
            toilet.OwnerId, ownerRemaining, approaching);
    }

    private ToiletFacility? BestToiletFor(ulong agentId, ToiletVisitKind kind)
    {
        var facilities = EffectiveToilets(_facilities).ToArray();
        var decision = QueuedServiceChoice.Choose(agentId, null,
            facilities.Select(toilet => ToiletChoiceCandidate(agentId, kind, toilet)).ToArray());
        return decision is null ? null : facilities.Single(toilet => toilet.Id == decision.Id);
    }

    private void AdvanceSingleToilet(ToiletFacility toilet)
    {
        var prep = _preparation!;
        if (toilet.InterruptedOccupantId is { } interrupted)
        {
            var nav = _navigationAgents[new(interrupted)];
            var centre = TraversalGrid.CellCentre(toilet.Cell);
            var dx = (long)nav.XMillimetres - centre.XMillimetres;
            var dz = (long)nav.ZMillimetres - centre.ZMillimetres;
            if (_persons[interrupted].Departed ||
                dx * dx + dz * dz > 2_500_000)
            {
                SetToilet(toilet with { InterruptedOccupantId = null, DoorOpen = false });
                toilet = GetToilet(toilet.Id);
            }
        }
        var running = prep.Status == PreparationStatus.Running;
        for (var personIndex = 0; personIndex < _persons.Count(PersonView.Consumption); personIndex++)
        {
            if (_persons.At(PersonView.Roster, personIndex).Departed) continue;
            var person = _persons.At(PersonView.Consumption, personIndex);
            // Visits start in the activity chooser.
            if (person.ToiletStage == ToiletVisitStage.None) continue;
            if ((person.ToiletId ?? "toilet.main") != toilet.Id) continue;
            var nav = _navigationAgents[new(person.Id)];
            if (person.ToiletStage is ToiletVisitStage.Approaching or ToiletVisitStage.Queued &&
                (!running || toilet.IsFull || !toilet.CanAccept(person.ToiletChoice!.Value) ||
                 nav.IntentId?.StartsWith("toilet.", StringComparison.Ordinal) != true))
            { ReleaseToiletPerson(person.Id, running && nav.IntentId?.StartsWith("toilet.", StringComparison.Ordinal) == true); toilet = GetToilet(toilet.Id); continue; }
            if (person.ToiletStage == ToiletVisitStage.Approaching && nav.Action == AgentNavigationAction.Arrived &&
                nav.IntentId == "toilet.approach" && toilet.Queue.Length < ToiletRules.MaximumQueue)
            {
                toilet = toilet with { Queue = toilet.Queue.Append(person.Id).ToArray() };
                SetToilet(toilet); SetConsumption(person with { ToiletStage = ToiletVisitStage.Queued });
            }
        }
        toilet = GetToilet(toilet.Id);
        if (toilet.OwnerId is null && toilet.Queue.Length == 0 &&
            !PeopleIn(PersonView.Consumption).Any(p => (p.ToiletId ?? "toilet.main") == toilet.Id && p.ToiletStage == ToiletVisitStage.Approaching))
            return;
        if (!running)
        {
            foreach (var id in toilet.Queue.Where(id => id != toilet.OwnerId).ToArray()) ReleaseToiletPerson(id, false);
            toilet = GetToilet(toilet.Id);
        }
        foreach (var person in PeopleIn(PersonView.Consumption).Where(p => (p.ToiletId ?? "toilet.main") == toilet.Id && p.ToiletStage == ToiletVisitStage.Approaching).OrderBy(p => p.Id))
        {
            var index = Math.Min(toilet.Queue.Length + PeopleIn(PersonView.Consumption).Count(p => (p.ToiletId ?? "toilet.main") == toilet.Id && p.ToiletStage == ToiletVisitStage.Approaching && p.Id < person.Id), ToiletRules.MaximumQueue - 1);
            var cell = ToiletQueueCell(toilet, index);
            if (_navigationAgents[new(person.Id)].Destination != cell)
                ApplyAgentDestination(new(person.Id), new(cell, "toilet.approach"));
        }
        for (var index = 0; index < toilet.Queue.Length; index++)
        {
            var id = toilet.Queue[index];
            if (id == toilet.OwnerId) continue;
            var cell = ToiletQueueCell(toilet, index);
            if (_navigationAgents[new(id)].Destination != cell)
                ApplyAgentDestination(new(id), new(cell, "toilet.queue"));
        }
        toilet = GetToilet(toilet.Id);
        if (running && !toilet.IsFull && toilet.InterruptedOccupantId is null &&
            toilet.OwnerId is null && toilet.Queue.Length > 0)
        {
            var id = toilet.Queue[0]; var nav = _navigationAgents[new(id)];
            if (!toilet.CanAccept(_persons[id].ToiletChoice!.Value))
            { ReleaseToiletPerson(id, true); return; }
            if (nav.Action == AgentNavigationAction.Arrived && nav.Destination == ToiletQueueCell(toilet, 0))
            {
                SetToilet(toilet with { OwnerId = id, DoorOpen = true });
                SetConsumption(_persons[id] with { ToiletStage = ToiletVisitStage.Entering });
                ApplyAgentDestination(new(id), new(ToiletInsideCell(toilet), "toilet.enter"));
            }
        }
        toilet = GetToilet(toilet.Id);
        if (toilet.OwnerId is not { } owner) return;
        var active = _persons[owner];
        var activeNav = _navigationAgents[new(owner)];
        if (running && active.ToiletStage is ToiletVisitStage.Entering or ToiletVisitStage.Leaving &&
            activeNav.IntentId is not ("toilet.enter" or "toilet.exit"))
        {
            InterruptToiletOwner(owner);
        }
        else if (!running && active.ToiletStage is ToiletVisitStage.Entering or ToiletVisitStage.Using && !StuckInToilet(owner))
        {
            SetToilet(toilet with { DoorOpen = true, ServiceTicks = 0 });
            SetConsumption(active with { ToiletStage = ToiletVisitStage.Leaving });
            ApplyAgentDestination(new(owner), new(ToiletExitCell(toilet), "toilet.exit"));
        }
        else if (active.ToiletStage == ToiletVisitStage.Entering && activeNav.Action == AgentNavigationAction.Arrived && activeNav.IntentId == "toilet.enter")
        {
            SetToilet(toilet with { DoorOpen = false, ServiceTicks = active.ToiletChoice == ToiletVisitKind.Poo ? ToiletRules.PooServiceTicks : ToiletRules.WeeServiceTicks });
            SetConsumption(active with { ToiletStage = ToiletVisitStage.Using });
        }
        else if (active.ToiletStage == ToiletVisitStage.Using)
        {
            if (activeNav.IntentId != "toilet.enter") InterruptToiletOwner(owner);
            else if (toilet.ServiceTicks > 1)
            {
                var remaining = toilet.ServiceTicks;
                var reduction = (Math.Max(0, active.ToiletNeed - 1_000) + remaining - 1) / remaining;
                SetConsumption(active with { ToiletNeed = Math.Max(1_000, active.ToiletNeed - reduction) });
                SetToilet(toilet with { ServiceTicks = remaining - 1 });
            }
            else if (ToiletDoorJammed(toilet, owner)) { } // Stuck: the visit is over but the door won't open.
            else
            {
                var poo = active.ToiletChoice == ToiletVisitKind.Poo;
                SetToilet(toilet with { ServiceTicks = 0, DoorOpen = true, WeeCount = toilet.WeeCount + (poo ? 0 : 1), PooCount = toilet.PooCount + (poo ? 1 : 0) });
                SetConsumption(active with { ToiletStage = ToiletVisitStage.Leaving,
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
    }

    private void InterruptToiletOwner(ulong id)
    {
        var person = _persons[id];
        var toilet = GetToilet(person.ToiletId ?? "toilet.main");
        SetToilet(toilet with { Queue = toilet.Queue.Where(member => member != id).ToArray(),
            OwnerId = null, InterruptedOccupantId = id, DoorOpen = true, ServiceTicks = 0 });
        SetConsumption(person with { ToiletStage = ToiletVisitStage.None, ToiletChoice = null, ToiletId = null });
    }

    private void ApplyToiletSmell()
    {
        if (CurrentTick % 80 != 0 || _immersion is null || _preparation?.Status != PreparationStatus.Running ||
            !EffectiveToilets(_facilities).Any(toilet => toilet.UsedMillilitres * 1_000 / toilet.CapacityMillilitres > ToiletRules.SmellStartsPermille)) return;
        foreach (var person in PeopleIn(PersonView.Roster).Select(person =>
        {
            if (person.Role != ProtectedPersonRole.Guest || !person.Admitted || person.Departed ||
                !_navigationAgents.ContainsKey(new(person.Id))) return person;
            var penalty = UnpleasantFor(person.Id, ToiletSmellPenaltyPerSecond(person.Id));
            return person with { Satisfaction = Math.Max(0, person.Satisfaction - penalty) };
        }).ToArray())
            SetPresence(person);
    }

    public int ToiletSmellPenaltyPerSecond(ulong agentId)
    {
        if (_immersion is null || !_navigationAgents.TryGetValue(new(agentId), out var nav)) return 0;
        return Math.Min(ToiletRules.SmellMaximumPenaltyPerSecond,
            EffectiveToilets(_facilities).Sum(toilet => ToiletSmellPenaltyAt(toilet, nav)));
    }

    private static int ToiletSmellPenaltyAt(ToiletFacility toilet, NavigationAgentState nav)
    {
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

    private static string? ValidatePersistedToilets(SessionPersistenceSnapshot snapshot, ImmersionSnapshot immersion, FacilitiesSnapshot facilities, GameSession geometry)
    {
        var toilets = EffectiveToilets(facilities).ToArray();
        if (toilets.Any(item => item is null) ||
            toilets.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != toilets.Length ||
            (!toilets.Select(item => item.Id).Order(StringComparer.Ordinal).SequenceEqual(
                snapshot.Preparation!.BuildPlacements.Where(item => item.Kind == BuildServiceKind.Toilet)
                    .Select(item => item.Id).Order(StringComparer.Ordinal)) || toilets.Length > BuildServiceLimit(BuildServiceKind.Toilet)) ||
            immersion.People.Any(person => person.ToiletStage != ToiletVisitStage.None &&
                !toilets.Any(toilet => toilet.Id == (person.ToiletId ?? "")) ||
                person.ToiletStage == ToiletVisitStage.None && person.ToiletId is not null) ||
            toilets.SelectMany(item => item.Queue).Distinct().Count() != toilets.Sum(item => item.Queue.Length))
            return "Toilet facility identities or person assignments invalid.";
        foreach (var toilet in toilets)
            if (ValidatePersistedToilet(snapshot, immersion, geometry, toilet) is { } issue) return issue;
        return null;
    }

    private static string? ValidatePersistedToilet(SessionPersistenceSnapshot snapshot, ImmersionSnapshot immersion, GameSession geometry,
        ToiletFacility toilet)
    {
        if (toilet.QuarterTurns is < 0 or > 3 ||
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
        // Geometry was already checked as one complete layout in ValidatePersistedPreparation.
        if (immersion.People.Any(person => person.ToiletNeed is < 0 or > ToiletRules.NeedMaximum ||
            person.LastToiletChoiceReviewTick is { } reviewed && (reviewed < 0 || reviewed > snapshot.CurrentTick) ||
            person.ToiletVisits < 0 || !Enum.IsDefined(person.ToiletStage) ||
            (person.ToiletStage == ToiletVisitStage.None) != (person.ToiletChoice is null) ||
            person.ToiletChoice is { } choice && !Enum.IsDefined(choice) ||
            person.ToiletStage is ToiletVisitStage.Queued or ToiletVisitStage.Entering or ToiletVisitStage.Using or ToiletVisitStage.Leaving or ToiletVisitStage.InterruptedLeaving &&
            (person.ToiletId ?? "") == toilet.Id && !toilet.Queue.Contains(person.AgentId)))
            return "Toilet person need, choice or queue ownership invalid.";
        if (toilet.Queue.Any(id => !immersion.People.Any(person => person.AgentId == id &&
            person.ToiletStage is not (ToiletVisitStage.None or ToiletVisitStage.Approaching))))
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
        var queueGrid = snapshot.TraversalGrid is { } savedTerrain
            ? new TraversalGrid(savedTerrain.Cells.Select(c => new TerrainCellOverride(new(c.X, c.Z), (GroundSurface)c.Surface, c.IsWalkable)))
            : new TraversalGrid(Fixtures.NavigationFixture.CreateLowerWitteringTerrain());
        if (ValidateToiletQueueGeometry(toilet, queueGrid, snapshot.Preparation) is { } queueIssue) return queueIssue;
        if (snapshot.Preparation is { Status: not PreparationStatus.Preparing } &&
            (snapshot.TraversalGrid is null || ToiletSolidCells(toilet).Any(cell =>
                !snapshot.TraversalGrid.Cells.Any(saved => saved.X == cell.X && saved.Z == cell.Z && !saved.IsWalkable))))
            return "Toilet solid wall cells absent from saved traversal.";
        return null;
    }
}

namespace Festival.Simulation;

public enum ServiceQueueAgentAction
{
    TravellingToQueue = 1,
    Waiting = 2,
    InService = 3,
    Departing = 4,
    Completed = 5,
    Failed = 6,
    Abandoned = 7,
}

public sealed record ServiceQueueAgentSnapshot(
    EntityId AgentId, ServiceQueueAgentAction Action, int? ReservedSlotIndex,
    int ExitIndex, bool OwnsExitReservation, long AdmissionTick, ulong ArrivalSequence);

public sealed record ServiceQueueSnapshot(
    EntityId Id, EntityId FestivalId, EntityId ServiceId, bool IsOpen,
    long UnitPricePennies, int ServiceDurationTicks, IReadOnlyList<EntityId> OrderedMembers,
    EntityId? ActiveOwnerId, int RemainingServiceTicks, ulong CompletionSequence,
    bool NeedsReassignment,
    IReadOnlyList<GridCell> QueueSlots, IReadOnlyList<GridCell> ExitCells,
    IReadOnlyList<ServiceQueueAgentSnapshot> Agents);

internal sealed class ServiceQueueAgentState
{
    public required EntityId AgentId { get; init; }
    public ServiceQueueAgentAction Action { get; set; }
    public int? ReservedSlotIndex { get; set; }
    public int ExitIndex { get; set; }
    public bool OwnsExitReservation { get; set; }
    public long AdmissionTick { get; set; }
    public ulong ArrivalSequence { get; set; }
}

internal sealed class ServiceQueueState
{
    public required EntityId Id { get; init; }
    public required EntityId FestivalId { get; init; }
    public required EntityId ServiceId { get; init; }
    public bool IsOpen { get; set; }
    public long UnitPricePennies { get; init; }
    public int ServiceDurationTicks { get; init; }
    public List<EntityId> OrderedMembers { get; } = [];
    public EntityId? ActiveOwnerId { get; set; }
    public int RemainingServiceTicks { get; set; }
    public ulong CompletionSequence { get; set; }
    public bool NeedsReassignment { get; set; }
    public List<GridCell> QueueSlots { get; } = [];
    public List<GridCell> ExitCells { get; } = [];
    public SortedDictionary<EntityId, ServiceQueueAgentState> Agents { get; } = [];
}

public sealed partial class GameSession
{
    private const ulong InternalQueueCommandBase = 1_000_000_000UL;
    private const ulong InternalQueueTransactionBase = 1_000_000UL;
    private readonly SortedDictionary<EntityId, ServiceQueueState> _serviceQueues = [];
    internal IReadOnlyDictionary<EntityId, ServiceQueueState> ServiceQueues => _serviceQueues;

    private CommandResult? ValidateInitializeServiceQueue(EntityId? targetId, InitializeServiceQueueFixtureCommand command)
    {
        if (targetId is not null || _traversalGrid is not null || _serviceQueues.Count != 0)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Service-queue fixture requires an empty navigation/queue session and no target.");
        if (command.Starts is null || command.QueueSlots is null || command.ExitCells is null || command.Terrain is null || command.OpeningCashPennies is null ||
            command.Starts.Count != command.OpeningCashPennies.Count || command.Starts.Count != command.ExitCells.Count || command.Starts.Count == 0 ||
            command.QueueSlots.Count < command.Starts.Count || command.StockQuantity < 0 || command.UnitCostBasisPennies < 0 ||
            command.UnitPricePennies <= 0 || command.ServiceDurationTicks <= 0)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Queue fixture needs matching starts, wallets and exits, enough physical slots, and valid stock/price/duration.");
        TraversalGrid grid;
        try { grid = new TraversalGrid(command.Terrain); }
        catch (ArgumentException exception) { return CommandResult.Rejected(CommandReasonCode.InvalidParameter, exception.Message); }
        var allCells = command.Starts.Concat(command.QueueSlots).Concat(command.ExitCells).ToArray();
        if (allCells.Distinct().Count() != allCells.Length || allCells.Any(cell => !grid.Contains(cell) || !grid.Get(cell).IsWalkable) || command.OpeningCashPennies.Any(value => value < 0))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Queue fixture cells must be unique, walkable and in bounds; cash cannot be negative.");
        return null;
    }

    private EntityId ApplyInitializeServiceQueue(InitializeServiceQueueFixtureCommand command)
    {
        _traversalGrid = new TraversalGrid(command.Terrain);
        var queueId = new EntityId(NextEntityId++);
        var festivalId = new EntityId(NextEntityId++);
        _festivalFinances.Add(festivalId, new FestivalFinanceState { OwnerId = festivalId, CashPennies = 0 });
        var serviceId = new EntityId(NextEntityId++);
        _ownedStocks.Add(serviceId, new OwnedStockState { ServiceId = serviceId, OwnerId = festivalId, Quantity = command.StockQuantity, UnitCostBasisPennies = command.UnitCostBasisPennies });
        var queue = new ServiceQueueState
        {
            Id = queueId, FestivalId = festivalId, ServiceId = serviceId, IsOpen = true,
            UnitPricePennies = command.UnitPricePennies, ServiceDurationTicks = command.ServiceDurationTicks,
        };
        queue.QueueSlots.AddRange(command.QueueSlots);
        queue.ExitCells.AddRange(command.ExitCells);
        for (var index = 0; index < command.Starts.Count; index++)
        {
            var id = new EntityId(NextEntityId++);
            _wallets.Add(id, new WalletState { OwnerId = id, CashPennies = command.OpeningCashPennies[index] });
            var position = TraversalGrid.CellCentre(command.Starts[index]);
            _navigationAgents.Add(id, new NavigationAgentState
            {
                Id = id, XMillimetres = position.XMillimetres, ZMillimetres = position.ZMillimetres,
                SegmentOriginXMillimetres = position.XMillimetres, SegmentOriginZMillimetres = position.ZMillimetres,
                Action = AgentNavigationAction.Idle,
            });
            queue.Agents.Add(id, new ServiceQueueAgentState
            {
                AgentId = id, Action = ServiceQueueAgentAction.TravellingToQueue,
                ExitIndex = index, AdmissionTick = CurrentTick, ArrivalSequence = 0,
            });
            queue.OrderedMembers.Add(id);
        }
        SortQueueMembers(queue);
        _serviceQueues.Add(queueId, queue);
        ReassignQueueSlots(queue);
        return queueId;
    }

    private CommandResult? ValidateServiceQueueTarget(EntityId? targetId) =>
        targetId is null || !_serviceQueues.ContainsKey(targetId.Value)
            ? CommandResult.Rejected(CommandReasonCode.UnknownTarget, "Service queue does not exist.") : null;

    private CommandResult? ValidateQueueProtectedDestination(EntityId? targetId)
    {
        if (targetId is { } id && _serviceQueues.Values.Any(queue => queue.OrderedMembers.Contains(id) ||
            (queue.Agents.TryGetValue(id, out var agent) && agent.OwnsExitReservation)))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Queued, in-service and service-exit attendee destinations are owned by service AI.");
        return null;
    }

    private CommandResult? ValidateEnqueueServiceQueueAgent(EntityId? targetId, EnqueueServiceQueueAgentCommand command, ulong submissionSequence)
    {
        var targetError = ValidateServiceQueueTarget(targetId);
        if (targetError is not null) return targetError;
        var queue = _serviceQueues[targetId!.Value];
        if (command.ArrivalSequence != submissionSequence)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Queue admission sequence must be the authoritative command submission sequence.");
        if (!queue.IsOpen || !queue.Agents.TryGetValue(command.AgentId, out var agent) ||
            queue.OrderedMembers.Contains(command.AgentId) || agent.Action is ServiceQueueAgentAction.Departing or ServiceQueueAgentAction.Completed)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Attendee is not eligible to join this open queue.");
        return null;
    }

    private void ApplyEnqueueServiceQueueAgent(EntityId queueId, EnqueueServiceQueueAgentCommand command, long admissionTick, ulong submissionSequence)
    {
        var queue = _serviceQueues[queueId];
        var agent = queue.Agents[command.AgentId];
        // A valid rejoin explicitly transfers navigation ownership from the exit
        // lifecycle back to the logical queue lifecycle.
        agent.OwnsExitReservation = false;
        agent.AdmissionTick = admissionTick;
        agent.ArrivalSequence = submissionSequence;
        agent.Action = ServiceQueueAgentAction.TravellingToQueue;
        queue.OrderedMembers.Add(command.AgentId);
        SortQueueMembers(queue);
        ReassignQueueSlots(queue);
    }

    private CommandResult? ValidateAbandonServiceQueueAgent(EntityId? targetId, AbandonServiceQueueCommand command)
    {
        var targetError = ValidateServiceQueueTarget(targetId);
        if (targetError is not null) return targetError;
        return !_serviceQueues[targetId!.Value].OrderedMembers.Contains(command.AgentId)
            ? CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Attendee is not currently queued.") : null;
    }

    private void ApplyAbandonServiceQueueAgent(EntityId queueId, EntityId agentId)
    {
        var queue = _serviceQueues[queueId];
        ReleaseQueueMember(queue, agentId, ServiceQueueAgentAction.Abandoned, depart: true);
    }

    private void ApplySetServiceQueueOpen(EntityId queueId, bool isOpen)
    {
        var queue = _serviceQueues[queueId];
        queue.IsOpen = isOpen;
        if (isOpen) return;
        foreach (var id in queue.OrderedMembers.ToArray()) ReleaseQueueMember(queue, id, ServiceQueueAgentAction.Abandoned, depart: true);
        queue.OrderedMembers.Clear();
        queue.ActiveOwnerId = null;
        queue.RemainingServiceTicks = 0;
        queue.NeedsReassignment = false;
    }

    private void AdvanceServiceQueues(List<SessionEvent> events)
    {
        foreach (var queue in _serviceQueues.Values)
        {
            var activeExitIndices = queue.Agents.Values.Where(item => item.OwnsExitReservation).Select(item => item.ExitIndex).ToArray();
            if (activeExitIndices.Distinct().Count() != activeExitIndices.Length)
                throw new InvalidOperationException("Service queue cannot have duplicate active exit reservations.");
            foreach (var queued in queue.Agents.Values.Where(item => item.OwnsExitReservation))
                if (_navigationAgents[queued.AgentId].Action == AgentNavigationAction.Arrived &&
                    _navigationAgents[queued.AgentId].Destination == queue.ExitCells[queued.ExitIndex])
                {
                    queued.OwnsExitReservation = false;
                    if (queued.Action == ServiceQueueAgentAction.Departing) queued.Action = ServiceQueueAgentAction.Completed;
                }
            if (queue.NeedsReassignment)
            {
                ReassignQueueSlots(queue);
                queue.NeedsReassignment = false;
            }
            var routeFailures = queue.OrderedMembers.Where(id => _navigationAgents[id].Action == AgentNavigationAction.NoRoute).ToArray();
            foreach (var id in routeFailures)
            {
                ReleaseQueueMember(queue, id, ServiceQueueAgentAction.Failed, depart: true);
                events.Add(new SessionEvent(CurrentTick, "service_queue_route_failed", id));
            }
            if (routeFailures.Length > 0) continue;
            if (!queue.IsOpen || queue.OrderedMembers.Count == 0) continue;
            var frontId = queue.OrderedMembers[0];
            var frontNavigation = _navigationAgents[frontId];
            if (queue.ActiveOwnerId is null)
            {
                if (frontNavigation.Action != AgentNavigationAction.Arrived || frontNavigation.Destination != queue.QueueSlots[0] ||
                    frontNavigation.IntentId != "ai.service-queue") continue;
                queue.ActiveOwnerId = frontId;
                queue.RemainingServiceTicks = queue.ServiceDurationTicks;
                queue.Agents[frontId].Action = ServiceQueueAgentAction.InService;
                events.Add(new SessionEvent(CurrentTick, "service_started", frontId));
                continue;
            }
            if (queue.ActiveOwnerId != frontId) throw new InvalidOperationException("Queue front must own the only active service slot.");
            if (!IsAtServicePosition(queue, frontId) || queue.Agents[frontId].Action != ServiceQueueAgentAction.InService)
            {
                ReleaseQueueMember(queue, frontId, ServiceQueueAgentAction.Abandoned, depart: true);
                events.Add(new SessionEvent(CurrentTick, "service_cancelled_absent", frontId));
                continue;
            }
            if (queue.RemainingServiceTicks > 0) queue.RemainingServiceTicks--;
            if (queue.RemainingServiceTicks != 0) continue;

            var completion = queue.CompletionSequence++;
            var commandValue = checked(InternalQueueCommandBase + queue.Id.Value * 10_000UL + completion);
            while (_acceptedCommandIds.Contains(new CommandId(commandValue))) commandValue++;
            var transactionValue = checked(InternalQueueTransactionBase + queue.Id.Value * 10_000UL + completion);
            while (_transactionIds.Contains(new TransactionId(transactionValue))) transactionValue++;
            var commandId = new CommandId(commandValue);
            var transactionId = new TransactionId(transactionValue);
            var purchase = new PurchaseItemCommand(transactionId, frontId, queue.FestivalId, queue.UnitPricePennies, 1);
            var envelope = new CommandEnvelope(commandId, CampaignId, Phase, CurrentTick, NextSubmissionSequence, queue.ServiceId, purchase);
            var rejection = ValidatePurchase(queue.ServiceId, purchase);
            if (rejection is null)
            {
                ApplyPurchase(envelope, queue.ServiceId, purchase);
                _acceptedCommandIds.Add(commandId);
                _appliedCommands.Add(new AppliedCommand(commandId, CurrentTick, NextSubmissionSequence, nameof(PurchaseItemCommand), queue.ServiceId));
                NextSubmissionSequence++;
                ReleaseQueueMember(queue, frontId, ServiceQueueAgentAction.Departing, depart: true);
                events.Add(new SessionEvent(CurrentTick, "service_purchase_completed", frontId));
            }
            else
            {
                ReleaseQueueMember(queue, frontId, ServiceQueueAgentAction.Failed, depart: true);
                events.Add(new SessionEvent(CurrentTick, rejection.ReasonCode == CommandReasonCode.OutOfStock ? "service_stockout" : "service_payment_failed", frontId));
            }
            queue.ActiveOwnerId = null;
            queue.RemainingServiceTicks = 0;
        }
    }

    private void ReleaseQueueMember(ServiceQueueState queue, EntityId agentId, ServiceQueueAgentAction action, bool depart)
    {
        queue.OrderedMembers.Remove(agentId);
        var queued = queue.Agents[agentId];
        queued.ReservedSlotIndex = null;
        queued.Action = action;
        queued.OwnsExitReservation = depart;
        queue.NeedsReassignment = queue.OrderedMembers.Count > 0;
        if (queue.ActiveOwnerId == agentId) { queue.ActiveOwnerId = null; queue.RemainingServiceTicks = 0; }
        if (depart)
        {
            SetDestinationDirect(agentId, queue.ExitCells[queued.ExitIndex], "ai.service-exit");
        }
        else
        {
            ClearDestination(agentId);
        }
    }

    private bool IsAtServicePosition(ServiceQueueState queue, EntityId agentId)
    {
        var navigation = _navigationAgents[agentId];
        var centre = TraversalGrid.CellCentre(queue.QueueSlots[0]);
        return navigation.Action == AgentNavigationAction.Arrived && navigation.Destination == queue.QueueSlots[0] &&
            navigation.IntentId == "ai.service-queue" &&
            navigation.XMillimetres == centre.XMillimetres && navigation.ZMillimetres == centre.ZMillimetres;
    }

    private static void SortQueueMembers(ServiceQueueState queue)
    {
        var active = queue.ActiveOwnerId;
        queue.OrderedMembers.Sort((left, right) =>
        {
            if (left == active) return right == active ? 0 : -1;
            if (right == active) return 1;
            var tick = queue.Agents[left].AdmissionTick.CompareTo(queue.Agents[right].AdmissionTick);
            if (tick != 0) return tick;
            var sequence = queue.Agents[left].ArrivalSequence.CompareTo(queue.Agents[right].ArrivalSequence);
            return sequence != 0 ? sequence : left.CompareTo(right);
        });
    }

    private void ReassignQueueSlots(ServiceQueueState queue)
    {
        for (var index = 0; index < queue.OrderedMembers.Count; index++)
        {
            var id = queue.OrderedMembers[index];
            var queued = queue.Agents[id];
            if (queued.ReservedSlotIndex == index && _navigationAgents[id].Destination == queue.QueueSlots[index]) continue;
            queued.ReservedSlotIndex = index;
            queued.Action = index == 0 ? ServiceQueueAgentAction.TravellingToQueue : ServiceQueueAgentAction.Waiting;
            SetDestinationDirect(id, queue.QueueSlots[index], "ai.service-queue");
        }
    }

    private void SetDestinationDirect(EntityId agentId, GridCell destination, string intentId)
    {
        ApplyAgentDestination(agentId, new SetAgentDestinationCommand(destination, intentId));
    }

    private void ClearDestination(EntityId agentId)
    {
        var agent = _navigationAgents[agentId];
        agent.Action = AgentNavigationAction.Idle;
        agent.Destination = null;
        agent.IntentId = null;
        agent.Route.Clear(); agent.RouteIndex = 0; agent.SegmentProgressMicrometres = 0; agent.MovementRemainder = 0;
        agent.SegmentOriginXMillimetres = agent.XMillimetres; agent.SegmentOriginZMillimetres = agent.ZMillimetres;
    }

    private ServiceQueueSnapshot[] CaptureServiceQueues() => _serviceQueues.Values.Select(queue => new ServiceQueueSnapshot(
        queue.Id, queue.FestivalId, queue.ServiceId, queue.IsOpen, queue.UnitPricePennies, queue.ServiceDurationTicks,
        queue.OrderedMembers.ToArray(), queue.ActiveOwnerId, queue.RemainingServiceTicks, queue.CompletionSequence, queue.NeedsReassignment,
        queue.QueueSlots.ToArray(), queue.ExitCells.ToArray(), queue.Agents.Values.Select(agent => new ServiceQueueAgentSnapshot(
            agent.AgentId, agent.Action, agent.ReservedSlotIndex, agent.ExitIndex, agent.OwnsExitReservation, agent.AdmissionTick, agent.ArrivalSequence)).ToArray())).ToArray();

    private PersistedServiceQueue[]? CapturePersistedServiceQueues() => _serviceQueues.Count == 0 ? null : _serviceQueues.Values.Select(queue => new PersistedServiceQueue(
        queue.Id.Value, queue.FestivalId.Value, queue.ServiceId.Value, queue.IsOpen, queue.UnitPricePennies, queue.ServiceDurationTicks,
        queue.OrderedMembers.Select(id => id.Value).ToArray(), queue.ActiveOwnerId?.Value, queue.RemainingServiceTicks, queue.CompletionSequence, queue.NeedsReassignment,
        queue.QueueSlots.Select(cell => new PersistedGridCell(cell.X, cell.Z)).ToArray(),
        queue.ExitCells.Select(cell => new PersistedGridCell(cell.X, cell.Z)).ToArray(),
        queue.Agents.Values.Select(agent => new PersistedQueueAgent(agent.AgentId.Value, (int)agent.Action, agent.ReservedSlotIndex, agent.ExitIndex, agent.OwnsExitReservation, agent.AdmissionTick, agent.ArrivalSequence)).ToArray())).ToArray();

    private void RestoreServiceQueues(PersistedServiceQueue[]? queues)
    {
        _serviceQueues.Clear();
        foreach (var item in queues ?? [])
        {
            var queue = new ServiceQueueState
            {
                Id = new EntityId(item.Id), FestivalId = new EntityId(item.FestivalId), ServiceId = new EntityId(item.ServiceId),
                IsOpen = item.IsOpen, UnitPricePennies = item.UnitPricePennies, ServiceDurationTicks = item.ServiceDurationTicks,
                ActiveOwnerId = item.ActiveOwnerId is { } owner ? new EntityId(owner) : null,
                RemainingServiceTicks = item.RemainingServiceTicks, CompletionSequence = item.CompletionSequence, NeedsReassignment = item.NeedsReassignment,
            };
            queue.OrderedMembers.AddRange(item.OrderedMembers.Select(id => new EntityId(id)));
            queue.QueueSlots.AddRange(item.QueueSlots.Select(cell => new GridCell(cell.X, cell.Z)));
            queue.ExitCells.AddRange(item.ExitCells.Select(cell => new GridCell(cell.X, cell.Z)));
            foreach (var agent in item.Agents)
            {
                var id = new EntityId(agent.AgentId);
                queue.Agents.Add(id, new ServiceQueueAgentState { AgentId = id, Action = (ServiceQueueAgentAction)agent.Action,
                    ReservedSlotIndex = agent.ReservedSlotIndex, ExitIndex = agent.ExitIndex, OwnsExitReservation = agent.OwnsExitReservation,
                    AdmissionTick = agent.AdmissionTick, ArrivalSequence = agent.ArrivalSequence });
            }
            _serviceQueues.Add(queue.Id, queue);
        }
    }

    private static string? ValidatePersistedServiceQueues(PersistedServiceQueue[]? queues, SessionPersistenceSnapshot snapshot)
    {
        if (queues is null) return null; // M0.05-M0.07 saves migrate by absence to no queue state.
        if (!StrictlyIncreasing(queues.Select(item => item.Id))) return "Service queues must have sorted unique IDs.";
        var walletIds = snapshot.Wallets.Select(item => item.OwnerId).ToHashSet();
        var navigation = (snapshot.NavigationAgents ?? []).ToDictionary(item => item.Id);
        var festivalIds = snapshot.FestivalFinances.Select(item => item.OwnerId).ToHashSet();
        var serviceIds = snapshot.OwnedStocks.Select(item => item.ServiceId).ToHashSet();
        if (snapshot.TraversalGrid is null && queues.Length > 0) return "Service queues require a traversal grid.";
        TraversalGrid? grid = null;
        if (snapshot.TraversalGrid is { } persistedGrid)
            grid = new TraversalGrid(persistedGrid.Cells.Select(item => new TerrainCellOverride(
                new GridCell(item.X, item.Z), (GroundSurface)item.Surface, item.IsWalkable,
                item.CostPermille, item.ElevationMillimetres, item.SlopePermille)));
        foreach (var queue in queues)
        {
            if (queue.Id == 0 || !festivalIds.Contains(queue.FestivalId) || !serviceIds.Contains(queue.ServiceId) || queue.UnitPricePennies <= 0 ||
                queue.ServiceDurationTicks <= 0 || queue.RemainingServiceTicks < 0 || queue.RemainingServiceTicks > queue.ServiceDurationTicks ||
                queue.OrderedMembers is null || queue.QueueSlots is null || queue.ExitCells is null || queue.Agents is null ||
                !StrictlyIncreasing(queue.Agents.Select(agent => agent.AgentId)) || queue.QueueSlots.Length < queue.Agents.Length || queue.ExitCells.Length != queue.Agents.Length)
                return $"Service queue {queue.Id} has invalid identity, configuration or collections.";
            var queueCells = queue.QueueSlots.Select(cell => new GridCell(cell.X, cell.Z)).ToArray();
            var exitCells = queue.ExitCells.Select(cell => new GridCell(cell.X, cell.Z)).ToArray();
            if (queueCells.Concat(exitCells).Distinct().Count() != queueCells.Length + exitCells.Length ||
                queueCells.Concat(exitCells).Any(cell => grid is null || !grid.Contains(cell) || !grid.Get(cell).IsWalkable))
                return $"Service queue {queue.Id} requires distinct walkable physical queue and exit cells.";
            var agents = queue.Agents.Select(agent => agent.AgentId).ToHashSet();
            if (agents.Any(id => !walletIds.Contains(id) || !navigation.ContainsKey(id)) || queue.OrderedMembers.Distinct().Count() != queue.OrderedMembers.Length ||
                queue.OrderedMembers.Any(id => !agents.Contains(id)) || queue.Agents.Any(agent => !Enum.IsDefined(typeof(ServiceQueueAgentAction), agent.Action) ||
                    agent.AdmissionTick < 0 || agent.AdmissionTick > snapshot.CurrentTick || agent.ArrivalSequence >= snapshot.NextSubmissionSequence ||
                    agent.ExitIndex < 0 || agent.ExitIndex >= queue.ExitCells.Length || agent.ReservedSlotIndex is < 0 || agent.ReservedSlotIndex >= queue.QueueSlots.Length))
                return $"Service queue {queue.Id} has invalid attendee or slot ownership.";
            if (!queue.IsOpen && (queue.OrderedMembers.Length != 0 || queue.ActiveOwnerId is not null || queue.RemainingServiceTicks != 0 ||
                queue.NeedsReassignment || queue.Agents.Any(agent => agent.ReservedSlotIndex is not null)))
                return $"Closed service queue {queue.Id} must have no members, owner, timer or physical reservations.";
            if (queue.ActiveOwnerId is { } owner && (queue.OrderedMembers.Length == 0 || queue.OrderedMembers[0] != owner || queue.RemainingServiceTicks <= 0))
                return $"Service queue {queue.Id} active owner is not its front member.";
            if (queue.ActiveOwnerId is null && queue.RemainingServiceTicks != 0) return $"Service queue {queue.Id} has a timer without an owner.";
            var expectedOrder = queue.OrderedMembers.OrderBy(id => id == queue.ActiveOwnerId ? 0 : 1)
                .ThenBy(id => queue.Agents.Single(agent => agent.AgentId == id).AdmissionTick)
                .ThenBy(id => queue.Agents.Single(agent => agent.AgentId == id).ArrivalSequence)
                .ThenBy(id => id).ToArray();
            if (!expectedOrder.SequenceEqual(queue.OrderedMembers)) return $"Service queue {queue.Id} logical admission order is invalid.";
            var reservedSlots = queue.Agents.Where(agent => agent.ReservedSlotIndex is not null).Select(agent => agent.ReservedSlotIndex!.Value).ToArray();
            if (reservedSlots.Distinct().Count() != reservedSlots.Length) return $"Service queue {queue.Id} has duplicate physical slot reservations.";
            var ownedExits = queue.Agents.Where(agent => agent.OwnsExitReservation).Select(agent => agent.ExitIndex).ToArray();
            if (ownedExits.Distinct().Count() != ownedExits.Length) return $"Service queue {queue.Id} has duplicate active exit reservations.";
            for (var index = 0; index < queue.OrderedMembers.Length; index++)
            {
                var agent = queue.Agents.Single(item => item.AgentId == queue.OrderedMembers[index]);
                if (agent.OwnsExitReservation) return $"Service queue {queue.Id} member cannot retain active exit ownership.";
                if (agent.ReservedSlotIndex is null) return $"Service queue {queue.Id} member lacks a physical reservation.";
                if (!queue.NeedsReassignment && agent.ReservedSlotIndex != index) return $"Service queue {queue.Id} physical reservations do not match logical order.";
                var nav = navigation[agent.AgentId];
                var destination = queueCells[agent.ReservedSlotIndex.Value];
                if (agent.AgentId != queue.ActiveOwnerId && agent.Action is not ((int)ServiceQueueAgentAction.TravellingToQueue) and not ((int)ServiceQueueAgentAction.Waiting))
                    return $"Service queue {queue.Id} non-owner member has an incoherent queue action.";
                if (nav.DestinationX != destination.X || nav.DestinationZ != destination.Z || nav.IntentId != "ai.service-queue" ||
                    nav.Action is not ((int)AgentNavigationAction.Travelling) and not ((int)AgentNavigationAction.Arrived) and not ((int)AgentNavigationAction.NoRoute))
                    return $"Service queue {queue.Id} member navigation intention does not match its reservation.";
            }
            if (queue.Agents.Any(agent => !queue.OrderedMembers.Contains(agent.AgentId) && agent.ReservedSlotIndex is not null))
                return $"Service queue {queue.Id} has a stale reservation outside its logical membership.";
            if (queue.ActiveOwnerId is { } activeOwner)
            {
                var agent = queue.Agents.Single(item => item.AgentId == activeOwner);
                var nav = navigation[activeOwner];
                var centre = TraversalGrid.CellCentre(queueCells[0]);
                if (agent.Action != (int)ServiceQueueAgentAction.InService || agent.ReservedSlotIndex != 0 ||
                    nav.Action != (int)AgentNavigationAction.Arrived || nav.IntentId != "ai.service-queue" ||
                    nav.DestinationX != queueCells[0].X || nav.DestinationZ != queueCells[0].Z ||
                    nav.XMillimetres != centre.XMillimetres || nav.ZMillimetres != centre.ZMillimetres)
                    return $"Service queue {queue.Id} active owner is not physically present with a coherent service intention.";
            }
            foreach (var agent in queue.Agents.Where(item => !queue.OrderedMembers.Contains(item.AgentId)))
            {
                if (agent.Action == (int)ServiceQueueAgentAction.InService)
                    return $"Service queue {queue.Id} has an in-service attendee outside logical membership.";
                if (agent.OwnsExitReservation)
                {
                    if (agent.Action is not ((int)ServiceQueueAgentAction.Departing) and not ((int)ServiceQueueAgentAction.Failed) and not ((int)ServiceQueueAgentAction.Abandoned))
                        return $"Service queue {queue.Id} has active exit ownership for an incoherent attendee action.";
                    var nav = navigation[agent.AgentId];
                    var exit = exitCells[agent.ExitIndex];
                    if (nav.DestinationX != exit.X || nav.DestinationZ != exit.Z || nav.IntentId != "ai.service-exit")
                        return $"Service queue {queue.Id} departing attendee has an incoherent exit intention.";
                }
                else if (agent.Action == (int)ServiceQueueAgentAction.Departing)
                    return $"Service queue {queue.Id} has a departing attendee without active exit ownership.";
            }
        }
        return null;
    }
}

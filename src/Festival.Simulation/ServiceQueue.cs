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
    int ExitIndex, ulong ArrivalSequence);

public sealed record ServiceQueueSnapshot(
    EntityId Id, EntityId FestivalId, EntityId ServiceId, bool IsOpen,
    long UnitPricePennies, int ServiceDurationTicks, IReadOnlyList<EntityId> OrderedMembers,
    EntityId? ActiveOwnerId, int RemainingServiceTicks, ulong CompletionSequence,
    IReadOnlyList<GridCell> QueueSlots, IReadOnlyList<GridCell> ExitCells,
    IReadOnlyList<ServiceQueueAgentSnapshot> Agents);

internal sealed class ServiceQueueAgentState
{
    public required EntityId AgentId { get; init; }
    public ServiceQueueAgentAction Action { get; set; }
    public int? ReservedSlotIndex { get; set; }
    public int ExitIndex { get; set; }
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
                ExitIndex = index, ArrivalSequence = (ulong)index,
            });
            queue.OrderedMembers.Add(id);
        }
        queue.OrderedMembers.Sort((left, right) =>
        {
            var arrival = queue.Agents[left].ArrivalSequence.CompareTo(queue.Agents[right].ArrivalSequence);
            return arrival != 0 ? arrival : left.CompareTo(right);
        });
        _serviceQueues.Add(queueId, queue);
        ReassignQueueSlots(queue);
        return queueId;
    }

    private CommandResult? ValidateServiceQueueTarget(EntityId? targetId) =>
        targetId is null || !_serviceQueues.ContainsKey(targetId.Value)
            ? CommandResult.Rejected(CommandReasonCode.UnknownTarget, "Service queue does not exist.") : null;

    private CommandResult? ValidateEnqueueServiceQueueAgent(EntityId? targetId, EnqueueServiceQueueAgentCommand command)
    {
        var targetError = ValidateServiceQueueTarget(targetId);
        if (targetError is not null) return targetError;
        var queue = _serviceQueues[targetId!.Value];
        if (!queue.IsOpen || !queue.Agents.TryGetValue(command.AgentId, out var agent) ||
            queue.OrderedMembers.Contains(command.AgentId) || agent.Action is ServiceQueueAgentAction.Departing or ServiceQueueAgentAction.Completed)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Attendee is not eligible to join this open queue.");
        return null;
    }

    private void ApplyEnqueueServiceQueueAgent(EntityId queueId, EnqueueServiceQueueAgentCommand command)
    {
        var queue = _serviceQueues[queueId];
        var agent = queue.Agents[command.AgentId];
        agent.ArrivalSequence = command.ArrivalSequence;
        agent.Action = ServiceQueueAgentAction.TravellingToQueue;
        queue.OrderedMembers.Add(command.AgentId);
        queue.OrderedMembers.Sort((left, right) =>
        {
            var arrival = queue.Agents[left].ArrivalSequence.CompareTo(queue.Agents[right].ArrivalSequence);
            return arrival != 0 ? arrival : left.CompareTo(right);
        });
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
        ReleaseQueueMember(queue, agentId, ServiceQueueAgentAction.Abandoned, depart: false);
        ReassignQueueSlots(queue);
    }

    private void ApplySetServiceQueueOpen(EntityId queueId, bool isOpen)
    {
        var queue = _serviceQueues[queueId];
        queue.IsOpen = isOpen;
        if (isOpen) return;
        foreach (var id in queue.OrderedMembers.ToArray()) ReleaseQueueMember(queue, id, ServiceQueueAgentAction.Abandoned, depart: false);
        queue.OrderedMembers.Clear();
        queue.ActiveOwnerId = null;
        queue.RemainingServiceTicks = 0;
    }

    private void AdvanceServiceQueues(List<SessionEvent> events)
    {
        foreach (var queue in _serviceQueues.Values)
        {
            foreach (var queued in queue.Agents.Values.Where(item => item.Action == ServiceQueueAgentAction.Departing))
                if (_navigationAgents[queued.AgentId].Action == AgentNavigationAction.Arrived)
                    queued.Action = ServiceQueueAgentAction.Completed;
            var routeFailures = queue.OrderedMembers.Where(id => _navigationAgents[id].Action == AgentNavigationAction.NoRoute).ToArray();
            foreach (var id in routeFailures)
            {
                ReleaseQueueMember(queue, id, ServiceQueueAgentAction.Failed, depart: false);
                events.Add(new SessionEvent(CurrentTick, "service_queue_route_failed", id));
            }
            if (routeFailures.Length > 0) ReassignQueueSlots(queue);
            if (!queue.IsOpen || queue.OrderedMembers.Count == 0) continue;
            var frontId = queue.OrderedMembers[0];
            var frontNavigation = _navigationAgents[frontId];
            if (queue.ActiveOwnerId is null)
            {
                if (frontNavigation.Action != AgentNavigationAction.Arrived || frontNavigation.Destination != queue.QueueSlots[0]) continue;
                queue.ActiveOwnerId = frontId;
                queue.RemainingServiceTicks = queue.ServiceDurationTicks;
                queue.Agents[frontId].Action = ServiceQueueAgentAction.InService;
                events.Add(new SessionEvent(CurrentTick, "service_started", frontId));
                continue;
            }
            if (queue.ActiveOwnerId != frontId) throw new InvalidOperationException("Queue front must own the only active service slot.");
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
                ReleaseQueueMember(queue, frontId, ServiceQueueAgentAction.Failed, depart: false);
                events.Add(new SessionEvent(CurrentTick, rejection.ReasonCode == CommandReasonCode.OutOfStock ? "service_stockout" : "service_payment_failed", frontId));
            }
            queue.ActiveOwnerId = null;
            queue.RemainingServiceTicks = 0;
            ReassignQueueSlots(queue);
        }
    }

    private void ReleaseQueueMember(ServiceQueueState queue, EntityId agentId, ServiceQueueAgentAction action, bool depart)
    {
        queue.OrderedMembers.Remove(agentId);
        var queued = queue.Agents[agentId];
        queued.ReservedSlotIndex = null;
        queued.Action = action;
        if (queue.ActiveOwnerId == agentId) { queue.ActiveOwnerId = null; queue.RemainingServiceTicks = 0; }
        if (depart)
        {
            SetDestinationDirect(agentId, queue.ExitCells[queued.ExitIndex]);
        }
        else
        {
            ClearDestination(agentId);
        }
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
            SetDestinationDirect(id, queue.QueueSlots[index]);
        }
    }

    private void SetDestinationDirect(EntityId agentId, GridCell destination)
    {
        ApplyAgentDestination(agentId, new SetAgentDestinationCommand(destination, "ai.service-queue"));
    }

    private void ClearDestination(EntityId agentId)
    {
        var agent = _navigationAgents[agentId];
        agent.Action = AgentNavigationAction.Idle;
        agent.Destination = null;
        agent.Route.Clear(); agent.RouteIndex = 0; agent.SegmentProgressMicrometres = 0; agent.MovementRemainder = 0;
        agent.SegmentOriginXMillimetres = agent.XMillimetres; agent.SegmentOriginZMillimetres = agent.ZMillimetres;
    }

    private ServiceQueueSnapshot[] CaptureServiceQueues() => _serviceQueues.Values.Select(queue => new ServiceQueueSnapshot(
        queue.Id, queue.FestivalId, queue.ServiceId, queue.IsOpen, queue.UnitPricePennies, queue.ServiceDurationTicks,
        queue.OrderedMembers.ToArray(), queue.ActiveOwnerId, queue.RemainingServiceTicks, queue.CompletionSequence,
        queue.QueueSlots.ToArray(), queue.ExitCells.ToArray(), queue.Agents.Values.Select(agent => new ServiceQueueAgentSnapshot(
            agent.AgentId, agent.Action, agent.ReservedSlotIndex, agent.ExitIndex, agent.ArrivalSequence)).ToArray())).ToArray();

    private PersistedServiceQueue[]? CapturePersistedServiceQueues() => _serviceQueues.Count == 0 ? null : _serviceQueues.Values.Select(queue => new PersistedServiceQueue(
        queue.Id.Value, queue.FestivalId.Value, queue.ServiceId.Value, queue.IsOpen, queue.UnitPricePennies, queue.ServiceDurationTicks,
        queue.OrderedMembers.Select(id => id.Value).ToArray(), queue.ActiveOwnerId?.Value, queue.RemainingServiceTicks, queue.CompletionSequence,
        queue.QueueSlots.Select(cell => new PersistedGridCell(cell.X, cell.Z)).ToArray(),
        queue.ExitCells.Select(cell => new PersistedGridCell(cell.X, cell.Z)).ToArray(),
        queue.Agents.Values.Select(agent => new PersistedQueueAgent(agent.AgentId.Value, (int)agent.Action, agent.ReservedSlotIndex, agent.ExitIndex, agent.ArrivalSequence)).ToArray())).ToArray();

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
                RemainingServiceTicks = item.RemainingServiceTicks, CompletionSequence = item.CompletionSequence,
            };
            queue.OrderedMembers.AddRange(item.OrderedMembers.Select(id => new EntityId(id)));
            queue.QueueSlots.AddRange(item.QueueSlots.Select(cell => new GridCell(cell.X, cell.Z)));
            queue.ExitCells.AddRange(item.ExitCells.Select(cell => new GridCell(cell.X, cell.Z)));
            foreach (var agent in item.Agents)
            {
                var id = new EntityId(agent.AgentId);
                queue.Agents.Add(id, new ServiceQueueAgentState { AgentId = id, Action = (ServiceQueueAgentAction)agent.Action,
                    ReservedSlotIndex = agent.ReservedSlotIndex, ExitIndex = agent.ExitIndex, ArrivalSequence = agent.ArrivalSequence });
            }
            _serviceQueues.Add(queue.Id, queue);
        }
    }

    private static string? ValidatePersistedServiceQueues(PersistedServiceQueue[]? queues, SessionPersistenceSnapshot snapshot)
    {
        if (queues is null) return null; // M0.05-M0.07 saves migrate by absence to no queue state.
        if (!StrictlyIncreasing(queues.Select(item => item.Id))) return "Service queues must have sorted unique IDs.";
        var walletIds = snapshot.Wallets.Select(item => item.OwnerId).ToHashSet();
        var navigationIds = (snapshot.NavigationAgents ?? []).Select(item => item.Id).ToHashSet();
        var festivalIds = snapshot.FestivalFinances.Select(item => item.OwnerId).ToHashSet();
        var serviceIds = snapshot.OwnedStocks.Select(item => item.ServiceId).ToHashSet();
        foreach (var queue in queues)
        {
            if (queue.Id == 0 || !festivalIds.Contains(queue.FestivalId) || !serviceIds.Contains(queue.ServiceId) || queue.UnitPricePennies <= 0 ||
                queue.ServiceDurationTicks <= 0 || queue.RemainingServiceTicks < 0 || queue.RemainingServiceTicks > queue.ServiceDurationTicks ||
                queue.OrderedMembers is null || queue.QueueSlots is null || queue.ExitCells is null || queue.Agents is null ||
                !StrictlyIncreasing(queue.Agents.Select(agent => agent.AgentId)) || queue.QueueSlots.Length < queue.Agents.Length || queue.ExitCells.Length != queue.Agents.Length)
                return $"Service queue {queue.Id} has invalid identity, configuration or collections.";
            var agents = queue.Agents.Select(agent => agent.AgentId).ToHashSet();
            if (agents.Any(id => !walletIds.Contains(id) || !navigationIds.Contains(id)) || queue.OrderedMembers.Distinct().Count() != queue.OrderedMembers.Length ||
                queue.OrderedMembers.Any(id => !agents.Contains(id)) || queue.Agents.Any(agent => !Enum.IsDefined(typeof(ServiceQueueAgentAction), agent.Action) ||
                    agent.ExitIndex < 0 || agent.ExitIndex >= queue.ExitCells.Length || agent.ReservedSlotIndex is < 0 || agent.ReservedSlotIndex >= queue.QueueSlots.Length))
                return $"Service queue {queue.Id} has invalid attendee or slot ownership.";
            if (queue.ActiveOwnerId is { } owner && (queue.OrderedMembers.Length == 0 || queue.OrderedMembers[0] != owner || queue.RemainingServiceTicks <= 0))
                return $"Service queue {queue.Id} active owner is not its front member.";
            if (queue.ActiveOwnerId is null && queue.RemainingServiceTicks != 0) return $"Service queue {queue.Id} has a timer without an owner.";
            for (var index = 0; index < queue.OrderedMembers.Length; index++)
            {
                var agent = queue.Agents.Single(item => item.AgentId == queue.OrderedMembers[index]);
                if (agent.ReservedSlotIndex != index) return $"Service queue {queue.Id} physical reservations do not match logical order.";
            }
            if (queue.Agents.Any(agent => !queue.OrderedMembers.Contains(agent.AgentId) && agent.ReservedSlotIndex is not null))
                return $"Service queue {queue.Id} has a stale reservation outside its logical membership.";
        }
        return null;
    }
}

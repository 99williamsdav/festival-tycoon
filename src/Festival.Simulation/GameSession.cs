using System.Diagnostics;

namespace Festival.Simulation;

public sealed partial class GameSession
{
    public const int TickDurationMilliseconds = 250;

    private readonly SortedDictionary<EntityId, FixtureRecordState> _fixtureRecords = [];
    private readonly SortedSet<CommandId> _acceptedCommandIds = [];
    private readonly List<AppliedCommand> _appliedCommands = [];
    private readonly SortedDictionary<RandomStreamId, Pcg32Random> _randomStreams = [];
    private readonly SortedDictionary<EntityId, WalletState> _wallets = [];
    private readonly SortedDictionary<EntityId, FestivalFinanceState> _festivalFinances = [];
    private readonly SortedDictionary<EntityId, OwnedStockState> _ownedStocks = [];
    private readonly SortedSet<TransactionId> _transactionIds = [];
    private readonly List<TransactionRecord> _transactions = [];

    /// <summary>Optional development-only measurement sink; never hashed or persisted.</summary>
    public ScaleDiagnosticProbe? ScaleDiagnosticProbe { get; set; }

    public GameSession(ulong campaignSeed, CampaignId? campaignId = null)
    {
        CampaignSeed = campaignSeed;
        CampaignId = campaignId ?? new CampaignId(campaignSeed);

        foreach (var streamId in Enum.GetValues<RandomStreamId>())
        {
            _randomStreams.Add(streamId, RandomStreamFactory.Create(campaignSeed, streamId));
        }
    }

    public CampaignId CampaignId { get; }

    public ulong CampaignSeed { get; }

    public SessionPhase Phase { get; private set; } = SessionPhase.Live;

    public long CurrentTick { get; private set; }

    public bool IsPaused { get; private set; }

    public RequestedSpeed RequestedSpeed { get; private set; } = RequestedSpeed.OneX;

    public ulong NextEntityId { get; private set; } = 1;

    public ulong NextSubmissionSequence { get; private set; }

    public IReadOnlyList<AppliedCommand> AppliedCommands => _appliedCommands.AsReadOnly();

    public IReadOnlyList<TransactionRecord> Transactions => _transactions.AsReadOnly();

    internal IReadOnlyDictionary<EntityId, FixtureRecordState> FixtureRecords => _fixtureRecords;

    internal IReadOnlySet<CommandId> AcceptedCommandIds => _acceptedCommandIds;

    internal IReadOnlyDictionary<RandomStreamId, Pcg32Random> RandomStreams => _randomStreams;

    internal IReadOnlyDictionary<EntityId, WalletState> Wallets => _wallets;

    internal IReadOnlyDictionary<EntityId, FestivalFinanceState> FestivalFinances => _festivalFinances;

    internal IReadOnlyDictionary<EntityId, OwnedStockState> OwnedStocks => _ownedStocks;

    public CommandResult Execute(CommandEnvelope envelope)
    {
        var rejection = ValidateEnvelope(envelope);
        if (rejection is not null)
        {
            return rejection;
        }

        EntityId? affectedTarget;
        switch (envelope.Command)
        {
            case EquipmentCommand equipment:
                affectedTarget = null;
                ApplyEquipmentCommand(equipment);
                break;
            case AcceptPreparationOfferCommand offer:
                affectedTarget = null;
                ApplyPreparationOffer(offer);
                break;
            case StartPreparedEditionCommand:
                affectedTarget = null;
                ApplyStartPreparedEdition();
                break;
            case CreateFixtureRecordCommand create:
                affectedTarget = new EntityId(NextEntityId);
                _fixtureRecords.Add(affectedTarget.Value, new FixtureRecordState
                {
                    Id = affectedTarget.Value,
                    Value = create.InitialValue,
                    RemainingTicks = create.ExpiresAfterTicks,
                });
                NextEntityId++;
                break;

            case ChangeFixtureValueCommand change:
                affectedTarget = envelope.TargetId;
                _fixtureRecords[affectedTarget!.Value].Value = change.NewValue;
                break;

            case SetPausedCommand pause:
                affectedTarget = null;
                IsPaused = pause.IsPaused;
                break;

            case ConfirmPlanningCommitmentCommand commitment:
                affectedTarget = null;
                ApplyConfirmPlanningCommitment(commitment);
                break;

            case AdvancePlanningWeekCommand:
                affectedTarget = null;
                ApplyAdvancePlanningWeek();
                break;

            case DismissCampaignTipCommand dismiss:
                affectedTarget = null;
                ApplyDismissTip(dismiss);
                break;

            case ForceFixtureDeathsCommand deaths:
                affectedTarget = null;
                ApplyForceFixtureDeaths(deaths);
                break;

            case SpendFixtureFavourCommand:
                affectedTarget = null;
                ApplySpendFixtureFavour();
                break;

            case ForceFixtureSafeCompletionCommand:
                affectedTarget = null;
                ApplyForceFixtureSafeCompletion();
                break;

            case CreateGuestWalletCommand createGuest:
                affectedTarget = new EntityId(NextEntityId++);
                _wallets.Add(affectedTarget.Value, new WalletState
                {
                    OwnerId = affectedTarget.Value,
                    CashPennies = createGuest.OpeningCashPennies,
                });
                break;

            case CreateFestivalFinanceCommand createFestival:
                affectedTarget = new EntityId(NextEntityId++);
                _festivalFinances.Add(affectedTarget.Value, new FestivalFinanceState
                {
                    OwnerId = affectedTarget.Value,
                    CashPennies = createFestival.OpeningCashPennies,
                });
                break;

            case CreateOwnedStockCommand createStock:
                affectedTarget = new EntityId(NextEntityId++);
                _ownedStocks.Add(affectedTarget.Value, new OwnedStockState
                {
                    ServiceId = affectedTarget.Value,
                    OwnerId = createStock.OwnerId,
                    Quantity = createStock.Quantity,
                    UnitCostBasisPennies = createStock.UnitCostBasisPennies,
                });
                break;

            case PurchaseItemCommand purchase:
                affectedTarget = envelope.TargetId;
                ApplyPurchase(envelope, affectedTarget!.Value, purchase);
                break;

            case InitializeNavigationFixtureCommand initializeNavigation:
                affectedTarget = ApplyInitializeNavigation(initializeNavigation);
                break;

            case SetAgentDestinationCommand destination:
                affectedTarget = envelope.TargetId;
                ApplyAgentDestination(affectedTarget!.Value, destination);
                break;

            case InitializeServiceQueueFixtureCommand initializeQueue:
                affectedTarget = ApplyInitializeServiceQueue(initializeQueue);
                break;

            case SetServiceQueueOpenCommand open:
                affectedTarget = envelope.TargetId;
                ApplySetServiceQueueOpen(affectedTarget!.Value, open.IsOpen);
                break;

            case EnqueueServiceQueueAgentCommand enqueue:
                affectedTarget = envelope.TargetId;
                ApplyEnqueueServiceQueueAgent(affectedTarget!.Value, enqueue, CurrentTick, envelope.SubmissionSequence);
                break;

            case AbandonServiceQueueCommand abandon:
                affectedTarget = envelope.TargetId;
                ApplyAbandonServiceQueueAgent(affectedTarget!.Value, abandon.AgentId);
                break;

            case RetargetServiceQueueAgentFixtureCommand retarget:
                affectedTarget = retarget.DestinationQueueId;
                ApplyRetargetServiceQueueAgentFixture(envelope.TargetId!.Value, retarget);
                break;

            default:
                return CommandResult.Rejected(CommandReasonCode.UnknownCommand, "Command type is not supported.");
        }

        _acceptedCommandIds.Add(envelope.CommandId);
        _appliedCommands.Add(new AppliedCommand(
            envelope.CommandId,
            CurrentTick,
            envelope.SubmissionSequence,
            envelope.Command.GetType().Name,
            affectedTarget));
        NextSubmissionSequence++;
        return CommandResult.Accepted(affectedTarget);
    }

    public AdvanceResult AdvanceTicks(int count)
    {
        var events = AdvanceAuthoritativeTicks(count);
        return new AdvanceResult(CaptureSnapshot(), events);
    }

    /// <summary>Advances authoritative state without constructing or hashing a public snapshot.</summary>
    public IReadOnlyList<SessionEvent> AdvanceWithoutSnapshot(int count) => AdvanceAuthoritativeTicks(count);

    private IReadOnlyList<SessionEvent> AdvanceAuthoritativeTicks(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (IsPaused || count == 0 || Phase is SessionPhase.Planning or SessionPhase.OpeningCheck || IsLifecycleEditionFrozen() ||
            _preparation?.Status is PreparationStatus.Failed or PreparationStatus.Finished)
            return Array.Empty<SessionEvent>();

        var events = new List<SessionEvent>();
        for (var index = 0; index < count; index++)
        {
            CurrentTick++;
            // A terminal hazard freezes before any other work at this tick.
            AdvanceEquipment();
            if (IsLifecycleEditionFrozen()) break;
            foreach (var record in _fixtureRecords.Values)
            {
                if (record.HasExpired || record.RemainingTicks <= 0)
                {
                    continue;
                }

                record.RemainingTicks--;
                if (record.RemainingTicks == 0)
                {
                    record.HasExpired = true;
                    events.Add(new SessionEvent(CurrentTick, "fixture_record_expired", record.Id));
                }
            }
            var navigationStart = Stopwatch.GetTimestamp();
            ScaleDiagnosticProbe?.SetPhase(DiagnosticPhase.Navigation);
            AdvanceNavigation(events);
            ScaleDiagnosticProbe?.AddNavigation(Stopwatch.GetTimestamp() - navigationStart);
            var queueStart = Stopwatch.GetTimestamp();
            ScaleDiagnosticProbe?.SetPhase(DiagnosticPhase.Queue);
            AdvanceServiceQueues(events);
            ScaleDiagnosticProbe?.AddQueue(Stopwatch.GetTimestamp() - queueStart);
            ScaleDiagnosticProbe?.SetPhase(DiagnosticPhase.None);
            AdvancePreparation();
            AdvanceLivePerformance();
            if (_preparation?.Status is PreparationStatus.Failed or PreparationStatus.Finished) break;
        }

        return events;
    }

    public uint NextRandom(RandomStreamId streamId) => _randomStreams[streamId].NextUInt32();

    public void RequestSpeed(RequestedSpeed speed)
    {
        if (!Enum.IsDefined(speed))
        {
            throw new ArgumentOutOfRangeException(nameof(speed));
        }

        RequestedSpeed = speed;
    }

    public SessionSnapshot CaptureSnapshot()
    {
        var snapshotStart = Stopwatch.GetTimestamp();
        ScaleDiagnosticProbe?.SetPhase(DiagnosticPhase.Snapshot);
        var records = _fixtureRecords.Values
            .Select(record => new FixtureRecordSnapshot(
                record.Id,
                record.Value,
                record.RemainingTicks,
                record.HasExpired))
            .ToArray();

        var wallets = _wallets.Values
            .Select(wallet => new WalletSnapshot(wallet.OwnerId, wallet.CashPennies))
            .ToArray();
        var festivalFinances = _festivalFinances.Values
            .Select(finance => new FestivalFinanceSnapshot(finance.OwnerId, finance.CashPennies))
            .ToArray();
        var ownedStocks = _ownedStocks.Values
            .Select(stock => new OwnedStockSnapshot(stock.ServiceId, stock.OwnerId, stock.Quantity, stock.UnitCostBasisPennies))
            .ToArray();

        var hashStart = Stopwatch.GetTimestamp();
        var hash = CanonicalStateHasher.Compute(this);
        ScaleDiagnosticProbe?.AddHash(Stopwatch.GetTimestamp() - hashStart);
        var snapshot = new SessionSnapshot(
            CampaignId,
            CampaignSeed,
            Phase,
            CurrentTick,
            IsPaused,
            RequestedSpeed,
            NextEntityId,
            NextSubmissionSequence,
            records,
            wallets,
            festivalFinances,
            ownedStocks,
            _transactions.ToArray(),
            CaptureNavigationAgents(),
            CaptureServiceQueues(),
            hash,
            CaptureCampaignPlanningSnapshot(),
            CaptureLifecycleSnapshot());
        ScaleDiagnosticProbe?.AddSnapshot(Stopwatch.GetTimestamp() - snapshotStart);
        ScaleDiagnosticProbe?.SetPhase(DiagnosticPhase.None);
        return snapshot;
    }

    public SessionObservation CaptureObservation()
    {
        var observationStart = Stopwatch.GetTimestamp();
        ScaleDiagnosticProbe?.SetPhase(DiagnosticPhase.Observation);
        var observation = new SessionObservation(
            CurrentTick,
            _navigationAgents.Values.Select(agent => new NavigationObservation(
                agent.Id, agent.XMillimetres, agent.ZMillimetres, agent.Action, agent.IntentId)).ToArray(),
            _serviceQueues.Values.Select(queue => new QueueObservation(
                queue.Id, queue.OrderedMembers.ToArray(), queue.ActiveOwnerId, queue.RemainingServiceTicks,
                queue.Agents.Values.Select(agent => new QueueAgentObservation(
                    agent.AgentId, agent.Action, agent.ReservedSlotIndex, agent.ExitIndex, agent.OwnsExitReservation,
                    agent.ReservedSlotIndex is { } slot && IsAtQueueSlot(queue, agent.AgentId, slot))).ToArray())).ToArray(),
            _wallets.Count,
            _transactions.Count);
        ScaleDiagnosticProbe?.AddObservation(Stopwatch.GetTimestamp() - observationStart);
        ScaleDiagnosticProbe?.SetPhase(DiagnosticPhase.None);
        return observation;
    }

    private bool IsAtQueueSlot(ServiceQueueState queue, EntityId agentId, int slot)
    {
        if ((uint)slot >= (uint)queue.QueueSlots.Count || !_navigationAgents.TryGetValue(agentId, out var navigation) ||
            navigation.Action != AgentNavigationAction.Arrived) return false;
        var centre = TraversalGrid.CellCentre(queue.QueueSlots[slot]);
        return navigation.XMillimetres == centre.XMillimetres && navigation.ZMillimetres == centre.ZMillimetres;
    }

    public SessionPersistenceSnapshot CapturePersistenceSnapshot() => new(
        CampaignId.Value,
        CampaignSeed,
        Pcg32Random.AlgorithmVersion,
        (int)Phase,
        CurrentTick,
        IsPaused,
        NextEntityId,
        NextSubmissionSequence,
        _fixtureRecords.Values.Select(item => new PersistedFixtureRecord(item.Id.Value, item.Value, item.RemainingTicks, item.HasExpired)).ToArray(),
        _acceptedCommandIds.Select(item => item.Value).ToArray(),
        _appliedCommands.Select(item => new PersistedAppliedCommand(item.CommandId.Value, item.Tick, item.SubmissionSequence, item.CommandType, item.TargetId?.Value)).ToArray(),
        _randomStreams.Select(pair => new PersistedRandomStream((int)pair.Key, pair.Value.State, pair.Value.Increment)).ToArray(),
        _wallets.Values.Select(item => new PersistedWallet(item.OwnerId.Value, item.CashPennies)).ToArray(),
        _festivalFinances.Values.Select(item => new PersistedFestivalFinance(item.OwnerId.Value, item.CashPennies)).ToArray(),
        _ownedStocks.Values.Select(item => new PersistedOwnedStock(item.ServiceId.Value, item.OwnerId.Value, item.Quantity, item.UnitCostBasisPennies)).ToArray(),
        _transactionIds.Select(item => item.Value).ToArray(),
        _transactions.Select(item => new PersistedTransaction(
            item.Id.Value,
            item.CommandId.Value,
            item.Tick,
            item.BuyerId.Value,
            item.FestivalId.Value,
            item.ServiceId.Value,
            item.Quantity,
            item.UnitPricePennies,
            item.Entries.Select(entry => new PersistedLedgerEntry(entry.OwnerId.Value, (int)entry.Account, entry.AmountPennies)).ToArray())).ToArray(),
        CanonicalStateHasher.Compute(this))
        {
            TraversalGrid = CaptureTraversalGrid(),
            NavigationAgents = CapturePersistedNavigationAgents(),
            ServiceQueues = CapturePersistedServiceQueues(),
            CampaignPlanning = CapturePersistedCampaignPlanning(),
            Lifecycle = CapturePersistedLifecycle(),
            Preparation = CapturePreparation(),
            Equipment = CaptureEquipment(),
            LivePerformance = CaptureLivePerformance(),
        };

    public static SessionRestoreResult Restore(SessionPersistenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var error = ValidatePersistenceSnapshot(snapshot);
        if (error is not null)
        {
            return SessionRestoreResult.Failure(error);
        }

        var session = new GameSession(snapshot.CampaignSeed, new CampaignId(snapshot.CampaignId))
        {
            Phase = (SessionPhase)snapshot.Phase,
            CurrentTick = snapshot.CurrentTick,
            IsPaused = snapshot.IsPaused,
            NextEntityId = snapshot.NextEntityId,
            NextSubmissionSequence = snapshot.NextSubmissionSequence,
        };
        session._fixtureRecords.Clear();
        foreach (var item in snapshot.FixtureRecords)
            session._fixtureRecords.Add(new EntityId(item.Id), new FixtureRecordState { Id = new EntityId(item.Id), Value = item.Value, RemainingTicks = item.RemainingTicks, HasExpired = item.HasExpired });
        session._acceptedCommandIds.Clear();
        foreach (var id in snapshot.AcceptedCommandIds) session._acceptedCommandIds.Add(new CommandId(id));
        session._appliedCommands.Clear();
        foreach (var item in snapshot.AppliedCommands)
            session._appliedCommands.Add(new AppliedCommand(new CommandId(item.CommandId), item.Tick, item.SubmissionSequence, item.CommandType, item.TargetId is { } target ? new EntityId(target) : null));
        session._randomStreams.Clear();
        foreach (var item in snapshot.RandomStreams)
            session._randomStreams.Add((RandomStreamId)item.StreamId, new Pcg32Random(item.State, item.Increment));
        session._wallets.Clear();
        foreach (var item in snapshot.Wallets)
            session._wallets.Add(new EntityId(item.OwnerId), new WalletState { OwnerId = new EntityId(item.OwnerId), CashPennies = item.CashPennies });
        session._festivalFinances.Clear();
        foreach (var item in snapshot.FestivalFinances)
            session._festivalFinances.Add(new EntityId(item.OwnerId), new FestivalFinanceState { OwnerId = new EntityId(item.OwnerId), CashPennies = item.CashPennies });
        session._ownedStocks.Clear();
        foreach (var item in snapshot.OwnedStocks)
            session._ownedStocks.Add(new EntityId(item.ServiceId), new OwnedStockState { ServiceId = new EntityId(item.ServiceId), OwnerId = new EntityId(item.OwnerId), Quantity = item.Quantity, UnitCostBasisPennies = item.UnitCostBasisPennies });
        session._transactionIds.Clear();
        foreach (var id in snapshot.CompletedTransactionIds) session._transactionIds.Add(new TransactionId(id));
        session._transactions.Clear();
        foreach (var item in snapshot.Transactions)
            session._transactions.Add(new TransactionRecord(
                new TransactionId(item.Id), new CommandId(item.CommandId), item.Tick, new EntityId(item.BuyerId),
                new EntityId(item.FestivalId), new EntityId(item.ServiceId), item.Quantity, item.UnitPricePennies,
                item.Entries.Select(entry => new LedgerEntry(new EntityId(entry.OwnerId), (LedgerAccountType)entry.Account, entry.AmountPennies))));

        session.RestoreNavigation(snapshot.TraversalGrid, snapshot.NavigationAgents);
        session.RestoreServiceQueues(snapshot.ServiceQueues, snapshot.NavigationAgents);
        session.RestoreCampaignPlanning(snapshot.CampaignPlanning);
        session.RestoreLifecycle(snapshot.Lifecycle);
        session._equipment = snapshot.Equipment is null ? null : snapshot.Equipment with { Evidence = snapshot.Equipment.Evidence.ToArray() };
        session._preparation = snapshot.Preparation is null ? null : System.Text.Json.JsonSerializer.Deserialize<PreparationSnapshot>(
            System.Text.Json.JsonSerializer.Serialize(snapshot.Preparation));
        session._livePerformance = snapshot.LivePerformance is null ? null : System.Text.Json.JsonSerializer.Deserialize<LivePerformanceSnapshot>(
            System.Text.Json.JsonSerializer.Serialize(snapshot.LivePerformance));

        var actualHash = CanonicalStateHasher.Compute(session);
        if (string.Equals(actualHash, snapshot.AuthoritativeHash, StringComparison.Ordinal)) return SessionRestoreResult.Success(session);
        if (snapshot.ServiceQueues is { Length: > 0 })
        {
            var modeWasAbsent = snapshot.ServiceQueues.Any(queue => queue.PhysicalArrivalAdmission is null);
            var compatibilityHash = CanonicalStateHasher.ComputeQueueCompatibility(session, includePhysicalQueueMode: !modeWasAbsent);
            if (string.Equals(compatibilityHash, snapshot.AuthoritativeHash, StringComparison.Ordinal)) return SessionRestoreResult.Success(session);
        }
        if (snapshot.CampaignPlanning is not null)
        {
            var compatibilityHash = CanonicalStateHasher.ComputeCampaignCompatibility(session);
            if (string.Equals(compatibilityHash, snapshot.AuthoritativeHash, StringComparison.Ordinal)) return SessionRestoreResult.Success(session);
        }
        return SessionRestoreResult.Failure($"Authoritative state hash mismatch after reconstruction: expected {snapshot.AuthoritativeHash}, got {actualHash}.");
    }

    private static string? ValidatePersistenceSnapshot(SessionPersistenceSnapshot snapshot)
    {
        if (!Enum.IsDefined(typeof(SessionPhase), snapshot.Phase)) return $"Unknown session phase {snapshot.Phase}.";
        if (!string.Equals(snapshot.RandomAlgorithmVersion, Pcg32Random.AlgorithmVersion, StringComparison.Ordinal))
            return $"Random algorithm '{snapshot.RandomAlgorithmVersion}' is incompatible; expected '{Pcg32Random.AlgorithmVersion}'.";
        if (snapshot.CurrentTick < 0) return "Current tick cannot be negative.";
        if (snapshot.NextEntityId == 0) return "Next entity ID must be positive.";
        if (string.IsNullOrWhiteSpace(snapshot.AuthoritativeHash)) return "Authoritative hash is required.";
        if (snapshot.FixtureRecords is null || snapshot.AcceptedCommandIds is null || snapshot.AppliedCommands is null ||
            snapshot.RandomStreams is null || snapshot.Wallets is null || snapshot.FestivalFinances is null ||
            snapshot.OwnedStocks is null || snapshot.CompletedTransactionIds is null || snapshot.Transactions is null)
            return "Every v1 authoritative collection must be present.";
        if (snapshot.FixtureRecords.Any(item => item is null) || snapshot.AppliedCommands.Any(item => item is null) ||
            snapshot.RandomStreams.Any(item => item is null) || snapshot.Wallets.Any(item => item is null) ||
            snapshot.FestivalFinances.Any(item => item is null) || snapshot.OwnedStocks.Any(item => item is null) ||
            snapshot.Transactions.Any(item => item is null)) return "Authoritative collections cannot contain null records.";
        if (!StrictlyIncreasing(snapshot.FixtureRecords.Select(item => item.Id)) || !StrictlyIncreasing(snapshot.AcceptedCommandIds) ||
            !StrictlyIncreasing(snapshot.RandomStreams.Select(item => (ulong)item.StreamId)) || !StrictlyIncreasing(snapshot.Wallets.Select(item => item.OwnerId)) ||
            !StrictlyIncreasing(snapshot.FestivalFinances.Select(item => item.OwnerId)) || !StrictlyIncreasing(snapshot.OwnedStocks.Select(item => item.ServiceId)) ||
            !StrictlyIncreasing(snapshot.CompletedTransactionIds)) return "ID-keyed authoritative collections must be sorted and unique.";
        var expectedStreams = Enum.GetValues<RandomStreamId>().Select(item => (int)item).ToArray();
        if (!snapshot.RandomStreams.Select(item => item.StreamId).SequenceEqual(expectedStreams) || snapshot.RandomStreams.Any(item => (item.Increment & 1UL) == 0))
            return $"Random streams must contain each {Pcg32Random.AlgorithmVersion} stream exactly once with an odd increment.";
        if (snapshot.NextSubmissionSequence != (ulong)snapshot.AppliedCommands.Length || snapshot.AppliedCommands.Where((item, index) => item.SubmissionSequence != (ulong)index).Any())
            return "Applied command sequences must be contiguous and match next submission sequence.";
        if (!snapshot.AppliedCommands.Select(item => item.CommandId).Order().SequenceEqual(snapshot.AcceptedCommandIds))
            return "Accepted command IDs must exactly match applied commands.";
        if (snapshot.FixtureRecords.Any(item => item.Id == 0 || item.RemainingTicks < 0)) return "Fixture record identity/progress is invalid.";
        if (snapshot.Wallets.Any(item => item.OwnerId == 0 || item.CashPennies < 0) || snapshot.FestivalFinances.Any(item => item.OwnerId == 0 || item.CashPennies < 0))
            return "Wallet and festival cash owners must be present with nonnegative cash.";
        var festivalIds = snapshot.FestivalFinances.Select(item => item.OwnerId).ToHashSet();
        if (snapshot.OwnedStocks.Any(item => item.ServiceId == 0 || !festivalIds.Contains(item.OwnerId) || item.Quantity < 0 || item.UnitCostBasisPennies < 0))
            return "Owned stock must have a valid festival owner and nonnegative quantity/cost basis.";
        var navigationError = ValidatePersistedNavigation(snapshot.TraversalGrid, snapshot.NavigationAgents);
        if (navigationError is not null) return navigationError;
        var queueError = ValidatePersistedServiceQueues(snapshot.ServiceQueues, snapshot);
        if (queueError is not null) return queueError;
        var campaignError = ValidatePersistedCampaignPlanning(snapshot.CampaignPlanning, snapshot);
        if (campaignError is not null) return campaignError;
        var lifecycleError = ValidatePersistedLifecycle(snapshot.Lifecycle);
        if (lifecycleError is not null) return lifecycleError;
        var preparationError = ValidatePersistedPreparation(snapshot.Preparation, snapshot);
        if (preparationError is not null) return preparationError;
        var equipmentError = ValidatePersistedEquipment(snapshot.Equipment, snapshot);
        if (equipmentError is not null) return equipmentError;
        var livePerformanceError = ValidatePersistedLivePerformance(snapshot.LivePerformance, snapshot);
        if (livePerformanceError is not null) return livePerformanceError;
        var ownedEntityIds = snapshot.FixtureRecords.Select(item => item.Id).Concat(snapshot.FestivalFinances.Select(item => item.OwnerId))
            .Concat(snapshot.OwnedStocks.Select(item => item.ServiceId)).Concat((snapshot.ServiceQueues ?? []).Select(item => item.Id)).ToArray();
        if (ownedEntityIds.Distinct().Count() != ownedEntityIds.Length || ownedEntityIds.Any(id => id >= snapshot.NextEntityId))
            return "Standalone entity IDs must be unique and lower than the next entity ID.";
        var walletIdsForIdentity = snapshot.Wallets.Select(item => item.OwnerId).ToArray();
        var navigationIdsForIdentity = (snapshot.NavigationAgents ?? []).Select(item => item.Id).ToArray();
        if (walletIdsForIdentity.Concat(navigationIdsForIdentity).Any(id => id >= snapshot.NextEntityId) ||
            walletIdsForIdentity.Any(id => ownedEntityIds.Contains(id)) || navigationIdsForIdentity.Any(id => ownedEntityIds.Contains(id)))
            return "Agent IDs must be lower than the next entity ID and distinct from standalone entities.";
        var knownEntities = ownedEntityIds.Concat(walletIdsForIdentity).Concat(navigationIdsForIdentity).ToHashSet();
        if (snapshot.AppliedCommands.Any(item => item.CommandId == 0 || item.Tick < 0 || item.Tick > snapshot.CurrentTick ||
            string.IsNullOrWhiteSpace(item.CommandType) || (item.TargetId is { } target && !knownEntities.Contains(target))))
            return "Applied commands contain invalid identities, ticks, types or targets.";
        if (!snapshot.Transactions.Select(item => item.Id).Order().SequenceEqual(snapshot.CompletedTransactionIds))
            return "Completed transaction IDs must exactly match transaction records in accepted order.";
        var accepted = snapshot.AcceptedCommandIds.ToHashSet();
        var wallets = snapshot.Wallets.Select(item => item.OwnerId).ToHashSet();
        var stocks = snapshot.OwnedStocks.ToDictionary(item => item.ServiceId);
        foreach (var item in snapshot.Transactions)
        {
            if (item.Id == 0 || !accepted.Contains(item.CommandId) || !wallets.Contains(item.BuyerId) || !festivalIds.Contains(item.FestivalId) ||
                !stocks.TryGetValue(item.ServiceId, out var stock) || stock.OwnerId != item.FestivalId || item.Quantity <= 0 || item.UnitPricePennies <= 0 ||
                item.Tick < 0 || item.Tick > snapshot.CurrentTick || item.Entries is null || item.Entries.Any(entry => entry is null) ||
                item.Entries.Sum(entry => (decimal)entry.AmountPennies) != 0m)
                return $"Transaction {item.Id} has invalid identities, amounts or unbalanced entries.";
            if (item.Entries.Any(entry => !Enum.IsDefined(typeof(LedgerAccountType), entry.Account))) return $"Transaction {item.Id} has an unknown ledger account.";
            var applied = snapshot.AppliedCommands.SingleOrDefault(command => command.CommandId == item.CommandId);
            if (applied is null || applied.CommandType != nameof(PurchaseItemCommand) || applied.TargetId != item.ServiceId)
                return $"Transaction {item.Id} does not resolve to its accepted purchase command.";
            if (!TryCalculateTotal(item.UnitPricePennies, item.Quantity, out var saleTotal) ||
                !TryCalculateTotal(stock.UnitCostBasisPennies, item.Quantity, out var inventoryCost))
                return $"Transaction {item.Id} totals exceed the supported integer-penny range.";
            var expectedEntries = new PersistedLedgerEntry[]
            {
                new(item.BuyerId, (int)LedgerAccountType.CashAsset, -saleTotal),
                new(item.BuyerId, (int)LedgerAccountType.GuestSpending, saleTotal),
                new(item.FestivalId, (int)LedgerAccountType.CashAsset, saleTotal),
                new(item.FestivalId, (int)LedgerAccountType.SalesRevenue, -saleTotal),
                new(item.FestivalId, (int)LedgerAccountType.CostOfGoodsSold, inventoryCost),
                new(item.FestivalId, (int)LedgerAccountType.InventoryAsset, -inventoryCost),
            };
            if (!item.Entries.SequenceEqual(expectedEntries)) return $"Transaction {item.Id} ledger entries do not match its sale and stock cost.";
        }
        return null;
    }

    private static bool StrictlyIncreasing(IEnumerable<ulong> values)
    {
        var first = true;
        var previous = 0UL;
        foreach (var value in values)
        {
            if (!first && value <= previous) return false;
            first = false;
            previous = value;
        }
        return true;
    }

    private CommandResult? ValidateEnvelope(CommandEnvelope envelope)
    {
        if (envelope.CommandId.Value == 0)
        {
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Command ID must be nonzero.");
        }

        if (_acceptedCommandIds.Contains(envelope.CommandId))
        {
            return CommandResult.Rejected(CommandReasonCode.DuplicateCommand, "Command ID was already accepted.");
        }

        if (envelope.CampaignId != CampaignId)
        {
            return CommandResult.Rejected(CommandReasonCode.WrongCampaign, "Command campaign does not match this session.");
        }

        if (envelope.ExpectedPhase != Phase)
        {
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Command is not valid in the current phase.");
        }

        if (envelope.Tick != CurrentTick)
        {
            return CommandResult.Rejected(CommandReasonCode.WrongTick, "Command must target the current deterministic tick.");
        }

        if (envelope.SubmissionSequence != NextSubmissionSequence)
        {
            return CommandResult.Rejected(CommandReasonCode.OutOfOrderSubmission, "Command submission sequence is not next.");
        }

        var lifecycleFrozen = ValidateLifecycleFrozenCommand(envelope.Command);
        if (lifecycleFrozen is not null) return lifecycleFrozen;
        if (_preparation is not null && envelope.Command is not (AcceptPreparationOfferCommand or StartPreparedEditionCommand or SetPausedCommand or EquipmentCommand))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Fixture and planning commands are unavailable in prepared editions.");
        if (_preparation?.Status is PreparationStatus.Failed or PreparationStatus.Finished)
            return CommandResult.Rejected(CommandReasonCode.EditionFrozen, "The edition is settled.");

        return envelope.Command switch
        {
            EquipmentCommand equipment => ValidateEquipmentCommand(envelope.TargetId, equipment),
            AcceptPreparationOfferCommand or StartPreparedEditionCommand => ValidatePreparationCommand(envelope.TargetId, envelope.Command),
            CreateFixtureRecordCommand create when envelope.TargetId is not null || create.ExpiresAfterTicks <= 0 =>
                CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Fixture creation requires no target and a positive expiry."),
            ChangeFixtureValueCommand when envelope.TargetId is null || !_fixtureRecords.ContainsKey(envelope.TargetId.Value) =>
                CommandResult.Rejected(CommandReasonCode.UnknownTarget, "Fixture target does not exist."),
            SetPausedCommand when envelope.TargetId is not null =>
                CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Pause command does not accept a target."),
            ConfirmPlanningCommitmentCommand commitment => ValidateConfirmPlanningCommitment(envelope.TargetId, commitment),
            AdvancePlanningWeekCommand => ValidateAdvancePlanningWeek(envelope.TargetId),
            DismissCampaignTipCommand dismiss => ValidateDismissCampaignTip(envelope.TargetId, dismiss),
            ForceFixtureDeathsCommand deaths => ValidateForceFixtureDeaths(envelope.TargetId, deaths),
            SpendFixtureFavourCommand => ValidateSpendFixtureFavour(envelope.TargetId),
            ForceFixtureSafeCompletionCommand => ValidateForceFixtureSafeCompletion(envelope.TargetId),
            CreateGuestWalletCommand create when envelope.TargetId is not null || create.OpeningCashPennies < 0 =>
                CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Guest setup requires no target and nonnegative opening cash."),
            CreateFestivalFinanceCommand create when envelope.TargetId is not null || create.OpeningCashPennies < 0 =>
                CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Festival setup requires no target and nonnegative opening cash."),
            CreateOwnedStockCommand create when envelope.TargetId is not null || create.Quantity < 0 || create.UnitCostBasisPennies < 0 =>
                CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Stock setup requires no target and nonnegative quantity and cost basis."),
            CreateOwnedStockCommand create when !_festivalFinances.ContainsKey(create.OwnerId) =>
                CommandResult.Rejected(CommandReasonCode.UnknownOwner, "Stock owner does not exist."),
            PurchaseItemCommand purchase => ValidatePurchase(envelope.TargetId, purchase),
            InitializeNavigationFixtureCommand initialize => ValidateInitializeNavigation(envelope.TargetId, initialize),
            SetAgentDestinationCommand destination => ValidateQueueProtectedDestination(envelope.TargetId) ?? ValidateAgentDestination(envelope.TargetId, destination),
            InitializeServiceQueueFixtureCommand initialize => ValidateInitializeServiceQueue(envelope.TargetId, initialize),
            SetServiceQueueOpenCommand => ValidateServiceQueueTarget(envelope.TargetId),
            EnqueueServiceQueueAgentCommand enqueue => ValidateEnqueueServiceQueueAgent(envelope.TargetId, enqueue, envelope.SubmissionSequence),
            AbandonServiceQueueCommand abandon => ValidateAbandonServiceQueueAgent(envelope.TargetId, abandon),
            RetargetServiceQueueAgentFixtureCommand retarget => ValidateRetargetServiceQueueAgentFixture(envelope.TargetId, retarget),
            CreateFixtureRecordCommand or ChangeFixtureValueCommand or SetPausedCommand or
                CreateGuestWalletCommand or CreateFestivalFinanceCommand or CreateOwnedStockCommand => null,
            _ => CommandResult.Rejected(CommandReasonCode.UnknownCommand, "Command type is not supported."),
        };
    }

    public CommandResult? ValidateCommand(CommandEnvelope envelope) => ValidateEnvelope(envelope);

    private CommandResult? ValidateConfirmPlanningCommitment(EntityId? targetId, ConfirmPlanningCommitmentCommand command)
    {
        if (targetId is not null || Phase != SessionPhase.Planning || _campaignPlanning is null)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Commitments can be confirmed only during campaign planning.");
        var commitment = _campaignPlanning.Commitments.SingleOrDefault(item => item.Id == command.CommitmentId);
        if (commitment is null)
            return CommandResult.Rejected(CommandReasonCode.UnknownTarget, "Planning commitment does not exist.");
        if (commitment.Status != PlanningCommitmentStatus.Available)
            return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "Planning commitment is already confirmed or paid.");
        var cash = _festivalFinances[_campaignPlanning.FinanceOwnerId].CashPennies;
        var reserved = _campaignPlanning.Commitments
            .Where(item => item.Status == PlanningCommitmentStatus.Confirmed)
            .Sum(item => item.AmountPennies);
        if (cash - reserved < commitment.AmountPennies)
            return CommandResult.Rejected(CommandReasonCode.InsufficientFunds, "Festival cash is insufficient for this commitment.");
        return null;
    }

    private CommandResult? ValidateAdvancePlanningWeek(EntityId? targetId)
    {
        if (targetId is not null || Phase != SessionPhase.Planning || _campaignPlanning is null || _campaignPlanning.PlanningWeek is < 1 or > 8)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Advance Week is available only during Planning W8 through W1.");
        var due = _campaignPlanning.Commitments
            .Where(item => item.Status == PlanningCommitmentStatus.Confirmed && item.DueOnAdvanceFromWeek == _campaignPlanning.PlanningWeek)
            .Sum(item => item.AmountPennies);
        if (_festivalFinances[_campaignPlanning.FinanceOwnerId].CashPennies < due)
            return CommandResult.Rejected(CommandReasonCode.InsufficientFunds, "Festival cash is insufficient for payments due on this advance.");
        return null;
    }

    private CommandResult? ValidateDismissCampaignTip(EntityId? targetId, DismissCampaignTipCommand command)
    {
        if (targetId is not null || _campaignPlanning is null)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "This session has no campaign tip state.");
        if (string.IsNullOrWhiteSpace(command.TipId) || command.TipId.Length > 80)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Tip ID must contain 1-80 characters.");
        if (_campaignPlanning.DismissedTipIds.Contains(command.TipId))
            return CommandResult.Rejected(CommandReasonCode.DuplicateCommand, "Tip was already dismissed.");
        return null;
    }

    private CommandResult? ValidatePurchase(EntityId? serviceId, PurchaseItemCommand purchase)
    {
        if (purchase.TransactionId.Value == 0 || purchase.UnitPricePennies <= 0 || purchase.Quantity <= 0)
        {
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Purchase requires a nonzero transaction ID, positive unit price and positive quantity.");
        }

        if (_transactionIds.Contains(purchase.TransactionId))
        {
            return CommandResult.Rejected(CommandReasonCode.DuplicateTransaction, "Transaction ID was already completed.");
        }

        if (!_wallets.TryGetValue(purchase.BuyerId, out var wallet))
        {
            return CommandResult.Rejected(CommandReasonCode.UnknownOwner, "Buyer wallet owner does not exist.");
        }

        if (!_festivalFinances.TryGetValue(purchase.FestivalId, out var festival))
        {
            return CommandResult.Rejected(CommandReasonCode.UnknownOwner, "Festival cash owner does not exist.");
        }

        if (serviceId is null || !_ownedStocks.TryGetValue(serviceId.Value, out var stock))
        {
            return CommandResult.Rejected(CommandReasonCode.UnknownTarget, "Festival-owned service stock does not exist.");
        }

        if (stock.OwnerId != purchase.FestivalId)
        {
            return CommandResult.Rejected(CommandReasonCode.UnknownOwner, "Service stock is not owned by the supplied festival owner.");
        }

        if (!TryCalculateTotal(purchase.UnitPricePennies, purchase.Quantity, out var saleTotal) ||
            !TryCalculateTotal(stock.UnitCostBasisPennies, purchase.Quantity, out _))
        {
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Purchase totals exceed the supported integer-penny range.");
        }

        if (wallet.CashPennies < saleTotal)
        {
            return CommandResult.Rejected(CommandReasonCode.InsufficientFunds, "Buyer has insufficient cash.");
        }

        if (stock.Quantity < purchase.Quantity)
        {
            return CommandResult.Rejected(CommandReasonCode.OutOfStock, "Service has insufficient stock.");
        }

        if (festival.CashPennies > long.MaxValue - saleTotal)
        {
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Festival cash would exceed the supported integer-penny range.");
        }

        return null;
    }

    private void ApplyPurchase(CommandEnvelope envelope, EntityId serviceId, PurchaseItemCommand purchase)
    {
        var wallet = _wallets[purchase.BuyerId];
        var festival = _festivalFinances[purchase.FestivalId];
        var stock = _ownedStocks[serviceId];
        _ = TryCalculateTotal(purchase.UnitPricePennies, purchase.Quantity, out var saleTotal);
        _ = TryCalculateTotal(stock.UnitCostBasisPennies, purchase.Quantity, out var inventoryCost);

        var entries = new LedgerEntry[]
        {
            new(purchase.BuyerId, LedgerAccountType.CashAsset, -saleTotal),
            new(purchase.BuyerId, LedgerAccountType.GuestSpending, saleTotal),
            new(purchase.FestivalId, LedgerAccountType.CashAsset, saleTotal),
            new(purchase.FestivalId, LedgerAccountType.SalesRevenue, -saleTotal),
            new(purchase.FestivalId, LedgerAccountType.CostOfGoodsSold, inventoryCost),
            new(purchase.FestivalId, LedgerAccountType.InventoryAsset, -inventoryCost),
        };
        var transaction = new TransactionRecord(
            purchase.TransactionId,
            envelope.CommandId,
            CurrentTick,
            purchase.BuyerId,
            purchase.FestivalId,
            serviceId,
            purchase.Quantity,
            purchase.UnitPricePennies,
            entries);
        if (!transaction.IsBalanced)
        {
            throw new InvalidOperationException("Purchase ledger transaction is not balanced.");
        }

        wallet.CashPennies -= saleTotal;
        festival.CashPennies += saleTotal;
        stock.Quantity -= purchase.Quantity;
        _transactionIds.Add(purchase.TransactionId);
        _transactions.Add(transaction);
    }

    private static bool TryCalculateTotal(long unitPennies, int quantity, out long total)
    {
        if (unitPennies < 0 || quantity < 0 || (quantity != 0 && unitPennies > long.MaxValue / quantity))
        {
            total = 0;
            return false;
        }

        total = unitPennies * quantity;
        return true;
    }
}

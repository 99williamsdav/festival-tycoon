namespace Festival.Simulation;

public sealed class GameSession
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
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (IsPaused || count == 0)
        {
            return new AdvanceResult(CaptureSnapshot(), Array.Empty<SessionEvent>());
        }

        var events = new List<SessionEvent>();
        for (var index = 0; index < count; index++)
        {
            CurrentTick++;
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
        }

        return new AdvanceResult(CaptureSnapshot(), events);
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

        return new SessionSnapshot(
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
            CanonicalStateHasher.Compute(this));
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
        CanonicalStateHasher.Compute(this));

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

        var actualHash = CanonicalStateHasher.Compute(session);
        return string.Equals(actualHash, snapshot.AuthoritativeHash, StringComparison.Ordinal)
            ? SessionRestoreResult.Success(session)
            : SessionRestoreResult.Failure($"Authoritative state hash mismatch after reconstruction: expected {snapshot.AuthoritativeHash}, got {actualHash}.");
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
        var entityIds = snapshot.FixtureRecords.Select(item => item.Id).Concat(snapshot.Wallets.Select(item => item.OwnerId))
            .Concat(snapshot.FestivalFinances.Select(item => item.OwnerId)).Concat(snapshot.OwnedStocks.Select(item => item.ServiceId)).ToArray();
        if (entityIds.Distinct().Count() != entityIds.Length || entityIds.Any(id => id >= snapshot.NextEntityId))
            return "Entity IDs must be unique and lower than the next entity ID.";
        var knownEntities = entityIds.ToHashSet();
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

        return envelope.Command switch
        {
            CreateFixtureRecordCommand create when envelope.TargetId is not null || create.ExpiresAfterTicks <= 0 =>
                CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Fixture creation requires no target and a positive expiry."),
            ChangeFixtureValueCommand when envelope.TargetId is null || !_fixtureRecords.ContainsKey(envelope.TargetId.Value) =>
                CommandResult.Rejected(CommandReasonCode.UnknownTarget, "Fixture target does not exist."),
            SetPausedCommand when envelope.TargetId is not null =>
                CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Pause command does not accept a target."),
            CreateGuestWalletCommand create when envelope.TargetId is not null || create.OpeningCashPennies < 0 =>
                CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Guest setup requires no target and nonnegative opening cash."),
            CreateFestivalFinanceCommand create when envelope.TargetId is not null || create.OpeningCashPennies < 0 =>
                CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Festival setup requires no target and nonnegative opening cash."),
            CreateOwnedStockCommand create when envelope.TargetId is not null || create.Quantity < 0 || create.UnitCostBasisPennies < 0 =>
                CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Stock setup requires no target and nonnegative quantity and cost basis."),
            CreateOwnedStockCommand create when !_festivalFinances.ContainsKey(create.OwnerId) =>
                CommandResult.Rejected(CommandReasonCode.UnknownOwner, "Stock owner does not exist."),
            PurchaseItemCommand purchase => ValidatePurchase(envelope.TargetId, purchase),
            CreateFixtureRecordCommand or ChangeFixtureValueCommand or SetPausedCommand or
                CreateGuestWalletCommand or CreateFestivalFinanceCommand or CreateOwnedStockCommand => null,
            _ => CommandResult.Rejected(CommandReasonCode.UnknownCommand, "Command type is not supported."),
        };
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

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

    private CommandResult? ValidateEnvelope(CommandEnvelope envelope)
    {
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

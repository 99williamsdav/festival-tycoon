namespace Festival.Simulation;

public enum SessionPhase
{
    Live = 1,
    Egress = 2,
}

public enum RequestedSpeed
{
    OneX = 1,
    TwoX = 2,
    FourX = 4,
}

public enum CommandReasonCode
{
    Accepted = 0,
    DuplicateCommand = 1,
    WrongCampaign = 2,
    WrongPhase = 3,
    WrongTick = 4,
    OutOfOrderSubmission = 5,
    UnknownTarget = 6,
    InvalidParameter = 7,
    UnknownCommand = 8,
    DuplicateTransaction = 9,
    UnknownOwner = 10,
    InsufficientFunds = 11,
    OutOfStock = 12,
}

public abstract record SessionCommand;

/// <summary>Development fixture: creates the smallest state record used by M0.02.</summary>
public sealed record CreateFixtureRecordCommand(int InitialValue, int ExpiresAfterTicks) : SessionCommand;

/// <summary>Development fixture: changes a record value without advancing time.</summary>
public sealed record ChangeFixtureValueCommand(int NewValue) : SessionCommand;

public sealed record SetPausedCommand(bool IsPaused) : SessionCommand;

/// <summary>Development fixture: creates one guest wallet for M0.04 verification.</summary>
public sealed record CreateGuestWalletCommand(long OpeningCashPennies) : SessionCommand;

/// <summary>Development fixture: creates one festival cash owner for M0.04 verification.</summary>
public sealed record CreateFestivalFinanceCommand(long OpeningCashPennies) : SessionCommand;

/// <summary>Development fixture: creates festival-owned service stock for M0.04 verification.</summary>
public sealed record CreateOwnedStockCommand(EntityId OwnerId, int Quantity, int UnitCostBasisPennies) : SessionCommand;

/// <summary>
/// Atomically transfers guest cash, festival cash and owned stock and records a balanced ledger transaction.
/// TargetId on the envelope is the service whose stock is sold.
/// </summary>
public sealed record PurchaseItemCommand(
    TransactionId TransactionId,
    EntityId BuyerId,
    EntityId FestivalId,
    long UnitPricePennies,
    int Quantity) : SessionCommand;

public sealed record CommandEnvelope(
    CommandId CommandId,
    CampaignId CampaignId,
    SessionPhase ExpectedPhase,
    long Tick,
    ulong SubmissionSequence,
    EntityId? TargetId,
    SessionCommand Command);

public sealed record CommandResult(
    bool IsAccepted,
    CommandReasonCode ReasonCode,
    string Message,
    EntityId? TargetId = null)
{
    public static CommandResult Accepted(EntityId? targetId = null) =>
        new(true, CommandReasonCode.Accepted, "Command accepted.", targetId);

    public static CommandResult Rejected(CommandReasonCode reasonCode, string message) =>
        new(false, reasonCode, message);
}

public sealed record AppliedCommand(
    CommandId CommandId,
    long Tick,
    ulong SubmissionSequence,
    string CommandType,
    EntityId? TargetId);

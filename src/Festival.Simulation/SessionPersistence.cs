namespace Festival.Simulation;

public sealed record PersistedFixtureRecord(ulong Id, int Value, int RemainingTicks, bool HasExpired);
public sealed record PersistedAppliedCommand(ulong CommandId, long Tick, ulong SubmissionSequence, string CommandType, ulong? TargetId);
public sealed record PersistedRandomStream(int StreamId, ulong State, ulong Increment);
public sealed record PersistedWallet(ulong OwnerId, long CashPennies);
public sealed record PersistedFestivalFinance(ulong OwnerId, long CashPennies);
public sealed record PersistedOwnedStock(ulong ServiceId, ulong OwnerId, int Quantity, int UnitCostBasisPennies);
public sealed record PersistedLedgerEntry(ulong OwnerId, int Account, long AmountPennies);
public sealed record PersistedTransaction(
    ulong Id,
    ulong CommandId,
    long Tick,
    ulong BuyerId,
    ulong FestivalId,
    ulong ServiceId,
    int Quantity,
    long UnitPricePennies,
    PersistedLedgerEntry[] Entries);

/// <summary>Explicit v1 persistence DTO for all authoritative M0.05 simulation state.</summary>
public sealed record SessionPersistenceSnapshot(
    ulong CampaignId,
    ulong CampaignSeed,
    string RandomAlgorithmVersion,
    int Phase,
    long CurrentTick,
    bool IsPaused,
    ulong NextEntityId,
    ulong NextSubmissionSequence,
    PersistedFixtureRecord[] FixtureRecords,
    ulong[] AcceptedCommandIds,
    PersistedAppliedCommand[] AppliedCommands,
    PersistedRandomStream[] RandomStreams,
    PersistedWallet[] Wallets,
    PersistedFestivalFinance[] FestivalFinances,
    PersistedOwnedStock[] OwnedStocks,
    ulong[] CompletedTransactionIds,
    PersistedTransaction[] Transactions,
    string AuthoritativeHash);

public sealed record SessionRestoreResult(GameSession? Session, string? Error)
{
    public bool IsSuccess => Session is not null;
    public static SessionRestoreResult Success(GameSession session) => new(session, null);
    public static SessionRestoreResult Failure(string error) => new(null, error);
}

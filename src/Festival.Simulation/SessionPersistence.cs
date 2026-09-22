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
public sealed record PersistedGridCell(int X, int Z);
public sealed record PersistedTerrainCell(int X, int Z, int Surface, bool IsWalkable, int CostPermille, int ElevationMillimetres, int SlopePermille);
public sealed record PersistedTraversalGrid(int Width, int Depth, int CellSizeMillimetres, PersistedTerrainCell[] Cells);
public sealed record PersistedNavigationAgent(
    ulong Id, int XMillimetres, int ZMillimetres, int Action,
    int? DestinationX, int? DestinationZ, PersistedGridCell[] Route, int RouteIndex,
    int SegmentOriginXMillimetres, int SegmentOriginZMillimetres,
    int SegmentProgressMicrometres, int MovementRemainder, int LastSearchExpandedNodes, string? IntentId,
    int WalkingSpeedPermille = 1_000);
public sealed record PersistedQueueAgent(ulong AgentId, int Action, int? ReservedSlotIndex, int ExitIndex, bool OwnsExitReservation, long AdmissionTick, ulong ArrivalSequence);
public sealed record PersistedServiceQueue(
    ulong Id, ulong FestivalId, ulong ServiceId, bool IsOpen, long UnitPricePennies, int ServiceDurationTicks,
    ulong[] OrderedMembers, ulong? ActiveOwnerId, int RemainingServiceTicks, ulong CompletionSequence, bool NeedsReassignment,
    PersistedGridCell[] QueueSlots, PersistedGridCell[] ExitCells, PersistedQueueAgent[] Agents,
    ulong NextArrivalSequence = 1,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    bool? PhysicalArrivalAdmission = null);
public sealed record PersistedPlanningCommitment(
    string Id, string DisplayName, long AmountPennies, int DueOnAdvanceFromWeek, int Status,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    int? ConfirmedInWeek = null);
public sealed record PersistedPlanningLedgerTransaction(ulong Id, string Reason, int PlanningWeek, PersistedLedgerEntry[] Entries);
public sealed record PersistedWeeklyPayment(string CommitmentId, string DisplayName, long AmountPennies);
public sealed record PersistedWeeklyDigest(
    int FromWeek, int ToWeek, int PhaseAfter, PersistedWeeklyPayment[] Payments,
    int RemainingCommitments, long CashPennies, long OutstandingDebtPennies, string[] Warnings);
public sealed record PersistedCampaignPlanning(
    string FestivalName, int Palette, string SiteId, ulong SiteSeed, int EditionNumber, int PlanningWeek, ulong FinanceOwnerId,
    long LoanOpeningPrincipalPennies, long LoanOutstandingPrincipalPennies, long LoanPrincipalDueAtSettlementPennies,
    long LoanInterestDueAtSettlementPennies, int LoanRemainingEditions, int LoanInterestBasisPoints,
    PersistedPlanningCommitment[] Commitments, PersistedPlanningLedgerTransaction[] LedgerTransactions,
    PersistedWeeklyDigest[] WeeklyDigests, string[] DismissedTipIds);

/// <summary>Explicit v1 persistence DTO for all authoritative state through M0.08.</summary>
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
    string AuthoritativeHash)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PersistedTraversalGrid? TraversalGrid { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PersistedNavigationAgent[]? NavigationAgents { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PersistedServiceQueue[]? ServiceQueues { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PersistedCampaignPlanning? CampaignPlanning { get; init; }
}

public sealed record SessionRestoreResult(GameSession? Session, string? Error)
{
    public bool IsSuccess => Session is not null;
    public static SessionRestoreResult Success(GameSession session) => new(session, null);
    public static SessionRestoreResult Failure(string error) => new(null, error);
}

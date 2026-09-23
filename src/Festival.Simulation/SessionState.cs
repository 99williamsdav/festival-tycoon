namespace Festival.Simulation;

public sealed record FixtureRecordSnapshot(
    EntityId Id,
    int Value,
    int RemainingTicks,
    bool HasExpired);

public sealed record SessionSnapshot(
    CampaignId CampaignId,
    ulong CampaignSeed,
    SessionPhase Phase,
    long CurrentTick,
    bool IsPaused,
    RequestedSpeed RequestedSpeed,
    ulong NextEntityId,
    ulong NextSubmissionSequence,
    IReadOnlyList<FixtureRecordSnapshot> FixtureRecords,
    IReadOnlyList<WalletSnapshot> Wallets,
    IReadOnlyList<FestivalFinanceSnapshot> FestivalFinances,
    IReadOnlyList<OwnedStockSnapshot> OwnedStocks,
    IReadOnlyList<TransactionRecord> Transactions,
    IReadOnlyList<NavigationAgentSnapshot> NavigationAgents,
    IReadOnlyList<ServiceQueueSnapshot> ServiceQueues,
    string AuthoritativeHash,
    CampaignPlanningSnapshot? Campaign = null,
    LifecycleSnapshot? Lifecycle = null);

public sealed record SessionEvent(long Tick, string EventType, EntityId EntityId);

public sealed record AdvanceResult(SessionSnapshot Snapshot, IReadOnlyList<SessionEvent> Events);

public sealed record NavigationObservation(
    EntityId Id, int XMillimetres, int ZMillimetres, AgentNavigationAction Action, string? IntentId);

public sealed record QueueAgentObservation(
    EntityId AgentId, ServiceQueueAgentAction Action, int? ReservedSlotIndex, int ExitIndex, bool OwnsExitReservation,
    bool IsAtReservedSlot);

public sealed record QueueObservation(
    EntityId Id, IReadOnlyList<EntityId> OrderedMembers, EntityId? ActiveOwnerId,
    int RemainingServiceTicks, IReadOnlyList<QueueAgentObservation> Agents);

/// <summary>Bounded identity/position/queue read model without routes, ledgers or canonical hashing.</summary>
public sealed record SessionObservation(
    long CurrentTick, IReadOnlyList<NavigationObservation> NavigationAgents,
    IReadOnlyList<QueueObservation> ServiceQueues, int WalletCount, int TransactionCount);

internal sealed class FixtureRecordState
{
    public required EntityId Id { get; init; }

    public int Value { get; set; }

    public int RemainingTicks { get; set; }

    public bool HasExpired { get; set; }
}

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
    string AuthoritativeHash);

public sealed record SessionEvent(long Tick, string EventType, EntityId EntityId);

public sealed record AdvanceResult(SessionSnapshot Snapshot, IReadOnlyList<SessionEvent> Events);

internal sealed class FixtureRecordState
{
    public required EntityId Id { get; init; }

    public int Value { get; set; }

    public int RemainingTicks { get; set; }

    public bool HasExpired { get; set; }
}

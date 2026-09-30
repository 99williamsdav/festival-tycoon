namespace Festival.Simulation;

public enum ProtectedPersonRole
{
    Guest = 1,
    Staff = 2,
    Performer = 3,
}

public enum EditionAttemptStatus
{
    Active = 1,
    Failed = 2,
    Safe = 3,
}

public enum HearingStatus
{
    Open = 1,
    FavourSpent = 2,
    Conceded = 3,
    LostNoFavour = 4,
}

public sealed record ProtectedPersonSnapshot(string PersonId, ProtectedPersonRole Role);
public sealed record EditionAttemptSnapshot(ulong AttemptId, string TierId, EditionAttemptStatus Status, string? OutcomeTransactionId);
public sealed record CasualtySnapshot(ulong CasualtyId, ulong AttemptId, string PersonId, ProtectedPersonRole Role, string Cause, long Tick, string TransactionId);
public sealed record CouncilHearingSnapshot(ulong HearingId, ulong AttemptId, HearingStatus Status, string CreatedTransactionId, string? ResolutionTransactionId);

public sealed record LifecycleSnapshot(
    string CurrentTierId,
    int TierOrdinal,
    ulong CurrentAttemptId,
    int FavourBalance,
    IReadOnlyList<ProtectedPersonSnapshot> ProtectedPeople,
    IReadOnlyList<EditionAttemptSnapshot> Attempts,
    IReadOnlyList<CasualtySnapshot> Casualties,
    IReadOnlyList<CouncilHearingSnapshot> Hearings,
    IReadOnlyList<string> CompletedOutcomeTransactionIds);

internal sealed class LifecycleState
{
    public required string CurrentTierId { get; set; }
    public int TierOrdinal { get; set; }
    public ulong CurrentAttemptId { get; set; }
    public ulong NextAttemptId { get; set; }
    public ulong NextCasualtyId { get; set; }
    public ulong NextHearingId { get; set; }
    public int FavourBalance { get; set; }
    public SortedDictionary<string, ProtectedPersonSnapshot> ProtectedPeople { get; } = new(StringComparer.Ordinal);
    public List<EditionAttemptSnapshot> Attempts { get; } = [];
    public List<CasualtySnapshot> Casualties { get; } = [];
    public List<CouncilHearingSnapshot> Hearings { get; } = [];
    public SortedSet<string> CompletedOutcomeTransactionIds { get; } = new(StringComparer.Ordinal);
}

public sealed partial class GameSession
{
    private LifecycleState? _lifecycle;
    internal LifecycleState? LifecycleState => _lifecycle;

    public LifecycleSnapshot? CaptureLifecycleSnapshot()
    {
        if (_lifecycle is null) return null;
        return new LifecycleSnapshot(
            _lifecycle.CurrentTierId,
            _lifecycle.TierOrdinal,
            _lifecycle.CurrentAttemptId,
            _lifecycle.FavourBalance,
            _lifecycle.ProtectedPeople.Values.ToArray(),
            _lifecycle.Attempts.ToArray(),
            _lifecycle.Casualties.ToArray(),
            _lifecycle.Hearings.ToArray(),
            _lifecycle.CompletedOutcomeTransactionIds.ToArray());
    }

    private CommandResult? ValidateSpendCouncilFavour(EntityId? targetId)
    {
        if (targetId is not null || _preparation is not { Status: PreparationStatus.Failed } ||
            _lifecycle is null)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "A real fatal hearing is required.");
        if (_lifecycle.Hearings.Count == 0 || _lifecycle.Hearings[^1].Status != HearingStatus.Open)
            return CommandResult.Rejected(CommandReasonCode.AlreadySettled, "This hearing is already resolved.");
        if (_lifecycle.FavourBalance < 1)
            return CommandResult.Rejected(CommandReasonCode.InsufficientFavour, "No Council Favour remains; the campaign is lost.");
        return null;
    }

    private CommandResult? ValidateConcedeCouncilHearing(EntityId? targetId)
    {
        if (targetId is not null || _preparation is not { Status: PreparationStatus.Failed } ||
            _lifecycle is null)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "A fatal Council hearing is required.");
        return _lifecycle.Hearings.Count > 0 && _lifecycle.Hearings[^1].Status == HearingStatus.Open
            ? null
            : CommandResult.Rejected(CommandReasonCode.AlreadySettled, "This hearing is already resolved.");
    }

    private void ApplyConcedeCouncilHearing()
    {
        var lifecycle = _lifecycle!;
        var hearing = lifecycle.Hearings[^1];
        var transactionId = $"council-concede:{CampaignId.Value}:{hearing.HearingId}";
        lifecycle.Hearings[^1] = hearing with { Status = HearingStatus.Conceded, ResolutionTransactionId = transactionId };
        lifecycle.CompletedOutcomeTransactionIds.Add(transactionId);
        EndPerkCampaign();
    }

    private void ResolveNoFavourHearing()
    {
        var lifecycle = _lifecycle!;
        if (lifecycle.FavourBalance != 0)
            return;
        var hearing = lifecycle.Hearings[^1];
        var transactionId = $"council-no-favour:{CampaignId.Value}:{hearing.HearingId}";
        lifecycle.Hearings[^1] = hearing with { Status = HearingStatus.LostNoFavour, ResolutionTransactionId = transactionId };
        lifecycle.CompletedOutcomeTransactionIds.Add(transactionId);
        EndPerkCampaign();
    }

    private void ApplySpendCouncilFavour()
    {
        var lifecycle = _lifecycle!;
        var hearing = lifecycle.Hearings[^1];
        var transactionId = $"council-favour:{CampaignId.Value}:{hearing.HearingId}";
        lifecycle.FavourBalance--;
        lifecycle.Hearings[^1] = hearing with { Status = HearingStatus.FavourSpent, ResolutionTransactionId = transactionId };
        lifecycle.CompletedOutcomeTransactionIds.Add(transactionId);
        var attemptId = lifecycle.NextAttemptId++;
        lifecycle.CurrentAttemptId = attemptId;
        lifecycle.Attempts.Add(new EditionAttemptSnapshot(attemptId, lifecycle.CurrentTierId, EditionAttemptStatus.Active, null));
        RetryPreparedWeekend();
    }

    private EditionAttemptSnapshot CurrentAttempt() => _lifecycle!.Attempts.Single(item => item.AttemptId == _lifecycle.CurrentAttemptId);

    private bool IsLifecycleEditionFrozen() => _lifecycle is not null && CurrentAttempt().Status != EditionAttemptStatus.Active;

    private CommandResult? ValidateLifecycleFrozenCommand(SessionCommand command)
    {
        if (!IsLifecycleEditionFrozen() || command is SpendCouncilFavourCommand or ConcedeCouncilHearingCommand) return null;
        return CommandResult.Rejected(CommandReasonCode.EditionFrozen, "The edition is frozen after its first terminal outcome.");
    }

    private void ReplaceAttempt(EditionAttemptSnapshot replacement)
    {
        var index = _lifecycle!.Attempts.FindIndex(item => item.AttemptId == replacement.AttemptId);
        _lifecycle.Attempts[index] = replacement;
    }

    private PersistedLifecycle? CapturePersistedLifecycle()
    {
        if (_lifecycle is null) return null;
        return new PersistedLifecycle(
            _lifecycle.CurrentTierId, _lifecycle.TierOrdinal, _lifecycle.CurrentAttemptId,
            _lifecycle.NextAttemptId, _lifecycle.NextCasualtyId, _lifecycle.NextHearingId, _lifecycle.FavourBalance,
            _lifecycle.ProtectedPeople.Values.Select(item => new PersistedProtectedPerson(item.PersonId, (int)item.Role)).ToArray(),
            _lifecycle.Attempts.Select(item => new PersistedEditionAttempt(item.AttemptId, item.TierId, (int)item.Status, item.OutcomeTransactionId)).ToArray(),
            _lifecycle.Casualties.Select(item => new PersistedCasualty(item.CasualtyId, item.AttemptId, item.PersonId, (int)item.Role, item.Cause, item.Tick, item.TransactionId)).ToArray(),
            _lifecycle.Hearings.Select(item => new PersistedCouncilHearing(item.HearingId, item.AttemptId, (int)item.Status, item.CreatedTransactionId, item.ResolutionTransactionId)).ToArray(),
            _lifecycle.CompletedOutcomeTransactionIds.ToArray());
    }

    private void RestoreLifecycle(PersistedLifecycle? persisted)
    {
        if (persisted is null) return;
        _lifecycle = new LifecycleState
        {
            CurrentTierId = persisted.CurrentTierId,
            TierOrdinal = persisted.TierOrdinal,
            CurrentAttemptId = persisted.CurrentAttemptId,
            NextAttemptId = persisted.NextAttemptId,
            NextCasualtyId = persisted.NextCasualtyId,
            NextHearingId = persisted.NextHearingId,
            FavourBalance = persisted.FavourBalance,
        };
        foreach (var item in persisted.ProtectedPeople)
            _lifecycle.ProtectedPeople.Add(item.PersonId, new ProtectedPersonSnapshot(item.PersonId, (ProtectedPersonRole)item.Role));
        _lifecycle.Attempts.AddRange(persisted.Attempts.Select(item => new EditionAttemptSnapshot(item.AttemptId, item.TierId, (EditionAttemptStatus)item.Status, item.OutcomeTransactionId)));
        _lifecycle.Casualties.AddRange(persisted.Casualties.Select(item => new CasualtySnapshot(item.CasualtyId, item.AttemptId, item.PersonId, (ProtectedPersonRole)item.Role, item.Cause, item.Tick, item.TransactionId)));
        _lifecycle.Hearings.AddRange(persisted.Hearings.Select(item => new CouncilHearingSnapshot(item.HearingId, item.AttemptId, (HearingStatus)item.Status, item.CreatedTransactionId, item.ResolutionTransactionId)));
        foreach (var id in persisted.CompletedOutcomeTransactionIds) _lifecycle.CompletedOutcomeTransactionIds.Add(id);
    }

    private static string? ValidatePersistedLifecycle(PersistedLifecycle? lifecycle, PreparationSnapshot? preparation, ulong campaignId)
    {
        if (lifecycle is null) return null;
        if (string.IsNullOrWhiteSpace(lifecycle.CurrentTierId) ||
            lifecycle.TierOrdinal < 1 || lifecycle.CurrentAttemptId == 0 || lifecycle.NextAttemptId == 0 ||
            lifecycle.NextCasualtyId == 0 || lifecycle.NextHearingId == 0 || lifecycle.FavourBalance is < 0 or > 2)
            return "Lifecycle identity, counters or Favour balance is invalid.";
        if (lifecycle.ProtectedPeople is null || lifecycle.Attempts is null || lifecycle.Casualties is null || lifecycle.Hearings is null ||
            lifecycle.CompletedOutcomeTransactionIds is null || lifecycle.ProtectedPeople.Any(item => item is null) ||
            lifecycle.Attempts.Any(item => item is null) || lifecycle.Casualties.Any(item => item is null) || lifecycle.Hearings.Any(item => item is null))
            return "Lifecycle authoritative collections must be present and contain no null records.";
        if (preparation is null || lifecycle.Attempts is not { Length: > 0 } ||
            lifecycle.CurrentAttemptId != (ulong)preparation.Attempt ||
            lifecycle.CurrentTierId != $"tier-{preparation.Tier}" ||
            lifecycle.Attempts.Any(item => item.TierId != lifecycle.CurrentTierId) ||
            ((EditionAttemptStatus)lifecycle.Attempts[^1].Status == EditionAttemptStatus.Failed) !=
                (preparation.Status == PreparationStatus.Failed))
            return "Real hearing identity, retry attempt and tier must match preparation.";
        if (lifecycle.ProtectedPeople.Length is < 22 or > 50 ||
            !lifecycle.ProtectedPeople.Select(item => item.PersonId).SequenceEqual(lifecycle.ProtectedPeople.Select(item => item.PersonId).Order(StringComparer.Ordinal)) ||
            lifecycle.ProtectedPeople.Select(item => item.PersonId).Distinct(StringComparer.Ordinal).Count() != lifecycle.ProtectedPeople.Length ||
            lifecycle.ProtectedPeople.Any(item => string.IsNullOrWhiteSpace(item.PersonId) || !Enum.IsDefined(typeof(ProtectedPersonRole), item.Role)) ||
            lifecycle.ProtectedPeople.Select(item => item.Role).Distinct().Count() != 3)
            return "Lifecycle protected people must be sorted, unique, and include guests, staff and performers.";
        if (!lifecycle.Attempts.Select(item => item.AttemptId).SequenceEqual(Enumerable.Range(1, lifecycle.Attempts.Length).Select(value => (ulong)value)) ||
            lifecycle.NextAttemptId != (ulong)lifecycle.Attempts.Length + 1 || lifecycle.Attempts.All(item => item.AttemptId != lifecycle.CurrentAttemptId) ||
            lifecycle.Attempts.Any(item => string.IsNullOrWhiteSpace(item.TierId) || !Enum.IsDefined(typeof(EditionAttemptStatus), item.Status) ||
                ((EditionAttemptStatus)item.Status == EditionAttemptStatus.Active) != (item.OutcomeTransactionId is null)))
            return "Lifecycle attempts must be contiguous and have coherent terminal transactions.";
        var people = lifecycle.ProtectedPeople.ToDictionary(item => item.PersonId, StringComparer.Ordinal);
        var attempts = lifecycle.Attempts.ToDictionary(item => item.AttemptId);
        if (!lifecycle.Casualties.Select(item => item.CasualtyId).SequenceEqual(Enumerable.Range(1, lifecycle.Casualties.Length).Select(value => (ulong)value)) ||
            lifecycle.NextCasualtyId != (ulong)lifecycle.Casualties.Length + 1 || lifecycle.Casualties.Any(item =>
                !attempts.TryGetValue(item.AttemptId, out var attempt) || (EditionAttemptStatus)attempt.Status != EditionAttemptStatus.Failed ||
                !people.TryGetValue(item.PersonId, out var person) || person.Role != item.Role || !Enum.IsDefined(typeof(ProtectedPersonRole), item.Role) ||
                string.IsNullOrWhiteSpace(item.Cause) || item.Tick < 0 || string.IsNullOrWhiteSpace(item.TransactionId)))
            return "Lifecycle casualties must be contiguous, protected and attributable to failed attempts.";
        if (!lifecycle.Hearings.Select(item => item.HearingId).SequenceEqual(Enumerable.Range(1, lifecycle.Hearings.Length).Select(value => (ulong)value)) ||
            lifecycle.NextHearingId != (ulong)lifecycle.Hearings.Length + 1 || lifecycle.Hearings.Any(item =>
                !attempts.TryGetValue(item.AttemptId, out var attempt) || (EditionAttemptStatus)attempt.Status != EditionAttemptStatus.Failed ||
                !Enum.IsDefined(typeof(HearingStatus), item.Status) || string.IsNullOrWhiteSpace(item.CreatedTransactionId) ||
                ((HearingStatus)item.Status != HearingStatus.Open) != (item.ResolutionTransactionId is not null)))
            return "Lifecycle hearings must be contiguous and correspond one-to-one with failed attempts.";
        if (lifecycle.Casualties.Length != lifecycle.Hearings.Length ||
            !lifecycle.Casualties.Select(item => item.AttemptId).SequenceEqual(lifecycle.Hearings.Select(item => item.AttemptId)))
            return "Each failed attempt must have exactly one casualty and one hearing.";
        var expectedTransactions = lifecycle.Casualties.Select(item => item.TransactionId)
            .Concat(lifecycle.Hearings.Select(item => item.CreatedTransactionId))
            .Concat(lifecycle.Hearings.Where(item => item.ResolutionTransactionId is not null).Select(item => item.ResolutionTransactionId!))
            .Concat(lifecycle.Attempts.Where(item => (EditionAttemptStatus)item.Status == EditionAttemptStatus.Safe).Select(item => item.OutcomeTransactionId!))
            .Concat(preparation.CommunityFavourClaimed ? [$"community-water-favour:{campaignId}"] : [])
            .Order(StringComparer.Ordinal).ToArray();
        if (!lifecycle.CompletedOutcomeTransactionIds.SequenceEqual(expectedTransactions) || expectedTransactions.Distinct(StringComparer.Ordinal).Count() != expectedTransactions.Length)
            return "Lifecycle completed transaction IDs must exactly match terminal, hearing, Favour and safe outcomes.";
        if (lifecycle.FavourBalance != 1 + (preparation.CommunityFavourClaimed ? 1 : 0) -
            lifecycle.Hearings.Count(item => item.Status == (int)HearingStatus.FavourSpent))
            return "Favour balance must reconcile with the starting grant, community claim and hearing spends.";
        return null;
    }
}

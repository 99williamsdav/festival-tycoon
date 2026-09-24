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
}

public sealed record ProtectedPersonSnapshot(string PersonId, ProtectedPersonRole Role);
public sealed record EditionAttemptSnapshot(ulong AttemptId, string TierId, EditionAttemptStatus Status, string? OutcomeTransactionId);
public sealed record CasualtySnapshot(ulong CasualtyId, ulong AttemptId, string PersonId, ProtectedPersonRole Role, string Cause, long Tick, string TransactionId);
public sealed record CouncilHearingSnapshot(ulong HearingId, ulong AttemptId, HearingStatus Status, string CreatedTransactionId, string? ResolutionTransactionId);

public sealed record LifecycleSnapshot(
    string FixtureLabel,
    string CurrentTierId,
    int FixtureTierOrdinal,
    ulong CurrentAttemptId,
    int FixtureFavourBalance,
    IReadOnlyList<ProtectedPersonSnapshot> ProtectedPeople,
    IReadOnlyList<EditionAttemptSnapshot> Attempts,
    IReadOnlyList<CasualtySnapshot> Casualties,
    IReadOnlyList<CouncilHearingSnapshot> Hearings,
    IReadOnlyList<string> CompletedOutcomeTransactionIds);

internal sealed class LifecycleState
{
    public required string FixtureLabel { get; init; }
    public required string CurrentTierId { get; set; }
    public int FixtureTierOrdinal { get; set; }
    public ulong CurrentAttemptId { get; set; }
    public ulong NextAttemptId { get; set; }
    public ulong NextCasualtyId { get; set; }
    public ulong NextHearingId { get; set; }
    public int FixtureFavourBalance { get; set; }
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

    public static GameSession CreateR000LifecycleFixture(ulong campaignSeed, CampaignId? campaignId = null)
    {
        var session = new GameSession(campaignSeed, campaignId);
        session._lifecycle = new LifecycleState
        {
            FixtureLabel = "R0.00 fixture-only; tier counts and weekend time are not product values",
            CurrentTierId = "fixture-tier-1",
            FixtureTierOrdinal = 1,
            CurrentAttemptId = 1,
            NextAttemptId = 2,
            NextCasualtyId = 1,
            NextHearingId = 1,
            FixtureFavourBalance = 1,
        };
        session._lifecycle.ProtectedPeople.Add("fixture-guest", new("fixture-guest", ProtectedPersonRole.Guest));
        session._lifecycle.ProtectedPeople.Add("fixture-staff", new("fixture-staff", ProtectedPersonRole.Staff));
        session._lifecycle.ProtectedPeople.Add("fixture-performer", new("fixture-performer", ProtectedPersonRole.Performer));
        session._lifecycle.Attempts.Add(new(1, session._lifecycle.CurrentTierId, EditionAttemptStatus.Active, null));
        return session;
    }

    public LifecycleSnapshot? CaptureLifecycleSnapshot()
    {
        if (_lifecycle is null) return null;
        return new LifecycleSnapshot(
            _lifecycle.FixtureLabel,
            _lifecycle.CurrentTierId,
            _lifecycle.FixtureTierOrdinal,
            _lifecycle.CurrentAttemptId,
            _lifecycle.FixtureFavourBalance,
            _lifecycle.ProtectedPeople.Values.ToArray(),
            _lifecycle.Attempts.ToArray(),
            _lifecycle.Casualties.ToArray(),
            _lifecycle.Hearings.ToArray(),
            _lifecycle.CompletedOutcomeTransactionIds.ToArray());
    }

    private CommandResult? ValidateForceFixtureDeaths(EntityId? targetId, ForceFixtureDeathsCommand command)
    {
        if (targetId is not null || _lifecycle is null)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Forced outcomes exist only in the headless R0.00 lifecycle fixture.");
        if (command.SubjectPersonIds is null || command.SubjectPersonIds.Count == 0 || command.SubjectPersonIds.Any(string.IsNullOrWhiteSpace))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "At least one fixture person is required.");
        if (CurrentAttempt().Status != EditionAttemptStatus.Active)
            return CommandResult.Rejected(CommandReasonCode.EditionFrozen, "The edition is frozen after its first terminal outcome.");
        if (command.SubjectPersonIds.Any(id => !_lifecycle.ProtectedPeople.ContainsKey(id)))
            return CommandResult.Rejected(CommandReasonCode.UnprotectedSubject, "Every terminal subject must be a known protected guest, staff member or performer.");
        return null;
    }

    private CommandResult? ValidateSpendFixtureFavour(EntityId? targetId)
    {
        if (targetId is not null || _lifecycle is null)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Fixture Favour exists only in the headless R0.00 lifecycle fixture.");
        if (_lifecycle.Hearings.Count == 0 || _lifecycle.Hearings[^1].Status != HearingStatus.Open)
            return CommandResult.Rejected(CommandReasonCode.AlreadySettled, "There is no unresolved hearing.");
        if (_lifecycle.FixtureFavourBalance != 1)
            return CommandResult.Rejected(CommandReasonCode.InsufficientFavour, "The single fixture Favour is unavailable.");
        return null;
    }

    private CommandResult? ValidateForceFixtureSafeCompletion(EntityId? targetId)
    {
        if (targetId is not null || _lifecycle is null)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Forced outcomes exist only in the headless R0.00 lifecycle fixture.");
        return CurrentAttempt().Status == EditionAttemptStatus.Active
            ? null
            : CommandResult.Rejected(CommandReasonCode.AlreadySettled, "The current attempt already has a terminal outcome.");
    }

    private void ApplyForceFixtureDeaths(ForceFixtureDeathsCommand command)
    {
        var lifecycle = _lifecycle!;
        var attempt = CurrentAttempt();
        var person = lifecycle.ProtectedPeople[command.SubjectPersonIds[0]];
        var transactionId = $"fixture-death:{CampaignId.Value}:{attempt.AttemptId}";
        var casualty = new CasualtySnapshot(lifecycle.NextCasualtyId++, attempt.AttemptId, person.PersonId, person.Role,
            "fixture-forced-death", CurrentTick, transactionId);
        lifecycle.Casualties.Add(casualty);
        lifecycle.CompletedOutcomeTransactionIds.Add(transactionId);
        ReplaceAttempt(attempt with { Status = EditionAttemptStatus.Failed, OutcomeTransactionId = transactionId });
        var hearingTransactionId = $"fixture-hearing:{CampaignId.Value}:{attempt.AttemptId}";
        lifecycle.Hearings.Add(new CouncilHearingSnapshot(lifecycle.NextHearingId++, attempt.AttemptId, HearingStatus.Open, hearingTransactionId, null));
        lifecycle.CompletedOutcomeTransactionIds.Add(hearingTransactionId);
    }

    private void ApplySpendFixtureFavour()
    {
        var lifecycle = _lifecycle!;
        var hearing = lifecycle.Hearings[^1];
        var transactionId = $"fixture-favour:{CampaignId.Value}:{hearing.HearingId}";
        lifecycle.FixtureFavourBalance--;
        lifecycle.Hearings[^1] = hearing with { Status = HearingStatus.FavourSpent, ResolutionTransactionId = transactionId };
        lifecycle.CompletedOutcomeTransactionIds.Add(transactionId);
        var attemptId = lifecycle.NextAttemptId++;
        lifecycle.CurrentAttemptId = attemptId;
        lifecycle.Attempts.Add(new EditionAttemptSnapshot(attemptId, lifecycle.CurrentTierId, EditionAttemptStatus.Active, null));
    }

    private void ApplyForceFixtureSafeCompletion()
    {
        var lifecycle = _lifecycle!;
        var attempt = CurrentAttempt();
        var transactionId = $"fixture-safe:{CampaignId.Value}:{attempt.AttemptId}";
        ReplaceAttempt(attempt with { Status = EditionAttemptStatus.Safe, OutcomeTransactionId = transactionId });
        lifecycle.CompletedOutcomeTransactionIds.Add(transactionId);
        lifecycle.FixtureTierOrdinal++;
        lifecycle.CurrentTierId = $"fixture-tier-{lifecycle.FixtureTierOrdinal}";
    }

    private EditionAttemptSnapshot CurrentAttempt() => _lifecycle!.Attempts.Single(item => item.AttemptId == _lifecycle.CurrentAttemptId);

    private bool IsLifecycleEditionFrozen() => _lifecycle is not null && CurrentAttempt().Status != EditionAttemptStatus.Active;

    private CommandResult? ValidateLifecycleFrozenCommand(SessionCommand command)
    {
        if (!IsLifecycleEditionFrozen() || command is SpendFixtureFavourCommand) return null;
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
            _lifecycle.FixtureLabel, _lifecycle.CurrentTierId, _lifecycle.FixtureTierOrdinal, _lifecycle.CurrentAttemptId,
            _lifecycle.NextAttemptId, _lifecycle.NextCasualtyId, _lifecycle.NextHearingId, _lifecycle.FixtureFavourBalance,
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
            FixtureLabel = persisted.FixtureLabel,
            CurrentTierId = persisted.CurrentTierId,
            FixtureTierOrdinal = persisted.FixtureTierOrdinal,
            CurrentAttemptId = persisted.CurrentAttemptId,
            NextAttemptId = persisted.NextAttemptId,
            NextCasualtyId = persisted.NextCasualtyId,
            NextHearingId = persisted.NextHearingId,
            FixtureFavourBalance = persisted.FixtureFavourBalance,
        };
        foreach (var item in persisted.ProtectedPeople)
            _lifecycle.ProtectedPeople.Add(item.PersonId, new ProtectedPersonSnapshot(item.PersonId, (ProtectedPersonRole)item.Role));
        _lifecycle.Attempts.AddRange(persisted.Attempts.Select(item => new EditionAttemptSnapshot(item.AttemptId, item.TierId, (EditionAttemptStatus)item.Status, item.OutcomeTransactionId)));
        _lifecycle.Casualties.AddRange(persisted.Casualties.Select(item => new CasualtySnapshot(item.CasualtyId, item.AttemptId, item.PersonId, (ProtectedPersonRole)item.Role, item.Cause, item.Tick, item.TransactionId)));
        _lifecycle.Hearings.AddRange(persisted.Hearings.Select(item => new CouncilHearingSnapshot(item.HearingId, item.AttemptId, (HearingStatus)item.Status, item.CreatedTransactionId, item.ResolutionTransactionId)));
        foreach (var id in persisted.CompletedOutcomeTransactionIds) _lifecycle.CompletedOutcomeTransactionIds.Add(id);
    }

    private static string? ValidatePersistedLifecycle(PersistedLifecycle? lifecycle)
    {
        if (lifecycle is null) return null;
        if (string.IsNullOrWhiteSpace(lifecycle.FixtureLabel) || string.IsNullOrWhiteSpace(lifecycle.CurrentTierId) ||
            lifecycle.FixtureTierOrdinal < 1 || lifecycle.CurrentAttemptId == 0 || lifecycle.NextAttemptId == 0 ||
            lifecycle.NextCasualtyId == 0 || lifecycle.NextHearingId == 0 || lifecycle.FixtureFavourBalance is < 0 or > 1)
            return "Lifecycle fixture identity, counters or Favour balance is invalid.";
        if (lifecycle.ProtectedPeople is null || lifecycle.Attempts is null || lifecycle.Casualties is null || lifecycle.Hearings is null ||
            lifecycle.CompletedOutcomeTransactionIds is null || lifecycle.ProtectedPeople.Any(item => item is null) ||
            lifecycle.Attempts.Any(item => item is null) || lifecycle.Casualties.Any(item => item is null) || lifecycle.Hearings.Any(item => item is null))
            return "Lifecycle authoritative collections must be present and contain no null records.";
        var equipmentLifecycle = lifecycle.FixtureLabel == "R0.02 equipment lifecycle; hearing only, no Favour economy";
        if ((!equipmentLifecycle && lifecycle.ProtectedPeople.Length != 3 || equipmentLifecycle && lifecycle.ProtectedPeople.Length is < 22 or > 50) ||
            !lifecycle.ProtectedPeople.Select(item => item.PersonId).SequenceEqual(lifecycle.ProtectedPeople.Select(item => item.PersonId).Order(StringComparer.Ordinal)) ||
            lifecycle.ProtectedPeople.Select(item => item.PersonId).Distinct(StringComparer.Ordinal).Count() != lifecycle.ProtectedPeople.Length ||
            lifecycle.ProtectedPeople.Any(item => string.IsNullOrWhiteSpace(item.PersonId) || !Enum.IsDefined(typeof(ProtectedPersonRole), item.Role)) ||
            lifecycle.ProtectedPeople.Select(item => item.Role).Distinct().Count() != 3)
            return "Lifecycle protected people must be sorted, unique, and contain one fixture guest, staff member and performer.";
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
                ((HearingStatus)item.Status == HearingStatus.FavourSpent) != (item.ResolutionTransactionId is not null)))
            return "Lifecycle hearings must be contiguous and correspond one-to-one with failed attempts.";
        if (lifecycle.Casualties.Length != lifecycle.Hearings.Length ||
            !lifecycle.Casualties.Select(item => item.AttemptId).SequenceEqual(lifecycle.Hearings.Select(item => item.AttemptId)))
            return "Each failed attempt must have exactly one casualty and one hearing.";
        var expectedTransactions = lifecycle.Casualties.Select(item => item.TransactionId)
            .Concat(lifecycle.Hearings.Select(item => item.CreatedTransactionId))
            .Concat(lifecycle.Hearings.Where(item => item.ResolutionTransactionId is not null).Select(item => item.ResolutionTransactionId!))
            .Concat(lifecycle.Attempts.Where(item => (EditionAttemptStatus)item.Status == EditionAttemptStatus.Safe).Select(item => item.OutcomeTransactionId!))
            .Order(StringComparer.Ordinal).ToArray();
        if (!lifecycle.CompletedOutcomeTransactionIds.SequenceEqual(expectedTransactions) || expectedTransactions.Distinct(StringComparer.Ordinal).Count() != expectedTransactions.Length)
            return "Lifecycle completed transaction IDs must exactly match terminal, hearing, Favour and safe outcomes.";
        if (equipmentLifecycle ? lifecycle.FixtureFavourBalance != 0 || lifecycle.Hearings.Any(item => item.ResolutionTransactionId is not null) :
            lifecycle.FixtureFavourBalance == 0 != lifecycle.Hearings.Any(item => (HearingStatus)item.Status == HearingStatus.FavourSpent))
            return "Fixture Favour balance must reconcile with the single hearing spend.";
        return null;
    }
}

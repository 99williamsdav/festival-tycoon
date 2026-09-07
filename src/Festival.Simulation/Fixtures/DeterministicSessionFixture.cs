namespace Festival.Simulation.Fixtures;

/// <summary>Development-only M0.02 runner/test fixture, not player-facing game content.</summary>
public static class DeterministicSessionFixture
{
    public const ulong Seed = 20_260_907;

    public static FixtureRunResult Run(params int[] repeatingBatchPattern)
    {
        if (repeatingBatchPattern.Length == 0 || repeatingBatchPattern.Any(size => size <= 0))
        {
            throw new ArgumentException("At least one positive batch size is required.", nameof(repeatingBatchPattern));
        }

        var session = CreateInitializedSession();
        var initialHash = session.CaptureSnapshot().AuthoritativeHash;
        var remaining = 400;
        var patternIndex = 0;
        while (remaining > 0)
        {
            var requested = repeatingBatchPattern[patternIndex % repeatingBatchPattern.Length];
            var count = Math.Min(requested, remaining);
            session.AdvanceTicks(count);
            remaining -= count;
            patternIndex++;
        }

        var finalSnapshot = session.CaptureSnapshot();
        return new FixtureRunResult(initialHash, finalSnapshot.AuthoritativeHash, finalSnapshot);
    }

    public static GameSession CreateInitializedSession()
    {
        var session = new GameSession(Seed, new CampaignId(1));
        var created = session.Execute(Envelope(
            session,
            new CommandId(1),
            null,
            new CreateFixtureRecordCommand(10, 350)));
        if (!created.IsAccepted || created.TargetId is null)
        {
            throw new InvalidOperationException("Deterministic fixture could not create its record.");
        }

        var changed = session.Execute(Envelope(
            session,
            new CommandId(2),
            created.TargetId,
            new ChangeFixtureValueCommand(42)));
        if (!changed.IsAccepted)
        {
            throw new InvalidOperationException("Deterministic fixture could not change its record.");
        }

        session.NextRandom(RandomStreamId.IndividualBehaviour);
        return session;
    }

    public static CommandEnvelope Envelope(
        GameSession session,
        CommandId commandId,
        EntityId? targetId,
        SessionCommand command,
        SessionPhase? expectedPhase = null) =>
        new(
            commandId,
            session.CampaignId,
            expectedPhase ?? session.Phase,
            session.CurrentTick,
            session.NextSubmissionSequence,
            targetId,
            command);
}

public sealed record FixtureRunResult(
    string InitialHash,
    string FinalHash,
    SessionSnapshot FinalSnapshot);

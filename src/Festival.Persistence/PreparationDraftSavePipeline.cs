using Festival.Simulation;

namespace Festival.Persistence;

public sealed record DraftSubmitResult(bool IsAccepted, string? Error);
public sealed record DraftPollResult(int Committed, bool RolledBack, string? Error);

/// <summary>
/// Publishes ordinary unpaid preparation edits immediately, then writes their
/// immutable checkpoints in command order. A failed write restores the last
/// durable candidate; dependent queued writes never reach disk.
/// </summary>
public sealed class PreparationDraftSavePipeline
{
    private sealed record Pending(SessionPersistenceSnapshot Checkpoint, long Generation, Task<SaveOperationResult> Save);

    private readonly string _directory;
    private readonly SaveCompatibility _compatibility;
    private readonly Queue<Pending> _pending = new();
    private Task<SaveOperationResult> _tail = Task.FromResult(SaveOperationResult.Success("no-write"));
    private SessionPersistenceSnapshot _durableSnapshot;
    private long _durableGeneration;
    private bool _failed;

    public GameSession VisibleSession { get; private set; }
    public long NextGeneration { get; private set; }
    public bool HasPending => _pending.Count != 0;

    public PreparationDraftSavePipeline(string directory, GameSession session, SaveCompatibility compatibility, long generation)
    {
        _directory = directory;
        _compatibility = compatibility;
        VisibleSession = session;
        _durableSnapshot = session.CapturePersistenceSnapshot();
        _durableGeneration = NextGeneration = generation;
    }

    public DraftSubmitResult Submit(SessionCommand command, DateTimeOffset now,
        Action<SaveFailurePoint>? failureInjector = null)
    {
        if (command is not (AcceptPreparationOfferCommand or RemovePreparationOfferCommand) ||
            VisibleSession.CapturePreparationPlan() is not { Committed: false })
            return new(false, "Only editable unpaid preparation offers use this save pipeline.");
        if (_failed && HasPending) return new(false, "The failed save is rolling back; retry after it finishes.");
        if (_failed) _failed = false;

        var stageStarted = PersistenceTiming.Start();
        var before = VisibleSession.CapturePersistenceSnapshot();
        PersistenceTiming.Record("draft.stage", stageStarted);
        stageStarted = PersistenceTiming.Start();
        var result = VisibleSession.Execute(new(new CommandId(2_020_000 + VisibleSession.NextSubmissionSequence), VisibleSession.CampaignId,
            VisibleSession.Phase, VisibleSession.CurrentTick, VisibleSession.NextSubmissionSequence, null, command));
        PersistenceTiming.Record("draft.execute", stageStarted);
        if (!result.IsAccepted)
        {
            var restored = GameSession.Restore(before);
            if (!restored.IsSuccess) throw new InvalidOperationException("Rejected draft edit could not restore its prior state: " + restored.Error);
            VisibleSession = restored.Session!;
            return new(false, result.Message);
        }

        stageStarted = PersistenceTiming.Start();
        var checkpoint = VisibleSession.CapturePersistenceSnapshot();
        PersistenceTiming.Record("draft.capture", stageStarted);
        var generation = NextGeneration++;
        var previous = _tail;
        _tail = Task.Run(async () =>
        {
            try
            {
                var prior = await previous.ConfigureAwait(false);
                if (!prior.IsSuccess) return SaveOperationResult.Failure("Dependent draft edit was not saved after an earlier save failed.");
                return AutosaveRotation.SaveCaptured(_directory, checkpoint, _compatibility, now, generation, failureInjector);
            }
            catch (Exception error)
            {
                return SaveOperationResult.Failure("Draft save failed before replacement: " + error.Message);
            }
        });
        _pending.Enqueue(new(checkpoint, generation, _tail));
        return new(true, null);
    }

    public DraftPollResult Poll()
    {
        var committed = 0;
        var rolledBack = false;
        string? error = null;
        while (_pending.TryPeek(out var head) && head.Save.IsCompleted)
        {
            _pending.Dequeue();
            var saved = head.Save.GetAwaiter().GetResult();
            if (!_failed && saved.IsSuccess)
            {
                _durableSnapshot = head.Checkpoint;
                _durableGeneration = head.Generation + 1;
                committed++;
            }
            else if (!_failed)
            {
                _failed = true;
                var restored = GameSession.Restore(_durableSnapshot);
                if (!restored.IsSuccess) throw new InvalidOperationException("Durable draft snapshot could not be restored: " + restored.Error);
                VisibleSession = restored.Session!;
                NextGeneration = _durableGeneration;
                rolledBack = true;
                error = saved.Error ?? "Draft save failed; unsaved edits were rolled back.";
            }
        }
        if (!HasPending) _tail = Task.FromResult(SaveOperationResult.Success("no-write"));
        return new(committed, rolledBack, error);
    }

    public DraftPollResult FinishPending()
    {
        if (HasPending) _tail.GetAwaiter().GetResult();
        return Poll();
    }
}

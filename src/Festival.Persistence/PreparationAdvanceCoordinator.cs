using Festival.Simulation;

namespace Festival.Persistence;

public sealed record PreparationAdvanceResult(bool IsSuccess, GameSession Session, SaveOperationResult? Autosave, string? Error);

/// <summary>Advance ordinary ticks directly; stage, persist and validate either edition
/// boundary before publishing its new phase and expiring contracts.</summary>
public static class PreparationAdvanceCoordinator
{
    public static PreparationAdvanceResult AdvanceOne(string directory, GameSession session, SaveCompatibility compatibility,
        DateTimeOffset now, long generation, Action<SaveFailurePoint>? failureInjector = null)
    {
        if (session.PreparedStatus is null)
            return new(false, session, null, "A prepared edition is required.");
        if (!session.ImmersionBoundaryOnNextTick && !session.PreparationBoundaryOnNextTick && !session.EquipmentBoundaryOnNextTick && !session.LivePerformanceBoundaryOnNextTick && !session.MedicalBoundaryOnNextTick && !session.DisorderBoundaryOnNextTick)
        {
            session.AdvanceWithoutSnapshot(1);
            return new(true, session, null, null);
        }
        var stageStarted = PersistenceTiming.Start();
        var snapshot = session.CapturePersistenceSnapshot();
        PersistenceTiming.Record("boundary.capture", stageStarted);
        return AdvanceCapturedBoundary(directory, session, snapshot, compatibility, now, generation, failureInjector);
    }

    /// <summary>The caller must keep source unchanged until this result is published.
    /// A captured boundary may be staged and written off the presentation thread.</summary>
    public static PreparationAdvanceResult AdvanceCapturedBoundary(string directory, GameSession source,
        SessionPersistenceSnapshot snapshot, SaveCompatibility compatibility, DateTimeOffset now, long generation,
        Action<SaveFailurePoint>? failureInjector = null)
    {
        var stageStarted = PersistenceTiming.Start();
        var staged = GameSession.Restore(snapshot);
        PersistenceTiming.Record("boundary.restore", stageStarted);
        if (!staged.IsSuccess) return new(false, source, null, $"Could not stage edition boundary: {staged.Error}");
        var candidate = staged.Session!;
        stageStarted = PersistenceTiming.Start();
        candidate.AdvanceWithoutSnapshot(1);
        PersistenceTiming.Record("boundary.advance", stageStarted);
        stageStarted = PersistenceTiming.Start();
        var saved = AutosaveRotation.Save(directory, candidate, compatibility, now, generation, failureInjector);
        PersistenceTiming.Record("boundary.save", stageStarted);
        return saved.IsSuccess ? new(true, candidate, saved, null) :
            new(false, source, saved, $"Edition boundary not applied; fix the save location then retry. {saved.Error}");
    }
}

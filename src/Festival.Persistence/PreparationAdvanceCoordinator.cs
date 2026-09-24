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
        if (!session.PreparationBoundaryOnNextTick && !session.EquipmentBoundaryOnNextTick && !session.LivePerformanceBoundaryOnNextTick)
        {
            session.AdvanceWithoutSnapshot(1);
            return new(true, session, null, null);
        }
        var staged = GameSession.Restore(session.CapturePersistenceSnapshot());
        if (!staged.IsSuccess) return new(false, session, null, $"Could not stage edition boundary: {staged.Error}");
        var candidate = staged.Session!;
        candidate.AdvanceWithoutSnapshot(1);
        var saved = AutosaveRotation.Save(directory, candidate, compatibility, now, generation, failureInjector);
        return saved.IsSuccess ? new(true, candidate, saved, null) :
            new(false, session, saved, $"Edition boundary not applied; fix the save location then retry. {saved.Error}");
    }
}

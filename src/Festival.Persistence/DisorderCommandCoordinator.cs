using Festival.Simulation;

namespace Festival.Persistence;

/// <summary>Publish a security or area action only after its authoritative result is durably saved.</summary>
public static class DisorderCommandCoordinator
{
    public static PreparationAdvanceResult Execute(string directory, GameSession session, DisorderCommand command,
        SaveCompatibility compatibility, DateTimeOffset now, long generation, Action<SaveFailurePoint>? failureInjector = null)
    {
        if (session.CaptureDisorder() is null) return new(false, session, null, "A disorder edition is required.");
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        if (!restored.IsSuccess) return new(false, session, null, restored.Error);
        var candidate = restored.Session!;
        var result = candidate.Execute(new(new CommandId(4_040_000 + candidate.NextSubmissionSequence), candidate.CampaignId,
            candidate.Phase, candidate.CurrentTick, candidate.NextSubmissionSequence, null, command));
        if (!result.IsAccepted) return new(false, session, null, result.Message);
        var saved = AutosaveRotation.Save(directory, candidate, compatibility, now, generation, failureInjector);
        return saved.IsSuccess ? new(true, candidate, saved, null) : new(false, session, saved,
            $"Disorder action not applied; prior state retained. Fix the save location and retry. {saved.Error}");
    }
}

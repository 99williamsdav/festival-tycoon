using Festival.Simulation;

namespace Festival.Persistence;

/// <summary>A medical response is published only after its command and owned route are durably saved.</summary>
public static class MedicalCommandCoordinator
{
    public static PreparationAdvanceResult Execute(string directory, GameSession session, MedicalCommand command,
        SaveCompatibility compatibility, DateTimeOffset now, long generation, Action<SaveFailurePoint>? failureInjector = null)
    {
        if (session.CaptureMedical() is null) return new(false, session, null, "A Hot medical edition is required.");
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        if (!restored.IsSuccess) return new(false, session, null, restored.Error);
        var candidate = restored.Session!;
        var result = candidate.Execute(new(new CommandId(3_030_000 + candidate.NextSubmissionSequence), candidate.CampaignId,
            candidate.Phase, candidate.CurrentTick, candidate.NextSubmissionSequence, null, command));
        if (!result.IsAccepted) return new(false, session, null, result.Message);
        var saved = AutosaveRotation.Save(directory, candidate, compatibility, now, generation, failureInjector);
        return saved.IsSuccess ? new(true, candidate, saved, null) : new(false, session, saved,
            $"Medical action not applied; prior state retained. Fix the save location and retry. {saved.Error}");
    }
}

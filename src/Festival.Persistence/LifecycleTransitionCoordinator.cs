using Festival.Simulation;

namespace Festival.Persistence;

public sealed record LifecycleTransitionResult(
    bool IsSuccess,
    CommandReasonCode ReasonCode,
    string Message,
    GameSession Session,
    SaveOperationResult? Autosave);

/// <summary>
/// R0.00 boundary commit: validate on the current state, apply to an isolated candidate,
/// durably save and validate that candidate, then expose it to the caller.
/// </summary>
public static class LifecycleTransitionCoordinator
{
    public static LifecycleTransitionResult Apply(
        string saveDirectory,
        GameSession session,
        SaveCompatibility compatibility,
        DateTimeOffset now,
        long autosaveGeneration,
        CommandEnvelope command,
        Action<SaveFailurePoint>? failureInjector = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (command.Command is not (ForceFixtureDeathsCommand or SpendFixtureFavourCommand or ForceFixtureSafeCompletionCommand))
            return new(false, CommandReasonCode.InvalidParameter, "Lifecycle transition requires an R0.00 fixture outcome command.", session, null);

        var rejection = session.ValidateCommand(command);
        if (rejection is not null)
            return new(false, rejection.ReasonCode, rejection.Message, session, null);

        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        if (!restored.IsSuccess)
            return new(false, CommandReasonCode.InvalidParameter, $"Could not stage lifecycle transition: {restored.Error}", session, null);
        var candidate = restored.Session!;
        var executed = candidate.Execute(command);
        if (!executed.IsAccepted)
            return new(false, executed.ReasonCode, $"Staged lifecycle transition rejected: {executed.Message}", session, null);

        var autosave = AutosaveRotation.Save(saveDirectory, candidate, compatibility, now, autosaveGeneration, failureInjector);
        if (!autosave.IsSuccess)
            return new(false, CommandReasonCode.InvalidParameter,
                $"Lifecycle transition was not exposed because its mandatory post-boundary autosave failed. {autosave.Error}", session, autosave);

        return new(true, CommandReasonCode.Accepted, "Lifecycle boundary applied after its post-transition autosave was validated.", candidate, autosave);
    }
}

using Festival.Simulation;

namespace Festival.Persistence;

public sealed record PlanningAdvanceResult(
    bool IsSuccess,
    CommandReasonCode ReasonCode,
    string Message,
    WeeklyDigestSnapshot? Digest,
    SaveOperationResult? Autosave);

public static class PlanningAdvanceCoordinator
{
    public static PlanningAdvanceResult Advance(
        string saveDirectory,
        GameSession session,
        SaveCompatibility compatibility,
        DateTimeOffset now,
        long autosaveGeneration,
        CommandEnvelope command,
        Action<SaveFailurePoint>? failureInjector = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (command.Command is not AdvancePlanningWeekCommand)
            return new PlanningAdvanceResult(false, CommandReasonCode.InvalidParameter, "Planning advance requires an Advance Planning Week command.", null, null);

        var rejection = session.ValidateCommand(command);
        if (rejection is not null)
            return new PlanningAdvanceResult(false, rejection.ReasonCode, rejection.Message, null, null);

        var autosave = AutosaveRotation.Save(saveDirectory, session, compatibility, now, autosaveGeneration, failureInjector);
        if (!autosave.IsSuccess)
            return new PlanningAdvanceResult(false, CommandReasonCode.InvalidParameter,
                $"Advance Week was not applied because the mandatory autosave failed. {autosave.Error}", null, autosave);

        var executed = session.Execute(command);
        if (!executed.IsAccepted)
            return new PlanningAdvanceResult(false, executed.ReasonCode,
                $"Advance Week was not applied after autosave: {executed.Message}", null, autosave);
        var digest = session.CaptureCampaignPlanningSnapshot()!.WeeklyDigests[^1];
        return new PlanningAdvanceResult(true, CommandReasonCode.Accepted, "Week advanced after a verified rotating autosave.", digest, autosave);
    }
}

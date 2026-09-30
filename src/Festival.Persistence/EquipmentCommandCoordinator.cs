using Festival.Simulation;

namespace Festival.Persistence;

public static class EquipmentCommandCoordinator
{
    public static PreparationAdvanceResult Execute(string directory, GameSession session, SessionCommand command,
        SaveCompatibility compatibility, DateTimeOffset now, long generation, Action<SaveFailurePoint>? failureInjector = null)
    {
        if (session.CaptureEquipment() is null || command is not (StartPreparedEditionCommand or RemovePreparationOfferCommand or SetPreparationStockCommand or PlaceBuildServiceCommand or MoveBuildServiceCommand or RemoveBuildServiceCommand or UseDefaultBuildLayoutCommand or PerkCommand or SetProgrammeCommand or EquipmentCommand or AcceptPreparationOfferCommand or
                CommitCommunityWaterShareCommand or
                StaffInterventionCommand or SpendCouncilFavourCommand or ConcedeCouncilHearingCommand))
            return new(false, session, null, "An equipment, preparation or Council hearing action is required.");
        var stageStarted = PersistenceTiming.Start();
        var snapshot = session.CapturePersistenceSnapshot();
        PersistenceTiming.Record("command.capture", stageStarted);
        stageStarted = PersistenceTiming.Start();
        var restored = GameSession.Restore(snapshot);
        PersistenceTiming.Record("command.restore", stageStarted);
        if (!restored.IsSuccess) return new(false, session, null, restored.Error);
        var candidate = restored.Session!;
        stageStarted = PersistenceTiming.Start();
        var result = candidate.Execute(new(new CommandId(2_020_000 + candidate.NextSubmissionSequence), candidate.CampaignId,
            candidate.Phase, candidate.CurrentTick, candidate.NextSubmissionSequence, null, command));
        PersistenceTiming.Record("command.execute", stageStarted);
        if (!result.IsAccepted) return new(false, session, null, result.Message);
        stageStarted = PersistenceTiming.Start();
        var saved = AutosaveRotation.Save(directory, candidate, compatibility, now, generation, failureInjector);
        PersistenceTiming.Record("command.save", stageStarted);
        return saved.IsSuccess ? new(true, candidate, saved, null) : new(false, session, saved,
            saved.Error?.Contains("Temporary save validation failed:", StringComparison.Ordinal) == true
                ? $"Action not applied; prior state retained. Save validation rejected the change. {saved.Error}"
                : $"Action not applied; prior state retained. Check the save location and retry. {saved.Error}");
    }
}

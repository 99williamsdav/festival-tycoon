using Festival.Simulation;

namespace Festival.Persistence;

public static class EquipmentCommandCoordinator
{
    public static PreparationAdvanceResult Execute(string directory, GameSession session, SessionCommand command,
        SaveCompatibility compatibility, DateTimeOffset now, long generation, Action<SaveFailurePoint>? failureInjector = null)
    {
        if (session.CaptureEquipment() is null || command is not (PurchaseImmersionStarterStockCommand or PlaceImmersionVendorCommand or SetProgrammeCommand or EquipmentCommand or AcceptPreparationOfferCommand or
                CommitCommunityWaterShareCommand or ApplyWaterFoundationEffectCommand or ApplyStaffFoundationEffectCommand or PlaceWaterPointCommand or
                MovePrimaryWaterPointCommand or MoveWaterPointCommand or StaffInterventionCommand or DevelopmentMedicalFixtureCommand or DevelopmentDisorderEgressFixtureCommand or SpendCouncilFavourCommand or ConcedeCouncilHearingCommand))
            return new(false, session, null, "An equipment, preparation or Council hearing action is required.");
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        if (!restored.IsSuccess) return new(false, session, null, restored.Error);
        var candidate = restored.Session!;
        var result = candidate.Execute(new(new CommandId(2_020_000 + candidate.NextSubmissionSequence), candidate.CampaignId,
            candidate.Phase, candidate.CurrentTick, candidate.NextSubmissionSequence, null, command));
        if (!result.IsAccepted) return new(false, session, null, result.Message);
        var saved = AutosaveRotation.Save(directory, candidate, compatibility, now, generation, failureInjector);
        return saved.IsSuccess ? new(true, candidate, saved, null) : new(false, session, saved,
            $"Action not applied; prior state retained. Fix the save location and retry the action. {saved.Error}");
    }
}

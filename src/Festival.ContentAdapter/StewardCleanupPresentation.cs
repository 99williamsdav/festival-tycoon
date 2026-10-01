using Festival.Simulation;

namespace Festival.ContentAdapter;

public enum StewardCleanupMode { Stowed, Equipped, GroundPickup, EmptyingBin }
public sealed record StewardCleanupVisual(StewardCleanupMode Mode, string? TargetId = null,
    WastePiece? Waste = null, double Progress = 0)
{
    // One cosmetic instance replaces the ground instance from contact onward, until actual removal.
    public bool LiftedWaste => Mode == StewardCleanupMode.GroundPickup && Progress >= StewardCleanupPresentation.ContactProgress;
}

// Read-only projection. No presentation clock, random calls, saved fields or waste mutation.
public static class StewardCleanupPresentation
{
    public const double ContactProgress = .45, BagProgress = .85;
    public static StewardCleanupVisual Read(GameSession session, ulong workerId)
    {
        var sweep = session.CaptureCleanupSweep(workerId);
        var person = session.CapturePerson(workerId);
        var blocks = PersonClaims.MedicalNavigation | PersonClaims.ResponseAssigned | PersonClaim.Fighting |
            PersonClaim.Performing | PersonClaim.ToiletVisit | PersonClaim.Shopping | PersonClaim.Maintaining | PersonClaim.WasteDisposal;
        if (session.PreparedStatus != PreparationStatus.Running || sweep is not { Remaining: > 0 } ||
            session.CaptureCarriedWaste(workerId) is not null ||
            person is not { Role: ProtectedPersonRole.Staff, Admitted: true, Departed: false, Held: null } ||
            (session.CaptureClaims(workerId) & blocks) != 0 || person.HealthStage is not (MedicalStage.Clear or MedicalStage.Treated) ||
            person.Thirst >= 7000 || person.HeatExposure >= 7000 || person.ToiletNeed >= 7500 || person.Hunger >= 8000 ||
            !session.GetStewardResponses().Any(s => s.WorkerId == workerId && !s.Incapacitated)) return new(StewardCleanupMode.Stowed);
        if (sweep.TargetId is null || sweep.Approach is null || sweep.ActionTick < 0 || sweep.ActionTick > session.CurrentTick)
            return new(StewardCleanupMode.Equipped);
        var duration = sweep.TargetIsBin ? LitterRules.EmptyTicks : LitterRules.DisposalTicks;
        var progress = Math.Clamp((session.CurrentTick - sweep.ActionTick) / (double)duration, 0, 1);
        if (sweep.TargetIsBin) return new(StewardCleanupMode.EmptyingBin, sweep.TargetId, Progress: progress);
        var waste = session.CaptureWastePiece(sweep.TargetId);
        return waste is { Location: WasteLocation.Ground }
            ? new(StewardCleanupMode.GroundPickup, sweep.TargetId, waste, progress)
            : new(StewardCleanupMode.Equipped);
    }
}

namespace Festival.Simulation;

public sealed partial class GameSession
{
    private static string StageSoundCategory(int stage)=>stage==0?"staff":stage==1?"extra-sound":"extra-sound-2";
    public static string StageSoundOfferId(StaffCandidate candidate,int stage)=>stage==0?candidate.Id:
        $"staff.{StageSoundCategory(stage)}.{candidate.Number}";
    public StaffCandidate? ChosenStageEngineer(int stage)=>_preparation is not {} p||stage<0||stage>=Stages.Count?null:
        GetStaffCandidates().FirstOrDefault(c=>c.Role==StaffRole.Sound&&(p.Plan is{Committed:false} plan?plan.OfferIds:p.AcceptedOffers)
            .Contains(StageSoundOfferId(c,stage)));
}

namespace Festival.Simulation;

internal readonly record struct FestivalProgressionContext(FestivalDefinition Festival,PreparationSnapshot? Preparation,
    PersistentCampaignSnapshot? Campaign,bool TerminalOutcome,bool PondTrial);
internal interface ICampaignProgressionPolicy
{FestivalDefinition? NextFestival(FestivalProgressionContext context);}
internal sealed class SurvivalProgressionPolicy:ICampaignProgressionPolicy
{
    internal static readonly SurvivalProgressionPolicy Instance=new();
    private SurvivalProgressionPolicy(){}
    public FestivalDefinition? NextFestival(FestivalProgressionContext context)=>
        context.Preparation is{Status:PreparationStatus.Finished,Result:not null}&&context.Festival.Tier<SurvivalBalance.HighestTier&&
        !context.TerminalOutcome&&context.Campaign is{Identity.ManagementCapability:true}
            ?CampaignRulesProfile.For(CampaignRuleset.Survival).Festival(context.Festival.Tier+1,context.PondTrial):null;
}

internal enum FatalIncidentKind { Medical,Disorder,Equipment }
internal readonly record struct FatalIncident(FatalIncidentKind Kind,Person Victim,string Cause);
internal sealed class ClassicProgressionPolicy:ICampaignProgressionPolicy
{
    internal static readonly ClassicProgressionPolicy Instance=new();
    public FestivalDefinition? NextFestival(FestivalProgressionContext context)=>
        context.Preparation is{Status:PreparationStatus.Finished,Result:not null}&&context.Campaign is{Identity.ManagementCapability:true}
            ?context.Festival:null;
}

internal enum IncidentConsequence { CouncilFailure,ReputationAndContinue }
internal interface IIncidentConsequencePolicy
{IncidentConsequence Resolve(FatalIncident incident);}
internal sealed class SurvivalIncidentConsequencePolicy:IIncidentConsequencePolicy
{
    internal static readonly SurvivalIncidentConsequencePolicy Instance=new();
    private SurvivalIncidentConsequencePolicy(){}
    public IncidentConsequence Resolve(FatalIncident incident)=>Enum.IsDefined(incident.Kind)?IncidentConsequence.CouncilFailure:
        throw new ArgumentOutOfRangeException(nameof(incident));
}
internal sealed class ClassicIncidentConsequencePolicy:IIncidentConsequencePolicy
{
    internal static readonly ClassicIncidentConsequencePolicy Instance=new();
    public IncidentConsequence Resolve(FatalIncident incident)=>Enum.IsDefined(incident.Kind)?IncidentConsequence.ReputationAndContinue:
        throw new ArgumentOutOfRangeException(nameof(incident));
}

public sealed partial class GameSession
{
    // Detection and its evidence stay in their hazard domains; the selected policy owns campaign consequences.
    private void ApplyIncidentConsequence(FatalIncident incident)
    {
        switch(RuleProfile.IncidentConsequences.Resolve(incident))
        {
            case IncidentConsequence.CouncilFailure:
                ApplyFatalCouncilFailure(incident);break;
            case IncidentConsequence.ReputationAndContinue:
                ApplyClassicFatalIncident(incident);break;
            default:throw new InvalidOperationException("Incident consequence is not implemented.");
        }
    }
    private void ApplyFatalCouncilFailure(FatalIncident incident)
    {
        var prefix=incident.Kind switch{FatalIncidentKind.Medical=>"medical",FatalIncidentKind.Disorder=>"disorder",FatalIncidentKind.Equipment=>"equipment",_=>throw new ArgumentOutOfRangeException(nameof(incident))};
        var lifecycle=_lifecycle!;var attempt=CurrentAttempt();var victim=incident.Victim;
        var transaction=$"{prefix}-death:{CampaignId.Value}:{attempt.AttemptId}";
        lifecycle.Casualties.Add(new(lifecycle.NextCasualtyId++,attempt.AttemptId,victim.Name,victim.Role,incident.Cause,CurrentTick,transaction));
        lifecycle.CompletedOutcomeTransactionIds.Add(transaction);
        ReplaceAttempt(attempt with{Status=EditionAttemptStatus.Failed,OutcomeTransactionId=transaction});
        var hearing=$"{prefix}-hearing:{CampaignId.Value}:{attempt.AttemptId}";
        lifecycle.Hearings.Add(new(lifecycle.NextHearingId++,attempt.AttemptId,HearingStatus.Open,hearing,null));
        lifecycle.CompletedOutcomeTransactionIds.Add(hearing);
        ResolveNoFavourHearing();FailEdition(_preparation!);
        ReleaseInterventionsForBoundary("First death froze the edition; intervention released");FinishLivePerformance();
    }
}

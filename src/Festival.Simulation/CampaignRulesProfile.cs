namespace Festival.Simulation;

/// <summary>Existing Survival campaign values, centralized without changing their public constant aliases.</summary>
public static class SurvivalBalance
{
    public const int HighestTier=2,GuestsPerTier=25,FreeExtraMedicFromTier=2,MaxActivePeople=100;
    public const int PreparedDayTicks=38_400,ClosingAfterLastSetTicks=800,DepartureCapTicks=8_000;
    public const long OpeningCashPennies=45_000,OpeningLoanPrincipalPennies=20_000,OverdraftPennies=10_000;
    public const long EditionPrincipalPennies=12_000,BasicAdministrationPennies=4_000;
    public const int InterestBasisPoints=800,LoanTermEditions=5;
    public const int MedicalCollapseDelayTicks=1600,MedicalCriticalDelayTicks=800,MedicalDeathDelayTicks=2400,DisorderInjuryDeathTicks=2400;
    public const int EquipmentWarningDelayTicks=2400,EquipmentDangerDelayTicks=3600,EquipmentDeathDelayTicks=4800;
}

public sealed record IncidentTimingParameters(int MedicalCollapseDelayTicks,int MedicalCriticalDelayTicks,int MedicalDeathDelayTicks,
    int DisorderInjuryDeathTicks,int EquipmentWarningDelayTicks,int EquipmentDangerDelayTicks,int EquipmentDeathDelayTicks);

/// <summary>Resolved setup for one current festival; persistent campaign holdings remain in existing session state.</summary>
public sealed record FestivalDefinition(int Tier,IReadOnlyList<FestivalStage> Stages,int Attendance,int TicketPricePennies,
    WeatherKind Weather,FestivalChallenge? Challenge,TierBuildLimits BuildLimits,bool ExtraMedicSlot,bool SecondSoundEngineerRequired)
{
    public long TicketReceiptsPennies=>(long)Attendance*TicketPricePennies;
    public int DurationTicks=>Stages.Max(stage=>stage.SlotEnds[^1])+SurvivalBalance.ClosingAfterLastSetTicks;
    public int PerformerCount=>Stages.Sum(stage=>stage.SlotStarts.Count*3);
}

/// <summary>One implemented profile. New rulesets must supply their own defined configuration and policies.</summary>
internal abstract class CampaignRulesProfile
{
    private static readonly CampaignRulesProfile Survival=new SurvivalRulesProfile();
    private static readonly CampaignRulesProfile Classic=new ClassicRulesProfile();
    internal static CampaignRulesProfile For(CampaignRuleset ruleset)=>ruleset switch
    {CampaignRuleset.Survival=>Survival,CampaignRuleset.Classic=>Classic,_=>throw new ArgumentOutOfRangeException(nameof(ruleset),"Ruleset is not implemented.")};
    internal abstract FestivalDefinition Festival(int tier,bool pondTrial=false);
    internal abstract ICampaignProgressionPolicy Progression {get;}
    internal abstract IIncidentConsequencePolicy IncidentConsequences {get;}
    internal abstract IncidentTimingParameters Incidents {get;}
    internal virtual int DisorderPressurePercent=>100;
    internal abstract int TicketPrice(int tier);
    internal abstract WeatherKind WeatherForTier(int tier);
    internal abstract FestivalChallenge? ChallengeForTier(int tier);
    internal abstract TierBuildLimits Limits(int tier);
    internal abstract int BuildFee(BuildServiceKind kind);
}

/// <summary>Approved provisional Classic values; stages, expansion and random forecasts remain later work.</summary>
public static class ClassicBalance
{
    public const int DeathReputationPenalty=10,DisorderPressurePercent=50,IncidentWindowMultiplier=2;
}

internal sealed class ClassicRulesProfile:CampaignRulesProfile
{
    private static CampaignRulesProfile Existing=>For(CampaignRuleset.Survival);
    internal override ICampaignProgressionPolicy Progression=>ClassicProgressionPolicy.Instance;
    internal override IIncidentConsequencePolicy IncidentConsequences=>ClassicIncidentConsequencePolicy.Instance;
    internal override IncidentTimingParameters Incidents {get;}=new(3200,1600,4800,4800,4800,7200,9600);
    internal override int DisorderPressurePercent=>ClassicBalance.DisorderPressurePercent;
    internal override FestivalDefinition Festival(int tier,bool pondTrial=false)
    {
        if(tier!=1||pondTrial)throw new ArgumentOutOfRangeException(nameof(tier),"Classic's first loop uses the inherited Trailer setup.");
        return new(1,FestivalStages.For(1,false),SurvivalBalance.GuestsPerTier,TicketPrice(1),WeatherKind.Mild,null,Limits(1),false,false);
    }
    internal override int TicketPrice(int tier)=>Existing.TicketPrice(1);
    internal override WeatherKind WeatherForTier(int tier)=>WeatherKind.Mild;
    internal override FestivalChallenge? ChallengeForTier(int tier)=>null;
    internal override TierBuildLimits Limits(int tier)=>Existing.Limits(1);
    internal override int BuildFee(BuildServiceKind kind)=>Existing.BuildFee(kind);
}

internal sealed class SurvivalRulesProfile:CampaignRulesProfile
{
    internal override ICampaignProgressionPolicy Progression=>SurvivalProgressionPolicy.Instance;
    internal override IIncidentConsequencePolicy IncidentConsequences=>SurvivalIncidentConsequencePolicy.Instance;
    internal override IncidentTimingParameters Incidents {get;}=new(SurvivalBalance.MedicalCollapseDelayTicks,SurvivalBalance.MedicalCriticalDelayTicks,
        SurvivalBalance.MedicalDeathDelayTicks,SurvivalBalance.DisorderInjuryDeathTicks,SurvivalBalance.EquipmentWarningDelayTicks,
        SurvivalBalance.EquipmentDangerDelayTicks,SurvivalBalance.EquipmentDeathDelayTicks);
    internal override FestivalDefinition Festival(int tier,bool pondTrial=false)
    {
        if(tier is <1 or >SurvivalBalance.HighestTier)throw new ArgumentOutOfRangeException(nameof(tier));
        var stages=FestivalStages.For(tier,pondTrial);
        return new(tier,stages,tier*SurvivalBalance.GuestsPerTier,TicketPrice(tier),WeatherForTier(tier),ChallengeForTier(tier),Limits(tier),
            tier>=SurvivalBalance.FreeExtraMedicFromTier,stages.Count>1);
    }
    // Preserve existing diagnostic/helper domains; these values do not enable later tiers.
    internal override int TicketPrice(int tier)=>tier switch{1=>1000,2=>1500,3=>3500,_=>6000};
    internal override WeatherKind WeatherForTier(int tier)=>tier>=2?WeatherKind.Rain:WeatherKind.Heatwave;
    internal override FestivalChallenge? ChallengeForTier(int tier)=>tier switch
    {
        1=>new("heatwave","Heatwave","Guests build up heat and thirst. Water and shade help them cool down.","heatwave-b.png",true),
        2=>new("heavy-rain","Heavy rain","Heavy showers soak guests and turn the field muddy. Ponchos and shelter help keep people dry.","heavy-rain-a.png",true),
        _=>null
    };
    internal override TierBuildLimits Limits(int tier)=>tier<2?new(1,2,1,1,1,1,1):new(2,3,2,2,1,1,2);
    internal override int BuildFee(BuildServiceKind kind)=>kind switch
    {BuildServiceKind.WaterTap=>1500,BuildServiceKind.Toilet=>3000,BuildServiceKind.FoodVan=>0,BuildServiceKind.Bar=>5000,
        BuildServiceKind.FirstAid=>3500,BuildServiceKind.StewardPost=>2500,BuildServiceKind.Bin=>1000,BuildServiceKind.Marquee=>MarqueeRules.FeePennies,
        _=>throw new InvalidOperationException("Sequence contains no matching element")};
}

namespace Festival.Simulation;

/// <summary>Existing Survival campaign values, centralized without changing their public constant aliases.</summary>
public static class SurvivalBalance
{
    public const int HighestTier=2,GuestsPerTier=25,FreeExtraMedicFromTier=2,MaxActivePeople=100;
    public const int PreparedDayTicks=38_400,ClosingAfterLastSetTicks=800,DepartureCapTicks=8_000;
    public const long OpeningCashPennies=45_000,OpeningLoanPrincipalPennies=20_000,OverdraftPennies=10_000;
    public const long EditionPrincipalPennies=12_000,BasicAdministrationPennies=4_000;
    public const int InterestBasisPoints=800,LoanTermEditions=5;
}

/// <summary>Resolved setup for one current festival; persistent campaign holdings remain in existing session state.</summary>
public sealed record FestivalDefinition(int Tier,IReadOnlyList<FestivalStage> Stages,int Attendance,int TicketPricePennies,
    WeatherKind Weather,FestivalChallenge? Challenge,TierBuildLimits BuildLimits,bool ExtraMedicSlot,bool SecondSoundEngineerRequired)
{
    public long TicketReceiptsPennies=>(long)Attendance*TicketPricePennies;
    public int DurationTicks=>Stages.Max(stage=>stage.SlotEnds[^1])+SurvivalBalance.ClosingAfterLastSetTicks;
    public int PerformerCount=>Stages.Sum(stage=>stage.SlotStarts.Count*3);
}

/// <summary>One implemented profile. New rulesets must supply their own defined configuration and policies.</summary>
internal sealed class CampaignRulesProfile
{
    private static readonly CampaignRulesProfile Survival=new();
    private CampaignRulesProfile(){}
    internal static CampaignRulesProfile For(CampaignRuleset ruleset)=>ruleset switch
    {CampaignRuleset.Survival=>Survival,_=>throw new ArgumentOutOfRangeException(nameof(ruleset),"Ruleset is not implemented.")};
    internal FestivalDefinition Festival(int tier,bool pondTrial=false)
    {
        if(tier is <1 or >SurvivalBalance.HighestTier)throw new ArgumentOutOfRangeException(nameof(tier));
        var stages=FestivalStages.For(tier,pondTrial);
        return new(tier,stages,tier*SurvivalBalance.GuestsPerTier,TicketPrice(tier),WeatherForTier(tier),ChallengeForTier(tier),Limits(tier),
            tier>=SurvivalBalance.FreeExtraMedicFromTier,stages.Count>1);
    }
    // Preserve existing diagnostic/helper domains; these values do not enable later tiers.
    internal int TicketPrice(int tier)=>tier switch{1=>1000,2=>1500,3=>3500,_=>6000};
    internal WeatherKind WeatherForTier(int tier)=>tier>=2?WeatherKind.Rain:WeatherKind.Heatwave;
    internal FestivalChallenge? ChallengeForTier(int tier)=>tier switch
    {
        1=>new("heatwave","Heatwave","Guests build up heat and thirst. Water and shade help them cool down.","heatwave-b.png",true),
        2=>new("heavy-rain","Heavy rain","Heavy showers soak guests and turn the field muddy. Ponchos and shelter help keep people dry.","heavy-rain-a.png",true),
        _=>null
    };
    internal TierBuildLimits Limits(int tier)=>tier<2?new(1,2,1,1,1,1,1):new(2,3,2,2,1,1,2);
    internal int BuildFee(BuildServiceKind kind)=>kind switch
    {BuildServiceKind.WaterTap=>1500,BuildServiceKind.Toilet=>3000,BuildServiceKind.FoodVan=>0,BuildServiceKind.Bar=>5000,
        BuildServiceKind.FirstAid=>3500,BuildServiceKind.StewardPost=>2500,BuildServiceKind.Bin=>1000,BuildServiceKind.Marquee=>MarqueeRules.FeePennies,
        _=>throw new InvalidOperationException("Sequence contains no matching element")};
}

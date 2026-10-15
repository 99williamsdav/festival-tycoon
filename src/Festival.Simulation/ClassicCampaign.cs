using System.Text.Json;

namespace Festival.Simulation;

public sealed record ClassicDeathPenalty(string Kind,CasualtySnapshot Casualty,int ReputationBefore,int ReputationAfter);
/// <summary>Factual closed-festival carry proof, not another store of current cash or property.</summary>
public sealed record ClassicFestivalRecord(int Number,long OpeningCashPennies,long ClosingCashPennies,LoanSnapshot Loan,
    FestivalStanding OpeningStanding,FestivalStanding ClosingStanding,FestivalResult Result,ClassicDeathPenalty[] DeathPenalties,
    string[] SeenActs,string[] OwnedEquipment,BuildPlacement[] Layout)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ClassicBookingSnapshot? Booking { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? StockPurchasesPennies { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ClassicDevelopmentSnapshot? Development {get;init;}
}
public sealed record ClassicHistorySnapshot(ClassicFestivalRecord[] CompletedFestivals,ClassicDeathPenalty[] DeathPenalties);

public sealed partial class GameSession
{
    private ClassicHistorySnapshot? _classicHistory;
    private (ClassicHistorySnapshot State,string Json)? _classicHistoryJson;
    internal string? ClassicHistoryCanonicalJson=>_classicHistory is not {} history?null:
        _classicHistoryJson is {} cached&&ReferenceEquals(cached.State,history)?cached.Json:CacheClassicHistory(history);
    private string CacheClassicHistory(ClassicHistorySnapshot history)
    {var json=JsonSerializer.Serialize(history);_classicHistoryJson=(history,json);return json;}
    public ClassicHistorySnapshot? CaptureClassicHistory()=>_classicHistory is not {} h?null:CloneClassicHistory(h);
    private static ClassicHistorySnapshot CloneClassicHistory(ClassicHistorySnapshot h)=>new(h.CompletedFestivals.Select(r=>r with
    {OpeningStanding=new(r.OpeningStanding.Reputation,r.OpeningStanding.SceneCredibility.ToArray()),
     ClosingStanding=new(r.ClosingStanding.Reputation,r.ClosingStanding.SceneCredibility.ToArray()),DeathPenalties=r.DeathPenalties.ToArray(),
     SeenActs=r.SeenActs.ToArray(),OwnedEquipment=r.OwnedEquipment.ToArray(),Layout=r.Layout.ToArray(),
     Booking=r.Booking is null?null:ClassicBookingLedger.Clone(r.Booking)}).ToArray(),h.DeathPenalties.ToArray());
    internal static ulong ClassicFestivalSeed(ulong seed,int number)=>number<=1?seed:seed^unchecked((ulong)number*0xC1A551C0C1A551C0UL);
    private ulong FestivalSeed=>IsClassicCampaign?ClassicFestivalSeed(CampaignSeed,FestivalNumber):CampaignSeed;
    private static ulong SavedFestivalSeed(SessionPersistenceSnapshot s)=>s.Identity?.Ruleset==CampaignRuleset.Classic
        ?ClassicFestivalSeed(s.CampaignSeed,s.CampaignPlanning?.EditionNumber??1):s.CampaignSeed;
    private static bool SavedClassic(SessionPersistenceSnapshot s)=>s.Identity?.Ruleset==CampaignRuleset.Classic;
    private FestivalStanding ClassicOpeningStanding=>IsClassicDevelopment&&FestivalNumber==ClassicDevelopmentFirstFestival
        ?new(ClassicDevelopmentReputation,_classicHistory!.CompletedFestivals[^1].ClosingStanding.SceneCredibility.ToArray())
        :_classicHistory?.CompletedFestivals.LastOrDefault()?.ClosingStanding??FestivalStanding.New;
    public static GameSession CreateClassicCampaign(ulong seed)=>CreateBuildCampaign(seed,1,null,null,
        identity:CampaignIdentity.ClassicManagement,classicFestivalNumber:1);
    private GameSession CreateNextClassicFestival()
    {
        var p=_preparation!;var state=CapturePersistentCampaignState()!;
        var record=new ClassicFestivalRecord(FestivalNumber,p.OpeningCashPennies,state.CashPennies,state.Loan,
            new(ClassicOpeningStanding.Reputation,ClassicOpeningStanding.SceneCredibility.ToArray()),
            new(p.Reputation,p.SceneCredibility.ToArray()),p.Result!,_classicHistory!.DeathPenalties.ToArray(),
            p.SeenActs.ToArray(),p.OwnedEquipment.ToArray(),p.BuildPlacements.OrderBy(b=>b.Id,StringComparer.Ordinal).ToArray())
            { Booking = CaptureClassicBooking(), StockPurchasesPennies = CompletedFestivalAccounts!.StockPurchasesPennies,Development=_classicDevelopment };
        var number=checked(FestivalNumber+1);
        var next=CreateBuildCampaign(CampaignSeed,1,CarryOverFrom(p),CampaignId,identity:_identity,classicFestivalNumber:number,classicDevelopment:_classicDevelopment);
        next._classicHistory=new([..CaptureClassicHistory()!.CompletedFestivals,record],[]);
        next._campaignPlanning!.FestivalName=_campaignPlanning!.FestivalName;next._campaignPlanning.Palette=_campaignPlanning.Palette;
        next._campaignPlanning.Loan=_campaignPlanning.Loan;
        foreach(var tip in _campaignPlanning.DismissedTipIds)next._campaignPlanning.DismissedTipIds.Add(tip);
        var (history,relationships,popularity)=RelationshipsForNextFestival(number);
        next.PreparationView=next.PreparationView! with{Reputation=p.Reputation,SceneCredibility=p.SceneCredibility.ToArray(),SeenActs=p.SeenActs.ToArray(),
            ActRelationships=relationships,ActPopularity=popularity,RelationshipHistory=history,
            PreviousLayout=record.Layout.Length==0?null:record.Layout.ToArray(),ClassicBooking=NewClassicBooking(p.Reputation,_classicDevelopment)};
        return next;
    }
    private static PlanningLedgerTransactionSnapshot ClassicOpeningTransaction(EntityId owner,int previous,FestivalCarryOver carry)=>
        CarriedOpeningTransaction(owner,1,carry) with{Reason=$"Carried forward from Festival {previous} and advance ticket sales"};
    private static LoanSnapshot SavedLoan(PersistedCampaignPlanning p)=>new(p.LoanOpeningPrincipalPennies,p.LoanOutstandingPrincipalPennies,
        p.LoanPrincipalDueAtSettlementPennies,p.LoanInterestDueAtSettlementPennies,p.LoanRemainingEditions,p.LoanInterestBasisPoints);
    private static bool StandingEqual(FestivalStanding a,FestivalStanding b)=>a.Reputation==b.Reputation&&a.SceneCredibility.SequenceEqual(b.SceneCredibility);
    private static bool ValidStanding(FestivalStanding? s)=>s is{Reputation:>=0 and <=100,SceneCredibility.Length:FestivalGenre.Count}
        &&s.SceneCredibility.All(v=>v is>=0 and <=100);
    private static bool KnownKit(string[]? kit)=>kit is not null&&kit.All(k=>k=="sound-rig")&&kit.SequenceEqual(kit.Distinct().Order(StringComparer.Ordinal));
    private static bool KnownActs(string[]? acts)=>acts is not null&&acts.All(a=>a is not null&&ActCatalogue.Find(a) is not null)
        &&acts.SequenceEqual(acts.Distinct().Order(StringComparer.Ordinal));
    private static string? ValidateClassicHistory(SessionPersistenceSnapshot s)
    {
        if(!SavedClassic(s))return s.ClassicHistory is null?null:"Classic history belongs only to a Classic campaign.";
        if(s.Identity!=CampaignIdentity.ClassicManagement||s.PondStageTrial||s.Preparation is not{Tier:1,Attempt:1} p||
            s.CampaignPlanning is not{EditionNumber:>=1} planning||s.ClassicHistory is not{} h||h.CompletedFestivals is null||h.DeathPenalties is null||
            h.CompletedFestivals.Any(r=>r is null)||h.CompletedFestivals.Length!=planning.EditionNumber-1)
            return "Classic festival identity, ordinal or history is invalid.";
        var loan=new LoanSnapshot(CampaignDefaults.OpeningLoanPrincipalPennies,CampaignDefaults.OpeningLoanPrincipalPennies,
            CampaignDefaults.EditionPrincipalPennies,CampaignDefaults.OpeningLoanPrincipalPennies*CampaignDefaults.InterestBasisPoints/10000,
            CampaignDefaults.LoanTermEditions,CampaignDefaults.InterestBasisPoints);
        if(SavedLoan(planning)!=loan)return "Classic parked loan differs from its actual starter debt.";
        var opening=CampaignDefaults.OpeningLoanPrincipalPennies;var standing=FestivalStanding.New;string[] owned=[],seen=[];
        foreach(var (r,index) in h.CompletedFestivals.Select((r,i)=>(r,i)))
        {
            if(s.ClassicDevelopment is {} dev&&r.Number==ClassicDevelopmentFirstFestival)
            {opening+=DevelopmentGrant(dev,r.Number);standing=new(ClassicDevelopmentReputation,standing.SceneCredibility);}
            if(r.Layout is null||r.Layout.Any(b=>b is null))return "Classic closed-festival layout is invalid.";
            var booking=ClassicBookingLedger.Evaluate(s.CampaignSeed,r.Number,r.Booking!);
            if(r.Number!=index+1||r.Result is null||r.Result.Attempt!=1||r.OpeningCashPennies!=opening||
                (decimal)r.ClosingCashPennies!=(decimal)r.OpeningCashPennies+r.Result.NetCashChangePennies||
                r.ClosingCashPennies<LeastClosingCashPennies||r.ClosingCashPennies>ClassicClosingCashBound(opening,r.Layout, r.Booking!.Capacity,r.Development is null?MaxActivePeople:ClassicDevelopmentPeopleBound)+booking.NetTicketReceiptsPennies||r.Loan!=loan||
                !ValidStanding(r.OpeningStanding)||!ValidStanding(r.ClosingStanding)||!StandingEqual(r.OpeningStanding,standing)||
                r.Booking!.InitialReputation!=standing.Reputation||
                r.StockPurchasesPennies is null or <0||r.StockPurchasesPennies<r.Result.ConsumedStockCostsPennies||
                r.Result.NetCashChangePennies!=r.Result.RevenuePennies-r.Result.ContractCostsPennies-r.Result.CapitalPurchasesPennies-r.StockPurchasesPennies||
                !KnownKit(r.OwnedEquipment)||!KnownActs(r.SeenActs)||!owned.All(r.OwnedEquipment.Contains)||!seen.All(r.SeenActs.Contains)||
                r.DeathPenalties is null||r.Layout is null||r.Layout.Any(b=>b is null))return "Classic closed-festival carry facts do not reconcile.";
            if(ValidateClassicHistoricalQuotes(s,r) is {} quoteIssue)return quoteIssue;
            if(ValidateClassicPenaltyList(r.DeathPenalties,r.Number,s.CampaignId,booking.Reputation,out var afterDeaths) is {} issue)return issue;
            if(r.Result.Deaths!=r.DeathPenalties.Length||r.Result.Deaths>(r.Development is null?MaxActivePeople:ClassicDevelopmentPeopleBound)||r.Result.AdmittedGuests!=booking.TicketHolders||
                r.Result.GuestCount!=r.Result.AdmittedGuests-r.DeathPenalties.Count(d=>d.Casualty.Role==ProtectedPersonRole.Guest))
                return "Classic historical deaths and rated guests disagree.";
            var historicalStages=r.Development is {} diagnostic?DevelopmentStages(diagnostic):FestivalStages.For(1,false);
            if(r.Result.Tick<DayTicksFor(historicalStages)||r.Result.SatisfactionTotal<0||r.Result.SatisfactionTotal>10000L*r.Result.GuestCount||
                r.Result.RevenuePennies<booking.NetTicketReceiptsPennies||r.Result.ContractCostsPennies<booking.NetBandCostsPennies||r.Result.ConsumedStockCostsPennies<0||
                r.Result.BeersFinished is null or <0||r.Result.Fights is null or <0||r.Result.MedicalCollapses is null or <0||
                ValidateBuildLayout(r.Layout,historicalStages,null,false,1,true) is not null)
                return "Classic closed-festival result or layout is invalid.";
            var gigs=(p.RelationshipHistory??[]).Where(g=>g.Tier==r.Number).ToArray();
            if(gigs.Length!=historicalStages.Count*3||gigs.Any(g=>ActCatalogue.Find(g.ActId) is null)||
                !gigs.Select(g=>g.ActId).Order(StringComparer.Ordinal).SequenceEqual(booking.Contracts.Select(a=>a.ActId).Order(StringComparer.Ordinal)))
                return "Classic closed festival lacks its actual programme facts.";
            var learned=seen.Concat(gigs.Where(g=>g.Started).Select(g=>g.ActId)).Distinct().Order(StringComparer.Ordinal).ToArray();
            if(!r.SeenActs.SequenceEqual(learned))return "Classic learned acts differ from its actual completed sets.";
            standing=new(afterDeaths,r.OpeningStanding.SceneCredibility.ToArray());
            if(r.Result.Stars is{} stars)standing=ActCatalogue.AfterFestival(standing,stars,gigs.Select(g=>ActCatalogue.Find(g.ActId)!.Genre));
            if(!StandingEqual(standing,r.ClosingStanding))return "Classic historical standing does not follow its deaths and festival rating.";
            if(r.Result.CapitalPurchasesPennies!=(r.OwnedEquipment.Length-owned.Length)*12000L)return "Classic carried kit lacks a recorded purchase.";
            owned=r.OwnedEquipment;seen=r.SeenActs;opening=r.ClosingCashPennies;
        }
        if(s.ClassicDevelopment is {} currentDev&&planning.EditionNumber==ClassicDevelopmentFirstFestival)
        {opening+=DevelopmentGrant(currentDev,planning.EditionNumber);standing=new(ClassicDevelopmentReputation,standing.SceneCredibility);}
        var carry=p.CarriedIn;
        if(planning.EditionNumber==1?carry is not null:carry is null||carry.FromTier!=1||carry.CashPennies!=h.CompletedFestivals[^1].ClosingCashPennies||
            carry.DebtPennies!=h.CompletedFestivals[^1].Loan.OutstandingPrincipalPennies||!carry.OwnedEquipment.SequenceEqual(owned))
            return "Classic current carry differs from its last closed festival.";
        if(p.OpeningCashPennies!=opening||!KnownKit(p.OwnedEquipment)||!KnownActs(p.SeenActs)||!seen.All(p.SeenActs.Contains)||
            planning.EditionNumber>1 && !(p.PreviousLayout??[]).SequenceEqual(h.CompletedFestivals[^1].Layout))return "Classic current holdings disagree with carry.";
        var currentLearned=seen.Concat((s.PerformanceRecords??[]).Where(r=>r.StartedTick>=0).Select(r=>r.ActId)).Distinct().Order(StringComparer.Ordinal).ToArray();
        if(!p.SeenActs.SequenceEqual(currentLearned))return "Classic current learned acts differ from its actual completed sets.";
        if(p.ClassicBooking!.InitialReputation!=standing.Reputation)return "Classic booking opening standing differs from carry.";
        var penaltyIssue=ValidateClassicPenaltyList(h.DeathPenalties,planning.EditionNumber,s.CampaignId,SavedBookings(s)!.Reputation,out var currentReputation);
        if(penaltyIssue is not null)return penaltyIssue;
        var currentStanding=new FestivalStanding(currentReputation,standing.SceneCredibility.ToArray());
        if(p.Result?.Stars is{} currentStars)currentStanding=ActCatalogue.AfterFestival(currentStanding,currentStars,BookedActIds(s.Programme)!.Select(a=>ActCatalogue.Find(a)!.Genre));
        return p.Reputation==currentStanding.Reputation&&p.SceneCredibility.SequenceEqual(currentStanding.SceneCredibility)?null:
            "Classic current standing does not follow from its carry, deaths and results.";
    }
    private static string? ValidateClassicPenaltyList(ClassicDeathPenalty[] changes,int number,ulong campaign,int opening,out int after)
    {
        after=opening;var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var (d,i) in changes.Select((d,i)=>(d,i)))
        {
            if(d is null||d.Casualty is not{} c||c.CasualtyId!=(ulong)i+1||c.AttemptId!=1||!Enum.IsDefined(c.Role)||
                d.Kind is not("medical" or "disorder" or "equipment")||
                string.IsNullOrWhiteSpace(c.PersonId)||!names.Add(c.PersonId)||string.IsNullOrWhiteSpace(c.Cause)||c.Tick<0||
                c.TransactionId!=$"classic-death:{campaign}:{number}:{c.PersonId}"||d.ReputationBefore!=after||
                d.ReputationAfter!=Math.Max(0,after-ClassicBalance.DeathReputationPenalty))return "Classic death or once-only reputation consequence is invalid.";
            after=d.ReputationAfter;
        }
        return null;
    }
}

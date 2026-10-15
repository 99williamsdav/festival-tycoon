using System.Text.Json;

namespace Festival.Simulation;

/// <summary>Explicit diagnostic provenance. Never present on an ordinary Classic or Survival campaign.</summary>
public sealed record ClassicDevelopmentSnapshot(int Version,int StageMask,bool GuaranteeHundred,bool TopUpCash);

public sealed partial class GameSession
{
    public const int ClassicDevelopmentCapacity=100;
    public const int ClassicDevelopmentReputation=40;
    // 100 guests + 27 performers + 3 sound engineers + medic/steward + maintenance + two optional responders =135.
    public const int ClassicDevelopmentPeopleBound=135;
    private const int ClassicDevelopmentFirstFestival=2;
    private const long RecordedClassicCash=26190;
    private ClassicDevelopmentSnapshot? _classicDevelopment;
    public bool IsClassicDevelopment=>_classicDevelopment is not null;
    public ClassicDevelopmentSnapshot? CaptureClassicDevelopment()=>_classicDevelopment;
    internal string? ClassicDevelopmentCanonicalJson=>_classicDevelopment is {} d?JsonSerializer.Serialize(d):null;
    private int PeopleSafetyBound=>IsClassicDevelopment?ClassicDevelopmentPeopleBound:MaxActivePeople;
    private static int SavedPeopleSafetyBound(SessionPersistenceSnapshot s)=>s.ClassicDevelopment is not null?ClassicDevelopmentPeopleBound:MaxActivePeople;
    private static readonly IReadOnlyList<FestivalStage>[] DevelopmentStageSets=Enumerable.Range(1,7)
        .Select(mask=>(IReadOnlyList<FestivalStage>)Array.AsReadOnly(FestivalStages.All.Where((stage,index)=>(mask&(1<<index))!=0).ToArray())).ToArray();
    private static IReadOnlyList<FestivalStage> DevelopmentStages(ClassicDevelopmentSnapshot d)=>DevelopmentStageSets[d.StageMask-1];
    private static long DevelopmentGrant(ClassicDevelopmentSnapshot? d,int number)=>d?.TopUpCash==true&&number==ClassicDevelopmentFirstFestival
        ?150000-RecordedClassicCash:0;
    private static long SavedDevelopmentGrant(SessionPersistenceSnapshot s)=>DevelopmentGrant(s.ClassicDevelopment,s.CampaignPlanning?.EditionNumber??1);
    private static ClassicBookingSnapshot NewClassicBooking(int reputation,ClassicDevelopmentSnapshot? d)=>d is null
        ?ClassicBookingLedger.Create(reputation):ClassicBookingLedger.CreateDevelopment(reputation,DevelopmentStages(d).Count*3,d.GuaranteeHundred);
    private static readonly Lazy<SessionPersistenceSnapshot> RecordedClassicClear=new(()=>
    {
        using var stream=typeof(GameSession).Assembly.GetManifestResourceStream("Festival.Simulation.recorded-classic-first-clear.json")
            ??throw new InvalidOperationException("The recorded normal Classic clear is missing.");
        return JsonSerializer.Deserialize<SessionPersistenceSnapshot>(stream)??throw new InvalidOperationException("The recorded Classic clear is empty.");
    });
    private static readonly Lazy<ClassicFestivalRecord> RecordedClassicHistory=new(()=>
    {
        var restored=Restore(RecordedClassicClear.Value);
        if(!restored.IsSuccess)throw new InvalidOperationException("Recorded normal Classic clear: "+restored.Error);
        return restored.Session!.CreateNextClassicFestival().CaptureClassicHistory()!.CompletedFestivals.Single();
    });
    /// <summary>Recorded legitimate carry plus an explicitly synthetic milestone. Diagnostic options default off.</summary>
    public static GameSession CreateClassicDevelopmentStart(int stageMask=1,bool guaranteeHundred=false,bool topUpCash=false)
    {
        if(stageMask is <1 or >7)throw new ArgumentOutOfRangeException(nameof(stageMask),"Open at least one of the three fixed stages.");
        var restored=Restore(RecordedClassicClear.Value);
        if(!restored.IsSuccess)throw new InvalidOperationException("Recorded normal Classic clear: "+restored.Error);
        var ordinary=restored.Session!.CreateNextClassicFestival();
        var d=new ClassicDevelopmentSnapshot(1,stageMask,guaranteeHundred,topUpCash);
        var p=ordinary.CapturePreparation()!;
        var session=CreateBuildCampaign(ordinary.CampaignSeed,1,p.CarriedIn,ordinary.CampaignId,
            identity:CampaignIdentity.ClassicManagement,classicFestivalNumber:ClassicDevelopmentFirstFestival,classicDevelopment:d);
        session._classicHistory=ordinary.CaptureClassicHistory();
        session.PreparationView=session.PreparationView! with { Reputation=ClassicDevelopmentReputation,
            SceneCredibility=p.SceneCredibility.ToArray(),SeenActs=p.SeenActs.ToArray(),ActRelationships=p.ActRelationships,
            ActPopularity=p.ActPopularity,RelationshipHistory=p.RelationshipHistory,PreviousLayout=p.PreviousLayout,
            ClassicBooking=NewClassicBooking(ClassicDevelopmentReputation,d)};
        return session;
    }
    /// <summary>Explicit all-stage test preset. Books/pay bands now; ordinary site/staff/stock costs are due at Start.</summary>
    public static GameSession CreateClassicDevelopmentTestDefaults(bool guaranteeHundred=false,bool topUpCash=false)
    {
        var s=CreateClassicDevelopmentStart(7,guaranteeHundred,topUpCash);
        void Send(SessionCommand command)
        {
            var result=s.Execute(new(new(s.NextSubmissionSequence+1),s.CampaignId,s.Phase,s.CurrentTick,s.NextSubmissionSequence,null,command));
            if(!result.IsAccepted)throw new InvalidOperationException($"Classic test defaults: {result.Message}");
        }
        var perk=s.CapturePerks()!;
        Send(new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,perk.Hand.FirstOrDefault(id=>id is "doctors-orders" or "extra-pair-of-hands")??perk.Hand[0]));
        var acts=s.GetFestivalActs().Where(a=>s.ActStandingOf(a)!=ActStanding.Locked).OrderBy(s.ActFee).ThenBy(a=>a.Id,StringComparer.Ordinal).Take(9).Select(a=>a.Id).ToArray();
        if(acts.Length!=9)throw new InvalidOperationException("The test market cannot fill all nine sets.");
        for(var stage=0;stage<s.Stages.Count;stage++)Send(new SetProgrammeCommand(acts.Skip(stage*3).Take(3).ToArray()){StageId=s.Stages[stage].Id});
        Send(new ConfirmClassicBookingsCommand());Send(new UseDefaultBuildLayoutCommand());
        foreach(var placement in StandardBuildLayout(2).Where(p=>!s.CaptureBuildPlacements().Any(b=>b.Id==p.Id)))
            Send(new PlaceBuildServiceCommand(placement.Kind,placement.Cell,placement.QuarterTurns));
        var sounds=s.GetStaffCandidates().Where(c=>c.Role==StaffRole.Sound).OrderBy(c=>c.Traits.Length).ThenBy(c=>c.WagePennies).Take(3).ToArray();
        for(var stage=0;stage<3;stage++)Send(new AcceptPreparationOfferCommand(StageSoundOfferId(sounds[stage],stage)));
        foreach(var c in s.GetStaffCandidates().Where(c=>c.Role!=StaffRole.Sound).GroupBy(c=>c.Role).Select(g=>g.OrderBy(c=>c.Traits.Length).ThenBy(c=>c.WagePennies).First()))
            Send(new AcceptPreparationOfferCommand(c.Id));
        Send(new AcceptPreparationOfferCommand("maintenance.worker"));Send(new SetPreparationStockCommand(160,80));
        return s;
    }
    private static string? ValidateClassicDevelopment(SessionPersistenceSnapshot s)
    {
        if(s.ClassicDevelopment is not {} d)return null;
        if(!SavedClassic(s)||d.Version!=1||d.StageMask is <1 or >7||s.CampaignSeed!=20260922||s.CampaignPlanning is not{EditionNumber:>=2}||
            s.ClassicHistory?.CompletedFestivals is not{Length:>=1} history||
            JsonSerializer.Serialize(history[0])!=JsonSerializer.Serialize(RecordedClassicHistory.Value))
            return "Classic development provenance, stage selection or recorded carry is invalid.";
        return null;
    }
}

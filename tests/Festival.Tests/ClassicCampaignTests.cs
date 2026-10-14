using Festival.Simulation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Text.Json;

namespace Festival.Tests;

[TestClass]
public sealed class ClassicCampaignTests
{
    private static void Accept(GameSession s,SessionCommand c)=>BuildSession.Accept(s,c);
    private static void Evidence(string name,object evidence)
    {
        if(Environment.GetEnvironmentVariable("FESTIVAL_CLASSIC_EVIDENCE_ROOT") is not{Length:>0} path)return;
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path,name+".json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
    }
    private static GameSession RoundTrip(GameSession s)
    {
        var loaded=GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(loaded.IsSuccess,loaded.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,loaded.Session!.CaptureSnapshot().AuthoritativeHash);
        return loaded.Session;
    }
    private static void Prepare(GameSession s,bool rentRig=false)
    {
        var perk=s.CapturePerks()!;
        Accept(s,new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,perk.Hand[0]));
        Accept(s,new UseDefaultBuildLayoutCommand());
        var acts=s.GetFestivalActs().Where(a=>s.ActStandingOf(a)==ActStanding.Available).OrderBy(s.ActFee)
            .ThenBy(a=>a.Id,StringComparer.Ordinal).Take(3).Select(a=>a.Id).ToArray();
        Accept(s,new SetProgrammeCommand(acts));
        Accept(s,new SetPreparationStockCommand(24,16));
        foreach(var candidate in s.GetStaffCandidates().GroupBy(c=>c.Role).Select(g=>g.OrderBy(c=>c.Traits.Length>0)
            .ThenBy(c=>c.Grade>0).ThenBy(c=>Math.Abs(c.Grade)).ThenBy(c=>c.Id,StringComparer.Ordinal).First()))
            Accept(s,new AcceptPreparationOfferCommand(candidate.Id));
        if(rentRig)Accept(s,new AcceptPreparationOfferCommand("equipment.rent"));
        Assert.AreEqual(0,s.GetPreparationStartBlockers().Count);
    }
    private static void Finish(GameSession s)
    {
        for(var ticks=0;ticks<60_000&&s.PreparedStatus!=PreparationStatus.Finished;ticks+=800)s.AdvanceWithoutSnapshot(800);
        Assert.AreEqual(PreparationStatus.Finished,s.PreparedStatus);
        Assert.IsTrue(s.CanStartNextFestival);
    }
    private static void CriticalNow(GameSession s,ulong id)
    {
        var tick=s.CurrentTick;
        typeof(GameSession).GetMethod("UpdatePerson",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(s,
            [id,(Func<Person,Person>)(p=>p with {HealthStage=MedicalStage.Critical,Thirst=10_000,Intent=MedicalIntent.Collapsed,
                HealthWarningTick=tick-6_000,HealthCollapseTick=tick-4_800,HealthCriticalTick=tick-1})]);
    }

    [TestMethod]
    public void ClassicHasApprovedMildDefinitionAndStrictIndependentIdentity()
    {
        var s=GameSession.CreateClassicCampaign(20260922);
        Assert.AreEqual(CampaignIdentity.ClassicManagement,s.Identity);
        Assert.AreEqual(WeatherKind.Mild,s.CurrentFestival.Weather);
        Assert.IsNull(s.CurrentFestival.Challenge);
        Assert.AreEqual(1,s.Stages.Count);
        Assert.AreEqual(25,s.CurrentFestival.Attendance);
        Assert.AreEqual(new IncidentTimingParameters(3200,1600,4800,4800,4800,7200,9600),s.IncidentTiming);
        Assert.IsFalse(s.CaptureMedical()!.IsHot);
        Assert.AreEqual(0,s.CaptureWeather()!.Spells.Length);
        Assert.AreEqual(0,s.CaptureClassicHistory()!.CompletedFestivals.Length);
        RoundTrip(s);
        var snapshot=s.CapturePersistenceSnapshot();
        StringAssert.Contains(GameSession.Restore(snapshot with {ClassicHistory=null}).Error!,"Classic");
        StringAssert.Contains(GameSession.Restore(snapshot with {Identity=CampaignIdentity.Management}).Error!,"Classic");
    }

    [TestMethod]
    public void FourPaidFestivalsCarryActualHoldingsRelationshipsAndResetOnlyTheDay()
    {
        var s=GameSession.CreateClassicCampaign(20260922);
        var evidence=new List<object>();
        for(var number=1;number<=4;number++)
        {
            Assert.AreEqual(number,s.FestivalNumber);
            Assert.AreEqual(1,s.CurrentFestival.Tier);
            Assert.AreEqual(PreparationStatus.Preparing,s.PreparedStatus);
            Assert.AreEqual(0L,s.CurrentTick);
            Assert.AreEqual(0,s.PerformanceRecords.Count);
            Assert.AreEqual(0,s.CaptureClassicHistory()!.DeathPenalties.Length);
            Assert.IsNull(s.CaptureLifecycleSnapshot());
            var opening=s.CapturePersistentCampaignState()!;
            var earlier=s.CapturePreparation()!;
            Prepare(s,number==1);
            s=RoundTrip(s);
            Accept(s,new StartPreparedEditionCommand());
            Assert.IsTrue(s.CapturePreparation()!.SetupPayments!.Length>0,"Opening really commits paid setup.");
            s=RoundTrip(s);
            Finish(s);
            s=RoundTrip(s);
            var closed=s.CapturePersistentCampaignState()!;
            var p=s.CapturePreparation()!;var result=p.Result!;
            evidence.Add(new { festival=number,identity=s.Identity,opening,closed,result,standing=new FestivalStanding(p.Reputation,p.SceneCredibility),
                p.SeenActs,p.OwnedEquipment,p.Rentals,p.WorkContracts,relationships=s.RelationshipsAfterFestival,popularity=s.PopularityAfterFestival,
                performanceRecords=s.PerformanceRecords,authoritativeHash=s.CaptureSnapshot().AuthoritativeHash });
            Assert.AreEqual(opening.CashPennies+result.NetCashChangePennies-s.CurrentFestival.TicketReceiptsPennies,closed.CashPennies);
            Assert.AreEqual(opening.Loan,closed.Loan);
            Assert.AreEqual(25,result.AdmittedGuests);
            Assert.AreEqual(result.Deaths,s.CaptureClassicHistory()!.DeathPenalties.Length);
            Assert.AreEqual(0,p.WorkContracts.Length);Assert.AreEqual(0,p.Rentals.Length);
            if(number==4)break;
            var relationships=s.RelationshipsAfterFestival;
            var popularity=s.PopularityAfterFestival;
            var next=s.CreateNextFestival();var carried=next.CapturePreparation()!;
            Assert.AreEqual(s.CampaignId,next.CampaignId);
            Assert.AreEqual(s.CampaignSeed,next.CampaignSeed);
            Assert.AreEqual(closed.CashPennies+25000,next.CapturePersistentCampaignState()!.CashPennies);
            Assert.AreEqual(closed.Loan,next.CapturePersistentCampaignState()!.Loan);
            Assert.AreEqual(p.Reputation,carried.Reputation);
            CollectionAssert.AreEqual(p.SceneCredibility,carried.SceneCredibility);
            CollectionAssert.AreEqual(p.SeenActs,carried.SeenActs);
            CollectionAssert.AreEqual(p.OwnedEquipment,carried.OwnedEquipment);
            CollectionAssert.AreEqual(relationships??[],carried.ActRelationships??[]);
            CollectionAssert.AreEqual(popularity??[],carried.ActPopularity??[]);
            Assert.AreEqual(0,carried.AcceptedOffers.Length);Assert.AreEqual(0,carried.Payments.Length);
            Assert.AreEqual(0,carried.BuildPlacements.Length);Assert.AreEqual(0,carried.Plan!.OfferIds.Length);
            Assert.IsTrue(carried.PreviousLayout!.Length>0);
            Assert.AreEqual(number*3,carried.RelationshipHistory!.Length);
            Assert.AreEqual(number,next.CaptureClassicHistory()!.CompletedFestivals.Length);
            var snapshot=next.CapturePersistenceSnapshot();
            var malformed=GameSession.Restore(snapshot with{Preparation=snapshot.Preparation! with{
                CarriedIn=snapshot.Preparation.CarriedIn! with{OwnedEquipment=null!}}});
            Assert.IsFalse(malformed.IsSuccess);StringAssert.Contains(malformed.Error!,"Classic");
            s=RoundTrip(next);
        }
        Evidence("four-paid-festivals",evidence);
    }

    [TestMethod]
    public void RepeatedGuestAndStaffDeathsContinueOnceCleanlyThroughResultsAndCarry()
    {
        var s=GameSession.CreateClassicCampaign(20260922);Prepare(s);Accept(s,new StartPreparedEditionCommand());
        s.AdvanceWithoutSnapshot(8_000);
        var victims=s.CapturePreparation()!.People.Where(p=>p.Admitted&&!p.Departed&&p.Role==ProtectedPersonRole.Guest).Take(2)
            .Concat(s.CapturePreparation()!.People.Where(p=>p.Admitted&&!p.Departed&&p.Role==ProtectedPersonRole.Performer).Take(1))
            .Concat(s.CapturePreparation()!.People.Where(p=>p.Admitted&&!p.Departed&&p.Role==ProtectedPersonRole.Staff).Take(1)).ToArray();
        Assert.AreEqual(4,victims.Length);
        foreach(var victim in victims)
        {
            var cash=s.CapturePersistentCampaignState()!.CashPennies;
            var before=s.CapturePreparation()!.Reputation;
            CriticalNow(s,victim.AgentId);s.AdvanceWithoutSnapshot(1);
            // Staff use their existing intoxication/injury routes, not the guest/performer heat loop.
            // Exercise the shared fatal entry for this staff cleanup fixture without changing that route.
            if(victim.Role==ProtectedPersonRole.Staff)
                typeof(GameSession).GetMethod("ApplyMedicalDeath",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(s,[victim.AgentId,s.CurrentTick-6000,s.CurrentTick-4800,s.CurrentTick-1]);
            Assert.AreEqual(PreparationStatus.Running,s.PreparedStatus);
            var history=s.CaptureClassicHistory()!;
            Assert.AreEqual(Math.Max(0,before-10),s.CapturePreparation()!.Reputation);
            Assert.AreEqual(cash,s.CapturePersistentCampaignState()!.CashPennies,"Death imposes no fine.");
            Assert.IsTrue(s.IsClassicDeceased(victim.AgentId));
            Assert.IsTrue(s.CapturePreparation()!.People.Single(p=>p.AgentId==victim.AgentId).Departed);
            var hash=s.CaptureSnapshot().AuthoritativeHash;
            typeof(GameSession).GetMethod("ApplyMedicalDeath",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(s,[victim.AgentId,0L,0L,0L]);
            Assert.AreEqual(hash,s.CaptureSnapshot().AuthoritativeHash,"Repeated reporting has no event, penalty or state effect.");
            Assert.AreEqual(0,s.CaptureLifecycleSnapshot()!.Hearings.Count);
            s=RoundTrip(s);
        }
        Finish(s);s=RoundTrip(s);
        Assert.AreEqual(4,s.CompletedFestivalResult!.Deaths);
        Assert.AreEqual(23,s.CompletedFestivalResult.GuestCount);
        Assert.AreEqual(25,s.CompletedFestivalResult.AdmittedGuests);
        var next=RoundTrip(s.CreateNextFestival());
        Assert.AreEqual(4,next.CaptureClassicHistory()!.CompletedFestivals.Single().DeathPenalties.Length);
        Assert.AreEqual(0,next.CaptureClassicHistory()!.DeathPenalties.Length);
        Evidence("repeated-guest-performer-staff-deaths",new{fixture="Injected overdue critical tracks and staff fatal entry; no cash/reputation edits",closed=s.CapturePersistenceSnapshot(),next=next.CapturePersistenceSnapshot()});
    }

    [TestMethod]
    public void EarnedReputationFallsOnceToZeroAndAnAllDeceasedCrowdStillCloses()
    {
        var s=GameSession.CreateClassicCampaign(20260922);Prepare(s);Accept(s,new StartPreparedEditionCommand());Finish(s);
        Assert.IsTrue(s.CapturePreparation()!.Reputation>=10,"An ordinary paid festival earns the standing for this penalty test.");
        s=RoundTrip(s.CreateNextFestival());Prepare(s);Accept(s,new StartPreparedEditionCommand());s.AdvanceWithoutSnapshot(8_000);
        var guests=s.CapturePreparation()!.People.Where(p=>p.Role==ProtectedPersonRole.Guest).ToArray();
        foreach(var guest in guests)
        {
            var before=s.CapturePreparation()!.Reputation;
            CriticalNow(s,guest.AgentId);s.AdvanceWithoutSnapshot(1);
            Assert.AreEqual(Math.Max(0,before-10),s.CapturePreparation()!.Reputation);
            Assert.IsTrue(s.IsClassicDeceased(guest.AgentId));
        }
        s=RoundTrip(s);Finish(s);s=RoundTrip(s);
        Assert.AreEqual(25,s.CompletedFestivalResult!.Deaths);
        Assert.AreEqual(0,s.CompletedFestivalResult.GuestCount);
        Assert.IsNull(s.CompletedFestivalResult.Stars);
        Assert.AreEqual(0,s.CapturePreparation()!.Reputation);
        s=RoundTrip(s.CreateNextFestival());Assert.AreEqual(3,s.FestivalNumber);
    }

    [TestMethod]
    [DataRow("equipment")]
    [DataRow("disorder")]
    public void EachFatalCauseRecordsItsActualEvidenceAndContinues(string kind)
    {
        var s=GameSession.CreateClassicCampaign(20260922);Prepare(s);Accept(s,new StartPreparedEditionCommand());s.AdvanceWithoutSnapshot(12_000);
        var id=s.CapturePreparation()!.People.First(p=>p.Role==ProtectedPersonRole.Guest&&p.Admitted&&!p.Departed).AgentId;
        var victim=s.CapturePerson(id)!;
        if(kind=="equipment")
        {
            var e=s.CaptureEquipment()! with {Stage=EquipmentStage.DangerousFault,WarningTick=s.CurrentTick-9_600};
            typeof(GameSession).GetField("_equipment",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,e);
            typeof(GameSession).GetMethod("EquipmentDeath",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(s,[e,victim]);
        }
        else
        {
            var d=s.CaptureDisorder()!;var opponent=d.People.First(p=>p.AgentId!=id).AgentId;var injury=s.CurrentTick-4_800;
            typeof(GameSession).GetProperty("DisorderView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,d with {
                People=d.People.Select(p=>p.AgentId!=id?p:p with {Pressure=8_000,Grievance=DisorderGrievance.MusicCutoff,
                    Stage=DisorderStage.Injured,GrievanceTick=injury-1_600,StageTick=injury,InjuryTick=injury,OpponentId=opponent}).ToArray(),
                Incidents=d.Incidents.Append(new(id,opponent,DisorderGrievance.MusicCutoff,8_000,injury-1_600,injury-800,injury,id)).ToArray()});
            CriticalNow(s,id);
            typeof(GameSession).GetMethod("ApplyDisorderDeath",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(s,[id]);
        }
        Assert.AreEqual(PreparationStatus.Running,s.PreparedStatus);
        Assert.AreEqual(kind,s.CaptureClassicHistory()!.DeathPenalties.Single().Kind);
        s=RoundTrip(s);s.AdvanceWithoutSnapshot(800);
        Assert.AreEqual(1,s.CaptureClassicHistory()!.DeathPenalties.Length);
        Finish(s);s=RoundTrip(s);Assert.AreEqual(1,s.CompletedFestivalResult!.Deaths);
        RoundTrip(s.CreateNextFestival());
        Evidence(kind+"-fatal-cause",new{fixture="Injected causal incident boundary; ordinary paid start",state=s.CapturePersistenceSnapshot()});
    }

    [TestMethod]
    public void SaveRejectsMissingConsequencesInventedReputationAndDeadNavigation()
    {
        var s=GameSession.CreateClassicCampaign(20260922);Prepare(s);Accept(s,new StartPreparedEditionCommand());s.AdvanceWithoutSnapshot(8_000);
        var id=s.CapturePreparation()!.People.First(p=>p.Role==ProtectedPersonRole.Guest&&p.Admitted).AgentId;
        CriticalNow(s,id);s.AdvanceWithoutSnapshot(1);s=RoundTrip(s);
        var snapshot=s.CapturePersistenceSnapshot();var history=snapshot.ClassicHistory!;
        void Reject(SessionPersistenceSnapshot altered,string word)
        {var loaded=GameSession.Restore(altered);Assert.IsFalse(loaded.IsSuccess);StringAssert.Contains(loaded.Error!,word);}
        Reject(snapshot with {ClassicHistory=history with {DeathPenalties=[]}},"Classic");
        Reject(snapshot with {Preparation=snapshot.Preparation! with {Reputation=1}},"Classic");
        Reject(snapshot with {NavigationAgents=snapshot.NavigationAgents!.Select(n=>n.Id!=id?n:n with {IntentId="invented-dead-route"}).ToArray()},"Classic");
        Reject(snapshot with {ClassicHistory=history with {DeathPenalties=[history.DeathPenalties[0] with {Kind="equipment"}]}},"Classic");
        Reject(snapshot with {Medical=snapshot.Medical! with {Fatal=false}},"Classic");
        Reject(snapshot with {ClassicHistory=history with {DeathPenalties=[history.DeathPenalties[0] with {Casualty=null!}]}},"Classic");
        Reject(snapshot with {PerformanceRecords=[null!]},"Classic");
        Reject(snapshot with {LivePerformances=snapshot.LivePerformances!.Select(l=>l is null?null:l with {Performers=null!}).ToArray()},"Classic");
        Reject(snapshot with {LivePerformances=snapshot.LivePerformances!.Select(l=>l is null?null:l with {Performers=[null!]}).ToArray()},"Classic");
        Reject(snapshot with {Equipment=snapshot.Equipment! with {Evidence=[null!]}},"Classic");
    }

    [TestMethod]
    [DataRow("medic")]
    [DataRow("steward")]
    [DataRow("sound")]
    [DataRow("maintenance")]
    public void DeceasedWorkersReleaseTheirRoleAndNeverResumeTheirDutyRoute(string role)
    {
        var s=GameSession.CreateClassicCampaign(20260922);Prepare(s);
        if(role=="maintenance")Accept(s,new AcceptPreparationOfferCommand("maintenance.worker"));
        Accept(s,new StartPreparedEditionCommand());s.AdvanceWithoutSnapshot(8_000);
        var id=role switch{
            "medic"=>s.CaptureMedical()!.MedicId,
            "steward"=>s.CaptureDisorder()!.SecurityId,
            "maintenance"=>s.CaptureEquipment()!.WorkerId!.Value,
            _=>s.CapturePreparation()!.People.First(p=>p.Role==ProtectedPersonRole.Staff&&
                p.AgentId!=s.CaptureMedical()!.MedicId&&p.AgentId!=s.CaptureDisorder()!.SecurityId).AgentId};
        CriticalNow(s,id);
        typeof(GameSession).GetMethod("ApplyMedicalDeath",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(s,[id,s.CurrentTick-6000,s.CurrentTick-4800,s.CurrentTick-1]);
        s=RoundTrip(s);s.AdvanceWithoutSnapshot(800);s=RoundTrip(s);
        var nav=s.CaptureObservation().NavigationAgents.Single(a=>a.Id.Value==id);
        Assert.AreEqual(AgentNavigationAction.Idle,nav.Action);
        Assert.IsTrue(s.IsClassicDeceased(id));
        if(role=="maintenance")
        {
            var hash=s.CaptureSnapshot().AuthoritativeHash;
            Assert.IsFalse(BuildSession.Send(s,new EquipmentCommand(EquipmentAction.DispatchMaintenance)).IsAccepted);
            Assert.AreEqual(hash,s.CaptureSnapshot().AuthoritativeHash,"Rejected dead-worker dispatch must not create a job or route.");
            s=RoundTrip(s);
        }
        Finish(s);s=RoundTrip(s);
        Assert.AreEqual(1,s.CompletedFestivalResult!.Deaths);
    }
}

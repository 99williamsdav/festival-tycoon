using Festival.Simulation;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Festival.Tests;

// Boundary fixtures isolate existing equipment/disorder fatal consequences before their extraction.
// They deliberately inject a due fault/injury and invoke the existing consequence body, not a normal-day claim.
[TestClass]
public sealed class ClassicSurvivalIncidentParityTests
{
    private static readonly JsonArray BaselineSaveFailures=[];
    private static void Invoke(GameSession session,string method,params object[] args)=>typeof(GameSession).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(session,args);
    private static string Root
    {get {var folder=new DirectoryInfo(AppContext.BaseDirectory);while(folder is not null&&!File.Exists(Path.Combine(folder.FullName,"docs/WORKING-AGREEMENTS.md")))folder=folder.Parent;return folder!.FullName;}}
    private static JsonObject Observe(GameSession session,string name,IReadOnlyList<SessionEvent> events)
    {
        var saved=session.CapturePersistenceSnapshot();var restored=GameSession.Restore(saved);
        Assert.IsTrue(restored.IsSuccess,restored.Error);Assert.AreEqual(saved.AuthoritativeHash,restored.Session!.CapturePersistenceSnapshot().AuthoritativeHash);
        var node=JsonSerializer.SerializeToNode(saved)!.AsObject();// Preserve every current field, including identity and authoritative hash.
        return new(){["name"]=name,["tick"]=session.CurrentTick,["digest"]=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString()))),
            ["events"]=JsonSerializer.SerializeToNode(events),["randomStreams"]=JsonSerializer.SerializeToNode(saved.RandomStreams),["lifecycle"]=JsonSerializer.SerializeToNode(saved.Lifecycle),["status"]=session.PreparedStatus!.Value.ToString()};
    }
    private static GameSession Started(int tier,int control)
    {
        const ulong seed=20260922;
        if(control==2)return GameSession.CreateStewardShift(seed,tier);
        var session=control==0?GameSession.CreateDevelopmentFestival(seed,tier):tier==1?GameSession.CreateHandsOnCampaign(seed):
            (GameSession)typeof(GameSession).GetMethod("CreateLaterFestivalIn",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[CampaignMode.HandsOn,seed,new CampaignId(seed),tier,GameSession.DevelopmentCarryOver(tier),FestivalStanding.Established,Array.Empty<string>(),null,false])!;
        NextFestivalTests.Ready(session);BuildSession.Accept(session,new StartPreparedEditionCommand());return session;
    }
    [TestMethod]
    public void SurvivalFatalConsequencesAndHashesRemainExactlyAsBeforeClassic()
    {
        var actual=new JsonArray();BaselineSaveFailures.Clear();
        foreach(var tier in new[]{1,2})foreach(var control in new[]{0,1,2})foreach(var kind in new[]{"equipment","disorder"})
        {
            var session=Started(tier,control);var events=session.AdvanceWithoutSnapshot(8000);
            Assert.AreEqual(PreparationStatus.Running,session.PreparedStatus);
            var guestId=session.CapturePreparation()!.People.First(p=>p.Role==ProtectedPersonRole.Guest&&p.Admitted&&!p.Departed).AgentId;
            var guest=session.CapturePerson(guestId)!;
            if(kind=="equipment")
            {
                var equipment=session.CaptureEquipment()! with{Stage=EquipmentStage.DangerousFault,WarningTick=session.CurrentTick-GameSession.EquipmentDeathDelayTicks};
                typeof(GameSession).GetField("_equipment",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(session,equipment);
                Invoke(session,"EquipmentDeath",equipment,guest);
            }
            else
            {
                var disorder=session.CaptureDisorder()!;var opponent=disorder.People.First(p=>p.AgentId!=guest.Id).AgentId;var injury=session.CurrentTick-GameSession.DisorderInjuryDeathTicks;
                typeof(GameSession).GetProperty("DisorderView",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(session,disorder with{
                    People=disorder.People.Select(p=>p.AgentId!=guest.Id?p:p with{Pressure=8000,Grievance=DisorderGrievance.MusicCutoff,Stage=DisorderStage.Injured,GrievanceTick=injury-1600,StageTick=injury,InjuryTick=injury,OpponentId=opponent}).ToArray(),
                    Incidents=disorder.Incidents.Append(new(guest.Id,opponent,DisorderGrievance.MusicCutoff,8000,injury-1600,injury-800,injury,guest.Id)).ToArray()});
                Invoke(session,"UpdatePerson",guest.Id,(Func<Person,Person>)(p=>p with{HealthStage=MedicalStage.Critical,Intent=MedicalIntent.Collapsed,HealthWarningTick=injury,HealthCollapseTick=injury,HealthCriticalTick=injury+GameSession.MedicalCriticalDelayTicks}));
                Invoke(session,"ApplyDisorderDeath",guest.Id);
            }
            Invoke(session,"ReleaseFrozenStaffClaims");
            var name=$"{kind}-tier{tier}-control{control}";actual.Add(Observe(session,name+"-death",events));
            // The normal Favour command retries the same fixture without bypassing restore validation.
            BuildSession.Accept(session,new SpendCouncilFavourCommand());actual.Add(Observe(session,name+"-retry",[]));
        }
        var medical=Started(2,0);var medicalEvents=new List<SessionEvent>(medical.AdvanceWithoutSnapshot(8000));
        var patient=medical.CapturePreparation()!.People.First(p=>p.Role==ProtectedPersonRole.Performer&&p.Admitted);var tick=medical.CurrentTick;
        Invoke(medical,"UpdatePerson",patient.AgentId,(Func<Person,Person>)(p=>p with{HealthStage=MedicalStage.Critical,Thirst=10000,HeatExposure=10000,Intent=MedicalIntent.Collapsed,HealthWarningTick=tick-4000,HealthCollapseTick=tick-GameSession.MedicalDeathDelayTicks,HealthCriticalTick=tick-1}));
        medicalEvents.AddRange(medical.AdvanceWithoutSnapshot(1));Assert.AreEqual(PreparationStatus.Failed,medical.PreparedStatus);
        actual.Add(Observe(medical,"natural-tick-rain-medical-death",medicalEvents));
        var path=Path.Combine(Root,"reports/evidence/classic-loop/survival-before/incident-consequence-baseline.json");
        if(Environment.GetEnvironmentVariable("FESTIVAL_RECORD_CLASSIC_SURVIVAL_INCIDENTS")=="6fed0e2504f9166ad212755d75827d2fae3a928c")
        {Assert.IsFalse(File.Exists(path),"Immutable pre-policy reference: never repin failures.");File.WriteAllText(path,actual.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));File.WriteAllText(Path.Combine(Root,"reports/evidence/classic-loop/survival-before/incident-save-limitation.json"),BaselineSaveFailures.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));return;}
        Assert.AreEqual(JsonNode.Parse(File.ReadAllText(path))!.ToJsonString(),actual.ToJsonString(),"Fatal consequence state/events/RNG changed; do not repin.");
    }
}

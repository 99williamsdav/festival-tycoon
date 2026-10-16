using Festival.Simulation;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Festival.Tests;

// Recorded BEFORE the architectural refactor at 4b404379f04154240c59543daeea5d3d49fea2a3.
// All existing authoritative fields are compared, including navigation, events, finances and RNG.
// Only the old/new identity representation and authoritative hash are excluded; never re-pin on failure.
[TestClass]
public sealed class SurvivalArchitectureParityTests
{
    private const ulong Seed=20260922;
    private static readonly Dictionary<GameSession,List<SessionEvent>> Events=[];
    private static readonly JsonArray EventObservations=[],ExtraObservations=[],RestoreFailures=[];
    private static void Step(GameSession session,int count)
    {if(!Events.TryGetValue(session,out var events))Events[session]=events=[];events.AddRange(session.AdvanceWithoutSnapshot(count));}
    private static string Root
    {
        get { var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"docs/WORKING-AGREEMENTS.md")))directory=directory.Parent;return directory!.FullName; }
    }
    private static void Send(GameSession session,SessionCommand command)=>BuildSession.Accept(session,command);
    private static GameSession Ready(GameSession session)
    {
        Send(session,new AcknowledgeFestivalChallengeCommand(session.CapturePreparation()!.Tier,1));
        var perk=session.CapturePerks()!;Send(session,new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,perk.Hand[0]));
        Send(session,new UseDefaultBuildLayoutCommand());
        var acts=session.GetFestivalActs().Where(a=>session.ActStandingOf(a)==ActStanding.Available).OrderBy(session.ActFee).ThenBy(a=>a.Id,StringComparer.Ordinal).Select(a=>a.Id).ToArray();
        Send(session,new SetProgrammeCommand(acts[..3]));
        if(session.SecondStageOpen)Send(session,new SetProgrammeCommand(acts[3..6]){StageId=session.Stages[1].Id});
        Send(session,new SetPreparationStockCommand(40,32));
        var crew=session.GetStaffCandidates().GroupBy(c=>c.Role).Select(g=>g.OrderBy(c=>c.Traits.Length>0).ThenBy(c=>c.Grade>0).ThenBy(c=>Math.Abs(c.Grade)).ThenBy(c=>c.Id,StringComparer.Ordinal).First()).ToArray();
        foreach(var person in crew)Send(session,new AcceptPreparationOfferCommand(person.Id));
        if(session.SecondStageOpen){var extra=session.GetStaffCandidates().Where(c=>c.Role==StaffRole.Sound&&!crew.Any(p=>p.Id==c.Id)).OrderBy(c=>c.Traits.Length>0).ThenBy(c=>c.Grade>0).ThenBy(c=>Math.Abs(c.Grade)).ThenBy(c=>c.Id,StringComparer.Ordinal).First();Send(session,new AcceptPreparationOfferCommand(extra.ExtraOfferId));}
        return session;
    }
    private static string Digest(JsonNode? node)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node?.ToJsonString()??"null")));
    private static JsonObject Observe(GameSession session,string name)
    {
        var snapshot=session.CapturePersistenceSnapshot();
        var state=JsonSerializer.SerializeToNode(snapshot)!.AsObject();
        state.Remove("AuthoritativeHash");state.Remove("Mode");state.Remove("Identity");
        var restored=GameSession.Restore(snapshot);
        Assert.IsTrue(restored.IsSuccess,restored.Error);Assert.AreEqual(snapshot.AuthoritativeHash,restored.Session!.CapturePersistenceSnapshot().AuthoritativeHash);
        EventObservations.Add(new JsonObject{["name"]=name,["events"]=JsonSerializer.SerializeToNode(Events.GetValueOrDefault(session)??[])});
        return new JsonObject{["name"]=name,["tick"]=session.CurrentTick,["behaviorDigest"]=Digest(state),
            ["fields"]=new JsonObject(state.Select(pair=>KeyValuePair.Create(pair.Key,(JsonNode?)JsonValue.Create(Digest(pair.Value))))),
            ["randomStreams"]=JsonSerializer.SerializeToNode(snapshot.RandomStreams),["status"]=session.PreparedStatus?.ToString(),
            ["cash"]=snapshot.FestivalFinances.Sum(f=>f.CashPennies),["canProgress"]=session.CanStartNextFestival,
            ["result"]=JsonSerializer.SerializeToNode(session.CompletedFestivalResult),["accounts"]=JsonSerializer.SerializeToNode(session.CompletedFestivalAccounts)};
    }
    private static void RunTo(GameSession session,int tick)
    {while(session.CurrentTick<tick&&session.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing)Step(session,Math.Min(80,tick-(int)session.CurrentTick));}
    private static void RunToEnd(GameSession session)
    {for(var i=0;i<80&&session.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing;i++)Step(session,1000);}
    private static void Fatal(GameSession session)
    {
        RunTo(session,6000);
        var victim=session.CapturePreparation()!.People.First(p=>p.Role==ProtectedPersonRole.Performer&&session.CapturePerson(p.AgentId)!.Admitted);
        var tick=session.CurrentTick;
        typeof(GameSession).GetMethod("UpdatePerson",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(session,[victim.AgentId,(Func<Person,Person>)(p=>p with{HealthStage=MedicalStage.Critical,Thirst=10000,HeatExposure=10000,Intent=MedicalIntent.Collapsed,HealthWarningTick=tick-4000,HealthCollapseTick=tick-GameSession.MedicalDeathDelayTicks,HealthCriticalTick=tick-1})]);
        for(var i=0;i<6000&&session.PreparedStatus==PreparationStatus.Running;i++)Step(session,1);
        Assert.AreEqual(PreparationStatus.Failed,session.PreparedStatus);
    }
    private static JsonArray Capture()
    {
        var observations=new JsonArray();Events.Clear();EventObservations.Clear();ExtraObservations.Clear();RestoreFailures.Clear();
        foreach(var handsOn in new[]{false,true})
        {
            var label=handsOn?"hands-on":"management";
            var session=Ready(handsOn?GameSession.CreateHandsOnCampaign(Seed):GameSession.CreateBuildCampaign(Seed));
            observations.Add(Observe(session,label+"-prepared"));Send(session,new StartPreparedEditionCommand());
            if(handsOn)Send(session,new SetPlayerStewardControlCommand(false));
            RunTo(session,8000);observations.Add(Observe(session,label+"-live-8000"));
            Send(session,new SetPausedCommand(true));Step(session,80);observations.Add(Observe(session,label+"-paused"));Send(session,new SetPausedCommand(false));
            if(handsOn){Send(session,new SetPlayerMoveCommand(0,-1000,true,180));Step(session,80);Send(session,new SetPlayerMoveCommand(0,0,false,180));observations.Add(Observe(session,label+"-manual-move"));}
            RunToEnd(session);observations.Add(Observe(session,label+"-closed"));
            Assert.IsTrue(session.CanStartNextFestival,label+" reference must complete normally");
            var next=Ready(session.CreateNextFestival());observations.Add(Observe(next,label+"-canonical-tier-two"));Send(next,new StartPreparedEditionCommand());
            if(handsOn)Send(next,new SetPlayerStewardControlCommand(false));
            RunTo(next,21000);observations.Add(Observe(next,label+"-tier-two-live"));
            Send(next,new SetPausedCommand(true));Step(next,80);ExtraObservations.Add(Observe(next,label+"-tier-two-paused"));Send(next,new SetPausedCommand(false));
            if(handsOn){Send(next,new SetPlayerMoveCommand(0,-1000,true,180));Step(next,80);Send(next,new SetPlayerMoveCommand(0,0,false,180));ExtraObservations.Add(Observe(next,label+"-tier-two-manual"));}
            RunToEnd(next);ExtraObservations.Add(Observe(next,label+"-tier-two-closed"));Assert.IsFalse(next.CanStartNextFestival);
        }
        foreach(var tier in new[]{1,2})
        {
            var session=GameSession.CreateStewardShift(Seed,tier);observations.Add(Observe(session,$"steward-{tier}-start"));
            Send(session,new SetPlayerMoveCommand(0,-1000,true,180));Step(session,80);Send(session,new SetPlayerMoveCommand(0,0,false,180));
            RunTo(session,8000);observations.Add(Observe(session,$"steward-{tier}-live"));
            Send(session,new SetPausedCommand(true));Step(session,80);ExtraObservations.Add(Observe(session,$"steward-{tier}-paused"));Send(session,new SetPausedCommand(false));
            RunToEnd(session);ExtraObservations.Add(Observe(session,$"steward-{tier}-closed"));Assert.IsFalse(session.CanStartNextFestival);
            var fatal=GameSession.CreateStewardShift(Seed,tier);Fatal(fatal);ExtraObservations.Add(Observe(fatal,$"steward-{tier}-death-hearing"));Send(fatal,new SpendCouncilFavourCommand());ExtraObservations.Add(Observe(fatal,$"steward-{tier}-retry"));
        }
        foreach(var handsOn in new[]{false,true})
        {
            var label=handsOn?"hands-on":"management";
            var session=Ready(handsOn?GameSession.CreateHandsOnCampaign(Seed):GameSession.CreateBuildCampaign(Seed));Send(session,new StartPreparedEditionCommand());RunTo(session,6000);
            Fatal(session);
            Assert.AreEqual(PreparationStatus.Failed,session.PreparedStatus);observations.Add(Observe(session,label+"-death-hearing"));
            Send(session,new SpendCouncilFavourCommand());observations.Add(Observe(session,label+"-council-retry"));
            var two=handsOn?(GameSession)typeof(GameSession).GetMethod("CreateLaterFestivalIn",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[CampaignMode.HandsOn,Seed,new CampaignId(Seed),2,GameSession.DevelopmentCarryOver(2),FestivalStanding.Established,Array.Empty<string>(),null,false])!:GameSession.CreateDevelopmentFestival(Seed,2);
            Ready(two);Send(two,new StartPreparedEditionCommand());Fatal(two);ExtraObservations.Add(Observe(two,label+"-tier-two-death-hearing"));Send(two,new SpendCouncilFavourCommand());ExtraObservations.Add(Observe(two,label+"-tier-two-retry"));
        }
        return observations;
    }
    [TestCategory("Slow")]
    [TestMethod]
    public void ExistingSurvivalBehaviorMatchesThePreRefactorReference()
    {
        var actual=Capture();var path=Path.Combine(Root,"reports/evidence/survival-architecture/baseline.json");
        if(Environment.GetEnvironmentVariable("FESTIVAL_RECORD_ARCHITECTURE_BASELINE")=="4b404379f04154240c59543daeea5d3d49fea2a3")
        {
            Assert.IsFalse(File.Exists(path),"A pre-refactor baseline is immutable; do not repin a mismatch.");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,actual.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));return;
        }
        var expected=JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        Assert.AreEqual(expected.Count,actual.Count);
        for(var i=0;i<expected.Count;i++)Assert.AreEqual(expected[i]!.ToJsonString(),actual[i]!.ToJsonString(),$"Behavior/RNG/event parity mismatch at {actual[i]!["name"]}. Baseline must not be changed.");
        var companion=new JsonObject{["events"]=EventObservations.DeepClone(),["extraState"]=ExtraObservations.DeepClone()};
        var companionPath=Path.Combine(Root,"reports/evidence/survival-architecture/baseline-companion.json");
        if(Environment.GetEnvironmentVariable("FESTIVAL_RECORD_ARCHITECTURE_EVENTS")=="4b404379f04154240c59543daeea5d3d49fea2a3")
        {Assert.IsFalse(File.Exists(companionPath),"Append-only pre-refactor companion; do not repin.");File.WriteAllText(companionPath,companion.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));File.WriteAllText(Path.Combine(Root,"reports/evidence/survival-architecture/baseline-save-limitation.json"),RestoreFailures.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));return;}
        Assert.AreEqual(JsonNode.Parse(File.ReadAllText(companionPath))!.ToJsonString(),companion.ToJsonString(),"Pre-refactor emitted-event order or extended state parity differs; do not repin.");
    }
}

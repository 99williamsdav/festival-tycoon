using Festival.Simulation;
using Festival.Persistence;
using System.Reflection;
using System.Text.Json;

namespace Festival.Tests;

[TestClass]
public sealed class PerkTests
{
    private static CommandResult Send(GameSession s,SessionCommand c)=>s.Execute(new(new CommandId(s.NextSubmissionSequence+1),s.CampaignId,s.Phase,s.CurrentTick,s.NextSubmissionSequence,null,c));
    private static void Accept(GameSession s,SessionCommand c){var r=Send(s,c);Assert.IsTrue(r.IsAccepted,r.Message);}
    private static GameSession Restored(GameSession s){var r=GameSession.Restore(s.CapturePersistenceSnapshot());Assert.IsTrue(r.IsSuccess,r.Error);Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,r.Session!.CaptureSnapshot().AuthoritativeHash);return r.Session;}
    private static void FixturePerks(GameSession s,params string[] equipped)
    {
        var p=s.CapturePerks()!;
        var field=typeof(GameSession).GetField("_perks",BindingFlags.NonPublic|BindingFlags.Instance)!;
        if(equipped.Length==0){field.SetValue(s,p with {Equipped=[],Pending=false,Hand=[]});typeof(GameSession).GetMethod("SynchronizePerkEffects",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(s,[]);return;}
        var chosen=equipped[^1];var starting=equipped.Where(id=>id!=chosen).Order(StringComparer.Ordinal).ToArray();
        field.SetValue(s,p with {Equipped=starting,Pending=true,Hand=[]});
        do{typeof(GameSession).GetMethod("OpenPerkDraft",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(s,[]);p=s.CapturePerks()!;}while(!p.Hand.Contains(chosen));
        Accept(s,new ChoosePerkCommand(p.DraftAttempt,p.Cursor,chosen));
    }
    private static void FixtureDraft(GameSession s)
    {
        typeof(GameSession).GetMethod("OpenPerkDraft",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(s,[]);
    }
    private static void Start(GameSession s)
    {
        Accept(s,new UseDefaultBuildLayoutCommand());
        Accept(s,new SetProgrammeCommand(["act.meadow-lanterns","act.barnstorm-circuit","act.field-frequency"]));
        Accept(s,new AcceptPreparationOfferCommand("staff.steward"));
        Accept(s,new StartPreparedEditionCommand());
    }
    private static void IgnoredResponseFatalFixture(GameSession s)
    {
        // Explicit headless fixture: inherited alcohol warning/collapse/death chain,
        // with normal deadlines and no medic response. No normal gameplay control.
        var prep=s.CapturePreparation()!;
        typeof(GameSession).GetProperty("PreparationView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,prep with {People=prep.People.Select(p=>p with {Admitted=true}).ToArray()});
        var immersion=s.CaptureImmersion()!;var id=prep.People.First(p=>p.Role==ProtectedPersonRole.Guest).AgentId;
        typeof(GameSession).GetProperty("ImmersionView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,immersion with {People=immersion.People.Select(p=>p.AgentId==id?p with {Intoxication=10000}:p).ToArray()});
        s.AdvanceWithoutSnapshot(4000);Assert.AreEqual(PreparationStatus.Failed,s.PreparedStatus);
    }
    [TestMethod]
    public void ThreeDistinctUniformChoicesUseSeparateSavedEntropyAndOneReroll()
    {
        var counts=PerkCatalogue.All.ToDictionary(p=>p.Id,p=>0);
        for(ulong seed=0;seed<800;seed++)
        {
            var s=GameSession.CreateBuildCampaign(seed);var p=s.CapturePerks()!;
            Assert.AreEqual(3,p.Hand.Distinct().Count());foreach(var id in p.Hand)counts[id]++;
            var legacy=GameSession.CreateBuildCampaign(seed);
            Assert.AreEqual(JsonSerializer.Serialize(legacy.CapturePersistenceSnapshot().RandomStreams),JsonSerializer.Serialize(s.CapturePersistenceSnapshot().RandomStreams));
            if(seed<10){s=Restored(s);Assert.AreEqual(JsonSerializer.Serialize(p),JsonSerializer.Serialize(s.CapturePerks()));Accept(s,new RerollPerksCommand(p.DraftAttempt,p.Cursor));var rerolled=s.CapturePerks()!;s=Restored(s);Assert.AreEqual(JsonSerializer.Serialize(rerolled),JsonSerializer.Serialize(s.CapturePerks()));Assert.IsFalse(Send(s,new RerollPerksCommand(rerolled.DraftAttempt,rerolled.Cursor)).IsAccepted);}
        }
        foreach(var count in counts.Values)Assert.IsTrue(count is >230 and <380,$"Distribution {count}/2400 outside broad equal-weight diagnostic.");
    }
    [TestMethod]
    public void FullHandExcludesOwnedAndAllowsRepeatedRemainingThreeReplacementAndSkip()
    {
        var s=GameSession.CreateBuildCampaign(123);FixturePerks(s,PerkCatalogue.All.Take(5).Select(p=>p.Id).ToArray());FixtureDraft(s);
        var p=s.CapturePerks()!;Assert.IsFalse(p.Hand.Any(p.Equipped.Contains));
        var before=p.Hand.Order().ToArray();Accept(s,new RerollPerksCommand(1,p.Cursor));p=s.CapturePerks()!;CollectionAssert.AreEqual(before,p.Hand.Order().ToArray());
        var original=Restored(s);var lost=p.Equipped[0];Accept(s,new ChoosePerkCommand(1,p.Cursor,p.Hand[0],lost));Assert.AreEqual(5,s.CapturePerks()!.Equipped.Length);Assert.IsFalse(s.CapturePerks()!.Equipped.Contains(lost));Restored(s);
        Accept(original,new SkipPerksCommand(1,p.Cursor));CollectionAssert.AreEqual(p.Equipped,original.CapturePerks()!.Equipped);Restored(original);
    }
    [TestMethod]
    public void SeparateRoleTrainingDerivesActualAbilitiesAndSavedWalkingSpeeds()
    {
        foreach(var role in new[]{ResponseRole.Medic,ResponseRole.Steward})
        {
            var s=GameSession.CreateBuildCampaign(123);var before=s.GetResponseStaff().ToArray();FixturePerks(s,role==ResponseRole.Medic?"first-responders":"smooth-operators");
            foreach(var profile in s.GetResponseStaff())
            {
                var old=before.Single(p=>p.AgentId==profile.AgentId);
                Assert.AreEqual(profile.Role==role?Math.Min(1150,old.WalkingSpeedPermille+100):old.WalkingSpeedPermille,profile.WalkingSpeedPermille);
                if(profile.Role==ResponseRole.Medic)Assert.AreEqual(role==ResponseRole.Medic?Math.Max(360,old.TreatmentTicks-120):old.TreatmentTicks,profile.TreatmentTicks);
                else Assert.AreEqual(role==ResponseRole.Steward?Math.Min(8000,old.CalmingSkill+500):old.CalmingSkill,profile.CalmingSkill);
            }
            Start(s);s.AdvanceWithoutSnapshot(200);Restored(s);
            foreach(var profile in s.GetResponseStaff())Assert.AreEqual(profile.WalkingSpeedPermille,s.CapturePersistenceSnapshot().NavigationAgents!.Single(n=>n.Id==profile.AgentId).WalkingSpeedPermille);
        }
    }
    [TestMethod]
    public void SaveFailureRetainsHandCursorMoneyAndIdsAndSuccessfulChoiceReloadsExactly()
    {
        var path=Path.Combine(Path.GetTempPath(),"festival-perks-"+Guid.NewGuid());var compat=new SaveCompatibility("perk-test","content","rules");
        var s=GameSession.CreateBuildCampaign(123);var p=s.CapturePerks()!;var hash=s.CaptureSnapshot().AuthoritativeHash;
        try
        {
            var failed=EquipmentCommandCoordinator.Execute(path,s,new ChoosePerkCommand(1,p.Cursor,p.Hand[0]),compat,DateTimeOffset.UtcNow,0,_=>throw new IOException("test write failure"));
            Assert.IsFalse(failed.IsSuccess);Assert.AreSame(s,failed.Session);Assert.AreEqual(hash,s.CaptureSnapshot().AuthoritativeHash);
            var ok=EquipmentCommandCoordinator.Execute(path,s,new ChoosePerkCommand(1,p.Cursor,p.Hand[0]),compat,DateTimeOffset.UtcNow,0);Assert.IsTrue(ok.IsSuccess,ok.Error);
            var load=AutosaveRotation.LoadNewestValid(path,compat);Assert.IsTrue(load.IsSuccess,load.Error);Assert.AreEqual(ok.Session.CaptureSnapshot().AuthoritativeHash,load.Session!.CaptureSnapshot().AuthoritativeHash);
        }finally{if(Directory.Exists(path))Directory.Delete(path,true);}
    }
    [TestMethod]
    public void MalformedVersionHandCapacityRngAndEffectsAreRejected()
    {
        var s=GameSession.CreateBuildCampaign(123);var snap=s.CapturePersistenceSnapshot();var p=s.CapturePerks()!;
        foreach(var bad in new[]{p with {Version=2},p with {Hand=["unknown","unknown","unknown"]},p with {Hand=[p.Hand[0],p.Hand[0],p.Hand[1]]},p with {Equipped=PerkCatalogue.All.Select(x=>x.Id).ToArray()},p with {RandomIncrement=2},p with {RandomState=p.RandomState+1},p with {DraftAttempt=2},p with {Equipped=[p.Hand[0]]},p with {FrozenEffects=["high-pressure"]},p with {Equipped=null!}})
        {
            var semantic=(string?)typeof(GameSession).GetMethod("ValidatePersistenceSnapshot",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[snap with {Perks=bad}]);
            Assert.IsNotNull(semantic,"Semantic validator must reject before final hash comparison.");
            Assert.IsFalse(GameSession.Restore(snap with {Perks=bad}).IsSuccess);
        }
        foreach(var bad in new[]{p with {Pending=false,Hand=[]},p with {RerollUsed=true},p with {DrawnHand=p.DrawnHand.Reverse().ToArray()}})
            Assert.IsNotNull(typeof(GameSession).GetMethod("ValidatePersistedPerks",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[snap with {Perks=bad}]));
        Accept(s,new RerollPerksCommand(1,p.Cursor));snap=s.CapturePersistenceSnapshot();Assert.IsNotNull(typeof(GameSession).GetMethod("ValidatePersistedPerks",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[snap with {Perks=snap.Perks! with {RerollUsed=false}}]));
    }
    [TestMethod]
    public void EveryPerkRemovalRestoresItsBoundedEffectAndOwnedExclusion()
    {
        foreach(var perk in PerkCatalogue.All)
        {
            var s=GameSession.CreateBuildCampaign(123);FixturePerks(s,perk.Id);FixtureDraft(s);Assert.IsFalse(s.CapturePerks()!.Hand.Contains(perk.Id));FixturePerks(s);
            var legacy=GameSession.CreateBuildCampaign(123);Assert.AreEqual(JsonSerializer.Serialize(legacy.CapturePreparation()),JsonSerializer.Serialize(s.CapturePreparation()));CollectionAssert.AreEqual(legacy.GetResponseStaff().ToArray(),s.GetResponseStaff().ToArray());
        }
    }
    [TestMethod]
    public void ActualGuestFreeWaterTicksImproveSatisfactionAndResumeExactlyWhileStaffDoNotGain()
    {
        var s=GameSession.CreateBuildCampaign(20260922);FixturePerks(s,"something-in-the-water");Start(s);
        // Normal no-stock gameplay: paid drinks cannot provide the tested perk reward.
        var found=false;
        for(var i=0;i<18000 && s.PreparedStatus==PreparationStatus.Running;i++)
        {
            s.AdvanceWithoutSnapshot(1);
            var point=s.CaptureWaterPoints().FirstOrDefault(p=>p.OwnerId is not null && p.DrinkTicks>0);
            if(point is null)continue;
            var person=s.CapturePreparation()!.People.Single(p=>p.AgentId==point.OwnerId);
            var before=person.Satisfaction;var previous=point.DrinkTicks;
            var saved=Restored(s);s.AdvanceWithoutSnapshot(1);saved.AdvanceWithoutSnapshot(1);
            Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,saved.CaptureSnapshot().AuthoritativeHash);
            if(s.CaptureWaterPoints().Single(p=>p.Id==point.Id).DrinkTicks==previous+1)
            {
                var after=s.CapturePreparation()!.People.Single(p=>p.AgentId==person.AgentId).Satisfaction;
                Assert.AreEqual(person.Role==ProtectedPersonRole.Guest?Math.Min(10000,before+1):before,after);
                if(person.Role==ProtectedPersonRole.Guest){found=true;break;}
            }
        }
        Assert.IsTrue(found,"Actual physically served guest drinking was required.");
        var original=s.CapturePreparation()!;var owner=s.CaptureWaterPoints().Single(p=>p.OwnerId is not null).OwnerId;
        typeof(GameSession).GetProperty("PreparationView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,original with {People=original.People.Select(p=>p.AgentId==owner?p with {Satisfaction=10000}:p).ToArray()});
        s.AdvanceWithoutSnapshot(1);Assert.AreEqual(10000,s.CapturePreparation()!.People.Single(p=>p.AgentId==owner).Satisfaction);Restored(s);
    }
    private static void PositionServiceFixture(GameSession s,ulong id,GridCell cell,string intent)
    {
        var agents=typeof(GameSession).GetField("_navigationAgents",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(s)!;
        var nav=agents.GetType().GetProperty("Item")!.GetValue(agents,[new EntityId(id)])!;var centre=TraversalGrid.CellCentre(cell);
        foreach(var pair in new (string,object)[]{("XMillimetres",centre.XMillimetres),("ZMillimetres",centre.ZMillimetres),("SegmentOriginXMillimetres",centre.XMillimetres),("SegmentOriginZMillimetres",centre.ZMillimetres),("Route",new List<GridCell>()),("RouteIndex",0),("SegmentProgressMicrometres",0),("Action",AgentNavigationAction.Arrived),("Destination",cell),("IntentId",intent)})nav.GetType().GetProperty(pair.Item1)!.SetValue(nav,pair.Item2);
    }
    [TestMethod]
    public void ActualStaffFreeWaterAndGuestPaidDrinkDoNotEarnPerkHappiness()
    {
        var s=GameSession.CreateBuildCampaign(20260922);FixturePerks(s,"something-in-the-water");Accept(s,new SetPreparationStockCommand(40, 40, 32));Start(s);s.AdvanceWithoutSnapshot(2000);
        var m=s.CaptureMedical()!;var id=s.CaptureDisorder()!.SecurityId;var point=s.CaptureWaterPoints().Single();var front=GameSession.WaterPointServiceCell(point);
        PositionServiceFixture(s,id,front,"medical.water");
        typeof(GameSession).GetProperty("MedicalView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,m with {WaterQueue=[id],WaterOverflow=[],WaterOwnerId=id,WaterDrinkTicks=1,MainWaterQueueCells=[front],Needs=m.Needs.Select(n=>n.AgentId==id?n with {Thirst=1000,Intent=MedicalIntent.Drinking,WaterPointId="water.main",QueueSlot=0}:n with {QueueSlot=null,WaterPointId="water.main",Intent=n.Intent is MedicalIntent.Drinking or MedicalIntent.SeekWater?MedicalIntent.WatchShow:n.Intent}).ToArray()});
        var before=s.CapturePreparation()!.People.Single(p=>p.AgentId==id).Satisfaction;s.AdvanceWithoutSnapshot(1);Assert.AreEqual(2,s.CaptureWaterPoints().Single().DrinkTicks);Assert.AreEqual(before,s.CapturePreparation()!.People.Single(p=>p.AgentId==id).Satisfaction);Restored(s);
        var paid=GameSession.CreateBuildCampaign(20260922);FixturePerks(paid,"something-in-the-water");Accept(paid,new SetPreparationStockCommand(40, 40, 32));Start(paid);
        var baseline=Restored(paid);typeof(GameSession).GetField("_perks",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(baseline,null);
        var guest=paid.CapturePreparation()!.People.First(p=>p.Role==ProtectedPersonRole.Guest).AgentId;
        foreach(var target in new[]{paid,baseline})typeof(GameSession).GetMethod("CompleteImmersionSale",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(target,[guest,ImmersionProduct.SoftDrink]);
        paid.AdvanceWithoutSnapshot(100);baseline.AdvanceWithoutSnapshot(100);
        Assert.AreEqual(baseline.CapturePreparation()!.People.Single(p=>p.AgentId==guest).Satisfaction,paid.CapturePreparation()!.People.Single(p=>p.AgentId==guest).Satisfaction);
        Assert.AreEqual(ImmersionProduct.SoftDrink,paid.CaptureImmersion()!.People.Single(p=>p.AgentId==guest).Held!.Product);Restored(paid);
    }
}

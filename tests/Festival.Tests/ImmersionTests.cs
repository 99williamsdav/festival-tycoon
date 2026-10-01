using Festival.Simulation;
using System.Reflection;
using Festival.Persistence;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Collections;
using System.Diagnostics;

namespace Festival.Tests;

[TestClass]
public sealed class ImmersionTests
{
    private static CommandResult Send(GameSession s,SessionCommand c)=>s.Execute(new(new(s.NextSubmissionSequence+1),s.CampaignId,s.Phase,s.CurrentTick,s.NextSubmissionSequence,null,c));
    private static void Set(GameSession s,ImmersionSnapshot snapshot)=>typeof(GameSession).GetProperty("ImmersionView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,snapshot);
    private static void Invoke(GameSession s,string method,params object[] args)=>typeof(GameSession).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(s,args);
    private static void PositionFixture(GameSession s,ulong id,GridCell cell,string intent)
    {
        var agents=(IDictionary)typeof(GameSession).GetField("_navigationAgents",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(s)!;var nav=agents[new EntityId(id)]!;var centre=TraversalGrid.CellCentre(cell);
        void SetProperty(string name,object value)=>nav.GetType().GetProperty(name)!.SetValue(nav,value);
        SetProperty("XMillimetres",centre.XMillimetres);SetProperty("ZMillimetres",centre.ZMillimetres);SetProperty("SegmentOriginXMillimetres",centre.XMillimetres);SetProperty("SegmentOriginZMillimetres",centre.ZMillimetres);SetProperty("Route",new List<GridCell>());SetProperty("RouteIndex",0);SetProperty("SegmentProgressMicrometres",0);SetProperty("Action",AgentNavigationAction.Arrived);SetProperty("Destination",cell);SetProperty("IntentId",intent);
    }
    private static GameSession Open(ulong seed=20260926,bool maintenance=false,bool extraMedic=false)
    {
        var s=extraMedic?BuildSession.PlannedWith("doctors-orders",seed):BuildSession.Planned(seed);
        foreach (var hire in BuildSession.Crew(s)) Assert.IsTrue(Send(s, hire).IsAccepted);
        if(maintenance)Assert.IsTrue(Send(s,new AcceptPreparationOfferCommand("maintenance.worker")).IsAccepted);
        if(extraMedic)Assert.IsTrue(Send(s,new AcceptPreparationOfferCommand(BuildSession.ExtraId(s, StaffRole.Medic))).IsAccepted);
        Assert.IsTrue(Send(s,new StartPreparedEditionCommand()).IsAccepted);
        // Labelled eligibility fixture; admission/navigation itself is covered by physical scenario below.
        var prep=s.CapturePreparation()!;
        typeof(GameSession).GetProperty("PreparationView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,prep with { People=prep.People.Select(p=>p with { Admitted=true }).ToArray() });
        return s;
    }
    private static GameSession Restore(GameSession s) {var result=GameSession.Restore(s.CapturePersistenceSnapshot());Assert.IsTrue(result.IsSuccess,result.Error);Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,result.Session!.CaptureSnapshot().AuthoritativeHash);return result.Session;}
    [TestMethod]
    public void SharedConsumptionEligibilityUsesCounterDistanceOwnershipAndOperationState()
    {
        var s=Open();var id=s.CaptureImmersion()!.People.First().AgentId;
        Assert.IsFalse(s.ImmersionConsumptionEligible(id)); Assert.IsFalse(s.ImmersionConsumptionEligible(ulong.MaxValue));
        Invoke(s,"CompleteImmersionSale",id,ImmersionProduct.SoftDrink);
        var service=GameSession.ImmersionServiceCell(s.CaptureVendors()[0]);
        PositionFixture(s,id,new(service.X+2,service.Z),"pose.counter-distance");
        Assert.IsTrue(s.ImmersionHandsAvailable(id)); Assert.IsFalse(s.ImmersionConsumptionEligible(id),"Exactly 1m is still at the counter.");
        Invoke(s,"AdvanceImmersion"); Assert.AreEqual(0,s.CaptureImmersion()!.People.Single(p=>p.AgentId==id).Held!.ConsumedTicks);
        PositionFixture(s,id,new(service.X+3,service.Z),"pose.counter-distance");
        Assert.IsTrue(s.ImmersionConsumptionEligible(id)); Invoke(s,"AdvanceImmersion");
        Assert.AreEqual(1,s.CaptureImmersion()!.People.Single(p=>p.AgentId==id).Held!.ConsumedTicks);
        var before=s.CaptureSnapshot().AuthoritativeHash;
        for(var repeat=0;repeat<5;repeat++) Assert.IsTrue(s.ImmersionConsumptionEligible(id));
        Assert.AreEqual(before,s.CaptureSnapshot().AuthoritativeHash);
        var m=s.CaptureImmersion()!;Set(s,m with { People=m.People.Select(p=>p.AgentId==id?p with { VendorId="food" }:p).ToArray() });
        Assert.IsFalse(s.ImmersionConsumptionEligible(id)); Set(s,m);
        var prep=s.CapturePreparation()!;
        foreach(var status in new[]{PreparationStatus.Preparing,PreparationStatus.Failed,PreparationStatus.Finished})
        {
            typeof(GameSession).GetProperty("PreparationView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,prep with { Status=status });
            Assert.IsFalse(s.ImmersionConsumptionEligible(id));
        }
        typeof(GameSession).GetProperty("PreparationView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,prep with { Status=PreparationStatus.Departing });
        Assert.IsTrue(s.ImmersionConsumptionEligible(id));
        typeof(GameSession).GetProperty("PreparationView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,prep with { People=prep.People.Select(p=>p.AgentId==id?p with { Departed=true }:p).ToArray() });
        Assert.IsFalse(s.ImmersionConsumptionEligible(id));
    }
    [TestMethod]
    public void LooseVendorSlotsGrowOnlyAsNeededKeepPrefixAndPersistExactReplay()
    {
        var s=Open();var ids=s.CapturePreparation()!.People.Where(p=>p.Role==ProtectedPersonRole.Guest).Take(9).Select(p=>p.AgentId).ToArray();var prefix=s.CaptureImmersionQueueCells("drinks").ToArray();Assert.AreEqual(1,prefix.Length);
        for(var count=1;count<=9;count++)
        {
            var m=s.CaptureImmersion()!;Set(s,m with { People=m.People.Select(p=>ids.Take(count).Contains(p.AgentId)?p with { VendorId="drinks",Order=ImmersionProduct.SoftDrink,LastDecisionTick=s.CurrentTick }:p).ToArray() });Invoke(s,"GrowImmersionQueues");
            var cells=s.CaptureImmersionQueueCells("drinks").ToArray();Assert.AreEqual(count+1,cells.Length);CollectionAssert.AreEqual(prefix,cells.Take(prefix.Length).ToArray());prefix=cells;
        }
        Assert.IsTrue(prefix.Select(c=>c.X).Distinct().Count()>1,"Small gradual side offsets must break the rigid straight row.");
        Assert.IsTrue(prefix.Zip(prefix.Skip(1),(a,b)=>(b.X-a.X)*(b.X-a.X)+(b.Z-a.Z)*(b.Z-a.Z)).Distinct().Count()>1,"Uneven integer gaps must not all repeat.");
        var before=s.CaptureSnapshot().AuthoritativeHash;Invoke(s,"GrowImmersionQueues");Assert.AreEqual(before,s.CaptureSnapshot().AuthoritativeHash);
        var r=Restore(s);s.AdvanceWithoutSnapshot(160);r.AdvanceWithoutSnapshot(160);Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,r.CaptureSnapshot().AuthoritativeHash);Restore(s);
    }
    [TestMethod]
    public void PaidItemPartialConsumptionResumeAndPauseAreExact()
    {
        var s=Open();var id=s.CaptureImmersion()!.People.First().AgentId;
        Invoke(s,"CompleteImmersionSale",id,ImmersionProduct.SoftDrink);
        var m=s.CaptureImmersion()!;Assert.AreEqual(39,m.SoftStock);Assert.AreEqual(1,m.Purchases.Length);Assert.AreEqual(0L,m.Purchases[0].Entries.Sum(e=>e.AmountPennies));Assert.IsNull(m.People.Single(p=>p.AgentId==id).VendorId);
        for(var i=0;i<400;i++)Invoke(s,"AdvanceImmersion");
        Assert.AreEqual(400,s.CaptureImmersion()!.People.Single(p=>p.AgentId==id).Held!.ConsumedTicks);
        var restored=Restore(s);for(var i=0;i<700;i++){Invoke(s,"AdvanceImmersion");Invoke(restored,"AdvanceImmersion");}Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,restored.CaptureSnapshot().AuthoritativeHash);
        Assert.IsTrue(Send(s,new SetPausedCommand(true)).IsAccepted);var hash=s.CaptureSnapshot().AuthoritativeHash;s.AdvanceWithoutSnapshot(400);Assert.AreEqual(hash,s.CaptureSnapshot().AuthoritativeHash);
    }
    [TestMethod]
    public void BeerRefusalAbstentionStaffAndOnstagePendingPhysiology()
    {
        var s=Open();var prep=s.CapturePreparation()!;var m=s.CaptureImmersion()!;
        var abstainer=m.People.First(p=>p.Abstains);var staff=m.People.First(p=>prep.People.Single(n=>n.AgentId==p.AgentId).Role==ProtectedPersonRole.Staff);
        var eligible=typeof(GameSession).GetMethod("ImmersionOrderEligible",BindingFlags.NonPublic|BindingFlags.Instance)!;
        Assert.AreEqual(false,eligible.Invoke(s,[s.CapturePerson(abstainer.AgentId),ImmersionProduct.Beer]));Assert.AreEqual(false,eligible.Invoke(s,[s.CapturePerson(staff.AgentId),ImmersionProduct.Beer]));
        var performer=s.CaptureLivePerformance()!.Performers.First();var id=performer.AgentId;
        Invoke(s,"CompleteImmersionSale",id,ImmersionProduct.Beer);
        var live=s.CaptureLivePerformance()!;typeof(GameSession).GetField("_livePerformance",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,live with { Performers=live.Performers.Select(p=>p.AgentId==id?p with { OnStage=true,InstrumentAttached=true }:p).ToArray() });
        m=s.CaptureImmersion()!;Set(s,m with { People=m.People.Select(p=>p.AgentId==id?p with { PendingDose=80,Intoxication=1000 }:p).ToArray() });
        Assert.IsFalse(s.ImmersionHandsAvailable(id));for(var i=0;i<80;i++)Invoke(s,"AdvanceImmersion");
        var after=s.CaptureImmersion()!.People.Single(p=>p.AgentId==id);Assert.AreEqual(0,after.Held!.ConsumedTicks);Assert.AreEqual(0,after.PendingDose);Assert.AreEqual(1070,after.Intoxication);
    }
    [TestMethod]
    public void FoodHalvesNewAbsorptionAndWaterDoesNotSober()
    {
        var s=Open();var m=s.CaptureImmersion()!;var id=m.People.First().AgentId;
        Set(s,m with { People=m.People.Select(p=>p.AgentId==id?p with { PendingDose=80,Intoxication=1000,FoodProtectionTicks=4800 }:p).ToArray() });
        for(var i=0;i<80;i++)Invoke(s,"AdvanceImmersion");var after=s.CaptureImmersion()!.People.Single(p=>p.AgentId==id);Assert.AreEqual(1030,after.Intoxication);Assert.AreEqual(0,after.PendingDose);
        Invoke(s,"UpdatePerson",id,(Func<Person,Person>)(n=>n with { Thirst=0 }));Assert.AreEqual(1030,s.CaptureImmersion()!.People.Single(p=>p.AgentId==id).Intoxication);
    }
    [TestMethod]
    public void SemanticTamperRejectsBudgetsStockHeldOrdersAndMalformedCollections()
    {
        var s=BuildSession.Planned(123);var saved=s.CapturePersistenceSnapshot();var m=saved.Immersion!;
        Assert.IsFalse(GameSession.Restore(saved with { Immersion=m with { Version=2 } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { Immersion=m with { SoftStock=1 } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { Immersion=m with { People=m.People.Select((p,i)=>i==0?p with { OpeningBudgetPennies=p.OpeningBudgetPennies+1 }:p).ToArray() } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { Immersion=m with { People=null! } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { Immersion=m with { Purchases=[null!] } }).IsSuccess);
    }
    [TestMethod]
    public void PhysicalDayCreatesSalesAndSaveReplayWithoutFreeAdmissionStock()
    {
        var s=BuildSession.Planned(20260926);
        Assert.IsTrue(Send(s,new SetPreparationStockCommand(40, 40, 32)).IsAccepted);
        Assert.IsTrue(Send(s,new SetProgrammeCommand(["act.meadow-lanterns","act.barnstorm-circuit","act.neon-postcards"])).IsAccepted);
        foreach (var hire in BuildSession.Crew(s)) Assert.IsTrue(Send(s, hire).IsAccepted);
        Assert.IsTrue(Send(s,new StartPreparedEditionCommand()).IsAccepted);
        s.AdvanceWithoutSnapshot(8000);
        Assert.AreEqual(0,s.CapturePreparation()!.StockConsumed);
        Assert.IsTrue(s.CaptureImmersion()!.Purchases.Length>0,"Autonomous physical routes must create real purchases.");
        var r=Restore(s);s.AdvanceWithoutSnapshot(2000);r.AdvanceWithoutSnapshot(2000);Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,r.CaptureSnapshot().AuthoritativeHash);
        Assert.IsTrue(s.CaptureImmersion()!.People.Any(p=>p.Intoxication>0),"Approved ordinary beer must have a measurable effect.");
    }
    [TestMethod]
    public void TimelyPhysicalMedicStabilizesGraduallyAndDoesNotCreateAlcoholImmunity()
    {
        var s=Open();var m=s.CaptureImmersion()!;var id=m.People.First().AgentId;
        Invoke(s,"UpdatePerson",id,(Func<Person,Person>)(n=>n with { HealthStage=MedicalStage.Treated }));
        Set(s,m with { People=m.People.Select(p=>p.AgentId==id?p with { Intoxication=10000 }:p).ToArray() });s.AdvanceWithoutSnapshot(1);
        Assert.IsTrue(Send(s,new MedicalCommand(id,MedicalAction.DispatchMedic)).IsAccepted);
        s.AdvanceWithoutSnapshot(200);Assert.IsTrue(s.CaptureImmersion()!.People.Single(p=>p.AgentId==id).Intoxication>7500,"Dispatch alone must not sober.");
        s.AdvanceWithoutSnapshot(3300);Assert.AreNotEqual(PreparationStatus.Failed,s.PreparedStatus);
        Assert.IsTrue(s.CaptureMedical()!.Evidence.Any(e=>e.Id=="intoxication:treatment-complete"));
        Assert.AreEqual(MedicalStage.Treated,s.CaptureMedical()!.Needs.Single(n=>n.AgentId==id).Stage,"Preventive alcohol care must preserve prior heat treatment.");
        var after=s.CaptureImmersion()!;Assert.AreEqual(-1L,after.People.Single(p=>p.AgentId==id).CollapseTick);Restore(s);
        Set(s,after with { People=after.People.Select(p=>p.AgentId==id?p with { Intoxication=10000 }:p).ToArray() });s.AdvanceWithoutSnapshot(1);
        Assert.IsTrue(Send(s,new MedicalCommand(id,MedicalAction.DispatchMedic)).IsAccepted,"Completed care must allow a fresh intoxication response.");
    }
    [TestMethod]
    public void PhysicalArrivalFifoHeterogeneousSaleAndFailureNeverChargeTwice()
    {
        var s=Open();var m=s.CaptureImmersion()!;var ids=m.People.Where(p=>!p.Abstains&&s.CapturePreparation()!.People.Single(n=>n.AgentId==p.AgentId).Role==ProtectedPersonRole.Guest).Take(2).Select(p=>p.AgentId).ToArray();var first=ids[1];var remote=ids[0];var vendor=s.CaptureVendors().Single(v=>v.Id=="drinks");
        Set(s,m with { People=m.People.Select(p=>p.AgentId==first?p with { VendorId="drinks",Order=ImmersionProduct.SoftDrink }:p.AgentId==remote?p with { VendorId="drinks",Order=ImmersionProduct.Beer }:p with { LastDecisionTick=s.CurrentTick }).ToArray() });
        PositionFixture(s,first,GameSession.ImmersionServiceCell(vendor),"immersion.approach");Invoke(s,"AdvanceImmersion");Assert.AreEqual(first,s.CaptureVendors().Single(v=>v.Id=="drinks").Queue.Single(),"Remote request must not join before physical arrival.");
        for(var i=0;i<160;i++)Invoke(s,"AdvanceImmersion");var purchase=s.CaptureImmersion()!.Purchases.Single();Assert.AreEqual(first,purchase.AgentId);Assert.AreEqual(ImmersionProduct.SoftDrink,purchase.Product);Assert.AreEqual(0,s.CaptureImmersion()!.People.Single(p=>p.AgentId==first).Held!.ConsumedTicks,"Counter purchase releases it before any ingestion.");
        vendor=s.CaptureVendors().Single(v=>v.Id=="drinks");PositionFixture(s,remote,GameSession.ImmersionServiceCell(vendor),"immersion.approach");for(var i=0;i<160;i++)Invoke(s,"AdvanceImmersion");Assert.AreEqual(2,s.CaptureImmersion()!.Purchases.Length);Assert.AreEqual(ImmersionProduct.Beer,s.CaptureImmersion()!.Purchases.Last().Product);
        var stock=s.CaptureImmersion()!.BeerStock;for(var i=0;i<160;i++)Invoke(s,"AdvanceImmersion");Assert.AreEqual(stock,s.CaptureImmersion()!.BeerStock);Assert.AreEqual(2,s.CaptureImmersion()!.Purchases.Length);Restore(s);
    }
    [TestMethod]
    public void NewDefaultVendorsFormTentFrontRowOnEastGrassWithAlignedEntrances()
    {
        var s=BuildSession.Planned(20260926);var vendors=s.CaptureVendors();
        Assert.AreEqual(new GridCell(144,119),vendors.Single(v=>v.Id=="food").Cell);Assert.AreEqual(new GridCell(160,120),vendors.Single(v=>v.Id=="drinks").Cell);
        Assert.IsTrue(vendors.All(v=>v.QuarterTurns==0));Assert.IsTrue(vendors.SelectMany(GameSession.ImmersionFootprint).All(c=>c.X>=137));
        Assert.AreEqual(16,vendors.Single(v=>v.Id=="drinks").Cell.X-vendors.Single(v=>v.Id=="food").Cell.X);
        Assert.IsFalse(GameSession.ImmersionFootprint(vendors[0]).Intersect(GameSession.ImmersionFootprint(vendors[1])).Any());
        foreach(var vendor in vendors)Assert.IsTrue(Enumerable.Range(0,10).Select(i=>GameSession.ImmersionQueueCell(vendor,i)).All(c=>c.X==vendor.Cell.X&&c.Z>vendor.Cell.Z));Restore(s);
    }
    [TestMethod]
    public void QueuedHeavyBeerRefusalCancelsWithoutCashStockOrReceipt()
    {
        var s=Open();var m=s.CaptureImmersion()!;var p=m.People.First(p=>!p.Abstains&&s.CapturePreparation()!.People.Single(n=>n.AgentId==p.AgentId).Role==ProtectedPersonRole.Guest);var vendor=s.CaptureVendors().Single(v=>v.Id=="drinks");
        PositionFixture(s,p.AgentId,GameSession.ImmersionServiceCell(vendor),"immersion.queue");
        BuildSession.SetVendor(s,vendor with { Queue=[p.AgentId],OwnerId=p.AgentId,ServiceTicks=1 });Set(s,m with { People=m.People.Select(n=>n.AgentId==p.AgentId?n with { VendorId="drinks",Order=ImmersionProduct.Beer,Intoxication=7000,PendingDose=20 }:n).ToArray() });
        var cash=s.CaptureSnapshot().Wallets.Single(w=>w.OwnerId.Value==p.AgentId).CashPennies;Invoke(s,"AdvanceImmersion");
        Assert.AreEqual(32,s.CaptureImmersion()!.BeerStock);Assert.AreEqual(0,s.CaptureImmersion()!.Purchases.Length);Assert.AreEqual(cash,s.CaptureSnapshot().Wallets.Single(w=>w.OwnerId.Value==p.AgentId).CashPennies);Assert.IsNull(s.CaptureImmersion()!.People.Single(n=>n.AgentId==p.AgentId).Held);Assert.IsNull(s.CaptureVendors().Single(v=>v.Id=="drinks").OwnerId);Restore(s);
    }
    [TestMethod]
    public void MaintenanceOwnershipSuspendsHeldItemAndPreventsShoppingRouteTheft()
    {
        var s=Open(maintenance:true);var worker=s.CapturePreparation()!.MaintenanceWorkerId!.Value;Invoke(s,"CompleteImmersionSale",worker,ImmersionProduct.SoftDrink);
        var equipment=s.CaptureEquipment()!;typeof(GameSession).GetField("_equipment",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,equipment with { Stage=EquipmentStage.Normal,LoadPercent=120,WarningTick=-1 });
        Assert.IsTrue(Send(s,new EquipmentCommand(EquipmentAction.DispatchMaintenance)).IsAccepted);Assert.IsFalse(s.ImmersionHandsAvailable(worker));
        s.AdvanceWithoutSnapshot(80);Assert.AreEqual(0,s.CaptureImmersion()!.People.Single(p=>p.AgentId==worker).Held!.ConsumedTicks);
        Assert.Contains("equipment",s.CaptureSnapshot().NavigationAgents.Single(n=>n.Id.Value==worker).IntentId!);Assert.AreNotEqual(MaintenanceStage.Completed,s.CaptureEquipment()!.JobStage);Restore(s);
    }
    [TestCategory("Slow")]
    [TestMethod]
    public void NinePaidPerformerItemsSaveWhileFirstBandOnstageAndResumeExactly()
    {
        var s=Open();s.AdvanceWithoutSnapshot(GameSession.FestivalSlotStarts[0]-800);
        foreach(var p in s.CapturePreparation()!.People.Where(p=>p.Role==ProtectedPersonRole.Performer))Invoke(s,"CompleteImmersionSale",p.AgentId,ImmersionProduct.SoftDrink);
        s.AdvanceWithoutSnapshot(1600);var performers=s.CaptureLivePerformance()!.Performers;Assert.IsTrue(performers.All(p=>p.OnStage));
        var m=s.CaptureImmersion()!;Set(s,m with { People=m.People.Select(p=>performers.Any(n=>n.AgentId==p.AgentId)?p with { PendingDose=80,Intoxication=1000 }:p).ToArray() });
        var consumed=performers.ToDictionary(p=>p.AgentId,p=>s.CaptureImmersion()!.People.Single(n=>n.AgentId==p.AgentId).Held!.ConsumedTicks);
        var compatibility=new SaveCompatibility("held-performer","content","rules");var directory=Path.Combine(Path.GetTempPath(),"festival-held-performer-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try { Assert.IsTrue(SaveFileAdapter.SaveSlot(directory,"stage",new(s,compatibility,"stage-held",DateTimeOffset.UtcNow)).IsSuccess);var loaded=SaveFileAdapter.LoadSlot(directory,"stage",compatibility);Assert.IsTrue(loaded.IsSuccess,loaded.Error);var r=loaded.Session!;
            s.AdvanceWithoutSnapshot(80);r.AdvanceWithoutSnapshot(80);Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,r.CaptureSnapshot().AuthoritativeHash);
            foreach(var p in performers){var after=s.CaptureImmersion()!.People.Single(n=>n.AgentId==p.AgentId);Assert.AreEqual(consumed[p.AgentId],after.Held!.ConsumedTicks);Assert.AreEqual(0,after.PendingDose);Assert.AreEqual(1070,after.Intoxication);}
            s.AdvanceWithoutSnapshot(5500);r.AdvanceWithoutSnapshot(5500);Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,r.CaptureSnapshot().AuthoritativeHash);Assert.IsTrue(performers.All(p=>s.CaptureImmersion()!.People.Single(n=>n.AgentId==p.AgentId).Held is null or { ConsumedTicks:>0 }));
        } finally { Directory.Delete(directory,true); }
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CoexistingHeatCollapseRetainsEarlierClocksAndOrdinaryMedicSaveBoundary(bool extraMedic)
    {
        var s=Open(extraMedic:extraMedic);typeof(GameSession).GetProperty(nameof(GameSession.CurrentTick))!.SetValue(s,4000L);
        var m=s.CaptureMedical()!;var id=BuildSession.LastGuest(s);var worker=s.GetMedicResponses().Last().WorkerId;
        m=m with { Needs=m.Needs.Select(n=>n.AgentId==id?n with { Stage=MedicalStage.Collapsed,WarningTick=2300,CollapseTick=3900,CriticalTick=-1,Intent=MedicalIntent.Collapsed,Thirst=9500,HeatExposure=8500 }:n).ToArray() };
        typeof(GameSession).GetProperty("MedicalView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,m);
        var immersion=s.CaptureImmersion()!;Set(s,immersion with { People=immersion.People.Select(p=>p.AgentId==id?p with { Intoxication=10000,WarningTick=2399,SevereTicks=1599 }:p).ToArray() });
        PositionFixture(s,id,new(120,125),"medical.collapsed");PositionFixture(s,worker,new(121,125),"medical.dispatch");
        var duration=s.GetResponseStaff().Single(p=>p.AgentId==worker).TreatmentTicks;
        Invoke(s,"SetMedicResponse",new MedicResponse(worker,MedicalResponseStage.Treating,id,4001-duration,"Labelled dual heat/intoxication physical treatment",4000-duration));
        Invoke(s,"AdvanceImmersion");Assert.AreEqual(3900L,s.CaptureMedical()!.Needs.Single(n=>n.AgentId==id).CollapseTick);Assert.AreEqual(-1L,s.CaptureImmersion()!.People.Single(p=>p.AgentId==id).CollapseTick,"Alcohol must not restart the inherited heat deadline.");
        Assert.IsFalse(s.ImmersionBoundaryOnNextTick,"A severe alcohol overlap blocked by existing heat ownership must not clone/save every tick.");
        Assert.IsTrue(s.MedicalBoundaryOnNextTick,"The actual ordinary heat treatment must still stage/persist its completion despite a simultaneous alcohol warning.");Restore(s);
        typeof(GameSession).GetProperty(nameof(GameSession.CurrentTick))!.SetValue(s,4001L);Invoke(s,"AdvanceMedicResponses");Assert.AreEqual(MedicalResponseStage.Completed,s.GetMedicResponses().Single(j=>j.WorkerId==worker).Stage);Assert.AreEqual(MedicalStage.Treated,s.CaptureMedical()!.Needs.Single(n=>n.AgentId==id).Stage);
    }
    private static void DepartureFixture(GameSession s)
    {
        typeof(GameSession).GetProperty(nameof(GameSession.CurrentTick))!.SetValue(s,GameSession.PreparedDayTicks);
        typeof(GameSession).GetProperty(nameof(GameSession.Phase))!.SetValue(s,SessionPhase.Egress);
        var prep=s.CapturePreparation()!;typeof(GameSession).GetProperty("PreparationView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,prep with { Status=PreparationStatus.Departing });
        Invoke(s,"StartImmersionDeparture");
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CollapsedMedicPhysicallyArrivesAtHalfMetreBedsideAndResumesExactly(bool intoxication)
    {
        var s=Open();var medical=s.CaptureMedical()!;var id=BuildSession.LastGuest(s);var worker=medical.MedicId;
        typeof(GameSession).GetProperty(nameof(GameSession.CurrentTick))!.SetValue(s,1600L);
        PositionFixture(s,id,new(120,125),"medical.collapsed");PositionFixture(s,worker,new(126,125),"medical.standby");
        typeof(GameSession).GetProperty("MedicalView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,medical with { Needs=medical.Needs.Select(n=>n.AgentId==id?n with { Stage=MedicalStage.Collapsed,Intent=MedicalIntent.Collapsed,WarningTick=0,CollapseTick=1600 }:n).ToArray() });
        if(intoxication){var m=s.CaptureImmersion()!;Set(s,m with { People=m.People.Select(p=>p.AgentId==id?p with { Intoxication=9800,WarningTick=0,CollapseTick=1600,SevereTicks=1600 }:p).ToArray() });}
        Assert.IsTrue(Send(s,new MedicalCommand(id,MedicalAction.DispatchMedic)).IsAccepted);Assert.AreEqual(MedicalResponseStage.Travelling,s.GetMedicResponses().Single(j=>j.WorkerId==worker).Stage);
        var r=Restore(s);for(var ticks=0;ticks<600&&s.GetMedicResponses().Single(j=>j.WorkerId==worker).Stage!=MedicalResponseStage.Treating;ticks++){s.AdvanceWithoutSnapshot(1);r.AdvanceWithoutSnapshot(1);}
        Assert.AreEqual(MedicalResponseStage.Treating,s.GetMedicResponses().Single(j=>j.WorkerId==worker).Stage);Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,r.CaptureSnapshot().AuthoritativeHash);
        var nav=s.CapturePersistenceSnapshot().NavigationAgents!;var medic=nav.Single(n=>n.Id==worker);var patient=nav.Single(n=>n.Id==id);var distance=Math.Sqrt(Math.Pow(medic.XMillimetres-patient.XMillimetres,2)+Math.Pow(medic.ZMillimetres-patient.ZMillimetres,2));Assert.AreEqual(500d,distance,0.01);Console.WriteLine($"physical bedside {distance}mm after {s.CurrentTick}ticks, intoxication={intoxication}");Restore(s);
    }
    [TestMethod]
    public void BedsideBlockedAccessRejectsDispatchAndLegacyDistantTreatmentReroutesWithoutRemoteCare()
    {
        var s=Open();var medical=s.CaptureMedical()!;var id=BuildSession.LastGuest(s);var worker=medical.MedicId;PositionFixture(s,id,new(120,125),"medical.collapsed");PositionFixture(s,worker,new(124,125),"medical.dispatch");
        typeof(GameSession).GetProperty("MedicalView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,medical with { Needs=medical.Needs.Select(n=>n.AgentId==id?n with { Stage=MedicalStage.Collapsed,Intent=MedicalIntent.Collapsed,WarningTick=0,CollapseTick=0 }:n).ToArray() });
        Invoke(s,"SetMedicResponse",new MedicResponse(worker,MedicalResponseStage.Treating,id,0,"Older saved distant treatment",0));Restore(s);
        Invoke(s,"AdvanceMedicResponses");Assert.AreEqual(MedicalResponseStage.Travelling,s.GetMedicResponses().Single(j=>j.WorkerId==worker).Stage);Assert.AreEqual(MedicalStage.Collapsed,s.CaptureMedical()!.Needs.Single(n=>n.AgentId==id).Stage);Restore(s);
        Invoke(s,"SetMedicResponse",new MedicResponse(worker,MedicalResponseStage.None,null,-1,"Blocked access fixture",-1));
        var grid=(TraversalGrid)typeof(GameSession).GetField("_traversalGrid",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(s)!;var cells=grid.Overrides.ToDictionary(p=>p.Key,p=>p.Value);
        for(var x=119;x<=121;x++)for(var z=124;z<=126;z++)if(x!=120||z!=125)cells[new(x,z)]=new(new(x,z),GroundSurface.Grass,false);
        typeof(GameSession).GetField("_traversalGrid",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,new TraversalGrid(cells.Values));
        Assert.IsFalse(Send(s,new MedicalCommand(id,MedicalAction.DispatchMedic)).IsAccepted);Assert.AreEqual(MedicalResponseStage.None,s.GetMedicResponses().Single(j=>j.WorkerId==worker).Stage);
    }
    [TestMethod]
    public void NoncollapsedMedicRetainsOriginalTwoMetreResponseAndRange()
    {
        var s=Open();var medical=s.CaptureMedical()!;var id=BuildSession.LastGuest(s);var worker=medical.MedicId;PositionFixture(s,id,new(120,125),"medical.await-medic");PositionFixture(s,worker,new(124,125),"medical.dispatch");
        var responseCell=typeof(GameSession).GetMethod("MedicalResponseCell",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(s,[worker,id]);Assert.AreEqual(new GridCell(124,125),responseCell);
        var cells=new[]{new TerrainCellOverride(new(122,125),GroundSurface.Grass,false)};typeof(GameSession).GetField("_traversalGrid",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,new TraversalGrid(cells));
        Assert.AreEqual(true,typeof(GameSession).GetMethod("MedicalTreatmentPositionValid",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(s,[worker,(ulong?)id]),"Noncollapsed guidance range must remain unchanged by the collapsed bedside correction.");
        Invoke(s,"UpdatePerson",id,(Func<Person,Person>)(n=>n with { HealthStage=MedicalStage.Collapsed,Intent=MedicalIntent.Collapsed,HealthCollapseTick=0,Reason="Labelled existing injury ownership" }));
        Invoke(s,"UpdatePerson",id,(Func<Person,Person>)(p=>p with { ConductStage=DisorderStage.Injured,InjuryTick=0,ConductStageTick=0 }));
        var bedside=typeof(GameSession).GetMethod("MedicalResponseCell",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(s,[worker,id]);Assert.IsNotNull(bedside);Assert.AreNotEqual(new GridCell(124,125),bedside,"Injury-owned collapse uses the same lawful bedside destination selection.");
    }
    [TestMethod]
    public void DepartureSevereBoundarySaveFailureNeverPublishesCollapse()
    {
        var s=Open();DepartureFixture(s);var m=s.CaptureImmersion()!;var id=BuildSession.LastGuest(s);
        Set(s,m with { People=m.People.Select(p=>p.AgentId==id?p with { Intoxication=9800,WarningTick=GameSession.PreparedDayTicks-1599,SevereTicks=1599 }:p).ToArray() });
        PositionFixture(s,id,new(120,125),"edition.departure");var hash=s.CaptureSnapshot().AuthoritativeHash;
        var directory=Path.Combine(Path.GetTempPath(),"festival-departure-atomic-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try
        {
            var compatibility=new SaveCompatibility("departure-atomic","content","rules");
            var failed=PreparationAdvanceCoordinator.AdvanceOne(directory,s,compatibility,DateTimeOffset.UtcNow,1,_=>throw new IOException("Labelled save failure"));
            Assert.IsFalse(failed.IsSuccess);Assert.AreSame(s,failed.Session);Assert.AreEqual(hash,s.CaptureSnapshot().AuthoritativeHash);
            var accepted=PreparationAdvanceCoordinator.AdvanceOne(directory,s,compatibility,DateTimeOffset.UtcNow,2);
            Assert.IsTrue(accepted.IsSuccess,accepted.Error);Assert.AreEqual(GameSession.PreparedDayTicks+1L,accepted.Session.CaptureImmersion()!.People.Single(p=>p.AgentId==id).CollapseTick);Restore(accepted.Session);
        }
        finally { Directory.Delete(directory,true); }
    }
    [TestMethod]
    public void ClosingContinuesPaidIngestionAndPendingExposureWithoutNewSalesAndStopsAtExit()
    {
        var s=Open();var id=s.CaptureImmersion()!.People.First().AgentId;Invoke(s,"CompleteImmersionSale",id,ImmersionProduct.SoftDrink);
        PositionFixture(s,id,new(120,125),"audience.watch");DepartureFixture(s);
        var m=s.CaptureImmersion()!;Set(s,m with { People=m.People.Select(p=>p.AgentId==id?p with { Intoxication=1000,PendingDose=80 }:p).ToArray() });
        var r=Restore(s);var purchases=m.Purchases.Length;var stock=(m.ChipsStock,m.SoftStock,m.BeerStock);
        s.AdvanceWithoutSnapshot(80);r.AdvanceWithoutSnapshot(80);Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,r.CaptureSnapshot().AuthoritativeHash);
        var person=s.CaptureImmersion()!.People.Single(p=>p.AgentId==id);Assert.AreEqual(0,person.PendingDose);Assert.AreEqual(1070,person.Intoxication);Assert.AreEqual(80,person.Held!.ConsumedTicks);
        Assert.AreEqual(purchases,s.CaptureImmersion()!.Purchases.Length);Assert.AreEqual(stock,(s.CaptureImmersion()!.ChipsStock,s.CaptureImmersion()!.SoftStock,s.CaptureImmersion()!.BeerStock));
        var prep=s.CapturePreparation()!;typeof(GameSession).GetProperty("PreparationView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,prep with { People=prep.People.Select(p=>p.AgentId==id?p with { Departed=true }:p).ToArray() });
        Invoke(s,"CleanupImmersionDeparture");m=s.CaptureImmersion()!;Set(s,m with { People=m.People.Select(p=>p.AgentId==id?p with { PendingDose=500,Intoxication=9000,WarningTick=-1 }:p).ToArray() });
        Assert.IsFalse(s.ImmersionBoundaryOnNextTick);s.AdvanceWithoutSnapshot(80);person=s.CaptureImmersion()!.People.Single(p=>p.AgentId==id);Assert.AreEqual(500,person.PendingDose);Assert.AreEqual(9000,person.Intoxication);Assert.IsNull(person.Held);
    }
    [TestMethod]
    [DataRow(MedicalStage.Distress)]
    [DataRow(MedicalStage.Collapsed)]
    [DataRow(MedicalStage.Critical)]
    public void DepartureHeatUsesPersonClocksForBoundaryAndDeath(MedicalStage stage)
    {
        var s=Open();DepartureFixture(s);var medical=s.CaptureMedical()!;var id=BuildSession.LastGuest(s);
        var collapse=stage==MedicalStage.Collapsed?GameSession.PreparedDayTicks-799L:GameSession.PreparedDayTicks-2399L;var warning=stage==MedicalStage.Distress?GameSession.PreparedDayTicks-1599L:GameSession.PreparedDayTicks-3999L;var critical=GameSession.PreparedDayTicks-1599L;
        Invoke(s,"UpdatePerson",id,(Func<Person,Person>)(n=>n with { HealthStage=stage,HealthWarningTick=warning,HealthCollapseTick=stage==MedicalStage.Distress?-1:collapse,HealthCriticalTick=stage==MedicalStage.Critical?critical:-1 }));
        if(stage!=MedicalStage.Distress)Invoke(s,"UpdatePerson",id,(Func<Person,Person>)(n=>n with { Intent=MedicalIntent.Collapsed }));
        PositionFixture(s,id,new(120,125),"medical.departure-collapse");Assert.IsTrue(s.MedicalBoundaryOnNextTick);
        s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(stage==MedicalStage.Distress?MedicalStage.Collapsed:MedicalStage.Critical,s.CaptureMedical()!.Needs.Single(n=>n.AgentId==id).Stage);Assert.AreEqual(stage==MedicalStage.Critical,s.CaptureMedical()!.Fatal);
        if(stage==MedicalStage.Critical)Assert.IsTrue(s.CaptureLifecycleSnapshot()!.Casualties.Single().Cause.Contains($"critical tick {critical}"));
        Restore(s);
    }
    [TestMethod]
    public void CoexistingInjuryKeepsOriginalDeadlineWhenAlcoholCrossesCollapseDuration()
    {
        // Labelled ownership fixture; the established disorder suite proves the causal fight/injury chain.
        var s=Open();typeof(GameSession).GetProperty(nameof(GameSession.CurrentTick))!.SetValue(s,4000L);var id=s.CaptureDisorder()!.People.First().AgentId;
        Invoke(s,"UpdatePerson",id,(Func<Person,Person>)(n=>n with { HealthStage=MedicalStage.Collapsed,HealthCollapseTick=3900,Intent=MedicalIntent.Collapsed,Reason="Existing confrontation injury" }));
        Invoke(s,"UpdatePerson",id,(Func<Person,Person>)(p=>p with { ConductStage=DisorderStage.Injured,InjuryTick=3900,ConductStageTick=3900 }));
        var m=s.CaptureImmersion()!;Set(s,m with { People=m.People.Select(p=>p.AgentId==id?p with { Intoxication=10000,WarningTick=2399,SevereTicks=1599 }:p).ToArray() });Invoke(s,"AdvanceImmersion");
        Assert.AreEqual(3900L,s.CaptureDisorder()!.People.Single(p=>p.AgentId==id).InjuryTick);Assert.AreEqual(3900L,s.CaptureMedical()!.Needs.Single(p=>p.AgentId==id).CollapseTick);Assert.AreEqual(-1L,s.CaptureImmersion()!.People.Single(p=>p.AgentId==id).CollapseTick);
        Assert.IsFalse(s.ImmersionBoundaryOnNextTick,"Blocked overlap must not create a repeated boundary save.");
        var response=new MedicResponse(s.CaptureMedical()!.MedicId,MedicalResponseStage.Treating,id,3500,"Existing injury treatment",3499);
        Assert.AreEqual(false,typeof(GameSession).GetMethod("IntoxicationCareOwns",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(s,[response]),"Alcohol care must not clear an unresolved confrontation injury.");
    }
}

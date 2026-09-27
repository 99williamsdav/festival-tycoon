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
        Accept(s,new SetProgrammeCommand(["act.meadow-lanterns","act.barnstorm-circuit","act.field-frequency"]));
        Accept(s,new AcceptPreparationOfferCommand("staff.steward"));
        Accept(s,new StartPreparedEditionCommand());
    }
    private static void IgnoredResponseFatalFixture(GameSession s)
    {
        // Explicit headless fixture: inherited alcohol warning/collapse/death chain,
        // with normal deadlines and no medic response. No normal gameplay control.
        var prep=s.CapturePreparation()!;
        typeof(GameSession).GetField("_preparation",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,prep with {People=prep.People.Select(p=>p with {Admitted=true}).ToArray()});
        var immersion=s.CaptureImmersion()!;var id=prep.People.First(p=>p.Role==ProtectedPersonRole.Guest).AgentId;
        typeof(GameSession).GetField("_immersion",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,immersion with {People=immersion.People.Select(p=>p.AgentId==id?p with {Intoxication=10000}:p).ToArray()});
        s.AdvanceWithoutSnapshot(4000);Assert.AreEqual(PreparationStatus.Failed,s.PreparedStatus);
    }
    [TestMethod]
    public void ThreeDistinctUniformChoicesUseSeparateSavedEntropyAndOneReroll()
    {
        var counts=PerkCatalogue.All.ToDictionary(p=>p.Id,p=>0);
        for(ulong seed=0;seed<800;seed++)
        {
            var s=GameSession.CreatePerkCampaign(seed);var p=s.CapturePerks()!;
            Assert.AreEqual(3,p.Hand.Distinct().Count());foreach(var id in p.Hand)counts[id]++;
            var legacy=GameSession.CreateImmersionCampaign(seed);
            Assert.AreEqual(JsonSerializer.Serialize(legacy.CapturePersistenceSnapshot().RandomStreams),JsonSerializer.Serialize(s.CapturePersistenceSnapshot().RandomStreams));
            if(seed<10){s=Restored(s);Assert.AreEqual(JsonSerializer.Serialize(p),JsonSerializer.Serialize(s.CapturePerks()));Accept(s,new RerollPerksCommand(p.DraftAttempt,p.Cursor));var rerolled=s.CapturePerks()!;s=Restored(s);Assert.AreEqual(JsonSerializer.Serialize(rerolled),JsonSerializer.Serialize(s.CapturePerks()));Assert.IsFalse(Send(s,new RerollPerksCommand(rerolled.DraftAttempt,rerolled.Cursor)).IsAccepted);}
        }
        foreach(var count in counts.Values)Assert.IsTrue(count is >230 and <380,$"Distribution {count}/2400 outside broad equal-weight diagnostic.");
    }
    [TestMethod]
    public void PendingDraftBlocksPreparationAndStaleUnknownDuplicateCommandsArePure()
    {
        var s=GameSession.CreatePerkCampaign(123);var p=s.CapturePerks()!;
        var hash=s.CaptureSnapshot().AuthoritativeHash;
        foreach(var c in new SessionCommand[]{new AcceptPreparationOfferCommand("staff.steward"),new PlaceWaterPointCommand(new(80,126)),new StartPreparedEditionCommand(),new ChoosePerkCommand(2,p.Cursor,p.Hand[0]),new ChoosePerkCommand(1,p.Cursor,"unknown"),new SkipPerksCommand(1,p.Cursor)}){Assert.IsFalse(Send(s,c).IsAccepted);Assert.AreEqual(hash,s.CaptureSnapshot().AuthoritativeHash);}
        Accept(s,new RerollPerksCommand(1,p.Cursor));Assert.IsFalse(Send(s,new ChoosePerkCommand(1,p.Cursor,p.Hand[0])).IsAccepted);
        p=s.CapturePerks()!;Accept(s,new ChoosePerkCommand(1,p.Cursor,p.Hand[0]));Assert.IsFalse(Send(s,new ChoosePerkCommand(1,p.Cursor,p.Hand[0])).IsAccepted);
        Assert.IsFalse(Send(s,new ApplyWaterFoundationEffectCommand("water.tower")).IsAccepted);Assert.IsFalse(Send(s,new ApplyStaffFoundationEffectCommand("staff.role-training")).IsAccepted);Restored(s);
    }
    [TestMethod]
    public void FullHandExcludesOwnedAndAllowsRepeatedRemainingThreeReplacementAndSkip()
    {
        var s=GameSession.CreatePerkCampaign(123);FixturePerks(s,PerkCatalogue.All.Take(5).Select(p=>p.Id).ToArray());FixtureDraft(s);
        var p=s.CapturePerks()!;Assert.IsFalse(p.Hand.Any(p.Equipped.Contains));
        var before=p.Hand.Order().ToArray();Accept(s,new RerollPerksCommand(1,p.Cursor));p=s.CapturePerks()!;CollectionAssert.AreEqual(before,p.Hand.Order().ToArray());
        var original=Restored(s);var lost=p.Equipped[0];Accept(s,new ChoosePerkCommand(1,p.Cursor,p.Hand[0],lost));Assert.AreEqual(5,s.CapturePerks()!.Equipped.Length);Assert.IsFalse(s.CapturePerks()!.Equipped.Contains(lost));Restored(s);
        Accept(original,new SkipPerksCommand(1,p.Cursor));CollectionAssert.AreEqual(p.Equipped,original.CapturePerks()!.Equipped);Restored(original);
    }
    [TestMethod]
    public void SlotPerksAddNoWorkersAndPaidContractsCannotBypassRemovedSlot()
    {
        var s=GameSession.CreatePerkCampaign(1);var people=s.CapturePreparation()!.People.Length;var cash=s.CaptureSnapshot().FestivalFinances.Single().CashPennies;
        FixturePerks(s,"doctors-orders","extra-pair-of-hands");Assert.AreEqual(people,s.CapturePreparation()!.People.Length);Assert.AreEqual(cash,s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        Accept(s,new AcceptPreparationOfferCommand("staff.extra-medic"));Accept(s,new AcceptPreparationOfferCommand("staff.extra-steward"));Assert.AreEqual(cash-6000,s.CaptureSnapshot().FestivalFinances.Single().CashPennies);Restored(s);
    }
    [TestMethod]
    public void SeparateRoleTrainingDerivesActualAbilitiesAndSavedWalkingSpeeds()
    {
        foreach(var role in new[]{ResponseRole.Medic,ResponseRole.Steward})
        {
            var s=GameSession.CreatePerkCampaign(123);var before=s.GetResponseStaff().ToArray();FixturePerks(s,role==ResponseRole.Medic?"first-responders":"smooth-operators");
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
    public void TowerTapRemovalRestoresBaselineAndNeverRemovesMain()
    {
        var s=GameSession.CreatePerkCampaign(1);FixturePerks(s,"another-round","high-pressure");Accept(s,new PlaceWaterPointCommand(new(80,126)));
        Assert.AreEqual(2,s.CaptureWaterPoints().Count);Assert.AreEqual(GameSession.MedicalDrinkThirstPerTickFor(5)+4,s.EffectiveMedicalDrinkThirstPerTickFor(5));
        Assert.IsFalse(Send(s,new PlaceWaterPointCommand(new(104,112))).IsAccepted);FixturePerks(s,"thirsty-crowd");
        Assert.AreEqual("water.main",s.CaptureWaterPoints().Single().Id);Assert.AreEqual(GameSession.MedicalDrinkThirstPerTickFor(5),s.EffectiveMedicalDrinkThirstPerTickFor(5));Restored(s);
    }
    [TestMethod]
    public void ThirstPerkChangesOnlyGuestsByTenPercentAndWaterHappinessRequiresActualService()
    {
        var baseline=GameSession.CreatePerkCampaign(123);FixturePerks(baseline);Start(baseline);
        var s=GameSession.CreatePerkCampaign(123);FixturePerks(s,"thirsty-crowd","something-in-the-water");Start(s);
        baseline.AdvanceWithoutSnapshot(40);s.AdvanceWithoutSnapshot(40);
        foreach(var need in s.CaptureMedical()!.Needs){var old=baseline.CaptureMedical()!.Needs.Single(n=>n.AgentId==need.AgentId);Assert.AreEqual(old.Thirst+(need.Profile==MedicalNeedProfile.Guest?1:0),need.Thirst);}
        CollectionAssert.AreEqual(baseline.CapturePreparation()!.People.Select(p=>p.Satisfaction).ToArray(),s.CapturePreparation()!.People.Select(p=>p.Satisfaction).ToArray());Restored(s);
    }
    [TestMethod]
    public void SaveFailureRetainsHandCursorMoneyAndIdsAndSuccessfulChoiceReloadsExactly()
    {
        var path=Path.Combine(Path.GetTempPath(),"festival-perks-"+Guid.NewGuid());var compat=new SaveCompatibility("perk-test","content","rules");
        var s=GameSession.CreatePerkCampaign(123);var p=s.CapturePerks()!;var hash=s.CaptureSnapshot().AuthoritativeHash;
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
        var s=GameSession.CreatePerkCampaign(123);var snap=s.CapturePersistenceSnapshot();var p=s.CapturePerks()!;
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
    public void LegacyNullableStateIsOmittedAndExactCanonicalAndEffectsRemainUnchanged()
    {
        var s=GameSession.CreateImmersionCampaign(123);Accept(s,new ApplyStaffFoundationEffectCommand("staff.role-training"));Accept(s,new ApplyWaterFoundationEffectCommand("water.tower"));Accept(s,new PlaceWaterPointCommand(new(80,126)));
        var snap=s.CapturePersistenceSnapshot();Assert.IsNull(snap.Perks);Assert.IsFalse(JsonSerializer.Serialize(snap).Contains("Perks"));var checksum=SaveFileAdapter.ComputePayloadChecksum(snap);s=Restored(s);Assert.AreEqual(checksum,SaveFileAdapter.ComputePayloadChecksum(s.CapturePersistenceSnapshot()));Assert.IsTrue(s.CapturePreparation()!.RespondersUpgraded);Assert.AreEqual(2,s.CaptureWaterPoints().Count);
    }
    [TestMethod]
    public void ActualCommittedLegacyHudSaveRetainsChecksumHashCanonicalEffectsAndFileBytes()
    {
        var repo=AppContext.BaseDirectory;while(!File.Exists(Path.Combine(repo,"ROGUELIKE_DESIGN.md")))repo=Directory.GetParent(repo)!.FullName;
        var path=Path.Combine(repo,"reports","evidence","R0.05f","final-1280x720","saves","manual-preparation.ftsave");
        Assert.IsTrue(File.Exists(path),path);var bytes=File.ReadAllBytes(path);
        using var zipped=new System.IO.Compression.GZipStream(new MemoryStream(bytes),System.IO.Compression.CompressionMode.Decompress);
        var envelope=JsonSerializer.Deserialize<SaveEnvelopeV1>(zipped,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var original=envelope.Payload;Assert.IsNull(original.Perks);var loaded=GameSession.Restore(original);Assert.IsTrue(loaded.IsSuccess,loaded.Error);
        Assert.AreEqual(envelope.Header.PayloadChecksum,SaveFileAdapter.ComputePayloadChecksum(original));
        Assert.AreEqual(original.AuthoritativeHash,loaded.Session!.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(SaveFileAdapter.ComputePayloadChecksum(original),SaveFileAdapter.ComputePayloadChecksum(loaded.Session.CapturePersistenceSnapshot()));
        Assert.AreEqual(JsonSerializer.Serialize(original),JsonSerializer.Serialize(loaded.Session.CapturePersistenceSnapshot()));
        CollectionAssert.AreEqual(bytes,File.ReadAllBytes(path));
    }
    [TestMethod]
    public void ActualFatalHearingRetryKeepsPerksDrawsOnceExpiresContractsAndTerminalResetPreservesHistory()
    {
        var s=GameSession.CreatePerkCampaign(20260922);FixturePerks(s,"doctors-orders","first-responders","another-round","high-pressure");
        Accept(s,new AcceptPreparationOfferCommand("staff.extra-medic"));Accept(s,new PlaceWaterPointCommand(new(80,126)));Start(s);
        IgnoredResponseFatalFixture(s);s=Restored(s);
        var casualty=JsonSerializer.Serialize(s.CaptureLifecycleSnapshot()!.Casualties);var equipped=s.CapturePerks()!.Equipped;
        var conceded=Restored(s);Accept(conceded,new ConcedeCouncilHearingCommand());conceded=Restored(conceded);Assert.IsTrue(conceded.CapturePerks()!.Ended);Assert.AreEqual(0,conceded.CapturePerks()!.Equipped.Length);Assert.AreEqual(casualty,JsonSerializer.Serialize(conceded.CaptureLifecycleSnapshot()!.Casualties));
        var frozenHash=conceded.CaptureSnapshot().AuthoritativeHash;conceded.AdvanceWithoutSnapshot(1000);Assert.IsFalse(Send(conceded,new SpendCouncilFavourCommand()).IsAccepted);Assert.IsFalse(Send(conceded,new PlaceWaterPointCommand(new(104,112))).IsAccepted);Assert.AreEqual(frozenHash,conceded.CaptureSnapshot().AuthoritativeHash);Assert.AreEqual(0,GameSession.CreatePerkCampaign(20260922).CapturePerks()!.Equipped.Length);
        Accept(s,new SpendCouncilFavourCommand());Assert.AreEqual(2,s.CapturePerks()!.DraftAttempt);Assert.IsTrue(s.CapturePerks()!.Pending);CollectionAssert.AreEqual(equipped,s.CapturePerks()!.Equipped);Assert.AreEqual(80000L,s.CaptureSnapshot().FestivalFinances.Single().CashPennies);Assert.AreEqual(0,s.CapturePreparation()!.WorkContracts.Length);Assert.IsFalse(s.GetResponseStaff().Any(p=>p.Name=="Avery Brooks"));s=Restored(s);
        var draft=s.CapturePerks()!;Assert.IsFalse(Send(s,new SpendCouncilFavourCommand()).IsAccepted);Accept(s,new ChoosePerkCommand(2,draft.Cursor,draft.Hand[0]));Accept(s,new AcceptPreparationOfferCommand("staff.extra-medic"));Assert.AreEqual(77000L,s.CaptureSnapshot().FestivalFinances.Single().CashPennies);Restored(s);
        Start(s);IgnoredResponseFatalFixture(s);Assert.AreEqual(HearingStatus.LostNoFavour,s.CaptureLifecycleSnapshot()!.Hearings.Last().Status);Assert.AreEqual(2,s.CaptureLifecycleSnapshot()!.Casualties.Count);Assert.IsTrue(s.CapturePerks()!.Ended);s=Restored(s);Assert.AreEqual(0,s.CapturePerks()!.Equipped.Length);
        // Labelled removal fixture emulates five-perk replacement after retry, preserving paid historical profiles.
        var removal=GameSession.CreatePerkCampaign(20260922);FixturePerks(removal,"doctors-orders");Accept(removal,new AcceptPreparationOfferCommand("staff.extra-medic"));Start(removal);IgnoredResponseFatalFixture(removal);Accept(removal,new SpendCouncilFavourCommand());FixturePerks(removal,"thirsty-crowd");Assert.IsFalse(Send(removal,new AcceptPreparationOfferCommand("staff.extra-medic")).IsAccepted);Restored(removal);
    }
    [TestMethod]
    public void EveryPerkRemovalRestoresItsBoundedEffectAndOwnedExclusion()
    {
        foreach(var perk in PerkCatalogue.All)
        {
            var s=GameSession.CreatePerkCampaign(123);FixturePerks(s,perk.Id);FixtureDraft(s);Assert.IsFalse(s.CapturePerks()!.Hand.Contains(perk.Id));FixturePerks(s);
            var legacy=GameSession.CreateImmersionCampaign(123);Assert.AreEqual(JsonSerializer.Serialize(legacy.CapturePreparation()),JsonSerializer.Serialize(s.CapturePreparation()));CollectionAssert.AreEqual(legacy.GetResponseStaff().ToArray(),s.GetResponseStaff().ToArray());
        }
    }
    [TestMethod]
    public void ActualGuestFreeWaterTicksImproveSatisfactionAndResumeExactlyWhileStaffDoNotGain()
    {
        var s=GameSession.CreatePerkCampaign(20260922);FixturePerks(s,"something-in-the-water");Start(s);
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
        typeof(GameSession).GetField("_preparation",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,original with {People=original.People.Select(p=>p.AgentId==owner?p with {Satisfaction=10000}:p).ToArray()});
        s.AdvanceWithoutSnapshot(1);Assert.AreEqual(10000,s.CapturePreparation()!.People.Single(p=>p.AgentId==owner).Satisfaction);Restored(s);
    }
    [TestMethod]
    public void PairedModestSynergyDiagnosticKeepsBaselineCountersAndExactContinuationBelowFifty()
    {
        var baseline=GameSession.CreateImmersionCampaign(20260922);Start(baseline);
        var s=GameSession.CreatePerkCampaign(20260922);FixturePerks(s,"thirsty-crowd","something-in-the-water");Start(s);
        var watch=System.Diagnostics.Stopwatch.StartNew();baseline.AdvanceWithoutSnapshot(4000);s.AdvanceWithoutSnapshot(4000);watch.Stop();
        Assert.AreEqual(PreparationStatus.Running,baseline.PreparedStatus);Assert.AreEqual(PreparationStatus.Running,s.PreparedStatus);
        Assert.IsTrue(s.CapturePreparation()!.People.Length<=50);Assert.AreEqual("water.main",s.CaptureWaterPoints().Single().Id);Assert.IsNotNull(s.CaptureMedical());Assert.IsNotNull(s.CaptureDisorder());
        Assert.IsTrue(s.CapturePreparation()!.People.All(p=>p.Satisfaction is >=0 and <=10000));var restored=Restored(s);s.AdvanceWithoutSnapshot(100);restored.AdvanceWithoutSnapshot(100);Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,restored.CaptureSnapshot().AuthoritativeHash);
        Console.WriteLine($"PERK_DIAGNOSTIC paired_ticks=4000 people={s.CapturePreparation()!.People.Length} elapsed_ms={watch.Elapsed.TotalMilliseconds:F2} baseline_status={baseline.PreparedStatus} synergy_status={s.PreparedStatus} guest_growth_extra=10percent hash={s.CaptureSnapshot().AuthoritativeHash}");
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
        var s=GameSession.CreatePerkCampaign(20260922);FixturePerks(s,"something-in-the-water");Accept(s,new PurchaseImmersionStarterStockCommand());Start(s);s.AdvanceWithoutSnapshot(2000);
        var m=s.CaptureMedical()!;var id=s.CaptureDisorder()!.SecurityId;var point=s.CaptureWaterPoints().Single();var front=GameSession.WaterPointServiceCell(point);
        PositionServiceFixture(s,id,front,"medical.water");
        typeof(GameSession).GetField("_medical",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,m with {WaterQueue=[id],WaterOverflow=[],WaterOwnerId=id,WaterDrinkTicks=1,MainWaterQueueCells=[front],Needs=m.Needs.Select(n=>n.AgentId==id?n with {Thirst=1000,Intent=MedicalIntent.Drinking,WaterPointId="water.main",QueueSlot=0}:n with {QueueSlot=null,WaterPointId="water.main",Intent=n.Intent is MedicalIntent.Drinking or MedicalIntent.SeekWater?MedicalIntent.WatchShow:n.Intent}).ToArray()});
        var before=s.CapturePreparation()!.People.Single(p=>p.AgentId==id).Satisfaction;s.AdvanceWithoutSnapshot(1);Assert.AreEqual(2,s.CaptureWaterPoints().Single().DrinkTicks);Assert.AreEqual(before,s.CapturePreparation()!.People.Single(p=>p.AgentId==id).Satisfaction);Restored(s);
        var paid=GameSession.CreatePerkCampaign(20260922);FixturePerks(paid,"something-in-the-water");Accept(paid,new PurchaseImmersionStarterStockCommand());Start(paid);
        var baseline=Restored(paid);typeof(GameSession).GetField("_perks",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(baseline,null);
        var guest=paid.CapturePreparation()!.People.First(p=>p.Role==ProtectedPersonRole.Guest).AgentId;
        foreach(var target in new[]{paid,baseline})typeof(GameSession).GetMethod("CompleteImmersionSale",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(target,[guest,ImmersionProduct.SoftDrink]);
        paid.AdvanceWithoutSnapshot(100);baseline.AdvanceWithoutSnapshot(100);
        Assert.AreEqual(baseline.CapturePreparation()!.People.Single(p=>p.AgentId==guest).Satisfaction,paid.CapturePreparation()!.People.Single(p=>p.AgentId==guest).Satisfaction);
        Assert.AreEqual(ImmersionProduct.SoftDrink,paid.CaptureImmersion()!.People.Single(p=>p.AgentId==guest).Held!.Product);Restored(paid);
    }
    [TestMethod]
    public void PerkTrainedPaidMedicTravelsTreatsAndResumesTheExactJob()
    {
        var s=GameSession.CreatePerkCampaign(20260922);FixturePerks(s,"doctors-orders","first-responders");Accept(s,new AcceptPreparationOfferCommand("staff.extra-medic"));Start(s);s.AdvanceWithoutSnapshot(2000);
        var m=s.CaptureMedical()!;var id=m.AtRiskGuestId;var worker=s.GetResponseStaff().Single(p=>p.Name=="Avery Brooks");
        typeof(GameSession).GetField("_medical",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,m with {Stage=MedicalStage.Distress,WarningTick=s.CurrentTick,Needs=m.Needs.Select(n=>n.AgentId==id?n with {Thirst=9500,HeatExposure=8500,Stage=MedicalStage.Distress,WarningTick=s.CurrentTick,Intent=MedicalIntent.AwaitMedic}:n).ToArray()});
        Accept(s,new MedicalCommand(id,MedicalAction.DispatchMedic,worker.AgentId));Assert.AreEqual(MedicalResponseStage.Travelling,s.GetMedicResponses().Single(p=>p.WorkerId==worker.AgentId).Stage);s=Restored(s);
        while(s.GetMedicResponses().Single(p=>p.WorkerId==worker.AgentId).Stage==MedicalResponseStage.Travelling && s.CurrentTick<5000)s.AdvanceWithoutSnapshot(1);
        var job=s.GetMedicResponses().Single(p=>p.WorkerId==worker.AgentId);Assert.AreEqual(MedicalResponseStage.Treating,job.Stage);var restored=Restored(s);s.AdvanceWithoutSnapshot(worker.TreatmentTicks);restored.AdvanceWithoutSnapshot(worker.TreatmentTicks);Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,restored.CaptureSnapshot().AuthoritativeHash);Assert.AreEqual(MedicalResponseStage.Completed,s.GetMedicResponses().Single(p=>p.WorkerId==worker.AgentId).Stage);Restored(s);
    }
}

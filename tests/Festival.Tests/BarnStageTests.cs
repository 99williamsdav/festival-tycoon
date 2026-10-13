using Festival.Simulation;
using Festival.Simulation.Fixtures;

namespace Festival.Tests;

[TestClass]
public sealed class BarnStageTests
{
    [TestMethod]
    public void TwoFrontOpeningsConnectTheInteriorButWallsAndDeckRimStaySolid()
    {
        var grid=new TraversalGrid(NavigationFixture.CreateLowerWitteringTerrain());
        foreach(var place in BarnLayout.Stage.ListeningPlaces)
        {
            Assert.IsTrue(grid.Get(place).IsWalkable);
            Assert.IsTrue(BarnLayout.Covered(place)); Assert.IsFalse(BarnLayout.Circulation(place));
            Assert.IsTrue(DeterministicPathfinder.FindPath(grid,new(128,189),place).Found,$"Guest place {place}");
        }
        foreach(var x in new[]{0,-7_650}) Assert.IsTrue(DeterministicPathfinder.FindPath(grid,BarnLayout.Cell(x,7_750),BarnLayout.Cell(x,4_750)).Found);
        foreach(var cell in new[]{BarnLayout.Cell(-10_000,0),BarnLayout.Cell(10_000,0),BarnLayout.Cell(0,-6_250),BarnLayout.Cell(5_000,6_250),BarnLayout.Cell(4_250,-1_250)})
            Assert.IsFalse(grid.Get(cell).IsWalkable,$"Wall/rim {cell}");
        for(var role=0;role<3;role++)
        {
            var stage=BarnLayout.Stage;
            Assert.IsTrue(stage.Deck(stage.PerformerMarks[role])); Assert.IsTrue(stage.Stairs(stage.StairCells[role]));
            var path=DeterministicPathfinder.FindPath(grid,stage.AccessCells[role],stage.PerformerMarks[role]);
            Assert.IsTrue(path.Found && path.Path.Any(stage.Stairs) && path.Path.SkipWhile(c=>!stage.Stairs(c)).All(c=>stage.Deck(c)||stage.Stairs(c)),"After the foot, climb must use stairs/deck only.");
        }
        for(var i=0;i<9;i++)
        {var stage=BarnLayout.Stage;Assert.IsTrue(grid.Get(stage.BackstagePlace(i)).IsWalkable);Assert.IsFalse(BarnLayout.Circulation(stage.BackstagePlace(i)));Assert.IsTrue(DeterministicPathfinder.FindPath(grid,stage.ArrivalStart(i),stage.BackstagePlace(i)).Found);}
    }
    [TestMethod]
    public void InteriorOnlyCoverInitializesWithoutPlacementsAndSurvivesNormalSave()
    {
        var bare=new GameSession(7,new(7));
        Assert.IsTrue(bare.CoveredCell(BarnLayout.Cell(0,0)));
        foreach(var cell in new[]{BarnLayout.Cell(0,6_250),BarnLayout.Cell(0,7_250),BarnLayout.Cell(9_750,0)})Assert.IsFalse(bare.CoveredCell(cell));
        var normal=BuildSession.Started();
        var restored=GameSession.Restore(normal.CapturePersistenceSnapshot());Assert.IsTrue(restored.IsSuccess,restored.Error);
        Assert.IsTrue(restored.Session!.CoveredCell(BarnLayout.Cell(7_050,-1_500)));
        Assert.IsFalse(restored.Session.CoveredCell(BarnLayout.Cell(0,6_250)));
    }
    [TestMethod]
    public void ControlledBarnHasDistinctProgrammeAndPowerWithoutChangingNormalAllocation()
    {
        var fixture=BarnStageFixture.Started(20260922);
        CollectionAssert.AreEqual(new[]{FestivalStages.MainId,BarnLayout.Stage.Id},fixture.Stages.Select(s=>s.Id).ToArray());
        Assert.AreEqual(BarnLayout.GeneratorX,fixture.CaptureStageGenerator(BarnLayout.Stage.Id)!.XMillimetres);
        Assert.IsNotNull(fixture.StageEngineerId(BarnLayout.Stage.Id));
        Assert.ThrowsExactly<InvalidOperationException>(()=>fixture.CapturePersistenceSnapshot());
        CollectionAssert.AreEqual(new[]{FestivalStages.MainId},GameSession.CreateBuildCampaign(20260922).Stages.Select(s=>s.Id).ToArray());
        CollectionAssert.AreEqual(new[]{FestivalStages.MainId,FestivalStages.PondId},GameSession.CreateBuildCampaign(20260922,true).Stages.Select(s=>s.Id).ToArray());
        CollectionAssert.AreEqual(new[]{FestivalStages.MainId,FestivalStages.PondId},GameSession.CreateDevelopmentFestival(20260922,2).Stages.Select(s=>s.Id).ToArray());
        Assert.AreEqual(2,FestivalStages.All.Count);
        Assert.IsTrue(GameSession.OnStageCells(FestivalStages.MainOnly,BarnLayout.Stage.PerformerMarks[0]),"Physical deck stays raised without activating a programme.");
    }
    [TestMethod]
    public void CuttingBarnPowerInterruptsOnlyItsOwnSet()
    {
        var session=BarnStageFixture.Started(20260922);session.AdvanceWithoutSnapshot(9_600);
        Assert.AreEqual(LiveSetStage.Live,session.CaptureLivePerformance(BarnLayout.Stage.Id)!.Stage);
        Assert.AreEqual(LiveSetStage.Live,session.CaptureLivePerformance(FestivalStages.MainId)!.Stage);
        BuildSession.Accept(session,new StageGeneratorCommand(BarnLayout.Stage.Id,StageGeneratorAction.Isolate));session.AdvanceWithoutSnapshot(80);
        Assert.AreEqual(LiveSetStage.Interrupted,session.CaptureLivePerformance(BarnLayout.Stage.Id)!.Stage);
        Assert.AreEqual(LiveSetStage.Live,session.CaptureLivePerformance(FestivalStages.MainId)!.Stage);
        Assert.IsFalse(session.StagePoweredAt(BarnLayout.Stage.Id));Assert.IsTrue(session.StagePoweredAt(FestivalStages.MainId));
    }
    [TestMethod]
    public void ActualRainWetnessSpareTheIndoorGuestButSoaksTheOutsideApron()
    {
        var session=BarnStageFixture.Started(20260922);session.AdvanceWithoutSnapshot(3_200);
        var people=session.CapturePreparation()!.People.Where(p=>p.Role==ProtectedPersonRole.Guest&&p.Admitted&&!p.Departed).Take(2).ToArray();
        var position=typeof(StaffAutonomyTests).GetMethod("PositionFixture",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
        position.Invoke(null,[session,people[0].AgentId,BarnLayout.Cell(0,0)]);
        position.Invoke(null,[session,people[1].AgentId,BarnLayout.Cell(0,6_750)]);
        var access=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        // Explicit weather-input fixture: use the real exposure/wetness update, with no progression/balance claim.
        typeof(GameSession).GetField("_weather",access)!.SetValue(session,new WeatherSnapshot(1,WeatherKind.Rain,[new(0,100_000,RainIntensity.Heavy)],[],[]));
        typeof(GameSession).GetMethod("AdvanceWeather",access)!.Invoke(session,[]);
        Assert.AreEqual(0,session.WetnessOf(people[0].AgentId));
        Assert.IsTrue(session.WetnessOf(people[1].AgentId)>0);
    }
    [TestMethod]
    public void StewardPhysicallyReachesAnIndoorFightThroughTheOpenLongSide()
    {
        var session=BarnStageFixture.Started(20260922);session.AdvanceWithoutSnapshot(3_200);
        var guests=session.CapturePreparation()!.People.Where(p=>p.Role==ProtectedPersonRole.Guest&&p.Admitted&&!p.Departed).Take(2).Select(p=>p.AgentId).ToArray();
        var privateInstance=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var position=typeof(StaffAutonomyTests).GetMethod("PositionFixture",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
        var disorder=session.CaptureDisorder()!;var worker=disorder.SecurityId;
        position.Invoke(null,[session,guests[0],BarnLayout.Cell(-4_750,-250)]);
        position.Invoke(null,[session,guests[1],BarnLayout.Cell(-4_250,250)]);
        position.Invoke(null,[session,worker,BarnLayout.Cell(0,8_750)]);
        var medical=session.CaptureMedical()!;
        typeof(GameSession).GetProperty("MedicalView",privateInstance)!.SetValue(session,medical with {Needs=medical.Needs.Select(n=>guests.Contains(n.AgentId)?n with {Thirst=0,HeatExposure=0,Intent=MedicalIntent.WatchShow}:n).ToArray()});
        typeof(GameSession).GetProperty("DisorderView",privateInstance)!.SetValue(session,disorder with {
            Stewards=disorder.Stewards.Select(s=>s.WorkerId==worker?s with{Stage=SecurityResponseStage.None,TargetId=null}:s).ToArray(),
            People=disorder.People.Select(p=>guests.Contains(p.AgentId)?p with {Stage=DisorderStage.Argument,Grievance=DisorderGrievance.MusicCutoff,Pressure=8000,StageTick=session.CurrentTick}:p).ToArray()});
        typeof(GameSession).GetMethod("BeginDisorderFight",privateInstance)!.Invoke(session,[guests[0],guests[1],"labelled:barn-response-fixture"]);
        BuildSession.Accept(session,new DisorderCommand(DisorderAction.DispatchSecurity,guests[0],worker));
        var previous=session.CaptureObservation().NavigationAgents.Single(n=>n.Id.Value==worker);var crossedDoor=false;
        for(var i=0;i<1_600;i++)
        {
            session.AdvanceWithoutSnapshot(1);var current=session.CaptureObservation().NavigationAgents.Single(n=>n.Id.Value==worker);
            Assert.IsTrue(TraversalSweep.IsWalkable(session.TraversalGrid!,previous.XMillimetres,previous.ZMillimetres,current.XMillimetres,current.ZMillimetres));
            var cell=TraversalGrid.WorldToCell(current.XMillimetres,current.ZMillimetres);var local=BarnLayout.Local(cell);
            if(local.Z is >=5_750 and <=6_750)crossedDoor|=local.X is >=-3_500 and <=3_500 || local.X is >=-8_250 and <=-7_000;
            previous=current;
            if(session.CaptureDisorder()!.Incidents.Last().HandlingAttempt is {Outcome:FightHandlingOutcome.Handling})
            {Assert.IsTrue(crossedDoor);Assert.IsTrue(BarnLayout.Interior(current.XMillimetres,current.ZMillimetres));return;}
        }
        Assert.Fail("Steward did not reach actual indoor handling via the open doorway.");
    }
    [TestMethod]
    public void AnyPhysicallyPresentRoleCountsEvenBeforeTheBandPlays()
    {
        var session=BarnStageFixture.Started(20260922);session.AdvanceWithoutSnapshot(3_200);
        var nav=(System.Collections.IDictionary)typeof(GameSession).GetField("_navigationAgents",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(session)!;
        void Stand(ulong id,int x,int z)
        {var state=nav[new EntityId(id)]!;state.GetType().GetProperty("XMillimetres")!.SetValue(state,x);state.GetType().GetProperty("ZMillimetres")!.SetValue(state,z);}
        // Explicit occupancy-only fixture: no traversal claim is made for these placements.
        foreach(System.Collections.DictionaryEntry item in nav)Stand(((EntityId)item.Key).Value,20_000,20_000);
        Assert.AreEqual(0,session.BarnOccupants);
        var people=session.CapturePreparation()!.People;
        foreach(var person in new[]{people.First(p=>p.Role==ProtectedPersonRole.Guest&&p.Admitted&&!p.Departed),people.First(p=>p.Role==ProtectedPersonRole.Performer),people.First(p=>p.Role==ProtectedPersonRole.Staff)})
        {Stand(person.AgentId,0,-23_000);Assert.AreEqual(1,session.BarnOccupants,person.Role.ToString());Stand(person.AgentId,0,-16_750);Assert.AreEqual(0,session.BarnOccupants);}
    }
    [TestMethod]
    public void OccupiedListeningPlacesLeaveBothDoorsUsableForOpposingRealWalkers()
    {
        // Explicit starting positions only. From here all movement, yielding and goal reservations are production navigation.
        var listening=BarnLayout.Stage.ListeningPlaces.ToArray();
        var walks=listening.SkipLast(2).Select(c=>(From:c,To:c)).Concat(new (GridCell From,GridCell To)[]{
            (BarnLayout.Cell(-7_750,3_250),BarnLayout.Cell(-9_250,10_250)),
            (BarnLayout.Cell(0,3_250),BarnLayout.Cell(2_250,10_250)),
            (BarnLayout.Cell(-7_750,10_750),listening[^1]),
            (BarnLayout.Cell(0,10_750),listening[^2])}).ToArray();
        var session=new GameSession(77);
        var created=session.Execute(new(new(1),session.CampaignId,session.Phase,session.CurrentTick,session.NextSubmissionSequence,null,
            new InitializeNavigationFixtureCommand(walks[0].From,NavigationFixture.CreateLowerWitteringTerrain())));
        Assert.IsTrue(created.IsAccepted);
        var access=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var states=(System.Collections.IDictionary)typeof(GameSession).GetField("_navigationAgents",access)!.GetValue(session)!;
        var stateType=states[created.TargetId!.Value]!.GetType();var ids=new List<EntityId>{created.TargetId.Value};
        for(var i=1;i<walks.Length;i++)
        {
            var id=new EntityId(created.TargetId.Value.Value+(ulong)i);var point=TraversalGrid.CellCentre(walks[i].From);
            var state=Activator.CreateInstance(stateType,nonPublic:true)!;
            void Set(string name,object value)=>stateType.GetProperty(name)!.SetValue(state,value);
            Set("Id",id);Set("XMillimetres",point.XMillimetres);Set("ZMillimetres",point.ZMillimetres);
            Set("SegmentOriginXMillimetres",point.XMillimetres);Set("SegmentOriginZMillimetres",point.ZMillimetres);Set("Action",AgentNavigationAction.Idle);
            states.Add(id,state);ids.Add(id);
        }
        var apply=typeof(GameSession).GetMethod("ApplyAgentDestination",access)!;
        for(var i=0;i<walks.Length;i++)apply.Invoke(session,[ids[i],new SetAgentDestinationCommand(walks[i].To,"test.barn-reserved-crossing"),false]);
        var movers=ids.TakeLast(4).ToHashSet();var crossed=new Dictionary<EntityId,int>();
        for(var tick=0;tick<2_400;tick++)
        {
            var before=session.CaptureSnapshot().NavigationAgents.Where(n=>movers.Contains(n.Id)).ToDictionary(n=>n.Id);
            session.AdvanceTicks(1);
            var current=session.CaptureSnapshot().NavigationAgents.Where(n=>movers.Contains(n.Id)).ToArray();
            foreach(var n in current)
            {
                var p=before[n.Id];Assert.IsTrue(TraversalSweep.IsWalkable(session.TraversalGrid!,p.XMillimetres,p.ZMillimetres,n.XMillimetres,n.ZMillimetres));
                Assert.IsTrue(BarnLayout.MovementSweepClear(p.XMillimetres,p.ZMillimetres,n.XMillimetres,n.ZMillimetres),"A yielded walker clipped the measured frame margin.");
                var z=n.ZMillimetres-BarnLayout.OriginZ;
                if(z is >=5_750 and <=6_750)crossed[n.Id]=n.XMillimetres < -6_650?1:0;
            }
            if(current.All(n=>n.Action==AgentNavigationAction.Arrived))
            {Assert.AreEqual(4,crossed.Count);Assert.AreEqual(2,crossed.Values.Count(door=>door==1));return;}
        }
        Assert.Fail(System.Text.Json.JsonSerializer.Serialize(session.CaptureSnapshot().NavigationAgents.Where(n=>movers.Contains(n.Id))));
    }
    [TestMethod]
    public void HandsOnPlayerClimbsThePhysicalBarnStairAndCannotStepOffItsTread()
    {
        var session=GameSession.CreateStewardShift(20260922,1);
        var stand=typeof(StewardShiftStageTests).GetMethod("Stand",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
        void Move(int x,int z)=>BuildSession.Accept(session,new SetPlayerMoveCommand(x,z,false,StewardShiftRules.FacingTowards(x==0&&z==0?1:x,z)));
        NavigationObservation At()=>session.CaptureObservation().NavigationAgents.Single(n=>n.Id.Value==session.PlayerStewardId);
        stand.Invoke(null,[session,BarnLayout.Cell(6_750,3_750)]);Move(0,-1000);
        for(var i=0;i<150;i++)
        {var before=At();session.AdvanceWithoutSnapshot(1);var after=At();Assert.IsTrue(BarnLayout.StairSweepClear(before.XMillimetres,before.ZMillimetres,after.XMillimetres,after.ZMillimetres));}
        Move(0,0);var top=At();Assert.IsTrue(BarnLayout.Deck(TraversalGrid.WorldToCell(top.XMillimetres,top.ZMillimetres)));
        stand.Invoke(null,[session,BarnLayout.Cell(6_750,2_250)]);Move(-1000,0);session.AdvanceWithoutSnapshot(40);Move(0,0);
        Assert.IsTrue(At().XMillimetres>=6_650,"Manual input must stop before feet leave the tread top.");
        Move(1000,0);session.AdvanceWithoutSnapshot(40);Move(0,0);
        Assert.IsTrue(At().XMillimetres<7_100,"Manual input must stay clear of the high rail.");
        CollectionAssert.AreEqual(new[]{FestivalStages.MainId},session.Stages.Select(s=>s.Id).ToArray());
    }
    [TestMethod]
    public void ContinuousStairClearanceRejectsRailAndTreadEdgeCrossings()
    {
        int World(int z)=>BarnLayout.OriginZ+z;
        Assert.IsTrue(BarnLayout.StairSweepClear(6_750,World(3_750),6_750,World(1_250)));
        Assert.IsFalse(BarnLayout.StairSweepClear(7_250,World(3_750),7_250,World(1_250)),"A complete single-update crossing still clips the high handrail.");
        Assert.IsFalse(BarnLayout.StairSweepClear(6_500,World(3_750),6_500,World(1_250)),"Feet cannot leave the tread's west edge.");
        Assert.IsFalse(BarnLayout.StairSweepClear(6_750,World(2_250),7_100,World(2_250)),"A lateral yield cannot enter the capsule/rail margin.");
        Assert.IsTrue(BarnLayout.StairSweepClear(0,World(2_250),2_250,World(2_250)),"Ordinary hall movement stays unchanged.");
        Assert.IsTrue(BarnLayout.MovementSweepClear(-8_250,World(4_750),-8_250,World(7_250)),"The exit's west grid column retains its measured capsule margin.");
        Assert.IsFalse(BarnLayout.MovementSweepClear(-8_400,World(4_750),-8_400,World(7_250)),"A single-update exit crossing may not clip the west frame.");
        Assert.IsFalse(BarnLayout.MovementSweepClear(3_650,World(4_750),3_650,World(7_250)),"A single-update main-door crossing may not clip its frame.");
    }
    [TestMethod]
    public void FirstBarnActComesDownTheStairsAndClearsBeforeTheSecondActPlays()
    {
        var session=BarnStageFixture.Started(20260922);session.AdvanceWithoutSnapshot(17_400);
        var first=session.CaptureLivePerformance(BarnLayout.Stage.Id)!.Performers.Select(p=>p.AgentId).ToArray();
        var descended=new HashSet<ulong>();
        var previous=session.CaptureObservation().NavigationAgents.Where(n=>first.Contains(n.Id.Value)).ToDictionary(n=>n.Id.Value);
        while(session.CurrentTick<20_600 && session.PreparedStatus==PreparationStatus.Running)
        {
            session.AdvanceWithoutSnapshot(4);
            foreach(var current in session.CaptureObservation().NavigationAgents.Where(n=>first.Contains(n.Id.Value)))
            {
                var before=previous[current.Id.Value];
                Assert.IsTrue(TraversalSweep.IsWalkable(session.TraversalGrid!,before.XMillimetres,before.ZMillimetres,current.XMillimetres,current.ZMillimetres),"Outgoing band crossed solid ground.");
                Assert.IsTrue(BarnLayout.StairSweepClear(before.XMillimetres,before.ZMillimetres,current.XMillimetres,current.ZMillimetres),"Outgoing member clipped the measured rail/tread clearance.");
                var cell=TraversalGrid.WorldToCell(current.XMillimetres,current.ZMillimetres);
                if(BarnLayout.Stairs(cell))descended.Add(current.Id.Value);
                previous[current.Id.Value]=current;
            }
        }
        Assert.AreEqual(20_600,session.CurrentTick);Assert.AreEqual(3,descended.Count,"All outgoing members must descend the actual stair.");
        foreach(var current in previous.Values)
            Assert.IsFalse(BarnLayout.Deck(TraversalGrid.WorldToCell(current.XMillimetres,current.ZMillimetres))||BarnLayout.Stairs(TraversalGrid.WorldToCell(current.XMillimetres,current.ZMillimetres)),"Outgoing member blocks the next act.");
        var second=session.CaptureLivePerformance(BarnLayout.Stage.Id)!;
        Assert.AreEqual(20_600,second.PlannedTick);Assert.AreEqual(LiveSetStage.Live,second.Stage,System.Text.Json.JsonSerializer.Serialize(new{second,nav=session.CaptureObservation().NavigationAgents.Where(n=>second.Performers.Any(p=>p.AgentId==n.Id.Value)),generator=session.CaptureStageGenerator(BarnLayout.Stage.Id)}));
        Assert.IsTrue(second.Performers.All(p=>p.OnStage&&p.InstrumentAttached&&!first.Contains(p.AgentId)));
    }
    [TestMethod]
    public void BarnBandActuallyArrivesClimbsAndPlaysThroughProductionLifecycle()
    {
        var session=BarnStageFixture.Started(20260922);var sawStairs=new HashSet<ulong>();
        var ids=session.CaptureLivePerformance(BarnLayout.Stage.Id)!.Performers.Select(p=>p.AgentId).ToHashSet();
        var previous=session.CaptureObservation().NavigationAgents.Where(n=>ids.Contains(n.Id.Value)).ToDictionary(n=>n.Id.Value);
        for(var step=0;step<2_750 && session.PreparedStatus==PreparationStatus.Running;step++)
        {
            var before=session.CurrentTick; session.AdvanceWithoutSnapshot(4);
            Assert.IsTrue(session.CurrentTick>before,$"Fixture frozen at {before}, phase {session.Phase}, paused {session.IsPaused}");
            var live=session.CaptureLivePerformance(BarnLayout.Stage.Id)!;
            foreach(var p in live.Performers)
            {
                var nav=session.CaptureObservation().NavigationAgents.Single(n=>n.Id.Value==p.AgentId);
                var old=previous[p.AgentId];Assert.IsTrue(BarnLayout.MovementSweepClear(old.XMillimetres,old.ZMillimetres,nav.XMillimetres,nav.ZMillimetres));
                previous[p.AgentId]=nav;
                if(BarnLayout.Stairs(TraversalGrid.WorldToCell(nav.XMillimetres,nav.ZMillimetres)))sawStairs.Add(p.AgentId);
            }
            if(live.Stage==LiveSetStage.Live)
            {Assert.IsTrue(live.Performers.All(p=>p.OnStage && p.InstrumentAttached));Assert.AreEqual(3,sawStairs.Count);Assert.AreEqual(9_000,live.PlannedTick);return;}
        }
        Assert.Fail($"Barn did not start: {session.PreparedStatus}, tick {session.CurrentTick}, {session.CaptureLivePerformance(BarnLayout.Stage.Id)?.LastReaction}");
    }
}

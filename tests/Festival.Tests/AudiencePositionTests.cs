using System.Diagnostics;
using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class AudiencePositionTests
{
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static GameSession Started(int tier = 1)
    {
        var session = GameSession.CreateEquipmentCampaign(2, tier);
        foreach (var command in new SessionCommand[] { new AcceptPreparationOfferCommand("act.folk"),
            new AcceptPreparationOfferCommand("staff.steward"), new AcceptPreparationOfferCommand("equipment.buy"),
            new StartPreparedEditionCommand(), new EquipmentCommand(EquipmentAction.ShedLoad) })
        {
            var result = session.Execute(new(new CommandId(session.NextSubmissionSequence + 1), session.CampaignId,
                session.Phase, session.CurrentTick, session.NextSubmissionSequence, null, command));
            Assert.IsTrue(result.IsAccepted, result.Message);
        }
        session.AdvanceWithoutSnapshot(3200);
        return session;
    }
    private static GameSession Restore(GameSession session)
    {
        var result = GameSession.Restore(session.CapturePersistenceSnapshot()); Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, result.Session!.CaptureSnapshot().AuthoritativeHash);
        return result.Session;
    }
    private static void Live(GameSession session, LivePerformanceSnapshot live) => typeof(GameSession).GetField("_livePerformance", Hidden)!.SetValue(session, live);
    private static void Route(GameSession session, ulong id, GridCell cell, string intent) => typeof(GameSession).GetMethod("ApplyAgentDestination", Hidden)!.Invoke(session,
        [new EntityId(id), new SetAgentDestinationCommand(cell, intent), false]);
    // Labelled initial geometry only. Every subsequent displacement uses the shared walker.
    private static void Place(GameSession session, ulong id, GridCell cell)
    {
        var agents = typeof(GameSession).GetField("_navigationAgents", Hidden)!.GetValue(session)!;
        var agent = agents.GetType().GetProperty("Item")!.GetValue(agents, [new EntityId(id)])!;
        var centre = TraversalGrid.CellCentre(cell);
        foreach (var (name, value) in new[] { ("XMillimetres", centre.XMillimetres), ("ZMillimetres", centre.ZMillimetres),
            ("SegmentOriginXMillimetres", centre.XMillimetres), ("SegmentOriginZMillimetres", centre.ZMillimetres) })
            agent.GetType().GetProperty(name)!.SetValue(agent, value);
        Route(session, id, cell, "labelled.audience-initial-layout");
    }
    private static ulong Sparse(GameSession session, int enthusiasm, GridCell cell)
    {
        var live = session.CaptureLivePerformance()!; var id = live.Listeners[0].AgentId;
        foreach (var (listener, index) in live.Listeners.Select((item, index) => (item, index)))
            Place(session, listener.AgentId, listener.AgentId == id ? cell : new(180, 110 + index * 2));
        Live(session, live with { Listeners = live.Listeners.Select(item => item with { Place = item.AgentId == id ? cell : null,
            AtPlace = item.AgentId == id, Enthusiasm = item.AgentId == id ? enthusiasm : item.Enthusiasm,
            LastDecisionTick = session.CurrentTick - (item.AgentId == id ? 800 : 0) }).ToArray() });
        return id;
    }
    private static LiveListener Listener(GameSession session, ulong id) => session.CaptureLivePerformance()!.Listeners.Single(item => item.AgentId == id);

    [TestMethod]
    public void SparseIndifferentListenerWalksForwardWithBoundedLegAndSavedDwell()
    {
        var session = Started(); var id = Sparse(session, 35, new(120,150));
        var replay = Restore(session);
        session.AdvanceWithoutSnapshot(320); replay.AdvanceWithoutSnapshot(320);
        var listener = Listener(session, id);
        Assert.IsTrue(listener.Place!.Value.X < 120, $"Sparse indifferent target {listener.Place}");
        Assert.IsTrue(Math.Abs(listener.Place.Value.X - 120) <= 4);
        Assert.IsTrue(listener.AtPlace, "The adjustment must physically arrive, not just choose a score.");
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, replay.CaptureSnapshot().AuthoritativeHash);
        var settled = listener.Place; session.AdvanceWithoutSnapshot(400);
        Assert.AreEqual(settled, Listener(session,id).Place, "Dwell holds the selected target.");
        // A sparse settled front position has no material improvement over repeated reviews.
        Place(session,id,new(103,149));
        Live(session,session.CaptureLivePerformance()! with { Listeners=session.CaptureLivePerformance()!.Listeners.Select(item=>item with {
            Place=item.AgentId==id ? new GridCell(103,149) : null,LastDecisionTick=session.CurrentTick,AtPlace=item.AgentId==id }).ToArray() });
        for(var cycle=0;cycle<4;cycle++)
        {
            for(var half=0;half<2;half++)
            {
                Live(session,session.CaptureLivePerformance()! with { Listeners=session.CaptureLivePerformance()!.Listeners.Select(item=>
                    item.AgentId==id ? item : item with { LastDecisionTick=session.CurrentTick }).ToArray() });
                session.AdvanceWithoutSnapshot(400);
            }
            Assert.AreEqual(new GridCell(103,149),Listener(session,id).Place,"Stable front layout must survive repeated reviews.");
        }
    }

    [TestMethod]
    public void ContinuousComfortMakesKeenListenerMoreTolerantOfSamePhysicalCrowd()
    {
        var low = Started(); var id = Sparse(low,35,new(110,150));
        var live = low.CaptureLivePerformance()!;
        GridCell[] neighbours = [new(108,148),new(108,152),new(112,148),new(112,152)];
        foreach (var (cell,index) in neighbours.Select((cell,index)=>(cell,index))) Place(low,live.Listeners[index+1].AgentId,cell);
        var keen = Restore(low); Live(keen,keen.CaptureLivePerformance()! with { Listeners = live.Listeners.Select(item=>item.AgentId==id ? item with { Enthusiasm=100 } : item).ToArray() });
        Assert.IsTrue(GameSession.AudienceComfortTolerance(Listener(keen,id)) > GameSession.AudienceComfortTolerance(Listener(low,id)));
        low.AdvanceWithoutSnapshot(320); keen.AdvanceWithoutSnapshot(320);
        var lowPlace=Listener(low,id).Place!.Value; var keenPlace=Listener(keen,id).Place!.Value;
        Console.WriteLine($"Same crowd indifferent {lowPlace}; keen {keenPlace}");
        Assert.IsTrue(lowPlace != keenPlace, "Tolerance changes actual arrived choices, not an assigned interest band.");
        static int Density(GridCell cell,GridCell[] crowd)=>crowd.Sum(other=>Math.Max(0,25-(cell.X-other.X)*(cell.X-other.X)-(cell.Z-other.Z)*(cell.Z-other.Z))*40);
        Assert.IsTrue(Density(keenPlace,neighbours)>Density(lowPlace,neighbours),"Keen person actually tolerates a denser destination.");
        Assert.IsTrue(Listener(low,id).AtPlace && Listener(keen,id).AtPlace);
    }

    [TestMethod]
    public void LocalExcitementPaceComposesWithPersonalBaselineAndDoesNotAffectOtherRoutes()
    {
        var low=Started(); var id=Sparse(low,35,new(110,150));
        Live(low,low.CaptureLivePerformance()! with { Listeners=low.CaptureLivePerformance()!.Listeners.Select(item=>item with { LastDecisionTick=low.CurrentTick }).ToArray() });
        var high=Restore(low); Live(high,high.CaptureLivePerformance()! with { Listeners=high.CaptureLivePerformance()!.Listeners.Select(item=>item.AgentId==id ? item with { Enthusiasm=100 } : item).ToArray() });
        Route(low,id,new(116,150),"performance.listen-local"); Route(high,id,new(116,150),"performance.listen-local");
        Assert.AreEqual(940,low.GetAudienceWalkingPacePermille(new(id))); Assert.AreEqual(1200,high.GetAudienceWalkingPacePermille(new(id)));
        var origin=TraversalGrid.CellCentre(new(110,150)).XMillimetres;
        low.AdvanceWithoutSnapshot(10); high.AdvanceWithoutSnapshot(10);
        var a=low.CaptureSnapshot().NavigationAgents.Single(item=>item.Id.Value==id);
        var b=high.CaptureSnapshot().NavigationAgents.Single(item=>item.Id.Value==id);
        Assert.IsTrue(b.XMillimetres>a.XMillimetres);
        Assert.AreEqual(GameSession.GetWalkingSpeedPermille(new(id)),a.WalkingSpeedPermille);
        var expected=300L*a.WalkingSpeedPermille*940/1000000;
        Assert.IsTrue(Math.Abs(a.XMillimetres-origin-expected)<=1, "Pace multiplies, rather than replacing natural speed.");
        var lowTicks=10; var highTicks=10;
        while(low.CaptureSnapshot().NavigationAgents.Single(item=>item.Id.Value==id).Action!=AgentNavigationAction.Arrived && lowTicks<300) { low.AdvanceWithoutSnapshot(1);lowTicks++; }
        while(high.CaptureSnapshot().NavigationAgents.Single(item=>item.Id.Value==id).Action!=AgentNavigationAction.Arrived && highTicks<300) { high.AdvanceWithoutSnapshot(1);highTicks++; }
        Assert.IsTrue(highTicks<lowTicks && lowTicks<300,$"Actual local arrivals keen {highTicks} < indifferent {lowTicks}");
        Console.WriteLine($"Same-person baseline {a.WalkingSpeedPermille}; 3m local arrivals indifferent={lowTicks} ticks keen={highTicks} ticks");
        foreach(var intent in new[]{"medical.seek-water","medical.rest","staff.intervention-approach","staff.escort-guest","performance.listen","performance.stage-entry"})
        { Route(low,id,new(116,150),intent); Assert.AreEqual(1000,low.GetAudienceWalkingPacePermille(new(id)),intent); }
        Place(low,id,new(110,150));Place(high,id,new(110,150));
        Route(low,id,new(116,150),"medical.seek-water");Route(high,id,new(116,150),"medical.seek-water");
        lowTicks=highTicks=0;
        while(low.CaptureSnapshot().NavigationAgents.Single(item=>item.Id.Value==id).Action!=AgentNavigationAction.Arrived && lowTicks<300) { low.AdvanceWithoutSnapshot(1);lowTicks++; }
        while(high.CaptureSnapshot().NavigationAgents.Single(item=>item.Id.Value==id).Action!=AgentNavigationAction.Arrived && highTicks<300) { high.AdvanceWithoutSnapshot(1);highTicks++; }
        Assert.AreEqual(lowTicks,highTicks,"Non-listening actual travel ignores enthusiasm.");
        Console.WriteLine($"Same 3m non-listening arrivals indifferent={lowTicks} ticks keen={highTicks} ticks");
    }

    [TestMethod]
    public void DecisionClocksAndEnthusiasmRejectTamperingAndOutsideRouteHasNormalPace()
    {
        var session=Started();var id=Sparse(session,100,new(110,150));
        var snapshot=session.CapturePersistenceSnapshot();var live=session.CaptureLivePerformance()!;
        foreach(var bad in new[]{live.Listeners[0] with { LastDecisionTick=session.CurrentTick+1 },
            live.Listeners[0] with { LastDecisionTick=-801 }, live.Listeners[0] with { Enthusiasm=101 }})
            Assert.IsFalse(GameSession.Restore(snapshot with { LivePerformance=live with {
                Listeners=live.Listeners.Select(item=>item.AgentId==id ? bad : item).ToArray() }}).IsSuccess);
        Live(session,live with { Listeners=live.Listeners.Select(item=>item with { LastDecisionTick=-800 }).ToArray() });
        Restore(session); // Fresh checksum: legacy default clock remains semantically valid.
        Route(session,id,new(140,150),"performance.listen-local");
        Assert.AreEqual(1000,session.GetAudienceWalkingPacePermille(new(id)),"Whole planned route leaves vicinity, so modifier is not latched.");
    }

    [TestMethod]
    public void ExtremeSightlinePrefersCentralBehindFrontPeopleButCrowdedCenterStillLoses()
    {
        var session=Started();var id=Sparse(session,35,new(105,157));
        var live=session.CaptureLivePerformance()!;
        Place(session,live.Listeners[1].AgentId,new(103,149));
        Place(session,live.Listeners[2].AgentId,new(103,151));
        var centre=new GridCell(106,150);var side=new GridCell(103,164);
        int Score(GridCell cell)=>(int)typeof(GameSession).GetMethod("PlaceScore",Hidden)!.Invoke(session,
            [Listener(session,id),cell,new GridCell(105,157),session.CaptureLivePerformance()!.Listeners])!;
        var clearCenter=Score(centre);var clearSide=Score(side);
        Assert.IsTrue(clearCenter<clearSide,$"Behind-front central {clearCenter} versus extreme side {clearSide}");
        GridCell[] addedCrowd=[new(106,148),new(106,152),new(108,150),new(104,150)];
        for(var i=0;i<addedCrowd.Length;i++) Place(session,live.Listeners[i+3].AgentId,addedCrowd[i]);
        Assert.IsTrue(Score(centre)>Score(side),"Central preference must yield to actual over-comfort crowd pressure.");
        Console.WriteLine($"Sightline tradeoff clear behind-front={clearCenter} extreme-side={clearSide}; crowded center={Score(centre)} side={Score(side)}");
    }

    [TestMethod]
    public void SparseExtremeLateralActuallyWalksInwardWithoutForcedCentralBand()
    {
        var session=Started();var id=Sparse(session,35,new(103,165));
        var replay=Restore(session);
        session.AdvanceWithoutSnapshot(320);replay.AdvanceWithoutSnapshot(320);
        var listener=Listener(session,id);
        Assert.IsTrue(listener.Place!.Value.Z<165 && listener.Place.Value.Z>150,"A modest inward leg, not a forced centre destination.");
        Assert.IsTrue(listener.AtPlace,"The listener physically completes the inward route.");
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash,replay.CaptureSnapshot().AuthoritativeHash);
        Console.WriteLine($"Extreme lateral actual inward arrival103,165 -> {listener.Place}");
    }

    [TestMethod]
    public void DenseFrontListenerMakesSavedPhysicalShortRetreatWithNormalPaceAndScopedFacing()
    {
        var session=Started();var id=Sparse(session,35,new(103,149));
        var live=session.CaptureLivePerformance()!;
        GridCell[] crowd=[new(103,147),new(103,151),new(104,147),new(104,151),new(105,147),new(105,151)];
        for(var i=0;i<crowd.Length;i++) Place(session,live.Listeners[i+1].AgentId,crowd[i]);
        Live(session,live with { Listeners=live.Listeners.Select((item,index)=>index is >=1 and <=6
            ? item with { Place=crowd[index-1],AtPlace=true } : item).ToArray() });
        for(var ticks=0;ticks<320 && session.CaptureSnapshot().NavigationAgents.Single(item=>item.Id.Value==id).IntentId!="performance.listen-local-retreat";ticks++)
            session.AdvanceWithoutSnapshot(1);
        var before=session.CaptureSnapshot().NavigationAgents.Single(item=>item.Id.Value==id);
        Assert.AreEqual("performance.listen-local-retreat",before.IntentId);
        Assert.IsTrue(before.Destination!.Value.X>103 && before.Destination.Value.X<=107);
        Assert.AreEqual(940,session.GetAudienceWalkingPacePermille(new(id)));
        var replay=Restore(session);
        Assert.AreEqual(before.IntentId,replay.CaptureSnapshot().NavigationAgents.Single(item=>item.Id.Value==id).IntentId);
        session.AdvanceWithoutSnapshot(1);replay.AdvanceWithoutSnapshot(1);
        var after=session.CaptureSnapshot().NavigationAgents.Single(item=>item.Id.Value==id);
        var dx=after.XMillimetres-before.XMillimetres;var dz=after.ZMillimetres-before.ZMillimetres;
        Assert.IsTrue(dx>0,"Actual shared walking displacement retreats from stage.");
        Assert.IsTrue(session.ShouldAudienceBackstepFacingStage(new(id),after.XMillimetres,after.ZMillimetres,dx,dz));
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash,replay.CaptureSnapshot().AuthoritativeHash);
        var target=before.Destination.Value;
        for(var ticks=0;ticks<160 && !Listener(session,id).AtPlace;ticks++) session.AdvanceWithoutSnapshot(1);
        Assert.IsTrue(Listener(session,id).AtPlace,"Retreat finishes by physical arrival.");
        Route(session,id,new(target.X+2,target.Z),"medical.seek-water");
        Assert.IsFalse(session.ShouldAudienceBackstepFacingStage(new(id),after.XMillimetres,after.ZMillimetres,dx,dz));
        Assert.AreEqual(1000,session.GetAudienceWalkingPacePermille(new(id)));
        Console.WriteLine($"Dense front listener={id} enthusiasm35 crowd103/104/105 by147/151, actual103149 -> {target}; first displacement {dx},{dz}mm");
    }

    [TestMethod]
    public void RearCongestionRedistributesPhysicallyAndFortyListenerDiagnosticReplays()
    {
        var session=Started(2); var live=session.CaptureLivePerformance()!;
        var cells=(from x in new[]{118,120,122} from z in Enumerable.Range(0,14).Select(i=>138+i*2) select new GridCell(x,z)).Take(40).ToArray();
        for(var i=0;i<40;i++) Place(session,live.Listeners[i].AgentId,cells[i]);
        Live(session,live with { Listeners=live.Listeners.Select((item,i)=>item with { Place=cells[i],AtPlace=true,LastDecisionTick=session.CurrentTick-800 }).ToArray() });
        var replay=Restore(session); var watch=Stopwatch.StartNew();
        session.AdvanceWithoutSnapshot(4800); watch.Stop(); replay.AdvanceWithoutSnapshot(4800);
        var after=session.CaptureLivePerformance()!.Listeners;
        Assert.IsTrue(after.Average(item=>item.Place!.Value.X)<cells.Average(item=>item.X)-1);
        Assert.IsTrue(after.Count(item=>item.AtPlace)>=20,"Redistribution must include real arrivals.");
        Assert.AreEqual(40,after.Select(item=>item.Place).Distinct().Count());
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash,replay.CaptureSnapshot().AuthoritativeHash);
        Console.WriteLine($"Bounded {session.CapturePreparation()!.People.Length}-person audience 4800 ticks: {watch.Elapsed.TotalMilliseconds:F2}ms; meanX {cells.Average(item=>item.X):F2}->{after.Average(item=>item.Place!.Value.X):F2}; arrived {after.Count(item=>item.AtPlace)}");
        Restore(session);
    }
}

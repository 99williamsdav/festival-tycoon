using System.Collections;
using System.Reflection;
using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class CowHerdPacingTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Call(GameSession s, string name, params object[] args) => typeof(GameSession).GetMethod(name, Private)!.Invoke(s, args)!;
    private static void Field(GameSession s, string name, object value) => typeof(GameSession).GetField(name, Private)!.SetValue(s, value);
    private static NavigationObservation At(GameSession s, ulong id) => s.CaptureObservation().NavigationAgents.Single(a => a.Id.Value == id);
    private static double Distance(int x, int z, int bx, int bz) => Math.Sqrt((long)(x-bx)*(x-bx)+(long)(z-bz)*(z-bz));
    private static void Place(GameSession s, ulong id, GridCell cell)
    {
        var agents = (IDictionary)typeof(GameSession).GetField("_navigationAgents", Private)!.GetValue(s)!;
        var nav = agents[new EntityId(id)]!; var p = TraversalGrid.CellCentre(cell);
        void Set(string name, object value) => nav.GetType().GetProperty(name)!.SetValue(nav, value);
        Set("XMillimetres",p.XMillimetres); Set("ZMillimetres",p.ZMillimetres);
        Set("SegmentOriginXMillimetres",p.XMillimetres); Set("SegmentOriginZMillimetres",p.ZMillimetres);
        Set("Action",AgentNavigationAction.Idle); Set("Route",new List<GridCell>()); Set("RouteIndex",0); Set("SegmentProgressMicrometres",0);
    }
    // Only the starting positions are staged. Every subsequent step uses the normal simulation.
    private static (GameSession S, string Cow, ulong Worker) Fixture(GridCell? start = null, bool fetch = false)
    {
        var s = WithoutFaults(Started()); s.AdvanceWithoutSnapshot(2_000);
        var cows = (CowsSnapshot)Call(s,"BreakGate",s.CaptureCows()!,s.CapturePreparation()!);
        var p = TraversalGrid.CellCentre(start ?? new(155,122));
        var cow = cows.Loose[0] with { XMillimetres=p.XMillimetres,ZMillimetres=p.ZMillimetres,Activity=CowActivity.Grazing,Route=[],RouteIndex=0,UntilTick=s.CurrentTick+4_000 };
        Field(s,"_cows",cows with {Loose=[cow]});
        var faults=s.CaptureFaults()!; Field(s,"_faults",faults with {Disabled=false});
        var worker=s.GetStewardResponses()[0].WorkerId;
        var route=DeterministicPathfinder.FindPath(s.TraversalGrid!,TraversalGrid.WorldToCell(p.XMillimetres,p.ZMillimetres),CowRules.GateInside);
        Assert.IsTrue(route.Found);
        Place(s,worker,fetch ? new(145,122) : route.Path[Math.Min(3,route.Path.Count-1)]);
        Call(s,"SendToHerdCow",cow.Id,worker);
        if(!fetch) s.AdvanceWithoutSnapshot(1);
        return(s,cow.Id,worker);
    }

    [TestMethod]
    public void FasterStewardStaysCloseAheadWithoutPositionJumpsAndCompletes()
    {
        var(s,id,worker)=Fixture(); var previous=At(s,worker); int samples=0; double maxGap=0,maxStep=0;
        for(var i=0;i<3_000 && s.CaptureCows()!.Loose.Any(c=>c.Id==id);i++)
        {
            s.AdvanceWithoutSnapshot(1); var nav=At(s,worker);
            maxStep=Math.Max(maxStep,Distance(nav.XMillimetres,nav.ZMillimetres,previous.XMillimetres,previous.ZMillimetres)); previous=nav;
            if(s.CaptureCows()!.Loose.FirstOrDefault(c=>c.Id==id) is not {} cow) break;
            var gap=Distance(nav.XMillimetres,nav.ZMillimetres,cow.XMillimetres,cow.ZMillimetres); maxGap=Math.Max(maxGap,gap);
            if(i>150 && cow.XMillimetres<TraversalGrid.CellCentre(new(180,122)).XMillimetres)
            { Assert.IsTrue(nav.XMillimetres>cow.XMillimetres,"Steward leads on the straight return."); Assert.IsTrue(gap is >900 and <3_100,$"Gap {gap}"); samples++; }
        }
        Assert.IsTrue(samples>500,$"Only {samples} established return samples.");
        Assert.IsTrue(maxStep<45,$"No lateral displacement jumps: {maxStep} mm/tick.");
        Assert.IsTrue(maxGap<3_100,$"Maximum gap {maxGap}.");
        Assert.IsFalse(s.CaptureCows()!.Loose.Any(c=>c.Id==id),"Cow passes through its original gate.");
        Assert.AreNotEqual("cow.drive",At(s,worker).IntentId,"Assignment released.");
    }

    [TestMethod]
    public void FetchUsesOrdinaryPaceThenJoinsTheShortLead()
    {
        var(s,id,worker)=Fixture(fetch:true); var before=At(s,worker); s.AdvanceWithoutSnapshot(20); var after=At(s,worker);
        Assert.IsTrue(Distance(before.XMillimetres,before.ZMillimetres,after.XMillimetres,after.ZMillimetres)>250,"Fetch has ordinary walking pace.");
        s.AdvanceWithoutSnapshot(500); var cow=s.CaptureCows()!.Loose.Single(c=>c.Id==id); var nav=At(s,worker);
        Assert.IsTrue(cow.Driving); Assert.IsTrue(nav.XMillimetres>cow.XMillimetres,"Walks round to lead.");
        Assert.IsTrue(Distance(nav.XMillimetres,nav.ZMillimetres,cow.XMillimetres,cow.ZMillimetres)<3_100);
    }

    [TestMethod]
    public void PausedAndRestoredReturnReplaysExactlyAndReassignmentRemovesPacing()
    {
        var(s,id,worker)=Fixture(); s.AdvanceWithoutSnapshot(400); var restored=Restored(s);
        Accept(s,new SetPausedCommand(true)); var hash=s.CaptureSnapshot().AuthoritativeHash; s.AdvanceWithoutSnapshot(200);
        Assert.AreEqual(hash,s.CaptureSnapshot().AuthoritativeHash); Accept(s,new SetPausedCommand(false));
        // Pause commands change command history: compare the same commands on the restored branch.
        Accept(restored,new SetPausedCommand(true)); restored.AdvanceWithoutSnapshot(200); Accept(restored,new SetPausedCommand(false));
        s.AdvanceWithoutSnapshot(200); restored.AdvanceWithoutSnapshot(200);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,restored.CaptureSnapshot().AuthoritativeHash);
        var nav=At(s,worker); var cell=TraversalGrid.WorldToCell(nav.XMillimetres,nav.ZMillimetres);
        Call(s,"ApplyAgentDestination",new EntityId(worker),new SetAgentDestinationCommand(new(cell.X,cell.Z+12),"test.reassigned"),false);
        var before=At(s,worker); s.AdvanceWithoutSnapshot(20); var after=At(s,worker);
        Assert.IsNull(s.CaptureCows()!.Loose.Single(c=>c.Id==id).HerderId);
        Assert.IsTrue(Distance(before.XMillimetres,before.ZMillimetres,after.XMillimetres,after.ZMillimetres)>200,"No lingering cow pace.");
    }

    [TestMethod]
    public void CurvedLegalRouteStillReachesThePen()
    {
        var(s,id,worker)=Fixture(new(155,140)); var cow=s.CaptureCows()!.Loose.Single(c=>c.Id==id);
        Assert.IsTrue(cow.Route.Select((c,i)=>i==0?default:new GridCell(c.X-cow.Route[i-1].X,c.Z-cow.Route[i-1].Z)).Distinct().Count()>2,"Fixture has bends.");
        for(var i=0;i<110 && s.CaptureCows()!.Loose.Any(c=>c.Id==id);i++)
        { s.AdvanceWithoutSnapshot(80); var nav=At(s,worker); Assert.IsTrue(s.TraversalGrid!.Get(TraversalGrid.WorldToCell(nav.XMillimetres,nav.ZMillimetres)).IsWalkable); }
        Assert.IsFalse(s.CaptureCows()!.Loose.Any(c=>c.Id==id),"Curved return completes without a deadlock.");
    }

    [TestMethod]
    public void StewardWaitsWhenAnotherCowHoldsTheReturnThenResumes()
    {
        var(s,id,worker)=Fixture(); var cows=s.CaptureCows()!; var cow=cows.Loose[0];
        Place(s,worker,new(159,122));
        Call(s,"ApplyAgentDestination",new EntityId(worker),new SetAgentDestinationCommand(new(159,122),"cow.drive"),true);
        // A second cow grazes just beside the return corridor. Existing MoveAside clears it at the next 80-tick boundary.
        var blocker=new LooseCow("cow.2",cow.XMillimetres+1_500,cow.ZMillimetres+1_500,CowActivity.Grazing,[],0,s.CurrentTick+4_000);
        Field(s,"_cows",cows with {Loose=[cow,blocker],Escaped=Math.Max(2,cows.Escaped)});
        int stopped=0,resumed=0; double largestGap=0;
        for(var i=0;i<600;i++)
        {
            var before=s.CaptureCows()!.Loose.Single(c=>c.Id==id); var navBefore=At(s,worker);
            s.AdvanceWithoutSnapshot(1); var after=s.CaptureCows()!.Loose.Single(c=>c.Id==id); var nav=At(s,worker);
            if(after.XMillimetres==before.XMillimetres && after.ZMillimetres==before.ZMillimetres &&
                Distance(nav.XMillimetres,nav.ZMillimetres,navBefore.XMillimetres,navBefore.ZMillimetres)<1) stopped++;
            if(i>160 && after.XMillimetres>before.XMillimetres && nav.XMillimetres>navBefore.XMillimetres) resumed++;
            largestGap=Math.Max(largestGap,Distance(nav.XMillimetres,nav.ZMillimetres,after.XMillimetres,after.ZMillimetres));
        }
        Assert.IsTrue(stopped>0,"Steward waits alongside the held cow."); Assert.IsTrue(resumed>100,"Both resume after the existing cow-yield rule clears the grazer.");
        Assert.IsTrue(largestGap<3_100,$"Held cow is not abandoned: {largestGap} mm.");
    }

}

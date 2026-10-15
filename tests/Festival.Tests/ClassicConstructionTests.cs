using System.Collections;
using System.Reflection;
using System.Text.Json;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class ClassicConstructionTests
{
    private static GameSession Ready()
    {
        var s=GameSession.CreateClassicCampaign(20260922);
        ClassicCampaignTests.Prepare(s);
        return s;
    }
    private static BuildPlacement Place(GameSession s,BuildServiceKind kind)
    {
        var ids=s.CaptureBuildPlacements().Select(p=>p.Id).ToHashSet();
        for(var z=110;z<=186;z+=4)
        for(var x=72;x<=186;x+=4)
        foreach(var turn in new[]{0,1,2,3})
            if(BuildSession.Send(s,new PlaceBuildServiceCommand(kind,new(x,z),turn)).IsAccepted)
                return s.CaptureBuildPlacements().Single(p=>!ids.Contains(p.Id));
        Assert.Fail($"No clear site for {kind}");return null!;
    }
    [TestMethod]
    [DataRow(BuildServiceKind.WaterTap,4)]
    [DataRow(BuildServiceKind.Toilet,5)]
    [DataRow(BuildServiceKind.Bar,3)]
    [DataRow(BuildServiceKind.FoodVan,3)]
    [DataRow(BuildServiceKind.FirstAid,2)]
    [DataRow(BuildServiceKind.StewardPost,2)]
    [DataRow(BuildServiceKind.Marquee,3)]
    public void ClassicCanBuildBeyondEverySurvivalQuantityAndRestore(BuildServiceKind kind,int target)
    {
        var s=Ready();
        while(s.CaptureBuildPlacements().Count(p=>p.Kind==kind)<target)Place(s,kind);
        var copy=BuildSession.Restored(s);
        CollectionAssert.AreEqual(s.CaptureBuildPlacements().ToArray(),copy.CaptureBuildPlacements().ToArray());
        Assert.IsTrue(s.ServiceLimit(kind)>target);
        Assert.IsTrue(s.PreparationPlanCost>=s.BuildDraftCost,"Costs remain in the unpaid plan.");
    }
    [TestMethod]
    public void CatalogueHasOnlyPlacedVansAndOneNextCandidateAndDuplicateCuisineIsValidInClassic()
    {
        var s=Ready(); Assert.AreEqual(2,s.FoodVanCatalogueIds.Count);
        Place(s,BuildServiceKind.FoodVan);Place(s,BuildServiceKind.FoodVan);
        Assert.AreEqual(4,s.FoodVanCatalogueIds.Count);
        foreach(var id in s.PlacedVans)BuildSession.Accept(s,new ChooseFoodTraderCommand(FoodTraders.Default.Id,id));
        var restored=BuildSession.Restored(s);
        Assert.AreEqual(3*FoodTraders.Default.PitchFeePennies,restored.PlannedPitchFeePennies);
        Assert.AreEqual(3,restored.PlacedVans.Count);
    }
    [TestMethod]
    public void AdditionalPostsHaveSolidFootprintsAndUsableRestGroundWithoutCreatingStaff()
    {
        var s=Ready();var aid=Place(s,BuildServiceKind.FirstAid);var post=Place(s,BuildServiceKind.StewardPost);
        var roster=s.ExpectedPreparedPeopleCount;
        Assert.AreEqual(2,s.CaptureRestCentres().Count);
        Assert.AreEqual(0,s.CaptureResponsePosts().Single(p=>p.Id==post.Id).AssignedWorkers);
        BuildSession.Accept(s,new StartPreparedEditionCommand());
        Assert.AreEqual(roster,s.CapturePreparation()!.People.Length);
        foreach(var p in new[]{aid,post})
        foreach(var cell in GameSession.ResponsePostFootprint(new(p.Cell,p.QuarterTurns),p.Kind==BuildServiceKind.FirstAid?ResponseRole.Medic:ResponseRole.Steward))
            Assert.IsFalse(s.TraversalGrid!.Get(cell).IsWalkable,"Every additional post is a physical obstacle.");
        var centre=GameSession.RestCentreFor(new(aid.Cell,aid.QuarterTurns));
        Assert.IsTrue(s.IsRestSpot(centre));Assert.IsTrue(s.TraversalGrid!.Get(centre).IsWalkable);
        BuildSession.Restored(s);
    }
    [TestMethod]
    public void BadHugeIdsAreRejectedWithoutEnumeratingTheNumberAndSurvivalStillHasItsLimits()
    {
        var s=Ready();var saved=s.CapturePersistenceSnapshot();var p=saved.Preparation!;
        var bad=p.BuildPlacements.Select(b=>b.Id=="water.main"?b with {Id="water.extra-2147483647"}:b).OrderBy(b=>b.Id,StringComparer.Ordinal).ToArray();
        Assert.IsFalse(GameSession.Restore(saved with {Preparation=p with {BuildPlacements=bad}}).IsSuccess);
        var survival=BuildSession.Drafted();
        Assert.AreEqual(1,survival.ServiceLimit(BuildServiceKind.FirstAid));
        Assert.IsFalse(BuildSession.Send(survival,new PlaceBuildServiceCommand(BuildServiceKind.FirstAid,new(176,116))).IsAccepted);
        Assert.AreEqual(1,survival.ServiceLimit(BuildServiceKind.FoodVan));
    }
    [TestMethod]
    public void MalformedHistoricalLayoutsFailWithoutThrowing()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null&&!File.Exists(Path.Combine(root.FullName,"docs/WORKING-AGREEMENTS.md")))root=root.Parent;
        using var recorded=JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName,
            "reports/evidence/classic-tickets/session/four-festivals-20260922.json")));
        var clear=GameSession.Restore(recorded.RootElement[0].GetProperty("state").Deserialize<SessionPersistenceSnapshot>()!);
        Assert.IsTrue(clear.IsSuccess,clear.Error);
        var next=clear.Session!.CreateNextFestival().CapturePersistenceSnapshot();
        foreach(var bad in new BuildPlacement[][] {null!,[null!]})
        {
            var history=next.ClassicHistory!;
            var result=GameSession.Restore(next with {ClassicHistory=history with {
                CompletedFestivals=[history.CompletedFestivals[0] with {Layout=bad}]}});
            Assert.IsFalse(result.IsSuccess);StringAssert.Contains(result.Error!,"layout");
        }
    }
    [TestMethod]
    public void GuestPhysicallyReachesAdditionalFirstAidAndCoolsThere()
    {
        var s=Ready();var aid=Place(s,BuildServiceKind.FirstAid);
        BuildSession.Accept(s,new StartPreparedEditionCommand());s.AdvanceWithoutSnapshot(8000);
        var id=s.CapturePreparation()!.People.First(p=>p.Role==ProtectedPersonRole.Guest&&p.Admitted&&!p.Departed).AgentId;
        var centre=GameSession.RestCentreFor(new(aid.Cell,aid.QuarterTurns));
        var start=new[]{new GridCell(centre.X,centre.Z+6),new(centre.X+6,centre.Z),new(centre.X-6,centre.Z)}
            .First(c=>s.TraversalGrid!.Get(c).IsWalkable);
        var agents=(IDictionary)typeof(GameSession).GetField("_navigationAgents",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(s)!;
        var agent=agents[new EntityId(id)]!;var point=TraversalGrid.CellCentre(start);
        agent.GetType().GetProperty("XMillimetres")!.SetValue(agent,point.XMillimetres);
        agent.GetType().GetProperty("ZMillimetres")!.SetValue(agent,point.ZMillimetres);
        // Labelled location/need fixture; selection, route, arrival and relief use production code.
        typeof(GameSession).GetMethod("UpdatePerson",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(s,
            [id,(Func<Person,Person>)(p=>p with {Intent=MedicalIntent.Rest,HeatExposure=8500,Thirst=0,HealthStage=MedicalStage.Clear})]);
        var destination=(GridCell)typeof(GameSession).GetMethod("RestSpotFor",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(s,[id])!;
        Assert.IsTrue(Math.Abs(destination.X-centre.X)<=3&&Math.Abs(destination.Z-centre.Z)<=3,"Select the nearby additional site.");
        Assert.AreNotEqual(start,destination);
        typeof(GameSession).GetMethod("ApplyAgentDestination",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(s,
            [new EntityId(id),new SetAgentDestinationCommand(destination,"medical.rest"),false]);
        var arrived=false;
        for(var t=0;t<2000;t+=20)
        {
            s.AdvanceWithoutSnapshot(20);
            if(s.CaptureSnapshot().NavigationAgents.Single(a=>a.Id.Value==id).Action!=AgentNavigationAction.Arrived)continue;
            arrived=true;break;
        }
        Assert.IsTrue(arrived,"Rest must be reached by walking.");
        s.AdvanceWithoutSnapshot(20);
        Assert.IsTrue(s.CapturePerson(id)!.HeatExposure<8500,"The additional site provides actual cooling.");
    }
    [TestMethod]
    public void ExistingExtraMedicContractUsesTheSecondPostWithoutExtraFreeWorkers()
    {
        GameSession? s=null;
        for(ulong seed=20260922;seed<20261122;seed++)
        {
            var candidate=GameSession.CreateClassicCampaign(seed);var perk=candidate.CapturePerks()!;
            if(!perk.Hand.Contains("doctors-orders"))continue;
            BuildSession.Accept(candidate,new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,"doctors-orders"));
            s=candidate;break;
        }
        Assert.IsNotNull(s);
        var acts=s.GetFestivalActs().Where(a=>s.ActStandingOf(a)==ActStanding.Available).OrderBy(s.ActFee).Take(3).Select(a=>a.Id).ToArray();
        BuildSession.Accept(s,new SetProgrammeCommand(acts));BuildSession.Accept(s,new ConfirmClassicBookingsCommand());
        BuildSession.Accept(s,new UseDefaultBuildLayoutCommand());var aid=Place(s,BuildServiceKind.FirstAid);
        foreach(var hire in BuildSession.Crew(s))BuildSession.Accept(s,hire);
        BuildSession.Accept(s,new AcceptPreparationOfferCommand(BuildSession.ExtraId(s,StaffRole.Medic)));
        var before=s.CapturePreparation()!.People.Length;
        BuildSession.Accept(s,new StartPreparedEditionCommand());
        Assert.AreEqual(before+1,s.CapturePreparation()!.People.Length);
        var extra=s.CapturePreparation()!.StaffProfiles.Single(p=>p.Role==ResponseRole.Medic);
        Assert.AreEqual(GameSession.ResponsePostHome(s.CapturePreparation(),ResponseRole.Medic,true),s.StaffAssignedPost(extra.AgentId));
        Assert.AreEqual(1,s.CaptureResponsePosts().Single(p=>p.Id==aid.Id).AssignedWorkers);
        BuildSession.Restored(s);
    }
}

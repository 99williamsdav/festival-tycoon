using Festival.Simulation;
using Festival.Persistence;
using System.Reflection;
using System.Text.Json;

namespace Festival.Tests;

[TestClass]
public sealed class BarnTractorTests
{
    private static GameSession RoundTrip(GameSession s)
    {var saved=s.CapturePersistenceSnapshot();var restored=GameSession.Restore(saved);Assert.IsTrue(restored.IsSuccess,restored.Error);Assert.AreEqual(saved.AuthoritativeHash,restored.Session!.CaptureSnapshot().AuthoritativeHash);return restored.Session;}
    [TestMethod]
    public void ExplicitRevisionPreservesLegacySaveAndOnlyChangesDeclaredGeometry()
    {
        var legacy=GameSession.CreateBuildCampaign(20260922);var before=legacy.CapturePersistenceSnapshot();RoundTrip(legacy);
        var current=GameSession.CreateBuildCampaign(20260922).WithCurrentBarnGeometry();
        Assert.AreEqual(0,legacy.BarnGeometryVersion);Assert.IsFalse(legacy.BarnTractorPresent);Assert.IsTrue(current.BarnTractorPresent);
        Assert.AreNotEqual(before.AuthoritativeHash,current.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(JsonSerializer.Serialize(before with{AuthoritativeHash=""}),JsonSerializer.Serialize(current.CapturePersistenceSnapshot() with{BarnGeometryVersion=0,AuthoritativeHash=""}));RoundTrip(current);
        Assert.IsFalse(GameSession.Restore(before with{BarnGeometryVersion=2}).IsSuccess);
    }
    [TestMethod]
    public void WholeTyresAndBallastPlusBodyClearanceAreBlockedWithoutClosingEitherDoor()
    {
        var s=NextFestivalTests.Ready(GameSession.CreateBuildCampaign(20260922).WithCurrentBarnGeometry());BuildSession.Accept(s,new StartPreparedEditionCommand());
        var grid=s.TraversalGrid!;var solid=BarnTractorLayout.SolidCells().ToArray();Assert.AreEqual(88,solid.Length);
        Assert.IsTrue(solid.All(c=>!grid.Get(c).IsWalkable));
        foreach(var x in new[]{BarnTractorLayout.MinX,BarnTractorLayout.MaxX})foreach(var z in new[]{BarnTractorLayout.MinZ,BarnTractorLayout.MaxZ})
            Assert.IsFalse(grid.Get(TraversalGrid.WorldToCell(x,z)).IsWalkable);
        foreach(var x in new[]{0,-7_650})
        {
            var route=DeterministicPathfinder.FindPath(grid,BarnLayout.Cell(x,10_750),BarnLayout.Cell(x,-4_250));
            Assert.IsTrue(route.Found);Assert.IsTrue(route.Path.All(c=>!solid.Contains(c)));
            for(var i=1;i<route.Path.Count;i++){var a=TraversalGrid.CellCentre(route.Path[i-1]);var b=TraversalGrid.CellCentre(route.Path[i]);Assert.IsTrue(BarnTractorLayout.SweepClear(a.XMillimetres,a.ZMillimetres,b.XMillimetres,b.ZMillimetres));}
        }
        Assert.IsTrue(grid.Get(BarnLayout.Cell(-4_250,0)).IsWalkable,"Remaining barn floor is usable.");
        Assert.IsFalse(BarnTractorLayout.SweepClear(-3_000,-20_900,3_000,-20_900));
        Assert.IsTrue(BarnTractorLayout.SweepClear(-3_000,-24_000,3_000,-24_000));
        RoundTrip(s);Assert.ThrowsExactly<InvalidOperationException>(()=>GameSession.CreateBuildCampaign(20260922).WithStarted().WithCurrentBarnGeometry());
    }
    [TestMethod]
    [DataRow(1)][DataRow(2)][DataRow(3)][DataRow(4)][DataRow(5)][DataRow(6)][DataRow(7)]
    public void EveryOptionalStageSelectionSavesExactlyAndConvertsOnlyTheActualBarn(int mask)
    {
        var s=GameSession.CreateClassicDevelopmentStart(mask,true,true).WithCurrentBarnGeometry();
        Assert.AreEqual((mask&2)==0,s.BarnTractorPresent);RoundTrip(s);
        var defaults=mask==7?GameSession.CreateClassicDevelopmentTestDefaults(true,true).WithCurrentBarnGeometry():null;
        if(defaults is null)return;BuildSession.Accept(defaults,new StartPreparedEditionCommand());
        Assert.IsTrue(BarnTractorLayout.SolidCells().All(c=>defaults.TraversalGrid!.Get(c).IsWalkable));
        defaults.AdvanceWithoutSnapshot(11000);Assert.AreEqual(3,defaults.CaptureLivePerformances().Count(p=>p.Stage==LiveSetStage.Live&&p.Performers.All(a=>a.OnStage)));
        RoundTrip(defaults);
    }
    [TestMethod]
    public void StewardAndRecordedTierTwoUseDeclaredRevisionBeforeOpening()
    {
        var one=GameSession.CreateStewardShift(20260922,1,currentBarnGeometry:true);Assert.IsTrue(one.BarnTractorPresent);RoundTrip(one);
        var two=GameSession.CreateRecordedTierTwoStart().WithCurrentBarnGeometry();Assert.IsFalse(two.BarnTractorPresent);RoundTrip(two);
        var old=GameSession.CreateRecordedTierTwoStart();Assert.AreEqual(0,old.BarnGeometryVersion);RoundTrip(old);
    }
    [TestMethod]
    public void CurrentSaveRejectsRemovedOrWalkableFootprintBeforeHashValidation()
    {
        var s=GameSession.CreateBuildCampaign(20260922).WithCurrentBarnGeometry().WithStarted();var saved=s.CapturePersistenceSnapshot();
        var cell=BarnTractorLayout.SolidCells().First();var grid=saved.TraversalGrid!;
        foreach(var badCells in new[]{grid.Cells.Where(c=>c.X!=cell.X||c.Z!=cell.Z).ToArray(),
            grid.Cells.Select(c=>c.X==cell.X&&c.Z==cell.Z?c with{IsWalkable=true}:c).ToArray()})
        {var restored=GameSession.Restore(saved with{TraversalGrid=grid with{Cells=badCells}});Assert.IsFalse(restored.IsSuccess);StringAssert.Contains(restored.Error!,"Barn tractor geometry");}
    }
    [TestMethod]
    public void UpgradeRejectsAnAlreadyStartedLegacySessionWithoutMutatingVersion()
    {
        var legacy=GameSession.CreateBuildCampaign(20260922).WithStarted();
        var hash=legacy.CaptureSnapshot().AuthoritativeHash;
        Assert.ThrowsExactly<InvalidOperationException>(()=>legacy.WithCurrentBarnGeometry());
        Assert.AreEqual(0,legacy.BarnGeometryVersion);Assert.AreEqual(hash,legacy.CaptureSnapshot().AuthoritativeHash);
    }
    [TestMethod]
    public void CurrentRevisionSurvivesRealRetryAndCanonicalNextFestival()
    {
        var live=GameSession.CreateBuildCampaign(20260922).WithCurrentBarnGeometry().WithStarted();
        // Reuse the existing labelled overdue-medical fixture; retry is the real council command.
        typeof(HandsOnCampaignTests).GetMethod("Fatal",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[live]);
        BuildSession.Accept(live,new SpendCouncilFavourCommand());
        Assert.AreEqual(1,live.BarnGeometryVersion);Assert.IsTrue(live.BarnTractorPresent);RoundTrip(live);
        var completed=GameSession.CreateBuildCampaign(20260922).WithCurrentBarnGeometry().WithStarted();
        for(var i=0;i<80&&completed.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing;i++)completed.AdvanceWithoutSnapshot(1000);
        Assert.IsTrue(completed.CanStartNextFestival);var next=completed.CreateNextFestival();
        Assert.AreEqual(1,next.BarnGeometryVersion);Assert.IsTrue(next.BarnStageOpen);Assert.IsFalse(next.BarnTractorPresent);RoundTrip(next);
    }
    [TestMethod]
    public void RealManualInputStopsAtTractorButCrossesTheRemainingBarnFloor()
    {
        var s=GameSession.CreateStewardShift(20260922,1,currentBarnGeometry:true);
        var stand=typeof(StewardShiftStageTests).GetMethod("Stand",BindingFlags.Static|BindingFlags.NonPublic)!;
        NavigationObservation At()=>s.CaptureObservation().NavigationAgents.Single(n=>n.Id.Value==s.PlayerStewardId);
        foreach(var obstructed in new[]{true,false})
        {
            // Only the initial position is a labelled fixture; input and every following tick are production movement.
            stand.Invoke(null,[s,BarnLayout.Cell(-3_250,obstructed?2_250:-1_250)]);
            BuildSession.Accept(s,new SetPlayerMoveCommand(1000,0,false,90));
            for(var tick=0;tick<240;tick++)
            {
                var before=At();s.AdvanceWithoutSnapshot(1);var after=At();
                Assert.IsTrue(BarnTractorLayout.SweepClear(before.XMillimetres,before.ZMillimetres,after.XMillimetres,after.ZMillimetres));
            }
            Assert.IsTrue(obstructed?At().XMillimetres<=BarnTractorLayout.MinX-BarnTractorLayout.BodyClearanceMillimetres:At().XMillimetres>2_000);
            BuildSession.Accept(s,new SetPlayerMoveCommand(0,0,false,90));
        }
        RoundTrip(s);
    }
    [TestMethod]
    public void RejectedCommandRollbackAndSaveFailureKeepCurrentGeometry()
    {
        var s=NextFestivalTests.Ready(GameSession.CreateBuildCampaign(20260922).WithCurrentBarnGeometry());
        var host=new SessionHost(s,Path.Combine(Path.GetTempPath(),"tractor-rollback-"+Guid.NewGuid()),new("tractor-test","content","rules"),_=>throw new IOException("Labelled save failure"));
        var hash=s.CaptureSnapshot().AuthoritativeHash;
        Assert.IsFalse(host.Execute(new SetPreparationStockCommand(-1,33),out _));
        Assert.AreEqual(1,host.Session.BarnGeometryVersion);Assert.AreEqual(hash,host.Session.CaptureSnapshot().AuthoritativeHash);RoundTrip(host.Session);
        Assert.IsTrue(host.ExecuteMilestone(new SetPreparationStockCommand(44,33),"fixture",out _));
        Assert.IsNotNull(host.SaveError);Assert.AreEqual(1,host.Session.BarnGeometryVersion);RoundTrip(host.Session);
    }
}

internal static class TractorTestStart
{
    public static GameSession WithStarted(this GameSession s){NextFestivalTests.Ready(s);BuildSession.Accept(s,new StartPreparedEditionCommand());return s;}
}

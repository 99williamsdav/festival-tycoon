using Festival.Simulation;
using System.Text.Json;

namespace Festival.Tests;

[TestClass]
public sealed class BarnCampaignAllocationTests
{
    internal static GameSession Ready()
    {
        var s = GameSession.CreateRecordedTierTwoStart();
        BuildSession.Accept(s,new AcknowledgeFestivalChallengeCommand(2,1));
        NextFestivalTests.Ready(s);
        BuildSession.Accept(s,new AcceptPreparationOfferCommand(BuildSession.ExtraId(s,StaffRole.Medic)));
        return s;
    }
    internal static TraversalGrid Grid(GameSession s) => new(s.CapturePersistenceSnapshot().TraversalGrid!.Cells
        .Select(c=>new TerrainCellOverride(new(c.X,c.Z),(GroundSurface)c.Surface,c.IsWalkable,c.CostPermille,c.ElevationMillimetres,c.SlopePermille)));

    [TestMethod]
    public void TierOneBarnHasOrdinaryFloorAndNoRaisedStageOrSecondProgramme()
    {
        var s = NextFestivalTests.Ready(GameSession.CreateBuildCampaign(20260922));
        BuildSession.Accept(s,new StartPreparedEditionCommand());
        Assert.IsFalse(s.BarnStageOpen);Assert.IsFalse(s.SecondStageOpen);
        CollectionAssert.AreEqual(new[]{FestivalStages.MainId},s.Stages.Select(stage=>stage.Id).ToArray());
        var grid=Grid(s);
        var formerRim=BarnLayout.Cell(4_250,-1_250);
        Assert.IsTrue(grid.Get(formerRim).IsWalkable,"The hidden riser's rim cannot block the ordinary floor.");
        Assert.IsTrue(DeterministicPathfinder.FindPath(grid,BarnLayout.Cell(3_250,-1_250),BarnLayout.Cell(8_750,-1_250)).Found);
        Assert.IsFalse(GameSession.OnStageCells(s.Stages,BarnLayout.Stage.PerformerMarks[0]));
        Assert.IsTrue(s.CoveredCell(formerRim));
        Assert.IsNull(s.CaptureStageGenerator(FestivalStages.BarnId));
        Assert.IsTrue(BarnLayout.MovementSweepClear(6_250,-20_750,7_500,-20_750,stageActive:false));
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void RecordedAndCanonicalTierTwoBothUseBarnWithNoPondProgrammeAndNoNewMoney()
    {
        using var stream=typeof(GameSession).Assembly.GetManifestResourceStream("Festival.Simulation.recorded-tier-one-clear.json")!;
        var completed=GameSession.Restore(JsonSerializer.Deserialize<SessionPersistenceSnapshot>(stream)!);
        Assert.IsTrue(completed.IsSuccess,completed.Error);Assert.IsTrue(completed.Session!.CanStartNextFestival);
        var canonical=completed.Session.CreateNextFestival();var direct=GameSession.CreateRecordedTierTwoStart();
        Assert.AreEqual(canonical.CaptureSnapshot().AuthoritativeHash,direct.CaptureSnapshot().AuthoritativeHash);
        foreach(var s in new[]{canonical,direct,GameSession.CreateDevelopmentFestival(20260922,2),GameSession.CreateDevelopmentFestival(20260922,2,true)})
        {
            CollectionAssert.AreEqual(new[]{FestivalStages.MainId,FestivalStages.BarnId},s.Stages.Select(stage=>stage.Id).ToArray());
            Assert.IsTrue(s.BarnStageOpen);Assert.IsFalse(s.PondStageOpen);
            Assert.IsNull(s.CaptureProgramme(FestivalStages.PondId));Assert.IsNull(s.CaptureStageGenerator(FestivalStages.PondId));
            CollectionAssert.AreEqual(FestivalStages.Pond.SlotStarts.ToArray(),s.Stages[1].SlotStarts.ToArray());
            CollectionAssert.AreEqual(FestivalStages.Pond.SlotEnds.ToArray(),s.Stages[1].SlotEnds.ToArray());
            Assert.AreEqual(18,s.CapturePreparation()!.People.Count(p=>p.Role==ProtectedPersonRole.Performer));
            Assert.AreEqual(70,s.CaptureStageGenerator(FestivalStages.BarnId)!.Capacity);
            BuildSession.Restored(s);
        }
        Assert.AreEqual(110_335L,direct.CapturePreparation()!.OpeningCashPennies);
        Assert.AreEqual(35_335L,direct.CapturePreparation()!.CarriedIn!.CashPennies);
        Assert.AreEqual(20_000L,direct.CaptureCampaignPlanningSnapshot()!.Loan.OutstandingPrincipalPennies);
    }

    [TestMethod]
    public void OutsideGeneratorFullAssetBoundsAreUncoveredReservedAndDoNotBlockDoorOrBandRoutes()
    {
        var s=Ready();BuildSession.Accept(s,new StartPreparedEditionCommand());
        var generator=s.CaptureStageGenerator(FestivalStages.BarnId)!;
        Assert.AreEqual((7_000,-13_500),(generator.XMillimetres,generator.ZMillimetres));
        // Measured unchanged hire-set bounds, including its trailer and lead; every corner is outside shelter/eaves.
        foreach(var x in new[]{generator.XMillimetres-1_730,generator.XMillimetres+2_061})
        foreach(var z in new[]{generator.ZMillimetres-730,generator.ZMillimetres+730})
        {
            Assert.IsFalse(BarnLayout.Interior(x,z));Assert.IsTrue(z> BarnLayout.OriginZ+6_707);
            var cell=TraversalGrid.WorldToCell(x,z);Assert.IsFalse(s.CoveredCell(cell));
            Assert.IsTrue(BarnLayout.Generator(cell));Assert.IsFalse(Grid(s).Get(cell).IsWalkable);
            Assert.IsTrue(BarnLayout.Stage.Reserve(cell));
        }
        var grid=Grid(s);
        foreach(var x in new[]{0,-7_650})Assert.IsTrue(DeterministicPathfinder.FindPath(grid,BarnLayout.Cell(x,10_750),BarnLayout.Cell(x,3_250)).Found);
        for(var i=0;i<9;i++)Assert.IsTrue(DeterministicPathfinder.FindPath(grid,BarnLayout.Stage.ArrivalStart(i),BarnLayout.Stage.BackstagePlace(i)).Found);
        for(var role=0;role<3;role++)Assert.IsTrue(DeterministicPathfinder.FindPath(grid,BarnLayout.Stage.AccessCells[role],BarnLayout.Stage.PerformerMarks[role]).Found);
        var prepared=Ready();
        Assert.IsFalse(BuildSession.Send(prepared,new MoveBuildServiceCommand("drinks",TraversalGrid.WorldToCell(generator.XMillimetres,generator.ZMillimetres),0)).IsAccepted);
    }

    [TestMethod]
    public void RealTierTwoBandAudienceAndPaidEngineerReachBarnInRainAndSecondActChangesOver()
    {
        var s=Ready();BuildSession.Accept(s,new StartPreparedEditionCommand());
        var engineer=s.StageEngineerId(FestivalStages.BarnId)!.Value;
        var reachedMix=false;var sawAudience=false;var climbed=new HashSet<ulong>();var firstMembers=Array.Empty<ulong>();
        for(var tick=0;tick<21_000 && s.PreparedStatus==PreparationStatus.Running;tick+=80)
        {
            s.AdvanceWithoutSnapshot(80);var nav=s.CaptureObservation().NavigationAgents.ToDictionary(n=>n.Id.Value);
            if(nav.TryGetValue(engineer,out var mix))reachedMix|=BarnLayout.Interior(mix.XMillimetres,mix.ZMillimetres);
            if(s.CaptureLivePerformance(FestivalStages.BarnId) is not {} live)continue;
            if(live.PlannedTick==9_000)firstMembers=live.Performers.Select(p=>p.AgentId).ToArray();
            foreach(var actor in live.Performers)
                if(nav.TryGetValue(actor.AgentId,out var position) && BarnLayout.Stairs(TraversalGrid.WorldToCell(position.XMillimetres,position.ZMillimetres)))climbed.Add(actor.AgentId);
            sawAudience|=live.Listeners.Any(l=>l.AtPlace && nav.TryGetValue(l.AgentId,out var position) && BarnLayout.Interior(position.XMillimetres,position.ZMillimetres));
        }
        Assert.AreEqual(PreparationStatus.Running,s.PreparedStatus);
        var second=s.CaptureLivePerformance(FestivalStages.BarnId)!;
        Assert.AreEqual(1,s.CaptureProgramme(FestivalStages.BarnId)!.CurrentSlot);Assert.AreEqual(LiveSetStage.Live,second.Stage);
        Assert.AreEqual(20_600L,second.StartedTick);
        Assert.IsTrue(reachedMix,"The paid second engineer must actually enter the venue.");
        Assert.IsTrue(sawAudience,"Real listeners must reach the covered listening area.");
        Assert.IsTrue(firstMembers.All(climbed.Contains),"All three first-act roles must use the real stairs.");
        Assert.IsTrue(second.Performers.All(p=>p.OnStage && p.InstrumentAttached));
        Assert.IsTrue(s.IsRainyFestival);
        BuildSession.Restored(s);
    }

    [TestMethod]
    public void BarnCutoffInterruptsOnlyBarnAndRestoresWithItsOwnGeneratorIdentity()
    {
        var s=Ready();BuildSession.Accept(s,new StartPreparedEditionCommand());s.AdvanceWithoutSnapshot(9_000);
        Assert.AreEqual(LiveSetStage.Live,s.CaptureLivePerformance(FestivalStages.BarnId)!.Stage);
        Assert.AreEqual(LiveSetStage.Live,s.CaptureLivePerformance(FestivalStages.MainId)!.Stage);
        Assert.IsFalse(BuildSession.Send(s,new StageGeneratorCommand(FestivalStages.PondId,StageGeneratorAction.Isolate)).IsAccepted);
        BuildSession.Accept(s,new StageGeneratorCommand(FestivalStages.BarnId,StageGeneratorAction.Isolate));s.AdvanceWithoutSnapshot(80);
        Assert.AreEqual(LiveSetStage.Interrupted,s.CaptureLivePerformance(FestivalStages.BarnId)!.Stage);
        Assert.AreEqual(LiveSetStage.Live,s.CaptureLivePerformance(FestivalStages.MainId)!.Stage);
        Assert.IsFalse(s.StagePoweredAt(FestivalStages.BarnId));Assert.IsTrue(s.StagePoweredAt(FestivalStages.MainId));
        var restored=BuildSession.Restored(s);Assert.AreEqual(EquipmentStage.Isolated,restored.CaptureStageGenerator(FestivalStages.BarnId)!.Stage);
    }
}

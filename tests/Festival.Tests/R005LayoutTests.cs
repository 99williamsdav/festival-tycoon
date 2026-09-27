using Festival.Persistence;
using Festival.Simulation;
using Festival.Simulation.Fixtures;
using System.Security.Cryptography;

namespace Festival.Tests;

[TestClass]
public sealed class R005LayoutTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ROGUELIKE_DESIGN.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root missing.");
    }
    private static void Accept(GameSession s, SessionCommand c)
    {
        var r = s.Execute(new(new CommandId(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase, s.CurrentTick, s.NextSubmissionSequence, null, c));
        Assert.IsTrue(r.IsAccepted, r.Message);
    }
    private static GameSession PaidStaffFixture()
    {
        var s = GameSession.CreateImmersionCampaign(20260922);
        // Explicit development capacity setup; each worker is still hired for £30.
        foreach (var effect in new[] { "staff.medic-slot", "staff.steward-slot" })
            if (effect == "staff.medic-slot" ? !s.CapturePreparation()!.ExtraMedicSlotOwned : !s.CapturePreparation()!.ExtraStewardSlotOwned)
                Accept(s, new ApplyStaffFoundationEffectCommand(effect));
        foreach (var offer in new[] { "staff.extra-medic", "staff.extra-steward", "staff.steward" }) Accept(s, new AcceptPreparationOfferCommand(offer));
        Accept(s, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.field-frequency"]));
        return s;
    }

    [TestMethod]
    public void BarnFrontsAlignAndApprovedAssetsAndLargeTransformRemainExact()
    {
        var scene = LowerWitteringFarmScenario.CreateReadModel();
        var small = scene.GetRequiredObject("farm.small-barn"); var large = scene.GetRequiredObject("farm.large-barn");
        Assert.AreEqual((19d, -17d, 0), (small.XMetres, small.ZMetres, small.YawQuarterTurns));
        Assert.AreEqual((0d, -23d, 0), (large.XMetres, large.ZMetres, large.YawQuarterTurns));
        // Both approved assets have +Z door axes with applied node transforms.
        Assert.AreEqual(large.YawQuarterTurns, small.YawQuarterTurns);
        foreach (var (file, expected) in new[] {
            ("lwf_barn_v2.glb", "FBA11E5775BF499B93D7D6BCAF06CEAA8DE2D8045896BF5F3F3B384387798B5E"),
            ("lwf_large_barn_v1.glb", "12ECBE1DC0A9A929B343AA77E6CABE9EA2501CF9C2868959EE3DF26EA60DBF24") })
            Assert.AreEqual(expected, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "game/assets/environment", file)))));
        // Full approved render bounds, including gutters, leave a 1.211m X gap.
        Assert.IsTrue(small.XMetres - 7.325 > large.XMetres + 10.4643);
    }

    [TestMethod]
    public void RotatedReservationBlocksWholeSmallBarnAndReleasesPriorLongRearStrip()
    {
        var grid = new TraversalGrid(NavigationFixture.CreateLowerWitteringTerrain());
        for (var z = -22500; z <= -11500; z += 500)
        for (var x = 11000; x <= 27000; x += 500)
            Assert.IsFalse(grid.Get(TraversalGrid.WorldToCell(x, z)).IsWalkable, $"{x},{z}");
        foreach (var (x,z) in new[] { (19000,-24000), (19000,-10500), (28500,-17000), (19000,-11000), (11500,-10500) })
            Assert.IsTrue(grid.Get(TraversalGrid.WorldToCell(x,z)).IsWalkable, $"Approach {x},{z}");
        Assert.IsFalse(grid.Get(TraversalGrid.WorldToCell(0,-23000)).IsWalkable);
    }

    [TestMethod]
    public void GateRoutesToActualServicesPostsStageAndBothBarnFrontApronsRemainViable()
    {
        var s = GameSession.CreateImmersionCampaign(20260922);
        // Starting the actual campaign creates its production vendor obstacles.
        Accept(s,new AcceptPreparationOfferCommand("staff.steward"));
        Accept(s,new SetProgrammeCommand(["act.meadow-lanterns","act.barnstorm-circuit","act.field-frequency"]));
        Accept(s,new StartPreparedEditionCommand());
        var grid = new TraversalGrid(s.CapturePersistenceSnapshot().TraversalGrid!.Cells.Select(c =>
            new TerrainCellOverride(new(c.X,c.Z),(GroundSurface)c.Surface,c.IsWalkable)));
        var targets = new[] { GameSession.MedicalMedicCell, GameSession.MedicalRestCell, GameSession.DisorderSecurityBaseCell,
            GameSession.MedicalExitCell, new GridCell(104,152), TraversalGrid.WorldToCell(19000,-10500), TraversalGrid.WorldToCell(0,-15000) }
            .Concat(s.CaptureWaterPoints().SelectMany(p => s.CaptureWaterQueueCells(p.Id)))
            .Concat(s.CaptureImmersion()!.Vendors.Select(v => GameSession.ImmersionQueueCell(v,0)));
        foreach(var cell in targets) Assert.IsTrue(DeterministicPathfinder.FindPath(grid,TraversalGrid.WorldToCell(0,30000),cell).Found,$"Route to {cell}");
    }

    [TestMethod]
    public void IdleRoleHintIsPureAndRequiresPhysicalDutyArrivalForAllPaidAndBaselineWorkers()
    {
        var s = PaidStaffFixture(); Accept(s,new StartPreparedEditionCommand());
        foreach(var worker in s.GetResponseStaff())
        {
            var nav=s.CaptureObservation().NavigationAgents.Single(n=>n.Id.Value==worker.AgentId);
            Assert.IsNull(s.IdleResponseStaffRole(nav.Id,nav.XMillimetres,nav.ZMillimetres));
        }
        s.AdvanceWithoutSnapshot(1800);
        var hash=s.CaptureSnapshot().AuthoritativeHash;
        foreach(var worker in s.GetResponseStaff())
        {
            var nav=s.CaptureObservation().NavigationAgents.Single(n=>n.Id.Value==worker.AgentId);
            Assert.AreEqual(worker.Role,s.IdleResponseStaffRole(nav.Id,nav.XMillimetres,nav.ZMillimetres),worker.Name);
            Assert.IsNull(s.IdleResponseStaffRole(nav.Id,nav.XMillimetres+10,nav.ZMillimetres),"Interpolation still away from duty.");
        }
        Assert.AreEqual(hash,s.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void ActualPriorHeaderRejectsWithoutChangingFileAndCurrentPerkPaidCampaignLoadsExactly()
    {
        var compatibility=new SaveCompatibility("0.0.1-r0.05-hearing-v1",LowerWitteringFarmScenario.ContentCompatibilityHash,"r0-disorder-layout-v13");
        var prior=Path.Combine(RepositoryRoot(),"reports/evidence/R0.05g/final-1280x720/saves/manual-preparation.ftsave");
        var bytes=File.ReadAllBytes(prior); var rejected=SaveFileAdapter.LoadFile(prior,compatibility);
        Assert.IsFalse(rejected.IsSuccess); StringAssert.Contains(rejected.Error!,"Content hash mismatch");
        CollectionAssert.AreEqual(bytes,File.ReadAllBytes(prior));
        Assert.AreNotEqual("153c4af484f24f92ee5bb4bd8572153bea4283bda785174685ef4f5c54d7d344",compatibility.ContentHash);
        var s=GameSession.CreatePerkCampaign(20260922);
        for(ulong seed=20260923;!s.CapturePerks()!.Hand.Contains("doctors-orders");seed++) s=GameSession.CreatePerkCampaign(seed);
        var perk=s.CapturePerks()!;Accept(s,new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,"doctors-orders"));
        Accept(s,new AcceptPreparationOfferCommand("staff.extra-medic"));
        Accept(s,new AcceptPreparationOfferCommand("staff.steward"));
        Accept(s,new SetProgrammeCommand(["act.meadow-lanterns","act.barnstorm-circuit","act.field-frequency"]));
        Accept(s,new StartPreparedEditionCommand()); s.AdvanceWithoutSnapshot(1800);
        // Labelled warning initialization; dispatch, physical route and job continuation
        // use production commands and ticks, including the normal save adapter.
        var patient=s.CapturePreparation()!.People.First(person=>person.Role==ProtectedPersonRole.Guest).AgentId;
        var medical=s.CaptureMedical()!;
        typeof(GameSession).GetField("_medical",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(s,
            medical with { Needs=medical.Needs.Select(n=>n.AgentId==patient?n with {Stage=MedicalStage.Distress,WarningTick=s.CurrentTick}:n).ToArray() });
        var worker=s.GetResponseStaff().Single(w=>w.Name=="Avery Brooks");
        Accept(s,new MedicalCommand(patient,MedicalAction.DispatchMedic,worker.AgentId));
        Assert.AreEqual(MedicalResponseStage.Travelling,s.GetMedicResponses().Single(j=>j.WorkerId==worker.AgentId).Stage);
        var nav=s.CaptureObservation().NavigationAgents.Single(n=>n.Id.Value==worker.AgentId);
        Assert.IsNull(s.IdleResponseStaffRole(nav.Id,nav.XMillimetres,nav.ZMillimetres));
        var directory=Path.Combine(Path.GetTempPath(),"r005h-layout-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assert.IsTrue(SaveFileAdapter.SaveSlot(directory,"current",new(s,compatibility,"labelled-layout-fixture",DateTimeOffset.UtcNow)).IsSuccess);
            var loaded=SaveFileAdapter.LoadSlot(directory,"current",compatibility); Assert.IsTrue(loaded.IsSuccess,loaded.Error);
            Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,loaded.Session!.CaptureSnapshot().AuthoritativeHash);
            CollectionAssert.AreEqual(s.CapturePerks()!.Equipped,loaded.Session.CapturePerks()!.Equipped);
            CollectionAssert.AreEqual(s.GetResponseStaff().ToArray(),loaded.Session.GetResponseStaff().ToArray());
            s.AdvanceWithoutSnapshot(100);loaded.Session.AdvanceWithoutSnapshot(100);
            Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,loaded.Session.CaptureSnapshot().AuthoritativeHash);
        }
        finally { Directory.Delete(directory,true); }
    }
}

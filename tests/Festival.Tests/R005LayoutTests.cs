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
        var s = BuildSession.PlannedWith("doctors-orders");
        foreach (var offer in BuildSession.CrewIds(s).Prepend(BuildSession.ExtraId(s, StaffRole.Medic))) Accept(s, new AcceptPreparationOfferCommand(offer));
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
        var s = BuildSession.Planned(20260922);
        // Starting the actual campaign creates its production vendor obstacles.
        foreach (var hire in BuildSession.Crew(s)) Accept(s, hire);
        Accept(s,new SetProgrammeCommand(["act.meadow-lanterns","act.overdue-library-books","act.low-battery"]));
        Accept(s,new StartPreparedEditionCommand());
        var grid = new TraversalGrid(s.CapturePersistenceSnapshot().TraversalGrid!.Cells.Select(c =>
            new TerrainCellOverride(new(c.X,c.Z),(GroundSurface)c.Surface,c.IsWalkable)));
        var targets = new[] { GameSession.MedicalMedicCell, GameSession.MedicalRestCell, GameSession.DisorderSecurityBaseCell,
            GameSession.MedicalExitCell, new GridCell(104,152), TraversalGrid.WorldToCell(19000,-10500), TraversalGrid.WorldToCell(0,-15000) }
            .Concat(s.CaptureWaterPoints().SelectMany(p => s.CaptureWaterQueueCells(p.Id)))
            .Concat(s.CaptureVendors().Select(v => GameSession.ImmersionQueueCell(v,0)));
        foreach(var cell in targets) Assert.IsTrue(DeterministicPathfinder.FindPath(grid,TraversalGrid.WorldToCell(0,30000),cell).Found,$"Route to {cell}");
    }

}

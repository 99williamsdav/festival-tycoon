using Festival.Simulation;
using Festival.Simulation.Fixtures;

namespace Festival.Tests;

[TestClass]
public sealed class FarmSceneTests
{
    [TestMethod]
    public void LowerWitteringFarmHasStableUniqueIdsAndBoundedPositions()
    {
        var scene = LowerWitteringFarmScenario.CreateReadModel();
        Assert.AreEqual("scenario.lower-wittering-farm", scene.ScenarioId);
        Assert.AreEqual(64, LowerWitteringFarmScenario.ContentCompatibilityHash.Length);
        Assert.AreNotEqual("d7e7597670c2f9bc2552fa5df29f4afe294270e346643160e92feb1436bb1dd9",
            LowerWitteringFarmScenario.ContentCompatibilityHash);
        Assert.AreEqual(6, scene.Objects.Count);
        Assert.AreEqual(scene.Objects.Count, scene.Objects.Select(item => item.StableId).Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(scene.Objects.All(item => Math.Abs(item.XMetres) <= scene.SiteWidthMetres / 2));
        Assert.IsTrue(scene.Objects.All(item => Math.Abs(item.ZMetres) <= scene.SiteDepthMetres / 2));
        Assert.IsTrue(scene.Objects.All(item => item.YawQuarterTurns is >= 0 and <= 3));
        CollectionAssert.AreEquivalent(Enum.GetValues<FarmObjectKind>(), scene.Objects.Select(item => item.Kind).ToArray());
    }

    [TestMethod]
    public void InheritedBuildingsAreImmovableAndGateStartsVisuallyOpen()
    {
        var scene = LowerWitteringFarmScenario.CreateReadModel();
        foreach (var id in new[] { "farm.farmhouse", "farm.small-barn", "farm.large-barn" })
        {
            var building = scene.GetRequiredObject(id);
            Assert.IsTrue(building.IsPermanent);
            Assert.IsFalse(building.IsMovable);
        }
        Assert.AreEqual(FarmObjectState.Open, scene.GetRequiredObject("farm.main-gate").State);
        Assert.IsTrue(scene.Objects.All(item => item.IsSelectable));
    }

    [TestMethod]
    public void R004RearBarnAndSeparatedResponseFacilitiesHaveWalkableApproaches()
    {
        var barn = LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.large-barn");
        Assert.AreEqual(0d, barn.XMetres);
        Assert.AreEqual(-23d, barn.ZMetres);
        var grid = new TraversalGrid(NavigationFixture.CreateLowerWitteringTerrain());
        Assert.IsFalse(grid.Get(TraversalGrid.WorldToCell(0, -23_000)).IsWalkable);
        foreach (var cell in new[] { GameSession.MedicalMedicCell, GameSession.MedicalRestCell,
                     GameSession.DisorderSecurityBaseCell, GameSession.MedicalExitCell })
            Assert.IsTrue(grid.Get(cell).IsWalkable, $"Approach {cell} must remain walkable.");
        Assert.AreNotEqual(GameSession.MedicalTentCell, GameSession.DisorderSecurityPostCell);
    }

    [TestMethod]
    public void InspectingScenarioObjectsCannotMutatePausedAuthoritativeState()
    {
        var session = new GameSession(606, new CampaignId(606));
        var pause = session.Execute(new CommandEnvelope(new CommandId(1), session.CampaignId, session.Phase,
            session.CurrentTick, session.NextSubmissionSequence, null, new SetPausedCommand(true)));
        Assert.IsTrue(pause.IsAccepted);
        var before = session.CaptureSnapshot().AuthoritativeHash;
        var scene = LowerWitteringFarmScenario.CreateReadModel();
        _ = scene.GetRequiredObject("farm.farmhouse");
        _ = scene.GetRequiredObject("farm.main-gate");
        _ = scene.GetRequiredObject("farm.service-point");
        Assert.AreEqual(before, session.CaptureSnapshot().AuthoritativeHash);
        Assert.IsTrue(session.IsPaused);
    }
}

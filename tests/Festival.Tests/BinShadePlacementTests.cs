using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class BinShadePlacementTests
{
    // Clear grass away from both audience reserves, valid at every quarter turn.
    private static readonly GridCell Centre = new(104, 180);
    private static GridCell Offset(GridCell centre, int turns, int x, int z)
    {
        for (var i = 0; i < turns; i++) (x, z) = (z, -x);
        return new(centre.X + x, centre.Z + z);
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)]
    public void EitherPlacementOrderAllowsCanopyBinAndKeepsPolesOtherObjectsAndAccess(int turns)
    {
        foreach (var binFirst in new[] { false, true })
        {
            var s = BuildSession.Ready(); var bin = Offset(Centre, turns, 2, 3);
            var tentCommand = new PlaceBuildServiceCommand(BuildServiceKind.Marquee, Centre, turns);
            var binCommand = new PlaceBuildServiceCommand(BuildServiceKind.Bin, bin);
            BuildSession.Accept(s, binFirst ? binCommand : tentCommand);
            BuildSession.Accept(s, binFirst ? tentCommand : binCommand);
            var hash = s.CaptureSnapshot().AuthoritativeHash;
            foreach (var pole in GameSession.MarqueeSolidCells(Centre, turns))
                Assert.IsFalse(BuildSession.Send(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, pole)).IsAccepted);
            Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash, "Rejected placements must not relocate or charge anything.");
            Assert.IsFalse(BuildSession.Send(s, new MoveBuildServiceCommand("water.main", Offset(Centre, turns, -2, 3))).IsAccepted);
            Assert.IsFalse(BuildSession.Send(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, bin)).IsAccepted, "Bins cannot overlap each other.");
            BuildSession.Accept(s, new StartPreparedEditionCommand());
            var grid = s.TraversalGrid!;
            Assert.IsTrue(GameSession.MarqueeSolidCells(Centre, turns).All(cell => !grid.Get(cell).IsWalkable));
            Assert.IsFalse(grid.Get(bin).IsWalkable);
            foreach (var side in new[] { new GridCell(bin.X + 2, bin.Z), new(bin.X - 2, bin.Z), new(bin.X, bin.Z + 2), new(bin.X, bin.Z - 2) })
                Assert.IsTrue(DeterministicPathfinder.FindPath(grid, GameSession.MedicalExitCell, side).Found, "All bin interaction sides must remain reachable.");
            var rest = GameSession.MarqueeRestSpots(Centre, turns, s.CaptureBuildPlacements());
            Assert.AreEqual(15, rest.Length); Assert.IsTrue(rest.All(cell => grid.Get(cell).IsWalkable && s.InMarqueeShade(cell) && s.IsMarqueeRestSpot(cell)));
            var saved = s.CapturePersistenceSnapshot(); var restored = GameSession.Restore(saved);
            Assert.IsTrue(restored.IsSuccess, restored.Error);
            Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
            CollectionAssert.AreEqual(rest, GameSession.MarqueeRestSpots(Centre, turns, restored.Session.CaptureBuildPlacements()));
        }
    }

    [TestMethod]
    public void MovingTentOverBinAndMovingBinUnderTentUseTheSameRule()
    {
        var s = BuildSession.Ready(); var bin = Offset(Centre, 0, 2, 3);
        BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Bin, bin));
        BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Marquee, new(Centre.X - 24, Centre.Z)));
        BuildSession.Accept(s, new MoveBuildServiceCommand("marquee.main", Centre));
        var binId = s.CaptureBuildPlacements().Single(p => p.Kind == BuildServiceKind.Bin && p.Cell == bin).Id;
        BuildSession.Accept(s, new MoveBuildServiceCommand(binId, Offset(Centre, 0, -2, 3)));
        var hash = s.CaptureSnapshot().AuthoritativeHash;
        var pole = GameSession.MarqueeSolidCells(Centre, 0)[0];
        Assert.IsFalse(BuildSession.Send(s, new MoveBuildServiceCommand(binId, pole)).IsAccepted);
        Assert.IsFalse(BuildSession.Send(s, new MoveBuildServiceCommand("marquee.main", new(bin.X - 4, bin.Z - 1))).IsAccepted);
        Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash);
        Assert.IsTrue(GameSession.Restore(s.CapturePersistenceSnapshot()).IsSuccess);
    }
}

using Festival.Simulation;
using Festival.Simulation.Fixtures;

namespace Festival.Tests;

[TestClass]
public sealed class FarmGateTraversalTests
{
    [TestMethod]
    public void OpenLeafAndSeparatePostsBlockWalkingButLeaveEntranceAndEndpointsConnected()
    {
        var grid = new TraversalGrid(NavigationFixture.CreateLowerWitteringTerrain());
        var solid = FarmGateGeometry.SolidCells();
        Assert.IsTrue(solid.Length > 0 && solid.All(c => !grid.Get(c).IsWalkable));
        Assert.IsFalse(grid.Get(TraversalGrid.WorldToCell(-1150, 28_500)).IsWalkable, "The screenshot's diagonal leaf is physical, not the entrance gap.");
        foreach (var x in new[] { -1830, 1830 }) Assert.IsFalse(grid.Get(TraversalGrid.WorldToCell(x, 30_000)).IsWalkable);
        var inside = TraversalGrid.WorldToCell(0, 25_000); var gate = TraversalGrid.WorldToCell(0, 30_000);
        Assert.IsTrue(grid.Get(gate).IsWalkable);
        // Every existing guest arrival/exit lane, including the off-site waiting rows.
        for (var i = 0; i < 60; i++)
        {
            var endpoint = FarmGateGeometry.GuestWaitingCell(new(122 + i % 6 * 2, 190 + i / 6 * 2));
            Assert.IsTrue(grid.Get(endpoint).IsWalkable);
            var route = DeterministicPathfinder.FindPath(grid, endpoint, inside);
            Assert.IsTrue(route.Found); Assert.IsFalse(route.Path.Any(solid.Contains));
            AssertGateSegmentClearance(route.Path);
            Assert.IsTrue(DeterministicPathfinder.FindPath(grid, inside, endpoint).Found);
        }
        var acrossLeaf = DeterministicPathfinder.FindPath(grid,
            TraversalGrid.WorldToCell(-2750, 28_000), TraversalGrid.WorldToCell(750, 28_000));
        Assert.IsTrue(acrossLeaf.Found); Assert.IsFalse(acrossLeaf.Path.Any(solid.Contains));
        AssertGateSegmentClearance(acrossLeaf.Path);
        Assert.IsTrue(acrossLeaf.Path.Any(c => TraversalGrid.CellCentre(c).ZMillimetres < 26_750 || TraversalGrid.CellCentre(c).ZMillimetres > 30_250),
            "The route must go around an end of the open leaf.");
    }

    [TestMethod]
    public void EveryWaitingGuestStartsOnTheMapAndCanReachTheGate()
    {
        var start = (Func<int, GridCell>)typeof(GameSession).GetMethod("GuestWaitingStart",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.CreateDelegate(typeof(Func<int, GridCell>));
        // Today's rows are unchanged, so existing days and saves start everyone where they did.
        for (var i = 0; i < 198; i++) Assert.AreEqual(FarmGateGeometry.GuestWaitingCell(new(122 + i % 6 * 2, 190 + i / 6 * 2)), start(i), $"guest {i}");
        // Past them (the 199th guest used to start off the bottom of the map), the odd columns take over.
        var grid = new TraversalGrid(NavigationFixture.CreateLowerWitteringTerrain());
        var inside = TraversalGrid.WorldToCell(0, 25_000);
        var cells = Enumerable.Range(0, 358).Select(start).ToArray();
        Assert.AreEqual(cells.Length, cells.Distinct().Count(), "No two of the first 358 share a start.");
        foreach (var cell in cells)
        {
            Assert.IsTrue(grid.Contains(cell) && grid.Get(cell).IsWalkable, $"{cell}");
            Assert.IsTrue(DeterministicPathfinder.FindPath(grid, cell, inside).Found, $"{cell} reaches the gate");
        }
        Assert.AreEqual(start(0), start(358), "A crowd beyond the lane's room reuses start cells rather than leaving the map.");
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)]
    public void FootprintUsesGateTranslationAndParentYawAsWellAsLeafHinge(int turns)
    {
        var model = LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.main-gate") with { XMetres = 5, ZMetres = 10, YawQuarterTurns = turns };
        var polygons = FarmGateGeometry.ObstaclePolygons(model);
        Assert.AreEqual(3, polygons.Length);
        var blocked = FarmGateGeometry.SolidCells(model);
        foreach (var polygon in polygons)
        {
            var centreX = (int)polygon.Average(p => p.X); var centreZ = (int)polygon.Average(p => p.Z);
            Assert.IsTrue(blocked.Contains(TraversalGrid.WorldToCell(centreX, centreZ)));
        }
        Assert.IsFalse(blocked.Contains(TraversalGrid.WorldToCell(5000, 10_000)), "The actual opening must not become a combined posts box.");
    }

    [TestMethod]
    public void RoundedHedgeEndsBlockFoliageCrossingsWithoutTrappingArrivalRows()
    {
        var grid = new TraversalGrid(NavigationFixture.CreateLowerWitteringTerrain());
        var hedge = FarmGateGeometry.HedgeEndSolidCells();
        Assert.IsTrue(hedge.All(c => !grid.Get(c).IsWalkable));
        foreach (var x in new[] { -2500, 2500 }) Assert.IsFalse(grid.Get(TraversalGrid.WorldToCell(x, 32_000)).IsWalkable);
        var inside = TraversalGrid.WorldToCell(0, 25_000);
        for (var i = 0; i < 60; i++)
        {
            var start = FarmGateGeometry.GuestWaitingCell(new(122 + i % 6 * 2, 190 + i / 6 * 2));
            Assert.IsTrue(grid.Get(start).IsWalkable);
            var route = DeterministicPathfinder.FindPath(grid, start, inside);
            Assert.IsTrue(route.Found); Assert.IsFalse(route.Path.Any(hedge.Contains));
            // Independently sample straight walking segments against padded hedge bounds.
            foreach (var (a, b) in route.Path.Zip(route.Path.Skip(1)))
            {
                var p = TraversalGrid.CellCentre(a); var q = TraversalGrid.CellCentre(b);
                for (var step = 0; step <= 100; step++)
                {
                    var x = p.XMillimetres + (q.XMillimetres - p.XMillimetres) * step / 100.0;
                    var z = p.ZMillimetres + (q.ZMillimetres - p.ZMillimetres) * step / 100.0;
                    Assert.IsFalse(x > -3300 && x < -1800 && z > 31337 && z < 32700 ||
                        x > 1800 && x < 3300 && z > 31300 && z < 32663, "A route segment must not clip the hedge/body clearance.");
                }
            }
        }
    }

    [TestMethod]
    public void ExactlyTwoObstructedWaitingSlotsMoveToUniqueClearCells()
    {
        var original = Enumerable.Range(0, 60).Select(i => new GridCell(122 + i % 6 * 2, 190 + i / 6 * 2)).ToArray();
        var adjusted = original.Select(FarmGateGeometry.GuestWaitingCell).ToArray();
        Assert.AreEqual(2, original.Zip(adjusted).Count(pair => pair.First != pair.Second));
        Assert.AreEqual(adjusted.Length, adjusted.Distinct().Count());
        var grid = new TraversalGrid(NavigationFixture.CreateLowerWitteringTerrain());
        Assert.IsTrue(adjusted.All(c => grid.Get(c).IsWalkable));
    }

    [TestMethod]
    public void SharedAuthoritativeGridReroutesGuestStaffAndPerformerAndRestoresExactly()
    {
        var s = BuildSession.Started(); s.AdvanceWithoutSnapshot(GameSession.FestivalSlotStarts[0] + 400);
        var blocked = FarmGateGeometry.SolidCells().ToHashSet();
        var snapshot = s.CaptureObservation();
        foreach (var role in new[] { ProtectedPersonRole.Guest, ProtectedPersonRole.Staff, ProtectedPersonRole.Performer })
        {
            var person = s.CapturePreparation()!.People.First(p => p.Role == role);
            var agent = snapshot.NavigationAgents.Single(p => p.Id.Value == person.AgentId);
            Assert.IsFalse(blocked.Contains(TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres)));
        }
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot()); Assert.IsTrue(restored.IsSuccess, restored.Error);
        s.AdvanceWithoutSnapshot(800); restored.Session!.AdvanceWithoutSnapshot(800);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.Session.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(25, s.OnSiteAttendeeCount, "Entry remains live for the whole Tier 1 crowd.");
    }

    [TestMethod]
    [DataRow("tier-1")] [DataRow("pond-trial")] [DataRow("tier-2")]
    public void NobodyStartsOrIsPostedOnTheGateOrHedgeEnds(string day)
    {
        // Every actor's start (guests in the waiting rows, staff at the farmhouse door, both stages' bands on the lane) and
        // every staff post on the default layout is open ground, off the gate and hedge ends, with a way onto the field.
        var s = day switch
        {
            "tier-1" => BuildSession.Started(),
            "pond-trial" => BuildSession.PondStarted(),
            _ => NextFestivalTests.Ready(GameSession.CreateDevelopmentFestival(20260922, 2)),
        };
        if (day == "tier-2") BuildSession.Accept(s, new StartPreparedEditionCommand());
        Assert.AreEqual(PreparationStatus.Running, s.PreparedStatus);
        var grid = s.TraversalGrid!;
        var blocked = FarmGateGeometry.SolidCells().Concat(FarmGateGeometry.HedgeEndSolidCells()).ToHashSet();
        var inside = TraversalGrid.WorldToCell(0, 25_000);
        void Open(GridCell cell, string what)
        {
            Assert.IsFalse(blocked.Contains(cell), $"{what} at {cell} is on the gate or a hedge end.");
            Assert.IsTrue(grid.Get(cell).IsWalkable, $"{what} at {cell} is not walkable.");
            Assert.IsTrue(DeterministicPathfinder.FindPath(grid, cell, inside).Found && DeterministicPathfinder.FindPath(grid, inside, cell).Found,
                $"{what} at {cell} has no way onto the field and back.");
        }
        var people = s.CapturePreparation()!.People;
        Assert.IsTrue(people.Any(p => p.Role == ProtectedPersonRole.Performer) && people.Any(p => p.Role == ProtectedPersonRole.Staff));
        foreach (var agent in s.CaptureSnapshot().NavigationAgents)
        {
            var role = people.Single(p => p.AgentId == agent.Id.Value).Role;
            Open(TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres), $"{role} {agent.Id.Value}'s start");
            if (agent.Destination is { } destination) Open(destination, $"{role} {agent.Id.Value}'s destination");
            if (role == ProtectedPersonRole.Staff && s.StaffAssignedPost(agent.Id.Value) is { } post) Open(post, $"Staff {agent.Id.Value}'s post");
        }
        Open(GameSession.MedicalExitCell, "The medical exit");
        Open(LavSuckerRules.Gate, "The lav-sucker's way in");
    }

    private static void AssertGateSegmentClearance(IReadOnlyList<GridCell> route)
    {
        foreach (var (a, b) in route.Zip(route.Skip(1)))
        {
            var p = TraversalGrid.CellCentre(a); var q = TraversalGrid.CellCentre(b);
            for (var step = 0; step <= 100; step++)
            {
                var x = p.XMillimetres + (q.XMillimetres - p.XMillimetres) * step / 100.0;
                var z = p.ZMillimetres + (q.ZMillimetres - p.ZMillimetres) * step / 100.0;
                foreach (var polygon in FarmGateGeometry.ObstaclePolygons())
                {
                    var crosses = polygon.Select((v, i) => { var w = polygon[(i + 1) % polygon.Length]; return (w.X - v.X) * (z - v.Z) - (w.Z - v.Z) * (x - v.X); }).ToArray();
                    Assert.IsFalse(crosses.All(c => c >= 0) || crosses.All(c => c <= 0), "A walking segment must not enter the actual timber.");
                    foreach (var (v, w) in polygon.Zip(polygon.Skip(1).Append(polygon[0])))
                    {
                        var dx = w.X - v.X; var dz = w.Z - v.Z;
                        var t = Math.Clamp(((x - v.X) * dx + (z - v.Z) * dz) / (dx * dx + dz * dz), 0, 1);
                        var distanceSquared = Math.Pow(x - v.X - t * dx, 2) + Math.Pow(z - v.Z - t * dz, 2);
                        Assert.IsTrue(distanceSquared >= 199 * 199, "The walking body must clear the leaf and each post.");
                    }
                }
            }
        }
    }
}

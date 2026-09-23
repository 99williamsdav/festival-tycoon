using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class PathfinderDifferentialTests
{
    [TestMethod]
    public void IndexedPathfinderExactlyMatchesFrozenReferenceCases()
    {
        var highCost = HighCostExactMaximumCase();
        var cases = new List<(TraversalGrid Grid, GridCell Start, GridCell Target)>
        {
            (new TraversalGrid(), new GridCell(10, 10), new GridCell(10, 10)),
            (new TraversalGrid(), new GridCell(10, 10), new GridCell(30, 23)),
            (new TraversalGrid(new[] { new TerrainCellOverride(new GridCell(10, 10), GroundSurface.Grass, false) }),
                new GridCell(10, 10), new GridCell(12, 12)),
            (new TraversalGrid(new[]
            {
                new TerrainCellOverride(new GridCell(11, 10), GroundSurface.Grass, false),
                new TerrainCellOverride(new GridCell(10, 11), GroundSurface.Grass, false),
            }), new GridCell(10, 10), new GridCell(12, 12)),
            (WeightedGrid(), new GridCell(10, 10), new GridCell(30, 10)),
            (EnclosedGrid(), new GridCell(12, 12), new GridCell(20, 20)),
            highCost,
        };

        foreach (var item in cases) AssertEquivalent(item.Grid, item.Start, item.Target);
        var highCostResult = FrozenReference.FindPath(highCost.Grid, highCost.Start, highCost.Target);
        Assert.IsTrue(highCostResult.Found);
        Assert.AreEqual((long)int.MaxValue, highCostResult.Path.Skip(1).Sum(cell => (long)highCost.Grid.Get(cell).CostPermille));
    }

    [TestMethod]
    public void IndexedPathfinderMatchesSeededRandomAndInterleavedCalls()
    {
        var random = new Random(20260924);
        var cases = new List<(TraversalGrid Grid, GridCell Start, GridCell Target)>();
        for (var sample = 0; sample < 40; sample++)
        {
            var overrides = BoundedRegion(8, 8, 31, 31).ToList();
            for (var z = 9; z < 31; z++)
            for (var x = 9; x < 31; x++)
            {
                var roll = random.Next(100);
                if (roll < 19) overrides.Add(new TerrainCellOverride(new GridCell(x, z), GroundSurface.Grass, false));
                else if (roll < 44) overrides.Add(new TerrainCellOverride(new GridCell(x, z), GroundSurface.VehicleTrack, true,
                    random.Next(1, 2_501)));
            }
            var start = new GridCell(random.Next(9, 31), random.Next(9, 31));
            var target = new GridCell(random.Next(9, 31), random.Next(9, 31));
            overrides.RemoveAll(item => item.Cell == start || item.Cell == target);
            cases.Add((new TraversalGrid(overrides), start, target));
        }

        foreach (var item in cases) AssertEquivalent(item.Grid, item.Start, item.Target);
        foreach (var item in cases.AsEnumerable().Reverse()) AssertEquivalent(item.Grid, item.Start, item.Target);
        for (var index = 0; index < cases.Count; index += 3)
        {
            var item = cases[index];
            var first = DeterministicPathfinder.FindPath(item.Grid, item.Start, item.Target);
            _ = DeterministicPathfinder.FindPath(cases[(index + 1) % cases.Count].Grid,
                cases[(index + 1) % cases.Count].Start, cases[(index + 1) % cases.Count].Target);
            var repeated = DeterministicPathfinder.FindPath(item.Grid, item.Start, item.Target);
            AssertResult(first, repeated, $"repeated sample {index}");
        }
    }

    private static void AssertEquivalent(TraversalGrid grid, GridCell start, GridCell target)
    {
        var expected = FrozenReference.FindPath(grid, start, target);
        var actual = DeterministicPathfinder.FindPath(grid, start, target);
        AssertResult(expected, actual, $"{start} -> {target}");
    }

    private static void AssertResult(PathSearchResult expected, PathSearchResult actual, string label)
    {
        Assert.AreEqual(expected.Found, actual.Found, label);
        Assert.AreEqual(expected.ExpandedNodes, actual.ExpandedNodes, label);
        CollectionAssert.AreEqual(expected.Path.ToArray(), actual.Path.ToArray(), label);
    }

    private static TraversalGrid WeightedGrid()
    {
        var overrides = new List<TerrainCellOverride>();
        for (var z = 11; z <= 15; z++) overrides.Add(new TerrainCellOverride(new GridCell(10, z), GroundSurface.VehicleTrack, true, 100));
        for (var x = 11; x <= 30; x++) overrides.Add(new TerrainCellOverride(new GridCell(x, 15), GroundSurface.VehicleTrack, true, 100));
        for (var z = 10; z <= 14; z++) overrides.Add(new TerrainCellOverride(new GridCell(30, z), GroundSurface.VehicleTrack, true, 100));
        return new TraversalGrid(overrides);
    }

    private static TraversalGrid EnclosedGrid()
    {
        var overrides = BoundedRegion(8, 8, 24, 24).ToList();
        for (var dz = -1; dz <= 1; dz++)
        for (var dx = -1; dx <= 1; dx++)
            if (dx != 0 || dz != 0)
                overrides.Add(new TerrainCellOverride(new GridCell(20 + dx, 20 + dz), GroundSurface.Grass, false));
        return new TraversalGrid(overrides);
    }

    private static (TraversalGrid Grid, GridCell Start, GridCell Target) HighCostExactMaximumCase()
    {
        var path = new List<GridCell>();
        for (var row = 0; row < 7; row++)
        {
            var z = 40 + row * 2;
            if ((row & 1) == 0)
                for (var x = 20; x <= 240; x++) path.Add(new GridCell(x, z));
            else
                for (var x = 240; x >= 20; x--) path.Add(new GridCell(x, z));
            if (row < 6) path.Add(new GridCell((row & 1) == 0 ? 240 : 20, z + 1));
        }
        path = path.Take(1_433).ToList(); // 1,432 orthogonal edges.
        var overrides = new Dictionary<GridCell, TerrainCellOverride>();
        for (var z = 39; z <= 53; z++)
        for (var x = 19; x <= 241; x++)
        {
            var cell = new GridCell(x, z);
            overrides[cell] = new TerrainCellOverride(cell, GroundSurface.Grass, false);
        }
        var ordinaryCost = int.MaxValue / 1_432;
        for (var index = 0; index < path.Count; index++)
        {
            var cost = index == 0 ? ordinaryCost
                : index < path.Count - 1 ? ordinaryCost
                : checked((int)(int.MaxValue - (long)ordinaryCost * (path.Count - 2)));
            overrides[path[index]] = new TerrainCellOverride(path[index], GroundSurface.VehicleTrack, true, cost);
        }
        return (new TraversalGrid(overrides.Values), path[0], path[^1]);
    }

    private static IEnumerable<TerrainCellOverride> BoundedRegion(int minX, int minZ, int maxX, int maxZ)
    {
        for (var x = minX; x <= maxX; x++)
        {
            yield return new TerrainCellOverride(new GridCell(x, minZ), GroundSurface.Grass, false);
            yield return new TerrainCellOverride(new GridCell(x, maxZ), GroundSurface.Grass, false);
        }
        for (var z = minZ + 1; z < maxZ; z++)
        {
            yield return new TerrainCellOverride(new GridCell(minX, z), GroundSurface.Grass, false);
            yield return new TerrainCellOverride(new GridCell(maxX, z), GroundSurface.Grass, false);
        }
    }

    private static class FrozenReference
    {
        private static readonly (int X, int Z, int Cost)[] Neighbours =
        [
            (0, -1, 1000), (1, 0, 1000), (0, 1, 1000), (-1, 0, 1000),
            (1, -1, 1414), (1, 1, 1414), (-1, 1, 1414), (-1, -1, 1414),
        ];

        public static PathSearchResult FindPath(TraversalGrid grid, GridCell start, GridCell target)
        {
            if (!grid.Contains(start) || !grid.Contains(target) || !grid.Get(start).IsWalkable || !grid.Get(target).IsWalkable)
                return new PathSearchResult(false, Array.Empty<GridCell>(), 0);
            var open = new List<GridCell> { start };
            var openSet = new HashSet<GridCell> { start };
            var closed = new HashSet<GridCell>();
            var cameFrom = new Dictionary<GridCell, GridCell>();
            var scores = new Dictionary<GridCell, int> { [start] = 0 };
            var expanded = 0;
            var minimumCost = grid.MinimumWalkableCostPermille;
            while (open.Count > 0 && expanded < TraversalGrid.Width * TraversalGrid.Depth)
            {
                var bestIndex = 0;
                for (var index = 1; index < open.Count; index++)
                    if (Compare(open[index], open[bestIndex], target, scores, minimumCost) < 0) bestIndex = index;
                var current = open[bestIndex];
                open.RemoveAt(bestIndex);
                openSet.Remove(current);
                if (current == target) return new PathSearchResult(true, Reconstruct(cameFrom, current), expanded);
                closed.Add(current);
                expanded++;
                foreach (var (dx, dz, baseCost) in Neighbours)
                {
                    var neighbour = new GridCell(current.X + dx, current.Z + dz);
                    if (!grid.Contains(neighbour) || !grid.Get(neighbour).IsWalkable) continue;
                    if (dx != 0 && dz != 0 &&
                        (!grid.Get(new GridCell(current.X + dx, current.Z)).IsWalkable ||
                         !grid.Get(new GridCell(current.X, current.Z + dz)).IsWalkable)) continue;
                    var stepCost = checked(baseCost * grid.Get(neighbour).CostPermille / 1000);
                    var tentative = checked(scores[current] + stepCost);
                    if (scores.TryGetValue(neighbour, out var known) && tentative >= known) continue;
                    cameFrom[neighbour] = current;
                    scores[neighbour] = tentative;
                    closed.Remove(neighbour);
                    if (openSet.Add(neighbour)) open.Add(neighbour);
                }
            }
            return new PathSearchResult(false, Array.Empty<GridCell>(), expanded);
        }

        private static int Compare(GridCell left, GridCell right, GridCell target,
            IReadOnlyDictionary<GridCell, int> scores, int minimumCost)
        {
            var leftH = Heuristic(left, target, minimumCost); var rightH = Heuristic(right, target, minimumCost);
            var comparison = (scores[left] + leftH).CompareTo(scores[right] + rightH);
            if (comparison != 0) return comparison;
            comparison = leftH.CompareTo(rightH);
            if (comparison != 0) return comparison;
            comparison = left.Z.CompareTo(right.Z);
            return comparison != 0 ? comparison : left.X.CompareTo(right.X);
        }

        private static int Heuristic(GridCell from, GridCell to, int minimumCost)
        {
            var dx = Math.Abs(from.X - to.X); var dz = Math.Abs(from.Z - to.Z);
            var diagonal = Math.Min(dx, dz); var orthogonal = Math.Abs(dx - dz);
            return diagonal * (1414 * minimumCost / 1000) + orthogonal * (1000 * minimumCost / 1000);
        }

        private static IReadOnlyList<GridCell> Reconstruct(IReadOnlyDictionary<GridCell, GridCell> cameFrom, GridCell current)
        {
            var path = new List<GridCell> { current };
            while (cameFrom.TryGetValue(current, out var previous)) { current = previous; path.Add(current); }
            path.Reverse();
            return path;
        }
    }
}

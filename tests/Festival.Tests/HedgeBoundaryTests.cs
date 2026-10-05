using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class HedgeBoundaryTests
{
    [TestMethod]
    public void EveryoneComesInThroughTheGateNotTheHedge()
    {
        var s = Started();
        var staff = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Staff).Select(p => p.AgentId).ToHashSet();
        var bands = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Performer).Select(p => p.AgentId).ToHashSet();
        var crossings = 0;
        for (var tick = 0; tick < 6_000; tick += 8)
        {
            s.AdvanceWithoutSnapshot(8);
            foreach (var agent in s.CaptureObservation().NavigationAgents)
            {
                var cell = TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres);
                // The north hedge line, cells 191 and 192: only the gate gap is open.
                if (cell.Z is 191 or 192) { crossings++; Assert.IsTrue(cell.X is >= 122 and <= 133, $"Person {agent.Id.Value} at {cell} went through the hedge."); }
                // Bands come in off the lane through the garden gate in the west hedge, and nowhere else.
                if (bands.Contains(agent.Id.Value) && cell.X <= 64) { Assert.IsTrue(cell.X < 63 || Backstage.HedgeGate(cell), $"Band member {agent.Id.Value} at {cell} went through the hedge."); continue; }
                Assert.IsTrue(cell.X is > 64 and < 191 || cell.Z > 192, $"Person {agent.Id.Value} at {cell} is in the side hedges.");
            }
        }
        Assert.IsTrue(crossings > 0 && staff.Count > 0, "People did come in through the gate.");
    }
}

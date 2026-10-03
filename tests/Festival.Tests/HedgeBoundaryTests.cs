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
        var crossings = 0;
        for (var tick = 0; tick < 6_000; tick += 8)
        {
            s.AdvanceWithoutSnapshot(8);
            foreach (var agent in s.CaptureObservation().NavigationAgents)
            {
                var cell = TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres);
                // The north hedge line, cells 191 and 192: only the gate gap is open.
                if (cell.Z is 191 or 192) { crossings++; Assert.IsTrue(cell.X is >= 122 and <= 133, $"Person {agent.Id.Value} at {cell} went through the hedge."); }
                Assert.IsTrue(cell.X is > 64 and < 191 || cell.Z > 192, $"Person {agent.Id.Value} at {cell} is in the side hedges.");
            }
        }
        Assert.IsTrue(crossings > 0 && staff.Count > 0, "People did come in through the gate.");
    }
}

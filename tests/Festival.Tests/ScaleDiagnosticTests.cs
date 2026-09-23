using Festival.Simulation;
using Festival.Simulation.Fixtures;

namespace Festival.Tests;

[TestClass]
public sealed class ScaleDiagnosticTests
{
    [TestMethod]
    [DataRow(50, ScaleDiagnosticLayout.Representative)]
    [DataRow(100, ScaleDiagnosticLayout.Congested)]
    public void FixtureUsesOneWorldWithRealPeopleWalletsAndThreeQueues(int population, ScaleDiagnosticLayout layout)
    {
        var fixture = ScaleDiagnosticFixture.Create(population, 20260923, layout);
        var snapshot = fixture.Session.CaptureSnapshot();
        Assert.AreEqual(population, snapshot.NavigationAgents.Count);
        Assert.AreEqual(population, snapshot.Wallets.Count);
        Assert.AreEqual(population, snapshot.ServiceQueues.SelectMany(queue => queue.Agents).Count());
        Assert.AreEqual(population, snapshot.ServiceQueues.SelectMany(queue => queue.Agents).Select(agent => agent.AgentId).Distinct().Count());
        Assert.AreEqual(3, snapshot.ServiceQueues.Count);
        Assert.IsTrue(snapshot.ServiceQueues.All(queue => queue.PhysicalArrivalAdmission));
        Assert.IsTrue(ScaleDiagnosticFixture.AllDeclaredAgentsPresentAndActive(fixture));
        Assert.AreEqual(fixture.RetargetCount, fixture.Session.CapturePersistenceSnapshot().AppliedCommands
            .Count(command => command.CommandType == nameof(RetargetServiceQueueAgentFixtureCommand)));
    }

    [TestMethod]
    public void DiagnosticProbeDoesNotChangeAuthoritativeState()
    {
        var measured = ScaleDiagnosticFixture.Create(50, 20260924, ScaleDiagnosticLayout.Representative,
            new ScaleDiagnosticProbe());
        var unmeasured = ScaleDiagnosticFixture.Create(50, 20260924, ScaleDiagnosticLayout.Representative);
        measured.Session.AdvanceTicks(25);
        unmeasured.Session.AdvanceTicks(25);
        Assert.AreEqual(unmeasured.Session.CaptureSnapshot().AuthoritativeHash,
            measured.Session.CaptureSnapshot().AuthoritativeHash);
        Assert.IsTrue(measured.Session.ScaleDiagnosticProbe!.Capture().SnapshotInclusiveMs > 0);
    }

    [TestMethod]
    public void CongestedLayoutAddsARealNarrowFenceOpening()
    {
        var fixture = ScaleDiagnosticFixture.Create(50, 20260923, ScaleDiagnosticLayout.Congested);
        Assert.IsFalse(fixture.Session.TraversalGrid!.Get(new GridCell(150, 118)).IsWalkable);
        Assert.IsTrue(fixture.Session.TraversalGrid.Get(new GridCell(150, 119)).IsWalkable);
        Assert.IsTrue(fixture.Session.TraversalGrid.Get(new GridCell(150, 120)).IsWalkable);
        Assert.IsFalse(fixture.Session.TraversalGrid.Get(new GridCell(150, 121)).IsWalkable);
    }
}

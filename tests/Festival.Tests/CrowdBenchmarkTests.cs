using Festival.Simulation.Fixtures;

namespace Festival.Tests;

[TestClass]
public sealed class CrowdBenchmarkTests
{
    [TestMethod]
    public void SameBenchmarkConfigurationIsAuthoritativelyRepeatable()
    {
        var left = CrowdBenchmarkFixture.Create(50, BenchmarkPassage.Wide);
        var right = CrowdBenchmarkFixture.Create(50, BenchmarkPassage.Wide);
        for (var tick = 0; tick < 250; tick++) { left.AdvanceOneTick(); right.AdvanceOneTick(); }
        Assert.AreEqual(left.CompositeHash(), right.CompositeHash());
        Assert.AreEqual(left.Completed, right.Completed);
        Assert.AreEqual(left.Backlog, right.Backlog);
    }

    [TestMethod]
    public void WideControlledPassageCompletesSameDemandSoonerWithoutFailuresOrRecovery()
    {
        var narrow = CrowdBenchmarkFixture.Create(50, BenchmarkPassage.Narrow);
        var wide = CrowdBenchmarkFixture.Create(50, BenchmarkPassage.Wide);
        while (!narrow.AllCompleted && narrow.ControllerTick < 10_000) narrow.AdvanceOneTick();
        while (!wide.AllCompleted && wide.ControllerTick < 10_000) wide.AdvanceOneTick();
        Assert.IsTrue(narrow.AllCompleted); Assert.IsTrue(wide.AllCompleted);
        Assert.AreEqual(50, narrow.Completed); Assert.AreEqual(50, wide.Completed);
        Assert.AreEqual(0, narrow.RouteFailures); Assert.AreEqual(0, wide.RouteFailures);
        Assert.AreEqual(0, narrow.Reserved); Assert.AreEqual(0, wide.Reserved);
        Assert.IsTrue(wide.ControllerTick < narrow.ControllerTick,
            $"Expected wide flow to improve completion: wide={wide.ControllerTick}, narrow={narrow.ControllerTick}.");
    }
}

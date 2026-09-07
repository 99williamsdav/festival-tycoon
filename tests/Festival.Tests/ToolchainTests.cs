using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class ToolchainTests
{
    [TestMethod]
    public void SmokeResultIsFixed()
    {
        Assert.AreEqual(
            "festival-tycoon-smoke|build=0.0.1-m0.01|seed=0|checksum=0000000000000000",
            ToolchainSmoke.GetFixedResult());
    }

    [TestMethod]
    public void SimulationAssemblyHasNoGodotDependency()
    {
        var references = typeof(ToolchainSmoke).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        CollectionAssert.DoesNotContain(references, "GodotSharp");
    }
}

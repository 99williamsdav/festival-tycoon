using Festival.ContentAdapter;
using Festival.Persistence;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class HoverFeedbackTests
{
    [TestMethod]
    public void FactualThirstyCopyKeepsExistingLayoutContentIdentity()
    {
        Assert.AreEqual("Guests grow thirsty faster.",PerkCatalogue.All.Single(p=>p.Id=="thirsty-crowd").Effect);
        Assert.AreEqual("0e6fa18d57decc54430afd5931e220b4477aa7cf793859883fc337ad600baa18",LowerWitteringFarmScenario.ContentCompatibilityHash);
    }
}

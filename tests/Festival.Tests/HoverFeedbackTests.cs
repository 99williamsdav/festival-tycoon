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
        Assert.AreEqual("aacc3f0797ddcceddda385a9aa07273797944a4a818bbb98c6459c2bd9e49a54",LowerWitteringFarmScenario.ContentCompatibilityHash);
    }
}

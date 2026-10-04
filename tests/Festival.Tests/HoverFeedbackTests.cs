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
        Assert.AreEqual("e134d789cbc2d06e562a7d2fc970fe7743d012cb9a70fa2f8586546e4d251e20",LowerWitteringFarmScenario.ContentCompatibilityHash);
    }
}

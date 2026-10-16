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
        Assert.AreEqual("1524338e625b43cfa00c60c393e793a2c5861e32a7c67549cb44a43cfb4c7d8e",LowerWitteringFarmScenario.ContentCompatibilityHash);
    }
}

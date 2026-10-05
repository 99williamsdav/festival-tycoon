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
        Assert.AreEqual("13e78ebc06b0b974f499403b7a12eeb55a362511692df94081190be0029f4eb0",LowerWitteringFarmScenario.ContentCompatibilityHash);
    }
}

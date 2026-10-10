using Festival.ContentAdapter;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class CouncilHearingPresentationTests
{
    [TestMethod]
    [DataRow("medical-death:1:1", "heat illness")]
    [DataRow("equipment-death:1:1", "generator incident")]
    [DataRow("disorder-death:1:1", "fatal injury")]
    public void HearingUsesHumaneChainSpecificCopyWithoutRawEvidence(string transactionId, string sequenceTail)
    {
        var casualty = new CasualtySnapshot(1, 1, "Guest 20", ProtectedPersonRole.Guest,
            "debug tick 6000; thirst 10000; load 120%; job Failed", 6000, transactionId);
        var presented = CouncilHearingPresenter.From(casualty);
        Assert.AreEqual("Guest 20 · attendee", presented.Person);
        StringAssert.Contains(presented.Sequence, sequenceTail);
        Assert.IsFalse(presented.Cause.Contains("6000") || presented.Cause.Contains("10000") ||
            presented.Cause.Contains("120%") || presented.Cause.Contains("job"));
        Assert.IsFalse(presented.Sequence.Contains("tick"));
    }

    /// <summary>A drink death isn't put down to the heat: a rainy festival has none.</summary>
    [TestMethod]
    public void ADrinkDeathIsNotBlamedOnTheHeat()
    {
        var casualty = new CasualtySnapshot(1, 1, "Zoe Fenwick", ProtectedPersonRole.Guest,
            "Zoe Fenwick died after sustained intoxication 9100/10000; visible intoxication warning tick 100, collapse tick 200", 6000, "medical-death:1:1");
        var presented = CouncilHearingPresenter.From(casualty);
        StringAssert.Contains(presented.Sequence, "intoxication");
        Assert.IsFalse(presented.Cause.Contains("Heat") || presented.Sequence.Contains("heat"));
    }

    [TestMethod]
    public void IdentityComesFromCasualtyAndNotConceptExample()
    {
        var casualty = new CasualtySnapshot(1, 1, "Jordan Hale", ProtectedPersonRole.Staff,
            "technical account", 10, "disorder-death:1:1");
        Assert.AreEqual("Jordan Hale · staff member", CouncilHearingPresenter.From(casualty).Person);
    }
}

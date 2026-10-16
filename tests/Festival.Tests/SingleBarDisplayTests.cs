using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class SingleBarDisplayTests
{
    [TestMethod]
    public void OnePlacedBarUsesTheShortDisplayLabelWithoutChangingRecordedNamesOrState()
    {
        var session = BuildSession.Drafted();
        var before = session.CaptureSnapshot().AuthoritativeHash;
        Assert.AreEqual("Bar", session.StallDisplayName(Stalls.FirstBar));
        Assert.AreEqual("Bar", session.StallDisplayLabel(session.StallName(Stalls.FirstBar)));
        Assert.AreEqual("Bar", session.StallName(Stalls.FirstBar), "A lone bar by no stage is named \"Bar\" outright.");
        Assert.AreEqual("Bar 2", session.StallDisplayName("drinks.2"), "Unplaced inventory is not a second bar on the field.");
        Assert.AreEqual(session.StallName(Stalls.FirstVan), session.StallDisplayName(Stalls.FirstVan));
        Assert.AreEqual("A loose cow chewed through the East Bar cable.", session.StallDisplayLabel("A loose cow chewed through the East Bar cable."));
        Assert.AreEqual(before, session.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void AddingAndRemovingASecondBarRecomputesTheCurrentDisplayNames()
    {
        var session = NextFestivalTests.Drafted(GameSession.CreateDevelopmentFestival(20260922, 2));
        var second = session.CaptureBuildPlacements().Single(p => p.Id == "drinks.2");
        Assert.AreEqual("West Bar", session.StallDisplayName(Stalls.FirstBar));
        Assert.AreEqual("East Bar", session.StallDisplayName(second.Id));
        BuildSession.Accept(session, new RemoveBuildServiceCommand(second.Id));
        Assert.AreEqual("Bar", session.StallDisplayName(Stalls.FirstBar));
        Assert.AreEqual("Bar", BuildSession.Restored(session).StallDisplayName(Stalls.FirstBar));
        BuildSession.Accept(session, new PlaceBuildServiceCommand(second.Kind, second.Cell, second.QuarterTurns));
        Assert.AreEqual("West Bar", session.StallDisplayName(Stalls.FirstBar));
        Assert.AreEqual("East Bar", session.StallDisplayName(second.Id));
        var restored = BuildSession.Restored(session);
        Assert.AreEqual("West Bar", restored.StallDisplayName(Stalls.FirstBar));
        Assert.AreEqual("East Bar", restored.StallDisplayName(second.Id));
        BuildSession.Accept(session, new RemoveBuildServiceCommand(Stalls.FirstBar));
        Assert.AreEqual("Bar", session.StallDisplayName(second.Id), "The lone remaining bar need not have the first stable ID.");
        Assert.AreEqual("Bar", BuildSession.Restored(session).StallDisplayName(second.Id));
    }

    [TestMethod]
    public void TheShortLabelSurvivesPreparationToLiveAndRestoringBothPhases()
    {
        var session = BuildSession.Ready();
        Assert.AreEqual("Bar", BuildSession.Restored(session).StallDisplayName(Stalls.FirstBar));
        BuildSession.Accept(session, new StartPreparedEditionCommand());
        Assert.AreEqual("Bar", session.StallDisplayName(Stalls.FirstBar));
        var restored = BuildSession.Restored(session);
        var before = restored.CaptureSnapshot().AuthoritativeHash;
        Assert.AreEqual("Bar", restored.StallDisplayName(Stalls.FirstBar));
        Assert.AreEqual(before, restored.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void MultipleBarsKeepTheirDistinctNamesWhenStartingAndRestoringLive()
    {
        var session = NextFestivalTests.Ready(GameSession.CreateDevelopmentFestival(20260922, 2));
        var expected = session.StallNamesNow().Where(p => Stalls.IsBar(p.Key)).ToArray();
        Assert.AreEqual(2, expected.Length);
        Assert.AreEqual(2, expected.Select(p => p.Value).Distinct().Count());
        BuildSession.Accept(session, new StartPreparedEditionCommand());
        foreach (var state in new[] { session, BuildSession.Restored(session) })
            foreach (var bar in expected)
                Assert.AreEqual(bar.Value, state.StallDisplayName(bar.Key));
    }
}

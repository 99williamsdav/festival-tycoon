using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class BeerFestivalTests
{
    private static GameSession StartedStocked(ulong seed, bool beerFestival)
    {
        var s = beerFestival ? Planned(seed, PerkCatalogue.BeerFestival) : Planned(seed);
        // Enough beer that the stock isn't what limits sales.
        Accept(s, new SetPreparationStockCommand(40, 90));
        foreach (var hire in Crew(s)) Accept(s, hire);
        Accept(s, new StartPreparedEditionCommand());
        return s;
    }

    [TestMethod]
    public void BeerCostsHalfAgainAndNobodyIsTeetotal()
    {
        var s = Started(20260922, PerkCatalogue.BeerFestival);
        Assert.AreEqual(GameSession.ImmersionPrice(ImmersionProduct.Beer) * 3 / 2, s.ImmersionListPrice(ImmersionProduct.Beer));
        Assert.AreEqual(GameSession.ImmersionPrice(ImmersionProduct.SoftDrink), s.ImmersionListPrice(ImmersionProduct.SoftDrink), "Only beer is marked up.");
        var people = s.CapturePreparation()!.People;
        var performer = people.First(p => p.Role == ProtectedPersonRole.Performer).AgentId;
        Assert.AreEqual(s.ImmersionListPrice(ImmersionProduct.Beer) / 2, s.ImmersionPriceFor(performer, ImmersionProduct.Beer), "The band still pays half.");
        var abstainers = s.CaptureImmersion()!.People.Where(p => p.Abstains).Select(p => p.AgentId).ToArray();
        Assert.IsTrue(abstainers.Length > 0, "The roster still has its usual abstainers underneath.");
        Assert.IsFalse(abstainers.Any(s.Teetotal), "But at a beer festival they drink.");
        Assert.IsTrue(Started(20260922).Teetotal(abstainers[0]), "Without the perk they don't.");
    }

    [TestMethod]
    public void MoreBeerIsSoldForMoreAndTheDayStillRestores()
    {
        long before = 0, after = 0, takingsBefore = 0, takingsAfter = 0;
        foreach (var seed in new ulong[] { 20260924, 20260925, 20260928 })
            foreach (var festival in new[] { false, true })
            {
                var s = StartedStocked(seed, festival);
                s.AdvanceWithoutSnapshot(24_000);
                var beers = s.CaptureImmersion()!.Purchases.Where(p => p.Product == ImmersionProduct.Beer).ToArray();
                if (festival) { after += beers.Length; takingsAfter += beers.Sum(b => b.PricePennies); } else { before += beers.Length; takingsBefore += beers.Sum(b => b.PricePennies); }
                if (festival) Assert.IsTrue(beers.Any(b => s.CaptureImmersion()!.People.Single(p => p.AgentId == b.AgentId).Abstains), "A would-be abstainer bought a beer.");
                var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
                Assert.IsTrue(restored.IsSuccess, restored.Error);
            }
        Assert.IsTrue(after > before && after <= before * 3 / 2, $"A little more beer: {before} to {after}.");
        Assert.IsTrue(takingsAfter >= takingsBefore * 3 / 2, $"Takings {takingsBefore} to {takingsAfter}.");
    }
}

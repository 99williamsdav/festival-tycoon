using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class PerkRarityTests
{
    [TestMethod]
    public void EachCardHasItsOwnWeightAndTheDraftComesOutNearSixtyThirtyTen()
    {
        Assert.AreEqual(1000, PerkCatalogue.All.Sum(p => p.Weight));
        Assert.AreEqual(600, PerkCatalogue.All.Where(p => p.Rarity == PerkRarity.Common).Sum(p => p.Weight));
        Assert.AreEqual(300, PerkCatalogue.All.Where(p => p.Rarity == PerkRarity.Uncommon).Sum(p => p.Weight));
        Assert.AreEqual(100, PerkCatalogue.All.Where(p => p.Rarity == PerkRarity.Rare).Sum(p => p.Weight));
        var dealt = Enumerable.Range(0, 600).SelectMany(i => GameSession.CreateBuildCampaign(20260922UL + (ulong)i).CapturePerks()!.Hand)
            .Select(id => PerkCatalogue.All.Single(p => p.Id == id).Rarity).ToArray();
        double Share(PerkRarity rarity) => dealt.Count(r => r == rarity) / (double)dealt.Length;
        // Three different cards a hand pulls a little towards the rarer ones, against single draws.
        Assert.IsTrue(Share(PerkRarity.Common) is > .45 and < .65, $"Common {Share(PerkRarity.Common):P0}");
        Assert.IsTrue(Share(PerkRarity.Uncommon) is > .25 and < .40, $"Uncommon {Share(PerkRarity.Uncommon):P0}");
        Assert.IsTrue(Share(PerkRarity.Rare) is > .06 and < .18, $"Rare {Share(PerkRarity.Rare):P0}");
    }
}

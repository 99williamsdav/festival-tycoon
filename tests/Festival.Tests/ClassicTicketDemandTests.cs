using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class ClassicTicketDemandTests
{
    private static ClassicTicketDemandInput Input(int price = 1_000, int reputation = 0, params int[] popularity) =>
        new(25, reputation, price, popularity.Length == 0 ? [10, 15, 20] : popularity);

    [TestMethod]
    public void MarketIsStableWhilePriceAndPopularityChangeMonotonically()
    {
        for (ulong seed = 1; seed <= 80; seed++)
        {
            var last = 25;
            for (var price = 0; price <= 10_000; price += 50)
            {
                var input = Input(price);
                var sales = ClassicTicketDemand.Resolve(seed, 1, input);
                Assert.IsTrue(sales <= last, $"Price {price} increased sales for market {seed}.");
                Assert.AreEqual(sales, ClassicTicketDemand.Resolve(seed, 1, input));
                last = sales;
            }
            var previousPopularity = 0; var previousReputation = 0;
            for (var points = 0; points <= 100; points++)
            {
                var popular = ClassicTicketDemand.Resolve(seed, 1, Input(1_000, 0, points, points, points));
                var reputable = ClassicTicketDemand.Resolve(seed, 1, Input(1_000, points));
                Assert.IsTrue(popular >= previousPopularity);
                Assert.IsTrue(reputable >= previousReputation);
                previousPopularity = popular; previousReputation = reputable;
            }
        }
    }

    [TestMethod]
    public void GuidanceEnclosesPrivateMarketOutcomesAndCapacityIsHard()
    {
        foreach (var input in new[] { Input(0), Input(500), Input(), Input(1_500), Input(2_000), Input(10_000), Input(1_000, 100, 100, 100, 100), new(1, 0, 1_000, [0]) })
        {
            var estimate = ClassicTicketDemand.Estimate(input);
            for (ulong seed = 1; seed <= 2_000; seed++)
            {
                var sales = ClassicTicketDemand.Resolve(seed, 3, input);
                Assert.IsTrue(sales >= estimate.MinimumSales && sales <= estimate.MaximumSales);
                Assert.IsTrue(sales >= 0 && sales <= input.Capacity);
            }
        }
        Assert.AreEqual(0, ClassicTicketDemand.Resolve(1, 1, Input(10_000)));
        Assert.AreEqual(25, ClassicTicketDemand.Resolve(1, 1, Input(0, 100, 100, 100, 100)));
    }

    [TestMethod]
    public void DrawDiscountsOverlapAndDoesNotDependOnOrderGenreOrTalent()
    {
        var coherent = new[] { new FestivalAct("one", "One", 0, 100, 25), new("two", "Two", 0, 100, 25), new("three", "Three", 0, 100, 25) };
        var varied = coherent.Select((a, i) => a with { Id = "other" + i, Genre = i }).ToArray();
        Assert.IsTrue(coherent.Select(PerformanceRules.Talent).SequenceEqual(varied.Select(PerformanceRules.Talent)) == false,
            "Fixture must actually differ in live talent.");
        var first = Input(1_000, 0, coherent.Select(a => a.Popularity).ToArray());
        var second = Input(1_000, 0, varied.Select(a => a.Popularity).Reverse().ToArray());
        Assert.AreEqual(25, ClassicTicketDemand.Estimate(first).OverlappingPopularity);
        Assert.AreEqual(ClassicTicketDemand.Resolve(11, 1, first), ClassicTicketDemand.Resolve(11, 1, second));
        Assert.IsTrue(ClassicTicketDemand.Resolve(11, 1, first) > 0);
    }

    [TestMethod]
    public void ReadsDoNotChangeSessionRandomnessAndOrdinalGetsItsOwnMarket()
    {
        var session = GameSession.CreateClassicCampaign(20260922);
        var before = session.CapturePersistenceSnapshot();
        var quantities = new HashSet<int>();
        for (var number = 1; number <= 100; number++) quantities.Add(ClassicTicketDemand.Resolve(session.CampaignSeed, number, Input()));
        Assert.IsTrue(quantities.Count > 1);
        Assert.AreEqual(before.AuthoritativeHash, session.CapturePersistenceSnapshot().AuthoritativeHash);
        var restored = GameSession.Restore(before);
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(ClassicTicketDemand.Resolve(session.CampaignSeed, 1, Input()),
            ClassicTicketDemand.Resolve(restored.Session!.CampaignSeed, 1, Input()));
    }

    [TestMethod]
    public void ConservativeBudgetAllowsHonestNegativeProjectionWithoutBookingAllowance()
    {
        var estimate = ClassicTicketDemand.Estimate(Input());
        Assert.IsTrue(estimate.MinimumSales < estimate.MaximumSales);
        Assert.IsTrue(estimate.ConservativeSetupCash(-10_000, 1_000, 100_000) < -10_000);
        Assert.AreEqual(20_000 - 100_000, estimate.ConservativeSetupCash(20_000, 0, 100_000));
        Assert.Throws<ArgumentException>(() => ClassicTicketDemand.Estimate(Input(-1)));
        Assert.Throws<ArgumentException>(() => ClassicTicketDemand.Estimate(new(25, 101, 1_000, [10])));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClassicTicketDemand.Resolve(1, 0, Input()));
    }

    [TestMethod]
    public void SampledStarterMarketsOfferABasicRecoveryFromTheExistingLowestCarry()
    {
        foreach (var seed in Enumerable.Range(1, 64).Select(n => (ulong)n).Append(20260922UL))
        {
            var session = GameSession.CreateClassicCampaign(seed);
            var lineup = session.GetFestivalActs().Where(a => session.ActStandingOf(a) == ActStanding.Available)
                .OrderBy(session.ActFee).ThenBy(a => a.Id, StringComparer.Ordinal).Take(3).ToArray();
            var minimumStaff = session.GetStaffCandidates().GroupBy(c => c.Role).Sum(g => g.Min(c => c.WagePennies));
            var minimumFacilities = new[] { BuildServiceKind.WaterTap, BuildServiceKind.Toilet, BuildServiceKind.FirstAid, BuildServiceKind.StewardPost }
                .Sum(GameSession.BuildServiceFeePennies);
            var sales = ClassicTicketDemand.Resolve(seed, 1, Input(1_000, 0, lineup.Select(a => a.Popularity).ToArray()));
            Assert.IsTrue((long)sales * 1_000 - lineup.Sum(session.ActFee) >= minimumStaff + minimumFacilities,
                $"Market {seed} lacks the sampled basic recovery at the existing overdraft floor.");
        }
    }
}

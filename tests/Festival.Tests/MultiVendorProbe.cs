using System.Text;
using Festival.Simulation;

namespace Festival.Tests;

// Not a check: whole Tier 2 days with one bar and one van (the default) against two of each, Korma Chameleon at the
// second van. Run on its own: --filter "TestCategory=Probe&FullyQualifiedName~MultiVendorProbe". Writes to PROBE_OUT if
// set, else the test output.
[TestClass]
public sealed class MultiVendorProbe
{
    public TestContext TestContext { get; set; } = null!;

    // Default: Tier 2's default layout and plan (one bar, one chip van, 40/32 stock). DefaultDoubleStock: the same with
    // 80/64. FourStalls: plus a second bar and Korma Chameleon at a second van (MultiVendorTests' spots), 80/64 and the
    // bigger generator; WithBins adds a bin by each; NoGenerator leaves the farm diesel. SecondBarOnly and CurryVanOnly add
    // just one of them. CurryInsteadOfChips: the default layout with curry at its one van.
    private enum Layout { Default, DefaultDoubleStock, FourStalls, FourStallsWithBins, FourStallsNoGenerator, SecondBarOnly, CurryVanOnly, CurryInsteadOfChips }

    [TestCategory("Probe")]
    [TestCategory("Slow")]
    [TestMethod]
    public void ProbeTierTwoWithOneAndTwoOfEachStall()
    {
        // PROBE_LAYOUTS, e.g. "FourStalls", narrows the run to those layouts.
        var only = Environment.GetEnvironmentVariable("PROBE_LAYOUTS")?.Split(',');
        var days = (from layout in Enum.GetValues<Layout>() where only is null || only.Contains(layout.ToString())
            // PROBE_SEEDS, e.g. "10", runs that many seeds from the usual first one rather than three.
            from seed in Enumerable.Range(0, int.TryParse(Environment.GetEnvironmentVariable("PROBE_SEEDS"), out var count) ? count : 3).Select(i => 20260922UL + (ulong)i)
            select (layout, seed)).ToArray();
        var rows = new string[days.Length];
        Parallel.For(0, days.Length, index => rows[index] = Day(days[index].layout, days[index].seed));
        var report = new StringBuilder();
        report.AppendLine("layout,seed,status,deaths,collapses,stars,mood%,hungerClose,thirstClose,hungryClose,genWarnings,closeCash," +
            "drinksMaxQ,drinksMeanQ,drinksSales,drinksTake,drinks2MaxQ,drinks2MeanQ,drinks2Sales,drinks2Take," +
            "foodTrader,foodMaxQ,foodMeanQ,foodSold,foodProfit,food2Trader,food2MaxQ,food2MeanQ,food2Sold,food2Profit,soldOutTick,moodMusic,moodFood,moodQueues,moodThirst,moodHeat,moodLitter,moodOther");
        foreach (var row in rows) report.AppendLine(row);
        if (Environment.GetEnvironmentVariable("PROBE_OUT") is { Length: > 0 } path) File.WriteAllText(path, report.ToString());
        TestContext.WriteLine(report.ToString());
    }

    private static string Day(Layout layout, ulong seed)
    {
        var s = layout is not (Layout.Default or Layout.DefaultDoubleStock or Layout.CurryInsteadOfChips)
            ? MultiVendorTests.TierTwoWithFourStalls(seed, generator: layout != Layout.FourStallsNoGenerator, secondBar: layout != Layout.CurryVanOnly, curryVan: layout != Layout.SecondBarOnly, bins: layout == Layout.FourStallsWithBins)
            : NextFestivalTests.Ready(GameSession.CreateDevelopmentFestival(seed, 2));
        if (layout is Layout.DefaultDoubleStock or Layout.CurryInsteadOfChips) BuildSession.Accept(s, new SetPreparationStockCommand(80, 64));
        if (layout == Layout.CurryInsteadOfChips) BuildSession.Accept(s, new ChooseFoodTraderCommand(MultiVendorTests.Curry.Id));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        var ids = new[] { "drinks", "drinks.2", "food", "food.2" };
        var maxQ = ids.ToDictionary(id => id, _ => 0); var sumQ = ids.ToDictionary(id => id, _ => 0L);
        var samples = 0; var warnings = 0; var lastStage = EquipmentStage.Normal; long soldOut = -1;
        double hunger = 0, thirst = 0; var hungry = 0;
        var moods = new long[Enum.GetValues<MoodCause>().Length];
        var start = s.CurrentTick;
        while (s.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing && s.CurrentTick - start < 80_000)
        {
            s.AdvanceWithoutSnapshot(400);
            // The ledger keeps ten festival minutes (800 ticks): read it every other step.
            if ((s.CurrentTick - start) % 800 == 0) foreach (var change in s.RecentMoodChanges()) moods[(int)change.Cause] += change.Change;
            if (s.PreparedStatus == PreparationStatus.Running)
            {
                samples++;
                foreach (var vendor in s.CaptureVendors()) { maxQ[vendor.Id] = Math.Max(maxQ[vendor.Id], vendor.Queue.Length); sumQ[vendor.Id] += vendor.Queue.Length; }
                var guests = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed).Select(p => p.AgentId).ToHashSet();
                var immersion = s.CaptureImmersion()!;
                if (soldOut < 0 && immersion.StockPurchased && immersion.SoftStock == 0 && immersion.BeerStock == 0) soldOut = s.CurrentTick - start;
                // Needs as they stand at the last look before the counters close.
                hunger = immersion.People.Where(p => guests.Contains(p.AgentId)).Select(p => (double)p.Hunger).DefaultIfEmpty().Average();
                hungry = immersion.People.Count(p => guests.Contains(p.AgentId) && p.Hunger >= 6_000);
                thirst = s.CaptureMedical()!.Needs.Where(n => guests.Contains(n.AgentId)).Select(n => (double)n.Thirst).DefaultIfEmpty().Average();
            }
            if (s.CaptureEquipment() is { } e && e.Stage != lastStage) { if (e.Stage == EquipmentStage.Warning) warnings++; lastStage = e.Stage; }
        }
        var prep = s.CapturePreparation()!;
        var purchases = s.CaptureImmersion()!.Purchases;
        var admitted = prep.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted).ToArray();
        var mood = prep.Result?.SatisfactionPercent ?? (admitted.Length == 0 ? 0 : (decimal)admitted.Average(p => p.Satisfaction) / 100m);
        string Bar(string id)
        {
            var sold = purchases.Where(p => !p.Product.IsFood() && p.Product != ImmersionProduct.Water && Stalls.Of(p) == id).ToArray();
            return s.CaptureVendors().Any(v => v.Id == id)
                ? $"{maxQ[id]},{(samples == 0 ? 0 : sumQ[id] / (double)samples):0.0},{sold.Length},{sold.Sum(p => p.PricePennies)}" : "-,-,-,-";
        }
        string Van(string id) => s.TraderAccountAt(id) is { } account
            ? $"{s.TraderAt(id).Menu},{maxQ[id]},{sumQ[id] / (double)samples:0.0},{account.PortionsSold},{account.ProfitPennies}" : "-,-,-,-,-";
        return string.Join(",", layout, seed, prep.Status, s.CaptureLifecycleSnapshot()?.Casualties.LastOrDefault() is { } c ? $"{c.Role}:{c.Cause.Replace(',', ';')}" : "-", prep.GuestMedicalCollapses,
            prep.Result?.Stars?.ToString() ?? "-", $"{mood:0.0}", $"{hunger:0}", $"{thirst:0}", hungry, warnings,
            s.CaptureSnapshot().FestivalFinances.Single().CashPennies, Bar("drinks"), Bar("drinks.2"), Van("food"), Van("food.2"), soldOut,
            Mood(MoodCause.Music), Mood(MoodCause.FoodAndDrink), Mood(MoodCause.LongQueues), Mood(MoodCause.Thirst), Mood(MoodCause.Heat), Mood(MoodCause.LitterAndWasps),
            moods.Sum() - new[] { MoodCause.Music, MoodCause.FoodAndDrink, MoodCause.LongQueues, MoodCause.Thirst, MoodCause.Heat, MoodCause.LitterAndWasps }.Sum(c => moods[(int)c]));
        long Mood(MoodCause cause) => moods[(int)cause];
    }
}

using System.Reflection;
using System.Text;
using Festival.Simulation;

namespace Festival.Tests;

// Not a check: whole days at Tier 2 (with and without Robot Workers, one and two medics) and Tier 1, for drink collapses
// and deaths. Run on its own: --filter "TestCategory=Probe&FullyQualifiedName~DrinkProbe". Writes to PROBE_OUT if set, else
// the test output; PROBE_TRACE names a "tier:seed:perk:medics" day whose drink deaths and collapses are traced in full.
[TestClass]
public sealed class DrinkProbe
{
    public TestContext TestContext { get; set; } = null!;

    private sealed record Day(int Tier, ulong Seed, bool Robots, int Medics)
    {
        public override string ToString() => $"{Tier}:{Seed}:{(Robots ? "robots" : "quiet")}:{Medics}";
    }

    [TestCategory("Probe")]
    [TestCategory("Slow")]
    [TestMethod]
    public void ProbeDrinkCollapsesAtTierOneAndTwo()
    {
        var seeds = int.TryParse(Environment.GetEnvironmentVariable("PROBE_SEEDS"), out var count) ? count : 6;
        var days = (from seed in Enumerable.Range(0, seeds).Select(i => 20260922UL + (ulong)i)
                    from robots in new[] { false, true }
                    from medics in new[] { 1, 2 }
                    select new Day(2, seed, robots, medics))
            .Concat(Enumerable.Range(0, 3).Select(i => new Day(1, 20260922UL + (ulong)i, false, 1))).ToArray();
        if (Environment.GetEnvironmentVariable("PROBE_TRACE") is { Length: > 0 } only) days = days.Where(d => d.ToString() == only).ToArray();
        var rows = new string[days.Length];
        var traces = new string[days.Length];
        Parallel.For(0, days.Length, i => (rows[i], traces[i]) = Run(days[i], days.Length == 1));
        var report = new StringBuilder("day,status,death,collapses,drinkCollapses,drinkWarnings,beerSold,barIncome,mood%,peakIntox\n");
        foreach (var row in rows) report.AppendLine(row);
        foreach (var trace in traces.Where(t => t.Length > 0)) report.AppendLine(trace);
        if (Environment.GetEnvironmentVariable("PROBE_OUT") is { Length: > 0 } path) File.WriteAllText(path, report.ToString());
        TestContext.WriteLine(report.ToString());
    }

    // PROBE_BEER: the beer order, 32 (the default plan) unless set; a bigger order shows the bars' full exposure.
    private static readonly int Beer = int.TryParse(Environment.GetEnvironmentVariable("PROBE_BEER"), out var beer) ? beer : 32;

    private static GameSession Setup(Day day)
    {
        var s = GameSession.CreateDevelopmentFestival(day.Seed, day.Tier);
        var redraw = typeof(GameSession).GetMethod("OpenPerkDraft", BindingFlags.NonPublic | BindingFlags.Instance)!;
        string[] wanted = day.Robots ? [PerkCatalogue.RobotWorkers] : ["extra-pair-of-hands", "doctors-orders"];
        var perks = s.CapturePerks()!;
        for (var draws = 0; !perks.Hand.Any(wanted.Contains); draws++)
        {
            Assert.IsTrue(draws < 2_000, $"{day} never offered {wanted[0]}.");
            redraw.Invoke(s, []); perks = s.CapturePerks()!;
        }
        BuildSession.Accept(s, new ChoosePerkCommand(perks.DraftAttempt, perks.Cursor, perks.Hand.First(wanted.Contains)));
        BuildSession.Accept(s, new UseDefaultBuildLayoutCommand());
        if (day.Tier == 1)
        {
            BuildSession.Accept(s, new SetProgrammeCommand(BuildSession.Acts));
            BuildSession.Accept(s, new SetPreparationStockCommand(40, Beer));
            foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        }
        else
        {
            var acts = s.GetFestivalActs().Where(act => s.ActStandingOf(act) == ActStanding.Available)
                .OrderBy(act => s.ActFee(act)).ThenBy(act => act.Id, StringComparer.Ordinal).Select(act => act.Id).ToArray();
            BuildSession.Accept(s, new SetProgrammeCommand(acts[..3]));
            if (s.SecondStageOpen) BuildSession.Accept(s, new SetProgrammeCommand(acts[3..6]) { StageId = s.Stages[1].Id });
            BuildSession.Accept(s, new SetPreparationStockCommand(40, Beer));
            foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
            if (s.SecondStageOpen) BuildSession.Accept(s, new AcceptPreparationOfferCommand(BuildSession.ExtraId(s, StaffRole.Sound)));
            if (day.Medics == 2) BuildSession.Accept(s, new AcceptPreparationOfferCommand(BuildSession.ExtraId(s, StaffRole.Medic)));
        }
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        return s;
    }

    private static (string Row, string Trace) Run(Day day, bool traceAll)
    {
        var s = Setup(day);
        var start = s.CurrentTick;
        var history = new Dictionary<ulong, List<string>>();
        var evidence = new List<MedicalEvidence>(); var seen = new HashSet<(string, long, string)>();
        var peak = 0;
        while (s.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing && s.CurrentTick - start < 90_000)
        {
            s.AdvanceWithoutSnapshot(80);
            var immersion = s.CaptureImmersion()!;
            var medical = s.CaptureMedical()!;
            foreach (var e in medical.Evidence) if (seen.Add((e.Id, e.Tick, e.Description))) evidence.Add(e);
            var needs = medical.Needs.ToDictionary(n => n.AgentId);
            foreach (var p in immersion.People)
            {
                peak = Math.Max(peak, p.Intoxication);
                if (p.Intoxication < 4_000 && p.PendingDose == 0) continue;
                if (!history.TryGetValue(p.AgentId, out var list)) history[p.AgentId] = list = [];
                var need = needs.GetValueOrDefault(p.AgentId);
                list.Add($"  t{s.CurrentTick - start,6} intox {p.Intoxication,5} pending {p.PendingDose,4} held {p.Held?.Product.ToString() ?? "-",-9} " +
                    $"food {p.FoodProtectionTicks,4} warn {p.WarningTick,6} severe {p.SevereTicks,4} collapse {p.CollapseTick,6} care {p.CareTicks,4} " +
                    $"stage {need?.Stage} intent {need?.Intent} queue {p.VendorId ?? "-"}");
            }
        }
        var prep = s.CapturePreparation()!;
        var purchases = s.CaptureImmersion()!.Purchases;
        var beer = purchases.Where(p => p.Product == ImmersionProduct.Beer).ToArray();
        var bar = purchases.Where(p => !p.Product.IsFood() && p.Product != ImmersionProduct.Water).Sum(p => p.PricePennies);
        var admitted = prep.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted).ToArray();
        var mood = prep.Result?.SatisfactionPercent ?? (admitted.Length == 0 ? 0 : (decimal)admitted.Average(p => p.Satisfaction) / 100m);
        var casualty = s.CaptureLifecycleSnapshot()?.Casualties.LastOrDefault();
        var drinkCollapses = evidence.Where(e => e.Id == "intoxication:collapse").ToArray();
        var row = string.Join(",", day, prep.Status, casualty is { } c ? $"{c.PersonId}:{c.Cause.Replace(',', ';')}" : "-", prep.GuestMedicalCollapses,
            drinkCollapses.Length, evidence.Count(e => e.Id == "intoxication:warning"), beer.Length, bar, $"{mood:0.0}", peak);
        // The traced people: whoever collapsed from drink (and, when tracing one day, the heaviest drinkers too).
        static ulong? PersonOf(string text) => text.StartsWith("Person ", StringComparison.Ordinal) && ulong.TryParse(text[7..text.IndexOf(':')], out var id) ? id : null;
        var traced = drinkCollapses.Select(e => PersonOf(e.Description)).OfType<ulong>().ToHashSet();
        if (traceAll) foreach (var id in history.Keys) traced.Add(id);
        var trace = new StringBuilder();
        foreach (var id in traced.Order())
        {
            var person = prep.People.Single(p => p.AgentId == id);
            trace.AppendLine($"== {day} person {id} {person.Name} ({person.Role}); beers bought at " +
                string.Join(" ", purchases.Where(p => p.AgentId == id && !p.Product.IsFood()).Select(p => $"{p.Product}@t{p.Tick - start}/{p.VendorId ?? "drinks"}")));
            foreach (var e in evidence.Where(e => e.Description.Contains($"{id}", StringComparison.Ordinal) && (e.Id.StartsWith("intoxication", StringComparison.Ordinal) || e.Id.StartsWith("medical", StringComparison.Ordinal))))
                trace.AppendLine($"  [{e.Id} t{e.Tick - start}] {e.Description}");
            foreach (var line in history.GetValueOrDefault(id) ?? []) trace.AppendLine(line);
        }
        return (row, trace.ToString());
    }
}

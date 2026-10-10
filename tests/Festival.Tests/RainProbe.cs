using System.Text;
using Festival.Simulation;

namespace Festival.Tests;

// Not a check: whole rainy Tier 2 days on the default layout, with nothing for the rain, with a marquee, and with the
// marquee, straw on the busy routes and a stock of ponchos. Run on its own:
// --filter "TestCategory=Probe&FullyQualifiedName~RainProbe". Writes to PROBE_OUT if set, else the test output.
[TestClass]
public sealed class RainProbe
{
    public TestContext TestContext { get; set; } = null!;

    internal enum Counters { None, Marquee, MarqueeStrawPonchos }

    [TestCategory("Probe")]
    [TestCategory("Slow")]
    [TestMethod]
    public void ProbeRainyTierTwoDays()
    {
        var report = new StringBuilder();
        report.AppendLine("counters,seed,downpour,status,death,stars,mood%,rainMood/guest,mudMood/guest,musicMood/guest,peakMud,closeMud,ponchos,sheltered,shelterSecs,peakMarquee," +
            "crowdDry,crowdHeavy,crowdDownpour,marqueeHeavy,marqueeDownpour,fans%sheltering,fans%listening,lingering,openCash,closeCash");
        // The first seed after the usual three whose day has a downpour.
        var day = GameSession.DayTicksFor(FestivalStages.For(true));
        var downpourSeed = Enumerable.Range(0, 200).Select(i => 20260925UL + (ulong)i)
            .First(seed => GameSession.RainForecast(seed, 2, day).Any(spell => spell.Intensity == RainIntensity.Downpour));
        foreach (var counters in Enum.GetValues<Counters>())
        foreach (var seed in new ulong[] { 20260922, 20260923, 20260924, downpourSeed })
            report.AppendLine(Day(seed, counters));
        if (Environment.GetEnvironmentVariable("PROBE_OUT") is { Length: > 0 } path) File.WriteAllText(path, report.ToString());
        TestContext.WriteLine(report.ToString());
    }

    /// <summary>A marquee spot that fits Tier 2's default layout: south-west of the main crowd.</summary>
    internal static readonly GridCell MarqueeSpot = MarqueeTests.ByTheCrowd;

    /// <summary>
    /// Straw down the busiest ground on Tier 2's default layout: across the main stage's crowd and the Pond Stage's, and
    /// the walk between them past the bars.
    /// </summary>
    internal static readonly (GridCell From, GridCell To)[] BusyRoutes =
    [
        .. Enumerable.Range(0, 7).Select(i => (new GridCell(104 + i * 3, 135), new GridCell(104 + i * 3, 165))),
        .. Enumerable.Range(0, 5).Select(i => (new GridCell(156, 142 + i * 3), new GridCell(180, 142 + i * 3))),
        (new(124, 154), new(150, 154)),
    ];

    internal static GameSession Prepared(ulong seed, Counters counters)
    {
        var s = NextFestivalTests.Ready(GameSession.CreateDevelopmentFestival(seed, 2));
        BuildSession.Accept(s, new AcceptPreparationOfferCommand(BuildSession.ExtraId(s, StaffRole.Medic)));
        if (counters != Counters.None) BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Marquee, MarqueeSpot));
        if (counters == Counters.MarqueeStrawPonchos)
        {
            foreach (var (from, to) in BusyRoutes) BuildSession.Send(s, new GroundCoverCommand(GroundCoverAction.Straw, from, to));
            BuildSession.Accept(s, new SetPreparationStockCommand(40, 32) { Ponchos = 30, StrawBales = 4 });
        }
        return s;
    }

    private static string Day(ulong seed, Counters counters)
    {
        var s = Prepared(seed, counters);
        var opening = s.CapturePreparation()!.OpeningCashPennies;
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        var guests = s.CapturePreparation()!.People.Count(person => person.Role == ProtectedPersonRole.Guest);
        var peakMud = 0;
        var sheltered = new HashSet<ulong>();
        var shelterSeconds = 0; var peakMarquee = 0;
        var start = s.CurrentTick;
        var spread = false;
        // Set crowds (listeners at their places on live sets) and the marquee, by the weather; fans among shelterers and listeners.
        var crowd = new Dictionary<RainIntensity, (long Sum, int Samples)>();
        var tent = new Dictionary<RainIntensity, (long Sum, int Samples)>();
        long fanShelter = 0, shelterCount = 0, fanListen = 0, listenCount = 0;
        var lingering = 0;
        while (s.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing && s.CurrentTick - start < 80_000)
        {
            s.AdvanceWithoutSnapshot(400);
            peakMud = Math.Max(peakMud, MudCells(s));
            var shelterers = s.CaptureObservation().NavigationAgents.Where(agent => agent.IntentId == ShelterRules.Intent).ToArray();
            foreach (var agent in shelterers) sheltered.Add(agent.Id.Value);
            shelterSeconds += shelterers.Length * 5;
            var inTent = s.CaptureShelterSpots().Select(spot => spot.CoverId).Where(id => id.StartsWith("marquee", StringComparison.Ordinal)).Distinct().Sum(s.ShelteringAt);
            peakMarquee = Math.Max(peakMarquee, inTent);
            if (s.PreparedStatus == PreparationStatus.Running)
            {
                var rain = s.RainNow;
                var dayTick = s.CurrentTick - s.CapturePreparation()!.StartedTick;
                var live = s.Stages.Select(stage => (Live: s.CaptureLivePerformance(stage.Id), Programme: s.CaptureProgramme(stage.Id)))
                    .Where(item => item.Live?.Stage == LiveSetStage.Live && item.Programme is { CurrentSlot: >= 0 }).ToArray();
                var acts = live.Select(item => ActCatalogue.Find(item.Programme!.ActIds[item.Programme.CurrentSlot])!).ToArray();
                bool Fan(ulong id) => acts.Any(act => s.FestivalAffinity(id, act) >= 80);
                if (live.Length > 0)
                {
                    var listening = live.SelectMany(item => item.Live!.Listeners.Where(l => l.AtPlace).Select(l => l.AgentId)).ToArray();
                    var (sum, samples) = crowd.GetValueOrDefault(rain); crowd[rain] = (sum + listening.Length, samples + 1);
                    if (rain != RainIntensity.Dry)
                    {
                        fanListen += listening.Count(Fan); listenCount += listening.Length;
                        fanShelter += shelterers.Count(agent => Fan(agent.Id.Value)); shelterCount += shelterers.Length;
                    }
                }
                var (tentSum, tentSamples) = tent.GetValueOrDefault(rain); tent[rain] = (tentSum + inTent, tentSamples + 1);
                // Still sheltering half a minute after a shower has passed: they should be back at the stage.
                if (rain == RainIntensity.Dry && s.CaptureWeather()!.Spells.Any(spell => dayTick - spell.EndTick is >= 2_400 and < 2_800))
                    lingering = Math.Max(lingering, shelterers.Length);
            }
            // An attentive player sends a steward out with a bale to the worst of the mud, once it's bad.
            if (counters == Counters.MarqueeStrawPonchos && s.PreparedStatus == PreparationStatus.Running && s.CaptureWeather()!.StrawBales > 0 && s.CaptureWeather()!.StrawJob is null)
            {
                var worst = WorstMud(s);
                if (worst is { } spot)
                    foreach (var steward in s.GetStewardResponses().Select(job => job.WorkerId))
                        if (BuildSession.Send(s, new SpreadStrawCommand(steward, spot)).IsAccepted) { spread = true; break; }
            }
        }
        var p = s.CapturePreparation()!;
        var death = s.CaptureLifecycleSnapshot()?.Casualties.LastOrDefault() is { } c ? $"{c.Role}:{c.Cause.Replace(',', ';')[..Math.Min(50, c.Cause.Length)]}" : "-";
        var admitted = p.People.Where(person => person.Role == ProtectedPersonRole.Guest && person.Admitted).ToArray();
        var mood = p.Result?.SatisfactionPercent ?? (admitted.Length == 0 ? 0 : (decimal)admitted.Average(person => person.Satisfaction) / 100m);
        var ponchos = s.CaptureImmersion()!.Purchases.Count(purchase => purchase.Product == ImmersionProduct.Poncho);
        var downpour = s.CaptureWeather()!.Spells.Any(spell => spell.Intensity == RainIntensity.Downpour);
        return string.Join(",", counters + (spread ? "+spread" : ""), seed, downpour ? "C" : "B", p.Status, death, p.Result?.Stars?.ToString() ?? "-", $"{mood:0.0}",
            s.MoodTotal(MoodCause.Rain) / guests, s.MoodTotal(MoodCause.MudAndPuddles) / guests, s.MoodTotal(MoodCause.Music) / guests, peakMud, MudCells(s), ponchos,
            sheltered.Count, shelterSeconds, peakMarquee,
            Average(crowd, RainIntensity.Dry), Average(crowd, RainIntensity.Heavy), Average(crowd, RainIntensity.Downpour),
            Average(tent, RainIntensity.Heavy), Average(tent, RainIntensity.Downpour),
            shelterCount == 0 ? "-" : $"{100 * fanShelter / shelterCount}", listenCount == 0 ? "-" : $"{100 * fanListen / listenCount}", lingering,
            opening, s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
    }

    /// <summary>A map of the field at closing, two cells a character: '#' mud, '~' puddle, '+' trampled, '=' straw, ' ' grass.</summary>
    [TestCategory("Probe")]
    [TestCategory("Slow")]
    [TestMethod]
    public void MapTheGroundAtClosing()
    {
        var s = Prepared(20260922, Enum.TryParse<Counters>(Environment.GetEnvironmentVariable("PROBE_COUNTERS"), out var c) ? c : Counters.None);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        var start = s.CurrentTick;
        while (s.PreparedStatus == PreparationStatus.Running && s.CurrentTick - start < 80_000) s.AdvanceWithoutSnapshot(400);
        var map = new StringBuilder();
        for (var z = 100; z <= 191; z += 2)
        {
            map.Append($"{z,3} ");
            for (var x = 64; x <= 191; x += 2)
            {
                var cell = new GridCell(x, z);
                map.Append(s.GroundTrampleAt(cell) >= 0 ? '=' : s.GroundStateAt(cell) switch
                {
                    GroundState.Mud or GroundState.Swamp => '#', GroundState.Puddle => '~',
                    _ => s.GroundWearAt(cell) >= WeatherRules.TrampledWear ? '+' : s.GroundWearAt(cell) > 10 ? '.' : ' '
                });
            }
            map.AppendLine();
        }
        map.AppendLine($"    x from 64 by 2; mud cells {MudCells(s)}");
        if (Environment.GetEnvironmentVariable("PROBE_OUT") is { Length: > 0 } path) File.WriteAllText(path, map.ToString());
        TestContext.WriteLine(map.ToString());
    }

    private static string Average(Dictionary<RainIntensity, (long Sum, int Samples)> values, RainIntensity rain) =>
        values.TryGetValue(rain, out var value) && value.Samples > 0 ? $"{(double)value.Sum / value.Samples:0.0}" : "-";

    internal static int MudCells(GameSession s)
    {
        var count = 0;
        for (var z = GroundRules.FieldFirstCell; z <= GroundRules.FieldLastCell; z++)
        for (var x = GroundRules.FieldFirstCell; x <= GroundRules.FieldLastCell; x++)
            if (s.GroundStateAt(new(x, z)) is GroundState.Mud or GroundState.Swamp) count++;
        return count;
    }

    /// <summary>The centre of the muddiest 7 × 7 patch not already under straw, on a 4-cell lattice; null when there's little mud.</summary>
    internal static GridCell? WorstMud(GameSession s)
    {
        GridCell? best = null; var bestScore = 40;
        for (var z = GroundRules.FieldFirstCell + 3; z <= GroundRules.FieldLastCell - 3; z += 4)
        for (var x = GroundRules.FieldFirstCell + 3; x <= GroundRules.FieldLastCell - 3; x += 4)
        {
            var score = 0;
            for (var dz = -3; dz <= 3; dz++) for (var dx = -3; dx <= 3; dx++)
                if (s.GroundStateAt(new(x + dx, z + dz)) is GroundState.Mud or GroundState.Swamp) score++;
            if (score > bestScore) { bestScore = score; best = new(x, z); }
        }
        return best;
    }
}

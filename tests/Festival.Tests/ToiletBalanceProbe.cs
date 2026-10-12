using System.Text;
using Festival.Simulation;

namespace Festival.Tests;

// Not a check: Tier 1 days on the default layout (two loos side by side), for how evenly the two toilets' queues share
// the crowd. Run on its own: --filter "TestCategory=Probe&FullyQualifiedName~ToiletBalanceProbe". Writes to PROBE_OUT if
// set, else the test output.
[TestClass]
public sealed class ToiletBalanceProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestCategory("Probe")]
    [TestCategory("Slow")]
    [TestMethod]
    public void ProbeToiletQueueBalanceAtTierOne()
    {
        var seeds = new ulong[] { 20260922, 20260923, 20260924, 20260926, 20260930, 20261003 };
        var rows = new string[seeds.Length];
        Parallel.For(0, seeds.Length, i => rows[i] = Run(seeds[i]));
        var report = new StringBuilder();
        foreach (var row in rows) report.AppendLine(row);
        if (Environment.GetEnvironmentVariable("PROBE_OUT") is { Length: > 0 } path) File.WriteAllText(path, report.ToString());
        TestContext.WriteLine(report.ToString());
    }

    private sealed class Tally
    {
        public long Samples, QueueSum, ApproachSum, BusySamples, IdleWhileOtherQueued, Switches;
        public int MaxQueue, MaxApproach;
        public readonly List<long> Waits = [];
    }

    internal static string Run(ulong seed)
    {
        var s = BuildSession.Started(seed);
        var end = s.CapturePreparation()!.StartedTick + GameSession.PreparedDayTicks;
        var ids = s.CaptureToilets().Select(t => t.Id).ToArray();
        var tally = ids.ToDictionary(id => id, _ => new Tally());
        var joined = new Dictionary<ulong, (string Toilet, long Tick)>();
        var lastToilet = new Dictionary<ulong, string>();
        var timeline = new StringBuilder();
        var imbalanced = 0L; var samples = 0L;
        while (s.CurrentTick < end)
        {
            s.AdvanceWithoutSnapshot(16);
            var toilets = s.CaptureToilets();
            var people = s.CaptureImmersion()!.People;
            var approaching = ids.ToDictionary(id => id, id => people.Count(p => p.ToiletId == id && p.ToiletStage == ToiletVisitStage.Approaching));
            foreach (var p in people)
            {
                if (p.ToiletStage is ToiletVisitStage.Approaching or ToiletVisitStage.Queued && p.ToiletId is { } at)
                {
                    if (lastToilet.TryGetValue(p.AgentId, out var was) && was != at) tally[at].Switches++;
                    lastToilet[p.AgentId] = at;
                    if (!joined.ContainsKey(p.AgentId)) joined[p.AgentId] = (at, s.CurrentTick);
                }
                else if (p.ToiletStage is ToiletVisitStage.Entering or ToiletVisitStage.Using && joined.Remove(p.AgentId, out var start))
                    tally[p.ToiletId!].Waits.Add(s.CurrentTick - start.Tick);
                else if (p.ToiletStage == ToiletVisitStage.None) { joined.Remove(p.AgentId); lastToilet.Remove(p.AgentId); }
            }
            foreach (var t in toilets)
            {
                var row = tally[t.Id];
                var waiting = t.Queue.Count(id => id != t.OwnerId);
                row.Samples++; row.QueueSum += waiting; row.ApproachSum += approaching[t.Id];
                row.MaxQueue = Math.Max(row.MaxQueue, waiting); row.MaxApproach = Math.Max(row.MaxApproach, approaching[t.Id]);
                if (t.OwnerId is not null) row.BusySamples++;
                var other = toilets.Where(o => o.Id != t.Id).Sum(o => o.Queue.Count(id => id != o.OwnerId));
                if (t.OwnerId is null && t.Queue.Length == 0 && other >= 2 && t.CanAccept(ToiletVisitKind.Poo) && t.InterruptedOccupantId is null) row.IdleWhileOtherQueued++;
            }
            samples++;
            var lengths = toilets.Select(t => t.Queue.Count(id => id != t.OwnerId) + approaching[t.Id]).ToArray();
            if (lengths.Max() - lengths.Min() >= 3 && toilets.All(t => t.CanAccept(ToiletVisitKind.Poo) && t.InterruptedOccupantId is null && s.ActiveFault(t.Id) is null)) imbalanced++;
            if (s.CurrentTick % 4_800 == 0)
                timeline.Append($"  t={s.CurrentTick / 80}s " + string.Join(" ", toilets.Select(t =>
                    $"{t.Id}:q{t.Queue.Count(id => id != t.OwnerId)}+a{approaching[t.Id]}{(t.OwnerId is null ? "" : "*")}")) + "\n");
        }
        var text = new StringBuilder($"seed {seed}: samples(16 ticks)={samples} imbalancedWhileBothUsable(|diff|>=3)={imbalanced} ({imbalanced * 100 / Math.Max(1, samples)}%)\n");
        foreach (var t in s.CaptureToilets())
        {
            var row = tally[t.Id];
            var waits = row.Waits.Order().ToArray();
            long Pct(int p) => waits.Length == 0 ? 0 : waits[Math.Min(waits.Length - 1, waits.Length * p / 100)] / 80;
            text.Append($"  {t.Id}: uses={t.WeeCount + t.PooCount} full={t.FullPercent}% busy={row.BusySamples * 100 / row.Samples}% meanQueue={row.QueueSum / (double)row.Samples:F2} " +
                $"maxQueue={row.MaxQueue} meanApproach={row.ApproachSum / (double)row.Samples:F2} maxApproach={row.MaxApproach} " +
                $"idleUsableWhileOther>=2={row.IdleWhileOtherQueued * 16 / 80}s switchesIn={row.Switches} " +
                $"wait s mean={(waits.Length == 0 ? 0 : waits.Average() / 80):F1} p50={Pct(50)} p90={Pct(90)} max={(waits.Length == 0 ? 0 : waits[^1] / 80)}\n");
        }
        text.Append(timeline);
        return text.ToString();
    }
}

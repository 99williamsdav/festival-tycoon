using System.Text;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>
/// Writes real days' set records to a folder, to see how the gig score reads in play: a Tier 1 day on a few seeds,
/// then the Tier 2 day that follows one. Set FESTIVAL_GIG_PROBE_DIR to run it.
/// </summary>
[TestClass]
public sealed class BandRelationshipProbe
{
    [TestMethod, TestCategory("Slow")]
    public void WritesGigRecords()
    {
        var folder = Environment.GetEnvironmentVariable("FESTIVAL_GIG_PROBE_DIR");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        var text = new StringBuilder();
        foreach (var seed in new ulong[] { 20260922, 20260923, 20260924, 20260925, 20260926, 20261003 })
        {
            var s = BuildSession.Started(seed);
            RunToEnd(s);
            text.Append($"== Tier 1 seed {seed} status {s.PreparedStatus}\n");
            Write(text, s);
            if (seed is 20260922 or 20260924 or 20260926 && s.CanStartNextFestival)
            {
                var next = NextFestivalTests.Ready(s.CreateNextFestival());
                BuildSession.Accept(next, new AcceptPreparationOfferCommand(BuildSession.ExtraId(next, StaffRole.Medic)));
                BuildSession.Accept(next, new StartPreparedEditionCommand());
                RunToEnd(next);
                text.Append($"== Tier 2 after seed {seed} status {next.PreparedStatus}\n");
                Write(text, next);
            }
        }
        File.WriteAllText(Path.Combine(folder, "gig-records.txt"), text.ToString());
    }

    /// <summary>
    /// Tier 2's crowds against the typical shares measured at Tier 1 (14 Oct): six default-layout Tier 2 days, both stages
    /// programmed, each played set's expected, average, end and peak crowd, each as a share of expected, and its crowd score.
    /// Set FESTIVAL_TIER2_CROWD_PROBE_DIR to run it.
    /// </summary>
    [TestMethod, TestCategory("Slow")]
    public void WritesTierTwoCrowds()
    {
        var folder = Environment.GetEnvironmentVariable("FESTIVAL_TIER2_CROWD_PROBE_DIR");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        var text = new StringBuilder();
        text.AppendLine("seed,stage,set,act,pop,expected,average,end,peak,avg%,end%,peak%,crowd,reaction,delta");
        foreach (var seed in new ulong[] { 20260922, 20260923, 20260924, 20260925, 20260926, 20261003 })
        {
            var s = NextFestivalTests.Ready(GameSession.CreateDevelopmentFestival(seed, 2));
            BuildSession.Accept(s, new AcceptPreparationOfferCommand(BuildSession.ExtraId(s, StaffRole.Medic)));
            BuildSession.Accept(s, new StartPreparedEditionCommand());
            RunToEnd(s);
            foreach (var r in s.PerformanceRecords)
            {
                var act = ActCatalogue.Find(r.ActId)!;
                var e = Math.Max(1, r.ExpectedCrowd);
                text.AppendLine(string.Join(",", seed, r.StageId, r.Slot + 1, act.Name.Replace(",", ""), act.Popularity, r.ExpectedCrowd,
                    $"{r.AverageCrowdTenths / 10.0:0.0}", r.SetEndCrowd, r.PeakCrowd, r.AverageCrowdTenths * 10 / e, r.SetEndCrowd * 100 / e,
                    r.PeakCrowd * 100 / e, GigRules.CrowdScore(r.PeakCrowd, r.AverageCrowdTenths, r.SetEndCrowd, r.ExpectedCrowd), r.Reaction, r.Delta));
            }
            text.AppendLine($"# seed {seed} status {s.PreparedStatus}");
        }
        File.WriteAllText(Path.Combine(folder, "tier2-crowds.csv"), text.ToString());
    }

    internal static void RunToEnd(GameSession s)
    {
        for (var i = 0; i < 80 && s.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing; i++) s.AdvanceWithoutSnapshot(1_000);
    }

    private static void Write(StringBuilder text, GameSession s)
    {
        var names = s.CapturePreparation()!.People.ToDictionary(person => person.AgentId, person => person.Name);
        foreach (var r in s.PerformanceRecords)
        {
            var act = ActCatalogue.Find(r.ActId)!;
            text.Append($"{r.StageId} set {r.Slot + 1} {act.Name} (pop {act.Popularity}, talent {PerformanceRules.Talent(act)}): due {r.ScheduledStartTick}-{r.ScheduledEndTick} " +
                $"ran {r.StartedTick}-{r.EndedTick} avg {r.AverageCrowdTenths / 10.0:0.0} peak {r.PeakCrowd} end {r.SetEndCrowd} expected {r.ExpectedCrowd} won {r.WonOverIds?.Length ?? 0} enjoy {r.AverageEnjoyment} " +
                $"{r.Reaction} [{string.Join(",", r.Hiccups)}] rel {r.RelationshipBefore}{r.Delta:+0;-0;+0}={r.RelationshipAfter} ({string.Join("; ", r.Reasons)}) " +
                $"crowd {r.CrowdIds.Length}\n");
            foreach (var quote in r.Quotes)
                text.Append($"    {names[quote.GuestId]}{(quote.Fan ? " (fan)" : "")}: {GigQuotes.Line(r.Reaction, quote)}\n");
        }
    }
}

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
        foreach (var seed in new ulong[] { 20260922, 20260926, 20261003 })
        {
            var s = BuildSession.Started(seed);
            RunToEnd(s);
            text.Append($"== Tier 1 seed {seed} status {s.PreparedStatus}\n");
            Write(text, s);
            if (seed == 20260922 && s.CanStartNextFestival)
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
                $"ran {r.StartedTick}-{r.EndedTick} peak {r.PeakCrowd} end {r.SetEndCrowd} expected {r.ExpectedCrowd} enjoy {r.AverageEnjoyment} " +
                $"{r.Reaction} [{string.Join(",", r.Hiccups)}] rel {r.RelationshipBefore}{r.Delta:+0;-0;+0}={r.RelationshipAfter} ({string.Join("; ", r.Reasons)}) " +
                $"crowd {r.CrowdIds.Length}\n");
            foreach (var quote in r.Quotes)
                text.Append($"    {names[quote.GuestId]}{(quote.Fan ? " (fan)" : "")}: {GigQuotes.Line(r.Reaction, quote)}\n");
        }
    }
}

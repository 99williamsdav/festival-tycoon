using System.Text;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>
/// A full Pond Stage trial day for a few seeds, written down for reading: how the crowd splits between the stages over
/// the day, each set's end audience, the final mood and cash, and any walkers stuck in a knot. Slow; it writes its
/// report to FESTIVAL_POND_PROBE_DIR when that is set.
/// </summary>
[TestClass]
public sealed class PondStageProbeTests
{
    [TestMethod, TestCategory("Slow")]
    public void ProbeThreeTrialDays()
    {
        var report = new StringBuilder();
        foreach (var seed in new ulong[] { 20260922, 20260926, 20261003 })
            report.Append(Probe(BuildSession.PondStarted(seed), $"seed {seed}, Pond Stage trial"))
                .Append(Probe(BuildSession.Started(seed), $"seed {seed}, trailer stage only"));
        Console.WriteLine(report);
        var folder = Environment.GetEnvironmentVariable("FESTIVAL_POND_PROBE_DIR");
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "pond-probe.txt"), report.ToString());
        }
    }

    internal static string Probe(GameSession s, string title)
    {
        var text = new StringBuilder($"== {title}\n");
        var start = s.CapturePreparation()!.StartedTick;
        var end = start + s.PreparedEditionDurationTicks;
        var stages = s.Stages;
        var stageOf = stages.Select(stage => (LiveSetStage?)null).ToArray();
        var history = new Dictionary<ulong, (int X, int Z)>[3];
        for (var i = 0; i < history.Length; i++) history[i] = [];
        var knotSamples = 0; var knotPeople = new HashSet<ulong>(); var knotPeak = 0; var bandKnots = 0;
        var pondBand = s.CaptureProgramme(FestivalStages.PondId)?.Performers.Select(p => p.AgentId).ToHashSet() ?? [];
        var bandArrived = new Dictionary<ulong, long>();
        var split = new StringBuilder();
        while (s.CurrentTick < end)
        {
            s.AdvanceWithoutSnapshot(1);
            var tick = s.CurrentTick - start;
            for (var index = 0; index < stages.Count; index++)
            {
                if (s.CaptureLivePerformance(stages[index].Id) is not { } live || live.Stage == stageOf[index]) continue;
                stageOf[index] = live.Stage;
                var slot = s.CaptureProgramme(stages[index].Id)!.CurrentSlot;
                if (live.Stage == LiveSetStage.Live) text.Append($"{tick,6} {stages[index].Id} set {slot + 1} live (planned {live.PlannedTick - start})\n");
                if (live.Stage == LiveSetStage.Finished)
                    text.Append($"{tick,6} {stages[index].Id} set {slot + 1} ended {live.LastReaction}: set-end audience {live.SetEndAudienceCount}, enjoyment {live.SetEndEnjoymentTotal}\n");
            }
            if (tick % 2_400 == 0)
            {
                split.Append($"{tick,6}");
                foreach (var stage in stages)
                {
                    var live = s.CaptureLivePerformance(stage.Id);
                    split.Append($"  {stage.Id}: crowd {live?.Listeners.Length ?? 0} at places {live?.Listeners.Count(l => l.AtPlace) ?? 0} ({live?.Stage})");
                }
                split.Append('\n');
            }
            if (tick % 80 == 0)
            {
                var agents = s.CaptureObservation().NavigationAgents;
                foreach (var agent in agents.Where(a => pondBand.Contains(a.Id.Value) && a.Action == AgentNavigationAction.Arrived && !bandArrived.ContainsKey(a.Id.Value) &&
                             s.CapturePerson(a.Id.Value) is { Admitted: true }))
                    bandArrived[agent.Id.Value] = tick;
                var now = agents.ToDictionary(a => a.Id.Value, a => (a.XMillimetres, a.ZMillimetres));
                var earlier = history[(int)(tick / 80 % 3)];
                var knotted = 0;
                if (tick >= 240)
                    foreach (var agent in agents.Where(a => a.Action == AgentNavigationAction.Travelling))
                    {
                        if (!earlier.TryGetValue(agent.Id.Value, out var before)) continue;
                        long dx = agent.XMillimetres - before.X, dz = agent.ZMillimetres - before.Z;
                        if (dx * dx + dz * dz > 600L * 600) continue;
                        if (!agents.Any(other => other.Id != agent.Id && Square(other.XMillimetres - agent.XMillimetres) + Square(other.ZMillimetres - agent.ZMillimetres) <= 1_000L * 1_000)) continue;
                        knotted++; knotPeople.Add(agent.Id.Value);
                        if (pondBand.Contains(agent.Id.Value)) bandKnots++;
                    }
                knotSamples += knotted; knotPeak = Math.Max(knotPeak, knotted);
                history[(int)(tick / 80 % 3)] = now;
            }
        }
        var people = s.CapturePreparation()!.People;
        var guests = people.Where(p => p.Role == ProtectedPersonRole.Guest).Select(p => s.CapturePerson(p.AgentId)!.Satisfaction).ToArray();
        text.Append("crowd split every 30 s (stage: crowd size, standing at their places):\n").Append(split);
        text.Append($"final mood: guests average {guests.Average():F0} (min {guests.Min()}, max {guests.Max()})\n");
        text.Append($"cash: {s.CaptureSnapshot().FestivalFinances.Single().CashPennies}\n");
        text.Append($"main generator {s.CaptureEquipment()?.Stage}, stage powered {s.StagePowered}; pond generator {s.CaptureStageGenerator(FestivalStages.PondId)?.Stage}; " +
            $"sound main {s.SoundScoreAt(FestivalStages.MainId)} pond {s.SoundScoreAt(FestivalStages.PondId)}\n");
        if (pondBand.Count > 0)
            text.Append($"pond band off the lane (release ticks): {string.Join(", ", pondBand.Select(s.BandReleaseTicks))}; first arrivals: {string.Join(", ", pondBand.Select(id => bandArrived.GetValueOrDefault(id, -1)))}; pond band knot samples {bandKnots}\n");
        text.Append($"stuck/knotted walkers (travelling, moved <=0.6 m in 3 s, someone within 1 m), sampled each second: {knotSamples} samples, {knotPeople.Count} people, peak {knotPeak} at once\n");
        return text.ToString();
    }

    private static long Square(long value) => value * value;
}

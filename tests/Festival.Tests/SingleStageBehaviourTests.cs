using System.Security.Cryptography;
using System.Text;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>
/// A full festival day on the one trailer stage, written down as plain facts: where everyone stands every 400 ticks,
/// when the set moves between stages of play, each crowd reaction, how many clapped at the end, and how the day
/// finished. The digest pins single-stage play, so work towards more stages can show it changed nothing here.
/// None of it reads the save shape or the state hash.
/// </summary>
[TestClass]
public sealed class SingleStageBehaviourTests
{
    private const ulong PinnedSeed = 20260922;
    private const string PinnedDigest = "b2a62e6027180caaea4dd8264062deacdd71ba7f3e6d374419009a806d2aec0d";

    [TestMethod]
    public void OneStageDayPlaysAsPinned()
    {
        var log = DayLog(PinnedSeed);
        Assert.AreEqual(PinnedDigest, Digest(log), "Single-stage play changed. If that's meant, re-pin after checking the day still reads right.");
    }

    /// <summary>Writes the day's facts for a few seeds to a folder, to compare by eye or with a diff tool.</summary>
    [TestMethod, TestCategory("Slow")]
    public void WritesDayLogsForComparison()
    {
        var folder = Environment.GetEnvironmentVariable("FESTIVAL_BEHAVIOUR_LOG_DIR");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        foreach (var seed in new ulong[] { 20260922, 20260926, 20261003 })
        {
            var log = DayLog(seed);
            File.WriteAllText(Path.Combine(folder, $"day-{seed}.log"), log);
            Console.WriteLine($"{seed} {Digest(log)}");
        }
    }

    private static string Digest(string log) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(log))).ToLowerInvariant();

    internal static string DayLog(ulong seed)
    {
        var s = BuildSession.Started(seed);
        var text = new StringBuilder();
        var end = s.CapturePreparation()!.StartedTick + GameSession.PreparedDayTicks + 1_600;
        LiveSetStage? stage = null;
        var sequence = -1;
        var slot = -2;
        string? status = null;
        while (s.CurrentTick < end)
        {
            s.AdvanceWithoutSnapshot(1);
            var tick = s.CurrentTick;
            if (s.CaptureProgramme() is { } programme && (programme.CurrentSlot != slot || programme.Status != status))
            {
                slot = programme.CurrentSlot; status = programme.Status;
                text.Append($"{tick} programme slot={slot} status={status}\n");
            }
            if (s.CaptureLivePerformance() is { } live)
            {
                if (live.Stage != stage)
                {
                    stage = live.Stage;
                    text.Append($"{tick} stage {stage} planned={live.PlannedTick} started={live.StartedTick} ended={live.EndedTick} audience={live.SetEndAudienceCount} enjoyment={live.SetEndEnjoymentTotal}\n");
                }
                if (live.ReactionSequence != sequence)
                {
                    sequence = live.ReactionSequence;
                    text.Append($"{tick} reaction {sequence} {live.LastReaction}\n");
                }
                if (tick % 400 == 0)
                    text.Append($"{tick} listeners placed={live.Listeners.Count(l => l.Place is not null)} at={live.Listeners.Count(l => l.AtPlace)} " +
                        $"ticks={live.Listeners.Sum(l => (long)l.ListenedTicks)} joy={live.Listeners.Sum(l => (long)l.EnjoymentEarned)} " +
                        $"onstage={live.Performers.Count(p => p.OnStage)} late={s.FestivalBandLate} silent={s.ScheduledSilence}\n");
            }
            if (tick % 400 == 0)
                foreach (var agent in s.CaptureObservation().NavigationAgents.OrderBy(a => a.Id.Value))
                    text.Append($"{tick} at {agent.Id.Value} {agent.XMillimetres},{agent.ZMillimetres} {agent.Action} {agent.IntentId}\n");
        }
        var preparation = s.CapturePreparation()!;
        text.Append($"end status={preparation.Status} seen={string.Join(',', preparation.SeenActs)}\n");
        foreach (var person in preparation.People.OrderBy(p => p.AgentId))
            text.Append($"end person {person.AgentId} satisfaction={s.CapturePerson(person.AgentId)?.Satisfaction}\n");
        var snapshot = s.CaptureSnapshot();
        foreach (var finance in snapshot.FestivalFinances.OrderBy(f => f.OwnerId.Value))
            text.Append($"end cash {finance.OwnerId.Value} {finance.CashPennies}\n");
        foreach (var wallet in snapshot.Wallets.OrderBy(w => w.OwnerId.Value))
            text.Append($"end wallet {wallet.OwnerId.Value} {wallet.CashPennies}\n");
        return text.ToString();
    }
}

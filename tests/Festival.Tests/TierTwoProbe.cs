using System.Diagnostics;
using System.Text;
using Festival.Simulation;

namespace Festival.Tests;

// Not a check: a headless look at whole Tier 2 days (and Tier 1 for comparison) on the development route with the
// default layout. Run on its own: --filter "TestCategory=Probe". Writes to PROBE_OUT if set, else the test output.
[TestClass]
public sealed class TierTwoProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestCategory("Probe")]
    [TestCategory("Slow")]
    [TestMethod]
    public void ProbeWholeDays()
    {
        var report = new StringBuilder();
        report.AppendLine("tier,layout,seed,status,death,stars,mood%,guests,toiletPeak%,toiletsFull,davCalls,maxTapQ,maxBarQ,maxFoodQ,maxToiletQ,openCash,closeCash,debt,stuck,noRoute,ticks,ticksPerSec");
        foreach (var (tier, secondMedic) in new[] { (2, true), (2, false), (1, false) })
        foreach (var seed in new ulong[] { 20260922, 20260923, 20260924 })
            report.AppendLine(Day(tier, seed, secondMedic));
        if (Environment.GetEnvironmentVariable("PROBE_OUT") is { Length: > 0 } path) File.WriteAllText(path, report.ToString());
        TestContext.WriteLine(report.ToString());
    }

    /// <summary><paramref name="secondMedic"/> fills Tier 2's free second medic slot.</summary>
    private static string Day(int tier, ulong seed, bool secondMedic)
    {
        var s = NextFestivalTests.Ready(GameSession.CreateDevelopmentFestival(seed, tier));
        if (secondMedic) BuildSession.Accept(s, new AcceptPreparationOfferCommand(BuildSession.ExtraId(s, StaffRole.Medic)));
        var opening = s.CapturePreparation()!.OpeningCashPennies;
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        int toiletPeak = 0, tapQ = 0, barQ = 0, foodQ = 0, toiletQ = 0, calls = 0;
        var full = new HashSet<string>();
        var history = new Dictionary<EntityId, Queue<(int X, int Z, AgentNavigationAction Action)>>();
        var stuck = new HashSet<EntityId>();
        var noRoute = new HashSet<EntityId>();
        var clock = Stopwatch.StartNew();
        var start = s.CurrentTick;
        while (s.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing && s.CurrentTick - start < 80_000)
        {
            s.AdvanceWithoutSnapshot(400);
            foreach (var toilet in s.CaptureToilets())
            {
                toiletPeak = Math.Max(toiletPeak, toilet.FullPercent);
                toiletQ = Math.Max(toiletQ, toilet.Queue.Length);
                if (toilet.IsFull) full.Add(toilet.Id);
                // Call Dav once a loo is nine-tenths full, as an attentive player would.
                if (toilet.FullPercent >= 90 && s.PreparedStatus == PreparationStatus.Running &&
                    BuildSession.Send(s, new CallLavSuckerCommand(toilet.Id)).IsAccepted) calls++;
            }
            foreach (var tap in s.CaptureWaterPoints()) tapQ = Math.Max(tapQ, tap.Queue.Length + tap.Overflow.Length);
            foreach (var vendor in s.CaptureVendors())
                if (vendor.Id == "drinks") barQ = Math.Max(barQ, vendor.Queue.Length); else foodQ = Math.Max(foodQ, vendor.Queue.Length);
            foreach (var agent in s.CaptureObservation().NavigationAgents)
            {
                if (agent.Action == AgentNavigationAction.NoRoute) noRoute.Add(agent.Id);
                if (!history.TryGetValue(agent.Id, out var seen)) history[agent.Id] = seen = new();
                seen.Enqueue((agent.XMillimetres, agent.ZMillimetres, agent.Action));
                if (seen.Count > 7) seen.Dequeue();
                // Travelling but in the same spot for six samples (2,400 ticks, half a festival hour).
                if (seen.Count == 7 && seen.All(item => item.Action == AgentNavigationAction.Travelling && item.X == agent.XMillimetres && item.Z == agent.ZMillimetres))
                    stuck.Add(agent.Id);
            }
        }
        clock.Stop();
        var ticks = s.CurrentTick - start;
        var p = s.CapturePreparation()!;
        var death = s.CaptureLifecycleSnapshot()?.Casualties.LastOrDefault() is { } c ? $"{c.Role}:{c.Cause.Replace(',', ';')}" : "-";
        var guests = p.People.Where(person => person.Role == ProtectedPersonRole.Guest && person.Admitted).ToArray();
        var mood = p.Result?.SatisfactionPercent ?? (guests.Length == 0 ? 0 : (decimal)guests.Average(person => person.Satisfaction) / 100m);
        return string.Join(",", tier, secondMedic ? "default+medic2" : "default", seed, p.Status, death, p.Result?.Stars?.ToString() ?? "-", $"{mood:0.0}", p.People.Count(x => x.Role == ProtectedPersonRole.Guest),
            toiletPeak, full.Count, calls, tapQ, barQ, foodQ, toiletQ, opening, s.CaptureSnapshot().FestivalFinances.Single().CashPennies,
            s.CaptureCampaignPlanningSnapshot()!.Loan.OutstandingPrincipalPennies, stuck.Count, noRoute.Count, ticks, $"{ticks / clock.Elapsed.TotalSeconds:0}");
    }
}

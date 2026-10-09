using System.Text;
using Festival.Simulation;

namespace Festival.Tests;

// Not a check: whole Tier 1 days on the default layout, with and without one marquee by the crowd.
// Run on its own: --filter "TestCategory=Probe&FullyQualifiedName~MarqueeProbe". Writes to PROBE_OUT if set, else the test output.
[TestClass]
public sealed class MarqueeProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestCategory("Probe")]
    [TestCategory("Slow")]
    [TestMethod]
    public void ProbeTierOneWithAndWithoutAMarquee()
    {
        var report = new StringBuilder();
        report.AppendLine("layout,seed,status,death,stars,mood%,peakHeat,meanPeakHeat,distressed,collapses,restedShade,restedFirstAid,peakSheltering,openCash,closeCash");
        foreach (var marquee in new[] { false, true })
        foreach (var seed in new ulong[] { 20260922, 20260923, 20260924 })
            report.AppendLine(Day(seed, marquee));
        if (Environment.GetEnvironmentVariable("PROBE_OUT") is { Length: > 0 } path) File.WriteAllText(path, report.ToString());
        TestContext.WriteLine(report.ToString());
    }

    private static string Day(ulong seed, bool marquee)
    {
        var s = NextFestivalTests.Ready(GameSession.CreateDevelopmentFestival(seed, 1));
        if (marquee) BuildSession.Accept(s, new PlaceBuildServiceCommand(BuildServiceKind.Marquee, MarqueeTests.ByTheCrowd));
        var opening = s.CapturePreparation()!.OpeningCashPennies;
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        var guests = s.CapturePreparation()!.People.Where(person => person.Role == ProtectedPersonRole.Guest).Select(person => person.AgentId).ToHashSet();
        var peak = new Dictionary<ulong, int>();
        var distressed = new HashSet<ulong>();
        var shade = new HashSet<(ulong, GridCell)>();
        var firstAid = new HashSet<(ulong, GridCell)>();
        var peakSheltering = 0;
        var start = s.CurrentTick;
        while (s.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing && s.CurrentTick - start < 80_000)
        {
            s.AdvanceWithoutSnapshot(80);
            if (s.CaptureMedical() is not { } medical) continue;
            foreach (var need in medical.Needs.Where(need => guests.Contains(need.AgentId)))
            {
                peak[need.AgentId] = Math.Max(peak.GetValueOrDefault(need.AgentId), need.HeatExposure);
                if (need.Stage is MedicalStage.Distress or MedicalStage.Collapsed or MedicalStage.Critical) distressed.Add(need.AgentId);
            }
            var resting = medical.Needs.Where(need => need.Intent == MedicalIntent.Rest && guests.Contains(need.AgentId)).Select(need => need.AgentId).ToHashSet();
            // One rest each time someone heads for a place; the same place twice in a row counts once.
            foreach (var agent in s.CaptureSnapshot().NavigationAgents.Where(agent => resting.Contains(agent.Id.Value) && agent.Destination is not null))
                if (s.IsMarqueeRestSpot(agent.Destination!.Value)) shade.Add((agent.Id.Value, agent.Destination.Value));
                else if (s.IsRestSpot(agent.Destination.Value)) firstAid.Add((agent.Id.Value, agent.Destination.Value));
            peakSheltering = Math.Max(peakSheltering, s.CaptureMarquees().Sum(tent => tent.Sheltering));
        }
        var p = s.CapturePreparation()!;
        var death = s.CaptureLifecycleSnapshot()?.Casualties.LastOrDefault() is { } c ? $"{c.Role}:{c.Cause.Replace(',', ';')[..Math.Min(60, c.Cause.Length)]}" : "-";
        var admitted = p.People.Where(person => person.Role == ProtectedPersonRole.Guest && person.Admitted).ToArray();
        var mood = p.Result?.SatisfactionPercent ?? (admitted.Length == 0 ? 0 : (decimal)admitted.Average(person => person.Satisfaction) / 100m);
        return string.Join(",", marquee ? "default+marquee" : "default", seed, p.Status, death, p.Result?.Stars?.ToString() ?? "-", $"{mood:0.0}",
            peak.Values.DefaultIfEmpty().Max(), peak.Count == 0 ? 0 : (int)peak.Values.Average(), distressed.Count, p.GuestMedicalCollapses,
            shade.Count, firstAid.Count, peakSheltering, opening, s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
    }
}

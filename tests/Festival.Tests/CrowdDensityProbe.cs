using System.Diagnostics;
using System.Text;
using Festival.Simulation;

namespace Festival.Tests;

// Not a check: how packed each set's crowd stands, by depth from the stage's front edge, over whole days at Tier 1,
// Tier 2, the Classic development preset (100 guests, three stages) and a scratch 200-guest Tier 1 day. Gated on
// FESTIVAL_CROWD_PROBE=1; run on its own with --filter "FullyQualifiedName~CrowdDensityProbe". Writes to PROBE_OUT if
// set, else the test output.
[TestClass]
public sealed class CrowdDensityProbe
{
    public TestContext TestContext { get; set; } = null!;

    // Depth bands, in cells (half a metre each) back from the front row: 0-1 m, 1-2 m, 2-3 m, 3-4 m, 4-6 m, 6-8 m, 8 m+.
    private static readonly (int From, int To, string Name)[] Bands =
        [(0, 1, "0-1m"), (2, 3, "1-2m"), (4, 5, "2-3m"), (6, 7, "3-4m"), (8, 11, "4-6m"), (12, 15, "6-8m"), (16, 99, "8m+")];
    // The crowd's middle: four cells either side of the stage's centre line, four and a half metres wide.
    private const int Strip = 4;

    [TestCategory("Probe")]
    [TestCategory("Slow")]
    [TestMethod]
    public void ProbeCrowdDensityByDepth()
    {
        if (Environment.GetEnvironmentVariable("FESTIVAL_CROWD_PROBE") != "1") return;
        var only = Environment.GetEnvironmentVariable("FESTIVAL_CROWD_PROBE_ONLY");
        var report = new StringBuilder();
        report.AppendLine("Central strip (9 cells, 4.5 m wide) people/m2 by depth from the front row; averaged over the settled part of each set.");
        report.AppendLine("scenario,stage,slot,crowd,front2m-empty%,nonfans-in-front2m,fans-in-front2m,depth-m(keen<40),depth-m(40-79),depth-m(80+)," + string.Join(",", Bands.Select(b => b.Name)) + ",enjoy/listener,moves/listener,crowd-permille,fan-front-permille,squeezed%,squeeze-loss-permille");
        var timing = new StringBuilder("scenario,guests,ticks,seconds,ticksPerSec\n");
        foreach (var (name, make) in Scenarios())
        {
            if (only is { Length: > 0 } && !name.Contains(only, StringComparison.Ordinal)) continue;
            Day(name, make(), report, timing);
            if (Environment.GetEnvironmentVariable("PROBE_OUT") is { Length: > 0 } partial) File.WriteAllText(partial, report + Environment.NewLine + timing);
        }
        report.AppendLine().Append(timing);
        if (Environment.GetEnvironmentVariable("PROBE_OUT") is { Length: > 0 } path) File.WriteAllText(path, report.ToString());
        TestContext.WriteLine(report.ToString());
    }

    private static IEnumerable<(string, Func<GameSession>)> Scenarios()
    {
        yield return ("tier1", () => NextFestivalTests.Ready(GameSession.CreateDevelopmentFestival(20260922, 1)));
        yield return ("tier2", () =>
        {
            var s = NextFestivalTests.Ready(GameSession.CreateDevelopmentFestival(20260922, 2));
            BuildSession.Accept(s, new AcceptPreparationOfferCommand(BuildSession.ExtraId(s, StaffRole.Medic)));
            return s;
        });
        yield return ("classic-dev-100", () => GameSession.CreateClassicDevelopmentTestDefaults(true, true));
        yield return ("scale-200", () =>
        {
            var s = Festival.Simulation.Fixtures.BuildScaleFixture.Create(20260922, 200);
            var perks = s.CapturePerks()!;
            BuildSession.Accept(s, new ChoosePerkCommand(perks.DraftAttempt, perks.Cursor, perks.Hand[0]));
            BuildSession.Accept(s, new UseDefaultBuildLayoutCommand());
            BuildSession.Accept(s, new SetProgrammeCommand(BuildSession.Acts));
            BuildSession.Accept(s, new SetPreparationStockCommand(40, 32));
            foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
            return s;
        });
    }

    private sealed class SetTally
    {
        public long Samples, Crowd, FrontEmpty, Front, FrontFans, Permille, FanFront, FanFrontPermille, Squeezed, SqueezedPermille;
        public readonly long[] DepthSum = new long[3], DepthCount = new long[3];
        public readonly long[] People = new long[Bands.Length];
        public int Enjoyment, Listeners, Moves;
        public string? Map;
    }

    private static void Day(string name, GameSession s, StringBuilder report, StringBuilder timing)
    {
        if (s.GetPreparationStartBlockers() is { Count: > 0 } blockers) throw new InvalidOperationException($"{name}: {string.Join("; ", blockers)}");
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        var sets = new SortedDictionary<string, SetTally>(StringComparer.Ordinal);
        var lastPlace = new Dictionary<ulong, GridCell?>();
        var clock = new Stopwatch();
        var start = s.CurrentTick;
        while (s.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing && s.CurrentTick - start < 80_000)
        {
            clock.Start();
            s.AdvanceWithoutSnapshot(80);
            clock.Stop();
            var stages = s.Stages;
            foreach (var live in s.CaptureLivePerformances())
            {
                var def = stages.First(stage => stage.Id == live.StageId);
                var key = $"{def.ShortName},{(live.PlannedTick - s.CapturePreparation()!.StartedTick) / 100}";
                if (!sets.TryGetValue(key, out var tally)) sets[key] = tally = new();
                if (live.Stage != LiveSetStage.Live) { if (live.Stage == LiveSetStage.Finished && tally.Samples > 0 && tally.Listeners == 0) Close(tally, live); continue; }
                foreach (var listener in live.Listeners)
                {
                    if (lastPlace.TryGetValue(listener.AgentId, out var was) && was is not null && listener.Place is not null && was != listener.Place) tally.Moves++;
                    lastPlace[listener.AgentId] = listener.Place;
                }
                if (s.CurrentTick % 400 != 0 || s.CurrentTick < live.StartedTick + 1_600) continue;
                tally.Samples++;
                var front = 0;
                var held = live.Listeners.Where(l => l.AtPlace && l.Place is not null).Select(l => l.Place!.Value).ToHashSet();
                foreach (var listener in live.Listeners)
                {
                    if (!listener.AtPlace || listener.Place is not { } cell) continue;
                    tally.Crowd++;
                    if (def.Depth(cell) <= 3) { front++; tally.Front++; if (listener.Enthusiasm >= 80) tally.FrontFans++; }
                    var keen = listener.Enthusiasm < 40 ? 0 : listener.Enthusiasm < 80 ? 1 : 2;
                    // What the crowd round them does to their enjoyment, worked out from the places as the game does.
                    int touching = 0, withinMetre = 0;
                    for (var dx = -2; dx <= 2; dx++)
                    for (var dz = -2; dz <= 2; dz++)
                        if ((dx != 0 || dz != 0) && held.Contains(new(cell.X + dx, cell.Z + dz))) { withinMetre++; if (Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1) touching++; }
                    var crowding = CrowdStanding.Crowding(touching, withinMetre);
                    var space = CrowdStanding.PersonalSpace(s.CampaignSeed, listener.AgentId, listener.Enthusiasm);
                    var permille = CrowdStanding.EnjoymentPermille(listener.Enthusiasm, def.Depth(cell), crowding, space);
                    tally.Permille += permille;
                    if (listener.Enthusiasm >= 80 && def.Depth(cell) <= 3) { tally.FanFront++; tally.FanFrontPermille += permille; }
                    if (crowding > space) { tally.Squeezed++; tally.SqueezedPermille += Math.Min(300, (crowding - space) * 10); }
                    tally.DepthSum[keen] += def.Depth(cell); tally.DepthCount[keen]++;
                    if (def.Lateral(cell) > Strip) continue;
                    for (var band = 0; band < Bands.Length; band++)
                        if (def.Depth(cell) >= Bands[band].From && def.Depth(cell) <= Bands[band].To) tally.People[band]++;
                }
                if (front == 0) tally.FrontEmpty++;
                if (s.CurrentTick >= live.StartedTick + 4_800 && tally.Map is null) tally.Map = Map(def, live);
            }
        }
        var ticks = s.CurrentTick - start;
        foreach (var live in s.CaptureLivePerformances())
        {
            var def = s.Stages.First(stage => stage.Id == live.StageId);
            var key = $"{def.ShortName},{(live.PlannedTick - s.CapturePreparation()!.StartedTick) / 100}";
            if (sets.TryGetValue(key, out var tally) && tally.Listeners == 0) Close(tally, live);
        }
        foreach (var (key, tally) in sets)
        {
            if (tally.Samples == 0) continue;
            var stageName = key.Split(',')[0];
            var def = s.Stages.First(stage => stage.ShortName == stageName);
            var cells = Bands.Select(band => StripCells(s, def, band.From, band.To)).ToArray();
            report.Append($"{name},{key},{(double)tally.Crowd / tally.Samples:0.0},{100.0 * tally.FrontEmpty / tally.Samples:0},{(double)(tally.Front - tally.FrontFans) / tally.Samples:0.0},{(double)tally.FrontFans / tally.Samples:0.0}");
            for (var keen = 0; keen < 3; keen++) report.Append(tally.DepthCount[keen] == 0 ? ",-" : $",{tally.DepthSum[keen] * 0.5 / tally.DepthCount[keen]:0.0}");
            for (var band = 0; band < Bands.Length; band++)
                report.Append(cells[band] == 0 ? ",-" : $",{tally.People[band] / (double)tally.Samples / (cells[band] * 0.25):0.00}");
            report.Append($",{(tally.Listeners == 0 ? 0 : (double)tally.Enjoyment / tally.Listeners):0.0},{(tally.Listeners == 0 ? 0 : (double)tally.Moves / tally.Listeners):0.0}");
            report.AppendLine($",{(tally.Crowd == 0 ? 0 : tally.Permille / tally.Crowd)},{(tally.FanFront == 0 ? "-" : (tally.FanFrontPermille / tally.FanFront).ToString())},{100.0 * tally.Squeezed / Math.Max(1, tally.Crowd):0},{(tally.Squeezed == 0 ? "-" : (tally.SqueezedPermille / tally.Squeezed).ToString())}");
        }
        if (Environment.GetEnvironmentVariable("FESTIVAL_CROWD_PROBE_MAPS") == "1")
            foreach (var (key, tally) in sets)
                if (tally.Map is not null) report.AppendLine($"{name} {key} (front at top; F fan 80+, k keen 40-79, o other):").Append(tally.Map);
        timing.AppendLine($"{name},{s.CapturePreparation()!.People.Count(p => p.Role == ProtectedPersonRole.Guest)},{ticks},{clock.Elapsed.TotalSeconds:0.0},{ticks / clock.Elapsed.TotalSeconds:0}");
    }

    /// <summary>The crowd at one moment, a row of text for each half-metre back from the front.</summary>
    private static string Map(FestivalStage def, LivePerformanceSnapshot live)
    {
        var at = live.Listeners.Where(l => l.AtPlace && l.Place is not null).ToDictionary(l => l.Place!.Value, l => l.Enthusiasm);
        var text = new StringBuilder();
        var cells = (from x in Enumerable.Range(def.AudienceBounds.MinX, def.AudienceBounds.MaxX - def.AudienceBounds.MinX + 1)
                     from z in Enumerable.Range(def.AudienceBounds.MinZ, def.AudienceBounds.MaxZ - def.AudienceBounds.MinZ + 1)
                     select new GridCell(x, z)).ToArray();
        var maxDepth = cells.Max(def.Depth);
        for (var depth = 0; depth <= maxDepth; depth++)
        {
            var row = cells.Where(c => def.Depth(c) == depth).OrderBy(c => def.Facing.X != 0 ? c.Z : c.X).ToArray();
            text.Append("  ");
            foreach (var cell in row) text.Append(at.TryGetValue(cell, out var keen) ? keen >= 80 ? 'F' : keen >= 40 ? 'k' : 'o' : '.');
            text.AppendLine();
        }
        return text.ToString();
    }

    private static void Close(SetTally tally, LivePerformanceSnapshot live)
    {
        var heard = live.Listeners.Where(listener => listener.ListenedTicks > 0).ToArray();
        tally.Listeners = Math.Max(1, heard.Length);
        tally.Enjoyment = heard.Sum(listener => listener.EnjoymentEarned);
    }

    private static int StripCells(GameSession s, FestivalStage def, int from, int to)
    {
        var count = 0;
        for (var x = def.AudienceBounds.MinX; x <= def.AudienceBounds.MaxX; x++)
        for (var z = def.AudienceBounds.MinZ; z <= def.AudienceBounds.MaxZ; z++)
        {
            var cell = new GridCell(x, z);
            if (def.Lateral(cell) <= Strip && def.Depth(cell) >= from && def.Depth(cell) <= to && s.TraversalGrid!.Get(cell).IsWalkable) count++;
        }
        return count;
    }
}

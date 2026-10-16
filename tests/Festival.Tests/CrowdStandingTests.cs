using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

/// <summary>
/// Where listeners choose to stand: any walkable cell of a stage's audience ground, one to a cell, by their own eagerness
/// for the act and their own personal space. A keen crowd packs the front; at a quiet set the unkeen hang back and leave
/// the front to the odd fan.
/// </summary>
[TestClass]
public sealed class CrowdStandingTests
{
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FestivalStage Trailer = FestivalStages.Main;

    // ---- The traits and the enjoyment rule ----

    [TestMethod]
    public void PersonalSpaceIsTheGuestsOwnAndEagernessNudgesItUp()
    {
        var spaces = Enumerable.Range(1, 400).Select(id => CrowdStanding.PersonalSpace(20260922, (ulong)id, 0)).ToArray();
        Assert.IsTrue(spaces.All(space => space is >= 4 and <= 30), "From wanting a metre to themselves to nearly shoulder to shoulder.");
        Assert.IsTrue(spaces.Min() <= 5 && spaces.Max() >= 29, "The whole range turns up in a crowd.");
        Assert.AreEqual(spaces[17], CrowdStanding.PersonalSpace(20260922, 18, 0), "Keyed on the guest, not drawn.");
        Assert.AreNotEqual(string.Join(',', spaces), string.Join(',', Enumerable.Range(1, 400).Select(id => CrowdStanding.PersonalSpace(7, (ulong)id, 0))),
            "Another campaign's crowd is made up of other people.");
        for (var id = 1UL; id <= 50; id++)
            Assert.AreEqual(CrowdStanding.PersonalSpace(20260922, id, 0) + 10, CrowdStanding.PersonalSpace(20260922, id, 100));
        Assert.AreEqual(CrowdStanding.MaxCrowding, CrowdStanding.Crowding(8, 24), "Every cell within a metre taken.");
        Assert.AreEqual(0, CrowdStanding.Crowding(0, 0));
    }

    [TestMethod]
    public void FansNearTheFrontEnjoyMoreAndTheSqueezedLessInProportion()
    {
        // A fan at the barrier gets a fifth more, less further back; nobody below 60 gets a front bonus.
        Assert.AreEqual(1200, CrowdStanding.EnjoymentPermille(100, 0, 0, 20));
        Assert.AreEqual(1100, CrowdStanding.EnjoymentPermille(100, 4, 0, 20));
        Assert.IsTrue(CrowdStanding.EnjoymentPermille(100, 16, 0, 20) < 1050);
        Assert.AreEqual(1000, CrowdStanding.EnjoymentPermille(55, 0, 0, 20));
        // Squeezed past their personal space: a hundredth less for each step of excess, at most three tenths.
        Assert.AreEqual(1000, CrowdStanding.EnjoymentPermille(30, 6, 20, 20));
        Assert.AreEqual(950, CrowdStanding.EnjoymentPermille(30, 6, 25, 20));
        Assert.AreEqual(900, CrowdStanding.EnjoymentPermille(30, 6, 30, 20));
        Assert.AreEqual(700, CrowdStanding.EnjoymentPermille(30, 6, 40, 4));
        // A packed front: the fan's bonus and their squeeze both count.
        Assert.AreEqual(1200 - 100, CrowdStanding.EnjoymentPermille(100, 0, 40, 30));
    }

    // ---- Choosing where to stand ----

    [TestMethod]
    public void AQuietSetLeavesTheFrontToTheFan()
    {
        var s = Arranged(20260922, [95, 30, 30, 35, 30, 45, 30, 30], out var ids);
        s.AdvanceWithoutSnapshot(2_400);
        var depths = Depths(s, ids);
        Console.WriteLine($"Quiet depths (cells): {string.Join(' ', depths.Select(d => d?.ToString() ?? "-"))}");
        Assert.IsTrue(depths[0] is <= 1, "The fan goes right up to the front.");
        Assert.IsTrue(depths.Skip(1).Count(d => d is not null) >= 5, "Most of the rest have found somewhere to stand.");
        Assert.IsTrue(depths.Skip(1).All(d => d is null or >= 4), "Nobody else stands in the first two metres.");
    }

    [TestMethod]
    public void AKeenCrowdPacksTheFrontAndThinsBehind()
    {
        var keen = Enumerable.Repeat(90, 22).ToArray();
        var busy = Arranged(20260922, keen, out var ids);
        busy.AdvanceWithoutSnapshot(2_400);
        var depths = Depths(busy, ids).OfType<int>().ToArray();
        Console.WriteLine($"Busy depths (cells): {string.Join(' ', depths.Order())}");
        var firstMetre = depths.Count(d => d <= 1);
        var nextMetre = depths.Count(d => d is 2 or 3);
        var beyond = depths.Count(d => d >= 4);
        Assert.IsTrue(depths.Length >= 16, "Nearly everyone has a place.");
        Assert.IsTrue(firstMetre >= 9, $"The first metre packs: {firstMetre}.");
        Assert.IsTrue(firstMetre > nextMetre && nextMetre >= beyond / 2, $"And thins behind: {firstMetre}, {nextMetre}, {beyond}.");
        // Packed: in the first metre the keen stand closer than the metre apart the old places kept.
        var places = Places(busy, ids).Where(p => Trailer.Depth(p) <= 1).ToHashSet();
        Assert.IsTrue(places.Count(p => places.Contains(new(p.X, p.Z + 1))) >= 4, "Shoulder to shoulder along the front.");
    }

    [TestMethod]
    public void OnlyOneListenerToACellOnStandableGround()
    {
        var s = BuildSession.Started(20260922);
        var moves = new Dictionary<ulong, int>();
        var last = new Dictionary<ulong, GridCell?>();
        var end = s.CapturePreparation()!.StartedTick + Trailer.SlotEnds[0];
        while (s.CurrentTick < end)
        {
            s.AdvanceWithoutSnapshot(80);
            var live = s.CaptureLivePerformance()!;
            var departed = s.CapturePreparation()!.People.Where(p => p.Departed).Select(p => p.AgentId).ToHashSet();
            var held = live.Listeners.Where(l => l.Place is not null && !departed.Contains(l.AgentId)).Select(l => l.Place!.Value).ToArray();
            Assert.AreEqual(held.Length, held.Distinct().Count(), $"Two listeners share a cell at {s.CurrentTick}.");
            foreach (var place in held)
                Assert.IsTrue(Trailer.InAudienceArea(place) && s.TraversalGrid!.Get(place).IsWalkable, $"{place} is off the audience's ground.");
            foreach (var listener in live.Listeners)
            {
                if (last.TryGetValue(listener.AgentId, out var was) && was is not null && was != listener.Place)
                    moves[listener.AgentId] = moves.GetValueOrDefault(listener.AgentId) + 1;
                last[listener.AgentId] = listener.Place;
            }
        }
        // No jitter: over a whole set a listener re-weighs every ten seconds or so, but moves only for a clear gain.
        Console.WriteLine($"Moves over the first set: max {moves.Values.DefaultIfEmpty().Max()}, total {moves.Values.Sum()} for {last.Count} listeners.");
        Assert.IsTrue(moves.Values.DefaultIfEmpty().Max() <= 8, "Someone kept moving.");
        Assert.IsTrue(moves.Values.Sum() <= last.Count * 3, "The crowd kept shuffling.");
    }

    [TestMethod]
    public void TheCrowdSettlesOnceItHasPacked()
    {
        var s = Arranged(20260922, Enumerable.Repeat(90, 22).ToArray(), out var ids);
        s.AdvanceWithoutSnapshot(3_200);
        var settled = PlacesOrNull(s, ids);
        var changes = 0;
        for (var tick = 0; tick < 4_000; tick += 80)
        {
            s.AdvanceWithoutSnapshot(80);
            var now = PlacesOrNull(s, ids);
            changes += now.Zip(settled).Count(pair => pair.First != pair.Second);
            settled = now;
        }
        Console.WriteLine($"Place changes in fifty seconds of a settled crowd: {changes}");
        Assert.IsTrue(changes <= ids.Length / 2, $"A settled crowd keeps shuffling: {changes} moves.");
    }

    [TestMethod]
    public void AFanAtTheFrontEnjoysTheSetMoreThanAtTheBack()
    {
        var earned = new List<int>();
        foreach (var depth in new[] { 0, 16 })
        {
            var s = LiveSession(20260922);
            var live = s.CaptureLivePerformance()!;
            var fan = live.Listeners.First(l => s.CapturePerson(l.AgentId) is { Admitted: true, Departed: false }).AgentId;
            var place = new GridCell(Trailer.Front + depth, Trailer.Sightline);
            Teleport(s, fan, place, "performance.listen");
            BuildSession.SetMainLive(s, live with { Listeners = live.Listeners.Select(l => l.AgentId == fan
                ? l with { Place = place, AtPlace = true, Enthusiasm = 100, ListenedTicks = 0, EnjoymentEarned = 0, LastDecisionTick = s.CurrentTick + 1_000_000 }
                : l with { Place = null, AtPlace = false, LastDecisionTick = s.CurrentTick + 1_000_000 }).ToArray() });
            s.AdvanceWithoutSnapshot(1_600);
            earned.Add(s.CaptureLivePerformance()!.Listeners.Single(l => l.AgentId == fan).EnjoymentEarned);
        }
        Console.WriteLine($"A fan's enjoyment over twenty seconds: front {earned[0]}, back {earned[1]}");
        Assert.IsTrue(earned[1] > 0, "The fan at the back still hears the set.");
        Assert.IsTrue(earned[0] * 100 >= earned[1] * 110, "The front is worth more to a fan.");
    }

    [TestMethod]
    public void APackedCrowdRestoresMidSetAndCarriesOnIdentically()
    {
        var s = Arranged(20260922, Enumerable.Repeat(90, 22).ToArray(), out _, othersWait: false);
        s.AdvanceWithoutSnapshot(2_400);
        var saved = s.CapturePersistenceSnapshot();
        var restored = GameSession.Restore(saved);
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
        s.AdvanceWithoutSnapshot(1_600);
        restored.Session.AdvanceWithoutSnapshot(1_600);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.Session.CaptureSnapshot().AuthoritativeHash);

        // A saved place is one to a cell, on the audience's ground and walkable.
        var live = saved.LivePerformances![0]!;
        var placed = live.Listeners.Where(l => l.Place is not null).ToArray();
        Assert.IsTrue(placed.Length >= 2);
        LivePerformanceSnapshot With(Func<LiveListener, LiveListener> change) => live with { Listeners = live.Listeners.Select(change).ToArray() };
        var shared = With(l => l.AgentId == placed[1].AgentId ? l with { Place = placed[0].Place } : l);
        var outside = With(l => l.AgentId == placed[1].AgentId ? l with { Place = new GridCell(Trailer.AudienceBounds.MaxX + 1, Trailer.Sightline) } : l);
        var blocked = saved.TraversalGrid!.Cells.First(c => !c.IsWalkable && Trailer.InAudienceArea(new(c.X, c.Z)));
        var solid = With(l => l.AgentId == placed[1].AgentId ? l with { Place = new GridCell(blocked.X, blocked.Z) } : l);
        foreach (var bad in new[] { shared, outside, solid })
            Assert.IsFalse(GameSession.Restore(saved with { LivePerformances = [bad] }).IsSuccess);
    }

    [TestMethod]
    public void ACrowdReplaysIdentically()
    {
        GameSession Run()
        {
            var s = Arranged(20260926, Enumerable.Range(0, 22).Select(i => 20 + i * 3).ToArray(), out _);
            s.AdvanceWithoutSnapshot(3_200);
            return s;
        }
        var first = Run();
        var second = Run();
        Assert.AreEqual(first.CaptureSnapshot().AuthoritativeHash, second.CaptureSnapshot().AuthoritativeHash);
        CollectionAssert.AreEqual(first.CaptureLivePerformance()!.Listeners.Select(l => l.Place).ToArray(),
            second.CaptureLivePerformance()!.Listeners.Select(l => l.Place).ToArray());
    }

    [TestMethod]
    public void CentralBehindTheFrontBeatsTheFarSideUntilTheCentreIsACrush()
    {
        var s = LiveSession(20260922);
        var live = s.CaptureLivePerformance()!;
        var ids = live.Listeners.Select(l => l.AgentId).ToArray();
        var centre = new GridCell(106, 150);
        var side = new GridCell(104, 158);
        var start = new GridCell(105, 157);
        int Cost(GridCell[] crowd, GridCell cell)
        {
            var listeners = live.Listeners.Select((l, i) => l with { Place = null, Enthusiasm = i == 0 ? 35 : l.Enthusiasm }).ToArray();
            var ground = typeof(GameSession).GetMethod("CrowdGroundOf", Hidden)!.Invoke(s, [0, listeners, new HashSet<ulong>()])!;
            // The crowd, standing on its cells.
            foreach (var body in crowd) ground.GetType().GetMethod("Set")!.Invoke(ground, [body, true]);
            return (int)typeof(GameSession).GetMethod("CrowdSpotCost", Hidden)!.Invoke(s, [ground, listeners[0], 4, cell, start])!;
        }
        GridCell[] front = [new(103, 149), new(103, 151)];
        Assert.IsTrue(Cost(front, centre) < Cost(front, side), "Behind the front, in the middle, beats the far side.");
        GridCell[] crush = [.. front, new(106, 149), new(106, 151), new(107, 150), new(105, 150), new(105, 149), new(105, 151), new(107, 149), new(107, 151)];
        Assert.IsTrue(Cost(crush, centre) > Cost(crush, side), $"A crowd-shy listener gives up the middle to a crush: {Cost(crush, centre)} vs {Cost(crush, side)}.");
        Assert.IsTrue(ids.Length > crush.Length);
    }

    [TestCategory("Slow")]
    [TestMethod]
    public void TwoHundredGuestsPackTheFrontOfABusySet()
    {
        var s = Festival.Simulation.Fixtures.BuildScaleFixture.Create(20260922, 200);
        var perks = s.CapturePerks()!;
        BuildSession.Accept(s, new ChoosePerkCommand(perks.DraftAttempt, perks.Cursor, perks.Hand[0]));
        BuildSession.Accept(s, new UseDefaultBuildLayoutCommand());
        BuildSession.Accept(s, new SetProgrammeCommand(BuildSession.Acts));
        BuildSession.Accept(s, new SetPreparationStockCommand(40, 32));
        foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        var started = s.CurrentTick;
        s.AdvanceWithoutSnapshot(Trailer.SlotStarts[0] + 4_800);
        // People a square metre in the crowd's middle (four cells either side of the centre line), by depth band.
        double[] bands = new double[3];
        var samples = 0;
        for (var i = 0; i < 6 && s.CurrentTick < started + Trailer.SlotEnds[0]; i++, s.AdvanceWithoutSnapshot(400))
        {
            var places = s.CaptureLivePerformance()!.Listeners.Where(l => l.AtPlace && Trailer.Lateral(l.Place!.Value) <= 4).Select(l => Trailer.Depth(l.Place!.Value)).ToArray();
            bands[0] += places.Count(d => d <= 1) / (2 * 9 * 0.25);
            bands[1] += places.Count(d => d is >= 6 and <= 9) / (4 * 9 * 0.25);
            bands[2] += places.Count(d => d >= 14) / (5 * 9 * 0.25);
            samples++;
        }
        var (front, middle, back) = (bands[0] / samples, bands[1] / samples, bands[2] / samples);
        Console.WriteLine($"200 guests, people/m2: front metre {front:0.00}, 3-5 m {middle:0.00}, 7 m+ {back:0.00}");
        Assert.IsTrue(front >= 2.5, "The front packs.");
        Assert.IsTrue(front > middle && middle > back && back < 1.2, "And thins towards the back.");
    }

    // ---- Set-up ----

    /// <summary>A Tier 1 day at its first set, with the stage's band on and the set playing.</summary>
    private static GameSession LiveSession(ulong seed)
    {
        var s = BuildSession.Started(seed);
        for (var i = 0; i < 200 && s.CaptureLivePerformance()?.Stage != LiveSetStage.Live; i++) s.AdvanceWithoutSnapshot(80);
        Assert.AreEqual(LiveSetStage.Live, s.CaptureLivePerformance()!.Stage);
        return s;
    }

    /// <summary>
    /// The first set playing, with as many admitted guests as there are eagernesses brought round behind the crowd to choose
    /// afresh where to stand; everyone else is out of the audience and not choosing.
    /// </summary>
    private static GameSession Arranged(ulong seed, int[] eagerness, out ulong[] ids, bool othersWait = true)
    {
        var s = LiveSession(seed);
        var live = s.CaptureLivePerformance()!;
        var chosen = live.Listeners.Where(l => s.CapturePerson(l.AgentId) is { Admitted: true, Departed: false } &&
            true)
            .Select(l => l.AgentId).Take(eagerness.Length).ToArray();
        Assert.AreEqual(eagerness.Length, chosen.Length, "Not enough guests on site.");
        for (var i = 0; i < live.Listeners.Length; i++)
        {
            var id = live.Listeners[i].AgentId;
            var at = Array.IndexOf(chosen, id);
            if (s.CapturePerson(id) is not { Admitted: true, Departed: false }) continue;
            Teleport(s, id, at >= 0 ? new GridCell(124 + at % 3, 140 + at) : new GridCell(180, 110 + i * 2), "labelled.audience-initial-layout");
        }
        BuildSession.SetMainLive(s, live with { Listeners = live.Listeners.Select(l => Array.IndexOf(chosen, l.AgentId) is var at and >= 0
            ? l with { Place = null, AtPlace = false, Enthusiasm = eagerness[at], LastDecisionTick = -800 }
            : l with { Place = null, AtPlace = false, LastDecisionTick = othersWait ? s.CurrentTick + 1_000_000 : s.CurrentTick }).ToArray() });
        ids = chosen;
        return s;
    }

    private static void Teleport(GameSession session, ulong id, GridCell cell, string intent)
    {
        var agents = typeof(GameSession).GetField("_navigationAgents", Hidden)!.GetValue(session)!;
        var agent = agents.GetType().GetProperty("Item")!.GetValue(agents, [new EntityId(id)])!;
        var centre = TraversalGrid.CellCentre(cell);
        foreach (var (name, value) in new[] { ("XMillimetres", centre.XMillimetres), ("ZMillimetres", centre.ZMillimetres),
            ("SegmentOriginXMillimetres", centre.XMillimetres), ("SegmentOriginZMillimetres", centre.ZMillimetres) })
            agent.GetType().GetProperty(name)!.SetValue(agent, value);
        typeof(GameSession).GetMethod("ApplyAgentDestination", Hidden)!.Invoke(session, [new EntityId(id), new SetAgentDestinationCommand(cell, intent), false]);
    }

    private static GridCell?[] PlacesOrNull(GameSession s, ulong[] ids) =>
        ids.Select(id => s.CaptureLivePerformance()!.Listeners.Single(l => l.AgentId == id).Place).ToArray();
    private static GridCell[] Places(GameSession s, ulong[] ids) => PlacesOrNull(s, ids).OfType<GridCell>().ToArray();
    private static int?[] Depths(GameSession s, ulong[] ids) => PlacesOrNull(s, ids).Select(p => p is { } c ? Trailer.Depth(c) : (int?)null).ToArray();
}

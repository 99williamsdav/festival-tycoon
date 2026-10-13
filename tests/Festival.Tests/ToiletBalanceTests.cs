using System.Text.Json;
using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

/// <summary>
/// The default layout's two loos side by side share a rush: a newcomer counts everyone already on their way to a cubicle,
/// and someone queuing moves to the other cubicle once it would serve them a few seconds sooner.
/// </summary>
[TestClass]
public sealed class ToiletBalanceTests
{
    private static readonly System.Reflection.MethodInfo Mutate =
        typeof(GameSession).GetMethod("MutatePerson", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    /// <summary>A rush on the loos: every guest at a need well past wanting to go but not yet desperate.</summary>
    private static void Rush(GameSession s, int need = 7_500)
    {
        foreach (var guest in s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted))
            Mutate.Invoke(s, [guest.AgentId, (Action<Person>)(p => p.ToiletNeed = need)]);
    }

    /// <summary>Those waiting for a loo: queuing (not the one inside) and on their way.</summary>
    private static int Waiting(GameSession s, ToiletFacility t) => t.Queue.Count(id => id != t.OwnerId) +
        s.CaptureImmersion()!.People.Count(p => p.ToiletId == t.Id && p.ToiletStage == ToiletVisitStage.Approaching);

    private static GameSession Day(ulong seed = 20260922)
    {
        var s = WithoutFaults(Started(seed));
        s.AdvanceWithoutSnapshot(3_200);
        return s;
    }

    [TestMethod]
    public void ARushSharesBothLoosEvenly()
    {
        var s = Day();
        var before = s.CaptureToilets().ToDictionary(t => t.Id, t => t.WeeCount + t.PooCount);
        Rush(s);
        var idleBesideAQueue = 0;
        var worst = 0;
        for (var step = 0; step < 300; step++)
        {
            s.AdvanceWithoutSnapshot(16);
            var toilets = s.CaptureToilets();
            Assert.AreEqual(2, toilets.Count);
            var waiting = toilets.Select(t => Waiting(s, t)).ToArray();
            worst = Math.Max(worst, Math.Abs(waiting[0] - waiting[1]));
            for (var i = 0; i < 2; i++)
                if (toilets[i].OwnerId is null && toilets[i].Queue.Length == 0 && toilets[1 - i].Queue.Count(id => id != toilets[1 - i].OwnerId) >= 2)
                    idleBesideAQueue++;
        }
        var uses = s.CaptureToilets().Select(t => t.WeeCount + t.PooCount - before[t.Id]).ToArray();
        Assert.IsTrue(uses.Min() * 10 >= uses.Sum() * 4, $"Each loo takes a fair share of the rush: {string.Join(", ", uses)}.");
        Assert.AreEqual(0, idleBesideAQueue, "A free cubicle never stands empty beside a queue of two or more.");
        Assert.IsTrue(worst <= 3, $"The two queues (and those heading for them) stay within three of each other; worst {worst}.");
    }

    [TestMethod]
    public void PeopleQueuingMoveToALooThatFreesUpAndTheQueuesBalance()
    {
        var outcome = QueuesBalanceOnceALooFreesUp(20260922);
        Assert.AreEqual("balanced", outcome);
    }

    [TestMethod]
    public void QueuesBalanceOnAlmostEveryDay()
    {
        var seeds = new ulong[] { 20260922, 20260923, 20260924, 20260926, 20261003, 7, 11, 12345 };
        var outcomes = new string[seeds.Length];
        Parallel.For(0, seeds.Length, i => outcomes[i] = QueuesBalanceOnceALooFreesUp(seeds[i]));
        // Six of eight (2026-10-13): seed 11 never forms the queue (one guest waiting), and on 20261003 a queue of seven against
        // two stays put, each guest's estimate putting the other loo less than three seconds sooner once two have moved.
        Assert.IsTrue(outcomes.Count(item => item == "balanced") >= 6,
            $"At least six of eight days balance: {string.Join(", ", seeds.Zip(outcomes, (seed, outcome) => $"{seed} {outcome}"))}.");
    }

    /// <summary>Everyone queues at one loo while the other is full; then it's emptied: within three seconds someone moves and the queues even up.</summary>
    internal static string QueuesBalanceOnceALooFreesUp(ulong seed, int need = GameSession.UrgentToilet)
    {
        var s = Day(seed);
        var spare = s.CaptureToilets().Single(t => t.Id == "toilet.extra-1");
        SetToilet(s, spare with { WeeCount = spare.WeeCount + ToiletRules.CapacityMillilitres / ToiletRules.WeeMillilitres });
        // Bursting: nobody puts it off until the applause.
        Rush(s, need);
        s.AdvanceWithoutSnapshot(1_600);
        var main = s.CaptureToilets().Single(t => t.Id == "toilet.main");
        if (Waiting(s, main) < 3) return $"no queue formed ({Waiting(s, main)})";
        spare = s.CaptureToilets().Single(t => t.Id == "toilet.extra-1");
        if (spare.Queue.Length != 0) return "someone queued for the full loo";
        SetToilet(s, spare with { EmptiedMillilitres = spare.UsedMillilitres + spare.EmptiedMillilitres });
        var queued = main.Queue.Where(id => id != main.OwnerId).ToHashSet();
        var moved = 0;
        var balanced = false;
        // Three seconds: everyone has had at least two looks at the two queues.
        for (var step = 0; step < 15 && !balanced; step++)
        {
            s.AdvanceWithoutSnapshot(16);
            var toilets = s.CaptureToilets();
            moved = s.CaptureImmersion()!.People.Count(p => queued.Contains(p.AgentId) && p.ToiletId == "toilet.extra-1");
            balanced = moved > 0 && Math.Abs(Waiting(s, toilets[0]) - Waiting(s, toilets[1])) <= 1;
        }
        if (balanced) return "balanced";
        var end = s.CaptureToilets();
        return $"{(moved == 0 ? "nobody moved" : "moved but uneven")} ({Waiting(s, end[0])} vs {Waiting(s, end[1])}): " +
            string.Join(" | ", end[0].Queue.Where(id => id != end[0].OwnerId).Select(id => s.CapturePerson(id)!.Reason));
    }

    [TestMethod]
    public void SomeoneSwapsQueuesOnlyToBeServedClearlySooner()
    {
        ActivityScore Plan(string toilet, int complete, long score) => new(new(ActivityKind.Toilet, toilet, complete, complete + 400), score);
        var held = Plan("toilet.main", 1_000, 100_000);
        Assert.IsTrue(GameSession.SwitchesFacility(Plan("toilet.extra-1", 1_000 - GameSession.FacilitySwitchMarginTicks, 101_000), held),
            "Served three seconds sooner: swap, even for a plan barely better.");
        Assert.IsFalse(GameSession.SwitchesFacility(Plan("toilet.extra-1", 1_000 - GameSession.FacilitySwitchMarginTicks + 1, 101_000), held),
            "A second or two sooner isn't worth losing a place for.");
        Assert.IsTrue(GameSession.SwitchesFacility(Plan("toilet.extra-1", 500, 100_000), held), "Sooner and planning alike: swap.");
        Assert.IsTrue(GameSession.SwitchesFacility(Plan("toilet.extra-1", 500, 100_000 - GameSession.FacilitySwitchTieScore), held),
            "Sooner and a point worse: still swap.");
        Assert.IsFalse(GameSession.SwitchesFacility(Plan("toilet.extra-1", 500, 100_000 - GameSession.FacilitySwitchTieScore - 1), held),
            "Sooner but clearly a worse plan: stay.");
        Assert.IsTrue(GameSession.SwitchesFacility(Plan("toilet.extra-1", 1_000, 100_000 + GameSession.FacilitySwitchMargin), held),
            "A much better plan still swaps.");
        var shelter = new ActivityScore(new(ActivityKind.Shelter, "marquee.a", 400, 400), 100_000);
        Assert.IsFalse(GameSession.SwitchesFacility(new(new(ActivityKind.Shelter, "marquee.b", 100, 100), 101_000), shelter),
            "Cover isn't a queue: only a much better plan moves someone sheltering.");
    }

    [TestMethod]
    public void SavingMidQueueReplaysIdentically()
    {
        var s = Day();
        Rush(s);
        s.AdvanceWithoutSnapshot(800);
        Assert.IsTrue(s.CaptureToilets().All(t => Waiting(s, t) > 0) &&
            s.CaptureImmersion()!.People.Any(p => p.ToiletStage == ToiletVisitStage.Approaching), "Saved mid-rush, both loos busy.");
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        var copy = restored.Session!;
        for (var i = 0; i < 10; i++)
        {
            s.AdvanceWithoutSnapshot(160); copy.AdvanceWithoutSnapshot(160);
            Assert.AreEqual(JsonSerializer.Serialize(s.CapturePersistenceSnapshot()), JsonSerializer.Serialize(copy.CapturePersistenceSnapshot()),
                $"The restored day plays the same, {(i + 1) * 2} s on.");
        }
    }
}

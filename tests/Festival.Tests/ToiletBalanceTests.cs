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

    private static GameSession Day()
    {
        var s = WithoutFaults(Started());
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
        // Everyone queues at one loo while the other is full; then it's emptied.
        var s = Day();
        var spare = s.CaptureToilets().Single(t => t.Id == "toilet.extra-1");
        SetToilet(s, spare with { WeeCount = spare.WeeCount + ToiletRules.CapacityMillilitres / ToiletRules.WeeMillilitres });
        Rush(s, 6_800);
        s.AdvanceWithoutSnapshot(1_600);
        var main = s.CaptureToilets().Single(t => t.Id == "toilet.main");
        Assert.IsTrue(Waiting(s, main) >= 3, $"The busy loo has a queue: {Waiting(s, main)}.");
        spare = s.CaptureToilets().Single(t => t.Id == "toilet.extra-1");
        Assert.AreEqual(0, spare.Queue.Length, "Nobody queues for a full loo.");
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
        Assert.IsTrue(moved > 0, "Someone already queuing for the busy loo moves to the emptied one.");
        Assert.IsTrue(balanced, "Within three seconds the two queues are within one of each other.");
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
        Assert.IsFalse(GameSession.SwitchesFacility(Plan("toilet.extra-1", 500, 100_000), held), "Sooner but no better a plan: stay.");
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

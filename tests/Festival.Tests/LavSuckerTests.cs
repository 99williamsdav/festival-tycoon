using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class LavSuckerTests
{
    private static (GameSession Session, string ToiletId) WithAFullToilet()
    {
        var s = WithoutFaults(Started());
        s.AdvanceWithoutSnapshot(2_000);
        var toilet = s.CaptureToilets().First(t => t.OwnerId is null && t.Queue.Length == 0);
        var wees = (toilet.CapacityMillilitres - toilet.UsedMillilitres) / ToiletRules.WeeMillilitres + toilet.WeeCount;
        SetToilet(s, toilet with { WeeCount = wees });
        Assert.IsTrue(s.CaptureToilets().Single(t => t.Id == toilet.Id).IsFull);
        return (s, toilet.Id);
    }

    [TestMethod]
    public void DavComesForTwentyFivePoundsEmptiesAFullLooAndDrivesOffAgainAcrossASave()
    {
        var (s, id) = WithAFullToilet();
        var cash = s.CaptureSnapshot().FestivalFinances.Single().CashPennies;
        var sent = Send(s, new CallLavSuckerCommand(id));
        Assert.IsTrue(sent.IsAccepted, sent.Message);
        Assert.AreEqual(cash - LavSuckerRules.FeePennies, s.CaptureSnapshot().FestivalFinances.Single().CashPennies, "Paid up front.");
        Assert.IsFalse(Send(s, new CallLavSuckerCommand(id)).IsAccepted, "Already on the way.");
        s.AdvanceWithoutSnapshot(400);
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        s = restored.Session!;
        var pumped = false;
        for (var guard = 0; guard < 600 && s.CaptureLavSucker()!.Calls.Single().Stage != LavSuckerStage.Gone; guard++)
        {
            s.AdvanceWithoutSnapshot(80);
            if (s.CaptureLavSucker()!.Calls.Single().Stage == LavSuckerStage.Pumping)
            {
                pumped = true;
                Assert.IsTrue(s.ToiletBeingEmptied(id), "Nobody goes in while the hose is on.");
            }
        }
        Assert.IsTrue(pumped, "The tanker got there and pumped.");
        Assert.AreEqual(LavSuckerStage.Gone, s.CaptureLavSucker()!.Calls.Single().Stage, "And drove off again.");
        var after = s.CaptureToilets().Single(t => t.Id == id);
        Assert.IsFalse(after.IsFull, "Back in service.");
        Assert.IsTrue(after.UsedMillilitres < after.CapacityMillilitres / 4, "Nearly empty: just what's been used since.");
        var restoredAfter = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restoredAfter.IsSuccess, "The paid call reconciles in the books: " + restoredAfter.Error);
    }

    [TestMethod]
    public void AnEmptyLooNeedsNoCallAndDavWantsPayingUpFront()
    {
        var s = WithoutFaults(Started());
        s.AdvanceWithoutSnapshot(800);
        var empty = s.CaptureToilets().First(t => t.UsedMillilitres == 0);
        Assert.IsFalse(Send(s, new CallLavSuckerCommand(empty.Id)).IsAccepted, "Nothing to empty.");
        StringAssert.Contains(s.LavSuckerUnavailable(empty.Id), "empty");
    }
}

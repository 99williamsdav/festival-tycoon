using Festival.ContentAdapter;

namespace Festival.Tests;

[TestClass]
public sealed class FightChoreographyTests
{
    private static readonly FightClipLengths Lengths = new(.208333, 1.33333, .25, .208333, .208333);
    private static bool Swing(FightPose pose) => pose is FightPose.SwingLeft or FightPose.SwingRight;

    [TestMethod]
    public void ComedyBurstsHaveRapidJabsAndLongerIrregularPausesWithoutSimultaneousSwings()
    {
        var pair = new FightChoreography(12, 17, 800); var starts = new List<double>(); var lastTurn = -1;
        for (var frame = 0; frame < 1800; frame++)
        {
            pair.Advance(1 / 60.0, false, false, true, Lengths);
            Assert.IsFalse(Swing(pair.First.Pose) && Swing(pair.Second.Pose));
            if ((Swing(pair.First.Pose) || Swing(pair.Second.Pose)) && pair.Turn != lastTurn)
            { starts.Add(frame / 60.0); lastTurn = pair.Turn; }
        }
        var intervals = starts.Zip(starts.Skip(1), (a, b) => b - a).ToArray();
        Assert.IsTrue(intervals.Min() < .45 && intervals.Max() > .70, "Rapid bursts with actual breathers, not a uniform loop.");
        Assert.IsTrue(intervals.Distinct().Count() > 4);
    }

    [TestMethod]
    public void PairAlternatesSingleSwingOwnersWithDelayedCounterpartReactions()
    {
        var pair = new FightChoreography(12, 17, 800);
        var owners = new List<int>(); var reaction = false; var lastTurn = -1;
        for (var frame = 0; frame < 1200; frame++)
        {
            pair.Advance(1 / 60.0, false, false, true, Lengths);
            Assert.IsFalse(Swing(pair.First.Pose) && Swing(pair.Second.Pose));
            if (Swing(pair.First.Pose) || Swing(pair.Second.Pose))
            {
                if (lastTurn != pair.Turn) { owners.Add(Swing(pair.First.Pose) ? 0 : 1); lastTurn = pair.Turn; }
                reaction |= pair.First.Pose == FightPose.Flinch || pair.Second.Pose == FightPose.Flinch;
            }
        }
        Assert.IsTrue(owners.Count > 5 && reaction);
        Assert.IsTrue(owners.Zip(owners.Skip(1)).All(p => p.First != p.Second));
    }

    [TestMethod]
    public void FreezePreservesBothPosesPhaseAndPendingTurnWithoutCatchup()
    {
        var pair = new FightChoreography(12, 17, 800);
        for (var i = 0; i < 80; i++) pair.Advance(.016, false, false, true, Lengths);
        var before = (pair.First, pair.Second, pair.Seconds, pair.Turn);
        for (var i = 0; i < 100; i++) pair.Advance(100, true, true, false, Lengths);
        Assert.AreEqual(before, (pair.First, pair.Second, pair.Seconds, pair.Turn));
        pair.Advance(.016, false, false, true, Lengths);
        Assert.AreEqual(before.Turn, pair.Turn);
    }

    [TestMethod]
    public void ActualHandlingCancelsPunchAndInterruptedHandlingReturnsToGuardWithoutBacklog()
    {
        var pair = new FightChoreography(12, 17, 800);
        for (var i = 0; i < 200 && !Swing(pair.First.Pose) && !Swing(pair.Second.Pose); i++) pair.Advance(.02, false, false, true, Lengths);
        Assert.IsTrue(Swing(pair.First.Pose) || Swing(pair.Second.Pose));
        pair.Advance(.02, false, true, true, Lengths);
        Assert.AreEqual(FightPose.StandDown, pair.First.Pose); Assert.AreEqual(pair.First, pair.Second);
        for (var i = 0; i < 100; i++) pair.Advance(.02, false, true, true, Lengths);
        Assert.IsTrue(pair.Attending); Assert.IsFalse(Swing(pair.First.Pose) || Swing(pair.Second.Pose));
        pair.Advance(.02, false, false, true, Lengths);
        Assert.AreEqual(FightPose.Guard, pair.First.Pose);
    }

    [TestMethod]
    public void RestoredOrUnsafePairsDoNotReplayEntryOrScheduleSwings()
    {
        var pair = new FightChoreography(12, 17, 800, restored: true);
        Assert.AreEqual(FightPose.Guard, pair.First.Pose);
        for (var i = 0; i < 500; i++)
        {
            pair.Advance(.02, false, false, false, Lengths);
            Assert.AreEqual(FightPose.Guard, pair.First.Pose); Assert.AreEqual(FightPose.Guard, pair.Second.Pose);
        }
        Assert.AreEqual(0, pair.Turn);
    }

    [TestMethod]
    public void SeparateEncountersAreRepeatableButHaveDifferentBoundedCadences()
    {
        var a = new FightChoreography(12, 17, 800); var copy = new FightChoreography(12, 17, 800); var other = new FightChoreography(13, 21, 801);
        var differs = false;
        for (var i = 0; i < 300; i++)
        {
            a.Advance(.02, false, false, true, Lengths); copy.Advance(.02, false, false, true, Lengths); other.Advance(.02, false, false, true, Lengths);
            Assert.AreEqual((a.First, a.Second), (copy.First, copy.Second));
            differs |= a.First != other.First || a.Second != other.Second;
        }
        Assert.IsTrue(differs);
    }
}

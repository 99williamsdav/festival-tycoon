using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class ClassicBookingTermsTests
{
    private static readonly ClassicAnnouncedAct[] Original = [new("a", 0, 20, 1_000), new("b", 1, 35, 2_000), new("c", 2, 45, 3_000)];
    private static FestivalAct Act(string id, int genre, int popularity) => new(id, id, genre, 100, popularity);
    private static FestivalAct[] Unchanged => Original.Select(a => Act(a.ActId, a.Genre, a.Popularity)).ToArray();

    [TestMethod]
    public void ReorderIsFreeButEveryChangedNameHasAtLeastComparableDisappointment()
    {
        Assert.AreEqual(ClassicLineupChange.None, ClassicBookingTerms.Assess(Original, Unchanged.Reverse().ToArray()));
        var next = Unchanged; next[0] = Act("new", 0, 15);
        Assert.AreEqual(ClassicLineupChange.Comparable, ClassicBookingTerms.Assess(Original, next));
        next = [Act("c", 2, 45), Act("b", 1, 35), Act("new", 0, 15)];
        Assert.AreEqual(ClassicLineupChange.Comparable, ClassicBookingTerms.Assess(Original, next));
    }

    [TestMethod]
    public void OrdinaryLossIsLessSevereThanLostHeadlinerOrLargePopularityLoss()
    {
        var ordinary = Unchanged; ordinary[0] = Act("new", 1, 15);
        var headline = Unchanged; headline[2] = Act("new", 1, 45);
        var popular = Unchanged; popular[1] = Act("new", 1, 20);
        Assert.AreEqual(ClassicLineupChange.Ordinary, ClassicBookingTerms.Assess(Original, ordinary));
        Assert.AreEqual(ClassicLineupChange.Substantial, ClassicBookingTerms.Assess(Original, headline));
        Assert.AreEqual(ClassicLineupChange.Substantial, ClassicBookingTerms.Assess(Original, popular));
        Assert.AreEqual(ClassicLineupChange.Substantial, ClassicBookingTerms.Assess(Original, Unchanged, ClassicLineupChange.Substantial));
    }

    [TestMethod]
    public void RefundTargetsRoundUpAndRepeatedChangesCannotResetOrCompoundThem()
    {
        Assert.AreEqual(2, ClassicBookingTerms.RefundTarget(25, ClassicLineupChange.Comparable));
        Assert.AreEqual(5, ClassicBookingTerms.RefundTarget(25, ClassicLineupChange.Ordinary));
        Assert.AreEqual(10, ClassicBookingTerms.RefundTarget(25, ClassicLineupChange.Substantial));
        Assert.AreEqual(0, ClassicBookingTerms.RefundTarget(0, ClassicLineupChange.Substantial));
        foreach (var severity in Enum.GetValues<ClassicLineupChange>())
        {
            var target = ClassicBookingTerms.RefundTarget(25, severity);
            Assert.AreEqual(0, Math.Max(0, ClassicBookingTerms.RefundTarget(25, severity) - target));
        }
        Assert.AreEqual(200, ClassicBookingTerms.SatisfactionPenalty(ClassicLineupChange.Comparable));
        Assert.AreEqual(1_000, ClassicBookingTerms.SatisfactionPenalty(ClassicLineupChange.Ordinary));
        Assert.AreEqual(2_000, ClassicBookingTerms.SatisfactionPenalty(ClassicLineupChange.Substantial));
    }

    [TestMethod]
    public void MultipleNewActsAreMatchedToMissingActsEvenWhenTheirSlotsCross()
    {
        var replacement = new[] { Act("new-b", 1, 30), Act("new-a", 0, 15), Act("c", 2, 45) };
        Assert.AreEqual(ClassicLineupChange.Comparable, ClassicBookingTerms.Assess(Original, replacement));
        Assert.AreEqual(ClassicLineupChange.Comparable, ClassicBookingTerms.Assess(Original, replacement.Reverse().ToArray()));
    }

    [TestMethod]
    public void ComparableAndSubstantialBoundariesAreExplicit()
    {
        var next = Unchanged; next[1] = Act("new", 1, 30);
        Assert.AreEqual(ClassicLineupChange.Comparable, ClassicBookingTerms.Assess(Original, next));
        next[1] = Act("new", 1, 29);
        Assert.AreEqual(ClassicLineupChange.Ordinary, ClassicBookingTerms.Assess(Original, next));
        next[1] = Act("new", 1, 21);
        Assert.AreEqual(ClassicLineupChange.Ordinary, ClassicBookingTerms.Assess(Original, next));
        next[1] = Act("new", 1, 20);
        Assert.AreEqual(ClassicLineupChange.Substantial, ClassicBookingTerms.Assess(Original, next));
        next = Unchanged; next[2] = Act("new", 2, 40);
        Assert.AreEqual(ClassicLineupChange.Comparable, ClassicBookingTerms.Assess(Original, next));
        next[2] = Act("new", 2, 39);
        Assert.AreEqual(ClassicLineupChange.Substantial, ClassicBookingTerms.Assess(Original, next));
    }
}

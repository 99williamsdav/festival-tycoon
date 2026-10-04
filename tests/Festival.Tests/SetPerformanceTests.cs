using Festival.Simulation;
using System.Reflection;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class SetPerformanceTests
{
    private static GameSession Live(params string[] offers)
    {
        var s = Ready(offers: offers);
        Accept(s, new StartPreparedEditionCommand());
        for (var guard = 0; guard < 2_000 && s.CaptureLivePerformance()!.Stage != LiveSetStage.Live; guard++) s.AdvanceWithoutSnapshot(8);
        Assert.AreEqual(LiveSetStage.Live, s.CaptureLivePerformance()!.Stage);
        return s;
    }

    [TestMethod]
    public void TalentLooselyFollowsFameWithRealSurprises()
    {
        var talents = ActCatalogue.All.Select(act => (act.Popularity, Talent: PerformanceRules.Talent(act))).ToArray();
        Assert.IsTrue(talents.All(t => t.Talent is >= 5 and <= 98 && Math.Abs(t.Talent - t.Popularity) <= 35));
        Assert.IsTrue(talents.Any(t => t.Popularity >= 50 && t.Talent <= t.Popularity - 20), "A famous act that's sloppy live.");
        Assert.IsTrue(talents.Any(t => t.Popularity <= 30 && t.Talent >= t.Popularity + 20), "An unknown that's brilliant.");
        Assert.AreEqual(PerformanceRules.Talent(ActCatalogue.All[0]), PerformanceRules.Talent(ActCatalogue.All[0]), "Steady.");
    }

    [TestMethod]
    public void ABiggerRigSoundsBetterAndAnActIsKnownOnceItHasPlayed()
    {
        var basic = Live(); var pro = Live(PowerRules.ProRigOffer);
        Assert.IsTrue(pro.SoundScore > basic.SoundScore + 30, $"{basic.SoundScore} to {pro.SoundScore}.");
        Assert.AreEqual(PerformanceRules.Overall(basic.CurrentPerformance!.Band, basic.SoundScore), basic.CurrentPerformance.Overall);
        var act = basic.CurrentFestivalAct!;
        Assert.IsFalse(basic.TalentKnown(act), "Unknown until they've played.");
        for (var guard = 0; guard < 4_000 && basic.CaptureLivePerformance()!.Stage != LiveSetStage.Finished; guard++) basic.AdvanceWithoutSnapshot(8);
        Assert.IsTrue(basic.TalentKnown(act));
        var restored = GameSession.Restore(basic.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.IsTrue(restored.Session!.TalentKnown(act));
    }

    [TestMethod]
    public void ADrunkBandMemberDragsTheBandDown()
    {
        var s = Live();
        var sober = s.CurrentPerformance!;
        var q = s.CaptureProgramme()!;
        var member = q.Performers.First(p => p.SlotIndex == q.CurrentSlot).AgentId;
        var mutate = typeof(GameSession).GetMethod("MutatePerson", BindingFlags.Instance | BindingFlags.NonPublic)!;
        mutate.Invoke(s, [member, (Action<Person>)(p => p.Intoxication = 6_500)]);
        var drunk = s.CurrentPerformance!;
        Assert.AreEqual((6_500 - PerformanceRules.SoberLimit) / PerformanceRules.DrunkStep, drunk.Drunkenness);
        Assert.IsTrue(drunk.Band < sober.Band && drunk.Overall < sober.Overall);
        Assert.IsTrue(PerformanceRules.MusicPermille(drunk.Overall) < PerformanceRules.MusicPermille(sober.Overall), "The crowd gets less from it.");
    }
}

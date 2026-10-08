using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class MoodLedgerTests
{
    [TestMethod]
    public void TheCrowdMoodBreakdownNamesTheMusicWhileABandPlaysAndAddsUpToWhatGuestsFelt()
    {
        var s = BuildSession.Started();
        Assert.AreEqual(0, s.RecentMoodChanges().Count, "Nothing felt before anyone's in.");
        while (s.CaptureLivePerformance()?.Stage != LiveSetStage.Live && s.CurrentTick < 20_000) s.AdvanceWithoutSnapshot(80);
        s.AdvanceWithoutSnapshot(800);
        var changes = s.RecentMoodChanges();
        Assert.IsTrue(changes.Any(c => c.Cause == MoodCause.Music && c.Change > 0), "The band lifts the crowd.");
        CollectionAssert.AreEqual(changes.OrderByDescending(c => Math.Abs(c.Change)).ToArray(), changes.ToArray(), "Biggest first.");

        // Every change a guest feels goes through the ledger: what it records matches what their satisfaction did.
        var before = Guests(s);
        var recorded = s.MoodRecordedTotal;
        s.AdvanceWithoutSnapshot(2_400);
        var after = Guests(s);
        var felt = after.Sum(g => (long)g.Value) - before.Where(g => after.ContainsKey(g.Key)).Sum(g => (long)g.Value);
        Assert.AreEqual(felt, s.MoodRecordedTotal - recorded, "Nothing moves a guest's mood without a named cause.");
    }

    [TestMethod]
    public void HotThirstyGuestsFeelItInTheirMoodHeatLessSo()
    {
        var s = BuildSession.WithoutFaults(BuildSession.Started());
        s.AdvanceWithoutSnapshot(2_000);
        var guest = s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed).AgentId;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var mutate = typeof(GameSession).GetMethod("MutatePerson", flags)!;
        mutate.Invoke(s, [guest, (Action<Person>)(p => { p.Thirst = 9_500; p.HeatExposure = 9_500; p.Satisfaction = 5_000; })]);
        // On to the next festival second, when the drain is felt.
        s.AdvanceWithoutSnapshot(80 - (int)(s.CurrentTick % 80));
        var changes = s.RecentMoodChanges();
        var thirst = changes.Single(c => c.Cause == MoodCause.Thirst).Change;
        var heat = changes.Single(c => c.Cause == MoodCause.Heat).Change;
        Assert.IsTrue(thirst < 0 && heat < 0, "Both drag the mood down.");
        Assert.IsTrue(heat > thirst, "Heat less so: there's little the player can do about the weather.");
    }

    private static Dictionary<ulong, int> Guests(GameSession s) => s.CapturePreparation()!.People
        .Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed).ToDictionary(p => p.AgentId, p => p.Satisfaction);
}

using System.Reflection;
using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class GuestCharacterTests
{
    private static void SetTime(GameSession s, long tick) => typeof(GameSession).GetProperty("CurrentTick")!.SetValue(s, tick);
    private static T Call<T>(GameSession s, string method, params object[] args) =>
        (T)typeof(GameSession).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, args)!;
    private static ulong[] Guests(GameSession s) => s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest).Select(p => p.AgentId).ToArray();

    [TestMethod]
    public void EveryGuestHasAUniqueNameThatNoStaffOrPerformerShares()
    {
        var s = Started();
        var people = s.CapturePreparation()!.People;
        var guests = people.Where(p => p.Role == ProtectedPersonRole.Guest).Select(p => p.Name).ToArray();
        Assert.AreEqual(guests.Length, guests.Distinct().Count());
        Assert.AreEqual(guests.Length, guests.Select(n => n.Split(' ')[0]).Distinct().Count(), "First names differ too.");
        Assert.IsFalse(guests.Any(n => n.StartsWith("Guest ", StringComparison.Ordinal)));
        Assert.AreEqual(people.Length, people.Select(p => p.Name).Distinct().Count(), "No name clashes with staff or performers.");
        var staffNames = s.GetStaffCandidates().Select(c => c.Name).ToHashSet();
        Assert.IsFalse(guests.Any(staffNames.Contains));
        CollectionAssert.AreEqual(guests, Started().CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest).Select(p => p.Name).ToArray(), "Same seed, same names.");
        CollectionAssert.AreNotEqual(guests, GuestCharacters.Names(20260923, guests.Length));
    }

    [TestMethod]
    public void LabelsMarkTheExtremesAndMostGuestsShowFewOrNone()
    {
        var labelled = 0; var total = 0; var seen = new HashSet<string>();
        for (var seed = 20260922UL; seed < 20260962UL; seed++)
        {
            var s = GameSession.CreateBuildCampaign(seed, FestivalStanding.Established);
            foreach (var id in Guests(s))
            {
                var labels = s.GuestLabels(id);
                Assert.IsTrue(labels.Count <= GuestCharacters.MaximumLabels);
                Assert.IsFalse(labels.Contains("Twat") && labels.Contains("Goody two-shoes"), "Opposite ends of one score.");
                Assert.IsFalse(labels.Contains("Princess") && labels.Contains("Hippie"));
                total++; if (labels.Count > 0) labelled++;
                seen.UnionWith(labels);
            }
        }
        foreach (var label in new[] { "Allergic to wasps", "Irritably-boweled", "Twat", "Goody two-shoes", "Princess", "Hippie", "Easy to overheat", "Alcoholic", "Rich", "Slow drinker", "Lightweight" })
            Assert.IsTrue(seen.Contains(label), label);
        var share = labelled * 100 / total;
        Assert.IsTrue(share is >= 35 and <= 85, $"{share}% of guests show a label.");
        Assert.AreEqual(0, GameSession.CreateBuildCampaign(20260922, FestivalStanding.Established).GuestLabels(1).Count, "Not a guest: no labels.");
    }

    [TestMethod]
    public void PrissinessScalesUnpleasantLossesBetweenHalfAndHalfAgain()
    {
        Assert.AreEqual(50, GuestCharacters.Unpleasant(100, 0));
        Assert.AreEqual(100, GuestCharacters.Unpleasant(100, 50));
        Assert.AreEqual(150, GuestCharacters.Unpleasant(100, 100));
        Assert.AreEqual(1, GuestCharacters.Unpleasant(1, 0), "Even a hippie notices.");
    }

    [TestMethod]
    public void SensitiveGuestsOverheatFasterInProportion()
    {
        var s = Started();
        var id = Guests(s).OrderByDescending(g => s.GuestCharacterOf(g).HeatSensitivity).First();
        var sensitivity = s.GuestCharacterOf(id).HeatSensitivity;
        var extra = 0;
        for (var k = 0; k < 2_000; k++) { SetTime(s, k * 4L); if (Call<bool>(s, "ExtraHeatThisTick", id)) extra++; }
        Assert.AreEqual(2_000 * sensitivity / 200, extra, 2);
    }

    [TestMethod]
    public void IbsAndSlowDrinkingShowInTheirNumbers()
    {
        var s = Started();
        var guests = Guests(s);
        foreach (var id in guests)
        {
            var c = s.GuestCharacterOf(id);
            Assert.AreEqual(c.Ibs ? ToiletRules.NeedGainEveryTicks / 2 : ToiletRules.NeedGainEveryTicks, Call<int>(s, "ToiletNeedGainEveryTicks", id));
            if (c.SlowDrinker) Assert.IsTrue(s.EffectiveMedicalDrinkThirstPerTickFor(id) <= GuestCharacters.SlowDrinkerThirstPerTick + 4, "Slower than any ordinary drinker.");
        }
    }

    [TestMethod]
    public void ALightweightGetsDrunkTwiceAsFastOnTheSameBeer()
    {
        GameSession? session = null; ulong light = 0, ordinary = 0;
        bool Drinker(GameSession s, ulong g) => s.CapturePerson(g) is { Admitted: true, Departed: false } &&
            s.CaptureImmersion()!.People.Single(p => p.AgentId == g) is { Abstains: false };
        for (var seed = 20260922UL; seed < 20260960UL && session is null; seed++)
        {
            var s = WithoutFaults(Started(seed));
            s.AdvanceWithoutSnapshot(4_000);
            light = Guests(s).FirstOrDefault(g => s.GuestCharacterOf(g).Lightweight && Drinker(s, g));
            ordinary = Guests(s).FirstOrDefault(g => !s.GuestCharacterOf(g).Lightweight && Drinker(s, g));
            if (light != 0 && ordinary != 0) session = s;
        }
        Assert.IsNotNull(session, "An admitted lightweight and an ordinary drinker within a few seeds.");
        Assert.IsTrue(session.GuestLabels(light).Contains("Lightweight") || session.GuestLabels(light).Count == GuestCharacters.MaximumLabels);
        // The same beer waiting to be absorbed, nothing eaten, sober to start.
        var mutate = typeof(GameSession).GetMethod("MutatePerson", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var id in new[] { light, ordinary })
            mutate.Invoke(session, [id, (Action<Person>)(p => { p.PendingDose = 2_000; p.Intoxication = 0; p.FoodProtectionTicks = 0; })]);
        session.AdvanceWithoutSnapshot(400);
        int Drunk(ulong id) => session.CaptureImmersion()!.People.Single(p => p.AgentId == id).Intoxication;
        Assert.IsTrue(Drunk(ordinary) > 200, $"The ordinary drinker felt it ({Drunk(ordinary)}).");
        Assert.IsTrue(Drunk(light) >= Drunk(ordinary) * 18 / 10, $"Lightweight {Drunk(light)} against ordinary {Drunk(ordinary)}.");
    }

    [TestMethod]
    public void AnAllergicGuestStungByAWaspCollapsesAndNeedsTheMedic()
    {
        GameSession? session = null; ulong victim = 0;
        for (var seed = 20260922UL; seed < 20260960UL && session is null; seed++)
        {
            var s = Started(seed);
            s.AdvanceWithoutSnapshot(4_000);
            var allergic = Guests(s).FirstOrDefault(g => s.GuestCharacterOf(g).WaspAllergy && s.CapturePerson(g) is { Admitted: true, Departed: false } &&
                s.CaptureMedical()!.Needs.Single(n => n.AgentId == g).Stage == MedicalStage.Clear);
            if (allergic != 0) { session = s; victim = allergic; }
        }
        Assert.IsNotNull(session, "An admitted allergic guest within a few seeds.");
        var start = session.CurrentTick;
        for (var second = 0; second < 200 && session.CaptureMedical()!.Needs.Single(n => n.AgentId == victim).Stage != MedicalStage.Collapsed; second++)
        {
            SetTime(session, start + second * 80L);
            Call<object?>(session, "MaybeWaspSting", victim);
        }
        var need = session.CaptureMedical()!.Needs.Single(n => n.AgentId == victim);
        Assert.AreEqual(MedicalStage.Collapsed, need.Stage);
        StringAssert.StartsWith(need.Reason, "Anaphylaxis");
        Assert.IsTrue(session.GuestLabels(victim).Contains("Allergic to wasps"));
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);

        // A medic is sent, which rewrites their status text, but arrives too late: the death is still a sting.
        var medical = session.CaptureMedical()!;
        var stungAt = session.CurrentTick - GameSession.MedicalDeathDelayTicks;
        typeof(GameSession).GetProperty("MedicalView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, medical with { Needs = medical.Needs
            .Select(n => n.AgentId == victim ? n with { Stage = MedicalStage.Critical, WarningTick = stungAt, CollapseTick = stungAt, CriticalTick = stungAt + GameSession.MedicalCriticalDelayTicks,
                Reason = "Awaiting physically dispatched medic" } : n).ToArray() });
        for (var guard = 0; guard < 4 && session.PreparedStatus != PreparationStatus.Failed; guard++) session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(PreparationStatus.Failed, session.PreparedStatus);
        StringAssert.Contains(session.CaptureMedical()!.Evidence.Last(e => e.Id == "medical:death").Description, "wasp sting");
    }
}

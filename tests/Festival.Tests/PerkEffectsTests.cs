using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class PerkEffectsTests
{
    private sealed record Day(int Beers, int Softs, int Taps, int DrunkSamples, long Satisfaction, int StaffPurchases, int StaffToiletVisits);

    private static readonly ulong[] Seeds = [20260922, 20260924, 20260925, 20260928];

    /// <summary>Two seeded days, with plenty of stock, totalled; and each must still restore.</summary>
    private static Day Days(string? perk)
    {
        var days = Seeds.AsParallel().Select(seed =>
        {
            var s = perk is null ? Planned(seed) : Planned(seed, perk);
            Accept(s, new SetPreparationStockCommand(60, 90, 90));
            foreach (var hire in Crew(s)) Accept(s, hire);
            Accept(s, new StartPreparedEditionCommand());
            var drunk = 0;
            for (var k = 0; k < 24; k++) { s.AdvanceWithoutSnapshot(800); drunk += s.CaptureImmersion()!.People.Count(p => p.Intoxication >= 5_000); }
            var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
            Assert.IsTrue(restored.IsSuccess, restored.Error);
            var staff = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Staff).Select(p => p.AgentId).ToHashSet();
            var m = s.CaptureImmersion()!;
            return new Day(m.Purchases.Count(p => p.Product == ImmersionProduct.Beer), m.Purchases.Count(p => p.Product == ImmersionProduct.SoftDrink),
                s.CaptureMedical()!.Evidence.Count(e => e.Id == "medical:water"), drunk,
                s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted).Sum(p => (long)p.Satisfaction),
                m.Purchases.Count(p => staff.Contains(p.AgentId)), m.People.Where(p => staff.Contains(p.AgentId)).Sum(p => p.ToiletVisits));
        }).ToArray();
        return new Day(days.Sum(d => d.Beers), days.Sum(d => d.Softs), days.Sum(d => d.Taps), days.Sum(d => d.DrunkSamples),
            days.Sum(d => d.Satisfaction), days.Sum(d => d.StaffPurchases), days.Sum(d => d.StaffToiletVisits));
    }

    [TestMethod]
    public void FriendlyQueuesCheerUpTheWaitingWithoutChangingWhatTheyDo()
    {
        var (before, after) = (Days(null), Days(PerkCatalogue.FriendlyQueues));
        Assert.AreEqual(before with { Satisfaction = 0 }, after with { Satisfaction = 0 }, "Same choices, same purchases.");
        Assert.IsTrue(after.Satisfaction > before.Satisfaction, $"{before.Satisfaction} to {after.Satisfaction}.");
    }

    [TestMethod]
    public void OwnBottlesMeanFewerTripsToTheTapButSlowerFills()
    {
        var s = Started(20260922, PerkCatalogue.BringYourOwnBottle);
        var guest = s.CapturePreparation()!.People.First(p => p.Role == ProtectedPersonRole.Guest).AgentId;
        var plain = Started(20260922);
        Assert.AreEqual(Math.Max(1, plain.EffectiveMedicalDrinkThirstPerTickFor(guest) * GameSession.ByobFillPercent / 100), s.EffectiveMedicalDrinkThirstPerTickFor(guest));
        Assert.AreEqual(Math.Max(1, s.EffectiveMedicalDrinkThirstPerTickFor(guest) / 4), s.EffectiveMedicalDrinkHeatPerTickFor(guest), "The same drink cools them the same amount.");
        var guests = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest).Select(p => p.AgentId).ToArray();
        var slow = guests.FirstOrDefault(g => s.GuestCharacterOf(g).SlowDrinker);
        Assert.AreNotEqual(0UL, slow, "A slow drinker on this roster.");
        Assert.IsTrue(guests.All(g => s.EffectiveMedicalDrinkHeatPerTickFor(g) >= 1), "Even a slow drinker filling a bottle is cooled.");
        var (before, after) = (Days(null), Days(PerkCatalogue.BringYourOwnBottle));
        Assert.IsTrue(after.Taps < before.Taps, $"Tap visits {before.Taps} to {after.Taps}.");
    }

    [TestMethod]
    public void ColaFiendsBuySoftDrinksInsteadOfQueuingForTheTap()
    {
        var (before, after) = (Days(null), Days(PerkCatalogue.ColaFiends));
        Assert.IsTrue(after.Softs > before.Softs, $"Soft drinks {before.Softs} to {after.Softs}.");
        Assert.IsTrue(after.Taps <= before.Taps * 11 / 10, $"No more tap visits: {before.Taps} to {after.Taps}.");
    }

    [TestMethod]
    public void AlcoholicsBuyMoreBeerAndGetDrunker()
    {
        var (before, after) = (Days(null), Days(PerkCatalogue.Alcoholics));
        Assert.IsTrue(after.Beers > before.Beers, $"Beers {before.Beers} to {after.Beers}.");
        Assert.IsTrue(after.DrunkSamples > before.DrunkSamples, $"Drunk {before.DrunkSamples} to {after.DrunkSamples}.");
    }

    [TestMethod]
    public void RobotWorkersNeverStopForThemselves()
    {
        var before = Days(null);
        Assert.IsTrue(before.StaffPurchases + before.StaffToiletVisits > 0, "Ordinary staff do take breaks.");
        var after = Days(PerkCatalogue.RobotWorkers);
        Assert.AreEqual(0, after.StaffPurchases + after.StaffToiletVisits);
        var s = Started(20260922, PerkCatalogue.RobotWorkers);
        s.AdvanceWithoutSnapshot(4_000);
        var staff = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Staff).Select(p => p.AgentId).ToArray();
        Assert.IsTrue(staff.All(id => s.CapturePerson(id) is { Thirst: 0, HeatExposure: 0, Hunger: 0, ToiletNeed: 0, StaffThirst: 0 }), "No needs of their own.");
    }
}

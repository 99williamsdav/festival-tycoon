using System.Reflection;
using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class StaffTraitTests
{
    /// <summary>The first seed from <paramref name="from"/> offering a candidate in the role with the trait.</summary>
    private static (ulong Seed, StaffCandidate Candidate) Find(StaffRole role, StaffTrait trait, ulong from = 20260922)
    {
        for (var seed = from; seed < from + 400; seed++)
            if (GameSession.CreateBuildCampaign(seed, FestivalStanding.Established).GetStaffCandidates()
                    .FirstOrDefault(c => c.Role == role && c.Has(trait)) is { } candidate) return (seed, candidate);
        throw new InvalidOperationException($"No {role} with {trait} near {from}.");
    }

    /// <summary>A started edition with the standard crew, except the given candidate in their role.</summary>
    private static (GameSession Session, ulong AgentId) StartedWith(StaffRole role, StaffTrait trait)
    {
        var (seed, candidate) = Find(role, trait);
        var s = Planned(seed);
        foreach (var id in CrewIds(s).Where(id => !id.StartsWith($"staff.{StaffCatalogue.Key(role)}.", StringComparison.Ordinal)).Append(candidate.Id))
            Accept(s, new AcceptPreparationOfferCommand(id));
        Accept(s, new StartPreparedEditionCommand());
        return (s, s.CapturePreparation()!.People.Single(person => person.Name == candidate.Name).AgentId);
    }

    private static void AssertRestores(GameSession s)
    {
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void AboutHalfTheMarketHasAQuirkAndOppositesNeverPair()
    {
        var all = Enumerable.Range(0, 200).SelectMany(offset => StaffCatalogue.Candidates(20260922UL + (ulong)offset, new(1_000, 1_000, 5_000, 5_000))).ToArray();
        Assert.IsTrue(all.Where(c => c.Grade == 0).Any(c => c.Traits.Length > 0), "The standard candidate can have traits too.");
        var share = all.Count(c => c.Traits.Length > 0) * 100 / all.Length;
        Assert.IsTrue(share is >= 40 and <= 60, $"{share}% have a trait.");
        Assert.IsTrue(all.All(c => c.Traits.Length <= 2 && c.WagePennies >= 500));
        Assert.IsFalse(all.Any(c => c.Has(StaffTrait.WeakBladder) && c.Has(StaffTrait.IronBladder) || c.Has(StaffTrait.Charismatic) && c.Has(StaffTrait.Abrasive)));
        Assert.IsTrue(all.All(c => c.Has(StaffTrait.Tardy) == c.LateTicks > 0 && c.LateTicks is 0 or (>= 30 * 80 and <= 90 * 80)));
        foreach (var trait in Enum.GetValues<StaffTrait>()) Assert.IsTrue(all.Any(c => c.Has(trait)), trait.ToString());
        Assert.AreEqual("Acts suspicious", StaffCatalogue.TraitLabel(StaffTrait.SneakyAlcoholic), "The alcoholic is only hinted at.");
    }

    [TestMethod]
    public void TraitsMoveTheWageByAPercentageThatMatchesTheirSign()
    {
        foreach (var trait in Enum.GetValues<StaffTrait>())
        {
            var percent = StaffCatalogue.TraitWagePercent(trait);
            Assert.AreEqual(StaffCatalogue.IsPositive(trait), percent > 0, trait.ToString());
            Assert.IsTrue(percent is >= -20 and <= 25 and not 0, trait.ToString());
        }
        var all = Enumerable.Range(0, 100).SelectMany(offset => StaffCatalogue.Candidates(20260922UL + (ulong)offset, new(1_000, 1_000, 5_000, 5_000))).ToArray();
        Assert.IsTrue(all.All(c => c.WagePennies % 100 == 0 && c.WagePennies >= 500), "Whole pounds, never below the floor.");
    }

    [TestMethod]
    public void ADodgyKneeCountsAgainstPace()
    {
        var plain = StaffCatalogue.Candidates(20260922, new(1_000, 1_000, 5_000, 5_000)).First(c => c.Role == StaffRole.Medic) with { WalkingSpeedPermille = 1_150 };
        Assert.IsTrue(StaffCatalogue.PaceRating(plain with { Traits = [StaffTrait.DodgyKnee] }) < StaffCatalogue.PaceRating(plain));
    }

    [TestMethod]
    public void ATardyStewardSetsOffOnlyAfterTheirDelay()
    {
        var (s, id) = StartedWith(StaffRole.Steward, StaffTrait.Tardy);
        var late = s.HiredCandidateFor(id)!.LateTicks;
        Assert.IsTrue(s.GuestWaitingForRelease(id), "Held off site at opening.");
        (int, int, int, int) Needs() => (s.CaptureMedical()!.Needs.Single(n => n.AgentId == id) is var m ? (m.Thirst, m.HeatExposure, 0, 0) : default) is var (thirst, heat, _, _) &&
            s.CaptureImmersion()!.People.Single(p => p.AgentId == id) is var c ? (thirst, heat, c.Hunger, c.ToiletNeed) : default;
        var before = Needs();
        s.AdvanceWithoutSnapshot(late - 80);
        Assert.IsTrue(s.GuestWaitingForRelease(id));
        Assert.AreEqual(before, Needs(), "Needs wait until they set off, as for late guests.");
        Assert.IsFalse(s.CapturePreparation()!.People.Single(p => p.AgentId == id).Admitted);
        AssertRestores(s);
        s.AdvanceWithoutSnapshot(160);
        Assert.IsFalse(s.GuestWaitingForRelease(id), "On the way once the delay has passed.");
    }

    [TestMethod]
    public void ASneakyAlcoholicGetsServedAndDrinkDullsTheirWork()
    {
        var (s, id) = StartedWith(StaffRole.Medic, StaffTrait.SneakyAlcoholic);
        var sober = s.GetResponseStaff().Single(p => p.AgentId == id).TreatmentTicks;
        var drank = false;
        for (var guard = 0; guard < 600 && !drank; guard++)
        {
            s.AdvanceWithoutSnapshot(80);
            drank = s.CaptureImmersion()!.Purchases.Any(p => p.AgentId == id && p.Product == ImmersionProduct.Beer);
        }
        Assert.IsTrue(drank, "The bar serves them despite the staff rule.");
        s.AdvanceWithoutSnapshot(2_400);
        AssertRestores(s);
        var intoxication = s.CaptureImmersion()!.People.Single(p => p.AgentId == id).Intoxication;
        Assert.IsTrue(intoxication > 0);
        Assert.IsTrue(s.GetResponseStaff().Single(p => p.AgentId == id).TreatmentTicks > sober, "Drunk treatment is slower.");
    }

    [TestMethod]
    public void BladdersFillFasterOrSlower()
    {
        int Gain(StaffTrait trait)
        {
            var (s, id) = StartedWith(StaffRole.Steward, trait);
            int Need() => s.CaptureImmersion()!.People.Single(p => p.AgentId == id).ToiletNeed;
            var before = Need(); s.AdvanceWithoutSnapshot(400);
            return Need() - before;
        }
        Assert.AreEqual(200, Gain(StaffTrait.WeakBladder));
        Assert.AreEqual(50, Gain(StaffTrait.IronBladder));
    }

    [TestMethod]
    public void CharismaLiftsNearbyGuestsAndAbrasionWearsThemDown()
    {
        foreach (var (trait, sign) in new[] { (StaffTrait.Charismatic, 1), (StaffTrait.Abrasive, -1) })
        {
            var (s, id) = StartedWith(StaffRole.Steward, trait);
            s.AdvanceWithoutSnapshot(4_000);
            var staff = s.CaptureObservation().NavigationAgents.Single(a => a.Id.Value == id);
            var near = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed &&
                s.CaptureObservation().NavigationAgents.Single(a => a.Id.Value == p.AgentId) is var guest &&
                Math.Pow(guest.XMillimetres - staff.XMillimetres, 2) + Math.Pow(guest.ZMillimetres - staff.ZMillimetres, 2) <= Math.Pow(GameSession.StaffPresenceRadiusMillimetres, 2))
                .Select(p => p.AgentId).ToHashSet();
            var before = s.CapturePreparation()!.People.ToDictionary(p => p.AgentId, p => p.Satisfaction);
            typeof(GameSession).GetMethod("ApplyStaffPresence", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, null);
            foreach (var person in s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest))
            {
                var expected = near.Contains(person.AgentId) ? Math.Clamp(before[person.AgentId] + sign * GameSession.StaffPresencePerSecond, 0, 10_000) : before[person.AgentId];
                Assert.AreEqual(expected, person.Satisfaction, $"{trait} guest {person.AgentId}");
            }
        }
    }

    [TestMethod]
    public void ASlackerVisitsTheVansMoreThanTheStandardCandidate()
    {
        int Purchases(GameSession s, ulong id)
        {
            for (var guard = 0; guard < 200 && s.PreparedStatus == PreparationStatus.Running; guard++) s.AdvanceWithoutSnapshot(400);
            return s.CaptureImmersion()!.Purchases.Count(p => p.AgentId == id);
        }
        var (slacker, slackerId) = StartedWith(StaffRole.Sound, StaffTrait.Slacker);
        var plain = Started(Find(StaffRole.Sound, StaffTrait.Slacker).Seed);
        var plainId = plain.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Staff).Min(p => p.AgentId);
        Assert.IsTrue(Purchases(slacker, slackerId) > Purchases(plain, plainId));
    }

    [TestMethod]
    public void ADrunkStewardWhoCollapsesDropsTheirJobAndStillSaves()
    {
        var (s, id) = StartedWith(StaffRole.Steward, StaffTrait.SneakyAlcoholic);
        s.AdvanceWithoutSnapshot(3_000);
        // Past the warning and close to twenty seconds above the collapse line.
        var immersion = s.CaptureImmersion()!;
        typeof(GameSession).GetProperty("ImmersionView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s, immersion with { People = immersion.People
            .Select(p => p.AgentId == id ? p with { Intoxication = 9_800, WarningTick = s.CurrentTick - 1_600, SevereTicks = 1_590 } : p).ToArray() });
        for (var guard = 0; guard < 80 && s.CaptureMedical()!.Needs.Single(n => n.AgentId == id).Stage != MedicalStage.Collapsed; guard++) s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalStage.Collapsed, s.CaptureMedical()!.Needs.Single(n => n.AgentId == id).Stage);
        var job = s.GetStewardResponses().Single(j => j.WorkerId == id);
        Assert.IsFalse(job.Incapacitated, "Drink, not a fight injury.");
        Assert.IsFalse(job.Stage is SecurityResponseStage.Travelling or SecurityResponseStage.Calming or SecurityResponseStage.Confronting);
        AssertRestores(s);
        s.AdvanceWithoutSnapshot(400);
        AssertRestores(s);
    }
}

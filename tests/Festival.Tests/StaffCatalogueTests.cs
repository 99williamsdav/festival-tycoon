using Festival.Simulation;
using static Festival.Tests.BuildSession;

namespace Festival.Tests;

[TestClass]
public sealed class StaffCatalogueTests
{
    [TestMethod]
    public void EachRoleOffersThreeNamedCandidatesWithAStandardOne()
    {
        var s = Planned();
        var candidates = s.GetStaffCandidates();
        foreach (var role in new[] { StaffRole.Sound, StaffRole.Medic, StaffRole.Steward })
            Assert.AreEqual(StaffCatalogue.PerRole, candidates.Count(c => c.Role == role), role.ToString());
        Assert.AreEqual(candidates.Count, candidates.Select(c => c.Name).Distinct().Count());
        Assert.AreEqual(candidates.Count, candidates.Select(c => c.Name.Split(' ')[0]).Distinct().Count(), "First names are unique so the staff page can use them.");
        Assert.IsTrue(candidates.All(c => c.Grade is >= -2 and <= 2 && c.WagePennies >= 800 && c.Traits.Length == 0 && c.Blurb.Length > 0));
        var standard = candidates.Single(c => c.Id == "staff.medic.1");
        Assert.AreEqual(0, standard.Grade);
        Assert.AreEqual(StaffCatalogue.UnhiredMedicTreatmentTicks, standard.TreatmentTicks);
        Assert.IsTrue(candidates.Single(c => c.Id == "staff.medic.3").TreatmentTicks < standard.TreatmentTicks);
        Assert.IsTrue(candidates.Single(c => c.Id == "staff.sound.3").MixingBonus > 0 && candidates.Single(c => c.Id == "staff.sound.2").MixingBonus < 0);
    }

    [TestMethod]
    public void CandidatesAreFixedForASeedAndVaryBetweenSeeds()
    {
        var first = Planned(20260922).GetStaffCandidates().Select(c => c.Name).ToArray();
        CollectionAssert.AreEqual(first, Planned(20260922).GetStaffCandidates().Select(c => c.Name).ToArray());
        CollectionAssert.AreNotEqual(first, Planned(20260923).GetStaffCandidates().Select(c => c.Name).ToArray());
    }

    [TestMethod]
    public void OpeningNeedsASoundEngineerAMedicAndASteward()
    {
        var s = Planned();
        Accept(s, new AcceptPreparationOfferCommand("staff.sound.2"));
        var blockers = s.GetPreparationStartRequirements().Where(r => !r.Complete).Select(r => r.Id).ToArray();
        CollectionAssert.IsSubsetOf(new[] { "medic", "steward" }, blockers);
        Assert.IsFalse(Send(s, new StartPreparedEditionCommand()).IsAccepted);
        Accept(s, new AcceptPreparationOfferCommand("staff.medic.3"));
        Accept(s, new AcceptPreparationOfferCommand("staff.steward.2"));
        Accept(s, new StartPreparedEditionCommand());
    }

    [TestMethod]
    public void HiredCandidatesNameTheirSlotAndSetTheirAbilities()
    {
        var s = Planned();
        foreach (var id in new[] { "staff.sound.3", "staff.medic.3", "staff.steward.2" }) Accept(s, new AcceptPreparationOfferCommand(id));
        Accept(s, new StartPreparedEditionCommand());
        var candidates = s.GetStaffCandidates();
        var medic = candidates.Single(c => c.Id == "staff.medic.3");
        var steward = candidates.Single(c => c.Id == "staff.steward.2");
        var people = s.CapturePreparation()!.People;
        Assert.IsTrue(people.Any(p => p.Name == candidates.Single(c => c.Id == "staff.sound.3").Name));
        var medicProfile = s.GetResponseStaff().Single(p => p.Role == ResponseRole.Medic);
        Assert.AreEqual(medic.Name, medicProfile.Name);
        Assert.AreEqual(people.Single(p => p.AgentId == medicProfile.AgentId).Name, medic.Name);
        var stewardProfile = s.GetResponseStaff().Single(p => p.Role == ResponseRole.Steward);
        Assert.AreEqual(steward.Name, stewardProfile.Name);
        Assert.IsFalse(people.Any(p => p.Name.Contains("(unhired)")));
        Assert.IsTrue(s.CapturePreparation()!.Contacts.Contains("contact.medic.3"));
        var restored = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
    }

    [TestMethod]
    public void ACandidateCannotFillBothTheMainAndTheExtraSlot()
    {
        var s = PlannedWith("doctors-orders");
        Accept(s, new AcceptPreparationOfferCommand("staff.medic.2"));
        Assert.IsFalse(Send(s, new AcceptPreparationOfferCommand("staff.extra-medic.2")).IsAccepted);
        Accept(s, new AcceptPreparationOfferCommand("staff.extra-medic.3"));
        Assert.IsFalse(Send(s, new AcceptPreparationOfferCommand("staff.medic.3")).IsAccepted);
    }
}

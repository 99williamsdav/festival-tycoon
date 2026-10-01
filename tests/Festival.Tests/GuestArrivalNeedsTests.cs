using Festival.Simulation;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class GuestArrivalNeedsTests
{
    private static void Accept(GameSession s, SessionCommand command)
    {
        var result=s.Execute(new(new(s.NextSubmissionSequence+1),s.CampaignId,s.Phase,s.CurrentTick,s.NextSubmissionSequence,null,command));
        Assert.IsTrue(result.IsAccepted,result.Message);
    }
    private static GameSession Ready(ulong seed)
    {
        var s=GameSession.CreateBuildCampaign(seed, FestivalStanding.Established);
        var perk=s.CapturePerks()!;Accept(s,new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,perk.Hand[0]));
        Accept(s,new UseDefaultBuildLayoutCommand());
        Accept(s,new SetProgrammeCommand(["act.meadow-lanterns","act.barnstorm-circuit","act.neon-postcards"]));
        foreach (var hire in BuildSession.Crew(s)) Accept(s, hire);
        return s;
    }
    private static GameSession Restore(GameSession s)
    {
        var r=GameSession.Restore(s.CapturePersistenceSnapshot());Assert.IsTrue(r.IsSuccess,r.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,r.Session!.CaptureSnapshot().AuthoritativeHash);return r.Session;
    }
    [TestMethod]
    public void FreshSeedsProduceVariedBoundedNeedsAndGuestOnlyReleaseBeforeFirstBand()
    {
        var signatures=new HashSet<string>();
        foreach(var seed in new ulong[]{20260922,20260929,41241})
        {
            var s=Ready(seed);var prep=s.CapturePreparation()!;var med=s.CaptureMedical()!;var immersion=s.CaptureImmersion()!;
            var guests=prep.People.Where(p=>p.Role==ProtectedPersonRole.Guest).ToArray();
            var releases=guests.Select(p=>GameSession.GuestReleaseTick(seed,p.AgentId)).ToArray();
            Assert.IsTrue(releases.All(t=>t is >=0 and <=4400 && t<GameSession.FestivalSlotStarts[0]));
            Assert.IsTrue(releases.Distinct().Count()>=10,"Guest entry must be visibly staggered.");
            Assert.IsTrue(guests.Select(p=>GameSession.GuestOpeningHunger(seed,p.AgentId)).Distinct().Count()>=15);
            Assert.IsTrue(guests.Select(p=>GameSession.GuestOpeningToiletNeed(seed,p.AgentId)).Distinct().Count()>=15);
            Assert.IsTrue(guests.Select(p=>GameSession.GuestOpeningThirst(seed,p.AgentId)).Distinct().Count()>=15);
            foreach(var person in guests)
            {
                var food=immersion.People.Single(p=>p.AgentId==person.AgentId);var need=med.Needs.Single(p=>p.AgentId==person.AgentId);
                Assert.IsTrue(food.Hunger is >=1500 and <=5500 && food.ToiletNeed is >=1200 and <=5200);
                Assert.AreEqual(GameSession.GuestOpeningHunger(seed,person.AgentId),food.Hunger);
                Assert.AreEqual(GameSession.GuestOpeningToiletNeed(seed,person.AgentId),food.ToiletNeed);
                Assert.AreEqual(GameSession.GuestOpeningThirst(seed,person.AgentId),need.Thirst);
                Assert.AreEqual(2500,need.HeatExposure);
                Assert.AreEqual(0,food.Intoxication);Assert.IsNull(food.Held);
            }
            var twin=Ready(seed);
            Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,twin.CaptureSnapshot().AuthoritativeHash);
            signatures.Add(string.Join(',',releases)+"|"+string.Join(',',guests.Select(p=>GameSession.GuestOpeningThirst(seed,p.AgentId))));
            Accept(s,new StartPreparedEditionCommand());
            var nonGuests=s.CapturePreparation()!.People.Where(p=>p.Role!=ProtectedPersonRole.Guest).ToArray();
            // Everyone but guests sets off at opening, except a tardy hire, who is held back like a late guest.
            Assert.IsTrue(nonGuests.All(p=>s.StaffHas(p.AgentId,StaffTrait.Tardy)
                ? s.GuestWaitingForRelease(p.AgentId)
                : s.CaptureSnapshot().NavigationAgents.Single(n=>n.Id.Value==p.AgentId).Destination is not null));
            Assert.AreEqual(20,s.CapturePreparation()!.People.Count(p=>p.Role==ProtectedPersonRole.Guest));
            Restore(s);
        }
        Assert.AreEqual(3,signatures.Count,"Different seeds should change the arrivals and needs.");
    }

    [TestMethod]
    public void PendingGuestStaysOffsiteThenWalksFromGateAndCountsOnlyAfterAdmissionAcrossRestore()
    {
        const ulong seed=20260929;
        var s=Ready(seed);Accept(s,new StartPreparedEditionCommand());
        var prep=s.CapturePreparation()!;
        var target=prep.People.Where(p=>p.Role==ProtectedPersonRole.Guest).MaxBy(p=>GameSession.GuestReleaseTick(seed,p.AgentId))!;
        var due=GameSession.GuestReleaseTick(seed,target.AgentId);Assert.IsTrue(due>=2400);
        var openingNeed=s.CaptureMedical()!.Needs.Single(n=>n.AgentId==target.AgentId);
        var openingFood=s.CaptureImmersion()!.People.Single(n=>n.AgentId==target.AgentId);
        var openingCash=s.CaptureSnapshot().Wallets.Single(w=>w.OwnerId.Value==target.AgentId).CashPennies;
        Assert.IsTrue(s.GuestWaitingForRelease(target.AgentId));
        s.AdvanceWithoutSnapshot(due-1);
        Assert.IsTrue(s.GuestWaitingForRelease(target.AgentId));
        Assert.IsFalse(s.CapturePreparation()!.People.Single(p=>p.AgentId==target.AgentId).Admitted);
        Assert.IsNull(s.CaptureSnapshot().NavigationAgents.Single(n=>n.Id.Value==target.AgentId).Destination);
        Assert.AreEqual(openingNeed.Thirst,s.CaptureMedical()!.Needs.Single(n=>n.AgentId==target.AgentId).Thirst);
        Assert.AreEqual(openingNeed.HeatExposure,s.CaptureMedical()!.Needs.Single(n=>n.AgentId==target.AgentId).HeatExposure);
        Assert.AreEqual(openingFood.Hunger,s.CaptureImmersion()!.People.Single(n=>n.AgentId==target.AgentId).Hunger);
        Assert.AreEqual(openingFood.ToiletNeed,s.CaptureImmersion()!.People.Single(n=>n.AgentId==target.AgentId).ToiletNeed);
        Assert.AreEqual(openingCash,s.CaptureSnapshot().Wallets.Single(w=>w.OwnerId.Value==target.AgentId).CashPennies);
        Assert.IsNull(s.CaptureImmersion()!.People.Single(n=>n.AgentId==target.AgentId).VendorId);
        var twin=Restore(s);
        s.AdvanceWithoutSnapshot(1);twin.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,twin.CaptureSnapshot().AuthoritativeHash);
        Assert.IsFalse(s.GuestWaitingForRelease(target.AgentId));
        var route=s.CaptureSnapshot().NavigationAgents.Single(n=>n.Id.Value==target.AgentId);
        Assert.AreEqual("edition.arrival",route.IntentId);
        Assert.AreEqual(AgentNavigationAction.Travelling,route.Action);
        Assert.IsFalse(s.CapturePreparation()!.People.Single(p=>p.AgentId==target.AgentId).Admitted);
        var gate=route;
        for(var i=0;i<2400 && !s.CapturePreparation()!.People.Single(p=>p.AgentId==target.AgentId).Admitted;i++)
        { s.AdvanceWithoutSnapshot(1);twin.AdvanceWithoutSnapshot(1); }
        Assert.IsTrue(s.CapturePreparation()!.People.Single(p=>p.AgentId==target.AgentId).Admitted,"Released guest must physically complete their route.");
        Assert.AreNotEqual(gate.ZMillimetres,s.CaptureSnapshot().NavigationAgents.Single(n=>n.Id.Value==target.AgentId).ZMillimetres);
        Assert.AreEqual(s.CapturePreparation()!.People.Count(p=>p.Role==ProtectedPersonRole.Guest && p.Admitted && !p.Departed),s.OnSiteAttendeeCount);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,twin.CaptureSnapshot().AuthoritativeHash);
        Restore(s);
        s.AdvanceWithoutSnapshot(400);
        var after=s.CaptureMedical()!.Needs.Single(n=>n.AgentId==target.AgentId);
        Assert.IsTrue(after.Thirst>openingNeed.Thirst || after.Intent is MedicalIntent.SeekWater or MedicalIntent.Drinking ||
            s.CaptureImmersion()!.People.Single(n=>n.AgentId==target.AgentId).VendorId is not null ||
            s.CaptureImmersion()!.Purchases.Any(p=>p.AgentId==target.AgentId),"Admitted guest must enter normal need/service decisions.");
    }

    [TestMethod]
    public void SameSeedRetryRebuildsTheGuestScheduleAndOpeningNeeds()
    {
        const ulong seed=20260929;
        var s=Ready(seed);var initialMedical=s.CaptureMedical()!;var initialImmersion=s.CaptureImmersion()!;
        Accept(s,new StartPreparedEditionCommand());s.AdvanceWithoutSnapshot(800);
        typeof(GameSession).GetMethod("RetryPreparedWeekend",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(s,null);
        Assert.AreEqual(PreparationStatus.Preparing,s.PreparedStatus);
        Assert.AreEqual(0,s.OnSiteAttendeeCount);
        Assert.IsTrue(s.CaptureMedical()!.Needs.Where(n=>n.Profile==MedicalNeedProfile.Guest).SequenceEqual(initialMedical.Needs.Where(n=>n.Profile==MedicalNeedProfile.Guest)));
        Assert.IsTrue(s.CaptureImmersion()!.People.Where(p=>s.CapturePreparation()!.People.Any(x=>x.AgentId==p.AgentId && x.Role==ProtectedPersonRole.Guest))
            .SequenceEqual(initialImmersion.People.Where(p=>s.CapturePreparation()!.People.Any(x=>x.AgentId==p.AgentId && x.Role==ProtectedPersonRole.Guest))));
    }
}

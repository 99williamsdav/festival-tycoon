using Festival.Simulation;
using Festival.ContentAdapter;
using System.Collections;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class PlaytestCorrectionsTests
{
    private static void Set(GameSession s, string field, object value) => SetMember(typeof(GameSession), field, BindingFlags.Instance | BindingFlags.NonPublic, s, value);
    private static object? Call(GameSession s, string method, params object[] args) => typeof(GameSession).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, args);
    private static void Send(GameSession s, SessionCommand command)
    { var result=s.Execute(new(new(s.NextSubmissionSequence+1),s.CampaignId,s.Phase,s.CurrentTick,s.NextSubmissionSequence,null,command)); Assert.IsTrue(result.IsAccepted,result.Message); }
    private static GameSession Open()
    {
        var s=BuildSession.Planned(20260929);
        Send(s,new AcceptPreparationOfferCommand("staff.steward"));
        Send(s,new AcceptPreparationOfferCommand("maintenance.worker"));
        Send(s,new StartPreparedEditionCommand());
        var p=s.CapturePreparation()!; Set(s,"PreparationView",p with { People=p.People.Select(person=>person with { Admitted=true }).ToArray() });
        return s;
    }
    private static void Position(GameSession s, ulong id, GridCell cell)
    {
        var agents=(IDictionary)typeof(GameSession).GetField("_navigationAgents",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(s)!;
        var nav=agents[new EntityId(id)]!; var c=TraversalGrid.CellCentre(cell);
        void Put(string name,object value)=>nav.GetType().GetProperty(name)!.SetValue(nav,value);
        Put("XMillimetres",c.XMillimetres);Put("ZMillimetres",c.ZMillimetres);Put("SegmentOriginXMillimetres",c.XMillimetres);Put("SegmentOriginZMillimetres",c.ZMillimetres);
        Put("Route",new List<GridCell>());Put("RouteIndex",0);Put("SegmentProgressMicrometres",0);Put("Destination",cell);Put("Action",AgentNavigationAction.Arrived);Put("IntentId","labelled.playtest-position");
    }
    private static GameSession Restore(GameSession s)
    { var r=GameSession.Restore(s.CapturePersistenceSnapshot()); Assert.IsTrue(r.IsSuccess,r.Error); Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,r.Session!.CaptureSnapshot().AuthoritativeHash);return r.Session; }

    [TestMethod]
    [DataRow(ProtectedPersonRole.Guest,ImmersionProduct.Chips)]
    [DataRow(ProtectedPersonRole.Guest,ImmersionProduct.SoftDrink)]
    [DataRow(ProtectedPersonRole.Guest,ImmersionProduct.Beer)]
    [DataRow(ProtectedPersonRole.Staff,ImmersionProduct.Chips)]
    [DataRow(ProtectedPersonRole.Staff,ImmersionProduct.SoftDrink)]
    [DataRow(ProtectedPersonRole.Performer,ImmersionProduct.Chips)]
    [DataRow(ProtectedPersonRole.Performer,ImmersionProduct.SoftDrink)]
    [DataRow(ProtectedPersonRole.Performer,ImmersionProduct.Beer)]
    public void RolePriceIsChargedOnceAndLedgerStockWalletRestoreAgree(ProtectedPersonRole role,ImmersionProduct product)
    {
        var s=Open();var before=s.CaptureImmersion()!;
        var person=s.CapturePreparation()!.People.First(p=>p.Role==role && (product!=ImmersionProduct.Beer || !before.People.Single(i=>i.AgentId==p.AgentId).Abstains));
        var cash=s.CaptureSnapshot().Wallets.Single(w=>w.OwnerId.Value==person.AgentId).CashPennies;
        var revenue=s.CaptureSnapshot().FestivalFinances.Single().CashPennies;
        Call(s,"CompleteImmersionSale",person.AgentId,product);
        var after=s.CaptureImmersion()!;var sale=after.Purchases.Single();var expected=GameSession.ImmersionPrice(product)/(role==ProtectedPersonRole.Guest?1:2);
        Assert.AreEqual(expected,sale.PricePennies);Assert.AreEqual(expected,s.ImmersionPriceFor(person.AgentId,product));
        Assert.AreEqual(cash-expected,s.CaptureSnapshot().Wallets.Single(w=>w.OwnerId.Value==person.AgentId).CashPennies);
        Assert.AreEqual(revenue+expected,s.CaptureSnapshot().FestivalFinances.Single().CashPennies);
        Assert.AreEqual(0L,sale.Entries.Sum(e=>e.AmountPennies));
        Assert.AreEqual(1,before.ChipsStock+before.SoftStock+before.BeerStock-after.ChipsStock-after.SoftStock-after.BeerStock);
        var restored=Restore(s); Assert.AreEqual(expected,restored.CaptureImmersion()!.Purchases.Single().PricePennies);
        Assert.AreEqual(after.People.Single(p=>p.AgentId==person.AgentId).Held,restored.CaptureImmersion()!.People.Single(p=>p.AgentId==person.AgentId).Held);
    }

    [TestMethod]
    public void DiscountedAffordabilityUsesActualPriceAndStaffBeerRefusalIsUnchanged()
    {
        var s=Open();var wallets=(IDictionary)typeof(GameSession).GetField("_wallets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(s)!;
        foreach(var role in Enum.GetValues<ProtectedPersonRole>())
        {
            var person=s.CapturePreparation()!.People.First(p=>p.Role==role && !s.IsCurrentProgrammePerformer(p.AgentId));
            var wallet=wallets[new EntityId(person.AgentId)]!;
            var price=s.ImmersionPriceFor(person.AgentId,ImmersionProduct.SoftDrink);
            wallet.GetType().GetProperty("CashPennies")!.SetValue(wallet,(long)price);
            var immersion=s.CapturePerson(person.AgentId)!;
            Assert.IsTrue((bool)Call(s,"ImmersionOrderEligible",immersion,ImmersionProduct.SoftDrink)!);
            wallet.GetType().GetProperty("CashPennies")!.SetValue(wallet,(long)price-1);
            Assert.IsFalse((bool)Call(s,"ImmersionOrderEligible",immersion,ImmersionProduct.SoftDrink)!);
            if(role==ProtectedPersonRole.Staff) Assert.IsFalse((bool)Call(s,"ImmersionOrderEligible",immersion,ImmersionProduct.Beer)!);
        }
    }

    [TestMethod]
    public void StaffPurchasesReturnToMovedAssignedPostsAndConsumeOnlyOnArrivalAcrossRestore()
    {
        var s=Open();
        foreach(var person in s.CapturePreparation()!.People.Where(p=>p.Role==ProtectedPersonRole.Staff))
        {
            var post=s.StaffAssignedPost(person.AgentId)!.Value;
            var response=s.GetResponseStaff().SingleOrDefault(p=>p.AgentId==person.AgentId);
            if(response is not null) Assert.AreEqual(GameSession.ResponsePostHome(s.CapturePreparation(),response.Role),post);
            Position(s,person.AgentId,new(120,164));Call(s,"CompleteImmersionSale",person.AgentId,ImmersionProduct.SoftDrink);
            Assert.AreEqual(post,s.CaptureSnapshot().NavigationAgents.Single(n=>n.Id.Value==person.AgentId).Destination);
            Assert.IsFalse(s.ImmersionConsumptionEligible(person.AgentId));
            s=Restore(s);Position(s,person.AgentId,post);Assert.IsTrue(s.ImmersionConsumptionEligible(person.AgentId));
            Call(s,"AdvanceImmersion");Assert.AreEqual(1,s.CaptureImmersion()!.People.Single(p=>p.AgentId==person.AgentId).Held!.ConsumedTicks);
            Position(s,person.AgentId,new(120,164));Assert.IsFalse(s.ImmersionConsumptionEligible(person.AgentId));
            Call(s,"ReturnToListening",person.AgentId);Assert.AreEqual(post,s.CaptureSnapshot().NavigationAgents.Single(n=>n.Id.Value==person.AgentId).Destination);
            Position(s,person.AgentId,post);Call(s,"AdvanceImmersion");Assert.AreEqual(2,s.CaptureImmersion()!.People.Single(p=>p.AgentId==person.AgentId).Held!.ConsumedTicks);
        }
        Assert.IsNull(s.StaffAssignedPost(s.CapturePreparation()!.People.First(p=>p.Role==ProtectedPersonRole.Performer).AgentId));
    }

    [TestMethod]
    public void AttendeesCountPhysicalGuestPresenceIncludingCollapseWithoutChangingSafetyRoster()
    {
        var s=Open();var p=s.CapturePreparation()!;var guests=p.People.Where(p=>p.Role==ProtectedPersonRole.Guest).ToArray();
        Assert.AreEqual(guests.Length,s.OnSiteAttendeeCount);
        Set(s,"PreparationView",p with {People=p.People.Select(person=>person.AgentId==guests[0].AgentId?person with {Admitted=false}:person.AgentId==guests[1].AgentId?person with {Departed=true}:person).ToArray()});
        Assert.AreEqual(guests.Length-2,s.OnSiteAttendeeCount);Assert.AreEqual(p.People.Length,s.CapturePreparation()!.People.Length);
        Set(s,"PreparationView",p); var medical=s.CaptureMedical()!;
        Set(s,"MedicalView",medical with {Needs=medical.Needs.Select(n=>n.AgentId==guests[0].AgentId?n with {Stage=MedicalStage.Collapsed,Intent=MedicalIntent.Collapsed}:n).ToArray()});
        Assert.AreEqual(guests.Length,s.OnSiteAttendeeCount);
        Set(s,"MedicalView",medical);Assert.AreEqual(guests.Length,Restore(s).OnSiteAttendeeCount);
    }

    [TestMethod]
    public void AlertsDeduplicatePrioritizeBoundFadeAndDoNotRefreshOnRepeatedObservation()
    {
        var display=new UrgentAlertDisplay();var alerts=Enumerable.Range(0,9).Select(i=>new UrgentAlert($"person:{i}",$"Alert {i}",i)).ToArray();
        display.Observe(alerts.Append(alerts[8] with {Priority=1}));Assert.AreEqual(4,display.Visible().Length);Assert.AreEqual("person:8",display.Visible()[0].Alert.Id);
        display.Advance(12);display.Observe(alerts);display.Advance(1);Assert.AreEqual(.5f,display.Visible()[0].Opacity,.001f);
        display.Advance(1);Assert.AreEqual(0,display.Visible().Length);
        display.ShowNextPage();Assert.AreEqual(4,display.Visible().Length);Assert.AreEqual("person:4",display.Visible()[0].Alert.Id);
        display.Observe(alerts.Append(new UrgentAlert("critical", "New critical emergency", 100)));
        Assert.AreEqual("critical", display.Visible()[0].Alert.Id, "New emergencies preempt a paged lower-priority group.");
        display.Observe([]);display.Advance(.5);Assert.AreEqual(.5f,display.Visible()[0].Opacity,.001f);display.Advance(.5);Assert.AreEqual(0,display.Visible().Length);
        display.Observe([alerts[0]]);Assert.AreEqual(1,display.Visible().Length);display.Reset();Assert.AreEqual(0,display.Visible().Length);
    }

}

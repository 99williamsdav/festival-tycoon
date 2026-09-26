using Festival.Simulation;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class FinanceFeedbackTests
{
    [TestMethod]
    public void CurrencyFormattingOmitsOnlyRoundPoundDecimals()
    {
        Assert.AreEqual("+£3", FestivalCurrency.Format(300, signed: true));
        Assert.AreEqual("−£3", FestivalCurrency.Format(-300, signed: true));
        Assert.AreEqual("+£3.50", FestivalCurrency.Format(350, signed: true));
        Assert.AreEqual("−£0.60", FestivalCurrency.Format(-60, signed: true));
        Assert.AreEqual("£0", FestivalCurrency.Format(0, signed: true));
        Assert.AreEqual("£800", FestivalCurrency.Format(80000));
        Assert.AreEqual("£3.50", FestivalCurrency.Format(350));
    }
    private static CommandResult Send(GameSession s,SessionCommand c)=>s.Execute(new(new(s.NextSubmissionSequence+1),s.CampaignId,s.Phase,s.CurrentTick,s.NextSubmissionSequence,null,c));
    [TestMethod]
    public void SuccessfulExpensesEmitOnceRejectedCommandsNeverEmitAndHashIsUntouched()
    {
        var s=GameSession.CreateImmersionCampaign(20260926);var cursor=new FestivalCashFeedbackCursor();cursor.Reset(s);
        Assert.IsTrue(Send(s,new PurchaseImmersionStarterStockCommand()).IsAccepted);Assert.IsTrue(Send(s,new AcceptPreparationOfferCommand("staff.steward")).IsAccepted);
        var hash=s.CaptureSnapshot().AuthoritativeHash;var events=cursor.Observe(s);Assert.AreEqual(2,events.Count);Assert.IsTrue(events.All(e=>e.FestivalCashPennies<0));Assert.AreEqual(-9600L,events.Single(e=>e.AnchorKey=="stock").FestivalCashPennies);Assert.IsTrue(events.Any(e=>e.AnchorKey=="offer:staff.steward"));
        Assert.AreEqual(0,cursor.Observe(s).Count);Assert.AreEqual(hash,s.CaptureSnapshot().AuthoritativeHash);
        Assert.IsFalse(Send(s,new PurchaseImmersionStarterStockCommand()).IsAccepted);Assert.IsFalse(Send(s,new AcceptPreparationOfferCommand("missing.offer")).IsAccepted);Assert.AreEqual(0,cursor.Observe(s).Count);
    }
    [TestMethod]
    public void SameTickSalesProjectFestivalCashNotBuyerDebitOrCostAndLoadResetsHistory()
    {
        var s=GameSession.CreateImmersionCampaign(20260926);Assert.IsTrue(Send(s,new PurchaseImmersionStarterStockCommand()).IsAccepted);
        Assert.IsTrue(Send(s,new SetProgrammeCommand(["act.meadow-lanterns","act.barnstorm-circuit","act.neon-postcards"])).IsAccepted);
        Assert.IsTrue(Send(s,new AcceptPreparationOfferCommand("staff.steward")).IsAccepted);Assert.IsTrue(Send(s,new StartPreparedEditionCommand()).IsAccepted);
        var cursor=new FestivalCashFeedbackCursor();cursor.Reset(s);var people=s.CaptureImmersion()!.People.Take(2).ToArray();
        var sale=typeof(GameSession).GetMethod("CompleteImmersionSale",BindingFlags.NonPublic|BindingFlags.Instance)!;
        sale.Invoke(s,[people[0].AgentId,ImmersionProduct.Chips]);sale.Invoke(s,[people[1].AgentId,ImmersionProduct.SoftDrink]);
        var events=cursor.Observe(s);Assert.AreEqual(2,events.Count);Assert.AreEqual(500L,events.Sum(e=>e.FestivalCashPennies));Assert.AreEqual(300L,events.Single(e=>e.AnchorKey=="vendor.food").FestivalCashPennies);Assert.AreEqual(200L,events.Single(e=>e.AnchorKey=="vendor.drinks").FestivalCashPennies);Assert.AreEqual(events[0].Tick,events[1].Tick);Assert.AreEqual(0,cursor.Observe(events.Concat(events)).Count);
        var restored=GameSession.Restore(s.CapturePersistenceSnapshot());Assert.IsTrue(restored.IsSuccess,restored.Error);Assert.AreEqual(0,cursor.Observe(restored.Session!).Count);var cold=new FestivalCashFeedbackCursor();cold.Reset(restored.Session!);Assert.AreEqual(0,cold.Observe(restored.Session!).Count);
        sale.Invoke(restored.Session,[restored.Session!.CaptureImmersion()!.People.Skip(2).First().AgentId,ImmersionProduct.SoftDrink]);Assert.AreEqual(200L,cold.Observe(restored.Session).Single().FestivalCashPennies);
    }
    [TestMethod]
    public void SuccessfulClonedCommandSessionStillEmitsRatherThanSilentlyResetting()
    {
        var s=GameSession.CreateImmersionCampaign(20260926);var cursor=new FestivalCashFeedbackCursor();cursor.Reset(s);
        var clone=GameSession.Restore(s.CapturePersistenceSnapshot()).Session!;Assert.IsTrue(Send(clone,new PurchaseImmersionStarterStockCommand()).IsAccepted);
        Assert.AreEqual(-9600L,cursor.Observe(clone).Single().FestivalCashPennies);Assert.AreEqual(0,cursor.Observe(clone).Count);
        var loaded=GameSession.Restore(clone.CapturePersistenceSnapshot()).Session!;cursor.Reset(loaded);Assert.AreEqual(0,cursor.Observe(loaded).Count);
    }
    [TestMethod]
    public void TransactionIdentityScopesCampaignAttemptAndPaymentId()
    {
        var s=GameSession.CreateImmersionCampaign(20260926);Assert.IsTrue(Send(s,new PurchaseImmersionStarterStockCommand()).IsAccepted);Assert.IsTrue(Send(s,new AcceptPreparationOfferCommand("staff.steward")).IsAccepted);
        var prep=s.CapturePreparation()!;var immersion=s.CaptureImmersion()!;var cursor=new FestivalCashFeedbackCursor();var original=FestivalCashFeedbackProjection.Capture(s.CampaignId.Value,prep,immersion);cursor.Reset(original);
        var retry=FestivalCashFeedbackProjection.Capture(s.CampaignId.Value,prep with { Attempt=2,Payments=prep.Payments.Select(p=>p with { Attempt=2 }).ToArray() },immersion with { StockPurchase=immersion.StockPurchase! with { Attempt=2 } });Assert.AreEqual(2,cursor.Observe(retry).Count);Assert.AreEqual(0,cursor.Observe(retry).Count);
        Assert.AreEqual(2,cursor.Observe(FestivalCashFeedbackProjection.Capture(s.CampaignId.Value+1,prep,immersion)).Count);
    }
}

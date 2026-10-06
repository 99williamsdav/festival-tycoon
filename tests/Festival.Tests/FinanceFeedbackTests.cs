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
    public void SameTickSalesProjectFestivalCashNotBuyerDebitOrCostAndLoadResetsHistory()
    {
        var s=BuildSession.Drafted(20260926);Assert.IsTrue(Send(s,new SetPreparationStockCommand(40, 32)).IsAccepted);
        Assert.IsTrue(Send(s,new SetProgrammeCommand(["act.meadow-lanterns","act.overdue-library-books","act.glitter-rota"])).IsAccepted);
        foreach (var hire in BuildSession.Crew(s)) Assert.IsTrue(Send(s, hire).IsAccepted);Assert.IsTrue(Send(s,new StartPreparedEditionCommand()).IsAccepted);
        var cursor=new FestivalCashFeedbackCursor();cursor.Reset(s);var people=s.CaptureImmersion()!.People.Take(2).ToArray();
        var sale=typeof(GameSession).GetMethod("CompleteImmersionSale",BindingFlags.NonPublic|BindingFlags.Instance)!;
        sale.Invoke(s,[people[0].AgentId,ImmersionProduct.Chips]);sale.Invoke(s,[people[1].AgentId,ImmersionProduct.SoftDrink]);
        // The chips are the food trader's takings: only the bar's sale moves festival cash.
        var events=cursor.Observe(s);Assert.AreEqual(1,events.Count);Assert.AreEqual(250L,events.Single(e=>e.AnchorKey=="vendor.drinks").FestivalCashPennies);Assert.IsFalse(events.Any(e=>e.AnchorKey=="vendor.food"));Assert.AreEqual(0,cursor.Observe(events.Concat(events)).Count);
        var restored=GameSession.Restore(s.CapturePersistenceSnapshot());Assert.IsTrue(restored.IsSuccess,restored.Error);Assert.AreEqual(0,cursor.Observe(restored.Session!).Count);var cold=new FestivalCashFeedbackCursor();cold.Reset(restored.Session!);Assert.AreEqual(0,cold.Observe(restored.Session!).Count);
        sale.Invoke(restored.Session,[restored.Session!.CaptureImmersion()!.People.Skip(2).First().AgentId,ImmersionProduct.SoftDrink]);Assert.AreEqual(250L,cold.Observe(restored.Session).Single().FestivalCashPennies);
    }
}

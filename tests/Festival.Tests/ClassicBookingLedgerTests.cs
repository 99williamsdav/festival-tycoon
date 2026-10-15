using System.Text.Json;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class ClassicBookingLedgerTests
{
    private static readonly ClassicAnnouncedAct[] Bands = [new("a", 0, 20, 2_000), new("b", 1, 30, 3_000), new("c", 2, 40, 4_000)];
    private static ClassicBookingView View(ClassicBookingSnapshot s) => ClassicBookingLedger.Evaluate(14, 1, s);

    [TestMethod]
    public void ReleasedSlotsRefundPaidFeesAndCannotOpenUntilRefilled()
    {
        var state = ClassicBookingLedger.Confirm(14, 1, ClassicBookingLedger.Create(0), Bands);
        var removed = new[] { Bands[0], ClassicAnnouncedAct.EmptySlot, ClassicAnnouncedAct.EmptySlot };
        state = ClassicBookingLedger.Rebook(14, 1, state, removed);
        var released = View(state);
        Assert.AreEqual(2_000L, released.NetBandCostsPennies);
        Assert.AreEqual(ClassicLineupChange.Substantial, released.HighestChange);
        Assert.Throws<InvalidOperationException>(() => ClassicBookingLedger.Open(14, 1, state));
        Assert.Throws<ArgumentException>(() => ClassicBookingLedger.Rebook(14, 1, state,
            [Bands[0], ClassicAnnouncedAct.EmptySlot with { PaidFeePennies = 1 }, ClassicAnnouncedAct.EmptySlot]));
        state = ClassicBookingLedger.Rebook(14, 1, state, Bands);
        Assert.AreEqual(released.RefundedTickets, View(state).RefundedTickets);
        Assert.AreEqual(9_000L, View(state).NetBandCostsPennies);
        state = ClassicBookingLedger.Rebook(14, 1, state,
            [ClassicAnnouncedAct.EmptySlot, ClassicAnnouncedAct.EmptySlot, ClassicAnnouncedAct.EmptySlot]);
        Assert.AreEqual(0L, View(state).NetBandCostsPennies);
        Assert.AreEqual(0L, View(ClassicBookingLedger.Cancel(14, 1, state)).CashChangePennies);
    }

    [TestMethod]
    public void ConfirmCreatesActualSalesAndBalancedBandChargesExactlyOnce()
    {
        var state = ClassicBookingLedger.Confirm(14, 1, ClassicBookingLedger.Create(0), Bands);
        var view = View(state);
        Assert.AreEqual(ClassicBookingPhase.SitePreparation, view.Phase);
        Assert.AreEqual(ClassicTicketDemand.Resolve(14, 1, new(25, 0, 1_000, Bands.Select(a => a.Popularity).ToArray())), view.TicketHolders);
        Assert.AreEqual(view.TicketHolders * 1_000 - 9_000, view.CashChangePennies);
        Assert.AreEqual(9_000L, view.NetBandCostsPennies);
        Assert.IsTrue(view.Money.All(m => m.Entries(new(1)).Sum(e => e.AmountPennies) == 0));
        Assert.Throws<InvalidOperationException>(() => ClassicBookingLedger.Confirm(14, 1, state, Bands));
        Assert.Throws<InvalidOperationException>(() => ClassicBookingLedger.SetPrice(14, 1, state, 1_500));
        Assert.AreEqual(view.CashChangePennies, View(ClassicBookingLedger.Open(14, 1, state)).CashChangePennies);
    }

    [TestMethod]
    public void RebookingReleasesOnlyActualPaidFeesAndRefundsOnlyTheIncrement()
    {
        var state = ClassicBookingLedger.Confirm(14, 1, ClassicBookingLedger.Create(0), Bands);
        var original = View(state);
        var next = Bands.ToArray(); next[0] = new("new-a", 0, 20, 500);
        state = ClassicBookingLedger.Rebook(14, 1, state, next);
        var changed = View(state);
        var refunds = ClassicBookingTerms.RefundTarget(original.OriginalTickets, ClassicLineupChange.Comparable);
        Assert.AreEqual(original.CashChangePennies + 1_500 - refunds * 1_000, changed.CashChangePennies);
        Assert.AreEqual(original.OriginalTickets, changed.OriginalTickets);
        Assert.AreEqual(original.OriginalTickets - refunds, changed.TicketHolders);
        Assert.AreEqual(200, changed.Disappointment);
        var noOp = View(ClassicBookingLedger.Rebook(14, 1, state, next));
        Assert.AreEqual(changed.CashChangePennies, noOp.CashChangePennies);
        Assert.AreEqual(changed.Money.Length, noOp.Money.Length);
        next[2] = new("new-c", 3, 5, 100);
        state = ClassicBookingLedger.Rebook(14, 1, state, next);
        var severe = View(state);
        Assert.AreEqual(ClassicBookingTerms.RefundTarget(original.OriginalTickets, ClassicLineupChange.Substantial), severe.RefundedTickets);
        var restored = View(ClassicBookingLedger.Rebook(14, 1, state, Bands));
        Assert.AreEqual(severe.RefundedTickets, restored.RefundedTickets);
        Assert.AreEqual(severe.Disappointment, restored.Disappointment);
        Assert.AreEqual(9_000L, restored.NetBandCostsPennies);
        Assert.AreEqual(original.Money.Count(m => m.Kind == ClassicBookingMoneyKind.Tickets), restored.Money.Count(m => m.Kind == ClassicBookingMoneyKind.Tickets));
    }

    [TestMethod]
    public void NegativeBookingBudgetHasFullCancellationRecoveryWithoutNewDebt()
    {
        var expensive = Bands.Select(b => b with { PaidFeePennies = 100_000 }).ToArray();
        var state = ClassicBookingLedger.Confirm(14, 1, ClassicBookingLedger.Create(8), expensive);
        Assert.IsTrue(View(state).CashChangePennies < -10_000);
        state = ClassicBookingLedger.Cancel(14, 1, state);
        var cancelled = View(state);
        Assert.AreEqual(0L, cancelled.CashChangePennies);
        Assert.AreEqual(0L, cancelled.NetTicketReceiptsPennies);
        Assert.AreEqual(0L, cancelled.NetBandCostsPennies);
        Assert.AreEqual(3, cancelled.Reputation);
        Assert.AreEqual(2, cancelled.Attempt);
        Assert.AreEqual(0, cancelled.TicketHolders);
        Assert.Throws<InvalidOperationException>(() => ClassicBookingLedger.Cancel(14, 1, state));
        state = ClassicBookingLedger.Confirm(14, 1, state, Bands);
        Assert.AreEqual(ClassicTicketDemand.Resolve(14, 1, new(25, 3, 1_000, Bands.Select(b => b.Popularity).ToArray())), View(state).OriginalTickets);
        state = ClassicBookingLedger.Cancel(14, 1, state);
        Assert.AreEqual(0, View(state).Reputation);
        Assert.AreEqual(0L, View(state).CashChangePennies);
    }

    [TestMethod]
    public void CancellationAfterRebookingRefundsRemainingHoldersWithoutPayingTwice()
    {
        var state = ClassicBookingLedger.Confirm(14, 1, ClassicBookingLedger.Create(0), Bands);
        var next = Bands.ToArray(); next[2] = new("cheap", 3, 0, 100);
        state = ClassicBookingLedger.Rebook(14, 1, state, next);
        state = ClassicBookingLedger.Cancel(14, 1, state);
        var view = View(state);
        Assert.AreEqual(0L, view.CashChangePennies);
        Assert.AreEqual(0L, view.NetBandCostsPennies);
        Assert.AreEqual(0L, view.NetTicketReceiptsPennies);
        state = ClassicBookingLedger.Confirm(14, 1, state, Bands);
        Assert.AreEqual(View(ClassicBookingLedger.Confirm(14, 1, ClassicBookingLedger.Create(0), Bands)).OriginalTickets, View(state).OriginalTickets,
            "Replanning at the reputation floor must keep the same market, rather than reroll it.");
    }

    [TestMethod]
    public void SaveFactsReplayAndRejectIllegalPhasePriceOrContractTampering()
    {
        var state = ClassicBookingLedger.Confirm(14, 1, ClassicBookingLedger.Create(0), Bands);
        var restored = JsonSerializer.Deserialize<ClassicBookingSnapshot>(JsonSerializer.Serialize(state))!;
        Assert.AreEqual(JsonSerializer.Serialize(View(state)), JsonSerializer.Serialize(View(restored)));
        Assert.Throws<ArgumentException>(() => View(restored with { DraftTicketPricePennies = 1_001 }));
        Assert.Throws<ArgumentException>(() => ClassicBookingLedger.Evaluate(14, 1, restored, (_, _) => false));
        var changed = Bands.ToArray(); changed[0] = changed[0] with { PaidFeePennies = 1 };
        Assert.Throws<ArgumentException>(() => ClassicBookingLedger.Rebook(14, 1, restored, changed));
        var opened = ClassicBookingLedger.Open(14, 1, state);
        Assert.Throws<InvalidOperationException>(() => ClassicBookingLedger.Cancel(14, 1, opened));
        Assert.Throws<InvalidOperationException>(() => ClassicBookingLedger.Rebook(14, 1, opened, Bands));
        var clone = ClassicBookingLedger.Clone(state);
        clone.Actions[0].Contracts[0] = clone.Actions[0].Contracts[0] with { PaidFeePennies = 1 };
        Assert.AreEqual(2_000, state.Actions[0].Contracts[0].PaidFeePennies);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(10_000)]
    public void FreeEntryAndZeroSalesNeverInventRefundMoney(int price)
    {
        var draft = ClassicBookingLedger.SetPrice(14, 1, ClassicBookingLedger.Create(0), price);
        var state = ClassicBookingLedger.Confirm(14, 1, draft, Bands);
        if (price == 10_000) Assert.AreEqual(0, View(state).TicketHolders);
        if (price == 0) Assert.AreEqual(0L, View(state).NetTicketReceiptsPennies);
        var next = Bands.ToArray(); next[2] = new("cheap", 4, 0, 100);
        state = ClassicBookingLedger.Rebook(14, 1, state, next);
        Assert.IsTrue(View(state).NetTicketReceiptsPennies >= 0);
        state = ClassicBookingLedger.Cancel(14, 1, state);
        Assert.AreEqual(0L, View(state).CashChangePennies);
        Assert.IsTrue(View(state).Money.All(m => m.Quantity >= 0 && m.UnitPennies >= 0));
    }
}

using Festival.Simulation;
using Festival.Simulation.Fixtures;

namespace Festival.Tests;

[TestClass]
public sealed class AtomicPurchaseTests
{
    [TestMethod]
    public void PurchaseTransfersCashConsumesStockAndBooksBalancedEntries()
    {
        var fixture = AtomicPurchaseFixture.Create();
        var result = fixture.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
            fixture, new CommandId(6), new TransactionId(1), fixture.PrimaryBuyerId));
        var snapshot = fixture.Session.CaptureSnapshot();

        Assert.IsTrue(result.IsAccepted);
        Assert.AreEqual(200L, Wallet(snapshot, fixture.PrimaryBuyerId));
        Assert.AreEqual(300L, FestivalCash(snapshot, fixture.FestivalId));
        Assert.AreEqual(1, Stock(snapshot, fixture.ServiceId).Quantity);
        var transaction = snapshot.Transactions.Single();
        Assert.IsTrue(transaction.IsBalanced);
        Assert.AreEqual(-300L, Entry(transaction, LedgerAccountType.SalesRevenue));
        Assert.AreEqual(120L, Entry(transaction, LedgerAccountType.CostOfGoodsSold));
        Assert.AreEqual(-120L, Entry(transaction, LedgerAccountType.InventoryAsset));
        Assert.AreEqual(0m, transaction.Entries.Sum(entry => (decimal)entry.AmountPennies));
    }

    [TestMethod]
    public void ExactCashSucceedsWhileInsufficientCashAndEmptyStockAreAtomicRejections()
    {
        var exact = AtomicPurchaseFixture.Create(stockQuantity: 1);
        var exactResult = exact.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
            exact, new CommandId(6), new TransactionId(1), exact.ExactCashBuyerId));
        Assert.IsTrue(exactResult.IsAccepted);
        Assert.AreEqual(0L, Wallet(exact.Session.CaptureSnapshot(), exact.ExactCashBuyerId));

        var insufficient = AtomicPurchaseFixture.Create(stockQuantity: 1);
        AssertRejectedWithoutMutation(
            insufficient,
            AtomicPurchaseFixture.PurchaseEnvelope(insufficient, new CommandId(6), new TransactionId(1), insufficient.PrimaryBuyerId, 501),
            CommandReasonCode.InsufficientFunds);

        var empty = AtomicPurchaseFixture.Create(stockQuantity: 0);
        AssertRejectedWithoutMutation(
            empty,
            AtomicPurchaseFixture.PurchaseEnvelope(empty, new CommandId(6), new TransactionId(1), empty.PrimaryBuyerId),
            CommandReasonCode.OutOfStock);
    }

    [TestMethod]
    public void AcceptedCommandAndTransactionIdentitiesAreBothIdempotent()
    {
        var fixture = AtomicPurchaseFixture.Create();
        var command = AtomicPurchaseFixture.PurchaseEnvelope(
            fixture, new CommandId(6), new TransactionId(40), fixture.PrimaryBuyerId);
        Assert.IsTrue(fixture.Session.Execute(command).IsAccepted);

        var afterSale = fixture.Session.CaptureSnapshot();
        var duplicateCommand = fixture.Session.Execute(command);
        Assert.AreEqual(CommandReasonCode.DuplicateCommand, duplicateCommand.ReasonCode);
        Assert.AreEqual(afterSale.AuthoritativeHash, fixture.Session.CaptureSnapshot().AuthoritativeHash);

        var duplicateTransaction = fixture.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
            fixture, new CommandId(7), new TransactionId(40), fixture.CompetingBuyerId));
        Assert.AreEqual(CommandReasonCode.DuplicateTransaction, duplicateTransaction.ReasonCode);
        Assert.AreEqual(afterSale.AuthoritativeHash, fixture.Session.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(1, fixture.Session.Transactions.Count);
    }

    [TestMethod]
    public void LastItemGoesToFirstBuyerInSubmissionOrder()
    {
        var fixture = AtomicPurchaseFixture.Create(stockQuantity: 1);
        var first = fixture.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
            fixture, new CommandId(6), new TransactionId(1), fixture.PrimaryBuyerId));
        var second = fixture.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
            fixture, new CommandId(7), new TransactionId(2), fixture.CompetingBuyerId));
        var snapshot = fixture.Session.CaptureSnapshot();

        Assert.IsTrue(first.IsAccepted);
        Assert.IsFalse(second.IsAccepted);
        Assert.AreEqual(CommandReasonCode.OutOfStock, second.ReasonCode);
        Assert.AreEqual(0, Stock(snapshot, fixture.ServiceId).Quantity);
        Assert.AreEqual(200L, Wallet(snapshot, fixture.PrimaryBuyerId));
        Assert.AreEqual(500L, Wallet(snapshot, fixture.CompetingBuyerId));
        Assert.AreEqual(fixture.PrimaryBuyerId, snapshot.Transactions.Single().BuyerId);
    }

    [TestMethod]
    public void InvalidAmountsAndMissingIdentitiesRejectWithoutMutation()
    {
        var fixture = AtomicPurchaseFixture.Create();
        var cases = new (CommandEnvelope Command, CommandReasonCode Reason)[]
        {
            (AtomicPurchaseFixture.PurchaseEnvelope(fixture, new CommandId(6), new TransactionId(1), fixture.PrimaryBuyerId, -1), CommandReasonCode.InvalidParameter),
            (AtomicPurchaseFixture.PurchaseEnvelope(fixture, new CommandId(6), new TransactionId(1), fixture.PrimaryBuyerId, 300, -1), CommandReasonCode.InvalidParameter),
            (AtomicPurchaseFixture.PurchaseEnvelope(fixture, new CommandId(6), new TransactionId(1), new EntityId(999)), CommandReasonCode.UnknownOwner),
            (AtomicPurchaseFixture.PurchaseEnvelope(fixture, new CommandId(6), new TransactionId(1), fixture.PrimaryBuyerId, festivalId: new EntityId(999)), CommandReasonCode.UnknownOwner),
            (AtomicPurchaseFixture.PurchaseEnvelope(fixture, new CommandId(6), new TransactionId(1), fixture.PrimaryBuyerId, serviceId: new EntityId(999)), CommandReasonCode.UnknownTarget),
        };

        var initialHash = fixture.Session.CaptureSnapshot().AuthoritativeHash;
        foreach (var item in cases)
        {
            var result = fixture.Session.Execute(item.Command);
            Assert.IsFalse(result.IsAccepted);
            Assert.AreEqual(item.Reason, result.ReasonCode);
            Assert.AreEqual(initialHash, fixture.Session.CaptureSnapshot().AuthoritativeHash);
        }
    }

    [TestMethod]
    public void CumulativeTransfersReconcileAcrossMultiplePurchases()
    {
        var fixture = AtomicPurchaseFixture.Create(stockQuantity: 3, unitCostBasisPennies: 80);
        Assert.IsTrue(fixture.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
            fixture, new CommandId(6), new TransactionId(1), fixture.PrimaryBuyerId, 300)).IsAccepted);
        Assert.IsTrue(fixture.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
            fixture, new CommandId(7), new TransactionId(2), fixture.ExactCashBuyerId, 300)).IsAccepted);
        Assert.IsTrue(fixture.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
            fixture, new CommandId(8), new TransactionId(3), fixture.CompetingBuyerId, 200)).IsAccepted);
        var snapshot = fixture.Session.CaptureSnapshot();

        const long transferred = 800;
        Assert.AreEqual(transferred, FestivalCash(snapshot, fixture.FestivalId));
        Assert.AreEqual(1_300L - transferred, snapshot.Wallets.Sum(wallet => wallet.CashPennies));
        Assert.AreEqual(0, Stock(snapshot, fixture.ServiceId).Quantity);
        Assert.AreEqual(transferred, snapshot.Transactions.Sum(transaction =>
            transaction.Entries.Where(entry => entry.OwnerId == fixture.FestivalId && entry.Account == LedgerAccountType.CashAsset).Sum(entry => entry.AmountPennies)));
        Assert.AreEqual(-transferred, snapshot.Transactions.Sum(transaction => Entry(transaction, LedgerAccountType.SalesRevenue)));
        Assert.IsTrue(snapshot.Transactions.All(transaction => transaction.IsBalanced));
    }

    [TestMethod]
    public void TransactionIdentityAndLedgerAreAuthoritativeHashInputs()
    {
        var first = AtomicPurchaseFixture.Create();
        var second = AtomicPurchaseFixture.Create();
        Assert.IsTrue(first.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
            first, new CommandId(6), new TransactionId(1), first.PrimaryBuyerId)).IsAccepted);
        Assert.IsTrue(second.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
            second, new CommandId(6), new TransactionId(2), second.PrimaryBuyerId)).IsAccepted);

        Assert.AreNotEqual(first.Session.CaptureSnapshot().AuthoritativeHash, second.Session.CaptureSnapshot().AuthoritativeHash);
    }

    private static void AssertRejectedWithoutMutation(PurchaseFixtureState fixture, CommandEnvelope command, CommandReasonCode reason)
    {
        var before = fixture.Session.CaptureSnapshot();
        var result = fixture.Session.Execute(command);
        var after = fixture.Session.CaptureSnapshot();
        Assert.IsFalse(result.IsAccepted);
        Assert.AreEqual(reason, result.ReasonCode);
        Assert.AreEqual(before.AuthoritativeHash, after.AuthoritativeHash);
        Assert.AreEqual(before.NextSubmissionSequence, after.NextSubmissionSequence);
        Assert.AreEqual(before.Transactions.Count, after.Transactions.Count);
    }

    private static long Wallet(SessionSnapshot snapshot, EntityId ownerId) => snapshot.Wallets.Single(item => item.OwnerId == ownerId).CashPennies;
    private static long FestivalCash(SessionSnapshot snapshot, EntityId ownerId) => snapshot.FestivalFinances.Single(item => item.OwnerId == ownerId).CashPennies;
    private static OwnedStockSnapshot Stock(SessionSnapshot snapshot, EntityId serviceId) => snapshot.OwnedStocks.Single(item => item.ServiceId == serviceId);
    private static long Entry(TransactionRecord transaction, LedgerAccountType account) => transaction.Entries.Single(item => item.Account == account).AmountPennies;
}

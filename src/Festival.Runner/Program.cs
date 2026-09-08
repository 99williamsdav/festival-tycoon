using Festival.ContentAdapter;
using Festival.Simulation;
using Festival.Simulation.Fixtures;

if (args is ["--content-catalogue", var validPath, var invalidPath])
{
    var valid = JsonContentAdapter.LoadFile(validPath);
    var invalid = JsonContentAdapter.LoadFile(invalidPath);
    Console.WriteLine($"valid.file={Path.GetFileName(validPath)} success={valid.IsSuccess} hash={valid.Catalogue?.ContentHash ?? "none"}");
    Console.WriteLine($"valid.scenarios={valid.Catalogue?.Scenarios.Count ?? 0} services={valid.Catalogue?.Services.Count ?? 0}");
    Console.WriteLine($"invalid.file={Path.GetFileName(invalidPath)} success={invalid.IsSuccess} diagnostics={invalid.Diagnostics.Count}");
    foreach (var diagnostic in invalid.Diagnostics) Console.WriteLine(diagnostic);
    Environment.ExitCode = valid.IsSuccess && !invalid.IsSuccess ? 0 : 1;
    return;
}

if (args is ["--scenario", "deterministic-session"])
{
    var singleBatch = DeterministicSessionFixture.Run(400);
    var mixedBatches = DeterministicSessionFixture.Run(1, 7, 32);
    var repeatedMixedBatches = DeterministicSessionFixture.Run(1, 7, 32);

    Console.WriteLine("scenario=deterministic-session seed=20260907 ticks=400 tick_ms=250");
    Console.WriteLine($"single.initial={singleBatch.InitialHash}");
    Console.WriteLine($"single.final={singleBatch.FinalHash}");
    Console.WriteLine($"mixed.initial={mixedBatches.InitialHash}");
    Console.WriteLine($"mixed.final={mixedBatches.FinalHash}");
    Console.WriteLine($"repeat.final={repeatedMixedBatches.FinalHash}");
    Console.WriteLine($"batch_equivalent={singleBatch.FinalHash == mixedBatches.FinalHash}");
    Console.WriteLine($"repeatable={mixedBatches.FinalHash == repeatedMixedBatches.FinalHash}");
    Console.WriteLine($"record_expired={mixedBatches.FinalSnapshot.FixtureRecords.Single().HasExpired}");
    return;
}

if (args is ["--scenario", "atomic-purchase"])
{
    var fixture = AtomicPurchaseFixture.Create(stockQuantity: 1, unitCostBasisPennies: 120);
    var before = fixture.Session.CaptureSnapshot();
    var sale = fixture.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
        fixture, new CommandId(6), new TransactionId(1), fixture.PrimaryBuyerId));
    var after = fixture.Session.CaptureSnapshot();
    var insufficient = fixture.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
        fixture, new CommandId(7), new TransactionId(2), fixture.PrimaryBuyerId));
    var empty = fixture.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
        fixture, new CommandId(7), new TransactionId(2), fixture.CompetingBuyerId));

    Console.WriteLine("scenario=atomic-purchase price_p=300 quantity=1 unit_cost_p=120");
    Console.WriteLine($"before buyer_p={before.Wallets.Single(item => item.OwnerId == fixture.PrimaryBuyerId).CashPennies} festival_p={before.FestivalFinances.Single().CashPennies} stock={before.OwnedStocks.Single().Quantity} ledger={before.Transactions.Count}");
    Console.WriteLine($"sale accepted={sale.IsAccepted} reason={sale.ReasonCode}");
    Console.WriteLine($"after buyer_p={after.Wallets.Single(item => item.OwnerId == fixture.PrimaryBuyerId).CashPennies} festival_p={after.FestivalFinances.Single().CashPennies} stock={after.OwnedStocks.Single().Quantity} ledger={after.Transactions.Count}");
    foreach (var entry in after.Transactions.Single().Entries)
    {
        Console.WriteLine($"entry owner={entry.OwnerId} account={entry.Account} amount_p={entry.AmountPennies}");
    }
    Console.WriteLine($"balanced={after.Transactions.Single().IsBalanced}");
    Console.WriteLine($"insufficient accepted={insufficient.IsAccepted} reason={insufficient.ReasonCode}");
    Console.WriteLine($"empty accepted={empty.IsAccepted} reason={empty.ReasonCode}");
    Environment.ExitCode = sale.IsAccepted && !insufficient.IsAccepted && !empty.IsAccepted && after.Transactions.Single().IsBalanced ? 0 : 1;
    return;
}

Console.WriteLine(ToolchainSmoke.GetFixedResult());

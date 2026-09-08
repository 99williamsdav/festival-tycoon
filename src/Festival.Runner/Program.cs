using Festival.ContentAdapter;
using Festival.Persistence;
using Festival.Simulation;
using Festival.Simulation.Fixtures;

var saveCompatibility = new SaveCompatibility(
    "0.0.1-m0.05",
    "d7e7597670c2f9bc2552fa5df29f4afe294270e346643160e92feb1436bb1dd9",
    "m0-rules-v1");

if (args is ["--save-roundtrip", var saveDirectory, var slotId])
{
    var fixture = AtomicPurchaseFixture.Create(stockQuantity: 3);
    fixture.Session.AdvanceTicks(10);
    fixture.Session.NextRandom(RandomStreamId.Demand);
    var firstSale = fixture.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
        fixture, new CommandId(6), new TransactionId(1), fixture.PrimaryBuyerId));
    var midHash = fixture.Session.CaptureSnapshot().AuthoritativeHash;
    var saved = SaveFileAdapter.SaveSlot(saveDirectory, slotId, new SaveWriteRequest(
        fixture.Session, saveCompatibility, "manual-demo", DateTimeOffset.UtcNow));
    var loaded = SaveFileAdapter.LoadSlot(saveDirectory, slotId, saveCompatibility);
    if (!saved.IsSuccess || !loaded.IsSuccess)
    {
        Console.Error.WriteLine(saved.Error ?? loaded.Error);
        Environment.ExitCode = 1;
        return;
    }

    var loadedMidHash = loaded.Session!.CaptureSnapshot().AuthoritativeHash;
    var resumedFixture = fixture with { Session = loaded.Session! };
    var randomMatches = fixture.Session.NextRandom(RandomStreamId.Weather) == loaded.Session!.NextRandom(RandomStreamId.Weather);
    var originalSecond = fixture.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
        fixture, new CommandId(7), new TransactionId(2), fixture.ExactCashBuyerId));
    var resumedSecond = loaded.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
        resumedFixture, new CommandId(7), new TransactionId(2), fixture.ExactCashBuyerId));
    fixture.Session.AdvanceTicks(25);
    loaded.Session.AdvanceTicks(25);
    var originalFinal = fixture.Session.CaptureSnapshot().AuthoritativeHash;
    var resumedFinal = loaded.Session.CaptureSnapshot().AuthoritativeHash;

    var slotPath = SaveFileAdapter.ResolveSlotPath(saveDirectory, slotId);
    var corruptPath = Path.Combine(saveDirectory, "corrupt-copy" + SaveFileAdapter.FileExtension);
    var corruptBytes = File.ReadAllBytes(slotPath);
    corruptBytes[corruptBytes.Length / 2] ^= 0x5A;
    File.WriteAllBytes(corruptPath, corruptBytes);
    var corrupt = SaveFileAdapter.LoadFile(corruptPath, saveCompatibility);

    Console.WriteLine($"scenario=save-roundtrip slot={slotId} format=gzip-json-v1");
    Console.WriteLine($"first_sale={firstSale.IsAccepted} save={saved.IsSuccess} load={loaded.IsSuccess}");
    Console.WriteLine($"mid_hash={midHash} loaded_hash={loadedMidHash} equal={midHash == loadedMidHash}");
    Console.WriteLine($"random_continuation={randomMatches} second_original={originalSecond.IsAccepted} second_resumed={resumedSecond.IsAccepted}");
    Console.WriteLine($"original_final={originalFinal} resumed_final={resumedFinal} equal={originalFinal == resumedFinal}");
    Console.WriteLine($"transactions={string.Join(',', loaded.Session.Transactions.Select(item => item.Id.Value))}");
    Console.WriteLine($"corrupt_load={corrupt.IsSuccess} error={corrupt.Error}");
    Environment.ExitCode = originalFinal == resumedFinal && randomMatches && !corrupt.IsSuccess ? 0 : 1;
    return;
}

if (args is ["--write-save-fixture", var fixturePath])
{
    var fixture = AtomicPurchaseFixture.Create(stockQuantity: 1);
    _ = fixture.Session.Execute(AtomicPurchaseFixture.PurchaseEnvelope(
        fixture, new CommandId(6), new TransactionId(1), fixture.PrimaryBuyerId));
    _ = fixture.Session.Execute(new CommandEnvelope(
        new CommandId(7), fixture.Session.CampaignId, fixture.Session.Phase, fixture.Session.CurrentTick,
        fixture.Session.NextSubmissionSequence, null, new SetPausedCommand(true)));
    var result = SaveFileAdapter.SaveFile(fixturePath, new SaveWriteRequest(
        fixture.Session, saveCompatibility, "nonpersonal-test-fixture", new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero)));
    Console.WriteLine($"fixture={Path.GetFileName(fixturePath)} saved={result.IsSuccess} hash={fixture.Session.CaptureSnapshot().AuthoritativeHash}");
    if (!result.IsSuccess) Console.Error.WriteLine(result.Error);
    Environment.ExitCode = result.IsSuccess ? 0 : 1;
    return;
}

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

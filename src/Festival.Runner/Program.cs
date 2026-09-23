using Festival.ContentAdapter;
using Festival.Persistence;
using Festival.Simulation;
using Festival.Simulation.Fixtures;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

var saveCompatibility = new SaveCompatibility(
    "0.0.1-m0.05",
    "d7e7597670c2f9bc2552fa5df29f4afe294270e346643160e92feb1436bb1dd9",
    "m0-rules-v1");

if (args.Length >= 5 && args[0] == "--benchmark")
{
    var agents = int.Parse(args[1]);
    var passage = Enum.Parse<BenchmarkPassage>(args[2], ignoreCase: true);
    var speed = int.Parse(args[3]);
    var output = args[4];
    var run = RunBenchmark(agents, passage, speed);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    File.WriteAllText(output, JsonSerializer.Serialize(run, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(run));
    Environment.ExitCode = run.Completed == agents && run.RouteFailures == 0 && run.ReleasedReservations ? 0 : 2;
    return;
}

if (args is ["--m1-feasibility", var m1Output])
{
    var report = RunSharedWorldFeasibility();
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(m1Output))!);
    File.WriteAllText(m1Output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(report));
    Environment.ExitCode = report.FunctionallyComplete && report.RouteFailures == 0 && report.ReleasedReservations ? 0 : 2;
    return;
}

if (args is ["--scale-diagnostic", var populationText, var layoutText, var seedText, var scaleOutput,
    var sourceRevision, var sourceFingerprint])
{
    var report = RunScaleDiagnostic(int.Parse(populationText),
        Enum.Parse<ScaleDiagnosticLayout>(layoutText, ignoreCase: true), ulong.Parse(seedText),
        scaleOutput, sourceRevision, sourceFingerprint);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(scaleOutput))!);
    File.WriteAllText(scaleOutput, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(report));
    Environment.ExitCode = report.CorrectnessPassed && report.Windows.All(window => window.AllDeclaredAgentsActive) ? 0 : 2;
    return;
}

if (args is ["--scale-contention-trace", var traceOutput])
{
    var report = RunScaleContentionTrace();
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(traceOutput))!);
    File.WriteAllText(traceOutput, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(report));
    Environment.ExitCode = report.FunctionallyComplete ? 0 : 2;
    return;
}

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

if (args is ["--scenario", "r0-lifecycle"])
{
    var directory = Path.Combine(Path.GetTempPath(), $"festival-r000-runner-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var compatibility = new SaveCompatibility("0.0.1-r0.00", "d7e7597670c2f9bc2552fa5df29f4afe294270e346643160e92feb1436bb1dd9", "r0.00-fixture-rules-v1");
        var session = GameSession.CreateR000LifecycleFixture(20260923);
        Console.WriteLine($"scenario=r0-lifecycle seed={session.CampaignSeed} label=\"{session.CaptureSnapshot().Lifecycle!.FixtureLabel}\"");
        Console.WriteLine($"boundary=active tier={session.CaptureSnapshot().Lifecycle!.CurrentTierId} attempt={session.CaptureSnapshot().Lifecycle!.CurrentAttemptId} hash={session.CaptureSnapshot().AuthoritativeHash}");
        var commands = new SessionCommand[]
        {
            new ForceFixtureDeathsCommand(["fixture-guest", "fixture-performer"]),
            new SpendFixtureFavourCommand(),
            new ForceFixtureSafeCompletionCommand(),
        };
        var names = new[] { "hearing", "same-tier-retry", "safe-tier-advance" };
        var passed = true;
        for (var index = 0; index < commands.Length; index++)
        {
            var command = new CommandEnvelope(new CommandId((ulong)index + 1), session.CampaignId, session.Phase,
                session.CurrentTick, session.NextSubmissionSequence, null, commands[index]);
            var result = LifecycleTransitionCoordinator.Apply(directory, session, compatibility,
                DateTimeOffset.UnixEpoch.AddMinutes(index), index, command);
            passed &= result.IsSuccess;
            if (!result.IsSuccess) { Console.Error.WriteLine(result.Message); break; }
            session = result.Session;
            var lifecycle = session.CaptureSnapshot().Lifecycle!;
            Console.WriteLine($"boundary={names[index]} tier={lifecycle.CurrentTierId} attempt={lifecycle.CurrentAttemptId} favour={lifecycle.FixtureFavourBalance} casualties={lifecycle.Casualties.Count} hearings={lifecycle.Hearings.Count} hash={session.CaptureSnapshot().AuthoritativeHash}");
        }
        var final = session.CaptureSnapshot().Lifecycle!;
        Console.WriteLine($"transactions={string.Join(',', final.CompletedOutcomeTransactionIds)}");
        Console.WriteLine($"attempts={string.Join(',', final.Attempts.Select(item => $"{item.AttemptId}:{item.TierId}:{item.Status}"))}");
        Environment.ExitCode = passed && final.Casualties.Count == 1 && final.Hearings.Count == 1 &&
            final.FixtureFavourBalance == 0 && final.FixtureTierOrdinal == 2 ? 0 : 1;
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
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

if (args is ["--scenario", "single-agent-navigation"])
{
    var fixture = NavigationFixture.CreateGateToServiceSession();
    var intent = NavigationFixture.IssueAutonomousServiceIntent(fixture);
    var initial = fixture.Session.CaptureSnapshot().NavigationAgents.Single();
    var ticks = 0;
    while (fixture.Session.CaptureSnapshot().NavigationAgents.Single().Action == AgentNavigationAction.Travelling && ticks < 10_000)
    {
        fixture.Session.AdvanceTicks(1);
        ticks++;
    }
    var arrived = fixture.Session.CaptureSnapshot();
    var blocked = fixture.Session.TraversalGrid!.Overrides.Values.First(item => !item.IsWalkable).Cell;
    var blockedResult = fixture.Session.Execute(new CommandEnvelope(
        new CommandId(3), fixture.Session.CampaignId, fixture.Session.Phase, fixture.Session.CurrentTick,
        fixture.Session.NextSubmissionSequence, fixture.AgentId,
        new SetAgentDestinationCommand(blocked, "fixture.blocked-target")));
    var noRoute = fixture.Session.CaptureSnapshot().NavigationAgents.Single();

    Console.WriteLine($"scenario=single-agent-navigation intent={intent.IsAccepted} start={initial.XMillimetres},{initial.ZMillimetres}");
    Console.WriteLine($"arrival action={arrived.NavigationAgents.Single().Action} ticks={ticks} position={arrived.NavigationAgents.Single().XMillimetres},{arrived.NavigationAgents.Single().ZMillimetres} hash={arrived.AuthoritativeHash}");
    Console.WriteLine($"blocked accepted={blockedResult.IsAccepted} action={noRoute.Action} expanded={noRoute.LastSearchExpandedNodes} position={noRoute.XMillimetres},{noRoute.ZMillimetres}");
    Environment.ExitCode = intent.IsAccepted && arrived.NavigationAgents.Single().Action == AgentNavigationAction.Arrived &&
        blockedResult.IsAccepted && noRoute.Action == AgentNavigationAction.NoRoute ? 0 : 1;
    return;
}

if (args is ["--scenario", "physical-service-queue"])
{
    var fixture = ServiceQueueFixture.Create();
    var initial = fixture.Session.CaptureSnapshot();
    ServiceQueueFixture.AdvanceUntilResolved(fixture.Session);
    ServiceQueueFixture.AdvanceUntilDeparted(fixture);
    var final = fixture.Session.CaptureSnapshot();
    Console.WriteLine($"scenario=physical-service-queue agents={fixture.AgentIds.Count} price_p={ServiceQueueFixture.DefaultPricePennies} duration_ticks={ServiceQueueFixture.DefaultServiceDurationTicks}");
    Console.WriteLine($"initial_order={string.Join(',', initial.ServiceQueues.Single().OrderedMembers.Select(id => id.Value))} reservations={string.Join(',', initial.ServiceQueues.Single().Agents.Select(item => item.ReservedSlotIndex))}");
    Console.WriteLine($"transactions={final.Transactions.Count} buyers={string.Join(',', final.Transactions.Select(item => item.BuyerId.Value))}");
    Console.WriteLine($"festival_cash_p={final.FestivalFinances.Single().CashPennies} stock={final.OwnedStocks.Single().Quantity} queue={final.ServiceQueues.Single().OrderedMembers.Count} departed={ServiceQueueFixture.AllAtExit(fixture)} tick={final.CurrentTick} hash={final.AuthoritativeHash}");
    Environment.ExitCode = final.Transactions.Count == 5 && final.FestivalFinances.Single().CashPennies == 1_500 &&
        final.OwnedStocks.Single().Quantity == 0 && final.ServiceQueues.Single().OrderedMembers.Count == 0 &&
        ServiceQueueFixture.AllAtExit(fixture) ? 0 : 1;
    return;
}

if (args is ["--scenario", "fifty-agent-foundation"])
{
    var fixture = FiftyAgentFoundationFixture.Create();
    var minimumSeparation = long.MaxValue;
    var exactOverlapPairTicks = 0;
    var belowTwoHundredRuns = new Dictionary<(EntityId, EntityId), int>();
    var maximumConsecutiveBelowTwoHundred = 0;
    var previousPositions = fixture.Session.CaptureSnapshot().NavigationAgents.ToDictionary(item => item.Id);
    var blockedSweptSegments = 0;
    var maximumStepMillimetres = 0L;
    var reproSweepLegal = false;
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    while (!FiftyAgentFoundationFixture.AllCompleted(fixture) && fixture.Session.CurrentTick < 30_000)
    {
        fixture.Session.AdvanceTicks(1);
        var agents = fixture.Session.CaptureSnapshot().NavigationAgents;
        var index = new SpatialNeighbourIndex(agents);
        var belowTwoHundredThisTick = new HashSet<(EntityId, EntityId)>();
        foreach (var agent in agents)
        {
            var previous = previousPositions[agent.Id];
            var dx = (long)agent.XMillimetres - previous.XMillimetres;
            var dz = (long)agent.ZMillimetres - previous.ZMillimetres;
            maximumStepMillimetres = Math.Max(maximumStepMillimetres, (long)Math.Sqrt(dx * dx + dz * dz));
            var legal = TraversalSweep.IsWalkable(fixture.Session.TraversalGrid!, previous.XMillimetres, previous.ZMillimetres, agent.XMillimetres, agent.ZMillimetres);
            if (!legal) blockedSweptSegments++;
            if (fixture.Session.CurrentTick == 792 && agent.Id == new EntityId(21)) reproSweepLegal = legal;
        }
        foreach (var agent in agents)
        foreach (var other in index.Query(agent.XMillimetres, agent.ZMillimetres, 1_000).Where(id => id.CompareTo(agent.Id) > 0))
        {
            var value = agents.Single(item => item.Id == other);
            var dx = (long)value.XMillimetres - agent.XMillimetres;
            var dz = (long)value.ZMillimetres - agent.ZMillimetres;
            var squared = dx * dx + dz * dz;
            minimumSeparation = Math.Min(minimumSeparation, squared);
            if (squared == 0) exactOverlapPairTicks++;
            if (squared < 40_000)
            {
                var pair = (agent.Id, other);
                belowTwoHundredThisTick.Add(pair);
                var run = belowTwoHundredRuns.GetValueOrDefault(pair) + 1;
                belowTwoHundredRuns[pair] = run;
                maximumConsecutiveBelowTwoHundred = Math.Max(maximumConsecutiveBelowTwoHundred, run);
            }
        }
        foreach (var pair in belowTwoHundredRuns.Keys.Where(pair => !belowTwoHundredThisTick.Contains(pair)).ToArray()) belowTwoHundredRuns[pair] = 0;
        previousPositions = agents.ToDictionary(item => item.Id);
    }
    stopwatch.Stop();
    var snapshot = fixture.Session.CaptureSnapshot();
    var queue = snapshot.ServiceQueues.Single();
    var blocked = snapshot.NavigationAgents.Count(agent => !fixture.Session.TraversalGrid!.Get(TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres)).IsWalkable);
    Console.WriteLine($"scenario=fifty-agent-foundation agents={snapshot.NavigationAgents.Count} ticks={snapshot.CurrentTick} elapsed_ms={stopwatch.ElapsedMilliseconds}");
    Console.WriteLine($"transactions={snapshot.Transactions.Count} festival_cash_p={snapshot.FestivalFinances.Single().CashPennies} stock={snapshot.OwnedStocks.Single().Quantity} queue={queue.OrderedMembers.Count}");
    Console.WriteLine($"completed={queue.Agents.Count(item => item.Action == ServiceQueueAgentAction.Completed)} failed={queue.Agents.Count(item => item.Action == ServiceQueueAgentAction.Failed)} blocked_cells={blocked} blocked_swept_segments={blockedSweptSegments} repro_tick792_id21_sweep_legal={reproSweepLegal} max_step_mm={maximumStepMillimetres} min_every_tick_separation_mm={(minimumSeparation == long.MaxValue ? 0 : (long)Math.Sqrt(minimumSeparation))} exact_overlap_pair_ticks={exactOverlapPairTicks} max_consecutive_below_200mm_ticks={maximumConsecutiveBelowTwoHundred}");
    Console.WriteLine($"unfinished={string.Join(',', queue.Agents.Where(item => item.Action != ServiceQueueAgentAction.Completed).Select(item => $"{item.AgentId.Value}:{item.Action}:{snapshot.NavigationAgents.Single(nav => nav.Id == item.AgentId).Action}:{snapshot.NavigationAgents.Single(nav => nav.Id == item.AgentId).Destination}"))}");
    Console.WriteLine($"hash={snapshot.AuthoritativeHash}");
    Environment.ExitCode = FiftyAgentFoundationFixture.AllCompleted(fixture) && blocked == 0 && blockedSweptSegments == 0 && reproSweepLegal && exactOverlapPairTicks == 0 && maximumConsecutiveBelowTwoHundred <= 2 ? 0 : 1;
    return;
}

Console.WriteLine(ToolchainSmoke.GetFixedResult());

static BenchmarkReport RunBenchmark(int agents, BenchmarkPassage passage, int requestedSpeed)
{
    var initialization = Stopwatch.StartNew();
    var fixture = CrowdBenchmarkFixture.Create(agents, passage);
    initialization.Stop();
    var warmupTicks = agents >= 1200 ? 4 : CrowdBenchmarkMeasurement.RepresentativeWarmupTicks;
    var measurementTicks = agents >= 1200 ? 12 : 300;
    Console.Error.WriteLine($"benchmark phase=warmup agents={agents} passage={passage} ticks={warmupTicks}");
    for (var i = 0; i < warmupTicks; i++) fixture.AdvanceOneTick();
    var activeSessionsAtMeasurementStart = fixture.ActiveSessionCount;
    var activeAgentsAtMeasurementStart = fixture.ActiveAgentCount;
    var samples = new List<double>(measurementTicks);
    var timer = new Stopwatch();
    var process = Process.GetCurrentProcess();
    var peakBytes = process.WorkingSet64;
    for (var i = 0; i < measurementTicks; i++)
    {
        timer.Restart(); fixture.AdvanceOneTick(); timer.Stop(); samples.Add(timer.Elapsed.TotalMilliseconds);
        if ((i & 63) == 0) { process.Refresh(); peakBytes = Math.Max(peakBytes, process.WorkingSet64); }
    }
    var measurementCompleted = fixture.Completed;
    var activeSessionsAtMeasurementEnd = fixture.ActiveSessionCount;
    var activeAgentsAtMeasurementEnd = fixture.ActiveAgentCount;
    Console.Error.WriteLine($"benchmark phase=measured agents={agents} tick={fixture.ControllerTick} completed={measurementCompleted}");
    var completionLimit = agents >= 1200 ? fixture.ControllerTick : 20_000;
    while (!fixture.AllCompleted && fixture.ControllerTick < completionLimit)
    {
        fixture.AdvanceOneTick();
        if (fixture.ControllerTick % 1000 == 0) Console.Error.WriteLine($"benchmark phase=completion tick={fixture.ControllerTick} completed={fixture.Completed}");
    }
    process.Refresh(); peakBytes = Math.Max(peakBytes, process.PeakWorkingSet64);
    samples.Sort();
    double Percentile(double p) => samples[Math.Clamp((int)Math.Ceiling(samples.Count * p) - 1, 0, samples.Count - 1)];
    var average = samples.Average();
    return new BenchmarkReport(agents, passage.ToString().ToLowerInvariant(), requestedSpeed, CrowdBenchmarkFixture.Seed,
        ToolchainSmoke.BuildVersion, "m0-rules-v1", RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription,
        Environment.ProcessorCount, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, warmupTicks, measurementTicks,
        activeSessionsAtMeasurementStart, activeAgentsAtMeasurementStart, activeSessionsAtMeasurementEnd, activeAgentsAtMeasurementEnd,
        initialization.Elapsed.TotalMilliseconds, average, Percentile(.5), Percentile(.95), Percentile(.99),
        CrowdBenchmarkMeasurement.GameSpeedCapacity(average), peakBytes, measurementCompleted, fixture.Completed, fixture.Backlog, fixture.RouteFailures,
        fixture.TotalAgents - fixture.Completed, 0, fixture.AllCompleted && fixture.Reserved == 0,
        fixture.AllCompleted, fixture.ControllerTick, fixture.CompositeHash());
}

static SharedWorldFeasibilityReport RunSharedWorldFeasibility()
{
    var process = Process.GetCurrentProcess();
    var allocatedBefore = GC.GetTotalAllocatedBytes(true);
    var initialization = Stopwatch.StartNew();
    var fixture = SharedWorldFeasibilityFixture.Create();
    initialization.Stop();
    while (!SharedWorldFeasibilityFixture.IsRepresentativeActiveState(fixture) && fixture.Session.CurrentTick < 5_000)
        fixture.Session.AdvanceTicks(1);
    var representativeStartTick = fixture.Session.CurrentTick;
    var representative = SharedWorldFeasibilityFixture.IsRepresentativeActiveState(fixture);
    var samples = new List<double>(300);
    var timer = new Stopwatch();
    for (var tick = 0; tick < 300; tick++)
    {
        timer.Restart(); fixture.Session.AdvanceTicks(1); timer.Stop(); samples.Add(timer.Elapsed.TotalMilliseconds);
    }
    samples.Sort();
    double Percentile(double value) => samples[Math.Clamp((int)Math.Ceiling(samples.Count * value) - 1, 0, samples.Count - 1)];
    var mean = samples.Average();
    var mixedHash = fixture.Session.CaptureSnapshot().AuthoritativeHash;
    var persistenceTimer = Stopwatch.StartNew();
    var persisted = fixture.Session.CapturePersistenceSnapshot();
    persistenceTimer.Stop();
    var restoreTimer = Stopwatch.StartNew();
    var restored = GameSession.Restore(persisted);
    restoreTimer.Stop();
    if (!restored.IsSuccess) throw new InvalidOperationException(restored.Error);
    var restoredHash = restored.Session!.CaptureSnapshot().AuthoritativeHash;
    SharedWorldFeasibilityFixture.AdvanceUntilCompleted(fixture);
    process.Refresh();
    var final = fixture.Session.CaptureSnapshot();
    return new SharedWorldFeasibilityReport(
        SharedWorldFeasibilityFixture.AgentCount, final.ServiceQueues.Count, true, representative,
        representativeStartTick, 300, initialization.Elapsed.TotalMilliseconds,
        mean, Percentile(.5), Percentile(.95), Percentile(.99),
        CrowdBenchmarkMeasurement.GameSpeedCapacity(mean), process.PeakWorkingSet64,
        GC.GetTotalAllocatedBytes(false) - allocatedBefore, persistenceTimer.Elapsed.TotalMilliseconds,
        restoreTimer.Elapsed.TotalMilliseconds, mixedHash == restoredHash,
        final.Transactions.Count, final.ServiceQueues.SelectMany(queue => queue.Agents).Count(agent => agent.Action == ServiceQueueAgentAction.Failed),
        final.ServiceQueues.Sum(queue => queue.OrderedMembers.Count),
        final.ServiceQueues.All(queue => queue.Agents.All(agent => agent.ReservedSlotIndex is null && !agent.OwnsExitReservation)),
        SharedWorldFeasibilityFixture.AllCompleted(fixture), fixture.Session.CurrentTick, final.AuthoritativeHash);
}

static ScaleDiagnosticReport RunScaleDiagnostic(int population, ScaleDiagnosticLayout layout, ulong seed,
    string output, string sourceRevision, string sourceFingerprint)
{
    const int windowTicks = 120;
    var boundaryTickLimit = population == 200 ? 60_000 : 25_000;
    var probe = new ScaleDiagnosticProbe();
    var initialization = Stopwatch.StartNew();
    var fixture = ScaleDiagnosticFixture.Create(population, seed, layout, probe);
    initialization.Stop();
    var initial = fixture.Session.CaptureSnapshot();
    var windows = new List<ScaleDiagnosticWindowReport>();
    windows.Add(MeasureScaleWindow("arrival", fixture, probe, windowTicks));
    WriteScaleProgress(output, population, layout, seed, sourceRevision, sourceFingerprint, "arrival-complete", fixture, windows);

    var serviceBoundaryReached = AdvanceUntil(fixture, observation => HasCongestedServiceState(observation), boundaryTickLimit);
    if (serviceBoundaryReached) windows.Add(MeasureScaleWindow("congested-service", fixture, probe, windowTicks));
    WriteScaleProgress(output, population, layout, seed, sourceRevision, sourceFingerprint, "service-window-complete", fixture, windows);

    // Three completed purchases are enough to exercise accumulated ledgers while the deliberately
    // distant exits keep every declared attendee in an active (non-completed) lifecycle.
    var lateTransactionTarget = 3;
    var lateBoundaryReached = AdvanceUntil(fixture,
        observation => observation.TransactionCount >= lateTransactionTarget &&
            IsActiveScaleObservation(observation, fixture.Population), boundaryTickLimit);
    if (lateBoundaryReached) windows.Add(MeasureScaleWindow("early-transactions", fixture, probe, windowTicks));
    WriteScaleProgress(output, population, layout, seed, sourceRevision, sourceFingerprint, "transaction-window-complete", fixture, windows);

    var boundarySnapshot = fixture.Session.CaptureSnapshot();
    var persistence = fixture.Session.CapturePersistenceSnapshot();
    var restored = GameSession.Restore(persistence);
    var restoreHashEqual = restored.IsSuccess &&
        restored.Session!.CaptureSnapshot().AuthoritativeHash == boundarySnapshot.AuthoritativeHash;

    var completionObservation = fixture.Session.CaptureObservation();
    while (fixture.Session.CurrentTick < boundaryTickLimit &&
        completionObservation.ServiceQueues.SelectMany(queue => queue.Agents)
            .Any(agent => agent.Action is not ServiceQueueAgentAction.Failed and not ServiceQueueAgentAction.Completed))
    {
        fixture.Session.AdvanceWithoutSnapshot(1);
        completionObservation = fixture.Session.CaptureObservation();
        if (fixture.Session.CurrentTick % 1_000 == 0)
            WriteScaleProgress(output, population, layout, seed, sourceRevision, sourceFingerprint,
                "completion", fixture, windows);
    }

    var final = fixture.Session.CaptureSnapshot();
    var queueAgents = final.ServiceQueues.SelectMany(queue => queue.Agents).ToArray();
    var noFailures = queueAgents.All(agent => agent.Action != ServiceQueueAgentAction.Failed);
    var noBacklog = final.ServiceQueues.All(queue => queue.OrderedMembers.Count == 0 && queue.ActiveOwnerId is null);
    var released = queueAgents.All(agent => agent.ReservedSlotIndex is null && !agent.OwnsExitReservation);
    var completed = final.Transactions.Count == population && queueAgents.All(agent => agent.Action == ServiceQueueAgentAction.Completed);
    var walletsReconciled = final.Wallets.Count == population &&
        final.Wallets.Sum(wallet => wallet.CashPennies) == population * 200L;
    var salesReconciled = final.FestivalFinances.Single().CashPennies == population * ServiceQueueFixture.DefaultPricePennies &&
        final.OwnedStocks.Sum(stock => stock.Quantity) == population * 3 - population;
    var correctness = serviceBoundaryReached && lateBoundaryReached && restoreHashEqual && noFailures &&
        noBacklog && released && completed && walletsReconciled && salesReconciled;
    var structuralOverload = windows.Any(window => window.TickMeanMs >= 12.5);
    var slowTailOverload = windows.Any(window => window.TickP99Ms >= 12.5);
    return new ScaleDiagnosticReport(3, population, layout.ToString().ToLowerInvariant(), seed,
        ToolchainSmoke.BuildVersion, "s0.05-observation-v1", sourceRevision, sourceFingerprint, RuntimeInformation.OSDescription,
        RuntimeInformation.FrameworkDescription, Environment.ProcessorCount,
        initialization.Elapsed.TotalMilliseconds, initial.NavigationAgents.Count, initial.Wallets.Count,
        initial.ServiceQueues.Count, fixture.RetargetCount, windowTicks, windows,
        serviceBoundaryReached, lateBoundaryReached, lateTransactionTarget, restoreHashEqual,
        final.CurrentTick, final.Transactions.Count, completed, noFailures, noBacklog, released,
        walletsReconciled, salesReconciled, correctness, structuralOverload, slowTailOverload,
        Process.GetCurrentProcess().PeakWorkingSet64, final.AuthoritativeHash,
        "Headless unpaced diagnostic. Timed ticks include compact every-person invariant observation; no rendered frame pacing or OS input latency measured.");
}

static ScaleContentionTraceReport RunScaleContentionTrace()
{
    const int population = 100;
    const int sustainedTicks = 32;
    const int maximumTick = 25_000;
    var fixture = ScaleDiagnosticFixture.Create(population, 20260923, ScaleDiagnosticLayout.Representative);
    var ages = new Dictionary<EntityId, int>();
    var previousPositions = new Dictionary<EntityId, (int X, int Z)>();
    var history = new Queue<IReadOnlyList<ScaleContentionAgentObservation>>();
    EntityId? first = null;
    long firstTick = 0;
    IReadOnlyList<ScaleContentionAgentObservation> trace = [];
    IReadOnlyList<ScaleContentionAgentObservation> context = [];

    while (fixture.Session.CurrentTick < maximumTick)
    {
        var snapshot = fixture.Session.AdvanceTicks(1).Snapshot;
        var observations = CaptureContentionObservations(snapshot);
        history.Enqueue(observations);
        while (history.Count > sustainedTicks + 1) history.Dequeue();
        foreach (var observation in observations.Where(item => item.NavigationAction == AgentNavigationAction.Travelling))
        {
            var progressed = previousPositions.TryGetValue(observation.AgentId, out var previous) &&
                (observation.XMillimetres != previous.X || observation.ZMillimetres != previous.Z);
            ages[observation.AgentId] = progressed ? 0 : ages.GetValueOrDefault(observation.AgentId) + 1;
            previousPositions[observation.AgentId] = (observation.XMillimetres, observation.ZMillimetres);
            if (first is null && ages[observation.AgentId] >= sustainedTicks)
            {
                first = observation.AgentId;
                firstTick = snapshot.CurrentTick;
                trace = history.SelectMany(items => items.Where(item => item.AgentId == first.Value)).ToArray();
                context = observations.Where(item =>
                {
                    var dx = item.XMillimetres - observation.XMillimetres;
                    var dz = item.ZMillimetres - observation.ZMillimetres;
                    return (long)dx * dx + (long)dz * dz <= 1_000_000;
                }).ToArray();
            }
        }
        if (first is not null) break;
    }

    while (fixture.Session.CurrentTick < maximumTick &&
        fixture.Session.CaptureSnapshot().ServiceQueues.SelectMany(queue => queue.Agents)
            .Any(agent => agent.Action is not ServiceQueueAgentAction.Failed and not ServiceQueueAgentAction.Completed))
        fixture.Session.AdvanceTicks(1);

    var final = fixture.Session.CaptureSnapshot();
    var queueAgents = final.ServiceQueues.SelectMany(queue => queue.Agents).ToArray();
    var complete = final.Transactions.Count == population &&
        queueAgents.All(agent => agent.Action == ServiceQueueAgentAction.Completed) &&
        final.ServiceQueues.All(queue => queue.OrderedMembers.Count == 0 && queue.ActiveOwnerId is null) &&
        queueAgents.All(agent => agent.ReservedSlotIndex is null && !agent.OwnsExitReservation);
    return new ScaleContentionTraceReport(population, "representative", 20260923, sustainedTicks,
        first?.Value, firstTick, trace, context, final.CurrentTick, final.Transactions.Count, complete,
        final.AuthoritativeHash,
        "Non-authoritative trace: sustained means 32 consecutive travelling ticks with no authoritative position change; route index, next waypoint, destination, queue state and ownership are recorded to distinguish obstacle detours and queue transitions.");
}

static IReadOnlyList<ScaleContentionAgentObservation> CaptureContentionObservations(SessionSnapshot snapshot)
{
    var queues = snapshot.ServiceQueues
        .SelectMany(queue => queue.Agents.Select(agent => (queue, agent)))
        .ToDictionary(pair => pair.agent.AgentId);
    return snapshot.NavigationAgents.Select(agent =>
    {
        queues.TryGetValue(agent.Id, out var membership);
        var queueIndex = membership.queue?.OrderedMembers.ToList().IndexOf(agent.Id) ?? -1;
        return new ScaleContentionAgentObservation(snapshot.CurrentTick, agent.Id, agent.XMillimetres, agent.ZMillimetres,
            TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres), agent.Action, agent.Destination,
            agent.RouteIndex, agent.RouteIndex >= 0 && agent.RouteIndex < agent.Route.Count ? agent.Route[agent.RouteIndex] : null,
            agent.IntentId, membership.queue?.Id, membership.agent?.Action, membership.agent?.ReservedSlotIndex,
            queueIndex, membership.queue?.ActiveOwnerId);
    }).ToArray();
}

static bool AdvanceUntil(ScaleDiagnosticFixtureState fixture,
    Func<SessionObservation, bool> predicate, int maximumTick)
{
    var observation = fixture.Session.CaptureObservation();
    while (fixture.Session.CurrentTick < maximumTick)
    {
        if (predicate(observation)) return true;
        fixture.Session.AdvanceWithoutSnapshot(1);
        observation = fixture.Session.CaptureObservation();
    }
    return predicate(observation);
}

static ScaleDiagnosticWindowReport MeasureScaleWindow(string name, ScaleDiagnosticFixtureState fixture,
    ScaleDiagnosticProbe probe, int ticks)
{
    var allActive = IsActiveScaleObservation(fixture.Session.CaptureObservation(), fixture.Population);
    var before = probe.Capture();
    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var gc0 = GC.CollectionCount(0); var gc1 = GC.CollectionCount(1); var gc2 = GC.CollectionCount(2);
    var samples = new double[ticks];
    SessionObservation? last = null;
    var timer = new Stopwatch();
    for (var index = 0; index < ticks; index++)
    {
        timer.Restart(); fixture.Session.AdvanceWithoutSnapshot(1); var observation = fixture.Session.CaptureObservation(); timer.Stop();
        samples[index] = timer.Elapsed.TotalMilliseconds;
        last = observation;
        allActive &= IsActiveScaleObservation(observation, fixture.Population);
    }
    var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    var after = probe.Capture();
    Array.Sort(samples);
    double Percentile(double value) => samples[Math.Clamp((int)Math.Ceiling(samples.Length * value) - 1, 0, samples.Length - 1)];
    double Delta(double end, double start) => Math.Max(0, end - start);
    var routeMs = Delta(after.RouteSearchMs, before.RouteSearchMs);
    var navigationInclusive = Delta(after.NavigationInclusiveMs, before.NavigationInclusiveMs);
    var queueInclusive = Delta(after.QueueInclusiveMs, before.QueueInclusiveMs);
    var snapshotInclusive = Delta(after.SnapshotInclusiveMs, before.SnapshotInclusiveMs);
    var hashMs = Delta(after.HashMs, before.HashMs);
    var observationMs = Delta(after.ObservationMs, before.ObservationMs);
    var navigationRoute = Delta(after.NavigationRouteSearchMs, before.NavigationRouteSearchMs);
    var queueRoute = Delta(after.QueueRouteSearchMs, before.QueueRouteSearchMs);
    var measuredTotal = samples.Sum();
    var navigationExclusive = navigationInclusive - navigationRoute;
    var queueExclusive = queueInclusive - queueRoute;
    var snapshotExclusive = snapshotInclusive - hashMs;
    var accounted = routeMs + navigationExclusive + queueExclusive + snapshotExclusive + hashMs + observationMs;
    var reconciliationDelta = measuredTotal - accounted;
    var negativeTolerance = Math.Max(0.25, measuredTotal * 0.01);
    if (navigationExclusive < -negativeTolerance || queueExclusive < -negativeTolerance ||
        snapshotExclusive < -negativeTolerance || reconciliationDelta < -negativeTolerance)
        throw new InvalidOperationException($"Diagnostic category reconciliation exceeded tolerance: {reconciliationDelta:0.###} ms.");
    return new ScaleDiagnosticWindowReport(name, last!.CurrentTick - ticks + 1, last.CurrentTick, ticks,
        allActive, last.NavigationAgents.Count, last.ServiceQueues.Sum(queue => queue.OrderedMembers.Count),
        last.TransactionCount, samples.Average(), Percentile(.5), Percentile(.95), Percentile(.99),
        1000d / (80d * samples.Average()), measuredTotal, routeMs, navigationExclusive,
        queueExclusive, snapshotExclusive, hashMs, observationMs, reconciliationDelta,
        after.RouteSearches - before.RouteSearches, after.AvoidanceRouteSearches - before.AvoidanceRouteSearches,
        after.ExpandedNodes - before.ExpandedNodes, after.AvoidanceExpandedNodes - before.AvoidanceExpandedNodes,
        after.QueueReassignments - before.QueueReassignments, after.BlockedAgentTicks - before.BlockedAgentTicks,
        after.MaximumBlockedAgentAgeTicks, after.CurrentlyBlockedAgents, allocated,
        GC.CollectionCount(0) - gc0, GC.CollectionCount(1) - gc1, GC.CollectionCount(2) - gc2);
}

static bool IsActiveScaleObservation(SessionObservation observation, int population)
{
    var queueAgents = observation.ServiceQueues.SelectMany(queue => queue.Agents).ToArray();
    var navigationIds = observation.NavigationAgents.Select(agent => agent.Id).ToHashSet();
    var queueAgentIds = queueAgents.Select(agent => agent.AgentId).ToHashSet();
    return observation.NavigationAgents.Count == population && observation.WalletCount == population &&
        navigationIds.Count == population && queueAgents.Length == population && queueAgentIds.Count == population &&
        navigationIds.SetEquals(queueAgentIds) &&
        observation.NavigationAgents.All(agent => agent.Action != AgentNavigationAction.Idle) &&
        queueAgents.All(agent => agent.Action is not ServiceQueueAgentAction.Failed and not ServiceQueueAgentAction.Completed) &&
        observation.ServiceQueues.All(queue =>
            queue.OrderedMembers.Count == queue.OrderedMembers.Distinct().Count() &&
            queue.OrderedMembers.All(queueAgentIds.Contains) &&
            queue.Agents.Where(agent => agent.ReservedSlotIndex is not null).Select(agent => agent.ReservedSlotIndex).Distinct().Count() ==
                queue.Agents.Count(agent => agent.ReservedSlotIndex is not null) &&
            queue.Agents.Where(agent => agent.OwnsExitReservation).Select(agent => agent.ExitIndex).Distinct().Count() ==
                queue.Agents.Count(agent => agent.OwnsExitReservation) &&
            (queue.ActiveOwnerId is null || queue.Agents.Any(agent =>
                agent.AgentId == queue.ActiveOwnerId && agent.Action == ServiceQueueAgentAction.InService)));
}

static bool HasCongestedServiceState(SessionObservation observation) =>
    observation.TransactionCount == 0 &&
    observation.ServiceQueues.All(queue => queue.OrderedMembers.Count > 0 || queue.ActiveOwnerId is not null);

static void WriteScaleProgress(string output, int population, ScaleDiagnosticLayout layout, ulong seed,
    string sourceRevision, string sourceFingerprint, string phase, ScaleDiagnosticFixtureState fixture,
    IReadOnlyList<ScaleDiagnosticWindowReport> windows)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    var progress = new ScaleDiagnosticProgressReport(3, population, layout.ToString().ToLowerInvariant(), seed,
        ToolchainSmoke.BuildVersion, "s0.05-observation-v1", sourceRevision, sourceFingerprint,
        phase, fixture.Session.CurrentTick, fixture.Session.Transactions.Count, windows,
        "Partial checkpoint; a wall-time timeout may stop the exact child before final reconciliation.");
    File.WriteAllText(output, JsonSerializer.Serialize(progress, new JsonSerializerOptions { WriteIndented = true }));
}

sealed record BenchmarkReport(int Agents, string Passage, int RequestedSpeed, ulong Seed, string Build, string Ruleset,
    string OS, string Runtime, int LogicalProcessors, long AvailableMemoryBytes, int WarmupTicks, int MeasurementTicks,
    int ActiveSessionsAtMeasurementStart, int ActiveAgentsAtMeasurementStart, int ActiveSessionsAtMeasurementEnd, int ActiveAgentsAtMeasurementEnd,
    double InitializationMs, double TickMeanMs, double TickP50Ms, double TickP95Ms, double TickP99Ms, double UnpacedGameSpeedCapacity,
    long ProcessPeakWorkingSetBytes, int CompletedDuringMeasurement, int Completed, int Backlog, int RouteFailures,
    int StuckOrUnfinished, int AssistedRecoveries, bool ReleasedReservations, bool FullScenarioCompleted,
    long FinalControllerTick, string AuthoritativeHash);

sealed record SharedWorldFeasibilityReport(
    int Agents, int Destinations, bool SingleSharedWorld, bool RepresentativePopulation,
    long RepresentativeStartTick, int MeasurementTicks, double InitializationMs,
    double TickMeanMs, double TickP50Ms, double TickP95Ms, double TickP99Ms,
    double UnpacedGameSpeedCapacity, long ProcessPeakWorkingSetBytes, long AllocatedBytes,
    double PersistenceCaptureMs, double PersistenceRestoreMs, bool RestoreHashEqual,
    int CompletedServices, int RouteFailures, int Backlog, bool ReleasedReservations,
    bool FunctionallyComplete, long FinalTick, string AuthoritativeHash);

sealed record ScaleDiagnosticReport(
    int SchemaVersion, int Population, string Layout, ulong Seed, string Build, string Ruleset,
    string SourceRevision, string SourceDiffFingerprint, string OS, string Runtime,
    int LogicalProcessors, double InitializationMs, int InitialNavigationAgents, int InitialWallets,
    int Destinations, int RetargetCount, int WindowTicks, IReadOnlyList<ScaleDiagnosticWindowReport> Windows,
    bool ServiceBoundaryReached, bool LateBoundaryReached, int LateTransactionTarget, bool RestoreHashEqual,
    long FinalTick, int Transactions, bool FunctionallyComplete, bool NoRouteFailures, bool NoBacklog,
    bool ReleasedReservations, bool WalletsReconciled, bool SalesReconciled, bool CorrectnessPassed,
    bool StructuralOverloadAtOneX, bool SlowTailOverloadAtOneX, long ProcessPeakWorkingSetBytes,
    string AuthoritativeHash, string Limitations);

sealed record ScaleDiagnosticProgressReport(
    int SchemaVersion, int Population, string Layout, ulong Seed, string Build, string Ruleset,
    string SourceRevision, string SourceDiffFingerprint, string Phase, long CurrentTick,
    int Transactions, IReadOnlyList<ScaleDiagnosticWindowReport> Windows, string Limitations);

sealed record ScaleContentionTraceReport(
    int Population, string Layout, ulong Seed, int SustainedThresholdTicks,
    ulong? FirstAgentId, long FirstSustainedTick, IReadOnlyList<ScaleContentionAgentObservation> Trace,
    IReadOnlyList<ScaleContentionAgentObservation> NearbyContextAtTrigger,
    long FinalTick, int Transactions, bool FunctionallyComplete, string AuthoritativeHash, string Definition);

sealed record ScaleContentionAgentObservation(
    long Tick, EntityId AgentId, int XMillimetres, int ZMillimetres, GridCell Cell,
    AgentNavigationAction NavigationAction, GridCell? Destination, int RouteIndex, GridCell? NextRouteCell,
    string? IntentId, EntityId? QueueId, ServiceQueueAgentAction? QueueAction, int? ReservedSlotIndex,
    int QueueOrderIndex, EntityId? ActiveOwnerId);

sealed record ScaleDiagnosticWindowReport(
    string Name, long StartTick, long EndTick, int Ticks, bool AllDeclaredAgentsActive,
    int ActivePopulation, int QueueBacklog, int Transactions, double TickMeanMs, double TickP50Ms,
    double TickP95Ms, double TickP99Ms, double UnpacedOneXCapacity, double MeasuredTotalMs,
    double RouteSearchMs, double MovementAndSeparationExclusiveMs, double QueueExclusiveMs,
    double SnapshotExclusiveMs, double HashMs, double ObservationMs, double UnattributedHarnessAndTickMs,
    long PathSearches, long AvoidancePathSearches, long ExpandedNodes, long AvoidanceExpandedNodes,
    long QueueReassignments, long BlockedAgentTicks, int MaximumBlockedAgentAgeTicks,
    int BlockedAgentsAtEnd, long AllocatedBytes, int Gen0Collections, int Gen1Collections, int Gen2Collections);

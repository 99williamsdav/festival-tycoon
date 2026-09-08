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

Console.WriteLine(ToolchainSmoke.GetFixedResult());

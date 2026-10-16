// Every test builds its own session and uses unique temp paths, so tests run in parallel
// across all cores. Soak runs are tagged [TestCategory("Slow")]; tools/test.ps1 skips them
// unless -Full is passed, and its everyday run adds back the few tagged [TestCategory("Everyday")].
// The project uses the server GC (see Festival.Tests.csproj): with the workstation GC, twenty
// threads simulating festival days spent most of their time waiting on collections.
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]

namespace Festival.Tests;

[TestClass]
public static class TestRunSetup
{
    /// <summary>
    /// Every parallel worker blocks a thread-pool thread for its whole test, so background saves
    /// (SessionHost's cadence save, the preparation draft pipeline) queued behind them waited for the
    /// pool to grow a thread at a time, and their 30-second waits timed out under a full run. Start the
    /// pool with room for a background task beside every worker. Test-only: the game never runs this.
    /// </summary>
    [AssemblyInitialize]
    public static void GiveBackgroundSavesThreads(TestContext _)
    {
        ThreadPool.GetMinThreads(out var workers, out var io);
        ThreadPool.SetMinThreads(Math.Max(workers, Environment.ProcessorCount * 4), io);
    }
}

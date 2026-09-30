// Every test builds its own session and uses unique temp paths, so tests run in parallel
// across all cores. Soak runs are tagged [TestCategory("Slow")]; tools/test.ps1 skips them
// unless -Slow is passed.
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]

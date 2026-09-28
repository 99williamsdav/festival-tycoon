using System.Diagnostics;

namespace Festival.Persistence;

/// <summary>Opt-in diagnostic stage timings; no production observer is installed.</summary>
public static class PersistenceTiming
{
    public static Action<string, double>? Observer { get; set; }
    public static long Start() => Observer is null ? 0 : Stopwatch.GetTimestamp();
    public static void Record(string stage, long started)
    {
        if (started != 0) Observer?.Invoke(stage, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
}

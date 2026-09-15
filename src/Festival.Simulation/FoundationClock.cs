namespace Festival.Simulation;

/// <summary>
/// Render-loop scheduler for fixed authoritative ticks. Wall time never enters simulation state.
/// Work is capped per frame; excess real-time demand is reported as reduced attained speed.
/// </summary>
public sealed class FoundationClock
{
    public const int OneXTickRate = 80;
    public const int MaximumTicksPerFrame = 16;
    public const double MaximumDebtTicks = 320;

    private double _tickDebt;
    private double _requestedTicks;
    private long _processedTicks;

    public bool IsPaused { get; set; }
    public RequestedSpeed RequestedSpeed { get; set; } = RequestedSpeed.OneX;
    public bool IsOverloaded { get; private set; }
    public double DebtTicks => _tickDebt;
    public double AttainedSpeed => _requestedTicks <= 0 ? 0 : (double)_processedTicks / _requestedTicks * (int)RequestedSpeed;

    public int Schedule(double realSeconds, int? workCap = null)
    {
        if (realSeconds < 0 || double.IsNaN(realSeconds) || double.IsInfinity(realSeconds))
            throw new ArgumentOutOfRangeException(nameof(realSeconds));
        if (IsPaused) { IsOverloaded = false; return 0; }
        var requested = realSeconds * OneXTickRate * (int)RequestedSpeed;
        _requestedTicks += requested;
        _tickDebt = Math.Min(MaximumDebtTicks, _tickDebt + requested);
        var cap = Math.Clamp(workCap ?? MaximumTicksPerFrame, 0, MaximumTicksPerFrame);
        var scheduled = Math.Min((int)_tickDebt, cap);
        _tickDebt -= scheduled;
        _processedTicks += scheduled;
        IsOverloaded = _tickDebt >= 1 || requested > cap;
        return scheduled;
    }

    public void ResetMeasurement() { _requestedTicks = 0; _processedTicks = 0; IsOverloaded = false; }
}

/// <summary>Presentation-only deterministic palette assignment; excluded from authoritative state/hash.</summary>
public static class AttendeePaletteAssignment
{
    public const int PaletteCount = 10;
    public static int FromOrdinal(int ordinal) => (int)(Math.Abs((long)ordinal) % PaletteCount);
    public static int FromStableId(EntityId id) => (int)(id.Value % PaletteCount);
}

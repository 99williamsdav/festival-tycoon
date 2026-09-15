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
    private readonly Queue<(double Requested, int Processed)> _samples = new();
    private double _windowRequestedTicks;
    private long _windowProcessedTicks;

    public bool IsPaused { get; set; }
    public RequestedSpeed RequestedSpeed { get; set; } = RequestedSpeed.OneX;
    public bool IsOverloaded { get; private set; }
    public double DebtTicks => _tickDebt;
    public double InterpolationFraction => _tickDebt - Math.Floor(_tickDebt);
    public double AttainedSpeed => _windowRequestedTicks <= 0 ? 0 : (double)_windowProcessedTicks / _windowRequestedTicks * (int)RequestedSpeed;

    public int Schedule(double realSeconds, int? workCap = null)
    {
        if (realSeconds < 0 || double.IsNaN(realSeconds) || double.IsInfinity(realSeconds))
            throw new ArgumentOutOfRangeException(nameof(realSeconds));
        if (IsPaused) { IsOverloaded = false; return 0; }
        var requested = realSeconds * OneXTickRate * (int)RequestedSpeed;
        _tickDebt = Math.Min(MaximumDebtTicks, _tickDebt + requested);
        var cap = Math.Clamp(workCap ?? MaximumTicksPerFrame, 0, MaximumTicksPerFrame);
        var scheduled = Math.Min((int)_tickDebt, cap);
        _tickDebt -= scheduled;
        _samples.Enqueue((requested, scheduled));
        _windowRequestedTicks += requested; _windowProcessedTicks += scheduled;
        while (_samples.Count > 120)
        {
            var removed = _samples.Dequeue(); _windowRequestedTicks -= removed.Requested; _windowProcessedTicks -= removed.Processed;
        }
        IsOverloaded = _tickDebt >= 1 || requested > cap;
        return scheduled;
    }

    public void ResetMeasurement() { _samples.Clear(); _windowRequestedTicks = 0; _windowProcessedTicks = 0; IsOverloaded = false; }
    public void ResetBoundary() { _tickDebt = 0; ResetMeasurement(); }
}

/// <summary>Presentation-only deterministic palette assignment; excluded from authoritative state/hash.</summary>
public static class AttendeePaletteAssignment
{
    public const int PaletteCount = 10;
    public static int FromOrdinal(int ordinal) => (int)(Math.Abs((long)ordinal) % PaletteCount);
    public static int FromStableId(EntityId id) => (int)(id.Value % PaletteCount);
}

public sealed record FoundationDiagnosticCounts(int Travelling, int Waiting, int InService, int Served, int Failed);

public static class FoundationDiagnostics
{
    public static FoundationDiagnosticCounts Count(SessionSnapshot snapshot)
    {
        var queue = snapshot.ServiceQueues.Single();
        return new(
            snapshot.NavigationAgents.Count(item => item.Action == AgentNavigationAction.Travelling),
            queue.Agents.Count(item => item.Action == ServiceQueueAgentAction.Waiting),
            queue.Agents.Count(item => item.Action == ServiceQueueAgentAction.InService),
            snapshot.Transactions.Count,
            queue.Agents.Count(item => item.Action == ServiceQueueAgentAction.Failed));
    }
}

/// <summary>Cosmetic previous/current positions. Sampling cannot mutate authoritative session state.</summary>
public sealed class FoundationPresentationInterpolator
{
    private readonly Dictionary<EntityId, ((int X, int Z) Previous, (int X, int Z) Current)> _positions = [];

    public void Reset(SessionSnapshot snapshot)
    {
        _positions.Clear();
        foreach (var agent in snapshot.NavigationAgents)
            _positions.Add(agent.Id, ((agent.XMillimetres, agent.ZMillimetres), (agent.XMillimetres, agent.ZMillimetres)));
    }

    public void Advance(SessionSnapshot snapshot)
    {
        if (snapshot.NavigationAgents.Count != _positions.Count || snapshot.NavigationAgents.Any(item => !_positions.ContainsKey(item.Id)))
        { Reset(snapshot); return; }
        foreach (var agent in snapshot.NavigationAgents)
            _positions[agent.Id] = (_positions[agent.Id].Current, (agent.XMillimetres, agent.ZMillimetres));
    }

    public (double XMillimetres, double ZMillimetres) Sample(EntityId id, double fraction)
    {
        var value = _positions[id];
        var amount = Math.Clamp(fraction, 0, 1);
        return (value.Previous.X + (value.Current.X - value.Previous.X) * amount,
            value.Previous.Z + (value.Current.Z - value.Previous.Z) * amount);
    }
}

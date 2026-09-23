using System.Diagnostics;

namespace Festival.Simulation;

/// <summary>
/// Development-only, non-authoritative timings and counters for the bounded S0.00 probe.
/// Nothing in this type participates in simulation decisions, hashing or persistence.
/// </summary>
public sealed class ScaleDiagnosticProbe
{
    private readonly Dictionary<EntityId, int> _blockedAges = [];
    private DiagnosticPhase _phase;

    public long RouteSearchStopwatchTicks { get; private set; }
    public long NavigationRouteSearchStopwatchTicks { get; private set; }
    public long QueueRouteSearchStopwatchTicks { get; private set; }
    public long NavigationStopwatchTicks { get; private set; }
    public long QueueStopwatchTicks { get; private set; }
    public long SnapshotStopwatchTicks { get; private set; }
    public long HashStopwatchTicks { get; private set; }
    public long RouteSearches { get; private set; }
    public long AvoidanceRouteSearches { get; private set; }
    public long ExpandedNodes { get; private set; }
    public long AvoidanceExpandedNodes { get; private set; }
    public long QueueReassignments { get; private set; }
    public long BlockedAgentTicks { get; private set; }
    public int MaximumBlockedAgentAgeTicks { get; private set; }
    public int CurrentlyBlockedAgents => _blockedAges.Count;

    internal DiagnosticPhase Phase => _phase;
    internal void SetPhase(DiagnosticPhase phase) => _phase = phase;
    internal void AddNavigation(long ticks) => NavigationStopwatchTicks += ticks;
    internal void AddQueue(long ticks) => QueueStopwatchTicks += ticks;
    internal void AddSnapshot(long ticks) => SnapshotStopwatchTicks += ticks;
    internal void AddHash(long ticks) => HashStopwatchTicks += ticks;
    internal void AddRouteSearch(long ticks, int expandedNodes, bool avoidance)
    {
        RouteSearchStopwatchTicks += ticks;
        if (_phase == DiagnosticPhase.Navigation) NavigationRouteSearchStopwatchTicks += ticks;
        if (_phase == DiagnosticPhase.Queue) QueueRouteSearchStopwatchTicks += ticks;
        RouteSearches++;
        ExpandedNodes += expandedNodes;
        if (!avoidance) return;
        AvoidanceRouteSearches++;
        AvoidanceExpandedNodes += expandedNodes;
    }
    internal void AddQueueReassignment() => QueueReassignments++;
    internal void RecordBlocked(EntityId id)
    {
        var age = _blockedAges.GetValueOrDefault(id) + 1;
        _blockedAges[id] = age;
        BlockedAgentTicks++;
        MaximumBlockedAgentAgeTicks = Math.Max(MaximumBlockedAgentAgeTicks, age);
    }
    internal void RecordProgress(EntityId id) => _blockedAges.Remove(id);

    public ScaleDiagnosticProbeSnapshot Capture() => new(
        ToMilliseconds(RouteSearchStopwatchTicks), ToMilliseconds(NavigationStopwatchTicks),
        ToMilliseconds(QueueStopwatchTicks), ToMilliseconds(SnapshotStopwatchTicks),
        ToMilliseconds(HashStopwatchTicks), ToMilliseconds(NavigationRouteSearchStopwatchTicks),
        ToMilliseconds(QueueRouteSearchStopwatchTicks), RouteSearches, AvoidanceRouteSearches,
        ExpandedNodes, AvoidanceExpandedNodes, QueueReassignments, BlockedAgentTicks,
        MaximumBlockedAgentAgeTicks, CurrentlyBlockedAgents);

    private static double ToMilliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
}

internal enum DiagnosticPhase { None, Navigation, Queue, Snapshot }

public sealed record ScaleDiagnosticProbeSnapshot(
    double RouteSearchMs, double NavigationInclusiveMs, double QueueInclusiveMs,
    double SnapshotInclusiveMs, double HashMs, double NavigationRouteSearchMs,
    double QueueRouteSearchMs, long RouteSearches,
    long AvoidanceRouteSearches, long ExpandedNodes, long AvoidanceExpandedNodes,
    long QueueReassignments, long BlockedAgentTicks, int MaximumBlockedAgentAgeTicks,
    int CurrentlyBlockedAgents);

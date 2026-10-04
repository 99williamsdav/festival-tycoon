namespace Festival.Simulation;

/// <summary>One deterministic estimate and hysteresis policy for interchangeable physical queues.</summary>
public static class QueuedServiceChoice
{
    public const int ReviewIntervalTicks = 160;
    public const int ReviewStagger = 8;
    public const int SwitchMarginTicks = 120;

    public sealed record Member(ulong AgentId, int ExpectedServiceTicks);
    public sealed record Approacher(ulong AgentId, int ArrivalTicks, int ExpectedServiceTicks);
    public sealed record Candidate(string Id, int WalkTicks, int OwnServiceTicks, bool CanServe, bool CanJoin,
        IReadOnlyList<Member> PhysicalOrder, ulong? OwnerId, int OwnerRemainingTicks,
        IReadOnlyList<Approacher> Approaching);
    public sealed record Decision(string Id, int TotalTicks, int CurrentTicks, bool Switched);

    public static bool ReviewDue(long tick, ulong agentId, long lastReviewTick) =>
        tick - lastReviewTick >= ReviewIntervalTicks && tick % ReviewStagger == (long)(agentId % ReviewStagger);

    public static int EstimateTicks(ulong agentId, Candidate candidate)
    {
        var position = -1;
        for (var i = 0; i < candidate.PhysicalOrder.Count; i++)
            if (candidate.PhysicalOrder[i].AgentId == agentId) { position = i; break; }
        if (!candidate.CanServe || (!candidate.CanJoin && position < 0) || candidate.WalkTicks == int.MaxValue ||
            candidate.OwnServiceTicks <= 0) return int.MaxValue;

        long wait = 0;
        var ahead = position < 0 ? candidate.PhysicalOrder.Count : position;
        for (var i = 0; i < ahead; i++)
        {
            var member = candidate.PhysicalOrder[i];
            wait += member.AgentId == candidate.OwnerId ? candidate.OwnerRemainingTicks : member.ExpectedServiceTicks;
        }
        // This is expected demand, never a reserved place. A person with an
        // earlier estimated arrival may join before us; a physical member may not.
        if (position < 0)
            foreach (var approaching in candidate.Approaching)
                if (approaching.AgentId != agentId &&
                    (approaching.ArrivalTicks < candidate.WalkTicks ||
                     approaching.ArrivalTicks == candidate.WalkTicks && approaching.AgentId < agentId))
                    wait += approaching.ExpectedServiceTicks;
        var total = (long)candidate.WalkTicks + wait + candidate.OwnServiceTicks;
        return total >= int.MaxValue ? int.MaxValue : (int)total;
    }

    public static Decision? Choose(ulong agentId, string? currentId, IReadOnlyList<Candidate> candidates)
    {
        var scored = candidates.Select(candidate => (candidate.Id, Ticks: EstimateTicks(agentId, candidate)))
            .Where(item => item.Ticks != int.MaxValue)
            .OrderBy(item => item.Ticks).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (scored.Length == 0) return null;
        var best = scored[0];
        if (currentId is null) return new(best.Id, best.Ticks, int.MaxValue, false);
        var current = candidates.FirstOrDefault(candidate => candidate.Id == currentId);
        var currentTicks = current is null ? int.MaxValue : EstimateTicks(agentId, current);
        if (best.Id == currentId || currentTicks != int.MaxValue &&
            (long)best.Ticks + SwitchMarginTicks >= currentTicks)
            return new(currentId, currentTicks, currentTicks, false);
        return new(best.Id, best.Ticks, currentTicks, true);
    }
}

public sealed partial class GameSession
{
    // Use the active route when it already targets this approach. A different
    // candidate needs one bounded route search at a simulation decision tick.
    private int EstimateQueuedServiceWalkTicks(ulong id, GridCell destination)
    {
        var nav = _navigationAgents[new(id)];
        var from = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
        IReadOnlyList<GridCell> remaining;
        if (nav.Destination == destination && nav.Action == AgentNavigationAction.Arrived) return 0;
        if (nav.Destination == destination && nav.Action == AgentNavigationAction.Travelling &&
            nav.RouteIndex >= 0 && nav.RouteIndex < nav.Route.Count)
            remaining = nav.Route.Skip(nav.RouteIndex).ToArray();
        else
        {
            var search = DeterministicPathfinder.FindPath(_traversalGrid!, from, destination);
            if (!search.Found) return int.MaxValue;
            remaining = search.Path.Skip(1).ToArray();
        }
        var x = nav.XMillimetres; var z = nav.ZMillimetres;
        long weightedMicrometres = 0;
        foreach (var cell in remaining)
        {
            var centre = TraversalGrid.CellCentre(cell);
            var dx = centre.XMillimetres - x; var dz = centre.ZMillimetres - z;
            var segment = IntegerSquareRoot((long)dx * dx * 1_000_000L + (long)dz * dz * 1_000_000L);
            weightedMicrometres += segment * _traversalGrid!.Get(cell).CostPermille / 1000;
            x = centre.XMillimetres; z = centre.ZMillimetres;
        }
        var perTick = (long)RouteProgressMicrometresPerTick * nav.WalkingSpeedPermille / 1000 * PersonPacePermille(id) / 1000;
        return perTick <= 0 ? int.MaxValue : checked((int)((weightedMicrometres + perTick - 1) / perTick));
    }
}

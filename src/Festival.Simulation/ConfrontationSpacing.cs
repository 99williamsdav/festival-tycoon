namespace Festival.Simulation;

public sealed partial class GameSession
{
    // Conservative margin above the Designer's paired-mesh clearance floor; grid goals land near 1 m.
    public const int ConfrontationPreferredMinMillimetres = 900, ConfrontationPreferredMaxMillimetres = 1000;
    private const int ConfrontationApproachMaximumMillimetres = 750, ConfrontationFallbackMaxMillimetres = 1250;

    private (GridCell First, GridCell Second)? CloserConfrontationCells(ulong firstId, ulong secondId)
    {
        var first = _navigationAgents[new(firstId)]; var second = _navigationAgents[new(secondId)];
        var gap = DistanceSquared(first.XMillimetres, first.ZMillimetres, second.XMillimetres, second.ZMillimetres);
        if (gap <= (long)ConfrontationPreferredMaxMillimetres * ConfrontationPreferredMaxMillimetres) return null;
        var queues = new HashSet<GridCell>(ImmersionQueueCorridor());
        foreach (var tap in WaterPoints()) queues.UnionWith(LooseQueueGeometry.Corridor(CaptureWaterQueueCells(tap.Id)));
        foreach (var toilet in Toilets) queues.UnionWith([ToiletExitCell(toilet), ToiletDoorFrontCell(toilet)]);
        var others = _navigationAgents.Values.Where(n => n.Id.Value != firstId && n.Id.Value != secondId && MovementOccupant(n.Id.Value)).ToArray();
        bool Clear(int x, int z)
        {
            var cell = TraversalGrid.WorldToCell(x, z);
            return _traversalGrid!.Get(cell).IsWalkable && !queues.Any(q => Math.Abs(q.X - cell.X) <= 1 && Math.Abs(q.Z - cell.Z) <= 1) &&
                others.All(n => DistanceSquared(x, z, n.XMillimetres, n.ZMillimetres) >= 750L * 750 && n.Destination != cell);
        }
        List<(GridCell Cell, int X, int Z, long Move)> Candidates(NavigationAgentState actor, bool fixedPoint)
        {
            var cell = TraversalGrid.WorldToCell(actor.XMillimetres, actor.ZMillimetres);
            var result = new List<(GridCell, int, int, long)>();
            for (var dz = -1; dz <= 1; dz++) for (var dx = -1; dx <= 1; dx++)
            {
                if (fixedPoint && (dx != 0 || dz != 0)) continue;
                var candidate = new GridCell(cell.X + dx, cell.Z + dz);
                var centre = candidate == cell ? (actor.XMillimetres, actor.ZMillimetres) : TraversalGrid.CellCentre(candidate);
                var move = DistanceSquared(actor.XMillimetres, actor.ZMillimetres, centre.Item1, centre.Item2);
                if (move > (long)ConfrontationApproachMaximumMillimetres * ConfrontationApproachMaximumMillimetres || !Clear(centre.Item1, centre.Item2)) continue;
                // A short, direct, swept legal movement only; no long detour to make the acting look closer.
                if (!TraversalSweep.IsWalkable(_traversalGrid!, actor.XMillimetres, actor.ZMillimetres, centre.Item1, centre.Item2)) continue;
                var path = DeterministicPathfinder.FindPath(_traversalGrid!, cell, candidate, _groundRouteCost);
                if (!path.Found || path.Path.Count > 2) continue;
                var safe = true;
                for (var step = 0; step <= 8; step++)
                    safe &= Clear(actor.XMillimetres + (centre.Item1 - actor.XMillimetres) * step / 8, actor.ZMillimetres + (centre.Item2 - actor.ZMillimetres) * step / 8);
                if (safe) result.Add((candidate, centre.Item1, centre.Item2, move));
            }
            return result;
        }
        // The actual steward opponent retains their response/navigation ownership.
        var a = Candidates(first, false); var b = Candidates(second, IsSteward(secondId));
        var plans = from left in a from right in b
                    let separation = DistanceSquared(left.X, left.Z, right.X, right.Z)
                    where separation >= (long)ConfrontationPreferredMinMillimetres * ConfrontationPreferredMinMillimetres &&
                        separation <= (long)ConfrontationFallbackMaxMillimetres * ConfrontationFallbackMaxMillimetres && separation < gap
                    orderby separation > (long)ConfrontationPreferredMaxMillimetres * ConfrontationPreferredMaxMillimetres,
                        separation, Math.Max(left.Move, right.Move), left.Move + right.Move, left.Cell, right.Cell
                    select (Left: left, Right: right);
        foreach (var plan in plans)
        {
            if (IsSteward(secondId)) return (plan.Left.Cell, plan.Right.Cell);
            // Keep a legal common steward approach ring. A constrained site falls back to the original reservation.
            var approach = Enumerable.Range(-3, 7).SelectMany(x => Enumerable.Range(-3, 7).Select(z => new GridCell(plan.Left.Cell.X + x, plan.Left.Cell.Z + z)))
                .Where(c => c != plan.Left.Cell && c != plan.Right.Cell && _traversalGrid!.Get(c).IsWalkable && !queues.Contains(c))
                .FirstOrDefault(c => { var p = TraversalGrid.CellCentre(c); var d1 = DistanceSquared(p.XMillimetres, p.ZMillimetres, plan.Left.X, plan.Left.Z); var d2 = DistanceSquared(p.XMillimetres, p.ZMillimetres, plan.Right.X, plan.Right.Z);
                    return d1 is >= 250_000 and <= StewardAttendanceDistanceSquared && d2 is >= 250_000 and <= StewardAttendanceDistanceSquared &&
                        TraversalSweep.IsWalkable(_traversalGrid!, p.XMillimetres, p.ZMillimetres, plan.Left.X, plan.Left.Z) && TraversalSweep.IsWalkable(_traversalGrid!, p.XMillimetres, p.ZMillimetres, plan.Right.X, plan.Right.Z) &&
                        others.Where(n => IsSteward(n.Id.Value)).Any(n => DeterministicPathfinder.FindPath(_traversalGrid!, TraversalGrid.WorldToCell(n.XMillimetres, n.ZMillimetres), c).Found); });
            if (approach == default) continue;
            return (plan.Left.Cell, plan.Right.Cell);
        }
        return null;
    }
    private static long DistanceSquared(int x, int z, int otherX, int otherZ)
    { var dx = (long)x - otherX; var dz = (long)z - otherZ; return dx * dx + dz * dz; }

    private void StopConfrontationApproach(DisorderIncidentOrigin origin)
    {
        foreach (var id in new[] { origin.InitiatorId, origin.OpponentId })
        {
            var nav = _navigationAgents[new(id)];
            if (nav.IntentId == "disorder.confrontation" && nav.Action == AgentNavigationAction.Travelling)
                ApplyAgentDestination(new(id), new(TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres), "disorder.confrontation"));
        }
    }
}

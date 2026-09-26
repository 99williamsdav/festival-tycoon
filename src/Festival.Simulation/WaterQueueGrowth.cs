namespace Festival.Simulation;

public sealed partial class GameSession
{
    // Only real members plus one unreserved arrival position are laid out. Up to 20
    // places, at most nine local candidates per extension; no recursive search.
    private bool GrowWaterQueue(string pointId)
    {
        var point = WaterPoints().Single(item => item.Id == pointId);
        var members = point.Queue.Length + point.Overflow.Length;
        var wanted = Math.Min(20, members + 1);
        var cells = point.QueueCells.Take(wanted).ToList();
        if (cells.Count == 0)
        {
            // Older saves keep occupied geometry and routes exactly, then grow locally.
            if (point.GeometryVersion == 0)
                for (var index = 0; index < members; index++) cells.Add(index < 10 ? WaterSlot(point, index) : WaterOverflowSlot(point, index - 10));
            if (cells.Count == 0) cells.Add(WaterPointServiceCell(point));
        }
        var forward = RotateWaterOffset(new(0, 1), point.QuarterTurns);
        var reserved=WaterPoints().Where(other=>other.Id!=pointId).SelectMany(other=>LooseQueueGeometry.Corridor(CaptureWaterQueueCells(other.Id))).Concat(ImmersionQueueCorridor()).ToArray();
        while (cells.Count < wanted)
        {
            var next=LooseQueueGeometry.Extend(pointId,cells,forward,_traversalGrid!,QueueGroundAllowed,reserved);
            if(next is null)break;
            cells.Add(next.Value);
        }
        SetWaterPoint(point with { QueueCells = cells.ToArray() });
        return cells.Count > members || members == 20;
    }

    private int RemainingOwnWaterWaitTicks(ulong id, WaterPointState point)
    {
        var members = point.Queue.Concat(point.Overflow).ToArray();
        var position = Array.IndexOf(members, id);
        if (position < 0) return EstimateWaterTotalTicks(id, point);
        return members.Take(position + 1).Sum(member =>
        {
            var thirst = _medical!.Needs.Single(item => item.AgentId == member).Thirst;
            var rate = EffectiveMedicalDrinkThirstPerTickFor(member);
            return (thirst + rate - 1) / rate;
        });
    }

    private static bool ValidSavedWaterGeometry(IReadOnlyList<WaterPointState> points, TraversalGrid? grid)
    {
        static GridCell[] Cells(WaterPointState point) => point.QueueCells.Length > 0 ? point.QueueCells :
            Enumerable.Range(0, Math.Min(20, point.Queue.Length + point.Overflow.Length + 1)).Select(index =>
                index < 10 ? WaterSlot(point, index) : WaterOverflowSlot(point, index - 10)).ToArray();
        foreach (var point in points)
        {
            if (point.QueueCells is null || point.GeometryVersion is < 0 or > 1) return false;
            var members = point.Queue.Length + point.Overflow.Length;
            if (point.QueueCells.Length == 0)
            {
                if (point.GeometryVersion == 1 && members > 0) return false;
                continue; // absent version0 geometry means the exact legacy layout
            }
            var forward = RotateWaterOffset(new(0, 1), point.QuarterTurns);
            var legacyPrefix = point.GeometryVersion == 0;
            for (var index = 0; index < point.QueueCells.Length; index++)
            {
                var cell = point.QueueCells[index];
                var legacy = index < 10 ? LegacyWaterSlot(point, index) : LegacyWaterOverflowSlot(point, index - 10);
                if (legacyPrefix && cell == legacy) continue;
                legacyPrefix = false;
                if (index == 0) { if (cell != WaterPointServiceCell(point)) return false; continue; }
                var previous = point.QueueCells[index - 1];
                var dx = cell.X - previous.X; var dz = cell.Z - previous.Z;
                if(LooseQueueGeometry.Corridor([previous,cell]).Any(part=>!QueueGroundAllowed(part)))return false;
                if (Math.Abs(dx)>3 || Math.Abs(dz)>3 || Math.Max(Math.Abs(dx),Math.Abs(dz))<2 ||
                    dx * forward.X + dz * forward.Z < 0 ||
                    cell.X is >= 90 and <= 101 && cell.Z is >= 139 and <= 160) return false;
                if (index > 1)
                {
                    var before = point.QueueCells[index - 2];
                    if (dx * (previous.X - before.X) + dz * (previous.Z - before.Z) < 0) return false;
                }
                if (point.QueueCells.Take(index - 1).Any(other => Math.Abs(other.X - cell.X) <= 1 && Math.Abs(other.Z - cell.Z) <= 1)) return false;
                var midpoint = new GridCell(previous.X + dx / 2, previous.Z + dz / 2);
                if (point.QueueCells.Take(index - 1).Any(other => Math.Abs(other.X - midpoint.X) <= 1 && Math.Abs(other.Z - midpoint.Z) <= 1) ||
                    points.Where(other => other.Id != point.Id).Any(other => Cells(other).Any(candidate =>
                        Math.Abs(candidate.X - midpoint.X) <= 1 && Math.Abs(candidate.Z - midpoint.Z) <= 1))) return false;
                if (grid is not null)
                {
                    var from = TraversalGrid.CellCentre(previous); var to = TraversalGrid.CellCentre(cell);
                    if (!TraversalSweep.IsWalkable(grid, from.XMillimetres, from.ZMillimetres, to.XMillimetres, to.ZMillimetres)) return false;
                    if (dx != 0 && dz != 0)
                    {
                        var middle = new GridCell(previous.X + dx / 2, previous.Z + dz / 2);
                        if (!grid.Get(new(previous.X + dx / 2, previous.Z)).IsWalkable || !grid.Get(new(previous.X, previous.Z + dz / 2)).IsWalkable ||
                            !grid.Get(new(middle.X + dx / 2, middle.Z)).IsWalkable || !grid.Get(new(middle.X, middle.Z + dz / 2)).IsWalkable) return false;
                    }
                }
            }
        }
        for (var index = 0; index < points.Count; index++)
        for (var other = index + 1; other < points.Count; other++)
            if (LooseQueueGeometry.Corridor(Cells(points[index])).Any(cell => LooseQueueGeometry.Corridor(Cells(points[other])).Any(candidate => Math.Abs(candidate.X - cell.X) <= 1 && Math.Abs(candidate.Z - cell.Z) <= 1))) return false;
        return true;
    }
}

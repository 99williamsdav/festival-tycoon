namespace Festival.Simulation;

public sealed partial class GameSession
{
    /// <summary>A toilet's grown queue places, or the doorstep alone for older saves.</summary>
    private static GridCell[] ToiletQueuePlaces(ToiletFacility toilet) => toilet.QueueCells ?? [ToiletDoorstepCell(toilet)];

    /// <summary>Every toilet's footprint and its grown queue, which other queues keep clear of.</summary>
    private GridCell[] ToiletQueueCorridor(string? except = null) => EffectiveToilets(_facilities).Where(toilet => toilet.Id != except)
        .SelectMany(toilet => ToiletReservedCells(toilet).Concat(LooseQueueGeometry.Corridor(ToiletQueuePlaces(toilet)))).ToArray();

    private int ToiletApproachers(string toiletId, ulong? except = null) => PeopleIn(PersonView.Consumption)
        .Count(person => person.Id != except && person.ToiletId == toiletId && person.ToiletStage == ToiletVisitStage.Approaching);

    /// <summary>Whether there's a place for one more: the queue has grown far enough for everyone heading there.</summary>
    private bool ToiletQueueHasRoom(ToiletFacility toilet, ulong agentId) =>
        toilet.QueueCells is not { } cells || toilet.Queue.Length + ToiletApproachers(toilet.Id, agentId) < cells.Length;

    /// <summary>
    /// Each toilet's queue grows a place at a time from its doorstep into free ground, for those queuing and those on
    /// their way plus one more, as the taps' and stalls' queues do. Grown places never move while in use, but the tail
    /// is trimmed as the queue shortens and may regrow elsewhere if another queue took that ground meanwhile. A queue
    /// with no room left stops growing, and nobody else picks that toilet until it shortens.
    /// </summary>
    private void GrowToiletQueues()
    {
        foreach (var toilet in EffectiveToilets(_facilities).ToArray())
        {
            if (toilet.QueueCells is null) continue; // Older saves keep their straight line exactly.
            var wanted = Math.Min(ToiletRules.MaximumQueue, toilet.Queue.Length + ToiletApproachers(toilet.Id) + 1);
            var cells = toilet.QueueCells.Take(Math.Max(1, wanted)).ToList();
            if (cells.Count < wanted)
            {
                var reserved = WaterPoints().SelectMany(point => LooseQueueGeometry.Corridor(CaptureWaterQueueCells(point.Id)))
                    .Concat(_immersion is null ? [] : Vendors.SelectMany(vendor => LooseQueueGeometry.Corridor(VendorQueueCells(vendor))))
                    .Concat(ToiletQueueCorridor(toilet.Id)).ToArray();
                var outward = RotateWaterOffset(new(0, -1), toilet.QuarterTurns);
                while (cells.Count < wanted)
                {
                    var next = LooseQueueGeometry.Extend("toilet." + toilet.Id, cells, outward, _traversalGrid!,
                        cell => QueueGroundAllowed(cell, _preparation), reserved);
                    if (next is null) break;
                    cells.Add(next.Value);
                }
            }
            if (!cells.SequenceEqual(toilet.QueueCells)) SetToilet(toilet with { QueueCells = cells.ToArray() });
        }
    }

    private static string? ValidateToiletQueueGeometry(ToiletFacility toilet, TraversalGrid grid, PreparationSnapshot? prep)
    {
        if (toilet.QueueCells is not { } cells) return null;
        return cells.Length is < 1 or > ToiletRules.MaximumQueue || cells[0] != ToiletDoorstepCell(toilet) ||
            // The doorstep belongs to the toilet's placement; only the grown places must be open queue ground.
            LooseQueueGeometry.Corridor(cells).Where(cell => cell != cells[0]).Any(cell => !QueueGroundAllowed(cell, prep)) ||
            !LooseQueueGeometry.Valid(cells, RotateWaterOffset(new(0, -1), toilet.QuarterTurns), grid, [])
            ? "Toilet saved loose queue geometry invalid." : null;
    }
}

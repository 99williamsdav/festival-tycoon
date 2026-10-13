namespace Festival.Simulation;

/// <summary>
/// A steward or the maintenance worker carrying a bale to a muddy spot and spreading it. <see cref="Spread"/> counts the
/// cells covered so far; <see cref="StartedTick"/> is when they got there, -1 while still walking.
/// </summary>
public sealed record StrawJob(ulong WorkerId, GridCell Spot, GridCell Stand, int Spread, long StartedTick, long UntilTick);

/// <summary>Sends a worker with a bale from the store to spread straw on the mud around a spot.</summary>
public sealed record SpreadStrawCommand(ulong WorkerId, GridCell Spot) : SessionCommand;

public static class StrawJobRules
{
    /// <summary>How far from the chosen spot straw is spread: 3 m.</summary>
    public const int RadiusCells = 6;
    /// <summary>Cells covered each second once there: a bale's 24 cells in six seconds.</summary>
    public const int CellsPerSecond = 4;
    /// <summary>Time enough to walk across the site and spread a bale.</summary>
    public const int DurationTicks = 4_800;
    /// <summary>Cleanup work, so a "litter." intent: setting it never interrupts the job itself.</summary>
    public const string Intent = "litter.spread-straw";
}

public sealed partial class GameSession
{
    /// <summary>What the bales taken out of the store cost: stock until then, an expense once spread.</summary>
    internal static long StrawSpreadCost(PreparationSnapshot p, WeatherSnapshot? weather) =>
        weather is null || p.Plan is not { Committed: true } plan ? 0 : Math.Max(0, plan.StrawBales - weather.StrawBales) * (long)GroundCoverRules.BalePennies;

    /// <summary>Whoever's spreading straw now; their route is the job's.</summary>
    private bool StrawOwnsNavigation(ulong id) => _weather?.StrawJob is { } job && job.WorkerId == id;

    /// <summary>Who could take a bale out now: stewards on duty and the maintenance worker, when free.</summary>
    public bool CanSpreadStraw(ulong workerId, out string? reason)
    {
        reason = null;
        if (_preparation?.Status != PreparationStatus.Running || _weather is not { Kind: WeatherKind.Rain } w) { reason = "Straw is spread during a rainy festival."; return false; }
        if (w.StrawBales <= 0) { reason = "No straw bales left in the store."; return false; }
        if (w.StrawJob is not null) { reason = "A bale is already being spread."; return false; }
        if (!GetStewardResponses().Any(job => job.WorkerId == workerId) && _preparation.MaintenanceWorkerId != workerId)
        { reason = "Only a steward or the maintenance worker spreads straw."; return false; }
        if (StaffUnavailableReason(workerId) is { } busy) { reason = busy; return false; }
        if (CleanupOwnsNavigation(workerId) || WasteOwnsNavigation(workerId)) { reason = "Busy cleaning up."; return false; }
        return true;
    }

    private CommandResult? ValidateSpreadStraw(EntityId? target, SpreadStrawCommand command)
    {
        if (target is not null) return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Name the worker in the command.");
        if (!CanSpreadStraw(command.WorkerId, out var reason)) return CommandResult.Rejected(CommandReasonCode.InvalidParameter, reason!);
        if (!GroundRules.InField(command.Spot) || StrawStand(command.WorkerId, command.Spot) is null)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Pick open ground on the field.");
        return null;
    }

    /// <summary>Where the worker stands to spread: the spot, or the nearest open cell to it they can reach.</summary>
    private GridCell? StrawStand(ulong workerId, GridCell spot)
    {
        if (_traversalGrid is not { } grid || !_navigationAgents.TryGetValue(new(workerId), out var nav)) return null;
        var from = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
        for (var radius = 0; radius <= 3; radius++)
            foreach (var cell in RingCells(spot, radius))
                if (grid.Contains(cell) && grid.Get(cell).IsWalkable && DeterministicPathfinder.FindPath(grid, from, cell).Found) return cell;
        return null;
    }

    private static IEnumerable<GridCell> RingCells(GridCell centre, int radius)
    {
        if (radius == 0) { yield return centre; yield break; }
        for (var x = -radius; x <= radius; x++)
        for (var z = -radius; z <= radius; z++)
            if (Math.Max(Math.Abs(x), Math.Abs(z)) == radius) yield return new(centre.X + x, centre.Z + z);
    }

    private void ApplySpreadStraw(SpreadStrawCommand command)
    {
        var stand = StrawStand(command.WorkerId, command.Spot)!.Value;
        RecallWorker(command.WorkerId, "Taking a straw bale out to the mud");
        var until = CurrentTick + StrawJobRules.DurationTicks;
        until += (80 - until % 80) % 80;
        SetWeather(_weather! with { StrawBales = _weather.StrawBales - 1, StrawJob = new(command.WorkerId, command.Spot, stand, 0, -1, until) });
        MutatePerson(command.WorkerId, item => item.Reason = "Spreading straw on the mud");
        ApplyAgentDestination(new(command.WorkerId), new(stand, StrawJobRules.Intent));
    }

    /// <summary>
    /// Once a second: the worker walks the bale out, then covers the muddiest ground around the spot, nearest first, four
    /// cells a second until the bale's 24 are down. A job runs out of time, or is dropped for anything more urgent.
    /// </summary>
    private void AdvanceStrawJob()
    {
        if (_weather?.StrawJob is not { } job) return;
        var id = job.WorkerId;
        var nav = _navigationAgents[new(id)];
        if (_preparation?.Status != PreparationStatus.Running || CurrentTick >= job.UntilTick || HigherPriorityOwns(id) ||
            nav.Action == AgentNavigationAction.NoRoute || nav.Destination != job.Stand || nav.IntentId != StrawJobRules.Intent)
        { EndStrawJob(true); return; }
        if (nav.Action != AgentNavigationAction.Arrived) return;
        if (job.StartedTick < 0) { SetWeather(_weather with { StrawJob = job with { StartedTick = CurrentTick } }); return; }
        var spread = job.Spread;
        foreach (var cell in StrawTargets(job.Spot).Take(Math.Min(StrawJobRules.CellsPerSecond, GroundCoverRules.CellsPerBale - spread)))
        {
            SpreadStrawOn(cell);
            spread++;
        }
        if (spread == job.Spread || spread >= GroundCoverRules.CellsPerBale) { EndStrawJob(true); return; }
        SetWeather(_weather with { StrawJob = job with { Spread = spread } });
    }

    /// <summary>
    /// Read-only, for the spot picker: the cells a bale spread around this spot would cover if it went down now, in the
    /// order they'd be laid. Changes nothing.
    /// </summary>
    public GridCell[] StrawSpreadPreview(GridCell spot) => StrawTargets(spot).Take(GroundCoverRules.CellsPerBale).ToArray();

    /// <summary>Ground near the spot that needs straw: muddiest first, then wettest, then nearest; never fresh straw or matting.</summary>
    private IEnumerable<GridCell> StrawTargets(GridCell spot)
    {
        var radius = StrawJobRules.RadiusCells;
        var candidates = new List<(GridCell Cell, int Mud, int Wet, long Distance)>();
        for (var x = spot.X - radius; x <= spot.X + radius; x++)
        for (var z = spot.Z - radius; z <= spot.Z + radius; z++)
        {
            var cell = new GridCell(x, z);
            long distance = (long)(x - spot.X) * (x - spot.X) + (long)(z - spot.Z) * (z - spot.Z);
            if (distance > (long)radius * radius || !GroundRules.InField(cell) || _traversalGrid?.Get(cell).IsWalkable != true || MattedAt(cell)) continue;
            var trample = GroundTrampleAt(cell);
            if (trample >= 0 && trample < GroundCoverRules.TrampleMaximum) continue;
            var mud = GroundMudAt(cell); var wet = GroundWetAt(cell);
            if (mud < GroundRules.MudLevel && wet < GroundRules.PuddleWet) continue;
            candidates.Add((cell, mud, wet, distance));
        }
        return candidates.OrderByDescending(item => item.Mud).ThenByDescending(item => item.Wet).ThenBy(item => item.Distance)
            .ThenBy(item => item.Cell.Z).ThenBy(item => item.Cell.X).Select(item => item.Cell);
    }

    private void EndStrawJob(bool returnToPost)
    {
        if (_weather?.StrawJob is not { } job) return;
        SetWeather(_weather with { StrawJob = null });
        if (returnToPost && !HigherPriorityOwns(job.WorkerId)) ReturnToListening(job.WorkerId);
    }

    /// <summary>A saved straw job belongs to a worker who could take it, walking to or standing at its stand.</summary>
    private static string? ValidatePersistedStrawJob(WeatherSnapshot w, SessionPersistenceSnapshot s, PreparationSnapshot p)
    {
        if (w.StrawJob is not { } job) return null;
        if (p.Status != PreparationStatus.Running || s.Disorder?.Stewards.Any(steward => steward.WorkerId == job.WorkerId) != true && p.MaintenanceWorkerId != job.WorkerId)
            return "Straw job worker invalid.";
        if (job.Spread is < 0 or >= GroundCoverRules.CellsPerBale || job.StartedTick > s.CurrentTick || job.StartedTick < -1 ||
            job.UntilTick % 80 != 0 || job.UntilTick < s.CurrentTick || job.UntilTick > s.CurrentTick + StrawJobRules.DurationTicks + 80 ||
            !GroundRules.InField(job.Spot) || Math.Max(Math.Abs(job.Stand.X - job.Spot.X), Math.Abs(job.Stand.Z - job.Spot.Z)) > 3 ||
            job.StartedTick < 0 && job.Spread != 0)
            return "Straw job progress invalid.";
        if (s.NavigationAgents?.SingleOrDefault(agent => agent.Id == job.WorkerId) is not { } nav || nav.IntentId != StrawJobRules.Intent ||
            nav.DestinationX != job.Stand.X || nav.DestinationZ != job.Stand.Z || job.StartedTick >= 0 && nav.Action != (int)AgentNavigationAction.Arrived)
            return "Straw job route invalid.";
        return null;
    }
}

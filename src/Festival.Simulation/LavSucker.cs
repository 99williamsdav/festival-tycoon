namespace Festival.Simulation;

public enum LavSuckerStage { Arriving, Pumping, Leaving, Gone }

/// <summary>
/// One call-out of Dav's Lav-Sucker, the toilet emptying company: paid up front, their tanker drives in from the north
/// gate, waits for the cubicle to be free, sucks it empty and drives off again.
/// </summary>
/// <param name="Route">The cells the tanker is driving; empty while it's parked.</param>
/// <param name="StageTick">When the current stage began (pumping counts from here).</param>
public sealed record LavSuckerCall(string Id, string ToiletId, long CalledTick, LavSuckerStage Stage, int XMillimetres, int ZMillimetres,
    GridCell[] Route, int RouteIndex, long StageTick)
{
    /// <summary>What this call sucked out, once pumping's done: a toilet's emptied amount is exactly the sum of its calls'.</summary>
    public int EmptiedMillilitres { get; init; }
}

public sealed record LavSuckerSnapshot(int Version, LavSuckerCall[] Calls);

/// <summary>Calls Dav's Lav-Sucker to empty a toilet, for a fee.</summary>
public sealed record CallLavSuckerCommand(string ToiletId) : SessionCommand;

public static class LavSuckerRules
{
    public const int FeePennies = 2_500;
    /// <summary>Where the tanker comes in: just inside the north gate.</summary>
    public static readonly GridCell Gate = new(128, 189);
    /// <summary>Walking-plus pace for a vehicle among people: about 3 m/s, so roughly fifteen festival minutes to a far toilet.</summary>
    public const int MillimetresPerTick = 38;
    /// <summary>Ten festival minutes on the hose.</summary>
    public const int PumpTicks = 800;
    /// <summary>The stink while it pumps, a second, out to this far.</summary>
    public const int StinkPerSecond = 6, StinkReachMillimetres = 7_000;
}

public sealed partial class GameSession
{
    private LavSuckerSnapshot? _lavSucker;
    private static readonly LavSuckerSnapshot EmptyLavSucker = new(1, []);
    public LavSuckerSnapshot? CaptureLavSucker() => _lavSucker;
    internal string? LavSuckerCanonicalJson => _lavSucker is null ? null : System.Text.Json.JsonSerializer.Serialize(_lavSucker);

    /// <summary>What the festival has paid Dav's Lav-Sucker today.</summary>
    private static long LavSuckerSpend(LavSuckerSnapshot? lav) => (lav?.Calls.Length ?? 0) * (long)LavSuckerRules.FeePennies;

    /// <summary>The tanker's on site for this toilet and nobody new goes in: parked waiting, or pumping.</summary>
    public bool ToiletBeingEmptied(string toiletId) => _lavSucker?.Calls.Any(c => c.ToiletId == toiletId &&
        (c.Stage == LavSuckerStage.Pumping || c.Stage == LavSuckerStage.Arriving && c.RouteIndex >= c.Route.Length)) == true;

    /// <summary>Whether this toilet already has a tanker on the way or at work.</summary>
    public bool LavSuckerBooked(string toiletId) => _lavSucker?.Calls.Any(c => c.ToiletId == toiletId && c.Stage is LavSuckerStage.Arriving or LavSuckerStage.Pumping) == true;

    /// <summary>Where the tanker parks for a toilet: behind it, clear of the door and its queue.</summary>
    private GridCell LavSuckerBay(ToiletFacility toilet)
    {
        var back = RotateWaterOffset(new(0, 4), toilet.QuarterTurns);
        return WalkableNear(new(toilet.Cell.X + back.X, toilet.Cell.Z + back.Z));
    }

    public string? LavSuckerUnavailable(string toiletId)
    {
        if (_preparation?.Status != PreparationStatus.Running || _lavSucker is null) return "Dav's Lav-Sucker comes out during the festival.";
        if (EffectiveToilets(_facilities).FirstOrDefault(t => t.Id == toiletId) is not { } toilet) return "No such toilet.";
        if (LavSuckerBooked(toiletId)) return "Dav's already on the way.";
        if (toilet.UsedMillilitres == 0) return "It's already empty.";
        if (_festivalFinances[new(_preparation.FinanceOwnerId)].CashPennies + CampaignDefaults.OverdraftPennies < LavSuckerRules.FeePennies)
            return $"Dav charges {FestivalCurrency.Format(LavSuckerRules.FeePennies)} up front.";
        return null;
    }

    private CommandResult? ValidateCallLavSucker(EntityId? target, CallLavSuckerCommand command) =>
        target is not null ? CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Call Dav for a toilet by its id.") :
        LavSuckerUnavailable(command.ToiletId) is { } reason ? CommandResult.Rejected(CommandReasonCode.InvalidParameter, reason) : null;

    private void ApplyCallLavSucker(CallLavSuckerCommand command)
    {
        _festivalFinances[new(_preparation!.FinanceOwnerId)].CashPennies -= LavSuckerRules.FeePennies;
        var toilet = EffectiveToilets(_facilities).Single(t => t.Id == command.ToiletId);
        var gate = TraversalGrid.CellCentre(LavSuckerRules.Gate);
        var path = DeterministicPathfinder.FindPath(_traversalGrid!, LavSuckerRules.Gate, LavSuckerBay(toilet));
        var call = new LavSuckerCall($"lav:{_preparation.Attempt}:{_lavSucker!.Calls.Length + 1}", toilet.Id, CurrentTick, LavSuckerStage.Arriving,
            gate.XMillimetres, gate.ZMillimetres, path.Found ? path.Path.ToArray() : [LavSuckerBay(toilet)], 0, CurrentTick);
        _lavSucker = _lavSucker with { Calls = _lavSucker.Calls.Append(call).ToArray() };
        MedicalEvent("lav:called", $"Dav's Lav-Sucker called to empty {toilet.Id}.");
    }

    private void AdvanceLavSucker()
    {
        if (_lavSucker is not { Calls.Length: > 0 } lav || _preparation?.Status is not (PreparationStatus.Running or PreparationStatus.Departing)) return;
        var calls = lav.Calls.Select(AdvanceLavSuckerCall).ToArray();
        _lavSucker = lav with { Calls = calls };
        if (CurrentTick % 80 == 0 && calls.Any(c => c.Stage == LavSuckerStage.Pumping)) ApplyLavSuckerStink(calls);
    }

    private LavSuckerCall AdvanceLavSuckerCall(LavSuckerCall call)
    {
        switch (call.Stage)
        {
            case LavSuckerStage.Arriving:
                if (call.RouteIndex < call.Route.Length) return DriveLavSucker(call);
                // Parked behind it: once nobody's inside, the hose goes on.
                var toilet = GetToilet(call.ToiletId);
                if (toilet.OwnerId is not null || toilet.InterruptedOccupantId is not null) return call;
                return call with { Stage = LavSuckerStage.Pumping, StageTick = CurrentTick };
            case LavSuckerStage.Pumping:
                if (CurrentTick - call.StageTick < LavSuckerRules.PumpTicks) return call;
                var emptied = GetToilet(call.ToiletId);
                var amount = emptied.UsedMillilitres;
                SetToilet(emptied with { EmptiedMillilitres = emptied.EmptiedMillilitres + amount });
                call = call with { EmptiedMillilitres = amount };
                MedicalEvent("lav:emptied", $"Dav's Lav-Sucker emptied {call.ToiletId}.");
                var here = TraversalGrid.WorldToCell(call.XMillimetres, call.ZMillimetres);
                var home = DeterministicPathfinder.FindPath(_traversalGrid!, here, LavSuckerRules.Gate);
                return call with { Stage = LavSuckerStage.Leaving, StageTick = CurrentTick, Route = home.Found ? home.Path.ToArray() : [LavSuckerRules.Gate], RouteIndex = 0 };
            case LavSuckerStage.Leaving:
                if (call.RouteIndex < call.Route.Length) return DriveLavSucker(call);
                return call with { Stage = LavSuckerStage.Gone, Route = [], RouteIndex = 0, StageTick = CurrentTick };
            default:
                return call;
        }
    }

    private static LavSuckerCall DriveLavSucker(LavSuckerCall call)
    {
        int x = call.XMillimetres, z = call.ZMillimetres, index = call.RouteIndex, budget = LavSuckerRules.MillimetresPerTick;
        while (budget > 0 && index < call.Route.Length)
        {
            var target = TraversalGrid.CellCentre(call.Route[index]);
            long dx = target.XMillimetres - x, dz = target.ZMillimetres - z;
            var distance = (int)Math.Sqrt(dx * dx + dz * dz);
            if (distance <= budget) { x = target.XMillimetres; z = target.ZMillimetres; budget -= distance; index++; continue; }
            x += (int)(dx * budget / distance); z += (int)(dz * budget / distance); budget = 0;
        }
        return call with { XMillimetres = x, ZMillimetres = z, RouteIndex = index };
    }

    /// <summary>The smell of the hose at work: anyone close by feels it, as they do a full toilet's.</summary>
    private void ApplyLavSuckerStink(LavSuckerCall[] calls)
    {
        var reach = (long)LavSuckerRules.StinkReachMillimetres * LavSuckerRules.StinkReachMillimetres;
        foreach (var person in PeopleIn(PersonView.Roster).Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed).ToArray())
        {
            if (!_navigationAgents.TryGetValue(new(person.Id), out var nav)) continue;
            if (!calls.Any(c => c.Stage == LavSuckerStage.Pumping && (long)(nav.XMillimetres - c.XMillimetres) * (nav.XMillimetres - c.XMillimetres) +
                    (long)(nav.ZMillimetres - c.ZMillimetres) * (nav.ZMillimetres - c.ZMillimetres) <= reach)) continue;
            ChangeSatisfaction(person.Id, -UnpleasantFor(person.Id, LavSuckerRules.StinkPerSecond), MoodCause.ToiletSmell);
        }
    }

    // Obstacle points in the crowd's occupancy use ids no person can have, counting down: cows from the very top (at most
    // 5 × 40 points), tankers from far below (9 points each, one per toilet), so the two ranges can never meet.
    internal const ulong CowOccupancyBase = ulong.MaxValue, LavOccupancyBase = ulong.MaxValue - 4_096;

    /// <summary>The tanker on the move or parked is a large obstacle: people step round it as they do a cow.</summary>
    private void AddLavSuckerOccupancy(SpatialNeighbourIndex occupied)
    {
        if (_lavSucker is null) return;
        var id = LavOccupancyBase;
        foreach (var call in _lavSucker.Calls.Where(c => c.Stage != LavSuckerStage.Gone))
            foreach (var (dx, dz) in new[] { (0, 0), (900, 0), (-900, 0), (0, 900), (0, -900), (1_800, 0), (-1_800, 0), (0, 1_800), (0, -1_800) })
                occupied.Add(new EntityId(id--), call.XMillimetres + dx, call.ZMillimetres + dz);
    }

    private static string? ValidatePersistedLavSucker(SessionPersistenceSnapshot s)
    {
        if (s.LavSucker is not { } lav) return s.Preparation?.Plan is null ? null : "Current Build save requires the emptying service's state.";
        if (lav.Version != 1 || lav.Calls is null || lav.Calls.Select(c => c.Id).Distinct().Count() != lav.Calls.Length) return "Emptying service state invalid.";
        var toilets = (s.Facilities?.Toilets ?? []).ToDictionary(t => t.Id);
        var grid = new TraversalGrid();
        var attempt = s.Preparation?.Attempt ?? 0;
        if (lav.Calls.Where((c, index) => c.Id != $"lav:{attempt}:{index + 1}").Any()) return "Emptying service call identity invalid.";
        // Only Dav empties a toilet: what each toilet's had taken away is exactly what its finished calls took.
        foreach (var toilet in toilets.Values)
            if (toilet.EmptiedMillilitres != lav.Calls.Where(c => c.ToiletId == toilet.Id && c.Stage is LavSuckerStage.Leaving or LavSuckerStage.Gone).Sum(c => c.EmptiedMillilitres))
                return "A toilet was emptied without a call.";
        foreach (var c in lav.Calls)
            if (!Enum.IsDefined(c.Stage) || !toilets.ContainsKey(c.ToiletId) ||
                (c.Stage is LavSuckerStage.Leaving or LavSuckerStage.Gone ? c.EmptiedMillilitres <= 0 : c.EmptiedMillilitres != 0) ||
                c.Stage == LavSuckerStage.Pumping && (toilets[c.ToiletId].OwnerId is not null || toilets[c.ToiletId].InterruptedOccupantId is not null) ||
                c.Route.Length > 0 && c.RouteIndex == c.Route.Length && c.Stage is LavSuckerStage.Arriving or LavSuckerStage.Pumping &&
                    (c.XMillimetres, c.ZMillimetres) != TraversalGrid.CellCentre(c.Route[^1]) || c.CalledTick < 0 || c.CalledTick > s.CurrentTick || c.StageTick < c.CalledTick ||
                c.StageTick > s.CurrentTick || c.Route is null || c.RouteIndex < 0 || c.RouteIndex > c.Route.Length || c.Route.Any(r => !grid.Contains(r)) ||
                !grid.Contains(TraversalGrid.WorldToCell(c.XMillimetres, c.ZMillimetres)) ||
                c.Stage == LavSuckerStage.Pumping && (c.Route.Length != 0 && c.RouteIndex != c.Route.Length || s.CurrentTick - c.StageTick >= LavSuckerRules.PumpTicks))
                return "Emptying service call invalid.";
        if (lav.Calls.Where(c => c.Stage is LavSuckerStage.Arriving or LavSuckerStage.Pumping).GroupBy(c => c.ToiletId).Any(g => g.Count() > 1))
            return "One tanker per toilet at a time.";
        return null;
    }
}

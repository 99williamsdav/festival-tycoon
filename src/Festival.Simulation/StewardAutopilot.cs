using System.Text.Json.Serialization;

namespace Festival.Simulation;

/// <summary>
/// A hands-on day: hands the player's steward to themselves (<paramref name="Auto"/>, the manager's view) or takes
/// them back (first person). Either way the steward stops where they are and lets go of anything held.
/// </summary>
public sealed record SetPlayerStewardControlCommand(bool Auto) : SessionCommand;

/// <summary>
/// The player's steward working by themselves, saved: what they're going to (a reach target, and the radio call it
/// answers, if any), when they took it on, and the next stretch of the way there.
/// </summary>
public sealed record StewardAutopilot
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TargetId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CallId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long TakenTick { get; init; }
    /// <summary>The next few cells of the way, walked from <see cref="RouteIndex"/>; replanned every <see cref="StewardAutopilotRules.RouteTicks"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public GridCell[]? Route { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int RouteIndex { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long RoutedTick { get; init; }
}

/// <summary>
/// The autopilot's handicaps against a player doing the job: it hears of trouble late, sets off at a walk, takes the
/// oldest call rather than the nearest or the most urgent and sticks with it, holds half as long again, and calms and
/// breaks up with a lower skill.
/// </summary>
public static class StewardAutopilotRules
{
    /// <summary>It sets off this long after the radio call (or after trouble began, when it's within <see cref="NoticeMillimetres"/>): 5 s.</summary>
    public const int ReactTicks = 400;
    public const int NoticeMillimetres = 8_000;
    /// <summary>It makes up its mind once a second.</summary>
    public const int ThinkTicks = 80;
    /// <summary>Its holds take half as long again as the player's.</summary>
    public const int HoldPermille = 1_500;
    public static int HoldTicks(StewardActionKind kind) => StewardShiftRules.DurationTicks(kind) * HoldPermille / 1_000;
    /// <summary>Taken off calming and confrontation skill for its rolls.</summary>
    public const int SkillPenalty = 1_500;
    /// <summary>It gives up on anything it hasn't got to in a minute.</summary>
    public const int GiveUpTicks = 4_800;
    public const int RouteTicks = 160, RouteCellsKept = 24, WaypointMillimetres = 400;
    /// <summary>It acts from this close (a little inside the player's reach); litter from nearer.</summary>
    public const int ActMillimetres = 2_000, LitterActMillimetres = 1_600;
    /// <summary>Thirst or heat from which it goes for a drink first.</summary>
    public const int DrinkLevel = 6_000;
    /// <summary>With nothing on, it picks up litter this close.</summary>
    public const int LitterMillimetres = 6_000;
}

public sealed partial class GameSession
{
    private CommandResult? ValidatePlayerStewardControl(EntityId? target, SetPlayerStewardControlCommand command)
    {
        if (target is not null || !IsHandsOn || _stewardShift?.PlayerId is null || _preparation?.Status != PreparationStatus.Running)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Only your own steward on a hands-on day works by themselves.");
        if (command.Auto == (_stewardShift.Autopilot is not null))
            return CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, command.Auto ? "Already working by themselves." : "Already yours.");
        return null;
    }

    private void ApplyPlayerStewardControl(SetPlayerStewardControlCommand command)
    {
        if (_stewardShift!.Action is { } held) DropStewardAction(held, command.Auto ? "Let go." : "You've taken over.");
        _stewardShift = _stewardShift with { MoveX = 0, MoveZ = 0, Run = false, Autopilot = command.Auto ? new() : null };
    }

    /// <summary>
    /// The start of a hands-on day's tick: Control routes the radio's calls to the hired crew, then the player's
    /// steward, if left to themselves, works. They go through the player's own moves and actions, so every rule the
    /// player's hands obey holds for them too.
    /// </summary>
    private void AdvanceStewardAutopilot()
    {
        if (!IsHandsOn || _preparation?.Status != PreparationStatus.Running || _stewardShift is not { PlayerId: { } me } || _traversalGrid is null) return;
        RouteCallsToCrew();
        if (_stewardShift.Autopilot is not { } pilot) return;
        var nav = _navigationAgents[new(me)];
        if (_stewardShift.Action is not null) { AutopilotStop(); return; }
        // Driving a cow home: keep up a few metres behind it.
        if (_cows?.Loose.FirstOrDefault(cow => cow.HerderId == me && cow.Driving) is { } driven)
        {
            long cx = driven.XMillimetres - nav.XMillimetres, cz = driven.ZMillimetres - nav.ZMillimetres;
            if (cx * cx + cz * cz <= 3_000L * 3_000) AutopilotStop(); else AutopilotStep(cx, cz);
            return;
        }
        if (pilot.TargetId is not null && !AutopilotStillWants(pilot)) pilot = new();
        if (pilot.TargetId is null && CurrentTick % StewardAutopilotRules.ThinkTicks == 0) pilot = ChooseAutopilotTarget(me, nav) ?? pilot;
        if (pilot.TargetId is not { } target || StewardTargetPoint(target) is not { } point)
        {
            _stewardShift = _stewardShift with { Autopilot = new() };
            AutopilotStop();
            return;
        }
        long dx = point.X - nav.XMillimetres, dz = point.Z - nav.ZMillimetres;
        var reach = SplitTarget(target).Prefix == "litter" ? StewardAutopilotRules.LitterActMillimetres : StewardAutopilotRules.ActMillimetres;
        if (dx * dx + dz * dz <= (long)reach * reach)
        {
            AutopilotStop();
            var facing = dx == 0 && dz == 0 ? _stewardShift.FacingDegrees : StewardShiftRules.FacingTowards(dx, dz);
            if (AutopilotAction(pilot) is { } action && new StewardActionCommand(action, target, facing) is var command &&
                ValidateStewardAction(null, command, autopilot: true) is null)
            {
                _stewardShift = _stewardShift with { Autopilot = new() };
                ApplyStewardAction(command, autopilot: true);
            }
            else _stewardShift = _stewardShift with { Autopilot = pilot };
            return;
        }
        if (AutopilotRouted(pilot, nav, point.X, point.Z) is not { } routed)
        {
            _stewardShift = _stewardShift with { Autopilot = new() };
            AutopilotStop();
            return;
        }
        _stewardShift = _stewardShift with { Autopilot = routed };
        var (wx, wz) = TraversalGrid.CellCentre(routed.Route![routed.RouteIndex]);
        // The last waypoint reached and still out of reach: straight at it.
        long sx = wx - nav.XMillimetres, sz = wz - nav.ZMillimetres;
        if (routed.RouteIndex == routed.Route.Length - 1 && sx * sx + sz * sz <= (long)StewardAutopilotRules.WaypointMillimetres * StewardAutopilotRules.WaypointMillimetres)
            (sx, sz) = (dx, dz);
        AutopilotStep(sx, sz);
    }

    /// <summary>A walk (never a run) along (dx, dz), facing that way.</summary>
    private void AutopilotStep(long dx, long dz)
    {
        var length = Math.Max(1, IntegerSquareRoot(dx * dx + dz * dz));
        var move = new SetPlayerMoveCommand((int)(dx * 1_000 / length), (int)(dz * 1_000 / length), false, StewardShiftRules.FacingTowards(dx, dz));
        var shift = _stewardShift!;
        if (shift.MoveX == move.DirectionX && shift.MoveZ == move.DirectionZ && !shift.Run && shift.FacingDegrees == move.FacingDegrees) return;
        if (ValidatePlayerMove(null, move, autopilot: true) is null) ApplyPlayerMove(move);
    }

    private void AutopilotStop()
    {
        var shift = _stewardShift!;
        if (shift.MoveX != 0 || shift.MoveZ != 0 || shift.Run) ApplyPlayerMove(new(0, 0, false, shift.FacingDegrees));
    }

    /// <summary>The way on to a point: the route kept, or a fresh one every couple of seconds; past any waypoint already reached. Null with no way there.</summary>
    private StewardAutopilot? AutopilotRouted(StewardAutopilot pilot, NavigationAgentState nav, int x, int z)
    {
        if (pilot.Route is not { Length: > 0 } || pilot.RouteIndex >= pilot.Route.Length || CurrentTick - pilot.RoutedTick >= StewardAutopilotRules.RouteTicks)
        {
            var here = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
            var goal = WalkableNear(TraversalGrid.WorldToCell(x, z));
            GridCell[] route;
            if (here == goal) route = [goal];
            else
            {
                var path = DeterministicPathfinder.FindPath(_traversalGrid!, here, goal);
                if (!path.Found) return null;
                route = path.Path.Where(cell => cell != here).Take(StewardAutopilotRules.RouteCellsKept).ToArray();
                if (route.Length == 0) route = [goal];
            }
            pilot = pilot with { Route = route, RouteIndex = 0, RoutedTick = CurrentTick };
        }
        var index = pilot.RouteIndex;
        while (index < pilot.Route!.Length - 1)
        {
            var (cx, cz) = TraversalGrid.CellCentre(pilot.Route[index]);
            long ax = cx - nav.XMillimetres, az = cz - nav.ZMillimetres;
            if (ax * ax + az * az > (long)StewardAutopilotRules.WaypointMillimetres * StewardAutopilotRules.WaypointMillimetres) break;
            index++;
        }
        return pilot with { RouteIndex = index };
    }

    private static string CallTargetId(StewardCall call) => call.Kind switch
    {
        StewardCallKind.Fight or StewardCallKind.Argument or StewardCallKind.Collapse => $"person:{call.Subject}",
        StewardCallKind.StuckInLoo => $"toilet:{call.Subject}",
        StewardCallKind.BrokenTap => $"tap:{call.Subject}",
        StewardCallKind.FullBin => $"bin:{call.Subject}",
        _ => $"cow:{call.Subject}",
    };

    /// <summary>What the autopilot would do to its target now: the call's own action, else a drink or the litter. Null when there's nothing to do.</summary>
    private StewardActionKind? AutopilotAction(StewardAutopilot pilot)
    {
        if (pilot.TargetId is not { } target) return null;
        StewardActionKind[] options;
        if (pilot.CallId is { } id)
        {
            if ((_stewardShift?.Calls ?? []).FirstOrDefault(call => call.Open && call.Id == id) is not { } call) return null;
            options = call.Kind switch
            {
                StewardCallKind.Fight => [StewardActionKind.BreakUp],
                StewardCallKind.Argument => [StewardActionKind.Calm],
                StewardCallKind.Collapse => [StewardActionKind.RadioMedic, StewardActionKind.StandBy],
                StewardCallKind.StuckInLoo => [StewardActionKind.FreeLoo],
                StewardCallKind.BrokenTap => [StewardActionKind.BodgeTap],
                StewardCallKind.FullBin => [StewardActionKind.EmptyBin],
                _ => [StewardActionKind.HerdCow],
            };
        }
        else options = SplitTarget(target).Prefix switch
        {
            "litter" => [StewardActionKind.PickLitter],
            "tap" or "bar" => [StewardActionKind.Drink],
            _ => [],
        };
        foreach (var kind in options) if (StewardActionAvailable(kind, target)) return kind;
        return null;
    }

    /// <summary>Still worth going on with: not given up on, its call still open and nobody hired on it, and still something to do.</summary>
    private bool AutopilotStillWants(StewardAutopilot pilot)
    {
        if (CurrentTick - pilot.TakenTick > StewardAutopilotRules.GiveUpTicks) return false;
        if (pilot.CallId is { } id && (_stewardShift!.Calls ?? []).FirstOrDefault(call => call.Open && call.Id == id) is { } call && CrewHolder(call) is not null) return false;
        return AutopilotAction(pilot) is not null;
    }

    /// <summary>
    /// What to take on next: a drink when parched; otherwise the call it has known about longest that nobody hired is
    /// on (heard over the radio, or seen close by, <see cref="StewardAutopilotRules.ReactTicks"/> ago), however far or
    /// whatever it is; otherwise litter near by. Null with nothing to do.
    /// </summary>
    private StewardAutopilot? ChooseAutopilotTarget(ulong me, NavigationAgentState nav)
    {
        long Distance(int x, int z) { long dx = x - nav.XMillimetres, dz = z - nav.ZMillimetres; return dx * dx + dz * dz; }
        StewardAutopilot Take(string target, string? call = null) => new() { TargetId = target, CallId = call, TakenTick = CurrentTick };
        var self = _persons[me];
        if (Math.Max(self.Thirst, self.HeatExposure) >= StewardAutopilotRules.DrinkLevel)
        {
            var drink = StewardFacilityTargets().Where(target => SplitTarget(target).Prefix is "tap" or "bar" && StewardActionAvailable(StewardActionKind.Drink, target))
                .Select(target => (Target: target, Point: StewardTargetPoint(target)!.Value))
                .OrderBy(item => Distance(item.Point.X, item.Point.Z)).ThenBy(item => item.Target, StringComparer.Ordinal).Select(item => item.Target).FirstOrDefault();
            if (drink is not null) return Take(drink);
        }
        var noticed = new List<(long Tick, string Id, StewardAutopilot Pilot)>();
        foreach (var call in _stewardShift!.Calls ?? [])
        {
            if (!call.Open || call.AnsweredTick >= 0 || CrewHolder(call) is not null || StewardCallPoint(call) is not { } at) continue;
            var near = Distance(at.X, at.Z) <= (long)StewardAutopilotRules.NoticeMillimetres * StewardAutopilotRules.NoticeMillimetres;
            var tick = (near ? call.OpenedTick : call.HeardTick) + StewardAutopilotRules.ReactTicks;
            var pilot = Take(CallTargetId(call), call.Id);
            if (tick <= CurrentTick && AutopilotAction(pilot) is not null) noticed.Add((tick, call.Id, pilot));
        }
        if (noticed.Count > 0) return noticed.OrderBy(item => item.Tick).ThenBy(item => item.Id, StringComparer.Ordinal).First().Pilot;
        var here = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
        var litter = GroundNear(here, StewardAutopilotRules.LitterMillimetres / TraversalGrid.CellSizeMillimetres)
            .Where(piece => StewardActionAvailable(StewardActionKind.PickLitter, $"litter:{piece.Id}"))
            .OrderBy(piece => Distance(piece.XMillimetres, piece.ZMillimetres)).ThenBy(piece => piece.Id, StringComparer.Ordinal).FirstOrDefault();
        return litter is null ? null : Take($"litter:{litter.Id}");
    }

    /// <summary>
    /// Control gives each call that has gone out over the radio, and that nobody hired is on, to a free hired steward
    /// first: a fight or an argument as a steward dispatch, a loose cow to herd. Jammed loos, broken taps and full bins
    /// already go to the crew by their own rules, and a collapse to the medic once it's radioed.
    /// </summary>
    private void RouteCallsToCrew()
    {
        if (CurrentTick % 8 != 0 || _stewardShift?.Calls is not { } calls) return;
        foreach (var call in calls.Where(call => call.Open && call.HeardTick <= CurrentTick && call.AnsweredTick < 0 &&
                     call.Kind is StewardCallKind.Fight or StewardCallKind.Argument or StewardCallKind.LooseCow)
                     .OrderBy(call => call.HeardTick).ThenBy(call => call.Id, StringComparer.Ordinal).ToArray())
        {
            if (CrewHolder(call) is not null) continue;
            if (call.Kind == StewardCallKind.LooseCow)
            {
                if (CowHerder(call.Subject).Reason is null) ApplyHerdCow(new(call.Subject));
                continue;
            }
            // An argument whose grievance has gone is winding down: a steward sent would only stand down again.
            if (!ulong.TryParse(call.Subject, out var person) || PersonIn(PersonView.Disorder, person) is not { } subject ||
                call.Kind == StewardCallKind.Argument && subject.Grievance == DisorderGrievance.None) continue;
            if (call.Kind == StewardCallKind.Fight && GuestFightOrigin(person)?.HandlingAttempt is not null) continue;
            if (SelectRoleResponse(ResponseRole.Steward, person, out _) is DisorderCommand dispatch) ApplyDisorderCommand(dispatch);
        }
    }

    /// <summary>A saved autopilot, against the snapshot alone: only while the player's steward is on shift, with its fields in bounds.</summary>
    private static string? ValidatePersistedAutopilot(StewardShiftSnapshot shift, SessionPersistenceSnapshot s)
    {
        if (shift.Autopilot is not { } pilot) return null;
        if (shift.PlayerId is null) return "Only a steward on shift works by themselves.";
        if (pilot.TargetId is { Length: 0 or > 64 } || pilot.CallId is { Length: 0 or > 64 } || pilot.CallId is not null && pilot.TargetId is null ||
            pilot.TakenTick < 0 || pilot.TakenTick > s.CurrentTick || pilot.RoutedTick < 0 || pilot.RoutedTick > s.CurrentTick ||
            pilot.Route is { } route && (route.Length is 0 or > StewardAutopilotRules.RouteCellsKept || pilot.RouteIndex < 0 || pilot.RouteIndex >= route.Length) ||
            pilot.Route is null && (pilot.RouteIndex != 0 || pilot.RoutedTick != 0) || pilot.TargetId is null && pilot.Route is not null)
            return "The steward's autopilot is invalid.";
        return null;
    }
}

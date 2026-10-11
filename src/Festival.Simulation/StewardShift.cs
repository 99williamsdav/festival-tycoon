namespace Festival.Simulation;

/// <summary>
/// Prototype steward shift: the player is one steward on the ground while the festival runs itself.
/// <paramref name="PlayerId"/> is the steward under the player's control, null once the shift is over
/// (closing hands them back to the crew so they walk home). The record being present marks the mode.
/// Movement input holds until the next <see cref="SetPlayerMoveCommand"/>; facing is whole degrees.
/// </summary>
public sealed record StewardShiftSnapshot(int Version, ulong? PlayerId, int MoveX, int MoveZ, bool Run, int FacingDegrees);

/// <summary>
/// The player steward's movement input, held until the next one. The direction is world X/Z in permille
/// (either may be 0, neither beyond ±1000; a shorter vector walks slower, for a stick later). Facing is
/// whole degrees, 0 looking down +Z and 90 down +X.
/// </summary>
public sealed record SetPlayerMoveCommand(int DirectionX, int DirectionZ, bool Run, int FacingDegrees) : SessionCommand;

/// <summary>What the player steward can do to someone in reach. Placeholder: the mechanics pass fills this in.</summary>
public enum StewardActionKind { Interact = 1 }

/// <summary>
/// Hook for the player steward's interactions (calming, fights, loos, bins, radio). Validated so the
/// shape is fixed, but always rejected until the mechanics exist. Target is a person from <see cref="GameSession.StewardReach"/>.
/// </summary>
public sealed record StewardActionCommand(StewardActionKind Action, ulong TargetId, int FacingDegrees) : SessionCommand;

/// <summary>Someone the player steward could act on: in reach and in front. Data only.</summary>
public sealed record StewardReachTarget(ulong PersonId, string Name, ProtectedPersonRole Role, int DistanceMillimetres);

public static class StewardShiftRules
{
    public const int Version = 1;
    /// <summary>The hedged field, in grid cells: −32 m to +32 m both ways. The player steward stays inside it.</summary>
    public const int FieldMinCell = 64, FieldMaxCell = 191;
    /// <summary>Running pace against a steward's walk.</summary>
    public const int RunPermille = 1_700;
    /// <summary>How close the player walks up to someone: a little more than the crowd keeps, so the view stays out of their heads.</summary>
    public const int PersonClearanceMillimetres = 450;
    /// <summary>How close loose cows and the tanker's body points may come to the player's centre.</summary>
    public const int ObstacleClearanceMillimetres = 650;
    public const int ReachMillimetres = 2_500;
    /// <summary>cos 40°: the reach cone's half-width either side of facing.</summary>
    public const int ReachConeCosPermille = 766;
    public const string Intent = "steward-shift.player";

    public static bool InField(GridCell cell) =>
        cell.X is >= FieldMinCell and <= FieldMaxCell && cell.Z is >= FieldMinCell and <= FieldMaxCell;

    // Whole-degree sines in permille, rounded once so every platform agrees.
    private static readonly int[] SinPermille = Enumerable.Range(0, 360).Select(d => (int)Math.Round(Math.Sin(d * Math.PI / 180) * 1000)).ToArray();

    /// <summary>The whole degree that points most nearly along (dx, dz), in integers; the lowest on a tie.</summary>
    public static int FacingTowards(long dx, long dz)
    {
        // Facing the right way, the least off to either side.
        var best = 0; var bestSide = long.MaxValue;
        for (var d = 0; d < 360; d++)
        {
            var (fx, fz) = Facing(d);
            var side = Math.Abs(fx * dz - fz * dx);
            if (fx * dx + fz * dz > 0 && side < bestSide) { best = d; bestSide = side; }
        }
        return best;
    }

    /// <summary>The unit facing vector in permille: 0° is +Z, 90° is +X.</summary>
    public static (int X, int Z) Facing(int degrees)
    {
        var d = ((degrees % 360) + 360) % 360;
        return (SinPermille[d], SinPermille[(d + 90) % 360]);
    }
}

public sealed partial class GameSession
{
    private StewardShiftSnapshot? _stewardShift;

    /// <summary>The steward shift's state; null in an ordinary campaign.</summary>
    public StewardShiftSnapshot? CaptureStewardShift() => _stewardShift;
    public bool IsStewardShift => _stewardShift is not null;
    /// <summary>The steward the player controls, while the shift is on.</summary>
    public ulong? PlayerStewardId => _stewardShift?.PlayerId;
    internal string? StewardShiftCanonicalJson => _stewardShift is null ? null : System.Text.Json.JsonSerializer.Serialize(_stewardShift);

    /// <summary>The player's steward: no autonomy, posts, jobs, breaks or needs; only the player moves them.</summary>
    private bool IsPlayerSteward(ulong id) => _stewardShift?.PlayerId == id;

    /// <summary>Hands one hired steward to the player: on duty where they stand, idle, nothing queued.</summary>
    private void TakeStewardControl(ulong id)
    {
        var nav = _navigationAgents[new(id)];
        // They start at their post rather than walking in from the farmhouse.
        var post = StaffDutyCell(id, ResponseRole.Steward);
        var start = _traversalGrid!.Get(post).IsWalkable && StewardShiftRules.InField(post) ? post : TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
        var (x, z) = TraversalGrid.CellCentre(start);
        nav.XMillimetres = x; nav.ZMillimetres = z;
        StopPlayerRoute(nav);
        MutatePerson(id, person => { person.Admitted = true; person.Intent = MedicalIntent.WatchShow; person.Reason = "Steward on shift"; });
        // Face the main stage.
        var (sx, sz) = TraversalGrid.CellCentre(Stages[0].PerformerMarks[0]);
        _stewardShift = new(StewardShiftRules.Version, id, 0, 0, false, StewardShiftRules.FacingTowards(sx - x, sz - z));
    }

    private static void StopPlayerRoute(NavigationAgentState nav)
    {
        nav.Action = AgentNavigationAction.Idle; nav.Destination = null; nav.Route = []; nav.RouteIndex = 0;
        nav.SegmentOriginXMillimetres = nav.XMillimetres; nav.SegmentOriginZMillimetres = nav.ZMillimetres;
        nav.SegmentProgressMicrometres = 0; nav.MovementRemainder = 0; nav.IntentId = StewardShiftRules.Intent;
    }

    /// <summary>Closing ends the shift: the crew's ordinary rules walk the steward home with everyone else.</summary>
    private void ReleaseStewardControl()
    {
        if (_stewardShift?.PlayerId is null) return;
        _stewardShift = _stewardShift with { PlayerId = null, MoveX = 0, MoveZ = 0, Run = false };
    }

    private CommandResult? ValidatePlayerMove(EntityId? target, SetPlayerMoveCommand command)
    {
        if (target is not null || _stewardShift?.PlayerId is null || _preparation?.Status != PreparationStatus.Running)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Nobody is on shift.");
        if (command.DirectionX is < -1000 or > 1000 || command.DirectionZ is < -1000 or > 1000 || command.FacingDegrees is < 0 or > 359)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Direction runs −1000 to 1000 each way and facing 0–359°.");
        return null;
    }

    private void ApplyPlayerMove(SetPlayerMoveCommand command) => _stewardShift = _stewardShift! with
        { MoveX = command.DirectionX, MoveZ = command.DirectionZ, Run = command.Run, FacingDegrees = command.FacingDegrees };

    // Mechanics hook: interaction commands are validated here and applied in Execute once they exist.
    private CommandResult? ValidateStewardAction(EntityId? target, StewardActionCommand command)
    {
        if (target is not null || _stewardShift?.PlayerId is null || _preparation?.Status != PreparationStatus.Running)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Nobody is on shift.");
        if (!Enum.IsDefined(command.Action) || command.FacingDegrees is < 0 or > 359 || !StewardReach(command.FacingDegrees).Any(item => item.PersonId == command.TargetId))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Nobody like that is in reach.");
        return CommandResult.Rejected(CommandReasonCode.UnknownCommand, "Steward actions come with the mechanics pass.");
    }

    /// <summary>
    /// One tick of the player's walk, before anyone else moves: at a steward's pace (faster running), slowed by
    /// rough or muddy ground, stopped by anything blocked in the grid, anyone standing there, loose cows and the
    /// tanker. Blocked head-on, they slide along whichever axis is free.
    /// </summary>
    private void AdvancePlayerSteward()
    {
        if (_stewardShift is not { PlayerId: { } id } shift || _preparation?.Status != PreparationStatus.Running ||
            (shift.MoveX == 0 && shift.MoveZ == 0) || _traversalGrid is null) return;
        var nav = _navigationAgents[new(id)];
        var cell = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
        var cost = (long)_traversalGrid.Get(cell).CostPermille * GroundSlowPermille(cell);
        var micro = (long)RouteProgressMicrometresPerTick * nav.WalkingSpeedPermille / 1000 * (shift.Run ? StewardShiftRules.RunPermille : 1000) / 1000;
        var stepMillimetres = micro * 1_000_000 / Math.Max(1, cost) / 1000;
        var length = Math.Max(1000, IntegerSquareRoot((long)shift.MoveX * shift.MoveX + (long)shift.MoveZ * shift.MoveZ));
        var dx = (int)(stepMillimetres * shift.MoveX / length);
        var dz = (int)(stepMillimetres * shift.MoveZ / length);
        if (dx == 0 && dz == 0) return;
        var obstacles = new SpatialNeighbourIndex();
        AddCowOccupancy(obstacles);
        AddLavSuckerOccupancy(obstacles);
        // The larger component first, so a glancing blow slides the way they were mostly going.
        var tries = Math.Abs(dx) >= Math.Abs(dz) ? new[] { (dx, dz), (dx, 0), (0, dz) } : [(dx, dz), (0, dz), (dx, 0)];
        foreach (var (mx, mz) in tries)
        {
            if (mx == 0 && mz == 0) continue;
            if (!PlayerStepAllowed(id, nav.XMillimetres, nav.ZMillimetres, nav.XMillimetres + mx, nav.ZMillimetres + mz, obstacles)) continue;
            nav.XMillimetres += mx; nav.ZMillimetres += mz;
            nav.SegmentOriginXMillimetres = nav.XMillimetres; nav.SegmentOriginZMillimetres = nav.ZMillimetres;
            return;
        }
    }

    private bool PlayerStepAllowed(ulong id, int fromX, int fromZ, int toX, int toZ, SpatialNeighbourIndex obstacles)
    {
        var cell = TraversalGrid.WorldToCell(toX, toZ);
        if (!StewardShiftRules.InField(cell) || !_traversalGrid!.Get(cell).IsWalkable || CubicleClosedTo(id, cell) ||
            !TraversalSweep.IsWalkable(_traversalGrid, fromX, fromZ, toX, toZ)) return false;
        // Someone already too close may be stepped away from, never further into.
        bool Closer(int x, int z, long radius)
        {
            long ax = toX - x, az = toZ - z, bx = fromX - x, bz = fromZ - z;
            var after = ax * ax + az * az;
            return after < radius * radius && after < bx * bx + bz * bz;
        }
        foreach (var other in _navigationAgents.Values)
            if (other.Id.Value != id && MovementOccupant(other.Id.Value) &&
                Closer(other.XMillimetres, other.ZMillimetres, StewardShiftRules.PersonClearanceMillimetres)) return false;
        foreach (var (_, x, z) in obstacles.Points)
            if (Closer(x, z, StewardShiftRules.ObstacleClearanceMillimetres)) return false;
        return true;
    }

    /// <summary>
    /// People in reach and in front of the player steward, nearest first (then lowest id): everyone on site
    /// but themselves. <paramref name="facingDegrees"/> previews another facing (the camera's); the saved one
    /// otherwise. Data only. Mechanics hook: bins, loos and taps join this list later.
    /// </summary>
    public IReadOnlyList<StewardReachTarget> StewardReach(int? facingDegrees = null, int reachMillimetres = StewardShiftRules.ReachMillimetres)
    {
        if (_stewardShift is not { PlayerId: { } id } shift || !_navigationAgents.TryGetValue(new(id), out var me)) return [];
        var (fx, fz) = StewardShiftRules.Facing(facingDegrees ?? shift.FacingDegrees);
        var found = new List<StewardReachTarget>();
        foreach (var person in PeopleIn(PersonView.Roster))
        {
            if (person.Id == id || !person.Admitted || person.Departed || GuestWaitingForRelease(person.Id) ||
                !_navigationAgents.TryGetValue(new(person.Id), out var nav)) continue;
            long dx = nav.XMillimetres - me.XMillimetres, dz = nav.ZMillimetres - me.ZMillimetres;
            var distance = IntegerSquareRoot(dx * dx + dz * dz);
            if (distance > reachMillimetres || fx * dx + fz * dz < StewardShiftRules.ReachConeCosPermille * distance) continue;
            found.Add(new(person.Id, person.Name, person.Role, (int)distance));
        }
        return found.OrderBy(item => item.DistanceMillimetres).ThenBy(item => item.PersonId).ToArray();
    }

    private static string? ValidatePersistedStewardShift(SessionPersistenceSnapshot snapshot)
    {
        if (snapshot.StewardShift is not { } shift) return null;
        if (shift.Version != StewardShiftRules.Version || shift.FacingDegrees is < 0 or > 359 ||
            shift.MoveX is < -1000 or > 1000 || shift.MoveZ is < -1000 or > 1000)
            return "Steward shift input is invalid.";
        if (snapshot.Preparation is not { } p || snapshot.Disorder is not { } d || snapshot.TraversalGrid is null || snapshot.NavigationAgents is null)
            return "A steward shift needs a festival with stewards.";
        if (shift.PlayerId is not { } id)
            return shift.MoveX != 0 || shift.MoveZ != 0 || shift.Run ? "A finished steward shift holds no movement." : null;
        if (p.Status != PreparationStatus.Running) return "Only a running festival has a steward on shift.";
        if (!d.Stewards.Any(item => item.WorkerId == id) || p.People.SingleOrDefault(item => item.AgentId == id) is not { Role: ProtectedPersonRole.Staff, Admitted: true, Departed: false })
            return "The player must be a steward on duty.";
        if (snapshot.NavigationAgents.SingleOrDefault(item => item.Id == id) is not { } nav || nav.Action != (int)AgentNavigationAction.Idle ||
            nav.Route.Length != 0 || nav.DestinationX is not null || nav.IntentId != StewardShiftRules.Intent)
            return "The player steward's navigation must be their own.";
        var cell = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
        var grid = new TraversalGrid(snapshot.TraversalGrid.Cells.Select(item => new TerrainCellOverride(new GridCell(item.X, item.Z),
            (GroundSurface)item.Surface, item.IsWalkable, item.CostPermille, item.ElevationMillimetres, item.SlopePermille)));
        if (!StewardShiftRules.InField(cell) || !grid.Get(cell).IsWalkable) return "The player steward must stand on open ground in the field.";
        return null;
    }
}

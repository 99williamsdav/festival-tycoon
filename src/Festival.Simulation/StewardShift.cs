using System.Text.Json.Serialization;

namespace Festival.Simulation;

/// <summary>
/// Prototype steward shift: the player is one steward on the ground while the festival runs itself.
/// <paramref name="PlayerId"/> is the steward under the player's control, null once the shift is over
/// (closing hands them back to the crew so they walk home). The record being present marks the mode.
/// Movement input holds until the next <see cref="SetPlayerMoveCommand"/>; facing is whole degrees. The remainders
/// carry the part of a millimetre each axis didn't move last tick (−999 to 999 µm), so slow and diagonal walks keep pace.
/// The rest is the shift's work: the action being held, the guest being walked out, the radio's calls and the score.
/// </summary>
public sealed record StewardShiftSnapshot(int Version, ulong? PlayerId, int MoveX, int MoveZ, bool Run, int FacingDegrees,
    int RemainderX = 0, int RemainderZ = 0)
{
    /// <summary>The action the player is holding the key on; null when their hands are free.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public StewardActionProgress? Action { get; init; }
    /// <summary>How the last action ended, for the HUD's word on it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public StewardActionOutcome? LastOutcome { get; init; }
    /// <summary>The instigator the player just calmed or parted, who may be walked out until the offer lapses.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public StewardEscortOffer? EscortOffer { get; init; }
    /// <summary>The guest following the player to the gate.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ulong? EscortId { get; init; }
    /// <summary>The radio's open calls, then the last few closed, oldest first.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public StewardCall[]? Calls { get; init; }
    /// <summary>Guests already given directions today, in the order they were.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ulong[]? Directed { get; init; }
    /// <summary>Collapsed guests the player has stood by today, once each.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ulong[]? StoodByFor { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public StewardShiftScore? Score { get; init; }
    /// <summary>The crowd's average satisfaction (of 10,000) when the shift ended; null while it's on.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ClosingMood { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? EndedTick { get; init; }
    /// <summary>The stage the player is up on, while they are (<see cref="StewardOnStage"/>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public StewardOnStage? OnStage { get; init; }
    /// <summary>Every set the player has invaded today, in the order they first did, with how many times; null before the first.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public InvadedSet[]? InvadedSets { get; init; }
    /// <summary>
    /// A hands-on day only: present while the player is in the manager's view and their steward works by itself
    /// (<see cref="SetPlayerStewardControlCommand"/>); null while the player has them in first person, and always on the standalone shift.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public StewardAutopilot? Autopilot { get; init; }
    /// <summary>
    /// The laminated site plan is up in the player's right hand (<see cref="SetSitePlanCommand"/>): they walk at
    /// <see cref="StewardRadioRules.PlanWalkPermille"/>, can't run, and their hands and radio menu wait until it's down.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool PlanUp { get; init; }
    /// <summary>
    /// A hands-on day only: every call of the night once it closed, oldest first, if it went out over the radio or the
    /// player answered it (the radio's <see cref="Calls"/> keep only the last few). The results paper's "Your shift" reads it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public StewardCall[]? Log { get; init; }
}

/// <summary>
/// The player steward's movement input, held until the next one. The direction is world X/Z in permille
/// (either may be 0, neither beyond ±1000; a shorter vector walks slower, for a stick later). Facing is
/// whole degrees, 0 looking down +Z and 90 down +X.
/// </summary>
public sealed record SetPlayerMoveCommand(int DirectionX, int DirectionZ, bool Run, int FacingDegrees) : SessionCommand;

/// <summary>
/// What the player steward can do, by holding the action key on something in reach. <see cref="Release"/> lets go.
/// The order is the order of preference when one target allows more than one.
/// </summary>
public enum StewardActionKind
{
    Release = 0,
    BreakUp = 1, RadioMedic, StandBy, Calm, FreeLoo, Escort, HerdCow, Drink, EmptyBin, BodgeTap, Directions, SpreadStraw, PickLitter,
}

/// <summary>What a reach target is: someone, a facility, a piece of litter, a cow, or a patch of mud.</summary>
public enum StewardTargetKind { Person = 1, Toilet, Tap, Bar, Bin, Litter, Cow, Mud }

/// <summary>
/// Starts holding <paramref name="Action"/> on <paramref name="TargetId"/> (from <see cref="GameSession.StewardReach"/>,
/// with the facing it was found at), or with <see cref="StewardActionKind.Release"/> lets go of whatever is held.
/// </summary>
public sealed record StewardActionCommand(StewardActionKind Action, string TargetId, int FacingDegrees) : SessionCommand;

/// <summary>Something the player steward could act on: in reach and in front, with the action they'd take, if any. Data only.</summary>
public sealed record StewardReachTarget(string TargetId, StewardTargetKind Kind, string Name, StewardActionKind? Action, int DistanceMillimetres,
    ulong? PersonId = null, ProtectedPersonRole Role = default);

/// <summary>A held action: when the key went down and how long it must stay down.</summary>
public sealed record StewardActionProgress(StewardActionKind Kind, string TargetId, long StartedTick, int DurationTicks)
{
    /// <summary>Started by the autopilot: it takes longer (<see cref="StewardAutopilotRules.HoldTicks"/>) and calms and breaks up less well.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Auto { get; init; }
}

/// <summary>How an action ended: done (with how it went) or dropped. <paramref name="Text"/> is the line the HUD shows.</summary>
public sealed record StewardActionOutcome(StewardActionKind Kind, string TargetId, bool Completed, bool Success, long Tick, string Text);

public sealed record StewardEscortOffer(ulong GuestId, long UntilTick);

/// <summary>The shift's tally, for the report card.</summary>
public sealed record StewardShiftScore
{
    /// <summary>Calls that went out over the radio (or that the player got to first), and those the player answered.</summary>
    public int Calls { get; init; }
    public int Answered { get; init; }
    /// <summary>Summed over answered calls: from the radio call (or the incident, if it never went out) to the player's action.</summary>
    public long ResponseTicks { get; init; }
    /// <summary>
    /// Urgent calls (fights, collapses) that went out over the radio and closed with nobody dealing with them: a fight
    /// no steward ever made the handling attempt on, a collapse no medic reached or was on the way to. One the crew
    /// handled before the player got there is only unanswered.
    /// </summary>
    public int UrgentUnhandled { get; init; }
    /// <summary>Complaining, agitated or arguing guests calmed: fights prevented.</summary>
    public int Calmed { get; init; }
    public int CalmsFailed { get; init; }
    public int FightsBrokenUp { get; init; }
    public int BreakUpsFailed { get; init; }
    public int Escorted { get; init; }
    public int MedicRadioed { get; init; }
    public int StoodBy { get; init; }
    public int LoosFreed { get; init; }
    public int TapsBodged { get; init; }
    public int BinsEmptied { get; init; }
    public int LitterPicked { get; init; }
    public int StrawSpread { get; init; }
    public int CowsHerded { get; init; }
    public int Directions { get; init; }
    public int Drinks { get; init; }
    /// <summary>
    /// The mood the player's own actions added, in satisfaction (of 10,000) summed over guests: what directions really
    /// added, and for each other success the mood it spares the crowd (<see cref="StewardShiftRules.MoodCredit"/>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int MoodAdded { get; init; }
    /// <summary>Times the player got up on a stage while its set played (or stayed up as one started), and the ticks they spent there.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int StageInvasions { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long StageTicks { get; init; }
    /// <summary>Of <see cref="Answered"/>, those the player's steward answered working by themselves (a hands-on day's autopilot).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int AutoAnswered { get; init; }
    /// <summary>A hands-on day's calls Control gave to the hired crew that the player left to them: not in <see cref="Calls"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Covered { get; init; }
    /// <summary>Jobs the player radioed out from first person (<see cref="StewardRadioCommand"/>): the medic, Henry or the crew sent.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int RadioJobs { get; init; }
    /// <summary>
    /// Of <see cref="Calls"/>, those the player radioed a crew member onto and left to them (<see cref="StewardCall.Radioed"/>):
    /// half credit in the calls share, no response time. Of those, the fights: dealt with, neither prevented nor broken up.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Dispatched { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int FightsDispatched { get; init; }
}

public static class StewardShiftRules
{
    public const int Version = 3;
    /// <summary>The most calls a hands-on day's log holds: every call the score can count, and every one the crew covered.</summary>
    public const int LogMax = 200_000;
    /// <summary>The hedged field, in grid cells: −32 m to +32 m both ways. The player steward stays inside it.</summary>
    public const int FieldMinCell = 64, FieldMaxCell = 191;
    /// <summary>Running pace against a steward's walk.</summary>
    public const int RunPermille = 1_700;
    /// <summary>How close the player walks up to someone: a little more than the crowd keeps, so the view stays out of their heads.</summary>
    public const int PersonClearanceMillimetres = 450;
    /// <summary>How close loose cows and the tanker's body points may come to the player's centre.</summary>
    public const int ObstacleClearanceMillimetres = 650;
    /// <summary>How far ahead of the player, along their step, the ground must be open: the view never presses into a wall.</summary>
    public const int WallClearanceMillimetres = 250;
    public const int ReachMillimetres = 2_500;
    /// <summary>cos 40°: the reach cone's half-width either side of facing.</summary>
    public const int ReachConeCosPermille = 766;
    public const string Intent = "steward-shift.player";
    /// <summary>A guest being walked out by the player follows them on this route.</summary>
    public const string EscortIntent = "steward-shift.escort";

    /// <summary>
    /// How long the key must be held for each action, in ticks (80 a second): the designer's suggested holds for calm,
    /// break up, escort, help up (stand by) and pick up; freeing a jammed loo is two seconds, a third of the crew's.
    /// </summary>
    public static int DurationTicks(StewardActionKind kind) => kind switch
    {
        StewardActionKind.Calm => 128,
        StewardActionKind.BreakUp => 176,
        StewardActionKind.Escort => 96,
        StewardActionKind.RadioMedic => 80,
        StewardActionKind.StandBy => 200,
        StewardActionKind.FreeLoo => 160,
        StewardActionKind.BodgeTap => 480,
        StewardActionKind.EmptyBin => 240,
        StewardActionKind.PickLitter => 48,
        StewardActionKind.SpreadStraw => 320,
        StewardActionKind.HerdCow => 80,
        StewardActionKind.Directions => 80,
        StewardActionKind.Drink => 120,
        _ => 0,
    };
    /// <summary>A held action carries on while its target stays this close: reach plus half a metre's give.</summary>
    public const int HoldReachMillimetres = ReachMillimetres + 500;
    /// <summary>Litter is picked up from a little nearer.</summary>
    public const int LitterReachMillimetres = 2_000;
    /// <summary>The patch of ground a bale is spread on: this far ahead of the player.</summary>
    public const int MudAheadMillimetres = 1_000;

    /// <summary>A failed calm winds them up this much.</summary>
    public const int FailedCalmPressure = 1_500;
    /// <summary>How long after a calm or a break-up the instigator may still be walked out.</summary>
    public const int EscortOfferTicks = 2_400;
    /// <summary>An escorted guest further than this from the player has slipped away.</summary>
    public const int EscortLostMillimetres = 15_000;
    /// <summary>Within this of the gate, with the guest close behind, the player hands them out of it.</summary>
    public const int EscortGateMillimetres = 6_000, EscortCloseMillimetres = 5_000;
    /// <summary>How far behind the player the escorted guest walks, and how often their route is refreshed.</summary>
    public const int EscortFollowMillimetres = 1_200, EscortRefreshTicks = 20;
    /// <summary>A driven cow walks on while the player keeps within this; it gives up on them beyond the second.</summary>
    public const int HerdKeepUpMillimetres = 8_000, HerdLostMillimetres = 16_000;
    /// <summary>A lost guest pointed the right way is this much happier (of 10,000).</summary>
    public const int DirectionsSatisfaction = 150;

    /// <summary>
    /// The mood each successful action is credited with on the report, in satisfaction summed over guests; directions
    /// count what they really added instead (up to <see cref="DirectionsSatisfaction"/>). These are the harm each
    /// spares the crowd: a fight prevented or stopped (the pair's and the onlookers' upset), a guest let out of a jammed
    /// loo (their panic, a point or more a second), a tap mended (the next drinker's broken-tap upset, and the thirst),
    /// a bin emptied (the wasps), a piece of litter (a point a second to anyone near it), a bale on the mud, a cow taken
    /// home, a troublemaker seen out. Standing by, radioing the medic and drinking add none.
    /// </summary>
    public static int MoodCredit(StewardActionKind kind) => kind switch
    {
        StewardActionKind.Calm or StewardActionKind.BreakUp => 300,
        StewardActionKind.FreeLoo => 200,
        StewardActionKind.BodgeTap => 150,
        StewardActionKind.EmptyBin or StewardActionKind.SpreadStraw or StewardActionKind.HerdCow => 100,
        StewardActionKind.Escort => 50,
        StewardActionKind.PickLitter => 20,
        _ => 0,
    };
    /// <summary>Mood added for the report's full 15 points: about a good shift's worth (four calms, four directions, a bin, ten pieces of litter).</summary>
    public const int MoodForFullMarks = 2_000;

    /// <summary>The mood a score's fixed-credit successes add: everything but directions.</summary>
    public static long CreditedMood(StewardShiftScore score) =>
        (long)score.Calmed * MoodCredit(StewardActionKind.Calm) + (long)score.FightsBrokenUp * MoodCredit(StewardActionKind.BreakUp) +
        (long)score.LoosFreed * MoodCredit(StewardActionKind.FreeLoo) + (long)score.TapsBodged * MoodCredit(StewardActionKind.BodgeTap) +
        (long)score.BinsEmptied * MoodCredit(StewardActionKind.EmptyBin) + (long)score.StrawSpread * MoodCredit(StewardActionKind.SpreadStraw) +
        (long)score.CowsHerded * MoodCredit(StewardActionKind.HerdCow) + (long)score.Escorted * MoodCredit(StewardActionKind.Escort) +
        (long)score.LitterPicked * MoodCredit(StewardActionKind.PickLitter);

    /// <summary>
    /// The player's own thirst and heat, every four ticks: thirst always (half as fast in the rain), heat in a heatwave
    /// out of the shade (running adds half again to both). Shade cools fast, and rain slowly.
    /// </summary>
    public const int ThirstPerStep = 1, HeatPerStep = 1, ShadeCoolPerStep = 5, RainCoolPerStep = 1;
    /// <summary>A drink takes this much off thirst, and a little off heat.</summary>
    public const int DrinkThirstRelief = 6_000, DrinkHeatRelief = 2_500;
    /// <summary>From here the player slows a little, and more from the next.</summary>
    public const int NeedSlowLevel = 7_000, NeedSlowerLevel = 9_000;
    public const int NeedSlowPermille = 900, NeedSlowerPermille = 800;

    /// <summary>How long after an incident starts Control puts it out over the radio, in ticks.</summary>
    public static int RadioDelayTicks(StewardCallKind kind) => kind switch
    {
        StewardCallKind.Fight => 160,
        StewardCallKind.Collapse => 240,
        StewardCallKind.Argument => 480,
        StewardCallKind.StuckInLoo => 640,
        StewardCallKind.BrokenTap => 320,
        StewardCallKind.LooseCow => 160,
        _ => 0,
    };
    public static bool Urgent(StewardCallKind kind) => kind is StewardCallKind.Fight or StewardCallKind.Collapse;
    /// <summary>Closed calls kept for the radio strip after they close: this many, for this long.</summary>
    public const int ClosedCallsKept = 6, ClosedCallTicks = 800;

    public static bool InField(GridCell cell) =>
        cell.X is >= FieldMinCell and <= FieldMaxCell && cell.Z is >= FieldMinCell and <= FieldMaxCell;

    // Sines of 0–90° in permille, rounded to the nearest; the rest of the circle by symmetry. No floating point.
    private static readonly int[] QuarterSinPermille =
    [
        0, 17, 35, 52, 70, 87, 105, 122, 139, 156, 174, 191, 208, 225, 242, 259, 276, 292, 309, 326, 342, 358, 375, 391, 407, 423,
        438, 454, 469, 485, 500, 515, 530, 545, 559, 574, 588, 602, 616, 629, 643, 656, 669, 682, 695, 707, 719, 731, 743, 755, 766,
        777, 788, 799, 809, 819, 829, 839, 848, 857, 866, 875, 883, 891, 899, 906, 914, 921, 927, 934, 940, 946, 951, 956, 961, 966,
        970, 974, 978, 982, 985, 988, 990, 993, 995, 996, 998, 999, 999, 1000, 1000,
    ];

    private static int SinPermille(int d) => d <= 90 ? QuarterSinPermille[d] : d <= 180 ? QuarterSinPermille[180 - d] :
        d <= 270 ? -QuarterSinPermille[d - 180] : -QuarterSinPermille[360 - d];

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
        return (SinPermille(d), SinPermille((d + 90) % 360));
    }
}

public sealed partial class GameSession
{
    private StewardShiftSnapshot? _stewardShift;

    /// <summary>The steward shift's state; null in an ordinary campaign, and in a hands-on one before the gates open.</summary>
    public StewardShiftSnapshot? CaptureStewardShift() => _stewardShift;
    /// <summary>The standalone steward shift prototype (a hands-on campaign's day has the shift's state too, but isn't one).</summary>
    public bool IsStewardShift => _stewardShift is not null && _identity.IsStewardOnly;
    /// <summary>The steward the player controls, while the shift is on.</summary>
    public ulong? PlayerStewardId => _stewardShift?.PlayerId;
    internal string? StewardShiftCanonicalJson => _stewardShift is null ? null : System.Text.Json.JsonSerializer.Serialize(_stewardShift);

    /// <summary>The player's steward: no autonomy, posts, jobs, breaks or needs; only the player moves them.</summary>
    private bool IsPlayerSteward(ulong id) => _stewardShift?.PlayerId == id;

    /// <summary>Hands one hired steward to the player: on duty where they stand, idle, nothing queued.</summary>
    private void TakeStewardControl(ulong id)
    {
        if(!IsHandsOn)_identity=CampaignIdentity.StewardOnly;
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
        // A hands-on day opens in the manager's view, so the player's steward starts out working by itself.
        _stewardShift = new(StewardShiftRules.Version, id, 0, 0, false, StewardShiftRules.FacingTowards(sx - x, sz - z))
            { Calls = [], Score = new(), Autopilot = IsHandsOn ? new() : null };
    }

    private static void StopPlayerRoute(NavigationAgentState nav)
    {
        nav.Action = AgentNavigationAction.Idle; nav.Destination = null; nav.Route = []; nav.RouteIndex = 0;
        nav.SegmentOriginXMillimetres = nav.XMillimetres; nav.SegmentOriginZMillimetres = nav.ZMillimetres;
        nav.SegmentProgressMicrometres = 0; nav.MovementRemainder = 0; nav.IntentId = StewardShiftRules.Intent;
    }

    private CommandResult? ValidatePlayerMove(EntityId? target, SetPlayerMoveCommand command, bool autopilot = false)
    {
        if (target is not null || _stewardShift?.PlayerId is null || _preparation?.Status != PreparationStatus.Running)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Nobody is on shift.");
        if (!autopilot && _stewardShift.Autopilot is not null)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Your steward is working by themselves: take them over first.");
        if (command.DirectionX is < -1000 or > 1000 || command.DirectionZ is < -1000 or > 1000 || command.FacingDegrees is < 0 or > 359)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Direction runs −1000 to 1000 each way and facing 0–359°.");
        return null;
    }

    private void ApplyPlayerMove(SetPlayerMoveCommand command) => _stewardShift = _stewardShift! with
        { MoveX = command.DirectionX, MoveZ = command.DirectionZ, Run = command.Run, FacingDegrees = command.FacingDegrees };

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
        // With the site plan up they walk slowly and can't run.
        var pace = shift.PlanUp ? StewardRadioRules.PlanWalkPermille : shift.Run ? StewardShiftRules.RunPermille : 1000;
        var micro = (long)RouteProgressMicrometresPerTick * nav.WalkingSpeedPermille / 1000 * pace / 1000 *
            PlayerNeedPacePermille(id) / 1000;
        var stepMicrometres = micro * 1_000_000 / Math.Max(1, cost);
        var length = Math.Max(1000, IntegerSquareRoot((long)shift.MoveX * shift.MoveX + (long)shift.MoveZ * shift.MoveZ));
        var ux = stepMicrometres * shift.MoveX / length + shift.RemainderX;
        var uz = stepMicrometres * shift.MoveZ / length + shift.RemainderZ;
        var dx = (int)(ux / 1000); var dz = (int)(uz / 1000);
        int rx = (int)(ux - dx * 1000L), rz = (int)(uz - dz * 1000L);
        if (dx == 0 && dz == 0) { _stewardShift = shift with { RemainderX = rx, RemainderZ = rz }; return; }
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
            // An axis that didn't move keeps no remainder.
            _stewardShift = shift with { RemainderX = mx == 0 ? 0 : rx, RemainderZ = mz == 0 ? 0 : rz };
            return;
        }
        _stewardShift = shift with { RemainderX = 0, RemainderZ = 0 };
    }

    private bool PlayerStepAllowed(ulong id, int fromX, int fromZ, int toX, int toZ, SpatialNeighbourIndex obstacles)
    {
        var cell = TraversalGrid.WorldToCell(toX, toZ);
        if (!StewardShiftRules.InField(cell) || !_traversalGrid!.Get(cell).IsWalkable || CubicleClosedTo(id, cell) ||
            !BarnLayout.MovementSweepClear(fromX,fromZ,toX,toZ,BarnStageOpen) ||
            !TraversalSweep.IsWalkable(_traversalGrid, fromX, fromZ, toX, toZ)) return false;
        // Working by themselves, the steward never steps from the ground onto a stage's steps or deck, sliding or turning.
        if (_stewardShift?.Autopilot is not null && OnStageCells(Stages, cell) && !OnStageCells(Stages, TraversalGrid.WorldToCell(fromX, fromZ))) return false;
        // A little open ground ahead along the step, so the eyes stay out of walls and hedges.
        long sx = toX - fromX, sz = toZ - fromZ;
        var stepLength = Math.Max(1, IntegerSquareRoot(sx * sx + sz * sz));
        var ahead = TraversalGrid.WorldToCell(toX + (int)(sx * StewardShiftRules.WallClearanceMillimetres / stepLength),
            toZ + (int)(sz * StewardShiftRules.WallClearanceMillimetres / stepLength));
        if (!StewardShiftRules.InField(ahead) || !_traversalGrid.Get(ahead).IsWalkable || CubicleClosedTo(id, ahead)) return false;
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
        // Up on a stage, the drum kit on the drummer's mark is solid.
        foreach (var (x, z) in DrumKits())
            if (Closer(x, z, StageInvasionRules.DrumKitClearanceMillimetres)) return false;
        // The band's marks are theirs: a set can't start, or carries on stopped, without each of them on it.
        foreach (var (x, z) in BandMarks())
            if (Closer(x, z, StageInvasionRules.MarkClearanceMillimetres)) return false;
        // Nobody steps onto a stage's flight while its band is on it, climbing or coming down.
        if (StairsBusyAt(cell) && !StairsBusyAt(TraversalGrid.WorldToCell(fromX, fromZ))) return false;
        return true;
    }

    /// <summary>
    /// Checked on the restored session, where the live rules can say it: a steward on shift holds no job, claim,
    /// queue place, errand or carried thing, since nothing could have given them one, but the cow they're driving
    /// home; and the shift's own work is what the live rules allow (<see cref="StewardShiftWorkIssue"/>).
    /// </summary>
    private string? PlayerStewardWorkIssue()
    {
        if (_stewardShift?.PlayerId is not { } id) return StewardShiftWorkIssue();
        var claims = CaptureClaims(id);
        // Driving one cow home is theirs to do: it's held as fault work.
        if (_cows?.Loose.Count(cow => cow.HerderId == id) == 1 && _cows.Loose.Single(cow => cow.HerderId == id).Driving) claims &= ~PersonClaim.FaultWork;
        if (claims != PersonClaim.None) return $"The player steward holds other work ({claims}).";
        if (Vendors.Any(v => v.OwnerId == id || v.Queue.Contains(id)) ||
            EffectiveToilets(_facilities).Any(t => t.OwnerId == id || t.InterruptedOccupantId == id || t.Queue.Contains(id)) ||
            WaterPoints().Any(w => w.OwnerId == id || w.Queue.Contains(id) || w.Overflow.Contains(id)))
            return "The player steward stands in a queue.";
        if (_equipment?.WorkerId == id || _preparation?.MaintenanceWorkerId == id) return "The player steward is the maintenance worker.";
        var me = _persons[id];
        if (me.Intent != MedicalIntent.WatchShow || me.Held is not null || me.VendorId is not null || me.Order is not null ||
            me.ToiletStage != ToiletVisitStage.None || me.WaterQueueSlot is not null || me.ConductStage != DisorderStage.Calm)
            return "The player steward is busy with something of their own.";
        return StewardShiftWorkIssue();
    }

    private static string? ValidatePersistedStewardShift(SessionPersistenceSnapshot snapshot)
    {
        if (CampaignIdentity.Issue(snapshot.Identity) is {} identityIssue) return identityIssue;
        if (snapshot.Identity!.IsHandsOn) return ValidatePersistedHandsOnShift(snapshot);
        if (snapshot.StewardShift is not { } shift) return snapshot.Identity.IsStewardOnly?"A standalone steward campaign needs its shift record.":null;
        if(!snapshot.Identity.IsStewardOnly)return "A standalone shift requires steward control without management capability.";
        if (shift.Version != StewardShiftRules.Version || shift.FacingDegrees is < 0 or > 359 ||
            shift.MoveX is < -1000 or > 1000 || shift.MoveZ is < -1000 or > 1000 || shift.RemainderX is < -999 or > 999 || shift.RemainderZ is < -999 or > 999)
            return "Steward shift input is invalid.";
        if (snapshot.Preparation is not { } p || snapshot.Disorder is not { } d)
            return "A steward shift needs a festival with stewards.";
        if (!SavedPerkEffect(snapshot.Perks, StewardShiftPerk)) return "A steward shift needs the second steward's perk.";
        if (shift.Autopilot is not null) return "The standalone steward shift has no autopilot.";
        var workPreparation=p;var workSnapshot=snapshot;
        if(p.Attempt>1&&shift.PlayerId is null)
        {
            // Standalone control belongs only to its first attempt. Its retained closed log uses that clock.
            if(snapshot.Lifecycle?.Casualties.FirstOrDefault() is not {AttemptId:1} firstDeath||shift.EndedTick!=firstDeath.Tick||
                !snapshot.Lifecycle.Attempts.Any(a=>a.AttemptId==1&&a.Status==(int)EditionAttemptStatus.Failed)||
                !snapshot.Lifecycle.Hearings.Any(h=>h.AttemptId==1&&h.Status==(int)HearingStatus.FavourSpent))
                return "A retained standalone shift must belong to its first failed attempt.";
            workPreparation=p with{StartedTick=0};
            workSnapshot=snapshot with{CurrentTick=firstDeath.Tick};
        }
        if (ValidatePersistedStewardWork(shift, workSnapshot, workPreparation) is { } work) return work;
        // Council retry keeps a released shift's score/log, but correctly clears its old live field.
        if(p is {Status:PreparationStatus.Preparing,Attempt:>1}&&shift.PlayerId is null&&snapshot.TraversalGrid is null&&snapshot.NavigationAgents is null)
            return shift.MoveX==0&&shift.MoveZ==0&&!shift.Run&&shift.RemainderX==0&&shift.RemainderZ==0?null:"A retried released shift holds no movement.";
        if(snapshot.TraversalGrid is null||snapshot.NavigationAgents is null)return "A steward shift needs a festival with stewards.";
        if (shift.PlayerId is not { } id)
        {
            if (shift.MoveX != 0 || shift.MoveZ != 0 || shift.Run || shift.RemainderX != 0 || shift.RemainderZ != 0) return "A finished steward shift holds no movement.";
            // Closing, a death or a retry ends the shift; a first attempt still running has its steward on it.
            return p.Status is PreparationStatus.Departing or PreparationStatus.Finished or PreparationStatus.Failed || p.Attempt > 1
                ? null : "A running steward shift needs its steward.";
        }
        if (p.Status != PreparationStatus.Running || p.Attempt != 1) return "Only a running first attempt has a steward on shift.";
        if (d.Stewards.Length != 2 || d.Stewards[1].WorkerId != id || p.People.SingleOrDefault(item => item.AgentId == id) is not { Role: ProtectedPersonRole.Staff, Admitted: true, Departed: false })
            return "The player must be the second steward, on duty.";
        if (d.Stewards[1] is not { Stage: SecurityResponseStage.None, TargetId: null, Incapacitated: false })
            return "The player steward holds a steward response.";
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

namespace Festival.Simulation;

/// <summary>
/// Where each listener stands. Nothing works out a crowd's shape: every guest weighs the stage's audience ground for
/// themselves, one person to a half-metre cell, by two traits of their own. How keen they are on the act now playing
/// (their eagerness) pulls them towards the front; how much crowding they put up with (their personal space) pushes
/// them out of a crush. A busy set's front packs with the keen and the crowd-tolerant and thins towards the back; at a
/// quiet set the unkeen hang back from an empty front and only the fans go right up. All of it is integer arithmetic on
/// state every guest already has: nothing new is saved.
/// </summary>
public static class CrowdStanding
{
    /// <summary>A listener re-weighs their whole patch of ground at most this often.</summary>
    public const int DwellTicks = 800;
    /// <summary>A settled listener may take a step or two forward into a gap this long after they last moved.</summary>
    public const int ShuffleDwellTicks = 320;
    /// <summary>A listener sent aside who found nowhere free to go tries again this long after (a second).</summary>
    public const int AsideRetryTicks = 80;
    /// <summary>How far a placed listener looks when they re-weigh: two metres.</summary>
    public const int RedecideRadiusSquared = 16;
    /// <summary>A move must beat staying put by this much, so a settled crowd doesn't chase small changes.</summary>
    public const int ImprovementThreshold = 400;

    /// <summary>
    /// Crowding round a cell: three for each of the eight cells touching it that has someone in it, one for each of the
    /// sixteen a metre out. 0 is nobody within a metre; 40 is shoulder to shoulder on every side (four people a square metre).
    /// </summary>
    public const int MaxCrowding = 40;

    /// <summary>How keen a listener is on the act playing now, 0-100: their taste for it.</summary>
    public static int Eagerness(int enthusiasm) => Math.Clamp(enthusiasm, 0, 100);

    /// <summary>
    /// The crowding a guest is comfortable in, on the <see cref="MaxCrowding"/> scale: their own from 4 (wants a metre or
    /// more to themselves) to 30, keyed on the campaign seed and their id, nudged up to ten more by eagerness for the act.
    /// </summary>
    public static int PersonalSpace(ulong campaignSeed, ulong id, int eagerness) =>
        4 + (int)(Mix(campaignSeed ^ unchecked(id * 0x9E3779B97F4A7C15UL) ^ 0x5EA7_5EA7UL) % 27) + Eagerness(eagerness) / 10;

    /// <summary>Crowding from the people in the eight cells touching a cell and the sixteen a metre out.</summary>
    public static int Crowding(int touching, int withinMetre) => 3 * touching + (withinMetre - touching);

    /// <summary>
    /// What the set is worth to a listener where they stand, in permille of what it would be otherwise: fans near the
    /// front enjoy it more (up to a fifth more at the barrier), and anyone squeezed past their personal space less, in
    /// proportion to the excess (up to three tenths less).
    /// </summary>
    public static int EnjoymentPermille(int eagerness, int depth, int crowding, int personalSpace)
    {
        var keen = Eagerness(eagerness);
        var front = keen >= 60 ? (keen - 50) * 4 * 4 / (Math.Max(0, depth) + 4) : 0;
        var squeezed = Math.Min(300, Math.Max(0, crowding - personalSpace) * 10);
        return 1000 + front - squeezed;
    }

    private static ulong Mix(ulong value)
    {
        value = unchecked(value + 0x9E3779B97F4A7C15UL);
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
        return value ^ (value >> 31);
    }
}

/// <summary>
/// One stage's audience ground for the pass in progress: who is standing (or on their way to stand) on each cell, and a
/// summed-area table over it so a patch of any size is counted in four reads. Derived each tick, never saved.
/// </summary>
internal sealed class CrowdGround
{
    public readonly FestivalStage Stage;
    public readonly int MinX, MinZ, Width, Depth;
    private readonly byte[] _bodies;
    private readonly int[] _sums;
    private bool _dirty = true;
    // The deciding listener's own body, left out of every count while they weigh where to go.
    private GridCell? _self;

    public CrowdGround(FestivalStage stage)
    {
        Stage = stage;
        (MinX, MinZ) = (stage.AudienceBounds.MinX, stage.AudienceBounds.MinZ);
        Width = stage.AudienceBounds.MaxX - MinX + 1;
        Depth = stage.AudienceBounds.MaxZ - MinZ + 1;
        _bodies = new byte[Width * Depth];
        _sums = new int[(Width + 1) * (Depth + 1)];
    }

    public bool Inside(GridCell cell) => cell.X >= MinX && cell.X < MinX + Width && cell.Z >= MinZ && cell.Z < MinZ + Depth;
    public void Clear() { Array.Clear(_bodies); _dirty = true; _self = null; }
    public void Set(GridCell cell, bool body)
    {
        if (!Inside(cell)) return;
        _bodies[(cell.Z - MinZ) * Width + cell.X - MinX] = (byte)(body ? 1 : 0);
        _dirty = true;
    }
    public bool Body(GridCell cell) => Inside(cell) && _bodies[(cell.Z - MinZ) * Width + cell.X - MinX] != 0 && cell != _self;
    public void LeaveOut(GridCell? cell) => _self = cell is { } c && Body(c) ? c : null;

    /// <summary>People in the rectangle, clipped to the ground, less the deciding listener.</summary>
    public int Count(int minX, int maxX, int minZ, int maxZ)
    {
        if (_dirty) Rebuild();
        var x0 = Math.Max(minX, MinX) - MinX; var x1 = Math.Min(maxX, MinX + Width - 1) - MinX;
        var z0 = Math.Max(minZ, MinZ) - MinZ; var z1 = Math.Min(maxZ, MinZ + Depth - 1) - MinZ;
        if (x0 > x1 || z0 > z1) return 0;
        var stride = Width + 1;
        var count = _sums[(z1 + 1) * stride + x1 + 1] - _sums[z0 * stride + x1 + 1] - _sums[(z1 + 1) * stride + x0] + _sums[z0 * stride + x0];
        if (_self is { } self && self.X >= minX && self.X <= maxX && self.Z >= minZ && self.Z <= maxZ) count--;
        return count;
    }

    /// <summary>Crowding round a cell (<see cref="CrowdStanding.Crowding"/>), not counting anyone on it.</summary>
    public int CrowdingAt(GridCell cell)
    {
        var on = Body(cell) ? 1 : 0;
        var touching = Count(cell.X - 1, cell.X + 1, cell.Z - 1, cell.Z + 1) - on;
        var withinMetre = Count(cell.X - 2, cell.X + 2, cell.Z - 2, cell.Z + 2) - on;
        return CrowdStanding.Crowding(touching, withinMetre);
    }

    private void Rebuild()
    {
        var stride = Width + 1;
        for (var z = 0; z < Depth; z++)
        {
            var row = 0;
            for (var x = 0; x < Width; x++)
            {
                row += _bodies[z * Width + x];
                _sums[(z + 1) * stride + x + 1] = _sums[z * stride + x + 1] + row;
            }
        }
        _dirty = false;
    }
}

public sealed partial class GameSession
{
    // Each stage's crowd ground: buffers reused from tick to tick, refilled whenever a tick needs one.
    private CrowdGround?[] _crowdGrounds = [];

    /// <summary>The weights a listener puts on each part of a spot; see <see cref="CrowdSpotCost"/>.</summary>
    internal static class CrowdWeights
    {
        public const int PullBase = 10, PullScale = 1_000, PullOffset = 4;
        public const int Sightline = 150, SightlineDepth = 4;
        public const int Crowding = 40;
        public const int Travel = 60;
        public const int Squeeze = 30;
        public const int Blocked = 1_200, BlockedRows = 3;
        /// <summary>
        /// Below this eagerness a guest won't stand out in front of an empty patch: the nearly keen hang back three rows (a
        /// metre and a half), the least keen nine; eight people ahead of them, within three cells of straight on, is cover enough.
        /// </summary>
        public const int ShyBelow = 80, ShyRowsMin = 3, ShyRowsMax = 9, Support = 8, SupportWidth = 3;
        public const int Stable = 20;
    }

    /// <summary>
    /// Who stands on this stage's audience ground now: every on-site listener with a place, at it or on their way to it.
    /// Someone away at the bar or the toilet keeps their place but isn't a body in it.
    /// </summary>
    private CrowdGround CrowdGroundOf(int stage, LiveListener[] listeners, HashSet<ulong> departed)
    {
        if (_crowdGrounds.Length != Stages.Count) _crowdGrounds = new CrowdGround?[Stages.Count];
        if (_crowdGrounds[stage] is not { } ground || ground.Stage != Stages[stage]) _crowdGrounds[stage] = ground = new CrowdGround(Stages[stage]);
        ground.Clear();
        foreach (var listener in listeners)
            if (listener.Place is { } place && !departed.Contains(listener.AgentId) && !AudienceNavigationOwned(listener.AgentId))
                ground.Set(place, true);
        return ground;
    }

    /// <summary>
    /// What standing on <paramref name="cell"/> costs a listener (lower is better), coming from <paramref name="start"/>:
    /// <list type="bullet">
    /// <item>less the front's pull: the square of eagerness over depth, so fans want it far more, with diminishing returns
    /// further back (each row nearer the front is worth more than the one behind it);</item>
    /// <item>a side-on view, rising with the square of the distance off the stage's centre line;</item>
    /// <item>crowding past their personal space, rising with the square of the excess;</item>
    /// <item>the effort of getting there: the walk, and a squeeze for each person in the way, dear for the unkeen and the crowd-shy;</item>
    /// <item>heads in the way: someone straight ahead, more the nearer they stand, which fans a crowd out sideways;</item>
    /// <item>for the unkeen, standing exposed near an empty front with nobody ahead of them.</item>
    /// </list>
    /// </summary>
    private int CrowdSpotCost(CrowdGround ground, LiveListener listener, int personalSpace, GridCell cell, GridCell start)
    {
        var def = ground.Stage;
        var keen = CrowdStanding.Eagerness(listener.Enthusiasm);
        var depth = Math.Max(0, def.Depth(cell));
        var lateral = def.Lateral(cell);
        var want = (CrowdWeights.PullBase + keen * keen / 100) * CrowdWeights.PullScale;
        var pull = want / (depth + CrowdWeights.PullOffset);
        // Off to one side matters less the further back: the angle to the stage is what counts.
        var angle = lateral * CrowdWeights.SightlineDepth * 10 / (depth + CrowdWeights.SightlineDepth);
        var sightline = angle * angle * CrowdWeights.Sightline / 100;
        var excess = Math.Max(0, ground.CrowdingAt(cell) - personalSpace);
        var crowding = excess * excess * CrowdWeights.Crowding;
        int dx = Math.Abs(cell.X - start.X), dz = Math.Abs(cell.Z - start.Z);
        var travel = (Math.Max(dx, dz) + Math.Min(dx, dz) / 2) * CrowdWeights.Travel;
        var squeeze = SqueezedPast(ground, start, cell) * CrowdWeights.Squeeze * (CrowdStanding.MaxCrowding + 1 - personalSpace) * 100 / (50 + keen);
        var (aheadX, aheadZ) = (-def.Facing.X, -def.Facing.Z);
        var blocked = 0;
        for (var row = 1; row <= CrowdWeights.BlockedRows; row++)
            if (ground.Body(new(cell.X + aheadX * row, cell.Z + aheadZ * row))) blocked += CrowdWeights.Blocked / row;
        // Out in front of an empty patch, the front's pull doesn't reach a guest who isn't keen: short of the row they'd
        // hang back to, it is paid back twice over, less for each person already standing ahead of them.
        var exposure = 0;
        var shy = CrowdWeights.ShyBelow - keen;
        var comfortRow = shy <= 0 ? 0 : CrowdWeights.ShyRowsMin + shy * (CrowdWeights.ShyRowsMax - CrowdWeights.ShyRowsMin) / CrowdWeights.ShyBelow;
        if (depth < comfortRow)
        {
            var support = Math.Min(CrowdWeights.Support, Ahead(ground, cell, depth));
            exposure = (pull - want / (comfortRow + CrowdWeights.PullOffset)) * 2 * (CrowdWeights.Support - support) / CrowdWeights.Support;
        }
        var stable = (int)((listener.AgentId * 17 + (ulong)(cell.X * 13 + cell.Z * 7)) % 5) * CrowdWeights.Stable;
        return -pull + sightline + crowding + travel + squeeze + blocked + exposure + stable;
    }

    /// <summary>People standing between a cell and the front, within three cells either side of straight ahead.</summary>
    private static int Ahead(CrowdGround ground, GridCell cell, int depth)
    {
        if (depth <= 0) return 0;
        var def = ground.Stage;
        var w = CrowdWeights.SupportWidth;
        return def.Facing.X != 0
            ? ground.Count(Math.Min(cell.X - def.Facing.X, def.Front), Math.Max(cell.X - def.Facing.X, def.Front), cell.Z - w, cell.Z + w)
            : ground.Count(cell.X - w, cell.X + w, Math.Min(cell.Z - def.Facing.Z, def.Front), Math.Max(cell.Z - def.Facing.Z, def.Front));
    }

    /// <summary>The people on the straight line from <paramref name="from"/> to <paramref name="to"/>, ends excluded, on the audience ground.</summary>
    private static int SqueezedPast(CrowdGround ground, GridCell from, GridCell to)
    {
        // Walk back from the spot towards where the listener is, stopping once off the ground: nobody stands beyond it.
        int x = to.X, z = to.Z, dx = Math.Abs(from.X - to.X), dz = -Math.Abs(from.Z - to.Z);
        int sx = from.X > to.X ? 1 : -1, sz = from.Z > to.Z ? 1 : -1, error = dx + dz, count = 0;
        while (true)
        {
            var twice = 2 * error;
            if (twice >= dz) { if (x == from.X) break; error += dz; x += sx; }
            if (twice <= dx) { if (z == from.Z) break; error += dx; z += sz; }
            var cell = new GridCell(x, z);
            if (cell == from || !ground.Inside(cell)) break;
            if (ground.Body(cell)) count++;
        }
        return count;
    }

    /// <summary>
    /// The cells a listener may stand on: on this stage's audience ground, walkable, clear of every queue and its
    /// corridor, and not someone else's place. Worked out once a pass, when someone first needs it.
    /// </summary>
    private bool[] CrowdStandable(CrowdGround ground, HashSet<GridCell> claimed, ref HashSet<GridCell>? queueGround)
    {
        var standable = new bool[ground.Width * ground.Depth];
        for (var z = 0; z < ground.Depth; z++)
        for (var x = 0; x < ground.Width; x++)
        {
            var cell = new GridCell(ground.MinX + x, ground.MinZ + z);
            standable[z * ground.Width + x] = _traversalGrid!.Contains(cell) && _traversalGrid.Get(cell).IsWalkable && !ground.Stage.Access(cell) &&
                !claimed.Contains(cell) && !NearQueue(cell, queueGround ??= AllQueueGround());
        }
        return standable;
    }

    /// <summary>
    /// The cells a listener weighs: the whole ground before they have a place; within two metres of where they stand
    /// when they re-weigh; and a step or two forward (straight on or a half-step to either side) when they only shuffle.
    /// </summary>
    private static IEnumerable<GridCell> CrowdCandidates(CrowdGround ground, GridCell? place, GridCell here, bool full)
    {
        if (place is null)
        {
            for (var z = 0; z < ground.Depth; z++)
            for (var x = 0; x < ground.Width; x++)
                yield return new GridCell(ground.MinX + x, ground.MinZ + z);
            yield break;
        }
        if (full)
        {
            for (var dz = -4; dz <= 4; dz++)
            for (var dx = -4; dx <= 4; dx++)
            {
                var cell = new GridCell(here.X + dx, here.Z + dz);
                if ((dx != 0 || dz != 0) && dx * dx + dz * dz <= CrowdStanding.RedecideRadiusSquared && ground.Inside(cell)) yield return cell;
            }
            yield break;
        }
        var (aheadX, aheadZ) = (-ground.Stage.Facing.X, -ground.Stage.Facing.Z);
        for (var step = 1; step <= 2; step++)
        for (var side = -1; side <= 1; side++)
        {
            var cell = new GridCell(here.X + aheadX * step + aheadZ * side, here.Z + aheadZ * step + aheadX * side);
            if (ground.Inside(cell)) yield return cell;
        }
    }

    // Set only while a listener's route to their new place is planned: the crowd costs extra to walk through, so they
    // thread the gaps where there are any and squeeze past people only where there aren't.
    private int[]? _crowdRouteCost;
    private int[]? _crowdRouteBuffer;
    private bool[]? _crowdRouteMarked;
    private readonly List<int> _crowdRouteMarks = [];
    private int[]? _crowdRouteBase;
    private bool _crowdRouteBuilt;
    private const int CrowdRouteExtraPermille = 2_000;

    /// <summary>
    /// The ground's own route cost with this crowd's bodies on top. The 256-by-256 copy is made only when the ground's
    /// cost changes; otherwise only the audience cells whose body changed are touched. Its content is always just the
    /// ground's cost plus this one crowd, so nothing carries over from one stage or pass to the next.
    /// </summary>
    private int[] CrowdRouteCost(CrowdGround ground)
    {
        var cost = _crowdRouteBuffer ??= new int[TraversalGrid.Width * TraversalGrid.Depth];
        var marked = _crowdRouteMarked ??= new bool[cost.Length];
        if (!_crowdRouteBuilt || !ReferenceEquals(_crowdRouteBase, _groundRouteCost))
        {
            if (_groundRouteCost is { } wet) Array.Copy(wet, cost, cost.Length); else Array.Clear(cost);
            foreach (var index in _crowdRouteMarks) marked[index] = false;
            _crowdRouteMarks.Clear();
            _crowdRouteBase = _groundRouteCost;
            _crowdRouteBuilt = true;
        }
        // Another crowd's bodies come off first.
        for (var i = _crowdRouteMarks.Count - 1; i >= 0; i--)
        {
            var index = _crowdRouteMarks[i];
            var cell = new GridCell(index % TraversalGrid.Width, index / TraversalGrid.Width);
            if (ground.Inside(cell) && ground.Body(cell)) continue;
            cost[index] -= CrowdRouteExtraPermille; marked[index] = false;
            _crowdRouteMarks.RemoveAt(i);
        }
        for (var z = 0; z < ground.Depth; z++)
        for (var x = 0; x < ground.Width; x++)
        {
            var cell = new GridCell(ground.MinX + x, ground.MinZ + z);
            if (!_traversalGrid!.Contains(cell) || !ground.Body(cell)) continue;
            var index = cell.Z * TraversalGrid.Width + cell.X;
            if (marked[index]) continue;
            cost[index] += CrowdRouteExtraPermille; marked[index] = true;
            _crowdRouteMarks.Add(index);
        }
        return cost;
    }

    // Audience cells someone other than their own listener is resting on or bound for, worked out once a tick.
    private long _claimedTick = -1;
    private readonly HashSet<GridCell> _claimedCells = [];
    private readonly Dictionary<ulong, GridCell> _claimPlaces = [];

    /// <summary>
    /// Audience cells nobody may stand on now, and whose listener steps aside: where anyone not standing on their own
    /// listening place has come to rest (a walker who stopped, a guest who collapsed or is fighting, a medic at work), and
    /// where anyone on any errand but listening is on their way to.
    /// </summary>
    private HashSet<GridCell> ClaimedAudienceCells()
    {
        if (_claimedTick == CurrentTick) return _claimedCells;
        _claimedTick = CurrentTick;
        _claimedCells.Clear();
        _claimPlaces.Clear();
        foreach (var live in _livePerformances)
            if (live is not null)
                foreach (var listener in live.Listeners)
                    if (listener.Place is { } place) _claimPlaces[listener.AgentId] = place;
        foreach (var agent in _navigationAgents.Values)
        {
            if (PersonIn(PersonView.Roster, agent.Id.Value) is not { Departed: false }) continue;
            if (agent.Action == AgentNavigationAction.Travelling)
            {
                // Heading back to their own place by another errand's route is not a claim on it.
                if (agent.Destination is { } goal && agent.IntentId?.StartsWith("performance.listen", StringComparison.Ordinal) != true && InStagesAudience(goal) &&
                    !(_claimPlaces.TryGetValue(agent.Id.Value, out var mine) && mine == goal))
                    _claimedCells.Add(goal);
                continue;
            }
            var here = TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres);
            if (!InStagesAudience(here) || _claimPlaces.TryGetValue(agent.Id.Value, out var own) && own == here) continue;
            _claimedCells.Add(here);
        }
        return _claimedCells;
    }

    /// <summary>A listener's personal space for the act they're listening to now.</summary>
    private int PersonalSpaceOf(LiveListener listener) => CrowdStanding.PersonalSpace(CampaignSeed, listener.AgentId, listener.Enthusiasm);

    /// <summary>What the crowd round a listener's place makes of the set, in permille (<see cref="CrowdStanding.EnjoymentPermille"/>).</summary>
    private int CrowdEnjoymentPermille(CrowdGround ground, LiveListener listener, GridCell place)
    {
        ground.LeaveOut(place);
        var crowding = ground.CrowdingAt(place);
        ground.LeaveOut(null);
        return CrowdStanding.EnjoymentPermille(listener.Enthusiasm, ground.Stage.Depth(place), crowding, PersonalSpaceOf(listener));
    }
}

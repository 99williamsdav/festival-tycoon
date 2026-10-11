namespace Festival.Simulation;

/// <summary>The field gate between the farmer's pasture and the festival: shut, broken open, or mended with twine.</summary>
public enum PastureGateState { Closed, Broken, Repaired }
public enum CowActivity { Grazing, Ambling, Herded }

/// <summary>A cow loose on the festival site, where it ambles and grazes until a steward drives it home.</summary>
/// <param name="Route">The cells it's walking, when ambling or being driven; empty when standing.</param>
/// <param name="UntilTick">When a grazing cow next moves on.</param>
public sealed record LooseCow(string Id, int XMillimetres, int ZMillimetres, CowActivity Activity, GridCell[] Route, int RouteIndex, long UntilTick)
{
    /// <summary>The steward sent to drive it home, once the player has asked.</summary>
    public ulong? HerderId { get; init; }
    /// <summary>When the steward was sent; -1 when nobody is herding it.</summary>
    public long HerdStartedTick { get; init; } = -1;
    /// <summary>The steward has reached it and is walking it back to the gate.</summary>
    public bool Driving { get; init; }
}

/// <param name="Escaped">How many cows have got out today, for naming the next.</param>
public sealed record CowsSnapshot(int Version, PastureGateState Gate, LooseCow[] Loose, int Escaped);

/// <summary>Sends the nearest free steward to drive a loose cow back to its field.</summary>
public sealed record HerdCowCommand(string CowId) : SessionCommand;

public static class CowRules
{
    public const int HerdSize = 10;
    /// <summary>The site side of the field gate in the east hedge, where loose cows come in and are driven back out.</summary>
    public static readonly GridCell GateInside = new(187, 122);
    /// <summary>A small chance each festival minute while the gate holds: about two days in five, it gives way.</summary>
    public const int GateBreakChancePer10k = 11;
    public const int EscapeMin = 2, EscapeMax = 4;
    /// <summary>An amble: about half a metre a second; a little brisker with a steward behind it.</summary>
    public const int AmbleMillimetresPerTick = 6, DrivenMillimetresPerTick = 9;
    public const int WanderRadiusCells = 14;
    public const int GrazeMinTicks = 1_600, GrazeMaxTicks = 4_800;
    /// <summary>How often a cow picks the grass behind a generator, bar or food van to graze, where the cables run.</summary>
    public const int CableBiasPercent = 20;
    public const int CableReachMillimetres = 2_500;
    /// <summary>The chance each festival minute that a cow grazing by an intact cable chews through it.</summary>
    public const int ChewChancePer10k = 200;
    /// <summary>How close a steward must get before the cow walks on ahead of them (a cow keeps people a body's width off).</summary>
    public const int HerdReachCells = 3;
    /// <summary>A cow's bulk, for people stepping round it: its centre and four points this far out.</summary>
    public const int BodyMillimetres = 650;
    /// <summary>
    /// How close one cow's middle comes to another's: a cow's length (2.2 m), so one walking behind another is nose to tail, not
    /// inside it. A walking cow never steps closer than this to another; it stops to graze, or waits for a herded cow ahead to move on.
    /// </summary>
    public const int SpacingMillimetres = 2_200;
    /// <summary>A cow ambles off to grass with no other cow standing, or heading, within this many cells (three metres).</summary>
    public const int TargetClearCells = 6;
    /// <summary>How far a grazing cow in a driven one's way is moved aside, in cells (two and a half metres).</summary>
    public const int AsideCells = 5;
    /// <summary>Where the cows come in, fanned out inside the gate rather than stacked on its one cell (cells from it).</summary>
    public static readonly (int X, int Z)[] EntryOffsets = [(-2, 0), (-5, -4), (-5, 4), (-8, 0)];
    /// <summary>Long enough to cross the site twice at a cow's pace; a herd that takes longer has gone wrong and gives up.</summary>
    public const int HerdDeadlineTicks = 9_600;
    /// <summary>Loose cows keep to the festival site, a cow's length clear of its hedges.</summary>
    public const int SiteMinCell = 69, SiteMaxCell = 186;

    /// <summary>A deterministic draw in 0..range-1 from the campaign seed and what's being decided.</summary>
    public static int Draw(ulong seed, string key, long a, ulong b, int range)
    {
        var value = seed;
        foreach (var ch in key) value = unchecked((value ^ ch) * 0x100000001B3UL);
        value = unchecked(value + (ulong)a * 0x9E3779B97F4A7C15UL + b * 0xD1B54A32D192ED03UL);
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return (int)((value ^ (value >> 31)) % (ulong)range);
    }
}

public sealed partial class GameSession
{
    private CowsSnapshot? _cows;
    private static readonly CowsSnapshot EmptyCows = new(1, PastureGateState.Closed, [], 0);
    public CowsSnapshot? CaptureCows() => _cows;
    internal string? CowsCanonicalJson => _cows is null ? null : System.Text.Json.JsonSerializer.Serialize(_cows);

    /// <summary>
    /// Loose cows are obstacles in the crowd: people step round them as they do round each other. Each stands for its
    /// centre and four points about it, under ids no person can have.
    /// </summary>
    private void AddCowOccupancy(SpatialNeighbourIndex occupied)
    {
        if (_cows is not { Loose.Length: > 0 } cows) return;
        var id = CowOccupancyBase;
        foreach (var cow in cows.Loose)
            foreach (var (dx, dz) in new[] { (0, 0), (CowRules.BodyMillimetres, 0), (-CowRules.BodyMillimetres, 0), (0, CowRules.BodyMillimetres), (0, -CowRules.BodyMillimetres) })
                occupied.Add(new EntityId(id--), cow.XMillimetres + dx, cow.ZMillimetres + dz);
    }

    /// <summary>
    /// A walker sidestepping near a loose cow plans its new route round the cow, not back through it: the path grid
    /// doesn't know about cows, so a grazing cow on the only planned line otherwise holds a walker shuffling in front
    /// of it for good (a guest who never left a Tier 2 pond day). Cells the cow's body covers cost twenty times as
    /// much; null when no cow is close enough to matter, so every other replan is unchanged.
    /// </summary>
    private int[]? CowDetourCost(NavigationAgentState agent)
    {
        if (_cows is not { Loose.Length: > 0 } cows) return null;
        const int Near = 3_000, Covered = CowRules.BodyMillimetres + TraversalGrid.CellSizeMillimetres / 2;
        // The cows near this walker, by the cells their bodies cover; walkers near the same cows share one overlay.
        _cowDetourBounds.Clear();
        foreach (var cow in cows.Loose)
        {
            if (Math.Abs(cow.XMillimetres - agent.XMillimetres) > Near || Math.Abs(cow.ZMillimetres - agent.ZMillimetres) > Near) continue;
            _cowDetourBounds.Add((TraversalGrid.WorldToCell(cow.XMillimetres - Covered, cow.ZMillimetres - Covered),
                TraversalGrid.WorldToCell(cow.XMillimetres + Covered, cow.ZMillimetres + Covered)));
        }
        if (_cowDetourBounds.Count == 0) return null;
        // Overlays last one tick and one ground cost; their buffers are reused rather than reallocated (64k cells each).
        if (_cowDetourTick != CurrentTick || !ReferenceEquals(_cowDetourBase, _groundRouteCost))
        {
            foreach (var overlay in _cowDetours.Values) _cowDetourSpare.Push(overlay);
            _cowDetours.Clear();
            _cowDetourTick = CurrentTick; _cowDetourBase = _groundRouteCost;
        }
        var key = string.Join(';', _cowDetourBounds.Select(b => $"{b.Lower.X},{b.Lower.Z},{b.Upper.X},{b.Upper.Z}"));
        if (_cowDetours.TryGetValue(key, out var cached)) return cached;
        var cost = _cowDetourSpare.TryPop(out var spare) ? spare : new int[TraversalGrid.Width * TraversalGrid.Depth];
        if (_groundRouteCost is { } ground) Array.Copy(ground, cost, cost.Length); else Array.Clear(cost);
        // Cells a cow's body covers cost twenty times as much, added once for each cow covering them.
        foreach (var (lower, upper) in _cowDetourBounds)
            for (var z = Math.Max(0, lower.Z); z <= Math.Min(TraversalGrid.Depth - 1, upper.Z); z++)
            for (var x = Math.Max(0, lower.X); x <= Math.Min(TraversalGrid.Width - 1, upper.X); x++)
                cost[z * TraversalGrid.Width + x] += CowDetourExtra;
        _cowDetours.Add(key, cost);
        return cost;
    }
    private const int CowDetourExtra = 20_000;
    // Derived each tick from the cows and the ground, never saved.
    private readonly Dictionary<string, int[]> _cowDetours = new(StringComparer.Ordinal);
    private readonly Stack<int[]> _cowDetourSpare = new();
    private long _cowDetourTick = -1;
    private int[]? _cowDetourBase;
    private readonly List<(GridCell Lower, GridCell Upper)> _cowDetourBounds = [];

    /// <summary>Whether a steward is out herding a cow: like a fault job, it holds their route until it's done.</summary>
    private bool CowWorkOwns(ulong id) => _cows?.Loose.Any(cow => cow.HerderId == id) == true;

    /// <summary>Where each utility's power cable runs behind it, for cows to graze by and maintenance to splice.</summary>
    public IReadOnlyList<(string Utility, GridCell Spot)> CableSpots()
    {
        var spots = new List<(string, GridCell)>();
        if (_equipment is { } e) spots.Add(("generator", WalkableNear(TraversalGrid.WorldToCell(e.XMillimetres + 1_500, e.ZMillimetres))));
        foreach (var vendor in Vendors)
        {
            var back = RotateWaterOffset(new(0, -3), vendor.QuarterTurns);
            spots.Add((vendor.Id, WalkableNear(new(vendor.Cell.X + back.X, vendor.Cell.Z + back.Z))));
        }
        return spots;
    }

    private GridCell WalkableNear(GridCell cell)
    {
        if (_traversalGrid is null) return cell;
        for (var r = 0; r <= 4; r++)
            for (var dx = -r; dx <= r; dx++)
                for (var dz = -r; dz <= r; dz++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                    var c = new GridCell(cell.X + dx, cell.Z + dz);
                    if (_traversalGrid!.Contains(c) && _traversalGrid.Get(c).IsWalkable) return c;
                }
        return cell;
    }

    /// <summary>A chewed cable has cut this utility's power: "generator", or a bar or van's id.</summary>
    public bool CableCut(string utility) => OpenFaults.Any(f => f.Kind == FacilityFaultKind.ChewedCable && f.FacilityId == "cable." + utility && f.Stage == FacilityFaultStage.Active);

    /// <summary>The stage has power: no emergency cut-off and the generator's lead intact.</summary>
    public bool StagePowered => _equipment?.Stage is not (EquipmentStage.Isolated or EquipmentStage.Terminal) && _equipment?.StageCut != true && !CableCut("generator");

    private void AdvanceCows()
    {
        if (_cows is null || _preparation is not { Status: PreparationStatus.Running } p || _traversalGrid is null) return;
        var cows = _cows;
        if (cows.Gate == PastureGateState.Closed && CurrentTick % 80 == 0 && _faults is not { Disabled: true } &&
            CurrentTick - p.StartedTick < PreparedEditionDurationTicks * 9 / 10 &&
            FaultRules.Roll(CampaignSeed, "gate.pasture", CurrentTick, (ulong)p.Attempt, CowRules.GateBreakChancePer10k))
            cows = BreakGate(cows, p);
        // Each cow moves in turn against where the others stand now (those already moved this tick at their new place), and
        // may nudge a grazing cow in a herded one's way; one that's gone home leaves a gap.
        var herd = cows.Loose.ToArray();
        for (var i = 0; i < herd.Length; i++) herd[i] = AdvanceCow(herd, i)!;
        _cows = cows with { Loose = herd.Where(cow => cow is not null).ToArray() };
    }

    private CowsSnapshot BreakGate(CowsSnapshot cows, PreparationSnapshot p)
    {
        AddFault($"gate:{CurrentTick}", FacilityFaultKind.BrokenGate, "gate.pasture", 0);
        var count = CowRules.EscapeMin + CowRules.Draw(CampaignSeed, "gate.escape", CurrentTick, (ulong)p.Attempt, CowRules.EscapeMax - CowRules.EscapeMin + 1);
        // Each comes through to its own patch just inside the gate, a body or two from the next, and they set off in turn.
        var escaped = Enumerable.Range(0, count).Select(i =>
        {
            var (dx, dz) = CowRules.EntryOffsets[i % CowRules.EntryOffsets.Length];
            var at = TraversalGrid.CellCentre(WalkableNear(new(CowRules.GateInside.X + dx, CowRules.GateInside.Z + dz)));
            return new LooseCow($"cow.{cows.Escaped + i + 1}", at.XMillimetres, at.ZMillimetres, CowActivity.Grazing, [], 0, CurrentTick + 160L * (i + 1));
        }).ToArray();
        MedicalEvent("cows:loose", $"The pasture gate broke and {count} cows wandered into the festival.");
        return cows with { Gate = PastureGateState.Broken, Loose = cows.Loose.Concat(escaped).ToArray(), Escaped = cows.Escaped + count };
    }

    /// <summary>One tick for a loose cow; null once it's back in its field.</summary>
    /// <param name="herd">Every loose cow this tick, already-moved ones at their new place; null where one has gone home.</param>
    private LooseCow? AdvanceCow(LooseCow?[] herd, int index)
    {
        var cow = herd[index]!;
        if (cow.HerderId is { } herder) return AdvanceHerdedCow(herd, index, herder);
        if (cow.Activity == CowActivity.Ambling)
        {
            var (moved, blocker) = StepCowApart(herd, index, CowRules.AmbleMillimetresPerTick);
            // A cow that would walk into another stops where it is and grazes, rather than pushing through.
            return moved.RouteIndex < moved.Route.Length && blocker < 0 ? moved : moved with
            {
                Activity = CowActivity.Grazing, Route = [], RouteIndex = 0,
                UntilTick = CurrentTick + CowRules.GrazeMinTicks + CowRules.Draw(CampaignSeed, cow.Id, CurrentTick, 1, CowRules.GrazeMaxTicks - CowRules.GrazeMinTicks)
            };
        }
        if (CurrentTick % 80 == 0) MaybeChewCable(cow);
        if (CurrentTick < cow.UntilTick) return cow;
        return CowWander(herd, cow);
    }

    /// <summary>
    /// Steps a cow along its route unless another cow stands within <see cref="CowRules.SpacingMillimetres"/> of where it
    /// would be and more or less straight ahead (within 60 degrees of the way it's stepping). It walks on past one off to
    /// the side, which keeps them better than a metre and a half apart, and away from one already that close. Returns the
    /// cow unmoved and the index of the cow in its way when blocked, or -1.
    /// </summary>
    private static (LooseCow Cow, int Blocker) StepCowApart(LooseCow?[] herd, int index, int pace, int ignore = -1)
    {
        var cow = herd[index]!;
        var moved = StepCow(cow, pace);
        for (var j = 0; j < herd.Length; j++)
            if (j != index && j != ignore && herd[j] is { } other && InTheWay(cow, moved, other)) return (cow, j);
        return (moved, -1);
    }

    /// <summary>
    /// Whether <paramref name="other"/> stops a cow stepping from <paramref name="cow"/> to <paramref name="moved"/>: the
    /// step would end within <see cref="CowRules.SpacingMillimetres"/> of it, and it lies within 60 degrees of the step.
    /// </summary>
    private static bool InTheWay(LooseCow cow, LooseCow moved, LooseCow other)
    {
        long sx = moved.XMillimetres - cow.XMillimetres, sz = moved.ZMillimetres - cow.ZMillimetres;
        if (sx == 0 && sz == 0) return false;
        const long Spacing = (long)CowRules.SpacingMillimetres * CowRules.SpacingMillimetres;
        long ax = moved.XMillimetres - other.XMillimetres, az = moved.ZMillimetres - other.ZMillimetres;
        if (ax * ax + az * az >= Spacing) return false;
        // Ahead: the step and the way to the other cow within 60 degrees, i.e. cos > 1/2.
        long tx = other.XMillimetres - cow.XMillimetres, tz = other.ZMillimetres - cow.ZMillimetres;
        var dot = sx * tx + sz * tz;
        return dot > 0 && 4 * dot * dot > (sx * sx + sz * sz) * (tx * tx + tz * tz);
    }

    /// <summary>
    /// Grass another cow is standing on or heading for (within <see cref="CowRules.TargetClearCells"/>): cows pick
    /// elsewhere, so they spread over the site rather than trailing each other to one spot.
    /// </summary>
    private static bool TargetTaken(LooseCow?[] herd, LooseCow cow, GridCell target)
    {
        static bool Close(GridCell a, GridCell b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Z - b.Z)) < CowRules.TargetClearCells;
        return herd.Any(other => other is not null && other.Id != cow.Id &&
            (Close(TraversalGrid.WorldToCell(other.XMillimetres, other.ZMillimetres), target) || other.Route.Length > 0 && Close(other.Route[^1], target)));
    }

    /// <summary>
    /// Where a loose cow may be, as a save checks it: the site, plus the gate's own cells. A route between two site cells
    /// can still cut through the strip between the site's edge and the hedge, so every cell of a route is checked.
    /// </summary>
    internal static bool CowOnSite(GridCell cell) => cell.X >= CowRules.SiteMinCell && cell.X <= CowRules.GateInside.X + 1 &&
        cell.Z >= CowRules.SiteMinCell && cell.Z <= CowRules.SiteMaxCell;

    private LooseCow CowWander(LooseCow?[] herd, LooseCow cow)
    {
        var here = TraversalGrid.WorldToCell(cow.XMillimetres, cow.ZMillimetres);
        var spots = CableSpots();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            GridCell target;
            if (spots.Count > 0 && CowRules.Draw(CampaignSeed, cow.Id + ":cable", CurrentTick, (ulong)attempt, 100) < CowRules.CableBiasPercent)
            {
                // Somewhere by the cable, not on the one cell every cow by it would pick.
                var spot = spots[CowRules.Draw(CampaignSeed, cow.Id + ":which", CurrentTick, (ulong)attempt, spots.Count)].Spot;
                target = new(spot.X - 3 + CowRules.Draw(CampaignSeed, cow.Id + ":cx", CurrentTick, (ulong)attempt, 7),
                    spot.Z - 3 + CowRules.Draw(CampaignSeed, cow.Id + ":cz", CurrentTick, (ulong)attempt, 7));
            }
            else
            {
                var span = CowRules.WanderRadiusCells * 2 + 1;
                target = new(here.X - CowRules.WanderRadiusCells + CowRules.Draw(CampaignSeed, cow.Id + ":x", CurrentTick, (ulong)attempt, span),
                    here.Z - CowRules.WanderRadiusCells + CowRules.Draw(CampaignSeed, cow.Id + ":z", CurrentTick, (ulong)attempt, span));
            }
            if (target.X is < CowRules.SiteMinCell or > CowRules.SiteMaxCell || target.Z is < CowRules.SiteMinCell or > CowRules.SiteMaxCell ||
                !_traversalGrid!.Contains(target) || !_traversalGrid.Get(target).IsWalkable || target == here || TargetTaken(herd, cow, target)) continue;
            var path = DeterministicPathfinder.FindPath(_traversalGrid, here, target);
            if (!path.Found || path.Path.Count == 0 || !path.Path.All(CowOnSite)) continue;
            return cow with { Activity = CowActivity.Ambling, Route = path.Path.ToArray(), RouteIndex = 0 };
        }
        return cow with { UntilTick = CurrentTick + CowRules.GrazeMinTicks };
    }

    /// <summary>
    /// A grazing cow in a driven one's way ambles a few metres off, always away from the driven cow and as square to its
    /// line home as that allows (its own side of the line first); unchanged if there's nowhere open.
    /// </summary>
    private LooseCow MoveAside(LooseCow?[] herd, LooseCow grazer, LooseCow driven)
    {
        var next = TraversalGrid.CellCentre(driven.Route[Math.Min(driven.RouteIndex, driven.Route.Length - 1)]);
        long hx = next.XMillimetres - driven.XMillimetres, hz = next.ZMillimetres - driven.ZMillimetres;
        long ax = grazer.XMillimetres - driven.XMillimetres, az = grazer.ZMillimetres - driven.ZMillimetres;
        var side = Math.Sign(hx * az - hz * ax);
        var here = TraversalGrid.WorldToCell(grazer.XMillimetres, grazer.ZMillimetres);
        // The eight ways out, those leading away from the driven cow only, squarest to its heading first.
        var ways = new (int X, int Z)[] { (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1) }
            .Where(w => w.X * ax + w.Z * az > 0)
            .OrderBy(w => Math.Abs(w.X * hx + w.Z * hz) * 1_000 / (w.X != 0 && w.Z != 0 ? 1_414 : 1_000))
            .ThenBy(w => Math.Sign(hx * w.Z - hz * w.X) == side ? 0 : 1);
        foreach (var (dx, dz) in ways)
        {
            var target = new GridCell(here.X + dx * CowRules.AsideCells, here.Z + dz * CowRules.AsideCells);
            if (target.X is < CowRules.SiteMinCell or > CowRules.SiteMaxCell || target.Z is < CowRules.SiteMinCell or > CowRules.SiteMaxCell ||
                !_traversalGrid!.Contains(target) || !_traversalGrid.Get(target).IsWalkable || TargetTaken(herd, grazer, target)) continue;
            var path = DeterministicPathfinder.FindPath(_traversalGrid, here, target);
            // From where it stands, not back via the middle of its own cell.
            var route = path.Path.SkipWhile(cell => cell == here).ToArray();
            if (path.Found && route.Length > 0 && route.All(CowOnSite)) return grazer with { Activity = CowActivity.Ambling, Route = route, RouteIndex = 0 };
        }
        return grazer;
    }

    /// <summary>Moves a cow along its route at the given pace, a cell centre at a time.</summary>
    private static LooseCow StepCow(LooseCow cow, int pace)
    {
        int x = cow.XMillimetres, z = cow.ZMillimetres, index = cow.RouteIndex, budget = pace;
        while (budget > 0 && index < cow.Route.Length)
        {
            var target = TraversalGrid.CellCentre(cow.Route[index]);
            long dx = target.XMillimetres - x, dz = target.ZMillimetres - z;
            var distance = (int)Math.Sqrt(dx * dx + dz * dz);
            if (distance <= budget) { x = target.XMillimetres; z = target.ZMillimetres; budget -= distance; index++; continue; }
            x += (int)(dx * budget / distance); z += (int)(dz * budget / distance); budget = 0;
        }
        return cow with { XMillimetres = x, ZMillimetres = z, RouteIndex = index };
    }

    private void MaybeChewCable(LooseCow cow)
    {
        foreach (var (utility, spot) in CableSpots())
        {
            var at = TraversalGrid.CellCentre(spot);
            long dx = at.XMillimetres - cow.XMillimetres, dz = at.ZMillimetres - cow.ZMillimetres;
            if (dx * dx + dz * dz > (long)CowRules.CableReachMillimetres * CowRules.CableReachMillimetres) continue;
            if (OpenFaults.Any(f => f.FacilityId == "cable." + utility)) continue;
            if (CowRules.Draw(CampaignSeed, $"cable.{utility}:{cow.Id}", CurrentTick, 0, 10_000) >= CowRules.ChewChancePer10k) continue;
            AddFault($"cable:{utility}:{CurrentTick}", FacilityFaultKind.ChewedCable, "cable." + utility, 0);
            MedicalEvent("cows:cable", $"A loose cow chewed through the {(utility == "generator" ? utility : Stalls.Label(utility).ToLowerInvariant())} cable.");
            return;
        }
    }

    private LooseCow? AdvanceHerdedCow(LooseCow?[] herd, int index, ulong herder)
    {
        var cow = herd[index]!;
        if (IsPlayerSteward(herder)) return AdvancePlayerHerdedCow(herd, index, herder);
        var nav = _navigationAgents[new(herder)];
        // Anything else that takes the steward's route (a fight, a collapse) lets the cow go back to grazing; so does a cow
        // the steward can't reach, or a herd that's run far too long.
        var lost = nav.IntentId?.StartsWith("cow.", StringComparison.Ordinal) != true || PersonCollapsed(herder) ||
            PersonIn(PersonView.Roster, herder) is not { Admitted: true, Departed: false };
        if (lost || nav.Action == AgentNavigationAction.NoRoute || CurrentTick - cow.HerdStartedTick > CowRules.HerdDeadlineTicks)
        {
            if (!lost) ReturnToListening(herder);
            return cow with { HerderId = null, HerdStartedTick = -1, Driving = false, Activity = CowActivity.Grazing, Route = [], RouteIndex = 0, UntilTick = CurrentTick + 400 };
        }
        var cowCell = TraversalGrid.WorldToCell(cow.XMillimetres, cow.ZMillimetres);
        if (!cow.Driving)
        {
            var stewardCell = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
            if (Math.Max(Math.Abs(stewardCell.X - cowCell.X), Math.Abs(stewardCell.Z - cowCell.Z)) > CowRules.HerdReachCells) return cow;
            var home = DeterministicPathfinder.FindPath(_traversalGrid!, cowCell, CowRules.GateInside);
            ApplyAgentDestination(new(herder), new(WalkableNear(new(CowRules.GateInside.X - 2, CowRules.GateInside.Z)), "cow.drive"));
            return cow with { Driving = true, Route = home.Found && home.Path.All(CowOnSite) ? home.Path.ToArray() : [CowRules.GateInside], RouteIndex = 0 };
        }
        var (moved, blocker) = StepCowApart(herd, index, CowRules.DrivenMillimetresPerTick);
        // Two driven cows each in the other's way (driven from different sides to the one gate) would both wait until their
        // herds ran out: the one earlier in the herd goes on, and the other waits for it.
        if (blocker > index && herd[blocker] is { Driving: true } other && InTheWay(other, StepCow(other, CowRules.DrivenMillimetresPerTick), cow))
            (moved, blocker) = StepCowApart(herd, index, CowRules.DrivenMillimetresPerTick, ignore: blocker);
        if (blocker >= 0)
        {
            // Another cow in the way: one grazing there is moved aside by the steward behind, off to one side of the line
            // home (tried once a festival minute, so a cow with nowhere to go costs no search every tick); one walking or
            // being driven is waited for.
            if (CurrentTick % 80 == 0 && herd[blocker] is { HerderId: null, Activity: CowActivity.Grazing } grazer) herd[blocker] = MoveAside(herd, grazer, cow);
            return cow;
        }
        if (moved.RouteIndex < moved.Route.Length) return moved;
        MedicalEvent("cows:home", $"{cow.Id} was driven back into its field.");
        ReturnToListening(herder);
        return null;
    }

    public (ulong Steward, string? Reason) CowHerder(string cowId)
    {
        if (_preparation?.Status != PreparationStatus.Running || _cows is null) return (0, "Cows are herded during the festival.");
        if (_cows.Loose.FirstOrDefault(cow => cow.Id == cowId) is not { } cow) return (0, "That cow isn't loose.");
        if (cow.HerderId is not null) return (0, "A steward is already on it.");
        var at = new { X = cow.XMillimetres, Z = cow.ZMillimetres };
        foreach (var worker in GetStewardResponses().Select(s => s.WorkerId)
                     .OrderBy(id => { var n = _navigationAgents[new(id)]; long dx = n.XMillimetres - at.X, dz = n.ZMillimetres - at.Z; return dx * dx + dz * dz; })
                     .ThenBy(id => id))
            if (StaffUnavailableReason(worker) is null && !CleanupOwnsNavigation(worker) && !WasteOwnsNavigation(worker)) return (worker, null);
        return (0, "No steward is free to go.");
    }

    private CommandResult? ValidateHerdCow(EntityId? target, HerdCowCommand command) =>
        target is not null ? CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Herd a cow by its id.") :
        CowHerder(command.CowId).Reason is { } reason ? CommandResult.Rejected(CommandReasonCode.InvalidParameter, reason) : null;

    private void ApplyHerdCow(HerdCowCommand command)
    {
        var (steward, _) = CowHerder(command.CowId);
        var cow = _cows!.Loose.Single(c => c.Id == command.CowId);
        RecallWorker(steward, "Steward sent to herd a cow");
        _cows = _cows with { Loose = _cows.Loose.Select(c => c.Id == cow.Id ? c with { HerderId = steward, HerdStartedTick = CurrentTick, Activity = CowActivity.Herded, Route = [], RouteIndex = 0 } : c).ToArray() };
        ApplyAgentDestination(new(steward), new(WalkableNear(TraversalGrid.WorldToCell(cow.XMillimetres, cow.ZMillimetres)), "cow.fetch"));
    }

    private static string? ValidatePersistedCows(SessionPersistenceSnapshot s)
    {
        if (s.Cows is not { } c) return s.Preparation?.Plan is null ? null : "Current Build save requires cow state.";
        if (c.Version != 1 || !Enum.IsDefined(c.Gate) || c.Loose is null || c.Escaped < 0 || c.Escaped > CowRules.HerdSize * 4 ||
            c.Loose.Length > c.Escaped || c.Loose.Select(cow => cow.Id).Distinct().Count() != c.Loose.Length ||
            c.Gate == PastureGateState.Closed && c.Escaped != 0)
            return "Cow state shape invalid.";
        // The gate's state and its fault agree: broken while the fault's open, repaired once it's mended.
        var gateFaults = (s.Faults?.Faults ?? []).Where(f => f.Kind == FacilityFaultKind.BrokenGate).ToArray();
        if (c.Gate == PastureGateState.Closed ? gateFaults.Length != 0 :
            gateFaults.Length != 1 || (gateFaults[0].Stage == FacilityFaultStage.Active) != (c.Gate == PastureGateState.Broken))
            return "Pasture gate and its fault disagree.";
        var grid = new TraversalGrid();
        var stewards = (s.Disorder?.Stewards ?? []).Select(w => w.WorkerId).ToHashSet();
        static bool OnSite(GridCell cell) => CowOnSite(cell);
        foreach (var cow in c.Loose)
        {
            var cell = TraversalGrid.WorldToCell(cow.XMillimetres, cow.ZMillimetres);
            if (!Enum.IsDefined(cow.Activity) || !grid.Contains(cell) || !OnSite(cell) || cow.Route is null || cow.RouteIndex < 0 || cow.RouteIndex > cow.Route.Length ||
                cow.Route.Any(r => !grid.Contains(r) || !OnSite(r)) ||
                cow.Route.Zip(cow.Route.Skip(1)).Any(pair => Math.Max(Math.Abs(pair.First.X - pair.Second.X), Math.Abs(pair.First.Z - pair.Second.Z)) > 1) ||
                cow.Activity == CowActivity.Grazing && cow.Route.Length != 0 || cow.Activity == CowActivity.Ambling && cow.RouteIndex >= cow.Route.Length ||
                (cow.HerderId is null) == (cow.Activity == CowActivity.Herded) || cow.Driving && cow.HerderId is null ||
                (cow.HerderId is null) != (cow.HerdStartedTick < 0) || cow.HerdStartedTick > s.CurrentTick ||
                // The player's steward drives a cow on their own feet, not on a cow route.
                cow.HerderId is { } h && (!stewards.Contains(h) || (h == s.StewardShift?.PlayerId ? !cow.Driving :
                    s.NavigationAgents?.SingleOrDefault(n => n.Id == h)?.IntentId?.StartsWith("cow.", StringComparison.Ordinal) != true)))
                return "Loose cow invalid.";
        }
        if (c.Loose.Where(cow => cow.HerderId is not null).GroupBy(cow => cow.HerderId).Any(g => g.Count() > 1)) return "One steward herds one cow.";
        return null;
    }
}

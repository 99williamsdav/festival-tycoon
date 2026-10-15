using System.Text.Json;

namespace Festival.Simulation;

/// <summary>
/// The field's ground, cell by cell. Each layer lists only cells with any amount, in ascending flat-index order
/// (<c>Z * Width + X</c>), with the amount at each: dry wear from feet, standing water, and churned mud. Muddy
/// boots are the people still carrying mud from wet ground, with how many more steps they will leave it on.
/// </summary>
public sealed record GroundSnapshot(int Version, int[] Cells, int[] Wear, int[] WetCells, int[] Wet, int[] MudCells, int[] Mud,
    ulong[] MuddyBoots, int[] MuddySteps)
{
    /// <summary>Straw on the ground, laid in Build or spread by a steward, by flat index; null when there's none.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int[]? StrawCells { get; init; }
    /// <summary>How trampled each straw cell is, 0 (fresh) to <see cref="GroundCoverRules.TrampleMaximum"/> (flat).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int[]? Trample { get; init; }
}

/// <summary>What a cell of ground has become, from fresh grass to a near-swamp.</summary>
public enum GroundState { Grass, Puddle, Mud, Swamp }

public static class GroundRules
{
    /// <summary>Every this many ticks the ground takes a step: water flows and dries, and feet wear and churn it.</summary>
    public const int FootfallEveryTicks = 40;
    /// <summary>Wear at which a cell has gone to bare earth; the ground shows nothing more after this.</summary>
    public const int BareEarthWear = 160;
    public const int MaximumWear = 1_000;
    /// <summary>The field inside the hedges, in grid cells: world -32 m to +32 m on both axes.</summary>
    public const int FieldFirstCell = 64, FieldLastCell = 191;

    /// <summary>Water a broken tap pours onto its cell each step; a bodged one only drips.</summary>
    public const int BrokenTapFlow = 160, BodgedTapFlow = 4;
    /// <summary>Water a cell holds before it spills half the excess to its neighbours, so a puddle grows outwards.</summary>
    public const int WetSpill = 120;
    /// <summary>Each wet cell loses one unit of water every this many steps; every step in a heatwave.</summary>
    public const int EvaporatesEverySteps = 2;
    public const int MaximumWet = 2_000;
    /// <summary>Mud churned by a foot on wet ground, and left by muddy boots on each of their next steps.</summary>
    public const int MudChurn = 3, MudTrack = 4, MuddyBootSteps = 8;
    public const int MaximumMud = 1_000;
    /// <summary>Mud on dry ground dries out one unit every this many steps.</summary>
    public const int MudDriesEverySteps = 8;
    /// <summary>Waterlogged ground turns to sludge by itself, one unit every this many steps, so a tap left broken
    /// for long enough makes a swamp at the heart of its puddle while a quick repair leaves only wet grass.</summary>
    public const int SoaksEverySteps = 6, SoakingWet = 150;
    public const int PuddleWet = 30, MudLevel = 20, SwampWet = 90, SwampMud = 40;

    /// <summary>How much longer a step onto this ground takes, per mille.</summary>
    public static int SlowPermille(GroundState state) => state switch
    {
        GroundState.Swamp => 1_800, GroundState.Mud => 1_350, GroundState.Puddle => 1_100, _ => 1_000,
    };
    /// <summary>
    /// Extra route cost, per mille. People splash through wet grass, which is how it churns to mud, but go around
    /// mud if it is easy and a swamp if there is any reasonable way.
    /// </summary>
    public static int RouteCostPermille(GroundState state) => state switch
    {
        GroundState.Swamp => 3_000, GroundState.Mud => 300, _ => 0,
    };
    /// <summary>Satisfaction a guest loses each second on this ground, before their prissiness scales it.</summary>
    public static int Unpleasantness(GroundState state) => state switch
    {
        GroundState.Swamp => 12, GroundState.Mud => 6, GroundState.Puddle => 3, _ => 0,
    };

    public static GroundState StateOf(int wet, int mud) =>
        wet >= SwampWet && mud >= SwampMud ? GroundState.Swamp : mud >= MudLevel ? GroundState.Mud : wet >= PuddleWet ? GroundState.Puddle : GroundState.Grass;

    public static bool InField(GridCell cell) =>
        cell.X is >= FieldFirstCell and <= FieldLastCell && cell.Z is >= FieldFirstCell and <= FieldLastCell;
}

public sealed partial class GameSession
{
    private const int GroundCellCount = TraversalGrid.Width * TraversalGrid.Depth;
    // Dense by flat cell index while an edition exists; saved sparsely.
    private int[]? _groundWear, _groundWet, _groundMud;
    // Extra route cost per cell from wet ground, or null while it is all dry; derived from the layers, never saved.
    private int[]? _groundRouteCost;
    private int[]? _groundFlow;
    // Straw by flat cell index: 0 for none, else 1 plus how trampled it is. Saved sparsely.
    private int[]? _groundStraw;
    // Matted cells, from the layout's panels; derived, never saved.
    private MatPanel[]? _mattedSource;
    private bool[]? _matted;
    private readonly SortedDictionary<ulong, int> _muddyBoots = [];

    /// <summary>Rises whenever any of the ground changes, so a view can tell when to redraw.</summary>
    public long GroundVersion { get; private set; }

    // The snapshot is hashed with every state snapshot but changes only every 40 ticks, so it is kept until it does.
    private (long Version, GroundSnapshot Snapshot, string Json)? _groundCapture;

    public GroundSnapshot? CaptureGround() => CachedGround()?.Snapshot;

    internal string? GroundCanonicalJson => CachedGround()?.Json;

    private static (int[] Cells, int[] Amounts) Sparse(int[] dense)
    {
        var cells = new List<int>(); var amounts = new List<int>();
        for (var z = GroundRules.FieldFirstCell; z <= GroundRules.FieldLastCell; z++)
        for (var x = GroundRules.FieldFirstCell; x <= GroundRules.FieldLastCell; x++)
        {
            var index = z * TraversalGrid.Width + x;
            if (dense[index] > 0) { cells.Add(index); amounts.Add(dense[index]); }
        }
        return (cells.ToArray(), amounts.ToArray());
    }

    private (long Version, GroundSnapshot Snapshot, string Json)? CachedGround()
    {
        if (_groundWear is not { } wear) return null;
        if (_groundCapture is { } cached && cached.Version == GroundVersion) return cached;
        var (cells, amounts) = Sparse(wear); var (wetCells, wet) = Sparse(_groundWet!); var (mudCells, mud) = Sparse(_groundMud!);
        var (strawCells, straw) = Sparse(_groundStraw!);
        var snapshot = new GroundSnapshot(1, cells, amounts, wetCells, wet, mudCells, mud, _muddyBoots.Keys.ToArray(), _muddyBoots.Values.ToArray())
            { StrawCells = strawCells.Length == 0 ? null : strawCells, Trample = strawCells.Length == 0 ? null : straw.Select(value => value - 1).ToArray() };
        _groundCapture = (GroundVersion, snapshot, JsonSerializer.Serialize(snapshot));
        return _groundCapture;
    }

    private static int Index(GridCell cell) => cell.Z * TraversalGrid.Width + cell.X;

    /// <summary>How worn a cell is, 0 for fresh grass up to <see cref="GroundRules.MaximumWear"/>.</summary>
    public int GroundWearAt(GridCell cell) => _groundWear is { } wear && GroundRules.InField(cell) ? wear[Index(cell)] : 0;
    public int GroundWetAt(GridCell cell) => _groundWet is { } wet && GroundRules.InField(cell) ? wet[Index(cell)] : 0;
    public int GroundMudAt(GridCell cell) => _groundMud is { } mud && GroundRules.InField(cell) ? mud[Index(cell)] : 0;
    /// <summary>What the ground is like to walk on: straw firms it up until it's trampled flat, and matting is always firm.</summary>
    public GroundState GroundStateAt(GridCell cell) => GroundRules.InField(cell) && _groundWet is not null ? EffectiveGroundState(Index(cell)) : GroundState.Grass;
    /// <summary>Whether straw lies here, and how trampled it is (0 fresh to <see cref="GroundCoverRules.TrampleMaximum"/>); -1 for none.</summary>
    public int GroundTrampleAt(GridCell cell) => _groundStraw is { } straw && GroundRules.InField(cell) ? straw[Index(cell)] - 1 : -1;
    public bool MattedAt(GridCell cell) => GroundRules.InField(cell) && Matted() is { } matted && matted[Index(cell)];

    private GroundState EffectiveGroundState(int index)
    {
        var raw = GroundRules.StateOf(_groundWet![index], _groundMud![index]);
        if (raw == GroundState.Grass) return raw;
        if (Matted() is { } matted && matted[index]) return GroundState.Grass;
        if (_groundStraw is { } straw && straw[index] > 0 && straw[index] - 1 < GroundCoverRules.TrampleMaximum)
            return raw == GroundState.Swamp ? GroundState.Puddle : GroundState.Grass;
        return raw;
    }

    /// <summary>The matted cells, from the layout's panels; null when there are none.</summary>
    private bool[]? Matted()
    {
        var panels = _preparation?.MatPanels;
        if (ReferenceEquals(panels, _mattedSource)) return _matted;
        _mattedSource = panels;
        if (panels is null) return _matted = null;
        var matted = new bool[GroundCellCount];
        foreach (var panel in panels) foreach (var cell in panel.Cells()) if (GroundRules.InField(cell)) matted[Index(cell)] = true;
        return _matted = matted;
    }

    private void ResetGround()
    {
        _groundWear = new int[GroundCellCount]; _groundWet = new int[GroundCellCount]; _groundMud = new int[GroundCellCount];
        _groundStraw = new int[GroundCellCount];
        _groundRouteCost = null; _muddyBoots.Clear();
        GroundVersion++;
    }

    /// <summary>At opening, the straw laid in Build goes down fresh.</summary>
    private void LayPlannedStraw()
    {
        if (_groundStraw is not { } straw || _preparation?.StrawCells is not { } cells) return;
        foreach (var cell in cells) if (GroundRules.InField(CellAt(cell))) straw[cell] = 1;
        GroundVersion++;
        RefreshGroundRouteCost();
    }

    /// <summary>A steward spreads fresh straw on a cell: it firms up the mud under it.</summary>
    private void SpreadStrawOn(GridCell cell)
    {
        if (_groundStraw is not { } straw || !GroundRules.InField(cell)) return;
        straw[Index(cell)] = 1;
        GroundVersion++;
        RefreshGroundRouteCost();
    }

    private void RestoreGround(GroundSnapshot? snapshot)
    {
        _muddyBoots.Clear();
        if (snapshot is null) { _groundWear = _groundWet = _groundMud = _groundRouteCost = null; return; }
        ResetGround();
        for (var i = 0; i < snapshot.Cells.Length; i++) _groundWear![snapshot.Cells[i]] = snapshot.Wear[i];
        for (var i = 0; i < snapshot.WetCells.Length; i++) _groundWet![snapshot.WetCells[i]] = snapshot.Wet[i];
        for (var i = 0; i < snapshot.MudCells.Length; i++) _groundMud![snapshot.MudCells[i]] = snapshot.Mud[i];
        for (var i = 0; i < snapshot.MuddyBoots.Length; i++) _muddyBoots[snapshot.MuddyBoots[i]] = snapshot.MuddySteps[i];
        for (var i = 0; i < (snapshot.StrawCells?.Length ?? 0); i++) _groundStraw![snapshot.StrawCells![i]] = snapshot.Trample![i] + 1;
        RefreshGroundRouteCost();
    }

    /// <summary>How much longer a step onto this cell takes, per mille, for its water and mud.</summary>
    private int GroundSlowPermille(GridCell cell) => GroundRules.SlowPermille(GroundStateAt(cell));

    private void RefreshGroundRouteCost()
    {
        int[]? cost = null;
        for (var z = GroundRules.FieldFirstCell; z <= GroundRules.FieldLastCell; z++)
        for (var x = GroundRules.FieldFirstCell; x <= GroundRules.FieldLastCell; x++)
        {
            var index = z * TraversalGrid.Width + x;
            var extra = GroundRules.RouteCostPermille(EffectiveGroundState(index));
            if (extra == 0) continue;
            cost ??= new int[GroundCellCount];
            cost[index] = extra;
        }
        // Dry ground plans and walks exactly as before.
        _groundRouteCost = cost;
    }

    /// <summary>
    /// The ground takes a step while the festival is on. A broken tap pours water that spills outwards cell by
    /// cell, so its puddle starts small and grows; every wet cell dries a little. Then everyone on the field wears
    /// the grass under them, churns wet ground into mud, and carries mud on their boots for a few steps after.
    /// </summary>
    private void AdvanceGround()
    {
        if (_groundWear is not { } wear || _preparation?.Status is not (PreparationStatus.Running or PreparationStatus.Departing) ||
            CurrentTick % GroundRules.FootfallEveryTicks != 0) return;
        var changed = AdvanceGroundWater();
        var wet = _groundWet!; var mud = _groundMud!; var straw = _groundStraw!;
        var matted = Matted();
        // Rain softens trampled ground: it churns under feet even before it puddles.
        var raining = RainNow != RainIntensity.Dry;
        foreach (var agent in _navigationAgents.Values)
        {
            var id = agent.Id.Value;
            // Only feet actually on the field: not those still waiting outside to be let in, nor anyone gone home.
            if (PersonIn(PersonView.Roster, id) is { } person && (person.Departed || !person.Admitted && agent.Destination is null))
            { changed |= _muddyBoots.Remove(id); continue; }
            var cell = TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres);
            if (!GroundRules.InField(cell)) continue;
            var index = Index(cell);
            // Matting takes the feet: nothing under it wears, churns or gets tracked onto.
            if (matted is not null && matted[index]) continue;
            if (wear[index] < GroundRules.MaximumWear) { wear[index]++; changed = true; }
            // Fresh straw takes the churning until it's trampled flat, and tramples quicker wet.
            if (straw[index] > 0 && straw[index] - 1 < GroundCoverRules.TrampleMaximum)
            {
                straw[index] = Math.Min(GroundCoverRules.TrampleMaximum + 1, straw[index] + (wet[index] >= GroundCoverRules.TrampleWetFrom ? 2 : 1));
                changed = true;
            }
            else if (raining && wet[index] < GroundRules.PuddleWet && wear[index] >= WeatherRules.TrampledWear)
            {
                if (agent.Action == AgentNavigationAction.Travelling || CurrentTick / GroundRules.FootfallEveryTicks % WeatherRules.StandingChurnEverySteps == 0)
                { mud[index] = Math.Min(GroundRules.MaximumMud, mud[index] + 1); changed = true; }
            }
            else if (wet[index] >= GroundRules.PuddleWet)
            {
                mud[index] = Math.Min(GroundRules.MaximumMud, mud[index] + GroundRules.MudChurn);
                _muddyBoots[id] = GroundRules.MuddyBootSteps; changed = true;
            }
            else if (_muddyBoots.TryGetValue(id, out var steps))
            {
                mud[index] = Math.Min(GroundRules.MaximumMud, mud[index] + GroundRules.MudTrack);
                if (steps <= 1) _muddyBoots.Remove(id); else _muddyBoots[id] = steps - 1;
                changed = true;
            }
            // Felt once a second, every other step, like the other nuisances.
            var loss = GroundRules.Unpleasantness(EffectiveGroundState(index));
            if (loss > 0 && CurrentTick / GroundRules.FootfallEveryTicks % 2 == 0 && IsGuest(id))
            {
                var felt = UnpleasantFor(id, loss);
                ChangeSatisfaction(id, -felt, MoodCause.MudAndPuddles);
            }
        }
        if (changed) { GroundVersion++; RefreshGroundRouteCost(); }
    }

    /// <summary>Taps pour, water spills to the four neighbours of any brimming cell, and wet ground dries.</summary>
    private bool AdvanceGroundWater()
    {
        var wet = _groundWet!; var mud = _groundMud!;
        var changed = false;
        foreach (var tap in Taps)
        {
            var flow = OpenFaults.FirstOrDefault(f => f.Kind == FacilityFaultKind.BrokenTap && f.FacilityId == tap.Id)?.Stage switch
            {
                FacilityFaultStage.Active => GroundRules.BrokenTapFlow,
                FacilityFaultStage.Bodged => GroundRules.BodgedTapFlow,
                _ => 0,
            };
            if (flow == 0 || !GroundRules.InField(tap.Cell)) continue;
            var index = Index(tap.Cell);
            wet[index] = Math.Min(GroundRules.MaximumWet, wet[index] + flow);
            changed = true;
        }
        var rain = RainNow;
        if (IsRainyFestival) changed |= RainOnTheField(rain);
        var spill = _groundFlow ??= new int[GroundCellCount];
        var any = false;
        for (var z = GroundRules.FieldFirstCell; z <= GroundRules.FieldLastCell; z++)
        for (var x = GroundRules.FieldFirstCell; x <= GroundRules.FieldLastCell; x++)
        {
            var index = z * TraversalGrid.Width + x;
            if (wet[index] <= GroundRules.WetSpill) continue;
            // Half the excess spills, an eighth to each neighbour, so water keeps flowing to the puddle's edge.
            var share = (wet[index] - GroundRules.WetSpill) / 8;
            if (share == 0) continue;
            foreach (var (dx, dz) in Spills)
            {
                if (!GroundRules.InField(new GridCell(x + dx, z + dz))) continue;
                spill[index] -= share; spill[index + dz * TraversalGrid.Width + dx] += share;
            }
            any = true;
        }
        var step = CurrentTick / GroundRules.FootfallEveryTicks;
        // Nothing dries while it's raining.
        var evaporating = rain == RainIntensity.Dry && (_medical?.IsHot == true || step % GroundRules.EvaporatesEverySteps == 0);
        var drying = step % GroundRules.MudDriesEverySteps == 0;
        var soaking = step % GroundRules.SoaksEverySteps == 0;
        for (var z = GroundRules.FieldFirstCell; z <= GroundRules.FieldLastCell; z++)
        for (var x = GroundRules.FieldFirstCell; x <= GroundRules.FieldLastCell; x++)
        {
            var index = z * TraversalGrid.Width + x;
            if (any && spill[index] != 0) { wet[index] = Math.Min(GroundRules.MaximumWet, wet[index] + spill[index]); spill[index] = 0; changed = true; }
            if (wet[index] > 0)
            {
                if (soaking && wet[index] >= GroundRules.SoakingWet && mud[index] < GroundRules.MaximumMud) { mud[index]++; changed = true; }
                if (evaporating) { wet[index]--; changed = true; }
            }
            else if (drying && mud[index] > 0) { mud[index]--; changed = true; }
        }
        return changed;
    }

    private static readonly (int X, int Z)[] Spills = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    // How much rain each cell drinks a step, before its wear: most grass drinks a heavy shower, but the field has low
    // hollows a few metres across, where it can't, and puddles gather. Fixed for the field, so the same hollows fill.
    private static readonly int[] FieldDrain = BuildFieldDrain();

    private static uint Mix(uint value)
    {
        value = unchecked(value * 2654435761u); value ^= value >> 15; value = unchecked(value * 2246822519u); value ^= value >> 13;
        return value;
    }

    /// <summary>
    /// The lie of the land: a smooth height from hashed corners every 8 cells (4 m), blended across each square in whole
    /// numbers. The lowest 5% of the field are deep hollows (drinking a quarter of a heavy shower's rain), the next 8%
    /// shallow ones (half).
    /// </summary>
    private static int[] BuildFieldDrain()
    {
        const int spacing = 8;
        int Corner(int cx, int cz) => (int)(Mix(unchecked((uint)(cx * 7919 + cz * 104_729 + 17))) % 1_000);
        var height = new int[GroundCellCount];
        for (var z = 0; z < TraversalGrid.Depth; z++)
        for (var x = 0; x < TraversalGrid.Width; x++)
        {
            int cx = x / spacing, cz = z / spacing, fx = x % spacing, fz = z % spacing;
            var top = Corner(cx, cz) * (spacing - fx) + Corner(cx + 1, cz) * fx;
            var bottom = Corner(cx, cz + 1) * (spacing - fx) + Corner(cx + 1, cz + 1) * fx;
            height[z * TraversalGrid.Width + x] = top * (spacing - fz) + bottom * fz;
        }
        var field = new List<int>();
        for (var z = GroundRules.FieldFirstCell; z <= GroundRules.FieldLastCell; z++)
        for (var x = GroundRules.FieldFirstCell; x <= GroundRules.FieldLastCell; x++) field.Add(height[z * TraversalGrid.Width + x]);
        field.Sort();
        int deep = field[field.Count * 5 / 100], shallow = field[field.Count * 13 / 100];
        var drain = new int[GroundCellCount];
        for (var index = 0; index < drain.Length; index++)
            drain[index] = height[index] < deep ? WeatherRules.GrassDrain - 3 : height[index] < shallow ? WeatherRules.GrassDrain - 2 : WeatherRules.GrassDrain;
        return drain;
    }

    /// <summary>
    /// On a rainy day, every open cell of the field gets rain while it falls and soaks some away, rain or dry: grass drinks
    /// most of a heavy shower, but trampled ground and hollows can't, so puddles gather where people walk. Matting sheds it.
    /// </summary>
    private bool RainOnTheField(RainIntensity rain)
    {
        var wet = _groundWet!; var wear = _groundWear!;
        var matted = Matted();
        var falling = WeatherRules.RainOnGround(rain);
        var changed = false;
        for (var z = GroundRules.FieldFirstCell; z <= GroundRules.FieldLastCell; z++)
        for (var x = GroundRules.FieldFirstCell; x <= GroundRules.FieldLastCell; x++)
        {
            var index = z * TraversalGrid.Width + x;
            if (matted is not null && matted[index]) { if (wet[index] > 0) { wet[index] = 0; changed = true; } continue; }
            if (falling == 0 && wet[index] == 0) continue;
            var drain = Math.Max(0, FieldDrain[index] - (wear[index] >= GroundRules.BareEarthWear ? 1 : 0));
            // Under the marquee, a canopy or a porch, the ground stays dry.
            var next = Math.Clamp(wet[index] + (falling > 0 && CoveredCell(new(x, z)) ? 0 : falling) - drain, 0, GroundRules.MaximumWet);
            if (next != wet[index]) { wet[index] = next; changed = true; }
        }
        return changed;
    }

    private static string? ValidateGroundLayer(int[]? cells, int[]? amounts, int maximum, string name)
    {
        if (cells is null || amounts is null || cells.Length != amounts.Length) return "Ground state shape invalid.";
        for (var i = 0; i < cells.Length; i++)
        {
            var index = cells[i];
            if (i > 0 && index <= cells[i - 1]) return $"Ground {name} cells must be in ascending order without repeats.";
            if (index < 0 || index >= GroundCellCount ||
                !GroundRules.InField(new GridCell(index % TraversalGrid.Width, index / TraversalGrid.Width)))
                return $"Ground {name} lies outside the field.";
            if (amounts[i] < 1 || amounts[i] > maximum) return $"Ground {name} out of range.";
        }
        return null;
    }

    private static string? ValidatePersistedGround(SessionPersistenceSnapshot s)
    {
        if (s.Ground is null) return s.Preparation?.Plan is null ? null : "Current Build save requires ground state.";
        var g = s.Ground;
        if (g.Version != 1 || s.Preparation is not { } prep || g.MuddyBoots is null || g.MuddySteps is null ||
            g.MuddyBoots.Length != g.MuddySteps.Length)
            return "Ground state shape invalid.";
        if (ValidateGroundLayer(g.Cells, g.Wear, GroundRules.MaximumWear, "wear") is { } wearError) return wearError;
        if (ValidateGroundLayer(g.WetCells, g.Wet, GroundRules.MaximumWet, "water") is { } wetError) return wetError;
        if (ValidateGroundLayer(g.MudCells, g.Mud, GroundRules.MaximumMud, "mud") is { } mudError) return mudError;
        if (prep.Status == PreparationStatus.Preparing && g.Cells.Length + g.WetCells.Length + g.MudCells.Length + g.MuddyBoots.Length + (g.StrawCells?.Length ?? 0) != 0)
            return "Draft/retry must clear the ground.";
        if ((g.StrawCells is null) != (g.Trample is null) || g.StrawCells is { Length: 0 }) return "Ground state shape invalid.";
        if (g.StrawCells is { } strawCells)
        {
            // Trample is saved as is (0 for fresh), so check it on its own scale.
            if (ValidateGroundLayer(strawCells, g.Trample!.Select(value => value + 1).ToArray(), GroundCoverRules.TrampleMaximum + 1, "straw") is { } strawError) return strawError;
            if (prep.Tier < GroundCoverRules.FromTier && !(SavedClassic(s)&&SavedWeatherKind(s)==WeatherKind.Rain)) return "Straw lies on a festival without it.";
        }
        // Everything laid in Build is down from opening; stewards may have spread more.
        if (prep.Status != PreparationStatus.Preparing && prep.StrawCells is { } planned && !planned.ToHashSet().IsSubsetOf(g.StrawCells ?? []))
            return "Straw laid in Build is missing from the ground.";
        var people = (s.NavigationAgents ?? []).Select(a => a.Id).ToHashSet();
        for (var i = 0; i < g.MuddyBoots.Length; i++)
        {
            if (i > 0 && g.MuddyBoots[i] <= g.MuddyBoots[i - 1]) return "Muddy boots must be in ascending order without repeats.";
            if (!people.Contains(g.MuddyBoots[i])) return "Muddy boots belong to someone not on the farm.";
            if (g.MuddySteps[i] is < 1 or > GroundRules.MuddyBootSteps) return "Muddy boot steps out of range.";
        }
        return null;
    }
}

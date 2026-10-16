namespace Festival.Simulation;

/// <summary>
/// The food vans and bars by id: the first of each keeps the old "food" and "drinks", so a one-van, one-bar festival
/// saves and plays exactly as before; the second is "food.2" or "drinks.2", and so on. Every id question goes through here.
/// </summary>
public static class Stalls
{
    public const string FirstVan = "food", FirstBar = "drinks";

    private static string Prefix(BuildServiceKind kind) => kind switch
    {
        BuildServiceKind.FoodVan => FirstVan,
        BuildServiceKind.Bar => FirstBar,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>The id of a kind's <paramref name="number"/>th stall, counting from 1.</summary>
    public static string Id(BuildServiceKind kind, int number) => number == 1 ? Prefix(kind) : $"{Prefix(kind)}.{number}";

    /// <summary>Which stall of its kind this is, from 1; 0 for anything that isn't a stall id.</summary>
    public static int Number(string? id)
    {
        foreach (var prefix in new[] { FirstVan, FirstBar })
        {
            if (id == prefix) return 1;
            if (id is not null && id.StartsWith(prefix + ".", StringComparison.Ordinal) && int.TryParse(id[(prefix.Length + 1)..], out var number) &&
                number >= 2 && id == $"{prefix}.{number}") return number;
        }
        return 0;
    }

    public static bool IsVan(string? id) => Number(id) > 0 && (id == FirstVan || id!.StartsWith(FirstVan + ".", StringComparison.Ordinal));
    public static bool IsBar(string? id) => Number(id) > 0 && (id == FirstBar || id!.StartsWith(FirstBar + ".", StringComparison.Ordinal));
    public static bool IsStall(BuildServiceKind kind) => kind is BuildServiceKind.FoodVan or BuildServiceKind.Bar;
    public static BuildServiceKind KindOf(string id) => IsVan(id) ? BuildServiceKind.FoodVan : IsBar(id) ? BuildServiceKind.Bar :
        throw new ArgumentOutOfRangeException(nameof(id));

    /// <summary>The first free id of a kind among what's placed.</summary>
    public static string Next(BuildServiceKind kind, IEnumerable<string> placed)
    {
        var taken = placed.ToHashSet(StringComparer.Ordinal);
        for (var number = 1; ; number++)
            if (!taken.Contains(Id(kind, number))) return Id(kind, number);
    }

    /// <summary>A sale names its stall only when it isn't the first of its kind, so one-stall saves keep their shape.</summary>
    public static string? Recorded(string vendorId) => Number(vendorId) == 1 ? null : vendorId;

    /// <summary>The stall a sale was made at.</summary>
    public static string Of(ImmersionPurchase purchase) => purchase.VendorId ?? (purchase.Product.IsFood() ? FirstVan : FirstBar);

    /// <summary>"Bar", "Bar 2", "Food van 2": a stall's fallback label, for one that isn't on the field (see <see cref="StallNames"/>).</summary>
    public static string Label(string id) => (IsVan(id) ? "Food van" : "Bar") + (Number(id) > 1 ? $" {Number(id)}" : "");
}

/// <summary>
/// The names the player sees for bars and vans, worked out from the layout alone, so nothing about them is saved.
/// Bars are named in relation to each other (the user: "they should be named in relation to each other"):
/// <list type="bullet">
/// <item>A van goes by its trader's name ("Korma Chameleon"). A van with no trader is named as a bar is, among the
/// other trader-less vans ("Van", "East Van").</item>
/// <item>A bar within <see cref="NearStageCells"/> of a stage's audience ground or its deck and steps is that stage's
/// bar (the nearer stage if two are close, the first stage on a tie). The only one by its stage is "Pond Stage Bar";
/// several by one stage each take a bearing from the middle of those bars: "West Pond Stage Bar", "East Pond Stage Bar".</item>
/// <item>Any other bar, if it's the only bar on the field, is just "Bar". Otherwise it takes its bearing from the middle
/// of all the bars on the field, stage bars included: with a Pond Stage Bar and one other bar to its west, "West Bar".</item>
/// </list>
/// Within each group (one stage's bars, or the bars by no stage) bearings are on four points; any that two bars share
/// go to eight points for just those bars; any still shared are numbered west to east, then north to south ("North
/// Bar 1", "North Bar 2"). A bar exactly at the middle is "Central Bar". North is up the site map (-Z), east is +X.
/// Middles are integer means compared without rounding (count × cell against the sum of cells).
/// The names come from the set of stalls alone, whatever order they're listed or numbered in, and no two stalls ever
/// share one. Because they're relative, placing, moving or removing a bar can rename the others: a lone "Bar" becomes
/// "East Bar" once a second bar goes in to its west.
/// </summary>
public static class StallNames
{
    /// <summary>How close, in cells (half a metre each), a stall must stand to a stage's crowd or stage to be named after it.</summary>
    public const int NearStageCells = 10;

    /// <summary>Every stall's name by id. <paramref name="traderName"/> names a van; null leaves vans to the bar rule.</summary>
    public static SortedDictionary<string, string> For(IEnumerable<(string Id, GridCell Cell)> stalls, IReadOnlyList<FestivalStage> stages,
        Func<string, string?>? traderName = null)
    {
        // An id listed twice counts once, at the first of its cells (west, then north, first).
        var all = stalls.Where(stall => Stalls.Number(stall.Id) > 0)
            .OrderBy(stall => stall.Id, StringComparer.Ordinal).ThenBy(stall => stall.Cell.X).ThenBy(stall => stall.Cell.Z)
            .DistinctBy(stall => stall.Id).ToArray();
        var names = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var placeNamed = new List<(string Id, GridCell Cell)>();
        foreach (var stall in all)
        {
            var trader = Stalls.IsVan(stall.Id) ? traderName?.Invoke(stall.Id) : null;
            if (string.IsNullOrWhiteSpace(trader)) placeNamed.Add(stall);
            else names[stall.Id] = trader;
        }
        NameByPlace(placeNamed.Where(stall => Stalls.IsBar(stall.Id)).ToArray(), "Bar", stages, names);
        NameByPlace(placeNamed.Where(stall => Stalls.IsVan(stall.Id)).ToArray(), "Van", stages, names);

        // Never two the same, whatever the layout (two vans with one trader, say): west to east, then north to south,
        // the first keeps the name and the rest take the lowest free number.
        var cells = all.ToDictionary(stall => stall.Id, stall => stall.Cell, StringComparer.Ordinal);
        var taken = names.Values.ToHashSet(StringComparer.Ordinal);
        foreach (var clash in names.GroupBy(pair => pair.Value, StringComparer.Ordinal).Where(group => group.Count() > 1)
                     .OrderBy(group => group.Key, StringComparer.Ordinal).ToArray())
        {
            foreach (var pair in clash.OrderBy(pair => cells[pair.Key].X).ThenBy(pair => cells[pair.Key].Z).ThenBy(pair => pair.Key, StringComparer.Ordinal).Skip(1))
            {
                var number = 2;
                while (taken.Contains($"{clash.Key} {number}")) number++;
                names[pair.Key] = $"{clash.Key} {number}";
                taken.Add(names[pair.Key]);
            }
        }
        return names;
    }

    /// <summary>One kind's stalls named by where they stand: by a stage, or by bearing from the middle of them all.</summary>
    private static void NameByPlace((string Id, GridCell Cell)[] stalls, string noun, IReadOnlyList<FestivalStage> stages, IDictionary<string, string> names)
    {
        if (stalls.Length == 0) return;
        var byStage = stalls.Select(stall => (Stall: stall, Stage: StageNear(stall.Cell, stages))).ToArray();
        foreach (var stage in stages)
        {
            var near = byStage.Where(item => ReferenceEquals(item.Stage, stage)).Select(item => item.Stall).ToArray();
            if (near.Length == 1) names[near[0].Id] = $"{stage.ShortName} {noun}";
            else if (near.Length > 1) NameGroup(near, near, $"{stage.ShortName} {noun}", names);
        }
        var elsewhere = byStage.Where(item => item.Stage is null).Select(item => item.Stall).ToArray();
        if (stalls.Length == 1 && elsewhere.Length == 1) names[elsewhere[0].Id] = noun;
        else if (elsewhere.Length > 0) NameGroup(elsewhere, stalls, noun, names);
    }

    /// <summary>
    /// A group named by bearing from the middle of <paramref name="around"/>: four points, eight for any that share,
    /// then a number, west to east and north to south, for any that still do.
    /// </summary>
    private static void NameGroup((string Id, GridCell Cell)[] group, (string Id, GridCell Cell)[] around, string rest, IDictionary<string, string> names)
    {
        long count = around.Length, sumX = around.Sum(stall => (long)stall.Cell.X), sumZ = around.Sum(stall => (long)stall.Cell.Z);
        string BearingOf(GridCell cell, bool eight) => Bearing(count * cell.X - sumX, count * cell.Z - sumZ, eight);
        var four = group.ToDictionary(stall => stall.Id, stall => BearingOf(stall.Cell, eight: false), StringComparer.Ordinal);
        var bearings = group.ToDictionary(stall => stall.Id,
            stall => four.Values.Count(value => value == four[stall.Id]) > 1 ? BearingOf(stall.Cell, eight: true) : four[stall.Id], StringComparer.Ordinal);
        foreach (var same in group.GroupBy(stall => bearings[stall.Id], StringComparer.Ordinal))
        {
            var ordered = same.OrderBy(stall => stall.Cell.X).ThenBy(stall => stall.Cell.Z).ThenBy(stall => stall.Id, StringComparer.Ordinal).ToArray();
            for (var index = 0; index < ordered.Length; index++)
                names[ordered[index].Id] = ordered.Length == 1 ? $"{same.Key} {rest}" : $"{same.Key} {rest} {index + 1}";
        }
    }

    /// <summary>The stage a stall stands by, if any: the nearest within range of its crowd or the stage itself.</summary>
    public static FestivalStage? StageNear(GridCell cell, IReadOnlyList<FestivalStage> stages)
    {
        FestivalStage? best = null;
        var bestDistance = int.MaxValue;
        foreach (var stage in stages)
        {
            var distance = Math.Min(Distance(cell, stage.AudienceBounds), Distance(cell, StageBounds(stage)));
            if (distance <= NearStageCells && distance < bestDistance) (best, bestDistance) = (stage, distance);
        }
        return best;
    }

    /// <summary>The stage itself, deck and steps, as one rectangle (not its band's ground or generator).</summary>
    private static (int MinX, int MaxX, int MinZ, int MaxZ) StageBounds(FestivalStage stage)
    {
        lock (Stages)
        {
            if (!Stages.TryGetValue(stage.Id, out var bounds))
                Stages[stage.Id] = bounds = FestivalStage.BoundsOf(stage.Cells().Select(item => item.Cell).Where(cell => stage.Deck(cell) || stage.Stairs(cell)).ToArray());
            return bounds;
        }
    }
    private static readonly Dictionary<string, (int MinX, int MaxX, int MinZ, int MaxZ)> Stages = new(StringComparer.Ordinal);

    /// <summary>Cells from a cell to a rectangle, the larger of the two axis gaps; 0 inside it.</summary>
    private static int Distance(GridCell cell, (int MinX, int MaxX, int MinZ, int MaxZ) box) =>
        Math.Max(Math.Max(box.MinX - cell.X, cell.X - box.MaxX), Math.Max(Math.Max(box.MinZ - cell.Z, cell.Z - box.MaxZ), 0));

    /// <summary>
    /// "North", "East", ..., from one cell to another, north being -Z. On four points the larger axis wins and an exact
    /// diagonal is named as one; on eight, a diagonal is anything within about 27° of one (neither axis twice the other).
    /// </summary>
    public static string Bearing(GridCell cell, GridCell from, bool eight) => Bearing(cell.X - from.X, cell.Z - from.Z, eight);

    /// <summary>The same from an offset, which may be scaled (count × cell − sum of cells) to keep a middle exact.</summary>
    public static string Bearing(long dx, long dz, bool eight)
    {
        var (ax, az) = (Math.Abs(dx), Math.Abs(dz));
        if (ax == 0 && az == 0) return "Central";
        var ns = dz < 0 ? "North" : "South";
        var ew = dx > 0 ? "East" : "West";
        if (eight ? ax > 2 * az : ax > az) return ew;
        if (eight ? az > 2 * ax : az > ax) return ns;
        return $"{ns}-{ew}";
    }
}

public sealed partial class GameSession
{
    private (ImmersionVendor[]? Vendors, PreparationSnapshot? Prep, IReadOnlyList<FestivalStage>? Stages, SortedDictionary<string, string> Names) _stallNames;

    /// <summary>Every bar's and van's name as the player sees it, from where they stand now (see <see cref="StallNames"/>).</summary>
    public IReadOnlyDictionary<string, string> StallNamesNow()
    {
        // Names hang on the stalls, the plan (placements and van traders) and the stages they're named after.
        var stages = Stages;
        if (_stallNames.Names is null || !ReferenceEquals(_stallNames.Vendors, Vendors) || !ReferenceEquals(_stallNames.Prep, _preparation) ||
            !ReferenceEquals(_stallNames.Stages, stages))
        {
            // What's standing, and anything placed that isn't yet.
            var standing = Vendors.Select(vendor => (vendor.Id, vendor.Cell)).ToArray();
            var stalls = standing.Concat((_preparation?.BuildPlacements ?? [])
                .Where(item => Stalls.IsStall(item.Kind) && !standing.Any(vendor => vendor.Id == item.Id)).Select(item => (item.Id, item.Cell)));
            _stallNames = (Vendors, _preparation, stages, StallNames.For(stalls, stages, id => TraderAt(id).Name));
        }
        return _stallNames.Names;
    }

    /// <summary>A bar's or van's name, "East Bar", "Bar" or "Korma Chameleon"; the fallback label for one not on the field.</summary>
    public string StallName(string id) => StallNamesNow().TryGetValue(id, out var name) ? name : Stalls.Label(id);

    /// <summary>The name the UI shows: now just <see cref="StallName"/>, as a lone bar by no stage is already "Bar".</summary>
    public string StallDisplayName(string id) => StallName(id);

    /// <summary>A recorded landmark or target label, shown as recorded (saved narrative text is never rewritten).</summary>
    public string StallDisplayLabel(string label) => label;
}

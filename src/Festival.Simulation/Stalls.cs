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
/// <list type="bullet">
/// <item>A bar within <see cref="NearStageCells"/> of a stage's audience ground or its deck and steps is named after it:
/// "Trailer Stage Bar", "Pond Stage Bar" (the nearer stage if two are close, the first stage on a tie).</item>
/// <item>Any other bar is named by its compass bearing from the middle of the festival ground: "East Bar". North is
/// up the site map (-Z), east is +X.</item>
/// <item>Several bars by one stage each take their bearing from the middle of that stage's crowd: "North Pond Stage
/// Bar", "West Pond Stage Bar".</item>
/// <item>Two names that would still be the same go to eight points ("North-East Bar"), and only then is a number
/// added, in stall order ("East Bar", "East Bar 2").</item>
/// <item>A van goes by its trader's name ("Korma Chameleon"), which no two vans share; one that somehow did would be
/// named as a bar is, as "East Van".</item>
/// </list>
/// The bearing is from the field's middle rather than from the other bars, so placing or moving one bar never renames another.
/// </summary>
public static class StallNames
{
    /// <summary>How close, in cells (half a metre each), a stall must stand to a stage's crowd or stage to be named after it.</summary>
    public const int NearStageCells = 10;
    /// <summary>The middle of the festival ground Build places on (x 68–190, z 108–190), on the farm track.</summary>
    public static readonly GridCell FieldCentre = new(129, 149);

    /// <summary>Every stall's name by id. <paramref name="traderName"/> names a van; null leaves vans to the bar rule.</summary>
    public static SortedDictionary<string, string> For(IEnumerable<(string Id, GridCell Cell)> stalls, IReadOnlyList<FestivalStage> stages,
        Func<string, string?>? traderName = null)
    {
        var all = stalls.Where(stall => Stalls.Number(stall.Id) > 0).DistinctBy(stall => stall.Id)
            .OrderBy(stall => Stalls.IsVan(stall.Id)).ThenBy(stall => Stalls.Number(stall.Id)).ToArray();
        var names = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, stall) in ByPlace(all.Where(stall => Stalls.IsBar(stall.Id)).ToArray(), "Bar", stages)) names[stall.Id] = name;
        var vans = all.Where(stall => Stalls.IsVan(stall.Id)).Select(stall => (Stall: stall, Trader: traderName?.Invoke(stall.Id))).ToArray();
        var traded = vans.Where(van => !string.IsNullOrWhiteSpace(van.Trader) && vans.Count(other => other.Trader == van.Trader) == 1).ToArray();
        foreach (var van in traded) names[van.Stall.Id] = van.Trader!;
        foreach (var (name, stall) in ByPlace(vans.Except(traded).Select(van => van.Stall).ToArray(), "Van", stages)) names[stall.Id] = name;
        // Never two the same, whatever the layout: a number after the first, in stall order.
        foreach (var same in all.GroupBy(stall => names[stall.Id], StringComparer.Ordinal).Where(group => group.Count() > 1))
            foreach (var (stall, index) in same.Select((stall, index) => (stall, index)).Skip(1))
                names[stall.Id] = Unused($"{same.Key} {index + 1}", names);
        return names;
    }

    private static string Unused(string name, SortedDictionary<string, string> names)
    {
        var candidate = name;
        for (var extra = 2; names.ContainsValue(candidate); extra++) candidate = $"{name} ({extra})";
        return candidate;
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

    private static IEnumerable<(string Name, (string Id, GridCell Cell) Stall)> ByPlace((string Id, GridCell Cell)[] stalls, string noun,
        IReadOnlyList<FestivalStage> stages)
    {
        var anchored = stalls.Select(stall => (Stall: stall, Stage: StageNear(stall.Cell, stages))).ToArray();
        // By a stage: its name, and a bearing from the middle of its crowd when it has more than one.
        foreach (var stage in stages)
        {
            var near = anchored.Where(item => item.Stage == stage).Select(item => item.Stall).ToArray();
            if (near.Length == 1) yield return ($"{stage.ShortName} {noun}", near[0]);
            else foreach (var named in Bearings(near, stage.AudienceCentre, $"{stage.ShortName} {noun}")) yield return named;
        }
        foreach (var named in Bearings(anchored.Where(item => item.Stage is null).Select(item => item.Stall).ToArray(), FieldCentre, noun))
            yield return named;
    }

    /// <summary>Each stall's bearing from a point before the rest of its name: four points, eight where four would repeat.</summary>
    private static IEnumerable<(string Name, (string Id, GridCell Cell) Stall)> Bearings((string Id, GridCell Cell)[] stalls, GridCell from, string rest)
    {
        var four = stalls.Select(stall => $"{Bearing(stall.Cell, from, eight: false)} {rest}").ToArray();
        for (var index = 0; index < stalls.Length; index++)
            yield return (four.Count(name => name == four[index]) > 1 ? $"{Bearing(stalls[index].Cell, from, eight: true)} {rest}" : four[index], stalls[index]);
    }

    /// <summary>
    /// "North", "East", ..., from one cell to another, north being -Z. On four points the larger axis wins and an exact
    /// diagonal is named as one; on eight, a diagonal is anything within about 27° of one (neither axis twice the other).
    /// </summary>
    public static string Bearing(GridCell cell, GridCell from, bool eight)
    {
        var (dx, dz) = (cell.X - from.X, cell.Z - from.Z);
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
    private (ImmersionVendor[]? Vendors, PreparationSnapshot? Prep, bool Pond, SortedDictionary<string, string> Names) _stallNames;

    /// <summary>Every bar's and van's name as the player sees it, from where they stand now (see <see cref="StallNames"/>).</summary>
    public IReadOnlyDictionary<string, string> StallNamesNow()
    {
        if (_stallNames.Names is null || !ReferenceEquals(_stallNames.Vendors, Vendors) || !ReferenceEquals(_stallNames.Prep, _preparation) ||
            _stallNames.Pond != PondStageOpen)
        {
            // What's standing, and anything placed that isn't yet.
            var stalls = Vendors.Select(vendor => (vendor.Id, vendor.Cell))
                .Concat((_preparation?.BuildPlacements ?? []).Where(item => Stalls.IsStall(item.Kind)).Select(item => (item.Id, item.Cell)));
            _stallNames = (Vendors, _preparation, PondStageOpen, StallNames.For(stalls, Stages, id => TraderAt(id).Name));
        }
        return _stallNames.Names;
    }

    /// <summary>A bar's or van's name, "East Bar" or "Korma Chameleon"; the fallback label for one not on the field.</summary>
    public string StallName(string id) => StallNamesNow().TryGetValue(id, out var name) ? name : Stalls.Label(id);
}

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
/// Each stall has a list of names it would like, best first:
/// <list type="bullet">
/// <item>A van, its trader's name ("Korma Chameleon").</item>
/// <item>A stall within <see cref="NearStageCells"/> of a stage's audience ground or its deck and steps, that stage's
/// name: "Trailer Stage Bar", "Pond Stage Bar" (the nearer stage if two are close, the first stage on a tie); then the
/// same with its bearing from the middle of that stage's crowd, on four points and then eight: "North Pond Stage Bar",
/// "North-West Pond Stage Bar".</item>
/// <item>Any other stall, its bearing from the middle of the festival ground on four points and then eight: "East Bar",
/// "South-East Bar". North is up the site map (-Z), east is +X.</item>
/// <item>Last, the last of those with a number: "South-East Bar 2".</item>
/// </list>
/// Names go out in stall order (bars, then vans, each by number), every stall taking the first name on its list that no
/// stall before it has. So a stall's name depends only on where it and the stalls numbered before it stand, whatever
/// order they're listed in: a newly placed bar, which takes the next number, never renames one already standing, and
/// it's the newcomer that gets the finer name ("East Bar" stays; the new one is "South-East Bar", and a second bar by
/// the Pond Stage is "North Pond Stage Bar" beside the plain "Pond Stage Bar"). Moving or removing a stall can rename
/// only stalls numbered after it, when it takes or frees a name they wanted; and as a new stall takes the lowest free
/// number, one placed after a removal can slot in before others and do the same.
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
        // In stall order; an id listed twice counts once, at the first of its cells (west, then north, first).
        var all = stalls.Where(stall => Stalls.Number(stall.Id) > 0)
            .OrderBy(stall => Stalls.IsVan(stall.Id)).ThenBy(stall => Stalls.Number(stall.Id)).ThenBy(stall => stall.Cell.X).ThenBy(stall => stall.Cell.Z)
            .DistinctBy(stall => stall.Id).ToArray();
        var names = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stall in all)
        {
            var trader = Stalls.IsVan(stall.Id) ? traderName?.Invoke(stall.Id) : null;
            var wanted = (string.IsNullOrWhiteSpace(trader) ? [] : new[] { trader })
                .Concat(ByPlace(stall.Cell, Stalls.IsVan(stall.Id) ? "Van" : "Bar", stages)).ToArray();
            var name = wanted.FirstOrDefault(candidate => !taken.Contains(candidate));
            // Never two the same, whatever the layout: a number on the last name it wanted.
            for (var number = 2; name is null; number++)
                if (!taken.Contains($"{wanted[^1]} {number}")) name = $"{wanted[^1]} {number}";
            taken.Add(name);
            names[stall.Id] = name;
        }
        return names;
    }

    /// <summary>The names a stall's place gives it, best first: by a stage, or by its bearing from the field's middle.</summary>
    private static IEnumerable<string> ByPlace(GridCell cell, string noun, IReadOnlyList<FestivalStage> stages)
    {
        var stage = StageNear(cell, stages);
        var rest = stage is null ? noun : $"{stage.ShortName} {noun}";
        var from = stage?.AudienceCentre ?? FieldCentre;
        if (stage is not null) yield return rest;
        yield return $"{Bearing(cell, from, eight: false)} {rest}";
        yield return $"{Bearing(cell, from, eight: true)} {rest}";
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

    /// <summary>A bar's or van's name, "East Bar" or "Korma Chameleon"; the fallback label for one not on the field.</summary>
    public string StallName(string id) => StallNamesNow().TryGetValue(id, out var name) ? name : Stalls.Label(id);
}

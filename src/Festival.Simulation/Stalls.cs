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

    /// <summary>"Bar", "Bar 2", "Food van 2": a stall as the player sees it.</summary>
    public static string Label(string id) => (IsVan(id) ? "Food van" : "Bar") + (Number(id) > 1 ? $" {Number(id)}" : "");
}

namespace Festival.Simulation;

/// <summary>A flyer's four meters, 0 to 5 pips each (see <see cref="TraderFlyers.Pips"/>).</summary>
public sealed record TraderPips(int Speed, int Filling, int Treat, int Pull);

/// <summary>A "vs current" chip on a flyer: what booking this trader gains (good) or loses against the van's trader.</summary>
public sealed record TraderChip(string Text, bool Good);

/// <summary>A trader shown on the board before they can be booked: a name, a strapline and art, but no stats yet.</summary>
public sealed record ComingSoonTrader(string Name, string Menu, string Strapline, string CardColour, string Art, string Tape);

/// <summary>
/// What the trader flyer board shows, worked out from the traders' own numbers so the flyers can't drift from the game:
/// the pip meters, the comparison chips and the second-van note. Presentation only; nothing here is saved.
/// </summary>
public static class TraderFlyers
{
    /// <summary>
    /// The coffee van: a parked concept with no trader or stats yet, shown as coming soon (Classic, later). Never bookable.
    /// "The Best of Brew": Foo Fighters' "Best of You".
    /// </summary>
    public static readonly ComingSoonTrader BrewFighters = new("Brew Fighters", "Coffee", "The Best of Brew", "1f5f5a", "coffee_concept",
        "Classic · coming soon");

    /// <summary>A meter reading as pips: rounded (halves up), then held to 0–5.</summary>
    public static int Pip(double value) => Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 5);

    /// <summary>
    /// The meters from the trader's numbers. Speed: the standard 4 s portion is 5 pips, twice as long 2.5. Filling: chips
    /// (100%) are 2, every 25% more another pip. Treat: chips (100%) are 2, every 14% more another. Pull: plain chips are 1,
    /// every £4 of appeal another.
    /// </summary>
    public static TraderPips Pips(FoodTrader trader) => new(
        Pip(5000.0 / trader.ServicePermille),
        Pip((trader.FillingPercent - 50) / 25.0),
        Pip((trader.EnjoymentPercent - 70) / 14.0),
        Pip(1 + trader.AppealPennies / 400.0));

    /// <summary>
    /// The chips on a flyer against the trader booked at this van, at most <paramref name="most"/>: the pitch fee first if
    /// it differs, then each meter that differs, in the order speed, filling, treat, pull. None on the booked trader's own
    /// flyer, or with no trader booked.
    /// </summary>
    public static IReadOnlyList<TraderChip> Compare(FoodTrader trader, FoodTrader? current, int most = 3)
    {
        if (current is null || current.Id == trader.Id || most <= 0) return [];
        var chips = new List<TraderChip>();
        var fee = trader.PitchFeePennies - current.PitchFeePennies;
        if (fee != 0) chips.Add(new($"{FestivalCurrency.Format(Math.Abs(fee))} {(fee > 0 ? "more" : "less")} pitch", fee > 0));
        var (mine, theirs) = (Pips(trader), Pips(current));
        foreach (var (difference, better, worse) in new[]
        {
            (mine.Speed - theirs.Speed, "quicker", "slower"),
            (mine.Filling - theirs.Filling, "more filling", "less filling"),
            (mine.Treat - theirs.Treat, "bigger treat", "smaller treat"),
            (mine.Pull - theirs.Pull, "pulls more", "pulls fewer"),
        })
            if (difference != 0) chips.Add(new(difference > 0 ? better : worse, difference > 0));
        return chips.Take(most).ToArray();
    }

    /// <summary>
    /// The note on a flyer when other placed vans already sell this food (Classic allows it): "2nd chips van: shares the
    /// chips crowd". Null when no other van does.
    /// </summary>
    public static string? SameCuisineNote(FoodTrader trader, int otherVansSellingIt)
    {
        if (otherVansSellingIt <= 0) return null;
        var food = trader.Menu.ToLowerInvariant();
        return $"{Ordinal(otherVansSellingIt + 1)} {food} van: shares the {food} crowd";
    }

    private static string Ordinal(int number) => number + ((number % 100) is 11 or 12 or 13 ? "th" : (number % 10) switch
    {
        1 => "st", 2 => "nd", 3 => "rd", _ => "th",
    });

    /// <summary>The flyer art for a trader's <see cref="FoodTrader.Art"/> key, as a Godot resource path.</summary>
    public static string ArtPath(string art) => $"res://assets/ui/traders/trader_art_{art}_v1.png";
}

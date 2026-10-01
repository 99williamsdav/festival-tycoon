namespace Festival.Simulation;

// Cosmetic projection only. Neither order nor filter enters GameSession state.
public enum BookingSortField { Band, Genre, Price, Popularity, Ego, Professionalism }

public static class BookingTableView
{
    public static int HalfStarUnits(int score) => (Math.Clamp(score, 0, 100) + 5) / 10;

    public static FestivalAct[] Project(IEnumerable<FestivalAct> acts, int? genre, BookingSortField field, bool descending)
    {
        if (genre is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(genre));
        var selected = genre is { } value ? acts.Where(act => act.Genre == value) : acts;
        IOrderedEnumerable<FestivalAct> sorted = field switch
        {
            BookingSortField.Band => descending ? selected.OrderByDescending(act => act.Name, StringComparer.Ordinal) : selected.OrderBy(act => act.Name, StringComparer.Ordinal),
            BookingSortField.Genre => descending ? selected.OrderByDescending(act => GenreLabel(act.Genre), StringComparer.Ordinal) : selected.OrderBy(act => GenreLabel(act.Genre), StringComparer.Ordinal),
            BookingSortField.Price => Sort(selected, act => act.PricePennies, descending),
            BookingSortField.Popularity => Sort(selected, act => act.Popularity, descending),
            BookingSortField.Ego => Sort(selected, act => act.Ego, descending),
            BookingSortField.Professionalism => Sort(selected, act => act.Professionalism, descending),
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        return sorted.ThenBy(act => act.Name, StringComparer.Ordinal).ThenBy(act => act.Id, StringComparer.Ordinal).ToArray();
    }

    private static IOrderedEnumerable<FestivalAct> Sort<TKey>(IEnumerable<FestivalAct> acts, Func<FestivalAct, TKey> key, bool descending)
        where TKey : IComparable<TKey> => descending ? acts.OrderByDescending(key) : acts.OrderBy(key);

    private static string GenreLabel(int genre) => FestivalGenre.Name(genre);
}

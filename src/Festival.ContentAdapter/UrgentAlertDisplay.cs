namespace Festival.ContentAdapter;

public sealed record UrgentAlert(string Id, string Text, int Priority);
public sealed record VisibleUrgentAlert(UrgentAlert Alert, float Opacity);

/// <summary>Presentation-only, wall-clock lifetimes. Persistent state remains in inspectors.</summary>
public sealed class UrgentAlertDisplay
{
    public const int MaximumVisible = 4;
    public const double DisplaySeconds = 12;
    public const double FadeSeconds = 2;
    public const double ResolutionFadeSeconds = 1;
    private sealed record Entry(UrgentAlert Alert, double Born, double? Resolved = null);
    private readonly Dictionary<string, Entry> _entries = [];
    private double _now;
    private int _page;
    public void Reset() { _entries.Clear(); _now = 0; _page = 0; }
    public void Observe(IEnumerable<UrgentAlert> alerts)
    {
        var current = alerts.GroupBy(alert => alert.Id).Select(group => group.OrderByDescending(a => a.Priority).First()).ToDictionary(a => a.Id);
        foreach (var (id, entry) in _entries.ToArray())
            if (!current.ContainsKey(id) && entry.Resolved is null) _entries[id] = entry with { Resolved = _now };
        foreach (var (id, alert) in current)
            if (!_entries.TryGetValue(id, out var entry) || entry.Resolved is not null || entry.Alert != alert)
            {
                _entries[id] = new(alert, _now);
                _page = 0; // A new/escalated emergency preempts a manually paged group.
            }
    }
    public void Advance(double seconds) => _now += Math.Max(0, seconds);
    public void ShowNextPage()
    {
        var active = _entries.Values.Where(e => e.Resolved is null).OrderByDescending(e => e.Alert.Priority).ThenBy(e => e.Alert.Id).ToArray();
        var pages = Math.Max(1, (active.Length + MaximumVisible - 1) / MaximumVisible);
        _page = (_page + 1) % pages;
        foreach (var entry in active) _entries[entry.Alert.Id] = entry with { Born = _now };
    }
    public VisibleUrgentAlert[] Visible()
    {
        float Alpha(Entry e) => (float)Math.Clamp(e.Resolved is { } ended
            ? 1 - (_now - ended) / ResolutionFadeSeconds
            : (DisplaySeconds + FadeSeconds - (_now - e.Born)) / FadeSeconds, 0, 1);
        var active = _entries.Values.Where(e => Alpha(e) > 0).OrderByDescending(e => e.Alert.Priority).ThenBy(e => e.Alert.Id).ToArray();
        var page = _page * MaximumVisible < active.Length ? _page : 0;
        return active.Skip(page * MaximumVisible).Take(MaximumVisible).Select(e => new VisibleUrgentAlert(e.Alert, Alpha(e))).ToArray();
    }
}

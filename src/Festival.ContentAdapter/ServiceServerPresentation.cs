using Festival.Simulation;

namespace Festival.ContentAdapter;

public enum ServerPhase { Idle, Turn, Pickup, Offer, Retract, Reset }

/// <summary>Vendor-owned, unsaved cosmetic state. All durations come from imported clips.</summary>
public sealed class ServiceServerPresentation(string? baselineTransaction = null)
{
    private string? _transaction = baselineTransaction;
    private ulong? _owner;
    private int _sales;
    public ServerPhase Phase { get; private set; }
    public double Seconds { get; private set; }
    public ImmersionProduct? Prop { get; private set; }
    public string? Transaction => _transaction;

    public void Advance(ulong? owner, ImmersionProduct? order, double remainingTicks, int durationTicks,
        bool eligible, string? transaction, long saleTick, long tick, double delta, bool frozen,
        double turn, double pickup, double offer, double retract, double reset, double idle = 4.04166650772095)
    {
        // Drain hidden/paused events too; resuming must never build an animation backlog.
        var sold = transaction is not null && transaction != _transaction;
        _transaction = transaction;
        if (frozen) return;
        Prop = null;
        if (sold && tick - saleTick <= 80 && tick >= saleTick)
        {
            _sales++; Phase = ServerPhase.Retract; Seconds = 0; _owner = null;
        }
        if (owner is not null && order is not null && durationTicks > 0 && eligible)
        {
            _owner = owner;
            // Sample the authoritative service progress; skipped ticks shorten the preamble.
            var time = Math.Clamp(1 - remainingTicks / durationTicks, 0, 1) * (turn + pickup + offer);
            if (time < turn) { Phase = ServerPhase.Turn; Seconds = time; }
            else if (time < turn + pickup) { Phase = ServerPhase.Pickup; Seconds = time - turn; }
            else { Phase = ServerPhase.Offer; Seconds = time - turn - pickup; Prop = order == ImmersionProduct.Poncho ? null : order; }
            return;
        }
        if (_owner is not null)
        {
            // Cancellation or failed eligibility withdraws an empty hand immediately.
            _owner = null; Phase = ServerPhase.Retract; Seconds = 0;
        }
        Seconds += Math.Max(0, delta);
        if (Phase == ServerPhase.Retract && Seconds >= retract)
        {
            Seconds -= retract;
            Phase = _sales > 0 && _sales % 4 == 0 ? ServerPhase.Reset : ServerPhase.Idle;
        }
        if (Phase == ServerPhase.Reset && Seconds >= reset) { Seconds -= reset; Phase = ServerPhase.Idle; }
        if (Phase == ServerPhase.Idle) Seconds %= idle;
    }
}

/// <summary>Only the newest new transaction per stall. Binding baselines existing history.</summary>
public sealed class ServerPurchaseCursor
{
    private ImmersionPurchase[]? _source;
    private string? _boundary;
    private readonly Dictionary<string, ImmersionPurchase> _latest = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    public ImmersionPurchase? Latest(string vendor) => _latest.GetValueOrDefault(vendor);
    public void Bind(ImmersionPurchase[] purchases)
    {
        _latest.Clear(); _source = purchases; _boundary = purchases.Length == 0 ? null : purchases[^1].Id;
    }
    public void Observe(ImmersionPurchase[] purchases)
    {
        if (ReferenceEquals(_source, purchases)) return;
        _source = purchases;
        // Iterate backwards, stop at the stable prior ID, retain a bounded one-per-vendor view.
        _seen.Clear();
        for (var i = purchases.Length - 1; i >= 0 && purchases[i].Id != _boundary; i--)
        {
            var purchase = purchases[i]; var vendor = Stalls.Of(purchase);
            if (_seen.Add(vendor)) _latest[vendor] = purchase;
        }
        _boundary = purchases.Length == 0 ? null : purchases[^1].Id;
    }
    public void Forget(string vendor) => _latest.Remove(vendor);
}

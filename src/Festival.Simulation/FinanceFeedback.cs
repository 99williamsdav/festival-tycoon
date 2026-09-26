namespace Festival.Simulation;

/// <summary>Player-facing currency text only; authoritative amounts remain integer pennies.</summary>
public static class FestivalCurrency
{
    public static string Format(long pennies, bool signed = false)
    {
        var prefix = pennies < 0 ? "−£" : signed && pennies > 0 ? "+£" : "£";
        var pounds = Math.Abs((decimal)pennies) / 100m;
        return prefix + pounds.ToString(pennies % 100 == 0 ? "0" : "0.00", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>Cosmetic projection of a successful festival cash ledger movement, never saved or hashed.</summary>
public sealed record FestivalCashFeedbackEvent(string TransactionId, long FestivalCashPennies, string AnchorKey, long Tick);

public static class FestivalCashFeedbackProjection
{
    public static IReadOnlyList<FestivalCashFeedbackEvent> Capture(ulong campaignId, PreparationSnapshot? preparation, ImmersionSnapshot? immersion)
    {
        if (preparation is null) return Array.Empty<FestivalCashFeedbackEvent>();
        var events = new List<FestivalCashFeedbackEvent>();
        var owner = new EntityId(preparation.FinanceOwnerId);
        void Add(string id, int attempt, long tick, string anchor, IEnumerable<LedgerEntry> entries)
        {
            var cash = entries.Where(entry => entry.OwnerId == owner && entry.Account == LedgerAccountType.CashAsset).Sum(entry => entry.AmountPennies);
            if (cash != 0) events.Add(new($"cash:{campaignId}:{attempt}:{id}", cash, anchor, tick));
        }
        foreach (var payment in preparation.Payments)
            Add($"preparation:{payment.Id}", payment.Attempt, payment.Tick, $"offer:{payment.OfferId}",
                [new(owner, payment.DebitAccount, payment.AmountPennies), new(owner, LedgerAccountType.CashAsset, -payment.AmountPennies)]);
        if (immersion?.StockPurchase is { } stock)
            Add($"stock:{stock.Id}", stock.Attempt, stock.Tick, "stock", stock.Entries);
        foreach (var sale in immersion?.Purchases ?? [])
            Add($"sale:{sale.Id}", preparation.Attempt, sale.Tick, sale.Product == ImmersionProduct.Chips ? "vendor.food" : "vendor.drinks", sale.Entries);
        return events.OrderBy(item => item.Tick).ThenBy(item => item.TransactionId, StringComparer.Ordinal).ToList().AsReadOnly();
    }
}

/// <summary>UI-local cursor. Reset on startup/load/retry/scene rebuild, not ordinary successful clone commits.</summary>
public sealed class FestivalCashFeedbackCursor
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    public void Reset(GameSession session)
    {
        Reset(session.CaptureFestivalCashFeedbackEvents());
    }
    public IReadOnlyList<FestivalCashFeedbackEvent> Observe(GameSession session)
    {
        return Observe(session.CaptureFestivalCashFeedbackEvents());
    }
    public void Reset(IEnumerable<FestivalCashFeedbackEvent> events)
    {
        _seen.Clear();
        foreach (var item in events) _seen.Add(item.TransactionId);
    }
    public IReadOnlyList<FestivalCashFeedbackEvent> Observe(IEnumerable<FestivalCashFeedbackEvent> events)
    {
        var fresh = new List<FestivalCashFeedbackEvent>();
        foreach (var item in events) if (_seen.Add(item.TransactionId)) fresh.Add(item);
        return fresh.AsReadOnly();
    }
}

public sealed partial class GameSession
{
    public IReadOnlyList<FestivalCashFeedbackEvent> CaptureFestivalCashFeedbackEvents() =>
        FestivalCashFeedbackProjection.Capture(CampaignId.Value, _preparation, _immersion);
}

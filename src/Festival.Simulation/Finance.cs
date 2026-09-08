using System.Collections.ObjectModel;

namespace Festival.Simulation;

public enum LedgerAccountType
{
    CashAsset = 1,
    InventoryAsset = 2,
    SalesRevenue = 3,
    CostOfGoodsSold = 4,
    GuestSpending = 5,
}

/// <summary>
/// Debit-positive, credit-negative entry. Assets and expenses increase with positive amounts;
/// revenue increases and asset reductions use negative amounts.
/// </summary>
public sealed record LedgerEntry(EntityId OwnerId, LedgerAccountType Account, long AmountPennies);

public sealed class TransactionRecord
{
    internal TransactionRecord(
        TransactionId id,
        CommandId commandId,
        long tick,
        EntityId buyerId,
        EntityId festivalId,
        EntityId serviceId,
        int quantity,
        long unitPricePennies,
        IEnumerable<LedgerEntry> entries)
    {
        Id = id;
        CommandId = commandId;
        Tick = tick;
        BuyerId = buyerId;
        FestivalId = festivalId;
        ServiceId = serviceId;
        Quantity = quantity;
        UnitPricePennies = unitPricePennies;
        Entries = Array.AsReadOnly(entries.ToArray());
    }

    public TransactionId Id { get; }
    public CommandId CommandId { get; }
    public long Tick { get; }
    public EntityId BuyerId { get; }
    public EntityId FestivalId { get; }
    public EntityId ServiceId { get; }
    public int Quantity { get; }
    public long UnitPricePennies { get; }
    public ReadOnlyCollection<LedgerEntry> Entries { get; }
    public bool IsBalanced => Entries.Sum(entry => (decimal)entry.AmountPennies) == 0m;
}

public sealed record WalletSnapshot(EntityId OwnerId, long CashPennies);
public sealed record FestivalFinanceSnapshot(EntityId OwnerId, long CashPennies);
public sealed record OwnedStockSnapshot(
    EntityId ServiceId,
    EntityId OwnerId,
    int Quantity,
    int UnitCostBasisPennies)
{
    public long InventoryValuePennies => checked((long)Quantity * UnitCostBasisPennies);
}

internal sealed class WalletState
{
    public required EntityId OwnerId { get; init; }
    public long CashPennies { get; set; }
}

internal sealed class FestivalFinanceState
{
    public required EntityId OwnerId { get; init; }
    public long CashPennies { get; set; }
}

internal sealed class OwnedStockState
{
    public required EntityId ServiceId { get; init; }
    public required EntityId OwnerId { get; init; }
    public int Quantity { get; set; }
    public int UnitCostBasisPennies { get; init; }
}

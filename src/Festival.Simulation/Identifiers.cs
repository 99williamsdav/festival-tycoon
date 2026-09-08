using System.Globalization;

namespace Festival.Simulation;

public readonly record struct CampaignId(ulong Value)
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct EntityId(ulong Value) : IComparable<EntityId>
{
    public int CompareTo(EntityId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct CommandId(ulong Value) : IComparable<CommandId>
{
    public int CompareTo(CommandId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct TransactionId(ulong Value) : IComparable<TransactionId>
{
    public int CompareTo(TransactionId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

namespace Festival.Simulation;

/// <summary>Demand inputs deliberately exclude talent, facilities and genre-variety bonuses.</summary>
public sealed record ClassicTicketDemandInput(int Capacity, int Reputation, int TicketPricePennies, int[] Popularities);
public sealed record ClassicTicketEstimate(int MinimumSales, int MaximumSales, int OverlappingPopularity)
{
    public long ConservativeSetupCash(long existingCashPennies, int ticketPricePennies, long bandFeesPennies)
    {
        if (ticketPricePennies < 0 || bandFeesPennies < 0) throw new ArgumentOutOfRangeException(nameof(ticketPricePennies));
        return checked(existingCashPennies + (long)MinimumSales * ticketPricePennies - bandFeesPennies);
    }
}

/// <summary>
/// Provisional small-site market. Public guidance uses a bounded range; settlement uses a stable local factor,
/// keyed only by campaign and festival, never by the current dropdown selection or a live RNG stream.
/// This pure model does not book bands, take money or change a session.
/// </summary>
public static class ClassicTicketDemand
{
    public const int InheritedCapacity = 25;
    public const int DefaultTicketPricePennies = 1_000;
    public const int MaximumTicketPricePennies = 10_000;
    private const int EstimateSwing = 1_000, MarketSwing = 800;

    public static ClassicTicketEstimate Estimate(ClassicTicketDemandInput input)
    {
        var (reach, willingness, popularity) = Parameters(input);
        return new(Quantity(input, reach - EstimateSwing, willingness),
            Quantity(input, reach + EstimateSwing, willingness), popularity);
    }

    public static int Resolve(ulong campaignSeed, int festivalNumber, ClassicTicketDemandInput input)
    {
        var (reach, willingness, _) = Parameters(input);
        if (festivalNumber < 1) throw new ArgumentOutOfRangeException(nameof(festivalNumber));
        var key = campaignSeed ^ 0x71C4E7D320261015UL ^ unchecked((ulong)festivalNumber * 0xD1B54A32D192ED03UL);
        var market = RandomStreamFactory.Create(key, RandomStreamId.Demand);
        var swing = (int)(market.NextUInt32() % (MarketSwing * 2 + 1)) - MarketSwing;
        return Quantity(input, reach + swing, willingness);
    }

    private static (int Reach, int Willingness, int Popularity) Parameters(ClassicTicketDemandInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Capacity is < 1 or > GameSession.MaxActivePeople || input.Reputation is < 0 or > 100 ||
            input.TicketPricePennies is < 0 or > MaximumTicketPricePennies || input.Popularities is null ||
            input.Popularities.Length > 3 || input.Popularities.Any(p => p is < 0 or > 100))
            throw new ArgumentException("Demand needs known capacity, reputation, a valid price and up to three popularity values.", nameof(input));
        // The same people can like several bands: a weighted draw, never three added audiences.
        var p = input.Popularities.OrderDescending().Concat(new[] { 0, 0, 0 }).Take(3).ToArray();
        var popularity = (p[0] * 60 + p[1] * 25 + p[2] * 15) / 100;
        var reach = 6_500 + popularity * 60 + input.Reputation * 50;
        var willingness = 1_000 + popularity * 8 + input.Reputation * 6;
        return (reach, willingness, popularity);
    }

    private static int Quantity(ClassicTicketDemandInput input, int reach, int willingness)
    {
        var priceFactor = Math.Max(0, 20_000 - (long)input.TicketPricePennies * 10_000 / willingness);
        return (int)Math.Clamp((long)input.Capacity * Math.Max(0, reach) * priceFactor / 100_000_000, 0, input.Capacity);
    }
}

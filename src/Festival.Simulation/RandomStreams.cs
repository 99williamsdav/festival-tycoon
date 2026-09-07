namespace Festival.Simulation;

public enum RandomStreamId
{
    Demand = 1,
    Weather = 2,
    ForecastError = 3,
    ArtistDecisions = 4,
    IndividualBehaviour = 5,
    Incidents = 6,
    Cosmetic = 100,
}

public sealed class Pcg32Random
{
    public const string AlgorithmVersion = "pcg32-xsh-rr-v1";

    private ulong _state;

    public Pcg32Random(ulong initialState, ulong increment)
    {
        _state = initialState;
        Increment = increment | 1UL;
    }

    public ulong State => _state;

    public ulong Increment { get; }

    public uint NextUInt32()
    {
        var oldState = _state;
        _state = unchecked((oldState * 6364136223846793005UL) + Increment);
        var xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
        var rotation = (int)(oldState >> 59);
        return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
    }
}

internal static class RandomStreamFactory
{
    public static Pcg32Random Create(ulong campaignSeed, RandomStreamId streamId)
    {
        var streamKey = Mix(campaignSeed ^ ((ulong)streamId * 0x9E3779B97F4A7C15UL));
        var increment = (Mix(streamKey ^ 0xD1B54A32D192ED03UL) << 1) | 1UL;
        var random = new Pcg32Random(0, increment);
        random.NextUInt32();

        var seededState = unchecked(random.State + Mix(campaignSeed ^ streamKey));
        random = new Pcg32Random(seededState, increment);
        random.NextUInt32();
        return random;
    }

    private static ulong Mix(ulong value)
    {
        value = unchecked(value + 0x9E3779B97F4A7C15UL);
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}

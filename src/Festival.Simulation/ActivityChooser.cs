namespace Festival.Simulation;

/// <summary>Watch is the default: the music for guests and performers, the post for staff. Respond is a staff job.</summary>
public enum ActivityKind { Watch, Water, Rest, Food, SoftDrink, Beer, Toilet, Respond, BarWater }

/// <summary>Need levels on the shared 0–10,000 scale.</summary>
public readonly record struct NeedLevels(int Thirst, int Heat, int Hunger, int Toilet);

/// <summary>Need growth per second (80 ticks).</summary>
public readonly record struct NeedGrowth(int Thirst, int Heat, int Hunger, int Toilet);

/// <summary>A later stop in a plan; <see cref="CompleteTicks"/> counts from now.</summary>
public sealed record ActivityStop(ActivityKind Kind, string? FacilityId, int CompleteTicks);

/// <summary>
/// One candidate plan. <see cref="CompleteTicks"/> is walk + wait + service from now, when the first
/// stop's relief lands; <see cref="Then"/> is an optional second stop straight after it.
/// <see cref="AwayTicks"/> runs to the walk back from the last stop, the time missing the music.
/// Enjoyment and cost cover every stop. Only the first stop is ever acted on: the plan is re-made
/// once it completes, so the second stop shapes which first stop, and where, is chosen.
/// </summary>
public sealed record ActivityOption(ActivityKind Kind, string? FacilityId, int CompleteTicks, int AwayTicks, long Enjoyment = 0, long Cost = 0)
{
    public ActivityStop? Then { get; init; }
}

public sealed record ActivityScore(ActivityOption Option, long Score);

/// <summary>
/// Scores single-stop activity plans over a fixed look-ahead. Each second of the horizon earns the
/// music on offer (when present) and pays each need's discomfort at its projected level; relief lands
/// when the activity completes. A plan already in progress is scored from its remaining time, so a
/// held queue place counts in its favour without any extra lock-in.
/// </summary>
public static class ActivityChooser
{
    // Two minutes: long enough that relief behind a real queue still lands inside the plan.
    public const int HorizonTicks = 9_600;
    // Needs left at the end of the horizon keep hurting; count them for this many more seconds.
    public const int TailSeconds = 10;
    public const int SampleTicks = 80;
    public const int Samples = HorizonTicks / SampleTicks;
    public const int RestHeatTarget = 6_000;
    public const int ToiletAfterVisit = 1_000;
    public const int FoodHungerRelief = 5_500;
    public const int SoftDrinkThirstRelief = 6_000;
    public const int BeerThirstRelief = 1_500;
    public const int BeerToiletGain = 600;

    /// <summary>Discomfort per second at a need level. Steep near the top so urgent needs dominate.</summary>
    public static long ThirstDiscomfort(int level) => Square(level - 3_000) / 20_000;
    public static long HeatDiscomfort(int level) => Square(level - 5_000) / 10_000;
    public static long HungerDiscomfort(int level) => Square(level - 3_000) / 20_000;
    public static long ToiletDiscomfort(int level) => Square(level - 4_000) / 12_000;
    private static long Square(int excess) => excess <= 0 ? 0 : (long)excess * excess;

    /// <summary>Heat distress leads to collapse and death; it outweighs any music.</summary>
    public const long DistressPerSecond = 20_000;

    public static long Discomfort(NeedLevels levels) => ThirstDiscomfort(levels.Thirst) + HeatDiscomfort(levels.Heat) +
        HungerDiscomfort(levels.Hunger) + ToiletDiscomfort(levels.Toilet) +
        (levels.Thirst >= GameSession.MedicalDistressThirst && levels.Heat >= GameSession.MedicalDistressHeat ? DistressPerSecond : 0);

    /// <summary>The needs just after an activity's relief lands.</summary>
    public static NeedLevels Relieve(ActivityKind kind, NeedLevels at) => kind switch
    {
        ActivityKind.Water => at with { Thirst = 0, Heat = Math.Max(0, at.Heat - at.Thirst / 4) },
        ActivityKind.Rest => at with { Heat = Math.Min(at.Heat, RestHeatTarget) },
        ActivityKind.Food => at with { Hunger = Math.Max(0, at.Hunger - FoodHungerRelief) },
        ActivityKind.SoftDrink or ActivityKind.BarWater => at with { Thirst = Math.Max(0, at.Thirst - SoftDrinkThirstRelief) },
        ActivityKind.Beer => at with { Thirst = Math.Max(0, at.Thirst - BeerThirstRelief), Toilet = Math.Min(10_000, at.Toilet + BeerToiletGain) },
        ActivityKind.Toilet => at with { Toilet = Math.Min(at.Toilet, ToiletAfterVisit) },
        _ => at
    };

    public static NeedLevels Grow(NeedLevels from, NeedGrowth growth, int ticks) => new(
        Math.Min(10_000, from.Thirst + (int)((long)growth.Thirst * ticks / SampleTicks)),
        Math.Min(10_000, from.Heat + (int)((long)growth.Heat * ticks / SampleTicks)),
        Math.Min(10_000, from.Hunger + (int)((long)growth.Hunger * ticks / SampleTicks)),
        Math.Min(10_000, from.Toilet + (int)((long)growth.Toilet * ticks / SampleTicks)));

    /// <summary>Scores one plan. <paramref name="musicPerSecond"/> has one value per sample.</summary>
    public static long Score(NeedLevels now, NeedGrowth growth, IReadOnlyList<long> musicPerSecond, ActivityOption option)
    {
        long score = option.Enjoyment - option.Cost;
        // Relief lands at each stop in turn; a stop beyond the horizon never lands.
        var first = option.Kind != ActivityKind.Watch && option.CompleteTicks < HorizonTicks
            ? Relieve(option.Kind, Grow(now, growth, option.CompleteTicks)) : (NeedLevels?)null;
        var second = first is { } afterFirst && option.Then is { } then && then.CompleteTicks < HorizonTicks
            ? Relieve(then.Kind, Grow(afterFirst, growth, then.CompleteTicks - option.CompleteTicks)) : (NeedLevels?)null;
        for (var sample = 0; sample < Samples; sample++)
        {
            var tick = (sample + 1) * SampleTicks;
            var levels = second is { } afterBoth && tick >= option.Then!.CompleteTicks ? Grow(afterBoth, growth, tick - option.Then.CompleteTicks)
                : first is { } after && tick >= option.CompleteTicks ? Grow(after, growth, tick - option.CompleteTicks) : Grow(now, growth, tick);
            if (tick > option.AwayTicks) score += musicPerSecond[sample];
            score -= Discomfort(levels);
            if (sample == Samples - 1) score -= Discomfort(levels) * TailSeconds;
        }
        return score;
    }

    /// <summary>All options, best first; ties keep the caller's option order.</summary>
    public static ActivityScore[] Rank(NeedLevels now, NeedGrowth growth, IReadOnlyList<long> musicPerSecond, IReadOnlyList<ActivityOption> options) =>
        options.Select((option, index) => (Score: new ActivityScore(option, Score(now, growth, musicPerSecond, option)), index))
            .OrderByDescending(item => item.Score.Score).ThenBy(item => item.index).Select(item => item.Score).ToArray();
}

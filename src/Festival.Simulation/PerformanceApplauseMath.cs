namespace Festival.Simulation;

// Presentation scaling derived from a saved end-of-set audience and its actual earned enjoyment.
public static class PerformanceApplauseMath
{
    public static double Strength(int audienceCount, int enjoymentTotal)
    {
        if (audienceCount <= 0) return 0;
        var average = Math.Max(0, enjoymentTotal) / (double)audienceCount;
        return Math.Clamp(audienceCount / 40d * (0.15 + 0.85 * Math.Clamp(average / 1_000, 0, 1)), 0.02, 0.65);
    }

    public static bool IsEnthusiastic(int audienceCount, int enjoymentTotal) =>
        audienceCount >= 10 && enjoymentTotal >= audienceCount * 400L;
}

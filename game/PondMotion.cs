using System;

namespace Festival.Game;

/// <summary>Local cosmetic time. Pause/terminal/hidden time never enters the clock or saved simulation.</summary>
internal sealed class PondClock
{
    internal double Seconds { get; private set; }
    internal void Advance(double delta, bool paused, bool terminal, bool hidden)
    {
        if (!paused && !terminal && !hidden && double.IsFinite(delta) && delta > 0)
            Seconds += Math.Min(delta, .1); // Keep a stalled render frame from teleporting a duck.
    }
    internal void Reset(double seconds = 0) => Seconds = seconds;
}

internal readonly record struct PondPoint(double X, double Z);
internal readonly record struct PondDuckPose(double X, double Z, double Yaw, double Bob, double Speed);

/// <summary>Uneven, slow closed glides with gentle eased arrivals and brief stillness. Fixed paths, no RNG.</summary>
internal sealed class PondDuckPath(PondPoint[] knots, double travelSeconds, double restSeconds, double phase)
{
    internal PondDuckPose Sample(double seconds)
    {
        var segmentSeconds = travelSeconds + restSeconds;
        var local = (seconds + phase) % (knots.Length * segmentSeconds);
        var index = (int)(local / segmentSeconds);
        var within = local - index * segmentSeconds;
        var u = Math.Clamp(within / travelSeconds, 0, 1);
        var eased = u * u * u * (10 + u * (-15 + 6 * u));
        var rate = 30 * u * u * (1 - u) * (1 - u) / travelSeconds;
        var a = knots[(index + knots.Length - 1) % knots.Length];
        var b = knots[index]; var c = knots[(index + 1) % knots.Length];
        var d = knots[(index + 2) % knots.Length];
        static (double At, double Tangent) Spline(double a, double b, double c, double d, double t)
        {
            var p = -a + c; var q = 2 * a - 5 * b + 4 * c - d; var r = -a + 3 * b - 3 * c + d;
            return (.5 * (2 * b + p * t + q * t * t + r * t * t * t), .5 * (p + 2 * q * t + 3 * r * t * t));
        }
        var x = Spline(a.X,b.X,c.X,d.X,eased); var z = Spline(a.Z,b.Z,c.Z,d.Z,eased);
        var bobWindow = Math.Pow(Math.Max(0, Math.Sin(seconds * Math.Tau / 19 + phase)), 8);
        var bob = .003 * bobWindow * Math.Sin(seconds * 1.8 + phase);
        return new(x.At, z.At, Math.Atan2(-z.Tangent, x.Tangent), bob, Math.Sqrt(x.Tangent*x.Tangent+z.Tangent*z.Tangent)*rate);
    }
}

internal static class PondPaths
{
    // Disjoint central glides; the delivered bank and lily-pad zones stay outside both routes.
    internal static readonly PondDuckPath Drake = new(
        [new(-1.25,-.3),new(-1.12,.5),new(-.68,.3),new(-.82,-.65)],25,5,7);
    internal static readonly PondDuckPath Brown = new(
        [new(.65,-.65),new(1.28,-.12),new(1.15,.6),new(.6,.7),new(.5,.05)],28,4,47);
}

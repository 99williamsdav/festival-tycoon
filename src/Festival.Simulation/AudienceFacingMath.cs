namespace Festival.Simulation;

/// <summary>Cosmetic heading choice; never changes a route, speed or authoritative state.</summary>
public static class AudienceFacingMath
{
    public const int StageXMillimetres = -16_000;
    public const int StageZMillimetres = 11_000;

    public static bool ShouldBackstep(string intent, AgentNavigationAction action,
        double x, double z, double destinationX, double destinationZ, double movementX, double movementZ) =>
        ShouldBackstep(intent, action, x, z, destinationX, destinationZ, movementX, movementZ, StageXMillimetres, StageZMillimetres);

    /// <summary>As above, facing the stage standing at the given point.</summary>
    public static bool ShouldBackstep(string intent, AgentNavigationAction action,
        double x, double z, double destinationX, double destinationZ, double movementX, double movementZ, int stageXMillimetres, int stageZMillimetres)
    {
        if (intent != "performance.listen-local-retreat" || action != AgentNavigationAction.Travelling)
            return false;
        var dx = destinationX - x;
        var dz = destinationZ - z;
        var stageX = stageXMillimetres - x;
        var stageZ = stageZMillimetres - z;
        // Both the remaining leg and actual rendered displacement must be away from
        // the stage. Side/forward avoidance steps and subsequent intents face travel.
        return dx * dx + dz * dz <= 2_001d * 2_001d &&
            movementX * movementX + movementZ * movementZ >= 36 &&
            dx * stageX + dz * stageZ < 0 && movementX * stageX + movementZ * stageZ < 0;
    }
}

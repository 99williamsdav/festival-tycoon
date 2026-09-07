namespace Festival.Simulation;

/// <summary>
/// Stable identity used only to prove the M0.01 project boundaries and toolchain.
/// </summary>
public static class ToolchainSmoke
{
    public const string BuildVersion = "0.0.1-m0.01";

    public static string GetFixedResult() =>
        $"festival-tycoon-smoke|build={BuildVersion}|seed=0|checksum=0000000000000000";
}

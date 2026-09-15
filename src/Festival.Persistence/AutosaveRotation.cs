using Festival.Simulation;

namespace Festival.Persistence;

/// <summary>Three independent atomic autosave slots; manual slots are never touched.</summary>
public static class AutosaveRotation
{
    public const int SlotCount = 3;
    // Five real minutes at 1x (80 authoritative ticks per real second).
    public const long CadenceTicks = 24_000;
    public const long CaptureFixtureCadenceTicks = 800;

    public static long NextDeadline(long currentTick, long cadenceTicks = CadenceTicks)
    {
        if (currentTick < 0 || cadenceTicks <= 0) throw new ArgumentOutOfRangeException();
        return checked((currentTick / cadenceTicks + 1) * cadenceTicks);
    }

    public static string SlotForTick(long tick, long cadenceTicks = CadenceTicks) => $"autosave-{Math.Abs(tick / cadenceTicks) % SlotCount}";

    public static SaveOperationResult Save(string directory, GameSession session, SaveCompatibility compatibility, DateTimeOffset now, long cadenceTicks = CadenceTicks) =>
        SaveFileAdapter.SaveSlot(directory, SlotForTick(session.CurrentTick, cadenceTicks), new SaveWriteRequest(session, compatibility, "autosave", now));

    public static SaveLoadResult LoadNewestValid(string directory, SaveCompatibility compatibility)
    {
        var candidates = Enumerable.Range(0, SlotCount)
            .Select(index => SaveFileAdapter.ResolveSlotPath(directory, $"autosave-{index}"))
            .Where(File.Exists)
            .Select(path => (Path: path, Result: SaveFileAdapter.LoadFile(path, compatibility)))
            .Where(item => item.Result.IsSuccess)
            .OrderByDescending(item => item.Result.Session!.CurrentTick)
            .ThenByDescending(item => item.Path, StringComparer.Ordinal)
            .ToArray();
        return candidates.Length > 0 ? candidates[0].Result : SaveLoadResult.Failure("No valid autosave exists in the three-slot rotation.");
    }
}

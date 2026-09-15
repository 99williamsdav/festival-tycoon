using Festival.Simulation;

namespace Festival.Persistence;

/// <summary>Three independent atomic autosave slots; manual slots are never touched.</summary>
public static class AutosaveRotation
{
    public const int SlotCount = 3;
    // Every 200 festival seconds (50 real seconds at 1x).
    public const long CadenceTicks = 800;

    public static string SlotForTick(long tick) => $"autosave-{Math.Abs(tick / CadenceTicks) % SlotCount}";

    public static SaveOperationResult Save(string directory, GameSession session, SaveCompatibility compatibility, DateTimeOffset now) =>
        SaveFileAdapter.SaveSlot(directory, SlotForTick(session.CurrentTick), new SaveWriteRequest(session, compatibility, "autosave", now));

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

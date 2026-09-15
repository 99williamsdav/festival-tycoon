using Festival.Simulation;

namespace Festival.Persistence;

/// <summary>Three independent atomic autosave slots; manual slots are never touched.</summary>
public static class AutosaveRotation
{
    public const int SlotCount = 3;
    public static string SlotForGeneration(long generation)
    {
        if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
        return $"autosave-{generation % SlotCount}";
    }

    public static SaveOperationResult Save(string directory, GameSession session, SaveCompatibility compatibility, DateTimeOffset now, long generation) =>
        SaveFileAdapter.SaveSlot(directory, SlotForGeneration(generation), new SaveWriteRequest(session, compatibility, "autosave", now));

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

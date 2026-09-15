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
        SaveFileAdapter.SaveSlot(directory, SlotForGeneration(generation), new SaveWriteRequest(session, compatibility, "autosave", now, generation));

    public static long NextGeneration(string directory, SaveCompatibility compatibility)
    {
        var sequences = ValidCandidates(directory, compatibility)
            .Select(item => item.Result.Header!.SaveSequence)
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .ToArray();
        return sequences.Length == 0 ? 0 : checked(sequences.Max() + 1);
    }

    public static SaveLoadResult LoadNewestValid(string directory, SaveCompatibility compatibility)
    {
        var candidates = ValidCandidates(directory, compatibility).ToArray();
        if (candidates.Length == 0) return SaveLoadResult.Failure("No valid autosave exists in the three-slot rotation.");
        Array.Sort(candidates, CompareNewestFirst);
        return candidates[0].Result;
    }

    private static IEnumerable<(string Path, SaveLoadResult Result)> ValidCandidates(string directory, SaveCompatibility compatibility) =>
        Enumerable.Range(0, SlotCount)
            .Select(index => SaveFileAdapter.ResolveSlotPath(directory, $"autosave-{index}"))
            .Where(File.Exists)
            .Select(path => (Path: path, Result: SaveFileAdapter.LoadFile(path, compatibility)))
            .Where(item => item.Result.IsSuccess);

    private static int CompareNewestFirst((string Path, SaveLoadResult Result) left, (string Path, SaveLoadResult Result) right)
    {
        var leftSequence = left.Result.Header!.SaveSequence;
        var rightSequence = right.Result.Header!.SaveSequence;
        int comparison;
        if (leftSequence.HasValue != rightSequence.HasValue)
            comparison = rightSequence.HasValue.CompareTo(leftSequence.HasValue); // sequenced format supersedes legacy slots
        else if (leftSequence.HasValue)
            comparison = rightSequence.GetValueOrDefault().CompareTo(leftSequence.GetValueOrDefault());
        else
        {
            var leftTime = DateTimeOffset.Parse(left.Result.Header.TimestampUtc);
            var rightTime = DateTimeOffset.Parse(right.Result.Header.TimestampUtc);
            comparison = rightTime.CompareTo(leftTime); // legacy/mixed fallback: persisted UTC timestamp
        }
        return comparison != 0 ? comparison : string.Compare(right.Path, left.Path, StringComparison.Ordinal);
    }
}

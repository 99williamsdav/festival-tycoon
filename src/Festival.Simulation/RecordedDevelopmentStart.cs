using System.Text.Json;

namespace Festival.Simulation;

public sealed partial class GameSession
{
    /// <summary>
    /// Menu development Tier 2: restore an actual completed, survival-cleared Tier 1 and use the same transition
    /// as "Next festival". No day is simulated here and no rewards or balances are supplied separately.
    /// The recorded campaign seed and identity deliberately stay fixed, making each start reproducible.
    /// Tier 1 in the menu still uses CreateBuildCampaign with a fresh seed. Legacy CLI/shift fixtures are unchanged.
    /// </summary>
    public static GameSession CreateRecordedTierTwoStart()
    {
        using var stream = typeof(GameSession).Assembly.GetManifestResourceStream("Festival.Simulation.recorded-tier-one-clear.json")
            ?? throw new InvalidOperationException("The recorded Tier 1 clear is missing.");
        var snapshot = JsonSerializer.Deserialize<SessionPersistenceSnapshot>(stream)
            ?? throw new InvalidOperationException("The recorded Tier 1 clear is empty.");
        var restored = Restore(snapshot);
        if (!restored.IsSuccess) throw new InvalidOperationException("The recorded Tier 1 clear could not restore: " + restored.Error);
        var completed = restored.Session!;
        if (completed.CapturePreparation()!.Tier != 1 || !completed.CanStartNextFestival)
            throw new InvalidOperationException("The recorded Tier 1 clear is not eligible for the normal next festival.");
        return completed.CreateNextFestival();
    }
}

using Festival.Simulation;

namespace Festival.ContentAdapter;

/// <summary>World-label text from the actual live set, never the upcoming schedule.</summary>
public static class StageNowPlayingText
{
    public static string? Read(GameSession session, string stageId, IReadOnlyList<LivePerformanceSnapshot>? lives = null, bool menuOpen = false)
    {
        if (menuOpen || session.PreparedStatus != PreparationStatus.Running) return null;
        lives ??= session.CaptureLivePerformances();
        if (!lives.Any(live => live.StageId == stageId && live.Stage == LiveSetStage.Live)) return null;
        return session.CurrentStageAct(stageId) is { } act ? "Now Playing: " + act.Name : null;
    }
}

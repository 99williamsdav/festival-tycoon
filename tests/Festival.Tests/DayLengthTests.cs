using Festival.Simulation;

namespace Festival.Tests;

// The day runs until the last set on any open stage ends, then closes ten festival minutes later.
[TestClass]
public sealed class DayLengthTests
{
    private static readonly FestivalStage Pond = FestivalStages.All.Single(stage => stage.Id == FestivalStages.PondId);

    [TestMethod]
    public void ASingleStageDayIsUnchangedAndThePondsFullThirdSetLengthensTheDay()
    {
        Assert.AreEqual(GameSession.PreparedDayTicks, GameSession.DayTicksFor(FestivalStages.For(false)));
        Assert.AreEqual(38_400, GameSession.CreateBuildCampaign(20260922).PreparedEditionDurationTicks);
        // Pond set 3 runs as long as its others (8,400 ticks) and the gap to closing stays the trailer's 800.
        Assert.AreEqual(Pond.SlotEnds[0] - Pond.SlotStarts[0], Pond.SlotEnds[2] - Pond.SlotStarts[2]);
        Assert.AreEqual(41_200, Pond.SlotEnds[2]);
        Assert.AreEqual(GameSession.ClosingAfterLastSetTicks, GameSession.PreparedDayTicks - FestivalStages.Main.SlotEnds[^1]);
        Assert.AreEqual(42_000, GameSession.CreateBuildCampaign(20260922, pondStageTrial: true).PreparedEditionDurationTicks);
        // The trailer's times don't move.
        CollectionAssert.AreEqual(new[] { 4_800, 16_400, 28_000 }, FestivalStages.Main.SlotStarts.ToArray());
        CollectionAssert.AreEqual(new[] { 13_200, 24_800, 37_600 }, FestivalStages.Main.SlotEnds.ToArray());
    }

    [TestMethod]
    public void APondDayPlaysItsFullLastSetAfterTheTrailerHasFinishedThenCloses()
    {
        var s = BuildSession.PondStarted();
        var start = s.CapturePreparation()!.StartedTick;
        // Past the trailer's last set and the old closing time, the pond's third set still plays and the day runs on.
        s.AdvanceWithoutSnapshot((int)(start + 39_000 - s.CurrentTick));
        Assert.AreEqual(PreparationStatus.Running, s.PreparedStatus, s.CaptureLifecycleSnapshot()?.Casualties.LastOrDefault()?.Cause);
        Assert.AreEqual(2, s.CaptureProgramme(FestivalStages.PondId)!.CurrentSlot);
        Assert.AreEqual(LiveSetStage.Live, s.CaptureLivePerformance(FestivalStages.PondId)!.Stage);
        Assert.AreNotEqual(LiveSetStage.Live, s.CaptureLivePerformance(FestivalStages.MainId)!.Stage);
        Assert.AreEqual(start + 41_200, s.CaptureProgramme(FestivalStages.PondId)!.SlotEndTick);
        // A save in the extra time restores and plays out the same.
        var restored = BuildSession.Restored(s);
        s.AdvanceWithoutSnapshot((int)(start + 41_900 - s.CurrentTick));
        restored.AdvanceWithoutSnapshot((int)(start + 41_900 - restored.CurrentTick));
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(PreparationStatus.Running, s.PreparedStatus, "The day closes at 42,000, not before.");
        s.AdvanceWithoutSnapshot((int)(start + 42_100 - s.CurrentTick));
        Assert.AreEqual(PreparationStatus.Departing, s.PreparedStatus);
        BuildSession.Restored(s);
    }
}

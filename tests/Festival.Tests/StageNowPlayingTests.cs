using Festival.ContentAdapter;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class StageNowPlayingTests
{
    [TestMethod]
    public void RealLiveActsAreIndependentExactAndRemainIdentifiedOnPause()
    {
        var s = BuildSession.PondReady();
        Assert.IsNull(StageNowPlayingText.Read(s, FestivalStages.MainId));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        Assert.IsNull(StageNowPlayingText.Read(s, FestivalStages.MainId));
        var limit = 20_000;
        while (s.CaptureLivePerformances().Count(l => l.Stage == LiveSetStage.Live) != 2 && s.CurrentTick < limit) s.AdvanceWithoutSnapshot(16);
        Assert.AreEqual(2, s.CaptureLivePerformances().Count(l => l.Stage == LiveSetStage.Live));
        var hash = s.CaptureSnapshot().AuthoritativeHash;
        foreach (var stage in s.Stages)
        {
            var expected = "Now Playing: " + s.CurrentStageAct(stage.Id)!.Name;
            Assert.AreEqual(expected, StageNowPlayingText.Read(s, stage.Id));
            Assert.IsNull(StageNowPlayingText.Read(s, stage.Id, menuOpen: true));
            foreach (var state in new[] { LiveSetStage.BeforeSet, LiveSetStage.Interrupted, LiveSetStage.Finished })
            {
                var lives = s.CaptureLivePerformances().Select(l => l.StageId == stage.Id ? l with { Stage = state } : l).ToArray();
                Assert.IsNull(StageNowPlayingText.Read(s, stage.Id, lives));
                var other = s.Stages.Single(p => p.Id != stage.Id);
                Assert.AreEqual("Now Playing: " + s.CurrentStageAct(other.Id)!.Name, StageNowPlayingText.Read(s, other.Id, lives));
            }
        }
        Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash, "World labels cannot mutate or save the simulation.");
        BuildSession.Accept(s, new SetPausedCommand(true));
        foreach (var stage in s.Stages) Assert.AreEqual("Now Playing: " + s.CurrentStageAct(stage.Id)!.Name, StageNowPlayingText.Read(s, stage.Id));
        Assert.IsNull(StageNowPlayingText.Read(s, "unrelated-object"));
    }
}

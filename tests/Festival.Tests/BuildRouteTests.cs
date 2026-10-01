using Festival.Simulation;

namespace Festival.Tests;

// Core-loop coverage on the one production route: a default-plan Build edition played with no
// operator input. Staff autonomy, needs, services, music and disorder all run naturally. The
// edition is simulated once and shared; each test asserts one system's observable outcome.
[TestClass]
public sealed class BuildRouteTests
{
    private sealed record EditionRun(
        GameSession Session,
        long StartingCash,
        (int Chips, int Soft, int Beer) StartingStock,
        HashSet<LiveSetStage> LiveStages,
        HashSet<long> LiveSetTicks,
        long ListenedTicks,
        HashSet<MedicalStage> MedicalStages,
        HashSet<DisorderStage> DisorderStages,
        string HashAtCheckpoint,
        SessionPersistenceSnapshot Checkpoint);

    private const long CheckpointTick = 16_000;
    private const long ContinuationTicks = 4_000;
    private static readonly Lazy<EditionRun> Run = new(PlayDefaultEdition, LazyThreadSafetyMode.ExecutionAndPublication);

    private static EditionRun PlayDefaultEdition()
    {
        var s = BuildSession.Started();
        var immersion = s.CaptureImmersion()!;
        var cash = s.CaptureSnapshot().FestivalFinances.Single().CashPennies;
        HashSet<LiveSetStage> liveStages = []; HashSet<long> liveSets = []; long listened = 0;
        HashSet<MedicalStage> medical = []; HashSet<DisorderStage> disorder = [];
        string? hash = null; SessionPersistenceSnapshot? checkpoint = null;
        while (s.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing && s.CurrentTick < 80_000)
        {
            s.AdvanceWithoutSnapshot(80);
            if (s.CaptureLivePerformance() is { } live)
            {
                liveStages.Add(live.Stage);
                if (live.Stage == LiveSetStage.Live) liveSets.Add(live.PlannedTick);
                listened = Math.Max(listened, live.Listeners.Sum(item => (long)item.ListenedTicks));
            }
            foreach (var need in s.CaptureMedical()!.Needs) medical.Add(need.Stage);
            foreach (var person in s.CaptureDisorder()!.People) disorder.Add(person.Stage);
            if (checkpoint is null && s.CurrentTick >= CheckpointTick)
            {
                checkpoint = s.CapturePersistenceSnapshot();
                var probe = BuildSession.Restored(s);
                probe.AdvanceWithoutSnapshot((int)ContinuationTicks);
                hash = probe.CaptureSnapshot().AuthoritativeHash;
            }
        }
        return new(s, cash, (immersion.ChipsStock, immersion.SoftStock, immersion.BeerStock),
            liveStages, liveSets, listened, medical, disorder, hash!, checkpoint!);
    }

    [TestCategory("Slow")]
    [TestMethod]
    public void DefaultPlanEditionFinishesWithEveryoneSafelyGone()
    {
        var s = Run.Value.Session;
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus);
        Assert.AreEqual(0, s.CaptureLifecycleSnapshot()?.Casualties.Count ?? 0);
        Assert.IsTrue(s.CapturePreparation()!.People.All(person => person.Admitted && person.Departed));
    }

    [TestCategory("Slow")]
    [TestMethod]
    public void EveryBookedSetGoesLiveAndListenersAccrueTime()
    {
        var run = Run.Value;
        Assert.AreEqual(GameSession.FestivalSlotStarts.Length, run.LiveSetTicks.Count);
        Assert.IsTrue(run.LiveStages.Contains(LiveSetStage.Finished));
        Assert.IsTrue(run.ListenedTicks > 0);
    }

    [TestCategory("Slow")]
    [TestMethod]
    public void GuestsUseToiletsAndBuyFoodDrinkAndBeer()
    {
        var run = Run.Value;
        Assert.IsTrue(run.Session.CaptureToilets().Sum(toilet => toilet.WeeCount + toilet.PooCount) > 0);
        var stock = run.Session.CaptureImmersion()!;
        Assert.IsTrue(stock.ChipsStock < run.StartingStock.Chips);
        Assert.IsTrue(stock.SoftStock < run.StartingStock.Soft);
        Assert.IsTrue(stock.BeerStock < run.StartingStock.Beer);
        Assert.IsTrue(run.Session.CaptureSnapshot().FestivalFinances.Single().CashPennies > run.StartingCash);
    }

    [TestCategory("Slow")]
    [TestMethod]
    public void OpenFreeWaterKeepsDefaultGuestsOutOfHeatDistress()
    {
        // Heat distress needs thirst and heat together; guests who can reach the tap drink first.
        var run = Run.Value;
        Assert.IsFalse(run.MedicalStages.Contains(MedicalStage.Distress));
        Assert.IsFalse(run.Session.CaptureMedical()!.Fatal);
    }

    [TestCategory("Slow")]
    [TestMethod]
    public void CrowdedSiteDisorderEscalatesAndStewardsResolveIt()
    {
        // The default plan is calm: guests leave long lines and performers are ready on time.
        // Three times the crowd on the same single tap still builds water-wait grievances.
        var s = Festival.Simulation.Fixtures.BuildScaleFixture.Create(20260922, 60);
        var perks = s.CapturePerks()!;
        BuildSession.Accept(s, new ChoosePerkCommand(perks.DraftAttempt, perks.Cursor, perks.Hand[0]));
        BuildSession.Accept(s, new UseDefaultBuildLayoutCommand());
        BuildSession.Accept(s, new SetProgrammeCommand(BuildSession.Acts));
        BuildSession.Accept(s, new SetPreparationStockCommand(40, 40, 32));
        foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        HashSet<DisorderStage> stages = [];
        while (s.PreparedStatus == PreparationStatus.Running && s.CurrentTick < 38_400)
        {
            s.AdvanceWithoutSnapshot(80);
            foreach (var person in s.CaptureDisorder()!.People) stages.Add(person.Stage);
        }
        Assert.IsTrue(stages.Contains(DisorderStage.Argument));
        Assert.IsTrue(stages.Contains(DisorderStage.Resolved));
    }

    [TestCategory("Slow")]
    [TestMethod]
    public void MidEditionSaveContinuesIdentically()
    {
        var run = Run.Value;
        var restored = GameSession.Restore(run.Checkpoint);
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        restored.Session!.AdvanceWithoutSnapshot((int)ContinuationTicks);
        Assert.AreEqual(run.HashAtCheckpoint, restored.Session.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void SameSeedAndPlanReplayToTheSameState()
    {
        var first = BuildSession.Started(20260930);
        var second = BuildSession.Started(20260930);
        first.AdvanceWithoutSnapshot(6_000);
        for (var elapsed = 0; elapsed < 6_000; elapsed += 250) second.AdvanceWithoutSnapshot(250);
        Assert.AreEqual(first.CaptureSnapshot().AuthoritativeHash, second.CaptureSnapshot().AuthoritativeHash);
    }
}

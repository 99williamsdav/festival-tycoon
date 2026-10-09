using System.Reflection;
using Festival.Simulation;
using Festival.Simulation.Fixtures;

namespace Festival.Tests;

/// <summary>
/// The Pond Stage, the second fixed stage a trial campaign runs before Tier 2: its ground, its band's way on and off,
/// how the crowd picks between the two stages, each stage's own engineer and generator, saves with both stages
/// playing, and a campaign without the trial playing exactly as before.
/// </summary>
[TestClass]
public sealed class PondStageTests
{
    private static readonly FestivalStage Pond = FestivalStages.Pond;

    // ---- Ground ----

    [TestMethod]
    public void PondStageGroundIsClearAndItsBandCanClimbToEveryMark()
    {
        var bare = new TraversalGrid(NavigationFixture.CreateLowerWitteringTerrain());
        var terrain = bare.Overrides.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var (cell, walkable) in FestivalStages.All.SelectMany(stage => stage.Cells())) terrain[cell] = new(cell, GroundSurface.Grass, walkable);
        var grid = new TraversalGrid(terrain.Values);
        // Everything the riser stands on was open grass, off the farm track and inside the hedge, which stays whole.
        foreach (var (cell, _) in Pond.Cells())
        {
            Assert.IsTrue(bare.Get(cell) is { IsWalkable: true, Surface: GroundSurface.Grass }, $"{cell} was not open grass.");
            Assert.IsTrue(cell.X is > 131 and <= 190 && cell.Z is >= 108 and <= 190, $"{cell} is on the track or past the hedge.");
        }
        for (var x = Pond.ReserveBounds.MinX; x <= Pond.ReserveBounds.MaxX; x++)
            for (var z = Pond.ReserveBounds.MinZ; z <= Pond.ReserveBounds.MaxZ; z++)
                Assert.IsTrue(bare.Get(new(x, z)).IsWalkable, $"The reserve's {x},{z} is in the pond or a hedge.");
        // The default layout and the trailer's crowd keep well away.
        foreach (var item in GameSession.StandardBuildLayout())
            Assert.IsFalse(GameSession.BuildFootprint(item).Any(cell => Pond.Reserve(cell) || Pond.InAudienceArea(cell)), item.Id);
        Assert.IsFalse(Pond.ListeningPlaces.Any(FestivalStages.Main.InAudienceArea) || Pond.ListeningPlaces.Any(Pond.Reserve));
        // A smaller crowd than the trailer's, on ground every guest can reach from the main gate.
        Assert.AreEqual(66, Pond.ListeningPlaces.Count);
        Assert.IsTrue(Pond.ListeningPlaces.Count < FestivalStages.Main.ListeningPlaces.Count);
        foreach (var place in Pond.ListeningPlaces)
            Assert.IsTrue(DeterministicPathfinder.FindPath(grid, new(128, 189), place).Found, $"{place} can't be reached from the gate.");
        for (var role = 0; role < 3; role++)
        {
            Assert.IsTrue(Pond.Deck(Pond.PerformerMarks[role]) && Pond.Stairs(Pond.StairCells[role]) && !Pond.Stairs(Pond.AccessCells[role]) && !Pond.Deck(Pond.AccessCells[role]));
            for (var member = role; member < 9; member += 3)
            {
                // In off the trailer bands' lane through the garden gate, across the field to the band's waiting ground,
                // then to the foot of the stair; never on a trailer band member's spot on the lane.
                Assert.IsFalse(Enumerable.Range(0, 9).Any(other => FestivalStages.Main.ArrivalStart(other) == Pond.ArrivalStart(member)));
                var lane = DeterministicPathfinder.FindPath(grid, Pond.ArrivalStart(member), Pond.BackstagePlace(member));
                Assert.IsTrue(lane.Found && lane.Path.Any(Backstage.HedgeGate), $"Band member {member} doesn't come in by the garden gate.");
                Assert.IsTrue(DeterministicPathfinder.FindPath(grid, Pond.BackstagePlace(member), Pond.AccessCells[role]).Found);
            }
            // Up the side stair: from its foot every step to the mark is on the flight or the deck.
            var climb = DeterministicPathfinder.FindPath(grid, Pond.AccessCells[role], Pond.PerformerMarks[role]);
            Assert.IsTrue(climb.Found && climb.Path.Skip(1).All(cell => Pond.Stairs(cell) || Pond.Deck(cell)) && climb.Path.Any(Pond.Stairs), $"Role {role} doesn't climb the stair.");
        }
        // The riser stands half turned at its fixed spot, deck 0.9 m up.
        Assert.AreEqual(new StagePlacement(20_000, 17_000, 180, 900), Pond.Placement);
    }

    [TestMethod]
    public void BuildingKeepsOffThePondStageAndItsCrowdOnlyWhenItRuns()
    {
        var pondCrowd = Pond.AudienceCentre;
        var trial = BuildSession.PondDrafted();
        var refused = BuildSession.Send(trial, new MoveBuildServiceCommand("drinks", pondCrowd, 3));
        Assert.IsFalse(refused.IsAccepted);
        StringAssert.Contains(refused.Message, "audience area");
        Assert.IsFalse(BuildSession.Send(trial, new MoveBuildServiceCommand("drinks", new(184, 163), 3)).IsAccepted, "The band's ground is kept clear.");
        Assert.IsFalse(BuildSession.Send(trial, new MoveBuildServiceCommand("first-aid", new(156, 160), 0)).IsAccepted, "The pond generator's ground is kept clear.");
        // Without the Pond Stage the same grass is free to build on.
        var plain = BuildSession.Drafted();
        BuildSession.Accept(plain, new MoveBuildServiceCommand("drinks", pondCrowd, 3));
    }

    // ---- The band ----

    [TestMethod]
    public void PondBandsComeInByTheGardenGateClimbTheStairPlayOnTimeAndClearOff()
    {
        var day = PondDay.Value;
        foreach (var slot in new[] { 0, 1 })
            Assert.AreEqual((long)Pond.SlotStarts[slot], day.LiveAt[(FestivalStages.PondId, slot)], $"Pond set {slot + 1} didn't start on time.");
        CollectionAssert.AreEqual(new[] { 9_000, 20_600, 32_800 }, Pond.SlotStarts.ToArray());
        CollectionAssert.AreEqual(new[] { 17_400, 29_000, 41_200 }, Pond.SlotEnds.ToArray());
        foreach (var (id, track) in day.PondBand)
            Assert.IsTrue(track.Cells.Any(Backstage.HedgeGate), $"Pond band member {id} never came in by the garden gate.");
        foreach (var (id, track) in day.PondBand.Where(item => item.Value.Slot < 2))
        {
            Assert.IsTrue(track.Cells.Any(Pond.Stairs), $"Pond band member {id} never climbed the stair.");
            Assert.IsTrue(track.OnMark, $"Pond band member {id} never stood on their mark.");
        }
        // Every pond band member left the lane on their own tick, after the trailer's bands had all set off, and walked
        // straight over: no longer than the release's own generous walk estimate.
        var leftAt = day.PondBand.Select(item => item.Value.LeftLane).ToArray();
        Assert.AreEqual(leftAt.Length, leftAt.Distinct().Count(), "Two pond band members left the lane together.");
        Assert.IsTrue(leftAt.All(tick => tick >= GameSession.BandReleaseStartTicks), string.Join(",", leftAt));
        Assert.IsTrue(day.TrailerBandLeft.Count == 9 && day.TrailerBandLeft.All(tick => tick < GameSession.BandReleaseStartTicks / 4), "The trailer's bands still set off at once.");
        foreach (var (id, track) in day.PondBand)
        {
            Assert.IsTrue(track.Arrived > track.LeftLane, $"Pond band member {id} never got there.");
            var walk = GameSession.BandWalkTicks(Pond.ArrivalStart(track.Ordinal), Pond.AccessCells[track.Ordinal % 3]);
            Assert.IsTrue(track.Arrived - track.LeftLane <= walk, $"Pond band member {id} took {track.Arrived - track.LeftLane} ticks against {walk}.");
            if (track.Slot == 0)
                Assert.IsTrue(track.Arrived <= Pond.SlotStarts[0] - GameSession.LiveSetStageEntryLeadTicks - GameSession.BandArrivalSlackTicks / 2,
                    $"Pond band member {id} reached the stair at {track.Arrived}, cutting it fine.");
        }
        // Set 1's band came back down the stair and off the riser before set 2's band went up.
        foreach (var (id, track) in day.PondBand.Where(item => item.Value.Slot == 0))
        {
            Assert.IsTrue(track.AfterSet.Any(Pond.Stairs), $"Pond band member {id} didn't come down the stair.");
            Assert.IsFalse(Pond.Access(track.BeforeChangeover), $"Pond band member {id} still on the riser at the changeover.");
        }
    }

    // ---- The crowd ----

    [TestMethod]
    public void GuestsWalkOverForTheActTheyWantToSeeMore()
    {
        var day = PondDay.Value;
        // Mid-way through the trailer's first set, with the pond's playing too: the crowd is split, and the pond's crowd
        // wants the pond's act more, against the trailer's, than the trailer's crowd does.
        var split = day.Crowds.Where(item => item.Key is >= 10_400 and <= 12_800).ToArray();
        Assert.IsTrue(split.All(item => item.Value.Main.Length > 0 && item.Value.Pond.Length > 0), "Both stages should have a crowd while both play.");
        foreach (var (tick, crowd) in split)
            Assert.IsTrue(crowd.Pond.Average(id => day.Preference[(tick, id)]) > crowd.Main.Average(id => day.Preference[(tick, id)]), $"At {tick}.");
        // Anyone who left a playing set for another, from their place, wanted that act by more than the margin.
        var walkedOver = day.Switches.Where(item => item.BothLive && item.FromPlace).ToArray();
        Assert.IsTrue(walkedOver.Length > 0, "Nobody left a playing set for the other stage.");
        foreach (var item in walkedOver)
            Assert.IsTrue(item.WantTo * 100 > item.WantFrom * (100 + GameSession.StageSwitchMarginPercent), $"{item.Guest} at {item.Tick}: {item.WantFrom} -> {item.WantTo}");
        // And once a set ends its crowd goes to the stage still playing.
        Assert.IsTrue(day.Switches.Any(item => !item.FromLive), "Nobody moved on when a set ended.");
    }

    [TestMethod]
    public void AGuestNeedsAClearReasonToChangeStage()
    {
        const long set = 48_000_000; // about two minutes of a good set from where they stand
        Assert.AreEqual(0, GameSession.ChooseStage(0, [set, set + set / 5]), "A fifth better isn't enough to walk over.");
        Assert.AreEqual(1, GameSession.ChooseStage(1, [set + set / 5, set]));
        Assert.AreEqual(1, GameSession.ChooseStage(0, [set, set * 3 / 2]));
        Assert.AreEqual(1, GameSession.ChooseStage(1, [GameSession.StageSwitchMarginValue - 1, 0]), "From a finished set, only real music draws them.");
        Assert.AreEqual(0, GameSession.ChooseStage(1, [GameSession.StageSwitchMarginValue + 1, 0]));
        // Over the day nobody goes back to a stage they left within the settling time.
        var day = PondDay.Value;
        foreach (var group in day.Switches.GroupBy(item => item.Guest))
        {
            var moves = group.OrderBy(item => item.Tick).ToArray();
            for (var i = 1; i < moves.Length; i++)
                Assert.IsTrue(moves[i].Tick - moves[i - 1].Tick >= GameSession.StageSwitchDwellTicks, $"Guest {group.Key} flip-flopped at {moves[i].Tick}.");
        }
    }

    // ---- Each stage's engineer and generator ----

    [TestMethod]
    public void EachStageHasItsOwnSoundEngineer()
    {
        var s = BuildSession.PondDrafted();
        BuildSession.Accept(s, new SetProgrammeCommand(BuildSession.Acts));
        BuildSession.Accept(s, new SetProgrammeCommand(BuildSession.PondActs) { StageId = FestivalStages.PondId });
        BuildSession.Accept(s, new SetPreparationStockCommand(40, 32));
        foreach (var hire in BuildSession.Crew(s).Where(hire => !hire.OfferId.StartsWith("staff.sound.", StringComparison.Ordinal))) BuildSession.Accept(s, hire);
        var sound = s.GetStaffCandidates().Where(c => c.Role == StaffRole.Sound).ToArray();
        var best = sound.MaxBy(c => c.MixingBonus)!;
        var worst = sound.MinBy(c => c.MixingBonus)!;
        Assert.IsTrue(best.MixingBonus > worst.MixingBonus);
        BuildSession.Accept(s, new AcceptPreparationOfferCommand(best.Id));
        Assert.IsFalse(BuildSession.Send(s, new AcceptPreparationOfferCommand(best.ExtraOfferId)).IsAccepted, "One engineer can't mix both stages.");
        BuildSession.Accept(s, new AcceptPreparationOfferCommand(worst.ExtraOfferId));
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        var rig = PerformanceRules.RigSound(s.Rig);
        Assert.AreEqual(Math.Clamp(rig + best.MixingBonus * PerformanceRules.MixingStep, 0, 100), s.SoundScoreAt(FestivalStages.MainId));
        Assert.AreEqual(Math.Clamp(rig + worst.MixingBonus * PerformanceRules.MixingStep, 0, 100), s.SoundScoreAt(FestivalStages.PondId));
        Assert.AreEqual(worst.Name, s.CapturePerson(s.StageEngineerId(FestivalStages.PondId)!.Value)!.Name);
        // The pond's engineer getting drunk dulls only the pond's sound.
        var main = s.SoundScoreAt(FestivalStages.MainId);
        var pond = s.SoundScoreAt(FestivalStages.PondId);
        var mutate = typeof(GameSession).GetMethod("MutatePerson", BindingFlags.Instance | BindingFlags.NonPublic)!;
        mutate.Invoke(s, [s.StageEngineerId(FestivalStages.PondId)!.Value, (Action<Person>)(p => p.Intoxication = 7_500)]);
        Assert.AreEqual(main, s.SoundScoreAt(FestivalStages.MainId));
        Assert.AreEqual(pond - 3 * PerformanceRules.MixingStep, s.SoundScoreAt(FestivalStages.PondId));
        // The pond's engineer mixes behind the pond's crowd.
        Assert.AreEqual(Pond.MixingPlace, s.StaffAssignedPost(s.StageEngineerId(FestivalStages.PondId)!.Value));
    }

    [TestMethod]
    public void CuttingOneStagesGeneratorSilencesOnlyThatStage()
    {
        var pondCut = LiveOnBoth();
        BuildSession.Accept(pondCut, new StageGeneratorCommand(FestivalStages.PondId, StageGeneratorAction.Isolate));
        pondCut.AdvanceWithoutSnapshot(2);
        Assert.AreEqual(LiveSetStage.Interrupted, pondCut.CaptureLivePerformance(FestivalStages.PondId)!.Stage);
        Assert.AreEqual("silence", pondCut.CaptureLivePerformance(FestivalStages.PondId)!.LastReaction);
        Assert.AreEqual(LiveSetStage.Live, pondCut.CaptureLivePerformance(FestivalStages.MainId)!.Stage);
        Assert.IsTrue(pondCut.StagePowered && pondCut.CaptureEquipment()!.Stage == EquipmentStage.Resolved);

        var mainCut = LiveOnBoth();
        BuildSession.Accept(mainCut, new EquipmentCommand(EquipmentAction.Isolate));
        mainCut.AdvanceWithoutSnapshot(2);
        Assert.AreEqual(LiveSetStage.Interrupted, mainCut.CaptureLivePerformance(FestivalStages.MainId)!.Stage);
        Assert.AreEqual(LiveSetStage.Live, mainCut.CaptureLivePerformance(FestivalStages.PondId)!.Stage);
        Assert.AreEqual(EquipmentStage.Resolved, mainCut.CaptureStageGenerator(FestivalStages.PondId)!.Stage);
    }

    [TestMethod]
    public void TheProRigStrainsThePondGeneratorButNotTheFarms()
    {
        // The pro rig on cheap acts. With the bar and food van switched off the farm's diesel carries it; the pond's 70 can't carry 80.
        var s = BuildSession.PondDrafted();
        BuildSession.Accept(s, new SetProgrammeCommand(BuildSession.Acts));
        BuildSession.Accept(s, new SetProgrammeCommand(["act.two-men-harmonium", "act.dj-spreadsheet", "act.kerry-co-op"]) { StageId = FestivalStages.PondId });
        BuildSession.Accept(s, new SetPreparationStockCommand(40, 32));
        foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        BuildSession.Accept(s, new AcceptPreparationOfferCommand(BuildSession.ExtraId(s, StaffRole.Sound)));
        BuildSession.Accept(s, new AcceptPreparationOfferCommand(PowerRules.ProRigOffer));
        // Planned, the pond rig's peak is over its generator; the trailer's power plan doesn't count it.
        Assert.AreEqual(new PowerDraw(PowerRules.ProRigDraw, 0, 0, 0, StageGeneratorRules.PondCapacity), s.CaptureStagePower(FestivalStages.PondId));
        Assert.AreEqual(PowerRules.ProRigDraw, s.CapturePower().Stage);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        BuildSession.Accept(s, new EquipmentCommand(EquipmentAction.ToggleBarPower));
        BuildSession.Accept(s, new EquipmentCommand(EquipmentAction.ToggleFoodPower));
        var start = s.CapturePreparation()!.StartedTick;
        Assert.AreEqual(PowerRules.RigStandbyDraw, s.CaptureStagePower(FestivalStages.PondId)!.Stage, "Idle until its set plays.");
        s.AdvanceWithoutSnapshot((int)(start + Pond.SlotStarts[0] + 200 - s.CurrentTick));
        Assert.AreEqual(LiveSetStage.Live, s.CaptureLivePerformance(FestivalStages.PondId)!.Stage);
        var soundBefore = s.SoundScoreAt(FestivalStages.PondId);
        while (s.CaptureStageGenerator(FestivalStages.PondId)!.Stage != EquipmentStage.Warning && s.CurrentTick < start + Pond.SlotEnds[0])
            s.AdvanceWithoutSnapshot(40);
        Assert.AreEqual(EquipmentStage.Warning, s.CaptureStageGenerator(FestivalStages.PondId)!.Stage);
        Assert.AreEqual(soundBefore - PerformanceRules.StrainedSoundPenalty, s.SoundScoreAt(FestivalStages.PondId));
        Assert.AreEqual(EquipmentStage.Resolved, s.CaptureEquipment()!.Stage);
        Assert.AreEqual(0, s.CaptureEquipment()!.Strain);
        // Once the trailer's set ends its rig idles, whatever the pond's is doing.
        s.AdvanceWithoutSnapshot((int)(start + FestivalStages.Main.SlotEnds[0] + 80 - s.CurrentTick));
        Assert.AreEqual(LiveSetStage.Live, s.CaptureLivePerformance(FestivalStages.PondId)!.Stage);
        Assert.AreEqual(PowerRules.RigStandbyDraw, s.CapturePower().Stage);
    }

    // ---- Saves ----

    [TestMethod]
    public void SavesRestoreMidDayWithBothStagesPlaying()
    {
        var s = LiveOnBoth();
        s.AdvanceWithoutSnapshot(2_000);
        Assert.IsTrue(s.CaptureLivePerformances().All(live => live.Stage == LiveSetStage.Live && live.Listeners.Length > 0), "Both stages should be playing to a crowd.");
        var restored = BuildSession.Restored(s);
        s.AdvanceWithoutSnapshot(1_200);
        restored.AdvanceWithoutSnapshot(1_200);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, restored.CaptureSnapshot().AuthoritativeHash);

        var saved = s.CapturePersistenceSnapshot();
        Assert.IsTrue(saved.PondStageTrial && saved.StageGenerators!.Length == 1 && saved.LivePerformances!.Length == 2);
        // Each must be caught by validation, not merely by the state hash no longer matching.
        void Rejects(SessionPersistenceSnapshot broken, string why)
        {
            var result = GameSession.Restore(broken);
            Assert.IsFalse(result.IsSuccess, why);
            Assert.IsFalse(result.Error!.Contains("hash mismatch", StringComparison.Ordinal), $"{why} {result.Error}");
        }
        var main = saved.LivePerformances[0]!;
        var pond = saved.LivePerformances[1]!;
        var someone = main.Listeners[0];
        Rejects(saved with { LivePerformances = [main, pond with { Listeners = [.. pond.Listeners, someone with { Place = null }] }] }, "A guest in both crowds.");
        Rejects(saved with { LivePerformances = [main with { Listeners = main.Listeners.Skip(1).ToArray() }, pond] }, "A guest in no crowd.");
        Rejects(saved with { LivePerformances = [main, pond with { Performers = [pond.Performers[0] with { AgentId = main.Performers[0].AgentId }, .. pond.Performers.Skip(1)] }] }, "A player in both bands.");
        Rejects(saved with { LivePerformances = [main, null] }, "The pond's set is playing, so it can't be missing.");
        Rejects(saved with { StageGenerators = null }, "The pond's generator is missing.");
        Rejects(saved with { PondStageTrial = false }, "The trial flag is part of the save.");
        Rejects(saved with { Programme = saved.Programme! with { Stages = [saved.Programme.Stages[0], saved.Programme.Stages[1] with { EngineerId = null }] } }, "The pond's engineer slot is fixed.");
        // A plain campaign's save can't claim the trial either.
        var plain = BuildSession.Started();
        Rejects(plain.CapturePersistenceSnapshot() with { PondStageTrial = true }, "A plain campaign isn't a trial.");
    }

    // ---- Without the trial ----

    [TestMethod]
    public void WithoutTheTrialTheFestivalIsTheTrailerStageAsBefore()
    {
        var plain = GameSession.CreateBuildCampaign(20260922);
        var off = GameSession.CreateBuildCampaign(20260922, pondStageTrial: false);
        Assert.AreEqual(plain.CaptureSnapshot().AuthoritativeHash, off.CaptureSnapshot().AuthoritativeHash);
        Assert.IsFalse(off.PondStageOpen);
        CollectionAssert.AreEqual(new[] { FestivalStages.MainId }, off.Stages.Select(stage => stage.Id).ToArray());
        Assert.AreEqual(25 + 1 + 3 + 1 + 1 + 6, off.CapturePreparation()!.People.Length);
        Assert.IsFalse(off.GetPreparationOffers().Any(offer => offer.Category == "extra-sound"));
        var on = GameSession.CreateBuildCampaign(20260922, pondStageTrial: true);
        Assert.AreNotEqual(plain.CaptureSnapshot().AuthoritativeHash, on.CaptureSnapshot().AuthoritativeHash);
        // A trial roster: the Pond Stage's engineer slot and nine more band members, 47 before any optional hire. With
        // maintenance and both perk-granted extras that is 50, the most the roster allows.
        Assert.AreEqual(47, on.CapturePreparation()!.People.Length);
        // A started plain day saves nothing of the second stage.
        var json = System.Text.Json.JsonSerializer.Serialize(BuildSession.Started().CapturePersistenceSnapshot());
        foreach (var field in new[] { "PondStageTrial", "StageGenerators", "EngineerId", "JoinedTick", "SetEndEnjoymentAway", FestivalStages.PondId })
            Assert.IsFalse(json.Contains($"\"{field}\"", StringComparison.Ordinal), field);
    }

    // ---- Booking ----

    [TestMethod]
    public void ATrialBooksThreeActsAndAnEngineerForEachStage()
    {
        var s = BuildSession.PondDrafted();
        Assert.IsTrue(s.GetFestivalActs().Count(act => s.ActStandingOf(act) != ActStanding.Locked) >= 14, "Six acts need a wider offer.");
        BuildSession.Accept(s, new SetProgrammeCommand(BuildSession.Acts));
        BuildSession.Accept(s, new SetPreparationStockCommand(40, 32));
        foreach (var hire in BuildSession.Crew(s)) BuildSession.Accept(s, hire);
        var blockers = s.GetPreparationStartBlockers().Select(blocker => blocker.Message).ToArray();
        Assert.AreEqual(2, blockers.Length, string.Join(" | ", blockers));
        Assert.IsFalse(BuildSession.Send(s, new SetProgrammeCommand([BuildSession.Acts[0], .. BuildSession.PondActs.Skip(1)]) { StageId = FestivalStages.PondId }).IsAccepted,
            "An act plays one stage.");
        BuildSession.Accept(s, new SetProgrammeCommand(BuildSession.PondActs) { StageId = FestivalStages.PondId });
        BuildSession.Accept(s, new AcceptPreparationOfferCommand(BuildSession.ExtraId(s, StaffRole.Sound)));
        Assert.AreEqual(0, s.GetPreparationStartBlockers().Count);
        BuildSession.Accept(s, new StartPreparedEditionCommand());
        CollectionAssert.AreEqual(BuildSession.PondActs, s.CaptureProgramme(FestivalStages.PondId)!.ActIds);
        CollectionAssert.AreEqual(BuildSession.Acts, s.CaptureProgramme()!.ActIds);
        Assert.AreEqual(9, s.CaptureProgramme(FestivalStages.PondId)!.Performers.Length);
    }

    [TestMethod]
    public void AnyoneNotInABandHasNoStage()
    {
        var s = BuildSession.PondReady();
        var bandStage = typeof(GameSession).GetMethod("BandStage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pondPlayer = s.CaptureProgramme(FestivalStages.PondId)!.Performers[0].AgentId;
        Assert.AreSame(Pond, bandStage.Invoke(s, [pondPlayer]));
        var guest = BuildSession.LastGuest(s);
        var thrown = Assert.ThrowsExactly<TargetInvocationException>(() => bandStage.Invoke(s, [guest]));
        Assert.IsInstanceOfType<InvalidOperationException>(thrown.InnerException);
    }

    /// <summary>A trial day just after the Pond Stage's first set starts, with the trailer's first set playing too.</summary>
    private static GameSession LiveOnBoth()
    {
        var s = BuildSession.PondStarted();
        s.AdvanceWithoutSnapshot((int)(s.CapturePreparation()!.StartedTick + Pond.SlotStarts[0] + 160 - s.CurrentTick));
        Assert.AreEqual(LiveSetStage.Live, s.CaptureLivePerformance(FestivalStages.MainId)!.Stage);
        Assert.AreEqual(LiveSetStage.Live, s.CaptureLivePerformance(FestivalStages.PondId)!.Stage);
        return s;
    }

    // ---- One recorded trial day, shared by the tests that read it ----

    private static readonly Lazy<DayRecord> PondDay = new(() => DayRecord.Run(20260922, 30_000));

    private sealed record Switch(long Tick, ulong Guest, string From, string To, long WantFrom, long WantTo, bool FromLive, bool BothLive, bool FromPlace);

    private sealed class BandTrack
    {
        public int Slot, Ordinal;
        public HashSet<GridCell> Cells = [], AfterSet = [];
        public bool OnMark;
        public GridCell BeforeChangeover, Start;
        public long LeftLane = -1, Arrived = -1;
    }

    private sealed class DayRecord
    {
        public readonly Dictionary<(string Stage, int Slot), long> LiveAt = [];
        public readonly Dictionary<ulong, BandTrack> PondBand = [];
        public readonly List<long> TrailerBandLeft = [];
        public readonly List<Switch> Switches = [];
        public readonly SortedDictionary<long, (ulong[] Main, ulong[] Pond)> Crowds = [];
        // How much more a guest wants the pond's act than the trailer's, at each crowd sample.
        public readonly Dictionary<(long Tick, ulong Guest), long> Preference = [];

        public static DayRecord Run(ulong seed, int ticks)
        {
            var record = new DayRecord();
            var s = BuildSession.PondStarted(seed);
            var start = s.CapturePreparation()!.StartedTick;
            var pondProgramme = s.CaptureProgramme(FestivalStages.PondId)!;
            foreach (var role in pondProgramme.Performers)
                record.PondBand[role.AgentId] = new BandTrack { Slot = role.SlotIndex, Ordinal = Array.IndexOf(pondProgramme.Performers, role),
                    Start = Pond.ArrivalStart(Array.IndexOf(pondProgramme.Performers, role)) };
            var mainPerformers = s.CaptureProgramme()!.Performers;
            var trailerBand = mainPerformers.Select((role, ordinal) => (role.AgentId, Cell: FestivalStages.Main.ArrivalStart(ordinal))).ToDictionary(item => item.AgentId, item => item.Cell);

            var guests = s.CapturePreparation()!.People.Where(p => p.Role == ProtectedPersonRole.Guest).Select(p => p.AgentId).ToArray();
            var lastStage = guests.ToDictionary(id => id, s.ListeningStageId);
            var lastAtPlace = new Dictionary<ulong, bool>();
            var changeover = start + Pond.SlotStarts[1] - GameSession.LiveSetStageEntryLeadTicks - 40;
            while (s.CurrentTick < start + ticks)
            {
                s.AdvanceWithoutSnapshot(1);
                var tick = s.CurrentTick - start;
                var lives = s.Stages.Select(stage => s.CaptureLivePerformance(stage.Id)!).ToArray();
                for (var index = 0; index < lives.Length; index++)
                    if (lives[index].Stage == LiveSetStage.Live && !record.LiveAt.ContainsKey((lives[index].StageId, s.CaptureProgramme(lives[index].StageId)!.CurrentSlot)))
                        record.LiveAt[(lives[index].StageId, s.CaptureProgramme(lives[index].StageId)!.CurrentSlot)] = tick;
                foreach (var id in guests)
                {
                    var now = s.ListeningStageId(id);
                    if (now != lastStage[id] && lastStage[id] is { } from && now is { } to)
                    {
                        var fromLive = lives.Single(live => live.StageId == from).Stage == LiveSetStage.Live;
                        var toLive = lives.Single(live => live.StageId == to).Stage == LiveSetStage.Live;
                        record.Switches.Add(new(tick, id, from, to, s.StageWantToSee(id, from), s.StageWantToSee(id, to), fromLive, fromLive && toLive,
                            lastAtPlace.GetValueOrDefault(id)));
                    }
                    lastStage[id] = now;
                }
                lastAtPlace.Clear();
                foreach (var live in lives)
                    foreach (var listener in live.Listeners) lastAtPlace[listener.AgentId] = listener.AtPlace;
                if (tick % 400 == 0)
                {
                    record.Crowds[tick] = (lives[0].Listeners.Select(l => l.AgentId).ToArray(), lives[1].Listeners.Select(l => l.AgentId).ToArray());
                    foreach (var id in guests)
                        record.Preference[(tick, id)] = s.StageWantToSee(id, FestivalStages.PondId) - s.StageWantToSee(id, FestivalStages.MainId);
                }
                {
                    var pondLive = lives[1];
                    foreach (var agent in s.CaptureObservation().NavigationAgents)
                    {
                        var cell = TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres);
                        if (trailerBand.TryGetValue(agent.Id.Value, out var lane) && cell != lane)
                        {
                            trailerBand.Remove(agent.Id.Value);
                            record.TrailerBandLeft.Add(tick);
                        }
                        if (!record.PondBand.TryGetValue(agent.Id.Value, out var track)) continue;
                        if (track.LeftLane < 0 && cell != track.Start) track.LeftLane = tick;
                        if (track.LeftLane >= 0 && track.Arrived < 0 && agent.Action == AgentNavigationAction.Arrived) track.Arrived = tick;
                        track.Cells.Add(cell);
                        if (tick > Pond.SlotEnds[track.Slot]) track.AfterSet.Add(cell);
                        if (pondLive.Performers.SingleOrDefault(p => p.AgentId == agent.Id.Value) is { OnStage: true } performer && cell == performer.StageCell) track.OnMark = true;
                        if (s.CurrentTick == changeover) track.BeforeChangeover = cell;
                    }
                }
            }
            return record;
        }
    }
}

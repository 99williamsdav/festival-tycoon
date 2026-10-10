using Festival.Simulation;
using System.Reflection;
using System.Collections;
using System.Diagnostics;

namespace Festival.Tests;

[TestClass]
public sealed class FestivalProgrammeTests
{
    private static CommandResult Send(GameSession s, SessionCommand command) => s.Execute(new(new CommandId(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase, s.CurrentTick, s.NextSubmissionSequence, null, command));
    private static readonly string[] Acts = ["act.meadow-lanterns", "act.overdue-library-books", "act.glitter-rota"];
    private static GameSession Restore(GameSession s)
    {
        var result = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, result.Session!.CaptureSnapshot().AuthoritativeHash);
        return result.Session;
    }
    [TestMethod]
    public void MalformedProgrammeAndOutOfWindowLiveStateAreRejectedWithoutThrowing()
    {
        var s = BuildSession.Drafted(20260926);
        var saved = s.CapturePersistenceSnapshot();
        Assert.IsFalse(GameSession.Restore(saved with { Preparation = saved.Preparation! with { People = null! } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { Preparation = saved.Preparation! with { AcceptedOffers = null! } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(BuildSession.WithMainProgramme(saved, q => q with { Performers = [null!] })).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { Programme = saved.Programme! with { Version = 6 } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { Programme = saved.Programme! with { Stages = [] } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(BuildSession.WithMainProgramme(saved, q => q with { StageId = "stage.elsewhere" })).IsSuccess);
        Assert.IsTrue(Send(s, new SetProgrammeCommand(Acts)).IsAccepted);
        foreach (var hire in BuildSession.Crew(s)) Assert.IsTrue(Send(s, hire).IsAccepted);
        Assert.IsTrue(Send(s, new StartPreparedEditionCommand()).IsAccepted);
        saved = s.CapturePersistenceSnapshot();
        Assert.IsFalse(GameSession.Restore(BuildSession.WithMainLive(saved, live => live with { PlannedTick = 1201 })).IsSuccess);
        Assert.IsFalse(GameSession.Restore(BuildSession.WithMainLive(saved, live => live with { StageId = "stage.elsewhere" })).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { LivePerformances = [] }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(BuildSession.WithMainProgramme(saved with { Preparation = saved.Preparation! with { Status = PreparationStatus.Preparing } }, q => q with { CurrentSlot = -1, SlotEndTick = -1 })).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { Preparation = saved.Preparation! with { Status = PreparationStatus.Departing }, Phase = (int)SessionPhase.Egress }).IsSuccess);
    }
    [TestMethod]
    public void IncomingCareRouteIsRetainedAndMissedSlotCannotExtendItsDeadline()
    {
        var s = BuildSession.Drafted(20260926);
        Assert.IsTrue(Send(s, new SetProgrammeCommand(Acts)).IsAccepted);
        foreach (var hire in BuildSession.Crew(s)) Assert.IsTrue(Send(s, hire).IsAccepted);
        Assert.IsTrue(Send(s, new StartPreparedEditionCommand()).IsAccepted);
        // Bounded incoming-patient and late-clock fixtures; no physical-run evidence comes from these hooks.
        var performer = s.CaptureProgramme()!.Performers.First(person => person.SlotIndex == 1).AgentId;
        typeof(GameSession).GetProperty("MedicalView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s,
            s.CaptureMedical()! with { Needs = s.CaptureMedical()!.Needs.Select(need => need.AgentId == performer ? need with { Intent = MedicalIntent.Rest, HeatExposure = 8000 } : need).ToArray() });
        typeof(GameSession).GetMethod("ApplyAgentDestination", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, [new EntityId(performer), new SetAgentDestinationCommand(GameSession.MedicalRestCell, "medical.rest"), false]);
        typeof(GameSession).GetProperty(nameof(GameSession.CurrentTick))!.SetValue(s, 16_000L);
        // The first set is over before the second is called: it never got going, and is written down as missed.
        typeof(GameSession).GetMethod("FinishLivePerformance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, null);
        BuildSession.SetMainProgramme(s, s.CaptureProgramme()! with { CurrentSlot = 1 });
        typeof(GameSession).GetMethod("StartLivePerformance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, null);
        Assert.AreEqual("medical.rest", s.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id.Value == performer).IntentId);
        Restore(s);
        typeof(GameSession).GetProperty(nameof(GameSession.CurrentTick))!.SetValue(s, 25_000L);
        typeof(GameSession).GetMethod("StartLivePerformance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, null);
        typeof(GameSession).GetMethod("AdvanceLivePerformance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, null);
        Assert.AreEqual(LiveSetStage.Finished, s.CaptureLivePerformance()!.Stage);
        Assert.AreEqual(24_800L, s.CaptureLivePerformance()!.EndedTick);
        Assert.IsFalse(s.CaptureSnapshot().NavigationAgents.Any(agent => agent.IntentId == "performance.stage-exit-stair"));
        Restore(s);
    }
    [TestMethod]
    public void AfterTheirSetTheBandWalkOffDownTheStairsBeforeDoingAnythingElse()
    {
        var s = BuildSession.WithoutFaults(BuildSession.Started());
        s.AdvanceWithoutSnapshot(GameSession.FestivalSlotEnds[0] - (int)s.CurrentTick + 40);
        var live = s.CaptureLivePerformance()!;
        Assert.AreEqual(LiveSetStage.Finished, live.Stage, "The first set is over.");
        var band = live.Performers.Where(p => p.StairReached).Select(p => p.AgentId).ToArray();
        Assert.AreNotEqual(0, band.Length, "Somebody played.");
        var seen = band.ToDictionary(id => id, _ => new List<string>());
        for (var tick = 0; tick < 2_400; tick += 8)
        {
            foreach (var agent in s.CaptureSnapshot().NavigationAgents.Where(a => seen.ContainsKey(a.Id.Value)))
                if (seen[agent.Id.Value].LastOrDefault() != agent.IntentId) seen[agent.Id.Value].Add(agent.IntentId ?? "");
            s.AdvanceWithoutSnapshot(8);
        }
        foreach (var (id, intents) in seen)
        {
            // Across the deck to the stairs, down them to the access point, and only then free to wander.
            var exit = intents.SkipWhile(i => i != "performance.stage-exit-stair").ToList();
            Assert.IsTrue(exit.Count >= 3, $"{id}: {string.Join(" > ", intents)}");
            CollectionAssert.AreEqual(new[] { "performance.stage-exit-stair", "performance.stage-exit-access", "performance.stage-exit" }, exit.Take(3).ToArray(),
                $"{id}: {string.Join(" > ", intents)}");
        }
    }
    [TestMethod]
    public void DayTimingIsExplicitAndOldTimetableVersionsAreNotSilentlyMigrated()
    {
        var s = BuildSession.Drafted(20260926);
        Assert.AreEqual(38_400, s.PreparedEditionDurationTicks);
        Assert.AreEqual(38400, BuildSession.Drafted(20260926).PreparedEditionDurationTicks);
        CollectionAssert.AreEqual(new[] { 4_800, 16_400, 28_000 }, GameSession.FestivalSlotStarts);
        CollectionAssert.AreEqual(new[] { 13_200, 24_800, 37_600 }, GameSession.FestivalSlotEnds);
        Assert.AreEqual(5, s.CaptureProgrammes()!.Version);
        var saved = s.CapturePersistenceSnapshot();
        var old = GameSession.Restore(saved with { Programme = saved.Programme! with { Version = 1 } });
        Assert.IsFalse(old.IsSuccess);
        StringAssert.Contains(old.Error!, "earlier 480-second timetable");
        var priorDay = GameSession.Restore(saved with { Programme = saved.Programme! with { Version = 2 } });
        Assert.IsFalse(priorDay.IsSuccess);
        StringAssert.Contains(priorDay.Error!, "earlier 160-second timetable");
        var priorFiveMinuteDay = GameSession.Restore(saved with { Programme = saved.Programme! with { Version = 3 } });
        Assert.IsFalse(priorFiveMinuteDay.IsSuccess);
        StringAssert.Contains(priorFiveMinuteDay.Error!, "earlier 300-second timetable");
        var singleStage = GameSession.Restore(saved with { Programme = saved.Programme! with { Version = 4 } });
        Assert.IsFalse(singleStage.IsSuccess);
        StringAssert.Contains(singleStage.Error!, "single-stage programme");
        Restore(BuildSession.Drafted(20260926));
    }
    [TestCategory("Slow")]
    [TestMethod]
    [DataRow(20260926UL, true)]
    [DataRow(20260922UL, false)]
    public void ThreeFixedSlotsPhysicallyRunAndFullRosterRemainsOnFarm(ulong seed, bool maintenance)
    {
        var s = BuildSession.Drafted(seed);
        Assert.IsTrue(Send(s, new SetProgrammeCommand(seed == 20260922 ? ["act.meadow-lanterns", "act.glitter-rota", "act.low-battery"] : Acts)).IsAccepted);
        foreach (var id in BuildSession.CrewIds(s).Concat(new[] { "equipment.rent" }).Concat(maintenance ? ["maintenance.worker"] : Array.Empty<string>())) Assert.IsTrue(Send(s, new AcceptPreparationOfferCommand(id)).IsAccepted);
        Assert.IsTrue(Send(s, new StartPreparedEditionCommand()).IsAccepted);
        // About the stage schedule: no random toilet or tap faults reshuffling the day.
        s = Restore(BuildSession.WithoutFaults(s));
        var ids = s.CapturePreparation()!.People.Select(p => p.AgentId).ToArray();
        Assert.AreEqual(maintenance ? 33 : 32, ids.Length);
        var played = new HashSet<int>();
        var restoredSlots = new HashSet<int>();
        var playedPeople = new HashSet<ulong>();
        var liveTicks = new int[3];
        var firstMusicTicks = new long[] { -1, -1, -1 };
        var guided = false;
        while (s.CurrentTick < GameSession.PreparedDayTicks)
        {
            s.AdvanceWithoutSnapshot(1);
            if (!guided && s.CapturePreparation()!.People.Single(person => person.AgentId == BuildSession.LastGuest(s)).Admitted)
                guided = Send(s, new StaffInterventionCommand(BuildSession.LastGuest(s), s.CaptureMedical()!.MedicId, StaffInterventionAction.GuideToRest)).IsAccepted;
            if (s.CurrentTick % 80 == 0)
            {
                if (s.CaptureEquipment()!.LoadPercent > 80) Send(s, new EquipmentCommand(EquipmentAction.ShedLoad));
                foreach (var need in s.CaptureMedical()!.Needs.Where(need => need.Stage is MedicalStage.Distress or MedicalStage.Collapsed or MedicalStage.Critical))
                    Send(s, new MedicalCommand(need.AgentId, MedicalAction.DispatchMedic));
            }
            var q = s.CaptureProgramme()!;
            var live = s.CaptureLivePerformance()!;
            if (s.CurrentTick % 80 == 0)
            {
                // A medic answering a performer's collapse may cross the stage; only the band counts here.
                var stagePeople = s.CaptureSnapshot().NavigationAgents.Where(agent => agent.IntentId != "medical.dispatch").Where(agent =>
                {
                    var cell = TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres);
                    return cell.X is >= 92 and <= 98 && cell.Z is >= 143 and <= 157;
                }).Select(agent => agent.Id.Value).ToArray();
                Assert.IsTrue(stagePeople.Length <= 3, $"Physical band overlap at {s.CurrentTick}: {string.Join(',', stagePeople)}");
                if (live.Stage == LiveSetStage.Live) Assert.IsTrue(stagePeople.All(id => live.Performers.Any(person => person.AgentId == id)));
            }
            if (live.Stage == LiveSetStage.Live)
            {
                played.Add(q.CurrentSlot);
                liveTicks[q.CurrentSlot]++;
                if (firstMusicTicks[q.CurrentSlot] < 0) firstMusicTicks[q.CurrentSlot] = s.CurrentTick;
                foreach (var person in live.Performers) playedPeople.Add(person.AgentId);
                Assert.IsTrue(s.CurrentTick >= GameSession.FestivalSlotStarts[q.CurrentSlot] && s.CurrentTick < GameSession.FestivalSlotEnds[q.CurrentSlot]);
                Assert.IsTrue(live.Performers.All(p => p.OnStage), $"tick={s.CurrentTick} slot={q.CurrentSlot} performers={string.Join(';', live.Performers.Select(person => $"{person.AgentId}:{person.OnStage}"))}");
                if (restoredSlots.Add(q.CurrentSlot)) s = Restore(s);
            }
            if (s.CurrentTick == 14_000 || s.CurrentTick == 25_600)
            {
                var continued = s;
                s = Restore(s);
                continued.AdvanceWithoutSnapshot(80);
                s.AdvanceWithoutSnapshot(80);
                Assert.AreEqual(continued.CaptureSnapshot().AuthoritativeHash, s.CaptureSnapshot().AuthoritativeHash);
            }
            if (s.PreparedStatus == PreparationStatus.Failed)
            {
                Console.WriteLine($"Failed at {s.CurrentTick}: {s.CaptureMedical()!.Medics[0].Description}");
                foreach (var need in s.CaptureMedical()!.Needs) Console.WriteLine($"{need.AgentId} {need.Profile} {need.Thirst}/{need.HeatExposure} {need.Stage} {need.Intent}");
                break;
            }
        }
        Console.WriteLine($"Played {string.Join(',', played)}; music starts={string.Join(',', firstMusicTicks)}; actual music ticks={string.Join(',', liveTicks)}; nine stage identities={playedPeople.Count}; tick={s.CurrentTick}; state={s.PreparedStatus}; programme={s.CaptureProgramme()!.Status}");
        Assert.AreEqual(3, played.Count);
        Assert.AreEqual(9, playedPeople.Count);
        Assert.IsTrue(liveTicks.All(ticks => ticks > 0 && ticks <= GameSession.FestivalSlotDurationTicks));
        Assert.AreEqual(GameSession.PreparedDayTicks, s.CurrentTick);
        Assert.IsTrue(s.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing);
        CollectionAssert.AreEqual(ids, s.CapturePreparation()!.People.Select(p => p.AgentId).ToArray());
        Assert.AreEqual(0, s.CaptureLifecycleSnapshot()!.Casualties.Count);
        s.AdvanceWithoutSnapshot(4000);
        Assert.AreEqual(PreparationStatus.Finished, s.PreparedStatus);
        Assert.IsTrue(s.CapturePreparation()!.People.All(p => p.Departed));
        Restore(s);
    }
}

using Festival.Simulation;
using System.Reflection;
using System.Collections;
using System.Diagnostics;

namespace Festival.Tests;

[TestClass]
public sealed class FestivalProgrammeTests
{
    private static CommandResult Send(GameSession s, SessionCommand command) => s.Execute(new(new CommandId(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase, s.CurrentTick, s.NextSubmissionSequence, null, command));
    private static readonly string[] Acts = ["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"];
    private static GameSession Restore(GameSession s)
    {
        var result = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, result.Session!.CaptureSnapshot().AuthoritativeHash);
        return result.Session;
    }
    [TestMethod]
    public void AtomicBookingsReorderWithoutDuplicateFeesAndRejectReplacement()
    {
        var s = GameSession.CreateTimetableCampaign(20260926);
        Restore(s);
        var hash = s.CaptureSnapshot().AuthoritativeHash;
        Assert.IsFalse(Send(s, new SetProgrammeCommand([Acts[0], Acts[0], Acts[2]])).IsAccepted);
        Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash);
        Assert.IsTrue(Send(s, new SetProgrammeCommand(Acts)).IsAccepted);
        var p = s.CapturePreparation()!;
        Assert.AreEqual(3, p.Payments.Length);
        Assert.AreEqual(20500, p.Payments.Sum(payment => payment.AmountPennies));
        Assert.IsTrue(Send(s, new SetProgrammeCommand(Acts.Reverse().ToArray())).IsAccepted);
        Assert.AreEqual(3, s.CapturePreparation()!.Payments.Length);
        Assert.IsFalse(Send(s, new SetProgrammeCommand([Acts[0], Acts[1], "act.field-frequency"])).IsAccepted);
        Restore(s);
        var saved = s.CapturePersistenceSnapshot();
        Assert.IsFalse(GameSession.Restore(saved with { Programme = saved.Programme! with { ActIds = [Acts[0], Acts[0], Acts[2]] } }).IsSuccess);
    }
    [TestMethod]
    public void InsufficientFundsAndLateProgrammeCommandsLeaveStateUnchanged()
    {
        var s = GameSession.CreateTimetableCampaign(20260926);
        // Labelled low-cash failure-path fixture, not a normal preparation route.
        var finances = (IDictionary)typeof(GameSession).GetField("_festivalFinances", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
        var finance = finances.Values.Cast<object>().Single();
        finance.GetType().GetProperty("CashPennies")!.SetValue(finance, 1000L);
        var before = s.CaptureSnapshot().AuthoritativeHash;
        Assert.IsFalse(Send(s, new SetProgrammeCommand(Acts)).IsAccepted);
        Assert.AreEqual(before, s.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(0, s.CapturePreparation()!.Payments.Length);
        s = GameSession.CreateTimetableCampaign(20260926);
        Assert.IsTrue(Send(s, new SetProgrammeCommand(Acts)).IsAccepted);
        Assert.IsTrue(Send(s, new AcceptPreparationOfferCommand("staff.steward")).IsAccepted);
        Assert.IsTrue(Send(s, new StartPreparedEditionCommand()).IsAccepted);
        before = s.CaptureSnapshot().AuthoritativeHash;
        Assert.IsFalse(Send(s, new SetProgrammeCommand(Acts.Reverse().ToArray())).IsAccepted);
        Assert.AreEqual(before, s.CaptureSnapshot().AuthoritativeHash);
    }
    [TestMethod]
    public void MalformedProgrammeAndOutOfWindowLiveStateAreRejectedWithoutThrowing()
    {
        var s = GameSession.CreateTimetableCampaign(20260926);
        var saved = s.CapturePersistenceSnapshot();
        Assert.IsFalse(GameSession.Restore(saved with { Preparation = saved.Preparation! with { People = null! } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { Preparation = saved.Preparation! with { AcceptedOffers = null! } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { Programme = saved.Programme! with { Performers = [null!] } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { Programme = saved.Programme! with { Version = 4 } }).IsSuccess);
        Assert.IsTrue(Send(s, new SetProgrammeCommand(Acts)).IsAccepted);
        Assert.IsTrue(Send(s, new AcceptPreparationOfferCommand("staff.steward")).IsAccepted);
        Assert.IsTrue(Send(s, new StartPreparedEditionCommand()).IsAccepted);
        saved = s.CapturePersistenceSnapshot();
        Assert.IsFalse(GameSession.Restore(saved with { LivePerformance = saved.LivePerformance! with { PlannedTick = 1201 } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { Preparation = saved.Preparation! with { Status = PreparationStatus.Preparing }, Programme = saved.Programme! with { CurrentSlot = -1, SlotEndTick = -1 } }).IsSuccess);
        Assert.IsFalse(GameSession.Restore(saved with { Preparation = saved.Preparation! with { Status = PreparationStatus.Departing }, Phase = (int)SessionPhase.Egress }).IsSuccess);
    }
    [TestMethod]
    public void IncomingCareRouteIsRetainedAndMissedSlotCannotExtendItsDeadline()
    {
        var s = GameSession.CreateTimetableCampaign(20260926);
        Assert.IsTrue(Send(s, new SetProgrammeCommand(Acts)).IsAccepted);
        Assert.IsTrue(Send(s, new AcceptPreparationOfferCommand("staff.steward")).IsAccepted);
        Assert.IsTrue(Send(s, new StartPreparedEditionCommand()).IsAccepted);
        // Bounded incoming-patient and late-clock fixtures; no physical-run evidence comes from these hooks.
        var performer = s.CaptureProgramme()!.Performers.First(person => person.SlotIndex == 1).AgentId;
        typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s,
            s.CaptureMedical()! with { Needs = s.CaptureMedical()!.Needs.Select(need => need.AgentId == performer ? need with { Intent = MedicalIntent.Rest, HeatExposure = 8000 } : need).ToArray() });
        typeof(GameSession).GetMethod("ApplyAgentDestination", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, [new EntityId(performer), new SetAgentDestinationCommand(GameSession.MedicalRestCell, "medical.rest"), false]);
        typeof(GameSession).GetProperty(nameof(GameSession.CurrentTick))!.SetValue(s, 8560L);
        typeof(GameSession).GetField("_programme", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s, s.CaptureProgramme()! with { CurrentSlot = 1 });
        typeof(GameSession).GetMethod("StartLivePerformance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, null);
        Assert.AreEqual("medical.rest", s.CaptureSnapshot().NavigationAgents.Single(agent => agent.Id.Value == performer).IntentId);
        Restore(s);
        typeof(GameSession).GetProperty(nameof(GameSession.CurrentTick))!.SetValue(s, 15400L);
        typeof(GameSession).GetMethod("StartLivePerformance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, null);
        typeof(GameSession).GetMethod("AdvanceLivePerformance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s, null);
        Assert.AreEqual(LiveSetStage.Finished, s.CaptureLivePerformance()!.Stage);
        Assert.AreEqual(15200L, s.CaptureLivePerformance()!.EndedTick);
        Assert.IsFalse(s.CaptureSnapshot().NavigationAgents.Any(agent => agent.IntentId == "performance.stage-exit-stair"));
        Restore(s);
    }
    [TestMethod]
    public void DayTimingIsExplicitAndOldTimetableVersionsAreNotSilentlyMigrated()
    {
        var s = GameSession.CreateTimetableCampaign(20260926);
        Assert.AreEqual(24000, s.PreparedEditionDurationTicks);
        Assert.AreEqual(38400, GameSession.CreatePreparedCampaign(20260926).PreparedEditionDurationTicks);
        CollectionAssert.AreEqual(new[] { 1200, 9200, 17200 }, GameSession.FestivalSlotStarts);
        CollectionAssert.AreEqual(new[] { 7200, 15200, 23200 }, GameSession.FestivalSlotEnds);
        Assert.AreEqual(3, s.CaptureProgramme()!.Version);
        var saved = s.CapturePersistenceSnapshot();
        var old = GameSession.Restore(saved with { Programme = saved.Programme! with { Version = 1 } });
        Assert.IsFalse(old.IsSuccess);
        StringAssert.Contains(old.Error!, "earlier 480-second timetable");
        var priorDay = GameSession.Restore(saved with { Programme = saved.Programme! with { Version = 2 } });
        Assert.IsFalse(priorDay.IsSuccess);
        StringAssert.Contains(priorDay.Error!, "earlier 160-second timetable");
        Restore(GameSession.CreatePreparedCampaign(20260926));
    }
    [TestMethod]
    public void FullStaffCapacityHasThirtyFiveDistinctPhysicalPeople()
    {
        var s = GameSession.CreateTimetableCampaign(20260922);
        foreach (var effect in new[] { "staff.medic-slot", "staff.steward-slot" }) Assert.IsTrue(Send(s, new ApplyStaffFoundationEffectCommand(effect)).IsAccepted);
        Assert.IsTrue(Send(s, new SetProgrammeCommand(Acts)).IsAccepted);
        foreach (var offer in new[] { "staff.steward", "maintenance.worker", "staff.extra-medic", "staff.extra-steward" }) Assert.IsTrue(Send(s, new AcceptPreparationOfferCommand(offer)).IsAccepted);
        Assert.AreEqual(35, s.CapturePreparation()!.People.Length);
        s = Restore(s);
        Assert.IsTrue(Send(s, new StartPreparedEditionCommand()).IsAccepted);
        Assert.AreEqual(35, s.CaptureSnapshot().NavigationAgents.Count);
        Assert.AreEqual(35, s.CaptureSnapshot().NavigationAgents.Select(agent => agent.Id).Distinct().Count());
        var probe = new ScaleDiagnosticProbe();
        s.ScaleDiagnosticProbe = probe;
        var maximumBlockedPeople = 0;
        var openingWatch = Stopwatch.StartNew();
        for (var tick = 0; tick < 2000; tick++)
        {
            s.AdvanceWithoutSnapshot(1);
            maximumBlockedPeople = Math.Max(maximumBlockedPeople, probe.CurrentlyBlockedAgents);
        }
        openingWatch.Stop();
        Console.WriteLine($"35-person opening: ticks=2000, elapsedMs={openingWatch.Elapsed.TotalMilliseconds:F1}, max blocked people={maximumBlockedPeople}, routes={probe.RouteSearches}, maximum blocked age={probe.MaximumBlockedAgentAgeTicks}");
        Assert.IsTrue(maximumBlockedPeople <= 35);
        Restore(s);
    }
    [TestMethod]
    public void SameTierRetryKeepsNineIdentitiesAndTastesButClearsProgrammeAndContracts()
    {
        var s = GameSession.CreateTimetableCampaign(20260926);
        var people = s.CapturePreparation()!.People;
        Assert.IsTrue(Send(s, new SetProgrammeCommand(Acts)).IsAccepted);
        foreach (var offer in new[] { "staff.steward", "equipment.buy" }) Assert.IsTrue(Send(s, new AcceptPreparationOfferCommand(offer)).IsAccepted);
        Assert.IsTrue(Send(s, new StartPreparedEditionCommand()).IsAccepted);
        s.AdvanceWithoutSnapshot(6200); // Labelled untreated medical failure, using ordinary hazards.
        Assert.AreEqual(PreparationStatus.Failed, s.PreparedStatus);
        s = Restore(s);
        Assert.IsTrue(Send(s, new SpendCouncilFavourCommand()).IsAccepted);
        var retry = s.CapturePreparation()!;
        Assert.AreEqual(0, s.CaptureProgramme()!.ActIds.Length);
        Assert.AreEqual(-1, s.CaptureProgramme()!.CurrentSlot);
        Assert.AreEqual(0, retry.WorkContracts.Length);
        CollectionAssert.AreEqual(people.Select(person => (person.AgentId, person.Name, person.ExpectedGenre)).ToArray(), retry.People.Select(person => (person.AgentId, person.Name, person.ExpectedGenre)).ToArray());
        Assert.IsTrue(retry.OwnedEquipment.Contains("sound-rig"));
        Assert.IsFalse(Send(s, new StartPreparedEditionCommand()).IsAccepted);
        Assert.IsTrue(Send(s, new SetProgrammeCommand(Acts.Reverse().ToArray())).IsAccepted);
        Assert.AreEqual(3, s.CapturePreparation()!.Payments.Count(payment => payment.Attempt == 2));
        Restore(s);
    }
    [TestMethod]
    [DataRow(20260926UL, true)]
    [DataRow(20260922UL, false)]
    public void ThreeFixedSlotsPhysicallyRunAndFullRosterRemainsOnFarm(ulong seed, bool maintenance)
    {
        var s = GameSession.CreateTimetableCampaign(seed);
        Assert.IsTrue(Send(s, new SetProgrammeCommand(seed == 20260922 ? ["act.meadow-lanterns", "act.neon-postcards", "act.field-frequency"] : Acts)).IsAccepted);
        foreach (var id in new[] { "staff.steward", "equipment.buy" }.Concat(maintenance ? ["maintenance.worker"] : Array.Empty<string>())) Assert.IsTrue(Send(s, new AcceptPreparationOfferCommand(id)).IsAccepted);
        Assert.IsTrue(Send(s, new StartPreparedEditionCommand()).IsAccepted);
        s = Restore(s);
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
            if (!guided && s.CapturePreparation()!.People.Single(person => person.AgentId == s.CaptureMedical()!.AtRiskGuestId).Admitted)
                guided = Send(s, new StaffInterventionCommand(s.CaptureMedical()!.AtRiskGuestId, s.CaptureMedical()!.MedicId, StaffInterventionAction.GuideToRest)).IsAccepted;
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
                var stagePeople = s.CaptureSnapshot().NavigationAgents.Where(agent =>
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
            if (s.CurrentTick == 8000 || s.CurrentTick == 16000)
            {
                var continued = s;
                s = Restore(s);
                continued.AdvanceWithoutSnapshot(80);
                s.AdvanceWithoutSnapshot(80);
                Assert.AreEqual(continued.CaptureSnapshot().AuthoritativeHash, s.CaptureSnapshot().AuthoritativeHash);
            }
            if (s.PreparedStatus == PreparationStatus.Failed)
            {
                Console.WriteLine($"Failed at {s.CurrentTick}: {s.CaptureMedical()!.Response}");
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

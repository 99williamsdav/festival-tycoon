using Festival.Simulation;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class MedicalIncidentTests
{
    private static CommandResult Send(GameSession s, SessionCommand command) => s.Execute(new(
        new CommandId(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase, s.CurrentTick,
        s.NextSubmissionSequence, null, command));

    private static CommandResult SendTo(GameSession s, ulong id, SessionCommand command) => s.Execute(new(
        new CommandId(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase, s.CurrentTick,
        s.NextSubmissionSequence, new EntityId(id), command));

    private static GameSession Started(ulong seed = 20260922, int tier = 1)
    {
        var s = GameSession.CreateMedicalCampaign(seed, tier);
        foreach (var offer in new[] { "act.folk", "staff.steward", "equipment.buy" })
            Assert.IsTrue(Send(s, new AcceptPreparationOfferCommand(offer)).IsAccepted);
        Assert.IsTrue(Send(s, new StartPreparedEditionCommand()).IsAccepted);
        return s;
    }

    private static GameSession Restored(GameSession s)
    {
        var loaded = GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(loaded.IsSuccess, loaded.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash, loaded.Session!.CaptureSnapshot().AuthoritativeHash);
        return loaded.Session;
    }

    private static void SuppressGuestWaterDemand(GameSession s)
    {
        var field = typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var m = s.CaptureMedical()!;
        field.SetValue(s, m with { Needs = m.Needs.Select(item => item.Profile == MedicalNeedProfile.Guest
            ? item with { Thirst = 0, HeatExposure = 0 } : item).ToArray() });
    }

    [TestMethod]
    public void UntreatedHotScenarioHasCausalWarningCollapseCriticalAndOneDeath()
    {
        var s = Started();
        s.AdvanceWithoutSnapshot(6_200);
        var m = s.CaptureMedical()!;
        Console.WriteLine($"stage={m.Stage} warning={m.WarningTick} collapse={m.CollapseTick} critical={m.CriticalTick} tick={s.CurrentTick} queue={m.WaterQueue.Length}");
        foreach (var item in m.Evidence) Console.WriteLine($"{item.Tick} {item.Id} {item.Description}");
        Assert.AreEqual(MedicalStage.Terminal, m.Stage);
        Assert.AreEqual(1, s.CaptureLifecycleSnapshot()!.Casualties.Count);
        Restored(s);
    }

    [TestMethod]
    public void EarlyFreeWaterAndTimelyMedicAreCounterfactualPreventions()
    {
        foreach (var action in new[] { MedicalAction.GuideToWater, MedicalAction.DispatchMedic, MedicalAction.SafeRemove, MedicalAction.GuideToRest })
        {
            var s = Started();
            var target = s.CaptureMedical()!.AtRiskGuestId;
            if (action is MedicalAction.DispatchMedic or MedicalAction.SafeRemove)
            {
                while (s.CaptureMedical()!.Stage != MedicalStage.Distress && s.CurrentTick < 4_000)
                    s.AdvanceWithoutSnapshot(1);
                Assert.AreEqual(MedicalStage.Distress, s.CaptureMedical()!.Stage);
            }
            Assert.IsTrue(Send(s, new MedicalCommand(target, action)).IsAccepted, action.ToString());
            if (action == MedicalAction.SafeRemove)
            {
                var interrupted = Send(s, new MedicalCommand(target, MedicalAction.GuideToWater));
                Assert.IsFalse(interrupted.IsAccepted);
                StringAssert.Contains(interrupted.Message, "response owns this guest");
            }
            s = Restored(s);
            s.AdvanceWithoutSnapshot(6_200 - checked((int)s.CurrentTick));
            Console.WriteLine($"action={action} stage={s.CaptureMedical()!.Stage} response={s.CaptureMedical()!.Response} tick={s.CurrentTick}");
            if (s.CaptureMedical()!.Stage == MedicalStage.Terminal)
            {
                foreach (var item in s.CaptureMedical()!.Evidence) Console.WriteLine($"{item.Tick} {item.Id} {item.Description}");
                var nav = s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == s.CaptureMedical()!.MedicId);
                Console.WriteLine($"medic nav={nav.Action} intent={nav.IntentId} destination={nav.Destination} position={nav.XMillimetres},{nav.ZMillimetres}");
            }
            Assert.AreEqual(0, s.CaptureLifecycleSnapshot()!.Casualties.Count);
            Assert.IsTrue(s.CaptureMedical()!.Stage is MedicalStage.Clear or MedicalStage.Treated or MedicalStage.Removed);
            if (action == MedicalAction.SafeRemove)
                Assert.IsTrue(s.CapturePreparation()!.People.Single(item => item.AgentId == target).Departed);
            if (action == MedicalAction.DispatchMedic)
                Assert.AreEqual(MedicalIntent.WatchShow, s.CaptureMedical()!.Needs.Single(item => item.AgentId == target).Intent);
            Restored(s);
        }
    }

    [TestMethod]
    public void QueueAbandonmentReassignsReservationsAndDoesNotTransferWater()
    {
        var s = Started();
        while (s.CaptureMedical()!.WaterQueue.Length < 2 && s.CurrentTick < 3_000)
            s.AdvanceWithoutSnapshot(1);
        var before = s.CaptureMedical()!;
        Assert.IsTrue(before.WaterQueue.Length >= 2);
        var leaver = before.WaterQueue[0];
        Assert.IsTrue(Send(s, new MedicalCommand(leaver, MedicalAction.ReturnToShow)).IsAccepted);
        var after = s.CaptureMedical()!;
        Assert.IsFalse(after.WaterQueue.Contains(leaver));
        Assert.IsFalse(after.WaterOwnerId == leaver);
        Assert.AreEqual(before.WaterQueue.Length - 1, after.WaterQueue.Length);
        for (var index = 0; index < after.WaterQueue.Length; index++)
            Assert.AreEqual(index, after.Needs.Single(item => item.AgentId == after.WaterQueue[index]).QueueSlot);
        Assert.AreEqual(-1L, after.Needs.Single(item => item.AgentId == leaver).LastWaterTick);
        Restored(s);
    }

    [TestMethod]
    public void WarningTravelTreatmentAndCriticalRestoreWithoutChangingOutcome()
    {
        var untreated = Started();
        while (untreated.CaptureMedical()!.Stage == MedicalStage.Clear && untreated.CurrentTick < 4_000)
            untreated.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalStage.Distress, untreated.CaptureMedical()!.Stage);
        untreated = Restored(untreated);
        var warningTick = untreated.CaptureMedical()!.WarningTick;
        var treated = Restored(untreated);
        Assert.IsTrue(Send(treated, new MedicalCommand(treated.CaptureMedical()!.AtRiskGuestId, MedicalAction.DispatchMedic)).IsAccepted);
        Assert.AreEqual(MedicalResponseStage.Travelling, treated.CaptureMedical()!.ResponseStage);
        var busy = Send(treated, new MedicalCommand(treated.CaptureMedical()!.AtRiskGuestId, MedicalAction.DispatchMedic));
        Assert.IsFalse(busy.IsAccepted);
        StringAssert.Contains(busy.Message, "already owns this response");
        treated = Restored(treated);
        while (treated.CaptureMedical()!.ResponseStage == MedicalResponseStage.Travelling &&
               treated.CaptureMedical()!.Stage != MedicalStage.Terminal &&
               treated.CurrentTick < warningTick + GameSession.MedicalCollapseDelayTicks + GameSession.MedicalDeathDelayTicks)
            treated.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalResponseStage.Treating, treated.CaptureMedical()!.ResponseStage);
        treated = Restored(treated);
        treated.AdvanceWithoutSnapshot(GameSession.MedicalTreatmentTicks + 1);
        Assert.AreEqual(MedicalStage.Treated, treated.CaptureMedical()!.Stage);
        Assert.AreEqual(0, treated.CaptureLifecycleSnapshot()!.Casualties.Count);

        while ((untreated.CaptureMedical()!.Stage is MedicalStage.Distress or MedicalStage.Collapsed) && untreated.CurrentTick < 6_000)
            untreated.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalStage.Critical, untreated.CaptureMedical()!.Stage);
        untreated = Restored(untreated);
        Assert.AreEqual(warningTick + GameSession.MedicalCollapseDelayTicks + GameSession.MedicalCriticalDelayTicks,
            untreated.CaptureMedical()!.CriticalTick);
        untreated.AdvanceWithoutSnapshot(GameSession.MedicalDeathDelayTicks);
        Assert.AreEqual(MedicalStage.Terminal, untreated.CaptureMedical()!.Stage);
        var frozenHash = untreated.CaptureSnapshot().AuthoritativeHash;
        var frozenTick = untreated.CurrentTick;
        untreated.AdvanceWithoutSnapshot(100);
        Assert.AreEqual(frozenTick, untreated.CurrentTick);
        Assert.AreEqual(frozenHash, untreated.CaptureSnapshot().AuthoritativeHash);
        Assert.AreEqual(1, untreated.CaptureMedical()!.Evidence.Count(item => item.Id == "medical:death"));
        Assert.AreEqual(1, untreated.CaptureLifecycleSnapshot()!.Casualties.Count);
    }

    [TestMethod]
    public void BlockedMedicApproachGivesActionableCauseBeforeCommandAcceptance()
    {
        var s = Started();
        s.AdvanceWithoutSnapshot(2_000);
        var medical = s.CaptureMedical()!;
        Assert.AreEqual(MedicalStage.Distress, medical.Stage);
        var patient = s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == medical.AtRiskGuestId);
        var cell = TraversalGrid.WorldToCell(patient.XMillimetres, patient.ZMillimetres);
        var field = typeof(GameSession).GetField("_traversalGrid", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var grid = (TraversalGrid)field.GetValue(s)!;
        var overrides = grid.Overrides.Values.ToDictionary(item => item.Cell);
        foreach (var (dx, dz) in new (int X, int Z)[] { (4, 0), (-4, 0), (0, 4), (0, -4),
                     (3, 2), (-3, 2), (3, -2), (-3, -2), (2, 3), (-2, 3), (2, -3), (-2, -3) })
        {
            var blocked = new GridCell(cell.X + dx, cell.Z + dz);
            overrides[blocked] = new TerrainCellOverride(blocked, GroundSurface.Grass, false);
        }
        field.SetValue(s, new TraversalGrid(overrides.Values));
        var envelope = new CommandEnvelope(new CommandId(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase,
            s.CurrentTick, s.NextSubmissionSequence, null, new MedicalCommand(medical.AtRiskGuestId, MedicalAction.DispatchMedic));
        var result = s.ValidateCommand(envelope);
        Assert.IsNotNull(result);
        StringAssert.Contains(result.Message, "cannot reach a walkable position");
    }

    [TestMethod]
    public void OwnedResponseRejectsPatientAndMedicRetargetsWithoutQueueLeak()
    {
        var s = Started();
        s.AdvanceWithoutSnapshot(2_000);
        var m = s.CaptureMedical()!;
        Assert.IsTrue(Send(s, new MedicalCommand(m.AtRiskGuestId, MedicalAction.DispatchMedic)).IsAccepted);
        Assert.IsFalse(s.CaptureMedical()!.WaterQueue.Contains(m.AtRiskGuestId));
        var hash = s.CaptureSnapshot().AuthoritativeHash;
        foreach (var action in new[] { MedicalAction.GuideToWater, MedicalAction.GuideToRest,
                     MedicalAction.ReturnToShow, MedicalAction.SafeRemove })
        {
            var rejected = Send(s, new MedicalCommand(m.AtRiskGuestId, action));
            Assert.IsFalse(rejected.IsAccepted, action.ToString());
            StringAssert.Contains(rejected.Message, "response owns this guest");
        }
        foreach (var id in new[] { m.AtRiskGuestId, m.MedicId })
        {
            var rejected = SendTo(s, id, new SetAgentDestinationCommand(GameSession.MedicalExitCell, "fixture.retarget"));
            Assert.IsFalse(rejected.IsAccepted);
            StringAssert.Contains(rejected.Message, "unavailable in prepared editions");
        }
        Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash);
        s.AdvanceWithoutSnapshot(2_000);
        Assert.AreEqual(MedicalStage.Treated, s.CaptureMedical()!.Stage);
        Assert.IsFalse(s.CaptureMedical()!.WaterQueue.Contains(m.AtRiskGuestId));
    }

    [TestMethod]
    public void OrdinaryGuestCannotEnterUnsupportedRestHold()
    {
        var s = Started();
        var m = s.CaptureMedical()!;
        var ordinary = m.Needs.First(item => item.AgentId != m.AtRiskGuestId).AgentId;
        var hash = s.CaptureSnapshot().AuthoritativeHash;
        var rejected = Send(s, new MedicalCommand(ordinary, MedicalAction.GuideToRest));
        Assert.IsFalse(rejected.IsAccepted);
        StringAssert.Contains(rejected.Message, "guide other guests to free water");
        Assert.AreEqual(hash, s.CaptureSnapshot().AuthoritativeHash);
        Assert.AreNotEqual(MedicalIntent.Rest, s.CaptureMedical()!.Needs.Single(item => item.AgentId == ordinary).Intent);
    }

    [TestMethod]
    public void TreatmentCannotCompleteAfterPhysicalSeparation()
    {
        var s = Started();
        s.AdvanceWithoutSnapshot(2_000);
        var target = s.CaptureMedical()!.AtRiskGuestId;
        Assert.IsTrue(Send(s, new MedicalCommand(target, MedicalAction.DispatchMedic)).IsAccepted);
        while (s.CaptureMedical()!.ResponseStage == MedicalResponseStage.Travelling && s.CurrentTick < 4_000)
            s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalResponseStage.Treating, s.CaptureMedical()!.ResponseStage);
        var treatmentHash = s.CaptureSnapshot().AuthoritativeHash;
        foreach (var action in new[] { MedicalAction.GuideToWater, MedicalAction.GuideToRest, MedicalAction.ReturnToShow })
        {
            var rejected = Send(s, new MedicalCommand(target, action));
            Assert.IsFalse(rejected.IsAccepted);
            StringAssert.Contains(rejected.Message, "response owns this guest");
        }
        Assert.AreEqual(treatmentHash, s.CaptureSnapshot().AuthoritativeHash);

        // Inject a one-tick physical separation to prove elapsed time cannot finish remotely.
        var field = typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var agents = field.GetValue(s)!;
        var patient = agents.GetType().GetProperty("Item")!.GetValue(agents, [new EntityId(target)])!;
        var position = patient.GetType().GetProperty("XMillimetres")!;
        position.SetValue(patient, (int)position.GetValue(patient)! + 10_000);
        s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalResponseStage.None, s.CaptureMedical()!.ResponseStage);
        Assert.AreNotEqual(MedicalStage.Treated, s.CaptureMedical()!.Stage);
        Assert.AreEqual(1, s.CaptureMedical()!.Evidence.Count(item => item.Id == "medical:treatment-interrupted"));
    }

    [TestMethod]
    public void StaggeredWaterSlotsHaveClearanceAndWalkableRoutes()
    {
        var s = Started();
        var slots = Enumerable.Range(0, 10).Select(GameSession.MedicalQueueSlot)
            .Concat(Enumerable.Range(0, 10).Select(index => GameSession.MedicalQueueApproach(10, index))).ToArray();
        Assert.AreEqual(slots.Length, slots.Distinct().Count());
        for (var index = 1; index < slots.Length; index++)
        {
            Assert.IsTrue(slots[index].Z > slots[index - 1].Z, "The line must progress away from the tap without snaking back.");
            var dx = slots[index].X - slots[index - 1].X;
            var dz = slots[index].Z - slots[index - 1].Z;
            Assert.IsTrue(dx * dx + dz * dz <= 8, "Adjacent queue places must remain one continuous line.");
        }
        for (var i = 0; i < slots.Length; i++)
        for (var j = i + 1; j < slots.Length; j++)
        {
            var dx = slots[i].X - slots[j].X;
            var dz = slots[i].Z - slots[j].Z;
            Assert.IsTrue(dx * dx + dz * dz >= 4, $"Slots {i} and {j} overlap standing clearance.");
        }
        var savedGrid = s.CapturePersistenceSnapshot().TraversalGrid!;
        var grid = new TraversalGrid(savedGrid.Cells.Select(item => new TerrainCellOverride(
            new(item.X, item.Z), (GroundSurface)item.Surface, item.IsWalkable,
            item.CostPermille, item.ElevationMillimetres, item.SlopePermille)));
        foreach (var slot in slots)
        {
            Assert.IsTrue(grid.Get(slot).IsWalkable, $"Water slot {slot} is blocked.");
            Assert.IsTrue(DeterministicPathfinder.FindPath(grid, new GridCell(122, 190), slot).Found,
                $"Water slot {slot} has no entrance route.");
        }
        Assert.IsTrue(grid.Get(GameSession.MedicalQueueApproach(slots.Length)).IsWalkable);
    }

    [TestMethod]
    public void FullLineKeepsEarlierPhysicalOverflowArrivalAheadOfLaterFasterWalkerAcrossRestore()
    {
        var s = Started();
        SuppressGuestWaterDemand(s);
        var ids = s.CapturePreparation()!.People.Where(item => item.Role == ProtectedPersonRole.Guest)
            .Take(12).Select(item => item.AgentId).ToArray();
        var medicalField = typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var medical = s.CaptureMedical()!;
        medicalField.SetValue(s, medical with
        {
            WaterQueue = ids.Take(10).ToArray(),
            Needs = medical.Needs.Select(item =>
            {
                var index = Array.IndexOf(ids, item.AgentId);
                return index < 0 ? item : item with { Thirst = 9_500,
                    Intent = index < 10 ? MedicalIntent.SeekWater : MedicalIntent.WatchShow,
                    QueueSlot = index < 10 ? index : null, LastDecisionTick = s.CurrentTick };
            }).ToArray()
        });
        var navigationField = typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var agents = navigationField.GetValue(s)!;
        var applyDestination = typeof(GameSession).GetMethod("ApplyAgentDestination", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Place(ulong id, GridCell cell, int speed)
        {
            var agent = agents.GetType().GetProperty("Item")!.GetValue(agents, [new EntityId(id)])!;
            var centre = TraversalGrid.CellCentre(cell);
            foreach (var (name, value) in new[] { ("XMillimetres", centre.XMillimetres), ("ZMillimetres", centre.ZMillimetres),
                         ("SegmentOriginXMillimetres", centre.XMillimetres), ("SegmentOriginZMillimetres", centre.ZMillimetres),
                         ("WalkingSpeedPermille", speed) })
                agent.GetType().GetProperty(name)!.SetValue(agent, value);
        }
        for (var index = 0; index < 10; index++)
        {
            Place(ids[index], GameSession.MedicalQueueSlot(index), 1_000);
            applyDestination.Invoke(s, [new EntityId(ids[index]),
                new SetAgentDestinationCommand(GameSession.MedicalQueueSlot(index), "medical.free-water-queue"), false]);
        }
        var earlier = ids[10];
        var later = ids[11];
        Place(earlier, GameSession.MedicalQueueApproach(10), 850);
        Place(later, new GridCell(110, 152), 1_150);
        Assert.IsTrue(Send(s, new MedicalCommand(earlier, MedicalAction.GuideToWater)).IsAccepted);
        Assert.AreEqual(0, s.CaptureMedical()!.WaterOverflow.Length);
        s.AdvanceWithoutSnapshot(1);
        CollectionAssert.AreEqual(new[] { earlier }, s.CaptureMedical()!.WaterOverflow);
        Assert.IsNull(s.CaptureMedical()!.Needs.Single(item => item.AgentId == earlier).QueueSlot);
        Assert.IsTrue(Send(s, new MedicalCommand(later, MedicalAction.GuideToWater)).IsAccepted);
        Assert.AreEqual(GameSession.MedicalQueueApproach(10, 1),
            s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == later).Destination);
        s = Restored(s);
        while (s.CaptureMedical()!.WaterOverflow.Length < 2 && s.CurrentTick < 500)
            s.AdvanceWithoutSnapshot(1);
        CollectionAssert.AreEqual(new[] { earlier, later }, s.CaptureMedical()!.WaterOverflow);
        s = Restored(s);
        Assert.IsTrue(Send(s, new MedicalCommand(ids[0], MedicalAction.ReturnToShow)).IsAccepted);
        var after = s.CaptureMedical()!;
        Assert.AreEqual(10, after.WaterQueue.Length);
        Assert.AreEqual(earlier, after.WaterQueue[9]);
        CollectionAssert.AreEqual(new[] { later }, after.WaterOverflow);
        Assert.AreEqual(9, after.Needs.Single(item => item.AgentId == earlier).QueueSlot);
        Assert.IsNull(after.Needs.Single(item => item.AgentId == later).QueueSlot);
        Assert.AreEqual(GameSession.MedicalQueueApproach(10),
            s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == later).Destination);
        s = Restored(s);
        while (s.CurrentTick < 300 && new[] { earlier, later }.Any(id =>
                   s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == id).Action != AgentNavigationAction.Arrived))
            s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(AgentNavigationAction.Arrived,
            s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == earlier).Action,
            "The promoted visitor must physically advance from overflow into the main line.");
        Assert.AreEqual(AgentNavigationAction.Arrived,
            s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == later).Action,
            "The next overflow visitor must physically advance to the released tail spot.");
        Assert.IsTrue(Send(s, new MedicalCommand(later, MedicalAction.ReturnToShow)).IsAccepted);
        Assert.AreEqual(0, s.CaptureMedical()!.WaterOverflow.Length);
        Assert.AreEqual(MedicalIntent.WatchShow, s.CaptureMedical()!.Needs.Single(item => item.AgentId == later).Intent);
        Restored(s);
    }

    [TestMethod]
    public void FasterLaterDepartingGuestTakesEarlierPlaceOnlyAfterPhysicalArrival()
    {
        var s = Started();
        SuppressGuestWaterDemand(s);
        var people = s.CapturePreparation()!.People.Where(item => item.Role == ProtectedPersonRole.Guest).Take(2).ToArray();
        var slow = people[0].AgentId;
        var fast = people[1].AgentId;
        var field = typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var agents = field.GetValue(s)!;
        void Place(ulong id, GridCell cell, int speed)
        {
            var agent = agents.GetType().GetProperty("Item")!.GetValue(agents, [new EntityId(id)])!;
            var centre = TraversalGrid.CellCentre(cell);
            foreach (var (name, value) in new[] { ("XMillimetres", centre.XMillimetres), ("ZMillimetres", centre.ZMillimetres),
                         ("SegmentOriginXMillimetres", centre.XMillimetres), ("SegmentOriginZMillimetres", centre.ZMillimetres),
                         ("WalkingSpeedPermille", speed) })
                agent.GetType().GetProperty(name)!.SetValue(agent, value);
        }
        Place(slow, new GridCell(95, 138), 850);
        Place(fast, new GridCell(97, 139), 1_150);
        var approach = GameSession.MedicalQueueApproach(0);
        var slowStart = TraversalGrid.CellCentre(new GridCell(95, 138));
        var fastStart = TraversalGrid.CellCentre(new GridCell(97, 139));
        var point = TraversalGrid.CellCentre(approach);
        static long DistanceSquared((int XMillimetres, int ZMillimetres) a, (int XMillimetres, int ZMillimetres) b) =>
            (long)(a.XMillimetres - b.XMillimetres) * (a.XMillimetres - b.XMillimetres) +
            (long)(a.ZMillimetres - b.ZMillimetres) * (a.ZMillimetres - b.ZMillimetres);
        Assert.IsTrue(DistanceSquared(fastStart, point) > DistanceSquared(slowStart, point));
        Assert.IsTrue(Send(s, new MedicalCommand(slow, MedicalAction.GuideToWater)).IsAccepted);
        Assert.IsTrue(Send(s, new MedicalCommand(fast, MedicalAction.GuideToWater)).IsAccepted);
        Assert.AreEqual(0, s.CaptureMedical()!.WaterQueue.Length);
        Assert.IsNull(s.CaptureMedical()!.Needs.Single(item => item.AgentId == slow).QueueSlot);
        Assert.IsNull(s.CaptureMedical()!.Needs.Single(item => item.AgentId == fast).QueueSlot);
        s = Restored(s);
        while (s.CaptureMedical()!.WaterQueue.Length < 2 && s.CurrentTick < 1_000)
            s.AdvanceWithoutSnapshot(1);
        var queue = s.CaptureMedical()!.WaterQueue;
        Assert.IsTrue(queue.Length >= 2);
        Assert.AreEqual(fast, queue[0], "The faster physical arrival must overtake the earlier-departing walker.");
        Assert.AreEqual(slow, queue[1]);
        Restored(s);
    }

    [TestMethod]
    public void AbandoningWaterApproachReleasesNoQueuePlaceAcrossRestore()
    {
        var s = Started();
        var id = s.CaptureMedical()!.Needs.First(item => item.Profile == MedicalNeedProfile.Guest &&
            item.AgentId != s.CaptureMedical()!.AtRiskGuestId).AgentId;
        Assert.IsTrue(Send(s, new MedicalCommand(id, MedicalAction.GuideToWater)).IsAccepted);
        Assert.AreEqual(MedicalIntent.SeekWater, s.CaptureMedical()!.Needs.Single(item => item.AgentId == id).Intent);
        Assert.IsNull(s.CaptureMedical()!.Needs.Single(item => item.AgentId == id).QueueSlot);
        Assert.IsFalse(s.CaptureMedical()!.WaterQueue.Contains(id));
        s = Restored(s);
        Assert.IsTrue(Send(s, new MedicalCommand(id, MedicalAction.ReturnToShow)).IsAccepted);
        Assert.AreEqual(MedicalIntent.WatchShow, s.CaptureMedical()!.Needs.Single(item => item.AgentId == id).Intent);
        Assert.IsFalse(s.CaptureMedical()!.WaterQueue.Contains(id));
        Assert.AreEqual(-1L, s.CaptureMedical()!.Needs.Single(item => item.AgentId == id).LastWaterTick);
        Restored(s);
    }

    [TestMethod]
    public void SameTickWaterArrivalsUseStablePersonIdTieBreak()
    {
        var s = Started();
        SuppressGuestWaterDemand(s);
        var ids = s.CapturePreparation()!.People.Where(item => item.Role == ProtectedPersonRole.Guest)
            .Take(2).Select(item => item.AgentId).ToArray();
        var field = typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var agents = field.GetValue(s)!;
        var centre = TraversalGrid.CellCentre(GameSession.MedicalQueueApproach(0));
        for (var i = 0; i < ids.Length; i++)
        {
            var agent = agents.GetType().GetProperty("Item")!.GetValue(agents, [new EntityId(ids[i])])!;
            var x = centre.XMillimetres + (i == 0 ? -500 : 500);
            foreach (var (name, value) in new[] { ("XMillimetres", x), ("ZMillimetres", centre.ZMillimetres),
                         ("SegmentOriginXMillimetres", x), ("SegmentOriginZMillimetres", centre.ZMillimetres) })
                agent.GetType().GetProperty(name)!.SetValue(agent, value);
            Assert.IsTrue(Send(s, new MedicalCommand(ids[i], MedicalAction.GuideToWater)).IsAccepted);
        }
        Assert.AreEqual(0, s.CaptureMedical()!.WaterQueue.Length);
        s.AdvanceWithoutSnapshot(1);
        CollectionAssert.AreEqual(ids, s.CaptureMedical()!.WaterQueue);
        Assert.AreEqual(s.CaptureMedical()!.Evidence.Last(item => item.Id == "medical:queue-join").Tick,
            s.CaptureMedical()!.Evidence.First(item => item.Id == "medical:queue-join").Tick);
        Restored(s);
    }

    [TestMethod]
    public void FivePersonLineAdvancesAndNextPhysicalArrivalOwnsTapAcrossRestore()
    {
        var s = Started();
        var ids = s.CapturePreparation()!.People.Where(item => item.Role == ProtectedPersonRole.Guest)
            .Take(6).Select(item => item.AgentId).ToArray();
        var field = typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var m = s.CaptureMedical()!;
        field.SetValue(s, m with { Needs = m.Needs.Select(item => ids.Contains(item.AgentId)
            ? item with { Thirst = 9_500 } : item).ToArray() });
        foreach (var id in ids)
            Assert.IsTrue(Send(s, new MedicalCommand(id, MedicalAction.GuideToWater)).IsAccepted);
        Assert.AreEqual(0, s.CaptureMedical()!.WaterQueue.Length);
        while (s.CaptureMedical()!.WaterQueue.Length < 5 && s.CurrentTick < 2_500)
            s.AdvanceWithoutSnapshot(1);
        m = s.CaptureMedical()!;
        Assert.IsTrue(m.WaterQueue.Length >= 5);
        for (var index = 0; index < m.WaterQueue.Length; index++)
        {
            Assert.AreEqual(index, m.Needs.Single(item => item.AgentId == m.WaterQueue[index]).QueueSlot);
            Assert.AreEqual(GameSession.MedicalQueueSlot(index),
                s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == m.WaterQueue[index]).Destination);
        }
        s = Restored(s);
        while (s.CaptureMedical()!.WaterOwnerId is null && s.CurrentTick < 3_000)
            s.AdvanceWithoutSnapshot(1);
        var owner = s.CaptureMedical()!.WaterOwnerId;
        Assert.IsNotNull(owner);
        var next = s.CaptureMedical()!.WaterQueue[1];
        while (s.CaptureMedical()!.WaterQueue.Contains(owner.Value) && s.CurrentTick < 4_000)
            s.AdvanceWithoutSnapshot(1);
        Assert.IsFalse(s.CaptureMedical()!.WaterQueue.Contains(owner.Value));
        Assert.AreEqual(next, s.CaptureMedical()!.WaterQueue[0]);
        while (s.CaptureMedical()!.WaterOwnerId != next && s.CurrentTick < 4_500)
            s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(next, s.CaptureMedical()!.WaterOwnerId);
        Restored(s);
    }

    [TestMethod]
    public void DrinkingOwnsOneTapAndContinuouslyRelievesNeedsUntilThirstZeroAcrossRestore()
    {
        var s = Started();
        while (s.CaptureMedical()!.WaterOwnerId is null && s.CurrentTick < 2_000)
            s.AdvanceWithoutSnapshot(1);
        var started = s.CaptureMedical()!;
        Assert.IsNotNull(started.WaterOwnerId);
        var owner = started.WaterOwnerId.Value;
        Assert.AreEqual(owner, started.WaterQueue[0]);
        Assert.AreEqual(MedicalIntent.Drinking, started.Needs.Single(item => item.AgentId == owner).Intent);
        Assert.IsTrue(started.WaterDrinkTicks > 0);
        var thirst = started.Needs.Single(item => item.AgentId == owner).Thirst;
        var heat = started.Needs.Single(item => item.AgentId == owner).HeatExposure;
        s = Restored(s);
        s.AdvanceWithoutSnapshot(20);
        var midway = s.CaptureMedical()!;
        Assert.AreEqual(owner, midway.WaterOwnerId);
        Assert.AreEqual(MedicalIntent.Drinking, midway.Needs.Single(item => item.AgentId == owner).Intent);
        Assert.IsTrue(midway.Needs.Single(item => item.AgentId == owner).Thirst < thirst);
        Assert.IsTrue(midway.Needs.Single(item => item.AgentId == owner).HeatExposure < heat);
        Assert.AreEqual(-1L, midway.Needs.Single(item => item.AgentId == owner).LastWaterTick);
        s = Restored(s);
        while (s.CaptureMedical()!.WaterOwnerId == owner && s.CurrentTick < 3_000)
            s.AdvanceWithoutSnapshot(1);
        var completed = s.CaptureMedical()!;
        Assert.IsFalse(completed.WaterQueue.Contains(owner));
        Assert.AreEqual(0, completed.Needs.Single(item => item.AgentId == owner).Thirst);
        Assert.AreEqual(s.CurrentTick, completed.Needs.Single(item => item.AgentId == owner).LastWaterTick);
        Assert.AreEqual(1, completed.Evidence.Count(item => item.Id == "medical:water" && item.Description.Contains($"Person {owner}")));
        Restored(s);
    }

    [TestMethod]
    public void PerformerUsesSameFreeWaterServiceAndNeedsPersist()
    {
        var s = Started();
        SuppressGuestWaterDemand(s);
        var performer = s.CaptureMedical()!.Needs.First(item => item.Profile == MedicalNeedProfile.Performer);
        Assert.AreEqual(3, s.CaptureMedical()!.Needs.Count(item => item.Profile == MedicalNeedProfile.Performer));
        Assert.IsTrue(Send(s, new MedicalCommand(performer.AgentId, MedicalAction.GuideToWater)).IsAccepted);
        s = Restored(s);
        while (s.CaptureMedical()!.WaterOwnerId != performer.AgentId && s.CurrentTick < 2_000)
            s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(performer.AgentId, s.CaptureMedical()!.WaterOwnerId);
        var before = s.CaptureMedical()!.Needs.Single(item => item.AgentId == performer.AgentId);
        Assert.AreEqual(MedicalIntent.Drinking, before.Intent);
        s.AdvanceWithoutSnapshot(20);
        var after = s.CaptureMedical()!.Needs.Single(item => item.AgentId == performer.AgentId);
        Assert.IsTrue(after.Thirst < before.Thirst);
        Assert.IsTrue(after.HeatExposure < before.HeatExposure);
        Restored(s);
    }

    [TestMethod]
    public void PerformerNeedsDoNotPreventInitialSetFromStarting()
    {
        var s = Started();
        s.AdvanceWithoutSnapshot(3_200);
        Assert.AreEqual(LiveSetStage.Live, s.CaptureLivePerformance()!.Stage);
        Assert.AreEqual(3, s.CaptureLivePerformance()!.Performers.Count(item => item.OnStage));
        Restored(s);
    }

    [TestMethod]
    public void PerformerCollapseCriticalDeathAreAuthoritativeAcrossRestore()
    {
        var s = Started();
        var field = typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var medical = s.CaptureMedical()!;
        var performer = medical.Needs.First(item => item.Profile == MedicalNeedProfile.Performer);
        field.SetValue(s, medical with { Needs = medical.Needs.Select(item => item.AgentId == performer.AgentId
            ? item with { Thirst = 9_000, HeatExposure = 8_000, Stage = MedicalStage.Distress,
                WarningTick = -GameSession.MedicalCollapseDelayTicks + 1 }
            : item).ToArray() });
        s = Restored(s);
        s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalStage.Collapsed, s.CaptureMedical()!.Needs.Single(item => item.AgentId == performer.AgentId).Stage);
        Assert.AreEqual(MedicalIntent.Collapsed, s.CaptureMedical()!.Needs.Single(item => item.AgentId == performer.AgentId).Intent);
        s = Restored(s);
        s.AdvanceWithoutSnapshot(GameSession.MedicalCriticalDelayTicks);
        Assert.AreEqual(MedicalStage.Critical, s.CaptureMedical()!.Needs.Single(item => item.AgentId == performer.AgentId).Stage);
        s = Restored(s);
        s.AdvanceWithoutSnapshot(GameSession.MedicalDeathDelayTicks - GameSession.MedicalCriticalDelayTicks);
        Assert.AreEqual(MedicalStage.Terminal, s.CaptureMedical()!.Stage);
        Assert.AreEqual(ProtectedPersonRole.Performer, s.CaptureLifecycleSnapshot()!.Casualties.Single().Role);
        Restored(s);
    }

    [TestMethod]
    public void DistressedPerformerCanReceivePhysicalMedicTreatment()
    {
        var s = Started();
        var medicId = s.CaptureMedical()!.MedicId;
        while (!s.CapturePreparation()!.People.Single(item => item.AgentId == medicId).Admitted && s.CurrentTick < 1_500)
            s.AdvanceWithoutSnapshot(1);
        Assert.IsTrue(s.CapturePreparation()!.People.Single(item => item.AgentId == medicId).Admitted);
        var field = typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var m = s.CaptureMedical()!;
        var performerId = m.Needs.First(item => item.Profile == MedicalNeedProfile.Performer).AgentId;
        field.SetValue(s, m with { Needs = m.Needs.Select(item => item.AgentId == performerId
            ? item with { Thirst = 9_000, HeatExposure = 8_000, Stage = MedicalStage.Distress,
                WarningTick = s.CurrentTick, LastDecisionTick = s.CurrentTick }
            : item).ToArray() });
        Assert.IsTrue(Send(s, new MedicalCommand(performerId, MedicalAction.DispatchMedic)).IsAccepted);
        Assert.AreEqual(performerId, s.CaptureMedical()!.ResponsePatientId);
        s = Restored(s);
        while (s.CaptureMedical()!.Needs.Single(item => item.AgentId == performerId).Stage != MedicalStage.Treated &&
               s.CaptureMedical()!.Stage != MedicalStage.Terminal && s.CurrentTick < 4_000)
            s.AdvanceWithoutSnapshot(1);
        var response = s.CaptureMedical()!;
        var medic = s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == medicId);
        var performer = s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == performerId);
        Assert.AreEqual(MedicalStage.Treated, response.Needs.Single(item => item.AgentId == performerId).Stage,
            $"tick={s.CurrentTick} response={response.ResponseStage} medic={medic.Action}/{medic.Destination} " +
            $"at=({medic.XMillimetres},{medic.ZMillimetres}) performer={performer.Action}/{performer.Destination} " +
            $"at=({performer.XMillimetres},{performer.ZMillimetres}) evidence=" +
            string.Join(';', response.Evidence.Where(item => item.Id.Contains("dispatch") ||
                item.Id.Contains("treatment") || item.Id.Contains("collapse") || item.Id.Contains("critical"))
                .Select(item => $"{item.Tick}:{item.Id}")));
        Assert.AreEqual(0, s.CaptureLifecycleSnapshot()!.Casualties.Count);
        s = Restored(s);
        var returning = s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == medicId);
        while ((returning.Destination != GameSession.MedicalMedicCell || returning.Action != AgentNavigationAction.Arrived) &&
               s.CurrentTick < 4_000)
        {
            s.AdvanceWithoutSnapshot(1);
            returning = s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == medicId);
        }
        Assert.AreEqual(GameSession.MedicalMedicCell, returning.Destination);
        Assert.AreEqual(AgentNavigationAction.Arrived, returning.Action);
        Restored(s);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void DispatchKeepsCollapsedGuestOrPerformerDownThroughTravelAndTreatment(bool performer, bool critical)
    {
        var s = Started();
        ulong patientId;
        if (performer)
        {
            var medicId = s.CaptureMedical()!.MedicId;
            while (!s.CapturePreparation()!.People.Single(item => item.AgentId == medicId).Admitted && s.CurrentTick < 1_500)
                s.AdvanceWithoutSnapshot(1);
            var field = typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var m = s.CaptureMedical()!;
            patientId = m.Needs.First(item => item.Profile == MedicalNeedProfile.Performer).AgentId;
            field.SetValue(s, m with { Needs = m.Needs.Select(item => item.AgentId == patientId
                ? item with { Thirst = 9_000, HeatExposure = 8_000, Stage = MedicalStage.Distress,
                    WarningTick = s.CurrentTick - GameSession.MedicalCollapseDelayTicks + 1 }
                : item).ToArray() });
            s.AdvanceWithoutSnapshot(1);
        }
        else
        {
            patientId = s.CaptureMedical()!.AtRiskGuestId;
            while (s.CaptureMedical()!.Stage != MedicalStage.Collapsed && s.CurrentTick < 4_000)
                s.AdvanceWithoutSnapshot(1);
        }
        if (critical) s.AdvanceWithoutSnapshot(GameSession.MedicalCriticalDelayTicks);
        MedicalStage Stage() => performer
            ? s.CaptureMedical()!.Needs.Single(item => item.AgentId == patientId).Stage
            : s.CaptureMedical()!.Stage;
        Assert.AreEqual(critical ? MedicalStage.Critical : MedicalStage.Collapsed, Stage());
        Assert.AreEqual(MedicalIntent.Collapsed, s.CaptureMedical()!.Needs.Single(item => item.AgentId == patientId).Intent);
        var result = Send(s, new MedicalCommand(patientId, MedicalAction.DispatchMedic));
        Assert.IsTrue(result.IsAccepted, result.Message);
        Assert.AreEqual(MedicalResponseStage.Travelling, s.CaptureMedical()!.ResponseStage);
        Assert.AreEqual(MedicalIntent.Collapsed, s.CaptureMedical()!.Needs.Single(item => item.AgentId == patientId).Intent);
        s = Restored(s);
        while (s.CaptureMedical()!.ResponseStage == MedicalResponseStage.Travelling && s.CurrentTick < 6_000)
        {
            s.AdvanceWithoutSnapshot(1);
            Assert.AreEqual(MedicalIntent.Collapsed, s.CaptureMedical()!.Needs.Single(item => item.AgentId == patientId).Intent);
        }
        Assert.AreEqual(MedicalResponseStage.Treating, s.CaptureMedical()!.ResponseStage);
        s = Restored(s);
        s.AdvanceWithoutSnapshot(GameSession.MedicalTreatmentTicks - 1);
        Assert.AreEqual(MedicalIntent.Collapsed, s.CaptureMedical()!.Needs.Single(item => item.AgentId == patientId).Intent);
        s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalResponseStage.Completed, s.CaptureMedical()!.ResponseStage);
        Assert.AreEqual(MedicalIntent.WatchShow, s.CaptureMedical()!.Needs.Single(item => item.AgentId == patientId).Intent);
        Restored(s);
    }

    [TestMethod]
    public void PerformerDrinksBeforeEntryThenReturnsViaAccessAndStairsAcrossRestores()
    {
        var s = Started();
        SuppressGuestWaterDemand(s);
        var performerId = s.CaptureMedical()!.Needs.First(item => item.Profile == MedicalNeedProfile.Performer).AgentId;
        Assert.IsTrue(Send(s, new MedicalCommand(performerId, MedicalAction.GuideToWater)).IsAccepted);
        s = Restored(s);
        while (s.CaptureMedical()!.Needs.Single(item => item.AgentId == performerId).LastWaterTick < 0 && s.CurrentTick < 4_500)
            s.AdvanceWithoutSnapshot(1);
        Assert.IsTrue(s.CaptureMedical()!.Needs.Single(item => item.AgentId == performerId).LastWaterTick >= 0);
        Assert.AreEqual("medical.return-to-stage-access", s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == performerId).IntentId);
        s = Restored(s);
        while (!s.CaptureLivePerformance()!.Performers.Single(item => item.AgentId == performerId).OnStage && s.CurrentTick < 5_900)
            s.AdvanceWithoutSnapshot(1);
        var performer = s.CaptureLivePerformance()!.Performers.Single(item => item.AgentId == performerId);
        Assert.IsTrue(performer.AccessReached);
        Assert.IsTrue(performer.StairReached);
        Assert.IsTrue(performer.OnStage);
        Restored(s);
    }

    [TestMethod]
    public void OnStageWaterRetargetIsImmediatelySaveable()
    {
        var s = Started();
        s.AdvanceWithoutSnapshot(3_200);
        var performer = s.CaptureLivePerformance()!.Performers.First();
        Assert.IsTrue(performer.OnStage);
        Assert.IsTrue(Send(s, new MedicalCommand(performer.AgentId, MedicalAction.GuideToWater)).IsAccepted);
        var retargeted = s.CaptureLivePerformance()!.Performers.First();
        Assert.IsFalse(retargeted.OnStage);
        Assert.IsFalse(retargeted.InstrumentAttached);
        Assert.IsFalse(retargeted.StairReached);
        Restored(s);
    }

    [TestMethod]
    public void GuestWaterReliefDoesNotCancelAnotherPatientsMedicResponse()
    {
        var s = Started();
        var medicId = s.CaptureMedical()!.MedicId;
        while (!s.CapturePreparation()!.People.Single(item => item.AgentId == medicId).Admitted && s.CurrentTick < 1_500)
            s.AdvanceWithoutSnapshot(1);
        var field = typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var m = s.CaptureMedical()!;
        var performerId = m.Needs.First(item => item.Profile == MedicalNeedProfile.Performer).AgentId;
        field.SetValue(s, m with { Stage = MedicalStage.Distress, WarningTick = s.CurrentTick,
            Needs = m.Needs.Select(item => item.AgentId == performerId
                ? item with { Thirst = 9_000, HeatExposure = 8_000, Stage = MedicalStage.Distress,
                    WarningTick = s.CurrentTick, LastDecisionTick = s.CurrentTick }
                : item).ToArray() });
        Assert.IsTrue(Send(s, new MedicalCommand(performerId, MedicalAction.DispatchMedic)).IsAccepted);
        s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalStage.Treated, s.CaptureMedical()!.Stage);
        Assert.AreEqual(performerId, s.CaptureMedical()!.ResponsePatientId);
        Assert.IsTrue(s.CaptureMedical()!.ResponseStage is MedicalResponseStage.Travelling or MedicalResponseStage.Treating);
        s = Restored(s);
        while (s.CaptureMedical()!.Needs.Single(item => item.AgentId == performerId).Stage != MedicalStage.Treated && s.CurrentTick < 4_000)
            s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalStage.Treated, s.CaptureMedical()!.Needs.Single(item => item.AgentId == performerId).Stage);
        Restored(s);
    }
}

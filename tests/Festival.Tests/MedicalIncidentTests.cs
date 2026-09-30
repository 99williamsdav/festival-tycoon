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
        var s = BuildSession.Planned(seed);
        foreach (var offer in new[] { "staff.steward", "equipment.buy" })
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
        var field = typeof(GameSession).GetProperty("MedicalView", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var m = s.CaptureMedical()!;
        field.SetValue(s, m with { Needs = m.Needs.Select(item => item.Profile == MedicalNeedProfile.Guest
            ? item with { Thirst = 0, HeatExposure = 0 } : item).ToArray() });
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
    public void DrinkingPaceVariesDeterministicallyByPerson()
    {
        CollectionAssert.AreEquivalent(new[] { 8, 12, 16, 20 },
            Enumerable.Range(1, 4).Select(id => GameSession.MedicalDrinkThirstPerTickFor((ulong)id)).ToArray());
        Assert.AreEqual(GameSession.MedicalDrinkThirstPerTickFor(1),
            GameSession.MedicalDrinkThirstPerTickFor(5), "Pace remains stable across sessions and saves.");
    }

    [TestMethod]
    public void DrinkingOwnsOneTapAndContinuouslyRelievesNeedsUntilThirstZeroAcrossRestore()
    {
        var s = Started();
        while (BuildSession.MainTap(s).OwnerId is null && s.CurrentTick < 12_000)
            s.AdvanceWithoutSnapshot(1);
        var started = s.CaptureMedical()!;
        var startedTap = BuildSession.MainTap(s);
        Assert.IsNotNull(startedTap.OwnerId);
        var owner = startedTap.OwnerId.Value;
        Assert.AreEqual(owner, startedTap.Queue[0]);
        Assert.AreEqual(MedicalIntent.Drinking, started.Needs.Single(item => item.AgentId == owner).Intent);
        Assert.IsTrue(startedTap.DrinkTicks > 0);
        var thirst = started.Needs.Single(item => item.AgentId == owner).Thirst;
        var heat = started.Needs.Single(item => item.AgentId == owner).HeatExposure;
        s = Restored(s);
        s.AdvanceWithoutSnapshot(20);
        var midway = s.CaptureMedical()!;
        Assert.AreEqual(owner, BuildSession.MainTap(s).OwnerId);
        Assert.AreEqual(MedicalIntent.Drinking, midway.Needs.Single(item => item.AgentId == owner).Intent);
        Assert.IsTrue(midway.Needs.Single(item => item.AgentId == owner).Thirst < thirst);
        Assert.IsTrue(midway.Needs.Single(item => item.AgentId == owner).HeatExposure < heat);
        Assert.AreEqual(-1L, midway.Needs.Single(item => item.AgentId == owner).LastWaterTick);
        s = Restored(s);
        while (BuildSession.MainTap(s).OwnerId == owner && s.CurrentTick < 15_000)
            s.AdvanceWithoutSnapshot(1);
        var completed = s.CaptureMedical()!;
        Assert.IsFalse(BuildSession.MainTap(s).Queue.Contains(owner));
        Assert.AreEqual(0, completed.Needs.Single(item => item.AgentId == owner).Thirst);
        Assert.AreEqual(s.CurrentTick, completed.Needs.Single(item => item.AgentId == owner).LastWaterTick);
        Assert.AreEqual(1, completed.Evidence.Count(item => item.Id == "medical:water" && item.Description.Contains($"Person {owner}")));
        Restored(s);
    }

    [TestMethod]
    public void PerformerNeedsDoNotPreventInitialSetFromStarting()
    {
        var s = Started();
        s.AdvanceWithoutSnapshot(4_800);
        Assert.AreEqual(LiveSetStage.Live, s.CaptureLivePerformance()!.Stage);
        Assert.AreEqual(3, s.CaptureLivePerformance()!.Performers.Count(item => item.OnStage));
        Restored(s);
    }

    [TestMethod]
    public void PerformerCollapseCriticalDeathAreAuthoritativeAcrossRestore()
    {
        var s = Started();
        var field = typeof(GameSession).GetProperty("MedicalView", BindingFlags.Instance | BindingFlags.NonPublic)!;
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
        Assert.IsTrue(s.CaptureMedical()!.Fatal);
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
        var field = typeof(GameSession).GetProperty("MedicalView", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var m = s.CaptureMedical()!;
        var performerId = m.Needs.First(item => item.Profile == MedicalNeedProfile.Performer).AgentId;
        field.SetValue(s, m with { Needs = m.Needs.Select(item => item.AgentId == performerId
            ? item with { Thirst = 9_000, HeatExposure = 8_000, Stage = MedicalStage.Distress,
                WarningTick = s.CurrentTick, LastDecisionTick = s.CurrentTick }
            : item).ToArray() });
        Assert.IsTrue(Send(s, new MedicalCommand(performerId, MedicalAction.DispatchMedic)).IsAccepted);
        Assert.AreEqual(performerId, s.CaptureMedical()!.Medics[0].PatientId);
        s = Restored(s);
        while (s.CaptureMedical()!.Needs.Single(item => item.AgentId == performerId).Stage != MedicalStage.Treated &&
               !s.CaptureMedical()!.Fatal && s.CurrentTick < 8_000)
            s.AdvanceWithoutSnapshot(1);
        var response = s.CaptureMedical()!;
        var medic = s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == medicId);
        var performer = s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == performerId);
        Assert.AreEqual(MedicalStage.Treated, response.Needs.Single(item => item.AgentId == performerId).Stage,
            $"tick={s.CurrentTick} response={response.Medics[0].Stage} medic={medic.Action}/{medic.Destination} " +
            $"at=({medic.XMillimetres},{medic.ZMillimetres}) performer={performer.Action}/{performer.Destination} " +
            $"at=({performer.XMillimetres},{performer.ZMillimetres}) evidence=" +
            string.Join(';', response.Evidence.Where(item => item.Id.Contains("dispatch") ||
                item.Id.Contains("treatment") || item.Id.Contains("collapse") || item.Id.Contains("critical"))
                .Select(item => $"{item.Tick}:{item.Id}")));
        Assert.AreEqual(0, s.CaptureLifecycleSnapshot()!.Casualties.Count);
        s = Restored(s);
        var returning = s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == medicId);
        while ((returning.Destination != GameSession.MedicalMedicCell || returning.Action != AgentNavigationAction.Arrived) &&
               s.CurrentTick < 8_000)
        {
            s.AdvanceWithoutSnapshot(1);
            returning = s.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == medicId);
        }
        Assert.AreEqual(GameSession.MedicalMedicCell, returning.Destination);
        Assert.AreEqual(AgentNavigationAction.Arrived, returning.Action);
        Restored(s);
    }

    [TestMethod]
    public void GuestWaterReliefDoesNotCancelAnotherPatientsMedicResponse()
    {
        var s = Started();
        var medicId = s.CaptureMedical()!.MedicId;
        while (!s.CapturePreparation()!.People.Single(item => item.AgentId == medicId).Admitted && s.CurrentTick < 1_500)
            s.AdvanceWithoutSnapshot(1);
        var field = typeof(GameSession).GetProperty("MedicalView", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var m = s.CaptureMedical()!;
        var performerId = m.Needs.First(item => item.Profile == MedicalNeedProfile.Performer).AgentId;
        field.SetValue(s, m with {
            Needs = m.Needs.Select(item => item.AgentId == performerId
                ? item with { Thirst = 9_000, HeatExposure = 8_000, Stage = MedicalStage.Distress,
                    WarningTick = s.CurrentTick, LastDecisionTick = s.CurrentTick }
                : item).ToArray() });
        Assert.IsTrue(Send(s, new MedicalCommand(performerId, MedicalAction.DispatchMedic)).IsAccepted);
        s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(performerId, s.CaptureMedical()!.Medics[0].PatientId);
        Assert.IsTrue(s.CaptureMedical()!.Medics[0].Stage is MedicalResponseStage.Travelling or MedicalResponseStage.Treating);
        s = Restored(s);
        while (s.CaptureMedical()!.Needs.Single(item => item.AgentId == performerId).Stage != MedicalStage.Treated && s.CurrentTick < 8_000)
            s.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(MedicalStage.Treated, s.CaptureMedical()!.Needs.Single(item => item.AgentId == performerId).Stage);
        Restored(s);
    }
}

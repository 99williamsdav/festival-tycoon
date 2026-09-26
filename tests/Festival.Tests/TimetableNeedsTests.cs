using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class TimetableNeedsTests
{
    private static CommandResult Send(GameSession session, SessionCommand command) => session.Execute(new(
        new CommandId(session.NextSubmissionSequence + 1), session.CampaignId, session.Phase,
        session.CurrentTick, session.NextSubmissionSequence, null, command));

    private static void SetMedical(GameSession session, Func<MedicalNeed, MedicalNeed> change)
    {
        var state = session.CaptureMedical()!;
        typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session, state with { Needs = state.Needs.Select(change).ToArray() });
    }

    private static GameSession Started(int ticks = 1_600)
    {
        var session = GameSession.CreateTimetableCampaign(20260926);
        Assert.IsTrue(Send(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.neon-postcards", "act.field-frequency"])).IsAccepted);
        foreach (var id in new[] { "staff.steward", "equipment.buy" })
            Assert.IsTrue(Send(session, new AcceptPreparationOfferCommand(id)).IsAccepted);
        Assert.IsTrue(Send(session, new StartPreparedEditionCommand()).IsAccepted);
        SetMedical(session, need => need with { Thirst = 0, HeatExposure = 0 });
        session.AdvanceWithoutSnapshot(ticks);
        return session;
    }

    private static GameSession Restored(GameSession session)
    {
        var restored = GameSession.Restore(session.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess, restored.Error);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.Session!.CaptureSnapshot().AuthoritativeHash);
        return restored.Session;
    }

    [TestMethod]
    public void NinePerformerMedicalProfilesRemainProtectedAndSaved()
    {
        var session = GameSession.CreateTimetableCampaign(20260926);
        var performers = session.CapturePreparation()!.People.Where(person => person.Role == ProtectedPersonRole.Performer).ToArray();
        Assert.AreEqual(9, performers.Length);
        Assert.AreEqual(9, session.CaptureMedical()!.Needs.Count(need => need.Profile == MedicalNeedProfile.Performer));
        Assert.IsTrue(performers.All(person => session.CaptureMedical()!.Needs.Any(need => need.AgentId == person.AgentId)));
        Restored(session);
        session = Started();
        Assert.IsTrue(session.CapturePreparation()!.People.Length <= 35);
        Restored(session);
    }

    [TestMethod]
    public void UpcomingAnticipationIsFiniteAndClampedAndSurvivesRestore()
    {
        Assert.AreEqual(0, GameSession.FestivalAnticipationAppeal(100, -1));
        Assert.AreEqual(0, GameSession.FestivalAnticipationAppeal(100, 1_601));
        Assert.AreEqual(0, GameSession.FestivalAnticipationAppeal(100, long.MaxValue));
        Assert.AreEqual(0, GameSession.FestivalAnticipationAppeal(100, 1_600));
        Assert.AreEqual(750, GameSession.FestivalAnticipationAppeal(100, 800));
        Assert.AreEqual(1_500, GameSession.FestivalAnticipationAppeal(100, 0));
        Assert.AreEqual(1_500, GameSession.FestivalAnticipationAppeal(int.MaxValue, 0));
        Assert.AreEqual(0, GameSession.FestivalAnticipationAppeal(-1, 0));
        var session = Started(GameSession.FestivalSlotStarts[0] - 160);
        var guest = session.CapturePreparation()!.People.First(person => person.Role == ProtectedPersonRole.Guest).AgentId;
        Assert.IsTrue(session.ScheduledSilence);
        Assert.IsTrue(session.FestivalNeedMusicAppeal(guest) is >= 2_500 and <= 4_000);
        var restored = Restored(session);
        Assert.AreEqual(session.FestivalNeedMusicAppeal(guest), restored.FestivalNeedMusicAppeal(guest));
        Assert.AreEqual(session.FestivalAffinity(guest, session.UpcomingFestivalAct!), restored.FestivalAffinity(guest, restored.UpcomingFestivalAct!));
        session.AdvanceWithoutSnapshot(80);
        restored.AdvanceWithoutSnapshot(80);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.CaptureSnapshot().AuthoritativeHash);
    }

    [TestMethod]
    public void UrgentThirstWinsUpcomingMusicAndNeedsChoiceRestoresDeterministically()
    {
        var session = Started();
        var guest = session.CapturePreparation()!.People.First(person => person.Role == ProtectedPersonRole.Guest && person.Admitted).AgentId;
        SetMedical(session, need => need.AgentId == guest ? need with { Thirst = 9_500, HeatExposure = 7_000, LastDecisionTick = session.CurrentTick } : need);
        var restored = Restored(session);
        session.AdvanceWithoutSnapshot(80);
        restored.AdvanceWithoutSnapshot(80);
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash, restored.CaptureSnapshot().AuthoritativeHash);
        Assert.IsTrue(session.CaptureMedical()!.Needs.Single(need => need.AgentId == guest).Intent is MedicalIntent.SeekWater or MedicalIntent.Drinking);
        Restored(session);
    }

    [TestMethod]
    public void IdlePerformerHeatChoosesPhysicalRestWithoutStageEntitlement()
    {
        var session = Started();
        var performer = session.CapturePreparation()!.People.First(person => person.Role == ProtectedPersonRole.Performer && person.Admitted && !session.IsCurrentProgrammePerformer(person.AgentId)).AgentId;
        SetMedical(session, need => need.AgentId == performer ? need with { Thirst = 2_000, HeatExposure = 8_500, LastDecisionTick = -240 } : need);
        session.AdvanceWithoutSnapshot(80);
        var need = session.CaptureMedical()!.Needs.Single(item => item.AgentId == performer);
        Assert.AreEqual(MedicalIntent.Rest, need.Intent);
        var agent = session.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == performer);
        Assert.AreEqual(GameSession.MedicalRestCell, agent.Destination);
        Assert.IsFalse(session.IsCurrentProgrammePerformer(performer));
        Restored(session);
        // Prior treatment must not trap a later ordinary rest choice forever.
        SetMedical(session, item => item.AgentId == performer ? item with { Stage = MedicalStage.Treated, HeatExposure = 5_900 } : item);
        var restDeadline = session.CurrentTick + 1_600;
        while (session.CaptureMedical()!.Needs.Single(item => item.AgentId == performer).Intent == MedicalIntent.Rest && session.CurrentTick < restDeadline)
            session.AdvanceWithoutSnapshot(1);
        Assert.AreNotEqual(MedicalIntent.Rest, session.CaptureMedical()!.Needs.Single(item => item.AgentId == performer).Intent);
        Assert.AreEqual(MedicalStage.Treated, session.CaptureMedical()!.Needs.Single(item => item.AgentId == performer).Stage);
        Restored(session);
    }

    [TestMethod]
    public void FuturePerformerRetainsWarningCollapseCriticalAndFirstDeathProtection()
    {
        var session = Started();
        var performer = session.CapturePreparation()!.People.First(person => person.Role == ProtectedPersonRole.Performer &&
            person.Admitted && !session.IsCurrentProgrammePerformer(person.AgentId));
        // A deliberately untreated warning isolates the protected medical deadline from ordinary prevention.
        SetMedical(session, need => need.AgentId == performer.AgentId ? need with { Thirst = 9_500, HeatExposure = 8_500,
            Stage = MedicalStage.Distress, WarningTick = session.CurrentTick, Intent = MedicalIntent.AwaitMedic } : need);
        session = Restored(session);
        session.AdvanceWithoutSnapshot(GameSession.MedicalCollapseDelayTicks);
        Assert.AreEqual(MedicalStage.Collapsed, session.CaptureMedical()!.Needs.Single(need => need.AgentId == performer.AgentId).Stage);
        session = Restored(session);
        session.AdvanceWithoutSnapshot(GameSession.MedicalCriticalDelayTicks);
        Assert.AreEqual(MedicalStage.Critical, session.CaptureMedical()!.Needs.Single(need => need.AgentId == performer.AgentId).Stage);
        session.AdvanceWithoutSnapshot(GameSession.MedicalDeathDelayTicks - GameSession.MedicalCriticalDelayTicks);
        Assert.AreEqual(PreparationStatus.Failed, session.PreparedStatus);
        Assert.AreEqual(performer.Name, session.CaptureLifecycleSnapshot()!.Casualties.Single().PersonId);
        Restored(session);
    }

    [TestMethod]
    public void PlannedSilenceDoesNotCreateMusicCutoffButActualPowerCutDoes()
    {
        var session = Started();
        session.AdvanceWithoutSnapshot(80);
        Assert.IsTrue(session.CaptureDisorder()!.People.All(person => person.Grievance != DisorderGrievance.MusicCutoff));
        session.AdvanceWithoutSnapshot(896); // Keep the isolated eligibility call on its eight-tick decision cadence.
        // Isolate eligibility from physical timing: existing listener positions are not moved.
        var liveField = typeof(GameSession).GetField("_livePerformance", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var live = session.CaptureLivePerformance()!;
        var equipmentField = typeof(GameSession).GetField("_equipment", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var equipment = session.CaptureEquipment()!;
        liveField.SetValue(session, live with { Stage = LiveSetStage.Interrupted,
            Listeners = live.Listeners.Select(listener => listener with { Enthusiasm = 100, AtPlace = true }).ToArray() });
        equipmentField.SetValue(session, equipment with { Stage = EquipmentStage.Isolated });
        typeof(GameSession).GetMethod("AdvanceDisorder", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, null);
        Assert.IsTrue(session.CaptureDisorder()!.People.Any(person => person.Grievance == DisorderGrievance.MusicCutoff));
    }
}

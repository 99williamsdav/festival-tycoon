using Festival.Simulation;
using Festival.Persistence;
using Godot;
using System;
using System.IO;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private string? _interventionCaptureDirectory;
    private int _interventionCaptureFrame;
    private ulong _interventionCaptureSteward;
    private ulong _interventionCaptureWaterGuest;
    private ulong _interventionCaptureEscortGuest;
    private GameSession? _interventionCapturePreparedBaseline;

    // Older labelled water/incident screenshots deliberately set needs/routes remotely.
    // Keep that fixture seam explicit and isolated; the physical staff capture never uses it.
    private DevelopmentMedicalFixtureCommand LegacyMedicalCaptureFixture(ulong id, MedicalAction action)
    {
        if (_interventionCaptureDirectory is not null ||
            (_waterPlaytestCaptureDirectory is null && _waterFoundationCaptureDirectory is null &&
             _medicalCaptureDirectory is null && _disorderCaptureDirectory is null))
            throw new InvalidOperationException("Legacy medical fixtures are restricted to isolated legacy capture modes.");
        var medical = _session.CaptureMedical()!;
        typeof(GameSession).GetField("_medical", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(_session, medical with { DevelopmentInterventionFixturesEnabled = true });
        return new(id, action);
    }

    private void InterventionCaptureImage(string name) => GetViewport().GetTexture().GetImage()
        .SavePng(Path.Combine(_interventionCaptureDirectory!, name + ".png"));

    private void InterventionCaptureUntil(Func<bool> condition, int limit)
    {
        StaffCaptureSend(new SetPausedCommand(false));
        for (var count = 0; count < limit && !condition(); count++) _session.AdvanceWithoutSnapshot(1);
        StaffCaptureSend(new SetPausedCommand(true));
        _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary(); RefreshPreparationHud();
        if (!condition()) throw new InvalidOperationException("Bounded physical intervention demonstration did not reach its milestone.");
    }

    private void CaptureChooseInterventionWorker(ResponseRole role, ulong id)
    {
        RefreshStaffInterventionControls();
        _staffInterventionChoices[role].Select(Array.IndexOf(_staffInterventionWorkerIds[role], id));
        RefreshStaffInterventionControls();
    }

    private void ProcessInterventionCapture()
    {
        if (_interventionCaptureDirectory is null) return;
        _interventionCaptureFrame++;
        if (_interventionCaptureFrame == 4)
        {
            if (_disorderButtons.ContainsKey(DisorderAction.CloseWater) || _disorderButtons.ContainsKey(DisorderAction.ReopenWater))
                throw new InvalidOperationException("Essential water must not expose player-facing closure controls.");
            GD.Print("WATER_UI_CONTROLS closure=False reopen=False simulation_diagnostic_preserved=True");
            foreach (var effect in new[] { "staff.medic-slot", "staff.steward-slot", "staff.role-training" }) _staffEffectButtons[effect].EmitSignal(Button.SignalName.Pressed);
            foreach (var offer in new[] { "staff.extra-medic", "staff.extra-steward", "act.folk", "staff.steward", "equipment.buy" }) _offerButtons[offer].EmitSignal(Button.SignalName.Pressed);
            _interventionCapturePreparedBaseline = GameSession.Restore(_session.CapturePersistenceSnapshot()).Session!;
            PreparationStart(); StaffCaptureAdvance(1500);
            if (_session.CaptureMedical()!.DevelopmentInterventionFixturesEnabled)
                throw new InvalidOperationException("Physical staff capture must not enable remote fixture interventions.");
            _interventionCaptureSteward = _session.GetResponseStaff().Single(profile => profile.Name == "Sam Ellis").AgentId;
            _interventionCaptureWaterGuest = _session.CapturePreparation()!.People.Where(person => person.Role == ProtectedPersonRole.Guest).Skip(17).First().AgentId;
            var medic = _session.CaptureMedical()!.MedicId;
            SelectAttendee(new EntityId(_interventionCaptureWaterGuest)); CaptureChooseInterventionWorker(ResponseRole.Steward, _interventionCaptureSteward);
            _staffInterventionButtons[(ResponseRole.Steward, StaffInterventionAction.GuideToWater)].EmitSignal(Button.SignalName.Pressed);
            SelectAttendee(new EntityId(_session.CaptureMedical()!.AtRiskGuestId)); CaptureChooseInterventionWorker(ResponseRole.Medic, medic);
            _staffInterventionButtons[(ResponseRole.Medic, StaffInterventionAction.GuideToRest)].EmitSignal(Button.SignalName.Pressed);
            if (_session.CaptureStaffInterventions().Count(job => job.Stage == StaffInterventionStage.Travelling) != 2)
                throw new InvalidOperationException("Named physical guidance requests were not both committed.");
            SelectAttendee(new EntityId(_interventionCaptureSteward));
            _focus = new Vector3(-4, 0, 12); _camera.Size = 38; ApplyCamera();
            _preparationMessage = "LABELLED DEMO • two independent physical approaches; no remote guest effect."; RefreshPreparationHud();
        }
        if (_interventionCaptureFrame == 6)
        {
            InterventionCaptureImage("named-abilities-and-approach");
            SelectAttendee(new EntityId(_interventionCaptureWaterGuest));
        }
        if (_interventionCaptureFrame == 8)
        {
            InterventionCaptureImage("named-help-no-remote-controls");
            InterventionCaptureUntil(() => _session.CaptureStaffInterventions().Any(job => job.Stage == StaffInterventionStage.Guiding), 3000);
            var job = _session.CaptureStaffInterventions().First(item => item.Stage == StaffInterventionStage.Guiding);
            SelectAttendee(new EntityId(job.GuestId));
            FocusMedicalCapturePerson(job.GuestId, 24);
            _preparationMessage = "LABELLED DEMO • staff physically arrived; only now does guidance begin."; RefreshPreparationHud();
        }
        if (_interventionCaptureFrame == 7)
        {
            InterventionCaptureImage("named-target-physical-help-status");
            if (_staffInterventionInspector!.GetParent().GetParent() is ScrollContainer scroll)
                scroll.EnsureControlVisible(_staffInterventionButtons[(ResponseRole.Medic, StaffInterventionAction.EscortOut)]);
        }
        if (_interventionCaptureFrame == 10)
        {
            InterventionCaptureImage("physical-arrival-before-guidance");
            InterventionCaptureUntil(() => _session.CaptureStaffInterventions().Count(job => job.Stage == StaffInterventionStage.Completed) == 2, 3000);
            StaffCaptureAdvance(Math.Max(0, 2900 - checked((int)_session.CurrentTick)));
            StaffCaptureSend(new EquipmentCommand(EquipmentAction.Isolate));
            InterventionCaptureUntil(() => _session.CaptureDisorder()!.People.Any(person => person.Stage == DisorderStage.Complaint), 1500);
            _interventionCaptureEscortGuest = _session.CaptureDisorder()!.People.First(person => person.Stage == DisorderStage.Complaint).AgentId;
            SelectAttendee(new EntityId(_interventionCaptureEscortGuest)); CaptureChooseInterventionWorker(ResponseRole.Steward, _interventionCaptureSteward);
            _staffInterventionButtons[(ResponseRole.Steward, StaffInterventionAction.EscortOut)].EmitSignal(Button.SignalName.Pressed);
            if (!_session.CaptureStaffInterventions().Any(job => job.Action == StaffInterventionAction.EscortOut && job.Stage == StaffInterventionStage.Travelling))
                throw new InvalidOperationException("Actual complaint escort request was not committed.");
            _preparationMessage = "LABELLED DEMO • actual music-cutoff complaint; named escort must first reach the person."; RefreshPreparationHud();
        }
        if (_interventionCaptureFrame == 12)
        {
            InterventionCaptureImage("escort-request-actual-complaint");
            // Use the ordinary music counter so unrelated attendees do not escalate
            // while this one already-requested physical escort is demonstrated.
            StaffCaptureSend(new DisorderCommand(DisorderAction.RestoreMusic));
            InterventionCaptureUntil(() => _session.CaptureStaffInterventions().Any(job => job.Action == StaffInterventionAction.EscortOut && job.Stage == StaffInterventionStage.Escorting), 3000);
            SelectAttendee(new EntityId(_interventionCaptureSteward));
            FocusMedicalCapturePerson(_interventionCaptureEscortGuest, 24);
            var saved = SaveFileAdapter.SaveSlot(SaveDirectory, "manual-mid-escort", new SaveWriteRequest(_session, _saveCompatibility, "r005c-physical-demo", DateTimeOffset.UtcNow));
            var loaded = SaveFileAdapter.LoadSlot(SaveDirectory, "manual-mid-escort", _saveCompatibility);
            if (!saved.IsSuccess || !loaded.IsSuccess || loaded.Session!.CaptureSnapshot().AuthoritativeHash != _session.CaptureSnapshot().AuthoritativeHash)
                throw new InvalidOperationException("Actual mid-escort save failed round-trip.");
            _preparationMessage = "LABELLED DEMO • physical escort in progress; not safe or departed yet. Mid-escort save verified."; RefreshPreparationHud();
        }
        if (_interventionCaptureFrame == 14)
        {
            InterventionCaptureImage("physical-escort-in-progress");
            InterventionCaptureUntil(() => _session.CaptureStaffInterventions().Any(job => job.Action == StaffInterventionAction.EscortOut && job.Stage == StaffInterventionStage.Completed), 7000);
            var person = _session.CapturePreparation()!.People.Single(item => item.AgentId == _interventionCaptureEscortGuest);
            var position = _session.CaptureObservation().NavigationAgents.Single(item => item.Id.Value == person.AgentId);
            var gate = TraversalGrid.CellCentre(GameSession.MedicalExitCell);
            if (!person.Departed || position.XMillimetres != gate.XMillimetres || position.ZMillimetres != gate.ZMillimetres || _session.PreparedStatus != PreparationStatus.Running)
                throw new InvalidOperationException("Individual escort did not complete physically at gate while festival remained Running.");
            SelectAttendee(new EntityId(_interventionCaptureSteward)); FocusMedicalCapturePerson(person.AgentId, 25);
            _preparationMessage = "LABELLED DEMO • one affected guest escorted to actual gate; festival continues. No teleport or closure."; RefreshPreparationHud();
            GD.Print($"STAFF_INTERVENTION_CAPTURE guidance_completed=2 escort_gate=True festival_running=True fixture_remote=False people={_session.CapturePreparation()!.People.Length} hash={_session.CaptureSnapshot().AuthoritativeHash}");
        }
        if (_interventionCaptureFrame == 16)
        {
            InterventionCaptureImage("individual-gate-completion-festival-continues");
            foreach (var visual in _attendeeVisuals.Values) visual.QueueFree();
            _attendeeVisuals.Clear(); _attendeePickRegistry.Clear();
            _session = GameSession.Restore(_interventionCapturePreparedBaseline!.CapturePersistenceSnapshot()).Session!;
            ClearSelection(); SyncExtraWaterWorld(); ResetLivePerformancePresentation();
            ResetMedicalCuePresentation(); ResetDisorderCuePresentation();
            PreparationStart();
            InterventionCaptureUntil(() => _session.CaptureMedical()!.Stage == MedicalStage.Collapsed, 4000);
            var medical = _session.CaptureMedical()!;
            SelectAttendee(new EntityId(medical.AtRiskGuestId)); FocusMedicalCapturePerson(medical.AtRiskGuestId, 24);
            if (_staffInterventionInspector!.GetParent().GetParent() is ScrollContainer scroll) scroll.ScrollVertical = 0;
            if (_staffInterventionButtons[(ResponseRole.Steward, StaffInterventionAction.GuideToWater)].Disabled == false ||
                _medicalButtons[MedicalAction.DispatchMedic].Disabled)
                throw new InvalidOperationException("Actual collapsed guest must remain selectable and medically actionable, without remote guidance.");
            _preparationMessage = "LABELLED DEMO • fresh baseline, actual collapse; visible and selectable, causal medical clock continues."; RefreshPreparationHud();
        }
        if (_interventionCaptureFrame == 18)
        {
            InterventionCaptureImage("actual-collapse-visible-selectable");
            _medicalButtons[MedicalAction.DispatchMedic].EmitSignal(Button.SignalName.Pressed);
            if (_session.CaptureMedical()!.Needs.Single(item => item.AgentId == _session.CaptureMedical()!.AtRiskGuestId).Intent != MedicalIntent.Collapsed)
                throw new InvalidOperationException("Dispatch must not stand a collapsed guest up remotely.");
            _preparationMessage = "LABELLED DEMO • Riley physically approaching; collapsed guest remains down and medically active."; RefreshPreparationHud();
        }
        if (_interventionCaptureFrame == 20)
        {
            InterventionCaptureImage("collapsed-physical-medic-approach");
            InterventionCaptureUntil(() => _session.CaptureMedical()!.ResponseStage == MedicalResponseStage.Treating, 3000);
            _preparationMessage = "LABELLED DEMO • medic arrived; collapsed guest remains down throughout physical treatment."; RefreshPreparationHud();
        }
        if (_interventionCaptureFrame == 22)
        {
            InterventionCaptureImage("collapsed-physical-treatment");
            InterventionCaptureUntil(() => _session.CaptureMedical()!.ResponseStage == MedicalResponseStage.Completed, 3000);
            if (_session.CaptureMedical()!.Stage != MedicalStage.Treated)
                throw new InvalidOperationException("Physical treatment did not complete before the actual causal deadline.");
            _preparationMessage = "LABELLED DEMO • ordinary physical medic counter completed before death; no remote cure."; RefreshPreparationHud();
        }
        if (_interventionCaptureFrame == 24)
        {
            InterventionCaptureImage("collapsed-counter-completed");
            GD.Print($"STAFF_INTERVENTION_COLLAPSE_CAPTURE treated=True fixture_remote={_session.CaptureMedical()!.DevelopmentInterventionFixturesEnabled} people={_session.CapturePreparation()!.People.Length}");
            GD.Print("STAFF_INTERVENTION_CAPTURE_COMPLETE physical_arrival=true existing_assets=true"); GetTree().Quit();
        }
    }
}

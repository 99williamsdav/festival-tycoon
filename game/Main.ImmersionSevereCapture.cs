using Festival.Simulation;
using Godot;
using System;
using System.Linq;
using System.Reflection;

namespace Festival.Game;

public partial class Main
{
    private int _immersionSevereStep;
    private ulong _immersionSeverePerson;
    private long _immersionSevereCareStart = -1;
    private Label? _immersionSevereFixtureLabel;
    private bool _immersionCloseCareFixture;
    private bool _immersionCloseCareImagePending;
    private bool _immersionCloseCareCaptured;
    private ulong _immersionCloseMedic;
    private double _immersionCloseDistanceMillimetres;
    private int _immersionCloseRenderedFramesRemaining;

    // Explicit CLI-only initialized-exposure visual fixture. It is NOT a route
    // proving ordinary sober drinking reaches severe exposure within 300 seconds.
    // No public gameplay command or player forced-death control is introduced.
    private void ProcessImmersionSevereCapture()
    {
        if (_immersionSevereStep == 0)
        {
            if (++_immersionCaptureFrame < 4) return;
            var layer = new CanvasLayer { Layer = 19 }; AddChild(layer);
            _immersionSevereFixtureLabel = LabelText("INITIALIZED SEVERE EXPOSURE FIXTURE\nNot ordinary drinking reachability", 18, new Color("fff0bd"));
            _immersionSevereFixtureLabel.Position = new Vector2((GetViewport().GetVisibleRect().Size.X - 530) / 2, 125);
            layer.AddChild(_immersionSevereFixtureLabel);
            _programmeDraft = ["act.meadow-lanterns", "act.neon-postcards", "act.field-frequency"];
            RefreshProgrammeControls(); _programmeBook!.EmitSignal(Button.SignalName.Pressed);
            foreach (var offer in new[] { "staff.steward", "equipment.buy" }) _offerButtons[offer].EmitSignal(Button.SignalName.Pressed);
            _immersionStockButton!.EmitSignal(Button.SignalName.Pressed); PreparationStart();
            if (_session.PreparedStatus != PreparationStatus.Running) throw new InvalidOperationException(_preparationMessage);
            TimetableAdvanceTo(1600);
            var medical = _session.CaptureMedical()!;
            StaffCaptureSend(new StaffInterventionCommand(medical.AtRiskGuestId, medical.MedicId, StaffInterventionAction.GuideToRest));
            var fixtureTick = _immersionDepartureFixture ? 23980 : 4000;
            TimetableAdvanceTo(fixtureTick);
            var state = _session.CaptureImmersion()!; var people = _session.CapturePreparation()!.People;
            var target = state.People.FirstOrDefault(p => !p.Abstains && p.Held is null && p.PendingDose == 0 && p.AgentId != medical.AtRiskGuestId &&
                people.Any(person => person.AgentId == p.AgentId && person.Role == ProtectedPersonRole.Guest && person.Admitted && !person.Departed) &&
                _session.CaptureMedical()!.Needs.Single(n => n.AgentId == p.AgentId) is { Stage: MedicalStage.Clear or MedicalStage.Treated, Intent: MedicalIntent.WatchShow });
            if (target is null) throw new InvalidOperationException("No eligible adult for explicit initialized-exposure fixture.");
            _immersionSeverePerson = target.AgentId;
            typeof(GameSession).GetField("_immersion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session,
                state with { People = state.People.Select(p => p.AgentId == target.AgentId ? p with { Intoxication = 9800 } : p).ToArray() });
            TimetableAdvanceTo(_immersionDepartureFixture ? 24001 : fixtureTick + 1); SelectAttendee(new(target.AgentId));
            if (_immersionDepartureFixture && _session.PreparedStatus != PreparationStatus.Departing) throw new InvalidOperationException("Departure care fixture did not reach physical closing.");
            _focus = _attendeeVisuals[new(target.AgentId)].Position; _camera.Size = 12; ApplyCamera();
            GD.Print($"IMMERSION_INITIALIZED_EXPOSURE_FIXTURE person={target.AgentId} exposure=9800 no_item_discard=True ordinary_reachability=False during_departure={_immersionDepartureFixture}");
            _immersionSevereStep = 1; return;
        }
        if (_immersionSevereStep == 1)
        {
            ImmersionImage("initialized-heavy-warning-bars-and-medic-control");
            if (!_liveSetCue!.Text.Contains("HEAVY INTOXICATION") || !_immersionNeedSection!.Visible ||
                !_immersionWarningLabels.TryGetValue(_immersionSeverePerson, out var warning) || !warning.Visible)
                throw new InvalidOperationException("Initialized heavy warning is not visibly represented.");
            if (_immersionCloseCareFixture) { _immersionSevereStep = 4; return; }
            var dispatch = new MedicalCommand(_immersionSeverePerson, MedicalAction.DispatchMedic);
            if (_session.ValidateCommand(CampaignEnvelope(dispatch)) is not null) throw new InvalidOperationException("Ordinary physical medic dispatch unavailable for heavy warning.");
            CommitMedicalAction(MedicalAction.DispatchMedic);
            _immersionSevereStep = 2; return;
        }
        if (_immersionSevereStep == 4)
        {
            var need = _session.CaptureMedical()!.Needs.Single(n => n.AgentId == _immersionSeverePerson);
            if (need.Intent == MedicalIntent.Collapsed && need.Stage is MedicalStage.Collapsed or MedicalStage.Critical)
            {
                ImmersionImage("initialized-collapse-awaiting-close-medic");
                SelectAttendee(new(_immersionSeverePerson)); CommitMedicalAction(MedicalAction.DispatchMedic);
                _immersionSevereStep = 2; return;
            }
            if (_session.CurrentTick > 6000 || _session.PreparedStatus == PreparationStatus.Failed)
                throw new InvalidOperationException("Labelled initialized-exposure fixture did not reach an ordinary collapse window.");
            // Deliberately unattended warning fixture; avoid the separate timetable
            // capture's automatic demonstration operator rescuing before collapse.
            StaffCaptureSend(new SetPausedCommand(false)); _session.AdvanceWithoutSnapshot(40); StaffCaptureSend(new SetPausedCommand(true));
            _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary(); RefreshPreparationHud();
            return;
        }
        if (_immersionSevereStep == 2)
        {
            if (_immersionCloseCareImagePending)
            {
                // Viewport readback contains the previous completed draw. A load
                // reconstructs upright meshes, so allow the existing collapse
                // presentation to draw before reading that completed frame.
                if (--_immersionCloseRenderedFramesRemaining > 0) return;
                var patientBody = _attendeeVisuals[new(_immersionSeverePerson)];
                if (Mathf.Abs(patientBody.Rotation.X - Mathf.Pi / 2) > .05f)
                    throw new InvalidOperationException("Actual collapsed patient presentation has not become horizontal.");
                Pick(_camera.UnprojectPosition(patientBody.ToGlobal(new Vector3(0, 1.45f, 0))));
                if (_selectedAttendeeId?.Value != _immersionSeverePerson)
                    throw new InvalidOperationException("Horizontal patient could not be physically picked beside the close medic.");
                ImmersionImage("actual-close-medic-over-collapsed-patient");
                GD.Print($"IMMERSION_CLOSE_MEDIC physical_treating=True patient={_immersionSeverePerson} medic={_immersionCloseMedic} authoritative_distance_mm={_immersionCloseDistanceMillimetres:0.0} no_visual_snap=True");
                _immersionCloseCareImagePending = false; _immersionCloseCareCaptured = true;
                // This bounded fixture proves bedside arrival and exact reload.
                // Other unchanged full-care fixtures own stabilization/deadlines.
                if (_session.CaptureLifecycleSnapshot()!.Casualties.Count != 0)
                    throw new InvalidOperationException("A casualty preceded the bounded physical bedside proof.");
                GD.Print($"CLOSE_MEDIC_CAPTURE_COMPLETE initialized=True ordinary_reachability=False physical_distance_mm={_immersionCloseDistanceMillimetres:0.0} reload=True no_snap=True patient_horizontal=True patient_pick=True no_casualties=True full_stabilization_not_exercised=True");
                _immersionCaptureCompleted = true; GetTree().Quit(); return;
            }
            var before = _session.CaptureImmersion()!.People.Single(p => p.AgentId == _immersionSeverePerson);
            if (before.CareTicks > 0 && _immersionSevereCareStart < 0)
            {
                _immersionSevereCareStart = _session.CurrentTick;
                PreparationSave(); var hash = _session.CaptureSnapshot().AuthoritativeHash; PreparationLoad();
                if (_session.CaptureSnapshot().AuthoritativeHash != hash) throw new InvalidOperationException("Physical intoxication treatment save/load changed state.");
                SelectAttendee(new(_immersionSeverePerson)); GD.Print($"IMMERSION_SEVERE_CARE_SAVE exact_hash=True ticks={before.CareTicks} exposure={before.Intoxication}");
                if (_immersionCloseCareFixture)
                {
                    var response = _session.GetMedicResponses().Single(job => job.PatientId == _immersionSeverePerson && job.Stage == MedicalResponseStage.Treating);
                    _immersionCloseMedic = response.WorkerId;
                    var navigation = _session.CaptureObservation().NavigationAgents;
                    var medic = navigation.Single(n => n.Id.Value == response.WorkerId);
                    var patient = navigation.Single(n => n.Id.Value == _immersionSeverePerson);
                    _immersionCloseDistanceMillimetres = Math.Sqrt(Math.Pow(medic.XMillimetres - patient.XMillimetres, 2) + Math.Pow(medic.ZMillimetres - patient.ZMillimetres, 2));
                    if (_immersionCloseDistanceMillimetres >= 750 || _session.CaptureMedical()!.Needs.Single(n => n.AgentId == _immersionSeverePerson).Intent != MedicalIntent.Collapsed)
                        throw new InvalidOperationException("Medic started care outside the close collapsed-patient approach.");
                    _focus = _attendeeVisuals[new(_immersionSeverePerson)].Position; _camera.Size = 8; ApplyCamera();
                    _immersionSevereFixtureLabel!.Text = $"INITIALIZED COLLAPSE • CLOSE MEDIC FIXTURE\nPhysical treatment • actual separation {_immersionCloseDistanceMillimetres / 1000:0.00} m";
                    _immersionCloseCareImagePending = true;
                    _immersionCloseRenderedFramesRemaining = 2;
                    return;
                }
            }
            if (_immersionSevereCareStart >= 0 && before.CareTicks == 0 && before.WarningTick < 0)
            {
                if (before.Intoxication <= 0 || _session.CurrentTick - _immersionSevereCareStart < 1500) throw new InvalidOperationException("Care became instant sobriety or completed too early.");
                _preparationSaveBlocked = true; RefreshPreparationHud(); SelectAttendee(new(_immersionSeverePerson));
                _focus = _attendeeVisuals[new(_immersionSeverePerson)].Position; ApplyCamera();
                _immersionSevereFixtureLabel!.Text = "INITIALIZED SEVERE EXPOSURE FIXTURE\nPhysical gradual care • not instant sobriety";
                _immersionSevereStep = 3; return;
            }
            if (_session.PreparedStatus == PreparationStatus.Failed || _session.CurrentTick > (_immersionDepartureFixture ? 33600 : 10000)) throw new InvalidOperationException("Physical intoxication stabilization exceeded fixture bound or failed.");
            if (_immersionDepartureFixture)
            {
                StaffCaptureSend(new SetPausedCommand(false)); _session.AdvanceWithoutSnapshot(40);
                if (_session.PreparedStatus == PreparationStatus.Departing) StaffCaptureSend(new SetPausedCommand(true));
                _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary(); RefreshPreparationHud();
            }
            else TimetableAdvanceTo((int)_session.CurrentTick + 40);
            SelectAttendee(new(_immersionSeverePerson)); return;
        }
        if (_immersionSevereStep == 3)
        {
            if (_immersionCloseCareFixture && !_immersionCloseCareCaptured) throw new InvalidOperationException("Close care was not captured before stabilization.");
            ImmersionImage("physical-gradual-medic-care-retained-exposure");
            var person = _session.CaptureImmersion()!.People.Single(p => p.AgentId == _immersionSeverePerson);
            GD.Print($"IMMERSION_SEVERE_FIXTURE_COMPLETE initialized=True ordinary_reachability=False physical_care=True treatment_reload=True close_care={_immersionCloseCareCaptured} during_departure={_immersionDepartureFixture} exposure_remaining={person.Intoxication} no_casualties={_session.CaptureLifecycleSnapshot()!.Casualties.Count == 0}");
            _immersionCaptureCompleted = true; GetTree().Quit();
        }
    }
}

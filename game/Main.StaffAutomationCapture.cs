using Festival.Simulation;
using Festival.Persistence;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Festival.Game;

public partial class Main
{
    private string? _automationCaptureDirectory;
    private int _automationCaptureFrame;
    private string? _automationCaptureImage;
    private ulong _automationCapturePatient;
    private ulong _automationCaptureFighter;
    private Vector2 _automationCapturePointer;
    private Label? _automationFixtureLabel;
    private bool _automationHoldPlacement;
    private bool _automationScrollStatus;
    private void AutomationCheck(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void AutomationShot(string name, string label)
    {
        _automationFixtureLabel!.Text = label; _automationCaptureImage = name; RefreshPreparationHud();
        _automationScrollStatus = name is "08-medic-treating" or "10-steward-handling-reloaded";
    }
    private void AutomationPositionFixture(ulong id, GridCell cell)
    {
        var agents = (System.Collections.IDictionary)typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_session)!;
        var nav = agents[new EntityId(id)]!; var centre = TraversalGrid.CellCentre(cell);
        void Property(string name, object? value) => nav.GetType().GetProperty(name)!.SetValue(nav, value);
        Property("XMillimetres", centre.XMillimetres); Property("ZMillimetres", centre.ZMillimetres);
        Property("SegmentOriginXMillimetres", centre.XMillimetres); Property("SegmentOriginZMillimetres", centre.ZMillimetres);
        Property("SegmentProgressMicrometres", 0); Property("RouteIndex", 0); Property("MovementRemainder", 0);
        Property("Route", new System.Collections.Generic.List<GridCell>()); Property("Action", AgentNavigationAction.Arrived); Property("Destination", cell);
    }
    private void AutomationVerifyRejectedDiagnosticLoads()
    {
        var original = _session; var hash = original.CaptureSnapshot().AuthoritativeHash;
        var directory = SaveDirectory; var compatibility = _saveCompatibility;
        var restored = GameSession.Restore(original.CapturePersistenceSnapshot());
        AutomationCheck(restored.IsSuccess, "Negative-load fixture clone failed");
        var diagnostic = restored.Session!;
        typeof(GameSession).GetField("_preparation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(diagnostic, diagnostic.CapturePreparation()! with { StaffAutonomyEnabled = false });
        foreach (var header in new[] { compatibility, new SaveCompatibility("0.0.1-r0.05k-unpaid-plan-v1", compatibility.ContentHash, "r0-editable-preparation-v1") })
        {
            var saved = SaveFileAdapter.SaveSlot(directory, "manual-preparation", new SaveWriteRequest(diagnostic, header, "labelled-negative-load-fixture", DateTimeOffset.UtcNow));
            AutomationCheck(saved.IsSuccess, "Negative-load fixture save failed");
            var path = SaveFileAdapter.ResolveSlotPath(directory, "manual-preparation"); var bytes = File.ReadAllBytes(path);
            var valid = SaveFileAdapter.LoadSlot(directory, "manual-preparation", header);
            AutomationCheck(valid.IsSuccess && !valid.Session!.StaffAutonomyEnabled, "Negative-load diagnostic body is not valid in its matching mode");
            PreparationLoad();
            AutomationCheck(ReferenceEquals(original, _session) && hash == _session.CaptureSnapshot().AuthoritativeHash &&
                directory == SaveDirectory && compatibility == _saveCompatibility && bytes.SequenceEqual(File.ReadAllBytes(path)), "Rejected load mutated normal session, namespace or diagnostic file");
            GD.Print($"STAFF_AUTOMATION_REJECTED_LOAD header={header.BuildId} valid_false_autonomy_body=True session_reference_hash_namespace_file_preserved=True");
        }
        PreparationSave();
        AutomationCheck(SaveFileAdapter.LoadSlot(directory, "manual-preparation", compatibility).Session?.StaffAutonomyEnabled == true, "Isolated normal slot was not restored");
    }
    private void ProcessStaffAutomationCapture()
    {
        if (_automationCaptureDirectory is null) return;
        try
        {
            var frame = ++_automationCaptureFrame;
            if (_automationHoldPlacement)
            { HoverMovePointer(_automationCapturePointer); UpdateWaterPlacementPreview(_automationCapturePointer); }
            if (_automationScrollStatus) _hudContextScroll!.ScrollVertical = 100_000;
            if (frame == 1)
            {
                var layer = new CanvasLayer { Layer = 30 }; AddChild(layer);
                _automationFixtureLabel = HudLabel("SCRIPTED NATIVE · no manual desktop QA", 14);
                _automationFixtureLabel.Size = new Vector2(GetViewport().GetVisibleRect().Size.X - 30, 26);
                _automationFixtureLabel.AutowrapMode = TextServer.AutowrapMode.Off;
                _automationFixtureLabel.Position = new Vector2(15, GetViewport().GetVisibleRect().Size.Y - 103);
                layer.AddChild(_automationFixtureLabel);
                GD.Print("STAFF_AUTOMATION_CAPTURE_SETUP normal_editable=True scripted_viewport=True accelerated_ticks=True incident_initialization=labelled one_time_position_fixture=True manual_desktop_QA=False");
            }
            if (frame % 12 == 11 && _automationCaptureImage is { } name)
            {
                if (name == "03-tap-placement-preview") AutomationCheck(_waterPlacementCandidate == new GridCell(104,112) && _waterPlacementIssue is null,
                    $"Settled preview invalid: candidate={_waterPlacementCandidate} issue={_waterPlacementIssue} pointer={_automationCapturePointer} blocks={HudBlocksPlacement(_automationCapturePointer)}");
                var image = GetViewport().GetTexture().GetImage();
                AutomationCheck(image.SavePng(Path.Combine(_automationCaptureDirectory, name + ".png")) == Error.Ok, "Native image save failed");
                GD.Print($"STAFF_AUTOMATION_IMAGE name={name} hash={_session.CaptureSnapshot().AuthoritativeHash} size={image.GetWidth()}x{image.GetHeight()}"); _automationCaptureImage = null;
            }
            if (frame % 12 != 0) return;
            switch (frame / 12)
            {
                case 1:
                    AutomationVerifyRejectedDiagnosticLoads();
                    var perk = _session.CapturePerks()!;
                    CommitEquipmentAction(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand.First(id => id != "another-round")));
                    StaffCaptureSend(new SetPausedCommand(true)); SelectHudTab("Site & water");
                    AutomationCheck(_waterPlaceButton!.Disabled && _waterAdditionReason!.Text.Contains("Requires Another Round"), "Absent tap entitlement reason is not authoritative");
                    AutomationShot("01-tap-unavailable", "NORMAL · Another Round required; unpaid preparation grants no tap"); break;
                case 2:
                    for (ulong seed = 1; ; seed++)
                    {
                        var candidate = GameSession.CreateEditableCampaign(seed); var draft = candidate.CapturePerks()!;
                        if (!draft.Hand.Contains("another-round")) continue;
                        _session = candidate; CommitEquipmentAction(new ChoosePerkCommand(draft.DraftAttempt, draft.Cursor, "another-round")); break;
                    }
                    StaffCaptureSend(new SetPausedCommand(true)); SelectHudTab("Site & water");
                    AutomationCheck(!_waterPlaceButton!.Disabled, "Eligible normal tap button disabled");
                    AutomationShot("02-tap-eligible", "NORMAL · actual Another Round draft chosen; one extra tap available"); break;
                case 3:
                    _waterPlaceButton!.EmitSignal(Button.SignalName.Pressed);
                    AutomationCheck(_waterPlacementMode == WaterPlacementMode.Add, "Normal Add tap did not open placement");
                    CancelWaterPlacement(); AutomationCheck(_session.CaptureWaterPoints().Count == 1, "Cancelling placement created a tap");
                    _waterPlaceButton.EmitSignal(Button.SignalName.Pressed);
                    _focus = new Vector3(-12, 0, -8); _camera.Size = 32; ApplyCamera();
                    var centre = TraversalGrid.CellCentre(new(104, 112)); _automationCapturePointer = _camera.UnprojectPosition(new Vector3(centre.XMillimetres / 1000f, 0, centre.ZMillimetres / 1000f));
                    _automationHoldPlacement = true;
                    AutomationShot("03-tap-placement-preview", "SCRIPTED POINTER · ordinary grass preview after cancel; no fixture entitlement"); break;
                case 4:
                    _automationHoldPlacement = false;
                    CommitWaterPlacement(_automationCapturePointer);
                    AutomationCheck(_session.CaptureWaterPoints().Count == 2 && _waterPlaceButton!.Disabled, "Normal placement did not consume the one tap capacity");
                    PreparationSave(); var hash = _session.CaptureSnapshot().AuthoritativeHash; PreparationLoad();
                    AutomationCheck(hash == _session.CaptureSnapshot().AuthoritativeHash, "Tap save/reload changed state"); SelectHudTab("Site & water");
                    AutomationShot("04-tap-consumed-reloaded", "NORMAL · placed tap saved/reloaded exactly; select it to Move"); break;
                case 5:
                    _hudWorkspaceOpen = false; _hudProgrammeOpen = false; RefreshPreparationHud();
                    var equipment = _session.CaptureEquipment()!; _focus = new Vector3(equipment.XMillimetres / 1000f, 0, equipment.ZMillimetres / 1000f); _camera.Size = 32; ApplyCamera();
                    break;
                case 6:
                    equipment = _session.CaptureEquipment()!;
                    _automationCapturePointer = _camera.UnprojectPosition(new Vector3(equipment.XMillimetres / 1000f, 1.3f, equipment.ZMillimetres / 1000f));
                    HoverMovePointer(_automationCapturePointer); Pick(_automationCapturePointer);
                    AutomationCheck(_selectedGenerator && _contextPanel!.Visible && _stagePowerButton!.Visible, "Generator failed ordinary pick/context resolution");
                    AutomationCheck(_inspectorBody.Text.Contains("trailer stage only") && _stagePowerButton!.Text == "Shut off stage supply", "Generator circuit copy misleading");
                    AutomationShot("05-generator-picked", "SCRIPTED RAY PICK · trailer-stage circuit only; no modeled engine flag"); break;
                case 7:
                    CommitEquipmentAction(new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.field-frequency"])); PreparationAccept("staff.steward");
                    PreparationStart(); StaffCaptureAdvance(1500); _hudProgrammeOpen = false;
                    SelectGenerator(); _stagePowerButton!.EmitSignal(Button.SignalName.Pressed);
                    AutomationCheck(_session.CaptureEquipment() is { Stage: EquipmentStage.Isolated, LoadPercent: 0 } && _stagePowerButton!.Disabled, "Generator action did not share existing isolation state");
                    AutomationShot("06-generator-isolated", "NORMAL ACTION · modeled load zero, overload escalation stopped, stage music interrupted"); break;
                case 8:
                    var medical = _session.CaptureMedical()!;
                    _automationCapturePatient = _session.CapturePreparation()!.People.Where(person => person.Role == ProtectedPersonRole.Guest && person.AgentId != medical.AtRiskGuestId).Skip(9).First().AgentId;
                    AutomationPositionFixture(_automationCapturePatient, new(118, 125));
                    typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session, medical with { Needs = medical.Needs.Select(need => need.AgentId == _automationCapturePatient ? need with {
                        Stage = MedicalStage.Collapsed, CollapseTick = _session.CurrentTick, WarningTick = _session.CurrentTick, Intent = MedicalIntent.Collapsed } : need).ToArray() });
                    _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
                    _focus = new Vector3(-5, 0, -1); _camera.Size = 24; ApplyCamera(); SelectAttendee(new(_automationCapturePatient)); RefreshPreparationHud();
                    AutomationCheck(_medicalButtons[MedicalAction.DispatchMedic].Text == "Send medic" && !_medicalButtons[MedicalAction.DispatchMedic].Disabled, "Generic medic action unavailable for eligible patient");
                    _medicalButtons[MedicalAction.DispatchMedic].EmitSignal(Button.SignalName.Pressed);
                    AutomationCheck(_session.GetMedicResponses().Single().Stage == MedicalResponseStage.Travelling, "Medic did not physically respond");
                    AutomationShot("07-send-medic", "LABELLED COLLAPSE + POSITION INITIALIZATION · normal Send medic; physical travel required"); break;
                case 9:
                    for (var tick = 0; tick < 700 && _session.GetMedicResponses().Single().Stage != MedicalResponseStage.Treating; tick++) StaffCaptureAdvance(1);
                    AutomationCheck(_session.GetMedicResponses().Single().Stage == MedicalResponseStage.Treating, "Medic never physically began treatment");
                    SelectAttendee(new(_session.CaptureMedical()!.MedicId));
                    AutomationCheck(_inspectorBody.Text.Contains("Treating"), "Worker status does not show actual treatment");
                    AutomationShot("08-medic-treating", "ACCELERATED TICKS · actual arrival and treatment clock; worker identity/abilities visible"); break;
                case 10:
                    var disorder = _session.CaptureDisorder()!; var pair = _session.CapturePreparation()!.People.Where(person => person.Role == ProtectedPersonRole.Guest && person.AgentId != _automationCapturePatient).Skip(11).Take(2).Select(person => person.AgentId).ToArray();
                    _automationCaptureFighter = pair[0]; AutomationPositionFixture(disorder.SecurityId, new(119, 178)); AutomationPositionFixture(pair[0], new(121, 178)); AutomationPositionFixture(pair[1], new(121, 179));
                    typeof(GameSession).GetField("_disorder", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session, disorder with { People = disorder.People.Select(person => pair.Contains(person.AgentId) ? person with {
                        Stage = DisorderStage.Argument, Pressure = 8000, Grievance = DisorderGrievance.MusicCutoff, GrievanceTick = _session.CurrentTick, StageTick = _session.CurrentTick } : person).ToArray() });
                    typeof(GameSession).GetMethod("BeginDisorderFight", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_session, [pair[0], pair[1], "labelled:capture-fight"]);
                    _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary(); _focus = new Vector3(-5, 0, 25); _camera.Size = 24; ApplyCamera();
                    SelectAttendee(new(pair[0])); RefreshPreparationHud();
                    AutomationCheck(!_disorderButtons[DisorderAction.DispatchSecurity].Disabled && _disorderButtons[DisorderAction.DispatchSecurity].Text == "Send steward", "Generic steward action unavailable");
                    _disorderButtons[DisorderAction.DispatchSecurity].EmitSignal(Button.SignalName.Pressed);
                    AutomationShot("09-send-steward", "LABELLED FIGHT + POSITION INITIALIZATION · normal Send steward; one reciprocal claim"); break;
                case 11:
                    for (var tick = 0; tick < 500 && _session.CaptureDisorder()!.Incidents.Last().HandlingAttempt is null; tick++) StaffCaptureAdvance(1);
                    AutomationCheck(_session.CaptureDisorder()!.Incidents.Last().HandlingAttempt is { Outcome: FightHandlingOutcome.Handling }, "Steward never physically began fight handling");
                    SelectAttendee(new(_session.CaptureDisorder()!.SecurityId));
                    AutomationCheck(_inspectorBody.Text.Contains("Handling"), "Worker status does not show physical handling");
                    PreparationSave(); var handlingHash = _session.CaptureSnapshot().AuthoritativeHash; PreparationLoad();
                    AutomationCheck(handlingHash == _session.CaptureSnapshot().AuthoritativeHash, "Handling save/reload changed state"); SelectAttendee(new(_session.CaptureDisorder()!.SecurityId));
                    AutomationShot("10-steward-handling-reloaded", "ACCELERATED TICKS · physical handling saved/reloaded; original fight deadline remains"); break;
                case 12:
                    GD.Print($"STAFF_AUTOMATION_CAPTURE_COMPLETE images=10 normal_autonomy={_session.StaffAutonomyEnabled} hash={_session.CaptureSnapshot().AuthoritativeHash} manual_desktop_QA=False"); GetTree().Quit(); break;
            }
        }
        catch (Exception error) { GD.PrintErr($"STAFF_AUTOMATION_CAPTURE_FAILED {error}"); GetTree().Quit(2); }
    }
}

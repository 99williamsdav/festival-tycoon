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
    private string? _waterPlaytestCaptureDirectory;
    private int _waterPlaytestCaptureFrame;
    private GameSession? _waterPlaytestPreparedBaseline;

    private void WaterPlaytestImage(string name) => GetViewport().GetTexture().GetImage()
        .SavePng(Path.Combine(_waterPlaytestCaptureDirectory!, name + ".png"));

    private void RestoreWaterPlaytestPreparedBaseline()
    {
        foreach (var visual in _attendeeVisuals.Values) visual.QueueFree();
        _attendeeVisuals.Clear(); _attendeePickRegistry.Clear();
        _session = GameSession.Restore(_waterPlaytestPreparedBaseline!.CapturePersistenceSnapshot()).Session!;
        ClearSelection(); SyncExtraWaterWorld(); ResetLivePerformancePresentation();
        ResetMedicalCuePresentation(); ResetDisorderCuePresentation();
        _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
    }

    private void ProcessWaterPlaytestCapture()
    {
        if (_waterPlaytestCaptureDirectory is null) return;
        _waterPlaytestCaptureFrame++;
        if (_waterPlaytestCaptureFrame == 4)
        {
            StaffCaptureSend(new SetPausedCommand(true));
            _focus = new Vector3(-16, 0, -2); _camera.Size = 27; ApplyCamera();
            Pick(_camera.UnprojectPosition(WaterVisualPosition(_session.CaptureWaterPoints().Single(point => point.Id == "water.main")) + new Vector3(0, 1.05f, 0)));
            if (_selectedMedicalFacility != MedicalFacility.Water || _selectedWaterPointId != "water.main")
                throw new InvalidOperationException("Centred standpipe ray pick failed.");
            _preparationMessage = "LABELLED PLAYTEST CORRECTION DEMO • selected tap centred; per-tap Move.";
            RefreshPreparationHud();
        }
        if (_waterPlaytestCaptureFrame == 6)
        {
            WaterPlaytestImage("centred-selected-normal");
            _selectedWaterMoveButton!.EmitSignal(Button.SignalName.Pressed);
            RotateWaterPlacement(1);
            var centre = TraversalGrid.CellCentre(new GridCell(104, 117));
            UpdateWaterPlacementPreview(_camera.UnprojectPosition(new Vector3(centre.XMillimetres / 1000f, 0, centre.ZMillimetres / 1000f)));
            if (_waterPlacementIssue is not null || _waterPlacementPreview?.Visible != true)
                throw new InvalidOperationException("Rotated move preview is not valid: " + _waterPlacementIssue);
        }
        if (_waterPlaytestCaptureFrame == 8)
        {
            WaterPlaytestImage("rotated-move-preview");
            var centre = TraversalGrid.CellCentre(new GridCell(104, 117));
            CommitWaterPlacement(_camera.UnprojectPosition(new Vector3(centre.XMillimetres / 1000f, 0, centre.ZMillimetres / 1000f)));
            var main = _session.CaptureWaterPoints().Single(point => point.Id == "water.main");
            if (main.Cell != new GridCell(104, 117) || main.QuarterTurns != 1)
                throw new InvalidOperationException("Rotated main move was not committed.");
            StaffCaptureSend(new PlaceWaterPointCommand(new GridCell(72, 120), 3));
            SyncExtraWaterWorld();
            var extra = _session.CaptureWaterPoints().Single(point => point.Id == "water.extra-1");
            _focus = WaterVisualPosition(extra); _camera.Size = 25; ApplyCamera();
            Pick(_camera.UnprojectPosition(WaterVisualPosition(extra) + new Vector3(0, 1.05f, 0)));
            if (_selectedMedicalFacility != MedicalFacility.Water || _selectedWaterPointId != extra.Id)
                throw new InvalidOperationException("Extra tap ray pick failed.");
            _preparationMessage = "LABELLED DEMO • each extra tap has its own Move; no full-tail reservation."; RefreshPreparationHud();
        }
        if (_waterPlaytestCaptureFrame == 10)
        {
            WaterPlaytestImage("extra-tap-context-move");
            foreach (var offer in new[] { "act.folk", "staff.steward", "equipment.buy" }) _offerButtons[offer].EmitSignal(Button.SignalName.Pressed);
            _waterPlaytestPreparedBaseline = GameSession.Restore(_session.CapturePersistenceSnapshot()).Session!;
            PreparationStart();
            StaffCaptureSend(LegacyMedicalCaptureFixture(_session.CaptureMedical()!.AtRiskGuestId, MedicalAction.GuideToRest));
            StaffCaptureAdvance(1500);
            // Labelled thirst-only visual fixture: physical positions/routes are unchanged.
            // Long refills make the organically grown line readable in a paused screenshot.
            var guestIds = _session.CapturePreparation()!.People.Where(person => person.Role == ProtectedPersonRole.Guest).Take(8).Select(person => person.AgentId).ToArray();
            var medical = _session.CaptureMedical()!;
            typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session,
                medical with { Needs = medical.Needs.Select(need => guestIds.Contains(need.AgentId) ? need with { Thirst = 10_000 } : need).ToArray() });
            foreach (var id in guestIds)
                StaffCaptureSend(LegacyMedicalCaptureFixture(id, MedicalAction.GuideToWater));
            StaffCaptureAdvance(900);
            var busiest = _session.CaptureWaterPoints().OrderByDescending(point => point.Queue.Length + point.Overflow.Length).First();
            SelectMedicalFacility(MedicalFacility.Water, busiest.Id);
            _focus = WaterVisualPosition(busiest); _camera.Size = 20; ApplyCamera();
            _preparationMessage = "LABELLED THIRST FIXTURE • real routes, arrival FIFO and organically grown queue. No teleport."; RefreshPreparationHud();
        }
        if (_waterPlaytestCaptureFrame == 12)
        {
            WaterPlaytestImage("organic-physical-line");
            var snapshot = _session.CaptureSnapshot();
            var saved = SaveFileAdapter.SaveSlot(SaveDirectory, "manual-water-playtest", new SaveWriteRequest(_session, _saveCompatibility, "r005c-labelled-fixture", DateTimeOffset.UtcNow));
            var loaded = SaveFileAdapter.LoadSlot(SaveDirectory, "manual-water-playtest", _saveCompatibility);
            if (!saved.IsSuccess || !loaded.IsSuccess || loaded.Session!.CaptureSnapshot().AuthoritativeHash != snapshot.AuthoritativeHash)
                throw new InvalidOperationException("Organic line saved state did not round-trip.");
            var main = _session.CaptureWaterPoints().Single(point => point.Id == "water.main");
            if (_session.CaptureWaterPoints().Sum(point => point.Queue.Length + point.Overflow.Length) < 3)
                throw new InvalidOperationException("Rendered organic fixture did not produce a multi-person physical line.");
            GD.Print($"WATER_PLAYTEST_CAPTURE centred=True rotated=1 selected_extra_move=True queue={main.Queue.Length} overflow={main.Overflow.Length} cells={_session.CaptureWaterQueueCells(main.Id).Count} saved=True hash={snapshot.AuthoritativeHash}");
            GD.Print($"WATER_PLAYTEST_LINES {string.Join(';', _session.CaptureWaterPoints().Select(point => $"{point.Id}:queue={point.Queue.Length},overflow={point.Overflow.Length},owner={point.OwnerId}"))}");
        }
        if (_waterPlaytestCaptureFrame == 14)
        {
            RestoreWaterPlaytestPreparedBaseline();
            CommitEquipmentAction(new ApplyWaterFoundationEffectCommand("water.tower"));
            SelectMedicalFacility(MedicalFacility.Water, "water.main");
            _focus = WaterVisualPosition(_session.CaptureWaterPoints().Single(point => point.Id == "water.main"));
            _camera.Size = 25; ApplyCamera();
            _preparationMessage = "LABELLED FLOW DEMO • tower alone boosts every personal rate by4; no tap-count penalty."; RefreshPreparationHud();
        }
        if (_waterPlaytestCaptureFrame == 16)
        {
            WaterPlaytestImage("flow-boosted-tower");
            CommitEquipmentAction(new CommitCommunityWaterShareCommand());
            StaffCaptureSend(new StartPreparedEditionCommand()); StaffCaptureSend(new SetPausedCommand(true));
            BuildAttendee();
            _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
            SelectMedicalFacility(MedicalFacility.Water, "water.main");
            _preparationMessage = "LABELLED FLOW DEMO • Council cap12 then tower+4: NORMAL with both modifiers."; RefreshPreparationHud();
        }
        if (_waterPlaytestCaptureFrame == 18)
        {
            WaterPlaytestImage("flow-normal-council-plus-tower");
            RestoreWaterPlaytestPreparedBaseline();
            SyncExtraWaterWorld(); CommitEquipmentAction(new CommitCommunityWaterShareCommand());
            StaffCaptureSend(new StartPreparedEditionCommand()); StaffCaptureSend(new SetPausedCommand(true));
            BuildAttendee();
            _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
            SelectMedicalFacility(MedicalFacility.Water, "water.main");
            _preparationMessage = "LABELLED FLOW DEMO • Council sharing alone lowers faster drinkers to baseline cap12."; RefreshPreparationHud();
        }
        if (_waterPlaytestCaptureFrame == 20)
        {
            WaterPlaytestImage("flow-low-council");
            GD.Print("WATER_PLAYTEST_CAPTURE_COMPLETE labelled_fixture=true flow_states=3 approved_icons=48px"); GetTree().Quit();
        }
    }
}

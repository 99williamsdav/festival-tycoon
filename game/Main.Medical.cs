using Festival.Simulation;
using Festival.Persistence;
using Godot;
using System;
using System.IO;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private Label? _medicalSummary;
    private readonly System.Collections.Generic.Dictionary<MedicalAction, Button> _medicalButtons = [];
    private string? _medicalCaptureDirectory;
    private string _medicalCaptureMode = "prevent";
    private int _medicalCaptureFrame;
    private Label3D? _medicalWorldAlert;

    private void BuildMedicalWorld()
    {
        static Vector3 At(GridCell cell)
        {
            var centre = TraversalGrid.CellCentre(cell);
            return new Vector3(centre.XMillimetres / 1000f, 0, centre.ZMillimetres / 1000f);
        }
        AddAsset("res://assets/environment/lwf_free_water_point_v1.glb", At(GameSession.MedicalWaterCell));
        AddAsset("res://assets/environment/lwf_first_aid_point_v2.glb", At(GameSession.MedicalTentCell));
        AddChild(new Label3D { Text = "FREE WATER", Position = At(GameSession.MedicalWaterCell) + new Vector3(0, 2.65f, 0),
            FontSize = 45, PixelSize = .009f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
        AddChild(new Label3D { Text = "FIRST AID", Position = At(GameSession.MedicalTentCell) + new Vector3(0, 3.1f, 0),
            FontSize = 45, PixelSize = .009f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
        _medicalWorldAlert = new Label3D { Text = "HOT", Position = At(GameSession.MedicalWaterCell) + new Vector3(0, 3.4f, 0),
            FontSize = 52, PixelSize = .009f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = new Color("e8a34d") };
        AddChild(_medicalWorldAlert);
    }

    private void BuildMedicalControls(VBoxContainer box)
    {
        _medicalSummary = LabelText("", 14, new Color("804126"));
        _medicalSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _medicalSummary.CustomMinimumSize = new Vector2(370, 165);
        box.AddChild(_medicalSummary);
        foreach (var (action, label) in new[] {
            (MedicalAction.GuideToWater, "GUIDE TO FREE WATER"), (MedicalAction.GuideToRest, "GUIDE TO REST"),
            (MedicalAction.DispatchMedic, "DISPATCH RILEY"), (MedicalAction.SafeRemove, "SAFE REMOVE"),
            (MedicalAction.ReturnToShow, "LEAVE WATER QUEUE") })
        {
            var button = ButtonText(label, () => CommitMedicalAction(action));
            button.AddThemeFontSizeOverride("font_size", 12);
            box.AddChild(button); _medicalButtons.Add(action, button);
        }
    }

    private ulong MedicalSelectedGuest()
    {
        var medical = _session.CaptureMedical()!;
        return _selectedAttendeeId is { } selected && medical.Needs.Any(item => item.AgentId == selected.Value)
            ? selected.Value : medical.AtRiskGuestId;
    }

    private void CommitMedicalAction(MedicalAction action)
    {
        var command = new MedicalCommand(MedicalSelectedGuest(), action);
        var result = MedicalCommandCoordinator.Execute(SaveDirectory, _session, command, _saveCompatibility,
            DateTimeOffset.UtcNow, _autosaveGeneration);
        if (result.IsSuccess)
        {
            _session = result.Session; _autosaveGeneration++;
            _preparationSaveBlocked = false;
            _preparationMessage = "Medical action committed and autosaved.";
        }
        else { _preparationMessage = result.Error!; if (result.Autosave is not null) _preparationSaveBlocked = true; }
        RefreshPreparationHud();
    }

    private void RefreshMedicalControls()
    {
        if (_medicalSummary is null || _session.CaptureMedical() is not { } m) return;
        var target = m.Needs.Single(item => item.AgentId == m.AtRiskGuestId);
        var selected = m.Needs.Single(item => item.AgentId == MedicalSelectedGuest());
        string Remaining(long dueTick) => $"{Math.Max(0, dueTick - _session.CurrentTick) / 80m:0.0}s";
        var clock = m.Stage switch
        {
            MedicalStage.Distress => $"collapse {Remaining(m.WarningTick + GameSession.MedicalCollapseDelayTicks)} • death {Remaining(m.WarningTick + GameSession.MedicalCollapseDelayTicks + GameSession.MedicalDeathDelayTicks)}",
            MedicalStage.Collapsed => $"critical {Remaining(m.CollapseTick + GameSession.MedicalCriticalDelayTicks)} • death {Remaining(m.CollapseTick + GameSession.MedicalDeathDelayTicks)}",
            MedicalStage.Critical => $"death {Remaining(m.CollapseTick + GameSession.MedicalDeathDelayTicks)}",
            MedicalStage.Clear => "no active response window",
            _ => "window settled"
        };
        var treatment = m.ResponseStage == MedicalResponseStage.Treating
            ? $"Treatment {Math.Clamp((_session.CurrentTick - m.ResponseStartedTick) * 100 / GameSession.MedicalTreatmentTicks, 0, 100)}% • {Remaining(m.ResponseStartedTick + GameSession.MedicalTreatmentTicks)} left"
            : m.ResponseStage == MedicalResponseStage.Travelling ? "Medic travelling • treatment begins on arrival" : m.Response;
        _medicalSummary.Text = $"HOT • FREE WATER • FIRST AID\n" +
            $"Guest 20: {m.Stage} • thirst {target.Thirst / 100m:0}% • heat {target.HeatExposure / 100m:0}%\n" +
            $"Water queue {m.WaterQueue.Length} • refill {GameSession.MedicalWaterServiceTicks / 80m:0}s • medic {m.ResponseStage}\n" +
            $"Clock: {clock}\n{treatment}\n" +
            $"Selected: {selected.Intent} • {selected.Reason}";
        foreach (var (action, button) in _medicalButtons)
            button.Disabled = _session.ValidateCommand(CampaignEnvelope(new MedicalCommand(selected.AgentId, action))) is not null;
        if (_medicalWorldAlert is not null) _medicalWorldAlert.Text = m.Stage switch
        {
            MedicalStage.Distress => "HOT • DISTRESS",
            MedicalStage.Collapsed => "MEDICAL • COLLAPSE",
            MedicalStage.Critical => "MEDICAL • CRITICAL",
            MedicalStage.Terminal => "MEDICAL • HEARING",
            MedicalStage.Treated or MedicalStage.Removed => "MEDICAL • SAFE",
            _ => "HOT"
        };
    }

    private void ProcessMedicalCapture()
    {
        if (_medicalCaptureDirectory is null) return;
        _medicalCaptureFrame++;
        if (_medicalCaptureFrame == 4)
            foreach (var id in new[] { "act.folk", "staff.steward", "equipment.buy" }) _offerButtons[id].EmitSignal(Button.SignalName.Pressed);
        if (_medicalCaptureFrame == 6) _preparationStart.EmitSignal(Button.SignalName.Pressed);
        if (_medicalCaptureFrame == 8)
        {
            _session.AdvanceWithoutSnapshot(2_000);
            _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
            _focus = new Vector3(1, 0, 15); _camera.Size = 35; ApplyCamera(); RefreshPreparationHud();
            GD.Print($"MEDICAL_CAPTURE warning={_session.CaptureMedical()?.Stage} queue={_session.CaptureMedical()?.WaterQueue.Length}");
        }
        if (_medicalCaptureFrame == 10)
        {
            _orientation = 2; _focus = new Vector3(4, 0, 12); _camera.Size = 24; ApplyCamera();
        }
        if (_medicalCaptureFrame == 12)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "water-queue.png"));
            _orientation = 0; _focus = new Vector3(1, 0, 15); _camera.Size = 35; ApplyCamera();
        }
        if (_medicalCaptureFrame == 16)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "warning.png"));
            if (_medicalCaptureMode == "prevent") CommitMedicalAction(MedicalAction.DispatchMedic);
        }
        if (_medicalCaptureFrame == 18)
        {
            if (_medicalCaptureMode == "prevent")
            {
                while (_session.CaptureMedical()!.ResponseStage == MedicalResponseStage.Travelling &&
                    _session.CurrentTick < 4_000) _session.AdvanceWithoutSnapshot(1);
                _session.AdvanceWithoutSnapshot(GameSession.MedicalTreatmentTicks / 2);
                _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
                RefreshPreparationHud();
                GD.Print($"MEDICAL_CAPTURE treatment={_session.CaptureMedical()?.ResponseStage}");
            }
        }
        if (_medicalCaptureFrame == 20 && _medicalCaptureMode == "prevent")
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "treatment.png"));
        if (_medicalCaptureFrame == 22)
        {
            _session.AdvanceWithoutSnapshot(checked((int)(6_200 - _session.CurrentTick)));
            _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
            RefreshPreparationHud();
            GD.Print($"MEDICAL_CAPTURE outcome={_session.CaptureMedical()?.Stage} casualties={_session.CaptureLifecycleSnapshot()?.Casualties.Count}");
        }
        if (_medicalCaptureFrame == 26)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "outcome.png"));
            GetTree().Quit();
        }
    }
}

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
    private ulong _medicalCaptureWaterCueId;
    private ulong _medicalCaptureTradeoffCueId;
    private readonly MedicalCuePlanner _medicalCuePlanner = new();
    private readonly System.Collections.Generic.Dictionary<ulong, Label3D> _medicalCueLabels = [];
    private VBoxContainer? _medicalNeedsBars;
    private ProgressBar? _medicalThirstBar;
    private ProgressBar? _medicalHeatBar;
    private enum MedicalFacility { Water, FirstAid }
    private readonly System.Collections.Generic.Dictionary<ulong, MedicalFacility> _medicalFacilityPicks = [];
    private MedicalFacility? _selectedMedicalFacility;

    private void BuildMedicalWorld()
    {
        static Vector3 At(GridCell cell)
        {
            var centre = TraversalGrid.CellCentre(cell);
            return new Vector3(centre.XMillimetres / 1000f, 0, centre.ZMillimetres / 1000f);
        }
        // The narrow standpipe has no v1-style approach pad. Put its tap within
        // arm's reach of the existing front queue slot without moving that slot.
        var waterPosition = At(GameSession.MedicalWaterCell) + new Vector3(0, 0, 1.9f);
        AddAsset("res://assets/environment/lwf_free_water_point_v4.glb", waterPosition);
        AddAsset("res://assets/environment/lwf_first_aid_point_v2.glb", At(GameSession.MedicalTentCell));
        RegisterMedicalPick(MedicalFacility.Water, waterPosition + new Vector3(0, 1.05f, 0), new Vector3(2.3f, 2.1f, 1.1f));
        RegisterMedicalPick(MedicalFacility.FirstAid, At(GameSession.MedicalTentCell) + new Vector3(0, 1.35f, 0), new Vector3(3.5f, 2.7f, 3.5f));
        AddChild(new Label3D { Text = "FIRST AID", Position = At(GameSession.MedicalTentCell) + new Vector3(0, 3.1f, 0),
            FontSize = 45, PixelSize = .009f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
    }

    private void ResetMedicalCuePresentation()
    {
        foreach (var label in _medicalCueLabels.Values) label.QueueFree();
        _medicalCueLabels.Clear();
        var medical = _session.CaptureMedical();
        _medicalCuePlanner.Reset(medical, _session.CurrentTick);
        if (medical is null) return;
        foreach (var need in medical.Needs)
        {
            if (!_attendeeVisuals.ContainsKey(new EntityId(need.AgentId))) continue;
            var label = new Label3D { Visible = false, FontSize = 42, PixelSize = .010f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                OutlineSize = 14, OutlineModulate = new Color("2b2825") };
            AddChild(label);
            _medicalCueLabels.Add(need.AgentId, label);
        }
    }

    private void AdvanceMedicalCuePresentation()
    {
        var medical = _session.CaptureMedical();
        if (medical is null) return;
        foreach (var label in _medicalCueLabels.Values) label.Visible = false;
        foreach (var cue in _medicalCuePlanner.Observe(medical, _session.CurrentTick))
        {
            if (!_medicalCueLabels.TryGetValue(cue.AgentId, out var label) ||
                !_attendeeVisuals.TryGetValue(new EntityId(cue.AgentId), out var visual)) continue;
            label.Text = cue.Text;
            label.Position = visual.Position + new Vector3(0, cue.Urgent ? 2.7f : 2.35f, 0);
            label.Modulate = cue.Urgent ? new Color("ffdb73") : new Color("fff7e1");
            label.Visible = true;
        }
    }

    private void RegisterMedicalPick(MedicalFacility facility, Vector3 position, Vector3 size)
    {
        var body = new StaticBody3D { Position = position, CollisionLayer = 1, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body);
        _medicalFacilityPicks.Add(body.GetInstanceId(), facility);
    }

    private void SelectMedicalFacility(MedicalFacility facility)
    {
        _selected = null; _selectedAttendeeId = null; _selectedMedicalFacility = facility;
        if (_medicalNeedsBars is not null) _medicalNeedsBars.Visible = false;
        var cell = facility == MedicalFacility.Water ? GameSession.MedicalWaterCell : GameSession.MedicalTentCell;
        var centre = TraversalGrid.CellCentre(cell);
        _highlight.Position = new Vector3(centre.XMillimetres / 1000f, .08f, centre.ZMillimetres / 1000f);
        var radius = facility == MedicalFacility.Water ? 2.5f : 4f;
        _highlight.Scale = new Vector3(radius, 1, radius); _highlight.Visible = true;
        RefreshMedicalFacilityInspector();
        GD.Print($"MEDICAL_FACILITY_SELECTED type={facility}");
    }

    private void BuildMedicalNeedBars(VBoxContainer parent)
    {
        if (_session.CaptureMedical() is null) return;
        _medicalNeedsBars = new VBoxContainer { Visible = false };
        parent.AddChild(_medicalNeedsBars);
        _medicalNeedsBars.AddChild(LabelText("THIRST", 12, new Color("8b5835")));
        _medicalThirstBar = new ProgressBar { MaxValue = 10_000, ShowPercentage = false,
            CustomMinimumSize = new Vector2(375, 13), Modulate = new Color("459ad1") };
        _medicalNeedsBars.AddChild(_medicalThirstBar);
        _medicalNeedsBars.AddChild(LabelText("HEAT EXPOSURE", 12, new Color("8b5835")));
        _medicalHeatBar = new ProgressBar { MaxValue = 10_000, ShowPercentage = false,
            CustomMinimumSize = new Vector2(375, 13), Modulate = new Color("e58a46") };
        _medicalNeedsBars.AddChild(_medicalHeatBar);
    }

    private void RefreshMedicalNeedBars(MedicalNeed? need)
    {
        if (_medicalNeedsBars is null) return;
        _medicalNeedsBars.Visible = need is not null;
        if (need is null) return;
        _medicalThirstBar!.Value = need.Thirst;
        _medicalHeatBar!.Value = need.HeatExposure;
    }

    private void RefreshMedicalFacilityInspector()
    {
        if (_selectedMedicalFacility is not { } facility || _session.CaptureMedical() is not { } m) return;
        var people = _session.CapturePreparation()!.People;
        if (facility == MedicalFacility.Water)
        {
            _inspectorTitle.Text = "Free water • WATER";
            var owner = m.WaterOwnerId is { } id ? people.Single(item => item.AgentId == id).Name : "None";
            _inspectorBody.Text = $"FREE • no stock or payment\nQUEUE  {m.WaterQueue.Length}/10 • VISIBLE TAIL  {m.WaterOverflow.Length}\n" +
                $"DRINKING  {owner}\nRELIEF  thirst -{GameSession.MedicalDrinkThirstPerTick}/tick • heat -{GameSession.MedicalDrinkHeatPerTick}/tick\n" +
                "Select a person, then use GUIDE TO FREE WATER in the HOT panel.";
        }
        else
        {
            _inspectorTitle.Text = "First aid • Riley Hart";
            _inspectorBody.Text = $"MEDIC  {m.ResponseStage}\nPATIENT  " +
                (m.ResponsePatientId is { } id ? people.Single(item => item.AgentId == id).Name : "None") +
                $"\nRESPONSE  {m.Response}\nREST  shade reduces heat after arrival\n" +
                "Select a distressed person, then DISPATCH RILEY or GUIDE TO REST in the HOT panel.";
        }
    }

    private void CapturePickMedicalFacility(MedicalFacility facility)
    {
        var cell = facility == MedicalFacility.Water ? GameSession.MedicalWaterCell : GameSession.MedicalTentCell;
        var centre = TraversalGrid.CellCentre(cell);
        var point = new Vector3(centre.XMillimetres / 1000f, 1.6f,
            centre.ZMillimetres / 1000f + (facility == MedicalFacility.Water ? 1.9f : 0f));
        Pick(_camera.UnprojectPosition(point));
        if (_selectedMedicalFacility != facility)
            throw new InvalidOperationException($"Capture ray did not select {facility}.");
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
        var drinking = m.WaterOwnerId is { } owner
            ? $"{_session.CapturePreparation()!.People.Single(item => item.AgentId == owner).Name} drinking • " +
              $"thirst {m.Needs.Single(item => item.AgentId == owner).Thirst / 100m:0}% • {m.WaterDrinkTicks / 80m:0.0}s"
            : "tap ready • one at a time";
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
        var selectedStage = selected.AgentId == m.AtRiskGuestId ? m.Stage : selected.Stage;
        _medicalSummary.Text = $"HOT • FREE WATER • FIRST AID\n" +
            $"Guest 20: {m.Stage} • thirst {target.Thirst / 100m:0}% • heat {target.HeatExposure / 100m:0}%\n" +
            $"Water queue {m.WaterQueue.Length} + tail {m.WaterOverflow.Length} • {drinking}\nMedic {m.ResponseStage} • Clock: {clock}\n" +
            $"{treatment}\n" +
            $"Selected: {selectedStage} / {selected.Intent} • {selected.Reason}";
        foreach (var (action, button) in _medicalButtons)
            button.Disabled = _session.ValidateCommand(CampaignEnvelope(new MedicalCommand(selected.AgentId, action))) is not null;
        RefreshMedicalFacilityInspector();
    }

    private void ProcessMedicalCapture()
    {
        if (_medicalCaptureDirectory is null) return;
        _medicalCaptureFrame++;
        if (_medicalCaptureFrame == 4)
            foreach (var id in new[] { "act.folk", "staff.steward", "equipment.buy" }) _offerButtons[id].EmitSignal(Button.SignalName.Pressed);
        if (_medicalCaptureFrame == 6) _preparationStart.EmitSignal(Button.SignalName.Pressed);
        if (_medicalCaptureFrame == 7 && _medicalCaptureMode == "line")
        {
            foreach (var person in _session.CapturePreparation()!.People.Where(item => item.Role == ProtectedPersonRole.Guest).Skip(8).Take(6))
            {
                var result = _session.Execute(CampaignEnvelope(new MedicalCommand(person.AgentId, MedicalAction.GuideToWater)));
                if (!result.IsAccepted) throw new InvalidOperationException($"Line fixture could not guide {person.Name}: {result.Message}");
            }
        }
        if (_medicalCaptureFrame == 8)
        {
            if (_medicalCaptureMode == "line")
            {
                while (_session.CaptureMedical()!.WaterQueue.Length < 5 && _session.CurrentTick < 5_200)
                    _session.AdvanceWithoutSnapshot(1);
                if (_session.CaptureMedical()!.WaterQueue.Length < 5)
                    throw new InvalidOperationException("Line fixture did not assemble five physical queue members.");
            }
            else if (_medicalCaptureMode == "cues")
            {
                while (_session.CurrentTick < 1_600)
                {
                    _session.AdvanceWithoutSnapshot(1);
                    var state = _session.CaptureMedical()!;
                    if (state.Needs.Any(item => item.Intent == MedicalIntent.SeekWater) &&
                        state.Needs.Single(item => item.AgentId == state.AtRiskGuestId).Reason.StartsWith("Watching band:", StringComparison.Ordinal))
                        break;
                }
                var stateAtDecision = _session.CaptureMedical()!;
                if (!stateAtDecision.Needs.Any(item => item.Intent == MedicalIntent.SeekWater) ||
                    !stateAtDecision.Needs.Single(item => item.AgentId == stateAtDecision.AtRiskGuestId).Reason.StartsWith("Watching band:", StringComparison.Ordinal))
                    throw new InvalidOperationException("Cue fixture did not reach an autonomous water/show tradeoff.");
                _medicalCaptureTradeoffCueId = stateAtDecision.Needs.First(item => item.Thirst >= 6_500 &&
                    item.Reason.StartsWith("Watching band:", StringComparison.Ordinal)).AgentId;
                FocusMedicalCapturePerson(_medicalCaptureTradeoffCueId, 24f);
            }
            else _session.AdvanceWithoutSnapshot(2_000);
            _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
            RefreshPreparationHud();
            GD.Print($"MEDICAL_CAPTURE warning={_session.CaptureMedical()?.Stage} queue={_session.CaptureMedical()?.WaterQueue.Length} tick={_session.CurrentTick}");
        }
        if (_medicalCaptureMode == "cues")
        {
            var medical = _session.CaptureMedical()!;
            if (_medicalCaptureFrame == 12)
            {
                var id = _medicalCaptureTradeoffCueId;
                if (!_medicalCueLabels[id].Visible || !_medicalCueLabels[id].Text.Contains("don't"))
                    throw new InvalidOperationException("Tradeoff cue was not visible over its person.");
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "tradeoff.png"));
                GD.Print($"MEDICAL_CAPTURE tradeoff-person={id} tick={_session.CurrentTick}");
            }
            if (_medicalCaptureFrame == 13)
            {
                _medicalCaptureWaterCueId = medical.Needs.First(item => item.Intent == MedicalIntent.SeekWater).AgentId;
                _session.AdvanceWithoutSnapshot(MedicalCuePlanner.RoutineDurationTicks + 8);
                _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
                FocusMedicalCapturePerson(_medicalCaptureWaterCueId, 24f);
                RefreshPreparationHud();
            }
            if (_medicalCaptureFrame == 16)
            {
                if (!_medicalCueLabels[_medicalCaptureWaterCueId].Visible ||
                    !_medicalCueLabels[_medicalCaptureWaterCueId].Text.Contains("going to get water"))
                    throw new InvalidOperationException("Water decision cue was not visible over its person.");
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "water-decision.png"));
                GD.Print($"MEDICAL_CAPTURE water-decision-person={_medicalCaptureWaterCueId} tick={_session.CurrentTick}");
            }
            if (_medicalCaptureFrame == 17)
            {
                while (_session.CaptureMedical()!.Stage == MedicalStage.Clear && _session.CurrentTick < 3_000)
                    _session.AdvanceWithoutSnapshot(1);
                if (_session.CaptureMedical()!.Stage != MedicalStage.Distress)
                    throw new InvalidOperationException("Distress cue fixture did not reach distress.");
                _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
                FocusMedicalCapturePerson(_session.CaptureMedical()!.AtRiskGuestId, 24f);
                RefreshPreparationHud();
            }
            if (_medicalCaptureFrame == 20)
            {
                var id = medical.AtRiskGuestId;
                if (!_medicalCueLabels[id].Visible || !_medicalCueLabels[id].Text.Contains("collapse"))
                    throw new InvalidOperationException("Urgent distress cue was not visible over its person.");
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "distress.png"));
                GD.Print($"MEDICAL_CAPTURE distress-person={id} tick={_session.CurrentTick}");
            }
            if (_medicalCaptureFrame == 21)
            {
                var id = new EntityId(medical.AtRiskGuestId);
                Pick(_camera.UnprojectPosition(_attendeeVisuals[id].GlobalPosition + new Vector3(0, .85f, 0)));
                if (_selectedAttendeeId != id) throw new InvalidOperationException("Person cue interfered with attendee picking.");
                GD.Print($"MEDICAL_CAPTURE person-pick={id.Value} selected=True");
                GetTree().Quit();
            }
            return;
        }
        if (_medicalCaptureMode == "water-v4")
        {
            if (_medicalCaptureFrame == 12)
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "default-view.png"));
            if (_medicalCaptureFrame == 13)
            {
                _focus = AtMedicalWaterForCapture(); _camera.Size = 8f; ApplyCamera();
            }
            if (_medicalCaptureFrame == 16)
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "water-close-up.png"));
            if (_medicalCaptureFrame == 17)
                CapturePickMedicalFacility(MedicalFacility.Water);
            if (_medicalCaptureFrame == 18)
            {
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "water-selected.png"));
                GD.Print("MEDICAL_CAPTURE water-v4 default-and-close-up selected=True");
                GetTree().Quit();
            }
            return;
        }
        if (_medicalCaptureFrame == 12 && _medicalCaptureMode == "line")
        {
            RefreshPreparationHud();
            return;
        }
        if (_medicalCaptureFrame == 13 && _medicalCaptureMode == "line")
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "water-line.png"));
            GD.Print($"MEDICAL_CAPTURE water-line={_session.CaptureMedical()?.WaterQueue.Length}");
            GetTree().Quit(); return;
        }
        if (_medicalCaptureFrame == 9)
            SelectAttendee(new EntityId(_session.CaptureMedical()!.Needs.First(item => item.Profile == MedicalNeedProfile.Performer).AgentId));
        if (_medicalCaptureFrame == 10)
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "performer-needs.png"));
        if (_medicalCaptureFrame == 12)
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "water-queue.png"));
        if (_medicalCaptureFrame == 13) CapturePickMedicalFacility(MedicalFacility.Water);
        if (_medicalCaptureFrame == 14)
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "water-selected.png"));
        if (_medicalCaptureFrame == 15) ClearSelection();
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
            else
            {
                var warningTick = _session.CaptureMedical()!.WarningTick;
                _session.AdvanceWithoutSnapshot(checked((int)(warningTick + GameSession.MedicalCollapseDelayTicks + 160 - _session.CurrentTick)));
                _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
                RefreshPreparationHud();
                GD.Print($"MEDICAL_CAPTURE collapse={_session.CaptureMedical()?.Stage}");
            }
        }
        if (_medicalCaptureFrame == 20 && _medicalCaptureMode == "prevent")
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "treatment.png"));
        if (_medicalCaptureFrame == 20 && _medicalCaptureMode != "prevent")
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "collapse.png"));
        if (_medicalCaptureFrame == 22)
        {
            _session.AdvanceWithoutSnapshot(checked((int)(6_200 - _session.CurrentTick)));
            _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
            RefreshPreparationHud();
            GD.Print($"MEDICAL_CAPTURE outcome={_session.CaptureMedical()?.Stage} casualties={_session.CaptureLifecycleSnapshot()?.Casualties.Count}");
        }
        if (_medicalCaptureFrame == 24) CapturePickMedicalFacility(MedicalFacility.FirstAid);
        if (_medicalCaptureFrame == 25)
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "first-aid-selected.png"));
        if (_medicalCaptureFrame == 26)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "outcome.png"));
            GetTree().Quit();
        }
    }

    private static Vector3 AtMedicalWaterForCapture()
    {
        var centre = TraversalGrid.CellCentre(GameSession.MedicalWaterCell);
        return new Vector3(centre.XMillimetres / 1000f, 0.9f, centre.ZMillimetres / 1000f + 1.9f);
    }

    private void FocusMedicalCapturePerson(ulong id, float size)
    {
        var agent = _session.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == id);
        _focus = ToWorld(agent) + new Vector3(0, .8f, 0);
        _camera.Size = size;
        ApplyCamera();
    }
}

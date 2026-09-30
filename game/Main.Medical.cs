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
    private readonly MedicalCuePlanner _medicalCuePlanner = new();
    private readonly System.Collections.Generic.Dictionary<ulong, Label3D> _medicalCueLabels = [];
    private VBoxContainer? _medicalNeedsBars;
    private GridContainer? _medicalActionInspector;
    private ProgressBar? _medicalThirstBar;
    private ProgressBar? _medicalHeatBar;
    private Label? _medicalThirstLabel;
    private Label? _medicalHeatLabel;
    private enum MedicalFacility { Water, FirstAid, WaterTower }
    private readonly System.Collections.Generic.Dictionary<ulong, (MedicalFacility Facility, string? WaterPointId)> _medicalFacilityPicks = [];
    private readonly System.Collections.Generic.Dictionary<string, (Node3D Visual, StaticBody3D Pick)> _extraWaterVisuals = [];
    private Node3D? _primaryWaterVisual;
    private StaticBody3D? _primaryWaterPick;
    private GridCell _displayedPrimaryWaterCell = GameSession.MedicalWaterCell;
    private Node3D? _waterTowerVisual;
    private StaticBody3D? _waterTowerPick;
    private enum WaterPlacementMode { None, Add, Move }
    private Node3D? _waterPlacementAsset;
    private Button? _selectedWaterMoveButton;
    private HBoxContainer? _waterFlowRow;
    private TextureRect? _waterFlowIcon;
    private Label? _waterFlowLabel;
    private readonly System.Collections.Generic.Dictionary<string, Texture2D> _waterFlowTextures = [];
    private MeshInstance3D? _waterPlacementPreview;
    private MedicalFacility? _selectedMedicalFacility;
    private string _selectedWaterPointId = "water.main";

    private static Vector3 WaterVisualPosition(WaterPointState point)
    {
        var centre = TraversalGrid.CellCentre(point.Cell);
        // Legacy saves used the former large approach pad. Keep its occupied routes
        // and visual tap reach; every new/moved tap uses centred geometry version1.
        var offset = point.GeometryVersion == 0 ? GameSession.RotateWaterOffset(new GridCell(0, 1900), point.QuarterTurns) : new GridCell(0, 0);
        return new Vector3((centre.XMillimetres + offset.X) / 1000f, 0, (centre.ZMillimetres + offset.Z) / 1000f);
    }

    private void BuildMedicalWorld()
    {
        static Vector3 At(GridCell cell)
        {
            var centre = TraversalGrid.CellCentre(cell);
            return new Vector3(centre.XMillimetres / 1000f, 0, centre.ZMillimetres / 1000f);
        }
        // The narrow standpipe has no v1-style approach pad. Put its tap within
        // arm's reach of the existing front queue slot without moving that slot.
        var primaryWater = _session.CaptureWaterPoints().SingleOrDefault(point => point.Id == "water.main") ??
            new WaterPointState("water.main", GameSession.MedicalWaterCell, [], [], null, 0);
        _displayedPrimaryWaterCell = primaryWater.Cell;
        var waterPosition = WaterVisualPosition(primaryWater);
        _primaryWaterVisual = AddAsset("res://assets/environment/lwf_free_water_point_v4.glb", waterPosition);
        _responsePostVisuals[ResponseRole.Medic]=AddAsset(PostAsset(ResponseRole.Medic),At(_session.CaptureResponsePost(ResponseRole.Medic).Cell));
        _primaryWaterPick = RegisterMedicalPick(MedicalFacility.Water, "water.main", waterPosition + new Vector3(0, 1.05f, 0), new Vector3(2.3f, 2.1f, 1.1f));
        _responsePostPicks[ResponseRole.Medic]=RegisterMedicalPick(MedicalFacility.FirstAid, null, At(GameSession.MedicalTentCell) + new Vector3(0, 1.35f, 0), new Vector3(3.5f, 2.7f, 3.5f));
        var tentName=BuildingName("FIRST AID",Vector3.Zero);AddChild(tentName);_responsePostLabels[ResponseRole.Medic]=tentName;SyncResponsePosts();
        _waterPlacementPreview = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = .75f, BottomRadius = .75f, Height = 0.07f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.78f, 0.38f, 0.48f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            Visible = false
        };
        AddChild(_waterPlacementPreview);
        _waterPlacementAsset = AddAsset("res://assets/environment/lwf_free_water_point_v4.glb", Vector3.Zero);
        _waterPlacementAsset.Visible = false;
        SyncExtraWaterWorld();
    }


    private void BeginWaterPlacementFor(string? pointId)
    {
        BeginBuildPlacement(BuildServiceKind.WaterTap, pointId);
    }

    private void SyncExtraWaterWorld()
    {
        if (_session.CaptureMedical() is null) return;
        var main = _session.CaptureWaterPoints().SingleOrDefault(point => point.Id == "water.main");
        if (_primaryWaterVisual is not null)
        {
            _primaryWaterVisual.Visible = main is not null;
            _primaryWaterPick!.CollisionLayer = main is null ? 0u : 1u;
            if (main is not null)
            {
                var position = WaterVisualPosition(main);
                _primaryWaterVisual.Position = position;
                _primaryWaterVisual.RotationDegrees = new Vector3(0, 90 * main.QuarterTurns, 0);
                _primaryWaterPick.Position = position + new Vector3(0, 1.05f, 0);
                _primaryWaterPick.RotationDegrees = _primaryWaterVisual.RotationDegrees;
                _displayedPrimaryWaterCell = main.Cell;
            }
        }
        var points = _session.CaptureWaterPoints().Where(point => point.Id != "water.main").ToArray();
        foreach (var stale in _extraWaterVisuals.Keys.Except(points.Select(point => point.Id)).ToArray())
        {
            var pair = _extraWaterVisuals[stale];
            _medicalFacilityPicks.Remove(pair.Pick.GetInstanceId());
            pair.Visual.QueueFree(); pair.Pick.QueueFree(); _extraWaterVisuals.Remove(stale);
        }
        foreach (var point in points)
        {
            var centre = TraversalGrid.CellCentre(point.Cell);
            var position = WaterVisualPosition(point);
            if (_extraWaterVisuals.TryGetValue(point.Id, out var existing))
            {
                existing.Visual.Position = position;
                existing.Visual.RotationDegrees = new Vector3(0, 90 * point.QuarterTurns, 0);
                existing.Pick.Position = position + new Vector3(0, 1.05f, 0);
                existing.Pick.RotationDegrees = existing.Visual.RotationDegrees;
                continue;
            }
            var visual = AddAsset("res://assets/environment/lwf_free_water_point_v4.glb", position);
            var pick = RegisterMedicalPick(MedicalFacility.Water, point.Id, position + new Vector3(0, 1.05f, 0), new Vector3(2.3f, 2.1f, 1.1f));
            visual.RotationDegrees = new Vector3(0, 90 * point.QuarterTurns, 0);
            pick.RotationDegrees = visual.RotationDegrees;
            _extraWaterVisuals.Add(point.Id, (visual, pick));
        }
        var owned = _session.CapturePreparation()!.WaterTowerOwned;
        if (owned && _waterTowerVisual is null)
        {
            var position = new Vector3(-12.3f, 0, -14f);
            _waterTowerVisual = AddAsset("res://assets/environment/lwf_water_tower_prototype_v1.glb", position);
            _waterTowerPick = RegisterMedicalPick(MedicalFacility.WaterTower, null,
                position + new Vector3(0, 2.75f, 0), new Vector3(3.5f, 5.5f, 3.5f));
        }
        else if (!owned && _waterTowerVisual is not null)
        {
            _medicalFacilityPicks.Remove(_waterTowerPick!.GetInstanceId());
            _waterTowerVisual.QueueFree(); _waterTowerPick.QueueFree();
            _waterTowerVisual = null; _waterTowerPick = null;
        }
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
            if (_hudAlerts is not null && cue.Urgent) continue;
            if (!_medicalCueLabels.TryGetValue(cue.AgentId, out var label) ||
                !_attendeeVisuals.TryGetValue(new EntityId(cue.AgentId), out var visual)) continue;
            label.Text = cue.Text;
            label.Position = visual.Position + new Vector3(0, cue.Urgent ? 2.7f : 2.35f, 0);
            label.Modulate = cue.Urgent ? new Color("ffdb73") : new Color("fff7e1");
            label.Visible = true;
        }
    }

    private StaticBody3D RegisterMedicalPick(MedicalFacility facility, string? waterPointId, Vector3 position, Vector3 size)
    {
        var body = new StaticBody3D { Position = position, CollisionLayer = 1, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body);
        _medicalFacilityPicks.Add(body.GetInstanceId(), (facility, waterPointId));
        return body;
    }

    private void SelectMedicalFacility(MedicalFacility facility, string? waterPointId = null)
    {
        _selectedImmersionVendor = null;
        ClearSecurityPostSelection();
        _selected = null; _selectedAttendeeId = null; _selectedMedicalFacility = facility;
        if (facility == MedicalFacility.Water) _selectedWaterPointId = waterPointId ?? "water.main";
        RefreshSatisfactionBar(null);
        RefreshStagePowerAction();
        RefreshMedicalNeedBars(null);
        RefreshMedicalActionInspector();
        var cell = facility switch
        {
            MedicalFacility.Water => _session.CaptureWaterPoints().Single(point => point.Id == _selectedWaterPointId).Cell,
            MedicalFacility.WaterTower => GameSession.WaterTowerCell,
            _ => _session.CaptureResponsePost(ResponseRole.Medic).Cell
        };
        var centre = TraversalGrid.CellCentre(cell);
        _highlight.Position = new Vector3(centre.XMillimetres / 1000f, .08f, centre.ZMillimetres / 1000f);
        if (facility == MedicalFacility.Water)
            _highlight.Position = WaterVisualPosition(_session.CaptureWaterPoints().Single(point => point.Id == _selectedWaterPointId)) + new Vector3(0, .08f, 0);
        var radius = facility == MedicalFacility.Water ? 1.1f : facility == MedicalFacility.WaterTower ? 3.5f : 4f;
        _highlight.Scale = new Vector3(radius, 1, radius); _highlight.Visible = true;
        RefreshMedicalFacilityInspector();
        GD.Print($"MEDICAL_FACILITY_SELECTED type={facility}");
    }

    private void BuildMedicalNeedBars(VBoxContainer parent)
    {
        if (_session.CaptureMedical() is null) return;
        _medicalNeedsBars = new VBoxContainer { Visible = false };
        parent.AddChild(_medicalNeedsBars);
        _medicalThirstLabel = LabelText("THIRST", 12, new Color("8b5835")); _medicalNeedsBars.AddChild(_medicalThirstLabel);
        _medicalThirstBar = new ProgressBar { MaxValue = 10_000, ShowPercentage = false,
            CustomMinimumSize = new Vector2(375, 13) };
        _medicalNeedsBars.AddChild(_medicalThirstBar);
        _medicalHeatLabel = LabelText("HEAT EXPOSURE", 12, new Color("8b5835")); _medicalNeedsBars.AddChild(_medicalHeatLabel);
        _medicalHeatBar = new ProgressBar { MaxValue = 10_000, ShowPercentage = false,
            CustomMinimumSize = new Vector2(375, 13) };
        _medicalNeedsBars.AddChild(_medicalHeatBar);
    }

    private void RefreshMedicalNeedBars(MedicalNeed? need)
    {
        RefreshImmersionNeedBars(null);
        if (_medicalNeedsBars is null) return;
        _medicalNeedsBars.Visible = need is not null;
        if (need is null) return;
        _medicalThirstBar!.Value = need.Thirst;
        _medicalHeatBar!.Value = need.HeatExposure;
        _medicalThirstLabel!.Text = $"THIRST  {need.Thirst / 100m:0}%";
        _medicalHeatLabel!.Text = $"HEAT EXPOSURE  {need.HeatExposure / 100m:0}%";
        _medicalThirstBar.Modulate = MedicalNeedColor(need.Thirst);
        _medicalHeatBar.Modulate = MedicalNeedColor(need.HeatExposure);
    }

    private static Color MedicalNeedColor(int value) => value < 3_500 ? new Color("53bb72")
        : value < 7_000 ? new Color("459ad1") : new Color("df5750");

    private void RefreshMedicalFacilityInspector()
    {
        RefreshResponsePostMoveButtons();
        if (_selectedWaterMoveButton is not null)
            _selectedWaterMoveButton.Visible = _selectedMedicalFacility == MedicalFacility.Water &&
                _session.CapturePreparation()?.Status == PreparationStatus.Preparing;
        if (_selectedMedicalFacility is not { } facility || _session.CaptureMedical() is not { } m) return;
        var people = _session.CapturePreparation()!.People;
        if (facility == MedicalFacility.Water)
        {
            var point = _session.CaptureWaterPoints().Single(item => item.Id == _selectedWaterPointId);
            _inspectorTitle.Text = $"Free water • {point.Id}";
            var owner = point.OwnerId is { } id ? PersonPresentationName(people.Single(item => item.AgentId == id)) : "None";
            var tower = _session.CapturePreparation()!.WaterTowerOwned;
            var flow = _session.CommunityWaterShareActive ? tower ? "NORMAL" : "LOW" : tower ? "BOOSTED" : "NORMAL";
            _waterFlowIcon!.Texture = _waterFlowTextures[flow.ToLowerInvariant()];
            _waterFlowLabel!.Text = $"FLOW • {flow}\nCouncil {(_session.CommunityWaterShareActive ? "baseline cap 12" : "not shared")} • tower {(tower ? "+4" : "none")}";
            _inspectorBody.Text = $"FREE • no stock or payment\nQUEUE  {point.Queue.Length}/10 • VISIBLE TAIL  {point.Overflow.Length}\n" +
                $"FACING  {point.QuarterTurns * 90}°\n" +
                $"DRINKING  {owner}\nPACE  {(point.OwnerId is { } drinker ? _session.EffectiveMedicalDrinkThirstPerTickFor(drinker).ToString() : _session.CommunityWaterShareActive ? tower ? "12–16" : "8–12" : tower ? "12–24" : "8–20")} thirst/tick • varies by person{(_session.CommunityWaterShareActive ? " • Council share caps baseline at 12" : "")}{(tower ? " • tower +4" : "")}\n" +
                "More taps do not reduce personal flow. The physical line grows on arrival; approaching reserves no place.";
        }
        else if (facility == MedicalFacility.WaterTower)
        {
            _inspectorTitle.Text = "Water tower • owned";
            _inspectorBody.Text = "Approved farmyard tower • durable property\n+4 thirst relief per tick for every drinker at every tap. " +
                "Council sharing still caps the personal baseline at 12 before this bonus. No additional tap or shared-pressure penalty.";
        }
        else
        {
            _inspectorTitle.Text = "First aid • named medic coverage";
            _inspectorBody.Text = string.Join("\n", _session.GetMedicResponses().Select(job =>
                $"{people.Single(item => item.AgentId == job.WorkerId).Name}: {job.Stage} • patient {(job.PatientId is { } id ? PersonPresentationName(people.Single(item => item.AgentId == id)) : "none")}")) +
                "\nREST reduces heat after arrival. Select a distressed person to choose an available named medic.";
        }
    }


    private void BuildMedicalControls(VBoxContainer box)
    {
        _medicalSummary = LabelText("", 14, new Color("804126"));
        _medicalSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _medicalSummary.CustomMinimumSize = new Vector2(370, 165);
        box.AddChild(_medicalSummary);
    }

    private void BuildMedicalActionInspector(VBoxContainer detail)
    {
        if (_session.CaptureMedical() is null) return;
        _selectedWaterMoveButton = ButtonText("MOVE THIS TAP • PREPARATION ONLY", () => BeginWaterPlacementFor(_selectedWaterPointId));
        _selectedWaterMoveButton.Visible = false;
        _selectedWaterMoveButton.TooltipText = "Choose a new grass site. Comma/period rotate the tap and its service access; Esc cancels.";
        detail.AddChild(_selectedWaterMoveButton);
        _firstAidMoveButton=ButtonText("Move",()=>BeginResponsePostPlacement(ResponseRole.Medic));_firstAidMoveButton.Visible=false;detail.AddChild(_firstAidMoveButton);
        _medicalActionInspector = new GridContainer { Columns = 2, Visible = false };
        detail.AddChild(_medicalActionInspector);
        foreach (var (action, label) in new[] {
            (MedicalAction.DispatchMedic, "Send medic") })
        {
            var button = ButtonText(label, () => CommitMedicalAction(action));
            button.AddThemeFontSizeOverride("font_size", 12);
            button.CustomMinimumSize = new Vector2(183, 30);
            _medicalActionInspector.AddChild(button); _medicalButtons.Add(action, button);
        }
        BuildStaffInterventionControls(detail);
    }

    private void BuildWaterFlowInspector(VBoxContainer detail)
    {
        if (_session.CaptureMedical() is null) return;
        _waterFlowRow = new HBoxContainer { Visible = false };
        _waterFlowRow.AddThemeConstantOverride("separation", 10);
        _waterFlowIcon = new TextureRect { CustomMinimumSize = new Vector2(48, 48),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
        foreach (var state in new[] { "low", "normal", "boosted" })
            _waterFlowTextures.Add(state, GD.Load<Texture2D>($"res://assets/ui/lwf_tap_flow_{state}_v1_48px.png"));
        _waterFlowLabel = LabelText("", 13, new Color("29352c"));
        _waterFlowLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _waterFlowLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _waterFlowRow.AddChild(_waterFlowIcon); _waterFlowRow.AddChild(_waterFlowLabel); detail.AddChild(_waterFlowRow);
    }

    private ulong? MedicalSelectedGuest()
    {
        var medical = _session.CaptureMedical();
        return _selectedAttendeeId is { } selected && medical?.Needs.Any(item => item.AgentId == selected.Value) == true
            ? selected.Value : null;
    }

    private void CommitMedicalAction(MedicalAction action, ulong? workerId = null)
    {
        if (MedicalSelectedGuest() is not { } selected)
        {
            _preparationMessage = "Select a person before choosing a medical action.";
            RefreshPreparationHud();
            return;
        }
        var command = new MedicalCommand(selected, action, workerId);
        if (action == MedicalAction.DispatchMedic && workerId is null)
        {
            if (_session.SelectRoleResponse(ResponseRole.Medic, selected, out var issue) is not MedicalCommand roleCommand)
            { _preparationMessage = issue!; RefreshPreparationHud(); return; }
            command = roleCommand;
        }
        _preparationMessage = _host.Execute(command, out var error)
            ? "Medical action applied; next background save is within 30 unpaused seconds." : error!;
        RefreshPreparationHud();
    }

    private void RefreshMedicalControls()
    {
        if (_medicalSummary is null || _session.CaptureMedical() is not { } m) return;
        // The most urgent person on the health track: furthest along distress, collapse, critical.
        var urgent = m.Needs.Where(item => item.Stage is MedicalStage.Distress or MedicalStage.Collapsed or MedicalStage.Critical)
            .OrderByDescending(item => item.Stage).ThenBy(item => item.WarningTick).FirstOrDefault();
        var points = _session.CaptureWaterPoints();
        var active = points.Where(point => point.OwnerId is not null).ToArray();
        var drinking = active.Length == 0 ? $"{points.Count} taps ready • one drinker per tap" :
            string.Join(", ", active.Select(point => $"{point.Id}: {_session.CapturePreparation()!.People.Single(person => person.AgentId == point.OwnerId).Name}"));
        string Remaining(long dueTick) => $"{Math.Max(0, dueTick - _session.CurrentTick) / 80m:0.0}s";
        var clock = urgent?.Stage switch
        {
            MedicalStage.Distress => $"collapse {Remaining(urgent.WarningTick + GameSession.MedicalCollapseDelayTicks)} • death {Remaining(urgent.WarningTick + GameSession.MedicalCollapseDelayTicks + GameSession.MedicalDeathDelayTicks)}",
            MedicalStage.Collapsed => $"critical {Remaining(urgent.CollapseTick + GameSession.MedicalCriticalDelayTicks)} • death {Remaining(urgent.CollapseTick + GameSession.MedicalDeathDelayTicks)}",
            MedicalStage.Critical => $"death {Remaining(urgent.CollapseTick + GameSession.MedicalDeathDelayTicks)}",
            _ => "no active response window"
        };
        var urgentLine = urgent is null ? "No one in heat distress"
            : $"{_session.CapturePreparation()!.People.Single(person => person.AgentId == urgent.AgentId).Name}: {urgent.Stage} • thirst {urgent.Thirst / 100m:0}% • heat {urgent.HeatExposure / 100m:0}%";
        var immersionCare = _session.CaptureImmersion();
        var treatment = string.Join("\n", _session.GetMedicResponses().Select(job => {
            var worker = _session.GetResponseStaff().Single(item => item.AgentId == job.WorkerId);
            if (ActiveStaffInterventionSummary(job.WorkerId) is { } intervention)
                return $"{worker.Name.Split(' ')[0]}: {intervention}";
            if (job.Stage == MedicalResponseStage.Treating && immersionCare?.People.SingleOrDefault(person => person.AgentId == job.PatientId) is { CareTicks: > 0 } care)
                return $"{worker.Name.Split(' ')[0]}: gradual intoxication care {Math.Clamp(care.CareTicks * 100 / 1600, 0, 100)}% • {Math.Max(0, 1600 - care.CareTicks) / 80m:0.0}s left • exposure {care.Intoxication / 100m:0}%";
            return $"{worker.Name.Split(' ')[0]}: {job.Stage}" + (job.Stage == MedicalResponseStage.Treating
                ? $" {Math.Clamp((_session.CurrentTick - job.StartedTick) * 100 / worker.TreatmentTicks, 0, 100)}% • {Remaining(job.StartedTick + worker.TreatmentTicks)} left"
                : job.Stage == MedicalResponseStage.Travelling ? " • starts after arrival" : "");
        }));
        _medicalSummary.Text = $"HOT • FREE WATER • FIRST AID\n" +
            $"{urgentLine}\n" +
            $"Water {points.Count} taps • queue {points.Sum(point => point.Queue.Length)} + tail {points.Sum(point => point.Overflow.Length)} • {drinking}\nClock: {clock}\n" +
            $"{treatment}\nPerson actions are in the selected person's inspector.";
        RefreshMedicalActionInspector();
        RefreshMedicalFacilityInspector();
    }

    private void RefreshMedicalActionInspector()
    {
        RefreshResponsePostMoveButtons();
        RefreshContextPanelVisibility();
        if (_medicalActionInspector is null) return;
        if (_waterFlowRow is not null) _waterFlowRow.Visible = _selectedMedicalFacility == MedicalFacility.Water;
        if (_selectedWaterMoveButton is not null)
            _selectedWaterMoveButton.Visible = _selectedMedicalFacility == MedicalFacility.Water &&
                _session.CapturePreparation()?.Status == PreparationStatus.Preparing;
        var selected = MedicalSelectedGuest();
        _medicalActionInspector.Visible = selected is not null && _selectedMedicalFacility is null;
        foreach (var (action, button) in _medicalButtons)
        {
            string? issue = "Select a person first.";
            if (selected is { } id) _session.SelectRoleResponse(ResponseRole.Medic, id, out issue);
            button.Disabled = issue is not null;
            button.TooltipText = issue ?? "Send nearest available suitable medic; physical arrival and treatment required.";
        }
        RefreshStaffInterventionControls();
        RefreshImmersionVendorInspector();
    }


}

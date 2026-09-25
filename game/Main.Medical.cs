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
    private string? _waterFoundationCaptureDirectory;
    private int _waterFoundationCaptureFrame;
    private string _medicalCaptureMode = "prevent";
    private int _medicalCaptureFrame;
    private ulong _medicalCaptureWaterCueId;
    private ulong _medicalCaptureTradeoffCueId;
    private readonly MedicalCuePlanner _medicalCuePlanner = new();
    private readonly System.Collections.Generic.Dictionary<ulong, Label3D> _medicalCueLabels = [];
    private VBoxContainer? _medicalNeedsBars;
    private GridContainer? _medicalActionInspector;
    private ProgressBar? _medicalThirstBar;
    private ProgressBar? _medicalHeatBar;
    private enum MedicalFacility { Water, FirstAid, WaterTower }
    private readonly System.Collections.Generic.Dictionary<ulong, (MedicalFacility Facility, string? WaterPointId)> _medicalFacilityPicks = [];
    private readonly System.Collections.Generic.Dictionary<string, (Node3D Visual, StaticBody3D Pick)> _extraWaterVisuals = [];
    private Node3D? _primaryWaterVisual;
    private StaticBody3D? _primaryWaterPick;
    private GridCell _displayedPrimaryWaterCell = GameSession.MedicalWaterCell;
    private Node3D? _waterTowerVisual;
    private StaticBody3D? _waterTowerPick;
    private enum WaterPlacementMode { None, Add, MovePrimary }
    private WaterPlacementMode _waterPlacementMode;
    private MeshInstance3D? _waterPlacementPreview;
    private readonly System.Collections.Generic.List<MeshInstance3D> _waterQueuePreview = [];
    private GridCell? _waterPlacementCandidate;
    private string? _waterPlacementIssue;
    private MedicalFacility? _selectedMedicalFacility;
    private string _selectedWaterPointId = "water.main";

    private void BuildMedicalWorld()
    {
        static Vector3 At(GridCell cell)
        {
            var centre = TraversalGrid.CellCentre(cell);
            return new Vector3(centre.XMillimetres / 1000f, 0, centre.ZMillimetres / 1000f);
        }
        // The narrow standpipe has no v1-style approach pad. Put its tap within
        // arm's reach of the existing front queue slot without moving that slot.
        _displayedPrimaryWaterCell = _session.CaptureWaterPoints().Single(point => point.Id == "water.main").Cell;
        var waterPosition = At(_displayedPrimaryWaterCell) + new Vector3(0, 0, 1.9f);
        _primaryWaterVisual = AddAsset("res://assets/environment/lwf_free_water_point_v4.glb", waterPosition);
        AddAsset("res://assets/environment/lwf_first_aid_point_v2.glb", At(GameSession.MedicalTentCell));
        _primaryWaterPick = RegisterMedicalPick(MedicalFacility.Water, "water.main", waterPosition + new Vector3(0, 1.05f, 0), new Vector3(2.3f, 2.1f, 1.1f));
        RegisterMedicalPick(MedicalFacility.FirstAid, null, At(GameSession.MedicalTentCell) + new Vector3(0, 1.35f, 0), new Vector3(3.5f, 2.7f, 3.5f));
        AddChild(new Label3D { Text = "FIRST AID", Position = At(GameSession.MedicalTentCell) + new Vector3(0, 3.1f, 0),
            FontSize = 45, PixelSize = .009f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
        _waterPlacementPreview = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1.75f, BottomRadius = 1.75f, Height = 0.07f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.78f, 0.38f, 0.48f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            Visible = false
        };
        AddChild(_waterPlacementPreview);
        for (var index = 0; index < 20; index++)
        {
            var marker = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = .4f, BottomRadius = .4f, Height = .045f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.25f, .78f, .38f, .48f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
                Visible = false
            };
            AddChild(marker);
            _waterQueuePreview.Add(marker);
        }
        SyncExtraWaterWorld();
    }

    private void BeginWaterPlacement(bool movePrimary)
    {
        if (_session.CapturePreparation() is not { Status: PreparationStatus.Preparing }) return;
        _waterPlacementMode = movePrimary ? WaterPlacementMode.MovePrimary : WaterPlacementMode.Add;
        _waterPlacementCandidate = null;
        _waterPlacementIssue = null;
        _preparationMessage = movePrimary ? "Move original tap: point at grass to preview footprint and queue."
            : "Add tap: point at grass to preview footprint and queue.";
        ClearSelection();
        RefreshPreparationHud();
        UpdateWaterPlacementPreview(GetViewport().GetMousePosition());
    }

    private void CancelWaterPlacement()
    {
        _waterPlacementMode = WaterPlacementMode.None;
        _waterPlacementCandidate = null;
        _waterPlacementIssue = null;
        _preparationMessage = "Water placement cancelled.";
        RefreshPreparationHud();
        if (_waterPlacementPreview is not null) _waterPlacementPreview.Visible = false;
        foreach (var marker in _waterQueuePreview) marker.Visible = false;
        if (_waterPlacementStatus is not null)
            _waterPlacementStatus.Text = "Choose a tap action, then click valid ground. Right-click or Esc cancels.";
    }

    private void UpdateWaterPlacementPreview(Vector2 screenPosition)
    {
        if (_waterPlacementMode == WaterPlacementMode.None || _waterPlacementPreview is null) return;
        var width = GetViewport().GetVisibleRect().Size.X;
        if (screenPosition.X < 435 || screenPosition.X > width - 435 || screenPosition.Y < 110)
        {
            _waterPlacementPreview.Visible = false;
            foreach (var marker in _waterQueuePreview) marker.Visible = false;
            _waterPlacementCandidate = null;
            _waterPlacementStatus!.Text = "Move over open grass to preview a 3.5 m tap footprint and queue.";
            _preparationMessage = _waterPlacementStatus.Text;
            RefreshPreparationHud();
            return;
        }
        var ray = _camera.ProjectRayNormal(screenPosition);
        var origin = _camera.ProjectRayOrigin(screenPosition);
        if (Mathf.Abs(ray.Y) < .001f || -origin.Y / ray.Y <= 0)
        {
            _waterPlacementPreview.Visible = false; _waterPlacementCandidate = null;
            foreach (var marker in _waterQueuePreview) marker.Visible = false;
            return;
        }
        var world = origin + ray * (-origin.Y / ray.Y);
        var cell = TraversalGrid.WorldToCell(Mathf.RoundToInt(world.X * 1000), Mathf.RoundToInt(world.Z * 1000));
        if (_waterPlacementCandidate != cell)
        {
            _waterPlacementCandidate = cell;
            SessionCommand command = _waterPlacementMode == WaterPlacementMode.MovePrimary
                ? new MovePrimaryWaterPointCommand(cell) : new PlaceWaterPointCommand(cell);
            _waterPlacementIssue = _session.ValidateCommand(CampaignEnvelope(command))?.Message;
            _waterPlacementStatus!.Text = _waterPlacementIssue is null
                ? $"VALID • {cell.X},{cell.Z} • click to {(_waterPlacementMode == WaterPlacementMode.MovePrimary ? "move original tap" : "add tap")}."
                : $"INVALID • {_waterPlacementIssue}";
            _preparationMessage = _waterPlacementStatus.Text;
            RefreshPreparationHud();
            ((StandardMaterial3D)_waterPlacementPreview.MaterialOverride!).AlbedoColor = _waterPlacementIssue is null
                ? new Color(.25f, .78f, .38f, .48f) : new Color(.9f, .24f, .18f, .5f);
            foreach (var marker in _waterQueuePreview)
                ((StandardMaterial3D)marker.MaterialOverride!).AlbedoColor = _waterPlacementIssue is null
                    ? new Color(.25f, .78f, .38f, .48f) : new Color(.9f, .24f, .18f, .5f);
        }
        var centre = TraversalGrid.CellCentre(cell);
        _waterPlacementPreview.Position = new Vector3(centre.XMillimetres / 1000f, .08f, centre.ZMillimetres / 1000f);
        _waterPlacementPreview.Visible = true;
        for (var index = 0; index < _waterQueuePreview.Count; index++)
        {
            var slot = index < 10 ? new GridCell(cell.X + index / 2, cell.Z + 5 + index * 2)
                : new GridCell(cell.X + 5 + (index - 10) / 2, cell.Z + 25 + (index - 10) * 2);
            var slotCentre = TraversalGrid.CellCentre(slot);
            _waterQueuePreview[index].Position = new Vector3(slotCentre.XMillimetres / 1000f, .08f,
                slotCentre.ZMillimetres / 1000f);
            _waterQueuePreview[index].Visible = true;
        }
    }

    private void CommitWaterPlacement(Vector2 screenPosition)
    {
        UpdateWaterPlacementPreview(screenPosition);
        if (_waterPlacementCandidate is not { } cell || _waterPlacementIssue is not null) return;
        var before = _session.CapturePreparation()!;
        SessionCommand command = _waterPlacementMode == WaterPlacementMode.MovePrimary
            ? new MovePrimaryWaterPointCommand(cell) : new PlaceWaterPointCommand(cell);
        CommitEquipmentAction(command);
        var after = _session.CapturePreparation()!;
        if (before.PrimaryWaterCell != after.PrimaryWaterCell || before.ExtraWaterSiteIds.Length < after.ExtraWaterSiteIds.Length)
            CancelWaterPlacement();
    }

    private void ProcessWaterFoundationCapture()
    {
        if (_waterFoundationCaptureDirectory is null) return;
        if (++_waterFoundationCaptureFrame == 5)
        {
            _focus = new Vector3(-12.3f, 0, -14f); _camera.Size = 22f; ApplyCamera();
            Pick(_camera.UnprojectPosition(new Vector3(-12.3f, 2.75f, -14f)));
            if (_selectedMedicalFacility != MedicalFacility.WaterTower)
                throw new InvalidOperationException("Water tower pick fixture did not select the approved tower.");
        }
        if (_waterFoundationCaptureFrame == 9)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_waterFoundationCaptureDirectory, "tower-selected.png"));
            GD.Print("WATER_FOUNDATION_CAPTURE tower-selected owned=True +4=True");
            var centre = TraversalGrid.CellCentre(new GridCell(70, 120));
            _focus = new Vector3(centre.XMillimetres / 1000f + 5f, 0, centre.ZMillimetres / 1000f + 5f);
            _camera.Size = 27f; ApplyCamera();
            BeginWaterPlacement(false);
            UpdateWaterPlacementPreview(_camera.UnprojectPosition(new Vector3(centre.XMillimetres / 1000f, 0,
                centre.ZMillimetres / 1000f)));
            if (_waterPlacementIssue is not null || _waterPlacementPreview?.Visible != true)
                throw new InvalidOperationException("Player-chosen grass preview was not valid and visible.");
        }
        if (_waterFoundationCaptureFrame == 10)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_waterFoundationCaptureDirectory, "valid-site-preview.png"));
            GD.Print("WATER_FOUNDATION_CAPTURE valid-site-preview cell=70,120 queue-markers=20");
        }
        if (_waterFoundationCaptureFrame == 11)
        {
            CancelWaterPlacement();
            foreach (var cell in new[] { new GridCell(70, 120), new GridCell(101, 130) })
                if (!_session.Execute(CampaignEnvelope(new PlaceWaterPointCommand(cell))).IsAccepted)
                    throw new InvalidOperationException($"Rendered placement fixture rejected {cell}.");
            SyncExtraWaterWorld(); RefreshPreparationHud();
            var extra = _session.CaptureWaterPoints().Single(point => point.Id == "water.extra-1").Cell;
            var centre = TraversalGrid.CellCentre(extra);
            Pick(_camera.UnprojectPosition(new Vector3(centre.XMillimetres / 1000f, 1.05f,
                centre.ZMillimetres / 1000f + 1.9f)));
            if (_selectedMedicalFacility != MedicalFacility.Water || _selectedWaterPointId != "water.extra-1")
                throw new InvalidOperationException("Player-placed standpipe pick fixture did not select its own inspector.");
        }
        if (_waterFoundationCaptureFrame == 13)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_waterFoundationCaptureDirectory, "placed-tap-selected.png"));
            GD.Print($"WATER_FOUNDATION_CAPTURE placed-tap-selected points={_session.CaptureWaterPoints().Count} layout={string.Join(',', _session.CapturePreparation()!.WaterPlacements.Select(site => $"{site.Id}:{site.Cell}"))}");
            BeginWaterPlacement(true);
            var occupied = _session.CaptureWaterPoints().Single(point => point.Id == "water.extra-1").Cell;
            var centre = TraversalGrid.CellCentre(occupied);
            UpdateWaterPlacementPreview(_camera.UnprojectPosition(new Vector3(centre.XMillimetres / 1000f, 0,
                centre.ZMillimetres / 1000f)));
            if (_waterPlacementIssue is null || _waterPlacementPreview?.Visible != true)
                throw new InvalidOperationException("Occupied-site placement preview did not reject with visible markers.");
        }
        if (_waterFoundationCaptureFrame == 14)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_waterFoundationCaptureDirectory, "occupied-site-preview.png"));
            GD.Print($"WATER_FOUNDATION_CAPTURE occupied-site-preview rejected={_waterPlacementIssue}");
        }
        if (_waterFoundationCaptureFrame == 15)
        {
            CancelWaterPlacement();
            PrepareWaterFoundationLiveFixture();
        }
        if (_waterFoundationCaptureFrame == 18)
        {
            if (_session.CaptureWaterPoints().Count(point => point.OwnerId is not null) != 2)
                throw new InvalidOperationException("Two-tap rendered fixture did not reach simultaneous drinkers.");
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_waterFoundationCaptureDirectory, "two-taps-live.png"));
            GD.Print($"WATER_FOUNDATION_CAPTURE two-taps-live owners={string.Join(',', _session.CaptureWaterPoints().Where(point => point.OwnerId is not null).Select(point => point.Id))}");
            // A second save has the same stable extra ID but a different player-chosen cell.
            var layoutB = GameSession.CreateMedicalCampaign(20260922);
            if (!layoutB.Execute(new CommandEnvelope(new CommandId(1), layoutB.CampaignId, layoutB.Phase,
                    layoutB.CurrentTick, layoutB.NextSubmissionSequence, null,
                    new PlaceWaterPointCommand(new GridCell(110, 130)))).IsAccepted)
                throw new InvalidOperationException("Same-ID layout B placement was rejected.");
            var save = SaveFileAdapter.SaveSlot(_waterFoundationCaptureDirectory, "same-id-layout-b",
                new SaveWriteRequest(layoutB, _saveCompatibility, "r005a-visual-fixture", DateTimeOffset.UtcNow));
            if (!save.IsSuccess) throw new InvalidOperationException($"Same-ID layout B save failed: {save.Error}");
            var loaded = SaveFileAdapter.LoadSlot(_waterFoundationCaptureDirectory, "same-id-layout-b", _saveCompatibility);
            if (!loaded.IsSuccess) throw new InvalidOperationException($"Same-ID layout B load failed: {loaded.Error}");
            _session = loaded.Session!;
            ClearSelection(); SyncExtraWaterWorld(); RefreshPreparationHud();
            var relocated = _extraWaterVisuals["water.extra-1"];
            var newCentre = TraversalGrid.CellCentre(new GridCell(110, 130));
            var newPosition = new Vector3(newCentre.XMillimetres / 1000f, 0, newCentre.ZMillimetres / 1000f + 1.9f);
            if (!relocated.Visual.Position.IsEqualApprox(newPosition) ||
                !relocated.Pick.Position.IsEqualApprox(newPosition + new Vector3(0, 1.05f, 0)))
                throw new InvalidOperationException("Same-ID layout B kept the old extra-tap visual or pick transform.");
            _focus = newPosition; _camera.Size = 27f; ApplyCamera();
        }
        if (_waterFoundationCaptureFrame == 19)
        {
            var oldCentre = TraversalGrid.CellCentre(new GridCell(70, 120));
            _focus = new Vector3(oldCentre.XMillimetres / 1000f, 0, oldCentre.ZMillimetres / 1000f);
            ApplyCamera();
            Pick(_camera.UnprojectPosition(new Vector3(oldCentre.XMillimetres / 1000f, 1.05f,
                oldCentre.ZMillimetres / 1000f + 1.9f)));
            if (_selectedMedicalFacility == MedicalFacility.Water && _selectedWaterPointId == "water.extra-1")
                throw new InvalidOperationException("Old extra-tap pick remained at layout A cell.");
            var newCentre = TraversalGrid.CellCentre(new GridCell(110, 130));
            _focus = new Vector3(newCentre.XMillimetres / 1000f, 0, newCentre.ZMillimetres / 1000f);
            ApplyCamera();
            Pick(_camera.UnprojectPosition(new Vector3(newCentre.XMillimetres / 1000f, 1.05f,
                newCentre.ZMillimetres / 1000f + 1.9f)));
            if (_selectedMedicalFacility != MedicalFacility.Water || _selectedWaterPointId != "water.extra-1")
                throw new InvalidOperationException("Relocated extra-tap pick did not select layout B site.");
        }
        if (_waterFoundationCaptureFrame == 20)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_waterFoundationCaptureDirectory, "same-id-relocated.png"));
            GD.Print("WATER_FOUNDATION_CAPTURE same-id-relocated old-pick-absent=True new-pick-selected=True cell=110,130");
        }
        if (_waterFoundationCaptureFrame == 21)
        {
            // Loading an older no-extra layout must still clear selection and stale objects.
            _session = GameSession.CreateMedicalCampaign(20260922);
            ClearSelection(); SyncExtraWaterWorld(); RefreshPreparationHud();
            if (_selectedMedicalFacility is not null || _selectedWaterPointId != "water.main" ||
                _extraWaterVisuals.Count != 0 || _waterTowerVisual is not null || _highlight.Visible)
                throw new InvalidOperationException("Older-layout load fixture retained stale water selection or visuals.");
            GD.Print("WATER_FOUNDATION_CAPTURE old-layout-selection-cleared=True");
            _waterFoundationCaptureDirectory = null;
            GetTree().Quit();
        }
    }

    private void PrepareWaterFoundationLiveFixture()
    {
        // Render-only deterministic fixture: two guests begin beside distinct player-chosen taps.
        foreach (var offer in new[] { "act.folk", "staff.steward", "equipment.buy" })
            if (!_session.Execute(CampaignEnvelope(new AcceptPreparationOfferCommand(offer))).IsAccepted)
                throw new InvalidOperationException("Water foundation fixture booking failed.");
        if (!_session.Execute(CampaignEnvelope(new StartPreparedEditionCommand())).IsAccepted)
            throw new InvalidOperationException("Water foundation fixture start failed.");
        var ids = _session.CapturePreparation()!.People.Where(person => person.Role == ProtectedPersonRole.Guest)
            .Take(2).Select(person => person.AgentId).ToArray();
        var agents = _session.GetType().GetField("_navigationAgents", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!.GetValue(_session)!;
        var main = _session.CaptureWaterPoints().Single(point => point.Id == "water.main").Cell;
        var extra = _session.CaptureWaterPoints().Single(point => point.Id == "water.extra-1").Cell;
        var cells = new[] { new GridCell(main.X, main.Z + 5), new GridCell(extra.X, extra.Z + 5) };
        for (var index = 0; index < ids.Length; index++)
        {
            var agent = agents.GetType().GetProperty("Item")!.GetValue(agents, [new EntityId(ids[index])])!;
            var centre = TraversalGrid.CellCentre(cells[index]);
            foreach (var (name, value) in new[] { ("XMillimetres", centre.XMillimetres), ("ZMillimetres", centre.ZMillimetres),
                         ("SegmentOriginXMillimetres", centre.XMillimetres), ("SegmentOriginZMillimetres", centre.ZMillimetres) })
                agent.GetType().GetProperty(name)!.SetValue(agent, value);
            if (!_session.Execute(CampaignEnvelope(new MedicalCommand(ids[index], MedicalAction.GuideToWater))).IsAccepted)
                throw new InvalidOperationException("Water foundation fixture guide failed.");
        }
        for (var tick = 0; tick < 150 && _session.CaptureWaterPoints().Count(point => point.OwnerId is not null) < 2; tick++)
            _session.AdvanceWithoutSnapshot(1);
        if (_session.CaptureWaterPoints().Count(point => point.OwnerId is not null) != 2)
            throw new InvalidOperationException("Water foundation fixture did not acquire two tap owners.");
        BuildAttendee();
        _foundationClock.ResetBoundary(); _foundationPresentation.Reset(_session.CaptureObservation());
        _focus = new Vector3(-20f, 0, 0); _camera.Size = 29f; ApplyCamera();
        ClearSelection();
        RefreshPreparationHud();
    }

    private void SyncExtraWaterWorld()
    {
        if (_session.CaptureMedical() is null) return;
        var main = _session.CaptureWaterPoints().Single(point => point.Id == "water.main");
        if (_primaryWaterVisual is not null && main.Cell != _displayedPrimaryWaterCell)
        {
            var centre = TraversalGrid.CellCentre(main.Cell);
            var position = new Vector3(centre.XMillimetres / 1000f, 0, centre.ZMillimetres / 1000f + 1.9f);
            _primaryWaterVisual.Position = position;
            _primaryWaterPick!.Position = position + new Vector3(0, 1.05f, 0);
            _displayedPrimaryWaterCell = main.Cell;
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
            var position = new Vector3(centre.XMillimetres / 1000f, 0, centre.ZMillimetres / 1000f + 1.9f);
            if (_extraWaterVisuals.TryGetValue(point.Id, out var existing))
            {
                existing.Visual.Position = position;
                existing.Pick.Position = position + new Vector3(0, 1.05f, 0);
                continue;
            }
            var visual = AddAsset("res://assets/environment/lwf_free_water_point_v4.glb", position);
            var pick = RegisterMedicalPick(MedicalFacility.Water, point.Id, position + new Vector3(0, 1.05f, 0), new Vector3(2.3f, 2.1f, 1.1f));
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
            _ => GameSession.MedicalTentCell
        };
        var centre = TraversalGrid.CellCentre(cell);
        _highlight.Position = new Vector3(centre.XMillimetres / 1000f, .08f, centre.ZMillimetres / 1000f);
        var radius = facility == MedicalFacility.Water ? 2.5f : facility == MedicalFacility.WaterTower ? 3.5f : 4f;
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
            CustomMinimumSize = new Vector2(375, 13) };
        _medicalNeedsBars.AddChild(_medicalThirstBar);
        _medicalNeedsBars.AddChild(LabelText("HEAT EXPOSURE", 12, new Color("8b5835")));
        _medicalHeatBar = new ProgressBar { MaxValue = 10_000, ShowPercentage = false,
            CustomMinimumSize = new Vector2(375, 13) };
        _medicalNeedsBars.AddChild(_medicalHeatBar);
    }

    private void RefreshMedicalNeedBars(MedicalNeed? need)
    {
        if (_medicalNeedsBars is null) return;
        _medicalNeedsBars.Visible = need is not null;
        if (need is null) return;
        _medicalThirstBar!.Value = need.Thirst;
        _medicalHeatBar!.Value = need.HeatExposure;
        _medicalThirstBar.Modulate = MedicalNeedColor(need.Thirst);
        _medicalHeatBar.Modulate = MedicalNeedColor(need.HeatExposure);
    }

    private static Color MedicalNeedColor(int value) => value < 3_500 ? new Color("53bb72")
        : value < 7_000 ? new Color("459ad1") : new Color("df5750");

    private void RefreshMedicalFacilityInspector()
    {
        if (_selectedMedicalFacility is not { } facility || _session.CaptureMedical() is not { } m) return;
        var people = _session.CapturePreparation()!.People;
        if (facility == MedicalFacility.Water)
        {
            var point = _session.CaptureWaterPoints().Single(item => item.Id == _selectedWaterPointId);
            _inspectorTitle.Text = $"Free water • {point.Id}";
            var owner = point.OwnerId is { } id ? people.Single(item => item.AgentId == id).Name : "None";
            var tower = _session.CapturePreparation()!.WaterTowerOwned;
            _inspectorBody.Text = $"FREE • no stock or payment\nQUEUE  {point.Queue.Length}/10 • VISIBLE TAIL  {point.Overflow.Length}\n" +
                $"DRINKING  {owner}\nPACE  {(point.OwnerId is { } drinker ? _session.EffectiveMedicalDrinkThirstPerTickFor(drinker).ToString() : _session.CommunityWaterShareActive ? tower ? "12–16" : "8–12" : tower ? "12–24" : "8–20")} thirst/tick • varies by person{(_session.CommunityWaterShareActive ? " • Council share caps baseline at 12" : "")}{(tower ? " • tower +4" : "")}\n" +
                "Select a person for GUIDE TO WATER in their inspector.";
        }
        else if (facility == MedicalFacility.WaterTower)
        {
            _inspectorTitle.Text = "Water tower • owned";
            _inspectorBody.Text = "Approved farmyard tower • durable property\n+4 thirst relief per tick for every drinker at every tap. " +
                "Council sharing still caps the personal baseline at 12 before this bonus. No additional tap or shared-pressure penalty.";
        }
        else
        {
            _inspectorTitle.Text = "First aid • Riley Hart";
            _inspectorBody.Text = $"MEDIC  {m.ResponseStage}\nPATIENT  " +
                (m.ResponsePatientId is { } id ? people.Single(item => item.AgentId == id).Name : "None") +
                $"\nRESPONSE  {StewardWording(m.Response)}\nREST  shade reduces heat after arrival\n" +
                "Select a distressed person for DISPATCH RILEY or GUIDE TO REST in their inspector.";
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
    }

    private void BuildMedicalActionInspector(VBoxContainer detail)
    {
        if (_session.CaptureMedical() is null) return;
        _medicalActionInspector = new GridContainer { Columns = 2, Visible = false };
        detail.AddChild(_medicalActionInspector);
        foreach (var (action, label) in new[] {
            (MedicalAction.GuideToWater, "GUIDE TO WATER"), (MedicalAction.GuideToRest, "GUIDE TO REST"),
            (MedicalAction.DispatchMedic, "DISPATCH RILEY"), (MedicalAction.SafeRemove, "SAFE REMOVE"),
            (MedicalAction.ReturnToShow, "LEAVE WATER QUEUE") })
        {
            var button = ButtonText(label, () => CommitMedicalAction(action));
            button.AddThemeFontSizeOverride("font_size", 12);
            button.CustomMinimumSize = new Vector2(183, 30);
            _medicalActionInspector.AddChild(button); _medicalButtons.Add(action, button);
        }
    }

    private ulong? MedicalSelectedGuest()
    {
        var medical = _session.CaptureMedical();
        return _selectedAttendeeId is { } selected && medical?.Needs.Any(item => item.AgentId == selected.Value) == true
            ? selected.Value : null;
    }

    private void CommitMedicalAction(MedicalAction action)
    {
        if (MedicalSelectedGuest() is not { } selected)
        {
            _preparationMessage = "Select a person before choosing a medical action.";
            RefreshPreparationHud();
            return;
        }
        var command = new MedicalCommand(selected, action);
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
        var points = _session.CaptureWaterPoints();
        var active = points.Where(point => point.OwnerId is not null).ToArray();
        var drinking = active.Length == 0 ? $"{points.Count} taps ready • one drinker per tap" :
            string.Join(", ", active.Select(point => $"{point.Id}: {_session.CapturePreparation()!.People.Single(person => person.AgentId == point.OwnerId).Name}"));
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
            : m.ResponseStage == MedicalResponseStage.Travelling ? "Medic travelling • treatment begins on arrival" : StewardWording(m.Response);
        _medicalSummary.Text = $"HOT • FREE WATER • FIRST AID\n" +
            $"Guest 20: {m.Stage} • thirst {target.Thirst / 100m:0}% • heat {target.HeatExposure / 100m:0}%\n" +
            $"Water {points.Count} taps • queue {points.Sum(point => point.Queue.Length)} + tail {points.Sum(point => point.Overflow.Length)} • {drinking}\nMedic {m.ResponseStage} • Clock: {clock}\n" +
            $"{treatment}\nPerson actions are in the selected person's inspector.";
        RefreshMedicalActionInspector();
        RefreshMedicalFacilityInspector();
    }

    private void RefreshMedicalActionInspector()
    {
        if (_medicalActionInspector is null) return;
        var selected = MedicalSelectedGuest();
        _medicalActionInspector.Visible = selected is not null && _selectedMedicalFacility is null;
        foreach (var (action, button) in _medicalButtons)
        {
            button.Disabled = selected is not { } id ||
                _session.ValidateCommand(CampaignEnvelope(new MedicalCommand(id, action))) is not null;
            button.TooltipText = selected is { } target
                ? _session.ValidateCommand(CampaignEnvelope(new MedicalCommand(target, action)))?.Message ?? ""
                : "Select a person first.";
        }
    }

    private void ProcessMedicalCapture()
    {
        if (_medicalCaptureDirectory is null) return;
        _medicalCaptureFrame++;
        if (_medicalCaptureFrame == 4)
            foreach (var id in new[] { "act.folk", "staff.steward", "equipment.buy" }) _offerButtons[id].EmitSignal(Button.SignalName.Pressed);
        if (_medicalCaptureFrame == 6) _preparationStart.EmitSignal(Button.SignalName.Pressed);
        if (_medicalCaptureFrame == 7 && _medicalCaptureMode == "ui-state")
            SelectAttendee(new EntityId(_session.CaptureMedical()!.AtRiskGuestId));
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
            if (_medicalCaptureMode == "ui-state")
            {
                if (MedicalNeedColor(0) != new Color("53bb72") ||
                    MedicalNeedColor(5_000) != new Color("459ad1") ||
                    MedicalNeedColor(10_000) != new Color("df5750"))
                    throw new InvalidOperationException("Medical need color grading changed.");
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "early-need-bars.png"));
            }
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
        if (_medicalCaptureMode == "ui-state")
        {
            var medical = _session.CaptureMedical()!;
            if (_medicalCaptureFrame == 9)
            {
                SelectAttendee(new EntityId(medical.Needs.First(item => item.Profile == MedicalNeedProfile.Performer).AgentId));
                if (_medicalActionInspector?.Visible != true || _medicalButtons[MedicalAction.DispatchMedic].Disabled == false)
                    throw new InvalidOperationException("Performer inspector did not show validated medical actions.");
            }
            if (_medicalCaptureFrame == 11)
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "performer-inspector.png"));
            if (_medicalCaptureFrame == 12)
            {
                ClearSelection();
                if (_medicalActionInspector?.Visible == true) throw new InvalidOperationException("Unselected actions remained visible.");
                var before = _session.CaptureSnapshot().AuthoritativeHash;
                CommitMedicalAction(MedicalAction.DispatchMedic);
                if (_session.CaptureSnapshot().AuthoritativeHash != before)
                    throw new InvalidOperationException("Unselected medical action changed authoritative state.");
                CapturePickMedicalFacility(MedicalFacility.Water);
                if (_medicalActionInspector?.Visible == true) throw new InvalidOperationException("Facility selection showed person actions.");
            }
            if (_medicalCaptureFrame == 13)
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "facility-inspector.png"));
            if (_medicalCaptureFrame == 14)
            {
                while (_session.CaptureMedical()!.Stage != MedicalStage.Collapsed && _session.CurrentTick < 4_000)
                    _session.AdvanceWithoutSnapshot(1);
                if (_session.CaptureMedical()!.Stage != MedicalStage.Collapsed)
                    throw new InvalidOperationException("UI fixture did not reach guest collapse.");
                _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
                FocusMedicalCapturePerson(medical.AtRiskGuestId, 24f);
                SelectAttendee(new EntityId(medical.AtRiskGuestId));
                RefreshPreparationHud();
                if (!_medicalButtons[MedicalAction.GuideToWater].Disabled || _medicalButtons[MedicalAction.DispatchMedic].Disabled)
                    throw new InvalidOperationException("Collapsed guest inspector validation was incorrect.");
            }
            if (_medicalCaptureFrame == 16)
            {
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "collapsed-before-dispatch.png"));
                _medicalButtons[MedicalAction.DispatchMedic].EmitSignal(Button.SignalName.Pressed);
                if (_session.CaptureMedical()!.Needs.Single(item => item.AgentId == medical.AtRiskGuestId).Intent != MedicalIntent.Collapsed)
                    throw new InvalidOperationException("Dispatch made collapsed guest stand.");
                var restored = GameSession.Restore(_session.CapturePersistenceSnapshot());
                if (!restored.IsSuccess) throw new InvalidOperationException(restored.Error);
                _session = restored.Session!;
                _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
                RefreshPreparationHud();
            }
            if (_medicalCaptureFrame == 17)
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "collapsed-medic-travelling.png"));
            if (_medicalCaptureFrame == 18)
            {
                var rosterBar = _preparationRosterScroll!.GetVScrollBar();
                if (rosterBar.MaxValue <= rosterBar.Page)
                    throw new InvalidOperationException("Roster did not expose its overflowing entries by scrolling.");
                _preparationRosterScroll.ScrollVertical = (int)(rosterBar.MaxValue - rosterBar.Page);
            }
            if (_medicalCaptureFrame == 19)
            {
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "roster-bottom.png"));
                GD.Print($"MEDICAL_UI_STATE selected={medical.AtRiskGuestId} intent={_session.CaptureMedical()!.Needs.Single(item => item.AgentId == medical.AtRiskGuestId).Intent} response={_session.CaptureMedical()!.ResponseStage}");
            }
            if (_medicalCaptureFrame == 20)
            {
                while (_session.CaptureMedical()!.ResponseStage == MedicalResponseStage.Travelling && _session.CurrentTick < 5_000)
                    _session.AdvanceWithoutSnapshot(1);
                if (_session.CaptureMedical()!.ResponseStage != MedicalResponseStage.Treating)
                    throw new InvalidOperationException("Medic did not reach collapsed guest.");
                if (_session.CaptureMedical()!.Needs.Single(item => item.AgentId == medical.AtRiskGuestId).Intent != MedicalIntent.Collapsed)
                    throw new InvalidOperationException("Treatment made collapsed guest stand early.");
                _session.AdvanceWithoutSnapshot(GameSession.MedicalTreatmentTicks);
                if (_session.CaptureMedical()!.ResponseStage != MedicalResponseStage.Completed)
                    throw new InvalidOperationException("Medic did not complete treatment.");
                _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
                RefreshPreparationHud();
                GD.Print($"MEDICAL_UI_STATE completed={_session.CaptureMedical()!.Stage} thirst={_session.CaptureMedical()!.Needs.Single(item => item.AgentId == medical.AtRiskGuestId).Thirst} heat={_session.CaptureMedical()!.Needs.Single(item => item.AgentId == medical.AtRiskGuestId).HeatExposure}");
            }
            if (_medicalCaptureFrame == 22)
            {
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_medicalCaptureDirectory, "treated-green-bars.png"));
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
            if (_medicalCaptureMode == "prevent")
            {
                SelectAttendee(new EntityId(_session.CaptureMedical()!.AtRiskGuestId));
                CommitMedicalAction(MedicalAction.DispatchMedic);
            }
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

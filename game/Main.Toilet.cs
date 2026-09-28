using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private const string ToiletAsset = "res://assets/environment/portaloo/lwf_portaloo_v1.glb";
    private const string OccupiedIndicatorAsset = "res://assets/environment/portaloo/lwf_portaloo_indicator_occupied_v1.glb";
    private StaticBody3D? _toiletBody;
    private Node3D? _toiletDoorPivot;
    private Node3D? _toiletFreeIndicator;
    private Node3D? _toiletOccupiedIndicator;
    private readonly HashSet<ulong> _toiletPickIds = [];
    private bool _selectedToilet;
    private Button? _toiletMoveButton;
    private bool _movingToilet;
    private int _toiletQuarterTurns;
    private GridCell? _toiletCandidate;
    private string? _toiletPlacementIssue;
    private Node3D? _toiletPreview;
    private MeshInstance3D? _toiletFootprintPreview;
    private Label3D? _toiletPreviewLabel;

    private static Node3D RequireToiletNode(Node3D root, string name) =>
        root.FindChild(name, true, false) as Node3D ?? throw new InvalidOperationException("Approved portaloo node missing: " + name);

    private void AddToiletCollision(StaticBody3D body)
    {
        // Disjoint shell boxes keep the measured doorway/interior open. The moving
        // door has its own body under DoorPivot, never part of a static hull.
        foreach (var (centre, size) in new (Vector3, Vector3)[]
        {
            (new(0,.04f,0),new(1.46f,.08f,1.56f)),
            (new(-.67f,1.11f,0),new(.06f,2.06f,1.5f)),
            (new(.67f,1.11f,0),new(.06f,2.06f,1.5f)),
            (new(-.565f,1.09f,-.73f),new(.27f,2.02f,.08f)),
            (new(.565f,1.09f,-.73f),new(.27f,2.02f,.08f)),
            (new(0,1.11f,.72f),new(1.28f,2.06f,.06f)),
            (new(0,2.135f,-.73f),new(1.4f,.07f,.08f)),
            (new(0,2.24f,0),new(1.5f,.22f,1.58f)),
            (new(0,.275f,.465f),new(1.12f,.39f,.45f)),
        }) body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = centre });
        var door = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0, Name = "PortalooDoorPick" };
        _toiletDoorPivot!.AddChild(door);
        door.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(.9f,2.032f,.035f) },
            Position = new(-.45f,1.024f,0) });
        _toiletPickIds.Add(body.GetInstanceId());
        _toiletPickIds.Add(door.GetInstanceId());
    }

    private void SyncToiletWorld()
    {
        var toilet = _session.CaptureToilet();
        if (toilet is null)
        {
            if (_toiletBody is not null) { _toiletBody.Visible = false; _toiletBody.QueueFree(); }
            _toiletBody = null; _toiletPickIds.Clear();
            return;
        }
        if (_toiletBody is null)
        {
            _toiletBody = new StaticBody3D { Name = "OwnedPortaloo", CollisionLayer = 1, CollisionMask = 0 };
            var visual = InstantiateAsset(ToiletAsset);
            _toiletBody.AddChild(visual);
            _toiletDoorPivot = RequireToiletNode(visual, "DoorPivot");
            var socket = RequireToiletNode(visual, "OccupancySocket");
            _toiletFreeIndicator = RequireToiletNode(visual, "IndicatorFree");
            _toiletOccupiedIndicator = InstantiateAsset(OccupiedIndicatorAsset);
            socket.AddChild(_toiletOccupiedIndicator);
            AddChild(_toiletBody);
            AddToiletCollision(_toiletBody);
            var name = BuildingName("TOILET", new Vector3(0, 2.8f, 0));
            name.FontSize = 54; name.PixelSize = .009f;
            _toiletBody.AddChild(name);
        }
        _toiletBody.Position = ImmersionPosition(toilet.Cell);
        _toiletBody.RotationDegrees = new Vector3(0, toilet.QuarterTurns * 90, 0);
        _toiletDoorPivot!.RotationDegrees = new Vector3(0, toilet.DoorOpen ? -110 : 0, 0);
        _toiletFreeIndicator!.Visible = toilet.OwnerId is null && toilet.InterruptedOccupantId is null && !toilet.IsFull;
        _toiletOccupiedIndicator!.Visible = !_toiletFreeIndicator.Visible;
        RefreshToiletInspector();
    }

    private void BuildToiletInspector(VBoxContainer parent)
    {
        _toiletMoveButton = ButtonText("Move", () =>
        {
            if (_movingToilet) CancelToiletPlacement();
            else if (_selectedToilet) BeginToiletPlacement();
            RefreshPreparationHud();
        });
        _toiletMoveButton.Visible = false;
        _toiletMoveButton.TooltipText = "Move before opening. Comma/period rotate; right-click or Esc cancels.";
        parent.AddChild(_toiletMoveButton);
    }

    private void SelectToilet()
    {
        ClearSelection(); _selectedToilet = true; RefreshToiletInspector();
    }

    private void RefreshToiletInspector()
    {
        RefreshContextPanelVisibility();
        if (_toiletMoveButton is null) return;
        _toiletMoveButton.Visible = _selectedToilet && _session.PreparedStatus == PreparationStatus.Preparing;
        _toiletMoveButton.Text = _movingToilet ? "Cancel move" : "Move";
        if (!_selectedToilet || _toiletBody is null || _session.CaptureToilet() is not { } toilet) return;
        _inspectorTitle.Text = "Portaloo • owned";
        _inspectorBody.Text = $"{(toilet.InterruptedOccupantId is { } interrupted ?
            $"Unavailable • {_session.CapturePreparation()!.People.Single(p => p.AgentId == interrupted).Name} needs a clear exit" :
            toilet.IsFull ? "FULL • no new visits" : toilet.OwnerId is { } occupant ?
            $"Occupied by {_session.CapturePreparation()!.People.Single(p => p.AgentId == occupant).Name}" : "Free")}" +
            $"\nTank {toilet.UsedMillilitres / 1000m:0.0}/{toilet.CapacityMillilitres / 1000m:0.0} L • {toilet.FullPercent}% full" +
            $"\nWees {toilet.WeeCount} • poos {toilet.PooCount} • queue {toilet.Queue.Length}" +
            $"\nFacing {toilet.QuarterTurns * 90}° • preparation placement only" +
            "\nSmell rises with waste; containment is a future upgrade hook.";
        _highlight.Position = _toiletBody.Position + new Vector3(0, .08f, 0);
        _highlight.Scale = new Vector3(1.3f, 1, 1.5f); _highlight.Visible = true;
    }

    private void BeginToiletPlacement()
    {
        if (_session.PreparedStatus != PreparationStatus.Preparing || _session.CaptureToilet() is not { } toilet) return;
        CancelWaterPlacement(); CancelImmersionPlacement(); CancelResponsePostPlacement(); CancelToiletPlacement();
        ClearSelection(); _selectedToilet = true;
        _movingToilet = true; _toiletQuarterTurns = toilet.QuarterTurns;
        _toiletPreview = InstantiateAsset(ToiletAsset); AddChild(_toiletPreview);
        _toiletPreviewLabel = new Label3D { FontSize = 34, PixelSize = .009f,
            Position = new Vector3(0, 3f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled };
        _toiletPreview.AddChild(_toiletPreviewLabel);
        _toiletFootprintPreview = new MeshInstance3D { Mesh = new BoxMesh { Size = new(3.5f,.035f,3.5f) },
            MaterialOverride = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
        AddChild(_toiletFootprintPreview);
        _preparationMessage = "Move toilet: click valid grass; comma/period rotate; right-click or Esc cancels.";
        RefreshPreparationHud(); UpdateToiletPlacementPreview(GetViewport().GetMousePosition());
    }

    private void CancelToiletPlacement()
    {
        _movingToilet = false; _toiletCandidate = null; _toiletPlacementIssue = null;
        if (_toiletPreview is not null) { _toiletPreview.Visible = false; _toiletPreview.QueueFree(); }
        if (_toiletFootprintPreview is not null) { _toiletFootprintPreview.Visible = false; _toiletFootprintPreview.QueueFree(); }
        _toiletPreview = null; _toiletFootprintPreview = null; _toiletPreviewLabel = null;
    }

    private void UpdateToiletPlacementPreview(Vector2 screen)
    {
        if (!_movingToilet || _toiletPreview is null) return;
        if (HudBlocksPlacement(screen))
        { _toiletCandidate = null; _toiletPreview.Visible = false; _toiletFootprintPreview!.Visible = false; return; }
        var ray = _camera.ProjectRayNormal(screen); var origin = _camera.ProjectRayOrigin(screen);
        if (Mathf.Abs(ray.Y) < .001f || -origin.Y / ray.Y <= 0)
        { _toiletCandidate = null; _toiletPreview.Visible = false; _toiletFootprintPreview!.Visible = false; return; }
        var point = origin + ray * (-origin.Y / ray.Y);
        var cell = TraversalGrid.WorldToCell(Mathf.RoundToInt(point.X * 1000), Mathf.RoundToInt(point.Z * 1000));
        if (_toiletCandidate == cell && _toiletPreview.Visible) return;
        _toiletCandidate = cell;
        _toiletPlacementIssue = _session.ValidateCommand(CampaignEnvelope(new MoveToiletCommand(cell, _toiletQuarterTurns)))?.Message;
        _toiletPreview.Position = ImmersionPosition(cell);
        _toiletPreview.RotationDegrees = new Vector3(0, _toiletQuarterTurns * 90, 0);
        _toiletPreview.Visible = true;
        _toiletPreviewLabel!.Text = _toiletPlacementIssue is null ? "VALID • CLICK TO MOVE" : "INVALID • " + _toiletPlacementIssue;
        var colour = _toiletPlacementIssue is null ? new Color(.25f,.78f,.38f,.4f) : new Color(.9f,.24f,.18f,.4f);
        _toiletPreviewLabel.Modulate = new Color(_toiletPlacementIssue is null ? "67db76" : "ff7566");
        _toiletFootprintPreview!.Position = _toiletPreview.Position + new Vector3(0,.06f,0);
        _toiletFootprintPreview.RotationDegrees = _toiletPreview.RotationDegrees;
        ((StandardMaterial3D)_toiletFootprintPreview.MaterialOverride!).AlbedoColor = colour;
        _toiletFootprintPreview.Visible = true;
    }

    private void RotateToiletPlacement(int step)
    {
        _toiletQuarterTurns = (_toiletQuarterTurns + step + 4) % 4;
        _toiletCandidate = null;
        UpdateToiletPlacementPreview(GetViewport().GetMousePosition());
    }

    private void CommitToiletPlacement(Vector2 screen)
    {
        UpdateToiletPlacementPreview(screen);
        if (_toiletCandidate is not { } cell || _toiletPlacementIssue is not null) return;
        CommitEquipmentAction(new MoveToiletCommand(cell, _toiletQuarterTurns));
        if (_session.CaptureToilet() is { } toilet && toilet.Cell == cell && toilet.QuarterTurns == _toiletQuarterTurns)
            CancelToiletPlacement();
    }
}

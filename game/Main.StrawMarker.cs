using Festival.Simulation;
using Godot;

namespace Festival.Game;

/// <summary>
/// Where a bale will go. While the player picks a spot after Spread straw, a gold ring the size of the spread
/// (<see cref="StrawJobRules.RadiusCells"/>) follows the cursor, with the very cells the bale would cover there now
/// (<see cref="GameSession.StrawSpreadPreview"/>) tinted inside it; red where the worker can't go, grey where there's no
/// mud to cover. Once ordered, the ring stays on the spot until the worker has spread the bale. Procedural shapes in the
/// cover tool's colours for now. Presentation only: the sim is only read.
/// </summary>
public partial class Main
{
    private static readonly Color StrawMarkGold = new(1f, .8f, .25f), StrawMarkBlocked = new(.9f, .18f, .15f), StrawMarkEmpty = new(.62f, .6f, .55f);
    private Node3D? _strawMark;
    private MeshInstance3D? _strawMarkRing, _strawMarkCentre;
    private MultiMeshInstance3D? _strawMarkCells;
    private StandardMaterial3D? _strawMarkRingMaterial, _strawMarkCellMaterial;
    private GridCell? _strawMarkSpot;
    private bool _strawMarkPicking;
    private double _strawMarkRefresh;
    private string? _strawPickIssue;

    private void BuildStrawMark()
    {
        StandardMaterial3D Material() => new()
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _strawMarkRingMaterial = Material(); _strawMarkCellMaterial = Material();
        _strawMark = new Node3D { Name = "StrawMark", Visible = false };
        // The ring's middle sits on the edge of the outermost cells the spread can reach.
        var reach = (StrawJobRules.RadiusCells + .5f) * TraversalGrid.CellSizeMillimetres / 1000f;
        _strawMarkRing = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = reach - .07f, OuterRadius = reach + .07f, Rings = 48, RingSegments = 6 },
            MaterialOverride = _strawMarkRingMaterial, Scale = new Vector3(1, .15f, 1), Position = new Vector3(0, .07f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = EyeHiddenLayer,
        };
        _strawMarkCentre = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = .14f, BottomRadius = .14f, Height = .02f, RadialSegments = 16 },
            MaterialOverride = _strawMarkRingMaterial, Position = new Vector3(0, .07f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = EyeHiddenLayer,
        };
        var size = TraversalGrid.CellSizeMillimetres / 1000f * .86f;
        _strawMarkCells = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = GroundCoverRules.CellsPerBale, VisibleInstanceCount = 0,
                Mesh = new PlaneMesh { Size = new Vector2(size, size) },
            },
            MaterialOverride = _strawMarkCellMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = EyeHiddenLayer,
        };
        _strawMark.AddChild(_strawMarkRing); _strawMark.AddChild(_strawMarkCentre);
        AddChild(_strawMark); AddChild(_strawMarkCells);
    }

    /// <summary>Each frame: the ring under the cursor while picking, or on the ordered spot until the bale's down.</summary>
    private void ProcessStrawMark(double delta)
    {
        var job = _session.CaptureWeather()?.StrawJob;
        var picking = _strawPicker is not null && !EyeViewActive && !StewardShiftActive;
        GridCell? spot = null;
        if (picking)
        {
            var screen = GetViewport().GetMousePosition();
            if (!HudBlocksPlacement(screen)) spot = FieldCellAt(screen);
        }
        else if (job is not null && !EyeViewActive && !StewardShiftActive) spot = job.Spot;
        if (spot is not { } cell)
        {
            if (_strawMark is not null) { _strawMark.Visible = false; _strawMarkCells!.Visible = false; }
            _strawMarkSpot = null;
            return;
        }
        if (_strawMark is null) BuildStrawMark();
        var moved = cell != _strawMarkSpot || picking != _strawMarkPicking;
        _strawMarkRefresh -= delta;
        if (!moved && _strawMarkRefresh > 0) return;
        _strawMarkRefresh = .5;
        _strawMarkSpot = cell; _strawMarkPicking = picking;
        // Validating finds the worker a route, so only when the cursor reaches a new cell.
        if (picking && moved) _strawPickIssue = _session.ValidateCommand(CampaignEnvelope(new SpreadStrawCommand(_strawPicker!.Value, cell)))?.Message;
        var cells = picking && _strawPickIssue is not null ? System.Array.Empty<GridCell>() : _session.StrawSpreadPreview(cell);
        // Once spreading, only what's left of the bale: the cells already down are straw now and drop out of the preview.
        if (!picking && job is not null) cells = cells[..System.Math.Min(cells.Length, GroundCoverRules.CellsPerBale - job.Spread)];
        var (x, z) = TraversalGrid.CellCentre(cell);
        _strawMark!.Position = new Vector3(x / 1000f, 0, z / 1000f);
        var tint = picking && _strawPickIssue is not null ? StrawMarkBlocked : cells.Length == 0 && picking ? StrawMarkEmpty : StrawMarkGold;
        // Ordered, the ring is steadier and the cells it has still to cover fainter, so it reads as "booked", not "choosing".
        _strawMarkRingMaterial!.AlbedoColor = new Color(tint, picking ? .9f : .7f);
        _strawMarkCellMaterial!.AlbedoColor = new Color(tint, picking ? .5f : .3f);
        var multimesh = _strawMarkCells!.Multimesh;
        for (var i = 0; i < cells.Length; i++)
        {
            var (cx, cz) = TraversalGrid.CellCentre(cells[i]);
            multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Vector3(cx / 1000f, .06f, cz / 1000f)));
        }
        multimesh.VisibleInstanceCount = cells.Length;
        _strawMark.Visible = true; _strawMarkCells.Visible = true;
        if (!picking) return;
        var hint = _strawPickIssue ?? (cells.Length == 0
            ? "No mud within 3 m of here: the bale would have nothing to cover · Esc cancels."
            : $"Spread a bale here · {cells.Length * TraversalGrid.CellSizeMillimetres * TraversalGrid.CellSizeMillimetres / 1_000_000f:0.#} m² of mud to cover · Esc cancels.");
        if (_hudStatus is not null && _hudStatus.Text != hint) { _preparationMessage = hint; _hudStatus.Text = hint; }
    }
}

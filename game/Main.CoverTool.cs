using Festival.Simulation;
using Godot;
using System;

namespace Festival.Game;

/// <summary>
/// Laying straw and track matting in Build: press on the field, drag along the way people walk, release to lay a
/// straight run (straw three cells wide, matting a panel's 2 m). Lift takes up whatever lies along the drag. The tool
/// stays in hand for the next run; right-click or Esc puts it down.
/// </summary>
public partial class Main
{
    private GroundCoverAction? _coverTool;
    private GridCell? _coverFrom;
    private MeshInstance3D? _coverPreview;
    private StandardMaterial3D? _coverPreviewMaterial;
    private string? _coverIssue;

    void IBuildActions.BeginCover(GroundCoverAction action) => BeginCoverTool(action);

    private static string CoverName(GroundCoverAction action) => action switch
    {
        GroundCoverAction.Straw => "straw", GroundCoverAction.Matting => "track matting", _ => "lift straw and matting",
    };

    private string CoverHint() => _coverTool is not { } action ? "" : action == GroundCoverAction.Lift
        ? "Lift · drag along the straw or matting to take it up · right-click or Esc to finish."
        : $"Lay {CoverName(action)} · drag along a path · " + (action == GroundCoverAction.Straw
            ? $"{FestivalCurrency.Format(GroundCoverRules.BalePennies)} a bale, {GroundCoverRules.CellsPerBale / GroundCoverRules.StrawWidthCells / 2} m of path"
            : $"{FestivalCurrency.Format(GroundCoverRules.MatPanelPennies)} a 2 × 1 m panel") + " · right-click or Esc to finish.";

    private void BeginCoverTool(GroundCoverAction action)
    {
        if (_session.PreparedStatus != PreparationStatus.Preparing) return;
        CancelBuildPlacement();
        CancelCoverTool();
        _coverTool = action;
        _coverPreviewMaterial = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true,
        };
        _coverPreview = new MeshInstance3D { Name = "CoverPreview", Mesh = new BoxMesh { Size = new Vector3(1, .02f, 1) }, MaterialOverride = _coverPreviewMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        AddChild(_coverPreview);
        ShowAudienceArea(true);
        _buildDrawerOpen = false; _hudWorkspaceOpen = false;
        _preparationMessage = CoverHint();
        RefreshHudWorkspace();
        UpdateCoverPreview(GetViewport().GetMousePosition());
    }

    private void CancelCoverTool()
    {
        if (_coverTool is null) return;
        _coverTool = null; _coverFrom = null; _coverIssue = null;
        _coverPreview?.QueueFree(); _coverPreview = null;
        ShowAudienceArea(false);
        Drawer.Refresh();
    }

    private GridCell? FieldCellAt(Vector2 screen)
    {
        var ray = _rig.Camera.ProjectRayNormal(screen); var origin = _rig.Camera.ProjectRayOrigin(screen);
        if (Mathf.Abs(ray.Y) < .001f || -origin.Y / ray.Y <= 0) return null;
        var point = origin + ray * (-origin.Y / ray.Y);
        return TraversalGrid.WorldToCell(Mathf.RoundToInt(point.X * 1000), Mathf.RoundToInt(point.Z * 1000));
    }

    /// <summary>Shows the run the drag would lay, gold for straw, charcoal for matting, red where it can't go or to lift.</summary>
    private void UpdateCoverPreview(Vector2 screen)
    {
        if (_coverTool is not { } action || _coverPreview is null || _coverPreviewMaterial is null) return;
        if (HudBlocksPlacement(screen) || FieldCellAt(screen) is not { } cell) { _coverPreview.Visible = false; return; }
        var from = _coverFrom ?? cell;
        var alongX = Math.Abs(cell.X - from.X) > Math.Abs(cell.Z - from.Z);
        var length = Math.Min(GroundCoverRules.MaximumRunCells, Math.Abs(alongX ? cell.X - from.X : cell.Z - from.Z) + 1);
        var width = action == GroundCoverAction.Straw ? GroundCoverRules.StrawWidthCells : 4;
        var low = Math.Min(alongX ? from.X : from.Z, alongX ? cell.X : cell.Z);
        // The run's middle, in metres: the drag's line, and halfway along.
        var line = alongX ? from.Z : from.X;
        var acrossMiddle = action == GroundCoverAction.Straw ? line : line - .5f;
        var alongMiddle = low + (length - 1) / 2f;
        var centre = TraversalGrid.CellCentre(new GridCell(0, 0));
        float Metres(float cells, int origin) => (origin + cells * TraversalGrid.CellSizeMillimetres) / 1000f;
        var x = alongX ? Metres(alongMiddle, centre.XMillimetres) : Metres(acrossMiddle, centre.XMillimetres);
        var z = alongX ? Metres(acrossMiddle, centre.ZMillimetres) : Metres(alongMiddle, centre.ZMillimetres);
        _coverPreview.Position = new Vector3(x, .06f, z);
        _coverPreview.Scale = alongX ? new Vector3(length * .5f, 1, width * .5f) : new Vector3(width * .5f, 1, length * .5f);
        _coverPreview.Visible = true;
        _coverIssue = _session.ValidateCommand(CampaignEnvelope(new GroundCoverCommand(action, from, cell)))?.Message;
        _coverPreviewMaterial.AlbedoColor = _coverIssue is not null || action == GroundCoverAction.Lift ? new Color(.9f, .18f, .15f, .45f) :
            action == GroundCoverAction.Straw ? new Color(1f, .8f, .25f, .7f) : new Color(.12f, .13f, .15f, .75f);
        var cost = action switch
        {
            GroundCoverAction.Straw => $" · about {GroundCoverRules.BalesFor(length * width)} bale{(GroundCoverRules.BalesFor(length * width) == 1 ? "" : "s")}",
            GroundCoverAction.Matting => $" · {Math.Max(1, length / 2)} panel{(length / 2 == 1 ? "" : "s")} · {FestivalCurrency.Format(Math.Max(1, length / 2) * GroundCoverRules.MatPanelPennies)}",
            _ => "",
        };
        var hint = _coverIssue ?? (_coverFrom is null ? CoverHint() : $"{length / 2f:0.#} m of {CoverName(action)}{cost} · release to lay");
        if (_hudStatus is not null && _hudStatus.Text != hint) { _preparationMessage = hint; _hudStatus.Text = hint; }
    }

    /// <summary>The cover tool's mouse: press starts a run, release lays it, right-click puts the tool down.</summary>
    private bool HandleCoverMouse(InputEventMouseButton mouse)
    {
        if (_coverTool is not { } action) return false;
        if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed) { CancelCoverTool(); RefreshHudWorkspace(); return true; }
        if (mouse.ButtonIndex != MouseButton.Left) return false;
        if (mouse.Pressed) { _coverFrom = FieldCellAt(mouse.Position); UpdateCoverPreview(mouse.Position); return true; }
        if (_coverFrom is not { } from || FieldCellAt(mouse.Position) is not { } to) { _coverFrom = null; return true; }
        _coverFrom = null;
        var command = new GroundCoverCommand(action, from, to);
        if (_session.ValidateCommand(CampaignEnvelope(command)) is { } issue) { _preparationMessage = issue.Message; RefreshPreparationHud(); return true; }
        CommitEquipmentAction(command);
        Drawer.Refresh();
        UpdateCoverPreview(mouse.Position);
        return true;
    }
}

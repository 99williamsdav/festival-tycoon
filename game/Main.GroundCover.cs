using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// Straw and track matting on the field. Straw is drawn by the ground shader from a straw map (fresh to trampled, with
/// ragged edges), from the Build plan before opening and from the ground once the gates are open. Matting is one
/// multimesh of 2 × 1 m panels, laid by the run rules: across the path, one a metre.
/// </summary>
public partial class Main
{
    private Image? _strawImage;
    private ImageTexture? _strawTexture;
    private (GameSession? Session, long Version, int[]? Plan) _strawShown;
    private MultiMeshInstance3D? _matPanelsView;
    private MatPanel[]? _matPanelsShown;
    private Mesh? _matMesh;

    private ImageTexture StrawTexture()
    {
        if (_strawTexture is not null) return _strawTexture;
        _strawImage = Image.CreateEmpty(GroundMapCells, GroundMapCells, false, Image.Format.Rg8);
        _strawTexture = ImageTexture.CreateFromImage(_strawImage);
        return _strawTexture;
    }

    private void SetUpStrawShader(ShaderMaterial material)
    {
        material.SetShaderParameter("straw_map", StrawTexture());
        material.SetShaderParameter("straw_fresh", GD.Load<Texture2D>("res://assets/environment/lwf_straw_fresh_v1.png"));
        material.SetShaderParameter("straw_trampled", GD.Load<Texture2D>("res://assets/environment/lwf_straw_trampled_v1.png"));
        material.SetShaderParameter("straw_breakup", GD.Load<Texture2D>("res://assets/environment/lwf_straw_breakup_v1.png"));
        material.SetShaderParameter("ripple_atlas", GD.Load<Texture2D>("res://assets/environment/lwf_rain_ripple_v1.png"));
    }

    /// <summary>Redraws the straw map when the ground or the plan's straw changes: presence box-blurred over 3 × 3 cells, and trampling.</summary>
    private void ProcessGroundCover()
    {
        if (_session is null || _strawImage is null || _strawTexture is null) return;
        var plan = _session.PreparedStatus == PreparationStatus.Preparing ? _session.CapturePreparation()?.StrawCells : null;
        if (_strawShown.Session == _session && _strawShown.Version == _session.GroundVersion && ReferenceEquals(_strawShown.Plan, plan))
        { SyncMatPanels(); return; }
        _strawShown = (_session, _session.GroundVersion, plan);
        const int n = GroundMapCells, first = GroundRules.FieldFirstCell;
        var straw = new float[n * n]; var trample = new float[n * n];
        if (plan is not null)
            foreach (var index in plan)
            {
                int x = index % TraversalGrid.Width - first, z = index / TraversalGrid.Width - first;
                if (x is >= 0 and < n && z is >= 0 and < n) straw[z * n + x] = 1;
            }
        else
            for (var z = 0; z < n; z++)
            for (var x = 0; x < n; x++)
                if (_session.GroundTrampleAt(new GridCell(first + x, first + z)) is var t and >= 0)
                { straw[z * n + x] = 1; trample[z * n + x] = Math.Min(1f, t / (float)GroundCoverRules.TrampleMaximum); }
        var pixels = new byte[n * n * 2];
        for (var z = 0; z < n; z++)
        for (var x = 0; x < n; x++)
        {
            float sum = 0, flat = 0, count = 0;
            for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
            {
                int sx = Math.Clamp(x + dx, 0, n - 1), sz = Math.Clamp(z + dz, 0, n - 1);
                sum += straw[sz * n + sx];
                if (straw[sz * n + sx] > 0) { flat += trample[sz * n + sx]; count++; }
            }
            pixels[(z * n + x) * 2] = (byte)Mathf.RoundToInt(255 * sum / 9);
            pixels[(z * n + x) * 2 + 1] = (byte)Mathf.RoundToInt(255 * (count == 0 ? 0 : flat / count));
        }
        _strawImage.SetData(n, n, false, Image.Format.Rg8, pixels);
        _strawTexture.Update(_strawImage);
        SyncMatPanels();
    }

    /// <summary>Lays the matting's panels: a run along Z square across it, a run along X turned a quarter.</summary>
    private void SyncMatPanels()
    {
        var panels = _session?.CapturePreparation()?.MatPanels;
        if (ReferenceEquals(panels, _matPanelsShown) && _matPanelsView is not null) return;
        _matPanelsShown = panels;
        if (_matMesh is null)
        {
            var scene = InstantiateAsset("res://assets/environment/lwf_track_mat_grid_v1.glb");
            _matMesh = scene.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().First().Mesh;
            scene.QueueFree();
        }
        if (_matPanelsView is null)
        {
            _matPanelsView = new MultiMeshInstance3D { Name = "TrackMatting", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(_matPanelsView);
        }
        var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = _matMesh, InstanceCount = panels?.Length ?? 0 };
        for (var i = 0; i < (panels?.Length ?? 0); i++)
        {
            var panel = panels![i];
            var corner = TraversalGrid.CellCentre(panel.Corner);
            // Four cells across and two along, so the middle is a cell and a half and half a cell in.
            var (dx, dz) = panel.AlongX ? (250, 750) : (750, 250);
            var at = new Vector3((corner.XMillimetres + dx) / 1000f, .005f, (corner.ZMillimetres + dz) / 1000f);
            multimesh.SetInstanceTransform(i, new Transform3D(panel.AlongX ? new Basis(Vector3.Up, Mathf.Pi / 2) : Basis.Identity, at));
        }
        _matPanelsView.Multimesh = multimesh;
        _matPanelsView.Visible = multimesh.InstanceCount > 0;
    }
}

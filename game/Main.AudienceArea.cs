using Festival.Simulation;
using Godot;
using System.Collections.Generic;

namespace Festival.Game;

/// <summary>While placing a service, each stage's audience ground in front of it shows as a faint marked area.</summary>
public partial class Main
{
    private readonly Dictionary<string, MeshInstance3D> _audienceAreas = [];

    private void ShowAudienceArea(bool show)
    {
        foreach (var stage in FestivalStages.All)
        {
            var open = show && FestivalStages.IndexOf(_session.Stages, stage.Id) >= 0;
            if (!_audienceAreas.TryGetValue(stage.Id, out var area))
            {
                if (!open) continue;
                _audienceAreas[stage.Id] = area = BuildAudienceArea(stage);
            }
            area.Visible = open;
        }
    }

    private MeshInstance3D BuildAudienceArea(FestivalStage stage)
    {
        var (min, max) = GameSession.AudienceAreaOf(stage);
        var from = TraversalGrid.CellCentre(min); var to = TraversalGrid.CellCentre(max);
        float x0 = from.XMillimetres / 1000f - .25f, z0 = from.ZMillimetres / 1000f - .25f;
        float x1 = to.XMillimetres / 1000f + .25f, z1 = to.ZMillimetres / 1000f + .25f;
        var area = new MeshInstance3D
        {
            Name = "AudienceArea",
            Mesh = new PlaneMesh { Size = new Vector2(x1 - x0, z1 - z0) },
            Position = new Vector3((x0 + x1) / 2, 0.08f, (z0 + z1) / 2),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.78f, 0.3f, 0.42f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        area.AddChild(new Label3D
        {
            Text = "AUDIENCE", FontSize = 64, PixelSize = .02f, Modulate = new Color(1f, 0.9f, 0.6f, 0.8f),
            RotationDegrees = new Vector3(-90, 0, 0), Position = new Vector3(0, 0.02f, 0), Shaded = false,
        });
        AddChild(area);
        return area;
    }
}

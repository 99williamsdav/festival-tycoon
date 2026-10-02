using Festival.Simulation;
using Godot;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// Right-click a placed service on the farm while preparing to move or remove it, without opening Build.
/// </summary>
public partial class Main
{
    private PopupMenu? _buildContextMenu;
    private string? _buildContextId;
    private const float BuildContextReachMetres = 2.6f;

    /// <summary>
    /// The placed service under the cursor. The ray is followed down through head height to the ground, and the first
    /// service whose footprint it crosses wins, so clicking a van's roof or far end finds the van, not the grass
    /// behind it. Failing that, the nearest service centre within reach of where it meets the ground.
    /// </summary>
    private BuildPlacement? PlacementUnder(Vector2 screen)
    {
        var ray = _rig.Camera.ProjectRayNormal(screen); var origin = _rig.Camera.ProjectRayOrigin(screen);
        if (Mathf.Abs(ray.Y) < .001f || -origin.Y / ray.Y <= 0) return null;
        var placements = _session.CaptureBuildPlacements();
        var footprints = placements.Select(item => (Item: item, Cells: GameSession.BuildFootprint(item).ToHashSet())).ToArray();
        for (var height = 3.0f; height >= 0; height -= 0.25f)
        {
            var point = origin + ray * ((height - origin.Y) / ray.Y);
            var cell = TraversalGrid.WorldToCell(Mathf.RoundToInt(point.X * 1000), Mathf.RoundToInt(point.Z * 1000));
            if (footprints.FirstOrDefault(pair => pair.Cells.Contains(cell)).Item is { } hit) return hit;
        }
        var ground = origin + ray * (-origin.Y / ray.Y);
        return placements
            .Select(item => (Item: item, Distance: new Vector2(ImmersionPosition(item.Cell).X - ground.X, ImmersionPosition(item.Cell).Z - ground.Z).Length()))
            .Where(pair => pair.Distance <= BuildContextReachMetres)
            .OrderBy(pair => pair.Distance).Select(pair => pair.Item).FirstOrDefault();
    }

    /// <summary>Opens the Move / Remove menu for a placed service; true if one was under the cursor.</summary>
    private bool OpenBuildContextMenu(Vector2 screen)
    {
        if (_session.PreparedStatus != PreparationStatus.Preparing || _buildGhostKind is not null || PlacementUnder(screen) is not { } item) return false;
        if (_buildContextMenu is null)
        {
            _buildContextMenu = new PopupMenu();
            _buildContextMenu.IdPressed += id =>
            {
                if (_buildContextId is not { } chosen ||
                    _session.CaptureBuildPlacements().FirstOrDefault(p => p.Id == chosen) is not { } placed) return;
                if (id == 0) BeginBuildPlacement(placed.Kind, placed.Id);
                else RemoveBuildPlacement(placed.Id);
            };
            _buildContextMenu.Theme = HudTheme();
            _buildContextMenu.AddThemeConstantOverride("icon_max_width", Ui.Px(15));
            _buildContextMenu.AddThemeFontSizeOverride("font_size", Ui.Px(13));
            AddChild(_buildContextMenu);
        }
        _buildContextId = item.Id;
        var name = BuildDrawer.PlacedName(item);
        _buildContextMenu.Clear();
        _buildContextMenu.AddSeparator(name);
        _buildContextMenu.AddIconItem(Ui.Icon("move"), $"Move {BuildName(item.Kind).ToLowerInvariant()}", 0);
        _buildContextMenu.AddIconItem(Ui.Icon("trash-2"), $"Remove · {FestivalCurrency.Format(GameSession.BuildServiceFeePennies(item.Kind))} back", 1);
        // From canvas units to window pixels, so it opens at the cursor whatever the window size or display scale.
        _buildContextMenu.Popup(new Rect2I((Vector2I)(GetViewport().GetScreenTransform() * screen), Vector2I.Zero));
        return true;
    }
}

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

    /// <summary>The placed service nearest the ground under the cursor, if it's close enough to be the one clicked.</summary>
    private BuildPlacement? PlacementUnder(Vector2 screen)
    {
        var ray = _rig.Camera.ProjectRayNormal(screen); var origin = _rig.Camera.ProjectRayOrigin(screen);
        if (Mathf.Abs(ray.Y) < .001f || -origin.Y / ray.Y <= 0) return null;
        var point = origin + ray * (-origin.Y / ray.Y);
        return _session.CaptureBuildPlacements()
            .Select(item => (Item: item, Distance: new Vector2(ImmersionPosition(item.Cell).X - point.X, ImmersionPosition(item.Cell).Z - point.Z).Length()))
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
        _buildContextMenu.Position = (Vector2I)(screen + GetViewport().GetVisibleRect().Position);
        _buildContextMenu.Popup();
        return true;
    }
}

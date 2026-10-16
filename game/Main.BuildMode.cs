using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main : IBuildActions
{
    private BuildDrawer? _drawerView;
    private BuildDrawer Drawer => _drawerView ??= new(this, this);

    void IBuildActions.BeginPlacement(BuildServiceKind kind, string? movingId) => BeginBuildPlacement(kind, movingId);
    void IBuildActions.RemovePlacement(string id) => RemoveBuildPlacement(id);
    void IBuildActions.ApplyDefaults() => ApplyBuildDefaults();
    void IBuildActions.ApplyLastLayout() => ApplyLastFestivalLayout();
    void IBuildActions.CloseDrawer() { _buildDrawerOpen = false; TraderFlyerBoard.Close(); RefreshHudWorkspace(); }
    void IBuildActions.OpenCatalogue(BuildServiceKind kind) => OpenBuildCatalogue(kind);
    void IBuildActions.OpenTab(string name) => SelectHudTab(name);
    void IBuildActions.OpenTraderBoard(string vanId) => OpenTraderBoard(vanId);
    string? IBuildActions.TraderBoardVan => _traderBoardView?.Van;

    private TraderBoard? _traderBoardView;
    private TraderBoard TraderFlyerBoard => _traderBoardView ??= new(this, RefreshHudWorkspace);

    /// <summary>
    /// Opens the trader flyer board on a placed van's tab, with the Build drawer's Food &amp; drink tab beside it (the board
    /// dims only the map). From a van clicked on the field, its drawer line, or a van just placed.
    /// </summary>
    private void OpenTraderBoard(string vanId)
    {
        if (_session.PreparedStatus != PreparationStatus.Preparing || _buildGhostKind is not null || !_session.PlacedVans.Contains(vanId)) return;
        _buildDrawerOpen = true; _hudWorkspaceOpen = false;
        Drawer.ShowRow(BuildServiceKind.FoodVan);
        TraderFlyerBoard.Open(vanId);
    }
    private bool _buildDrawerOpen;
    private BuildServiceKind? _buildGhostKind;
    private string? _buildMovingId;
    private int _buildQuarterTurns;
    private GridCell? _buildCandidate;
    private string? _buildCandidateIssue;
    private bool _buildClickRejected;
    private Node3D? _buildGhost;
    private readonly StandardMaterial3D _buildBlockedOverlay = new()
    {
        AlbedoColor = new Color(.9f, .18f, .15f, .58f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        NoDepthTest = true
    };


    private Node3D BuildAsset(BuildServiceKind kind, string? movingId = null) => kind switch
    {
        BuildServiceKind.WaterTap => InstantiateAsset("res://assets/environment/lwf_free_water_point_v4.glb"),
        BuildServiceKind.Toilet => InstantiateAsset(ToiletAsset),
        BuildServiceKind.FoodVan => InstantiateImmersionVendor(_session.TraderAt(movingId ?? Stalls.Next(kind, _session.CaptureBuildPlacements().Select(item => item.Id)))),
        BuildServiceKind.Bar => InstantiateImmersionVendor(null),
        BuildServiceKind.FirstAid => InstantiateAsset(PostAsset(ResponseRole.Medic)),
        BuildServiceKind.StewardPost => InstantiateAsset(PostAsset(ResponseRole.Steward)),
        BuildServiceKind.Bin => InstantiateAsset(BinAsset),
        BuildServiceKind.Marquee => InstantiateMarquee(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };




    private void OpenBuildCatalogue(BuildServiceKind? focus = null)
    {
        if (_session.PreparedStatus != PreparationStatus.Preparing) return;
        _buildDrawerOpen = true; _hudWorkspaceOpen = false;
        RefreshHudWorkspace();
        if (focus is { } kind) Drawer.FocusRow(kind);
    }





    private void ApplyBuildDefaults()
    {
        CommitEquipmentAction(new UseDefaultBuildLayoutCommand());
        SyncBuildWorld(); Drawer.Refresh();
    }

    /// <summary>Copies last festival's layout and says how many of its services had to be left off.</summary>
    private void ApplyLastFestivalLayout()
    {
        var fit = _session.LastFestivalLayoutFit();
        CommitEquipmentAction(new UseLastFestivalLayoutCommand());
        if (fit is { } copied && _session.CaptureBuildPlacements().Count == copied.Placed.Length)
        {
            _preparationMessage = $"Copied last festival's layout: {copied.Placed.Length} services placed" +
                (copied.Skipped > 0 ? $", {copied.Skipped} left off where they no longer fit." : ".");
            RefreshPreparationHud();
        }
        SyncBuildWorld(); Drawer.Refresh();
    }

    private void RemoveBuildPlacement(string id)
    {
        CommitEquipmentAction(new RemoveBuildServiceCommand(id));
        SyncBuildWorld(); Drawer.Refresh();
    }

    private void SyncBuildWorld()
    {
        SyncExtraWaterWorld(); SyncResponsePosts(); SyncImmersionWorld(); SyncToiletWorld(); SyncLitterWorld(); SyncMarqueeWorld();
    }

    private void BeginBuildPlacement(BuildServiceKind kind, string? movingId = null)
    {
        if (_session.PreparedStatus != PreparationStatus.Preparing) return;
CancelBuildPlacement();
        _buildGhostKind = kind; _buildMovingId = movingId;
        _buildClickRejected = false;
        _buildQuarterTurns = movingId is null ? kind == BuildServiceKind.Toilet ? 2 : 0 :
            _session.CaptureBuildPlacements().Single(item => item.Id == movingId).QuarterTurns;
        _buildGhost = BuildAsset(kind, movingId); AddChild(_buildGhost);
        ShowAudienceArea(true);
        foreach (var mesh in _buildGhost.FindChildren("*", "MeshInstance3D", true, false))
            if (mesh is GeometryInstance3D geometry) geometry.Transparency = .12f;
        _buildDrawerOpen = false; _hudWorkspaceOpen = false;
        _preparationMessage = $"{(movingId is null ? "Place" : "Move")} {BuildName(kind)} · comma/period rotate · Esc cancels.";
        RefreshHudWorkspace(); UpdateBuildGhost(GetViewport().GetMousePosition()); RequestBuildOriginOverlay();
    }

    private void CancelBuildPlacement()
    {
        _buildGhostKind = null; _buildMovingId = null; _buildCandidate = null; _buildCandidateIssue = null;
        _buildClickRejected = false;
        ClearBuildOriginOverlay();
        if (_buildGhost is not null) { _buildGhost.QueueFree(); _buildGhost = null; }
        ShowAudienceArea(false);
        Drawer.Refresh();
    }

    private void RotateBuildGhost(int step)
    {
        _buildQuarterTurns = (_buildQuarterTurns + step + 4) % 4;
        _buildCandidate = null;
        UpdateBuildGhost(GetViewport().GetMousePosition());
        RequestBuildOriginOverlay();
    }

    private void UpdateBuildGhost(Vector2 screen)
    {
        if (_buildGhostKind is not { } kind || _buildGhost is null) return;
        if (HudBlocksPlacement(screen)) { _buildGhost.Visible = false; _buildCandidate = null; RefreshBuildDebugHover(); return; }
        var ray = _rig.Camera.ProjectRayNormal(screen); var origin = _rig.Camera.ProjectRayOrigin(screen);
        if (Mathf.Abs(ray.Y) < .001f || -origin.Y / ray.Y <= 0)
        { _buildGhost.Visible = false; _buildCandidate = null; RefreshBuildDebugHover(); return; }
        var point = origin + ray * (-origin.Y / ray.Y);
        var cell = TraversalGrid.WorldToCell(Mathf.RoundToInt(point.X * 1000), Mathf.RoundToInt(point.Z * 1000));
        if (_buildCandidate != cell)
        {
            if (_buildClickRejected)
            {
                _preparationMessage = $"{(_buildMovingId is null ? "Place" : "Move")} {BuildName(kind)} · comma/period rotate · Esc cancels.";
                if (_hudStatus is not null) _hudStatus.Text = _preparationMessage;
                _buildClickRejected = false;
            }
            _buildCandidate = cell;
            var command = _buildMovingId is null ? (SessionCommand)new PlaceBuildServiceCommand(kind, cell, _buildQuarterTurns) :
                new MoveBuildServiceCommand(_buildMovingId, cell, _buildQuarterTurns);
            _buildCandidateIssue = _session.ValidateCommand(CampaignEnvelope(command))?.Message;
            foreach (var mesh in _buildGhost.FindChildren("*", "MeshInstance3D", true, false))
                if (mesh is MeshInstance3D visual) visual.MaterialOverlay = _buildCandidateIssue is null ? null : _buildBlockedOverlay;
        }
        _buildGhost.Position = ImmersionPosition(cell);
        _buildGhost.RotationDegrees = new Vector3(0, _buildQuarterTurns * 90, 0);
        _buildGhost.Visible = true;
        RefreshBuildDebugHover();
    }

    private void CommitBuildPlacement(Vector2 screen)
    {
        UpdateBuildGhost(screen);
        if (_buildGhostKind is not { } kind || _buildCandidate is not { } cell) return;
        if (_buildCandidateIssue is { } issue)
        { _preparationMessage = issue; _buildClickRejected = true; RefreshPreparationHud(); return; }
        var command = _buildMovingId is null ? (SessionCommand)new PlaceBuildServiceCommand(kind, cell, _buildQuarterTurns) :
            new MoveBuildServiceCommand(_buildMovingId, cell, _buildQuarterTurns);
        // A new van opens the trader board on its tab, to choose who pitches there.
        var newVan = kind == BuildServiceKind.FoodVan && _buildMovingId is null ? Stalls.Next(kind, _session.CaptureBuildPlacements().Select(item => item.Id)) : null;
        var before = _session.CaptureSnapshot().AuthoritativeHash;
        CommitEquipmentAction(command);
        if (_session.CaptureSnapshot().AuthoritativeHash == before) return;
        CancelBuildPlacement(); SyncBuildWorld();
        _buildDrawerOpen = true;
        RefreshHudWorkspace(); Drawer.Refresh();
        if (newVan is not null) OpenTraderBoard(newVan);
    }
}

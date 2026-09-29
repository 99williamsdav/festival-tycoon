using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private PanelContainer? _buildDrawer;
    private PanelContainer? _buildBudgetFooter;
    private Label? _buildBudgetText;
    private Label? _buildChecklist;
    private Label? _buildFooterReason;
    private ScrollContainer? _buildCatalogueScroll;
    private VBoxContainer? _buildPlacedList;
    private Button? _buildStartButton;
    private Button? _buildToggleButton;
    private ConfirmationDialog? _buildDefaultsDialog;
    private readonly Dictionary<BuildServiceKind, (Label Count, Button Action, HBoxContainer Row)> _buildCatalogueRows = [];
    private readonly Dictionary<BuildServiceKind, List<Button>> _buildShortcutButtons = [];
    private bool _buildDrawerOpen;
    private Button? _buildDrawerClose;
    private Button? _buildSiteWaterButton;
    private string _buildPlacedKey = "";
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

    private static string BuildName(BuildServiceKind kind) => kind switch
    {
        BuildServiceKind.WaterTap => "Water tap",
        BuildServiceKind.Toilet => "Toilet",
        BuildServiceKind.FoodVan => "Food van",
        BuildServiceKind.Bar => "Bar",
        BuildServiceKind.FirstAid => "First aid",
        BuildServiceKind.StewardPost => "Steward post",
        _ => "Service"
    };

    private Node3D BuildAsset(BuildServiceKind kind) => kind switch
    {
        BuildServiceKind.WaterTap => InstantiateAsset("res://assets/environment/lwf_free_water_point_v4.glb"),
        BuildServiceKind.Toilet => InstantiateAsset(ToiletAsset),
        BuildServiceKind.FoodVan => InstantiateImmersionVendor(true),
        BuildServiceKind.Bar => InstantiateImmersionVendor(false),
        BuildServiceKind.FirstAid => InstantiateAsset(PostAsset(ResponseRole.Medic)),
        BuildServiceKind.StewardPost => InstantiateAsset(PostAsset(ResponseRole.Steward)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private Control BuildThumbnail(BuildServiceKind kind)
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(64, 46),
            TransparentBg = true,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once
        };
        var root = new SubViewportContainer { CustomMinimumSize = new Vector2(64, 46), Stretch = true,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(viewport);
        var model = BuildAsset(kind);
        viewport.AddChild(model);
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal,
            Size = kind switch { BuildServiceKind.WaterTap => 3, BuildServiceKind.Toilet => 3.5f,
                BuildServiceKind.FoodVan => 7, BuildServiceKind.Bar => 5, _ => 5 },
            Position = new Vector3(5, 5, 7), Current = true };
        viewport.AddChild(camera);
        camera.LookAtFromPosition(camera.Position, new Vector3(0, 1, 0));
        viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-45, -30, 0), LightEnergy = 2 });
        return root;
    }

    private void BuildBuildDrawer(CanvasLayer layer, Vector2 size)
    {
        if (!_session.BuildModeEnabled) return;
        var width = size.X >= 1600 ? 390f : 318f;
        _buildDrawer = HudPanel(layer, new Vector2(15, 77), new Vector2(width,
            Math.Min(size.X >= 1600 ? 735 : 530, size.Y - 212)));
        _buildDrawer.Visible = false;
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _buildCatalogueScroll = scroll;
        _buildDrawer.AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(box);
        var heading = new HBoxContainer(); box.AddChild(heading);
        var title = HudLabel("Build your festival", 23); title.AddThemeFontOverride("font", HearingSerif());
        heading.AddChild(title);
        var close = ButtonText("×", () => { _buildDrawerOpen = false; RefreshHudWorkspace(); });
        _buildDrawerClose = close;
        close.TooltipText = "Collapse Build drawer"; close.CustomMinimumSize = new Vector2(34, 34); heading.AddChild(close);
        box.AddChild(HudLabel("Select a service · scroll for all six.", 12));
        var defaults = ButtonText("Use defaults…", ShowBuildDefaults);
        defaults.TooltipText = "Restore standard service positions at normal draft cost; other preparation choices stay.";
        box.AddChild(defaults);
        _buildSiteWaterButton = ButtonText("Site & water ▸", () => SelectHudTab("Site & water"));
        _buildSiteWaterButton.TooltipText = "Open water sharing and additional tap choices; placement remains in Build.";
        box.AddChild(_buildSiteWaterButton);
        _buildChecklist = HudLabel("", 12); box.AddChild(_buildChecklist);
        foreach (var kind in Enum.GetValues<BuildServiceKind>())
        {
            box.AddChild(new HSeparator());
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 5); box.AddChild(row);
            row.AddChild(BuildThumbnail(kind));
            var words = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; row.AddChild(words);
            words.AddChild(HudLabel(BuildName(kind), 14));
            var count = HudLabel("", 11); words.AddChild(count);
            var button = ButtonText("+ Place", () => BeginBuildPlacement(kind));
            button.CustomMinimumSize = new Vector2(74, 36); row.AddChild(button);
            _buildCatalogueRows.Add(kind, (count, button, row));
        }
        box.AddChild(new HSeparator());
        box.AddChild(HudLabel("Placed services · select one to move or remove", 13));
        _buildPlacedList = new VBoxContainer(); box.AddChild(_buildPlacedList);

        _buildBudgetFooter = HudPanel(layer, new Vector2(15, size.Y - 108), new Vector2(size.X - 30, 53));
        _buildBudgetFooter.Visible = false;
        var footer = new HBoxContainer(); footer.AddThemeConstantOverride("separation", 12); _buildBudgetFooter.AddChild(footer);
        _buildBudgetText = HudLabel("", 12); footer.AddChild(_buildBudgetText);
        _buildFooterReason = HudLabel("", 11); footer.AddChild(_buildFooterReason);
        _buildStartButton = ButtonText("Start festival", ShowHudStartConfirmation);
        _buildStartButton.CustomMinimumSize = new Vector2(145, 40); footer.AddChild(_buildStartButton);

        _buildDefaultsDialog = new ConfirmationDialog { Title = "Replace service layout?", OkButtonText = "Replace layout",
            CancelButtonText = "Keep my layout", Theme = HudTheme() };
        _buildDefaultsDialog.GetLabel().AddThemeColorOverride("font_color", HudPaper);
        _buildDefaultsDialog.GetLabel().AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _buildDefaultsDialog.GetLabel().CustomMinimumSize = new Vector2(440, 84);
        _buildDefaultsDialog.Confirmed += ApplyBuildDefaults;
        layer.AddChild(_buildDefaultsDialog);
        RefreshBuildDrawer();
    }

    private void ToggleBuildDrawer()
    {
        if (!_session.BuildModeEnabled || _session.PreparedStatus != PreparationStatus.Preparing) return;
        if (_buildGhostKind is not null) CancelBuildPlacement();
        _buildDrawerOpen = !_buildDrawerOpen;
        if (_buildDrawerOpen) _hudWorkspaceOpen = false;
        RefreshHudWorkspace();
    }

    private void OpenBuildCatalogue(BuildServiceKind? focus = null)
    {
        if (!_session.BuildModeEnabled || _session.PreparedStatus != PreparationStatus.Preparing) return;
        _buildDrawerOpen = true; _hudWorkspaceOpen = false;
        RefreshHudWorkspace();
        if (focus is { } kind && _buildCatalogueRows.TryGetValue(kind, out var entry))
        {
            _buildCatalogueScroll?.EnsureControlVisible(entry.Row);
            if (!entry.Action.Disabled) entry.Action.GrabFocus();
        }
    }

    private void AddBuildChecklistShortcuts(VBoxContainer parent)
    {
        foreach (var (kind, caption) in new[]
        {
            (BuildServiceKind.WaterTap, "Water — open taps in Build"),
            (BuildServiceKind.Toilet, "Toilet — open toilets in Build"),
            (BuildServiceKind.FirstAid, "Safety — open first aid in Build"),
            (BuildServiceKind.StewardPost, "Safety — open steward posts in Build")
        })
        {
            var button = ButtonText(caption, () => OpenBuildCatalogue(kind));
            button.TooltipText = $"Open the Build catalogue at {BuildName(kind)}; placement remains your choice.";
            parent.AddChild(button);
            if (!_buildShortcutButtons.TryGetValue(kind, out var existing))
                _buildShortcutButtons[kind] = existing = [];
            existing.Add(button);
        }
    }

    private void RefreshBuildDrawer()
    {
        if (_buildDrawer is null || !_session.BuildModeEnabled || _session.CapturePreparationPlan() is not { } plan) return;
        var placements = _session.CaptureBuildPlacements();
        foreach (var (kind, pair) in _buildCatalogueRows)
        {
            var count = placements.Count(item => item.Kind == kind);
            var limit = GameSession.BuildServiceLimit(kind);
            pair.Count.Text = $"{FestivalCurrency.Format(GameSession.BuildServiceFeePennies(kind))} each · {count}/{limit} placed";
            pair.Action.Text = "+ Place";
            pair.Action.Disabled = count >= limit;
            pair.Row.Modulate = pair.Action.Disabled ? new Color(.63f, .63f, .63f) : Colors.White;
            pair.Action.TooltipText = $"{BuildName(kind)} costs {FestivalCurrency.Format(GameSession.BuildServiceFeePennies(kind))} at Start. {count} of {limit} placed. " +
                (pair.Action.Disabled ? "Select a placed one to move or remove." : "Place an unpaid draft service.");
        }
        foreach (var (kind, buttons) in _buildShortcutButtons)
        {
            var complete = placements.Any(item => item.Kind == kind);
            foreach (var button in buttons)
            {
                button.Visible = true;
                button.Text = (complete ? "✓ " : "! ") + (kind switch
                {
                    BuildServiceKind.WaterTap => "Water — open taps in Build",
                    BuildServiceKind.Toilet => "Toilet — open toilets in Build",
                    BuildServiceKind.FirstAid => "Safety — open first aid in Build",
                    BuildServiceKind.StewardPost => "Safety — open steward posts in Build",
                    _ => BuildName(kind)
                });
                button.TooltipText = complete
                    ? $"{BuildName(kind)} placed. Open its Build row to review or change it."
                    : $"{BuildName(kind)} is required before opening. Open its Build row to place one.";
            }
        }
        var key = string.Join("|", placements.Select(item => $"{item.Id}:{item.Cell.X}:{item.Cell.Z}:{item.QuarterTurns}"));
        if (key != _buildPlacedKey)
        {
            foreach (var child in _buildPlacedList!.GetChildren()) child.QueueFree();
            foreach (var item in placements)
            {
                var row = new HBoxContainer(); _buildPlacedList.AddChild(row);
                row.AddChild(HudLabel($"{BuildName(item.Kind)} {item.Id.Split('.').Last()}", 12));
                var move = ButtonText("Move", () => BeginBuildPlacement(item.Kind, item.Id));
                move.TooltipText = $"Move {BuildName(item.Kind)} without changing its draft cost."; row.AddChild(move);
                var remove = ButtonText("Remove", () => RemoveBuildPlacement(item.Id));
                remove.TooltipText = $"Remove {BuildName(item.Kind)} from the unpaid draft; deduct its fee from draft total."; row.AddChild(remove);
            }
            _buildPlacedKey = key;
        }
        var blockers = _session.GetPreparationStartBlockers();
        string Checklist(string title, bool ready, string action) => $"{(ready ? "Ready" : "Missing")} · {title}{(ready ? "" : " — " + action)}";
        _buildChecklist!.Text = "BEFORE OPENING\n" + string.Join("\n", new[]
        {
            Checklist("Water", placements.Any(item => item.Kind == BuildServiceKind.WaterTap), "place a tap"),
            Checklist("Toilet", placements.Any(item => item.Kind == BuildServiceKind.Toilet), "place a toilet"),
            Checklist("Safety coverage", placements.Any(item => item.Kind == BuildServiceKind.FirstAid) && placements.Any(item => item.Kind == BuildServiceKind.StewardPost), "place first aid and steward post"),
            Checklist("Programme", !blockers.Any(item => item.Owner == PreparationStartOwner.Programme), "choose three acts"),
            Checklist("Sound staff", !blockers.Any(item => item.Owner == PreparationStartOwner.Staff), "hire a worker")
        }) + "\nFood and bar are optional.";
        var offers = _session.GetPreparationOffers();
        var acts = plan.ActIds.Where(id => id != "").Sum(id => offers.Single(offer => offer.Id == id).PricePennies);
        var staff = plan.OfferIds.Where(id => offers.Single(offer => offer.Id == id).Category is "staff" or "maintenance" or "extra-medic" or "extra-steward")
            .Sum(id => offers.Single(offer => offer.Id == id).PricePennies);
        var equipment = plan.OfferIds.Where(id => offers.Single(offer => offer.Id == id).Category == "equipment")
            .Sum(id => offers.Single(offer => offer.Id == id).PricePennies);
        var stock = plan.Chips * 100 + plan.SoftDrinks * 60 + plan.Beers * 100;
        var funds = _session.CaptureSnapshot().FestivalFinances.Single().CashPennies;
        _buildBudgetText!.Text = $"Budget {FestivalCurrency.Format(funds)}  ·  Draft {FestivalCurrency.Format(_session.PreparationPlanCost)}  ·  Remaining {FestivalCurrency.Format(_session.PreparationRemainingCash)}\n" +
            $"Services {FestivalCurrency.Format(_session.BuildDraftCost)} · Acts {FestivalCurrency.Format(acts)} · Staff {FestivalCurrency.Format(staff)} · Equipment {FestivalCurrency.Format(equipment)} · Stock {FestivalCurrency.Format(stock)}";
        var issue = _buildGhostKind is not null ? "Finish or cancel placement" : blockers.FirstOrDefault()?.Message;
        _buildFooterReason!.Text = issue ?? "Ready to open";
        _buildStartButton!.TooltipText = issue ?? "Review the complete charge, then start the festival.";
        if (_buildToggleButton is not null) _buildToggleButton.Text = _buildDrawerOpen ? "Build ×" : "Build";
    }

    private string BuildStartCostSummary()
    {
        var plan = _session.CapturePreparationPlan()!;
        var offers = _session.GetPreparationOffers();
        var acts = plan.ActIds.Where(id => id != "").Sum(id => offers.Single(offer => offer.Id == id).PricePennies);
        var staff = plan.OfferIds.Where(id => offers.Single(offer => offer.Id == id).Category is "staff" or "maintenance" or "extra-medic" or "extra-steward")
            .Sum(id => offers.Single(offer => offer.Id == id).PricePennies);
        var equipment = plan.OfferIds.Where(id => offers.Single(offer => offer.Id == id).Category == "equipment")
            .Sum(id => offers.Single(offer => offer.Id == id).PricePennies);
        var stock = plan.Chips * 100 + plan.SoftDrinks * 60 + plan.Beers * 100;
        return $"Services {FestivalCurrency.Format(_session.BuildDraftCost)} · Acts {FestivalCurrency.Format(acts)} · Staff {FestivalCurrency.Format(staff)}\n" +
            $"Equipment {FestivalCurrency.Format(equipment)} · Stock {FestivalCurrency.Format(stock)}\n" +
            $"Complete setup {FestivalCurrency.Format(_session.PreparationPlanCost)} · Remaining {FestivalCurrency.Format(_session.PreparationRemainingCash)}";
    }

    private void ShowBuildDefaults()
    {
        var old = _session.CaptureBuildPlacements();
        var defaults = GameSession.StandardBuildLayout();
        var oldCost = _session.BuildDraftCost;
        var newCost = defaults.Sum(item => GameSession.BuildServiceFeePennies(item.Kind));
        _buildDefaultsDialog!.DialogText = $"Replace {old.Count} placed services ({FestivalCurrency.Format(oldCost)}) with {defaults.Length} standard services ({FestivalCurrency.Format(newCost)})?\n" +
            $"Service draft change: {FestivalCurrency.Format(newCost - oldCost)}. New complete draft: {FestivalCurrency.Format(_session.PreparationPlanCost - oldCost + newCost)}.\n" +
            "Bookings, staff, equipment, stock and perks stay.";
        _buildDefaultsDialog.PopupCentered(new Vector2I(510, 220));
    }

    private void ApplyBuildDefaults()
    {
        CommitEquipmentAction(new UseDefaultBuildLayoutCommand());
        SyncBuildWorld(); RefreshBuildDrawer();
    }

    private void RemoveBuildPlacement(string id)
    {
        CommitEquipmentAction(new RemoveBuildServiceCommand(id));
        SyncBuildWorld(); RefreshBuildDrawer();
    }

    private void SyncBuildWorld()
    {
        SyncExtraWaterWorld(); SyncResponsePosts(); SyncImmersionWorld(); SyncToiletWorld();
    }

    private void BeginBuildPlacement(BuildServiceKind kind, string? movingId = null)
    {
        if (!_session.BuildModeEnabled || _session.PreparedStatus != PreparationStatus.Preparing) return;
        CancelWaterPlacement(); CancelImmersionPlacement(); CancelToiletPlacement(); CancelResponsePostPlacement(); CancelBuildPlacement();
        _buildGhostKind = kind; _buildMovingId = movingId;
        _buildClickRejected = false;
        _buildQuarterTurns = movingId is null ? kind == BuildServiceKind.Toilet ? 2 : 0 :
            _session.CaptureBuildPlacements().Single(item => item.Id == movingId).QuarterTurns;
        _buildGhost = BuildAsset(kind); AddChild(_buildGhost);
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
        RefreshBuildDrawer();
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
        var ray = _camera.ProjectRayNormal(screen); var origin = _camera.ProjectRayOrigin(screen);
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
        var before = _session.CaptureSnapshot().AuthoritativeHash;
        CommitEquipmentAction(command);
        if (_session.CaptureSnapshot().AuthoritativeHash == before) return;
        CancelBuildPlacement(); SyncBuildWorld();
        _buildDrawerOpen = true;
        RefreshHudWorkspace(); RefreshBuildDrawer();
    }
}

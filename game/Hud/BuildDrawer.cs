using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>What the Build drawer asks the game to do: placement, removal, defaults and navigation.</summary>
internal interface IBuildActions
{
    void BeginPlacement(BuildServiceKind kind, string? movingId);
    void RemovePlacement(string id);
    void ApplyDefaults();
    void CloseDrawer();
    void OpenCatalogue(BuildServiceKind kind);
    void OpenTab(string name);
    /// <summary>The service's model, for catalogue thumbnails.</summary>
    Node3D BuildAsset(BuildServiceKind kind);
}

/// <summary>
/// The Build drawer: one catalogue row per service (fee, count, Place), the placed-services list with
/// Move and Remove, the Before opening checklist, and Use defaults with a replace confirmation.
/// </summary>
internal sealed class BuildDrawer(IHudHost _hud, IBuildActions _actions)
{
    private PanelContainer? _buildDrawer;
    private Label? _buildChecklist;
    private ScrollContainer? _buildCatalogueScroll;
    private VBoxContainer? _buildPlacedList;
    private ConfirmationDialog? _buildDefaultsDialog;
    private readonly Dictionary<BuildServiceKind, (Label Count, Button Action, HBoxContainer Row)> _buildCatalogueRows = [];
    private readonly Dictionary<BuildServiceKind, List<Button>> _buildShortcutButtons = [];
    private Button? _buildSiteWaterButton;
    private string _buildPlacedKey = "";

    public PanelContainer? Panel => _buildDrawer;

    /// <summary>Scrolls the catalogue to a service's row and focuses its Place button.</summary>
    public void FocusRow(BuildServiceKind kind)
    {
        if (!_buildCatalogueRows.TryGetValue(kind, out var entry)) return;
        _buildCatalogueScroll?.EnsureControlVisible(entry.Row);
        if (!entry.Action.Disabled) entry.Action.GrabFocus();
    }

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
        var model = _actions.BuildAsset(kind);
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
    public void Build(CanvasLayer layer, Vector2 size)
    {
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
        var title = HudLabel("Build your festival", 23); title.AddThemeFontOverride("font", Ui.SlabBold);
        heading.AddChild(title);
        var close = ButtonText("×", _actions.CloseDrawer);
        close.TooltipText = "Collapse Build drawer"; close.CustomMinimumSize = new Vector2(34, 34); heading.AddChild(close);
        box.AddChild(HudLabel("Select a service · scroll for all six.", 12));
        var defaults = ButtonText("Use defaults…", ShowDefaults);
        defaults.TooltipText = "Restore standard service positions at normal draft cost; other preparation choices stay.";
        box.AddChild(defaults);
        _buildSiteWaterButton = ButtonText("Site & water ▸", () => _actions.OpenTab("Site & water"));
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
            var button = ButtonText("+ Place", () => _actions.BeginPlacement(kind, null));
            button.CustomMinimumSize = new Vector2(74, 36); row.AddChild(button);
            _buildCatalogueRows.Add(kind, (count, button, row));
        }
        box.AddChild(new HSeparator());
        box.AddChild(HudLabel("Placed services · select one to move or remove", 13));
        _buildPlacedList = new VBoxContainer(); box.AddChild(_buildPlacedList);


        _buildDefaultsDialog = new ConfirmationDialog { Title = "Replace service layout?", OkButtonText = "Replace layout",
            CancelButtonText = "Keep my layout", Theme = HudTheme() };
        _buildDefaultsDialog.GetLabel().AddThemeColorOverride("font_color", HudPaper);
        _buildDefaultsDialog.GetLabel().AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _buildDefaultsDialog.GetLabel().CustomMinimumSize = new Vector2(440, 84);
        _buildDefaultsDialog.Confirmed += _actions.ApplyDefaults;
        layer.AddChild(_buildDefaultsDialog);
        Refresh();
    }
    public void AddChecklistShortcuts(VBoxContainer parent)
    {
        foreach (var (kind, caption) in new[]
        {
            (BuildServiceKind.WaterTap, "Water — open taps in Build"),
            (BuildServiceKind.Toilet, "Toilet — open toilets in Build"),
            (BuildServiceKind.FirstAid, "Safety — open first aid in Build"),
            (BuildServiceKind.StewardPost, "Safety — open steward posts in Build")
        })
        {
            var button = ButtonText(caption, () => _actions.OpenCatalogue(kind));
            button.TooltipText = $"Open the Build catalogue at {BuildName(kind)}; placement remains your choice.";
            parent.AddChild(button);
            if (!_buildShortcutButtons.TryGetValue(kind, out var existing))
                _buildShortcutButtons[kind] = existing = [];
            existing.Add(button);
        }
    }
    public void Refresh()
    {
        if (_buildDrawer is null || _hud.Session.CapturePreparationPlan() is not { } plan) return;
        var placements = _hud.Session.CaptureBuildPlacements();
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
                var move = ButtonText("Move", () => _actions.BeginPlacement(item.Kind, item.Id));
                move.TooltipText = $"Move {BuildName(item.Kind)} without changing its draft cost."; row.AddChild(move);
                var remove = ButtonText("Remove", () => _actions.RemovePlacement(item.Id));
                remove.TooltipText = $"Remove {BuildName(item.Kind)} from the unpaid draft; deduct its fee from draft total."; row.AddChild(remove);
            }
            _buildPlacedKey = key;
        }
        var blockers = _hud.Session.GetPreparationStartBlockers();
        string Checklist(string title, bool ready, string action) => $"{(ready ? "Ready" : "Missing")} · {title}{(ready ? "" : " — " + action)}";
        _buildChecklist!.Text = "BEFORE OPENING\n" + string.Join("\n", new[]
        {
            Checklist("Water", placements.Any(item => item.Kind == BuildServiceKind.WaterTap), "place a tap"),
            Checklist("Toilet", placements.Any(item => item.Kind == BuildServiceKind.Toilet), "place a toilet"),
            Checklist("Safety coverage", placements.Any(item => item.Kind == BuildServiceKind.FirstAid) && placements.Any(item => item.Kind == BuildServiceKind.StewardPost), "place first aid and steward post"),
            Checklist("Programme", !blockers.Any(item => item.Owner == PreparationStartOwner.Programme), "choose three acts"),
            Checklist("Sound staff", !blockers.Any(item => item.Owner == PreparationStartOwner.Staff), "hire a worker")
        }) + "\nFood and bar are optional.";
    }
    public string CostSummary()
    {
        var plan = _hud.Session.CapturePreparationPlan()!;
        var offers = _hud.Session.GetPreparationOffers();
        var acts = plan.ActIds.Where(id => id != "").Sum(id => offers.Single(offer => offer.Id == id).PricePennies);
        var staff = plan.OfferIds.Where(id => offers.Single(offer => offer.Id == id).Category is "staff" or "maintenance" or "extra-medic" or "extra-steward")
            .Sum(id => offers.Single(offer => offer.Id == id).PricePennies);
        var equipment = plan.OfferIds.Where(id => offers.Single(offer => offer.Id == id).Category == "equipment")
            .Sum(id => offers.Single(offer => offer.Id == id).PricePennies);
        var stock = plan.Chips * 100 + plan.SoftDrinks * 60 + plan.Beers * 100;
        return $"Services {FestivalCurrency.Format(_hud.Session.BuildDraftCost)} · Acts {FestivalCurrency.Format(acts)} · Staff {FestivalCurrency.Format(staff)}\n" +
            $"Equipment {FestivalCurrency.Format(equipment)} · Stock {FestivalCurrency.Format(stock)}\n" +
            $"Complete setup {FestivalCurrency.Format(_hud.Session.PreparationPlanCost)} · Remaining {FestivalCurrency.Format(_hud.Session.PreparationRemainingCash)}";
    }
    private void ShowDefaults()
    {
        var old = _hud.Session.CaptureBuildPlacements();
        var defaults = GameSession.StandardBuildLayout();
        var oldCost = _hud.Session.BuildDraftCost;
        var newCost = defaults.Sum(item => GameSession.BuildServiceFeePennies(item.Kind));
        _buildDefaultsDialog!.DialogText = $"Replace {old.Count} placed services ({FestivalCurrency.Format(oldCost)}) with {defaults.Length} standard services ({FestivalCurrency.Format(newCost)})?\n" +
            $"Service draft change: {FestivalCurrency.Format(newCost - oldCost)}. New complete draft: {FestivalCurrency.Format(_hud.Session.PreparationPlanCost - oldCost + newCost)}.\n" +
            "Bookings, staff, equipment, stock and perks stay.";
        _buildDefaultsDialog.PopupCentered(new Vector2I(510, 220));
    }
}

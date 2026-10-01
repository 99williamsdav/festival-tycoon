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
}

/// <summary>
/// The Build sheet: one row per service (tile, Required/Optional, fee, placed dots, Place), the placed
/// services with Move and Remove, and Use default layout with a replace confirmation.
/// </summary>
internal sealed class BuildDrawer(IHudHost _hud, IBuildActions _actions)
{
    private sealed record CatalogueRow(PanelContainer Row, Label Meta, HBoxContainer Dots, Label Count, Button Action);

    private PanelContainer? _buildDrawer;
    private ScrollContainer? _buildCatalogueScroll;
    private Label? _servicesTotal;
    private Button? _defaultsButton;
    private VBoxContainer? _buildPlacedList;
    private Label? _placedHeading;
    private ConfirmationDialog? _buildDefaultsDialog;
    private readonly Dictionary<BuildServiceKind, CatalogueRow> _buildCatalogueRows = [];
    private readonly Dictionary<BuildServiceKind, List<Button>> _buildShortcutButtons = [];
    private string _buildPlacedKey = "";

    public PanelContainer? Panel => _buildDrawer;

    /// <summary>Scrolls the catalogue to a service's row and focuses its Place button.</summary>
    public void FocusRow(BuildServiceKind kind)
    {
        if (!_buildCatalogueRows.TryGetValue(kind, out var entry)) return;
        _buildCatalogueScroll?.EnsureControlVisible(entry.Row);
        if (!entry.Action.Disabled) entry.Action.GrabFocus();
    }

    private static (string Icon, Color Tile, bool Required) Look(BuildServiceKind kind) => kind switch
    {
        BuildServiceKind.WaterTap => ("droplet", new Color("3c7fa6"), true),
        BuildServiceKind.Toilet => ("door-closed", new Color("4f7382"), true),
        BuildServiceKind.FirstAid => ("square-plus", new Color("2f8a5f"), true),
        BuildServiceKind.StewardPost => ("shield", new Color("3e5a8c"), true),
        BuildServiceKind.FoodVan => ("truck", new Color("b85c28"), false),
        BuildServiceKind.Bar => ("beer", new Color("a87a1f"), false),
        _ => ("package", Ui.InkMuted, false),
    };

    public void Build(CanvasLayer layer, Vector2 size)
    {
        _buildDrawer = new PanelContainer { Position = new Vector2(Ui.Gutter, Ui.ContentTop), Theme = HudTheme(),
            Size = new Vector2(Ui.S(364), Math.Min(Ui.S(522), size.Y - Ui.ContentTop - Ui.Dock - Ui.S(12))) };
        _buildDrawer.AddThemeStyleboxOverride("panel", Ui.Sheet(20, 0));
        Ui.Clipboard(_buildDrawer);
        layer.AddChild(_buildDrawer);
        _buildDrawer.Visible = false;
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _buildCatalogueScroll = scroll;
        _buildDrawer.AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 0);
        var gutter = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        gutter.AddThemeConstantOverride("margin_right", Ui.Px(10));
        gutter.AddChild(box); scroll.AddChild(gutter);
        Ui.SlimScrollbar(scroll);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(24)) });
        var heading = new HBoxContainer(); box.AddChild(heading);
        var words = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; words.AddThemeConstantOverride("separation", Ui.Px(6)); heading.AddChild(words);
        words.AddChild(Ui.Heading("Build your site", 27));
        var lead = Ui.Text("Place services on the farm. Nothing is paid until Start.", 13.5f, Ui.InkMuted);
        lead.AutowrapMode = TextServer.AutowrapMode.WordSmart; words.AddChild(lead);
        var close = new Button { Icon = Ui.Icon("x"), ExpandIcon = true, IconAlignment = HorizontalAlignment.Center, TooltipText = "Close Build",
            CustomMinimumSize = Ui.S(32, 32), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        Ui.Style(close, Ui.ButtonKind.Quiet); close.Pressed += _actions.CloseDrawer; heading.AddChild(close);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(14)) });
        var actions = new HBoxContainer(); actions.AddThemeConstantOverride("separation", Ui.Px(8)); box.AddChild(actions);
        _defaultsButton = Ui.Style(new Button { CustomMinimumSize = new Vector2(0, Ui.S(36)), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Restore standard service positions at normal draft cost; other preparation choices stay.",
            MouseDefaultCursorShape = Control.CursorShape.PointingHand }, Ui.ButtonKind.Secondary, 13.5f);
        _defaultsButton.Pressed += ShowDefaults; actions.AddChild(_defaultsButton);
        var water = Ui.Style(new Button { Text = "Site & water", CustomMinimumSize = new Vector2(0, Ui.S(36)),
            TooltipText = "Water sharing and additional tap choices; placement stays in Build.", MouseDefaultCursorShape = Control.CursorShape.PointingHand },
            Ui.ButtonKind.Quiet, 13.5f);
        water.Pressed += () => _actions.OpenTab("Site & water"); actions.AddChild(water);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(16)) });
        var caption = new HBoxContainer(); box.AddChild(caption);
        var services = Ui.Caps("Services", Ui.InkMuted); services.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; caption.AddChild(services);
        _servicesTotal = Ui.Caps("", Ui.InkMuted); caption.AddChild(_servicesTotal);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(4)) });
        foreach (var kind in Enum.GetValues<BuildServiceKind>().OrderBy(kind => Look(kind).Required ? 0 : 1))
            box.AddChild(CatalogueEntry(kind));
        box.AddChild(Rule());
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(14)) });
        _placedHeading = Ui.Caps("On the field · move or remove", Ui.InkMuted); box.AddChild(_placedHeading);
        _buildPlacedList = new VBoxContainer(); _buildPlacedList.AddThemeConstantOverride("separation", Ui.Px(2)); box.AddChild(_buildPlacedList);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(16)) });

        _buildDefaultsDialog = new ConfirmationDialog { Title = "Replace service layout?", OkButtonText = "Replace layout",
            CancelButtonText = "Keep my layout", Theme = HudTheme() };
        _buildDefaultsDialog.GetLabel().AddThemeColorOverride("font_color", HudPaper);
        _buildDefaultsDialog.GetLabel().AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _buildDefaultsDialog.GetLabel().CustomMinimumSize = new Vector2(440, 84);
        _buildDefaultsDialog.Confirmed += _actions.ApplyDefaults;
        layer.AddChild(_buildDefaultsDialog);
        Refresh();
    }

    private static ColorRect Rule() => new() { Color = Ui.PaperRule, CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore };

    private PanelContainer CatalogueEntry(BuildServiceKind kind)
    {
        var (icon, tile, required) = Look(kind);
        var row = new PanelContainer();
        var lined = Ui.Box(new Color(0, 0, 0, 0), 0, padY: 8);
        lined.BorderColor = Ui.PaperRule; lined.BorderWidthTop = 1;
        row.AddThemeStyleboxOverride("panel", lined);
        var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", Ui.Px(12)); row.AddChild(line);
        var badge = new PanelContainer { CustomMinimumSize = Ui.S(42, 42), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        badge.AddThemeStyleboxOverride("panel", Ui.Box(tile, 8));
        var glyph = Ui.IconRect(icon, 22, Colors.White); glyph.SizeFlagsHorizontal = glyph.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        badge.AddChild(glyph); line.AddChild(badge);
        var words = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        words.AddThemeConstantOverride("separation", Ui.Px(3)); line.AddChild(words);
        var title = new HBoxContainer(); title.AddThemeConstantOverride("separation", Ui.Px(8)); words.AddChild(title);
        title.AddChild(Ui.Text(BuildName(kind), 15.5f, Ui.Ink, Ui.BodyBold));
        var tag = Ui.Caps(required ? "Required" : "Optional", required ? Ui.Teal : Ui.InkMuted, 9.5f);
        var tagBox = Ui.Box(new Color(0, 0, 0, 0), 4, required ? Ui.Teal : new Color("c9b994"), 1, 5, 1);
        tag.AddThemeStyleboxOverride("normal", tagBox); tag.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; title.AddChild(tag);
        var meta = new HBoxContainer(); meta.AddThemeConstantOverride("separation", Ui.Px(8)); words.AddChild(meta);
        var price = Ui.Text("", 13, Ui.InkMuted); meta.AddChild(price);
        var dots = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; dots.AddThemeConstantOverride("separation", Ui.Px(3)); meta.AddChild(dots);
        var count = Ui.Text("", 13, Ui.InkMuted); meta.AddChild(count);
        var action = Ui.IconButton("Place", "plus", Ui.ButtonKind.Accent, () => _actions.BeginPlacement(kind, null), 13.5f);
        action.CustomMinimumSize = new Vector2(0, Ui.S(34)); action.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        action.AddThemeConstantOverride("icon_max_width", Ui.Px(15));
        line.AddChild(action);
        _buildCatalogueRows.Add(kind, new CatalogueRow(row, price, dots, count, action));
        return row;
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
        _servicesTotal!.Text = $"{placements.Count} placed · {FestivalCurrency.Format(_hud.Session.BuildDraftCost)}".ToUpperInvariant();
        var standard = GameSession.StandardBuildLayout().Sum(item => GameSession.BuildServiceFeePennies(item.Kind));
        _defaultsButton!.Text = $"Use default layout · {FestivalCurrency.Format(standard)}";
        foreach (var (kind, row) in _buildCatalogueRows)
        {
            var count = placements.Count(item => item.Kind == kind);
            var limit = GameSession.BuildServiceLimit(kind);
            var fee = FestivalCurrency.Format(GameSession.BuildServiceFeePennies(kind));
            row.Meta.Text = limit > 1 ? $"{fee} each" : fee;
            row.Count.Text = kind == BuildServiceKind.Bin ? $"{count} placed" : $"{count} of {limit}";
            var dotCount = kind == BuildServiceKind.Bin ? 0 : limit;
            if (row.Dots.GetChildCount() != dotCount)
            {
                foreach (var child in row.Dots.GetChildren()) { row.Dots.RemoveChild(child); child.QueueFree(); }
                for (var i = 0; i < dotCount; i++) row.Dots.AddChild(new Panel { CustomMinimumSize = Ui.S(9, 9), MouseFilter = Control.MouseFilterEnum.Ignore });
            }
            for (var i = 0; i < dotCount; i++)
                row.Dots.GetChild<Panel>(i).AddThemeStyleboxOverride("panel", i < count ? Ui.Box(Ui.Teal, 5) : Ui.Box(new Color(0, 0, 0, 0), 5, new Color("9db5ae"), 1.5f));
            var full = count >= limit;
            row.Action.Disabled = full;
            row.Action.Text = full ? "Placed" : "Place";
            row.Action.Icon = Ui.Icon(full ? "check" : "plus");
            row.Action.TooltipText = $"{BuildName(kind)} costs {fee} at Start. {count} placed. " +
                (full ? "Move or remove a placed one below." : "Place an unpaid draft service.");
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
        if (key == _buildPlacedKey) return;
        _buildPlacedKey = key;
        _placedHeading!.Visible = placements.Count > 0;
        foreach (var child in _buildPlacedList!.GetChildren()) child.QueueFree();
        foreach (var item in placements)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Ui.Px(6)); _buildPlacedList.AddChild(row);
            var name = Ui.Text($"{BuildName(item.Kind)} {item.Id.Split('.').Last()}", 13.5f, Ui.Ink); name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            name.VerticalAlignment = VerticalAlignment.Center; row.AddChild(name);
            var move = Ui.Style(new Button { Text = "Move", CustomMinimumSize = new Vector2(0, Ui.S(28)), MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                TooltipText = $"Move {BuildName(item.Kind)} without changing its draft cost." }, Ui.ButtonKind.Quiet, 12.5f);
            move.Pressed += () => _actions.BeginPlacement(item.Kind, item.Id); row.AddChild(move);
            var remove = Ui.Style(new Button { Text = "Remove", CustomMinimumSize = new Vector2(0, Ui.S(28)), MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                TooltipText = $"Remove {BuildName(item.Kind)} from the unpaid draft; deduct its fee from draft total." }, Ui.ButtonKind.Quiet, 12.5f);
            remove.Pressed += () => _actions.RemovePlacement(item.Id); row.AddChild(remove);
        }
    }
    public string CostSummary()
    {
        var costs = PlanCosts.Of(_hud.Session);
        return $"Services {FestivalCurrency.Format(costs.Services)} · Acts {FestivalCurrency.Format(costs.Acts)} · Staff {FestivalCurrency.Format(costs.Staff)}\n" +
            $"Equipment {FestivalCurrency.Format(costs.Equipment)} · Stock {FestivalCurrency.Format(costs.Stock)}\n" +
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

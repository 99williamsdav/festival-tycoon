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
    void ApplyLastLayout();
    void CloseDrawer();
    void OpenCatalogue(BuildServiceKind kind);
    void OpenTab(string name);
    void BeginCover(GroundCoverAction action);
}

/// <summary>
/// The Build sheet, kept compact: services grouped into Essentials, Food and drink, and Site tabs, one slim row each
/// (tile, name, Req chip, fee, placed dots, count, and a small Place button). A row opens to its placed services with
/// Move and Remove, and Use default layout asks before replacing. A tab shows ! while a required service in it is
/// missing and a tick once they're all placed. Placed services can also be moved or removed by right-clicking them
/// on the farm.
/// </summary>
internal sealed class BuildDrawer(IHudHost _hud, IBuildActions _actions)
{
    private sealed record CatalogueRow(Button Chevron, Label Price, HBoxContainer Dots, Label Count, Button Action, VBoxContainer Placed);

    private static readonly (string Name, BuildServiceKind[] Kinds)[] Categories =
    [
        ("Essentials", [BuildServiceKind.WaterTap, BuildServiceKind.Toilet, BuildServiceKind.FirstAid, BuildServiceKind.StewardPost]),
        ("Food & drink", [BuildServiceKind.FoodVan, BuildServiceKind.Bar]),
        ("Site", [BuildServiceKind.Bin, BuildServiceKind.Marquee]),
    ];

    private PanelContainer? _buildDrawer;
    private ScrollContainer? _scroll;
    private float _maxHeight;
    private Label? _servicesTotal;
    private Button? _defaultsButton;
    private Button? _lastLayoutButton;
    private bool _dialogCopiesLast;
    private bool? _defaultsQuiet;
    private ConfirmationDialog? _buildDefaultsDialog;
    private readonly List<Button> _tabs = [];
    private readonly List<VBoxContainer> _tabPages = [];
    private readonly HashSet<BuildServiceKind> _expanded = [];
    private readonly Dictionary<BuildServiceKind, CatalogueRow> _buildCatalogueRows = [];
    private readonly Dictionary<BuildServiceKind, List<Button>> _buildShortcutButtons = [];
    private string _buildPlacedKey = "";

    public PanelContainer? Panel => _buildDrawer;

    /// <summary>Opens the tab holding a service and focuses its Place button.</summary>
    public void FocusRow(BuildServiceKind kind)
    {
        var tab = Array.FindIndex(Categories, category => category.Kinds.Contains(kind));
        if (tab >= 0) SelectTab(tab);
        if (_buildCatalogueRows.TryGetValue(kind, out var entry) && !entry.Action.Disabled) entry.Action.GrabFocus();
    }

    private static (string Icon, Color Tile, bool Required) Look(BuildServiceKind kind) => kind switch
    {
        BuildServiceKind.WaterTap => ("droplet", new Color("3c7fa6"), true),
        BuildServiceKind.Toilet => ("door-closed", new Color("4f7382"), true),
        BuildServiceKind.FirstAid => ("square-plus", new Color("2f8a5f"), true),
        BuildServiceKind.StewardPost => ("shield", new Color("3e5a8c"), true),
        BuildServiceKind.FoodVan => ("truck", new Color("b85c28"), false),
        BuildServiceKind.Bar => ("beer", new Color("a87a1f"), false),
        BuildServiceKind.Marquee => ("sun", new Color("1f7a78"), false),
        _ => ("package", Ui.InkMuted, false),
    };

    public void Build(CanvasLayer layer, Vector2 size)
    {
        // Sized by its contents: no scrolling, and only as tall as the open tab needs.
        _buildDrawer = new PanelContainer { Position = new Vector2(Ui.Gutter, Ui.ContentTop), Theme = HudTheme(),
            Size = new Vector2(Ui.S(330), 0) };
        _buildDrawer.AddThemeStyleboxOverride("panel", Ui.Sheet(16, 0));
        Ui.Clipboard(_buildDrawer, 112);
        layer.AddChild(_buildDrawer);
        _buildDrawer.Visible = false;
        // Scrolls only if what's open won't fit on screen; otherwise the sheet is just as tall as its contents.
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        _maxHeight = size.Y - Ui.ContentTop - Ui.Dock - Ui.S(12);
        _buildDrawer.AddChild(_scroll);
        Ui.SlimScrollbar(_scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 0);
        _scroll.AddChild(box);
        box.Resized += Shrink;
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(18)) });
        var heading = new HBoxContainer(); box.AddChild(heading);
        var words = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        words.AddThemeConstantOverride("separation", Ui.Px(2)); heading.AddChild(words);
        words.AddChild(Ui.Heading("Build your site", 22));
        _servicesTotal = Ui.Text("", 12, Ui.InkMuted); words.AddChild(_servicesTotal);
        var close = new Button { Icon = Ui.Icon("x"), ExpandIcon = true, IconAlignment = HorizontalAlignment.Center, TooltipText = "Close Build",
            CustomMinimumSize = Ui.S(26, 26), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        Tight(Ui.Style(close, Ui.ButtonKind.Quiet), 12); close.Pressed += _actions.CloseDrawer; heading.AddChild(close);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(10)) });

        var tabs = new HBoxContainer(); tabs.AddThemeConstantOverride("separation", Ui.Px(2)); box.AddChild(tabs);
        var pages = new PanelContainer();
        pages.AddThemeStyleboxOverride("panel", Ui.Box(Ui.PaperBright, 8, Ui.PaperRule, 1, 8, 2));
        box.AddChild(pages);
        var pageStack = new VBoxContainer(); pages.AddChild(pageStack);
        for (var index = 0; index < Categories.Length; index++)
        {
            var (name, kinds) = Categories[index];
            var tab = new Button { Text = name, ToggleMode = true, MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var chosen = index;
            tab.Pressed += () => SelectTab(chosen);
            tabs.AddChild(tab); _tabs.Add(tab);
            var page = new VBoxContainer(); page.AddThemeConstantOverride("separation", 0); pageStack.AddChild(page); _tabPages.Add(page);
            foreach (var kind in kinds) page.AddChild(CatalogueEntry(kind, kind == kinds[0]));
            if (name == "Site") page.AddChild(GroundCoverEntry());
        }
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(10)) });
        // From Tier 2 the main action copies last festival's layout; the default layout stays as a smaller link under it.
        var actions = new VBoxContainer(); actions.AddThemeConstantOverride("separation", Ui.Px(4)); box.AddChild(actions);
        _lastLayoutButton = Ui.Style(new Button { CustomMinimumSize = new Vector2(0, Ui.S(30)), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Text = "Copy last festival's layout", TooltipText = "Put your services back where they stood at the last festival, wherever they still fit.",
            MouseDefaultCursorShape = Control.CursorShape.PointingHand, Visible = false }, Ui.ButtonKind.Secondary, 12.5f);
        _lastLayoutButton.Pressed += ShowLastLayout; actions.AddChild(_lastLayoutButton);
        _defaultsButton = new Button { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Put back the standard services.",
            MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        _defaultsButton.Pressed += ShowDefaults; actions.AddChild(_defaultsButton);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(14)) });

        _buildDefaultsDialog = new ConfirmationDialog { Title = "Replace service layout?", OkButtonText = "Replace layout",
            CancelButtonText = "Keep my layout", Theme = HudTheme() };
        _buildDefaultsDialog.GetLabel().AddThemeColorOverride("font_color", HudPaper);
        _buildDefaultsDialog.GetLabel().AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _buildDefaultsDialog.GetLabel().CustomMinimumSize = new Vector2(440, 84);
        _buildDefaultsDialog.Confirmed += () => { if (_dialogCopiesLast) _actions.ApplyLastLayout(); else _actions.ApplyDefaults(); };
        layer.AddChild(_buildDefaultsDialog);
        SelectTab(0);
        Refresh();
    }

    private void SelectTab(int index)
    {
        for (var i = 0; i < _tabs.Count; i++)
        {
            _tabPages[i].Visible = i == index;
            _tabs[i].SetPressedNoSignal(i == index);
            Ui.Style(_tabs[i], i == index ? Ui.ButtonKind.Secondary : Ui.ButtonKind.Quiet, 12.5f);
            _tabs[i].CustomMinimumSize = new Vector2(0, Ui.S(28));
        }
        Shrink();
    }

    /// <summary>The sheet fits whatever is open, up to the room on screen, scrolling beyond that.</summary>
    private void Shrink()
    {
        if (_buildDrawer is null || _scroll?.GetChild(0) is not Control content) return;
        var wanted = content.GetCombinedMinimumSize().Y;
        _scroll.CustomMinimumSize = new Vector2(0, Mathf.Min(wanted, _maxHeight));
        _buildDrawer.Size = new Vector2(_buildDrawer.Size.X, 0);
    }

    private static ColorRect Rule() => new() { Color = Ui.PaperRule, CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore };

    private VBoxContainer CatalogueEntry(BuildServiceKind kind, bool first)
    {
        var (icon, tile, required) = Look(kind);
        var block = new VBoxContainer(); block.AddThemeConstantOverride("separation", 0);
        if (!first) block.AddChild(Rule());
        var line = new HBoxContainer { CustomMinimumSize = new Vector2(0, Ui.S(34)) };
        line.AddThemeConstantOverride("separation", Ui.Px(6)); block.AddChild(line);
        var chevron = new Button { Icon = Ui.Icon("chevron-right"), ExpandIcon = true, Flat = true, CustomMinimumSize = Ui.S(16, 16),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = $"Show placed {BuildName(kind).ToLowerInvariant()}s to move or remove" };
        Tight(chevron, 12, flat: true);
        chevron.AddThemeColorOverride("icon_normal_color", Ui.InkMuted);
        chevron.AddThemeColorOverride("icon_hover_color", Ui.Ink);
        chevron.Pressed += () => { if (!_expanded.Remove(kind)) _expanded.Add(kind); Refresh(); };
        line.AddChild(chevron);
        var badge = new PanelContainer { CustomMinimumSize = Ui.S(24, 24), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        badge.AddThemeStyleboxOverride("panel", Ui.Box(tile, 6));
        var glyph = Ui.IconRect(icon, 14, Colors.White);
        glyph.SizeFlagsHorizontal = glyph.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        badge.AddChild(glyph); line.AddChild(badge);
        var name = Ui.Text(RowName(kind), 13.5f, Ui.Ink, Ui.BodyBold); name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; line.AddChild(name);
        if (Blurb(kind) is { } blurb) { name.TooltipText = blurb; name.MouseFilter = Control.MouseFilterEnum.Pass; }
        if (required)
        {
            var tag = Ui.Caps("Req", Ui.Teal, 8.5f);
            tag.AddThemeStyleboxOverride("normal", Ui.Box(new Color(0, 0, 0, 0), 3, Ui.Teal, 1, 4, 0));
            tag.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            tag.TooltipText = "Required before the festival can open."; tag.MouseFilter = Control.MouseFilterEnum.Pass;
            line.AddChild(tag);
        }
        line.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var price = Ui.Text("", 12, Ui.InkMuted); price.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; line.AddChild(price);
        var dots = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        dots.AddThemeConstantOverride("separation", Ui.Px(2)); line.AddChild(dots);
        var count = Ui.Text("", 12, Ui.Ink, Ui.BodySemi); count.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        count.CustomMinimumSize = new Vector2(Ui.S(24), 0); count.HorizontalAlignment = HorizontalAlignment.Right; line.AddChild(count);
        var action = new Button { Icon = Ui.Icon("plus"), ExpandIcon = true, IconAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = Ui.S(26, 26), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        Tight(Ui.Style(action, Ui.ButtonKind.Accent), 14);
        action.Pressed += () => _actions.BeginPlacement(kind, null);
        line.AddChild(action);
        if (Blurb(kind) is { } note)
        {
            var margin = new MarginContainer(); margin.AddThemeConstantOverride("margin_left", Ui.Px(46)); margin.AddThemeConstantOverride("margin_bottom", Ui.Px(6));
            var words = Ui.Text(note, 11.5f, Ui.InkMuted); words.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            margin.AddChild(words); block.AddChild(margin);
        }
        var placed = new VBoxContainer { Visible = false }; placed.AddThemeConstantOverride("separation", 0); block.AddChild(placed);
        _buildCatalogueRows.Add(kind, new CatalogueRow(chevron, price, dots, count, action, placed));
        if (kind == BuildServiceKind.FoodVan) block.AddChild(TraderPicker());
        return block;
    }

    private Label? _coverSummary;
    private readonly List<Button> _coverButtons = [];

    /// <summary>
    /// Straw and track matting, from Tier 2: walked on, never built on. Straw keeps a path from churning to mud until it's
    /// trampled flat; matting never churns.
    /// </summary>
    private VBoxContainer GroundCoverEntry()
    {
        var block = new VBoxContainer(); block.AddThemeConstantOverride("separation", Ui.Px(4));
        block.AddChild(Rule());
        var line = new HBoxContainer { CustomMinimumSize = new Vector2(0, Ui.S(30)) };
        line.AddThemeConstantOverride("separation", Ui.Px(6)); block.AddChild(line);
        line.AddChild(new Control { CustomMinimumSize = new Vector2(Ui.S(16), 0) });
        var badge = new PanelContainer { CustomMinimumSize = Ui.S(24, 24), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        badge.AddThemeStyleboxOverride("panel", Ui.Box(new Color("9a7a2c"), 6));
        var glyph = Ui.IconRect("cloud-heavy-showers", 14, Colors.White);
        glyph.SizeFlagsHorizontal = glyph.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        badge.AddChild(glyph); line.AddChild(badge);
        var name = Ui.Text("Straw & matting", 13.5f, Ui.Ink, Ui.BodyBold); name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; line.AddChild(name);
        var margin = new MarginContainer(); margin.AddThemeConstantOverride("margin_left", Ui.Px(46)); margin.AddThemeConstantOverride("margin_bottom", Ui.Px(8));
        var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", Ui.Px(4)); margin.AddChild(stack); block.AddChild(margin);
        var words = Ui.Text("Paths for the rain: drag along the way people walk. Straw (" + FestivalCurrency.Format(GroundCoverRules.BalePennies) +
            " a bale, 4 m of path) stops mud until it's trampled flat; matting (" + FestivalCurrency.Format(GroundCoverRules.MatPanelPennies) +
            " a panel) never churns. Walked on, never built on.", 11.5f, Ui.InkMuted);
        words.AutowrapMode = TextServer.AutowrapMode.WordSmart; stack.AddChild(words);
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", Ui.Px(4)); stack.AddChild(buttons);
        foreach (var (text, action) in new[] { ("Straw", GroundCoverAction.Straw), ("Matting", GroundCoverAction.Matting), ("Lift", GroundCoverAction.Lift) })
        {
            var button = Ui.Style(new Button { Text = text, CustomMinimumSize = new Vector2(0, Ui.S(26)), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand }, action == GroundCoverAction.Lift ? Ui.ButtonKind.Quiet : Ui.ButtonKind.Secondary, 12);
            var chosen = action;
            button.Pressed += () => _actions.BeginCover(chosen);
            buttons.AddChild(button); _coverButtons.Add(button);
        }
        _coverSummary = Ui.Text("", 11.5f, Ui.InkMuted); stack.AddChild(_coverSummary);
        return block;
    }

    private void RefreshGroundCover()
    {
        if (_coverSummary is null) return;
        var p = _hud.Session.CapturePreparation();
        var open = _hud.Session.GroundCoverAvailable;
        foreach (var button in _coverButtons)
        {
            button.Disabled = !open;
            button.TooltipText = open ? "" : _hud.Session.IsClassicCampaign?"Straw and matting are available on wet festival days.":"Straw and matting come with Tier 2's wet weather.";
        }
        var straw = p?.StrawCells?.Length ?? 0; var panels = p?.MatPanels?.Length ?? 0;
        _coverSummary.Text = !open ? (_hud.Session.IsClassicCampaign?"For wet festival days.":"From Tier 2.") : straw + panels == 0 ? "None laid." :
            $"{(straw == 0 ? "" : $"Straw {straw / 2f:0} m² · {GroundCoverRules.BalesFor(straw)} bales")}{(straw > 0 && panels > 0 ? " · " : "")}" +
            $"{(panels == 0 ? "" : $"matting {panels} panels")} · {FestivalCurrency.Format(GameSession.GroundCoverCost(p))}";
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
            button.TooltipText = $"Show {BuildName(kind)} in Build";
            parent.AddChild(button);
            if (!_buildShortcutButtons.TryGetValue(kind, out var existing))
                _buildShortcutButtons[kind] = existing = [];
            existing.Add(button);
        }
    }

    private readonly List<(string Van, FoodTrader Trader, Button Button)> _traderButtons = [];
    private VBoxContainer? _traderBox;
    private string? _traderKey;

    /// <summary>The traders to choose from, a set for each van the tier allows: each pays the festival to pitch.</summary>
    private VBoxContainer TraderPicker()
    {
        _traderBox = new VBoxContainer(); _traderBox.AddThemeConstantOverride("separation", Ui.Px(3));
        var margin = new MarginContainer(); margin.AddThemeConstantOverride("margin_left", Ui.Px(46)); margin.AddThemeConstantOverride("margin_bottom", Ui.Px(6));
        var wrapper = new VBoxContainer(); wrapper.AddChild(margin); margin.AddChild(_traderBox);
        return wrapper;
    }

    /// <summary>
    /// One picker per van. A trader another van already has, or one the tier can't book yet, is shown but can't be
    /// picked, and says why.
    /// </summary>
    private void RefreshTraderPicker()
    {
        if (_traderBox is null) return;
        var session = _hud.Session;
        var vans = Enumerable.Range(1, session.ServiceLimit(BuildServiceKind.FoodVan)).Select(number => Stalls.Id(BuildServiceKind.FoodVan, number)).ToArray();
        var placed = session.PlacedVans;
        var key = $"{session.CapturePreparation()?.Tier}:" + string.Join(",", vans.Select(van => van + (placed.Contains(van) ? "+" : "-")));
        if (key != _traderKey)
        {
            _traderKey = key;
            foreach (var child in _traderBox.GetChildren()) { _traderBox.RemoveChild(child); child.QueueFree(); }
            _traderButtons.Clear();
            foreach (var van in vans)
            {
                _traderBox.AddChild(Ui.Caps(vans.Length == 1 ? "Trader · they pay you to pitch"
                    : $"Van {Stalls.Number(van)} trader · {(placed.Contains(van) ? "pays you to pitch" : "if you place it")}", Ui.InkMuted, 9.5f));
                foreach (var trader in FoodTraders.All)
                {
                    var button = new Button { Text = $"{trader.Name} · {trader.Menu.ToLowerInvariant()} · pays {FestivalCurrency.Format(trader.PitchFeePennies)}" +
                            (trader.SlowService ? " · slower service" : "") + (trader.FromTier > (session.CapturePreparation()?.Tier ?? 1) ? $" · from Tier {trader.FromTier}" : ""), ToggleMode = true,
                        Alignment = HorizontalAlignment.Left, MouseDefaultCursorShape = Control.CursorShape.PointingHand, CustomMinimumSize = new Vector2(0, Ui.S(26)) };
                    var (chosen, at) = (trader, van);
                    button.Pressed += () => _hud.Commit(new ChooseFoodTraderCommand(chosen.Id, at));
                    _traderBox.AddChild(button); _traderButtons.Add((van, trader, button));
                }
            }
        }
        foreach (var (van, trader, button) in _traderButtons)
        {
            var current = session.TraderAt(van).Id == trader.Id;
            var issue = current ? null : session.TraderUnavailable(van, trader);
            button.SetPressedNoSignal(current);
            button.Disabled = issue is not null;
            button.Modulate = issue is null ? Colors.White : new Color(1, 1, 1, .5f);
            button.TooltipText = issue ?? trader.Blurb;
            Tight(Ui.Style(button, current ? Ui.ButtonKind.Secondary : Ui.ButtonKind.Quiet, 12), 12);
        }
    }

    public void Refresh()
    {
        if (_buildDrawer is null || _hud.Session.CapturePreparationPlan() is null) return;
        RefreshTraderPicker();
        RefreshGroundCover();
        var placements = _hud.Session.CaptureBuildPlacements();
        _servicesTotal!.Text = $"{placements.Count} placed · {FestivalCurrency.Format(_hud.Session.BuildDraftCost)} · nothing paid until Start";
        var standard = GameSession.StandardBuildLayout(_hud.Session.CapturePreparation()?.Tier ?? 1).Sum(item => GameSession.BuildServiceFeePennies(item.Kind));
        var lastLayout = _hud.Session.CapturePreparation()?.PreviousLayout;
        _lastLayoutButton!.Visible = lastLayout is not null;
        // Priced as the confirmation is: only what still fits this field (worked out once and remembered).
        if (lastLayout is not null && _hud.Session.LastFestivalLayoutFit() is { } fit)
            _lastLayoutButton.Text = $"Copy last festival's layout · {FestivalCurrency.Format(fit.Placed.Sum(item => GameSession.BuildServiceFeePennies(item.Kind)))}";
        _defaultsButton!.Text = lastLayout is null ? $"Default layout · {FestivalCurrency.Format(standard)}" : $"or use the default layout · {FestivalCurrency.Format(standard)}";
        if (_defaultsQuiet != lastLayout is not null)
        {
            _defaultsQuiet = lastLayout is not null;
            if (lastLayout is null) { Ui.Style(_defaultsButton, Ui.ButtonKind.Secondary, 12.5f); _defaultsButton.CustomMinimumSize = new Vector2(0, Ui.S(30)); }
            else { Ui.Style(_defaultsButton, Ui.ButtonKind.Quiet, 11); _defaultsButton.CustomMinimumSize = new Vector2(0, Ui.S(22)); }
        }
        for (var index = 0; index < Categories.Length; index++)
        {
            var (name, kinds) = Categories[index];
            var placedHere = placements.Count(item => kinds.Contains(item.Kind));
            var required = kinds.Where(kind => Look(kind).Required).ToArray();
            var missing = required.Any(kind => !placements.Any(item => item.Kind == kind));
            var mark = required.Length == 0 ? "" : missing ? " !" : " ✓";
            _tabs[index].Text = $"{name} {placedHere}{mark}";
            _tabs[index].TooltipText = missing ? $"{name}: a required service still needs placing." : name;
        }
        foreach (var (kind, row) in _buildCatalogueRows)
        {
            var count = placements.Count(item => item.Kind == kind);
            var limit = _hud.Session.ServiceLimit(kind);
            // The food van costs nothing to place: its trader pays to pitch.
            // The next van's trader, or with every van placed, the last one's.
            var nextVan = _hud.Session.TraderAt(_hud.Session.PlacedVans.Count < _hud.Session.ServiceLimit(BuildServiceKind.FoodVan) || _hud.Session.PlacedVans.Count == 0
                ? Stalls.Next(BuildServiceKind.FoodVan, placements.Select(item => item.Id)) : _hud.Session.PlacedVans[^1]);
            row.Price.Text = kind == BuildServiceKind.FoodVan ? $"+{FestivalCurrency.Format(nextVan.PitchFeePennies)}"
                : FestivalCurrency.Format(GameSession.BuildServiceFeePennies(kind));
            row.Count.Text = kind == BuildServiceKind.Bin ? $"{count}" : $"{count}/{limit}";
            var dotCount = kind == BuildServiceKind.Bin ? 0 : limit;
            if (row.Dots.GetChildCount() != dotCount)
            {
                foreach (var child in row.Dots.GetChildren()) { row.Dots.RemoveChild(child); child.QueueFree(); }
                for (var i = 0; i < dotCount; i++) row.Dots.AddChild(new Panel { CustomMinimumSize = Ui.S(7, 7), MouseFilter = Control.MouseFilterEnum.Ignore });
            }
            for (var i = 0; i < dotCount; i++)
                row.Dots.GetChild<Panel>(i).AddThemeStyleboxOverride("panel",
                    i < count ? Ui.Box(Ui.Teal, 4) : Ui.Box(new Color(0, 0, 0, 0), 4, new Color("9db5ae"), 1.5f));
            var full = count >= limit;
            row.Action.Disabled = full;
            row.Action.Icon = Ui.Icon(full ? "check" : "plus");
            Tight(Ui.Style(row.Action, full ? Ui.ButtonKind.Quiet : Ui.ButtonKind.Accent), 14);
            row.Action.TooltipText = full
                ? $"{BuildName(kind)}: all {limit} placed. Open the row, or right-click one on the farm, to move or remove it."
                : kind == BuildServiceKind.FoodVan ? $"Pitch a food van: {nextVan.Name} pays {FestivalCurrency.Format(nextVan.PitchFeePennies)} at Start. {count} placed."
                : $"Place a {BuildName(kind).ToLowerInvariant()} ({FestivalCurrency.Format(GameSession.BuildServiceFeePennies(kind))} at Start). {count} placed.";
            if (count == 0) _expanded.Remove(kind);
            row.Chevron.Disabled = count == 0;
            row.Chevron.Modulate = count == 0 ? new Color(1, 1, 1, .3f) : Colors.White;
            row.Chevron.Icon = Ui.Icon(_expanded.Contains(kind) ? "chevron-down" : "chevron-right");
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
        var key = string.Join("|", placements.Select(item => $"{item.Id}:{item.Cell.X}:{item.Cell.Z}:{item.QuarterTurns}")) +
            "#" + string.Join(",", _expanded.Order());
        if (key == _buildPlacedKey) return;
        _buildPlacedKey = key;
        foreach (var (kind, row) in _buildCatalogueRows)
        {
            foreach (var child in row.Placed.GetChildren()) { row.Placed.RemoveChild(child); child.QueueFree(); }
            row.Placed.Visible = _expanded.Contains(kind);
            if (!row.Placed.Visible) continue;
            foreach (var item in placements.Where(item => item.Kind == kind))
            {
                var line = new HBoxContainer { CustomMinimumSize = new Vector2(0, Ui.S(28)) };
                line.AddThemeConstantOverride("separation", Ui.Px(4)); row.Placed.AddChild(line);
                line.AddChild(new Control { CustomMinimumSize = new Vector2(Ui.S(46), 0) });
                var name = Ui.Text($"{PlacedName(item, _hud.Session)} · on the field", 12, Ui.InkMuted);
                name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; name.VerticalAlignment = VerticalAlignment.Center; line.AddChild(name);
                line.AddChild(SmallIcon("move", "Move",
                    () => _actions.BeginPlacement(item.Kind, item.Id)));
                line.AddChild(SmallIcon("trash-2", "Remove",
                    () => _actions.RemovePlacement(item.Id)));
            }
        }
        Shrink();
    }

    /// <summary>
    /// An icon-only button at row size: the shared button padding would squeeze a small icon to nothing, so it's
    /// trimmed, and the icon drawn at its own size.
    /// </summary>
    private static T Tight<T>(T button, float iconPx, bool flat = false) where T : Button
    {
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled", "hover_pressed" })
        {
            if (flat) { button.AddThemeStyleboxOverride(state, new StyleBoxEmpty()); continue; }
            if (button.GetThemeStylebox(state) is not StyleBoxFlat box) continue;
            var tight = (StyleBoxFlat)box.Duplicate();
            tight.ContentMarginLeft = tight.ContentMarginRight = tight.ContentMarginTop = tight.ContentMarginBottom = Ui.S(2);
            button.AddThemeStyleboxOverride(state, tight);
        }
        button.ExpandIcon = false;
        button.AddThemeConstantOverride("icon_max_width", Ui.Px(iconPx));
        return button;
    }

    private static Button SmallIcon(string icon, string tooltip, Action pressed)
    {
        var button = new Button { Icon = Ui.Icon(icon), ExpandIcon = true, IconAlignment = HorizontalAlignment.Center, TooltipText = tooltip,
            CustomMinimumSize = Ui.S(24, 24), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        Tight(Ui.Style(button, Ui.ButtonKind.Quiet), 13);
        button.Pressed += pressed;
        return button;
    }

    /// <summary>The catalogue's name for a service: the marquee goes by its banner.</summary>
    private static string RowName(BuildServiceKind kind) => kind == BuildServiceKind.Marquee ? Main.MarqueeTitle : BuildName(kind);

    /// <summary>A line under the row for a service that needs saying what it's for.</summary>
    private static string? Blurb(BuildServiceKind kind) => kind == BuildServiceKind.Marquee
        ? "Shade in a heatwave: no heat builds up under it, and 15 can rest there. In the rain, 15 can shelter under it and dry off."
        : null;

    /// <summary>"Toilet 2" rather than "toilet.extra-1"; a bar or van by its name on the field, "East Bar".</summary>
    internal static string PlacedName(BuildPlacement item, GameSession session)
    {
        if (Stalls.IsStall(item.Kind)) return session.StallName(item.Id);
        var suffix = item.Id.Split('.').Last();
        var number = Stalls.IsStall(item.Kind) ? Stalls.Number(item.Id).ToString() :
            suffix == "main" ? "1" : suffix.StartsWith("extra-", StringComparison.Ordinal) && int.TryParse(suffix[6..], out var extra)
            ? (extra + 1).ToString() : suffix;
        return GameSession.BuildServiceLimit(item.Kind, GameSession.HighestTier) == 1 ? BuildName(item.Kind) : $"{BuildName(item.Kind)} {number}";
    }

    public string CostSummary()
    {
        var costs = PlanCosts.Of(_hud.Session);
        return $"Services {FestivalCurrency.Format(costs.Services)} · Acts {FestivalCurrency.Format(costs.Acts)} · Staff {FestivalCurrency.Format(costs.Staff)}\n" +
            $"Equipment {FestivalCurrency.Format(costs.Equipment)} · Stock {FestivalCurrency.Format(costs.Stock)}\n" +
            $"Complete setup {FestivalCurrency.Format(_hud.Session.PreparationPlanCost)} · Remaining {FestivalCurrency.Format(_hud.Session.PreparationRemainingCash)}";
    }

    /// <summary>Asks before copying last festival's layout, saying what it costs and how many services won't fit this year.</summary>
    private void ShowLastLayout()
    {
        if (_hud.Session.LastFestivalLayoutFit() is not { } fit) return;
        var oldCost = _hud.Session.BuildDraftCost;
        var newCost = fit.Placed.Sum(item => GameSession.BuildServiceFeePennies(item.Kind));
        _dialogCopiesLast = true;
        _buildDefaultsDialog!.Title = "Copy last festival's layout?";
        _buildDefaultsDialog.DialogText = $"Replace {_hud.Session.CaptureBuildPlacements().Count} placed services ({FestivalCurrency.Format(oldCost)}) with {fit.Placed.Length} from last festival's layout ({FestivalCurrency.Format(newCost)})?\n" +
            (fit.Skipped > 0 ? $"{fit.Skipped} of last festival's services {(fit.Skipped == 1 ? "doesn't" : "don't")} fit this field and will be left off. " : "") +
            "Anything new this tier is left for you to place.\nBookings, staff, equipment, stock and perks stay.";
        _buildDefaultsDialog.PopupCentered(new Vector2I(510, 240));
    }

    private void ShowDefaults()
    {
        _dialogCopiesLast = false;
        _buildDefaultsDialog!.Title = "Replace service layout?";
        var old = _hud.Session.CaptureBuildPlacements();
        var defaults = GameSession.StandardBuildLayout(_hud.Session.CapturePreparation()?.Tier ?? 1);
        var oldCost = _hud.Session.BuildDraftCost;
        var newCost = defaults.Sum(item => GameSession.BuildServiceFeePennies(item.Kind));
        _buildDefaultsDialog!.DialogText = $"Replace {old.Count} placed services ({FestivalCurrency.Format(oldCost)}) with {defaults.Length} standard services ({FestivalCurrency.Format(newCost)})?\n" +
            $"Service draft change: {FestivalCurrency.Format(newCost - oldCost)}. New complete draft: {FestivalCurrency.Format(_hud.Session.PreparationPlanCost - oldCost + newCost)}.\n" +
            "Bookings, staff, equipment, stock and perks stay.";
        _buildDefaultsDialog.PopupCentered(new Vector2I(510, 220));
    }
}

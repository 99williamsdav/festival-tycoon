using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

/// <summary>What the top bar's buttons ask the HUD shell to do.</summary>
internal interface ITopBarActions
{
    void TogglePause();
    void ShowAlerts();
    void ToggleMenu();
    void TogglePerks();
    void ToggleRoster();
}

/// <summary>
/// The status bar across the top: the festival and its phase, cash, the festival clock, guests and
/// weather, then Pause (live), People and Perks (preparation), alerts with a count badge, and the menu.
/// </summary>
internal sealed class TopBar(IHudHost _hud, ITopBarActions _actions)
{
    private Label? _phase;
    private Panel? _liveDot;
    private Label? _cash;
    private Label? _clock;
    private Label? _guests;
    private Label? _guestsOf;
    private Label? _weather;
    private Button? _pause;
    private Button? _people;
    private Button? _perks;
    private Button? _alerts;
    private Label? _alertBadge;

    public bool IsBuilt => _cash is not null;
    /// <summary>The cash figure, where festival cash popups start.</summary>
    public Control? Cash => _cash;

    public void Build(CanvasLayer layer, float width)
    {
        var bar = new PanelContainer { Position = Vector2.Zero, Size = new Vector2(width, Ui.TopBar), Theme = HudTheme() };
        var style = Ui.Box(Ui.Bar, 0, padX: 10, padY: 0, shadow: 14);
        style.BorderColor = Ui.Gold; style.BorderWidthBottom = Ui.Px(2);
        style.ContentMarginRight = Ui.S(12);
        bar.AddThemeStyleboxOverride("panel", style); layer.AddChild(bar);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        row.AddThemeConstantOverride("separation", 0); bar.AddChild(row);

        var identity = Group(row, divider: true, first: true);
        var logo = new TextureRect { Texture = GD.Load<Texture2D>("res://assets/branding/festival-tycoon-stage-sun-icon-v3.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = Ui.S(38, 38), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        identity.AddChild(logo);
        var names = Stack(identity);
        names.AddChild(Ui.Text("Lower Wittering", 20, Ui.BarText, Ui.SlabBold));
        var phaseRow = new HBoxContainer(); phaseRow.AddThemeConstantOverride("separation", Ui.Px(6)); names.AddChild(phaseRow);
        _liveDot = new Panel { CustomMinimumSize = Ui.S(7, 7), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Visible = false };
        _liveDot.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Warn, 4));
        phaseRow.AddChild(_liveDot);
        _phase = Ui.Caps("", Ui.Gold); phaseRow.AddChild(_phase);

        _cash = Stat(row, "Cash", glyph: "£", icon: null, divider: true).Value;
        _clock = Stat(row, "Festival clock", glyph: null, icon: "clock", divider: true).Value;
        (_guests, var guestLine) = Stat(row, "Guests", glyph: null, icon: "users", divider: true);
        _guestsOf = Ui.Text("", 14, Ui.BarMuted, Ui.Slab); guestLine.AddChild(_guestsOf);
        _weather = Stat(row, "Weather", glyph: null, icon: "sun", divider: false).Value;

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var buttons = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        buttons.AddThemeConstantOverride("separation", Ui.Px(8)); row.AddChild(buttons);
        _pause = BarIconButton("pause", "Pause / resume (Space)", _actions.TogglePause); buttons.AddChild(_pause);
        _people = Ui.IconButton("People", "users", Ui.ButtonKind.Bar, _actions.ToggleRoster);
        _people.CustomMinimumSize = new Vector2(0, Ui.S(40)); _people.TooltipText = "Everyone on the farm"; buttons.AddChild(_people);
        _perks = Ui.IconButton("Perks", "star", Ui.ButtonKind.Bar, _actions.TogglePerks);
        _perks.CustomMinimumSize = new Vector2(0, Ui.S(40));
        foreach (var state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" })
            _perks.AddThemeColorOverride(state, Ui.Gold);
        buttons.AddChild(_perks);
        _alerts = BarIconButton("bell", "Show current urgent alerts; press again for the next group. Select an alert to locate it.", _actions.ShowAlerts);
        buttons.AddChild(_alerts);
        _alertBadge = Ui.Text("", 11.5f, Colors.White, Ui.BodyBold);
        var badge = Ui.Box(Ui.Alert, 9); badge.SetBorderWidthAll(Ui.Px(2)); badge.BorderColor = Ui.Bar;
        badge.ContentMarginLeft = badge.ContentMarginRight = Ui.S(4);
        _alertBadge.AddThemeStyleboxOverride("normal", badge);
        _alertBadge.HorizontalAlignment = HorizontalAlignment.Center; _alertBadge.VerticalAlignment = VerticalAlignment.Center;
        _alertBadge.CustomMinimumSize = Ui.S(20, 20); _alertBadge.MouseFilter = Control.MouseFilterEnum.Ignore;
        _alertBadge.SetAnchorsPreset(Control.LayoutPreset.TopRight); _alertBadge.Position = new Vector2(Ui.S(44) - Ui.S(14), -Ui.S(6));
        _alerts.AddChild(_alertBadge);
        buttons.AddChild(BarIconButton("menu", "Festival menu", _actions.ToggleMenu));
    }

    private static HBoxContainer Group(HBoxContainer row, bool divider, bool first = false)
    {
        var group = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(0, Ui.S(44)) };
        group.AddThemeConstantOverride("separation", Ui.Px(10));
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", first ? 0 : Ui.Px(18));
        margin.AddThemeConstantOverride("margin_right", Ui.Px(18));
        margin.AddChild(group); row.AddChild(margin);
        if (divider)
            row.AddChild(new ColorRect { Color = Ui.BarLine, CustomMinimumSize = new Vector2(1, Ui.S(44)), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        return group;
    }

    private static VBoxContainer Stack(HBoxContainer group)
    {
        var stack = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        stack.AddThemeConstantOverride("separation", 0); group.AddChild(stack);
        return stack;
    }

    private static (Label Value, HBoxContainer Line) Stat(HBoxContainer row, string caption, string? glyph, string? icon, bool divider)
    {
        var group = Group(row, divider);
        var disc = new PanelContainer { CustomMinimumSize = Ui.S(30, 30), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        disc.AddThemeStyleboxOverride("panel", Ui.Box(new Color(Ui.Gold, 0.16f), 15));
        group.AddChild(disc);
        if (icon is not null)
        {
            var image = Ui.IconRect(icon, 18, Ui.Gold); image.SizeFlagsHorizontal = image.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            disc.AddChild(image);
        }
        else
        {
            var mark = Ui.Text(glyph ?? "", 16, Ui.Gold, Ui.BodyBold);
            mark.HorizontalAlignment = HorizontalAlignment.Center; mark.VerticalAlignment = VerticalAlignment.Center;
            disc.AddChild(mark);
        }
        var stack = Stack(group);
        stack.AddChild(Ui.Caps(caption, Ui.BarMuted));
        var line = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        line.AddThemeConstantOverride("separation", Ui.Px(5)); stack.AddChild(line);
        var value = Ui.Text("", 19, Ui.BarText, Ui.Slab); line.AddChild(value);
        return (value, line);
    }

    private static Button BarIconButton(string icon, string tooltip, Action action)
    {
        var button = Ui.Style(new Button { Icon = Ui.Icon(icon), ExpandIcon = true, IconAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = Ui.S(44, 40), TooltipText = tooltip, MouseDefaultCursorShape = Control.CursorShape.PointingHand }, Ui.ButtonKind.Bar);
        button.Pressed += action;
        return button;
    }

    public void Refresh(int alertCount)
    {
        if (_cash is null || _hud.Session.CapturePreparation() is not { } p) return;
        var session = _hud.Session;
        var preparing = p.Status == PreparationStatus.Preparing;
        var live = p.Status is PreparationStatus.Running;
        _phase!.Text = (p.Status switch
        {
            PreparationStatus.Preparing => "Preparation · before opening",
            PreparationStatus.Departing => "Departing · festival finished",
            _ => "Live · festival day",
        }).ToUpperInvariant();
        _phase.AddThemeColorOverride("font_color", preparing ? Ui.Gold : Ui.Warn);
        _liveDot!.Visible = live;
        var finance = session.CaptureSnapshot().FestivalFinances.Single(f => f.OwnerId.Value == p.FinanceOwnerId);
        _cash.Text = FestivalCurrency.Format(finance.CashPennies);
        _clock!.Text = preparing ? "Not started" : $"{FestivalClockText(session.CurrentTick - p.StartedTick)} / 08:00";
        _guests!.Text = session.OnSiteAttendeeCount.ToString();
        _guestsOf!.Text = $"/ {p.People.Count(person => person.Role == ProtectedPersonRole.Guest)}";
        _weather!.Text = session.CaptureMedical() is { IsHot: true } ? "Hot" : "Unavailable";
        _pause!.Visible = !preparing;
        _pause.Icon = Ui.Icon(session.IsPaused ? "play" : "pause");
        var perks = session.CapturePerks();
        _people!.Visible = preparing;
        _perks!.Visible = preparing && perks is { Ended: false, Pending: false };
        if (perks is not null) _perks.Text = $"Perks {perks.Equipped.Length}/5";
        _alertBadge!.Text = alertCount.ToString();
        _alertBadge.Visible = alertCount > 0;
        _alerts!.TooltipText = alertCount == 0 ? "No urgent alerts" : $"{alertCount} urgent alert{(alertCount == 1 ? "" : "s")} · show them";
    }

}

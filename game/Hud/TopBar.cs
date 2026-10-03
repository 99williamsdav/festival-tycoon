using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

/// <summary>What the top bar's buttons ask the HUD shell to do.</summary>
internal interface ITopBarActions
{
    void TogglePause();
    void CycleSpeed();
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
    private Control? _clockTrack;
    private float _clockFraction;
    private readonly (float Start, float End, Color Colour)[] _clockSets = new (float, float, Color)[3];
    private Label? _guests;
    private Label? _guestsOf;
    private Label? _weather;
    private Button? _pause;
    private Button? _speed;
    private Button? _people;
    private Button? _perks;
    private Button? _alerts;
    private Label? _alertBadge;
    private readonly System.Collections.Generic.List<Control> _dayOnly = [];

    public bool IsBuilt => _cash is not null;
    /// <summary>The box office briefing is open: the bar slims down as it does for the perk draft.</summary>
    public bool Briefing { get; set; }
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
        (_clock, var clockLine) = Stat(row, "Festival clock", glyph: null, icon: "clock", divider: true);
        _clockTrack = new Control { CustomMinimumSize = new Vector2(Ui.S(240), Ui.S(12)), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _clockTrack.Draw += DrawClockTrack;
        ((VBoxContainer)clockLine.GetParent()).AddChild(_clockTrack);
        (_guests, var guestLine) = Stat(row, "Guests", glyph: null, icon: "users", divider: true);
        _guestsOf = Ui.Text("", 14, Ui.BarMuted, Ui.Slab); guestLine.AddChild(_guestsOf);
        _weather = Stat(row, "Weather", glyph: null, icon: "sun", divider: false).Value;
        // Clock, guests and weather (with their dividers) step aside during the perk draft.
        var cashGroup = _cash.GetParent().GetParent().GetParent().GetParent<Control>();
        // Cash's divider, then clock, divider, guests, divider and weather.
        for (var i = cashGroup.GetIndex() + 1; i <= cashGroup.GetIndex() + 6; i++) _dayOnly.Add(row.GetChild<Control>(i));

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var buttons = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        buttons.AddThemeConstantOverride("separation", Ui.Px(8)); row.AddChild(buttons);
        _pause = BarIconButton("pause", "Pause / resume (Space)", _actions.TogglePause); buttons.AddChild(_pause);
        // Day speed: 1×, 2× or 4×, for the quieter stretches.
        _speed = Ui.Style(new Button { Text = "1×", CustomMinimumSize = Ui.S(48, 40), TooltipText = "Day speed 1× · 2× · 4× (keys 1, 2, 3)",
            MouseDefaultCursorShape = Control.CursorShape.PointingHand }, Ui.ButtonKind.Bar, 15);
        _speed.Pressed += _actions.CycleSpeed; buttons.AddChild(_speed);
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

    /// <summary>The live day as a track: elapsed time, each set in its genre's colour, and a playhead.</summary>
    private void DrawClockTrack()
    {
        var track = _clockTrack!; var size = track.Size;
        track.DrawStyleBox(Ui.Box(Ui.BarDeep, 6), new Rect2(Vector2.Zero, size));
        if (_clockFraction > 0) track.DrawStyleBox(Ui.Box(new Color(Ui.BarText, 0.28f), 6), new Rect2(Vector2.Zero, new Vector2(size.X * _clockFraction, size.Y)));
        foreach (var (start, end, colour) in _clockSets)
            if (end > start) track.DrawStyleBox(Ui.Box(colour, 3), new Rect2(size.X * start, Ui.S(2), size.X * (end - start), size.Y - Ui.S(4)));
        var x = size.X * _clockFraction;
        track.DrawRect(new Rect2(x - Ui.S(2.5f), -Ui.S(4), Ui.S(5), size.Y + Ui.S(8)), Ui.Bar);
        track.DrawRect(new Rect2(x - Ui.S(1.5f), -Ui.S(4), Ui.S(3), size.Y + Ui.S(8)), Ui.BarText);
    }

    private static Color SetColour(int genre) => genre switch
    {
        0 => new Color("8fb27a"), 1 => new Color("d98a63"), 2 => new Color("e7c15a"), 3 => new Color("7fa9c9"),
        4 => new Color("e07aa2"), 5 => new Color("9a99a3"), _ => Ui.BarMuted,
    };

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
        var programme = session.CaptureProgramme();
        _clockTrack!.Visible = !preparing && programme is not null;
        if (_clockTrack.Visible)
        {
            float day = GameSession.PreparedDayTicks;
            _clockFraction = Math.Clamp((session.CurrentTick - p.StartedTick) / day, 0, 1);
            var acts = session.GetFestivalActs().ToDictionary(act => act.Id);
            for (var i = 0; i < 3; i++)
                _clockSets[i] = (GameSession.FestivalSlotStarts[i] / day, GameSession.FestivalSlotEnds[i] / day,
                    SetColour(acts.TryGetValue(programme!.ActIds[i], out var act) ? act.Genre : -1));
            _clockTrack.QueueRedraw();
        }
        _guests!.Text = session.OnSiteAttendeeCount.ToString();
        _guestsOf!.Text = $"/ {p.People.Count(person => person.Role == ProtectedPersonRole.Guest)}";
        _weather!.Text = session.CaptureMedical() is { IsHot: true } ? "Hot" : "Unavailable";
        _pause!.Visible = !preparing;
        _pause.Icon = Ui.Icon(session.IsPaused ? "play" : "pause");
        _speed!.Visible = _pause.Visible;
        _speed.Text = $"{(int)_hud.Host.Clock.RequestedSpeed}×";
        var perks = session.CapturePerks();
        _people!.Visible = preparing;
        _perks!.Visible = preparing && perks is { Ended: false, Pending: false };
        if (perks is not null) _perks.Text = $"Perks {perks.Equipped.Length}/5";
        var drafting = perks?.Pending == true;
        var slim = drafting || Briefing;
        foreach (var control in _dayOnly) control.Visible = !slim;
        _people.Visible &= !slim; _perks.Visible &= !slim; _alerts!.Visible = !slim;
        if (drafting) _phase.Text = "PREPARATION · PERK DRAFT";
        else if (Briefing) _phase.Text = "PREPARATION · BOX OFFICE";
        _alertBadge!.Text = alertCount.ToString();
        _alertBadge.Visible = alertCount > 0;
        _alerts!.TooltipText = alertCount == 0 ? "No urgent alerts" : $"{alertCount} urgent alert{(alertCount == 1 ? "" : "s")} · show them";
    }

}

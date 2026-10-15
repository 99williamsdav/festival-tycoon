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
    // The Pond Stage's sets, a second thin row under the trailer's; null on a one-stage day.
    private (float Start, float End, Color Colour)[]? _pondClockSets;
    private Label? _guests;
    private Label? _guestsOf;
    private Label? _weather;
    private Label? _weatherWhen;
    private TextureRect? _weatherIcon;
    private string _weatherIconName = "sun";
    private Label? _mood;
    private ProgressBar? _moodBar;
    private TextureRect? _moodTrend;
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
        // How the crowd feels overall: the average satisfaction the newspaper's rating is built from.
        (_mood, var moodLine) = Stat(row, "Crowd mood", glyph: null, icon: "star", divider: true);
        // Which way it's heading over the last few festival minutes; the tooltip says why.
        _moodTrend = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = Ui.S(16, 16), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        moodLine.AddChild(_moodTrend);
        // The bar sits under the figure, as the clock's track does, to keep the bar narrow.
        _moodBar = new ProgressBar { ShowPercentage = false, MaxValue = 100, CustomMinimumSize = new Vector2(Ui.S(70), Ui.S(6)),
            MouseFilter = Control.MouseFilterEnum.Ignore };
        _moodBar.AddThemeStyleboxOverride("background", Ui.Box(Ui.BarDeep, 3));
        ((VBoxContainer)moodLine.GetParent()).AddChild(_moodBar);
        (_weather, var weatherLine) = Stat(row, "Weather", glyph: null, icon: "sun", divider: false);
        // Chip B: the weather now, and a muted when it changes ("till 03:40", "rain in 0:45").
        _weatherWhen = Ui.Text("", 14, Ui.BarMuted, Ui.Slab); weatherLine.AddChild(_weatherWhen);
        _weatherIcon = weatherLine.GetParent().GetParent().FindChildren("*", "TextureRect", true, false).OfType<TextureRect>().FirstOrDefault();
        // Clock, guests and weather (with their dividers) step aside during the perk draft.
        var cashGroup = _cash.GetParent().GetParent().GetParent().GetParent<Control>();
        // Cash's divider, then clock, divider, guests, divider, mood, divider and weather.
        for (var i = cashGroup.GetIndex() + 1; i <= cashGroup.GetIndex() + 8; i++) _dayOnly.Add(row.GetChild<Control>(i));

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
        _alerts.Name="FestivalAlerts";
        buttons.AddChild(_alerts);
        _alertBadge = Ui.Text("", 11.5f, Colors.White, Ui.BodyBold);
        var badge = Ui.Box(Ui.Alert, 9); badge.SetBorderWidthAll(Ui.Px(2)); badge.BorderColor = Ui.Bar;
        badge.ContentMarginLeft = badge.ContentMarginRight = Ui.S(4);
        _alertBadge.AddThemeStyleboxOverride("normal", badge);
        _alertBadge.HorizontalAlignment = HorizontalAlignment.Center; _alertBadge.VerticalAlignment = VerticalAlignment.Center;
        _alertBadge.CustomMinimumSize = Ui.S(20, 20); _alertBadge.MouseFilter = Control.MouseFilterEnum.Ignore;
        // Placed from the bell's top-left: anchored to its right edge, it slid off when the bell took its size.
        _alertBadge.Position = new Vector2(Ui.S(44) - Ui.S(14), -Ui.S(6));
        _alerts.AddChild(_alertBadge);
        var menu=BarIconButton("menu", "Festival menu", _actions.ToggleMenu);menu.Name="FestivalMenu";buttons.AddChild(menu);
    }

    /// <summary>The live day as a track: elapsed time, each set in its genre's colour, and a playhead.</summary>
    private void DrawClockTrack()
    {
        var track = _clockTrack!; var size = track.Size;
        track.DrawStyleBox(Ui.Box(Ui.BarDeep, 6), new Rect2(Vector2.Zero, size));
        if (_clockFraction > 0) track.DrawStyleBox(Ui.Box(new Color(Ui.BarText, 0.28f), 6), new Rect2(Vector2.Zero, new Vector2(size.X * _clockFraction, size.Y)));
        if (_pondClockSets is { } pond)
        {
            // Two stages: the trailer's sets along the top half, the Pond Stage's half-offset sets along the bottom.
            var half = (size.Y - Ui.S(4)) / 2;
            foreach (var (start, end, colour) in _clockSets)
                if (end > start) track.DrawStyleBox(Ui.Box(colour, 2), new Rect2(size.X * start, Ui.S(2), size.X * (end - start), half - Ui.S(0.5f)));
            foreach (var (start, end, colour) in pond)
                if (end > start) track.DrawStyleBox(Ui.Box(colour, 2), new Rect2(size.X * start, Ui.S(2) + half + Ui.S(0.5f), size.X * (end - start), half - Ui.S(0.5f)));
        }
        else
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

    // A change under a quarter of a point over the window doesn't count as the mood going anywhere.
    private const double MoodSteadyPoints = 0.25, MoodCausePoints = 0.05;

    /// <summary>The trend arrow and the tooltip's reasons: each cause's push on the crowd's average over the last few minutes.</summary>
    private string MoodBreakdown(IReadOnlyList<MoodChange> changes, int guests)
    {
        double Points(long change) => change / 100.0 / guests;
        var total = Points(changes.Sum(change => change.Change));
        _moodTrend!.Visible = Math.Abs(total) >= MoodSteadyPoints;
        _moodTrend.Texture = Ui.Icon(total > 0 ? "chevron-up" : "chevron-down");
        _moodTrend.Modulate = total > 0 ? new Color("53bb72") : new Color("df5750");
        string Line(MoodChange change) => $"\n  {MoodCauseName(change)}  {(Points(change.Change) > 0 ? "+" : "−")}{Math.Abs(Points(change.Change)):0.0}%";
        var up = changes.Where(change => Points(change.Change) >= MoodCausePoints).ToArray();
        var down = changes.Where(change => Points(change.Change) <= -MoodCausePoints).ToArray();
        var heading = Math.Abs(total) < MoodSteadyPoints ? "Steady" : total > 0 ? $"Rising, +{total:0.0}%" : $"Falling, −{-total:0.0}%";
        return $"\n\n{heading} over the last {GameSession.MoodWindowMinutes} minutes." +
            (up.Length > 0 ? "\nLifting it:" + string.Concat(up.Select(Line)) : "") +
            (down.Length > 0 ? "\nDragging it down:" + string.Concat(down.Select(Line)) : "");
    }

    private static string MoodCauseName(MoodChange change) => change.Cause switch
    {
        MoodCause.Music => "The music",
        MoodCause.MusicCutOff => "The music cutting out",
        MoodCause.FoodAndDrink => "Food and drink",
        MoodCause.FriendlyQueues => "Chatting in queues",
        MoodCause.FreeWaterPerk => "Something in the water",
        MoodCause.StaffNearby => change.Change > 0 ? "Friendly staff" : "Grumpy staff",
        MoodCause.MudAndPuddles => "Mud and puddles",
        MoodCause.LitterAndWasps => "Litter and wasps",
        MoodCause.ToiletSmell => "Toilet smell",
        MoodCause.BrokenTap => "A broken tap",
        MoodCause.StuckInToilet => "Someone stuck in the loo",
        MoodCause.Heat => "Heat",
        MoodCause.Thirst => "Thirst",
        MoodCause.LongQueues => "Long queues",
        MoodCause.StageInvader => "A steward on the stage",
        _ => change.Cause.ToString(),
    };

    /// <summary>
    /// The weather chip: Hot in a heatwave; on a rainy day the shower now (heavy showers or a downpour) and when it stops,
    /// or the dry spell and when the next one's due. Before opening it's the day's forecast.
    /// </summary>
    private void RefreshWeather(GameSession session, PreparationSnapshot p)
    {
        var outlook = session.CaptureWeatherOutlook();
        string icon, text, when = "", tip;
        if(session.IsClassicCampaign&&session.CaptureClassicForecast() is{} forecast)
        {
            var elapsed=Math.Max(0,session.CurrentTick-p.StartedTick);
            text=$"{session.TemperatureAt(elapsed)}°C";
            icon=outlook.Now==RainIntensity.Downpour?"cloud-downpour":outlook.Now!=RainIntensity.Dry?"cloud-heavy-showers":
                forecast.Conditions==ClassicWeatherKind.Cloudy?"sun-dry-spell":"sun";
            tip=forecast.Summary+" "+forecast.PlanningAdvice+" Now: "+ClassicForecastScreen.RainName(outlook.Now)+".";
        }
        else if(outlook.Kind==WeatherKind.Mild)
        {
            icon="sun";text="Mild";
            tip="Dry conditions. Water, first aid and ordinary safety responses still matter.";
        }
        else if (outlook.Kind == WeatherKind.Heatwave)
        {
            (icon, text) = session.CaptureMedical() is { IsHot: true } ? ("sun", "Hot") : ("sun", "Fine");
            tip = "A heatwave: guests heat up out in the sun and get thirsty. Water, shade and rest keep them going.";
        }
        else if (p.Status == PreparationStatus.Preparing)
        {
            icon = outlook.Downpour ? "cloud-downpour" : "cloud-heavy-showers"; text = "Showers";
            when = outlook.ChangeTick >= 0 ? $"from {FestivalClockText(outlook.ChangeTick)}" : "";
            tip = $"Forecast: {outlook.Showers} heavy showers with dry spells, from the first set into dusk" +
                (outlook.Downpour ? ", and a downpour in one of them." : ".") + " Wet guests get miserable: put up a marquee, stock ponchos, lay straw or matting.";
        }
        else
        {
            var now = session.CurrentTick - p.StartedTick;
            var raining = outlook.Now != RainIntensity.Dry;
            icon = outlook.Now switch { RainIntensity.Downpour => "cloud-downpour", RainIntensity.Heavy => "cloud-heavy-showers", _ => "sun-dry-spell" };
            text = outlook.Now switch { RainIntensity.Downpour => "Downpour", RainIntensity.Heavy => "Heavy showers", _ => outlook.ChangeTick >= 0 ? "Dry spell" : "Clearing" };
            var left = outlook.ChangeTick - now;
            when = outlook.ChangeTick < 0 ? "" : raining
                ? left <= 1_600 && outlook.Next == RainIntensity.Dry ? $"dry in {FestivalDurationText(left)}" : $"till {FestivalClockText(outlook.ChangeTick)}"
                : $"rain in {FestivalDurationText(left)}";
            tip = raining ? "Out in it, guests soak and sulk. Under the marquee, a bar canopy or the first-aid porch, or in a poncho, they dry off."
                : outlook.ChangeTick >= 0 ? (outlook.Next == RainIntensity.Downpour ? "A downpour is on its way." : "Another shower is on its way.") + " Straw and ponchos now save mud and moods later."
                : "The rain's done for the day.";
        }
        _weather!.Text = text;
        _weatherWhen!.Text = when;
        _weatherWhen.Visible = when.Length > 0;
        var group = _weather.GetParent().GetParent().GetParent<Control>();
        group.TooltipText = tip;
        if (_weatherIcon is not null && icon != _weatherIconName) { _weatherIcon.Texture = Ui.Icon(icon); _weatherIconName = icon; }
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
        _clockTrack!.CustomMinimumSize=new Vector2(Ui.S(session.IsClassicCampaign?192:240),Ui.S(12));
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
        // The festival day (eight festival hours on one stage, longer with the Pond Stage), at 80 ticks a minute; past that, people are only leaving. Its length is the session's.
        var dayTicks = session.PreparedEditionDurationTicks;
        _clock!.Text = preparing ? "Not started" : session.CurrentTick - p.StartedTick >= dayTicks ? "Closing time" : $"{FestivalClockText(session.CurrentTick - p.StartedTick)} / {FestivalDurationText(dayTicks)}";
        var programme = session.CaptureProgramme();
        _clockTrack!.Visible = !preparing && programme is not null;
        if (_clockTrack.Visible)
        {
            float day = dayTicks;
            _clockFraction = Math.Clamp((session.CurrentTick - p.StartedTick) / day, 0, 1);
            var acts = session.GetFestivalActs().ToDictionary(act => act.Id);
            for (var i = 0; i < 3; i++)
                _clockSets[i] = (GameSession.FestivalSlotStarts[i] / day, GameSession.FestivalSlotEnds[i] / day,
                    SetColour(acts.TryGetValue(programme!.ActIds[i], out var act) ? act.Genre : -1));
            _pondClockSets = null;
            if (session.Stages.Count > 1 && session.CaptureProgramme(session.Stages[1].Id) is { ActIds.Length: 3 } pond)
            {
                var stage = session.Stages[1];
                _pondClockSets = Enumerable.Range(0, 3).Select(i => (stage.SlotStarts[i] / day, stage.SlotEnds[i] / day,
                    SetColour(acts.TryGetValue(pond.ActIds[i], out var act) ? act.Genre : -1))).ToArray();
            }
            _clockTrack.QueueRedraw();
        }
        _guests!.Text = session.OnSiteAttendeeCount.ToString();
        _guestsOf!.Text = $"/ {p.People.Count(person => person.Role == ProtectedPersonRole.Guest)}";
        RefreshWeather(session, p);
        var admitted = p.People.Where(person => person.Role == ProtectedPersonRole.Guest && person.Admitted).ToArray();
        var mood = admitted.Length == 0 ? (int?)null : (int)admitted.Average(person => person.Satisfaction) / 100;
        _mood!.Text = mood is { } percent ? $"{percent}%" : "–";
        _moodBar!.Value = mood ?? 0;
        // The newspaper's bands: below 40% is a one- or two-star day, 60% and up four or five.
        _moodBar.AddThemeStyleboxOverride("fill", Ui.Box(mood < 40 ? new Color("df5750") : mood < 60 ? Ui.Warn : new Color("53bb72"), 4));
        var moodGroup = _mood.GetParent().GetParent().GetParent().GetParent<Control>();
        moodGroup.TooltipText = "The crowd's average satisfaction, which the newspaper's star rating is based on." +
            (admitted.Length == 0 ? "" : MoodBreakdown(session.RecentMoodChanges(), admitted.Length));
        // Nobody on site (or not yet): no direction to show, whatever the last guests left behind.
        if (admitted.Length == 0 || preparing) _moodTrend!.Visible = false;
        // Nothing to show before anyone's through the gate.
        moodGroup.Modulate = preparing ? new Color(1, 1, 1, 0) : Colors.White;
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
        if(session.IsClassicCampaign)_phase.Text=$"CLASSIC {session.FestivalNumber} · "+(preparing?"PREPARATION":p.Status==PreparationStatus.Departing?"DEPARTING":"LIVE");
        _alertBadge!.Text = alertCount.ToString();
        _alertBadge.Visible = alertCount > 0;
        _alerts!.TooltipText = alertCount == 0 ? "No urgent alerts" : $"{alertCount} urgent alert{(alertCount == 1 ? "" : "s")} · show them";
    }

}

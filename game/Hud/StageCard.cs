using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The live Trailer Stage card: the act on stage (or next up) with its genre, set and slot times, a
/// countdown to its start or end, and the three sets as pills. It collapses to a small pill.
/// </summary>
internal sealed class StageCard(IHudHost _hud, Action _toggle)
{
    private PanelContainer? _card;
    private Button? _reopen;
    private Label? _state;
    private Label? _name;
    private Label? _detail;
    private Label? _countdownCaption;
    private Label? _countdown;
    private readonly Label[] _sets = new Label[3];

    public Control? Panel => _card;
    /// <summary>The older multi-line stage summary, kept as the card's tooltip.</summary>
    public Label? Summary { get; private set; }
    public float Bottom => _card is { Visible: true } ? _card.Position.Y + _card.Size.Y : _reopen?.Position.Y + _reopen?.Size.Y ?? Ui.ContentTop;

    public void Build(CanvasLayer layer, Vector2 size)
    {
        _card = new PanelContainer { Position = new Vector2(size.X - Ui.Gutter - Ui.S(300), Ui.S(76)), Size = new Vector2(Ui.S(300), 0), Theme = HudTheme() };
        _card.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Paper, 8, shadow: 10, shadowAlpha: 0.35f));
        layer.AddChild(_card);
        var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 0); _card.AddChild(stack);
        var head = new PanelContainer(); head.AddThemeStyleboxOverride("panel", HeaderStyle()); stack.AddChild(head);
        var headLine = new HBoxContainer(); head.AddChild(headLine);
        var title = Ui.Caps("Trailer Stage", Ui.BarText); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; title.VerticalAlignment = VerticalAlignment.Center; headLine.AddChild(title);
        _state = Ui.Caps("", Ui.Gold); _state.VerticalAlignment = VerticalAlignment.Center; headLine.AddChild(_state);
        var collapse = new Button { Icon = Ui.Icon("chevron-up"), ExpandIcon = true, IconAlignment = HorizontalAlignment.Center, Flat = true,
            CustomMinimumSize = Ui.S(22, 22), TooltipText = "Hide the stage card", MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        foreach (var state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" }) collapse.AddThemeColorOverride(state, Ui.BarMuted);
        collapse.Pressed += _toggle; headLine.AddChild(collapse);
        var body = new MarginContainer();
        foreach (var (side, value) in new[] { ("margin_left", 14f), ("margin_right", 14f), ("margin_top", 12f), ("margin_bottom", 14f) })
            body.AddThemeConstantOverride(side, Ui.Px(value));
        stack.AddChild(body);
        var content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 0); body.AddChild(content);
        var top = new HBoxContainer(); content.AddChild(top);
        var words = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; words.AddThemeConstantOverride("separation", Ui.Px(3)); top.AddChild(words);
        _name = Ui.Heading("", 23); _name.ClipText = true; words.AddChild(_name);
        _detail = Ui.Text("", 13, Ui.InkMuted); words.AddChild(_detail);
        var clock = new VBoxContainer(); clock.AddThemeConstantOverride("separation", 0); top.AddChild(clock);
        _countdownCaption = Ui.Caps("", Ui.InkMuted, 9.5f); _countdownCaption.HorizontalAlignment = HorizontalAlignment.Right; clock.AddChild(_countdownCaption);
        _countdown = Ui.Heading("", 24, Ui.Link); _countdown.HorizontalAlignment = HorizontalAlignment.Right; clock.AddChild(_countdown);
        content.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(12)) });
        var pills = new HBoxContainer(); pills.AddThemeConstantOverride("separation", Ui.Px(6)); content.AddChild(pills);
        for (var i = 0; i < 3; i++)
        {
            _sets[i] = Ui.Text($"Set {i + 1}", 12, Ui.Ink, Ui.BodyBold);
            _sets[i].HorizontalAlignment = HorizontalAlignment.Center; _sets[i].VerticalAlignment = VerticalAlignment.Center;
            _sets[i].CustomMinimumSize = new Vector2(0, Ui.S(26)); _sets[i].SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            pills.AddChild(_sets[i]);
        }
        Summary = new Label { Visible = false }; content.AddChild(Summary);

        _reopen = Ui.IconButton("Trailer Stage", "chevron-down", Ui.ButtonKind.Bar, _toggle, 13);
        _reopen.Position = new Vector2(size.X - Ui.Gutter - Ui.S(150), Ui.S(76)); _reopen.Size = new Vector2(Ui.S(150), Ui.S(34));
        _reopen.TooltipText = "Show the stage card"; _reopen.Visible = false;
        layer.AddChild(_reopen);
    }

    private static StyleBoxFlat HeaderStyle()
    {
        var box = Ui.Box(Ui.Bar, 0, padX: 14, padY: 6);
        box.CornerRadiusTopLeft = box.CornerRadiusTopRight = Ui.Px(8);
        return box;
    }

    /// <summary>A genre's set pill colours: the wash and the ink on it.</summary>
    private static (Color Wash, Color Ink) Pill(int genre) => genre switch
    {
        0 => (new Color("cfddbf"), new Color("3e5a33")), 1 => (new Color("edc6ae"), new Color("5a3a2a")),
        2 => (new Color("f1d48e"), new Color("5c4410")), 3 => (new Color("c9dcea"), new Color("2c4f6b")),
        4 => (new Color("f1c9d8"), new Color("7a1f42")), 5 => (new Color("d2d1d6"), new Color("2a2930")),
        _ => (Ui.PaperRule, Ui.InkMuted),
    };

    public void Refresh(bool open)
    {
        if (_card is null) return;
        var session = _hud.Session;
        var live = session.CaptureLivePerformance();
        var shown = session.PreparedStatus is not PreparationStatus.Preparing && live is not null;
        _card.Visible = shown && open;
        _reopen!.Visible = shown && !open;
        if (!_card.Visible) return;
        _card.TooltipText = Summary!.Text;
        var programme = session.CaptureProgramme();
        var acts = session.GetFestivalActs().ToDictionary(act => act.Id);
        var onStage = live!.Stage is LiveSetStage.Live or LiveSetStage.Interrupted;
        var act = onStage ? session.CurrentFestivalAct : session.UpcomingFestivalAct ?? session.CurrentFestivalAct;
        var slot = programme is null || act is null ? -1 : Array.IndexOf(programme.ActIds, act.Id);
        var finished = session.PreparedStatus is PreparationStatus.Departing or PreparationStatus.Finished;
        _state!.Text = (finished ? "Finished" : session.LateReadyFestivalAct is not null ? "Late" : onStage ? live.Stage == LiveSetStage.Interrupted ? "Interrupted" : "On stage" : "Next up").ToUpperInvariant();
        _state.AddThemeColorOverride("font_color", onStage ? Ui.Warn : Ui.Gold);
        _name!.Text = finished ? "That's the festival" : act?.Name ?? "Awaiting booking";
        _detail!.Text = finished ? "Guests are heading home" : act is null ? "" :
            $"{FestivalGenreName(act.Genre)} · Set {slot + 1}" + (slot >= 0 ? $" · {FestivalClockText(GameSession.FestivalSlotStarts[slot])}–{FestivalClockText(GameSession.FestivalSlotEnds[slot])}" : "");
        long? until = finished ? null : onStage ? programme?.SlotEndTick - session.CurrentTick : session.UpcomingFestivalTick >= 0 ? session.UpcomingFestivalTick - session.CurrentTick : null;
        _countdownCaption!.Text = until is null ? "" : onStage ? "ENDS IN" : "STARTS IN";
        _countdown!.Text = until is { } ticks ? $"{Math.Max(0, ticks) / 80 / 60}:{Math.Max(0, ticks) / 80 % 60:00}" : "";
        for (var i = 0; i < 3; i++)
        {
            var genre = programme is not null && acts.TryGetValue(programme.ActIds[i], out var booked) ? booked.Genre : -1;
            var (wash, ink) = Pill(genre);
            var current = i == slot && !finished;
            var box = Ui.Box(wash, 5);
            if (current) { box.BorderColor = ink; box.SetBorderWidthAll(Ui.Px(2)); }
            _sets[i].AddThemeStyleboxOverride("normal", box);
            _sets[i].AddThemeColorOverride("font_color", ink);
            _sets[i].TooltipText = programme is not null && acts.TryGetValue(programme.ActIds[i], out var named) ? $"Set {i + 1} · {named.Name}" : $"Set {i + 1}";
        }
    }
}

using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The live Trailer Stage card: the act on stage (or next up) with its genre, set and slot times, a
/// countdown to its start or end, and the three sets as pills. It collapses to a small pill. With a second
/// stage open it lists both stages in compact rows instead: each act and its state, the countdown, the crowd,
/// how the band and sound are doing, and the Pond Stage's generator.
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
    private HBoxContainer? _play;
    private Label? _bandWord, _soundWord;
    private Label? _title;
    private Control? _single;
    private VBoxContainer? _multi;
    private sealed record StageRow(string StageId, Label State, Label Act, Label Countdown, Label Detail, Label Play);
    private readonly System.Collections.Generic.List<StageRow> _rows = [];

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
        var title = _title = Ui.Caps("Trailer Stage", Ui.BarText); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; title.VerticalAlignment = VerticalAlignment.Center; headLine.AddChild(title);
        _state = Ui.Caps("", Ui.Gold); _state.VerticalAlignment = VerticalAlignment.Center; headLine.AddChild(_state);
        var collapse = new Button { Icon = Ui.Icon("chevron-up"), ExpandIcon = true, IconAlignment = HorizontalAlignment.Center, Flat = true,
            CustomMinimumSize = Ui.S(22, 22), TooltipText = "Hide the stage card", MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        foreach (var state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" }) collapse.AddThemeColorOverride(state, Ui.BarMuted);
        collapse.Pressed += _toggle; headLine.AddChild(collapse);
        var body = new MarginContainer();
        foreach (var (side, value) in new[] { ("margin_left", 14f), ("margin_right", 14f), ("margin_top", 12f), ("margin_bottom", 14f) })
            body.AddThemeConstantOverride(side, Ui.Px(value));
        stack.AddChild(body);
        _single = body;
        BuildRows(stack);
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
        // How the set is going: the band and the sound, so a poor set says which is to blame.
        _play = new HBoxContainer { Visible = false }; _play.AddThemeConstantOverride("separation", Ui.Px(14));
        content.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(8)) }); content.AddChild(_play);
        Label Reading(string caption)
        {
            var pair = new HBoxContainer(); pair.AddThemeConstantOverride("separation", Ui.Px(6)); _play.AddChild(pair);
            var label = Ui.Caps(caption, Ui.InkMuted, 9.5f); label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; pair.AddChild(label);
            var word = Ui.Text("", 13.5f, Ui.Ink, Ui.BodyBold); pair.AddChild(word);
            return word;
        }
        _bandWord = Reading("Band"); _soundWord = Reading("Sound");
        Summary = new Label { Visible = false }; content.AddChild(Summary);

        _reopen = Ui.IconButton("Trailer Stage", "chevron-down", Ui.ButtonKind.Bar, _toggle, 13);
        _reopen.Position = new Vector2(size.X - Ui.Gutter - Ui.S(150), Ui.S(76)); _reopen.Size = new Vector2(Ui.S(150), Ui.S(34));
        _reopen.TooltipText = "Show the stage card"; _reopen.Visible = false;
        layer.AddChild(_reopen);
    }

    /// <summary>One compact row per stage, for a festival with two stages.</summary>
    private void BuildRows(VBoxContainer stack)
    {
        var margin = new MarginContainer { Visible = false };
        foreach (var (side, value) in new[] { ("margin_left", 12f), ("margin_right", 12f), ("margin_top", 8f), ("margin_bottom", 10f) })
            margin.AddThemeConstantOverride(side, Ui.Px(value));
        stack.AddChild(margin);
        _multi = new VBoxContainer(); _multi.AddThemeConstantOverride("separation", Ui.Px(2)); margin.AddChild(_multi);
        foreach (var stage in FestivalStages.All)
        {
            if (_rows.Count > 0) _multi.AddChild(new ColorRect { Color = Ui.PaperRule, CustomMinimumSize = new Vector2(0, 1) });
            var row = new VBoxContainer(); row.AddThemeConstantOverride("separation", 0); _multi.AddChild(row);
            var top = new HBoxContainer(); row.AddChild(top);
            var name = Ui.Caps(Main.StageTitle(stage), Ui.InkMuted, 9.5f); name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; top.AddChild(name);
            var state = Ui.Caps("", Ui.Gold, 9.5f); top.AddChild(state);
            var middle = new HBoxContainer(); row.AddChild(middle);
            var act = Ui.Heading("", 17); act.ClipText = true; act.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            act.CustomMinimumSize = new Vector2(1, 0); act.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; middle.AddChild(act);
            var countdown = Ui.Heading("", 17, Ui.Link); countdown.HorizontalAlignment = HorizontalAlignment.Right; middle.AddChild(countdown);
            var detail = Ui.Text("", 12, Ui.InkMuted); row.AddChild(detail);
            var play = Ui.Text("", 12, Ui.Ink, Ui.BodyBold); play.MouseFilter = Control.MouseFilterEnum.Pass; row.AddChild(play);
            _rows.Add(new StageRow(stage.Id, state, act, countdown, detail, play));
        }
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
        _reopen.Text = session.Stages.Count > 1 ? "Stages" : "Trailer Stage";
        if (!_card.Visible) return;
        _card.TooltipText = Summary!.Text;
        var multi = session.Stages.Count > 1;
        _single!.Visible = !multi;
        _multi!.GetParent<Control>().Visible = multi;
        _title!.Text = multi ? "Stages" : "Trailer Stage";
        if (multi) { RefreshRows(session); return; }
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
        var performance = onStage && session.PowerBudgetActive ? session.CurrentPerformance : null;
        _play!.Visible = performance is not null;
        if (performance is not null)
        {
            static Color Ink(int score) => score < 40 ? Ui.Alert : score < 60 ? Ui.Link : Ui.TealDeep;
            _bandWord!.Text = performance.BandWord; _bandWord.AddThemeColorOverride("font_color", Ink(performance.Band));
            _soundWord!.Text = performance.SoundWord; _soundWord.AddThemeColorOverride("font_color", Ink(performance.Sound));
            _play.TooltipText = $"Band {performance.Band}/100 (talent {performance.Talent}" + (performance.Drunkenness > 0 ? $", less {performance.Drunkenness} for drink on stage" : "") +
                $")\nSound {performance.Sound}/100 ({PowerRules.RigName(session.Rig)}, the engineer's mixing, the generator)";
            _play.MouseFilter = Control.MouseFilterEnum.Pass;
        }
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

    private static Color Ink(int score) => score < 40 ? Ui.Alert : score < 60 ? Ui.Link : Ui.TealDeep;

    /// <summary>Each stage's row: what's on or next, its countdown, its crowd, and how it's playing.</summary>
    private void RefreshRows(GameSession session)
    {
        var finishedDay = session.PreparedStatus is PreparationStatus.Departing or PreparationStatus.Finished;
        var anyOn = false;
        foreach (var row in _rows)
        {
            var id = row.StageId;
            var stage = FestivalStages.Find(id)!;
            var live = session.CaptureLivePerformance(id);
            var programme = session.CaptureProgramme(id);
            var onStage = live?.Stage is LiveSetStage.Live or LiveSetStage.Interrupted;
            anyOn |= onStage;
            var act = onStage ? session.CurrentStageAct(id) : session.UpcomingStageAct(id) ?? session.CurrentStageAct(id);
            var slot = programme is null || act is null ? -1 : Array.IndexOf(programme.ActIds, act.Id);
            var next = session.UpcomingStageTick(id);
            var done = finishedDay || !onStage && next < 0 && live?.Stage == LiveSetStage.Finished;
            row.State.Text = (done ? "Finished" : session.StageBandLate(id) ? "Late" : onStage ? live!.Stage == LiveSetStage.Interrupted ? "Interrupted" : "On stage" : "Next up").ToUpperInvariant();
            row.State.AddThemeColorOverride("font_color", onStage ? Ui.Warn : Ui.Gold);
            row.Act.Text = done ? "Sets over" : act?.Name ?? "Awaiting booking";
            long? until = done ? null : onStage ? programme?.SlotEndTick - session.CurrentTick : next >= 0 ? next - session.CurrentTick : null;
            row.Countdown.Text = until is { } ticks ? (onStage ? "ends " : "in ") + $"{Math.Max(0, ticks) / 80 / 60}:{Math.Max(0, ticks) / 80 % 60:00}" : "";
            var crowd = live?.Listeners.Count(item => item.AtPlace) ?? 0;
            row.Detail.Text = done ? $"{crowd} still about" : act is null ? "" :
                $"{FestivalGenreName(act.Genre)} · Set {slot + 1}" + (slot >= 0 ? $" · {FestivalClockText(stage.SlotStarts[slot])}–{FestivalClockText(stage.SlotEnds[slot])}" : "") + $" · crowd {crowd}";
            var performance = onStage && session.PowerBudgetActive ? session.CurrentStagePerformance(id) : null;
            var sound = session.SoundScoreAt(id);
            var generator = session.CaptureStageGenerator(id);
            var power = generator?.Stage switch
            {
                EquipmentStage.Warning => "generator straining", EquipmentStage.DangerousFault => "generator fault",
                EquipmentStage.Isolated or EquipmentStage.Terminal => "power cut", null => "", _ => "generator OK",
            };
            row.Play.Text = (performance is not null ? $"Band {performance.BandWord} · Sound {performance.SoundWord} {performance.Sound}" : $"Sound {PerformanceRules.SoundWord(sound)} {sound}") +
                (power == "" ? "" : $" · {power}");
            row.Play.AddThemeColorOverride("font_color", generator?.Stage is EquipmentStage.Warning or EquipmentStage.DangerousFault or EquipmentStage.Isolated ? Ui.Alert :
                Ink(Math.Min(performance?.Band ?? 100, performance?.Sound ?? sound)));
            row.Play.TooltipText = performance is null ? $"Sound {sound}/100 ({PowerRules.RigName(session.Rig)}, this stage's engineer, its generator)" :
                $"Band {performance.Band}/100 (talent {performance.Talent}" + (performance.Drunkenness > 0 ? $", less {performance.Drunkenness} for drink on stage" : "") +
                $")\nSound {performance.Sound}/100 ({PowerRules.RigName(session.Rig)}, this stage's engineer, its generator)";
        }
        _state!.Text = (finishedDay ? "Finished" : anyOn ? "Live" : "").ToUpperInvariant();
        _state.AddThemeColorOverride("font_color", anyOn ? Ui.Warn : Ui.Gold);
    }
}

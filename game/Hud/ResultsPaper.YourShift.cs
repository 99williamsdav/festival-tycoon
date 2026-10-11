using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// All-Rounder's "Your shift" tab, to the designer's spec (assets/source/ui/hands-on-v1/INTEGRATION.md §2A): the
/// title and rule, four stat cards (calls, fights, mood you added, Control's grade), the calls on the night with who
/// went, the press line, and Control's mini clipboard with the grade stamp. The tab carries an unseen dot until it's
/// opened, and again for a new grade. Management Only has no tab; Steward Only keeps its own clipboard report.
/// </summary>
internal sealed partial class ResultsPaper
{
    private static readonly Color CardFill = new("fffdf6");
    private static readonly Color QuoteFill = new("efe6d2");
    /// <summary>The last shift whose tab was opened, so its dot stays off until a new grade comes in.</summary>
    private string? _seenShift;
    private bool _stampThumped;

    /// <summary>The finished hands-on day's shift report; null in a classic campaign or before the day.</summary>
    private StewardShiftReport? YourShiftReport() => _hud.Session.IsHandsOn ? _hud.Session.CaptureStewardShiftReport() : null;

    private string ShiftKey(StewardShiftReport report)
    {
        var session = _hud.Session;
        return $"{session.CampaignId.Value}:{session.CapturePreparation()?.Tier}:{session.CompletedFestivalResult?.Attempt}:{report.Grade}";
    }

    private void AddYourShiftTab(Container tabs, StewardShiftReport report)
    {
        _stampThumped = false;
        _yourShiftTab = Tab("Your shift", "radio", () => { _seenShift = ShiftKey(report); if (_yourShiftDot is not null) _yourShiftDot.Visible = false; ShowPage(Page.YourShift); });
        _yourShiftTab.Name = "YourShiftTab";
        _yourShiftTab.TooltipText = "How you did on the radio: your calls, Control's grade, and what the paper made of it";
        tabs.AddChild(_yourShiftTab);
        // The unseen dot: 12 across at (right − 20, top + 8), alert orange with a paper-coloured ring.
        var tab = _yourShiftTab;
        var dot = new Control { Name = "UnseenDot", Size = Ui.S(12, 12), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = _seenShift != ShiftKey(report) };
        dot.Draw += () =>
        {
            dot.DrawCircle(dot.Size / 2, Ui.S(6), Ui.Alert);
            dot.DrawArc(dot.Size / 2, Ui.S(6) - Ui.S(1), 0, Mathf.Tau, 24, Programme, Math.Max(1, Ui.S(2)), true);
        };
        void Place() => dot.Position = new Vector2(tab.Size.X - Ui.S(20), Ui.S(8));
        tab.Resized += Place;
        tab.AddChild(dot);
        Place();
        _yourShiftDot = dot;
    }

    private static string CallNoun(StewardCallKind kind) => kind switch
    {
        StewardCallKind.Fight => "Fight", StewardCallKind.Argument => "Argument", StewardCallKind.Collapse => "Collapse",
        StewardCallKind.StuckInLoo => "Stuck in a loo", StewardCallKind.BrokenTap => "Broken tap", StewardCallKind.FullBin => "Full bin", _ => "Loose cow",
    };

    /// <summary>Who went, as the list says it: the label, its icon and colour.</summary>
    private static (string Label, string Icon, Color Colour) WhoWent(StewardNightCall call) => call.Who switch
    {
        StewardCallResponder.You => ("you went", "user", Ui.Teal),
        StewardCallResponder.Autopilot => ("you (autopilot)", "user", Ui.Teal),
        StewardCallResponder.Medic => ("sent the medic", "heart-pulse", Ui.Teal),
        StewardCallResponder.Steward => ($"sent {FirstName(call.Crew, "a steward")}", "shield", Ui.Teal),
        StewardCallResponder.Maintenance => ($"sent {FirstName(call.Crew, "maintenance")}", "wrench", Ui.Teal),
        _ => ("missed", "x", Ui.Alert),
    };

    private static string FirstName(string? name, string otherwise) => name is { Length: > 0 } ? name.Split(' ')[0] : otherwise;

    private static string PaperSource => $"{FestivalReview.Masthead}, page 2";

    /// <summary>The newspaper's nod to page 2: the press line about the organiser.</summary>
    private Control PageTwo(StewardShiftReport report)
    {
        var box = new PanelContainer();
        var style = Ui.Box(QuoteFill, 0, padX: 12, padY: 8); style.BorderColor = Ui.Ink; style.BorderWidthLeft = Ui.Px(4); style.ContentMarginLeft = Ui.S(16);
        box.AddThemeStyleboxOverride("panel", style);
        var words = new VBoxContainer(); words.AddThemeConstantOverride("separation", Ui.Px(2)); box.AddChild(words);
        words.AddChild(Ui.Caps("Page 2 · Organiser on the beat", Ui.InkMuted, 9.5f));
        words.AddChild(Paragraph($"“{StewardShiftPress.Quote(report, _hud.Session.StewardShiftNight())}”", 15, Ui.Ink, Ui.Slab));
        return box;
    }

    private void RenderYourShift(VBoxContainer body)
    {
        var session = _hud.Session;
        var report = YourShiftReport()!;
        var night = session.StewardShiftNight();
        var p = session.CapturePreparation()!;
        var shift = session.CaptureStewardShift();
        // The page is laid out in screen mockup units; its origin is the sheet's content corner at (260, 52).
        // Down to the clipboard's foot at 680; the sheet keeps only a slim bottom margin on this page (ShowPage), so it fits without scrolling.
        var page = new Control { Name = "YourShiftPage", CustomMinimumSize = Ui.S(756, 628), MouseFilter = Control.MouseFilterEnum.Ignore };
        body.AddChild(page);
        Vector2 At(float x, float y) => Ui.S(x - 260, y - 52);
        Label Put(Control parent, string text, float size, Color colour, Font font, Vector2 at, float width = 0, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            var label = Ui.Text(text, size, colour, font);
            label.MouseFilter = Control.MouseFilterEnum.Ignore; label.HorizontalAlignment = align; label.Position = at;
            if (width > 0) { label.Size = new Vector2(width, 0); label.ClipText = true; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; }
            parent.AddChild(label);
            return label;
        }

        // Title, subtitle and rule.
        Put(page, "Your shift", 30, Ui.Ink, Ui.Slab, At(260, 52) - new Vector2(0, Ui.S(6)));
        var ended = (shift?.EndedTick ?? session.CurrentTick) - p.StartedTick;
        Put(page, $"You on the radio · organiser and steward · {FestivalClockText(0)}–{FestivalClockText(ended)}", 13, Ui.InkMuted, Ui.Body, At(260, 92) - new Vector2(0, Ui.S(3)));
        page.AddChild(new ColorRect { Color = Ui.Ink, Position = At(260, 118), Size = new Vector2(Ui.S(756), Math.Max(1, Ui.S(2))), MouseFilter = Control.MouseFilterEnum.Ignore });

        // Four cards: calls, fights, mood you added, and Control's grade on the dark one.
        // Calls the crew covered and you left to them aren't yours (Score.Covered); the fights line adds the day's own count.
        var average = report.AverageResponseSeconds < 0 ? "no calls answered" : $"avg response {report.AverageResponseSeconds / 60}:{report.AverageResponseSeconds % 60:00}";
        if (report.Score.Covered > 0) average += $" · crew took {report.Score.Covered}";
        var yours = report.FightsPrevented + report.FightsBrokenUp;
        var cards = new (string Caption, string Value, string Line, bool Dark)[]
        {
            ("Calls answered", $"{report.Answered} of {report.Calls}", average, false),
            ("Fights", $"{report.FightsPrevented} · {report.FightsBrokenUp}", report.DayFights > yours ? $"prevented · broken up · {report.DayFights} in all" : "prevented · broken up", false),
            ("Mood you added", $"+{report.MoodAdded / 100}", $"{report.MoodFromPercent}% → {report.CrowdMoodPercent}% while on shift", false),
            ("Control's grade", StewardShiftGrading.Name(report.Grade), $"{(int)report.Grade + 1} of 5 · litter {report.LitterPicked}", true),
        };
        for (var i = 0; i < cards.Length; i++)
        {
            var (caption, value, line, dark) = cards[i];
            var card = new Panel { Position = At(260 + i * 192, 134), Size = Ui.S(180, 84), MouseFilter = Control.MouseFilterEnum.Ignore };
            card.AddThemeStyleboxOverride("panel", dark ? Ui.Box(Ui.Ink, 6) : Ui.Box(CardFill, 6, Ui.PaperRule, 1));
            page.AddChild(card);
            Put(card, caption.ToUpperInvariant(), 9.5f, dark ? Ui.Gold : Ui.InkMuted, Ui.CapsFont, Ui.S(12, 10));
            // A long grade name ("Crowd-pleaser") steps down a size or two rather than trailing off.
            var fit = 26f;
            while (fit > 18 && Ui.Slab.GetStringSize(value, HorizontalAlignment.Left, -1, Ui.Px(fit)).X > Ui.S(158)) fit -= 1;
            Put(card, value, fit, dark ? Ui.BarText : Ui.Teal, Ui.Slab, Ui.S(12, 20) + new Vector2(0, Ui.S((26 - fit) / 2)), Ui.S(160));
            Put(card, line, 11.5f, dark ? Ui.BarMuted : Ui.InkMuted, Ui.Body, Ui.S(12, 60), Ui.S(160));
        }

        // The calls on the night: five rows fit above the press line.
        var shown = StewardShiftPress.Highlights(night, 5);
        Put(page, "Calls on the night", 19, Ui.Ink, Ui.Slab, At(260, 240) - new Vector2(0, Ui.S(4)));
        if (shown.Count < night.Count) Put(page, $"{shown.Count} of {night.Count} shown", 11.5f, Ui.InkMuted, Ui.Body, At(440, 244), Ui.S(200), HorizontalAlignment.Right);
        if (night.Count == 0) Put(page, "No calls came in while you were on.", 13.5f, Ui.InkMuted, Ui.Body, At(260, 272));
        for (var i = 0; i < shown.Count; i++)
        {
            var call = shown[i];
            var y = 272 + 30 * i;
            var (who, icon, colour) = WhoWent(call);
            var whoWidth = Ui.BodyBold.GetStringSize(who, HorizontalAlignment.Left, -1, Ui.Px(12)).X;
            Put(page, FestivalClockText(call.Tick - p.StartedTick), 13, Ui.InkMuted, Ui.BodyBold, At(260, y) - new Vector2(0, Ui.S(2)));
            Put(page, $"{CallNoun(call.Kind)} by {call.Where}", 13.5f, Ui.Ink, Ui.Body, At(310, y) - new Vector2(0, Ui.S(3)), Ui.S(330) - whoWidth - Ui.S(28));
            Put(page, who, 12, colour, Ui.BodyBold, At(440, y) - new Vector2(0, Ui.S(1)), Ui.S(200), HorizontalAlignment.Right);
            var mark = Ui.IconRect(icon, 14, colour); mark.Position = At(640, y + 1) - new Vector2(whoWidth + Ui.S(20), 0); mark.Size = Ui.S(14, 14); page.AddChild(mark);
            page.AddChild(new ColorRect { Color = Ui.PaperRule, Position = At(260, y + 24), Size = new Vector2(Ui.S(380), 1), MouseFilter = Control.MouseFilterEnum.Ignore });
        }

        // The press line: IN THE PAPER, the quote, the source; the block grows if a long line wraps.
        var quote = new PanelContainer { Position = At(260, 440), CustomMinimumSize = Ui.S(380, 96), Size = Ui.S(380, 96), MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = Ui.Box(QuoteFill, 0, padX: 18, padY: 10); style.BorderColor = Ui.Ink; style.BorderWidthLeft = Ui.Px(4);
        quote.AddThemeStyleboxOverride("panel", style);
        page.AddChild(quote);
        var words = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; words.AddThemeConstantOverride("separation", Ui.Px(4)); quote.AddChild(words);
        words.AddChild(Ui.Caps("In the paper", Ui.InkMuted, 9.5f));
        var said = Paragraph($"“{StewardShiftPress.Quote(report, night)}”", 19, Ui.Ink, Ui.Slab); said.Name = "PressQuote"; words.AddChild(said);
        words.AddChild(Ui.Text(PaperSource, 11.5f, Ui.InkMuted));

        // Control's mini clipboard (2× art at half its pixels), then the grade's stamp at 0.4, turned −14°.
        page.AddChild(new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/ui/handson/lwf_results_mini_clipboard_v1.png"), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, Position = At(684, 240), Size = Ui.S(332, 440), MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        var stamp = new TextureRect
        {
            Name = "GradeStamp", Texture = GD.Load<Texture2D>($"res://assets/ui/steward/lwf_report_stamp_{Main.StampFile(report.Grade)}_v1.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, Size = Ui.S(300, 120),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        stamp.PivotOffset = stamp.Size / 2;
        stamp.Position = At(885, 420) - stamp.Size / 2;
        stamp.RotationDegrees = -14;
        stamp.Scale = Vector2.One * .4f;
        page.AddChild(stamp);
        // The first time the page opens on this paper the stamp is thumped down, as on the shift report.
        if (!_stampThumped)
        {
            _stampThumped = true;
            stamp.Scale = Vector2.One * .64f; stamp.Modulate = new Color(1, 1, 1, 0);
            var tween = stamp.CreateTween();
            tween.TweenInterval(.35f);
            tween.TweenProperty(stamp, "scale", Vector2.One * .4f, .18f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
            tween.Parallel().TweenProperty(stamp, "modulate:a", 1f, .18f);
        }
        GD.Print($"RESULTS_YOUR_SHIFT grade={StewardShiftGrading.Name(report.Grade)} calls={report.Answered}/{report.Calls} night={night.Count} quote=\"{StewardShiftPress.Quote(report, night)}\"");
    }
}

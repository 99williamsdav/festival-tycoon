using Festival.Simulation;
using Godot;
using System;

namespace Festival.Game;

/// <summary>
/// The end of a shift: Control's clipboard shift sheet over the blurred field, to the designer's spec
/// (assets/source/ui/steward-report-v1/INTEGRATION.md): the board, the sheet rotated a touch, the clip, six stats, the
/// fights split bar, the grade pills, Control's note, and the grade's rubber stamp thumped down across the corner.
/// After a death the same sheet says the day was stopped. Next shift starts another at the same tier.
/// </summary>
public partial class Main
{
    private const string BackdropBlur = @"shader_type canvas_item;
uniform sampler2D screen : hint_screen_texture, filter_linear_mipmap;
void fragment() { COLOR = vec4(mix(textureLod(screen, SCREEN_UV, 2.6).rgb, vec3(0.055, 0.122, 0.102), 0.55), 1.0); }";

    internal static string StampFile(StewardShiftGrade grade) => grade switch
    {
        StewardShiftGrade.Encore => "encore", StewardShiftGrade.Headliner => "headliner", StewardShiftGrade.CrowdPleaser => "crowd_pleaser",
        StewardShiftGrade.BSide => "b_side", _ => "off_key",
    };

    private static string ControlsNote(StewardShiftReport report) => report.Failed
        ? "We lost someone today. The day's stopped while it's looked into. Get some rest."
        : GradeNote(report) + (report.Score.StageInvasions > 0 ? " And stay off the stage, you're not on the bill." : "");

    private static string GradeNote(StewardShiftReport report) => report.Grade switch
        {
            StewardShiftGrade.Encore => "Textbook. First on every call and the crowd loved you for it. Same again next week?",
            StewardShiftGrade.Headliner => "Strong shift. You were where we needed you, most of the time.",
            StewardShiftGrade.CrowdPleaser => "Solid. A few calls slipped by, but nobody got hurt on your watch.",
            StewardShiftGrade.BSide => report.UrgentUnhandled > 0 ? "An urgent call went out and nobody got to it. Keep the radio close." : "Steady, but too many calls went to the crew.",
            _ => "Quiet on the radio from you today. Answer the calls.",
        };

    private void ShowStewardShiftReport()
    {
        if (_shiftHud is null || _session.CaptureStewardShiftReport() is not { } report) return;
        _shiftKeyDown = false;
        SetShiftMenu(null);
        _shiftLooking = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        foreach (var hidden in new Control?[] { _shiftCrosshair, _shiftKeys, _shiftCompass, _shiftClock, _shiftRadioStrip, _shiftNeeds }) if (hidden is not null) hidden.Visible = false;
        PushShiftRadio(report.Failed ? "All stewards stand down. The day's been stopped." : "That's closing. See them out safely.");

        var size = ShiftScreen;
        var root = new Control { Name = "ShiftReport", MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var backdrop = new ColorRect { Material = new ShaderMaterial { Shader = new Shader { Code = BackdropBlur } }, MouseFilter = Control.MouseFilterEnum.Ignore };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(backdrop);
        var header = new VBoxContainer { Position = Ui.S(36, 34) };
        header.AddThemeConstantOverride("separation", Ui.Px(2));
        header.AddChild(Ui.Caps(report.Failed ? "Shift stopped" : "End of shift", Ui.Gold, 10));
        header.AddChild(Ui.Text("Steward's report", 22, Ui.BarText, Ui.Slab));
        root.AddChild(header);

        // The board, its shadow, the sheet on it and the clip over the sheet's top.
        var board = new Control { Position = Ui.S(380, 18), Size = Ui.S(520, 694), MouseFilter = Control.MouseFilterEnum.Ignore };
        // Centred on screens that aren't 16:9.
        board.Position = new Vector2((size.X - board.Size.X) / 2, Ui.S(18));
        var shadow = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        shadow.Draw += () => shadow.DrawStyleBox(Ui.Box(new Color(0, 0, 0, .47f), 18, shadow: 8, shadowAlpha: .47f), new Rect2(Ui.S(4, 6), board.Size - Ui.S(4, 6)));
        board.AddChild(shadow);
        // The art is drawn at 2×: shown at half its pixels, in mockup units. Expand mode first, or the size can't shrink.
        TextureRect Art(string file, Vector2 at, Vector2 mockup) => new()
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            Texture = GD.Load<Texture2D>($"res://assets/ui/steward/{file}"), Position = at, Size = mockup, CustomMinimumSize = mockup,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        board.AddChild(Art("lwf_report_clipboard_v1.png", Vector2.Zero, Ui.S(520, 694)));
        var sheet = new Control { Position = Ui.S(22, 44), Size = Ui.S(476, 634), MouseFilter = Control.MouseFilterEnum.Ignore };
        sheet.PivotOffset = sheet.Size / 2;
        sheet.RotationDegrees = 1.2f;
        sheet.AddChild(Art("lwf_report_sheet_v1.png", Vector2.Zero, Ui.S(476, 634)));
        board.AddChild(sheet);
        board.AddChild(Art("lwf_report_clip_v1.png", Ui.S(185, 2), Ui.S(150, 70)));
        root.AddChild(board);

        Label Put(Control parent, string text, float px, Color colour, Font font, Vector2 at, HorizontalAlignment align = HorizontalAlignment.Left, float width = 0)
        {
            var label = Ui.Text(text, px, colour, font);
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            label.HorizontalAlignment = align;
            label.Position = at;
            if (width > 0) label.Size = new Vector2(width, 0);
            parent.AddChild(label);
            return label;
        }
        var tier = _session.CapturePreparation()!.Tier;
        var steward = _session.GetStewardResponses().Count > 1 ? _session.CapturePerson(_session.GetStewardResponses()[1].WorkerId)?.Name : null;
        Put(sheet, report.Failed ? "Shift stopped" : "Steward shift report", 28, Ui.Ink, Ui.Slab, Ui.S(24, 22));
        Put(sheet, $"{steward ?? "Steward 2"} · Tier {tier} · {(_session.IsRainyFestival ? "rain" : "heatwave")} · Lower Wittering", 12.5f, Ui.InkMuted, Ui.Body, Ui.S(24, 60));
        if (report.Deaths > 0) Put(sheet, report.Deaths == 1 ? "One death on site" : $"{report.Deaths} deaths on site", 12.5f, Ui.Alert, Ui.BodyBold, Ui.S(24, 74));
        var rules = new Control { Size = sheet.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        sheet.AddChild(rules);
        var stats = new (string Icon, string Label, string Value)[]
        {
            ("radio", "Calls answered", $"{report.Answered} of {report.Calls}"),
            ("clock", "Average response", report.AverageResponseSeconds < 0 ? "—" : $"{report.AverageResponseSeconds / 60}:{report.AverageResponseSeconds % 60:00}"),
            ("shield", "Fights prevented", report.FightsPrevented.ToString()),
            ("triangle-alert", "Fights broken up", report.FightsBrokenUp.ToString()),
            ("star", "Mood you added", $"+{report.MoodAdded / 100}"),
            ("users", "Crowd mood", $"{report.CrowdMoodPercent}%  (from {report.MoodFromPercent}%)"),
            ("trash-2", "Litter picked up", report.LitterPicked.ToString()),
        };
        // Seven rows fit between the title rule and the fights bar at a 38-unit pitch (six were 44).
        const int RowPitch = 38;
        for (var i = 0; i < stats.Length; i++)
        {
            var y = Ui.S(108 + RowPitch * i);
            var icon = Ui.IconRect(stats[i].Icon, 18, Ui.Teal); icon.Position = new Vector2(Ui.S(28), y + Ui.S(6)); sheet.AddChild(icon);
            Put(sheet, stats[i].Label, 15, Ui.Ink, Ui.Body, new Vector2(Ui.S(58), y + Ui.S(6)));
            Put(sheet, stats[i].Value, 20, Ui.Ink, Ui.Slab, new Vector2(Ui.S(200), y + Ui.S(1)), HorizontalAlignment.Right, Ui.S(248));
        }
        rules.Draw += () =>
        {
            rules.DrawRect(new Rect2(Ui.S(24), Ui.S(90), Ui.S(428), Ui.S(2)), Ui.Ink);
            for (var i = 0; i < stats.Length; i++) rules.DrawRect(new Rect2(Ui.S(24), Ui.S(108 + RowPitch * i + RowPitch - 6), Ui.S(428), 1), Ui.PaperRule);
            // The fights split bar.
            var bar = new Rect2(Ui.S(24), Ui.S(412), Ui.S(428), Ui.S(14));
            var total = report.FightsPrevented + report.FightsBrokenUp;
            rules.DrawStyleBox(Ui.Box(total == 0 ? Ui.PaperRule : Ui.AlertWash, 7), bar);
            if (report.FightsPrevented > 0)
                rules.DrawStyleBox(Ui.Box(Ui.Teal, 7), new Rect2(bar.Position, new Vector2(bar.Size.X * report.FightsPrevented / total, bar.Size.Y)));
        };
        Put(sheet, "Fights", 17, Ui.Ink, Ui.Slab, Ui.S(24, 380));
        if (report.FightsPrevented + report.FightsBrokenUp == 0)
            Put(sheet, "No fights on your shift. Unplugged!", 12, Ui.InkMuted, Ui.BodySemi, Ui.S(24, 430), HorizontalAlignment.Center, Ui.S(428));
        else
        {
            Put(sheet, $"{report.FightsPrevented} prevented before a punch", 12, Ui.Teal, Ui.BodySemi, Ui.S(24, 430));
            Put(sheet, $"{report.FightsBrokenUp} broken up", 12, Ui.Alert, Ui.BodySemi, Ui.S(24, 430), HorizontalAlignment.Right, Ui.S(428));
        }
        Put(sheet, "Grade", 17, Ui.Ink, Ui.Slab, Ui.S(24, 466));
        var pills = new HBoxContainer { Position = Ui.S(24, 496), MouseFilter = Control.MouseFilterEnum.Ignore };
        pills.AddThemeConstantOverride("separation", Ui.Px(6));
        foreach (var grade in new[] { StewardShiftGrade.Encore, StewardShiftGrade.Headliner, StewardShiftGrade.CrowdPleaser, StewardShiftGrade.BSide, StewardShiftGrade.OffKey })
        {
            var mine = grade == report.Grade;
            var pill = Ui.Text(StewardShiftGrading.Name(grade), 12.5f, mine ? Ui.BarText : Ui.InkMuted, Ui.BodyBold);
            pill.AddThemeStyleboxOverride("normal", mine ? Ui.Box(Ui.Ink, 12, padX: 9, padY: 2) : Ui.Box(new Color(0, 0, 0, 0), 12, Ui.PaperRule, 1, 9, 2));
            pills.AddChild(pill);
        }
        sheet.AddChild(pills);
        Put(sheet, "CONTROL'S NOTE", 10, Ui.InkMuted, Ui.CapsFont, Ui.S(24, 544));
        var note = Put(sheet, ControlsNote(report), 13.5f, Ui.Ink, Ui.Body, Ui.S(24, 560));
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart; note.Size = new Vector2(Ui.S(428), 0);
        Put(sheet, "~ Control", 16, Ui.InkMuted, Ui.Slab, Ui.S(24, 606), HorizontalAlignment.Right, Ui.S(428));

        // The stamp: 0.6 of its art, turned 14°, across the sheet's top-right corner; thumped down after the sheet arrives.
        var stamp = Art($"lwf_report_stamp_{StampFile(report.Grade)}_v1.png", Vector2.Zero, Ui.S(300, 120));
        stamp.PivotOffset = stamp.Size / 2;
        stamp.Position = Ui.S(408, 114) - stamp.Size / 2;
        stamp.RotationDegrees = -14;
        stamp.Scale = Vector2.One * 1.6f;
        stamp.Modulate = new Color(1, 1, 1, 0);
        board.AddChild(stamp);

        // Buttons: Next shift and Return to menu, the results paper's gold ones.
        Button Gold(string text, Vector2 at, Action pressed)
        {
            var button = Ui.Style(new Button { Text = text, Position = at, Size = Ui.S(188, 46), CustomMinimumSize = Ui.S(188, 46) }, Ui.ButtonKind.Primary, 15, 8);
            button.Pressed += pressed;
            root.AddChild(button);
            return button;
        }
        var right = size.X - Ui.S(1280 - 1076);
        Gold("Next shift", new Vector2(right, Ui.S(590)), () => { var next = tier; ExitStewardShiftView(); StartStewardShift(next); });
        Gold("Return to menu", new Vector2(right, Ui.S(646)), ReturnFromStewardShift);

        _shiftHud.AddChild(root);
        _shiftReport = root;
        // The sheet slides up; 0.6 s later the stamp lands with a little shake.
        var rest = board.Position;
        board.Position = rest + new Vector2(0, Ui.S(60));
        board.Modulate = new Color(1, 1, 1, 0);
        var tween = root.CreateTween();
        tween.TweenProperty(board, "position", rest, .35f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(board, "modulate:a", 1f, .25f);
        tween.TweenInterval(.6f);
        tween.TweenProperty(stamp, "scale", Vector2.One * .6f, .18f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
        tween.Parallel().TweenProperty(stamp, "modulate:a", 1f, .18f);
        tween.TweenProperty(board, "position", rest + new Vector2(Ui.S(2), Ui.S(1)), .04f);
        tween.TweenProperty(board, "position", rest + new Vector2(-Ui.S(2), -Ui.S(1)), .04f);
        tween.TweenProperty(board, "position", rest, .04f);
        GD.Print($"STEWARD_SHIFT_REPORT grade={StewardShiftGrading.Name(report.Grade)} points={report.Points} calls={report.Answered}/{report.Calls} failed={report.Failed}");
    }
}

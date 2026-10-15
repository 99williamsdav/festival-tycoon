using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The steward shift's HUD, to the designer's spec (assets/source/ui/steward-shift-hud-v1/INTEGRATION.md, picks A):
/// the cream hold-to-act card under the crosshair with its key-cap ring and result states; the slim compass strip
/// with landmark icons, the current call's pin (clamped to the edge, orange when urgent) and its distance chip; the
/// radio strip bottom left, alert orange with a response timer and an edge pulse for urgent calls; and the thirst and
/// heat bars above it, hidden while fine. Measurements are mockup units (Ui.S). Everything reads the saved shift.
/// </summary>
public partial class Main
{
    private CanvasLayer? _shiftHud;
    private Label? _shiftClock, _shiftKeys, _shiftMenuTitle;
    private Control? _shiftCrosshair, _shiftCompass, _shiftNeeds, _shiftCard, _shiftRadioStrip, _shiftVignette;
    private PanelContainer? _shiftMenu;
    private Button? _shiftResume;
    private Control? _shiftReport;
    private string _shiftRadioIntro = "";
    private long _shiftRadioIntroTick;
    private readonly Dictionary<string, string> _shiftCallPlaces = new(StringComparer.Ordinal);
    private (string Icon, float X, float Z)[] _shiftLandmarks = [];

    // The prompt card this frame.
    private enum CardState { Prompt, Holding, Success, Fail, Cancelled }
    private CardState _cardState;
    private string _cardTitle = "", _cardDetail = "", _cardIcon = "hand";
    private bool _cardKey;
    private float _cardRing, _cardAlpha, _cardRise;
    private long _shiftCancelledTick = -1;
    private StewardActionProgress? _shiftLastHeld;

    // The compass pin, gliding to its bearing; its chip's distance updated four times a second.
    private (float Bearing, string Place, bool Urgent)? _shiftPin;
    private float _shiftPinShown = float.NaN, _shiftPinMetres, _shiftPinMetresAge;

    // The radio strip.
    private (string Message, string Where, StewardCall? Call, long Tick) _shiftRadioNow;
    private long _shiftRadioShownTick = -1;
    private float _shiftRadioSlide = 1, _shiftVignettePulse = -1;
    private int _shiftQueued;
    private string? _shiftLastUrgentId;

    // Needs: shown alpha per row (thirst, heat).
    private int _shiftThirst, _shiftHeat;
    private readonly float[] _shiftNeedAlpha = new float[2];

    private static readonly Color HiVis = new("d8f126");
    private const float CompassHalfDegrees = 75, CompassClampDegrees = 72;

    private bool StewardShiftReportShown => _shiftReport is not null;
    private Vector2 ShiftScreen => GetViewport().GetVisibleRect().Size;

    private static Label ShiftText(string text, float px, Color colour, Font? font = null, bool outline = false)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", Ui.Px(px));
        label.AddThemeFontOverride("font", font ?? Ui.BodySemi);
        label.AddThemeColorOverride("font_color", colour);
        if (outline) { label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, .75f)); label.AddThemeConstantOverride("outline_size", Ui.Px(5)); }
        return label;
    }

    private void ClearStewardShiftHud()
    {
        _shiftHud?.QueueFree();
        _shiftHud = null;
        _shiftClock = _shiftKeys = _shiftMenuTitle = null;
        _shiftCrosshair = _shiftCompass = _shiftNeeds = _shiftCard = _shiftRadioStrip = _shiftVignette = _shiftReport = null;
        _shiftMenu = null; _shiftResume = null;
        _shiftCallPlaces.Clear();
        ClearSiteMap();
        _radialControl = null;
        _shiftLastHeld = null; _shiftCancelledTick = -1; _shiftLastUrgentId = null; _shiftRadioShownTick = -1;
        _shiftPinShown = float.NaN;
    }

    private Control Drawn(string name, Action<Control> draw)
    {
        var control = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore };
        control.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        control.Draw += () => draw(control);
        _shiftHud!.AddChild(control);
        return control;
    }

    private void BuildStewardShiftHud()
    {
        ClearStewardShiftHud();
        _shiftHud = new CanvasLayer { Layer = 21, Name = "StewardShiftHud" };
        AddChild(_shiftHud);
        var size = ShiftScreen;
        _shiftLandmarks = ShiftLandmarks();

        _shiftVignette = Drawn("UrgentPulse", DrawShiftVignette);
        _shiftCrosshair = Drawn("Crosshair", c =>
        {
            var centre = ShiftScreen / 2;
            var arm = Ui.S(7); var gap = Ui.S(3); var width = Mathf.Max(1.5f, Ui.S(2));
            foreach (var (colour, w) in new[] { (new Color(0, 0, 0, .55f), width + 2), (new Color(1, 1, 1, .9f), width) })
            {
                c.DrawLine(centre + new Vector2(-gap - arm, 0), centre + new Vector2(-gap, 0), colour, w);
                c.DrawLine(centre + new Vector2(gap, 0), centre + new Vector2(gap + arm, 0), colour, w);
                c.DrawLine(centre + new Vector2(0, -gap - arm), centre + new Vector2(0, -gap), colour, w);
                c.DrawLine(centre + new Vector2(0, gap), centre + new Vector2(0, gap + arm), colour, w);
            }
        });
        _shiftCompass = Drawn("Compass", DrawShiftCompass);
        _shiftCard = Drawn("PromptCard", DrawShiftCard);
        _shiftNeeds = Drawn("Needs", DrawShiftNeeds);
        _shiftRadioStrip = Drawn("RadioStrip", DrawShiftRadio);
        BuildSiteMap();

        _shiftClock = ShiftText("", 12, new Color(1, 1, 1, .85f), outline: true);
        _shiftClock.HorizontalAlignment = HorizontalAlignment.Center;
        _shiftClock.Position = new Vector2(0, Ui.S(64)); _shiftClock.Size = new Vector2(size.X, Ui.S(20));
        _shiftHud.AddChild(_shiftClock);
        _shiftKeys = ShiftText(_session.IsHandsOn ? "V · Manager view  ·  WASD walk  ·  Shift run  ·  hold E to act  ·  hold Q radio  ·  M map  ·  Esc menu  ·  P pause"
            : "WASD walk  ·  Shift run  ·  hold E to act  ·  hold Q radio  ·  M map  ·  Esc menu  ·  P pause", 11, new Color(1, 1, 1, .7f), outline: true);
        _shiftKeys.HorizontalAlignment = HorizontalAlignment.Right;
        _shiftKeys.Position = new Vector2(size.X - Ui.S(700) - Ui.Gutter, size.Y - Ui.S(40)); _shiftKeys.Size = new Vector2(Ui.S(700), Ui.S(24));
        _shiftHud.AddChild(_shiftKeys);
        BuildRadialMenu();

        _shiftMenu = new PanelContainer { Visible = false };
        _shiftMenu.AddThemeStyleboxOverride("panel", Ui.Sheet(24, 22));
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", Ui.Px(10));
        _shiftMenu.AddChild(box);
        _shiftMenuTitle = HudLabel("", 21);
        box.AddChild(_shiftMenuTitle);
        _shiftResume = ButtonText("Back on shift", () => SetShiftMenu(null));
        box.AddChild(_shiftResume);
        // In Front of House the way out is the manager's view; the day carries on.
        box.AddChild(_session.IsHandsOn ? ButtonText("Manager view  (V)", LeaveHandsOnFirstPerson) : ButtonText("Return to menu", ReturnFromStewardShift));
        _shiftMenu.Size = new Vector2(Ui.S(320), 0);
        _shiftMenu.Position = new Vector2((size.X - Ui.S(320)) / 2, size.Y * .3f);
        _shiftHud.AddChild(_shiftMenu);
    }

    /// <summary>A line from Control that isn't a call: the shift's opening word, or its closing one.</summary>
    private void PushShiftRadio(string message)
    {
        _shiftRadioIntro = message;
        _shiftRadioIntroTick = _session.CurrentTick;
    }

    /// <summary>Opens the shift menu (paused, cursor free) with a title, or closes it (back to looking about).</summary>
    private void SetShiftMenu(string? title)
    {
        if (_shiftMenu is null) return;
        var open = title is not null;
        _shiftMenu.Visible = open;
        if (open) _shiftMenuTitle!.Text = title;
        var over = _session.PlayerStewardId is null;
        _shiftResume!.Visible = !over;
        if (open && _shiftKeyDown) { _shiftKeyDown = false; ReleaseShiftAction(); }
        if (open != _session.IsPaused && !over) _host.Submit(new SetPausedCommand(open));
        _shiftLooking = !open;
        Input.MouseMode = open || DisplayServer.GetName() == "headless" ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        if (_shiftCrosshair is not null) _shiftCrosshair.Visible = !open;
    }

    /// <summary>What the compass marks: the stages, the bars, the loos, first aid and the gate, by icon.</summary>
    private (string Icon, float X, float Z)[] ShiftLandmarks()
    {
        var marks = new List<(string, GridCell)>();
        foreach (var stage in _session.Stages) marks.Add(("music", stage.PerformerMarks[0]));
        foreach (var place in _session.CapturePreparation()?.BuildPlacements ?? [])
        {
            var icon = place.Kind switch
            {
                BuildServiceKind.Bar => "beer", BuildServiceKind.Toilet => "door-closed", BuildServiceKind.FirstAid => "heart-pulse", _ => null,
            };
            // One icon for a block of loos.
            if (icon is not null && !(icon == "door-closed" && marks.Any(m => m.Item1 == icon && Math.Abs(m.Item2.X - place.Cell.X) + Math.Abs(m.Item2.Z - place.Cell.Z) < 16)))
                marks.Add((icon, place.Cell));
        }
        marks.Add(("map-pin", GameSession.MedicalExitCell));
        return marks.Select(mark => { var (x, z) = TraversalGrid.CellCentre(mark.Item2); return (mark.Item1, x / 1000f, z / 1000f); }).ToArray();
    }

    /// <summary>Bearing of a point from the player, relative to the camera, in degrees: positive to the right.</summary>
    private float ShiftBearing(float x, float z)
    {
        var me = _eyeCamera!.GlobalPosition;
        var target = Mathf.RadToDeg(Mathf.Atan2(x - me.X, z - me.Z));
        var facing = Mathf.RadToDeg(_shiftYaw) + 180;
        return -Mathf.Wrap(target - facing, -180f, 180f);
    }

    private static void Icon(Control c, string name, Vector2 at, float size, Color colour) =>
        c.DrawTextureRect(Ui.Icon(name), new Rect2(at, new Vector2(size, size)), false, colour);

    private static void TextAt(Control c, Font font, Vector2 topLeft, string text, float px, Color colour, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1)
    {
        var size = Ui.Px(px);
        // Godot draws from the baseline: put the ascent under the top.
        c.DrawString(font, topLeft + new Vector2(0, font.GetAscent(size)), text, align, width, size, colour);
    }

    private static float TextWidth(Font font, string text, float px) => font.GetStringSize(text, fontSize: Ui.Px(px)).X;

    // ---- Compass A -------------------------------------------------------------------------------------------

    private void DrawShiftCompass(Control c)
    {
        if (_eyeCamera is null) return;
        var w = Ui.S(520); var h = Ui.S(26);
        var x0 = (ShiftScreen.X - w) / 2; var y = Ui.S(14);
        var cx = x0 + w / 2;
        float X(float bearing) => cx + bearing * w / 2 / CompassHalfDegrees;
        c.DrawStyleBox(Ui.Box(new Color(Ui.BarDeep, .59f), 13), new Rect2(x0, y, w, h));
        var facing = Mathf.RadToDeg(_shiftYaw) + 180;
        for (var d = 0; d < 360; d += 15)
        {
            var bearing = -Mathf.Wrap(d - facing, -180f, 180f);
            var letter = d switch { 180 => "N", 90 => "E", 0 => "S", 270 => "W", _ => null };
            if (letter is not null && Mathf.Abs(bearing) <= 74)
            {
                var size = Ui.Px(13);
                c.DrawString(Ui.BodyBold, new Vector2(X(bearing) - TextWidth(Ui.BodyBold, letter, 13) / 2, y + Ui.S(13) + Ui.BodyBold.GetAscent(size) / 2 - Ui.S(1)), letter, fontSize: size, modulate: Ui.BarText);
            }
            else if (letter is null && Mathf.Abs(bearing) <= 74)
                c.DrawLine(new Vector2(X(bearing), y + Ui.S(9)), new Vector2(X(bearing), y + Ui.S(17)), new Color(Ui.BarMuted, .63f), 1);
        }
        foreach (var (icon, lx, lz) in _shiftLandmarks)
        {
            var bearing = ShiftBearing(lx, lz);
            if (Mathf.Abs(bearing) > CompassClampDegrees) continue;
            Icon(c, icon, new Vector2(X(bearing) - Ui.S(7), y + Ui.S(6)), Ui.S(14), new Color(Ui.BarMuted, icon == "map-pin" ? .5f : .78f));
        }
        c.DrawColoredPolygon([new Vector2(cx - Ui.S(5), y + Ui.S(27)), new Vector2(cx + Ui.S(5), y + Ui.S(27)), new Vector2(cx, y + Ui.S(22))], Ui.BarText);
        if (_shiftPin is not { } pin || float.IsNaN(_shiftPinShown)) return;
        var fill = pin.Urgent ? Ui.Alert : Ui.Gold;
        var px = X(Mathf.Clamp(_shiftPinShown, -CompassClampDegrees, CompassClampDegrees));
        Icon(c, "map-pin", new Vector2(px - Ui.S(10), y + Ui.S(2)), Ui.S(20), fill);
        var chip = $"{pin.Place} · {_shiftPinMetres:0} m";
        var cw = TextWidth(Ui.BodyBold, chip, 12) + Ui.S(16);
        var chipX = Mathf.Clamp(px - cw / 2, x0, x0 + w - cw);
        c.DrawStyleBox(Ui.Box(fill, 9), new Rect2(chipX, y + Ui.S(30), cw, Ui.S(18)));
        TextAt(c, Ui.BodyBold, new Vector2(chipX + Ui.S(8), y + Ui.S(31)), chip, 12, pin.Urgent ? Ui.PaperBright : Ui.Bar);
    }

    // ---- Prompt A --------------------------------------------------------------------------------------------

    private void DrawShiftCard(Control c)
    {
        if (_cardAlpha <= 0.01f || _cardTitle == "") return;
        var a = _cardAlpha;
        var result = _cardState is CardState.Success or CardState.Fail or CardState.Cancelled;
        var width = Mathf.Max(TextWidth(Ui.Slab, _cardTitle, 17), TextWidth(Ui.BodySemi, _cardDetail, 12)) + Ui.S(84);
        if (!_cardKey) width -= Ui.S(40);
        var h = Ui.S(62);
        var x0 = (ShiftScreen.X - width) / 2;
        var y = ShiftScreen.Y / 2 + Ui.S(44) + _cardRise;
        var stateColour = _cardState switch { CardState.Success => Ui.Teal, CardState.Fail => Ui.Alert, CardState.Cancelled => Ui.InkMuted, _ => Colors.Transparent };
        var box = Ui.Box(new Color(Ui.Paper, .96f * a), 10, result ? new Color(stateColour, a) : null, result ? 2 : 0, shadow: 5, shadowAlpha: .43f * a);
        box.ShadowOffset = new Vector2(0, Ui.S(3));
        c.DrawStyleBox(box, new Rect2(x0, y, width, h));
        var tx = x0 + (_cardKey ? Ui.S(58) : Ui.S(18));
        var capCentre = new Vector2(x0 + Ui.S(28), y + Ui.S(31));
        if (result)
        {
            c.DrawCircle(capCentre, Ui.S(18), new Color(stateColour, a));
            Icon(c, _cardState switch { CardState.Success => "check", CardState.Fail => "x", _ => "minus" }, capCentre - Ui.S(11, 11), Ui.S(22), new Color(Ui.PaperBright, a));
            TextAt(c, Ui.Slab, new Vector2(tx, y + Ui.S(12)), _cardTitle, 17, new Color(Ui.Ink, a));
            TextAt(c, Ui.BodySemi, new Vector2(tx, y + Ui.S(35)), _cardDetail, 12, new Color(Ui.InkMuted, a));
            return;
        }
        if (_cardKey)
        {
            c.DrawArc(capCentre, Ui.S(19), 0, Mathf.Tau, 48, new Color(Ui.PaperRule, a), Ui.S(3.2f), true);
            if (_cardRing > 0) c.DrawArc(capCentre, Ui.S(19), -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * _cardRing, 48, new Color(Ui.Gold, a), Ui.S(3.2f), true);
            c.DrawStyleBox(Ui.Box(new Color(Ui.Bar, a), 5), new Rect2(capCentre - Ui.S(12, 12), Ui.S(24, 24)));
            var e = Ui.Px(14);
            c.DrawString(Ui.BodyBold, capCentre + new Vector2(-TextWidth(Ui.BodyBold, "E", 14) / 2, Ui.BodyBold.GetAscent(e) / 2 - Ui.S(1)), "E", fontSize: e, modulate: new Color(Ui.BarText, a));
            Icon(c, _cardIcon, new Vector2(tx, y + Ui.S(9)), Ui.S(15), new Color(Ui.Teal, a));
            TextAt(c, Ui.CapsFont, new Vector2(x0 + Ui.S(78), y + Ui.S(8)), "HOLD E", 10, new Color(Ui.Teal, a));
        }
        TextAt(c, Ui.Slab, new Vector2(tx, y + Ui.S(21) - (_cardKey ? 0 : Ui.S(9))), _cardTitle, 17, new Color(Ui.Ink, a));
        TextAt(c, Ui.BodySemi, new Vector2(tx, y + Ui.S(42) - (_cardKey ? 0 : Ui.S(9))), _cardDetail, 12, new Color(Ui.InkMuted, a));
    }

    // ---- Radio A ---------------------------------------------------------------------------------------------

    private bool ShiftRadioUrgent => _shiftRadioNow.Call is { Urgent: true, Open: true };
    /// <summary>An urgent call's response timer: the 45 s Control gives before sending someone else.</summary>
    private const long UrgentResponseTicks = 45 * 80;
    /// <summary>A normal call shows in full for 20 s, then folds to one line.</summary>
    private const long RadioFullTicks = 20 * 80;

    private void DrawShiftRadio(Control c)
    {
        if (_shiftRadioNow.Message == "") return;
        var urgent = ShiftRadioUrgent;
        var compact = !urgent && _session.CurrentTick - _shiftRadioNow.Tick > RadioFullTicks && _shiftRadioNow.Call is not null;
        var w = Ui.S(urgent ? 520 : 470); var h = Ui.S(urgent ? 74 : compact ? 34 : 62);
        var x0 = Ui.S(16) - (1 - EaseOut(_shiftRadioSlide)) * (w + Ui.S(16));
        var y1 = ShiftScreen.Y - Ui.S(16); var y0 = y1 - h;
        c.DrawStyleBox(Ui.Box(urgent ? new Color(Ui.Alert, .93f) : new Color(Ui.BarDeep, .8f), 8), new Rect2(x0, y0, w, h));
        c.DrawRect(new Rect2(x0, y0 + Ui.S(4), Ui.S(4), h - Ui.S(8)), urgent ? Ui.PaperBright : HiVis);
        if (urgent) Icon(c, "heart-pulse", new Vector2(x0 + Ui.S(14), y0 + Ui.S(12)), Ui.S(26), Ui.PaperBright);
        else Icon(c, "radio", new Vector2(x0 + Ui.S(16), y0 + (compact ? Ui.S(6) : Ui.S(14))), Ui.S(22), HiVis);
        var tag = urgent ? "URGENT · CONTROL" : "RADIO · CONTROL";
        TextAt(c, Ui.CapsFont, new Vector2(x0 + Ui.S(52), y0 + Ui.S(9)), compact ? "" : tag, 10.5f, urgent ? Ui.PaperBright : Ui.BarMuted);
        var message = _shiftRadioNow.Message;
        if (compact) TextAt(c, Ui.Slab, new Vector2(x0 + Ui.S(52), y0 + Ui.S(7)), message, 16, Ui.BarText, width: w - Ui.S(70));
        else
        {
            TextAt(c, Ui.Slab, new Vector2(x0 + Ui.S(52), y0 + Ui.S(22)), message, urgent ? 21 : 18, urgent ? Ui.PaperBright : Ui.BarText, width: w - Ui.S(70));
            TextAt(c, Ui.BodySemi, new Vector2(x0 + Ui.S(52), y0 + Ui.S(urgent ? 49 : 43)), _shiftRadioNow.Where, 12, urgent ? new Color(Ui.PaperBright, .86f) : Ui.BarMuted);
        }
        if (urgent && _shiftRadioNow.Call is { } call)
        {
            var left = Math.Max(0, UrgentResponseTicks - (_session.CurrentTick - call.HeardTick));
            c.DrawRect(new Rect2(x0 + Ui.S(4), y1 - Ui.S(4), (w - Ui.S(8)) * left / UrgentResponseTicks, Ui.S(4)), Ui.PaperBright);
            var seconds = left / 80;
            TextAt(c, Ui.BodyBold, new Vector2(x0, y0 + Ui.S(9)), FestivalDurationText(left,false), 12, Ui.PaperBright, HorizontalAlignment.Right, w - Ui.S(14));
        }
        if (_shiftQueued > 0)
        {
            var badge = $"+{_shiftQueued}";
            var bw = TextWidth(Ui.BodyBold, badge, 11) + Ui.S(12);
            c.DrawStyleBox(Ui.Box(Ui.Bar, 9), new Rect2(x0 + w - bw / 2 - Ui.S(4), y0 - Ui.S(9), bw, Ui.S(18)));
            TextAt(c, Ui.BodyBold, new Vector2(x0 + w - bw / 2 + Ui.S(2), y0 - Ui.S(8)), badge, 11, Ui.BarText);
        }
    }

    private static float EaseOut(float t) => 1 - Mathf.Pow(1 - Mathf.Clamp(t, 0, 1), 3);

    /// <summary>The urgent edge pulse: an alert vignette that rises in 0.15 s and fades over 0.9 s, once per urgent call.</summary>
    private void DrawShiftVignette(Control c)
    {
        if (_shiftVignettePulse < 0) return;
        var t = _shiftVignettePulse;
        var alpha = .32f * (t < .15f ? t / .15f : Mathf.Max(0, 1 - (t - .15f) / .9f));
        if (alpha <= 0) return;
        var s = ShiftScreen; var ix = s.X * .03f; var iy = s.Y * .05f;
        var edge = new Color(Ui.Alert, alpha); var clear = new Color(Ui.Alert, 0);
        Vector2[] outer = [Vector2.Zero, new(s.X, 0), s, new(0, s.Y)];
        Vector2[] inner = [new(ix * 3, iy * 3), new(s.X - ix * 3, iy * 3), new(s.X - ix * 3, s.Y - iy * 3), new(ix * 3, s.Y - iy * 3)];
        for (var i = 0; i < 4; i++)
        {
            var j = (i + 1) % 4;
            c.DrawPolygon([outer[i], outer[j], inner[j], inner[i]], [edge, edge, clear, clear]);
        }
    }

    // ---- Needs A ---------------------------------------------------------------------------------------------

    private void DrawShiftNeeds(Control c)
    {
        var stripTop = ShiftScreen.Y - Ui.S(16) - Ui.S(62);
        var x0 = Ui.S(24);
        var rows = new[] { ("droplet", _shiftThirst, 0), ("thermometer", _shiftHeat, 1) };
        // Each hides on its own; one alone takes the lower slot.
        var shown = rows.Where(row => _shiftNeedAlpha[row.Item3] > .01f).ToArray();
        for (var i = 0; i < shown.Length; i++)
        {
            var (icon, value, index) = shown[i];
            var slot = shown.Length - 1 - i;
            var y = stripTop - Ui.S(38) - slot * Ui.S(22);
            var v = value / 10_000f;
            var a = _shiftNeedAlpha[index] * (v > .8f ? .85f + .15f * Mathf.Sin(Time.GetTicksMsec() / 1000f * Mathf.Tau) : 1);
            c.DrawStyleBox(Ui.Box(new Color(Ui.BarDeep, .47f * a), 9), new Rect2(x0 - Ui.S(6), y - Ui.S(3), Ui.S(124), Ui.S(20)));
            Icon(c, icon, new Vector2(x0, y), Ui.S(14), new Color(Ui.BarText, .86f * a));
            c.DrawStyleBox(Ui.Box(new Color(Ui.BarText, .24f * a), 3), new Rect2(x0 + Ui.S(22), y + Ui.S(5), Ui.S(86), Ui.S(5)));
            c.DrawStyleBox(Ui.Box(new Color(v > .5f ? Ui.Warn : Ui.Good, a), 3), new Rect2(x0 + Ui.S(22), y + Ui.S(5), Ui.S(86) * Mathf.Clamp(v, 0, 1), Ui.S(5)));
        }
    }

    // ---- Words -----------------------------------------------------------------------------------------------

    private static string ActionVerb(StewardActionKind kind) => kind switch
    {
        StewardActionKind.BreakUp => "Break up", StewardActionKind.RadioMedic => "Radio the medic for", StewardActionKind.StandBy => "Help up",
        StewardActionKind.Calm => "Calm", StewardActionKind.FreeLoo => "Open", StewardActionKind.Escort => "Escort",
        StewardActionKind.HerdCow => "Drive home", StewardActionKind.Drink => "Drink at", StewardActionKind.EmptyBin => "Empty",
        StewardActionKind.BodgeTap => "Bodge", StewardActionKind.Directions => "Point the way for", StewardActionKind.SpreadStraw => "Spread straw on",
        StewardActionKind.PickLitter => "Pick up", _ => "",
    };

    private static string ActionIcon(StewardActionKind kind) => kind switch
    {
        StewardActionKind.Calm or StewardActionKind.Directions => "hand", StewardActionKind.BreakUp => "shield", StewardActionKind.Escort => "users",
        StewardActionKind.FreeLoo => "door-closed", StewardActionKind.PickLitter or StewardActionKind.EmptyBin => "trash-2",
        StewardActionKind.StandBy => "heart-pulse", StewardActionKind.RadioMedic => "radio", StewardActionKind.Drink => "droplet",
        StewardActionKind.BodgeTap => "wrench", StewardActionKind.HerdCow => "map-pin", StewardActionKind.SpreadStraw => "package", _ => "hand",
    };

    private static string PastTense(StewardActionKind kind, bool success) => !success
        ? kind is StewardActionKind.Calm or StewardActionKind.BreakUp ? "Shrugged off" : kind == StewardActionKind.RadioMedic ? "No medic free" : "Didn't take"
        : kind switch
        {
            StewardActionKind.Calm => "Calmed", StewardActionKind.BreakUp => "Broken up", StewardActionKind.Escort => "Escorting",
            StewardActionKind.RadioMedic => "Medic called", StewardActionKind.StandBy => "Stood by", StewardActionKind.FreeLoo => "Opened",
            StewardActionKind.PickLitter => "Picked up", StewardActionKind.HerdCow => "Driving home", StewardActionKind.Drink => "Refreshed",
            StewardActionKind.EmptyBin => "Emptied", StewardActionKind.BodgeTap => "Bodged", StewardActionKind.Directions => "Pointed the way",
            StewardActionKind.SpreadStraw => "Straw down", _ => "Done",
        };

    private static string Lowered(StewardActionKind kind, string name) => kind is StewardActionKind.FreeLoo or StewardActionKind.HerdCow or StewardActionKind.Drink or
        StewardActionKind.EmptyBin or StewardActionKind.BodgeTap or StewardActionKind.SpreadStraw or StewardActionKind.PickLitter ? "the " + name.ToLowerInvariant() : name;

    /// <summary>The card's detail line: kind · distance · state, as the board has it ("guest · 2 m · agitated").</summary>
    private string TargetDetail(string target, int distance)
    {
        var metres = $"{Mathf.Max(1, Mathf.RoundToInt(distance / 1000f))} m";
        var key = target[(target.IndexOf(':') + 1)..];
        if (target.StartsWith("person:", StringComparison.Ordinal) && ulong.TryParse(key, out var id) && _session.CapturePerson(id) is { } person)
        {
            var kind = person.Role switch { ProtectedPersonRole.Staff => "crew", ProtectedPersonRole.Performer => "band", _ => "guest" };
            var state = person.HealthStage is MedicalStage.Collapsed or MedicalStage.Critical ? "collapsed"
                : person.ConductStage switch
                {
                    DisorderStage.Fight => "fighting", DisorderStage.Argument => "arguing", DisorderStage.Agitated => "agitated", DisorderStage.Complaint => "complaining",
                    _ when _session.CaptureStewardShift()?.EscortId == id => "following you to the gate",
                    _ when _session.CaptureStewardShift()?.EscortOffer?.GuestId == id => "calmed · walk them out",
                    _ => person.VendorId is { } vendor ? $"looking for {_session.StallName(vendor)}" : person.ToiletStage == ToiletVisitStage.Approaching ? "looking for the loos" :
                        person.Intent == MedicalIntent.SeekWater ? "looking for water" : "",
                };
            return state == "" ? $"{kind} · {metres}" : $"{kind} · {metres} · {state}";
        }
        if (target.StartsWith("toilet:", StringComparison.Ordinal)) return $"portaloo · {metres} · someone's stuck";
        if (target.StartsWith("bin:", StringComparison.Ordinal) && _session.CaptureBins().FirstOrDefault(b => b.Id == key) is { } bin) return $"bin · {metres} · {bin.FullPercent}% full";
        if (target.StartsWith("tap:", StringComparison.Ordinal)) return _session.ActiveFault(key) is not null ? $"tap · {metres} · broken" : $"tap · {metres}";
        if (target.StartsWith("bar:", StringComparison.Ordinal)) return $"bar · {metres}";
        if (target.StartsWith("cow:", StringComparison.Ordinal)) return $"cow · {metres} · loose";
        if (target.StartsWith("litter:", StringComparison.Ordinal)) return $"litter · {metres}";
        if (target.StartsWith("mud:", StringComparison.Ordinal)) return $"mud · {_session.CaptureWeather()?.StrawBales ?? 0} bales in the store";
        return metres;
    }

    private static string CallLine(StewardCall call, string place) => call.Kind switch
    {
        StewardCallKind.Fight => $"Fight by {place}!",
        StewardCallKind.Argument => $"Argument brewing by {place}.",
        StewardCallKind.Collapse => $"Someone's down by {place}!",
        StewardCallKind.StuckInLoo => $"Someone's stuck in a portaloo by {place}.",
        StewardCallKind.BrokenTap => $"Tap's gone at {place}.",
        StewardCallKind.FullBin => $"Bin's full by {place}.",
        _ => $"Cow loose by {place}!",
    };

    private static string CallNoun(StewardCallKind kind) => kind switch
    {
        StewardCallKind.Fight => "Fight", StewardCallKind.Argument => "Argument", StewardCallKind.Collapse => "Collapse",
        StewardCallKind.StuckInLoo => "Stuck in a loo", StewardCallKind.BrokenTap => "Broken tap", StewardCallKind.FullBin => "Full bin", _ => "Loose cow",
    };

    private string ClosedLine(StewardCall call, string place) => call.AnsweredTick >= 0
        ? $"Nice one: {CallNoun(call.Kind).ToLowerInvariant()} by {place}, sorted."
        : call.CoveredBy is { } crew ? $"{CallNoun(call.Kind)} by {place}: {CrewFirstName(crew)} sorted it."
        : $"{CallNoun(call.Kind)} by {place}: the crew's dealt with it.";

    /// <summary>A hands-on day: Control has given the call to someone hired, so it isn't the player's.</summary>
    private string CoveredLine(StewardCall call, string place) => $"{CallNoun(call.Kind)} by {place}: {CrewFirstName(call.CoveredBy!.Value)}'s on it.";

    private string CrewFirstName(ulong id) => _session.CapturePerson(id)?.Name is { Length: > 0 } name ? name.Split(' ')[0] : "The crew";

    // ---- Per frame -------------------------------------------------------------------------------------------

    private void UpdateStewardShiftHud()
    {
        if (_shiftHud is null) return;
        var delta = (float)GetProcessDeltaTime();
        var p = _session.CapturePreparation()!;
        var day = _session.PreparedEditionDurationTicks;
        var elapsed = _session.CurrentTick - p.StartedTick;
        if (_shiftClock is not null)
            _shiftClock.Text = (elapsed >= day ? "Closing time" : $"{FestivalClockText(elapsed)} / {FestivalDurationText(day)}") + (_session.PlayerStageId is not null ? "  ·  ON STAGE" : "") + (_session.IsPaused ? "  ·  PAUSED" : "");
        var shift = _session.CaptureStewardShift();
        if (shift is null) return;
        var now = _session.CurrentTick;
        var me = _eyeCamera?.GlobalPosition ?? Vector3.Zero;
        int Distance(int x, int z) => (int)(new Vector2(x / 1000f - me.X, z / 1000f - me.Z).Length() * 1000);

        // The card: a hold, how one ended, or what's in front.
        var wanted = true;
        if (shift.Action is { } held && _session.StewardTargetPoint(held.TargetId) is { } point)
        {
            _cardState = CardState.Holding; _cardKey = true; _cardIcon = ActionIcon(held.Kind);
            _cardTitle = $"{ActionVerb(held.Kind)} {Lowered(held.Kind, point.Name)}";
            _cardDetail = TargetDetail(held.TargetId, Distance(point.X, point.Z));
            _cardRing = Mathf.Clamp((now - held.StartedTick + (float)_host.Clock.InterpolationFraction) / held.DurationTicks, 0, 1);
            _shiftLastHeld = held;
        }
        else if (shift.LastOutcome is { } last && last.Tick >= (_shiftLastHeld?.StartedTick ?? long.MaxValue) && now - last.Tick < (last.Completed ? 96 : 64))
        {
            _cardState = !last.Completed ? CardState.Cancelled : last.Success ? CardState.Success : CardState.Fail;
            // A driven cow's arrival comes after its hold: home, not on the way.
            var home = last.Kind == StewardActionKind.HerdCow && _session.StewardTargetPoint(last.TargetId) is null;
            _cardTitle = !last.Completed ? "Cancelled" : home ? "Home" : PastTense(last.Kind, last.Success);
            _cardDetail = last.Text;
        }
        else if (_shiftCancelledTick >= 0 && now - _shiftCancelledTick < 64)
        {
            _cardState = CardState.Cancelled; _cardTitle = "Cancelled"; _cardDetail = "You let go too soon";
        }
        else if (shift.PlayerId is not null && !StewardShiftReportShown && _session.StewardReach(ShiftFacing).FirstOrDefault() is { } first)
        {
            _cardState = CardState.Prompt; _cardRing = 0;
            _cardKey = first.Action is not null;
            _cardIcon = first.Action is { } action ? ActionIcon(action) : "hand";
            _cardTitle = first.Action is { } verb ? $"{ActionVerb(verb)} {Lowered(verb, first.Name)}" : first.Name;
            _cardDetail = TargetDetail(first.TargetId, first.DistanceMillimetres);
        }
        else wanted = false;
        // The plan in the right hand, or the radio menu up: the prompt waits.
        if (StewardShiftReportShown || shift.PlanUp || RadialMenuOpen) wanted = false;
        // Fade in rising 6 over 0.12 s, out over 0.10 s.
        _cardAlpha = Mathf.MoveToward(_cardAlpha, wanted ? 1 : 0, delta / (wanted ? .12f : .10f));
        _cardRise = (1 - _cardAlpha) * Ui.S(6);
        _shiftCard!.QueueRedraw();

        // The radio: the newest thing Control said; urgent calls take over the strip.
        var calls = shift.Calls ?? [];
        string Place(StewardCall call)
        {
            var key = $"{call.Id}@{call.OpenedTick}";
            if (_shiftCallPlaces.TryGetValue(key, out var known)) return known;
            if (_session.StewardCallPoint(call) is not { } at) return "the field";
            return _shiftCallPlaces[key] = _session.StewardLandmark(at.X, at.Z);
        }
        string Where(StewardCall call) => _session.StewardCallPoint(call) is { } at ? $"{Place(call)} · {Distance(at.X, at.Z) / 1000} m away" : Place(call);
        // A hands-on day's calls Control gave to the hired crew aren't the player's: they show once, as covered.
        var heard = calls.Where(call => call.Open && call.HeardTick <= now && call.CoveredBy is null).ToArray();
        var covered = calls.Where(call => call.Open && call.HeardTick <= now && call.CoveredBy is not null).ToArray();
        var urgentNow = heard.Where(call => call.Urgent).OrderBy(call => call.HeardTick).FirstOrDefault();
        (string, string, StewardCall?, long) radio;
        if (urgentNow is not null) radio = (CallLine(urgentNow, Place(urgentNow)), Where(urgentNow), urgentNow, urgentNow.HeardTick);
        else
        {
            var events = heard.Select(call => (Tick: call.HeardTick, Message: CallLine(call, Place(call)), Where: Where(call), Call: (StewardCall?)call))
                .Concat(covered.Select(call => (Tick: call.HeardTick, Message: CoveredLine(call, Place(call)), Where: "", Call: (StewardCall?)null)))
                .Concat(calls.Where(call => !call.Open && (call.ClosedTick >= call.HeardTick || call.AnsweredTick >= 0))
                    .Select(call => (Tick: call.ClosedTick, Message: ClosedLine(call, Place(call)), Where: "", Call: (StewardCall?)null)))
                .Append((Tick: _shiftRadioIntroTick, Message: _shiftRadioIntro, Where: "", Call: null))
                .OrderByDescending(item => item.Tick).First();
            radio = (events.Message, events.Where, events.Call, events.Tick);
        }
        if (radio.Item4 != _shiftRadioNow.Tick || radio.Item1 != _shiftRadioNow.Message && radio.Item3?.Id != _shiftRadioNow.Call?.Id)
        {
            if (radio.Item4 != _shiftRadioShownTick) { _shiftRadioSlide = 0; RaiseShiftRadio(radio.Item3 is { Urgent: true }); }
            _shiftRadioShownTick = radio.Item4;
        }
        _shiftRadioNow = radio;
        _shiftQueued = Math.Max(0, heard.Length - (radio.Item3 is { Open: true } ? 1 : 0));
        _shiftRadioSlide = Mathf.MoveToward(_shiftRadioSlide, 1, delta / .2f);
        if (urgentNow is not null && urgentNow.Id != _shiftLastUrgentId) { _shiftLastUrgentId = urgentNow.Id; _shiftVignettePulse = 0; }
        if (_shiftVignettePulse >= 0) { _shiftVignettePulse += delta; if (_shiftVignettePulse > 1.05f) _shiftVignettePulse = -1; }
        _shiftRadioStrip!.QueueRedraw(); _shiftVignette!.QueueRedraw();

        // The compass pin for the most pressing call: it glides; the distance ticks four times a second.
        var pinCall = heard.OrderBy(call => call.Urgent ? 0 : 1).ThenBy(call => call.HeardTick).FirstOrDefault();
        if (pinCall is not null && _session.StewardCallPoint(pinCall) is { } spot && _eyeCamera is not null)
        {
            var bearing = ShiftBearing(spot.X / 1000f, spot.Z / 1000f);
            _shiftPin = (bearing, Place(pinCall), pinCall.Urgent);
            _shiftPinShown = float.IsNaN(_shiftPinShown) ? bearing : _shiftPinShown + Mathf.Wrap(bearing - _shiftPinShown, -180f, 180f) * Mathf.Min(1, 12 * delta);
            _shiftPinMetresAge += delta;
            if (_shiftPinMetresAge >= .25f || _shiftPinMetres == 0) { _shiftPinMetresAge = 0; _shiftPinMetres = Mathf.Round(Distance(spot.X, spot.Z) / 1000f); }
        }
        else { _shiftPin = null; _shiftPinShown = float.NaN; _shiftPinMetres = 0; }
        _shiftCompass!.QueueRedraw();

        // Needs: both bars show from the start of the shift, empty until they fill; they fade in over 0.4 s and out over
        // 0.6 s as the report comes up.
        if (shift.PlayerId is { } id && _session.CapturePerson(id) is { } self) { _shiftThirst = self.Thirst; _shiftHeat = self.HeatExposure; }
        foreach (var index in new[] { 0, 1 })
        {
            var on = !StewardShiftReportShown;
            _shiftNeedAlpha[index] = Mathf.MoveToward(_shiftNeedAlpha[index], on ? 1 : 0, delta / (on ? .4f : .6f));
        }
        _shiftNeeds!.QueueRedraw();
        // The clock steps down under the plan's hint while it's up.
        if (_shiftClock is not null) _shiftClock.Position = new Vector2(0, Ui.S(shift.PlanUp ? 96 : 64));
        UpdateSiteMap(delta, heard);
        UpdateRadialMenu(delta, radio.Item3 is { Open: true } current ? current.Id : pinCall?.Id);
        AnimateStewardShiftArms(delta);
    }
}

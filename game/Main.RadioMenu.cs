using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The radial radio menu in first person (the designer's 1B, assets/source/ui/hands-on-v1/INTEGRATION.md): hold Q and
/// Control's three jobs open round the crosshair: Send the medic, Call Dirty Henry, Send crew (which fans out the hired
/// crew). The mouse drives a virtual cursor while the look is frozen; WASD still walks, without running. Releasing Q
/// over a free or busy entry radios the job as a <see cref="StewardRadioCommand"/> (the sim says yes or why not, and the
/// reply comes back on the radio strip); a disabled wedge shakes and says why; the dead zone, Esc and right-click cancel.
/// 1 / 2 / 3 pick the inner wedges while Q is held, 1–6 the crew once the fan is open. Measurements are mockup units.
/// </summary>
public partial class Main
{
    private enum RadialState { Free, Busy, Disabled }
    private sealed record RadialEntry(string Name, string Icon, Color IconColour, RadialState State, string Status, string Note,
        StewardRadioJob Job, string? Target, ulong? Worker = null, string Role = "");

    private Control? _radialControl;
    private bool _radialQDown, _radialOpen;
    private float _radialHeld, _radialT, _radialCloseT = -1, _radialLookEase = 1;
    private Vector2 _radialCursor;
    private int _radialHover = -1, _radialCrewHover = -1;
    private float _radialHoverEase, _radialCrewDwell;
    private bool _radialFan;
    private int _radialShakeWedge = -1, _radialShakeCrew = -1;
    private float _radialShake;
    private string? _radialHubNote;
    private RadialEntry[] _radialInner = [];
    private RadialEntry[] _radialCrew = [];
    private int _radialCrewMore;
    private string? _radialCurrentCallId;
    private readonly Dictionary<string, Texture2D> _radialArt = new(StringComparer.Ordinal);

    private const float RadialOpenDelay = .12f, RadialInner = 150, RadialDead = 24, RadialFanOuter = 236;

    /// <summary>The menu is up (or still easing in or out): the look is frozen and running is off.</summary>
    private bool RadialMenuOpen => _radialOpen;
    /// <summary>How much of a mouse turn reaches the look: none while the menu's up, easing back in over 0.15 s after.</summary>
    private float RadialLookFactor => _radialOpen ? 0 : _radialLookEase;

    private Texture2D RadialArt(string name)
    {
        if (!_radialArt.TryGetValue(name, out var texture)) _radialArt[name] = texture = GD.Load<Texture2D>($"res://assets/ui/handson/{name}.png");
        return texture;
    }

    private void BuildRadialMenu()
    {
        _radialControl = Drawn("RadialMenu", DrawRadialMenu);
        ResetRadialMenu();
    }

    private void ResetRadialMenu()
    {
        _radialQDown = _radialOpen = _radialFan = false;
        _radialHeld = _radialT = 0; _radialCloseT = -1; _radialLookEase = 1;
        _radialHover = _radialCrewHover = -1; _radialShakeWedge = _radialShakeCrew = -1; _radialHubNote = null;
    }

    // ---- Input -----------------------------------------------------------------------------------------------

    /// <summary>The menu's share of the shift's input. True when it took the event.</summary>
    private bool HandleRadialInput(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventKey { Keycode: Key.Q, Echo: false } q:
                if (q.Pressed && _shiftLooking && !_radialQDown && _session.PlayerStewardId is not null && !_session.IsPaused)
                {
                    // Q with the plan up puts it away first.
                    LowerSitePlan();
                    _radialQDown = true; _radialHeld = 0;
                }
                else if (!q.Pressed && _radialQDown)
                {
                    _radialQDown = false;
                    if (_radialOpen) ReleaseRadial();
                }
                return true;
            case InputEventMouseMotion motion when _radialOpen:
                var limit = _radialFan ? RadialFanOuter : RadialInner;
                _radialCursor = (_radialCursor + motion.Relative / Ui.Scale).LimitLength(limit);
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } when _radialOpen:
                CloseRadial(); _radialQDown = false;
                return true;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } when _radialOpen || _radialQDown:
                CloseRadial(); _radialQDown = false;
                return true;
            case InputEventKey { Pressed: true, Echo: false } key when _radialOpen && key.Keycode is >= Key.Key1 and <= Key.Key6:
            {
                var n = (int)(key.Keycode - Key.Key1);
                if (_radialFan && n < _radialCrew.Length) { _radialCrewHover = n; SendRadial(_radialCrew[n], n, crew: true); }
                else if (!_radialFan && n < 3)
                {
                    _radialHover = n;
                    if (n == 2) { if (_radialInner[2].State != RadialState.Disabled) _radialFan = true; else SendRadial(_radialInner[2], 2, crew: false); }
                    else SendRadial(_radialInner[n], n, crew: false);
                }
                return true;
            }
        }
        return false;
    }

    private void ReleaseRadial()
    {
        if (_radialFan && _radialCrewHover >= 0 && _radialCrewHover < _radialCrew.Length) { SendRadial(_radialCrew[_radialCrewHover], _radialCrewHover, crew: true); return; }
        // Inside the dead zone, or on Send crew with nobody picked from the fan: nothing goes.
        if (_radialHover < 0 || _radialCursor.Length() < RadialDead || _radialHover == 2 && _radialInner[2].State != RadialState.Disabled) { CloseRadial(); return; }
        SendRadial(_radialInner[_radialHover], _radialHover, crew: false);
    }

    /// <summary>Radios the job (free or busy: the sim sends, queues or says why not); a disabled entry shakes instead.</summary>
    private void SendRadial(RadialEntry entry, int index, bool crew)
    {
        if (entry.State == RadialState.Disabled || entry.Target is null)
        {
            if (crew) _radialShakeCrew = index; else _radialShakeWedge = index;
            _radialShake = .2f;
            _radialHubNote = entry.Note;
            PushShiftRadio($"Control: {entry.Note}");
            CloseRadial(.2f);
            return;
        }
        var queuedBin = entry.Job == StewardRadioJob.Crew && entry.Target.StartsWith("bin:", StringComparison.Ordinal) && entry.State == RadialState.Busy;
        var result = _host.Submit(new StewardRadioCommand(entry.Job, entry.Target, entry.Worker));
        PushShiftRadio(result.IsAccepted ? RadioReply(entry, queuedBin) : $"{(entry.Job == StewardRadioJob.Henry ? "Henry" : entry.Job == StewardRadioJob.Medic ? "Medic" : entry.Name.Split(' ')[0])}: {result.Message}");
        RaiseShiftRadio(false);
        CloseRadial();
    }

    private string RadioReply(RadialEntry entry, bool queued)
    {
        var place = _session.StewardTargetPoint(entry.Target!) is { } at ? _session.StallDisplayLabel(_session.StewardLandmark(at.X, at.Z)) : "the field";
        var who = entry.Target!.StartsWith("person:", StringComparison.Ordinal) && _session.StewardTargetPoint(entry.Target) is { } person ? person.Name.Split(' ')[0] : null;
        return entry.Job switch
        {
            StewardRadioJob.Medic => $"Medic: on my way to {who ?? "them"} by {place}.",
            StewardRadioJob.Henry => $"Henry: on my way to the loos. Keep the lane clear!",
            _ when queued => "Control: all the stewards are busy. That bin's next on the list.",
            _ => $"{entry.Name.Split(' ')[0]}: on it, heading for {place}.",
        };
    }

    private void CloseRadial(float shake = 0)
    {
        if (!_radialOpen) return;
        _radialOpen = false;
        _radialCloseT = .08f + shake;
        _radialLookEase = 0;
    }

    // ---- Per frame -------------------------------------------------------------------------------------------

    private void UpdateRadialMenu(float delta, string? currentCallId)
    {
        if (_radialControl is null) return;
        _radialCurrentCallId = currentCallId;
        if (_session.PlayerStewardId is null || !_shiftLooking || _session.IsPaused) { if (_radialOpen) CloseRadial(); _radialQDown = false; }
        if (_radialQDown && !_radialOpen)
        {
            _radialHeld += delta;
            if (_radialHeld >= RadialOpenDelay && !SitePlanUp)
            {
                _radialOpen = true; _radialT = 0; _radialCloseT = -1; _radialCursor = Vector2.Zero; _radialFan = false;
                _radialHover = _radialCrewHover = -1; _radialHubNote = null; _radialCrewDwell = 0;
            }
        }
        if (!_radialOpen) _radialLookEase = Mathf.MoveToward(_radialLookEase, 1, delta / .15f);
        if (_radialOpen) _radialT = Mathf.MoveToward(_radialT, 1, delta / .12f);
        else if (_radialCloseT > 0) _radialCloseT = Mathf.Max(0, _radialCloseT - delta);
        if (_radialShake > 0) _radialShake = Mathf.Max(0, _radialShake - delta);
        if (_radialOpen || _radialCloseT > 0)
        {
            RefreshRadialEntries();
            if (_radialOpen) PickRadialHover(delta);
        }
        _radialControl.QueueRedraw();
    }

    /// <summary>The three jobs and the crew fan, as they stand: where each would go, and whether it's free, busy or can't.</summary>
    private void RefreshRadialEntries()
    {
        var facing = ShiftFacing;
        var me = _eyeCamera?.GlobalPosition ?? Vector3.Zero;
        int Metres(int x0, int z0, int x1, int z1) => (int)Mathf.Round(new Vector2((x1 - x0) / 1000f, (z1 - z0) / 1000f).Length());

        // The medic.
        var medicTarget = _session.StewardRadioTarget(StewardRadioJob.Medic, facing, _radialCurrentCallId);
        RadialEntry medic;
        var medics = _session.GetMedicResponses();
        if (medics.Count == 0) medic = new("Send the medic", "heart-pulse", Ui.Alert, RadialState.Disabled, "No medic hired", "No medic hired.", StewardRadioJob.Medic, null);
        else if (medicTarget is null) medic = new("Send the medic", "heart-pulse", Ui.Alert, RadialState.Disabled, "Nobody down", "Nobody's down that Control knows of.", StewardRadioJob.Medic, null);
        else
        {
            var (issue, busy) = _session.StewardRadioCheck(StewardRadioJob.Medic, medicTarget);
            var at = _session.StewardTargetPoint(medicTarget)!.Value;
            var nearest = medics.Select(m => _foundationPresentation.Sample(new EntityId(m.WorkerId), 0)).Select(p => Metres((int)p.XMillimetres, (int)p.ZMillimetres, at.X, at.Z)).Min();
            medic = issue is null ? new("Send the medic", "heart-pulse", Ui.Alert, RadialState.Free, $"FREE · {nearest} m", "", StewardRadioJob.Medic, medicTarget)
                : busy ? new("Send the medic", "heart-pulse", Ui.Alert, RadialState.Busy, "BUSY · on a call", issue, StewardRadioJob.Medic, medicTarget)
                : new("Send the medic", "heart-pulse", Ui.Alert, RadialState.Disabled, ShortNote(issue), issue, StewardRadioJob.Medic, medicTarget);
        }

        // Dirty Henry.
        var looTarget = _session.StewardRadioTarget(StewardRadioJob.Henry, facing, _radialCurrentCallId);
        RadialEntry henry;
        if (looTarget is null) henry = new("Call Dirty Henry", "truck", Ui.Teal, RadialState.Disabled, "No loo needs him", "No loo needs emptying.", StewardRadioJob.Henry, null);
        else
        {
            var (issue, busy) = _session.StewardRadioCheck(StewardRadioJob.Henry, looTarget);
            var at = _session.StewardTargetPoint(looTarget)!.Value;
            var (gx, gz) = TraversalGrid.CellCentre(LavSuckerRules.Gate);
            var metres = Metres(gx, gz, at.X, at.Z);
            henry = issue is null ? new("Call Dirty Henry", "truck", Ui.Teal, RadialState.Free, $"FREE · {metres} m", "", StewardRadioJob.Henry, looTarget)
                : busy ? new("Call Dirty Henry", "truck", Ui.Teal, RadialState.Busy, "BUSY · on his way", issue, StewardRadioJob.Henry, looTarget)
                : new("Call Dirty Henry", "truck", Ui.Teal, RadialState.Disabled, ShortNote(issue), issue, StewardRadioJob.Henry, looTarget);
        }

        // The crew.
        var crewTarget = _session.StewardRadioTarget(StewardRadioJob.Crew, facing, _radialCurrentCallId);
        var crew = _session.StewardRadioCrew();
        RadialEntry send;
        if (!_session.IsHandsOn) send = new("Send crew", "users", Ui.Teal, RadialState.Disabled, "Control runs the crew", "Control runs the crew on this shift.", StewardRadioJob.Crew, null);
        else if (crew.Count == 0) send = new("Send crew", "users", Ui.Teal, RadialState.Disabled, "No crew hired · hire in Staff", "No crew hired · hire in Staff.", StewardRadioJob.Crew, null);
        else send = new("Send crew", "users", Ui.Teal, crew.Any(m => m.Busy is null) ? RadialState.Free : RadialState.Busy,
            crew.Any(m => m.Busy is null) ? "FREE" : "BUSY", "", StewardRadioJob.Crew, crewTarget);
        _radialInner = [medic, henry, send];

        var point = crewTarget is not null && _session.StewardTargetPoint(crewTarget) is { } p ? (p.X, p.Z) : ((int)(me.X * 1000), (int)(me.Z * 1000));
        var ordered = crew.OrderBy(m => m.Busy is null ? 0 : 1).ThenBy(m => Metres(m.XMillimetres, m.ZMillimetres, point.Item1, point.Item2)).ThenBy(m => m.WorkerId).ToArray();
        _radialCrewMore = Math.Max(0, ordered.Length - 6);
        _radialCrew = ordered.Take(6).Select(m =>
        {
            var metres = Metres(m.XMillimetres, m.ZMillimetres, point.Item1, point.Item2);
            var role = m.Busy ?? (m.Maintenance ? "maintenance" : "steward");
            var icon = m.Maintenance ? "wrench" : "shield";
            if (crewTarget is null) return new RadialEntry(m.Name, icon, Ui.Teal, RadialState.Disabled, "nothing to go to", "Nothing for the crew to go to.", StewardRadioJob.Crew, null, m.WorkerId, role);
            var (issue, busy) = _session.StewardRadioCheck(StewardRadioJob.Crew, crewTarget, m.WorkerId);
            return issue is null || busy && m.Busy is not null
                ? new RadialEntry(m.Name, icon, Ui.Teal, m.Busy is null ? RadialState.Free : RadialState.Busy, $"{(m.Busy is null ? "FREE" : "BUSY")} · {metres} m",
                    issue ?? "", StewardRadioJob.Crew, crewTarget, m.WorkerId, role)
                : new RadialEntry(m.Name, icon, Ui.Teal, RadialState.Disabled, ShortNote(issue!), issue!, StewardRadioJob.Crew, crewTarget, m.WorkerId, role);
        }).ToArray();
    }

    private static string ShortNote(string note) => note.TrimEnd('.').Length > 26 ? note.TrimEnd('.')[..25] + "…" : note.TrimEnd('.');

    /// <summary>Clockwise from straight up, 0–360°.</summary>
    private static float RadialAngle(Vector2 v) => Mathf.PosMod(Mathf.RadToDeg(Mathf.Atan2(v.X, -v.Y)), 360f);

    private (float Span, float Centre)[] CrewFanWedges()
    {
        var n = _radialCrew.Length;
        if (n == 0) return [];
        var span = (180f - 6f * (n - 1)) / n;
        return Enumerable.Range(0, n).Select(j => (span, 150 + span / 2 + j * (span + 6))).ToArray();
    }

    private void PickRadialHover(float delta)
    {
        var r = _radialCursor.Length();
        var a = RadialAngle(_radialCursor);
        var was = (_radialHover, _radialCrewHover);
        if (_radialFan && r > RadialInner + 4)
        {
            _radialHover = 2;
            _radialCrewHover = -1;
            foreach (var (j, (span, centre)) in CrewFanWedges().Select((w, j) => (j, w)))
                if (Mathf.Abs(Mathf.Wrap(a - centre, -180f, 180f)) <= span / 2 + 3) _radialCrewHover = j;
        }
        else if (r < RadialDead) { _radialHover = -1; _radialCrewHover = -1; }
        else
        {
            _radialHover = (int)Mathf.PosMod(Mathf.Round(a / 120f), 3);
            _radialCrewHover = -1;
            if (_radialHover != 2) _radialFan = false;
        }
        // The fan opens after 0.2 s on Send crew, or at once past the inner ring.
        if (!_radialFan && _radialHover == 2 && _radialInner.Length == 3 && _radialInner[2].State != RadialState.Disabled)
        {
            _radialCrewDwell += delta;
            if (_radialCrewDwell >= .2f || r >= RadialInner - 1) _radialFan = true;
        }
        else if (_radialHover != 2) _radialCrewDwell = 0;
        if (was != (_radialHover, _radialCrewHover)) _radialHoverEase = 0;
        _radialHoverEase = Mathf.MoveToward(_radialHoverEase, 1, delta / .06f);
    }

    // ---- Drawing ---------------------------------------------------------------------------------------------

    private static void CentredText(Control c, Font font, Vector2 centre, string text, float px, Color colour)
    {
        var size = Ui.Px(px);
        var w = font.GetStringSize(text, fontSize: size).X;
        c.DrawString(font, centre + new Vector2(-w / 2, (font.GetAscent(size) - font.GetDescent(size)) / 2), text, fontSize: size, modulate: colour);
    }

    private void DrawRadialMenu(Control c)
    {
        var shown = _radialOpen ? _radialT : _radialCloseT > 0 ? _radialCloseT / .08f : 0;
        if (shown <= 0.01f || _radialInner.Length != 3) return;
        var e = EaseOut(Mathf.Clamp(shown, 0, 1));
        var a = Mathf.Clamp(shown, 0, 1);
        var scale = .85f + .15f * e;
        var centre = ShiftScreen / 2;
        Vector2 Axis(float degrees, float radius) => new Vector2(Mathf.Sin(Mathf.DegToRad(degrees)), -Mathf.Cos(Mathf.DegToRad(degrees))) * Ui.S(radius);
        float Shake(bool on) => on && _radialShake > 0 ? Ui.S(4) * Mathf.Sin(_radialShake * 80) : 0;
        void Sprite(string name, float size, float degrees, Vector2 nudge, Color modulate)
        {
            c.DrawSetTransform(centre + nudge * scale, Mathf.DegToRad(degrees), Vector2.One * scale);
            var s = Ui.S(size);
            c.DrawTextureRect(RadialArt(name), new Rect2(-s / 2, -s / 2, s, s), false, modulate);
        }
        var white = new Color(1, 1, 1, a);
        Sprite("lwf_radial_backdrop_v1", 320, 0, Vector2.Zero, white);
        for (var i = 0; i < 3; i++)
        {
            var entry = _radialInner[i];
            var hover = _radialHover == i;
            var state = hover && entry.State != RadialState.Disabled ? "hover" : entry.State switch { RadialState.Busy => "busy", RadialState.Disabled => "disabled", _ => "normal" };
            var nudge = new Vector2(Mathf.Cos(Mathf.DegToRad(i * 120f)), Mathf.Sin(Mathf.DegToRad(i * 120f))) * Shake(_radialShakeWedge == i);
            Sprite($"lwf_radial_wedge3_{state}_v1", 320, i * 120f, nudge, white);
        }
        var fan = CrewFanWedges();
        if (_radialFan && _radialOpen || _radialFan && _radialCloseT > 0)
            for (var j = 0; j < fan.Length; j++)
            {
                var entry = _radialCrew[j];
                var hover = _radialCrewHover == j;
                var state = hover && entry.State != RadialState.Disabled ? "hover" : entry.State == RadialState.Busy ? "busy" : "normal";
                var tint = entry.State == RadialState.Disabled ? new Color(.45f, .5f, .48f, (hover ? .95f : .85f) * a) : white;
                var nudge = new Vector2(Mathf.Cos(Mathf.DegToRad(fan[j].Centre)), Mathf.Sin(Mathf.DegToRad(fan[j].Centre))) * Shake(_radialShakeCrew == j);
                Sprite($"lwf_radial_crew{fan.Length}_{state}_v1", 500, fan[j].Centre, nudge, tint);
            }
        Sprite("lwf_radial_hub_v1", 108, 0, Vector2.Zero, white);
        c.DrawSetTransform(centre, 0, Vector2.One * scale);

        void Label(RadialEntry entry, float degrees, float radius, bool hover, bool crew, Vector2 nudge)
        {
            var p = Axis(degrees, radius) + nudge;
            var disabled = entry.State == RadialState.Disabled;
            var iconColour = hover && !disabled ? Ui.Bar : disabled ? Ui.BarMuted : entry.IconColour;
            var iconSize = Ui.S(20);
            c.DrawTextureRect(Ui.Icon(entry.Icon), new Rect2(p + new Vector2(-iconSize / 2, -Ui.S(30) - iconSize / 2), new Vector2(iconSize, iconSize)), false, new Color(iconColour, a));
            CentredText(c, Ui.Slab, p, entry.Name, crew ? 12.5f : 13, new Color(disabled ? Ui.BarText : Ui.Ink, a));
            if (crew && entry.Role != "") CentredText(c, Ui.BodySemi, p + new Vector2(0, Ui.S(14)), entry.Role, 9.5f, new Color(disabled ? Ui.BarMuted : Ui.InkMuted, a));
            var statusColour = disabled ? Ui.BarMuted : hover ? Ui.Bar : entry.State == RadialState.Busy ? Ui.Alert : Ui.Teal;
            CentredText(c, Ui.BodyBold, p + new Vector2(0, Ui.S(crew ? 27 : 17)), entry.Status, 10, new Color(statusColour, a));
        }
        for (var i = 0; i < 3; i++)
            Label(_radialInner[i], i * 120f, 104, _radialHover == i && !(_radialFan && i != 2),  false,
                new Vector2(Mathf.Cos(Mathf.DegToRad(i * 120f)), Mathf.Sin(Mathf.DegToRad(i * 120f))) * Shake(_radialShakeWedge == i));
        if (_radialFan)
            for (var j = 0; j < fan.Length; j++)
                Label(_radialCrew[j], fan[j].Centre, 199, _radialCrewHover == j, true,
                    new Vector2(Mathf.Cos(Mathf.DegToRad(fan[j].Centre)), Mathf.Sin(Mathf.DegToRad(fan[j].Centre))) * Shake(_radialShakeCrew == j));

        // The hub: CONTROL, what's picked, the hint, and the needle to the cursor.
        CentredText(c, Ui.CapsFont, new Vector2(0, -Ui.S(14)), "CONTROL", 10, new Color(HiVis, a));
        var picked = _radialFan && _radialCrewHover >= 0 ? _radialCrew[_radialCrewHover].Name : _radialHover >= 0 ? _radialInner[_radialHover].Name : "";
        if (_radialHubNote is not null) picked = ShortNote(_radialHubNote);
        CentredText(c, Ui.Slab, new Vector2(0, Ui.S(2)), picked, 12, new Color(Ui.BarText, a));
        var hint = _radialFan && _radialCrewMore > 0 ? $"+{_radialCrewMore} more" : "release to send";
        CentredText(c, Ui.Body, new Vector2(0, Ui.S(18)), hint, 9.5f, new Color(Ui.BarMuted, a));
        if (_radialCursor.Length() >= 1)
        {
            var tip = _radialCursor.Normalized() * Ui.S(46);
            c.DrawLine(Vector2.Zero, tip, new Color(Ui.Gold, a), Ui.S(3));
            c.DrawCircle(tip, Ui.S(4), new Color(Ui.Gold, a));
        }
        c.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}

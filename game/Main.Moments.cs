using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The day's stories as they happen: a wasp sting, someone stuck in the loo, a tap bursting into a swamp, a band
/// finishing to cheers or boos, a goody two-shoes tidying up. Each appears briefly in the corner; click one to look.
/// Read from the simulation's state twice a second; presentation only.
/// </summary>
public partial class Main
{
    private sealed record Moment(string Icon, string Text, Color Tint, Action Locate);

    private const int MomentsShown = 4;
    private const double MomentSeconds = 18;
    private VBoxContainer? _momentsBox;
    private readonly List<(Control Card, double Age)> _momentCards = [];
    private readonly HashSet<string> _momentsSeen = [];
    private double _momentsPoll;
    private (GameSession? Session, int Attempt) _momentsDay;
    // Who we've told collapsed, and when: only they get a "back on their feet" card, once per collapse.
    private readonly Dictionary<ulong, long> _momentsCollapsed = [];

    private void BuildMoments(CanvasLayer layer, Vector2 size)
    {
        // Top left, under the bar, where it's seen: the day's story matters.
        _momentsBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(Ui.Gutter, Ui.ContentTop), Size = new Vector2(Ui.S(330), 0) };
        _momentsBox.AddThemeConstantOverride("separation", Ui.Px(6));
        layer.AddChild(_momentsBox);
    }

    private void ProcessMoments(double delta)
    {
        if (_momentsBox is null) return;
        for (var i = _momentCards.Count - 1; i >= 0; i--)
        {
            var (card, age) = _momentCards[i];
            age += _session.IsPaused ? 0 : delta;
            _momentCards[i] = (card, age);
            card.Modulate = new Color(1, 1, 1, Mathf.Clamp((float)(MomentSeconds - age) / 2f, 0, 1));
            if (age >= MomentSeconds) { card.QueueFree(); _momentCards.RemoveAt(i); }
        }
        var running = _session.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing;
        _momentsBox.Visible = running && !EyeViewActive;
        // Under the incident cards when there are any, so the two never overlap.
        _momentsBox.Position = new Vector2(Ui.Gutter, _hudAlerts is { Visible: true } alerts ? alerts.Position.Y + alerts.Size.Y + Ui.S(10) : Ui.ContentTop);
        var day = (_session, _session.CapturePreparation()?.Attempt ?? 0);
        if (_momentsDay != day)
        {
            // A new day, a retried weekend or a loaded save: nothing that already happened is news, and the day
            // starts at normal speed.
            _momentsDay = day; _momentsSeen.Clear(); _momentsCollapsed.Clear();
            _host.Clock.RequestedSpeed = RequestedSpeed.OneX;
            foreach (var (card, _) in _momentCards) card.QueueFree();
            _momentCards.Clear();
            foreach (var moment in CurrentMoments()) _momentsSeen.Add(moment.Key);
            return;
        }
        if (!running) return;
        _momentsPoll -= delta;
        if (_momentsPoll > 0) return;
        _momentsPoll = .5;
        foreach (var (key, moment) in CurrentMoments())
            if (_momentsSeen.Add(key))
            {
                ShowMoment(moment);
                if (key.Split(':') is ["collapse", var who, var when]) _momentsCollapsed[ulong.Parse(who)] = long.Parse(when);
            }
        // Recoveries are only news for someone we saw collapse.
        // Anyone no longer down is forgotten, so a later drink of water can't pass for the medic's work.
        foreach (var need in _session.CaptureMedical()?.Needs ?? [])
            if (need.Stage is not (MedicalStage.Collapsed or MedicalStage.Critical) && _momentsCollapsed.Remove(need.AgentId) &&
                need.Stage == MedicalStage.Treated)
                ShowMoment(new("heart-pulse", $"The medic has {NameOf(need.AgentId)} back on their feet", new Color("53bb72"), LocatePerson(need.AgentId)));
    }

    private string NameOf(ulong id) => _session.CapturePreparation()?.People.FirstOrDefault(p => p.AgentId == id)?.Name ?? "Someone";

    private Action LocatePerson(ulong id) => () =>
    {
        if (!_attendeeVisuals.ContainsKey(new EntityId(id))) return;
        SelectAttendee(new EntityId(id));
        _rig.Frame(_attendeeVisuals[new EntityId(id)].Position, Math.Min(_rig.Camera.Size, 30));
    };

    private Action LocateCell(GridCell cell) => () => _rig.Frame(ImmersionPosition(cell), Math.Min(_rig.Camera.Size, 30));

    /// <summary>Every story the current state tells, each with a stable key so it's only told once.</summary>
    private IEnumerable<(string Key, Moment Moment)> CurrentMoments()
    {
        var warm = new Color("f2a65a"); var bad = new Color("df5750"); var good = new Color("53bb72"); var calm = new Color("459ad1");
        foreach (var fault in _session.CaptureFaults()?.Faults ?? [])
        {
            if (fault.Kind == FacilityFaultKind.StuckInToilet)
            {
                var toilet = _session.CaptureToilets().FirstOrDefault(t => t.Id == fault.FacilityId);
                yield return ($"stuck:{fault.Id}", new("door-closed", $"{NameOf(fault.VictimId)} is stuck in the loo!", warm,
                    toilet is null ? LocatePerson(fault.VictimId) : LocateCell(toilet.Cell)));
                if (fault.Stage == FacilityFaultStage.Fixed)
                    yield return ($"freed:{fault.Id}", new("door-closed", $"{NameOf(fault.VictimId)} is out of the loo at last", good,
                        LocatePerson(fault.VictimId)));
            }
            else if (fault.Kind == FacilityFaultKind.BrokenGate)
            {
                yield return ($"gate:{fault.Id}", new("triangle-alert", "The field gate's broken: cows are loose!", warm, LocateCell(CowRules.GateInside)));
                if (fault.Stage != FacilityFaultStage.Active)
                    yield return ($"gatefix:{fault.Id}", new("wrench", "The field gate's mended", good, LocateCell(CowRules.GateInside)));
            }
            else if (fault.Kind == FacilityFaultKind.ChewedCable)
            {
                var utility = fault.FacilityId["cable.".Length..];
                var name = utility switch { "generator" => "the generator", "drinks" => "the bar", _ => "the food van" };
                var spot = _session.CableSpots().Where(s => s.Utility == utility).Select(s => s.Spot).DefaultIfEmpty(CowRules.GateInside).First();
                yield return ($"cable:{fault.Id}", new("zap", $"A cow has chewed through {name}'s cable!", bad, LocateCell(spot)));
                if (fault.Stage != FacilityFaultStage.Active)
                    yield return ($"cablefix:{fault.Id}", new("wrench", $"{char.ToUpper(name[0])}{name[1..]}'s cable is spliced", good, LocateCell(spot)));
            }
            else
            {
                var tap = _session.CaptureWaterPoints().FirstOrDefault(t => t.Id == fault.FacilityId);
                var at = tap is null ? (Action)(() => { }) : LocateCell(tap.Cell);
                yield return ($"tap:{fault.Id}", new("droplet", "A water tap has burst!", warm, at));
                if (fault.Stage != FacilityFaultStage.Active)
                    yield return ($"tapfix:{fault.Id}", new("wrench", fault.Stage == FacilityFaultStage.Bodged ? "The tap's been bodged back into use" : "The tap's mended", good, at));
            }
        }
        foreach (var tap in _session.CaptureWaterPoints())
        {
            var swampy = 0;
            for (var dz = -6; dz <= 6; dz++)
                for (var dx = -6; dx <= 6; dx++)
                    if (_session.GroundStateAt(new GridCell(tap.Cell.X + dx, tap.Cell.Z + dz)) == GroundState.Swamp) swampy++;
            if (swampy >= 8) yield return ($"swamp:{tap.Id}", new("cloud-rain", "The ground by the tap has turned to swamp", calm, LocateCell(tap.Cell)));
        }
        foreach (var need in _session.CaptureMedical()?.Needs ?? [])
        {
            var name = NameOf(need.AgentId);
            if (need.Stage is MedicalStage.Collapsed or MedicalStage.Critical or MedicalStage.Terminal && need.CollapseTick >= 0)
            {
                var why = _session.CollapseCauseOf(need.AgentId) switch
                {
                    CollapseCause.WaspSting => $"{name} was stung by a wasp and collapsed!",
                    CollapseCause.Drink => $"{name} has had far too much to drink and collapsed!",
                    CollapseCause.Injury => $"{name} was hurt in the fight and is down!",
                    CollapseCause.ToiletFumes => $"{name} was overcome by the fumes in a jammed loo!",
                    _ => $"{name} has collapsed in the heat!",
                };
                yield return ($"collapse:{need.AgentId}:{need.CollapseTick}", new("heart-pulse", why, bad, LocatePerson(need.AgentId)));
            }
        }
        // Trouble brewing, before it comes to blows: anger, then arguments, said once a minute while it lasts.
        var disorder = _session.CaptureDisorder()?.People ?? [];
        static string About(IEnumerable<DisorderPerson> people) => people.GroupBy(p => p.Grievance).OrderByDescending(g => g.Count()).First().Key switch
        {
            DisorderGrievance.WaterWait => " about the water queue",
            DisorderGrievance.QueueWait => " about the queues",
            DisorderGrievance.BandDelayed => " about the late band",
            DisorderGrievance.MusicCutoff => " about the music cutting out",
            _ => "",
        };
        var angry = disorder.Where(p => p.Stage == DisorderStage.Agitated).ToArray();
        if (angry.Length >= 2)
            yield return ($"angry:{angry.Min(p => p.StageTick) / 4_800}", new("zap", $"Some people are getting angry{About(angry)}", warm, LocatePerson(angry[0].AgentId)));
        var arguing = disorder.Where(p => p.Stage == DisorderStage.Argument).ToArray();
        if (arguing.Length > 0)
            yield return ($"arguments:{arguing.Min(p => p.StageTick) / 4_800}", new("zap", $"Rising discontent is causing arguments{About(arguing)}", bad, LocatePerson(arguing[0].AgentId)));
        foreach (var person in disorder)
            if (person.Stage == DisorderStage.Fight && person.OpponentId is { } other && person.AgentId < other)
                yield return ($"fight:{person.AgentId}:{person.StageTick}", new("zap", $"{NameOf(person.AgentId)} and {NameOf(other)} are fighting!", bad, LocatePerson(person.AgentId)));
        // Told once per guest per day: it's a trait worth noticing, not every piece they pick up.
        foreach (var piece in _session.CaptureLitter()?.Pieces ?? [])
            if (piece.CarrierId is { } carrier && piece.CarrierId != piece.ProducerId && _session.GuestLabels(carrier).Contains("Goody two-shoes"))
                yield return ($"goody:{carrier}", new("star", $"{NameOf(carrier)} is picking up other people's litter", good, LocatePerson(carrier)));
        if (_session.CaptureLivePerformance() is { } live && _session.CaptureProgramme() is { } programme && programme.CurrentSlot >= 0)
        {
            var act = _session.CurrentFestivalAct?.Name ?? "The band";
            var slot = programme.CurrentSlot;
            if (live.Stage == LiveSetStage.Live)
                yield return ($"set:{slot}", new("music", $"{act} take the stage", calm, LocateCell(new GridCell(96, 150))));
            if (live.Stage == LiveSetStage.Finished && live.EndedTick >= 0)
            {
                // The send-off matches who's actually there at the end, and the applause you hear: big cheers only
                // from a sizeable crowd that enjoyed it, a couple of fans just clap.
                var there = live.SetEndAudienceCount;
                var (text, tint) = live.LastReaction switch
                {
                    "set-finished-applause" when PerformanceApplauseMath.IsEnthusiastic(there, live.SetEndEnjoymentTotal) => ($"{act} finish to big cheers", good),
                    "set-finished-applause" when there >= 5 => ($"{act} finish to warm applause", calm),
                    "set-finished-applause" when there == 1 => ($"{act} finish to a lone cheer", calm),
                    "set-finished-applause" => ($"{act} finish to a smattering of applause from {there} fans", calm),
                    "set-finished-interrupted" => ($"{act}'s set was cut short", bad),
                    "set-finished-muted" => ($"{act} finish to an empty field", calm),
                    "slot-missed-not-ready" => ($"{act} never made it on stage", bad),
                    _ => ($"{act}'s set is over", calm),
                };
                yield return ($"setend:{slot}", new("music", text, tint, LocateCell(new GridCell(96, 150))));
            }
            if (live.LastReaction == "sustained-boo")
                yield return ($"boo:{slot}", new("music", $"The crowd is booing {act}!", bad, LocateCell(new GridCell(96, 150))));
        }
    }

    private void ShowMoment(Moment moment)
    {
        var card = new Button { Text = moment.Text, Icon = Ui.Icon(moment.Icon), Alignment = HorizontalAlignment.Left,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = "Look",
            CustomMinimumSize = new Vector2(0, Ui.S(32)), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        Ui.Style(card, Ui.ButtonKind.Bar, 12.5f, radius: 16);
        var style = Ui.Box(new Color(Ui.Bar, .92f), 16, moment.Tint, 1.5f, 12, 4, shadow: 4, shadowAlpha: .25f);
        foreach (var state in new[] { "normal", "focus" }) card.AddThemeStyleboxOverride(state, style);
        card.AddThemeStyleboxOverride("hover", Ui.Box(Ui.BarRaised, 16, moment.Tint, 1.5f, 12, 4, shadow: 4, shadowAlpha: .25f));
        foreach (var state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" })
            card.AddThemeColorOverride(state, moment.Tint);
        card.AddThemeConstantOverride("icon_max_width", Ui.Px(15));
        card.Pressed += moment.Locate;
        _momentsBox!.AddChild(card);
        _momentsBox.MoveChild(card, 0); // Newest at the top.
        _momentCards.Add((card, 0));
        while (_momentCards.Count > MomentsShown) { _momentCards[0].Card.QueueFree(); _momentCards.RemoveAt(0); }
    }
}

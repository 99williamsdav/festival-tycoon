using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// All-Rounder's view cues and YOU marker, to the designer's spec (assets/source/ui/hands-on-v1/INTEGRATION.md §3A):
/// the V chip in the manager's bottom bar ("Walk the field", with the calls that are yours) and top right in first
/// person ("Manager view"), whose edge flashes gold on V; and in the manager's view the YOU tag over your steward's
/// head, a pulsing hi-vis ring at their feet, and, when they're off screen, an edge badge with the distance that
/// pans the camera to them when clicked.
/// </summary>
public partial class Main
{
    internal const string HandsOnArt = "res://assets/ui/handson/";
    internal static readonly Color ChipEdge = new("3b5a50"), HiVisCue = new("d8f126");

    private CanvasLayer? _cueLayer;
    private CueChip? _managerCue, _firstPersonCue;
    private float _cueFlash;
    private TextureRect? _youTag;
    private YouBadge? _youBadge;
    private MeshInstance3D? _youRing;
    private float _youPulse;
    private (Vector3 From, Vector3 To, float T)? _youPan;

    /// <summary>V was pressed: the chip in the view it lands in flashes its edge gold for 0.2 s.</summary>
    private void FlashHandsOnCue() => _cueFlash = .2f;

    private void ProcessHandsOnCues(bool manager, bool firstPerson, int callsYours)
    {
        var delta = (float)GetProcessDeltaTime();
        _cueFlash = Mathf.Max(0, _cueFlash - delta);
        if (!manager && !firstPerson && _cueLayer is null) { HideYouRing(); return; }
        if (_cueLayer is null)
        {
            _cueLayer = new CanvasLayer { Layer = 19, Name = "HandsOnCues" };
            AddChild(_cueLayer);
            _managerCue = new CueChip("user", HiVisCue) { Name = "WalkTheFieldCue" };
            _firstPersonCue = new CueChip("map", Ui.Gold) { Name = "ManagerViewCue" };
            _youTag = new TextureRect { Name = "YouTag", Texture = GD.Load<Texture2D>(HandsOnArt + "lwf_you_tag_v1.png"), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale, Size = Ui.S(60, 38), MouseFilter = Control.MouseFilterEnum.Ignore };
            _youBadge = new YouBadge { Name = "YouEdgeBadge" };
            _youBadge.Clicked += PanToYou;
            foreach (var node in new Control[] { _youTag, _youBadge, _managerCue, _firstPersonCue }) _cueLayer.AddChild(node);
        }
        var size = GetViewport().GetVisibleRect().Size;
        var flash = _cueFlash / .2f;

        // The chips: right-aligned to the rotate button's left edge (1108) at y 668 in the manager's bar; top right in first person.
        _managerCue!.Visible = manager;
        if (manager)
        {
            _managerCue.Set("Walk the field" + (callsYours > 0 ? $"  ·  radio: {callsYours} call{(callsYours == 1 ? "" : "s")} yours" : ""), flash);
            _managerCue.Position = new Vector2(size.X - Ui.S(1280 - 1108) - _managerCue.Size.X, size.Y - Ui.S(720 - 668));
        }
        _firstPersonCue!.Visible = firstPerson;
        if (firstPerson)
        {
            _firstPersonCue.Set("Manager view", flash);
            _firstPersonCue.Position = new Vector2(size.X - Ui.S(16) - _firstPersonCue.Size.X, Ui.S(16));
        }

        // Pan to your steward, after the badge was clicked: 0.4 s, eased.
        if (_youPan is { } pan)
        {
            var t = Mathf.Min(1, pan.T + delta / .4f);
            _rig.FocusOn(pan.From.Lerp(pan.To, Mathf.Ease(t, -2f)));
            _youPan = t >= 1 ? null : pan with { T = t };
        }
        ProcessYouMarker(manager && !EyeViewActive, size, delta);
    }

    private void ProcessYouMarker(bool shown, Vector2 size, float delta)
    {
        Node3D? body = shown && _session.PlayerStewardId is { } id && _attendeeVisuals.TryGetValue(new EntityId(id), out var root) ? root : null;
        var camera = GetViewport().GetCamera3D();
        if (body is null || camera is null)
        {
            _youTag!.Visible = false; _youBadge!.Visible = false; HideYouRing();
            return;
        }
        // The ground ring at their feet, 1.2 m across, pulsing 1.0–1.08 at 1 Hz.
        if (_youRing is null || !IsInstanceValid(_youRing) || _youRing.GetParent() != body)
        {
            if (_youRing is not null && IsInstanceValid(_youRing)) _youRing.QueueFree();
            _youRing = new MeshInstance3D
            {
                Name = "HandsOnYouRing", Position = new Vector3(0, .02f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Mesh = new PlaneMesh { Size = new Vector2(1.2f, 1.2f) },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoTexture = GD.Load<Texture2D>(HandsOnArt + "lwf_you_ring_v1.png"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled, RenderPriority = 1,
                },
            };
            body.AddChild(_youRing);
        }
        _youPulse = (_youPulse + delta) % 1f;
        var pulse = 1f + .04f * (1f - Mathf.Cos(Mathf.Tau * _youPulse));
        _youRing.Visible = true;
        _youRing.Scale = new Vector3(pulse, 1, pulse);

        // The head, on screen: the tag's pointer tip sits 4 above it, its centre 22 above.
        var head = HeadPoint(body);
        var behind = camera.IsPositionBehind(head);
        var screen = camera.UnprojectPosition(head);
        var inset = new Rect2(Ui.S(24), Ui.TopBar + Ui.S(24), size.X - Ui.S(48), size.Y - Ui.TopBar - Ui.S(64) - Ui.S(48));
        var onScreen = !behind && inset.HasPoint(screen);
        _youTag!.Visible = onScreen;
        _youBadge!.Visible = !onScreen;
        if (onScreen)
        {
            _youTag.Size = Ui.S(60, 38);
            _youTag.Position = screen - new Vector2(Ui.S(30), Ui.S(22) + Ui.S(19));
            return;
        }
        // Off screen: the badge's centre clamped to the inset, its arrow pointing from the screen's centre towards you.
        var centre = size / 2;
        var away = behind ? centre - screen : screen - centre;
        var margin = Ui.S(25);
        var at = new Vector2(Mathf.Clamp(centre.X + away.X, inset.Position.X + margin, inset.End.X - margin),
            Mathf.Clamp(centre.Y + away.Y, inset.Position.Y + margin, inset.End.Y - margin));
        var feet = body.GlobalPosition;
        var metres = Mathf.RoundToInt(new Vector2(feet.X - _rig.Focus.X, feet.Z - _rig.Focus.Z).Length());
        _youBadge.Place(at, away.Angle(), $"You · {metres} m");
    }

    private void HideYouRing()
    {
        if (_youRing is not null && IsInstanceValid(_youRing)) _youRing.Visible = false;
    }

    /// <summary>The top of the steward's head: their head bone a little up, or 1.85 m above their feet on a still body.</summary>
    private static Vector3 HeadPoint(Node3D body)
    {
        var skeleton = body.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().FirstOrDefault();
        var bone = skeleton?.FindBone("Head") ?? -1;
        return skeleton is not null && bone >= 0
            ? skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(bone).Origin + new Vector3(0, .3f, 0)
            : body.GlobalPosition + new Vector3(0, 1.85f, 0);
    }

    /// <summary>The edge badge was clicked: the camera pans to centre on your steward.</summary>
    private void PanToYou()
    {
        if (_session.PlayerStewardId is not { } id || !_attendeeVisuals.TryGetValue(new EntityId(id), out var body)) return;
        var to = new Vector3(Mathf.Clamp(body.GlobalPosition.X, -CameraRig.PanLimit, CameraRig.PanLimit), 0,
            Mathf.Clamp(body.GlobalPosition.Z, -CameraRig.PanLimit, CameraRig.PanLimit));
        _youPan = (_rig.Focus, to, 0f);
    }
}

/// <summary>
/// The view chip: 36 tall, radius 8, BarRaised with a 1-unit edge (gold while it flashes), the V key cap at
/// (+7, +7), the icon 18 at (+36, +9) and the label in body bold 14 from +60; 16 after the label.
/// </summary>
internal sealed partial class CueChip : Control
{
    private readonly Texture2D _icon;
    private readonly Color _iconColour;
    private string _label = "";
    private float _flash;

    public CueChip(string icon, Color iconColour)
    {
        _icon = Ui.Icon(icon); _iconColour = iconColour;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Set(string label, float flash)
    {
        if (label == _label && Mathf.IsEqualApprox(flash, _flash) && Size.Y > 0) return;
        _label = label; _flash = flash;
        Size = new Vector2(Ui.S(60) + Ui.BodyBold.GetStringSize(label, HorizontalAlignment.Left, -1, Ui.Px(14)).X + Ui.S(16), Ui.S(36));
        QueueRedraw();
    }

    public override void _Draw()
    {
        var box = Ui.Box(Ui.BarRaised, 8, Main.ChipEdge.Lerp(Ui.Gold, _flash), _flash > 0 ? 2 : 1);
        DrawStyleBox(box, new Rect2(Vector2.Zero, Size));
        var key = new Rect2(Ui.S(7, 7), Ui.S(22, 22));
        DrawStyleBox(Ui.Box(Ui.Bar, 5), key);
        var keySize = Ui.Px(14);
        var keyWidth = Ui.BodyBold.GetStringSize("V", HorizontalAlignment.Left, -1, keySize).X;
        DrawString(Ui.BodyBold, key.Position + new Vector2((key.Size.X - keyWidth) / 2, (key.Size.Y + Ui.BodyBold.GetAscent(keySize) - Ui.BodyBold.GetDescent(keySize)) / 2),
            "V", HorizontalAlignment.Left, -1, keySize, Ui.BarText);
        DrawTextureRect(_icon, new Rect2(Ui.S(36, 9), Ui.S(18, 18)), false, _iconColour);
        var size = Ui.Px(14);
        DrawString(Ui.BodyBold, new Vector2(Ui.S(60), (Size.Y + Ui.BodyBold.GetAscent(size) - Ui.BodyBold.GetDescent(size)) / 2), _label,
            HorizontalAlignment.Left, -1, size, Ui.BarText);
    }
}

/// <summary>
/// The off-screen badge: the upright disc (50), the arrow (84) turned towards you, and "You · 85 m" 34 below in body
/// bold 12 with a dark shadow. Clicking the disc pans the camera to you.
/// </summary>
internal sealed partial class YouBadge : Control
{
    private static readonly Texture2D Disc = GD.Load<Texture2D>(Main.HandsOnArt + "lwf_you_edge_disc_v1.png");
    private static readonly Texture2D Arrow = GD.Load<Texture2D>(Main.HandsOnArt + "lwf_you_edge_arrow_v1.png");
    private float _angle;
    private string _label = "";
    public event Action? Clicked;

    public YouBadge()
    {
        MouseFilter = MouseFilterEnum.Stop; MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = "Your steward: click to centre the view on them";
    }

    public void Place(Vector2 centre, float angle, string label)
    {
        Size = Ui.S(50, 50);
        Position = centre - Size / 2;
        if (Mathf.IsEqualApprox(angle, _angle) && label == _label) return;
        _angle = angle; _label = label;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) { Clicked?.Invoke(); AcceptEvent(); }
    }

    public override void _Draw()
    {
        var middle = Size / 2;
        DrawSetTransform(middle, _angle, Vector2.One);
        DrawTextureRect(Arrow, new Rect2(-Ui.S(42, 42), Ui.S(84, 84)), false);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        DrawTextureRect(Disc, new Rect2(Vector2.Zero, Size), false);
        var size = Ui.Px(12);
        var width = Ui.BodyBold.GetStringSize(_label, HorizontalAlignment.Left, -1, size).X;
        var baseline = new Vector2(middle.X - width / 2, middle.Y + Ui.S(34) + (Ui.BodyBold.GetAscent(size) - Ui.BodyBold.GetDescent(size)) / 2);
        DrawString(Ui.BodyBold, baseline + Ui.S(1, 1), _label, HorizontalAlignment.Left, -1, size, new Color(0, 0, 0, .75f));
        DrawString(Ui.BodyBold, baseline, _label, HorizontalAlignment.Left, -1, size, Ui.BarText);
    }
}

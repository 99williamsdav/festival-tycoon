using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;

namespace Festival.Game;

/// <summary>
/// The first-person arms, to the designer's spec (assets/source/characters/steward-fp-arms-v1/INTEGRATION.md): the
/// radio in the left hand all shift, bobbing with the walk, lagging the look a little and raised to talk when a call
/// comes in, its screen pulsing; and one right hand that eases in for a held action (calm, door or litter pose) and
/// out again once it reads. Each GLB sits at identity under a fixed 72° viewmodel camera, authored in its space,
/// drawn in a second pass (its own transparent viewport under the HUD) so the arms never clip into walls or the crowd.
/// </summary>
public partial class Main
{
    private CanvasLayer? _armsLayer;
    private Camera3D? _armsCamera;
    private Node3D? _radioArm;
    private readonly Dictionary<string, Node3D> _actionHands = new(StringComparer.Ordinal);
    private StandardMaterial3D? _radioScreen;
    private float _radioScreenEnergy = 1;
    private Node3D? _litterCup;
    // Walk bob: the step phase, advanced by distance walked; the last camera spot to measure it from.
    private float _stepPhase, _breath;
    private Vector3 _lastEye;
    private Vector2 _sway;
    // Talk raise: 0 down to 1 up; how long it holds up.
    private float _talk, _talkHold;
    private bool _talkUrgent;
    // The right hand: which pose, how far in (0 rest, 1 in), what it's doing.
    private enum HandMove { Hidden, In, Holding, Pause, Out }
    private HandMove _handMove;
    private string? _handPose, _handNext;
    private float _handT, _handPause;
    private long _handHeldStart = -1;

    private static readonly Vector3 HandRest = new(0.12f, -0.55f, 0.15f);
    private const float StrideMetres = 0.75f;

    private Node3D LoadArm(string file)
    {
        var arm = GD.Load<PackedScene>($"res://assets/characters/fp/{file}").Instantiate<Node3D>();
        foreach (var mesh in arm.FindChildren("*", "MeshInstance3D", true, false))
            ((MeshInstance3D)mesh).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        _armsCamera!.AddChild(arm);
        return arm;
    }

    /// <summary>The viewmodel pass: its own world with a camera matching the eye's, a sun and some sky light, over the field.</summary>
    private void BuildArmsPass()
    {
        _armsLayer = new CanvasLayer { Layer = 20, Name = "StewardArmsPass" };
        AddChild(_armsLayer);
        var container = new SubViewportContainer { Stretch = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        container.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _armsLayer.AddChild(container);
        var viewport = new SubViewport { TransparentBg = true, OwnWorld3D = true, HandleInputLocally = false, Msaa3D = GetViewport().Msaa3D };
        container.AddChild(viewport);
        _armsCamera = new Camera3D { Fov = 72, Near = 0.05f, Far = 10, Current = true };
        viewport.AddChild(_armsCamera);
        viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 35, 0), LightEnergy = 1.1f, ShadowEnabled = false });
        viewport.AddChild(new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor, AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("cfd9e6"), AmbientLightEnergy = .55f, TonemapMode = Godot.Environment.ToneMapper.Filmic,
        } });
    }

    private void BuildStewardShiftArms()
    {
        ClearStewardShiftArms();
        BuildArmsPass();
        _radioArm = LoadArm("lwf_fp_arm_left_radio_v1.glb");
        _radioArm.Name = "StewardRadioArm";
        // The radio's screen gets its own material, so its glow can pulse.
        foreach (var node in _radioArm.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mesh = (MeshInstance3D)node;
            for (var i = 0; i < (mesh.Mesh?.GetSurfaceCount() ?? 0); i++)
                if (mesh.Mesh!.SurfaceGetMaterial(i) is StandardMaterial3D material &&
                    (material.ResourceName.Contains("RadioScreen", StringComparison.Ordinal) || mesh.Name.ToString().Contains("RadioScreen", StringComparison.Ordinal)))
                {
                    _radioScreen = (StandardMaterial3D)material.Duplicate();
                    _radioScreen.EmissionEnabled = true;
                    _radioScreenEnergy = Mathf.Max(0.01f, _radioScreen.EmissionEnergyMultiplier);
                    mesh.SetSurfaceOverrideMaterial(i, _radioScreen);
                }
        }
        foreach (var pose in new[] { "calm", "door", "litter" })
        {
            var hand = LoadArm($"lwf_fp_arm_right_{pose}_v1.glb");
            hand.Name = $"StewardHand_{pose}";
            hand.Visible = false;
            _actionHands[pose] = hand;
        }
        _litterCup = _actionHands["litter"].FindChild("LWF_FPLitterCup", true, false) as Node3D;
        _handMove = HandMove.Hidden; _handPose = _handNext = null; _handT = 0; _handHeldStart = -1;
        _talk = 0; _talkHold = 0; _sway = Vector2.Zero;
        _lastEye = _eyeCamera!.GlobalPosition;
    }

    private void ClearStewardShiftArms()
    {
        _armsLayer?.QueueFree(); _armsLayer = null; _armsCamera = null;
        _radioArm = null;
        _actionHands.Clear();
        _radioScreen = null; _litterCup = null;
    }

    /// <summary>Calm, break up, escort and directions use the open palm; doors, taps and a hand up the reaching one; litter and bins the litter one.</summary>
    private static string HandPose(StewardActionKind kind) => kind switch
    {
        StewardActionKind.PickLitter or StewardActionKind.EmptyBin or StewardActionKind.SpreadStraw => "litter",
        StewardActionKind.FreeLoo or StewardActionKind.StandBy or StewardActionKind.RadioMedic or StewardActionKind.BodgeTap or StewardActionKind.Drink => "door",
        _ => "calm",
    };

    /// <summary>Raises the radio to talk as a call comes in or is answered; urgent calls tint the screen alert.</summary>
    private void RaiseShiftRadio(bool urgent)
    {
        _talkHold = urgent ? 3.5f : 2.5f;
        _talkUrgent = urgent;
    }

    /// <summary>A look turn: the radio arm lags it by up to 2°.</summary>
    private void SwayShiftArms(Vector2 relative)
    {
        var degrees = relative * Mathf.RadToDeg(ShiftMouseRadians);
        _sway = new Vector2(Mathf.Clamp(_sway.X + degrees.X, -2, 2), Mathf.Clamp(_sway.Y + degrees.Y, -2, 2));
    }

    private void AnimateStewardShiftArms(float delta)
    {
        if (_radioArm is null || _eyeCamera is null) return;
        if (_armsLayer is not null) _armsLayer.Visible = !StewardShiftReportShown && _shiftMenu?.Visible != true;
        var shift = _session.CaptureStewardShift();
        // Walk bob from the distance actually walked; idle, a slow breath.
        var eye = _eyeCamera.GlobalPosition;
        var moved = new Vector2(eye.X - _lastEye.X, eye.Z - _lastEye.Z).Length();
        _lastEye = eye;
        var walking = moved > 0.0005f && !_session.IsPaused;
        var running = walking && shift?.Run == true;
        if (walking) _stepPhase = (_stepPhase + moved / StrideMetres) % 2f;
        _breath += delta;
        var amp = running ? 2f : walking ? 1f : 0f;
        var bob = new Vector3(0.004f * Mathf.Cos(Mathf.Pi * _stepPhase) * amp,
            walking ? 0.006f * Mathf.Sin(Mathf.Tau * _stepPhase) * amp : 0.002f * Mathf.Sin(Mathf.Tau * _breath / 3.5f), 0);
        // Look sway returns at about 8 a second.
        _sway = _sway.Lerp(Vector2.Zero, Mathf.Min(1, 8 * delta));
        // Talk raise: up over 0.25 s (ease-out), held while the call shows, down over 0.3 s.
        if (_talkHold > 0) { _talkHold -= delta; _talk = Mathf.MoveToward(_talk, 1, delta / .25f); }
        else _talk = Mathf.MoveToward(_talk, 0, delta / .3f);
        var raise = new Vector3(0.03f, 0.05f, 0.04f) * EaseOut(_talk);
        _radioArm.Position = bob + raise;
        _radioArm.RotationDegrees = new Vector3(_sway.Y, _sway.X, 0);
        if (_radioScreen is not null)
        {
            var pulse = _talkHold > 0 ? 1f + 0.4f * Mathf.Sin(Mathf.Tau * 2 * _breath) : 1f;
            _radioScreen.EmissionEnergyMultiplier = _radioScreenEnergy * pulse;
            _radioScreen.Emission = _talkHold > 0 && _talkUrgent ? Ui.Alert : _radioScreen.AlbedoColor.Lightened(.2f);
        }

        // The right hand follows the hold.
        var held = shift?.Action;
        if (held is not null && held.StartedTick != _handHeldStart)
        {
            _handHeldStart = held.StartedTick;
            var pose = HandPose(held.Kind);
            if (_handMove == HandMove.Hidden || _handPose == pose && _handMove != HandMove.Out) { _handPose = pose; _handMove = HandMove.In; }
            else { _handNext = pose; _handMove = HandMove.Out; }
            if (_litterCup is not null) _litterCup.Visible = false;
        }
        else if (held is null && _handMove is HandMove.In or HandMove.Holding)
        {
            var last = shift?.LastOutcome;
            var done = last is { Completed: true } && last.Tick >= _handHeldStart;
            if (done)
            {
                _handMove = HandMove.Pause; _handPause = .25f;
                if (last!.Success && _litterCup is not null && _handPose == "litter") _litterCup.Visible = true;
            }
            else _handMove = HandMove.Out;
        }
        switch (_handMove)
        {
            case HandMove.In:
                _handT = Mathf.MoveToward(_handT, 1, delta / .2f);
                if (_handT >= 1) _handMove = HandMove.Holding;
                break;
            case HandMove.Pause:
                _handPause -= delta;
                if (_handPause <= 0) _handMove = HandMove.Out;
                break;
            case HandMove.Out:
                _handT = Mathf.MoveToward(_handT, 0, delta / .15f);
                if (_handT <= 0)
                {
                    if (_handNext is not null) { _handPose = _handNext; _handNext = null; _handMove = HandMove.In; }
                    else { _handMove = HandMove.Hidden; _handPose = null; }
                }
                break;
        }
        foreach (var (pose, hand) in _actionHands)
        {
            hand.Visible = pose == _handPose && _handT > 0;
            if (!hand.Visible) continue;
            // In with an ease-out cubic, out with an ease-in cubic.
            var e = _handMove == HandMove.Out ? 1 - Mathf.Pow(1 - _handT, 3) : EaseOut(_handT);
            hand.Position = HandRest.Lerp(Vector3.Zero, e) + (_handMove == HandMove.Holding ? bob * .5f : Vector3.Zero);
        }
    }
}

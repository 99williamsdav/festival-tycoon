using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.Linq;
using CryptographicRandom = System.Security.Cryptography.RandomNumberGenerator;

namespace Festival.Game;

/// <summary>
/// Prototype steward shift: the player is one steward on the ground while the festival runs itself. It rides
/// the eye view (eye height, hidden labels, own body hidden, fog, sound at the head) with mouse-look and WASD.
/// The sim moves the steward from a held input command; the camera follows the interpolated position, so it
/// glides between ticks. The tycoon HUD is hidden for a crosshair, the clock, who's in reach and a radio strip.
/// Its saves have their own folder. Interactions, scoring and the radio come with the mechanics pass.
/// </summary>
public partial class Main
{
    /// <summary>The campaign's host, set aside while a shift has the session.</summary>
    private SessionHost? _campaignHost;
    private bool _shiftActive;
    private float _shiftYaw, _shiftPitch;
    private bool _shiftLooking;
    private (int X, int Z, bool Run, int Facing) _shiftSent;
    private long _shiftSentTick = -1;
    private CanvasLayer? _shiftHud;
    private Label? _shiftClock, _shiftPrompt, _shiftPromptDetail, _shiftRadio, _shiftKeys;
    private Control? _shiftCrosshair;
    private PanelContainer? _shiftMenu;
    private Label? _shiftMenuTitle;
    private Button? _shiftResume;
    private Node3D? _shiftArm;
    private CanvasLayer? _hudLayer;

    private bool StewardShiftActive => _shiftActive;
    private string StewardShiftSaveDirectory => ProjectSettings.GlobalizePath("user://saves/r0-build-v65-steward-shift");
    private const float ShiftMouseRadians = 0.0024f;

    /// <summary>The title screen's way in: a fresh self-running festival with the player as its second steward.</summary>
    private void StartStewardShift()
    {
        _campaignHost ??= _host;
        if (_host is not null) _menuCampaignIds.Add(_session.CampaignId.Value);
        ulong seed;
        do seed = BitConverter.ToUInt64(CryptographicRandom.GetBytes(sizeof(ulong)));
        while (seed == 0 || _menuCampaignIds.Contains(seed));
        _menuCampaignIds.Add(seed);
        var shift = GameSession.CreateStewardShift(seed);
        _host = new SessionHost(shift, StewardShiftSaveDirectory, _saveCompatibility);
        SwitchToFestival(shift);
        _host.Clock.RequestedSpeed = RequestedSpeed.OneX;
        StartMenu.Close();
        EnterStewardShiftView();
        GD.Print($"STEWARD_SHIFT_STARTED id={_session.CampaignId.Value} seed={seed} player={_session.PlayerStewardId}");
    }

    private void EnterStewardShiftView()
    {
        if (_session.PlayerStewardId is not { } id) return;
        _shiftActive = true;
        if (_hudLayer is not null) _hudLayer.Visible = false;
        EnterEyeView(new EntityId(id));
        if (_eyeOverlay is not null) _eyeOverlay.Visible = false;
        // Turn the camera to the steward's saved facing: sim 0° looks down +Z, the camera's yaw 0 down −Z.
        _shiftYaw = Mathf.DegToRad(_session.CaptureStewardShift()!.FacingDegrees + 180);
        _shiftPitch = Mathf.DegToRad(-4);
        _shiftSent = (0, 0, false, -1);
        BuildStewardShiftHud();
        BuildStewardShiftArm();
        PushShiftRadio("Control: you're on the second post. Keep an eye on the crowd.");
        SetShiftMenu(null);
    }

    private void ExitStewardShiftView()
    {
        _shiftActive = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _shiftLooking = false;
        if (EyeViewActive) ExitEyeView();
        if (_shiftArm is not null) { _shiftArm.QueueFree(); _shiftArm = null; }
        if (_shiftHud is not null) { _shiftHud.QueueFree(); _shiftHud = null; }
        _shiftClock = _shiftPrompt = _shiftPromptDetail = _shiftRadio = _shiftKeys = _shiftMenuTitle = null;
        _shiftCrosshair = null; _shiftMenu = null; _shiftResume = null;
        ResetFinanceFeedback();
        if (_hudLayer is not null) _hudLayer.Visible = true;
    }

    /// <summary>Back to the title: the shift's host and saves are left behind and the campaign's host returns.</summary>
    private void ReturnFromStewardShift()
    {
        _host.SaveMilestone("Shift", force: false);
        ExitStewardShiftView();
        _host = _campaignHost!;
        _campaignHost = null;
        _newCampaignOnEnter = true;
        BuildStartSplash();
    }

    private void BuildStewardShiftHud()
    {
        _shiftHud?.QueueFree();
        _shiftHud = new CanvasLayer { Layer = 21, Name = "StewardShiftHud" };
        AddChild(_shiftHud);
        var size = GetViewport().GetVisibleRect().Size;
        Label Text(string text, int px, Color colour)
        {
            var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
            label.AddThemeFontSizeOverride("font_size", Ui.Px(px));
            label.AddThemeFontOverride("font", Ui.BodySemi);
            label.AddThemeColorOverride("font_color", colour);
            label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, .75f));
            label.AddThemeConstantOverride("outline_size", Ui.Px(5));
            return label;
        }
        Label Centred(string text, int px, Color colour, float y)
        {
            var label = Text(text, px, colour);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Position = new Vector2(0, y); label.Size = new Vector2(size.X, Ui.S(px + 10));
            _shiftHud!.AddChild(label);
            return label;
        }
        _shiftCrosshair = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Position = size / 2, Size = Vector2.Zero };
        _shiftCrosshair.Draw += () =>
        {
            var c = _shiftCrosshair!;
            var arm = Ui.S(7); var gap = Ui.S(3); var width = Mathf.Max(1.5f, Ui.S(2));
            var shade = new Color(0, 0, 0, .55f); var ink = new Color(1, 1, 1, .9f);
            foreach (var (colour, w) in new[] { (shade, width + 2), (ink, width) })
            {
                c.DrawLine(new Vector2(-gap - arm, 0), new Vector2(-gap, 0), colour, w);
                c.DrawLine(new Vector2(gap, 0), new Vector2(gap + arm, 0), colour, w);
                c.DrawLine(new Vector2(0, -gap - arm), new Vector2(0, -gap), colour, w);
                c.DrawLine(new Vector2(0, gap), new Vector2(0, gap + arm), colour, w);
            }
        };
        _shiftHud.AddChild(_shiftCrosshair);
        _shiftClock = Centred("", 20, Colors.White, Ui.S(14));
        Centred("STEWARD SHIFT  ·  PROTOTYPE", 11, new Color(1, 1, 1, .7f), Ui.S(42));
        // Who's in reach and in front: a name now, the actions later.
        _shiftPrompt = Centred("", 19, new Color("fff1b8"), size.Y / 2 + Ui.S(60));
        _shiftPromptDetail = Centred("", 12, new Color(1, 1, 1, .78f), size.Y / 2 + Ui.S(86));
        // The radio strip: placeholder messages until the radio calls exist.
        var radio = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(Ui.Gutter, size.Y - Ui.S(58)), Size = new Vector2(Ui.S(520), Ui.S(40)) };
        radio.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.06f, 0.1f, 0.09f, .72f), CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, ContentMarginLeft = Ui.S(12), ContentMarginRight = Ui.S(12), ContentMarginTop = Ui.S(6), ContentMarginBottom = Ui.S(6),
            BorderWidthLeft = Ui.Px(3), BorderColor = new Color("e8f23a") });
        _shiftRadio = Text("", 13, Colors.White);
        _shiftRadio.ClipText = true;
        radio.AddChild(_shiftRadio);
        _shiftHud.AddChild(radio);
        var keys = _shiftKeys = Text("WASD walk  ·  Shift run  ·  Esc menu  ·  P pause", 12, new Color(1, 1, 1, .7f));
        keys.HorizontalAlignment = HorizontalAlignment.Right;
        keys.Position = new Vector2(size.X - Ui.S(360) - Ui.Gutter, size.Y - Ui.S(46)); keys.Size = new Vector2(Ui.S(360), Ui.S(24));
        _shiftHud.AddChild(keys);

        _shiftMenu = new PanelContainer { Visible = false };
        _shiftMenu.AddThemeStyleboxOverride("panel", Ui.Sheet(24, 22));
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", Ui.Px(10));
        _shiftMenu.AddChild(box);
        _shiftMenuTitle = HudLabel("", 21);
        box.AddChild(_shiftMenuTitle);
        _shiftResume = ButtonText("Back on shift", () => SetShiftMenu(null));
        box.AddChild(_shiftResume);
        box.AddChild(ButtonText("Return to menu", ReturnFromStewardShift));
        _shiftMenu.Size = new Vector2(Ui.S(320), 0);
        _shiftMenu.Position = new Vector2((size.X - Ui.S(320)) / 2, size.Y * .3f);
        _shiftHud.AddChild(_shiftMenu);
    }

    /// <summary>Hook for the radio: the newest call replaces the strip's text.</summary>
    private void PushShiftRadio(string message)
    {
        if (_shiftRadio is not null) _shiftRadio.Text = "RADIO  ·  " + message;
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
        if (open != _session.IsPaused && !over) _host.Submit(new SetPausedCommand(open));
        _shiftLooking = !open;
        Input.MouseMode = open || DisplayServer.GetName() == "headless" ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        if (_shiftCrosshair is not null) _shiftCrosshair.Visible = !open;
    }

    /// <summary>A hi-vis forearm holding a radio in the corner of the view, until the designer's hands arrive.</summary>
    private void BuildStewardShiftArm()
    {
        _shiftArm?.QueueFree();
        _shiftArm = new Node3D { Name = "StewardArm" };
        var hiVis = new StandardMaterial3D { AlbedoColor = new Color("b4c62a"), Roughness = .85f };
        var radio = new StandardMaterial3D { AlbedoColor = new Color("2b2f33"), Roughness = .6f };
        var band = new StandardMaterial3D { AlbedoColor = new Color("b9bdc2"), Metallic = .5f, Roughness = .35f };
        var skin = new StandardMaterial3D { AlbedoColor = new Color("d8a383"), Roughness = .8f };
        MeshInstance3D Part(Mesh mesh, Material material, Vector3 at)
        {
            var part = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = at, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _shiftArm!.AddChild(part); return part;
        }
        // A sleeve running in from the bottom right corner, its reflective band, the hand, and the radio in it.
        Part(new CylinderMesh { TopRadius = .027f, BottomRadius = .033f, Height = .32f }, hiVis, Vector3.Zero);
        Part(new CylinderMesh { TopRadius = .029f, BottomRadius = .03f, Height = .025f }, band, new Vector3(0, .05f, 0));
        Part(new SphereMesh { Radius = .03f, Height = .06f }, skin, new Vector3(0, .18f, 0)).Scale = new Vector3(1.1f, 1.3f, .75f);
        Part(new BoxMesh { Size = new Vector3(.042f, .1f, .026f) }, radio, new Vector3(0, .235f, .012f));
        Part(new CylinderMesh { TopRadius = .005f, BottomRadius = .005f, Height = .05f }, radio, new Vector3(.012f, .305f, .012f));
        _shiftArm.Position = new Vector3(.21f, -.26f, -.4f);
        _shiftArm.RotationDegrees = new Vector3(-72, 20, 0);
        _eyeCamera!.AddChild(_shiftArm);
    }

    /// <summary>Before the sim ticks: what the keys and mouse ask for, sent as the held move command when it changes.</summary>
    private void ProcessStewardShiftInput()
    {
        if (!_shiftActive) return;
        if (_session.PlayerStewardId is null)
        {
            // Closing time (or a death) ends the shift; the steward walks home with everyone else.
            if (_shiftMenu?.Visible == false)
            {
                SetShiftMenu(_session.PreparedStatus == PreparationStatus.Failed ? "The day's been stopped" : "Shift over · thanks, everyone");
                PushShiftRadio(_session.PreparedStatus == PreparationStatus.Failed ? "Control: all stewards stand down." : "Control: that's closing. See them out safely.");
                _shiftKeys!.Text = "";
                if (_shiftPrompt is not null) _shiftPrompt.Text = _shiftPromptDetail!.Text = "";
            }
            return;
        }
        int forward = 0, right = 0;
        var running = false;
        if (_shiftLooking && DisplayServer.GetName() != "headless")
        {
            forward = (Input.IsKeyPressed(Key.W) ? 1 : 0) - (Input.IsKeyPressed(Key.S) ? 1 : 0);
            right = (Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0);
            running = Input.IsKeyPressed(Key.Shift);
        }
        SendShiftInput(forward, right, running);
    }

    /// <summary>Sends WASD (forward/right, −1 to 1) as a world direction; key changes go at once, turning at most every few ticks.</summary>
    private void SendShiftInput(int forward, int right, bool running)
    {
        // Camera forward is (−sin yaw, −cos yaw) on the ground; right is (cos yaw, −sin yaw).
        var (sin, cos) = (Mathf.Sin(_shiftYaw), Mathf.Cos(_shiftYaw));
        var x = -sin * forward + cos * right;
        var z = -cos * forward - sin * right;
        var length = Mathf.Sqrt(x * x + z * z);
        var dx = length < .01f ? 0 : (int)Mathf.Round(x / length * 1000);
        var dz = length < .01f ? 0 : (int)Mathf.Round(z / length * 1000);
        var facing = ((int)Mathf.Round(Mathf.RadToDeg(_shiftYaw) + 180) % 360 + 360) % 360;
        var keysChanged = (dx == 0 && dz == 0) != (_shiftSent.X == 0 && _shiftSent.Z == 0) || running != _shiftSent.Run || _shiftSent.Facing < 0;
        var turned = Math.Abs(dx - _shiftSent.X) + Math.Abs(dz - _shiftSent.Z) > 40 || AngleGap(facing, _shiftSent.Facing) >= 4;
        if (!keysChanged && !(turned && _session.CurrentTick - _shiftSentTick >= 6)) return;
        if (!_host.Submit(new SetPlayerMoveCommand(dx, dz, running, facing)).IsAccepted) return;
        _shiftSent = (dx, dz, running, facing);
        _shiftSentTick = _session.CurrentTick;
    }

    private static int AngleGap(int a, int b) { var d = Math.Abs(a - b) % 360; return d > 180 ? 360 - d : d; }

    /// <summary>After the sim ticks: the camera to the steward's interpolated head, then the HUD.</summary>
    private void ProcessStewardShiftView()
    {
        if (!_shiftActive || _eyeCamera is null) return;
        if (EyeViewActive && _session.PlayerStewardId is { } id && _attendeeVisuals.TryGetValue(new EntityId(id), out var visual))
        {
            var at = _foundationPresentation.Sample(new EntityId(id), _host.Clock.InterpolationFraction);
            _eyeCamera.GlobalPosition = new Vector3((float)(at.XMillimetres / 1000), EyeHeight(visual) + .04f, (float)(at.ZMillimetres / 1000));
            _eyeCamera.Rotation = new Vector3(_shiftPitch, _shiftYaw, 0);
        }
        var p = _session.CapturePreparation()!;
        var day = _session.PreparedEditionDurationTicks;
        var elapsed = _session.CurrentTick - p.StartedTick;
        if (_shiftClock is not null)
            _shiftClock.Text = (elapsed >= day ? "Closing time" : $"{FestivalClockText(elapsed)} / {FestivalClockText(day)}") + (_session.IsPaused ? "  ·  PAUSED" : "");
        if (_shiftPrompt is null || _session.PlayerStewardId is null) return;
        var facing = ((int)Mathf.Round(Mathf.RadToDeg(_shiftYaw) + 180) % 360 + 360) % 360;
        var near = _session.StewardReach(facing).FirstOrDefault();
        _shiftPrompt.Text = near?.Name ?? "";
        _shiftPromptDetail!.Text = near is null ? "" : $"{RoleName(near.Role)}  ·  {near.DistanceMillimetres / 1000f:0.0} m";
    }

    private static string RoleName(ProtectedPersonRole role) => role switch
    {
        ProtectedPersonRole.Guest => "guest", ProtectedPersonRole.Staff => "crew", ProtectedPersonRole.Performer => "band", _ => "",
    };

    /// <summary>Shift keys and mouse-look. True when the event was the shift's.</summary>
    private bool HandleStewardShiftInput(InputEvent inputEvent)
    {
        if (!_shiftActive) return false;
        switch (inputEvent)
        {
            case InputEventMouseMotion motion when _shiftLooking:
                _shiftYaw -= motion.Relative.X * ShiftMouseRadians;
                _shiftPitch = Mathf.Clamp(_shiftPitch - motion.Relative.Y * ShiftMouseRadians, Mathf.DegToRad(-80), Mathf.DegToRad(75));
                return true;
            case InputEventMouseButton { Pressed: true } when !_shiftLooking && _shiftMenu?.Visible == false:
                SetShiftMenu(null);
                return true;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (key.Keycode == Key.Escape)
                {
                    if (_session.PlayerStewardId is null) return true;
                    if (_shiftMenu!.Visible) SetShiftMenu(null); else SetShiftMenu("Paused");
                    return true;
                }
                if (key.Keycode == Key.P && _session.PlayerStewardId is not null) { _host.Submit(new SetPausedCommand(!_session.IsPaused)); return true; }
                // First person reads in real time: 2× and 4× stay locked for now.
                return key.Keycode is Key.Key1 or Key.Key2 or Key.Key3 or Key.Space or Key.Q or Key.E || _shiftLooking;
        }
        return inputEvent is InputEventMouse && _shiftLooking;
    }
}

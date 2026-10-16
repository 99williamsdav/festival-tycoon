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
/// glides between ticks. Holding E on whatever's in front sends the sim's hold-to-act command, and letting go
/// releases it. The tycoon HUD is hidden for the shift's own (Main.StewardShiftHud.cs), with the designer's arms
/// (Main.StewardShiftArms.cs) and the report (Main.StewardShiftReport.cs). Its saves have their own folder.
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
    private bool _shiftKeyDown;
    private CanvasLayer? _hudLayer;

    private bool StewardShiftActive => _shiftActive;
    private string StewardShiftSaveDirectory => _architectureEvidenceOutput is not null
        ? System.IO.Path.Combine(_architectureEvidenceOutput, "steward-saves") : ProjectSettings.GlobalizePath("user://saves/r0-build-v105-steward-shift");
    private const float ShiftMouseRadians = 0.0024f;

    /// <summary>The title screen's way in: a fresh self-running festival at the picked tier, the player its second steward.</summary>
    private void StartStewardShift(int tier)
    {
        ParkCampaignHost();
        if (_host is not null) _menuCampaignIds.Add(_session.CampaignId.Value);
        ulong seed;
        do seed = BitConverter.ToUInt64(CryptographicRandom.GetBytes(sizeof(ulong)));
        while (seed == 0 || _menuCampaignIds.Contains(seed));
        _menuCampaignIds.Add(seed);
        BeginStewardShift(GameSession.CreateStewardShift(seed, tier,currentBarnGeometry:true));
        GD.Print($"STEWARD_SHIFT_STARTED id={_session.CampaignId.Value} seed={seed} tier={tier} player={_session.PlayerStewardId}");
    }

    /// <summary>The newest valid shift save while its steward is still on shift; null when there's none to go back to.</summary>
    private GameSession? SavedStewardShift() =>
        AutosaveRotation.LoadNewestValid(StewardShiftSaveDirectory, _saveCompatibility).Session is { IsStewardShift: true, PlayerStewardId: not null } saved ? saved : null;

    /// <summary>The title screen's Continue shift: back on the saved shift where it was left.</summary>
    private void ContinueStewardShift(GameSession saved)
    {
        ParkCampaignHost();
        // Still the shift's host: its own session is at least as new as anything it saved.
        BeginStewardShift(_host.SaveDirectory == StewardShiftSaveDirectory && _session.IsStewardShift ? _session : saved);
        GD.Print($"STEWARD_SHIFT_CONTINUED id={_session.CampaignId.Value} tick={_session.CurrentTick} player={_session.PlayerStewardId}");
    }

    private void BeginStewardShift(GameSession shift)
    {
        _host = HostFor(StewardShiftSaveDirectory, shift);
        SwitchToFestival(shift);
        _host.Clock.RequestedSpeed = RequestedSpeed.OneX;
        StartMenu.Close();
        EnterStewardShiftView();
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
        _shiftKeyDown = false;
        BuildStewardShiftHud();
        BuildStewardShiftArms();
        PushShiftRadio(_session.IsHandsOn ? "Boots on, boss. Control's listening." : _session.IsRainyFestival
            ? "Second post. Wet one: straw's in the store."
            : "Second post. Hot one: keep drinking.");
        SetShiftMenu(null);
    }

    private void ExitStewardShiftView()
    {
        _shiftActive = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _shiftLooking = false;
        if (EyeViewActive) ExitEyeView();
        ClearStewardShiftArms();
        ClearStewardShiftHud();
        ResetRadialMenu();
        ResetFinanceFeedback();
        if (_hudLayer is not null) _hudLayer.Visible = true;
    }

    /// <summary>Back to the title: the shift is saved and left behind, and the campaign's host returns as it was left.</summary>
    private void ReturnFromStewardShift()
    {
        _host.SaveMilestone("Shift", force: false);
        ExitStewardShiftView();
        LeaveHandsOnHost();
        BuildStartSplash();
    }

    /// <summary>Before the sim ticks: what the keys and mouse ask for, sent as the held move command when it changes.</summary>
    private void ProcessStewardShiftInput()
    {
        if (!_shiftActive) return;
        if (_session.PlayerStewardId is null && _session.IsHandsOn)
        {
            // A hands-on day's close (or a death) goes back to the manager's view, where the paper or the hearing waits.
            LeaveHandsOnFirstPerson();
            return;
        }
        if (_session.PlayerStewardId is null)
        {
            // Closing time (or a death) ends the shift: the report card takes the screen; the steward stays on post until the last guest is out.
            if (!StewardShiftReportShown) ShowStewardShiftReport();
            return;
        }
        int forward = 0, right = 0;
        var running = false;
        if (_shiftLooking && DisplayServer.GetName() != "headless")
        {
            forward = (Input.IsKeyPressed(Key.W) ? 1 : 0) - (Input.IsKeyPressed(Key.S) ? 1 : 0);
            right = (Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0);
            // No running with the radio menu or the site plan up.
            running = Input.IsKeyPressed(Key.Shift) && !RadialMenuOpen && !SitePlanUp;
        }
        SendShiftInput(forward, right, running);
    }

    private int ShiftFacing => ((int)Mathf.Round(Mathf.RadToDeg(_shiftYaw) + 180) % 360 + 360) % 360;

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
        var facing = ShiftFacing;
        var moving = dx != 0 || dz != 0;
        var started = moving != (_shiftSent.X != 0 || _shiftSent.Z != 0) || running != _shiftSent.Run || _shiftSent.Facing < 0;
        // Looking about on the spot sends nothing: the facing goes with the next step, and the prompt reads the camera.
        // Steering while walking goes every few ticks; a new key (a reversal or a turn) at once.
        var steer = Math.Abs(dx - _shiftSent.X) + Math.Abs(dz - _shiftSent.Z);
        if (!started && !(moving && (steer > 600 || steer > 40 && _session.CurrentTick - _shiftSentTick >= 6))) return;
        if (!_host.Submit(new SetPlayerMoveCommand(dx, dz, running, facing)).IsAccepted) return;
        _shiftSent = (dx, dz, running, facing);
        _shiftSentTick = _session.CurrentTick;
    }

    /// <summary>The action key goes down: hold the best action on whatever's first in front, as the camera faces.</summary>
    private void PressShiftAction()
    {
        if (_session.PlayerStewardId is null || _session.IsPaused || RadialMenuOpen) return;
        // With the site plan up, E puts it away first.
        LowerSitePlan();
        var facing = ShiftFacing;
        if (_session.StewardReach(facing).FirstOrDefault(item => item.Action is not null) is not { Action: { } action } target) return;
        _host.Submit(new StewardActionCommand(action, target.TargetId, facing));
    }

    private void ReleaseShiftAction()
    {
        if (_session.CaptureStewardShift()?.Action is null) return;
        if (_host.Submit(new StewardActionCommand(StewardActionKind.Release, "", ShiftFacing)).IsAccepted) _shiftCancelledTick = _session.CurrentTick;
    }

    /// <summary>After the sim ticks: the camera to the steward's interpolated head, then the HUD.</summary>
    private void ProcessStewardShiftView()
    {
        if (!_shiftActive || _eyeCamera is null) return;
        if (EyeViewActive && _session.PlayerStewardId is { } id && _attendeeVisuals.TryGetValue(new EntityId(id), out var visual))
        {
            var at = _foundationPresentation.Sample(new EntityId(id), _host.Clock.InterpolationFraction);
            // Up a stage's steps and on its deck, the eyes ride up with them.
            var lift = PlayerStageLift((int)at.XMillimetres, (int)at.ZMillimetres);
            _eyeCamera.GlobalPosition = new Vector3((float)(at.XMillimetres / 1000), EyeHeight(visual) + .04f + lift, (float)(at.ZMillimetres / 1000));
            _eyeCamera.Rotation = new Vector3(_shiftPitch, _shiftYaw, 0);
        }
        UpdateStewardShiftHud();
    }

    /// <summary>Shift keys and mouse-look. True when the event was the shift's.</summary>
    private bool HandleStewardShiftInput(InputEvent inputEvent)
    {
        if (!_shiftActive) return false;
        // The radio menu (hold Q) takes the mouse and its own keys while it's up.
        if (HandleRadialInput(inputEvent)) return true;
        switch (inputEvent)
        {
            case InputEventMouseMotion motion when _shiftLooking:
                // Frozen while the radio menu's up, the look eases back in after it closes.
                _shiftYaw -= motion.Relative.X * ShiftMouseRadians * RadialLookFactor;
                _shiftPitch = Mathf.Clamp(_shiftPitch - motion.Relative.Y * ShiftMouseRadians * RadialLookFactor, Mathf.DegToRad(-80), Mathf.DegToRad(75));
                SwayShiftArms(motion.Relative);
                return true;
            case InputEventMouseButton { Pressed: true } when !_shiftLooking && _shiftMenu?.Visible == false && !StewardShiftReportShown:
                SetShiftMenu(null);
                return true;
            case InputEventKey { Keycode: Key.E } action when !action.Echo:
                if (action.Pressed && _shiftLooking && !_shiftKeyDown) { _shiftKeyDown = true; PressShiftAction(); }
                else if (!action.Pressed && _shiftKeyDown) { _shiftKeyDown = false; ReleaseShiftAction(); }
                return true;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (key.Keycode == Key.Escape)
                {
                    if (_session.PlayerStewardId is null) return true;
                    if (_shiftMenu!.Visible) SetShiftMenu(null); else SetShiftMenu("Paused");
                    return true;
                }
                if (key.Keycode == Key.V && _session.IsHandsOn) { LeaveHandsOnFirstPerson(); return true; }
                if (key.Keycode == Key.M && _shiftLooking) { ToggleSitePlan(); return true; }
                if ((key.Keycode == Key.P || key.Keycode == Key.Space && _session.IsHandsOn) && _session.PlayerStewardId is not null)
                { _host.Submit(new SetPausedCommand(!_session.IsPaused)); return true; }
                // First person reads in real time: 2× and 4× stay locked for now.
                return key.Keycode is Key.Key1 or Key.Key2 or Key.Key3 or Key.Space or Key.Q || _shiftLooking;
        }
        return inputEvent is InputEventMouse && _shiftLooking;
    }
}

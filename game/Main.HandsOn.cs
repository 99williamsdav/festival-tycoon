using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using CryptographicRandom = System.Security.Cryptography.RandomNumberGenerator;

namespace Festival.Game;

/// <summary>
/// Front of House, the hands-on campaign: the ordinary campaign with the player also on the ground as a steward.
/// Tab swaps between the manager's view and first person on that steward while the day runs; first person rides the
/// steward shift's controller and HUD (Main.StewardShift*.cs). In the manager's view the steward stands where they
/// were left, marked YOU, and the radio's calls keep coming. Its saves keep to their own folder.
/// </summary>
public partial class Main
{
    public const string HandsOnModeName = "Front of House";
    public const string HandsOnStrapline = "You're the steward on the gate until you can afford one.";

    private string HandsOnSaveDirectory => ProjectSettings.GlobalizePath("user://saves/r0-build-v70-front-of-house");
    private CanvasLayer? _handsOnHintLayer;
    private Label? _handsOnHint;

    private bool HandsOnFirstPerson => _shiftActive && _session.IsHandsOn;

    /// <summary>The title screen's Front of House: a fresh hands-on Tier 1 campaign on its own host and save folder.</summary>
    private void StartHandsOnCampaign()
    {
        _campaignHost ??= _host;
        if (_host is not null) _menuCampaignIds.Add(_session.CampaignId.Value);
        ulong seed;
        do seed = BitConverter.ToUInt64(CryptographicRandom.GetBytes(sizeof(ulong)));
        while (seed == 0 || _menuCampaignIds.Contains(seed));
        _menuCampaignIds.Add(seed);
        BeginHandsOn(GameSession.CreateHandsOnCampaign(seed));
        GD.Print($"HANDS_ON_CAMPAIGN_STARTED id={_session.CampaignId.Value} seed={seed} steward={_session.HandsOnStewardId}");
    }

    /// <summary>The newest valid Front of House save; null when there's none.</summary>
    private GameSession? SavedHandsOnCampaign() =>
        AutosaveRotation.LoadNewestValid(HandsOnSaveDirectory, _saveCompatibility).Session is { IsHandsOn: true } saved ? saved : null;

    private void ContinueHandsOnCampaign(GameSession saved)
    {
        _campaignHost ??= _host;
        BeginHandsOn(saved);
        GD.Print($"HANDS_ON_CAMPAIGN_CONTINUED id={_session.CampaignId.Value} tick={_session.CurrentTick} status={_session.PreparedStatus}");
    }

    private void BeginHandsOn(GameSession session)
    {
        _host = new SessionHost(session, HandsOnSaveDirectory, _saveCompatibility);
        SwitchToFestival(session);
        StartMenu.Close();
        RefreshChallengeReveal();
    }

    /// <summary>Leaving a hands-on campaign for the classic one: the classic campaign's host comes back.</summary>
    private void LeaveHandsOnHost()
    {
        if (_campaignHost is null || StewardShiftActive) return;
        _host = _campaignHost;
        _campaignHost = null;
        _newCampaignOnEnter = true;
    }

    /// <summary>Tab: into first person on the player's steward, or back to the manager's view. The day keeps running either way.</summary>
    private void ToggleHandsOnView()
    {
        if (!_session.IsHandsOn) return;
        if (HandsOnFirstPerson) { LeaveHandsOnFirstPerson(); return; }
        if (_session.PlayerStewardId is not { } id || !_attendeeVisuals.ContainsKey(new EntityId(id))) return;
        if (EyeViewActive) ExitEyeView();
        ClearSelection();
        // First person reads in real time.
        SetDaySpeed(RequestedSpeed.OneX);
        var paused = _session.IsPaused;
        // Cash floating over the bars belongs to the overview, not a steward's eyes.
        ResetFinanceFeedback();
        EnterStewardShiftView();
        // Paused in the manager's view stays paused, with the menu up.
        if (paused) SetShiftMenu("Paused");
        GD.Print($"HANDS_ON_VIEW first-person tick={_session.CurrentTick}");
    }

    /// <summary>Back to the manager's view: the steward stops where they are, hands empty, and stands there.</summary>
    private void LeaveHandsOnFirstPerson()
    {
        if (_shiftKeyDown) { _shiftKeyDown = false; ReleaseShiftAction(); }
        if (_session.PlayerStewardId is not null && _session.CaptureStewardShift() is { } shift && (shift.MoveX != 0 || shift.MoveZ != 0 || shift.Run))
            _host.Submit(new SetPlayerMoveCommand(0, 0, false, shift.FacingDegrees));
        ExitStewardShiftView();
        RefreshPreparationHud();
        GD.Print($"HANDS_ON_VIEW manager tick={_session.CurrentTick}");
    }

    /// <summary>The manager's view: a reminder of the key while the player's steward is on duty.</summary>
    private void ProcessHandsOnHint()
    {
        var show = _session.IsHandsOn && !_shiftActive && _session.PlayerStewardId is not null && !StartMenu.IsOpen && !ResultsPaper.IsOpen;
        if (!show) { if (_handsOnHintLayer is not null) _handsOnHintLayer.Visible = false; return; }
        if (_handsOnHintLayer is null)
        {
            _handsOnHintLayer = new CanvasLayer { Layer = 19, Name = "HandsOnHint" };
            AddChild(_handsOnHintLayer);
            _handsOnHint = new Label { Name = "HandsOnHintText", MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
            _handsOnHint.AddThemeFontSizeOverride("font_size", Ui.Px(15));
            _handsOnHint.AddThemeColorOverride("font_color", new Color("e8f23a"));
            _handsOnHint.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, .8f));
            _handsOnHint.AddThemeConstantOverride("outline_size", Ui.Px(5));
            _handsOnHintLayer.AddChild(_handsOnHint);
        }
        _handsOnHintLayer.Visible = true;
        var open = _session.CaptureStewardShift()?.Calls is { } calls ? Array.FindAll(calls, call => call.Open).Length : 0;
        _handsOnHint!.Text = $"TAB · step into your boots{(open > 0 ? $"  ·  radio: {open} call{(open == 1 ? "" : "s")} open" : "")}";
        var size = GetViewport().GetVisibleRect().Size;
        _handsOnHint.Position = new Vector2(0, size.Y - Ui.S(96));
        _handsOnHint.Size = new Vector2(size.X, Ui.S(24));
    }
}

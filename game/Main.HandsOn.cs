using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using CryptographicRandom = System.Security.Cryptography.RandomNumberGenerator;

namespace Festival.Game;

/// <summary>
/// Front of House, the hands-on campaign: the ordinary campaign with the player also on the ground as a steward.
/// V swaps between the manager's view and first person on that steward while the day runs; first person rides the
/// steward shift's controller and HUD (Main.StewardShift*.cs). In the manager's view the steward, marked YOU, works by
/// themselves (the sim's autopilot, <see cref="SetPlayerStewardControlCommand"/>), and the radio's calls keep coming.
/// Its saves keep to their own folder.
/// </summary>
public partial class Main
{
    public const string HandsOnModeName = "All-Rounder";
    public const string HandsOnStrapline = "Run it, and steward it yourself until you can afford the help.";

    private string HandsOnSaveDirectory => ProjectSettings.GlobalizePath("user://saves/r0-build-v72-front-of-house");
    private CanvasLayer? _handsOnHintLayer;
    private Label? _handsOnHint;

    private bool HandsOnFirstPerson => _shiftActive && _session.IsHandsOn;

    /// <summary>
    /// The title screen's Front of House button. With a Front of House campaign already saved it asks first, since a new
    /// one takes over the autosaves: start new, continue the saved one, or cancel.
    /// </summary>
    private void PickFrontOfHouse()
    {
        if (SavedHandsOnCampaign() is not { } saved) { StartHandsOnCampaign(); return; }
        _handsOnConfirmLayer?.QueueFree();
        _handsOnConfirmLayer = new CanvasLayer { Layer = 25, Name = "FrontOfHouseConfirm" };
        AddChild(_handsOnConfirmLayer);
        var dialog = new ConfirmationDialog { Title = "Start a new " + HandsOnModeName + "?",
            DialogText = "Start a new " + HandsOnModeName + "? Your current one will be replaced.",
            OkButtonText = "Start new", CancelButtonText = "Cancel", Theme = HudTheme() };
        dialog.Theme.SetStylebox("panel", "AcceptDialog", HudStyle(HudInk, 16));
        dialog.GetLabel().AddThemeColorOverride("font_color", HudPaper);
        var resume = dialog.AddButton("Continue", false, "continue");
        foreach (var button in new[] { dialog.GetOkButton(), dialog.GetCancelButton(), resume })
        {
            button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
                button.AddThemeColorOverride(state, HudInk);
            foreach (var state in new[] { "normal", "hover", "pressed", "focus" })
                button.AddThemeStyleboxOverride(state, HudStyle(state == "hover" ? new Color("d3e8df") : HudPaper, 8));
        }
        void Close() { _handsOnConfirmLayer?.QueueFree(); _handsOnConfirmLayer = null; }
        dialog.Confirmed += () => { Close(); StartHandsOnCampaign(); };
        dialog.Canceled += Close;
        dialog.CustomAction += action => { if (action == "continue") { Close(); ContinueHandsOnCampaign(saved); } };
        _handsOnConfirmLayer.AddChild(dialog);
        dialog.PopupCentered(new Vector2I(480, 170));
    }

    private CanvasLayer? _handsOnConfirmLayer;

    /// <summary>The title screen's Front of House: a fresh hands-on Tier 1 campaign on its own host and save folder.</summary>
    private void StartHandsOnCampaign()
    {
        ParkCampaignHost();
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
        ParkCampaignHost();
        // Still the current host: its own session is at least as new as anything it saved.
        BeginHandsOn(_host.SaveDirectory == HandsOnSaveDirectory && _session.IsHandsOn ? _session : saved);
        GD.Print($"HANDS_ON_CAMPAIGN_CONTINUED id={_session.CampaignId.Value} tick={_session.CurrentTick} status={_session.PreparedStatus}");
    }

    private void BeginHandsOn(GameSession session)
    {
        _host = HostFor(HandsOnSaveDirectory, session);
        SwitchToFestival(session);
        StartMenu.Close();
        RefreshChallengeReveal();
    }

    /// <summary>
    /// A host on <paramref name="directory"/>: the current one when it already saves there (after any write in flight
    /// finishes), so two hosts never write the same folder; otherwise a new one.
    /// </summary>
    private SessionHost HostFor(string directory, GameSession session)
    {
        if (_host is null || _host.SaveDirectory != directory) return new SessionHost(session, directory, _saveCompatibility);
        _host.SaveMilestone("Switch", force: false);
        return _host;
    }

    /// <summary>
    /// The classic campaign's host is set aside while Front of House or a steward shift has the screen, with whether
    /// its Enter would start a new campaign, so coming back resumes it exactly as it was left.
    /// </summary>
    private bool _campaignHostNewOnEnter;
    private void ParkCampaignHost()
    {
        if (_campaignHost is not null) return;
        _campaignHost = _host;
        _campaignHostNewOnEnter = _newCampaignOnEnter;
    }

    /// <summary>Set when the classic host comes back while the world still shows another session; Enter rebuilds the views.</summary>
    private bool _campaignViewsStale;

    /// <summary>Back to the classic campaign's host, if it was parked: out of first person first, then its own Enter state.</summary>
    private void LeaveHandsOnHost()
    {
        if (_campaignHost is null) return;
        if (_shiftActive) ExitStewardShiftView();
        _host = _campaignHost;
        _campaignHost = null;
        _newCampaignOnEnter = _campaignHostNewOnEnter;
        _campaignViewsStale = true;
    }
    /// <summary>V: into first person on the player's steward, or back to the manager's view. The day keeps running either way.</summary>
    private void ToggleHandsOnView()
    {
        if (!_session.IsHandsOn) return;
        if (HandsOnFirstPerson) { LeaveHandsOnFirstPerson(); return; }
        if (_session.PlayerStewardId is not { } id || !_attendeeVisuals.ContainsKey(new EntityId(id))) return;
        if (EyeViewActive) ExitEyeView();
        ClearSelection();
        // The steward is the player's again, stopped where they were.
        if (_session.CaptureStewardShift()?.Autopilot is not null) _host.Submit(new SetPlayerStewardControlCommand(false));
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

    /// <summary>Back to the manager's view: the steward lets go of whatever they held and carries on by themselves.</summary>
    private void LeaveHandsOnFirstPerson()
    {
        if (_shiftKeyDown) { _shiftKeyDown = false; ReleaseShiftAction(); }
        if (_session.PlayerStewardId is not null && _session.CaptureStewardShift() is { Autopilot: null })
            _host.Submit(new SetPlayerStewardControlCommand(true));
        ExitStewardShiftView();
        RefreshPreparationHud();
        GD.Print($"HANDS_ON_VIEW manager tick={_session.CurrentTick}");
    }

    /// <summary>
    /// The manager's view: a reminder of the key while the player's steward is on duty, with the calls that are theirs
    /// (radioed, and nobody hired on them). The steward works by themselves whenever the player isn't in first person,
    /// whatever left them otherwise (a save from first person, say).
    /// </summary>
    private void ProcessHandsOnHint()
    {
        if (_session.IsHandsOn && _session.PlayerStewardId is not null && _session.CaptureStewardShift() is { } own && (own.Autopilot is null) == !_shiftActive)
            _host.Submit(new SetPlayerStewardControlCommand(!_shiftActive));
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
        var now = _session.CurrentTick;
        var open = _session.CaptureStewardShift()?.Calls is { } calls ? Array.FindAll(calls, call => call.Open && call.CoveredBy is null && call.HeardTick <= now).Length : 0;
        _handsOnHint!.Text = $"V · Walk the field{(open > 0 ? $"  ·  radio: {open} call{(open == 1 ? "" : "s")} yours" : "")}";
        var size = GetViewport().GetVisibleRect().Size;
        _handsOnHint.Position = new Vector2(0, size.Y - Ui.S(96));
        _handsOnHint.Size = new Vector2(size.X, Ui.S(24));
    }
}

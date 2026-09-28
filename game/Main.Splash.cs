using Godot;
using Festival.Persistence;
using Festival.Simulation;
using System;
using System.Collections.Generic;
using CryptographicRandom = System.Security.Cryptography.RandomNumberGenerator;

namespace Festival.Game;

public partial class Main
{
    private CanvasLayer? _startSplash;
    private bool _newCampaignOnEnter;
    private readonly HashSet<ulong> _menuCampaignIds = [];
    private string? _startSplashCapturePath;
    private int _startSplashCaptureFrame;

    private void BuildStartSplash()
    {
        _startSplash = new CanvasLayer { Layer = 20 };
        AddChild(_startSplash);
        var backdrop = new ColorRect { Color = new Color("142630") };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
        _startSplash.AddChild(backdrop);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backdrop.AddChild(centre);
        var content = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        content.AddThemeConstantOverride("separation", 16);
        centre.AddChild(content);

        var visible = GetViewport().GetVisibleRect().Size;
        var side = Mathf.Min(620f, Mathf.Min(visible.X * 0.55f, visible.Y * 0.69f));
        var logo = new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/branding/festival-tycoon-mosaic-logo-v2.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(side, side),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        content.AddChild(logo);
        var subtitle = new Label { Text = "LOWER WITTERING FARM  •  YOUR WEEKEND STARTS HERE",
            HorizontalAlignment = HorizontalAlignment.Center };
        subtitle.AddThemeFontSizeOverride("font_size", 18);
        subtitle.AddThemeColorOverride("font_color", new Color("f3e8c9"));
        content.AddChild(subtitle);
        var button = new Button { Name = "EnterFestival", Text = "ENTER FESTIVAL", CustomMinimumSize = new Vector2(280, 54),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        button.AddThemeFontSizeOverride("font_size", 21);
        button.Pressed += EnterFestival;
        content.AddChild(button);
        button.GrabFocus();
    }

    private void EnterFestival()
    {
        if (_newCampaignOnEnter)
        {
            // Create only on Enter: merely viewing the menu must not touch a terminal save.
            _menuCampaignIds.Add(_session.CampaignId.Value);
            var seed = BitConverter.ToUInt64(CryptographicRandom.GetBytes(sizeof(ulong)));
            while (seed == 0 || _menuCampaignIds.Contains(seed))
                seed = BitConverter.ToUInt64(CryptographicRandom.GetBytes(sizeof(ulong)));
            _menuCampaignIds.Add(seed);
            var next = GameSession.CreateBookingCampaign(seed);
            CancelResponsePostPlacement(); CancelImmersionPlacement(); CancelWaterPlacement(); CancelPerkConfirmation();
            ClearSelection(); ResetImmersionHeldVisuals();
            foreach (var visual in _attendeeVisuals.Values) visual.QueueFree();
            _attendeeVisuals.Clear(); _attendeePickRegistry.Clear(); _selectedAttendeeId = null;
            _session = next;
            _autosaveGeneration = AutosaveRotation.NextGeneration(SaveDirectory, _saveCompatibility);
            _autosaveScheduler.Rebase(); _preparationSaveBlocked = false;
            _foundationClock.ResetBoundary(); _foundationPresentation.Reset(_session.CaptureObservation());
            _pausedHash = _foundationPublishedHash = _session.CaptureSnapshot().AuthoritativeHash;
            _foundationPublishedHashTick = _session.CurrentTick;
            _perksExpanded = false; _selectedPerk = null; _perkHudKey = "";
            _bookingSelected = null; _bookingDurableMessage = "Select a band, then activate a set. Dragging also works.";
            _bookingSort = BookingSortField.Price; _bookingDescending = false; _bookingGenre = null;
            _bookingGenreFilter?.Select(0);
            _hudWorkspaceOpen = true; _hudProgrammeOpen = false;
            _preparationMessage = "Choose three different acts and hire a sound engineer. Equipment and stock are optional.";
            ResetFinanceFeedback(); ResetLivePerformancePresentation();
            ResetMedicalCuePresentation(); ResetDisorderCuePresentation();
            SyncExtraWaterWorld(); SyncResponsePosts(); SyncImmersionWorld();
            if (_session.CaptureObservation().NavigationAgents.Count > 0) BuildAttendee();
            RebuildPreparationOffers(); SelectHudTab("Programme"); RefreshPreparationHud();
            _newCampaignOnEnter = false;
            GD.Print($"NEW_CAMPAIGN_STARTED id={_session.CampaignId.Value} seed={seed} status={_session.PreparedStatus}");
        }
        _startSplash?.QueueFree(); _startSplash = null;
    }

    private void ProcessStartSplashCapture()
    {
        if (_startSplashCapturePath is null || ++_startSplashCaptureFrame < 4) return;
        GetViewport().GetTexture().GetImage().SavePng(_startSplashCapturePath);
        GD.Print($"START_SPLASH_CAPTURE path={_startSplashCapturePath}");
        GetTree().Quit();
    }
}

using Godot;
using Festival.Persistence;
using Festival.Simulation;
using System;
using System.Collections.Generic;
using CryptographicRandom = System.Security.Cryptography.RandomNumberGenerator;

namespace Festival.Game;

public partial class Main
{
    private bool _newCampaignOnEnter;
    private readonly HashSet<ulong> _menuCampaignIds = [];

    private StartMenu? _startMenuView;
    private StartMenu StartMenu => _startMenuView ??= new();

    private void BuildStartSplash() => StartMenu.Open(this, EnterFestival, OpenFieldGuide);

    private void EnterFestival()
    {
        if (_newCampaignOnEnter)
        {
            // Create only on Enter: merely viewing the menu must not touch a terminal save.
            var next = CreateFreshBuildCampaign(out var seed);
            SwitchToFestival(next);
            GD.Print($"NEW_CAMPAIGN_STARTED id={_session.CampaignId.Value} seed={seed} status={_session.PreparedStatus}");
        }
        StartMenu.Close();
    }

    /// <summary>Hands the host a fresh festival in preparation (a new campaign or the next tier) and resets the views.</summary>
    private void SwitchToFestival(GameSession next)
    {
        Perks.CancelConfirmation();
        ClearSelection(); ResetImmersionHeldVisuals();
        foreach (var visual in _attendeeVisuals.Values) visual.QueueFree();
        _attendeeVisuals.Clear(); _attendeePickRegistry.Clear(); _selectedAttendeeId = null;
        _host.StartNewCampaign(next);
        SyncSaveStatus();
        _foundationPresentation.Reset(_session.CaptureObservation());
        Perks.Reset();
        Booking.Reset();
        _hudWorkspaceOpen = true; _hudProgrammeOpen = true; _boxOfficeSeenAttempt = 0;
        _preparationMessage = "Choose three different acts and hire a sound engineer. Equipment and stock are optional.";
        ResetFinanceFeedback(); ResetLivePerformancePresentation();
        ResetMedicalCuePresentation(); ResetDisorderCuePresentation();
        SyncExtraWaterWorld(); SyncResponsePosts(); SyncImmersionWorld();
        if (_session.CaptureObservation().NavigationAgents.Count > 0) BuildAttendee();
        RebuildPreparationOffers(); SelectHudTab("Programme"); RefreshPreparationHud();
        _newCampaignOnEnter = false;
    }

    private GameSession CreateFreshBuildCampaign(out ulong seed)
    {
        // Entropy belongs at the user-facing creation boundary, never inside the
        // deterministic simulation factory. Keep previous campaigns distinct in
        // this process, including a session created before the splash is entered.
        if (_host is not null) _menuCampaignIds.Add(_session.CampaignId.Value);
        do seed = BitConverter.ToUInt64(CryptographicRandom.GetBytes(sizeof(ulong)));
        while (seed == 0 || _menuCampaignIds.Contains(seed));
        _menuCampaignIds.Add(seed);
        return GameSession.CreateBuildCampaign(seed);
    }

}

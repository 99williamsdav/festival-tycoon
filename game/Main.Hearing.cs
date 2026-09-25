using Festival.Simulation;
using Festival.Persistence;
using Godot;
using System;

namespace Festival.Game;

public partial class Main
{
    private ColorRect? _hearingShade;
    private Label? _hearingRecord;
    private Label? _hearingMast;
    private Label? _hearingSequence;
    private Label? _hearingBalance;
    private Label? _hearingOutcome;
    private HBoxContainer? _hearingChoices;
    private Button? _hearingRetry;
    private PanelContainer? _hearingConfirm;
    private Label? _hearingConfirmCopy;
    private SessionCommand? _pendingHearingDecision;

    private void BuildHearingHud(CanvasLayer layer)
    {
        var ink = new Color("2d3a37");
        _hearingShade = new ColorRect { Color = new Color(0.12f, 0.16f, 0.13f, 0.63f), MouseFilter = Control.MouseFilterEnum.Stop };
        _hearingShade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_hearingShade);
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hearingShade.AddChild(center);
        var paper = new PanelContainer { CustomMinimumSize = new Vector2(720, 0) };
        paper.AddThemeStyleboxOverride("panel", PaperStyle(new Color("fff4d6")));
        center.AddChild(paper);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(660, 0) };
        box.AddThemeConstantOverride("separation", 12);
        paper.AddChild(box);
        _hearingMast = LabelText("", 13, ink); box.AddChild(_hearingMast);
        var heading = new HBoxContainer(); heading.AddThemeConstantOverride("separation", 18); box.AddChild(heading);
        var headingWords = new VBoxContainer { CustomMinimumSize = new Vector2(490, 0) }; heading.AddChild(headingWords);
        headingWords.AddChild(LabelText("WEEKEND ENDED", 13, new Color("995541")));
        headingWords.AddChild(LabelText("Council hearing", 34, ink));
        var lead = LabelText("Someone has died. We explicitly told you this shouldn’t happen. We’re considering revoking your licence.", 17, ink);
        lead.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        lead.CustomMinimumSize = new Vector2(490, 84);
        headingWords.AddChild(lead);
        var token = new PanelContainer { CustomMinimumSize = new Vector2(130, 120) };
        token.AddThemeStyleboxOverride("panel", PaperStyle(new Color("f4e6bd")));
        heading.AddChild(token);
        var tokenWords = new VBoxContainer(); token.AddChild(tokenWords);
        tokenWords.AddChild(LabelText("COUNCIL FAVOUR", 12, new Color("604823")));
        _hearingBalance = LabelText("", 42, new Color("654a22")); tokenWords.AddChild(_hearingBalance);
        box.AddChild(LabelText("RECORD OF LOSS", 16, ink));
        _hearingRecord = LabelText("", 15, ink);
        _hearingRecord.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hearingRecord.CustomMinimumSize = new Vector2(650, 60);
        box.AddChild(_hearingRecord);
        _hearingSequence = LabelText("", 13, new Color("995541"));
        _hearingSequence.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_hearingSequence);
        box.AddChild(LabelText("LICENCE DECISION", 16, ink));
        _hearingChoices = new HBoxContainer(); _hearingChoices.AddThemeConstantOverride("separation", 12); box.AddChild(_hearingChoices);
        _hearingRetry = ButtonText("", () => ConfirmHearing(new SpendCouncilFavourCommand()));
        _hearingRetry.CustomMinimumSize = new Vector2(320, 80); _hearingChoices.AddChild(_hearingRetry);
        var concede = ButtonText("CONCEDE\nEnd campaign\nNo retry · Favour is not spent", () => ConfirmHearing(new ConcedeCouncilHearingCommand()));
        concede.CustomMinimumSize = new Vector2(320, 80); _hearingChoices.AddChild(concede);
        _hearingOutcome = LabelText("", 16, ink); _hearingOutcome.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hearingOutcome.CustomMinimumSize = new Vector2(650, 55); box.AddChild(_hearingOutcome);
        _hearingConfirm = new PanelContainer(); _hearingConfirm.Visible = false;
        _hearingConfirm.AddThemeStyleboxOverride("panel", PaperStyle(new Color("f4e6bd"))); box.AddChild(_hearingConfirm);
        var confirmBox = new VBoxContainer(); _hearingConfirm.AddChild(confirmBox);
        _hearingConfirmCopy = LabelText("", 16, ink); _hearingConfirmCopy.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hearingConfirmCopy.CustomMinimumSize = new Vector2(630, 50); confirmBox.AddChild(_hearingConfirmCopy);
        var confirmActions = new HBoxContainer(); confirmBox.AddChild(confirmActions);
        confirmActions.AddChild(ButtonText("GO BACK", () => { _pendingHearingDecision = null; _hearingConfirm!.Visible = false; }));
        confirmActions.AddChild(ButtonText("CONFIRM", ApplyHearingDecision));
        _hearingShade.Visible = false;
    }

    private void RefreshHearingHud()
    {
        if (_hearingShade is null) return;
        var preparation = _session.CapturePreparation();
        var lifecycle = _session.CaptureLifecycleSnapshot();
        var show = preparation?.Status == PreparationStatus.Failed && lifecycle is { Hearings.Count: > 0 };
        _hearingShade.Visible = show;
        if (!show) { _pendingHearingDecision = null; _hearingConfirm!.Visible = false; return; }
        var hearing = lifecycle!.Hearings[^1];
        var casualty = lifecycle.Casualties[^1];
        var weekendDay = new[] { "FRIDAY", "SATURDAY", "SUNDAY" }[Math.Clamp((int)((casualty.Tick - preparation!.StartedTick) / 12_800), 0, 2)];
        _hearingMast!.Text = $"LOWER WITTERING                         TIER {preparation.Tier} · {weekendDay} · ATTEMPT {preparation.Attempt}";
        _hearingBalance!.Text = $"{lifecycle.FixtureFavourBalance}";
        _hearingRecord!.Text = $"Person   {casualty.PersonId} · {casualty.Role}\nCause    {casualty.Cause}";
        _hearingSequence!.Text = casualty.TransactionId.StartsWith("medical-death:", StringComparison.Ordinal)
            ? "Distress → collapse → response window missed"
            : casualty.TransactionId.StartsWith("disorder-death:", StringComparison.Ordinal)
                ? "Grievance → argument → fight → untreated injury"
                : "Overload → fault warning → fatal generator incident";
        _hearingChoices!.Visible = hearing.Status == HearingStatus.Open && lifecycle.FixtureFavourBalance > 0;
        _hearingRetry!.Text = $"(SPEND 1 FAVOUR)\nIt won’t happen again\nRetry Tier {preparation.Tier} · same tier · balance {lifecycle.FixtureFavourBalance} → {lifecycle.FixtureFavourBalance - 1}";
        _hearingRetry!.Disabled = lifecycle.FixtureFavourBalance == 0;
        _hearingOutcome!.Text = hearing.Status == HearingStatus.Conceded
            ? "The licence is lost. The campaign is over."
            : hearing.Status == HearingStatus.LostNoFavour
                ? "No Favour remains. The licence is lost; the campaign is over and no retry remains."
                : "This weekend has ended. Spend 1 Favour to retry this tier, or concede.";
    }

    private void ConfirmHearing(SessionCommand command)
    {
        _pendingHearingDecision = command;
        _hearingConfirmCopy!.Text = command is SpendCouncilFavourCommand
            ? "It won’t happen again? Spend 1 Council Favour to keep the licence and retry this tier. This failed weekend stays on the record."
            : "Concede? The licence is lost and this campaign ends. No Favour is spent.";
        _hearingConfirm!.Visible = true;
    }

    private void ApplyHearingDecision()
    {
        if (_pendingHearingDecision is null) return;
        var command = _pendingHearingDecision;
        _pendingHearingDecision = null;
        _hearingConfirm!.Visible = false;
        var result = EquipmentCommandCoordinator.Execute(SaveDirectory, _session, command, _saveCompatibility, DateTimeOffset.UtcNow, _autosaveGeneration);
        if (!result.IsSuccess)
        {
            _preparationMessage = result.Error!;
            RefreshPreparationHud();
            return;
        }
        _session = result.Session;
        _autosaveGeneration++;
        _preparationSaveBlocked = false;
        _preparationMessage = command is SpendCouncilFavourCommand ? "Council Favour spent. Prepare this tier’s next weekend." : "The campaign has ended.";
        if (command is SpendCouncilFavourCommand)
        {
            foreach (var visual in _attendeeVisuals.Values) visual.QueueFree();
            _attendeeVisuals.Clear(); _attendeePickRegistry.Clear(); _selectedAttendeeId = null;
            ResetLivePerformancePresentation();
            _foundationClock.ResetBoundary(); _foundationPresentation.Reset(_session.CaptureObservation());
            if (_session.CaptureObservation().NavigationAgents.Count > 0) BuildAttendee();
        }
        RefreshPreparationHud();
    }
}

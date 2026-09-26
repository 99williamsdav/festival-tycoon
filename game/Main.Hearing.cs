using Festival.Simulation;
using Festival.ContentAdapter;
using Festival.Persistence;
using Godot;
using System;
using System.IO;

namespace Festival.Game;

public partial class Main
{
    private ColorRect? _hearingShade;
    private Label? _hearingRecord;
    private Label? _hearingPerson;
    private Label? _hearingMast;
    private Label? _hearingSequence;
    private Label? _hearingBalance;
    private Label? _hearingBalanceState;
    private Label? _hearingOutcome;
    private Label? _hearingFoot;
    private ColorRect? _hearingModalShade;
    private Label? _hearingConfirmTitle;
    private Label? _hearingConfirmTerms;
    private Button? _hearingConfirmAction;
    private HBoxContainer? _hearingChoices;
    private Button? _hearingRetry;
    private Label? _hearingRetryDetail;
    private PanelContainer? _hearingConfirm;
    private Label? _hearingConfirmCopy;
    private SessionCommand? _pendingHearingDecision;
    private string? _hearingCaptureDirectory;
    private int _hearingCaptureFrame;

    private void ProcessHearingCapture()
    {
        if (_hearingCaptureDirectory is null) return;
        _hearingCaptureFrame++;
        if (_hearingCaptureFrame == 4)
        {
            if (DisplayServer.GetName() != "headless")
            {
                var image = GetViewport().GetTexture().GetImage();
                var size = image.GetSize();
                image.SavePng(Path.Combine(_hearingCaptureDirectory, $"hearing-{size.X}x{size.Y}.png"));
                GD.Print($"HEARING_CAPTURE viewport={size.X}x{size.Y}");
            }
            else GD.Print("HEARING_CAPTURE screenshot skipped: headless dummy renderer");
            ConfirmHearing(new SpendCouncilFavourCommand());
        }
        if (_hearingCaptureFrame == 7)
        {
            if (DisplayServer.GetName() != "headless")
            {
                var image = GetViewport().GetTexture().GetImage();
                var size = image.GetSize();
                image.SavePng(Path.Combine(_hearingCaptureDirectory, $"hearing-confirm-{size.X}x{size.Y}.png"));
            }
            GD.Print("HEARING_CAPTURE completed");
            GetTree().Quit();
        }
    }

    private static SystemFont HearingSerif() => new() { FontNames = ["Georgia", "Times New Roman"] };

    private static MarginContainer HearingMargins(int horizontal, int vertical)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", horizontal);
        margin.AddThemeConstantOverride("margin_right", horizontal);
        margin.AddThemeConstantOverride("margin_top", vertical);
        margin.AddThemeConstantOverride("margin_bottom", vertical);
        return margin;
    }

    private static ColorRect HearingRule() => new()
    {
        Color = new Color("8d8b73"), CustomMinimumSize = new Vector2(0, 1),
        MouseFilter = Control.MouseFilterEnum.Ignore
    };

    private static Control HearingGap(float height) => new() { CustomMinimumSize = new Vector2(0, height) };

    private static void HearingRecordRow(VBoxContainer parent, string key, Label value)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 14); parent.AddChild(row);
        var keyLabel = LabelText(key, 20, new Color("59645d"));
        keyLabel.CustomMinimumSize = new Vector2(112, 0); row.AddChild(keyLabel);
        value.AddThemeFontSizeOverride("font_size", 22);
        value.AddThemeColorOverride("font_color", new Color("2d3a37"));
        value.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        value.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(value);
    }

    private static (Button Button, Label Detail) HearingChoice(Color background, Color foreground,
        string verb, string title, string detail, Action action)
    {
        var button = new Button { Text = "", CustomMinimumSize = new Vector2(0, 158),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var normal = PaperStyle(background); normal.BorderColor = new Color("39433c"); normal.ShadowSize = 0;
        var hover = PaperStyle(background.Lightened(0.08f)); hover.BorderColor = new Color("39433c"); hover.ShadowSize = 0;
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.Pressed += action;
        var margin = HearingMargins(20, 17); margin.MouseFilter = Control.MouseFilterEnum.Ignore;
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect); button.AddChild(margin);
        var words = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        words.AddThemeConstantOverride("separation", 5); margin.AddChild(words);
        var verbLabel = LabelText(verb, 17, foreground); verbLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        words.AddChild(verbLabel);
        var titleLabel = LabelText(title, 30, foreground); titleLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        words.AddChild(titleLabel);
        var detailLabel = LabelText(detail, 18, foreground);
        detailLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        detailLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart; words.AddChild(detailLabel);
        return (button, detailLabel);
    }

    private void BuildHearingHud(CanvasLayer layer)
    {
        var ink = new Color("2d3a37");
        _hearingShade = new ColorRect { Color = new Color(0.12f, 0.16f, 0.13f, 0.75f), MouseFilter = Control.MouseFilterEnum.Stop };
        _hearingShade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_hearingShade);
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hearingShade.AddChild(center);
        var paper = new PanelContainer { CustomMinimumSize = new Vector2(1180, 0) };
        var paperStyle = PaperStyle(new Color("fff4d6"));
        paperStyle.BorderColor = new Color("777460"); paperStyle.ShadowSize = 14;
        paper.AddThemeStyleboxOverride("panel", paperStyle);
        center.AddChild(paper);
        var paperMargin = HearingMargins(30, 22); paper.AddChild(paperMargin);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(1120, 0) };
        box.AddThemeConstantOverride("separation", 0);
        paperMargin.AddChild(box);
        var mast = new HBoxContainer(); box.AddChild(mast);
        var mastLeft = LabelText("LOWER WITTERING", 18, ink);
        mastLeft.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; mast.AddChild(mastLeft);
        _hearingMast = LabelText("", 18, new Color("59645d")); mast.AddChild(_hearingMast);
        box.AddChild(HearingGap(10)); box.AddChild(HearingRule()); box.AddChild(HearingGap(19));
        var heading = new HBoxContainer(); heading.AddThemeConstantOverride("separation", 18); box.AddChild(heading);
        var headingWords = new VBoxContainer { CustomMinimumSize = new Vector2(900, 0) };
        headingWords.AddThemeConstantOverride("separation", 0); heading.AddChild(headingWords);
        var stamp = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        var stampStyle = PaperStyle(new Color("995541")); stampStyle.BorderColor = new Color("995541"); stampStyle.ShadowSize = 0;
        stamp.AddThemeStyleboxOverride("panel", stampStyle); headingWords.AddChild(stamp);
        var stampMargin = HearingMargins(8, 4); stamp.AddChild(stampMargin);
        stampMargin.AddChild(LabelText(FestivalCopy("WEEKEND ENDED"), 17, new Color("fff8ec")));
        headingWords.AddChild(HearingGap(8));
        var title = LabelText("Council hearing", 45, ink);
        title.AddThemeFontOverride("font", HearingSerif()); headingWords.AddChild(title);
        headingWords.AddChild(HearingGap(6));
        var lead = LabelText("Someone has died. We explicitly told you this shouldn’t happen. We’re considering revoking your licence.", 24, ink);
        lead.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        lead.CustomMinimumSize = new Vector2(900, 64);
        headingWords.AddChild(lead);
        var token = new PanelContainer { CustomMinimumSize = new Vector2(190, 160) };
        var tokenStyle = PaperStyle(new Color("f4e6bd")); tokenStyle.BorderColor = new Color("a27a35");
        tokenStyle.ShadowSize = 0; token.AddThemeStyleboxOverride("panel", tokenStyle);
        heading.AddChild(token);
        var tokenMargin = HearingMargins(9, 10); token.AddChild(tokenMargin);
        var tokenWords = new VBoxContainer(); tokenWords.AddThemeConstantOverride("separation", 0); tokenMargin.AddChild(tokenWords);
        var tokenName = LabelText("COUNCIL\nFAVOUR", 18, new Color("604823"));
        tokenName.HorizontalAlignment = HorizontalAlignment.Center; tokenWords.AddChild(tokenName);
        _hearingBalance = LabelText("", 58, new Color("654a22"));
        _hearingBalance.AddThemeFontOverride("font", HearingSerif());
        _hearingBalance.HorizontalAlignment = HorizontalAlignment.Center; tokenWords.AddChild(_hearingBalance);
        _hearingBalanceState = LabelText("", 18, new Color("604823"));
        _hearingBalanceState.HorizontalAlignment = HorizontalAlignment.Center; tokenWords.AddChild(_hearingBalanceState);
        box.AddChild(HearingGap(18)); box.AddChild(HearingRule()); box.AddChild(HearingGap(15));
        box.AddChild(LabelText("Record of loss", 25, ink)); box.AddChild(HearingGap(11));
        _hearingPerson = new Label(); HearingRecordRow(box, "Person", _hearingPerson);
        box.AddChild(HearingGap(8));
        _hearingRecord = new Label(); HearingRecordRow(box, "Cause", _hearingRecord);
        box.AddChild(HearingGap(12));
        var sequenceStrip = new PanelContainer();
        var sequenceStyle = PaperStyle(new Color("f6e8c9"));
        sequenceStyle.BorderWidthLeft = 4; sequenceStyle.BorderWidthTop = 0;
        sequenceStyle.BorderWidthRight = 0; sequenceStyle.BorderWidthBottom = 0;
        sequenceStyle.BorderColor = new Color("995541"); sequenceStyle.ShadowSize = 0;
        sequenceStrip.AddThemeStyleboxOverride("panel", sequenceStyle); box.AddChild(sequenceStrip);
        var sequenceMargin = HearingMargins(10, 8); sequenceStrip.AddChild(sequenceMargin);
        _hearingSequence = LabelText("", 19, new Color("594c3f"));
        _hearingSequence.AutowrapMode = TextServer.AutowrapMode.WordSmart; sequenceMargin.AddChild(_hearingSequence);
        box.AddChild(HearingGap(16)); box.AddChild(HearingRule()); box.AddChild(HearingGap(15));
        box.AddChild(LabelText("Licence decision", 25, ink)); box.AddChild(HearingGap(12));
        _hearingChoices = new HBoxContainer(); _hearingChoices.AddThemeConstantOverride("separation", 12); box.AddChild(_hearingChoices);
        var primary = HearingChoice(new Color("39433c"), new Color("fff9e8"), "(SPEND 1 FAVOUR)",
            "It won’t happen again", "", () => ConfirmHearing(new SpendCouncilFavourCommand()));
        _hearingRetry = primary.Button; _hearingRetryDetail = primary.Detail;
        _hearingChoices.AddChild(primary.Button);
        var secondary = HearingChoice(new Color("fff4d6"), new Color("39433c"), "CONCEDE",
            "End campaign", "No retry · Favour is not spent", () => ConfirmHearing(new ConcedeCouncilHearingCommand()));
        _hearingChoices.AddChild(secondary.Button);
        _hearingOutcome = LabelText("", 19, ink); _hearingOutcome.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_hearingOutcome);
        box.AddChild(HearingGap(13));
        _hearingFoot = LabelText("", 19, new Color("59645d"));
        _hearingFoot.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_hearingFoot);

        _hearingModalShade = new ColorRect { Color = new Color(0.12f, 0.15f, 0.12f, 0.62f),
            MouseFilter = Control.MouseFilterEnum.Stop };
        _hearingModalShade.SetAnchorsPreset(Control.LayoutPreset.FullRect); _hearingShade.AddChild(_hearingModalShade);
        var modalCenter = new CenterContainer(); modalCenter.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hearingModalShade.AddChild(modalCenter);
        _hearingConfirm = new PanelContainer { CustomMinimumSize = new Vector2(830, 0) };
        var confirmStyle = PaperStyle(new Color("fff4d6")); confirmStyle.BorderColor = new Color("39433c");
        confirmStyle.ShadowSize = 12; _hearingConfirm.AddThemeStyleboxOverride("panel", confirmStyle);
        modalCenter.AddChild(_hearingConfirm);
        var confirmMargin = HearingMargins(30, 27); _hearingConfirm.AddChild(confirmMargin);
        var confirmBox = new VBoxContainer(); confirmBox.AddThemeConstantOverride("separation", 11); confirmMargin.AddChild(confirmBox);
        _hearingConfirmTitle = LabelText("", 40, ink);
        _hearingConfirmTitle.AddThemeFontOverride("font", HearingSerif()); confirmBox.AddChild(_hearingConfirmTitle);
        _hearingConfirmCopy = LabelText("", 25, ink); _hearingConfirmCopy.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        confirmBox.AddChild(_hearingConfirmCopy);
        var terms = new PanelContainer();
        var termsStyle = PaperStyle(new Color("f4e6bd"));
        termsStyle.BorderWidthLeft = 0; termsStyle.BorderWidthTop = 0;
        termsStyle.BorderWidthRight = 0; termsStyle.BorderWidthBottom = 0; termsStyle.ShadowSize = 0;
        terms.AddThemeStyleboxOverride("panel", termsStyle);
        confirmBox.AddChild(terms);
        var termsMargin = HearingMargins(11, 9); terms.AddChild(termsMargin);
        _hearingConfirmTerms = LabelText("", 20, ink); termsMargin.AddChild(_hearingConfirmTerms);
        var confirmActions = new HBoxContainer(); confirmActions.AddThemeConstantOverride("separation", 10); confirmBox.AddChild(confirmActions);
        confirmActions.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var back = ButtonText("Go back", () => { _pendingHearingDecision = null; _hearingModalShade!.Visible = false; });
        back.CustomMinimumSize = new Vector2(130, 50); back.AddThemeFontSizeOverride("font_size", 21);
        var backStyle = PaperStyle(new Color("fff4d6")); backStyle.BorderColor = new Color("39433c"); backStyle.ShadowSize = 0;
        back.AddThemeStyleboxOverride("normal", backStyle);
        back.AddThemeColorOverride("font_color", ink); confirmActions.AddChild(back);
        _hearingConfirmAction = ButtonText("", ApplyHearingDecision);
        _hearingConfirmAction.CustomMinimumSize = new Vector2(185, 50);
        _hearingConfirmAction.AddThemeFontSizeOverride("font_size", 21);
        var actionStyle = PaperStyle(new Color("39433c")); actionStyle.BorderColor = new Color("39433c"); actionStyle.ShadowSize = 0;
        _hearingConfirmAction.AddThemeStyleboxOverride("normal", actionStyle);
        _hearingConfirmAction.AddThemeColorOverride("font_color", new Color("fff9e8"));
        _hearingConfirmAction.AddThemeColorOverride("font_hover_color", new Color("fff9e8"));
        confirmActions.AddChild(_hearingConfirmAction);
        _hearingModalShade.Visible = false;
        _hearingShade.Visible = false;
    }

    private void RefreshHearingHud()
    {
        if (_hearingShade is null) return;
        var preparation = _session.CapturePreparation();
        var lifecycle = _session.CaptureLifecycleSnapshot();
        var show = preparation?.Status == PreparationStatus.Failed && lifecycle is { Hearings.Count: > 0 };
        _hearingShade.Visible = show;
        if (!show) { _pendingHearingDecision = null; _hearingModalShade!.Visible = false; return; }
        var hearing = lifecycle!.Hearings[^1];
        var casualty = lifecycle.Casualties[^1];
        var account = CouncilHearingPresenter.From(casualty);
        var weekendDay = _session.CaptureProgramme() is null ? new[] { "FRIDAY", "SATURDAY", "SUNDAY" }[Math.Clamp((int)((casualty.Tick - preparation!.StartedTick) / 12_800), 0, 2)] : "FESTIVAL DAY";
        _hearingMast!.Text = $"TIER {preparation!.Tier} · {weekendDay} · ATTEMPT {preparation.Attempt}";
        _hearingBalance!.Text = $"{lifecycle.FixtureFavourBalance}";
        _hearingBalanceState!.Text = lifecycle.FixtureFavourBalance == 0 ? "NONE LEFT" : "AVAILABLE";
        _hearingPerson!.Text = account.Person;
        _hearingRecord!.Text = account.Cause;
        _hearingSequence!.Text = account.Sequence;
        var showChoices = hearing.Status == HearingStatus.Open && lifecycle.FixtureFavourBalance > 0;
        _hearingChoices!.Visible = showChoices;
        _hearingRetryDetail!.Text = $"Retry Tier {preparation.Tier} · same tier · balance {lifecycle.FixtureFavourBalance} → {lifecycle.FixtureFavourBalance - 1}";
        _hearingRetry!.Disabled = lifecycle.FixtureFavourBalance == 0;
        _hearingOutcome!.Visible = !showChoices;
        _hearingOutcome!.Text = hearing.Status == HearingStatus.Conceded
            ? "Campaign ended. The casualty remains in the campaign record."
            : hearing.Status == HearingStatus.LostNoFavour
                ? "No Favour remains. The licence is lost; the campaign is over and no retry remains."
                : "This weekend has ended.";
        _hearingOutcome.Text = FestivalCopy(_hearingOutcome.Text);
        _hearingFoot!.Text = showChoices
            ? $"This weekend has ended. Spend 1 Favour to retry Tier {preparation.Tier}, or concede the licence."
            : "This weekend has ended.";
        _hearingFoot.Text = FestivalCopy(_hearingFoot.Text);
    }

    private void ConfirmHearing(SessionCommand command)
    {
        _pendingHearingDecision = command;
        var preparation = _session.CapturePreparation()!;
        var balance = _session.CaptureLifecycleSnapshot()!.FixtureFavourBalance;
        if (command is SpendCouncilFavourCommand)
        {
            _hearingConfirmTitle!.Text = "It won’t happen again?";
            _hearingConfirmCopy!.Text = $"Spend 1 Favour to keep the licence and retry Tier {preparation.Tier}. This failed weekend stays on the record.";
            _hearingConfirmCopy.Text = FestivalCopy(_hearingConfirmCopy.Text);
            _hearingConfirmTerms!.Text = $"Favour {balance} → {balance - 1} · same-tier retry";
            _hearingConfirmAction!.Text = "Spend 1 Favour";
        }
        else
        {
            _hearingConfirmTitle!.Text = "End this campaign?";
            _hearingConfirmCopy!.Text = "Concede the licence after this death. There will be no same-tier retry.";
            _hearingConfirmTerms!.Text = "Campaign ends · no Favour spent";
            _hearingConfirmAction!.Text = "End campaign";
        }
        _hearingModalShade!.Visible = true;
    }

    private void ApplyHearingDecision()
    {
        if (_pendingHearingDecision is null) return;
        var command = _pendingHearingDecision;
        _pendingHearingDecision = null;
        _hearingModalShade!.Visible = false;
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
            ResetFinanceFeedback();
            foreach (var visual in _attendeeVisuals.Values) visual.QueueFree();
            _attendeeVisuals.Clear(); _attendeePickRegistry.Clear(); _selectedAttendeeId = null;
            ResetLivePerformancePresentation();
            _foundationClock.ResetBoundary(); _foundationPresentation.Reset(_session.CaptureObservation());
            if (_session.CaptureObservation().NavigationAgents.Count > 0) BuildAttendee();
        }
        RefreshPreparationHud();
    }
}

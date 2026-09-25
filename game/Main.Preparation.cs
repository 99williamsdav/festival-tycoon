using Festival.Simulation;
using Festival.Persistence;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;

namespace Festival.Game;

public partial class Main
{
    private Label _preparationSummary = null!;
    private Label _preparationPeople = null!;
    private ScrollContainer? _preparationRosterScroll;
    private readonly Dictionary<string, Button> _offerButtons = [];
    private Button _preparationStart = null!;
    private Button? _communityShareButton;
    private Label? _waterFoundationHeading;
    private Button? _waterPlaceButton;
    private Button? _waterMoveButton;
    private Button? _waterTowerButton;
    private Label? _waterPlacementStatus;
    private Label? _communityShareInfo;
    private string _preparationMessage = "Choose one act and one worker. Equipment and stock are optional.";
    private string? _preparationCaptureDirectory;
    private int _preparationCaptureFrame;
    private readonly List<double> _preparationFrameMilliseconds = [];
    private long _preparationPriorTimestamp;
    private bool _preparationSaveBlocked;
    private int _preparationMeasurementTier;
    private long _preparationMeasurementStarted;
    private long _preparationLiveStarted;
    private readonly List<double> _preparationWorkMilliseconds = [];
    private readonly List<double> _preparationMovingFrameMilliseconds = [];

    private void BuildPreparationHud()
    {
        var layer = new CanvasLayer(); AddChild(layer);
        var viewportWidth = GetViewport().GetVisibleRect().Size.X;
        var viewportHeight = GetViewport().GetVisibleRect().Size.Y;
        var rightPanelX = viewportWidth - 420;
        var livePanel = new PanelContainer { Position = new Vector2((viewportWidth - 400) / 2, 16), Size = new Vector2(400, 66) };
        livePanel.AddThemeStyleboxOverride("panel", PaperStyle(new Color("f5e9c9"))); layer.AddChild(livePanel);
        _liveSetCue = LabelText("STAGE • awaiting booking", 16, new Color("29352c"));
        livePanel.AddChild(_liveSetCue);
        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        panel.Position = new Vector2(16, 16); panel.Size = new Vector2(410, 680);
        panel.AddThemeStyleboxOverride("panel", PaperStyle(new Color("f5e9c9"))); layer.AddChild(panel);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(390, 660) }; panel.AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 6); scroll.AddChild(box);
        var ink = new Color("29352c");
        box.AddChild(LabelText("LOWER WITTERING • PREPARATION", 19, ink));
        _preparationSummary = LabelText("", 15, ink);
        _preparationSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _preparationSummary.CustomMinimumSize = new Vector2(370, 140); _preparationSummary.MaxLinesVisible = 9; box.AddChild(_preparationSummary);
        if (_session.CaptureMedical() is not null)
            box.AddChild(LabelText("GENERATOR • safe 80% baseline", 13, ink));
        else if (_session.CaptureEquipment() is not null) BuildEquipmentControls(box);
        if (_session.CaptureMedical() is not null) BuildMedicalControls(box);
        if (_session.CaptureDisorder() is not null) BuildDisorderControls(box);
        foreach (var offer in _session.GetPreparationOffers().OrderBy(item => item.Category == "maintenance" ? 0 : 1))
        {
            var button = ButtonText($"{offer.Name}  £{offer.PricePennies / 100m:0}", () => PreparationAccept(offer.Id));
            button.AddThemeFontSizeOverride("font_size", 14);
            button.ClipText = true; button.TooltipText = offer.Name;
            _offerButtons.Add(offer.Id, button); box.AddChild(button);
        }
        if (_session.CommunityWaterShareDisclosure is { } disclosure)
        {
            _communityShareInfo = LabelText(disclosure, 13, ink);
            _communityShareInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _communityShareInfo.CustomMinimumSize = new Vector2(370, 0);
            box.AddChild(_communityShareInfo);
            _communityShareButton = ButtonText("SHARE FREE WATER • THIS WEEKEND", () => CommitEquipmentAction(new CommitCommunityWaterShareCommand()));
            _communityShareButton.TooltipText = disclosure;
            box.AddChild(_communityShareButton);
        }
        if (_session.CaptureMedical() is not null)
        {
            _waterFoundationHeading = LabelText("FREE WATER • PLACE BEFORE OPENING", 14, ink);
            box.AddChild(_waterFoundationHeading);
            _waterPlaceButton = ButtonText("ADD TAP • CHOOSE A GRASS SPOT", () => BeginWaterPlacement(false));
            box.AddChild(_waterPlaceButton);
            _waterMoveButton = ButtonText("MOVE ORIGINAL TAP • CHOOSE A GRASS SPOT", () => BeginWaterPlacement(true));
            box.AddChild(_waterMoveButton);
            _waterPlacementStatus = LabelText("Choose a tap action, then click valid ground. Right-click or Esc cancels.", 13, ink);
            _waterPlacementStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            box.AddChild(_waterPlacementStatus);
            _waterTowerButton = ButtonText("BUILD WATER TOWER • +4 PERSONAL RELIEF", () =>
                CommitEquipmentAction(new ApplyWaterFoundationEffectCommand("water.tower")));
            box.AddChild(_waterTowerButton);
        }
        _preparationStart = ButtonText("START FIXED ROSTER", PreparationStart); box.AddChild(_preparationStart);
        var controls = new HBoxContainer(); box.AddChild(controls);
        controls.AddChild(ButtonText("PAUSE", () =>
        {
            _session.Execute(CampaignEnvelope(new SetPausedCommand(!_session.IsPaused))); RefreshPreparationHud();
        }));
        controls.AddChild(ButtonText("SAVE", PreparationSave));
        controls.AddChild(ButtonText("LOAD", PreparationLoad));
        _stageMuteButton = ButtonText("MUTE AUDIO", ToggleStageMute); box.AddChild(_stageMuteButton);
        controls.AddChild(ButtonText("RETRY SAVE", () => { _preparationSaveBlocked = false; _preparationMessage = "Retrying pending boundary."; RefreshPreparationHud(); }));
        var rosterPanel = new PanelContainer { Position = new Vector2(rightPanelX, 16), Size = new Vector2(400, 210) };
        rosterPanel.AddThemeStyleboxOverride("panel", PaperStyle(new Color("f5e9c9"))); layer.AddChild(rosterPanel);
        _preparationRosterScroll = new ScrollContainer { CustomMinimumSize = new Vector2(380, 190) };
        rosterPanel.AddChild(_preparationRosterScroll);
        _preparationPeople = LabelText("", 15, ink); _preparationPeople.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _preparationPeople.CustomMinimumSize = new Vector2(360, 0);
        _preparationRosterScroll.AddChild(_preparationPeople);
        var inspectorHeight = Math.Max(300f, viewportHeight - 254f);
        var inspector = new PanelContainer { Position = new Vector2(rightPanelX, 238),
            Size = new Vector2(400, inspectorHeight) };
        inspector.AddThemeStyleboxOverride("panel", PaperStyle(new Color("f5e9c9"))); layer.AddChild(inspector);
        var inspectorScroll = new ScrollContainer { CustomMinimumSize = new Vector2(380, inspectorHeight - 20) };
        inspector.AddChild(inspectorScroll);
        var detail = new VBoxContainer { CustomMinimumSize = new Vector2(375, 0) }; inspectorScroll.AddChild(detail);
        _inspectorTitle = LabelText("Inspect the persistent farm", 18, ink); detail.AddChild(_inspectorTitle);
        BuildSatisfactionBar(detail);
        BuildMedicalNeedBars(detail);
        _inspectorBody = LabelText("Click a building to inspect its retained identity.\nAll guests and workers remain protected people.", 14, ink);
        _inspectorBody.AutowrapMode = TextServer.AutowrapMode.WordSmart; detail.AddChild(_inspectorBody);
        BuildMedicalActionInspector(detail);
        BuildDisorderActionInspector();
        BuildStagePowerAction(detail);
        BuildSecurityPostInspectorAction(detail);
        BuildHearingHud(layer);
        RefreshPreparationHud();
    }

    private void PreparationAccept(string id)
    {
        if (_session.CaptureEquipment() is not null)
        {
            CommitEquipmentAction(new AcceptPreparationOfferCommand(id));
            return;
        }
        var result = _session.Execute(CampaignEnvelope(new AcceptPreparationOfferCommand(id)));
        _preparationMessage = result.IsAccepted ? $"Committed and paid: {_session.GetPreparationOffers().Single(item => item.Id == id).Name}" : result.Message;
        RefreshPreparationHud();
    }

    private void PreparationStart()
    {
        CancelWaterPlacement();
        var candidate = GameSession.Restore(_session.CapturePersistenceSnapshot());
        if (!candidate.IsSuccess) { _preparationMessage = candidate.Error!; RefreshPreparationHud(); return; }
        var command = new CommandEnvelope(new CommandId(1_010_000UL + candidate.Session!.NextSubmissionSequence),
            candidate.Session.CampaignId, candidate.Session.Phase, candidate.Session.CurrentTick, candidate.Session.NextSubmissionSequence, null, new StartPreparedEditionCommand());
        var result = candidate.Session.Execute(command);
        if (!result.IsAccepted) { _preparationMessage = result.Message; RefreshPreparationHud(); return; }
        var saved = AutosaveRotation.Save(SaveDirectory, candidate.Session, _saveCompatibility, DateTimeOffset.UtcNow, _autosaveGeneration);
        if (!saved.IsSuccess) { _preparationMessage = "Start not applied: autosave failed."; RefreshPreparationHud(); return; }
        _autosaveGeneration++; _session = candidate.Session;
        ResetLivePerformancePresentation();
        BuildAttendee(); _foundationClock.ResetBoundary(); _foundationPresentation.Reset(_session.CaptureObservation());
        _preparationMessage = "Autosaved. Everyone now walks into the field.";
        RefreshPreparationHud();
    }

    private void PreparationSave()
    {
        var result = SaveFileAdapter.SaveSlot(SaveDirectory, "manual-preparation", new SaveWriteRequest(_session, _saveCompatibility, "manual", DateTimeOffset.UtcNow));
        _preparationMessage = result.IsSuccess ? "Preparation / live weekend saved." : result.Error!;
        RefreshPreparationHud();
    }

    private void PreparationLoad()
    {
        CancelWaterPlacement();
        var result = SaveFileAdapter.LoadSlot(SaveDirectory, "manual-preparation", _saveCompatibility);
        if (result.IsSuccess && result.Session!.CapturePreparation() is not null)
        {
            foreach (var visual in _attendeeVisuals.Values) visual.QueueFree();
            _attendeeVisuals.Clear(); _attendeePickRegistry.Clear(); _selectedAttendeeId = null; ClearSecurityPostSelection(); _session = result.Session;
            ClearSelection();
            SyncExtraWaterWorld();
            RefreshMedicalNeedBars(null);
            ResetLivePerformancePresentation();
            if (_session.CaptureObservation().NavigationAgents.Count > 0) BuildAttendee();
            _foundationClock.ResetBoundary(); _foundationPresentation.Reset(_session.CaptureObservation());
            _preparationSaveBlocked = false;
            _preparationMessage = "Loaded with the same offers, ownership and physical roster.";
        }
        else _preparationMessage = result.Error ?? "Save is not a prepared weekend.";
        RefreshPreparationHud();
    }

    private void RefreshPreparationHud()
    {
        var p = _session.CapturePreparation()!;
        var snapshot = _session.CaptureSnapshot();
        var finance = snapshot.FestivalFinances.Single(item => item.OwnerId.Value == p.FinanceOwnerId);
        var day = new[] { "FRIDAY", "SATURDAY", "SUNDAY" }[Math.Min(2, (int)((_session.CurrentTick - p.StartedTick) / 12_800))];
        _preparationSummary.Text = $"Tier {p.Tier} • {p.Status}{(_session.IsPaused || _preparationSaveBlocked ? " • PAUSED" : "")} • {day}\n" +
            $"£{finance.CashPennies / 100m:0.00} • debt £800 • stock {snapshot.OwnedStocks.Single(item => item.ServiceId.Value == p.StockId).Quantity}\n" +
            $"{p.Tier * 20} mandatory guests + {p.People.Count(item => item.Role == ProtectedPersonRole.Staff)} staff + 3 performers\n" +
            $"Owned rig: {p.OwnedEquipment.Length} • rental: {p.Rentals.Length} • known staff: {p.Contacts.Length}\n" +
            $"8 live minutes + preparation/pauses; provisional pace.\n{_preparationMessage}";
        foreach (var (id, button) in _offerButtons)
        {
            button.Disabled = _session.ValidateCommand(CampaignEnvelope(new AcceptPreparationOfferCommand(id))) is not null;
            button.Visible = p.Status == PreparationStatus.Preparing;
        }
        _preparationStart.Disabled = _session.ValidateCommand(CampaignEnvelope(new StartPreparedEditionCommand())) is not null;
        if (_communityShareButton is not null)
        {
            _communityShareButton.Visible = p.Status == PreparationStatus.Preparing;
            _communityShareButton.Disabled = _session.ValidateCommand(CampaignEnvelope(new CommitCommunityWaterShareCommand())) is not null;
            _communityShareInfo!.Text = p.CommunityShareAttempt == 0 ? _session.CommunityWaterShareDisclosure! :
                $"SHARING COMMITTED • weekend attempt {p.CommunityShareAttempt}. Personal baseline cap 12 thirst units/tick for faster drinkers before tower +4; queues may grow. " +
                (p.CommunityFavourClaimed ? "1 Council Favour awarded after the full weekend." : "1 Council Favour only after the full weekend is honoured.");
        }
        if (_waterPlaceButton is not null)
        {
            _waterPlaceButton.Visible = p.Status == PreparationStatus.Preparing;
            _waterPlaceButton.Disabled = p.ExtraWaterSiteIds.Length >= 2;
            _waterMoveButton!.Visible = p.Status == PreparationStatus.Preparing;
            _waterTowerButton!.Visible = p.Status == PreparationStatus.Preparing;
            _waterTowerButton.Disabled = _session.ValidateCommand(CampaignEnvelope(new ApplyWaterFoundationEffectCommand("water.tower"))) is not null;
            _waterPlacementStatus!.Visible = p.Status == PreparationStatus.Preparing;
        }
        if (_waterFoundationHeading is not null) _waterFoundationHeading.Visible = p.Status == PreparationStatus.Preparing;
        _preparationSummary.TooltipText = _preparationMessage;
        var examples = p.People.Where(item => item.Role == ProtectedPersonRole.Guest).Take(2)
            .Concat(p.People.Where(item => item.Role != ProtectedPersonRole.Guest));
        _preparationPeople.Text = "FIXED WEEKEND ROSTER\n" +
            $"Arrived {p.People.Count(item => item.Admitted)}/{p.People.Length} • departed {p.People.Count(item => item.Departed)}/{p.People.Length}\n\n" +
            string.Join("\n\n", examples.Select(item => $"[{(item.Name == "Jordan Hale" ? "STEWARD" : item.Role.ToString().ToUpperInvariant())}] {item.Name}\n" +
                (item.Role == ProtectedPersonRole.Guest ? $"expects {(item.ExpectedGenre == 0 ? "folk" : "punk")} • satisfaction {item.Satisfaction / 100m:0}% • music risk {item.MusicRisk / 100m:0}%" : "Protected • physical arrival and departure"))) +
            (_session.CaptureEquipment() is null ? "\n\nNo lethal chains or success rewards in this preparation slice." : "\n\nEquipment chain active. Fatal hearings are recorded.");
        RefreshLivePerformanceHud();
        RefreshEquipmentControls();
        RefreshMedicalControls();
        RefreshDisorderControls();
        RefreshStagePowerAction();
        RefreshHearingHud();
    }

    private void AdvancePreparationPresentation(double delta)
    {
        var workStarted = Stopwatch.GetTimestamp();
        _foundationClock.IsPaused = _session.IsPaused || _preparationSaveBlocked || _session.CapturePreparation()!.Status is not (PreparationStatus.Running or PreparationStatus.Departing);
        if (_equipmentPerformanceOutput is not null) { _equipmentDeltaMs = delta * 1000; _equipmentDebtBefore = _foundationClock.DebtTicks; }
        var ticks = _foundationClock.Schedule(delta);
        if (_equipmentPerformanceOutput is not null) _equipmentScheduledTicks = ticks;
        for (var tick = 0; tick < ticks; tick++)
        {
            var advanced = PreparationAdvanceCoordinator.AdvanceOne(SaveDirectory, _session, _saveCompatibility, DateTimeOffset.UtcNow, _autosaveGeneration);
            if (!advanced.IsSuccess)
            {
                _preparationSaveBlocked = true; _preparationMessage = advanced.Error!;
                _foundationClock.ResetBoundary(); RefreshPreparationHud(); break;
            }
            _session = advanced.Session;
            if (advanced.Autosave is not null) { _autosaveGeneration++; _preparationMessage = $"{_session.CapturePreparation()!.Status} boundary autosaved."; }
            _foundationPresentation.Advance(_session.CaptureObservation());
        }
        var presentationStarted = Stopwatch.GetTimestamp();
        if (_preparationProfileOutput is not null || _equipmentPerformanceOutput is not null) _profileSimulationMs = Stopwatch.GetElapsedTime(workStarted, presentationStarted).TotalMilliseconds;
        var live = _session.CaptureLivePerformance();
        var watching = live is { Stage: LiveSetStage.BeforeSet or LiveSetStage.Live or LiveSetStage.Interrupted }
            ? live.Listeners.Where(item => item.AtPlace).Select(item => new EntityId(item.AgentId)).ToHashSet()
            : [];
        var onStage = live?.Performers.Where(item => item.OnStage).Select(item => new EntityId(item.AgentId)).ToHashSet() ?? [];
        var casualtyName = _session.CapturePreparation()?.Status == PreparationStatus.Failed
            ? _session.CaptureLifecycleSnapshot()?.Casualties.LastOrDefault()?.PersonId : null;
        var casualtyId = casualtyName is null ? (EntityId?)null : _session.CapturePreparation()!.People
            .Where(item => item.Name == casualtyName).Select(item => (EntityId?)new EntityId(item.AgentId)).FirstOrDefault();
        var collapsed = _session.CaptureMedical()?.Needs.Where(item => item.Intent == MedicalIntent.Collapsed)
            .Select(item => new EntityId(item.AgentId)).ToHashSet() ?? [];
        foreach (var agent in _session.CaptureObservation().NavigationAgents)
        {
            var position = _foundationPresentation.Sample(agent.Id, _foundationClock.InterpolationFraction);
            var visual = _attendeeVisuals[agent.Id];
            var renderedPosition = new Vector3((float)(position.XMillimetres / 1000), 0.04f, (float)(position.ZMillimetres / 1000));
            visual.Position = renderedPosition;
            if (agent.Id == casualtyId || collapsed.Contains(agent.Id))
            {
                // Show collapse through the entire rescue window, not only after death.
                visual.Position = renderedPosition + new Vector3(0, .75f, 0);
                visual.Rotation = new Vector3(Mathf.Pi / 2f, visual.Rotation.Y, 0);
                continue;
            }
            if (Mathf.Abs(visual.Rotation.X) > .1f) visual.Rotation = new Vector3(0, visual.Rotation.Y, 0);
            UpdatePersonFacing(agent.Id, visual, renderedPosition, agent.Action,
                watching.Contains(agent.Id), onStage.Contains(agent.Id), delta);
        }
        AdvanceLivePerformancePresentation(delta);
        AdvanceMedicalCuePresentation();
        AdvanceDisorderCuePresentation();
        if (_selectedAttendeeId is not null) RefreshAttendeeInspector();
        AdvanceIncidentAudioPresentation();
        ProcessLivePerformanceCapture();
        if (_autosaveScheduler.Advance(delta))
        {
            var result = AutosaveRotation.Save(SaveDirectory, _session, _saveCompatibility, DateTimeOffset.UtcNow, _autosaveGeneration);
            if (result.IsSuccess) _autosaveGeneration++;
            else { _preparationMessage = $"Periodic autosave failed: {result.Error}"; RefreshPreparationHud(); }
        }
        if (Engine.GetProcessFrames() % 15 == 0) RefreshPreparationHud();
        if (_preparationMeasurementTier > 0 && _preparationLiveStarted != 0)
            _preparationWorkMilliseconds.Add(Stopwatch.GetElapsedTime(workStarted).TotalMilliseconds);
        var captureStarted = Stopwatch.GetTimestamp();
        if (_preparationProfileOutput is not null || _equipmentPerformanceOutput is not null) _profilePresentationMs = Stopwatch.GetElapsedTime(presentationStarted, captureStarted).TotalMilliseconds;
        if (_preparationCaptureDirectory is not null &&
            (_preparationProfileOutput is null || _preparationProfileCapture || _preparationCaptureFrame < 10)) ProcessPreparationCapture();
        if (_preparationProfileOutput is not null || _equipmentPerformanceOutput is not null) _profileCaptureMs = Stopwatch.GetElapsedTime(captureStarted).TotalMilliseconds;
    }

    private void ProcessPreparationCapture()
    {
        _preparationCaptureFrame++;
        var now = Stopwatch.GetTimestamp();
        if (_preparationMeasurementStarted == 0) _preparationMeasurementStarted = now;
        if (_preparationPriorTimestamp != 0 && _preparationCaptureFrame > 30)
            _preparationFrameMilliseconds.Add(Stopwatch.GetElapsedTime(_preparationPriorTimestamp, now).TotalMilliseconds);
        if (_preparationMeasurementTier > 0 && _preparationPriorTimestamp != 0 &&
            _session.CaptureObservation().NavigationAgents.Any(item => item.Action == AgentNavigationAction.Travelling))
            _preparationMovingFrameMilliseconds.Add(Stopwatch.GetElapsedTime(_preparationPriorTimestamp, now).TotalMilliseconds);
        _preparationPriorTimestamp = now;
        Directory.CreateDirectory(_preparationCaptureDirectory!);
        if (_preparationCaptureFrame == 5)
        {
            _offerButtons["act.folk"].EmitSignal(Button.SignalName.Pressed);
            _offerButtons["staff.steward"].EmitSignal(Button.SignalName.Pressed);
            _offerButtons["equipment.buy"].EmitSignal(Button.SignalName.Pressed);
        }
        if (_preparationCaptureFrame == 8) GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_preparationCaptureDirectory!, "preparation.png"));
        if (_preparationCaptureFrame == 10)
        {
            _preparationStart.EmitSignal(Button.SignalName.Pressed);
            if (_preparationProfileOutput is not null && _preparationProfileDeparture)
            {
                _session.AdvanceWithoutSnapshot(GameSession.PreparedWeekendTicks);
                _session = GameSession.Restore(_session.CapturePersistenceSnapshot()).Session!;
                _foundationPresentation.Reset(_session.CaptureObservation());
                _foundationClock.ResetBoundary();
            }
            _preparationLiveStarted = Stopwatch.GetTimestamp();
        }
        if (_preparationMeasurementTier > 0)
        {
            if (_preparationProfileOutput is not null) return;
            if (_session.PreparedStatus == PreparationStatus.Finished || _preparationSaveBlocked ||
                Stopwatch.GetElapsedTime(_preparationMeasurementStarted).TotalSeconds > 660)
            {
                var p = _session.CapturePreparation()!;
                var latest = AutosaveRotation.LoadNewestValid(SaveDirectory, _saveCompatibility);
                var exact = latest.IsSuccess && latest.Session!.CaptureSnapshot().AuthoritativeHash == _session.CaptureSnapshot().AuthoritativeHash;
                var passed = p.Status == PreparationStatus.Finished && p.People.All(item => item.Admitted && item.Departed) && exact;
                var liveSeconds = Stopwatch.GetElapsedTime(_preparationLiveStarted).TotalSeconds;
                static string Stats(List<double> samples)
                {
                    if (samples.Count == 0) return "unavailable";
                    var sorted = samples.Order().ToArray();
                    double Percentile(double p) => sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];
                    return $"n={samples.Count};mean={samples.Average():0.000};p50={Percentile(.50):0.000};p95={Percentile(.95):0.000};p99={Percentile(.99):0.000};max={sorted[^1]:0.000}";
                }
                File.WriteAllText(Path.Combine(_preparationCaptureDirectory!, "complete-attempt.txt"),
                    $"passed={passed}\ntier={p.Tier}\npeople={p.People.Length}\nadmitted={p.People.Count(item => item.Admitted)}\ndeparted={p.People.Count(item => item.Departed)}\n" +
                    $"newestAutosaveExact={exact}\nticks={_session.CurrentTick}\nliveWallSeconds={liveSeconds:0.000}\nscriptedAttemptWallSeconds={Stopwatch.GetElapsedTime(_preparationMeasurementStarted).TotalSeconds:0.000}\n" +
                    $"attainedSpeed={_session.CurrentTick / (80 * liveSeconds):0.000000}\nframeMs={Stats(_preparationFrameMilliseconds)}\nmovingFrameMs={Stats(_preparationMovingFrameMilliseconds)}\nworkMs={Stats(_preparationWorkMilliseconds)}\n" +
                    $"peakWorkingSetBytes={Process.GetCurrentProcess().PeakWorkingSet64}\nstatus={p.Status}\nmessage={_preparationMessage}\n" +
                    "Scripted preparation; no human decision or pause time included. One bounded 1x run, not a general capacity claim.\n");
                RefreshPreparationHud();
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_preparationCaptureDirectory!, "complete-attempt.png"));
                GetTree().Quit(passed ? 0 : 2);
            }
            return;
        }
        if (_preparationCaptureFrame == 100)
        {
            _session.Execute(CampaignEnvelope(new SetPausedCommand(true)));
            var hash = _session.CaptureSnapshot().AuthoritativeHash;
            PreparationSave(); PreparationLoad();
            var exact = hash == _session.CaptureSnapshot().AuthoritativeHash;
            var p = _session.CapturePreparation()!;
            var passed = exact && p.Status == PreparationStatus.Running && _session.CaptureObservation().NavigationAgents.Count == 22;
            File.WriteAllText(Path.Combine(_preparationCaptureDirectory!, "verification.txt"),
                $"passed={passed}\nrestoreExact={exact}\npeople={p.People.Length}\ntick={_session.CurrentTick}\n" +
                $"meanFrameMs={_preparationFrameMilliseconds.Average():0.000}\nmaxFrameMs={_preparationFrameMilliseconds.Max():0.000}\n" +
                "Measurement: short runtime smoke only; not a capacity or complete-attempt pacing claim.\n");
        }
        if (_preparationCaptureFrame == 103)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_preparationCaptureDirectory!, "live-restored.png"));
            GetTree().Quit();
        }
    }
}

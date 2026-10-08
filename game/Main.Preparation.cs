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
    private PanelContainer? _contextPanel;
    private readonly Dictionary<string, Button> _offerButtons = [];
    private readonly Dictionary<string, OfferCard> _offerCards = [];
    private readonly Dictionary<EntityId, float> _personPace = [];
    private GridContainer? _rigChoices;
    private GridContainer? _generatorChoices;
    private ProgressBar? _generatorBar;
    private Label? _generatorText;
    private VBoxContainer? _preparationOfferBox;
    private int _preparationOfferInsertIndex;
    private Button _preparationStart = null!;
    private string _preparationMessage = "Choose one act and one worker. Equipment and stock are optional.";
    private long _preparationLiveStarted;

    private void BuildPreparationHud()
    {
        BuildHudWorkspace();
    }

    private static bool ContextVisualAvailable(Node3D? visual) => visual is not null &&
        GodotObject.IsInstanceValid(visual) && !visual.IsQueuedForDeletion() && visual.Visible;

    private void RefreshContextPanelVisibility()
    {
        if (_contextPanel is null) return;
        // The generator's power switches belong to the generator alone: selecting anything else hides them.
        RefreshPowerSwitches();
        if (_selectedBinId is null && _binEmptyButton is not null) _binEmptyButton.Visible = false;
        if (_selectedAttendeeId is null && _inspectorTraits is not null) _inspectorTraits.Visible = false;
        var farm = _selected is { } item && _visualRegistry.TryGetValue(item.StableId, out var farmVisual) && ContextVisualAvailable(farmVisual);
        var person = _selectedAttendeeId is { } id && _attendeeVisuals.TryGetValue(id, out var personVisual) && ContextVisualAvailable(personVisual);
        var vendor = _selectedImmersionVendor is { } vendorId && _session.CaptureVendors().Any(v => v.Id == vendorId) &&
            _immersionVendors.TryGetValue(vendorId, out var vendorVisual) && ContextVisualAvailable(vendorVisual);
        var facility = _selectedMedicalFacility switch
        {
            MedicalFacility.Water => _session.CaptureWaterPoints().Any(point => point.Id == _selectedWaterPointId),
            MedicalFacility.WaterTower => ContextVisualAvailable(_waterTowerVisual),
            MedicalFacility.FirstAid => _session.CaptureMedical() is not null,
            _ => false
        };
        _contextPanel.Visible = farm || person || vendor || facility ||
            (_selectedBinId is { } binId && _binViews.TryGetValue(binId, out var selectedBin) && ContextVisualAvailable(selectedBin.Body)) ||
            (_selectedToilet && _selectedToiletId is { } toiletId && _toiletViews.TryGetValue(toiletId, out var selectedToilet) && ContextVisualAvailable(selectedToilet.Body)) ||
            (_selectedSecurityPost && _session.CaptureDisorder() is not null && _securityPostPickId != 0) ||
            (_selectedGenerator && _session.CaptureEquipment() is not null && ContextVisualAvailable(_equipmentVisual)) ||
            (_selectedCowId is { } cowId && _looseCows.ContainsKey(cowId));
        if (Dock.Readiness is { } readiness)
            readiness.Visible = Dock.Visible && !((IPreparationNavigation)this).ReadinessCovered;
    }


    private void PreparationAccept(string id)
    {
        if (_session.CapturePreparationPlan() is { Committed: false } plan)
        {
            if (id.StartsWith("staff.", StringComparison.Ordinal) || id == "maintenance.worker")
            {
                var command = plan.OfferIds.Contains(id) ? (SessionCommand)new RemovePreparationOfferCommand(id) : new AcceptPreparationOfferCommand(id);
                var accepted = _host.Execute(command, out var error);
                _preparationMessage = accepted ? "Staff plan updated; saves every 30 unpaused seconds and at opening." : error ?? "Staff plan was not changed.";
                RefreshPreparationHud();
                return;
            }
            if (plan.OfferIds.Contains(id))
            { CommitEquipmentAction(new RemovePreparationOfferCommand(id)); return; }
        }
        if (_session.CaptureEquipment() is not null)
        {
            CommitEquipmentAction(new AcceptPreparationOfferCommand(id));
            return;
        }
        var result = _host.Submit(new AcceptPreparationOfferCommand(id));
        _preparationMessage = result.IsAccepted ? $"Committed and paid: {_session.GetPreparationOffers().Single(item => item.Id == id).Name}" : result.Message;
        RefreshPreparationHud();
    }

    private void PreparationStart()
    {
        if (_buildGhostKind is not null) { _preparationMessage = "Finish or cancel placement before opening."; RefreshPreparationHud(); return; }
        if (!_host.ExecuteMilestone(new StartPreparedEditionCommand(), "Festival start", out var error))
        { SyncSaveStatus(); _preparationMessage = error ?? _preparationMessage; RefreshPreparationHud(); return; }
        _host.TakeNotice();
        ResetLivePerformancePresentation();
        BuildAttendee(); _host.Clock.ResetBoundary(); _foundationPresentation.Reset(_session.CaptureObservation());
        _preparationMessage = "Festival start saved. Everyone now walks into the field.";
        RefreshPreparationHud();
    }

    private void PreparationSave()
    {
        var result = _host.SaveManual();
        _preparationMessage = result.IsSuccess ? FestivalCopy("Preparation / live weekend saved.") : result.Error!;
        RefreshPreparationHud();
    }

    private void PreparationLoad()
    {
        CancelBuildPlacement();
        ResetImmersionHeldVisuals();
        var previousProgrammeMode = _session.CaptureProgramme() is not null;
        var previousImmersionMode = _session.CaptureImmersion() is not null;
        if (_host.LoadManual(out var loadError))
        {
            SyncSaveStatus();
            Perks.CancelConfirmation();
            foreach (var visual in _attendeeVisuals.Values) visual.QueueFree();
            _attendeeVisuals.Clear(); _attendeePickRegistry.Clear(); _selectedAttendeeId = null; ClearSecurityPostSelection();
            ResetFinanceFeedback();
            ClearSelection();
            SyncExtraWaterWorld();
            SyncResponsePosts();
            SyncImmersionWorld();
            RefreshMedicalNeedBars(null);
            ResetLivePerformancePresentation();
            if (_session.CaptureObservation().NavigationAgents.Count > 0) BuildAttendee();
            _foundationPresentation.Reset(_session.CaptureObservation());
            if (previousProgrammeMode != (_session.CaptureProgramme() is not null) || previousImmersionMode != (_session.CaptureImmersion() is not null)) RebuildPreparationOffers();
            _preparationMessage = "Loaded with the same offers, ownership and physical roster.";
        }
        else _preparationMessage = loadError!;
        RefreshPreparationHud();
    }

    private void RefreshPreparationHud()
    {
        var p = _session.CapturePreparation()!;
        var snapshot = _session.CaptureSnapshot();
        var finance = snapshot.FestivalFinances.Single(item => item.OwnerId.Value == p.FinanceOwnerId);
        var immersion = _session.CaptureImmersion();
        var stockDescription = immersion is null ? $"stock {snapshot.OwnedStocks.Single(item => item.ServiceId.Value == p.StockId).Quantity}" :
            $"soft {immersion.SoftStock} • beer {immersion.BeerStock}";
        var day = _session.CaptureProgramme() is not null ? "FESTIVAL DAY" : new[] { "FRIDAY", "SATURDAY", "SUNDAY" }[Math.Min(2, (int)((_session.CurrentTick - p.StartedTick) / 12_800))];
        _preparationSummary.Text = $"Tier {p.Tier} • {p.Status}{(_session.IsPaused ? " • PAUSED" : "")} • {day}\n" +
            $"{FestivalCurrency.Format(finance.CashPennies)} • {stockDescription}\n" +
            $"{p.Tier * 20} mandatory guests + {p.People.Count(item => item.Role == ProtectedPersonRole.Staff)} staff + {p.People.Count(item => item.Role == ProtectedPersonRole.Performer)} performers\n" +
            $"Owned rig: {p.OwnedEquipment.Length} • rental: {p.Rentals.Length} • known staff: {p.Contacts.Length}\n" +
            $"8 live minutes + preparation/pauses; provisional pace.\n{_preparationMessage}";
        _preparationSummary.Text = FestivalCopy(_preparationSummary.Text);
        if (p.Plan is { Committed: false } planned)
            _preparationSummary.Text += $"\nExpected protected people: {_session.ExpectedPreparedPeopleCount}/50\nPlanned hires: {string.Join(", ", planned.OfferIds.Where(id => id.StartsWith("staff.") || id == "maintenance.worker").Select(id => _session.GetPreparationOffers().Single(o => o.Id == id).Name))}";
        Booking.Refresh();
        RefreshStaffPanel();
        RefreshImmersionControls();
        var offersById = _session.GetPreparationOffers().ToDictionary(offer => offer.Id);
        foreach (var (id, card) in _offerCards)
        {
            var issue = _session.ValidateCommand(CampaignEnvelope(new AcceptPreparationOfferCommand(id)))?.Message;
            var chosen = p.Plan?.OfferIds.Contains(id) ?? p.AcceptedOffers.Contains(id);
            var state = chosen ? OfferState.Chosen : issue is not null ? OfferState.Locked : OfferState.Available;
            var copy = OfferCopy(offersById[id], p, chosen, issue);
            var candidate = StaffCatalogue.ForOffer(_session.GetStaffCandidates(), id);
            // A candidate held in the role's other slot is locked because they are already on the crew.
            var otherSlot = candidate is null ? null : offersById[id].Category.StartsWith("extra-", StringComparison.Ordinal) ? candidate.Id : candidate.ExtraOfferId;
            var alreadyHired = otherSlot is not null && (p.Plan is { Committed: false } held ? held.OfferIds : p.AcceptedOffers).Contains(otherSlot);
            card.Show(copy.Initial, copy.Title, copy.Detail, offersById[id].PricePennies, state, copy.Choose, copy.Chosen, copy.Tooltip,
                alreadyHired ? "Already hired" : "Locked");
            if (candidate is not null)
            {
                card.SetStats(StaffRatings(candidate));
                card.SetTags(candidate.Traits.Select(trait => (StaffCatalogue.TraitLabel(trait), StaffCatalogue.IsPositive(trait))).ToArray());
            }
            card.Action.Disabled = issue is not null;
            var category = offersById[id].Category;
            card.Root.Visible = p.Status == PreparationStatus.Preparing &&
                (category != "extra-medic" || p.ExtraMedicSlotOwned) && (category != "extra-steward" || p.ExtraStewardSlotOwned);
        }
        if (_generatorBar is not null)
        {
            // The plan's busiest moment: the rig, both stalls and the festoon lights all on at dusk.
            var power = _session.CapturePower();
            _generatorBar.Value = Math.Min(100, power.Total * 100 / power.Capacity);
            _generatorBar.AddThemeStyleboxOverride("fill", Ui.Box(power.Over ? Ui.Alert : power.Total * 100 >= power.Capacity * 90 ? Ui.Warn : Ui.Teal, 4));
            _generatorText!.Text = power.Over ? $"{power.Total} of {power.Capacity} power · stretches the generator: overload risk" : $"{power.Total} of {power.Capacity} power · within capacity";
            _generatorText.AddThemeColorOverride("font_color", power.Over ? Ui.Link : Ui.InkMuted);
            _generatorText.TooltipText = $"Stage {power.Stage} ({PowerRules.RigName(_session.Rig)}) · bar {power.Bar} · food van {power.Food} · festoon lights {power.Lights} (from dusk)";
        }
        _preparationStart.Disabled = _session.ValidateCommand(CampaignEnvelope(new StartPreparedEditionCommand())) is not null;
        _preparationSummary.TooltipText = FestivalCopy(_preparationMessage);
        var examples = p.People.Where(item => item.Role == ProtectedPersonRole.Guest).Take(2)
            .Concat(p.People.Where(item => item.Role != ProtectedPersonRole.Guest));
        _preparationPeople.Text = FestivalCopy("FIXED WEEKEND ROSTER").ToUpperInvariant() + "\n" +
            $"Arrived {p.People.Count(item => item.Admitted)}/{p.People.Length} • departed {p.People.Count(item => item.Departed)}/{p.People.Length}\n\n" +
            string.Join("\n\n", examples.Select(item => $"[{PersonPresentationRole(item).ToUpperInvariant()}] {item.Name}\n" +
                (item.Role == ProtectedPersonRole.Guest ? $"prefers {(_session.CaptureProgramme() is null ? item.ExpectedGenre == 0 ? "folk" : "punk" : FestivalGenreName(item.ExpectedGenre))} • satisfaction {item.Satisfaction / 100m:0}% • music risk {item.MusicRisk / 100m:0}%" : "Protected • physical arrival and departure"))) +
            (_session.CaptureEquipment() is null ? "\n\nNo lethal chains or success rewards in this preparation slice." : "\n\nEquipment chain active. Fatal hearings are recorded.");
        RefreshLivePerformanceHud();
        RefreshEquipmentControls();
        RefreshMedicalControls();
        RefreshDisorderControls();
        RefreshStagePowerAction();
        Hearing.Refresh();
        RefreshHudWorkspace();
        Perks.Refresh();
        RefreshFestivalPaper();
    }

private void RebuildPreparationOffers()
    {
        if (_preparationOfferBox is not { } box) return;
        foreach (var card in _offerCards.Values) { card.Root.GetParent()?.RemoveChild(card.Root); card.Root.QueueFree(); }
        _offerCards.Clear(); _offerButtons.Clear();
        if (_session.CaptureProgramme() is not null && !Booking.IsBuilt)
        {
            Booking.Build(_hudPages.GetValueOrDefault("Programme") ?? box);
            if (_hudTabs is null) box.MoveChild(Booking.Root!, _preparationOfferInsertIndex++);
        }
        var index = _preparationOfferInsertIndex;
        static int Order(PreparationOffer offer) => offer.Category switch
        { "extra-medic" => 1, "extra-steward" => 2, _ => offer.Id switch { "equipment.rent" => 0, "equipment.buy" => 1, _ => 0 } };
        var candidates = _session.GetStaffCandidates();
        // Candidates show cheapest first, so the market's internal order is not on show.
        foreach (var offer in _session.GetPreparationOffers().OrderBy(Order).ThenBy(offer => StaffCatalogue.ForOffer(candidates, offer.Id) is null ? 0 : offer.PricePennies))
        {
            if (_session.CaptureImmersion() is not null && offer.Id == "contract.stock") continue;
            if (_session.CaptureProgramme() is not null && offer.Category == "act") continue;
            // Hiring lives in the Staff sheet's crew slots and candidate table.
            if (StaffCatalogue.IsWorkCategory(offer.Category)) continue;
            var id = offer.Id;
            var (layout, colour, icon, destination) = offer.Category switch
            {
                "equipment" => (OfferLayout.Choice, Ui.Teal, "volume-2", (Container?)_rigChoices),
                "generator" => (OfferLayout.Choice, Ui.Teal, "zap", _generatorChoices),
                _ => (OfferLayout.Row, Ui.InkMuted, "package", (Container?)null),
            };
            var candidate = StaffCatalogue.ForOffer(candidates, id);
            var card = new OfferCard(layout, colour, icon, () => PreparationAccept(id), candidate is null ? null : StaffStatLabels(candidate.Role));
            _offerCards.Add(id, card); _offerButtons.Add(id, card.Action);
            if (_hudTabs is null) { box.AddChild(card.Root); box.MoveChild(card.Root, index++); }
            else (destination ?? _hudPages[offer.Category == "contract" ? "Supplies" : "Staff"]).AddChild(card.Root);
        }
    }

    /// <summary>The name, detail and button wording an offer card shows.</summary>
    private (string Initial, string Title, string Detail, string Choose, string Chosen, string Tooltip) OfferCopy(PreparationOffer offer, PreparationSnapshot p, bool chosen, string? issue)
    {
        var parts = FestivalCopy(offer.Name).Split(" • ");
        var head = parts[0]; var tail = parts.Length > 1 ? parts[1] : "";
        var who = head.Contains(": ") ? head[..head.IndexOf(": ", StringComparison.Ordinal)] : head;
        var what = head.Contains(": ") ? head[(head.IndexOf(": ", StringComparison.Ordinal) + 2)..] : "";
        var tooltip = chosen ? "Remove this unpaid purchase from the setup plan." : issue ?? "Add or replace this choice in the unpaid setup plan. Payment is due at Start.";
        switch (offer.Category)
        {
            case "staff" or "medic" or "steward" or "extra-medic" or "extra-steward":
                var candidate = StaffCatalogue.ForOffer(_session.GetStaffCandidates(), offer.Id)!;
                var extra = offer.Category.StartsWith("extra-", StringComparison.Ordinal);
                if (issue is null || chosen)
                    tooltip = FestivalCopy($"{candidate.Name} · {(extra ? "extra " : "")}{StaffCatalogue.RoleName(candidate.Role)}\n{StaffCandidateAbilities(candidate)}\n" +
                        string.Concat(candidate.Traits.Select(trait => $"{StaffCatalogue.TraitLabel(trait).ToUpperInvariant()}  {StaffCatalogue.TraitDescription(trait)}\n")) +
                        $"Weekend contract; {(p.Plan is null ? "paid now" : "unpaid until Start, freely swap")}. Expires on any outcome.");
                return (candidate.Name[..1], extra ? $"{candidate.Name} · extra {StaffCatalogue.RoleName(candidate.Role)}" : candidate.Name, candidate.Blurb, "Hire", "Hired", tooltip);
            case "maintenance":
                return (who[..1], $"{who} · {what}", "Fixes physical breakdowns", "Hire", "Hired", tooltip);
            case "equipment" when _session.PowerBudgetActive:
            {
                var pro = offer.Id == PowerRules.ProRigOffer;
                var draw = pro ? PowerRules.ProRigDraw : PowerRules.StandardRigDraw;
                var peak = PlannedPeakWith(rig: pro ? SoundRig.Pro : SoundRig.Standard);
                return ("", pro ? "Pro rig · hire" : "Standard rig · hire",
                    $"{(pro ? "Great" : "Good")} sound · draws {draw} power" + (peak.Over ? " · stretches the generator" : ""), "", "Selected",
                    peak.Over ? $"With this rig the evening peak is {peak.Total} of {peak.Capacity} power: the generator is stretched and may overload. Hire a bigger generator to be safe.\n{tooltip}" : tooltip);
            }
            case "generator":
                return ("", "Bigger generator · hire", $"{PowerRules.HiredGeneratorCapacity} power · room for a bigger rig", "", "Selected", tooltip);
            case "equipment":
                var rent = offer.Id == "equipment.rent";
                return ("", rent ? "Rent · this festival" : "Buy · yours to keep", tail.Split(';')[0].Replace("quality", "sound quality", StringComparison.Ordinal), "", "Selected", tooltip);
            default:
                return (who[..1], head, tail, "Plan", "Planned", tooltip);
        }
    }

    private void AdvancePreparationPresentation(double delta)
    {
        var workStarted = Stopwatch.GetTimestamp();
        _host.Advance(delta, session => _foundationPresentation.Advance(session.CaptureObservation()));
        SyncSaveStatus();
        var presentationStarted = Stopwatch.GetTimestamp();
        var characterDelta = CharacterPresentationPaused ? 0 : delta;
        _characterPresentationSeconds += characterDelta;
        SyncPresentationPause();
        if (_preparationProfileOutput is not null) _profileSimulationMs = Stopwatch.GetElapsedTime(workStarted, presentationStarted).TotalMilliseconds;
        var live = _session.CaptureLivePerformance();
        var watching = live is { Stage: LiveSetStage.BeforeSet or LiveSetStage.Live or LiveSetStage.Interrupted }
            ? live.Listeners.Where(item => item.AtPlace).Select(item => new EntityId(item.AgentId)).ToHashSet()
            : [];
        var onStage = live?.Performers.Where(item => item.OnStage).Select(item => new EntityId(item.AgentId)).ToHashSet() ?? [];
        PrepareCrowdDance(live);
        var casualtyName = _session.CapturePreparation()?.Status == PreparationStatus.Failed
            ? _session.CaptureLifecycleSnapshot()?.Casualties.LastOrDefault()?.PersonId : null;
        var casualtyId = casualtyName is null ? (EntityId?)null : _session.CapturePreparation()!.People
            .Where(item => item.Name == casualtyName).Select(item => (EntityId?)new EntityId(item.AgentId)).FirstOrDefault();
        var collapsed = _session.CaptureMedical()?.Needs.Where(item => item.Intent == MedicalIntent.Collapsed)
            .Select(item => new EntityId(item.AgentId)).ToHashSet() ?? [];
        foreach (var agent in _session.CaptureObservation().NavigationAgents)
        {
            var position = _foundationPresentation.Sample(agent.Id, _host.Clock.InterpolationFraction);
            var visual = _attendeeVisuals[agent.Id];
            if (_session.GuestWaitingForRelease(agent.Id.Value))
            {
                visual.Hide();
                foreach (var body in visual.FindChildren("*", "StaticBody3D", true, false))
                    if (body is StaticBody3D collider) collider.CollisionLayer = 0;
                if (_selectedAttendeeId == agent.Id) ClearSelection();
                continue;
            }
            if (_session.CapturePreparation()!.People.Any(person => person.AgentId == agent.Id.Value && person.Departed))
            {
                visual.Hide();
                foreach (var key in _attendeePickRegistry.Where(pair => pair.Value == agent.Id).Select(pair => pair.Key).ToArray()) _attendeePickRegistry.Remove(key);
                foreach (var body in visual.FindChildren("*", "StaticBody3D", true, false)) if (body is StaticBody3D collider) collider.CollisionLayer = 0;
                if (_selectedAttendeeId == agent.Id) ClearSelection();
                continue;
            }
            if (!visual.Visible)
            {
                visual.Show();
                foreach (var body in visual.FindChildren("*", "StaticBody3D", true, false))
                    if (body is StaticBody3D collider) collider.CollisionLayer = 1;
            }
            var renderedPosition = new Vector3((float)(position.XMillimetres / 1000), 0.04f, (float)(position.ZMillimetres / 1000));
            // How fast they're really moving, smoothed over a few frames, sets a walking guest's stride.
            var moved = _lastPresentedPersonPositions.TryGetValue(agent.Id, out var before) && characterDelta > 0
                ? new Vector3(renderedPosition.X - before.X, 0, renderedPosition.Z - before.Z).Length() / (float)characterDelta : 0f;
            var pace = _personPace.GetValueOrDefault(agent.Id) * .75f + moved * .25f;
            _personPace[agent.Id] = pace;
            Bodies.AnimateGuest(visual, pace, CharacterPresentationPaused);
            visual.Position = renderedPosition;
            if (agent.Id == casualtyId || collapsed.Contains(agent.Id))
            {
                // Show collapse through the entire rescue window, not only after death.
                visual.Position = renderedPosition + new Vector3(0, .75f, 0);
                visual.Rotation = new Vector3(Mathf.Pi / 2f, visual.Rotation.Y, 0);
                continue;
            }
            // Upright again after a collapse, or after nodding along to a set.
            if (visual.Rotation.X != 0) visual.Rotation = new Vector3(0, visual.Rotation.Y, visual.Rotation.Z);
            UpdatePersonFacing(agent.Id, visual, renderedPosition, agent.Action,
                watching.Contains(agent.Id), onStage.Contains(agent.Id), characterDelta);
            // A dodgy knee hobbles: a dip and a sideways lurch on every other step.
            var hobble = agent.Action == AgentNavigationAction.Travelling && _session.StaffHas(agent.Id.Value, StaffTrait.DodgyKnee)
                ? Mathf.Sin((float)_characterPresentationSeconds * 7f) : 0f;
            if (hobble != 0) visual.Position -= new Vector3(0, Mathf.Abs(hobble) * .05f, 0);
            if (hobble != 0 || visual.Rotation.Z != 0) visual.Rotation = new Vector3(visual.Rotation.X, visual.Rotation.Y, hobble * .1f);
            ApplyCrowdDance(agent.Id, visual);
        }
        AdvanceLivePerformancePresentation(characterDelta);
        AdvanceImmersionPresentation(characterDelta);
        AdvanceMedicalCuePresentation();
        AdvanceDisorderCuePresentation();
        AdvanceImmersionCuePresentation();
        AdvanceLitterCuePresentation();
        AdvanceFaultCuePresentation();
        if (_selectedAttendeeId is not null) RefreshAttendeeInspector();
        EnsureStage(); Audio.AdvanceIncidents(_session, ListenerPoint);
        SyncPresentationPause();
        _host.AdvanceSaves(delta);
        SyncSaveStatus();
        if (Engine.GetProcessFrames() % 15 == 0) RefreshPreparationHud();
        var captureStarted = Stopwatch.GetTimestamp();
        if (_preparationProfileOutput is not null) _profilePresentationMs = Stopwatch.GetElapsedTime(presentationStarted, captureStarted).TotalMilliseconds;
        if (_preparationProfileOutput is not null) _profileCaptureMs = Stopwatch.GetElapsedTime(captureStarted).TotalMilliseconds;
    }

}

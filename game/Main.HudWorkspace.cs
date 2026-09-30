using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    // Presentation preferences deliberately never enter the session or its save/hash.
    private PanelContainer? _hudWorkspace;
    private PanelContainer? _hudMenu;
    private PanelContainer? _hudRoster;
    private PanelContainer? _hudProgramme;
    private Control? _hudAlerts;
    private PanelContainer? _hudDiagnostics;
    private Label? _hudMoney;
    private Label? _hudPhase;
    private Label? _hudClock;
    private Label? _hudAttendance;
    private Label? _hudWeather;
    private Label? _hudStatus;
    private Label? _hudStartReason;
    private Label? _hudDiagnosticsText;
    private Button? _hudPause;
    private Button? _hudAlertToggle;
    private Button? _hudPreparationToggle;
    private Button? _hudProgrammeToggle;
    private Button? _hudRosterToggle;
    private HBoxContainer? _hudLegacyBottom;
    private PanelContainer? _hudLegacyStatusPanel;
    private ConfirmationDialog? _hudStartConfirmation;
    private ScrollContainer? _hudContextScroll;
    private TabContainer? _hudTabs;
    private readonly Dictionary<string, VBoxContainer> _hudPages = [];
    private readonly List<Button> _hudAlertActions = [];
    private VBoxContainer? _hudAlertBox;
    private readonly Festival.ContentAdapter.UrgentAlertDisplay _urgentAlertDisplay = new();
    private readonly Dictionary<string, Action> _urgentAlertActions = [];
    private string _hudAlertKey = "uninitialized";
    private readonly Dictionary<Control, (Vector2 Position, Vector2 Size, Vector2 Applied)> _alertClearance = [];
    private bool _hudWorkspaceOpen = true;
    private bool _hudProgrammeOpen = true;
    private bool _hudDevelopment;
    private bool _hudWaterExact;





    private void BuildHudWorkspace()
    {
        if (_session.CaptureProgramme() is not null) _preparationMessage = "Choose three different acts and hire a sound engineer. Equipment and stock are optional.";
        var layer = new CanvasLayer(); AddChild(layer);
        var size = GetViewport().GetVisibleRect().Size;
        var width = size.X; var height = size.Y;
        var top = HudPanel(layer, Vector2.Zero, new Vector2(width, 58), HudInk);
        top.AddThemeStyleboxOverride("panel", HudStyle(HudInk, 6));
        var topRow = new HBoxContainer(); topRow.AddThemeConstantOverride("separation", 22); top.AddChild(topRow);
        _hudPhase = LabelText("", 14, HudPaper); _hudPhase.CustomMinimumSize = new Vector2(228, 0); topRow.AddChild(_hudPhase);
        _hudMoney = LabelText("", 15, HudPaper); _hudMoney.CustomMinimumSize = new Vector2(90, 0); topRow.AddChild(_hudMoney);
        _hudClock = LabelText("", 15, HudPaper); _hudClock.CustomMinimumSize = new Vector2(155, 0); topRow.AddChild(_hudClock);
        _hudAttendance = LabelText("", 15, HudPaper); _hudAttendance.CustomMinimumSize = new Vector2(125, 0); topRow.AddChild(_hudAttendance);
        _hudWeather = LabelText("", 15, HudPaper); topRow.AddChild(_hudWeather);
        topRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _hudAlertToggle = ButtonText("Alerts · 0", () => { _urgentAlertDisplay.ShowNextPage(); RenderUrgentAlerts(); }); topRow.AddChild(_hudAlertToggle);
        _hudAlertToggle.TooltipText = "Show current urgent alerts; press again for the next group. Select an alert to locate it.";
        _hudPause = ButtonText("Pause", () => { _host.Submit(new SetPausedCommand(!_session.IsPaused)); RefreshPreparationHud(); }); topRow.AddChild(_hudPause);
        _buildToggleButton = ButtonText("Build", ToggleBuildDrawer); topRow.AddChild(_buildToggleButton);
        topRow.AddChild(ButtonText("Menu", () => { _hudMenu!.Visible = !_hudMenu.Visible; }));

        var workspaceWidth = width >= 1600 ? 690 : 650;
        _hudWorkspace = HudPanel(layer, new Vector2(15, 77), new Vector2(workspaceWidth, Math.Min(520, height - 150)));
        _hudWorkspace.MinimumSizeChanged += () => Booking.ScheduleLayout();
        var workspaceBox = new VBoxContainer(); workspaceBox.AddThemeConstantOverride("separation", 12); _hudWorkspace.AddChild(workspaceBox);
        var heading = new HBoxContainer(); workspaceBox.AddChild(heading);
        var title = HudLabel("Prepare the festival", 25); title.AddThemeFontOverride("font", HearingSerif()); heading.AddChild(title);
        var collapsePreparation = ButtonText("×", () => { _hudWorkspaceOpen = false; RefreshHudWorkspace(); });
        collapsePreparation.TooltipText = "Collapse preparation"; heading.AddChild(collapsePreparation);
        _hudTabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; workspaceBox.AddChild(_hudTabs);
        _hudTabs.TabsVisible = false;
        _hudTabs.UseHiddenTabsForMinSize = false;
        foreach (var name in new[] { "Build", "Overview", "Programme", "Staff", "Equipment", "Stock", "Site & water" })
        {
            var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            if (name == "Programme")
                scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
            _hudTabs.AddChild(scroll);
            var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 12); scroll.AddChild(box); _hudPages.Add(name, box);
        }
        _hudTabs.TabChanged += _ => { RefreshHudWorkspace(); Booking.ScheduleLayout(); };
        // A per-tab red border and underline leave the native selected/hover text legible.
        // The title and tooltip convey the same requirement without relying on colour.
        _hudTabs.GetTabBar().Draw += DrawHudPreparationBlockers;
        var build = _hudPages["Build"];
        build.AddChild(HudLabel("Build your festival", 21));
        build.AddChild(HudLabel("Place essential services in any order. Choose a row to begin; select an existing service on the field to move it."));
        build.AddChild(ButtonText("Open Build catalogue", () => OpenBuildCatalogue()));
        build.AddChild(HudLabel("Before opening · open the matching Build row", 13));
        Drawer.AddChecklistShortcuts(build);

        var overview = _hudPages["Overview"];
        overview.AddThemeConstantOverride("separation", 6);
        overview.AddChild(HudLabel("Before opening", 21));
        _preparationSummary = HudLabel("", 14); overview.AddChild(_preparationSummary);
        overview.AddChild(ButtonText("Open Build catalogue", () => OpenBuildCatalogue()));
        Drawer.AddChecklistShortcuts(overview);

        foreach (var (name, label) in new[] { ("Programme", "Choose acts"), ("Staff", "Manage staff"), ("Stock", "Review stock"), ("Site & water", "Review site") })
        {
            var destination = name; var shortcut = ButtonText(label, () => SelectHudTab(destination)); shortcut.CustomMinimumSize = new Vector2(0, 32); overview.AddChild(shortcut);
        }
        Booking.Build(_hudPages["Programme"]);
        _hudPages["Equipment"].AddChild(HudLabel("Optional sound rig", 21));
        _hudPages["Equipment"].AddChild(HudLabel("Buy £120 (+1000 quality, retained) or rent £30 (+500, this festival).\nGenerator: safe 80% baseline."));
        _hudPages["Staff"].AddChild(HudLabel("Festival staff", 21));
        _hudPages["Staff"].AddChild(HudLabel("Hire at least one sound engineer or unlocked role before opening. Maintenance is optional; extra role hires require their unlocked slot. Pay at Start."));
        _preparationOfferBox = _hudPages["Staff"]; _preparationOfferInsertIndex = _preparationOfferBox.GetChildCount(); RebuildPreparationOffers();
        _hudPages["Stock"].AddChild(HudLabel("Food & drink starter stock", 21));
        _hudPages["Stock"].AddChild(HudLabel(_session.CapturePreparationPlan() is null ? "Optional fixed bundle • once before opening\n\nPRODUCT              QUANTITY              SALE PRICE\nChips                         40                              £3\nSoft drink                  40                              £2\nBeer                           32                              £3" : "Optional unpaid quantities • change before Start\nSale prices: chips £3 · soft drinks £2 · beer £3"));
        BuildImmersionControls(_hudPages["Stock"]);
        _hudPages["Stock"].AddThemeConstantOverride("separation", 8);
        _immersionControls!.GetChild<Control>(0).Visible = false;
        var site = _hudPages["Site & water"];
        site.AddChild(HudLabel("Site & water", 21)); site.AddChild(HudLabel("Manage water choices here. Place taps and other services through Build; select a placed object on the field to move it."));
        site.AddChild(HudLabel("Council water choice • this festival\nShare free water with the neighbouring community. Faster drinkers take longer; queues may grow. Honour the full festival for 1 Council Favour, once per campaign."));
        _communityShareInfo = HudLabel(""); _communityShareInfo.Visible = false; site.AddChild(_communityShareInfo);
        site.AddChild(ButtonText("Exact effect ▸", () => { _hudWaterExact = !_hudWaterExact; RefreshHudWorkspace(); }));
        _communityShareButton = ButtonText("Commit water sharing", () => CommitEquipmentAction(new CommitCommunityWaterShareCommand())); site.AddChild(_communityShareButton);
        var footer = new HBoxContainer(); var footerSeparator = new HSeparator(); workspaceBox.AddChild(footerSeparator); workspaceBox.AddChild(footer);
        footer.Visible = false; footerSeparator.Visible = false;
        _hudStartReason = HudLabel(""); footer.AddChild(_hudStartReason);
        // Existing development captures emit this button directly. Keep that bounded
        // route's established behavior while ordinary player clicks confirm below.
        _preparationStart = ButtonText("Start festival", ShowHudStartConfirmation); footer.AddChild(_preparationStart);
        _hudStartConfirmation = new ConfirmationDialog { Title = "Start the festival?", DialogText = "Open the gates for the full fixed roster. Preparation purchases and placement close when you start.", OkButtonText = "Start festival", CancelButtonText = "Keep preparing", Theme = HudTheme() };
        _hudStartConfirmation.Theme.SetStylebox("panel", "AcceptDialog", HudStyle(HudInk, 16));
        _hudStartConfirmation.GetLabel().AddThemeColorOverride("font_color", HudPaper);
        foreach (var button in new[] { _hudStartConfirmation.GetOkButton(), _hudStartConfirmation.GetCancelButton() })
        {
            button.MouseEntered += () => button.MouseDefaultCursorShape = button.Disabled ? Control.CursorShape.Arrow : Control.CursorShape.PointingHand;
            foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
                button.AddThemeColorOverride(state, HudInk);
            foreach (var state in new[] { "normal", "hover", "pressed", "focus" })
                button.AddThemeStyleboxOverride(state, HudStyle(state == "hover" ? new Color("d3e8df") : HudPaper, 8));
        }
        _hudStartConfirmation.Confirmed += PreparationStart; layer.AddChild(_hudStartConfirmation);

        _hudProgrammeToggle = ButtonText("Programme ▴", () => { _hudProgrammeOpen = !_hudProgrammeOpen; RefreshHudWorkspace(); });
        _hudProgrammeToggle.Position = new Vector2(width - 150, 58); _hudProgrammeToggle.Size = new Vector2(150, 34); _hudProgrammeToggle.Theme = HudTheme(); layer.AddChild(_hudProgrammeToggle);
        _hudProgramme = HudPanel(layer, new Vector2(width - 300, 92), new Vector2(300, 172));
        var programmeBox = new VBoxContainer(); _hudProgramme.AddChild(programmeBox);
        _liveSetCue = HudLabel("", 15); programmeBox.AddChild(_liveSetCue);

        _contextPanel = HudPanel(layer, new Vector2(width - 300, 280), new Vector2(300, Math.Min(380, height - 340))); _contextPanel.Visible = false;
        _hudContextScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _contextPanel.AddChild(_hudContextScroll);
        var detail = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; detail.AddThemeConstantOverride("separation", 8); _hudContextScroll.AddChild(detail);
        var contextHeading = new HBoxContainer(); detail.AddChild(contextHeading); contextHeading.AddChild(HudLabel("Selected", 12));
        var contextClose = ButtonText("×", ClearSelection); contextClose.Name = "CloseSelectedPanel";
        contextClose.TooltipText = "Close selected object panel"; contextHeading.AddChild(contextClose);
        _inspectorTitle = HudLabel("", 20); detail.AddChild(_inspectorTitle);
        BuildWaterFlowInspector(detail); BuildSatisfactionBar(detail); BuildMedicalNeedBars(detail); BuildImmersionNeedBars(detail);
        // Essential actions precede optional prose and remain accessible by scroll.
        BuildImmersionVendorInspector(detail); BuildMedicalActionInspector(detail); BuildDisorderActionInspector();
        BuildDisorderStageInspector(detail); BuildStagePowerAction(detail); BuildSecurityPostInspectorAction(detail);
        _inspectorBody = HudLabel("", 13); detail.AddChild(_inspectorBody);
        ConstrainHudControls(detail);
        contextClose.CustomMinimumSize = new Vector2(38, 38); contextClose.SizeFlagsHorizontal = Control.SizeFlags.Fill;

        _hudMenu = HudPanel(layer, new Vector2(width - 265, 65), new Vector2(250, 180)); _hudMenu.Visible = false;
        var menuScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _hudMenu.AddChild(menuScroll);
        var menuBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; menuBox.AddThemeConstantOverride("separation", 6); menuScroll.AddChild(menuBox);
        menuBox.AddChild(HudLabel("Festival menu", 14)); menuBox.AddChild(ButtonText("Save", PreparationSave)); menuBox.AddChild(ButtonText("Load", PreparationLoad));
        _stageMuteButton = ButtonText("Mute audio", ToggleStageMute); menuBox.AddChild(_stageMuteButton);
        menuBox.AddChild(ButtonText("Development diagnostics ▸", () => { _hudDevelopment = !_hudDevelopment; _hudDiagnostics!.Visible = _hudDevelopment; RefreshHudWorkspace(); if (_selectedAttendeeId is not null) RefreshAttendeeInspector(); }));
        ConstrainHudControls(menuBox);

        _hudDiagnostics = HudPanel(layer, new Vector2(15, 150), new Vector2(600, 380)); _hudDiagnostics.Visible = false;
        var diagnosticBox = new VBoxContainer(); _hudDiagnostics.AddChild(diagnosticBox); diagnosticBox.AddChild(HudLabel("DEVELOPMENT DIAGNOSTICS • not player HUD", 16));
        _hudDiagnosticsText = HudLabel("", 12); diagnosticBox.AddChild(_hudDiagnosticsText);
        BuildDebugControls(diagnosticBox, layer, size);
        if (_session.CaptureEquipment() is not null) BuildEquipmentControls(diagnosticBox);
        if (_session.CaptureMedical() is not null) BuildMedicalControls(diagnosticBox);
        if (_session.CaptureDisorder() is not null) BuildDisorderControls(diagnosticBox);

        _hudAlerts = new Control { Position = new Vector2(15, 70), Size = new Vector2(400, 130), MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 20 }; layer.AddChild(_hudAlerts);
        _hudAlertBox = new VBoxContainer { Size = new Vector2(400, 0), MouseFilter = Control.MouseFilterEnum.Ignore }; _hudAlerts.AddChild(_hudAlertBox);
        _hudRoster = HudPanel(layer, new Vector2(15, height - 422), new Vector2(330, 280)); _hudRoster.Visible = false;
        _preparationRosterScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _hudRoster.AddChild(_preparationRosterScroll);
        _preparationPeople = HudLabel("", 13); _preparationPeople.CustomMinimumSize = new Vector2(300, 0); _preparationRosterScroll.AddChild(_preparationPeople);
        var bottom = new HBoxContainer { Position = new Vector2(15, height - 50), Theme = HudTheme() }; layer.AddChild(bottom); _hudLegacyBottom = bottom;
        _hudPreparationToggle = ButtonText("Preparation ▾", () => { _buildDrawerOpen = false; _hudWorkspaceOpen = !_hudWorkspaceOpen; RefreshHudWorkspace(); }); bottom.AddChild(_hudPreparationToggle);
        _hudRosterToggle = ButtonText("People ▸", () => { _hudRoster.Visible = !_hudRoster.Visible; }); bottom.AddChild(_hudRosterToggle);
        bottom.AddChild(ButtonText("Rotate view", () => Rotate(1)));
        _orientationLabel = HudLabel("", 12); _orientationLabel.Visible = false; bottom.AddChild(_orientationLabel);
        var statusPanel = HudPanel(layer, new Vector2(width - 510, height - 50), new Vector2(495, 40)); _hudLegacyStatusPanel = statusPanel;
        statusPanel.AddThemeStyleboxOverride("panel", HudStyle(HudPaper, 6));
        _hudStatus = HudLabel("", 12); _hudStatus.MaxLinesVisible = 2; statusPanel.AddChild(_hudStatus);
        Perks.Build(layer);
        Hearing.Build(layer); Drawer.Build(layer, size); Dock.Build(layer, size); RefreshPreparationHud();
    }

    private static void ConstrainHudControls(Node root)
    {
        foreach (var node in root.GetChildren())
        {
            if (node is Control control && node is not TextureRect) control.CustomMinimumSize = new Vector2(0, control is Button ? 32 : control is ProgressBar ? 8 : 0);
            if (node is Label label) label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (node is Button button) { button.AddThemeFontSizeOverride("font_size", 12); button.ClipText = true; button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; }
            ConstrainHudControls(node);
        }
    }

    private void SelectHudTab(string name)
    {
        if (_hudTabs is null) return;
        _buildDrawerOpen = false; _hudWorkspaceOpen = true; _hudTabs.CurrentTab = Array.IndexOf(_hudPages.Keys.ToArray(), name); RefreshHudWorkspace();
    }

    private bool HudProgrammeSelected() => _hudTabs is not null &&
        _hudTabs.CurrentTab == Array.IndexOf(_hudPages.Keys.ToArray(), "Programme");

    private void ShowHudStartConfirmation()
    {
        if (_buildGhostKind is not null) { _preparationMessage = "Finish or cancel placement before opening."; RefreshPreparationHud(); return; }
        var issue = _session.ValidateCommand(CampaignEnvelope(new StartPreparedEditionCommand()));
        if (issue is not null) { _preparationMessage = issue.Message; RefreshPreparationHud(); return; }
        _hudStartConfirmation!.PopupCentered(new Vector2I(480, 180));
    }

    private static string HudTime(long ticks) => $"{Math.Max(0, ticks) / 80 / 60:00}:{Math.Max(0, ticks) / 80 % 60:00}";

    private void RefreshHudWorkspace()
    {
        if (_hudMoney is null || _session.CapturePreparation() is not { } p) return;
        var preparing = p.Status == PreparationStatus.Preparing;
        var placing = _buildGhostKind is not null;
        if (placing) _hudWorkspaceOpen = false;
        var finance = _session.CaptureSnapshot().FestivalFinances.Single(f => f.OwnerId.Value == p.FinanceOwnerId);
        _hudPhase!.Text = $"Lower Wittering\n{(preparing ? "PREPARATION · BEFORE OPENING" : "LIVE · FESTIVAL DAY")}";
        if (p.Status == PreparationStatus.Departing) _hudPhase.Text = "Lower Wittering\nDEPARTING · FESTIVAL FINISHED";
        _hudMoney.Text = $"MONEY\n{FestivalCurrency.Format(finance.CashPennies)}";
        _hudClock!.Text = preparing ? "FESTIVAL CLOCK\nNot started" : $"FESTIVAL CLOCK\n{HudTime(_session.CurrentTick - p.StartedTick)} / 08:00";
        _hudAttendance!.Text = $"Attendees\n{_session.OnSiteAttendeeCount} / {p.People.Count(person => person.Role == ProtectedPersonRole.Guest)}";
        _hudWeather!.Text = "WEATHER\n" + (_session.CaptureMedical() is { IsHot: true } ? "☀ Hot" : "Unavailable");
        _hudPause!.Visible = !preparing; _hudPause.Text = _session.IsPaused ? "Resume" : "Pause";
        _hudPause.TooltipText = "Pause / resume (Space)";
        _hudStatus!.Text = placing ? "Placement preview · no change until a valid click" : _preparationMessage;
        if (_buildGhostKind is not null) _hudStatus.Text = _preparationMessage;
        if (p.Status == PreparationStatus.Departing)
            _hudStatus.Text = $"Festival finished · Guests leaving: {p.People.Count(person => person.Role == ProtectedPersonRole.Guest && person.Admitted && !person.Departed)}";
        _hudStatus.TooltipText = _preparationMessage;
        _hudWorkspace!.Visible = preparing && _hudWorkspaceOpen && !placing && _session.CapturePerks()?.Pending != true;
        if (_buildToggleButton is not null) _buildToggleButton.Visible = preparing;
        if (Drawer.Panel is not null) Drawer.Panel.Visible = preparing && _buildDrawerOpen && !placing && _session.CapturePerks()?.Pending != true;
        LayoutOwnedPerkWorkspace();
        _hudPreparationToggle!.Visible = false; _hudPreparationToggle.Text = _hudWorkspaceOpen ? "Preparation ▴" : "Preparation ▾";
        _hudLegacyBottom!.Visible = !preparing;
        _hudLegacyStatusPanel!.Visible = !preparing;
        if (_buildToggleButton is not null) _buildToggleButton.Visible = false;
        if (Perks.Toggle is not null && _session.CapturePerks()?.Pending != true) Perks.Toggle.Visible = !preparing;

        _hudRosterToggle!.Text = $"People · {p.People.Length} ▸";
        _hudProgrammeToggle!.Visible = !preparing; _hudProgramme!.Visible = !preparing && _hudProgrammeOpen;
        _hudProgrammeToggle.Text = _hudProgrammeOpen ? "Programme ▴" : "Programme ▾";
        LayoutOwnedContext(_ownedWorkspaceConstrained);
        var issue = _session.ValidateCommand(CampaignEnvelope(new StartPreparedEditionCommand()));
        var blockers = _session.GetPreparationStartBlockers();
        _hudStartReason!.Text = issue is null ? "Ready to open. Equipment and stock remain optional."
            : preparing && _session.CapturePerks()?.Pending != true && blockers.Count != 0
                ? string.Join("\n", blockers.Select(blocker => blocker.Message)) : issue.Message;
        if (_session.CapturePreparationPlan() is { Committed: false })
        {
            var funds = _session.CaptureSnapshot().FestivalFinances.Single().CashPennies;
            var costs = $"Available {FestivalCurrency.Format(funds)} • Setup {FestivalCurrency.Format(_session.PreparationPlanCost)} • Remaining {FestivalCurrency.Format(_session.PreparationRemainingCash)}";
            _hudStartReason.Text = costs + "\n" + _hudStartReason.Text;
            _hudStartConfirmation!.DialogText = costs + "\nPay the complete setup once and open for the full fixed roster.";
            _hudStartConfirmation.DialogText = Drawer.CostSummary() + "\nPay this complete setup once and open the festival?";
        }
        RefreshHudPreparationReadiness();
        if (Booking.IsBuilt)
        {
            _hudStartReason.MaxLinesVisible = HudProgrammeSelected() ? 2 : -1;
            var actions = blockers.Select(b => b.Owner switch
            {
                PreparationStartOwner.Programme => "choose 3 different acts",
                PreparationStartOwner.Staff => "hire 1 worker",
                PreparationStartOwner.Overview => "reduce planned cost",
                _ => b.Message
            });
            _hudStartReason.Text = Booking.Summary + "\n" + (blockers.Count > 0 ? "Before Start: " + string.Join("; ", actions) + "." : issue?.Message ?? "Ready to open; setup paid once at Start.");
            if (HudProgrammeSelected()) _hudStatus!.Text = Booking.DurableMessage;
        }
        _preparationStart.TooltipText = _hudStartReason.Text;
        _hudMenu!.Size = new Vector2(250, 270);
        if (_communityShareInfo is not null) _communityShareInfo.Visible = _hudWaterExact;
        _hudDiagnosticsText!.Text = $"Tick {_session.CurrentTick} · hash {_session.CaptureSnapshot().AuthoritativeHash}\nPhase {_session.Phase} · status {p.Status} · paused {_session.IsPaused}\n{_preparationMessage}";
        _preparationSummary.Text = $"{(_session.CaptureProgramme() is null ? "Fixed festival roster" : "Three fixed sets · eight-minute festival day")}\n" +
            $"Programme: {(_session.CaptureProgramme() is { ActIds.Length: 3 } ? "three acts booked" : p.AcceptedOffers.Any(id => id.StartsWith("act.", StringComparison.Ordinal)) ? "act booked" : "choose before opening")}\n" +
            $"Sound: {(p.AcceptedOffers.Contains("staff.steward") ? "Casey · standard sound engineer hired · £20" : p.AcceptedOffers.Contains("staff.engineer") ? "Casey · better sound engineer hired · £40" : "hire a sound engineer before opening")}\n" +
            $"Owned rig {p.OwnedEquipment.Length} · rental {p.Rentals.Length} · equipment & stock optional";
        if (preparing && _session.CaptureImmersion() is { } stock)
            _immersionSummary!.Text = $"Current stock: chips {stock.ChipsStock} · soft {stock.SoftStock} · beer {stock.BeerStock}\nFree water remains available. Staff do not buy beer.";
        if (preparing && p.Plan is { } plan)
        {
            _preparationSummary.Text = $"Available {FestivalCurrency.Format(finance.CashPennies)}\nSetup cost {FestivalCurrency.Format(_session.PreparationPlanCost)} · remaining {FestivalCurrency.Format(_session.PreparationRemainingCash)}\n" +
                $"Unpaid lineup: {plan.ActIds.Count(id => id != "")}/3 acts\nPlanned hires: {string.Join(", ", plan.OfferIds.Where(id => id.StartsWith("staff.") || id == "maintenance.worker").Select(id => _session.GetPreparationOffers().Single(o => o.Id == id).Name))}\nExpected protected people: {_session.ExpectedPreparedPeopleCount}/50\nOwned rig {p.OwnedEquipment.Length} · selected rig {(plan.OfferIds.SingleOrDefault(id => id.StartsWith("equipment.")) ?? "none")}\nFreely revise purchases. Existing site and perk property stays committed.";
            _immersionSummary!.Text = $"Planned chips {plan.Chips} · soft {plan.SoftDrinks} · beer {plan.Beers}\nUnpaid; pay at Start. Free water remains available.";
        }
        if (_medicalActionInspector is not null)
        {
            if (_medicalButtons.TryGetValue(MedicalAction.DispatchMedic, out var medicButton)) medicButton.Text = "Send medic";
            if (_disorderButtons.TryGetValue(DisorderAction.DispatchSecurity, out var stewardButton)) stewardButton.Text = "Send steward";
        }
        RefreshHudAlerts();
        Drawer.Refresh();
        if (_buildToggleButton is not null) _buildToggleButton.Text = _buildDrawerOpen ? "Build ×" : "Build";
        Dock.Refresh();
    }

    private void RefreshHudPreparationReadiness()
    {
        if (_hudTabs is null) return;
        var blockers = _session.GetPreparationStartBlockers();
        var names = _hudPages.Keys.ToArray();
        for (var index = 0; index < names.Length; index++)
        {
            var name = names[index];
            var reasons = blockers.Where(blocker => blocker.Owner.ToString() == name).Select(blocker => blocker.Message).ToArray();
            _hudTabs.SetTabTitle(index, reasons.Length == 0 ? name : name + " !");
            _hudTabs.SetTabTooltip(index, reasons.Length == 0 ? name : "Required before Start festival: " + string.Join("\n", reasons));
        }
        _hudTabs.GetTabBar().QueueRedraw();
    }

    private void DrawHudPreparationBlockers()
    {
        if (_hudTabs is null) return;
        var bar = _hudTabs.GetTabBar();
        var names = _hudPages.Keys.ToArray();
        foreach (var blocker in _session.GetPreparationStartBlockers())
        {
            var index = Array.IndexOf(names, blocker.Owner.ToString());
            if (index < 0) continue;
            var rect = bar.GetTabRect(index);
            bar.DrawRect(new Rect2(rect.Position + new Vector2(1, 1), rect.Size - new Vector2(2, 2)), new Color("a52e32"), false, 2);
            bar.DrawRect(new Rect2(rect.Position + new Vector2(3, rect.Size.Y - 5), new Vector2(rect.Size.X - 6, 4)), new Color("a52e32"));
        }
    }

    private void RefreshHudAlerts()
    {
        var p = _session.CapturePreparation()!;
        var alerts = new List<Festival.ContentAdapter.UrgentAlert>();
        void Add(string id, string text, int priority, Action action)
        {
            if (alerts.Any(alert => alert.Id == id && alert.Priority >= priority)) return;
            alerts.RemoveAll(alert => alert.Id == id);
            alerts.Add(new(id, text, priority)); _urgentAlertActions[id] = action;
        }
        var onSite = p.People.Where(person => person.Admitted && !person.Departed).Select(person => person.AgentId).ToHashSet();
        if (_session.CaptureMedical() is { } medical)
            foreach (var need in medical.Needs.Where(n => onSite.Contains(n.AgentId) && n.Stage is MedicalStage.Distress or MedicalStage.Collapsed or MedicalStage.Critical))
            {
                var target = need.AgentId; Add($"person:{target}", $"{p.People.Single(person => person.AgentId == target).Name}: {need.Stage} · locate", need.Stage == MedicalStage.Critical ? 100 : need.Stage == MedicalStage.Collapsed ? 95 : 70, () => HudLocatePerson(target));
            }
        if (_session.CaptureImmersion() is { } immersion)
            foreach (var person in immersion.People.Where(person => onSite.Contains(person.AgentId) && person.Intoxication >= 7500))
            {
                var target = person.AgentId; Add($"person:{target}", $"{p.People.Single(person => person.AgentId == target).Name}: needs care · locate", 60, () => HudLocatePerson(target));
            }
        if (_session.CaptureDisorder() is { } disorder)
            foreach (var person in disorder.People.Where(person => onSite.Contains(person.AgentId) && person.Stage is DisorderStage.Argument or DisorderStage.Fight or DisorderStage.Injured))
            {
                var target = person.AgentId; Add($"person:{target}", $"{p.People.Single(person => person.AgentId == target).Name}: {person.Stage} · locate", person.Stage == DisorderStage.Fight ? 90 : person.Stage == DisorderStage.Injured ? 85 : 50, () => HudLocatePerson(target));
            }
        if (_session.CaptureEquipment() is { Stage: EquipmentStage.Warning or EquipmentStage.DangerousFault })
            Add("generator", "Generator overload · inspect power", 80, () => SelectObject(LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.trailer-stage")));
        _hudAlertToggle!.Text = alerts.Count == 0 ? "Alerts · 0" : $"{alerts.Count} urgent alert{(alerts.Count == 1 ? "" : "s")}";
        _urgentAlertDisplay.Observe(alerts);
        RenderUrgentAlerts();
    }

    private void RenderUrgentAlerts()
    {
        if (_hudAlertBox is null) return;
        var alerts = _urgentAlertDisplay.Visible();
        foreach (var panel in new Control?[] { Drawer.Panel, _hudWorkspace })
        {
            if (panel is null) continue;
            if (!_alertClearance.TryGetValue(panel, out var layout) || panel.Position != layout.Applied)
                layout = (panel.Position, panel.Size, panel.Position);
            var y = alerts.Length == 0 ? layout.Position.Y : Math.Max(layout.Position.Y, 78 + alerts.Length * 32);
            panel.Position = new Vector2(layout.Position.X, y);
            // Scrollable preparation content keeps its original lower edge above the dock.
            panel.Size = new Vector2(layout.Size.X, Math.Max(65, layout.Size.Y - (y - layout.Position.Y)));
            _alertClearance[panel] = (layout.Position, layout.Size, panel.Position);
        }
        var key = string.Join("|", alerts.Select(a => a.Alert.Id + a.Alert.Text));
        _hudAlerts!.Visible = alerts.Length > 0;
        for (var i = 0; i < Math.Min(alerts.Length, _hudAlertActions.Count); i++)
            _hudAlertActions[i].Modulate = new Color(1, 1, 1, alerts[i].Opacity);
        if (key == _hudAlertKey) return;
        _hudAlertKey = key;
        foreach (var child in _hudAlertBox!.GetChildren()) { _hudAlertBox.RemoveChild(child); child.QueueFree(); }
        _hudAlertActions.Clear();
        foreach (var entry in alerts)
        {
            var id = entry.Alert.Id;
            var button = ButtonText(entry.Alert.Text, () => { if (_urgentAlertActions.TryGetValue(id, out var action)) action(); });
            button.Alignment = HorizontalAlignment.Left; button.Flat = true; button.ClipText = true;
            button.CustomMinimumSize = new Vector2(400, 28); button.TooltipText = entry.Alert.Text;
            foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" }) button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
            foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) button.AddThemeColorOverride(state, new Color("fff3d3"));
            button.AddThemeColorOverride("font_outline_color", new Color("202828")); button.AddThemeConstantOverride("outline_size", 5);
            button.AddThemeFontSizeOverride("font_size", 16); button.Modulate = new Color(1, 1, 1, entry.Opacity);
            _hudAlertBox.AddChild(button); _hudAlertActions.Add(button);
        }
        _hudAlerts.Size = new Vector2(400, alerts.Length * 32);
    }

    private void HudLocatePerson(ulong id)
    {
        SelectAttendee(new EntityId(id));
        if (_attendeeVisuals.TryGetValue(new EntityId(id), out var visual)) { _focus = visual.Position; ApplyCamera(); }
        _hudAlerts!.Visible = false;
    }

    private bool HudBlocksPlacement(Vector2 screen)
    {
        if(Perks.Panel?.Visible==true && Perks.Panel.GetGlobalRect().HasPoint(screen))return true;
        if(Perks.EffectPopup?.Visible==true && Perks.EffectPopup.GetGlobalRect().HasPoint(screen))return true;
        if (_hudMoney is null) return screen.X < 435 || screen.X > GetViewport().GetVisibleRect().Size.X - 435 || screen.Y < 110;
        if (screen.Y < 60 || screen.Y > GetViewport().GetVisibleRect().Size.Y - (_session.PreparedStatus == PreparationStatus.Preparing ? 128 : 54)) return true;
        return new Control?[] { _hudWorkspace, Drawer.Panel, Dock.Readiness, _hudMenu, _contextPanel, _hudAlerts, _hudRoster, _hudDiagnostics, _hudProgramme }
            .Any(control => control?.IsVisibleInTree() == true && control.GetGlobalRect().HasPoint(screen));
    }
}

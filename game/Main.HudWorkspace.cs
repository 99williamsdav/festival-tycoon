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
    private Control? _hudAlerts;
    private PanelContainer? _hudDiagnostics;
    private Label? _hudStatus;
    private Label? _hudStartReason;
    private Label? _hudDiagnosticsText;
    private ConfirmationDialog? _hudStartConfirmation;
    private ScrollContainer? _hudContextScroll;
    private TabContainer? _hudTabs;
    private readonly Dictionary<string, VBoxContainer> _hudPages = [];
    private readonly List<Control> _hudAlertActions = [];
    private VBoxContainer? _hudAlertBox;
    private readonly Festival.ContentAdapter.UrgentAlertDisplay _urgentAlertDisplay = new();
    private readonly Dictionary<string, Action> _urgentAlertActions = [];
    private string _hudAlertKey = "uninitialized";
    private int _activeAlertCount;
    private readonly Dictionary<Control, (Vector2 Position, Vector2 Size, Vector2 Applied, Vector2 AppliedSize)> _alertClearance = [];
    private bool _hudWorkspaceOpen = true;
    private bool _hudProgrammeOpen = true;
    private bool _hudDevelopment;





    private void BuildHudWorkspace()
    {
        if (_session.CaptureProgramme() is not null) _preparationMessage = "Choose three different acts and hire a sound engineer. Equipment and stock are optional.";
        var layer = new CanvasLayer(); AddChild(layer);
        var size = GetViewport().GetVisibleRect().Size;
        Ui.Configure(size);
        var width = size.X; var height = size.Y;
        Top.Build(layer, width);

        var workspaceWidth = Ui.S(690);
        _hudWorkspace = HudPanel(layer, new Vector2(Ui.Gutter, Ui.ContentTop), new Vector2(workspaceWidth, Math.Min(Ui.S(522), height - Ui.ContentTop - Ui.Dock - Ui.S(12))));
        _hudWorkspace.MinimumSizeChanged += () => Booking.ScheduleLayout();
        _hudWorkspace.AddThemeStyleboxOverride("panel", Ui.Sheet(24, 22));
        Ui.Clipboard(_hudWorkspace);
        var workspaceBox = new VBoxContainer(); workspaceBox.AddThemeConstantOverride("separation", 12); _hudWorkspace.AddChild(workspaceBox);
        _hudTabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; workspaceBox.AddChild(_hudTabs);
        _hudTabs.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var corner = new Control { MouseFilter = Control.MouseFilterEnum.Ignore }; _hudWorkspace.AddChild(corner);
        var collapsePreparation = new Button { Icon = Ui.Icon("x"), ExpandIcon = true, IconAlignment = HorizontalAlignment.Center,
            TooltipText = "Close", MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -Ui.S(26), OffsetRight = Ui.S(6), OffsetTop = -Ui.S(14), OffsetBottom = Ui.S(18) };
        Ui.Style(collapsePreparation, Ui.ButtonKind.Quiet);
        collapsePreparation.Pressed += () => { _hudWorkspaceOpen = false; RefreshHudWorkspace(); };
        corner.AddChild(collapsePreparation);
        _hudTabs.TabsVisible = false;
        _hudTabs.UseHiddenTabsForMinSize = false;
        foreach (var name in new[] { "Build", "Overview", "Programme", "Staff", "Supplies" })
        {
            var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            // These sheets scroll their own tables, keeping the running order or crew in view.
            if (name is "Programme" or "Staff")
                scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
            _hudTabs.AddChild(scroll);
            var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 12);
            var gutter = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; gutter.AddThemeConstantOverride("margin_right", Ui.Px(12));
            gutter.AddChild(box); scroll.AddChild(gutter); Ui.SlimScrollbar(scroll); _hudPages.Add(name, box);
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

        foreach (var (name, label) in new[] { ("Programme", "Choose acts"), ("Staff", "Manage staff"), ("Supplies", "Review supplies") })
        {
            var destination = name; var shortcut = ButtonText(label, () => SelectHudTab(destination)); shortcut.CustomMinimumSize = new Vector2(0, 32); overview.AddChild(shortcut);
        }
        Booking.Build(_hudPages["Programme"]);
        BuildStaffPage(_hudPages["Staff"]);
        BuildSuppliesPage(_hudPages["Supplies"]);
        _preparationOfferBox = _hudPages["Staff"]; _preparationOfferInsertIndex = _preparationOfferBox.GetChildCount(); RebuildPreparationOffers();
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

        Stage.Build(layer, size); _liveSetCue = Stage.Summary;

        _contextPanel = HudPanel(layer, new Vector2(width - 300, 280), new Vector2(300, Math.Min(380, height - 340))); _contextPanel.Visible = false;
        _hudContextScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _contextPanel.AddChild(_hudContextScroll);
        var detail = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; detail.AddThemeConstantOverride("separation", 8); _hudContextScroll.AddChild(detail);
        var contextHeading = new HBoxContainer(); detail.AddChild(contextHeading); contextHeading.AddChild(HudLabel("Selected", 12));
        var contextClose = ButtonText("×", ClearSelection); contextClose.Name = "CloseSelectedPanel";
        contextClose.TooltipText = "Close selected object panel"; contextHeading.AddChild(contextClose);
        _inspectorTitle = HudLabel("", 20); detail.AddChild(_inspectorTitle);
        _inspectorTraits = HudLabel("", 13); _inspectorTraits.AddThemeColorOverride("font_color", Ui.TealDeep);
        _inspectorTraits.AutowrapMode = TextServer.AutowrapMode.WordSmart; _inspectorTraits.Visible = false; detail.AddChild(_inspectorTraits);
        BuildWaterFlowInspector(detail); BuildSatisfactionBar(detail); BuildMedicalNeedBars(detail); BuildImmersionNeedBars(detail);
        // Essential actions precede optional prose and remain accessible by scroll.
        BuildImmersionVendorInspector(detail); BuildMedicalActionInspector(detail); BuildDisorderActionInspector();
        BuildDisorderStageInspector(detail); BuildStagePowerAction(detail); BuildSecurityPostInspectorAction(detail); BuildEyeViewAction(detail);
        _inspectorBody = HudLabel("", 13); detail.AddChild(_inspectorBody);
        ConstrainHudControls(detail);
        contextClose.CustomMinimumSize = new Vector2(38, 38); contextClose.SizeFlagsHorizontal = Control.SizeFlags.Fill;

        _hudMenu = HudPanel(layer, new Vector2(width - 265, Ui.TopBar + 6), new Vector2(250, 180)); _hudMenu.Visible = false;
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

        _hudAlerts = new Control { Position = new Vector2(Ui.Gutter, Ui.S(76)), Size = new Vector2(Ui.S(320), 130), MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 20 }; layer.AddChild(_hudAlerts);
        _hudAlertBox = new VBoxContainer { Size = new Vector2(Ui.S(320), 0), MouseFilter = Control.MouseFilterEnum.Ignore }; _hudAlertBox.AddThemeConstantOverride("separation", Ui.Px(8)); _hudAlerts.AddChild(_hudAlertBox);
        _hudRoster = HudPanel(layer, new Vector2(Ui.Gutter, height - Ui.Dock - 8 - 280), new Vector2(330, 280)); _hudRoster.Visible = false;
        _preparationRosterScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _hudRoster.AddChild(_preparationRosterScroll);
        _preparationPeople = HudLabel("", 13); _preparationPeople.CustomMinimumSize = new Vector2(300, 0); _preparationRosterScroll.AddChild(_preparationPeople);
        LiveBottom.Build(layer, size); _hudStatus = LiveBottom.Message;
        BuildMoments(layer, size);
        BuildFieldNotes(layer, size);
        Perks.Build(layer);
        Hearing.Build(layer); Drawer.Build(layer, size); Dock.Build(layer, size); BuildMapControls(layer, size); BoxOfficeView.Build(layer, size); RefreshPreparationHud();
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

    private bool HudProgrammeSelected() => HudPageSelected("Programme");
    private bool HudPageSelected(string name) => _hudTabs is not null &&
        _hudTabs.CurrentTab == Array.IndexOf(_hudPages.Keys.ToArray(), name);

    private void ShowHudStartConfirmation()
    {
        if (_buildGhostKind is not null) { _preparationMessage = "Finish or cancel placement before opening."; RefreshPreparationHud(); return; }
        var issue = _session.ValidateCommand(CampaignEnvelope(new StartPreparedEditionCommand()));
        if (issue is not null) { _preparationMessage = issue.Message; RefreshPreparationHud(); return; }
        // Stock is optional, but opening with none is almost always a mistake: say so before the money goes.
        var noStock = _session.CapturePreparationPlan() is { SoftDrinks: 0, Beers: 0 };
        const string warning = "\n\nNo stock ordered: the bar will have nothing to sell. Open anyway, or order some in Supplies.";
        var text = _hudStartConfirmation!.DialogText.Replace(warning, "");
        _hudStartConfirmation.DialogText = noStock ? text + warning : text;
        _hudStartConfirmation.PopupCentered(new Vector2I(480, noStock ? 240 : 180));
    }


    private void RefreshHudWorkspace()
    {
        if (!Top.IsBuilt || _session.CapturePreparation() is not { } p) return;
        var preparing = p.Status == PreparationStatus.Preparing;
        var placing = _buildGhostKind is not null;
        if (placing) _hudWorkspaceOpen = false;
        var finance = _session.CaptureSnapshot().FestivalFinances.Single(f => f.OwnerId.Value == p.FinanceOwnerId);
        _hudStatus!.Text = placing ? "Placement preview · no change until a valid click" : _preparationMessage;
        if (_buildGhostKind is not null) _hudStatus.Text = _preparationMessage;
        if (p.Status == PreparationStatus.Departing)
            _hudStatus.Text = $"Festival finished · Guests leaving: {p.People.Count(person => person.Role == ProtectedPersonRole.Guest && person.Admitted && !person.Departed)}";
        _hudStatus.TooltipText = _preparationMessage;
        var briefing = BoxOfficeOpen;
        _hudWorkspace!.Visible = preparing && _hudWorkspaceOpen && !placing && _session.CapturePerks()?.Pending != true && !briefing;
        if (Drawer.Panel is not null) Drawer.Panel.Visible = preparing && _buildDrawerOpen && !placing && _session.CapturePerks()?.Pending != true && !briefing;
        BoxOfficeView.Refresh(briefing);
        Top.Briefing = briefing;
        LayoutOwnedPerkWorkspace();
        LiveBottom.Refresh(!preparing);
        Stage.Refresh(_hudProgrammeOpen);
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
        _hudDiagnosticsText!.Text = $"Tick {_session.CurrentTick} · hash {_session.CaptureSnapshot().AuthoritativeHash}\nPhase {_session.Phase} · status {p.Status} · paused {_session.IsPaused}\n{_preparationMessage}";
        _preparationSummary.Text = $"{(_session.CaptureProgramme() is null ? "Fixed festival roster" : "Three fixed sets · eight-minute festival day")}\n" +
            $"Programme: {(_session.CaptureProgramme() is { ActIds.Length: 3 } ? "three acts booked" : p.AcceptedOffers.Any(id => id.StartsWith("act.", StringComparison.Ordinal)) ? "act booked" : "choose before opening")}\n" +
            $"Sound: {(_session.HiredStaff(StaffRole.Sound) is { } sound ? $"{sound.Name} · sound engineer hired · {FestivalCurrency.Format(sound.WagePennies)}" : "hire a sound engineer before opening")}\n" +
            $"Owned rig {p.OwnedEquipment.Length} · rental {p.Rentals.Length} · equipment & stock optional";
        if (preparing && p.Plan is { } plan)
        {
            _preparationSummary.Text = $"Available {FestivalCurrency.Format(finance.CashPennies)}\nSetup cost {FestivalCurrency.Format(_session.PreparationPlanCost)} · remaining {FestivalCurrency.Format(_session.PreparationRemainingCash)}\n" +
                $"Unpaid lineup: {plan.ActIds.Count(id => id != "")}/3 acts\nPlanned hires: {string.Join(", ", plan.OfferIds.Where(id => id.StartsWith("staff.") || id == "maintenance.worker").Select(id => _session.GetPreparationOffers().Single(o => o.Id == id).Name))}\nExpected protected people: {_session.ExpectedPreparedPeopleCount}/50\nOwned rig {p.OwnedEquipment.Length} · selected rig {(plan.OfferIds.SingleOrDefault(id => id.StartsWith("equipment.")) ?? "none")}\nFreely revise purchases. Existing site and perk property stays committed.";
        }
        if (_medicalActionInspector is not null)
        {
            if (_medicalButtons.TryGetValue(MedicalAction.DispatchMedic, out var medicButton)) medicButton.Text = "Send medic";
            if (_disorderButtons.TryGetValue(DisorderAction.DispatchSecurity, out var stewardButton)) stewardButton.Text = "Send steward";
        }
        RefreshHudAlerts();
        Drawer.Refresh();
        Dock.Refresh();
        if (_mapControls is not null) _mapControls.Visible = Dock.Visible && !(_hudWorkspaceOpen && !_buildDrawerOpen && (HudProgrammeSelected() || HudPageSelected("Staff")));
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
        _activeAlertCount = alerts.Count;
        Top.Refresh(alerts.Count);
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
            if (!_alertClearance.TryGetValue(panel, out var layout) || panel.Position != layout.Applied || panel.Size != layout.AppliedSize)
                layout = (panel.Position, panel.Size, panel.Position, panel.Size);
            var y = alerts.Length == 0 ? layout.Position.Y : Math.Max(layout.Position.Y, Ui.S(76) + _hudAlertBox.GetCombinedMinimumSize().Y + Ui.S(10));
            panel.Position = new Vector2(layout.Position.X, y);
            // Scrollable preparation content keeps its original lower edge above the dock.
            panel.Size = new Vector2(layout.Size.X, Math.Max(65, layout.Size.Y - (y - layout.Position.Y)));
            _alertClearance[panel] = (layout.Position, layout.Size, panel.Position, panel.Size);
        }
        var key = string.Join("|", alerts.Select(a => a.Alert.Id + a.Alert.Text));
        // Modal documents own the screen; the feed returns when they close.
        _hudAlerts!.Visible = alerts.Length > 0 && !Hearing.IsOpen && !ResultsPaper.IsOpen && _session.CapturePerks()?.Pending != true && !BoxOfficeOpen;
        for (var i = 0; i < Math.Min(alerts.Length, _hudAlertActions.Count); i++)
            _hudAlertActions[i].Modulate = new Color(1, 1, 1, alerts[i].Opacity);
        if (key == _hudAlertKey) return;
        _hudAlertKey = key;
        foreach (var child in _hudAlertBox!.GetChildren()) { _hudAlertBox.RemoveChild(child); child.QueueFree(); }
        _hudAlertActions.Clear();
        if (alerts.Length > 0)
        {
            var header = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            header.AddThemeStyleboxOverride("panel", Ui.Box(new Color(Ui.Bar, 0.88f), 6, padX: 10, padY: 7));
            var line = new HBoxContainer(); header.AddChild(line);
            var caption = Ui.Caps("Incidents", Ui.BarText); caption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; line.AddChild(caption);
            line.AddChild(Ui.Caps($"{Math.Max(_activeAlertCount, alerts.Length)} urgent", Ui.Warn));
            _hudAlertBox.AddChild(header);
        }
        foreach (var entry in alerts)
        {
            var id = entry.Alert.Id;
            var card = IncidentCard(entry.Alert, () => { if (_urgentAlertActions.TryGetValue(id, out var action)) action(); });
            card.Modulate = new Color(1, 1, 1, entry.Opacity);
            _hudAlertBox.AddChild(card); _hudAlertActions.Add(card);
        }
        _hudAlerts.Size = new Vector2(Ui.S(320), _hudAlertBox.GetCombinedMinimumSize().Y);
    }

    /// <summary>An urgent alert as a card: what is happening, to whom, and Locate.</summary>
    private static PanelContainer IncidentCard(Festival.ContentAdapter.UrgentAlert alert, Action locate)
    {
        // Alert text reads "Who: what · locate" for people and "What · inspect ..." for equipment.
        var text = alert.Text;
        var cut = text.LastIndexOf(" · ", StringComparison.Ordinal);
        var core = cut >= 0 ? text[..cut] : text;
        var colon = core.IndexOf(": ", StringComparison.Ordinal);
        var (title, subject) = colon >= 0 ? (core[(colon + 2)..], core[..colon]) : (core, cut >= 0 ? text[(cut + 3)..] : "");
        title = title.Length > 0 ? char.ToUpperInvariant(title[0]) + title[1..] : title;
        subject = subject.Length > 0 ? char.ToUpperInvariant(subject[0]) + subject[1..] : subject;
        var card = new PanelContainer { TooltipText = text, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        card.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Paper, 8, padX: 12, padY: 10, shadow: 9, shadowAlpha: 0.3f));
        card.GuiInput += input => { if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) { locate(); card.AcceptEvent(); } };
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation", Ui.Px(10)); card.AddChild(row);
        var badge = new PanelContainer { CustomMinimumSize = Ui.S(34, 34), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        badge.AddThemeStyleboxOverride("panel", Ui.Box(alert.Priority >= 60 ? Ui.Alert : Ui.GoldShadow, 17));
        var warning = Ui.IconRect("triangle-alert", 18, Colors.White); warning.SizeFlagsHorizontal = warning.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        badge.AddChild(warning); row.AddChild(badge);
        var words = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        words.AddThemeConstantOverride("separation", 0); row.AddChild(words);
        var heading = Ui.Heading(title, 17); heading.ClipText = true; heading.MouseFilter = Control.MouseFilterEnum.Ignore; words.AddChild(heading);
        var who = Ui.Text(subject, 13, Ui.InkMuted); who.ClipText = true; who.MouseFilter = Control.MouseFilterEnum.Ignore; words.AddChild(who);
        var button = Ui.IconButton("Locate", "crosshair", Ui.ButtonKind.Alert, locate, 13.5f);
        button.CustomMinimumSize = new Vector2(0, Ui.S(34)); button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        button.AddThemeConstantOverride("icon_max_width", Ui.Px(15));
        row.AddChild(button);
        return card;
    }

    private void HudLocatePerson(ulong id)
    {
        SelectAttendee(new EntityId(id));
        if (_attendeeVisuals.TryGetValue(new EntityId(id), out var visual)) _rig.FocusOn(visual.Position);
        _hudAlerts!.Visible = false;
    }

    private bool HudBlocksPlacement(Vector2 screen)
    {
        if(Perks.Panel?.Visible==true && Perks.Panel.GetGlobalRect().HasPoint(screen))return true;
        if(Perks.EffectPopup?.Visible==true && Perks.EffectPopup.GetGlobalRect().HasPoint(screen))return true;
        if (!Top.IsBuilt) return screen.X < 435 || screen.X > GetViewport().GetVisibleRect().Size.X - 435 || screen.Y < 110;
        if (screen.Y < Ui.TopBar || screen.Y > GetViewport().GetVisibleRect().Size.Y - (_session.PreparedStatus == PreparationStatus.Preparing ? Ui.Dock : Ui.S(64))) return true;
        return new Control?[] { _hudWorkspace, Drawer.Panel, Dock.Readiness, _mapControls, _hudMenu, _contextPanel, _hudAlerts, _hudRoster, _hudDiagnostics, Stage.Panel, LiveBottom.Panel }
            .Any(control => control?.IsVisibleInTree() == true && control.GetGlobalRect().HasPoint(screen));
    }

    private VBoxContainer? _mapControls;

    /// <summary>Round rotate and zoom buttons at the field's right edge during preparation.</summary>
    private void BuildMapControls(CanvasLayer layer, Vector2 size)
    {
        _mapControls = new VBoxContainer();
        _mapControls.AddThemeConstantOverride("separation", Ui.Px(8));
        foreach (var (icon, tip, action) in new (string, string, Action)[]
                 { ("rotate-cw", "Rotate view (Q / E)", () => _rig.Rotate(1)), ("plus", "Zoom in (wheel)", () => _rig.Zoom(-4)), ("minus", "Zoom out (wheel)", () => _rig.Zoom(4)) })
        {
            var button = new Button { Icon = Ui.Icon(icon), ExpandIcon = true, IconAlignment = HorizontalAlignment.Center, TooltipText = tip,
                CustomMinimumSize = Ui.S(44, 44), MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            Ui.Style(button, Ui.ButtonKind.Bar, radius: 22);
            var round = Ui.Box(Ui.Bar, 22, new Color(Ui.BarText, 0.25f), 1, shadow: 4, shadowAlpha: 0.3f);
            foreach (var state in new[] { "normal", "focus", "pressed" }) button.AddThemeStyleboxOverride(state, round);
            button.AddThemeStyleboxOverride("hover", Ui.Box(Ui.BarRaised, 22, new Color(Ui.BarText, 0.3f), 1, shadow: 4, shadowAlpha: 0.3f));
            button.Pressed += action; _mapControls.AddChild(button);
        }
        _mapControls.Position = new Vector2(size.X - Ui.Gutter - Ui.S(44), size.Y - Ui.Dock - Ui.S(20) - Ui.S(44) * 3 - Ui.S(8) * 2);
        layer.AddChild(_mapControls);
    }
}

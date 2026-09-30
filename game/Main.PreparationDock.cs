using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private PanelContainer? _preparationDock;
    private PanelContainer? _preparationReadiness;
    private VBoxContainer? _preparationReadinessRows;
    private Label? _preparationDockCost;
    private Button? _preparationDockStart;
    private Button? _preparationDockReason;
    private readonly Dictionary<string, Button> _preparationDockTabs = [];
    private readonly Dictionary<string, Label> _preparationDockBadges = [];
    private string _preparationReadinessKey = "";

    private void BuildPreparationDock(CanvasLayer layer, Vector2 size)
    {
        _preparationDock = HudPanel(layer, new Vector2(8, size.Y - 124), new Vector2(size.X - 16, 116), HudInk);
        _preparationDock.AddThemeStyleboxOverride("panel", HudStyle(HudInk, 5));
        var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 4); _preparationDock.AddChild(stack);
        var navigation = new HBoxContainer(); navigation.AddThemeConstantOverride("separation", 5); stack.AddChild(navigation);
        foreach (var (name, icon) in new[] { ("Build", "⚒"), ("Programme", "♫"), ("Staff", "♟"),
                     ("Equipment", "▣"), ("Stock", "▤") })
        {
            var destination = name;
            var button = ButtonText($"{icon}  {name}", () => SelectPreparationDockDestination(destination));
            button.CustomMinimumSize = new Vector2(120, 54);
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.AddThemeFontSizeOverride("font_size", 16);
            button.TooltipText = $"Open {name} preparation";
            navigation.AddChild(button);
            _preparationDockTabs.Add(name, button);
            var badge = HudLabel("!", 20);
            badge.MouseFilter = Control.MouseFilterEnum.Ignore;
            badge.AddThemeColorOverride("font_color", new Color("b9212a"));
            badge.SetAnchorsPreset(Control.LayoutPreset.TopRight);
            badge.Position = new Vector2(-19, 0);
            badge.CustomMinimumSize = new Vector2(18, 22);
            badge.Visible = false;
            button.AddChild(badge);
            _preparationDockBadges.Add(name, badge);
        }
        var ancillary = new HBoxContainer(); ancillary.AddThemeConstantOverride("separation", 3);
        ancillary.CustomMinimumSize = new Vector2(size.X >= 1600 ? 330 : 275, 0); navigation.AddChild(ancillary);
        foreach (var (text, action) in new (string, Action)[]
                 { ("People", () => _hudRoster!.Visible = !_hudRoster.Visible),
                   ("Your Perks", () => { _perksExpanded = !_perksExpanded; _perkHudKey = ""; RefreshPerkHud(); }),
                   ("Rotate view", () => Rotate(1)) })
        {
            var button = ButtonText(text, action); button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.CustomMinimumSize = new Vector2(70, 54); button.AddThemeFontSizeOverride("font_size", 12);
            button.TooltipText = text; ancillary.AddChild(button);
        }

        var accounts = new HBoxContainer(); accounts.AddThemeConstantOverride("separation", 8); stack.AddChild(accounts);
        _preparationDockCost = HudLabel("", 13); _preparationDockCost.AddThemeColorOverride("font_color", HudPaper);
        accounts.AddChild(_preparationDockCost);
        _preparationDockStart = ButtonText("Start festival", ShowHudStartConfirmation);
        _preparationDockStart.CustomMinimumSize = new Vector2(148, 41); accounts.AddChild(_preparationDockStart);
        _preparationDockReason = ButtonText("! Required tasks", OpenFirstPreparationBlocker);
        _preparationDockReason.CustomMinimumSize = new Vector2(210, 41);
        _preparationDockReason.AddThemeColorOverride("font_color", new Color("a52028"));
        accounts.AddChild(_preparationDockReason);

        _preparationReadiness = HudPanel(layer, new Vector2(size.X - 292, 77), new Vector2(277, 305));
        var readiness = new VBoxContainer(); readiness.AddThemeConstantOverride("separation", 5);
        _preparationReadiness.AddChild(readiness);
        readiness.AddChild(HudLabel("Before opening", 20));
        readiness.AddChild(new HSeparator());
        _preparationReadinessRows = new VBoxContainer(); _preparationReadinessRows.AddThemeConstantOverride("separation", 3);
        readiness.AddChild(_preparationReadinessRows);
        readiness.AddChild(new HSeparator());
        readiness.AddChild(HudLabel("Equipment & stock optional", 12));
    }

    private void SelectPreparationDockDestination(string destination)
    {
        if (_session.PreparedStatus != PreparationStatus.Preparing) return;
        if (_buildGhostKind is not null) CancelBuildPlacement();
        if (destination == "Build")
        {
            if (!_buildDrawerOpen) OpenBuildCatalogue();
            return;
        }
        if (_hudWorkspaceOpen && !_buildDrawerOpen && _hudTabs is not null &&
            _hudPages.Keys.ElementAt(_hudTabs.CurrentTab) == destination) return;
        SelectHudTab(destination);
    }

    private void OpenFirstPreparationBlocker()
    {
        var blocker = _session.GetPreparationStartBlockers().FirstOrDefault();
        if (blocker is null) return;
        OpenPreparationBlocker(blocker.Owner);
    }

    private void OpenPreparationBlocker(PreparationStartOwner owner)
    {
        switch (owner)
        {
            case PreparationStartOwner.Programme: SelectPreparationDockDestination("Programme"); break;
            case PreparationStartOwner.Staff: SelectPreparationDockDestination("Staff"); break;
            default: SelectPreparationDockDestination("Build"); break;
        }
    }

    private void RefreshPreparationDock()
    {
        if (_preparationDock is null) return;
        var preparing = _session.PreparedStatus == PreparationStatus.Preparing;
        var shown = preparing && _session.CapturePerks()?.Pending != true;
        _preparationDock.Visible = shown;
        _preparationReadiness!.Visible = shown && !(_hudWorkspaceOpen && !_buildDrawerOpen && HudProgrammeSelected()) &&
            _contextPanel?.Visible != true;
        if (!shown) return;
        var blockers = _session.GetPreparationStartBlockers();
        var selected = _buildDrawerOpen ? "Build" : _hudWorkspaceOpen && _hudTabs is not null
            ? _hudPages.Keys.ElementAt(_hudTabs.CurrentTab) : "";
        foreach (var (name, button) in _preparationDockTabs)
        {
            var active = selected == name;
            var fill = active ? new Color("126c70") : HudPaper;
            foreach (var state in new[] { "normal", "pressed", "hover", "focus" })
                button.AddThemeStyleboxOverride(state, HudStyle(fill, 8));
            foreach (var state in new[] { "font_color", "font_pressed_color", "font_hover_color", "font_focus_color" })
                button.AddThemeColorOverride(state, active ? Colors.White : HudInk);
            var relevant = blockers.Where(blocker => name switch
            {
                "Build" => blocker.Owner == PreparationStartOwner.Overview,
                "Programme" => blocker.Owner == PreparationStartOwner.Programme,
                "Staff" => blocker.Owner == PreparationStartOwner.Staff,
                _ => false
            }).ToArray();
            _preparationDockBadges[name].Visible = relevant.Length != 0;
            button.TooltipText = relevant.Length == 0 ? $"Open {name} preparation" :
                $"{name}: required before Start festival. " + string.Join(" ", relevant.Select(item => item.Message));
        }
        var funds = _session.CaptureSnapshot().FestivalFinances.Single().CashPennies;
        _preparationDockCost!.Text = $"Budget {FestivalCurrency.Format(funds)}   ·   Full draft {FestivalCurrency.Format(_session.PreparationPlanCost)}   ·   " +
            $"Remaining {FestivalCurrency.Format(_session.PreparationRemainingCash)}   ·   Pay only at Start";
        if (_buildGhostKind is not null ||
            false)
            _preparationDockCost.Text += "\n" + _preparationMessage;
        var issue = _buildGhostKind is not null ? "Finish or cancel placement" :
            blockers.FirstOrDefault()?.Message ?? _session.ValidateCommand(CampaignEnvelope(new StartPreparedEditionCommand()))?.Message;
        _preparationDockStart!.Disabled = issue is not null;
        _preparationDockStart.TooltipText = issue ?? "Review the full setup charge, then open the festival.";
        _preparationDockReason!.Visible = issue is not null;
        _preparationDockReason.Text = issue is null ? "Ready to open" : "! " + (blockers.Count == 0 ? "Finish placement" : "Required tasks");
        _preparationDockReason.TooltipText = issue ?? "Ready to open";
        var requirements = _session.GetPreparationStartRequirements();
        var key = string.Join("|", requirements.Select(item => item.Id + ":" + item.Complete));
        if (key == _preparationReadinessKey) return;
        _preparationReadinessKey = key;
        foreach (var child in _preparationReadinessRows!.GetChildren()) child.QueueFree();
        foreach (var requirement in requirements)
        {
            var owner = requirement.Owner;
            var row = ButtonText((requirement.Complete ? "✓ " : "! ") + requirement.Label,
                () => OpenPreparationBlocker(owner));
            row.TooltipText = requirement.Complete
                ? requirement.Label + " complete. Open the matching preparation panel to review or revise it."
                : requirement.Detail + " Open the matching preparation panel.";
            row.AddThemeColorOverride("font_color", requirement.Complete ? new Color("126c70") : new Color("aa242b"));
            row.AddThemeFontSizeOverride("font_size", 12);
            row.CustomMinimumSize = new Vector2(0, 30);
            _preparationReadinessRows.AddChild(row);
        }
    }
}

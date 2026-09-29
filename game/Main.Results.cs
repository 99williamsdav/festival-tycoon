using Festival.Simulation;
using Godot;
using System;

namespace Festival.Game;

public partial class Main
{
    private CanvasLayer? _festivalPaper;
    private ScrollContainer? _resultsScroll;
    private VBoxContainer? _resultsBody;
    private Button? _newspaperTab;
    private Button? _accountsTab;
    private bool _resultsShowingAccounts;
    private int _newspaperScrollPosition;
    private int _accountsScrollPosition;

    private void RefreshFestivalPaper()
    {
        if (_newCampaignOnEnter) return;
        if (_session.CompletedFestivalResult is null)
        {
            _festivalPaper?.QueueFree(); _festivalPaper = null;
            if (_session.PreparedStatus == PreparationStatus.Departing)
                _preparationSummary.Text += $"\nFestival finished · Guests leaving: {_session.CapturePreparation()!.People.Count(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed)}";
            return;
        }
        if (_festivalPaper is not null) return;
        _resultsShowingAccounts = false; _newspaperScrollPosition = _accountsScrollPosition = 0;
        ResetFinanceFeedback();
        _festivalPaper = new CanvasLayer { Layer = 19 }; AddChild(_festivalPaper);
        var shade = new ColorRect { Color = new Color("172d2b") };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect); _festivalPaper.AddChild(shade);
        var center = new CenterContainer(); center.SetAnchorsPreset(Control.LayoutPreset.FullRect); shade.AddChild(center);
        var viewport = GetViewport().GetVisibleRect().Size;
        var width = Math.Min(1060, viewport.X - 40);
        var height = Math.Min(850, viewport.Y - 40);
        var paper = new PanelContainer { CustomMinimumSize = new Vector2(width, height) };
        paper.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("fff3d3") }); center.AddChild(paper);
        var margins = HearingMargins(viewport.X < 900 ? 18 : 30, 18); paper.AddChild(margins);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 10); margins.AddChild(column);
        var nav = new HBoxContainer(); nav.AddThemeConstantOverride("separation", 10); column.AddChild(nav);
        nav.AddChild(ResultText("Festival results", viewport.X < 900 ? 20 : 25, true));
        _newspaperTab = ButtonText("Newspaper", () => ShowResultDocument(false));
        _newspaperTab.Name = "NewspaperTab"; _newspaperTab.TooltipText = "Public festival review";
        _newspaperTab.CustomMinimumSize = new Vector2(125, 44); nav.AddChild(_newspaperTab);
        _accountsTab = ButtonText("Accounts", () => ShowResultDocument(true));
        _accountsTab.Name = "AccountsTab"; _accountsTab.TooltipText = "Income, expenditure and cash";
        _accountsTab.CustomMinimumSize = new Vector2(125, 44); nav.AddChild(_accountsTab);
        Button? menu = null;
        menu = ButtonText("Return to menu", () =>
        {
            if (RelaxedSaveCadence && !SaveCadenceMilestone("Return to menu"))
            {
                menu!.Text = "Save failed · retry menu";
                menu.TooltipText = _preparationMessage;
                return;
            }
            _newCampaignOnEnter = true; _festivalPaper?.QueueFree(); _festivalPaper = null; BuildStartSplash();
        });
        menu.Name = "ReturnToMenu"; menu.CustomMinimumSize = new Vector2(170, 44); nav.AddChild(menu);
        column.AddChild(HearingRule());
        _resultsScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FocusMode = Control.FocusModeEnum.All };
        column.AddChild(_resultsScroll);
        _resultsBody = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _resultsBody.AddThemeConstantOverride("separation", 12); _resultsScroll.AddChild(_resultsBody);
        ShowResultDocument(false, first: true);
    }
}

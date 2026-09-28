using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private PanelContainer? _perkPanel;
    private PanelContainer? _ownedEffectPopup;
    private Label? _ownedEffectText;
    private Control? _ownedEffectTarget;
    private Control? _ownedHoveredCard;
    private Control? _ownedFocusedCard;
    private VBoxContainer? _perkBody;
    private Button? _perkToggle;
    private ScrollContainer? _perkScroll;
    private ScrollContainer? _ownedPerkScroll;
    private string _ownedPerkSignature = "";
    private int _ownedPerkOffset;
    private bool _ownedWorkspaceConstrained;
    private int _ownedWorkspaceLayoutGeneration;
    private int _ownedContextScroll;
    private int _ownedContextClosedScroll;
    private GameSession? _ownedContextSession;
    private string _ownedContextIdentity = "";
    private string ContextScrollIdentity => $"{_selected?.StableId}|{_selectedAttendeeId}|{_selectedMedicalFacility}|{_selectedWaterPointId}|{_selectedImmersionVendor}|{_selectedSecurityPost}|{_selectedGenerator}";
    private readonly System.Collections.Generic.Dictionary<int, int> _ownedWorkspaceScroll = [];
    private readonly System.Collections.Generic.Dictionary<int, int> _ownedWorkspaceClosedScroll = [];
    private string _perkHudKey = "";
    private bool _perksExpanded;
    private string? _selectedPerk;
    private string? _pendingPerkChoice;
    private string? _pendingPerkReplacement;
    private bool _pendingPerkSkip;
    private int _confirmationDraftAttempt;
    private ulong _confirmationCursor;
    private string? _perkCaptureDirectory;
    private int _perkCaptureFrame;
    private string _perkCapturePureHash = "";

    private void BuildPerkHud(CanvasLayer layer)
    {
        var size = GetViewport().GetVisibleRect().Size;
        _perkToggle = ButtonText("Your Perks", () => { _perksExpanded = !_perksExpanded; _perkHudKey = ""; RefreshPerkHud(); });
        _perkToggle.Position = new Vector2(365, size.Y - 50); _perkToggle.Theme = HudTheme(); layer.AddChild(_perkToggle);
        _perkPanel = HudPanel(layer, new Vector2(60, 65), new Vector2(size.X - 120, size.Y - 120));
        _perkPanel.AddThemeStyleboxOverride("panel", HudStyle(new Color("d8d6bd"), 22));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _perkScroll = scroll; _perkPanel.AddChild(scroll);
        _perkBody = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _perkBody.AddThemeConstantOverride("separation", 9); scroll.AddChild(_perkBody);
        _ownedEffectPopup = HudPanel(layer, Vector2.Zero, new Vector2(350, 90));
        _ownedEffectPopup.Visible = false;
        _ownedEffectPopup.MouseFilter = Control.MouseFilterEnum.Ignore;
        _ownedEffectPopup.ZIndex = 20;
        _ownedEffectText = HudLabel("", 16); _ownedEffectText.CustomMinimumSize = new Vector2(326, 0);
        _ownedEffectText.Size = new Vector2(326, 66); _ownedEffectText.MouseFilter = Control.MouseFilterEnum.Ignore; _ownedEffectPopup.AddChild(_ownedEffectText);
    }
    private static void ClearPerkChildren(Node parent)
    {
        foreach (var child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }
    private PanelContainer PerkCard(string id)
    {
        var perk = PerkCatalogue.All.Single(item => item.Id == id);
        var card = new PanelContainer { CustomMinimumSize = new Vector2(248, 358) };
        var style = HudStyle(new Color("fff4d6"), 14);
        style.BorderColor = new Color("596450"); style.BorderWidthTop = style.BorderWidthBottom = style.BorderWidthLeft = style.BorderWidthRight = 2;
        style.CornerRadiusTopLeft = style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 10;
        card.AddThemeStyleboxOverride("panel", style);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 10); card.AddChild(box);
        box.AddChild(HudLabel("FESTIVAL PERK", 10));
        var title = HudLabel(perk.Name, 25); title.AddThemeFontOverride("font", HearingSerif()); title.CustomMinimumSize = new Vector2(216, 66); box.AddChild(title);
        box.AddChild(PerkArtwork(id, 220));
        box.AddChild(new HSeparator());
        var effect = HudLabel(perk.Effect, 16); effect.CustomMinimumSize = new Vector2(216, 70); box.AddChild(effect);
        return card;
    }
    private PanelContainer OwnedPerkCard(string id)
    {
        var perk = PerkCatalogue.All.Single(item => item.Id == id);
        var card = new PanelContainer { CustomMinimumSize = new Vector2(156, 152), FocusMode = Control.FocusModeEnum.All };
        card.AddThemeStyleboxOverride("panel", HudStyle(new Color("fff4d6"), 6));
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 4); card.AddChild(box);
        var title = HudLabel(perk.Name, 17); title.AddThemeFontOverride("font", HearingSerif());
        title.CustomMinimumSize = new Vector2(144, 40); box.AddChild(title);
        box.AddChild(PerkArtwork(id, 144));
        foreach (var child in PerkDescendants(card).OfType<Control>()) child.MouseFilter = Control.MouseFilterEnum.Ignore;
        card.SetMeta("effect", perk.Effect);
        card.MouseEntered += () => { _ownedHoveredCard = card; RefreshOwnedEffect(); };
        card.MouseExited += () => { if (_ownedHoveredCard == card) _ownedHoveredCard = null; RefreshOwnedEffect(); };
        card.FocusEntered += () => { _ownedFocusedCard = card; RefreshOwnedEffect(); };
        card.FocusExited += () => { if (_ownedFocusedCard == card) _ownedFocusedCard = null; RefreshOwnedEffect(); };
        return card;
    }
    private TextureRect PerkArtwork(string id, float width) => new() { Name = "PerkArtwork", Texture = GD.Load<Texture2D>($"res://assets/ui/perks/{id}.png"),
        TextureFilter = CanvasItem.TextureFilterEnum.Linear, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(width, width * 2 / 3),
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
    private void ShowOwnedEffect(Control card, string effect)
    {
        if (_perkPanel?.Visible != true || _session.CapturePerks()?.Pending != false) return;
        _ownedEffectTarget = card; _ownedEffectText!.Text = effect;
        _ownedEffectPopup!.Size = new Vector2(350, 90);
        var size = GetViewport().GetVisibleRect().Size;
        _ownedEffectPopup.Position = new Vector2(Math.Clamp(card.GetGlobalRect().GetCenter().X - 175, 10, size.X - 360), _perkPanel.Position.Y + 55);
        _ownedEffectPopup.Visible = true;
    }
    private void RefreshOwnedEffect()
    {
        var target = _ownedHoveredCard ?? _ownedFocusedCard;
        if (target is not null && GodotObject.IsInstanceValid(target) && target.IsVisibleInTree()) ShowOwnedEffect(target, target.GetMeta("effect").AsString());
        else { if (_ownedEffectPopup is not null) _ownedEffectPopup.Visible = false; _ownedEffectTarget = null; }
    }
    private void HideOwnedEffect() { if (_ownedEffectPopup is not null) _ownedEffectPopup.Visible = false; _ownedEffectTarget = null; _ownedHoveredCard = null; _ownedFocusedCard = null; }
    private void BeginPerkChoice(string id)
    {
        var p = _session.CapturePerks()!;
        if (p.Equipped.Length < 5) { CommitEquipmentAction(new ChoosePerkCommand(p.DraftAttempt, p.Cursor, id)); return; }
        _confirmationDraftAttempt=p.DraftAttempt;_confirmationCursor=p.Cursor;
        _pendingPerkChoice = id; _pendingPerkReplacement = null; _pendingPerkSkip = false; _perkHudKey = ""; RefreshPerkHud();
    }
    private void CancelPerkConfirmation()
    {
        HideOwnedEffect();
        _pendingPerkChoice = null; _pendingPerkReplacement = null; _pendingPerkSkip = false; _perkHudKey = ""; RefreshPerkHud();
    }
    private void ConfirmPerkChoice()
    {
        var p = _session.CapturePerks()!;
        SessionCommand command = _pendingPerkSkip ? new SkipPerksCommand(_confirmationDraftAttempt,_confirmationCursor) :
            new ChoosePerkCommand(_confirmationDraftAttempt,_confirmationCursor,_pendingPerkChoice!,_pendingPerkReplacement);
        CancelPerkConfirmation(); CommitEquipmentAction(command);
    }
    private void RefreshPerkHud()
    {
        if (_perkPanel is null) return;
        var p = _session.CapturePerks();
        _perkToggle!.Visible = p is { Ended: false };
        _perkPanel.Visible = p is { Ended: false } && (p.Pending || _perksExpanded);
        if (!_perkPanel.Visible || p?.Pending != false) HideOwnedEffect();
        LayoutOwnedPerkWorkspace();
        if (p is null || p.Ended) return;
        LayoutPerkHud(p.Pending);
        if((_pendingPerkChoice is not null || _pendingPerkSkip) && (!p.Pending || _confirmationDraftAttempt!=p.DraftAttempt || _confirmationCursor!=p.Cursor))
        { _pendingPerkChoice=null;_pendingPerkReplacement=null;_pendingPerkSkip=false; }
        _perkToggle.Text = "Your Perks";
        _perkToggle.TooltipText = $"{p.Equipped.Length} / 5 equipped · {(_perksExpanded ? "Collapse" : "Expand")} upwards";
        var key = System.Text.Json.JsonSerializer.Serialize(p) + (p.Pending && _perksExpanded) + _selectedPerk + _pendingPerkChoice + _pendingPerkReplacement + _pendingPerkSkip + _preparationMessage + GetViewport().GetVisibleRect().Size;
        if (key == _perkHudKey) return; _perkHudKey = key;
        var ownedSignature = string.Join("|", p.Equipped);
        if(!p.Pending && ownedSignature == _ownedPerkSignature && GodotObject.IsInstanceValid(_ownedPerkScroll))
            _ownedPerkOffset = _ownedPerkScroll!.ScrollHorizontal;
        else _ownedPerkOffset = 0;
        _ownedPerkSignature = ownedSignature;
        HideOwnedEffect(); ClearPerkChildren(_perkBody!);
        var heading = new HBoxContainer(); _perkBody!.AddChild(heading);
        var title = HudLabel(p.Pending ? "Choose a festival perk" : $"Your Perks · {p.Equipped.Length} / 5 equipped", p.Pending ? 29 : 21); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; title.AddThemeFontOverride("font", HearingSerif()); heading.AddChild(title);
        if (p.Pending)
        {
            var reroll = ButtonText(p.RerollUsed ? "Free reroll used" : "Reroll all 3 · 1 free", () => CommitEquipmentAction(new RerollPerksCommand(p.DraftAttempt,p.Cursor)));
            reroll.Disabled = p.RerollUsed || _pendingPerkChoice is not null || _pendingPerkSkip; reroll.TooltipText = "Owned perks excluded; options may repeat"; heading.AddChild(reroll);
        }
        else heading.AddChild(ButtonText("Collapse perks ▴", () => { _perksExpanded = false; RefreshPerkHud(); }));
        if(p.Pending) _perkBody.AddChild(HudLabel($"Keep it across retries while equipped. · {p.Equipped.Length} / 5 equipped", 14));
        if (_pendingPerkChoice is not null || _pendingPerkSkip) { BuildPerkConfirmation(p); return; }
        if (p.Pending)
        {
            var hand = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; hand.AddThemeConstantOverride("separation",26); _perkBody.AddChild(hand);
            foreach (var id in p.Hand)
            {
                var wrap = new VBoxContainer(); wrap.AddThemeConstantOverride("separation",12); hand.AddChild(wrap);
                var card = PerkCard(id); card.Name = "DraftPerkCard_" + id; wrap.AddChild(card);
                var choose = ButtonText(p.Equipped.Length == 5 ? "Replace a perk…" : "Choose this perk", () => BeginPerkChoice(id));
                choose.Name = "ChooseDraftPerk_" + id; wrap.AddChild(choose);
            }
            var footer = new HBoxContainer(); _perkBody.AddChild(footer);
            footer.AddChild(HudLabel("Three distinct eligible choices, equally likely. Owned perks excluded; options may repeat.\nYour hand is saved; reloading does not change it.",13));
            if (p.Equipped.Length == 5) footer.AddChild(ButtonText("Skip this choice", () => { _confirmationDraftAttempt=p.DraftAttempt;_confirmationCursor=p.Cursor;_pendingPerkSkip = true; _perkHudKey = ""; RefreshPerkHud(); }));
        }
        if(p.Pending){ _perkBody.AddChild(HudLabel("Equipped: " + (p.Equipped.Length == 0 ? "none yet" : string.Join(" · ",p.Equipped.Select(id=>PerkCatalogue.All.Single(item=>item.Id==id).Name))),12)); return; }
        _ownedPerkScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _perkBody.AddChild(_ownedPerkScroll);
        var slots = new HBoxContainer { Name = "EquippedPerkCards", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        slots.AddThemeConstantOverride("separation",10); _ownedPerkScroll.AddChild(slots);
        for (var i = 0; i < 5; i++)
        {
            var slot = new CenterContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; slots.AddChild(slot);
            if (i < p.Equipped.Length)
            {
                var card = OwnedPerkCard(p.Equipped[i]); card.Name = "Equipped_" + p.Equipped[i]; slot.AddChild(card);
            }
            else
            {
                var empty = new PanelContainer { CustomMinimumSize = new Vector2(156,152) };
                empty.AddThemeStyleboxOverride("panel",HudStyle(new Color("c7cbb8"),14));
                empty.AddChild(HudLabel($"Slot {i+1}\nEmpty",20)); slot.AddChild(empty);
            }
        }
        // Rebuilt containers release the preceding draft/card minimum on the next layout pass.
        // The old completed session may queue this release immediately before
        // Enter creates a fresh draft. Never apply its owned-strip size to a
        // different session's full-height draft panel on the next frame.
        var owner = _session;
        Callable.From(() =>
        {
            if (ReferenceEquals(owner, _session) && _session.CapturePerks()?.Pending == false &&
                _perkPanel is not null && GodotObject.IsInstanceValid(_perkPanel))
                _perkPanel.Size = new Vector2(GetViewport().GetVisibleRect().Size.X - 20, 220);
        }).CallDeferred();
        _ownedPerkScroll.SetDeferred(ScrollContainer.PropertyName.ScrollHorizontal, _ownedPerkOffset);
    }
    private void LayoutPerkHud(bool draft)
    {
        var size = GetViewport().GetVisibleRect().Size;
        _perkToggle!.Position = new Vector2((size.X - 90) / 2, size.Y - 50);
        _perkToggle.CustomMinimumSize = new Vector2(90, 38);
        _perkToggle.AddThemeFontSizeOverride("font_size", 12);
        _perkPanel!.AddThemeStyleboxOverride("panel", HudStyle(new Color("d8d6bd"), draft ? 22 : 10));
        _perkScroll!.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _perkScroll.VerticalScrollMode = draft ? ScrollContainer.ScrollMode.Auto : ScrollContainer.ScrollMode.Disabled;
        _perkBody!.AddThemeConstantOverride("separation", draft ? 9 : 6);
        var width = draft ? size.X - 120 : size.X - 20;
        var height = draft ? size.Y - 120 : 220;
        _perkPanel.Position = draft ? new Vector2(60,65) : new Vector2((size.X-width)/2,size.Y - 60 - height);
        _perkPanel.Size = new Vector2(width,height);
    }
    private void LayoutOwnedPerkWorkspace()
    {
        if (_hudWorkspace is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        var height = Math.Min(520, size.Y - 150);
        var constrained = _perkPanel?.Visible == true && _session.CapturePerks()?.Pending == false;
        if(constrained != _ownedWorkspaceConstrained) _ownedWorkspaceLayoutGeneration++;
        if (_ownedWorkspaceConstrained && !constrained && _hudTabs is not null)
        {
            _ownedContextScroll = _hudContextScroll?.ScrollVertical ?? 0;
            _ownedContextSession = _session; _ownedContextIdentity = ContextScrollIdentity;
            for(var i=0;i<_hudTabs.GetChildCount();i++) _ownedWorkspaceScroll[i]=((ScrollContainer)_hudTabs.GetChild(i)).ScrollVertical;
            CaptureOwnedWorkspaceClosedScroll();
        }
        if (constrained)
            height = Math.Min(height, size.Y - 60 - 220 - 10 - _hudWorkspace.Position.Y);
        var bookingPage = _bookingLane is not null && _hudTabs?.CurrentTab == 1;
        if (bookingPage && !constrained) height = Math.Min(660, size.Y - 138);
        _hudWorkspace.Position = bookingPage && !constrained ? new Vector2(16, 72) : new Vector2(15, 77);
        _hudWorkspace.Size = new Vector2(bookingPage && !constrained ? size.X - 32 : size.X >= 1600 ? 690 : 650, height);
        LayoutOwnedContext(constrained);
        if (!_ownedWorkspaceConstrained && constrained && _hudTabs is not null)
        {
            if (_hudContextScroll is not null && ReferenceEquals(_ownedContextSession, _session) && _ownedContextIdentity == ContextScrollIdentity) RestoreOwnedWorkspaceScroll(_hudContextScroll,
                _hudContextScroll.ScrollVertical != _ownedContextClosedScroll ? _hudContextScroll.ScrollVertical : _ownedContextScroll);
            foreach(var (index, offset) in _ownedWorkspaceScroll)
            {
                var tab=(ScrollContainer)_hudTabs.GetChild(index);
                // A clamp from the larger viewport is cosmetic; a player scroll while
                // closed is a new preference and must take precedence over that cache.
                var desired=_ownedWorkspaceClosedScroll.TryGetValue(index,out var closed) && tab.ScrollVertical!=closed ? tab.ScrollVertical : offset;
                RestoreOwnedWorkspaceScroll(tab, desired);
            }
        }
        _ownedWorkspaceConstrained = constrained;
    }
    private void LayoutOwnedContext(bool constrained)
    {
        if (_contextPanel is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        var y = _session.PreparedStatus == PreparationStatus.Preparing ? 77 : _hudProgrammeOpen ? 280 : 99;
        var height = Math.Min(380, size.Y - y - 60);
        if (constrained) height = Math.Min(height, size.Y - 60 - 220 - 10 - y);
        _contextPanel.Position = new Vector2(size.X - 300, y);
        _contextPanel.Size = new Vector2(300, height);
    }
    private async void CaptureOwnedWorkspaceClosedScroll()
    {
        var generation = _ownedWorkspaceLayoutGeneration;
        var session = _session;
        var context = ContextScrollIdentity;
        // Container layout clamps scroll after the refresh's deferred calls. Observe
        // the settled expanded viewport, rather than mistaking that clamp for input.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if(generation != _ownedWorkspaceLayoutGeneration || !ReferenceEquals(session, _session) || _ownedWorkspaceConstrained || _hudTabs is null) return;
        for(var i=0;i<_hudTabs.GetChildCount();i++) _ownedWorkspaceClosedScroll[i]=((ScrollContainer)_hudTabs.GetChild(i)).ScrollVertical;
        if (context == ContextScrollIdentity) _ownedContextClosedScroll = _hudContextScroll?.ScrollVertical ?? 0;
    }
    private async void RestoreOwnedWorkspaceScroll(ScrollContainer tab, int offset)
    {
        var generation = _ownedWorkspaceLayoutGeneration;
        var session = _session;
        var context = ContextScrollIdentity;
        var startingOffset = tab.ScrollVertical;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if(generation == _ownedWorkspaceLayoutGeneration && ReferenceEquals(session, _session) && _ownedWorkspaceConstrained &&
            (tab != _hudContextScroll || context == ContextScrollIdentity) &&
            GodotObject.IsInstanceValid(tab) && tab.ScrollVertical == startingOffset) tab.ScrollVertical=offset;
    }
    private void BuildPerkConfirmation(PerkSnapshot p)
    {
        _perkBody!.AddChild(HudLabel(_pendingPerkSkip ? "Skip this choice?" : _pendingPerkReplacement is null ? "Choose an equipped perk to give up" : "Confirm replacement",26));
        if (_pendingPerkChoice is { } choice)
        {
            _perkBody.AddChild(HudLabel("Gain: " + PerkCatalogue.All.Single(item=>item.Id==choice).Name + "\n" + PerkCatalogue.All.Single(item=>item.Id==choice).Effect,16));
            var row = new GridContainer { Columns = 3 }; _perkBody.AddChild(row);
            foreach(var owned in p.Equipped)
            {
                var option = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; row.AddChild(option);
                option.AddChild(ButtonText(PerkCatalogue.All.Single(item=>item.Id==owned).Name,
                    () => { _pendingPerkReplacement = owned; _perkHudKey = ""; RefreshPerkHud(); }));
                option.AddChild(HudLabel(PerkCatalogue.All.Single(item=>item.Id==owned).Effect, 14));
            }
            if (_pendingPerkReplacement is { } loss)
                _perkBody.AddChild(HudLabel("Lose: " + PerkCatalogue.All.Single(item=>item.Id==loss).Name + "\n" + PerkCatalogue.All.Single(item=>item.Id==loss).Effect + "\nIts effect is removed before preparation. Paid contracts still require payment.",16));
        }
        else _perkBody.AddChild(HudLabel("Give up this draft's perk choice. Keep all five equipped perks unchanged.",16));
        var buttons = new HBoxContainer(); _perkBody.AddChild(buttons);
        if(_pendingPerkSkip || _pendingPerkReplacement is not null)buttons.AddChild(ButtonText(_pendingPerkSkip ? "Skip & continue" : "Replace perk",ConfirmPerkChoice));
        buttons.AddChild(ButtonText(_pendingPerkSkip ? "Back to cards" : "Keep current perks",CancelPerkConfirmation));
    }
    private void ProcessPerkCapture()
    {
        if (_perkCaptureDirectory is null) return;
        _perkCaptureFrame++;
        try
        {
            switch(_perkCaptureFrame)
            {
                case 4: PerkCaptureImage("01-first-draft"); var p=_session.CapturePerks()!; CommitEquipmentAction(new RerollPerksCommand(p.DraftAttempt,p.Cursor)); break;
                case 8: PerkCaptureImage("02-rerolled-draft"); p=_session.CapturePerks()!; BeginPerkChoice(p.Hand[0]); _perksExpanded=true; _selectedPerk=p.Hand[0]; RefreshPerkHud(); break;
                case 12: PerkCaptureImage("03-equipped-detail"); _perksExpanded=false; RefreshPerkHud(); break;
                case 16: PerkCaptureImage("04-equipped-collapsed-preparation"); PerkCaptureFullFixture(); break;
                case 20: PerkCaptureImage("05-five-equipped-draft"); p=_session.CapturePerks()!; BeginPerkChoice(p.Hand[0]); _pendingPerkReplacement=p.Equipped[0];_perkHudKey="";RefreshPerkHud();_perkCapturePureHash=_session.CaptureSnapshot().AuthoritativeHash;break;
                case 24: PerkCaptureImage("06-replacement-loss-benefit");CancelPerkConfirmation();PerkCaptureAssertPure();PreparationSave();p=_session.CapturePerks()!;BeginPerkChoice(p.Hand[0]);_pendingPerkReplacement=p.Equipped[0];PreparationLoad();if(_pendingPerkChoice is not null || _pendingPerkReplacement is not null || _pendingPerkSkip)throw new InvalidOperationException("Successful same-cursor load retained a confirmation.");PerkCaptureAssertPure();GD.Print("PERK_CAPTURE load_same_cursor_confirmation_cancelled=true");break;
                case 28: PerkCaptureImage("07-replacement-cancelled");p=_session.CapturePerks()!;_confirmationDraftAttempt=p.DraftAttempt;_confirmationCursor=p.Cursor;_pendingPerkSkip=true;_perkHudKey="";RefreshPerkHud();break;
                case 32: PerkCaptureImage("08-skip-confirmation");CancelPerkConfirmation();PerkCaptureAssertPure();p=_session.CapturePerks()!;_confirmationDraftAttempt=p.DraftAttempt;_confirmationCursor=p.Cursor;_pendingPerkSkip=true;ConfirmPerkChoice();_perksExpanded=true;RefreshPerkHud();break;
                case 36: PerkCaptureImage("09-skipped-five-kept");PerkCaptureFullFixture();p=_session.CapturePerks()!;BeginPerkChoice(p.Hand[0]);_pendingPerkReplacement="another-round";_perkHudKey="";RefreshPerkHud();break;
                case 40: PerkCaptureImage("10-replace-tap-confirmation");ConfirmPerkChoice();_perksExpanded=true;_selectedPerk=_session.CapturePerks()!.Equipped.Last();RefreshPerkHud();break;
                case 44: PerkCaptureImage("11-replaced-five-detail");_perksExpanded=false;RefreshPerkHud();PerkCaptureFatalFixture();break;
                case 48: PerkCaptureImage("12-fatal-hearing-labelled-fixture");ConfirmHearing(new SpendCouncilFavourCommand());break;
                case 52: PerkCaptureImage("13-favour-confirmation");ApplyHearingDecision();break;
                case 56: PerkCaptureImage("14-saved-favour-retry-draft");p=_session.CapturePerks()!;if(p.DraftAttempt!=2 || !p.Pending || _session.CapturePreparation()!.AcceptedOffers.Length!=0)throw new InvalidOperationException("Actual Favour retry did not produce the saved preparation gate.");GD.Print("PERK_CAPTURE_COMPLETE native=true first_draft=true reroll=true five_slots=true replacement=true cancel_pure=true skip=true favour_retry=true labelled_fixture=full_capacity_and_ignored_intoxication_response");GetTree().Quit();break;
            }
        }catch(Exception error){GD.PrintErr("PERK_CAPTURE_FAILED "+error);GetTree().Quit(2);}
    }
    private void PerkCaptureAssertPure()
    {
        if(_session.CaptureSnapshot().AuthoritativeHash!=_perkCapturePureHash)throw new InvalidOperationException("Cancelled confirmation changed state.");
    }
    private void PerkCaptureFullFixture()
    {
        // Explicit capture-only setup, because R0 has no progression to five choices yet.
        // Commands and all confirmation/save interactions after setup use production paths.
        _session=GameSession.CreatePerkCampaign(20260922);_pendingPerkChoice=null;_pendingPerkReplacement=null;_pendingPerkSkip=false;
        var field=typeof(GameSession).GetField("_perks",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        var p=_session.CapturePerks()!;field.SetValue(_session,p with {Equipped=["another-round","doctors-orders","extra-pair-of-hands","high-pressure"],Pending=true});
        typeof(GameSession).GetMethod("SynchronizePerkEffects",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(_session,[]);
        do{typeof(GameSession).GetMethod("OpenPerkDraft",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(_session,[]);p=_session.CapturePerks()!;}while(!p.Hand.Contains("smooth-operators"));
        CommitEquipmentAction(new ChoosePerkCommand(p.DraftAttempt,p.Cursor,"smooth-operators"));
        CommitEquipmentAction(new PlaceWaterPointCommand(new(80,126)));
        typeof(GameSession).GetMethod("OpenPerkDraft",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(_session,[]);
        _preparationMessage="LABELLED CAPACITY FIXTURE · four owned inserted, fifth chosen normally · saved draft/confirmation commands";_perksExpanded=false;_perkHudKey="";RefreshPreparationHud();
        GD.Print("PERK_CAPTURE_SETUP fixture=capacity inserted_owned=4 production_choice=5 extra_tap=production_placement");
    }
    private void PerkCaptureFatalFixture()
    {
        CommitEquipmentAction(new AcceptPreparationOfferCommand("staff.extra-medic"));
        CommitEquipmentAction(new SetProgrammeCommand(["act.meadow-lanterns","act.barnstorm-circuit","act.field-frequency"]));
        CommitEquipmentAction(new AcceptPreparationOfferCommand("staff.steward"));PreparationStart();
        var prep=_session.CapturePreparation()!;
        typeof(GameSession).GetField("_preparation",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(_session,prep with {People=prep.People.Select(person=>person with {Admitted=true}).ToArray()});
        var immersion=_session.CaptureImmersion()!;var id=prep.People.First(person=>person.Role==ProtectedPersonRole.Guest).AgentId;
        typeof(GameSession).GetField("_immersion",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(_session,immersion with {People=immersion.People.Select(person=>person.AgentId==id?person with {Intoxication=10000}:person).ToArray()});
        _session.AdvanceWithoutSnapshot(4000);
        if(_session.PreparedStatus!=PreparationStatus.Failed)throw new InvalidOperationException("Labelled ignored-response setup failed to reach the existing fatal chain.");
        PreparationSave();_preparationMessage="LABELLED FATAL FIXTURE · all admitted; guest intoxication set to10000;4000ticks with no response · normal Council retry";RefreshPreparationHud();
        GD.Print("PERK_CAPTURE_SETUP fixture=fatal all_admitted=true guest_intoxication=10000 inherited_deadlines_ticks=4000 medic_response=none normal_favour_retry=true");
    }
    private void PerkCaptureImage(string name)
    {
        var image=GetViewport().GetTexture().GetImage();
        if(image.GetWidth()!=GetWindow().Size.X || image.GetHeight()!=GetWindow().Size.Y)throw new InvalidOperationException("Perk capture must be native size.");
        image.SavePng(Path.Combine(_perkCaptureDirectory!,name+".png"));
        if(_perkPanel!.Visible && (_perkPanel.GetGlobalRect().End.X>image.GetWidth() || _perkPanel.GetGlobalRect().End.Y>image.GetHeight()-45))throw new InvalidOperationException("Perk panel exceeds native bounds.");
        if(_perkPanel.Visible && !HudBlocksPlacement(_perkPanel.GetGlobalRect().GetCenter()))throw new InvalidOperationException("Perk overlay does not block background placement.");
        if(_perkPanel.Visible)
        {
            foreach(var button in PerkDescendants(_perkBody!).OfType<Button>().Where(button=>button.Text is "Choose this perk" or "Replace a perk…" or "Skip this choice"))
                if(button.GetGlobalRect().End.Y>_perkPanel.GetGlobalRect().End.Y-15)throw new InvalidOperationException("Primary draft action clipped below panel.");
            foreach(var detail in PerkDescendants(_perkBody!).OfType<VBoxContainer>().Where(box=>box.CustomMinimumSize.X==420))
                if(detail.Size.X<420)throw new InvalidOperationException("Selected perk detail copy is too narrow.");
        }
        var loaded=GameSession.Restore(_session.CapturePersistenceSnapshot());if(!loaded.IsSuccess)throw new InvalidOperationException("Native state failed restoration: "+loaded.Error);
        GD.Print($"PERK_CAPTURE image={name} viewport={image.GetWidth()}x{image.GetHeight()} people={_session.CapturePreparation()!.People.Length} hash={_session.CaptureSnapshot().AuthoritativeHash}");
    }
    private static System.Collections.Generic.IEnumerable<Node> PerkDescendants(Node node)
    {
        foreach(var child in node.GetChildren()){yield return child;foreach(var next in PerkDescendants(child))yield return next;}
    }
}

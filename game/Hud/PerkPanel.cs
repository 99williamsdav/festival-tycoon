using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The perk draft (three cards, reroll, replace or skip with confirmation) and the owned-perk strip.
/// <c>layoutWorkspace</c> lets the HUD shell make room for the strip whenever the panel changes.
/// </summary>
internal sealed class PerkPanel(IHudHost _hud, Action _layoutWorkspace)
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
    private string _perkHudKey = "";
    private bool _perksExpanded;
    private string? _selectedPerk;
    private string? _pendingPerkChoice;
    private string? _pendingPerkReplacement;
    private bool _pendingPerkSkip;
    private int _confirmationDraftAttempt;
    private ulong _confirmationCursor;

    public PanelContainer? Panel => _perkPanel;
    public PanelContainer? EffectPopup => _ownedEffectPopup;
    public Button? Toggle => _perkToggle;
    public bool ConfirmationPending => _pendingPerkChoice is not null || _pendingPerkSkip;

    public void SetExpanded(bool expanded) { _perksExpanded = expanded; _perkHudKey = ""; Refresh(); }
    public void ToggleExpanded() => SetExpanded(!_perksExpanded);

    /// <summary>Forgets view state from the previous campaign.</summary>
    public void Reset() { _perksExpanded = false; _selectedPerk = null; _perkHudKey = ""; }

    public void Build(CanvasLayer layer)
    {
        var size = _hud.Viewport.GetVisibleRect().Size;
        _perkToggle = ButtonText("Your Perks", () => { _perksExpanded = !_perksExpanded; _perkHudKey = ""; Refresh(); });
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
        var title = HudLabel(perk.Name, 25); title.AddThemeFontOverride("font", Ui.SlabBold); title.CustomMinimumSize = new Vector2(216, 66); box.AddChild(title);
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
        var title = HudLabel(perk.Name, 17); title.AddThemeFontOverride("font", Ui.SlabBold);
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
        if (_perkPanel?.Visible != true || _hud.Session.CapturePerks()?.Pending != false) return;
        _ownedEffectTarget = card; _ownedEffectText!.Text = effect;
        _ownedEffectPopup!.Size = new Vector2(350, 90);
        var size = _hud.Viewport.GetVisibleRect().Size;
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
        var p = _hud.Session.CapturePerks()!;
        if (p.Equipped.Length < 5) { _hud.Commit(new ChoosePerkCommand(p.DraftAttempt, p.Cursor, id)); return; }
        _confirmationDraftAttempt=p.DraftAttempt;_confirmationCursor=p.Cursor;
        _pendingPerkChoice = id; _pendingPerkReplacement = null; _pendingPerkSkip = false; _perkHudKey = ""; Refresh();
    }
    public void CancelConfirmation()
    {
        HideOwnedEffect();
        _pendingPerkChoice = null; _pendingPerkReplacement = null; _pendingPerkSkip = false; _perkHudKey = ""; Refresh();
    }
    private void ConfirmPerkChoice()
    {
        var p = _hud.Session.CapturePerks()!;
        SessionCommand command = _pendingPerkSkip ? new SkipPerksCommand(_confirmationDraftAttempt,_confirmationCursor) :
            new ChoosePerkCommand(_confirmationDraftAttempt,_confirmationCursor,_pendingPerkChoice!,_pendingPerkReplacement);
        CancelConfirmation(); _hud.Commit(command);
    }
    public void Refresh()
    {
        if (_perkPanel is null) return;
        var p = _hud.Session.CapturePerks();
        _perkToggle!.Visible = p is { Ended: false };
        if (_hud.Session.PreparedStatus == PreparationStatus.Preparing && p?.Pending == false)
            _perkToggle.Visible = false;
        _perkPanel.Visible = p is { Ended: false } && (p.Pending || _perksExpanded);
        if (!_perkPanel.Visible || p?.Pending != false) HideOwnedEffect();
        _layoutWorkspace();
        if (p is null || p.Ended) return;
        LayoutPerkHud(p.Pending);
        if((_pendingPerkChoice is not null || _pendingPerkSkip) && (!p.Pending || _confirmationDraftAttempt!=p.DraftAttempt || _confirmationCursor!=p.Cursor))
        { _pendingPerkChoice=null;_pendingPerkReplacement=null;_pendingPerkSkip=false; }
        _perkToggle.Text = "Your Perks";
        _perkToggle.TooltipText = $"{p.Equipped.Length} / 5 equipped · {(_perksExpanded ? "Collapse" : "Expand")} upwards";
        var key = System.Text.Json.JsonSerializer.Serialize(p) + (p.Pending && _perksExpanded) + _selectedPerk + _pendingPerkChoice + _pendingPerkReplacement + _pendingPerkSkip + _hud.Message + _hud.Viewport.GetVisibleRect().Size;
        if (key == _perkHudKey) return; _perkHudKey = key;
        var ownedSignature = string.Join("|", p.Equipped);
        if(!p.Pending && ownedSignature == _ownedPerkSignature && GodotObject.IsInstanceValid(_ownedPerkScroll))
            _ownedPerkOffset = _ownedPerkScroll!.ScrollHorizontal;
        else _ownedPerkOffset = 0;
        _ownedPerkSignature = ownedSignature;
        HideOwnedEffect(); ClearPerkChildren(_perkBody!);
        var heading = new HBoxContainer(); _perkBody!.AddChild(heading);
        var title = HudLabel(p.Pending ? "Choose a festival perk" : $"Your Perks · {p.Equipped.Length} / 5 equipped", p.Pending ? 29 : 21); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; title.AddThemeFontOverride("font", Ui.SlabBold); heading.AddChild(title);
        if (p.Pending)
        {
            var reroll = ButtonText(p.RerollUsed ? "Free reroll used" : "Reroll all 3 · 1 free", () => _hud.Commit(new RerollPerksCommand(p.DraftAttempt,p.Cursor)));
            reroll.Disabled = p.RerollUsed || _pendingPerkChoice is not null || _pendingPerkSkip; reroll.TooltipText = "Owned perks excluded; options may repeat"; heading.AddChild(reroll);
        }
        else heading.AddChild(ButtonText("Collapse perks ▴", () => { _perksExpanded = false; Refresh(); }));
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
            footer.AddChild(HudLabel("Three distinct eligible choices, equally likely. Owned perks excluded; options may repeat.\nYour choice joins the next timed save; opening saves it immediately.",13));
            if (p.Equipped.Length == 5) footer.AddChild(ButtonText("Skip this choice", () => { _confirmationDraftAttempt=p.DraftAttempt;_confirmationCursor=p.Cursor;_pendingPerkSkip = true; _perkHudKey = ""; Refresh(); }));
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
        var owner = _hud.Session;
        Callable.From(() =>
        {
            if (ReferenceEquals(owner, _hud.Session) && _hud.Session.CapturePerks()?.Pending == false &&
                _perkPanel is not null && GodotObject.IsInstanceValid(_perkPanel))
                _perkPanel.Size = new Vector2(_hud.Viewport.GetVisibleRect().Size.X - 20, 220);
        }).CallDeferred();
        _ownedPerkScroll.SetDeferred(ScrollContainer.PropertyName.ScrollHorizontal, _ownedPerkOffset);
    }
    private void LayoutPerkHud(bool draft)
    {
        var size = _hud.Viewport.GetVisibleRect().Size;
        _perkToggle!.Position = new Vector2((size.X - 90) / 2, size.Y - 50);
        _perkToggle.CustomMinimumSize = new Vector2(90, 38);
        _perkToggle.AddThemeFontSizeOverride("font_size", 12);
        _perkPanel!.AddThemeStyleboxOverride("panel", HudStyle(new Color("d8d6bd"), draft ? 22 : 10));
        _perkScroll!.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _perkScroll.VerticalScrollMode = draft ? ScrollContainer.ScrollMode.Auto : ScrollContainer.ScrollMode.Disabled;
        _perkBody!.AddThemeConstantOverride("separation", draft ? 9 : 6);
        var width = draft ? size.X - 120 : size.X - 20;
        var height = draft ? size.Y - Ui.TopBar - 63 : 220;
        _perkPanel.Position = draft ? new Vector2(60, Ui.TopBar + 8) : new Vector2((size.X-width)/2,
            size.Y - (_hud.Session.PreparedStatus == PreparationStatus.Preparing ? Ui.Dock + 8 : 60) - height);
        _perkPanel.Size = new Vector2(width,height);
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
                    () => { _pendingPerkReplacement = owned; _perkHudKey = ""; Refresh(); }));
                option.AddChild(HudLabel(PerkCatalogue.All.Single(item=>item.Id==owned).Effect, 14));
            }
            if (_pendingPerkReplacement is { } loss)
                _perkBody.AddChild(HudLabel("Lose: " + PerkCatalogue.All.Single(item=>item.Id==loss).Name + "\n" + PerkCatalogue.All.Single(item=>item.Id==loss).Effect + "\nIts effect is removed before preparation. Paid contracts still require payment.",16));
        }
        else _perkBody.AddChild(HudLabel("Give up this draft's perk choice. Keep all five equipped perks unchanged.",16));
        var buttons = new HBoxContainer(); _perkBody.AddChild(buttons);
        if(_pendingPerkSkip || _pendingPerkReplacement is not null)buttons.AddChild(ButtonText(_pendingPerkSkip ? "Skip & continue" : "Replace perk",ConfirmPerkChoice));
        buttons.AddChild(ButtonText(_pendingPerkSkip ? "Back to cards" : "Keep current perks",CancelConfirmation));
    }
    private static System.Collections.Generic.IEnumerable<Node> PerkDescendants(Node node)
    {
        foreach(var child in node.GetChildren()){yield return child;foreach(var next in PerkDescendants(child))yield return next;}
    }
}

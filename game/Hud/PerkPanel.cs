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
    private PanelContainer PerkCard(string id, Button choose)
    {
        var perk = PerkCatalogue.All.Single(item => item.Id == id);
        var card = new PanelContainer { CustomMinimumSize = new Vector2(Ui.S(300), 0), ClipContents = true };
        card.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Paper, 10, shadow: 16, shadowAlpha: 0.45f));
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 0); card.AddChild(box);
        var art = PerkArtwork(id, Ui.S(300)); art.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered; art.CustomMinimumSize = Ui.S(300, 200);
        box.AddChild(art);
        var body = new MarginContainer();
        foreach (var (side, value) in new[] { ("margin_left", 18f), ("margin_right", 18f), ("margin_top", 14f), ("margin_bottom", 18f) })
            body.AddThemeConstantOverride(side, Ui.Px(value));
        box.AddChild(body);
        var words = new VBoxContainer(); words.AddThemeConstantOverride("separation", Ui.Px(8)); body.AddChild(words);
        var tag = Ui.Caps("Festival perk", Ui.TealDeep, 9.5f); tag.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        tag.AddThemeStyleboxOverride("normal", Ui.Box(Ui.TealWash, 4, padX: 7, padY: 3)); words.AddChild(tag);
        var title = Ui.Heading(perk.Name, 25); title.AutowrapMode = TextServer.AutowrapMode.WordSmart; words.AddChild(title);
        var effect = Ui.Text(perk.Effect, 14.5f, new Color("3a4640")); effect.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        effect.CustomMinimumSize = new Vector2(0, Ui.S(60)); words.AddChild(effect);
        words.AddChild(choose);
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
        TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
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
        // The top bar (preparation) and the live bar own the Perks button now.
        _perkToggle!.Visible = false;
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
        if (_pendingPerkChoice is not null || _pendingPerkSkip) { BuildPerkConfirmation(p); return; }
        if (p.Pending) { BuildDraft(p); return; }
        var heading = new HBoxContainer(); _perkBody!.AddChild(heading);
        var title = Ui.Heading($"Your Perks · {p.Equipped.Length} / 5 equipped", 21); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; heading.AddChild(title);
        heading.AddChild(ButtonText("Collapse perks ▴", () => { _perksExpanded = false; Refresh(); }));
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
                empty.AddThemeStyleboxOverride("panel", Ui.Box(new Color(0, 0, 0, 0), 8, Ui.PaperEdge, 1.5f, 12, 12));
                var words = Ui.Text($"Slot {i+1}\nEmpty", 16, Ui.InkMuted); words.HorizontalAlignment = HorizontalAlignment.Center; words.VerticalAlignment = VerticalAlignment.Center;
                empty.AddChild(words); slot.AddChild(empty);
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
        _perkPanel!.AddThemeStyleboxOverride("panel", draft ? Ui.Box(new Color(Ui.BarDeep, 0.74f), 0) : Ui.Sheet(14, 10));
        _perkScroll!.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _perkScroll.VerticalScrollMode = draft ? ScrollContainer.ScrollMode.Auto : ScrollContainer.ScrollMode.Disabled;
        _perkBody!.AddThemeConstantOverride("separation", draft ? 9 : 6);
        var width = draft ? size.X : size.X - 20;
        var height = draft ? size.Y - Ui.TopBar : 220;
        _perkPanel.Position = draft ? new Vector2(0, Ui.TopBar) : new Vector2((size.X-width)/2,
            size.Y - (_hud.Session.PreparedStatus == PreparationStatus.Preparing ? Ui.Dock + 8 : Ui.S(64) + 8) - height);
        _perkPanel.Size = new Vector2(width,height);
    }
    /// <summary>The draft: heading with reroll, three cards, and the equipped slots.</summary>
    private void BuildDraft(PerkSnapshot p)
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(Ui.S(956), 0), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        column.AddThemeConstantOverride("separation", 0);
        _perkBody!.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(24)) });
        _perkBody.AddChild(column);
        var heading = new HBoxContainer(); column.AddChild(heading);
        var words = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; words.AddThemeConstantOverride("separation", Ui.Px(4)); heading.AddChild(words);
        words.AddChild(Ui.Caps("Before you build", Ui.Gold));
        words.AddChild(Ui.Heading("Choose a festival perk", 36, Ui.BarText));
        words.AddChild(Ui.Text("It stays equipped across retries. Three distinct choices, equally likely.", 14.5f, Ui.BarMuted));
        var reroll = new Button { MouseDefaultCursorShape = Control.CursorShape.PointingHand, SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
            Disabled = p.RerollUsed, TooltipText = "Owned perks excluded; options may repeat" };
        reroll.Pressed += () => _hud.Commit(new RerollPerksCommand(p.DraftAttempt, p.Cursor));
        var outline = Ui.Box(new Color(0, 0, 0, 0), 8, Ui.Gold, 1.5f);
        foreach (var state in new[] { "normal", "pressed", "focus" }) reroll.AddThemeStyleboxOverride(state, outline);
        reroll.AddThemeStyleboxOverride("hover", Ui.Box(new Color(Ui.Gold, 0.12f), 8, Ui.Gold, 1.5f));
        reroll.AddThemeStyleboxOverride("disabled", Ui.Box(new Color(0, 0, 0, 0), 8, Ui.BarLine, 1.5f));
        var face = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        face.SetAnchorsPreset(Control.LayoutPreset.FullRect); face.AddThemeConstantOverride("separation", Ui.Px(8));
        var spin = Ui.IconRect("refresh-cw", 18, p.RerollUsed ? Ui.BarMuted : Ui.Gold); spin.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; face.AddChild(spin);
        var label = Ui.Text(p.RerollUsed ? "Free reroll used" : "Reroll all three", 14.5f, p.RerollUsed ? Ui.BarMuted : Ui.BarText, Ui.BodyBold);
        label.VerticalAlignment = VerticalAlignment.Center; face.AddChild(label);
        if (!p.RerollUsed)
        {
            var free = Ui.Text("1 free", 12, Ui.Bar, Ui.BodyBold); free.AddThemeStyleboxOverride("normal", Ui.Box(Ui.Gold, 4, padX: 6, padY: 1));
            free.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; face.AddChild(free);
        }
        reroll.AddChild(face);
        reroll.CustomMinimumSize = new Vector2(face.GetCombinedMinimumSize().X + Ui.S(32), Ui.S(42));
        heading.AddChild(reroll);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(26)) });
        var hand = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; hand.AddThemeConstantOverride("separation", Ui.Px(28)); column.AddChild(hand);
        foreach (var id in p.Hand)
        {
            var choose = Ui.Style(new Button { Text = p.Equipped.Length == 5 ? "Replace a perk…" : "Take this perk", Name = "ChooseDraftPerk_" + id,
                CustomMinimumSize = new Vector2(0, Ui.S(44)), MouseDefaultCursorShape = Control.CursorShape.PointingHand }, Ui.ButtonKind.Primary, 15, 8);
            var quiet = Ui.Box(new Color(0, 0, 0, 0), 8, Ui.Bar, 1.5f, 10, 4);
            foreach (var state in new[] { "normal", "focus" }) choose.AddThemeStyleboxOverride(state, quiet);
            foreach (var state in new[] { "font_color", "font_focus_color" }) choose.AddThemeColorOverride(state, Ui.Bar);
            var perkId = id; choose.Pressed += () => BeginPerkChoice(perkId);
            var card = PerkCard(id, choose); card.Name = "DraftPerkCard_" + id; card.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            hand.AddChild(card);
        }
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(28)) });
        var equipped = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; equipped.AddThemeConstantOverride("separation", Ui.Px(14)); column.AddChild(equipped);
        var caption = Ui.Caps("Equipped", Ui.BarMuted); caption.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; equipped.AddChild(caption);
        var slots = new HBoxContainer(); slots.AddThemeConstantOverride("separation", Ui.Px(8)); equipped.AddChild(slots);
        for (var i = 0; i < 5; i++)
        {
            var slot = new PanelContainer { CustomMinimumSize = Ui.S(34, 34), ClipContents = true };
            if (i < p.Equipped.Length)
            {
                slot.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Gold, 8, Ui.Gold, 1.5f));
                var art = PerkArtwork(p.Equipped[i], Ui.S(34)); art.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered; art.CustomMinimumSize = Ui.S(34, 34);
                slot.AddChild(art); slot.TooltipText = PerkCatalogue.All.Single(item => item.Id == p.Equipped[i]).Name;
            }
            else slot.AddThemeStyleboxOverride("panel", Ui.Box(new Color(0, 0, 0, 0), 8, new Color(Ui.Gold, 0.6f), 1.5f));
            slots.AddChild(slot);
        }
        var note = Ui.Text($"{p.Equipped.Length} of 5 · your choice joins the next timed save; opening saves it immediately", 13.5f, Ui.BarMuted);
        note.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; equipped.AddChild(note);
        if (p.Equipped.Length == 5)
        {
            var skip = ButtonText("Skip this choice", () => { _confirmationDraftAttempt=p.DraftAttempt;_confirmationCursor=p.Cursor;_pendingPerkSkip = true; _perkHudKey = ""; Refresh(); });
            equipped.AddChild(skip);
        }
    }

    private void BuildPerkConfirmation(PerkSnapshot p)
    {
        if (p.Pending)
        {
            // On the dark draft scrim, the confirmation sits on its own paper sheet.
            var sheet = new PanelContainer { CustomMinimumSize = new Vector2(Ui.S(760), 0), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
            sheet.AddThemeStyleboxOverride("panel", Ui.Sheet(24, 22));
            var inner = new VBoxContainer(); inner.AddThemeConstantOverride("separation", 9); sheet.AddChild(inner);
            _perkBody!.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(40)) });
            _perkBody.AddChild(sheet);
            var outer = _perkBody; _perkBody = inner;
            try { BuildPerkConfirmation(p with { Pending = false }); } finally { _perkBody = outer; }
            return;
        }
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

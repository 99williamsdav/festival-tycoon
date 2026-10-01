using Festival.Simulation;
using Godot;
using System;

namespace Festival.Game;

internal enum OfferState { Available, Chosen, Locked }

/// <summary>How an offer card is laid out: a person card in a grid, a person row in a list, or a radio choice.</summary>
internal enum OfferLayout { Card, Row, Choice }

/// <summary>
/// One preparation offer as the redesign shows it. Cards and rows have an avatar initial, a name, a
/// detail line, the price and a Choose/Hire button; choices are radio cards that are clicked whole.
/// </summary>
internal sealed class OfferCard
{
    private readonly OfferLayout _layout;
    private readonly Color _avatarColour;
    private readonly PanelContainer? _frame;
    private readonly Label _title;
    private readonly Label _detail;
    private readonly TextureRect? _detailIcon;
    private readonly Label _price;
    private readonly Label? _ribbon;
    private readonly PanelContainer? _avatar;
    private readonly Label? _initial;
    private readonly Panel? _radio;
    private readonly RatingBars[] _stats = [];
    private readonly HFlowContainer? _tags;

    public Control Root { get; }
    /// <summary>What the player presses: the button on cards and rows, the whole card for choices.</summary>
    public Button Action { get; }

    /// <param name="stats">Labels for 1-5 ability bars shown under the detail line; set them with <see cref="SetStats"/>.</param>
    public OfferCard(OfferLayout layout, Color avatarColour, string detailIcon, Action press, string[]? stats = null)
    {
        _layout = layout; _avatarColour = avatarColour;
        _title = Ui.Text("", layout == OfferLayout.Row ? 15 : 15.5f, Ui.Ink, Ui.BodyBold);
        _detail = Ui.Text("", 13, Ui.InkMuted);
        _detail.AutowrapMode = TextServer.AutowrapMode.WordSmart; _detail.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _price = Ui.Heading("", layout == OfferLayout.Row ? 18 : 20);
        if (layout != OfferLayout.Row) _ribbon = Ui.Caps("", Colors.White, 9.5f);
        if (_ribbon is not null)
        {
            _ribbon.AddThemeStyleboxOverride("normal", Ui.Box(Ui.Teal, 4, padX: 8, padY: 3));
            _ribbon.MouseFilter = Control.MouseFilterEnum.Ignore;
            _ribbon.AnchorLeft = _ribbon.AnchorRight = 1;
            _ribbon.OffsetRight = _ribbon.OffsetLeft = -Ui.S(12); _ribbon.OffsetTop = -Ui.S(10);
            _ribbon.GrowHorizontal = Control.GrowDirection.Begin;
        }
        var words = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        words.AddThemeConstantOverride("separation", Ui.Px(2));
        words.AddChild(_title);
        if (stats is { Length: > 0 })
        {
            _tags = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            _tags.AddThemeConstantOverride("h_separation", Ui.Px(4)); _tags.AddThemeConstantOverride("v_separation", Ui.Px(4));
            words.AddChild(_tags);
        }
        var detailLine = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; detailLine.AddThemeConstantOverride("separation", Ui.Px(5));
        if (layout != OfferLayout.Choice && detailIcon != "")
        {
            _detailIcon = Ui.IconRect(detailIcon, 14, Ui.InkMuted); _detailIcon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            detailLine.AddChild(_detailIcon);
        }
        detailLine.AddChild(_detail); words.AddChild(detailLine);
        if (stats is { Length: > 0 })
        {
            var grid = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
            grid.AddThemeConstantOverride("h_separation", Ui.Px(8)); grid.AddThemeConstantOverride("v_separation", Ui.Px(3));
            _stats = new RatingBars[stats.Length];
            for (var i = 0; i < stats.Length; i++)
            {
                var label = Ui.Caps(stats[i], Ui.InkMuted, 9.5f); label.MouseFilter = Control.MouseFilterEnum.Ignore;
                label.CustomMinimumSize = new Vector2(Ui.S(66), 0);
                grid.AddChild(label);
                _stats[i] = new RatingBars { Ink = Ui.TealDeep, MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
                grid.AddChild(_stats[i]);
            }
            words.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(4)), MouseFilter = Control.MouseFilterEnum.Ignore });
            words.AddChild(grid);
        }

        if (layout == OfferLayout.Choice)
        {
            var button = new Button { CustomMinimumSize = new Vector2(0, Ui.S(64)), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            button.Pressed += press;
            var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            line.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            line.OffsetLeft = Ui.S(14); line.OffsetRight = -Ui.S(14);
            line.AddThemeConstantOverride("separation", Ui.Px(12)); button.AddChild(line);
            _radio = new Panel { CustomMinimumSize = Ui.S(20, 20), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
            line.AddChild(_radio); line.AddChild(words);
            _price.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; _price.MouseFilter = Control.MouseFilterEnum.Ignore; line.AddChild(_price);
            var overlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore }; overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            overlay.AddChild(_ribbon!); button.AddChild(overlay);
            Root = Action = button;
            return;
        }

        _frame = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Root = _frame;
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Ui.Px(12)); _frame.AddChild(row);
        _avatar = new PanelContainer { CustomMinimumSize = Ui.S(layout == OfferLayout.Row ? 40 : 44, layout == OfferLayout.Row ? 40 : 44),
            SizeFlagsVertical = layout == OfferLayout.Row ? Control.SizeFlags.ShrinkCenter : Control.SizeFlags.ShrinkBegin };
        _initial = Ui.Heading("", layout == OfferLayout.Row ? 18 : 19, Colors.White);
        _initial.HorizontalAlignment = HorizontalAlignment.Center; _initial.VerticalAlignment = VerticalAlignment.Center;
        _avatar.AddChild(_initial); row.AddChild(_avatar);
        Action = Ui.Style(new Button { CustomMinimumSize = new Vector2(layout == OfferLayout.Row ? Ui.S(84) : 0, Ui.S(34)),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseDefaultCursorShape = Control.CursorShape.PointingHand }, Ui.ButtonKind.Accent, 13.5f);
        Action.AddThemeConstantOverride("icon_max_width", Ui.Px(15));
        Action.Pressed += press;
        if (layout == OfferLayout.Row)
        {
            row.AddChild(words);
            _price.CustomMinimumSize = new Vector2(Ui.S(48), 0); _price.HorizontalAlignment = HorizontalAlignment.Right;
            _price.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(_price);
            row.AddChild(Action);
        }
        else
        {
            row.AddChild(words);
            words.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(8)) });
            var foot = new HBoxContainer(); words.AddChild(foot);
            _price.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; foot.AddChild(_price); foot.AddChild(Action);
            // The overlay fills the card's padded content box; the ribbon sits on the frame's top edge.
            var overlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            _ribbon!.OffsetTop = -Ui.S(24); _ribbon.OffsetLeft = _ribbon.OffsetRight = Ui.S(2);
            overlay.AddChild(_ribbon); _frame.AddChild(overlay);
        }
    }

    public void Show(string initial, string title, string detail, long pricePennies, OfferState state, string chooseWord, string chosenWord, string tooltip, string lockedWord = "Locked")
    {
        _title.Text = title; _detail.Text = detail; _price.Text = FestivalCurrency.Format(pricePennies);
        Root.TooltipText = tooltip; Action.TooltipText = tooltip;
        var chosen = state == OfferState.Chosen; var locked = state == OfferState.Locked;
        if (_ribbon is not null) { _ribbon.Text = chosenWord.ToUpperInvariant(); _ribbon.Visible = chosen; }
        var muted = new Color("b7ac93");
        _title.AddThemeColorOverride("font_color", locked ? Ui.InkMuted : Ui.Ink);
        _price.AddThemeColorOverride("font_color", locked ? new Color("8a8a7e") : Ui.Ink);
        if (_layout == OfferLayout.Choice)
        {
            var face = chosen ? Ui.Box(Ui.PaperBright, 8, Ui.GoldShadow, 2) : Ui.Box(Ui.GoldWash, 8, Ui.PaperRule, 1);
            foreach (var name in new[] { "normal", "pressed", "focus", "disabled" }) Action.AddThemeStyleboxOverride(name, face);
            Action.AddThemeStyleboxOverride("hover", chosen ? face : Ui.Box(new Color("fff9ec"), 8, Ui.PaperEdge, 1));
            _radio!.AddThemeStyleboxOverride("panel", chosen ? Ui.Box(new Color(0, 0, 0, 0), 10, Ui.Teal, 6) : Ui.Box(new Color(0, 0, 0, 0), 10, new Color("9db5ae"), 2));
            return;
        }
        _initial!.Text = initial;
        _avatar!.AddThemeStyleboxOverride("panel", Ui.Box(locked ? muted : _avatarColour, 22));
        if (_detailIcon is not null) _detailIcon.Visible = !locked;
        _frame!.AddThemeStyleboxOverride("panel", _layout == OfferLayout.Row ? RowStyle()
            : chosen ? Ui.Box(Ui.PaperBright, 8, Ui.GoldShadow, 2, 14, 14) : Ui.Box(Ui.GoldWash, 8, Ui.PaperRule, 1, 14, 14));
        Action.Text = locked ? lockedWord : chosen ? chosenWord : chooseWord;
        Action.Icon = locked ? Ui.Icon("lock") : chosen ? Ui.Icon("check") : null;
        if (locked)
        {
            var dashed = Ui.Box(new Color(0, 0, 0, 0), 6, muted, 1);
            foreach (var name in new[] { "normal", "hover", "pressed", "focus", "disabled" }) Action.AddThemeStyleboxOverride(name, dashed);
            Action.AddThemeColorOverride("font_disabled_color", new Color("6b6a5e")); Action.AddThemeColorOverride("icon_disabled_color", new Color("6b6a5e"));
        }
        else if (chosen || _layout == OfferLayout.Row) Ui.Style(Action, Ui.ButtonKind.Accent, 13.5f);
        else
        {
            Ui.Style(Action, Ui.ButtonKind.Quiet, 13.5f);
            var outline = Ui.Box(new Color(0, 0, 0, 0), 6, Ui.Teal, 1.5f, 14, 4);
            foreach (var name in new[] { "normal", "pressed", "focus" }) Action.AddThemeStyleboxOverride(name, outline);
            Action.AddThemeStyleboxOverride("hover", Ui.Box(Ui.TealWash, 6, Ui.Teal, 1.5f, 14, 4));
            foreach (var name in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) Action.AddThemeColorOverride(name, Ui.TealDeep);
        }
        Action.AddThemeConstantOverride("icon_max_width", Ui.Px(15));
    }

    private string _tagKey = "";

    /// <summary>Small chips under the name: teal for a good trait, amber for a bad one.</summary>
    public void SetTags((string Text, bool Good)[] tags)
    {
        if (_tags is null) return;
        var key = string.Join("|", tags.Select(tag => tag.Text + tag.Good));
        if (key == _tagKey) return;
        _tagKey = key;
        foreach (var child in _tags.GetChildren()) { _tags.RemoveChild(child); child.QueueFree(); }
        foreach (var (text, good) in tags)
        {
            var chip = Ui.Caps(text, good ? Ui.TealDeep : new Color("8a5a12"), 9.5f);
            chip.AddThemeStyleboxOverride("normal", Ui.Box(good ? Ui.TealWash : new Color("f6e2b8"), 4, padX: 6, padY: 2));
            chip.MouseFilter = Control.MouseFilterEnum.Ignore;
            _tags.AddChild(chip);
        }
        _tags.Visible = tags.Length > 0;
    }

    /// <summary>Ability ratings from 1 to 5, in the order of the labels given at construction.</summary>
    public void SetStats(int[] ratings)
    {
        for (var i = 0; i < _stats.Length && i < ratings.Length; i++) _stats[i].Score = ratings[i] * 20;
    }

    private static StyleBoxFlat RowStyle()
    {
        var box = Ui.Box(new Color(0, 0, 0, 0), 0, padY: 10); box.BorderColor = Ui.PaperRule; box.BorderWidthBottom = 1;
        return box;
    }
}

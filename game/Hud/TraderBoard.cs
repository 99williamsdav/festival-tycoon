using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The trader flyer board (assets/source/ui/food-van-traders-v1/INTEGRATION.md): one tab per placed food van, and a flyer
/// per trader with their van, strapline, what they pay, four pip meters and "vs current" chips. Clicking a flyer books
/// that trader for the open van straight away; the board stays open so another van can be checked. Opened by clicking a
/// van on the field, its line in the Build drawer, or by placing a new van; closed by Done, × or Esc.
/// Every measurement is a 1280-wide mockup unit through <see cref="Ui.S(float)"/>.
/// </summary>
internal sealed class TraderBoard(IHudHost _hud, Action _changed)
{
    private const float CardWidth = 182, CardHeight = 292, CardGap = 12;
    private static readonly Color LockedCard = new("eee6d3"), LockedPip = new("b9c4b8"), OpenLine = new("fbf0d4");

    private Control? _root;
    private ColorRect? _dim;
    private Control? _cards;
    private HBoxContainer? _tabs;
    private ScrollContainer? _tabScroll;
    private Label? _title;
    private string _key = "";
    private (string Van, string TraderId)? _thump;
    private static ShaderMaterial? _grey;

    /// <summary>The van whose tab is open, or null while the board is shut.</summary>
    public string? Van { get; private set; }
    public bool IsOpen => Van is not null;
    /// <summary>The dimmed area and board, for the HUD's "is the mouse over UI" checks.</summary>
    public Control? Panel => _dim;

    private enum Look { Normal, Booked, TierLocked, Elsewhere, ComingSoon, Fixed }

    public void Build(CanvasLayer layer)
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, Name = "TraderBoard", Theme = HudTheme() };
        layer.AddChild(_root);
        // The map dims; the Build drawer to its left stays readable.
        _root.AddChild(_dim = new ColorRect { Color = new Color(Ui.BarDeep, 0.59f), Position = Ui.S(456, 60), Size = Ui.S(824, 552), MouseFilter = Control.MouseFilterEnum.Stop });
        var panel = new Panel { Position = Ui.S(466, 74), Size = Ui.S(796, 502), MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", Shadowed(Ui.Paper, 10, null, 0, 0.55f, 8));
        _root.AddChild(panel);
        panel.AddChild(At(Ui.Caps("Who's pitching?", Ui.InkMuted, 10), 20, 16));
        _title = At(Ui.Text("", 22, Ui.Ink, Ui.Slab), 20, 30); panel.AddChild(_title);

        var close = new Button { Name = "TraderBoardClose", Icon = Ui.Icon("x"), ExpandIcon = true, IconAlignment = HorizontalAlignment.Center, TooltipText = "Close (Esc)",
            Position = Ui.S(756, 16), Size = Ui.S(24, 24), CustomMinimumSize = Ui.S(24, 24), MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        close.AddThemeStyleboxOverride("normal", Ui.Box(new Color(0, 0, 0, 0), 12, Ui.PaperRule, 1, 5, 5));
        close.AddThemeStyleboxOverride("hover", Ui.Box(Ui.PaperBright, 12, Ui.PaperRule, 1, 5, 5));
        close.AddThemeStyleboxOverride("pressed", Ui.Box(Ui.PaperRule, 12, Ui.PaperRule, 1, 5, 5));
        close.AddThemeStyleboxOverride("focus", Ui.Box(new Color(0, 0, 0, 0), 12, Ui.Gold, 2, 5, 5));
        foreach (var state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" }) close.AddThemeColorOverride(state, Ui.InkMuted);
        close.AddThemeConstantOverride("icon_max_width", Ui.Px(14));
        close.Pressed += Close; panel.AddChild(close);

        // Tabs scroll sideways with the wheel once there are more vans than fit (Classic).
        _tabScroll = new ScrollContainer { Position = Ui.S(20, 64), Size = Ui.S(756, 24), VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever, FollowFocus = true };
        _tabScroll.GuiInput += input =>
        {
            if (input is not InputEventMouseButton { Pressed: true } wheel || _tabScroll is null) return;
            var step = wheel.ButtonIndex switch { MouseButton.WheelUp or MouseButton.WheelLeft => -1, MouseButton.WheelDown or MouseButton.WheelRight => 1, _ => 0 };
            if (step == 0) return;
            _tabScroll.ScrollHorizontal += step * Ui.Px(60); _tabScroll.AcceptEvent();
        };
        panel.AddChild(_tabScroll);
        _tabs = new HBoxContainer(); _tabs.AddThemeConstantOverride("separation", Ui.Px(6)); _tabScroll.AddChild(_tabs);

        _cards = new Control { Position = Ui.S(20, 104), Size = Ui.S(756, CardHeight), MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddChild(_cards);
        panel.AddChild(At(Ui.Text("Click a flyer to book it for this van · it pays you at Start · swap any time before then", 11.5f, Ui.InkMuted, Ui.BodySemi), 20, 468));
        var done = Ui.Style(new Button { Name = "TraderBoardDone", Text = "Done", Position = Ui.S(656, 458), Size = Ui.S(120, 30), CustomMinimumSize = Ui.S(120, 30),
            MouseDefaultCursorShape = Control.CursorShape.PointingHand }, Ui.ButtonKind.Primary, 14, 8);
        done.Pressed += Close; panel.AddChild(done);
    }

    /// <summary>Opens the board on a van's tab, or switches to it if open.</summary>
    public void Open(string van)
    {
        if (_root is null) return;
        Van = van; _key = "";
        _root.GetParent()?.MoveChild(_root, -1);
        _changed();
        FocusBooked();
    }

    public void Close()
    {
        if (Van is null) return;
        Van = null; _key = "";
        if (_root is not null) _root.Visible = false;
        _changed();
    }

    /// <summary>Shuts the board without asking the HUD to refresh (the HUD is the one shutting it).</summary>
    public void Dismiss()
    {
        Van = null; _key = "";
        if (_root is not null) _root.Visible = false;
    }

    /// <summary>Re-reads the session. <paramref name="allowed"/> is false while anything else owns the screen (placing, perks, the briefing).</summary>
    public void Refresh(bool allowed)
    {
        if (_root is null || _cards is null || _tabs is null || _title is null) return;
        var session = _hud.Session;
        var placed = session.PlacedVans;
        if (Van is not null && !placed.Contains(Van)) Van = placed.FirstOrDefault();
        _root.Visible = allowed && Van is not null;
        if (!_root.Visible || Van is not { } van) return;
        var tier = session.CapturePreparation()?.Tier ?? 1;
        var open = session.CanChooseFoodTraders;
        var key = $"{van}|{tier}|{session.IsClassicCampaign}|{open}|" + string.Join(",", placed.Select(item => item + "=" + session.TraderAt(item).Id)) + "|" +
            string.Join(",", FoodTraders.All.Select(trader => session.TraderUnavailable(van, trader) ?? "ok"));
        if (key == _key) return;
        _key = key;
        var focused = _root.GetViewport()?.GuiGetFocusOwner();
        var focusedName = focused is not null && _root.IsAncestorOf(focused) ? focused.Name.ToString() : null;

        _title.Text = $"Van {Stalls.Number(van)}{Landmark(van) switch { { } near => " · by " + near, _ => "" }}";
        Clear(_tabs);
        foreach (var item in placed) _tabs.AddChild(Tab(item, session.TraderAt(item), item == van));

        Clear(_cards);
        var current = session.TraderAt(van);
        var count = FoodTraders.All.Length + 1;
        var width = count <= 4 ? CardWidth : Math.Max(150, (756 - CardGap * (count - 1)) / count);
        var index = 0;
        foreach (var trader in FoodTraders.All)
        {
            var issue = trader.Id == current.Id ? null : session.TraderUnavailable(van, trader);
            var elsewhere = issue is null || trader.FromTier > tier ? null : session.TraderBookedElsewhere(van, trader);
            // Once the plan is paid at Start the traders are fixed: every other flyer locks.
            var look = trader.Id == current.Id ? Look.Booked : trader.FromTier > tier ? Look.TierLocked : elsewhere is not null ? Look.Elsewhere
                : open ? Look.Normal : Look.Fixed;
            if (look == Look.Fixed) issue = "Traders are fixed once the festival's plan is paid at Start.";
            var sharing = placed.Count(other => other != van && session.TraderAt(other).Product == trader.Product);
            var note = look is Look.Normal or Look.Booked ? TraderFlyers.SameCuisineNote(trader, sharing) : null;
            var card = Card(width, trader.Name, trader.Menu, trader.Strapline, new Color(trader.CardColour), trader.Art,
                $"pays you {FestivalCurrency.Format(trader.PitchFeePennies)}", "£" + (trader.PricePennies / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " a portion", TraderFlyers.Pips(trader), look,
                // No room for a third chip above the note line.
                TraderFlyers.Compare(trader, current, note is null ? 3 : 2), note, look == Look.TierLocked ? $"From tier {trader.FromTier}" : null, elsewhere, van);
            card.Name = "TraderCard_" + trader.Art;
            card.TooltipText = issue ?? trader.Blurb;
            var chosen = trader;
            // Booked after the click has finished: booking rebuilds the flyers, this one included.
            if (look == Look.Normal) card.Pressed += () => Callable.From(() => Book(van, chosen)).CallDeferred();
            Place(card, index++, width);
            if (_thump is { } thump && thump.Van == van && thump.TraderId == trader.Id && look == Look.Booked) Thump(card);
        }
        var coffee = TraderFlyers.BrewFighters;
        var soon = Card(width, coffee.Name, coffee.Menu, coffee.Strapline, new Color(coffee.CardColour), coffee.Art, "pays you —", "— a portion", null, Look.ComingSoon,
            [], null, coffee.Tape, null, van);
        soon.Name = "TraderCard_" + coffee.Art;
        soon.TooltipText = $"{coffee.Name}: coffee is coming to Classic later.";
        Place(soon, index, width);
        _thump = null;

        if (focusedName is not null && _root.FindChild(focusedName, true, false) is Control again && again.FocusMode != Control.FocusModeEnum.None) again.GrabFocus();
    }

    /// <summary>Puts keyboard focus on the booked flyer, so arrows and Enter work straight away.</summary>
    private void FocusBooked()
    {
        if (_cards is null || !_root!.Visible) return;
        var booked = _cards.GetChildren().OfType<Button>().FirstOrDefault(card => card.HasMeta("booked")) ?? _cards.GetChildren().OfType<Button>().FirstOrDefault();
        booked?.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void Book(string van, FoodTrader trader)
    {
        _thump = (van, trader.Id);
        _hud.Commit(new ChooseFoodTraderCommand(trader.Id, van));
        Refresh(true);
        // Keep focus on the flyer just booked.
        if (_cards?.FindChild("TraderCard_" + trader.Art, false, false) is Control card) card.GrabFocus();
    }

    /// <summary>"the Pond Stage bar": the nearest stage or bar to the van, as the game names them.</summary>
    private string? Landmark(string van)
    {
        var session = _hud.Session;
        var placements = session.CaptureBuildPlacements();
        if (placements.FirstOrDefault(item => item.Id == van) is not { } here) return null;
        var marks = session.Stages.Select(stage => (Name: stage.Id == FestivalStages.MainId ? "the main stage" : "the " + stage.ShortName, Cell: stage.PerformerMarks[0]))
            .Concat(placements.Where(item => item.Kind == BuildServiceKind.Bar).Select(item => (Name: "the " + BarName(session.StallDisplayName(item.Id)), item.Cell)));
        return marks.OrderBy(mark => (long)(mark.Cell.X - here.Cell.X) * (mark.Cell.X - here.Cell.X) + (long)(mark.Cell.Z - here.Cell.Z) * (mark.Cell.Z - here.Cell.Z))
            .ThenBy(mark => mark.Name, StringComparer.Ordinal).Select(mark => mark.Name).FirstOrDefault();
        static string BarName(string name) => name.EndsWith(" Bar", StringComparison.Ordinal) ? name[..^4] + " bar" : name == "Bar" ? "bar" : name;
    }

    private Button Tab(string van, FoodTrader trader, bool active)
    {
        var text = $"Van {Stalls.Number(van)} · {trader.Menu}";
        var width = Ui.BodyBold.GetStringSize(text, HorizontalAlignment.Left, -1, Ui.Px(11)).X + Ui.S(30);
        var tab = new Button { Name = "TraderTab_" + van, CustomMinimumSize = new Vector2(width, Ui.S(24)), MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = $"{trader.Name} at van {Stalls.Number(van)}" };
        var fill = active ? Ui.Box(Ui.Ink, 12) : Ui.Box(Ui.PaperBright, 12, Ui.PaperRule, 1);
        foreach (var state in new[] { "normal", "pressed", "disabled" }) tab.AddThemeStyleboxOverride(state, fill);
        tab.AddThemeStyleboxOverride("hover", active ? fill : Ui.Box(Colors.White, 12, Ui.PaperEdge, 1));
        tab.AddThemeStyleboxOverride("focus", Ui.Box(new Color(0, 0, 0, 0), 12, Ui.Gold, 2));
        var dot = new Panel { Position = Ui.S(8, 8), Size = Ui.S(8, 8), MouseFilter = Control.MouseFilterEnum.Ignore };
        dot.AddThemeStyleboxOverride("panel", Ui.Box(new Color(trader.CardColour), 4)); tab.AddChild(dot);
        var label = Ui.Text(text, 11, active ? Ui.BarText : Ui.Ink, Ui.BodyBold);
        label.Position = new Vector2(Ui.S(21), 0); label.Size = new Vector2(width - Ui.S(21), Ui.S(24)); label.VerticalAlignment = VerticalAlignment.Center;
        tab.AddChild(label);
        tab.Pressed += () => { if (Van != van) Callable.From(() => Open(van)).CallDeferred(); };
        return tab;
    }

    private void Place(Button card, int index, float width)
    {
        card.Position = new Vector2(Ui.S(index * (width + CardGap)), 0);
        card.SetMeta("rest", card.Position.Y);
        _cards!.AddChild(card);
    }

    /// <summary>One flyer. Locked, elsewhere and coming-soon flyers are greyed and can't be clicked.</summary>
    private Button Card(float w, string name, string menu, string strapline, Color colour, string art, string pays, string portion, TraderPips? pips, Look look,
        IReadOnlyList<TraderChip> chips, string? note, string? tape, string? elsewhere, string van)
    {
        var grey = look is Look.TierLocked or Look.Elsewhere or Look.Fixed;
        var faded = grey || look == Look.ComingSoon;
        var card = new Button { Size = Ui.S(w, CardHeight), CustomMinimumSize = Ui.S(w, CardHeight), Disabled = look is not (Look.Normal or Look.Booked),
            FocusMode = look is Look.Normal or Look.Booked ? Control.FocusModeEnum.All : Control.FocusModeEnum.None, MouseDefaultCursorShape = look == Look.Normal ? Control.CursorShape.PointingHand : Control.CursorShape.Arrow };
        var rest = Shadowed(grey ? LockedCard : Ui.PaperBright, 8, look == Look.Booked ? Ui.Teal : null, 3, 0.27f, 5);
        var lifted = look == Look.Booked ? rest : Shadowed(Ui.PaperBright, 8, Ui.Gold, 3, 0.47f, 8);
        card.AddThemeStyleboxOverride("normal", rest);
        card.AddThemeStyleboxOverride("disabled", rest);
        card.AddThemeStyleboxOverride("hover", lifted);
        card.AddThemeStyleboxOverride("pressed", lifted);
        card.AddThemeStyleboxOverride("hover_pressed", lifted);
        card.AddThemeStyleboxOverride("focus", look == Look.Booked ? new StyleBoxEmpty() : Shadowed(new Color(0, 0, 0, 0), 8, Ui.Gold, 3, 0, 0));
        if (look == Look.Booked) card.SetMeta("booked", true);
        if (look == Look.Normal)
        {
            // Hover (or keyboard focus) lifts the flyer 4 units over 0.08 s.
            void Lift() => card.CreateTween().TweenProperty(card, "position:y", card.GetMeta("rest").AsSingle() - (card.IsHovered() || card.HasFocus() ? Ui.S(4) : 0), 0.08)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
            card.MouseEntered += Lift; card.MouseExited += Lift; card.FocusEntered += Lift; card.FocusExited += Lift;
        }

        Box(card, 6, 6, w - 12, 86, new Color(colour, grey ? 0.07f : 0.16f), 6);
        var picture = new TextureRect { Texture = GD.Load<Texture2D>(TraderFlyers.ArtPath(art)), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Position = Ui.S(10, 10), Size = Ui.S(w - 20, 80), MouseFilter = Control.MouseFilterEnum.Ignore };
        if (faded) picture.Material = Grey;
        card.AddChild(picture);
        var tagText = menu.ToUpperInvariant();
        var tag = Box(card, 12, 12, Measure(tagText, Ui.CapsFont, 9.5f) + 14, 16, colour, 8);
        tag.AddChild(Centred(Ui.Caps(tagText, Ui.PaperBright, 9.5f), 7, 16, left: true));

        var ink = grey ? Ui.InkMuted : Ui.Ink;
        card.AddChild(At(Ui.Text(name, name.Length >= 20 ? 13.5f : 15, ink, Ui.Slab), 12, 100));
        card.AddChild(At(Ui.Text($"“{strapline}”", 11.5f, grey ? Ui.InkMuted : colour, Ui.BodySemi), 12, 121));
        var chip = Box(card, 12, 142, Measure(pays, Ui.BodyBold, 11) + 16, 18, grey ? Ui.PaperRule : Ui.TealWash, 9);
        chip.AddChild(Centred(Ui.Text(pays, 11, grey ? Ui.InkMuted : Ui.Teal, Ui.BodyBold), 8, 18, left: true));
        var price = Ui.Text(portion, 10.5f, Ui.InkMuted, Ui.BodySemi);
        price.Position = Ui.S(12, 142); price.Size = Ui.S(w - 24, 18); price.HorizontalAlignment = HorizontalAlignment.Right; price.VerticalAlignment = VerticalAlignment.Center;
        card.AddChild(price);

        var values = pips is null ? null : new[] { pips.Speed, pips.Filling, pips.Treat, pips.Pull };
        string[] labels = ["Speed", "Filling", "Treat", "Pull"];
        for (var row = 0; row < 4; row++)
        {
            var y = 170 + 15 * row;
            var label = Ui.Text(labels[row], 10.5f, Ui.InkMuted, Ui.BodySemi);
            label.Position = Ui.S(12, y - 3); label.Size = Ui.S(80, 15); label.VerticalAlignment = VerticalAlignment.Center; card.AddChild(label);
            for (var pip = 0; pip < 5; pip++)
            {
                var square = new Panel { Position = Ui.S(w - 72 + pip * 12, y), Size = Ui.S(9, 9), MouseFilter = Control.MouseFilterEnum.Ignore };
                square.AddThemeStyleboxOverride("panel", values is null ? Ui.Box(new Color(0, 0, 0, 0), 2, Ui.PaperRule, 1)
                    : Ui.Box(pip < values[row] ? grey ? LockedPip : colour : Ui.PaperRule, 2));
                card.AddChild(square);
            }
        }

        var chipY = 232f;
        foreach (var compare in chips)
        {
            var box = Box(card, 12, chipY, Measure(compare.Text, Ui.BodyBold, 10) + 12, 15, compare.Good ? Ui.TealWash : Ui.AlertWash, 7);
            box.AddChild(Centred(Ui.Text(compare.Text, 10, compare.Good ? Ui.Teal : Ui.Alert, Ui.BodyBold), 6, 15, left: true));
            chipY += 18;
        }
        if (note is not null) card.AddChild(At(Ui.Text(note, 10, Ui.InkMuted, Ui.BodySemi), 12, 274));

        if (look == Look.Booked)
        {
            var words = $"BOOKED · VAN {Stalls.Number(van)}";
            var stampWidth = Measure(words, Ui.Slab, 13) + 20;
            var stamp = new Panel { Name = "Stamp", Position = Ui.S(w / 2 - stampWidth / 2, 248), Size = Ui.S(stampWidth, 26), MouseFilter = Control.MouseFilterEnum.Ignore };
            stamp.AddThemeStyleboxOverride("panel", Ui.Box(new Color(0, 0, 0, 0), 4, Ui.Teal, 2));
            stamp.PivotOffset = stamp.Size / 2; stamp.RotationDegrees = -5;
            var stampLabel = Ui.Text(words, 13, Ui.Teal, Ui.Slab);
            stampLabel.Size = stamp.Size; stampLabel.HorizontalAlignment = HorizontalAlignment.Center; stampLabel.VerticalAlignment = VerticalAlignment.Center;
            stamp.AddChild(stampLabel); card.AddChild(stamp);
        }
        if (tape is not null)
        {
            var soon = look == Look.ComingSoon;
            var strip = new Panel { Position = Ui.S(-6, 42), Size = Ui.S(w + 12, 20), MouseFilter = Control.MouseFilterEnum.Ignore };
            strip.AddThemeStyleboxOverride("panel", Ui.Box(soon ? new Color(Ui.Gold, 0.94f) : new Color(Ui.Ink, 0.92f)));
            strip.PivotOffset = strip.Size / 2; strip.RotationDegrees = 8;
            var words = Ui.Text(tape.ToUpperInvariant(), soon ? 10.5f : 11, soon ? Ui.Bar : Ui.Gold, Ui.BodyBold);
            words.Size = strip.Size; words.HorizontalAlignment = HorizontalAlignment.Center; words.VerticalAlignment = VerticalAlignment.Center;
            strip.AddChild(words); card.AddChild(strip);
        }
        if (look == Look.TierLocked)
        {
            var padlock = Ui.IconRect("lock", 14, Ui.InkMuted); padlock.Position = Ui.S(w - 26, 100); padlock.Size = Ui.S(14, 14); card.AddChild(padlock);
        }
        if (elsewhere is not null)
        {
            var number = Stalls.Number(elsewhere);
            var words = $"At van {number} · change van {number} first";
            var jump = new Button { Name = "TraderElsewhere", Position = Ui.S(12, 56), Size = Ui.S(Measure(words, Ui.BodyBold, 10.5f) + 16, 18),
                TooltipText = $"Open van {number}'s tab", MouseDefaultCursorShape = Control.CursorShape.PointingHand, FocusMode = Control.FocusModeEnum.All };
            jump.CustomMinimumSize = jump.Size;
            jump.AddThemeStyleboxOverride("normal", Ui.Box(Ui.Ink, 9));
            jump.AddThemeStyleboxOverride("hover", Ui.Box(Ui.Bar, 9));
            jump.AddThemeStyleboxOverride("pressed", Ui.Box(Ui.BarDeep, 9));
            jump.AddThemeStyleboxOverride("focus", Ui.Box(new Color(0, 0, 0, 0), 9, Ui.Gold, 2));
            jump.AddChild(Centred(Ui.Text(words, 10.5f, Ui.Gold, Ui.BodyBold), 8, 18, left: true));
            jump.Pressed += () => Callable.From(() => Open(elsewhere)).CallDeferred();
            card.AddChild(jump);
        }
        return card;
    }

    /// <summary>The stamp thumps in: scale 1.4 to 1 and fading in, over 0.18 s.</summary>
    private static void Thump(Control card)
    {
        if (card.GetNodeOrNull<Control>("Stamp") is not { } stamp) return;
        stamp.Scale = new Vector2(1.4f, 1.4f); stamp.Modulate = new Color(1, 1, 1, 0);
        var tween = stamp.CreateTween().SetParallel();
        tween.TweenProperty(stamp, "scale", Vector2.One, 0.18).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(stamp, "modulate:a", 1f, 0.18);
    }

    private static ShaderMaterial Grey => _grey ??= new ShaderMaterial
    {
        Shader = new Shader
        {
            Code = "shader_type canvas_item;\nvoid fragment() {\n\tvec4 c = texture(TEXTURE, UV);\n\tfloat g = dot(c.rgb, vec3(0.299, 0.587, 0.114));\n\tCOLOR = vec4(vec3(g), c.a * 0.55);\n}\n",
        },
    };

    private static StyleBoxFlat Shadowed(Color fill, float radius, Color? edge, float edgeWidth, float shadowAlpha, float blur)
    {
        var box = Ui.Box(fill, radius, edge, edge is null ? 0 : edgeWidth);
        if (shadowAlpha > 0) { box.ShadowColor = new Color(0, 0, 0, shadowAlpha); box.ShadowSize = Ui.Px(blur); box.ShadowOffset = new Vector2(0, Ui.S(3)); }
        return box;
    }

    private static Panel Box(Control parent, float x, float y, float w, float h, Color fill, float radius)
    {
        var panel = new Panel { Position = Ui.S(x, y), Size = Ui.S(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", Ui.Box(fill, radius));
        parent.AddChild(panel);
        return panel;
    }

    private static T At<T>(T control, float x, float y) where T : Control { control.Position = Ui.S(x, y); return control; }

    /// <summary>A label filling a strip's height, centred on it, starting <paramref name="inset"/> units in.</summary>
    private static Label Centred(Label label, float inset, float height, bool left)
    {
        label.Position = new Vector2(Ui.S(inset), 0); label.Size = new Vector2(0, Ui.S(height));
        label.VerticalAlignment = VerticalAlignment.Center;
        label.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        return label;
    }

    /// <summary>A string's width in mockup units, for sizing chips and tags to their text.</summary>
    private static float Measure(string text, Font font, float mockupSize) => font.GetStringSize(text, HorizontalAlignment.Left, -1, Ui.Px(mockupSize)).X / Ui.Scale;

    private static void Clear(Node node)
    {
        foreach (var child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); }
    }
}

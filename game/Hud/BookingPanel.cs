using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The Programme sheet: the Trailer Stage running order with three sets, the sortable act table with
/// a genre filter, and the selected act's details with a Book button. Acts are booked by selecting one
/// and then a set (or Book), or by dragging; every edit is previewed against the session first.
/// <c>setStatus</c> shows the booking message in the HUD status line.
/// </summary>
internal sealed class BookingPanel(IHudHost _hud, Action _layoutWorkspace, Action<string> _setStatus)
{
    private HBoxContainer? _root;
    public Control? Root => _root;
    public bool IsBuilt => _bookingLane is not null;
    /// <summary>Sets filled and lineup cost, for the Start button's explanation.</summary>
    public string Summary { get; private set; } = "";
    /// <summary>The latest booking outcome to show while the Programme page is open.</summary>
    public string DurableMessage => _bookingDurableMessage;

    /// <summary>Forgets the previous campaign's selection, sort and filter.</summary>
    public void Reset()
    {
        _bookingSelected = null; _bookingDurableMessage = "Select an act, then choose a set or Book. Dragging also works.";
        _bookingSort = BookingSortField.Price; _bookingDescending = false; _bookingGenre = null;
        _bookingGenreFilter?.Select(0);
    }

    private sealed record SetCard(BookingDragButton Target, Label Caption, Label Title, Label Detail, Button Remove);
    private sealed record ActRow(BookingDragButton Card, Label Detail, Label Fee);

    private static readonly string[] ColumnTitles = ["Act", "Genre", "Fee", "Popular", "Ego", "Pro"];
    private static readonly string[] SortWords = ["name", "genre", "fee", "popularity", "ego", "professionalism"];
    private const string BookingDefaultMeaning = "Bars show intensity, not quality. High ego = more demanding and headline-sensitive.";

    private Control? _bookingLane;
    private readonly Dictionary<string, ActRow> _bookingRows = [];
    private readonly Button[] _bookingHeaders = new Button[6];
    private VBoxContainer? _bookingTableBody;
    private OptionButton? _bookingGenreFilter;
    private Label? _bookingTableCount;
    private Label? _standingLine;
    private string _offerKey = "";
    private Label? _bookingMeaning;
    private BookingSortField _bookingSort = BookingSortField.Price;
    private bool _bookingDescending;
    private int? _bookingGenre;
    private readonly SetCard[] _sets = new SetCard[3];
    private string? _bookingSelected;
    private string _bookingDurableMessage = "Select an act, then choose a set or Book. Dragging also works.";
    private bool _bookingLayoutPending;

    private VBoxContainer? _detail;
    private Label? _detailEmpty;
    private HBoxContainer? _detailTags;
    private Label? _detailName;
    private readonly (Label Value, ProgressBar Bar)[] _detailMetrics = new (Label, ProgressBar)[3];
    private Label? _detailFee;
    private Label? _detailLeft;
    private Button? _detailBook;

    public async void ScheduleLayout()
    {
        if (_bookingLayoutPending || _bookingLane is null) return;
        _bookingLayoutPending = true;
        await _hud.Viewport.ToSignal(_hud.Viewport.GetTree(), SceneTree.SignalName.ProcessFrame);
        await _hud.Viewport.ToSignal(_hud.Viewport.GetTree(), SceneTree.SignalName.ProcessFrame);
        _bookingLayoutPending = false;
        if (!_hud.Viewport.GuiIsDragging()) _layoutWorkspace();
    }
    private string[] BookingIds => _hud.Session.CapturePreparationPlan()?.ActIds is { Length: 3 } plan ? plan :
        _hud.Session.CaptureProgramme()?.ActIds is { Length: 3 } booked ? booked : ["", "", ""];
    private bool BookingLocked => _hud.Session.PreparedStatus != PreparationStatus.Preparing;
    private static string BookingTime(int ticks) => $"{ticks / 80 / 60:00}:{ticks / 80 % 60:00}";
    private static string SlotTimes(int slot) => $"{BookingTime(GameSession.FestivalSlotStarts[slot])}–{BookingTime(GameSession.FestivalSlotEnds[slot])}";
    private static bool ExpectsToHeadline(FestivalAct act) => act.Ego >= 70;

    /// <summary>A genre's swatch, its set-card wash, and the ink used on that wash.</summary>
    private static (Color Dot, Color Wash, Color Ink) GenreColours(int genre) => genre switch
    {
        0 => (new Color("6f9a5b"), new Color("cfddbf"), new Color("3e5a33")),
        1 => (new Color("c0643a"), new Color("edc6ae"), new Color("7a3a1c")),
        2 => (new Color("c99a1e"), new Color("f1d48e"), new Color("5c4410")),
        3 => (new Color("4a7fa6"), new Color("c9dcea"), new Color("2c4f6b")),
        4 => (new Color("c94a7a"), new Color("f1c9d8"), new Color("7a1f42")),
        5 => (new Color("4b4a52"), new Color("d2d1d6"), new Color("2a2930")),
        _ => (Ui.InkMuted, Ui.PaperRule, Ui.Ink),
    };

    private Variant BookingPayload(string id)
    {
        if (BookingLocked) return default;
        return new Godot.Collections.Dictionary { ["kind"] = "festival-band-v1", ["act"] = id, ["source"] = Array.IndexOf(BookingIds, id) };
    }
    private Variant BeginBookingDrag(string id)
    {
        if (WontPlay(id)) return default;
        var data = BookingPayload(id);
        if (data.VariantType == Variant.Type.Nil) return data;
        for (var i = 0; i < 3; i++) PreviewBookingDrop(i, data);
        return data;
    }
    private bool ReadBookingPayload(Variant data, out string id, out int source)
    {
        id = ""; source = -1;
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (d.Count != 3 || !d.ContainsKey("kind") || !d.ContainsKey("act") || !d.ContainsKey("source") ||
            d["kind"].VariantType != Variant.Type.String || d["act"].VariantType != Variant.Type.String || d["source"].VariantType != Variant.Type.Int || d["kind"].AsString() != "festival-band-v1") return false;
        id = d["act"].AsString(); var n = d["source"].AsInt64();
        if (n is < -1 or > 2) return false;
        source = (int)n; return true;
    }
    private void SelectBookingBand(string id)
    {
        if (BookingLocked) return;
        if (WontPlay(id)) { _bookingDurableMessage = $"{ActCatalogue.Find(id)!.Name} won't play for the festival yet."; Refresh(); return; }
        _bookingSelected = id; _bookingDurableMessage = $"Selected {_hud.Session.GetFestivalActs().Single(a => a.Id == id).Name}. Choose a set or Book; Escape cancels.";
        Refresh();
    }
    private bool PreviewBookingDrop(int slot, Variant payload)
    {
        if (!ReadBookingPayload(payload, out var id, out var source)) return false;
        var preview = _hud.Session.PreviewLineupEdit(id, source, slot);
        _sets[slot].Detail.Text = preview.Message;
        return preview.IsValid;
    }
    private void CommitBookingDrop(int slot, Variant payload, bool remove = false)
    {
        if (!ReadBookingPayload(payload, out var id, out var source)) { _bookingDurableMessage = "Invalid band payload; lineup retained."; Refresh(); return; }
        var preview = _hud.Session.PreviewLineupEdit(id, source, slot, remove);
        if (!preview.IsValid || preview.IsNoOp) { _bookingDurableMessage = preview.Message; Refresh(); return; }
        var priorHash = _hud.Session.CaptureSnapshot().AuthoritativeHash;
        _hud.Commit(new SetProgrammeCommand(preview.ActIds));
        var changed = _hud.Session.CaptureSnapshot().AuthoritativeHash != priorHash;
        _bookingDurableMessage = changed ? preview.Message + " · Unpaid plan updated; next timed save pending." : _hud.Message;
        if (changed) _bookingSelected = null;
        Refresh();
    }

    /// <summary>The set Book would fill: the headline slot for an act that expects it, else the first free set.</summary>
    private int? BookTarget(FestivalAct act)
    {
        var ids = BookingIds;
        if (Array.IndexOf(ids, act.Id) >= 0) return null;
        if (ExpectsToHeadline(act) && ids[2] == "") return 2;
        var free = Array.IndexOf(ids, "");
        return free >= 0 ? free : null;
    }

    private static float ColumnWidth(int column) => column switch { 1 => Ui.S(84), 2 => Ui.S(44), _ => Ui.S(58) };
    private static Control.SizeFlags ColumnFlags(int column) => column == 0 ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill;
    private static HBoxContainer TableLine()
    {
        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", Ui.Px(10));
        return line;
    }
    private static Control Cell(HBoxContainer line, int column, Control content)
    {
        content.CustomMinimumSize = new Vector2(column == 0 ? 0 : ColumnWidth(column), content.CustomMinimumSize.Y);
        content.SizeFlagsHorizontal = ColumnFlags(column);
        content.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        content.MouseFilter = Control.MouseFilterEnum.Ignore;
        line.AddChild(content);
        return content;
    }

    public void Build(VBoxContainer parent)
    {
        _root = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _root.AddThemeConstantOverride("separation", Ui.Px(22)); parent.AddChild(_root);
        BuildRunningOrder(_root);
        _root.AddChild(new ColorRect { Color = Ui.PaperRule, CustomMinimumSize = new Vector2(1, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        BuildActTable(_root);
        BuildDetail(_root);
        Refresh();
    }

    private void BuildRunningOrder(HBoxContainer parent)
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(Ui.S(300), 0) };
        column.AddThemeConstantOverride("separation", Ui.Px(4)); parent.AddChild(column);
        column.AddChild(Ui.Heading("Running order", 27));
        column.AddChild(Ui.Text("Trailer Stage · festival time mm:ss", 13.5f, Ui.InkMuted));
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(8)) });
        _bookingLane = new Control { CustomMinimumSize = new Vector2(Ui.S(300), Ui.S(420)), MouseFilter = Control.MouseFilterEnum.Pass };
        column.AddChild(_bookingLane);
        _bookingLane.AddChild(new ColorRect { Color = Ui.PaperEdge, Position = new Vector2(Ui.S(46), 0), Size = new Vector2(1, Ui.S(420)), MouseFilter = Control.MouseFilterEnum.Ignore });
        void Time(string text, float mockupTop)
        {
            var label = Ui.Text(text, 12, Ui.InkMuted); label.Position = new Vector2(0, Ui.S(mockupTop)); label.MouseFilter = Control.MouseFilterEnum.Ignore;
            _bookingLane.AddChild(label);
        }
        // Blocks follow the mockup's running order rather than true scale, so each set has room for its card.
        var tops = new[] { 52f, 180f, 308f };
        var heights = new[] { 92f, 92f, 104f };
        Time("00:00", -6);
        var gates = new PanelContainer { Position = new Vector2(Ui.S(56), 0), Size = Ui.S(244, 48), MouseFilter = Control.MouseFilterEnum.Ignore };
        gates.AddThemeStyleboxOverride("panel", Ui.Box(new Color("efe3ca"), 6, padX: 12));
        var gatesText = Ui.Text("Gates open · guests arrive", 12.5f, Ui.InkMuted); gatesText.VerticalAlignment = VerticalAlignment.Center; gates.AddChild(gatesText);
        _bookingLane.AddChild(gates);
        for (var slot = 0; slot < 3; slot++)
        {
            var i = slot;
            Time(BookingTime(GameSession.FestivalSlotStarts[i]), tops[i] - 6);
            var target = new BookingDragButton { Position = new Vector2(Ui.S(56), Ui.S(tops[i])), Size = Ui.S(244, heights[i]), FocusMode = Control.FocusModeEnum.All,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand, ClipContents = true };
            _bookingLane.AddChild(target);
            var words = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
            words.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            words.OffsetLeft = Ui.S(12); words.OffsetRight = -Ui.S(12); words.OffsetTop = Ui.S(8); words.OffsetBottom = -Ui.S(8);
            words.AddThemeConstantOverride("separation", Ui.Px(2)); target.AddChild(words);
            var caption = Ui.Caps("", Ui.Ink, 9.5f); caption.MouseFilter = Control.MouseFilterEnum.Ignore; words.AddChild(caption);
            var title = Ui.Heading("", 18); title.MouseFilter = Control.MouseFilterEnum.Ignore; title.ClipText = true; words.AddChild(title);
            var detail = Ui.Text("", 12.5f, Ui.Ink); detail.MouseFilter = Control.MouseFilterEnum.Ignore;
            detail.AutowrapMode = TextServer.AutowrapMode.WordSmart; words.AddChild(detail);
            var remove = new Button { Text = "Remove", Flat = true, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            remove.AddThemeFontSizeOverride("font_size", Ui.Px(13)); remove.AddThemeFontOverride("font", Ui.BodySemi);
            remove.Position = new Vector2(Ui.S(244) - Ui.S(70), Ui.S(heights[i]) - Ui.S(30)); remove.Size = Ui.S(62, 26);
            remove.Pressed += () => { if (BookingIds[i] != "") CommitBookingDrop(i, BookingPayload(BookingIds[i]), true); };
            target.AddChild(remove);
            target.AcceptPayload = data => PreviewBookingDrop(i, data); target.CommitPayload = data => CommitBookingDrop(i, data);
            target.DragPayload = () => BookingIds[i] == "" ? default : BeginBookingDrag(BookingIds[i]);
            void Activate() { if (_bookingSelected is { } id) CommitBookingDrop(i, BookingPayload(id)); else if (BookingIds[i] != "") SelectBookingBand(BookingIds[i]); }
            target.Pressed += Activate;
            target.GuiInput += input => HandleBookingKey(target, input, Activate);
            _sets[i] = new SetCard(target, caption, title, detail, remove);
            if (i < 2)
            {
                var gapTop = tops[i] + heights[i] + 2;
                var change = new PanelContainer { Position = new Vector2(Ui.S(56), Ui.S(gapTop)), Size = Ui.S(244, 32), MouseFilter = Control.MouseFilterEnum.Ignore };
                var dashes = Ui.Box(new Color(0, 0, 0, 0), 0); dashes.BorderColor = Ui.PaperEdge; dashes.BorderWidthTop = dashes.BorderWidthBottom = 1;
                change.AddThemeStyleboxOverride("panel", dashes);
                var text = Ui.Text($"Changeover · {(GameSession.FestivalSlotStarts[i + 1] - GameSession.FestivalSlotEnds[i]) / 80}s", 12, Ui.InkMuted);
                text.HorizontalAlignment = HorizontalAlignment.Center; text.VerticalAlignment = VerticalAlignment.Center; change.AddChild(text);
                _bookingLane.AddChild(change);
            }
        }
        Time(BookingTime(GameSession.FestivalSlotEnds[2]), tops[2] + heights[2] - 7);
    }

    private void BuildActTable(HBoxContainer parent)
    {
        var table = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        table.AddThemeConstantOverride("separation", 0); parent.AddChild(table);
        var heading = new HBoxContainer(); table.AddChild(heading);
        var title = Ui.Heading("Available acts", 22); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; heading.AddChild(title);
        _bookingTableCount = Ui.Text("", 13, Ui.InkMuted); _bookingTableCount.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd; heading.AddChild(_bookingTableCount);
        _standingLine = Ui.Text("", 13, Ui.InkMuted); _standingLine.AutowrapMode = TextServer.AutowrapMode.WordSmart; table.AddChild(_standingLine);
        table.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(10)) });
        var filters = new HBoxContainer(); filters.AddThemeConstantOverride("separation", Ui.Px(8)); table.AddChild(filters);
        _bookingGenreFilter = new OptionButton { CustomMinimumSize = new Vector2(Ui.S(150), Ui.S(36)), FocusMode = Control.FocusModeEnum.All };
        _bookingGenreFilter.AddThemeFontSizeOverride("font_size", Ui.Px(13.5f));
        _bookingGenreFilter.AddItem("All genres");
        for (var genre = 0; genre < FestivalGenre.Count; genre++) _bookingGenreFilter.AddItem(FestivalGenre.Name(genre));
        _bookingGenreFilter.ItemSelected += index =>
        {
            if (_hud.Viewport.GuiIsDragging()) { _bookingGenreFilter.Select(_bookingGenre is { } g ? g + 1 : 0); return; }
            _bookingGenre = index == 0 ? null : (int)index - 1;
            Refresh();
        };
        filters.AddChild(_bookingGenreFilter);
        table.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(12)) });

        var header = new PanelContainer();
        var underline = Ui.Box(new Color(0, 0, 0, 0), 0, padX: 10); underline.BorderColor = Ui.Ink; underline.BorderWidthBottom = Ui.Px(1.5f);
        underline.ContentMarginBottom = Ui.S(6);
        header.AddThemeStyleboxOverride("panel", underline); table.AddChild(header);
        var headerLine = TableLine(); header.AddChild(headerLine);
        for (var column = 0; column < ColumnTitles.Length; column++)
        {
            var index = column;
            var button = new Button { Flat = true, Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.All,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                CustomMinimumSize = new Vector2(index == 0 ? 0 : ColumnWidth(index), 0), SizeFlagsHorizontal = ColumnFlags(index) };
            button.AddThemeFontOverride("font", Ui.CapsFont); button.AddThemeFontSizeOverride("font_size", Ui.Px(10.5f));
            foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
                button.AddThemeColorOverride(state, state == "font_hover_color" ? Ui.Ink : Ui.InkMuted);
            foreach (var state in new[] { "normal", "hover", "pressed", "focus" }) button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
            button.Pressed += () =>
            {
                if (_hud.Viewport.GuiIsDragging()) return;
                var field = (BookingSortField)index;
                if (_bookingSort == field) _bookingDescending = !_bookingDescending;
                else { _bookingSort = field; _bookingDescending = false; }
                Refresh();
            };
            headerLine.AddChild(button); _bookingHeaders[index] = button;
        }
        _bookingTableBody = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _bookingTableBody.AddThemeConstantOverride("separation", 0);
        // About six rows show at once; the rest of the offer, then the acts out of reach, scroll.
        var tableScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, Ui.S(300)) };
        Ui.SlimScrollbar(tableScroll); tableScroll.AddChild(_bookingTableBody); table.AddChild(tableScroll);
        SyncRows(_hud.Session.GetFestivalActs());
        table.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(10)) });
        _bookingMeaning = Ui.Text(BookingDefaultMeaning, 12.5f, Ui.InkMuted); _bookingMeaning.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        table.AddChild(_bookingMeaning);
    }

    private bool WontPlay(string id) => ActCatalogue.Find(id) is not { } act || _hud.Session.ActStandingOf(act) == ActStanding.Locked;

    /// <summary>Keeps one row per act in this run's offer; a new campaign or a changed standing offers different acts.</summary>
    private void SyncRows(IReadOnlyList<FestivalAct> acts)
    {
        var key = string.Join("|", acts.Select(act => act.Id));
        if (key == _offerKey || _bookingTableBody is null) return;
        _offerKey = key;
        foreach (var (id, row) in _bookingRows.Where(pair => acts.All(act => act.Id != pair.Key)).ToArray())
        {
            _bookingTableBody.RemoveChild(row.Card); row.Card.QueueFree(); _bookingRows.Remove(id);
        }
        foreach (var act in acts.Where(act => !_bookingRows.ContainsKey(act.Id)))
            _bookingTableBody.AddChild(ActLine(act));
    }

    private BookingDragButton ActLine(FestivalAct act)
    {
        var id = act.Id;
        var card = new BookingDragButton { Text = "", CustomMinimumSize = new Vector2(0, Ui.S(30)), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.All, MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = ExactTooltip(act) };
        card.DragPreviewText = () => $"{act.Name} · {FestivalGenreName(act.Genre)} · {FestivalCurrency.Format(_hud.Session.ActFee(act))}";
        var line = TableLine(); line.SetAnchorsPreset(Control.LayoutPreset.FullRect); line.OffsetLeft = Ui.S(10); line.OffsetRight = -Ui.S(10);
        card.AddChild(line);
        // One line per act: the name, then a small status tag only when there is something to say.
        var name = new HBoxContainer(); name.AddThemeConstantOverride("separation", Ui.Px(8));
        var title = Ui.Text(act.Name, 14, Ui.Ink, Ui.BodyBold); title.ClipText = true; title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        title.CustomMinimumSize = new Vector2(1, 0); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; title.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        title.MouseFilter = Control.MouseFilterEnum.Ignore; name.AddChild(title);
        var detail = Ui.Text("", 11.5f, Ui.InkMuted, Ui.BodyBold); detail.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        detail.MouseFilter = Control.MouseFilterEnum.Ignore; name.AddChild(detail);
        Cell(line, 0, name);
        var genre = new HBoxContainer(); genre.AddThemeConstantOverride("separation", Ui.Px(6));
        var dot = new Panel { CustomMinimumSize = Ui.S(10, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        dot.AddThemeStyleboxOverride("panel", Ui.Box(GenreColours(act.Genre).Dot, 5)); genre.AddChild(dot);
        genre.AddChild(Ui.Text(FestivalGenreName(act.Genre), 13.5f, Ui.Ink));
        Cell(line, 1, genre);
        var fee = Ui.Text("", 14, Ui.Ink, Ui.BodySemi); Cell(line, 2, fee);
        Cell(line, 3, new RatingBars { Score = act.Popularity, Ink = Ui.Teal });
        Cell(line, 4, new RatingBars { Score = act.Ego, Ink = Ui.Alert });
        Cell(line, 5, new RatingBars { Score = act.Professionalism, Ink = new Color("3e5a8c") });
        card.DragPayload = () => BeginBookingDrag(id); card.Pressed += () => SelectBookingBand(id);
        card.GuiInput += input => HandleBookingKey(card, input, () => SelectBookingBand(id));
        card.FocusEntered += () => ShowExactScores(act);
        card.MouseEntered += () => ShowExactScores(act);
        card.FocusExited += () => { if (_bookingMeaning is not null) _bookingMeaning.Text = BookingDefaultMeaning; };
        card.MouseExited += () => { if (!card.HasFocus() && _bookingMeaning is not null) _bookingMeaning.Text = BookingDefaultMeaning; };
        _bookingRows.Add(id, new ActRow(card, detail, fee));
        return card;
    }

    private void BuildDetail(HBoxContainer parent)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(Ui.S(300), 0) };
        panel.AddThemeStyleboxOverride("panel", Ui.Box(Ui.GoldWash, 8, Ui.PaperRule, 1, 18, 18)); parent.AddChild(panel);
        _detail = new VBoxContainer(); _detail.AddThemeConstantOverride("separation", 0); panel.AddChild(_detail);
        _detailEmpty = Ui.Text("Select an act to see its details and book it.", 14, Ui.InkMuted);
        _detailEmpty.AutowrapMode = TextServer.AutowrapMode.WordSmart; _detail.AddChild(_detailEmpty);
        _detailTags = new HBoxContainer(); _detailTags.AddThemeConstantOverride("separation", Ui.Px(6)); _detail.AddChild(_detailTags);
        _detail.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(10)) });
        _detailName = Ui.Heading("", 29); _detailName.AutowrapMode = TextServer.AutowrapMode.WordSmart; _detail.AddChild(_detailName);
        _detail.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(16)) });
        var metrics = new[] { ("Popularity", Ui.Teal), ("Ego", Ui.Alert), ("Professionalism", new Color("3e5a8c")) };
        for (var i = 0; i < 3; i++)
        {
            var row = new HBoxContainer(); _detail.AddChild(row);
            var name = Ui.Text(metrics[i].Item1, 13.5f, Ui.Ink, Ui.BodyBold); name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(name);
            var value = Ui.Text("", 13.5f, Ui.InkMuted); row.AddChild(value);
            var bar = new ProgressBar { ShowPercentage = false, MaxValue = 100, CustomMinimumSize = new Vector2(0, Ui.S(8)) };
            bar.AddThemeStyleboxOverride("background", Ui.Box(Ui.PaperRule, 4));
            bar.AddThemeStyleboxOverride("fill", Ui.Box(metrics[i].Item2, 4));
            _detail.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(4)) });
            _detail.AddChild(bar);
            _detail.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(10)) });
            _detailMetrics[i] = (value, bar);
        }
        _detail.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var feeRule = new ColorRect { Color = Ui.PaperRule, CustomMinimumSize = new Vector2(0, 1) }; _detail.AddChild(feeRule);
        _detail.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(10)) });
        var fee = new HBoxContainer(); _detail.AddChild(fee);
        var feeCaption = Ui.Caps("Fee", Ui.InkMuted); feeCaption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; feeCaption.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd; fee.AddChild(feeCaption);
        _detailFee = Ui.Heading("", 26); fee.AddChild(_detailFee);
        _detailLeft = Ui.Text("", 12.5f, Ui.InkMuted); _detailLeft.HorizontalAlignment = HorizontalAlignment.Right; _detail.AddChild(_detailLeft);
        _detail.AddChild(new Control { CustomMinimumSize = new Vector2(0, Ui.S(10)) });
        _detailBook = Ui.Style(new Button { CustomMinimumSize = new Vector2(0, Ui.S(46)), MouseDefaultCursorShape = Control.CursorShape.PointingHand }, Ui.ButtonKind.Primary, 16, 8);
        _detailBook.AddThemeStyleboxOverride("disabled", Ui.Box(Ui.PaperRule, 8));
        _detailBook.AddThemeColorOverride("font_disabled_color", Ui.InkMuted);
        _detailBook.Pressed += () =>
        {
            if (_bookingSelected is not { } id) return;
            var act = _hud.Session.GetFestivalActs().Single(item => item.Id == id);
            if (BookTarget(act) is { } slot) CommitBookingDrop(slot, BookingPayload(id));
        };
        _detail.AddChild(_detailBook);
    }

    private static string Halves(int score) => $"{BookingTableView.HalfStarUnits(score) / 2.0:0.#} / 5";
    private static string ExactTooltip(FestivalAct act) =>
        $"{act.Name} · {FestivalGenreName(act.Genre)} · {FestivalCurrency.Format(act.PricePennies)}\n" +
        $"Popularity {act.Popularity}/100 · audience appeal\n" +
        $"Ego {act.Ego}/100 · higher means more demanding/headline-sensitive, not quality\n" +
        $"Professionalism {act.Professionalism}/100 · softens ego disappointment, not immunity" +
        (ExpectsToHeadline(act) ? "\nExpects to headline; non-headline performance still allowed." : "");
    private void ShowExactScores(FestivalAct act)
    {
        if (_bookingMeaning is not null) _bookingMeaning.Text =
            $"{act.Name} · Popularity {act.Popularity}/100 appeal · Ego {act.Ego}/100 demanding, not quality · Professionalism {act.Professionalism}/100 softens ego, not immunity";
    }

    private void HandleBookingKey(Control control, InputEvent input, Action activate)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.Escape)
        {
            _hud.Viewport.GuiCancelDrag(); _bookingSelected = null; _bookingDurableMessage = "Selection cancelled; lineup retained.";
            Refresh(); control.AcceptEvent();
        }
        else if (key.Keycode is Key.Enter or Key.Space)
        { if (!BookingLocked) activate(); control.AcceptEvent(); }
    }

    public void Refresh()
    {
        if (_bookingLane is null || _hud.Viewport.GuiIsDragging()) return;
        var session = _hud.Session;
        var acts = session.GetFestivalActs().ToArray(); var ids = BookingIds;
        SyncRows(acts);
        if (_bookingSelected is { } stale && !_bookingRows.ContainsKey(stale)) _bookingSelected = null;
        // Acts who won't play yet sit below the ones who will, whatever the sort.
        var projected = BookingTableView.Project(acts, _bookingGenre, _bookingSort, _bookingDescending)
            .OrderBy(act => session.ActStandingOf(act) == ActStanding.Locked ? 1 : 0).ToArray();
        for (var index = 0; index < projected.Length; index++) _bookingTableBody!.MoveChild(_bookingRows[projected[index].Id].Card, index);
        var willPlay = acts.Count(act => session.ActStandingOf(act) != ActStanding.Locked);
        _bookingTableCount!.Text = $"{willPlay} acts will play · sorted by {SortWords[(int)_bookingSort]}{(_bookingDescending ? ", high first" : "")}";
        _standingLine!.Text = $"Festival reputation {session.Standing.Reputation} · {FestivalCurrency.Format(session.TicketPricePennies)} tickets: guests expect popularity around {session.ExpectedPopularity}";
        _standingLine.TooltipText = "Acts more popular than the ticket promises delight guests; less popular acts disappoint them. Reputation decides who will play for you.";
        _standingLine.MouseFilter = Control.MouseFilterEnum.Pass;
        for (var index = 0; index < _bookingHeaders.Length; index++)
        {
            var active = _bookingSort == (BookingSortField)index;
            _bookingHeaders[index].Text = ColumnTitles[index].ToUpperInvariant() + (active ? _bookingDescending ? " ↓" : " ↑" : "");
            _bookingHeaders[index].TooltipText = active ? $"Sorted {(_bookingDescending ? "descending" : "ascending")}. Activate to reverse." : "Activate to sort ascending; activate again to reverse.";
        }
        foreach (var act in acts)
        {
            var row = _bookingRows[act.Id]; var assigned = Array.IndexOf(ids, act.Id);
            row.Card.Visible = _bookingGenre is null || act.Genre == _bookingGenre;
            var standing = session.ActStandingOf(act);
            row.Fee.Text = FestivalCurrency.Format(session.ActFee(act));
            row.Fee.AddThemeColorOverride("font_color", standing == ActStanding.Stretch ? Ui.Link : Ui.Ink);
            row.Detail.Text = assigned >= 0 ? $"Set {assigned + 1}" : standing == ActStanding.Locked ? $"Needs rep {session.ActReputationNeeded(act)}" :
                standing == ActStanding.Stretch ? "Stretch ×1.5" : ExpectsToHeadline(act) ? "Headliner" : "";
            row.Detail.AddThemeColorOverride("font_color", assigned >= 0 ? Ui.Teal : standing == ActStanding.Locked ? Ui.InkMuted :
                standing == ActStanding.Stretch || ExpectsToHeadline(act) ? Ui.Link : Ui.InkMuted);
            row.Detail.AddThemeFontOverride("font", assigned >= 0 || standing == ActStanding.Stretch || ExpectsToHeadline(act) ? Ui.BodyBold : Ui.Body);
            row.Card.Disabled = BookingLocked || standing == ActStanding.Locked;
            row.Card.TooltipText = standing == ActStanding.Locked
                ? $"{act.Name} won't play for the festival yet: they need a reputation of {session.ActReputationNeeded(act)} (yours is {session.Standing.Reputation})."
                : ExactTooltip(act) + (standing == ActStanding.Stretch ? $"\nStretch booking: {FestivalCurrency.Format(session.ActFee(act))} instead of {FestivalCurrency.Format(act.PricePennies)}." : "");
            var selected = _bookingSelected == act.Id;
            var rest = selected ? Ui.Box(new Color("ebd9b0"), 6, GoldShadowInset, 2) : RowRule();
            foreach (var state in new[] { "normal", "disabled", "focus", "pressed" }) row.Card.AddThemeStyleboxOverride(state, rest);
            row.Card.AddThemeStyleboxOverride("hover", selected ? rest : RowRule(new Color(0, 0, 0, 0.03f)));
            row.Card.Modulate = standing == ActStanding.Locked ? new Color(1, 1, 1, 0.42f) : assigned >= 0 && !selected ? new Color(1, 1, 1, 0.55f) : Colors.White;
        }
        if (_bookingSelected is { } hidden && !_bookingRows[hidden].Card.Visible)
            _bookingDurableMessage = $"{acts.Single(a => a.Id == hidden).Name} selected · hidden by genre filter; choose a set or show its genre.";
        var picked = _bookingSelected is { } chosen ? acts.Single(a => a.Id == chosen) : null;
        for (var i = 0; i < 3; i++) RefreshSet(i, acts.SingleOrDefault(a => a.Id == ids[i]), picked, ids);
        RefreshDetail(picked);
        Summary = $"{ids.Count(id => id != "")} of 3 sets filled · Lineup {FestivalCurrency.Format(ids.Where(id => id != "").Sum(id => session.ActFee(ActCatalogue.Find(id)!)))} · Paid at Start";
        if (!BookingLocked) _setStatus(_bookingDurableMessage);
    }

    private static readonly Color GoldShadowInset = Ui.GoldShadow;
    private static StyleBoxFlat RowRule(Color? fill = null)
    {
        var box = Ui.Box(fill ?? new Color(0, 0, 0, 0), 0); box.BorderColor = Ui.PaperRule; box.BorderWidthBottom = 1;
        return box;
    }

    private void RefreshSet(int slot, FestivalAct? act, FestivalAct? picked, string[] ids)
    {
        var card = _sets[slot];
        var headline = slot == 2;
        if (act is not null)
        {
            var (_, wash, ink) = GenreColours(act.Genre);
            var style = Ui.Box(wash, 6, padX: 12, padY: 10); style.ShadowColor = new Color(0, 0, 0, 0.12f); style.ShadowSize = 1; style.ShadowOffset = new Vector2(0, 1);
            foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" }) card.Target.AddThemeStyleboxOverride(state, style);
            card.Caption.Text = $"Set {slot + 1} · {SlotTimes(slot)} · {FestivalGenreName(act.Genre)}".ToUpperInvariant();
            card.Caption.AddThemeColorOverride("font_color", ink);
            card.Title.Text = act.Name; card.Title.AddThemeColorOverride("font_color", Ui.Ink);
            card.Detail.Text = picked is not null && picked.Id != act.Id
                ? _hud.Session.PreviewLineupEdit(picked.Id, Array.IndexOf(ids, picked.Id), slot).Message
                : FestivalCurrency.Format(_hud.Session.ActFee(act)) + (ExpectsToHeadline(act) && !headline ? " · expects to headline" : "");
            card.Detail.AddThemeColorOverride("font_color", ink);
            card.Remove.Visible = !BookingLocked;
            foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) card.Remove.AddThemeColorOverride(state, ink);
            card.Remove.TooltipText = $"Remove {act.Name} from Set {slot + 1}";
        }
        else
        {
            var style = Ui.Box(new Color(Ui.Gold, 0.2f), 6, Ui.GoldShadow, 2, 12, 10);
            foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" }) card.Target.AddThemeStyleboxOverride(state, style);
            card.Caption.Text = $"Set {slot + 1} · {SlotTimes(slot)}{(headline ? " · Headline slot" : "")}".ToUpperInvariant();
            card.Caption.AddThemeColorOverride("font_color", Ui.GoldInk);
            card.Title.Text = picked is not null ? $"Drop {picked.Name} here" : "Drop an act here";
            card.Title.AddThemeColorOverride("font_color", Ui.GoldInk);
            card.Detail.Text = picked is not null
                ? headline && ExpectsToHeadline(picked) ? "They expect to headline — this slot suits them" : _hud.Session.PreviewLineupEdit(picked.Id, Array.IndexOf(ids, picked.Id), slot).Message
                : headline ? "The closing set; acts that expect to headline want it" : "Select an act, then this set";
            card.Detail.AddThemeColorOverride("font_color", Ui.GoldInk);
            card.Remove.Visible = false;
        }
        card.Target.Disabled = BookingLocked;
        card.Target.TooltipText = act is null ? $"Set {slot + 1} · {SlotTimes(slot)} · empty" : $"{act.Name} · Set {slot + 1} · {SlotTimes(slot)}";
    }

    private void RefreshDetail(FestivalAct? act)
    {
        _detailEmpty!.Visible = act is null;
        foreach (var child in _detail!.GetChildren())
            if (child is Control control && control != _detailEmpty) control.Visible = act is not null;
        if (act is null) return;
        foreach (var child in _detailTags!.GetChildren()) { _detailTags.RemoveChild(child); child.QueueFree(); }
        var (_, wash, ink) = GenreColours(act.Genre);
        _detailTags.AddChild(Tag(FestivalGenreName(act.Genre), wash, ink));
        if (ExpectsToHeadline(act)) _detailTags.AddChild(Tag("Expects to headline", Ui.AlertWash, new Color("7a3312")));
        _detailName!.Text = act.Name;
        var scores = new[] { act.Popularity, act.Ego, act.Professionalism };
        for (var i = 0; i < 3; i++)
        {
            _detailMetrics[i].Value.Text = Halves(scores[i]);
            _detailMetrics[i].Bar.Value = BookingTableView.HalfStarUnits(scores[i]) * 10;
        }
        var fee = _hud.Session.ActFee(act);
        var standing = _hud.Session.ActStandingOf(act);
        _detailFee!.Text = FestivalCurrency.Format(fee);
        var assigned = Array.IndexOf(BookingIds, act.Id);
        var target = BookTarget(act);
        _detailLeft!.Text = assigned >= 0 ? $"Booked for Set {assigned + 1}" : standing == ActStanding.Locked ? $"Needs a festival reputation of {_hud.Session.ActReputationNeeded(act)}" :
            (standing == ActStanding.Stretch ? $"Stretch booking (usually {FestivalCurrency.Format(act.PricePennies)}) · " : "") +
            $"{FestivalCurrency.Format(_hud.Session.PreparationRemainingCash - fee)} left after booking";
        _detailBook!.Disabled = BookingLocked || target is null || standing == ActStanding.Locked;
        _detailBook.Text = assigned >= 0 ? $"Booked · Set {assigned + 1}" : standing == ActStanding.Locked ? "Won't play for you yet" :
            target is { } slot ? $"Book for Set {slot + 1} · {FestivalCurrency.Format(fee)}" : "All sets filled · drop on a set";
    }

    private static Label Tag(string text, Color wash, Color ink)
    {
        var tag = Ui.Caps(text, ink, 9.5f);
        tag.AddThemeStyleboxOverride("normal", Ui.Box(wash, 4, padX: 7, padY: 3));
        return tag;
    }
}

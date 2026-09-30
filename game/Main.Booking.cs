using Festival.Simulation;
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Festival.Game;

public partial class Main
{
    private Control? _bookingLane;
    private readonly Dictionary<string, BookingDragButton> _bookingCards = [];
    private readonly Dictionary<string, (Label Name, Label Detail)> _bookingRowCopy = [];
    private readonly Dictionary<string, (BookingStarRating Popularity, BookingStarRating Ego, BookingStarRating Professionalism)> _bookingRatings = [];
    private readonly Button[] _bookingHeaders = new Button[6];
    private readonly Label[] _bookingHeaderTitles = new Label[6];
    private VBoxContainer? _bookingTableBody;
    private OptionButton? _bookingGenreFilter;
    private Label? _bookingTableCount;
    private Label? _bookingMeaning;
    private const string BookingDefaultMeaning = "High ego = more demanding / headline-sensitive. Stars show intensity, not universal quality.";
    private BookingSortField _bookingSort = BookingSortField.Price;
    private bool _bookingDescending;
    private int? _bookingGenre;
    private readonly BookingDragButton[] _bookingSlots = new BookingDragButton[3];
    private readonly Button[] _bookingRemove = new Button[3];
    private readonly Label[] _bookingHints = new Label[3];
    private Label? _bookingStatus;
    private string? _bookingSelected;
    private string _bookingDurableMessage = "Select a band, then activate a set. Dragging also works.";
    private bool _bookingLayoutPending;
    private async void ScheduleBookingLayout()
    {
        if (_bookingLayoutPending || _bookingLane is null) return;
        _bookingLayoutPending = true;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _bookingLayoutPending = false;
        if (!GetViewport().GuiIsDragging()) LayoutOwnedPerkWorkspace();
    }
    private string[] BookingIds => _session.CapturePreparationPlan()?.ActIds is { Length: 3 } plan ? plan :
        _session.CaptureProgramme()?.ActIds is { Length: 3 } booked ? booked : ["", "", ""];
    private bool BookingLocked => _session.PreparedStatus != PreparationStatus.Preparing;
    private static string BookingTime(int ticks) => $"{ticks / 80 / 60:00}:{ticks / 80 % 60:00}";
    private Variant BookingPayload(string id)
    {
        if (BookingLocked) return default;
        return new Godot.Collections.Dictionary { ["kind"] = "festival-band-v1", ["act"] = id, ["source"] = Array.IndexOf(BookingIds, id) };
    }
    private Variant BeginBookingDrag(string id)
    {
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
        _bookingSelected = id; _bookingDurableMessage = $"Selected {_session.GetFestivalActs().Single(a => a.Id == id).Name}. Choose a set; Escape cancels.";
        RefreshBookingControls();
    }
    private bool PreviewBookingDrop(int slot, Variant payload)
    {
        if (!ReadBookingPayload(payload, out var id, out var source)) return false;
        var preview = _session.PreviewLineupEdit(id, source, slot);
        _bookingHints[slot].Text = preview.Message;
        _bookingSlots[slot].AddThemeStyleboxOverride("normal", HudStyle(new Color(preview.IsValid ? BookingIds[slot] == "" ? "d3e8df" : "e5d5aa" : "eee2be"), 7));
        return preview.IsValid;
    }
    private void CommitBookingDrop(int slot, Variant payload, bool remove = false)
    {
        if (!ReadBookingPayload(payload, out var id, out var source)) { _bookingDurableMessage = "Invalid band payload; lineup retained."; RefreshBookingControls(); return; }
        var preview = _session.PreviewLineupEdit(id, source, slot, remove);
        if (!preview.IsValid || preview.IsNoOp) { _bookingDurableMessage = preview.Message; RefreshBookingControls(); return; }
        var priorHash = _session.CaptureSnapshot().AuthoritativeHash;
        CommitEquipmentAction(new SetProgrammeCommand(preview.ActIds));
        var changed = _session.CaptureSnapshot().AuthoritativeHash != priorHash;
        _bookingDurableMessage = changed ? preview.Message + " · Unpaid plan updated; next timed save pending." : _preparationMessage;
        if (changed) _bookingSelected = null;
        RefreshBookingControls();
    }
    private static float BookingColumnMin(int column) => column switch
    { 0 => 238, 1 => 96, 2 => 68, 3 => 136, 4 => 136, 5 => 150, _ => throw new ArgumentOutOfRangeException(nameof(column)) };
    private static Label BookingText(string text, int size)
    { var label = HudLabel(text, size); label.MouseFilter = Control.MouseFilterEnum.Ignore; return label; }
    private static Control.SizeFlags BookingColumnFlags(int column) => column is 0 or 5 ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill;
    private static VBoxContainer BookingCell(HBoxContainer parent, int column)
    {
        var cell = new VBoxContainer { CustomMinimumSize = new Vector2(BookingColumnMin(column), 0), SizeFlagsHorizontal = BookingColumnFlags(column), Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        cell.AddThemeConstantOverride("separation", 0); parent.AddChild(cell); return cell;
    }
    private static string BookingStars(int score) => $"{BookingTableView.HalfStarUnits(score) / 2.0:0.0}";
    private static string BookingExactTooltip(FestivalAct act) =>
        $"{act.Name} · {FestivalGenreName(act.Genre)} · {FestivalCurrency.Format(act.PricePennies)}\n" +
        $"Popularity {act.Popularity}/100 · {BookingStars(act.Popularity)} stars · audience appeal (existing display role)\n" +
        $"Ego {act.Ego}/100 · {BookingStars(act.Ego)} stars · higher means more demanding/headline-sensitive, not quality\n" +
        $"Professionalism {act.Professionalism}/100 · {BookingStars(act.Professionalism)} stars · softens ego disappointment, not immunity" +
        (act.Ego >= 70 ? "\nExpects to headline; non-headline performance still allowed." : "");
    private void ShowBookingExactScores(FestivalAct act)
    {
        if (_bookingMeaning is not null) _bookingMeaning.Text =
            $"{act.Name} · Popularity {act.Popularity}/100 appeal · Ego {act.Ego}/100 demanding, not quality · Professionalism {act.Professionalism}/100 softens ego, not immunity";
    }
    private BookingStarRating BookingRatingCell(HBoxContainer parent, int column, FestivalAct act, string metric, int score, string meaning, Color colour)
    {
        var cell = BookingCell(parent, column);
        cell.MouseFilter = Control.MouseFilterEnum.Pass;
        var content = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        content.AddThemeConstantOverride("separation", 1); cell.AddChild(content);
        var stars = new BookingStarRating { Score = score, Ink = colour, MouseFilter = Control.MouseFilterEnum.Pass, FocusMode = Control.FocusModeEnum.All };
        content.AddChild(stars);
        var exact = $"{act.Name} — {metric} {score}/100 · {BookingStars(score)} stars · {meaning}";
        stars.TooltipText = exact;
        cell.TooltipText = exact;
        stars.FocusEntered += () => { if (_bookingMeaning is not null) _bookingMeaning.Text = exact; };
        stars.MouseEntered += () => { if (_bookingMeaning is not null) _bookingMeaning.Text = exact; };
        stars.FocusExited += () => { if (_bookingMeaning is not null) _bookingMeaning.Text = BookingDefaultMeaning; };
        stars.MouseExited += () => { if (!stars.HasFocus() && _bookingMeaning is not null) _bookingMeaning.Text = BookingDefaultMeaning; };
        return stars;
    }
    private void BuildBookingControls(VBoxContainer parent)
    {
        _programmeControls = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; parent.AddChild(_programmeControls);
        var columns = new HBoxContainer(); columns.AddThemeConstantOverride("separation", 24); _programmeControls.AddChild(columns);
        var lineup = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        columns.AddChild(lineup);
        var table = new VBoxContainer { CustomMinimumSize = new Vector2(824, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.7f };
        table.AddThemeConstantOverride("separation", 0); columns.AddChild(table);
        table.AddChild(HudLabel("Available bands", 22));
        var filters = new HBoxContainer { CustomMinimumSize = new Vector2(0, 34) }; filters.AddThemeConstantOverride("separation", 12); table.AddChild(filters);
        _bookingGenreFilter = new OptionButton { CustomMinimumSize = new Vector2(150, 32), FocusMode = Control.FocusModeEnum.All };
        foreach (var option in new[] { "All genres", "Folk", "Rock", "Pop", "Electronic" }) _bookingGenreFilter.AddItem(option);
        _bookingGenreFilter.ItemSelected += index =>
        {
            if (GetViewport().GuiIsDragging()) { _bookingGenreFilter.Select(_bookingGenre is { } g ? g + 1 : 0); return; }
            _bookingGenre = index == 0 ? null : (int)index - 1;
            RefreshBookingControls();
        };
        filters.AddChild(_bookingGenreFilter);
        _bookingTableCount = HudLabel("", 12); filters.AddChild(_bookingTableCount);
        var header = new HBoxContainer { CustomMinimumSize = new Vector2(824, 36) }; header.AddThemeConstantOverride("separation", 0); table.AddChild(header);
        var labels = new[] { "Band", "Genre", "Price", "Popularity", "Ego", "Professionalism" };
        var meanings = new[] { "Band name", "Genre", "Price in pounds", "Audience appeal", "Demandingness; higher means more demanding/headline-sensitive, not quality", "Softens ego reaction; does not prevent it" };
        for (var column = 0; column < labels.Length; column++)
        {
            var index = column;
            var button = new Button { CustomMinimumSize = new Vector2(BookingColumnMin(index), 36), SizeFlagsHorizontal = BookingColumnFlags(index), FocusMode = Control.FocusModeEnum.All };
            button.Pressed += () =>
            {
                if (GetViewport().GuiIsDragging()) return;
                var field = (BookingSortField)index;
                if (_bookingSort == field) _bookingDescending = !_bookingDescending;
                else { _bookingSort = field; _bookingDescending = false; }
                RefreshBookingControls();
            };
            var copy = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; copy.AddThemeConstantOverride("separation", 0);
            button.AddChild(copy); copy.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var title = BookingText(labels[index], 14); copy.AddChild(title); _bookingHeaderTitles[index] = title;
            var meaning = meanings[index];
            button.FocusEntered += () => { if (_bookingMeaning is not null) _bookingMeaning.Text = $"{labels[index]} — {meaning}"; };
            button.MouseEntered += () => { if (_bookingMeaning is not null) _bookingMeaning.Text = $"{labels[index]} — {meaning}"; };
            button.FocusExited += () => { if (_bookingMeaning is not null) _bookingMeaning.Text = BookingDefaultMeaning; };
            button.MouseExited += () => { if (!button.HasFocus() && _bookingMeaning is not null) _bookingMeaning.Text = BookingDefaultMeaning; };
            header.AddChild(button); _bookingHeaders[index] = button;
        }
        _bookingTableBody = new VBoxContainer { CustomMinimumSize = new Vector2(824, 252), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _bookingTableBody.AddThemeConstantOverride("separation", 0); table.AddChild(_bookingTableBody);
        foreach (var act in _session.GetFestivalActs())
        {
            var id = act.Id;
            var card = new BookingDragButton { Text = "", CustomMinimumSize = new Vector2(824, 42), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.All };
            _bookingTableBody.AddChild(card); _bookingCards.Add(id, card);
            card.DragPreviewText = () => $"{act.Name} · {FestivalGenreName(act.Genre)} · {FestivalCurrency.Format(act.PricePennies)}";
            var cells = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; cells.AddThemeConstantOverride("separation", 0);
            card.AddChild(cells); cells.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var band = BookingCell(cells, 0);
            var name = BookingText(act.Name, 14); band.AddChild(name);
            var detail = BookingText("", 12); band.AddChild(detail);
            _bookingRowCopy.Add(id, (name, detail));
            BookingCell(cells, 1).AddChild(BookingText(FestivalGenreName(act.Genre), 13));
            BookingCell(cells, 2).AddChild(BookingText(FestivalCurrency.Format(act.PricePennies), 13));
            var popularity = BookingRatingCell(cells, 3, act, "Popularity", act.Popularity, "Audience appeal", new Color("293b38"));
            var ego = BookingRatingCell(cells, 4, act, "Ego", act.Ego, "Higher means more demanding/headline-sensitive, not quality", new Color("795336"));
            var professionalism = BookingRatingCell(cells, 5, act, "Professionalism", act.Professionalism, "Softens ego reaction; does not prevent it", new Color("293b38"));
            _bookingRatings.Add(id, (popularity, ego, professionalism));
            card.DragPayload = () => BeginBookingDrag(id); card.Pressed += () => SelectBookingBand(id);
            card.GuiInput += input => HandleBookingKey(card, input, () => SelectBookingBand(id));
            card.FocusEntered += () => ShowBookingExactScores(act);
            card.MouseEntered += () => ShowBookingExactScores(act);
            card.FocusExited += () => { if (_bookingMeaning is not null) _bookingMeaning.Text = BookingDefaultMeaning; };
            card.MouseExited += () => { if (!card.HasFocus() && _bookingMeaning is not null) _bookingMeaning.Text = BookingDefaultMeaning; };
            card.TooltipText = BookingExactTooltip(act);
        }
        _bookingMeaning = HudLabel(BookingDefaultMeaning, 12);
        table.AddChild(_bookingMeaning);
        var heading = HudLabel("Trailer Stage", 24); heading.AddThemeFontOverride("font", HearingSerif()); lineup.AddChild(heading);
        lineup.AddChild(HudLabel("Elapsed festival time · mm:ss", 13));
        _bookingLane = new Control { CustomMinimumSize = new Vector2(354, 300), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Stop }; lineup.AddChild(_bookingLane);
        var backdrop = new ColorRect { Color = new Color("eee2be"), Position = new Vector2(54, 0), Size = new Vector2(300, 300), MouseFilter = Control.MouseFilterEnum.Stop }; _bookingLane.AddChild(backdrop);
        var secondsToLanePixels = 300f / (GameSession.PreparedDayTicks / 80f);
        for (var slot = 0; slot < 3; slot++)
        {
            var i = slot; float top = GameSession.FestivalSlotStarts[i] / 80f * secondsToLanePixels;
            float height = (GameSession.FestivalSlotEnds[i] - GameSession.FestivalSlotStarts[i]) / 80f * secondsToLanePixels;
            var time = HudLabel(BookingTime(GameSession.FestivalSlotStarts[i]), 13); time.Position = new Vector2(0, top); time.Size = new Vector2(50, 20); _bookingLane.AddChild(time);
            var end = HudLabel(BookingTime(GameSession.FestivalSlotEnds[i]), 13); end.Position = new Vector2(0, top + height - 5); end.Size = new Vector2(50, 20); _bookingLane.AddChild(end);
            var target = new BookingDragButton { Position = new Vector2(54, top), Size = new Vector2(300, height), Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.All }; target.AddThemeFontSizeOverride("font_size", 14); _bookingLane.AddChild(target); _bookingSlots[i] = target;
            target.AcceptPayload = data => PreviewBookingDrop(i, data); target.CommitPayload = data => CommitBookingDrop(i, data);
            target.DragPayload = () => BookingIds[i] == "" ? default : BeginBookingDrag(BookingIds[i]);
            void Activate() { if (_bookingSelected is { } id) CommitBookingDrop(i, BookingPayload(id)); else if (BookingIds[i] != "") SelectBookingBand(BookingIds[i]); }
            target.Pressed += Activate;
            target.GuiInput += input => HandleBookingKey(target, input, Activate);
            var remove = new Button { Text = "×", Position = new Vector2(316, top + 2), Size = new Vector2(32, 32), CustomMinimumSize = new Vector2(32, 32) }; remove.Pressed += () => { if (BookingIds[i] != "") CommitBookingDrop(i, BookingPayload(BookingIds[i]), true); }; _bookingLane.AddChild(remove); _bookingRemove[i] = remove;
            var hint = HudLabel("", 12); hint.Position = new Vector2(61, top + height - 21); hint.Size = new Vector2(280, 21); hint.MouseFilter = Control.MouseFilterEnum.Ignore; _bookingLane.AddChild(hint); _bookingHints[i] = hint;
            if (i < 2)
            {
                var gap = HudLabel($"Changeover · {(GameSession.FestivalSlotStarts[i + 1] - GameSession.FestivalSlotEnds[i]) / 80}s · no drop", 12); gap.Position = new Vector2(61, top + height + 2); gap.Size = new Vector2(285, 24); _bookingLane.AddChild(gap);
            }
        }
        void FitStageLane()
        {
            var laneWidth = Math.Max(300, _bookingLane.Size.X - 54);
            backdrop.Size = new Vector2(laneWidth, 300);
            for (var i = 0; i < 3; i++)
            {
                _bookingSlots[i].Size = new Vector2(laneWidth, _bookingSlots[i].Size.Y);
                _bookingRemove[i].Position = new Vector2(54 + laneWidth - 38, _bookingRemove[i].Position.Y);
                _bookingHints[i].Size = new Vector2(laneWidth - 20, _bookingHints[i].Size.Y);
            }
        }
        _bookingLane.Resized += FitStageLane;
        FitStageLane();
        _programmeSummary = HudLabel("", 14); _programmeSummary.Visible = false; _programmeControls.AddChild(_programmeSummary);
        _bookingStatus = HudLabel("", 13); _bookingStatus.Visible = false; _programmeControls.AddChild(_bookingStatus);
        // Kept for older diagnostic capture routing; not a second user payment control.
        _programmeBook = ButtonText("SAVE LINEUP", () => CommitEquipmentAction(new SetProgrammeCommand(_programmeDraft))); _programmeBook.Visible = false; _programmeControls.AddChild(_programmeBook);
        RefreshBookingControls();
    }
    private void HandleBookingKey(Control control, InputEvent input, Action activate)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.Escape)
        {
            GetViewport().GuiCancelDrag(); _bookingSelected = null; _bookingDurableMessage = "Selection cancelled; lineup retained.";
            RefreshBookingControls(); control.AcceptEvent();
        }
        else if (key.Keycode is Key.Enter or Key.Space)
        { if (!BookingLocked) activate(); control.AcceptEvent(); }
    }
    private void RefreshBookingControls()
    {
        if (_bookingLane is null || GetViewport().GuiIsDragging()) return;
        var acts = _session.GetFestivalActs().ToArray(); var ids = BookingIds; _programmeDraft = ids.ToArray();
        var projected = BookingTableView.Project(acts, _bookingGenre, _bookingSort, _bookingDescending);
        for (var index = 0; index < projected.Length; index++) _bookingTableBody!.MoveChild(_bookingCards[projected[index].Id], index);
        _bookingTableCount!.Text = $"{projected.Length} of {acts.Length} bands · {_bookingSort} {(_bookingDescending ? "↓" : "↑")} · select a row, then a set";
        for (var index = 0; index < _bookingHeaders.Length; index++)
        {
            var active = _bookingSort == (BookingSortField)index;
            _bookingHeaderTitles[index].Text = new[] { "Band", "Genre", "Price", "Popularity", "Ego", "Professionalism" }[index] + (active ? _bookingDescending ? " ↓" : " ↑" : " ↕");
            var meaning = index switch { 3 => " Audience appeal.", 4 => " Demandingness; higher means more demanding/headline-sensitive, not quality.", 5 => " Softens ego reaction; does not prevent it.", _ => "" };
            _bookingHeaders[index].TooltipText = (active ? $"Sorted {(_bookingDescending ? "descending" : "ascending")}. Activate to reverse." : "Activate to sort ascending; activate again to reverse.") + meaning;
        }
        foreach (var act in acts)
        {
            var card = _bookingCards[act.Id]; var assigned = Array.IndexOf(ids, act.Id);
            card.Visible = _bookingGenre is null || act.Genre == _bookingGenre;
            _bookingRowCopy[act.Id].Detail.Text = (assigned >= 0 ? $"Assigned · Set {assigned + 1}" : "Available") + (act.Ego >= 70 ? " · Expects to headline" : "");
            card.Disabled = BookingLocked; card.AddThemeStyleboxOverride("normal", HudStyle(new Color(_bookingSelected == act.Id ? "e5d5aa" : "fff6df"), 7));
        }
        if (_bookingSelected is { } selected && !_bookingCards[selected].Visible)
            _bookingDurableMessage = $"{acts.Single(a => a.Id == selected).Name} selected · hidden by genre filter; choose a set or show its genre.";
        for (var i = 0; i < 3; i++)
        {
            var act = acts.SingleOrDefault(a => a.Id == ids[i]);
            _bookingSlots[i].Text = act is null ? $"Set {i + 1} · Drop a band here\n{BookingTime(GameSession.FestivalSlotStarts[i])}–{BookingTime(GameSession.FestivalSlotEnds[i])}" :
                $"{act.Name}\n{FestivalGenreName(act.Genre)} · {BookingTime(GameSession.FestivalSlotStarts[i])}–{BookingTime(GameSession.FestivalSlotEnds[i])}";
            _bookingSlots[i].Disabled = BookingLocked;
            _bookingSlots[i].AddThemeStyleboxOverride("normal", HudStyle(new Color(act is null ? "fff6df" : "d3e8df"), 7));
            _bookingHints[i].Text = _bookingSelected is { } selectedAct ? _session.PreviewLineupEdit(selectedAct, Array.IndexOf(ids, selectedAct), i).Message : act?.Ego >= 70 && i != 2 ? "Expects to headline · disappointment applies" : "";
            _bookingRemove[i].Visible = act is not null; _bookingRemove[i].Disabled = BookingLocked;
            _bookingRemove[i].TooltipText = act is null ? "Empty set" : $"Remove {act.Name} from Set {i + 1}";
        }
        _programmeSummary!.Text = $"{ids.Count(id => id != "")} of 3 sets filled · Lineup {FestivalCurrency.Format(ids.Where(id => id != "").Sum(id => acts.Single(a => a.Id == id).PricePennies))} · Paid at Start";
        _bookingStatus!.Text = BookingLocked ? "Programme locked · final paid lineup. Admission reactions apply once." : _bookingDurableMessage;
        if (!BookingLocked && _hudStatus is not null) _hudStatus.Text = _bookingDurableMessage;
    }
}

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
    private readonly BookingDragButton[] _bookingSlots = new BookingDragButton[3];
    private readonly Button[] _bookingRemove = new Button[3];
    private readonly Label[] _bookingHints = new Label[3];
    private Label? _bookingStatus;
    private string? _bookingSelected;
    private string _bookingDurableMessage = "Select a band, then activate a set. Dragging also works.";
    private ScrollContainer? _bookingListScroll;
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
        var prior = _session;
        CommitEquipmentAction(new SetProgrammeCommand(preview.ActIds));
        _bookingDurableMessage = ReferenceEquals(prior, _session) ? _preparationMessage : preview.Message + " · Unpaid plan saved.";
        if (!ReferenceEquals(prior, _session)) _bookingSelected = null;
        RefreshBookingControls();
    }
    private void BuildBookingControls(VBoxContainer parent)
    {
        _programmeControls = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; parent.AddChild(_programmeControls);
        var columns = new HBoxContainer(); columns.AddThemeConstantOverride("separation", 16); _programmeControls.AddChild(columns);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(250, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; columns.AddChild(left);
        left.AddChild(HudLabel("Available bands", 22)); left.AddChild(HudLabel("Drag a band, or select then choose a set.", 13));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(250, 310), FollowFocus = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; _bookingListScroll = scroll; left.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; scroll.AddChild(list);
        foreach (var act in _session.GetFestivalActs())
        {
            var id = act.Id;
            var card = new BookingDragButton { Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(250, 112), FocusMode = Control.FocusModeEnum.All };
            card.AddThemeFontSizeOverride("font_size", 14); list.AddChild(card); _bookingCards.Add(id, card);
            card.DragPayload = () => BeginBookingDrag(id); card.Pressed += () => SelectBookingBand(id);
            card.GuiInput += input => HandleBookingKey(card, input, () => SelectBookingBand(id));
        }
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(354, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; columns.AddChild(right);
        var heading = HudLabel("Main stage", 24); heading.AddThemeFontOverride("font", HearingSerif()); right.AddChild(heading);
        right.AddChild(HudLabel("Elapsed festival time · mm:ss", 13));
        _bookingLane = new Control { CustomMinimumSize = new Vector2(354, 300), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Stop }; right.AddChild(_bookingLane);
        var backdrop = new ColorRect { Color = new Color("eee2be"), Position = new Vector2(54, 0), Size = new Vector2(300, 300), MouseFilter = Control.MouseFilterEnum.Stop }; _bookingLane.AddChild(backdrop);
        for (var slot = 0; slot < 3; slot++)
        {
            var i = slot; float top = GameSession.FestivalSlotStarts[i] / 80f;
            float height = (GameSession.FestivalSlotEnds[i] - GameSession.FestivalSlotStarts[i]) / 80f;
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
        foreach (var act in acts)
        {
            var card = _bookingCards[act.Id]; var assigned = Array.IndexOf(ids, act.Id);
            card.Text = $"{act.Name} · {FestivalCurrency.Format(act.PricePennies)}\n{FestivalGenreName(act.Genre)}{(assigned >= 0 ? $" · Set {assigned + 1}" : "")}\nPopularity {act.Popularity}/100\nEgo {act.Ego}/100 · Professionalism {act.Professionalism}/100\n{(act.Ego >= 70 ? "Expects headline Set 3" : "No headline expectation")}";
            card.TooltipText = act.Ego >= 70 ? "Expects headline Set 3. In Set 1 or 2, performers lose admission satisfaction; professionalism reduces the penalty. Provisional tuning." : "No headline disappointment below Ego 70. Guest appraisal uses existing genre affinity equally across three paid acts. Provisional tuning.";
            card.Disabled = BookingLocked; card.AddThemeStyleboxOverride("normal", HudStyle(new Color(_bookingSelected == act.Id ? "e5d5aa" : "fff6df"), 7));
        }
        for (var i = 0; i < 3; i++)
        {
            var act = acts.SingleOrDefault(a => a.Id == ids[i]);
            _bookingSlots[i].Text = act is null ? $"Set {i + 1} · Drop a band here\n{BookingTime(GameSession.FestivalSlotStarts[i])}–{BookingTime(GameSession.FestivalSlotEnds[i])}" :
                $"{act.Name}\n{FestivalGenreName(act.Genre)} · {BookingTime(GameSession.FestivalSlotStarts[i])}–{BookingTime(GameSession.FestivalSlotEnds[i])}";
            _bookingSlots[i].Disabled = BookingLocked;
            _bookingSlots[i].AddThemeStyleboxOverride("normal", HudStyle(new Color(act is null ? "fff6df" : "d3e8df"), 7));
            _bookingHints[i].Text = _bookingSelected is { } selected ? _session.PreviewLineupEdit(selected, Array.IndexOf(ids, selected), i).Message : act?.Ego >= 70 && i != 2 ? "Expects headline · disappointment applies" : "";
            _bookingRemove[i].Visible = act is not null; _bookingRemove[i].Disabled = BookingLocked;
            _bookingRemove[i].TooltipText = act is null ? "Empty set" : $"Remove {act.Name} from Set {i + 1}";
        }
        _programmeSummary!.Text = $"{ids.Count(id => id != "")} of 3 sets filled · Lineup {FestivalCurrency.Format(ids.Where(id => id != "").Sum(id => acts.Single(a => a.Id == id).PricePennies))} · Paid at Start";
        _bookingStatus!.Text = BookingLocked ? "Programme locked · final paid lineup. Admission reactions apply once." : _bookingDurableMessage;
        if (!BookingLocked && _hudStatus is not null) _hudStatus.Text = _bookingDurableMessage;
    }
}

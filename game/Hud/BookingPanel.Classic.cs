using Festival.Simulation;
using Godot;
using System;

namespace Festival.Game;

internal sealed partial class BookingPanel
{
    private void BuildCompactRunningOrder(HBoxContainer parent)
    {
        var column = new VBoxContainer { CustomMinimumSize = Ui.S(258, 0) };
        column.AddThemeConstantOverride("separation", Ui.Px(10)); parent.AddChild(column);
        column.AddChild(Ui.Heading("Running order", 24));
        _stageLine = Ui.Text("Trailer Stage · three sets", 16, Ui.InkMuted); column.AddChild(_stageLine);
        _stageTabs = new HBoxContainer(); column.AddChild(_stageTabs);
        _bookingLane = new Control { CustomMinimumSize = Ui.S(258, 274), MouseFilter = Control.MouseFilterEnum.Pass };
        column.AddChild(_bookingLane);
        for (var slot = 0; slot < 3; slot++)
        {
            var i = slot;
            var target = new BookingDragButton { Name = "ClassicSet" + (i + 1), Position = Ui.S(0, i * 92), Size = Ui.S(258, 88),
                FocusMode = Control.FocusModeEnum.All, MouseDefaultCursorShape = Control.CursorShape.PointingHand, ClipContents = true };
            _bookingLane.AddChild(target);
            var words = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            target.AddChild(words); words.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            words.OffsetLeft = Ui.S(12); words.OffsetRight = -Ui.S(12); words.OffsetTop = Ui.S(8);
            var caption = Ui.Text("", 13, Ui.InkMuted); words.AddChild(caption);
            var title = Ui.Heading("", 20); title.ClipText = true; words.AddChild(title);
            var detail = Ui.Text("", 15, Ui.Ink); words.AddChild(detail);
            var remove = Ui.Style(new Button { Name = "ClassicRemove" + (i + 1), Text = "Remove ×", Position = Ui.S(164, 57), Size = Ui.S(84, 28),
                MouseDefaultCursorShape = Control.CursorShape.PointingHand }, Ui.ButtonKind.Quiet, 14);
            target.AddChild(remove);
            remove.Pressed += () => { if (BookingIds[i] != "") CommitBookingDrop(i, BookingPayload(BookingIds[i]), true); };
            target.AcceptPayload = data => PreviewBookingDrop(i, data); target.CommitPayload = data => CommitBookingDrop(i, data);
            target.DragPayload = () => BookingIds[i] == "" ? default : BeginBookingDrag(BookingIds[i]);
            void Activate() { if (_bookingSelected is { } id) CommitBookingDrop(i, BookingPayload(id)); else if (BookingIds[i] != "") SelectBookingBand(BookingIds[i]); }
            target.Pressed += Activate; target.GuiInput += input => HandleBookingKey(target, input, Activate);
            _sets[i] = new(target, caption, title, detail, remove);
        }
        var help = Ui.Text("Select an act, then a set. Dragging also works.", 16, Ui.InkMuted);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart; column.AddChild(help);
    }
}

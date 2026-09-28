using Godot;
using System;

namespace Festival.Game;

public partial class BookingDragButton : Button
{
    public Func<Variant>? DragPayload;
    public Func<string>? DragPreviewText;
    public Func<Variant, bool>? AcceptPayload;
    public Action<Variant>? CommitPayload;
    public override Variant _GetDragData(Vector2 atPosition)
    {
        var data = DragPayload?.Invoke() ?? default;
        if (data.VariantType == Variant.Type.Nil) return data;
        var ghost = new Label { Text = DragPreviewText?.Invoke() ?? Text, CustomMinimumSize = new Vector2(250, 60) };
        ghost.AddThemeColorOverride("font_color", new Color("293b38"));
        var panel = new PanelContainer(); panel.AddChild(ghost);
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("fff3d3"), ContentMarginLeft = 8, ContentMarginRight = 8 });
        SetDragPreview(panel);
        return data;
    }
    public override bool _CanDropData(Vector2 atPosition, Variant data) => AcceptPayload?.Invoke(data) == true;
    public override void _DropData(Vector2 atPosition, Variant data) => CommitPayload?.Invoke(data);
}

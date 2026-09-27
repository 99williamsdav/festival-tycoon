using Festival.Simulation;
using Godot;
using System;

namespace Festival.Game;

public partial class Main
{
    private MeshInstance3D _hoverHighlight = null!;
    private ulong _hoveredColliderId;
    private Input.CursorShape _feedbackCursor;
    private PopupMenu? _hoverPopup;
    private OptionButton? _hoverPopupOwner;
    private void RegisterHoverPopup(OptionButton choice)
    {
        var popup=choice.GetPopup();
        popup.AboutToPopup+=()=>{_hoverPopup=popup;_hoverPopupOwner=choice;};
        popup.PopupHide+=()=>{if(_hoverPopup==popup){_hoverPopup=null;_hoverPopupOwner=null;}};
    }
    private bool HoverPopupActive => _hoverPopup is not null && GodotObject.IsInstanceValid(_hoverPopup) && _hoverPopup.Visible;

    private void BuildHoverFeedback()
    {
        _hoverHighlight = new MeshInstance3D {
            Mesh = new TorusMesh { InnerRadius = .94f, OuterRadius = 1f, Rings = 32, RingSegments = 8 },
            MaterialOverride = new StandardMaterial3D {
                AlbedoColor = new Color(.72f,.92f,.85f,.65f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }, Visible = false };
        AddChild(_hoverHighlight);
    }

    // The nearest physical hit, including rotated/collapsed person capsules, is
    // shared by click and hover. No screen-distance or second-hit fallback.
    private CollisionObject3D? ResolveWorldHit(Vector2 screen)
    {
        var origin = _camera.ProjectRayOrigin(screen);
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + _camera.ProjectRayNormal(screen) * 250);
        query.CollisionMask = 1;
        var result = _camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (!result.ContainsKey("collider")) return null;
        var collider = result["collider"].AsGodotObject() as CollisionObject3D;
        if (collider is null || !GodotObject.IsInstanceValid(collider) || collider.IsQueuedForDeletion() || !collider.IsVisibleInTree()) return null;
        var key = collider.GetInstanceId();
        if (_attendeePickRegistry.TryGetValue(key,out var person))
            return _attendeeVisuals.TryGetValue(person,out var body) && body.IsVisibleInTree() && !body.IsQueuedForDeletion() ? collider : null;
        if (_immersionVendorPicks.TryGetValue(key,out var vendor))
            return _immersionVendors.TryGetValue(vendor,out var body) && body.IsVisibleInTree() && !body.IsQueuedForDeletion() ? collider : null;
        if (_securityPostPickId != 0 && key == _securityPostPickId) return collider;
        if (_medicalFacilityPicks.ContainsKey(key)) return collider;
        if (_pickRegistry.TryGetValue(key,out var item))
            return _visualRegistry.TryGetValue(item.StableId,out var body) && body.IsVisibleInTree() && !body.IsQueuedForDeletion() ? collider : null;
        return null;
    }

    private bool WorldInputOccluded(Vector2 screen)
    {
        if (_startSplash is not null || _hudStartConfirmation?.Visible == true || _hearingShade?.Visible == true || HoverPopupActive) return true;
        if (_perkPanel?.IsVisibleInTree() == true && _perkPanel.GetGlobalRect().HasPoint(screen)) return true;
        if (_hudMoney is not null && HudBlocksPlacement(screen)) return true;
        // Godot resolves Ignore/Pass/Stop and child ordering; a label inside a
        // panel still blocks the world through its receiving ancestor.
        if (screen.IsEqualApprox(GetViewport().GetMousePosition()))
            for (var node = GetViewport().GuiGetHoveredControl(); node is not null; node = node.GetParent() as Control)
                if (node.MouseFilter != Control.MouseFilterEnum.Ignore && node.IsVisibleInTree()) return true;
        return false;
    }

    private void UpdateHoverFeedback(Vector2 screen)
    {
        _hoveredColliderId = 0; _hoverHighlight.Visible = false;
        var cursor = Input.CursorShape.Arrow;
        var hovered = GetViewport().GuiGetHoveredControl();
        for (var node = hovered; node is not null; node = node.GetParent() as Control)
            if (node is ScrollBar bar)
            {
                var usable=bar.MaxValue-bar.MinValue>bar.Page;
                bar.MouseDefaultCursorShape=usable?Control.CursorShape.PointingHand:Control.CursorShape.Arrow;
                cursor=usable?Input.CursorShape.PointingHand:Input.CursorShape.Arrow;
                break;
            }
            else if (node is BaseButton button)
            {
                button.MouseDefaultCursorShape = button.Disabled ? Control.CursorShape.Arrow : Control.CursorShape.PointingHand;
                if (!button.Disabled) cursor = Input.CursorShape.PointingHand;
                break;
            }
        if (!WorldInputOccluded(screen))
        {
            if (_waterPlacementMode != WaterPlacementMode.None)
                cursor = _waterPlacementCandidate is not null && _waterPlacementIssue is null ? Input.CursorShape.Cross : Input.CursorShape.Forbidden;
            else if (_placingImmersionVendor is not null)
                cursor = _immersionCandidate is not null && _immersionPlacementIssue is null ? Input.CursorShape.Cross : Input.CursorShape.Forbidden;
            else if (ResolveWorldHit(screen) is { } collider)
            {
                var key = collider.GetInstanceId(); _hoveredColliderId = key;
                var point = collider.GlobalPosition; var radius = 1.7f;
                if (_attendeePickRegistry.TryGetValue(key,out var person)) { point = _attendeeVisuals[person].GlobalPosition; radius = .78f; }
                else if (_pickRegistry.TryGetValue(key,out var item))
                {
                    point = _visualRegistry[item.StableId].GlobalPosition;
                    radius = item.Kind switch { FarmObjectKind.LargeBarn => 11.8f, FarmObjectKind.SmallBarn => 8.6f,
                        FarmObjectKind.Farmhouse => 7.6f, FarmObjectKind.TrailerStage => 6.3f, _ => 3.8f };
                }
                _hoverHighlight.Position = new Vector3(point.X,.14f,point.Z);
                _hoverHighlight.Scale = new Vector3(radius,.3f,radius); _hoverHighlight.Visible = true;
                cursor = Input.CursorShape.PointingHand;
            }
        }
        if (hovered is TabBar tabs)
        {
            var index = tabs.GetTabIdxAtPoint(screen - tabs.GlobalPosition);
            tabs.MouseDefaultCursorShape = index >= 0 && !tabs.IsTabDisabled(index) ? Control.CursorShape.PointingHand : Control.CursorShape.Arrow;
            if(index>=0 && !tabs.IsTabDisabled(index))cursor=Input.CursorShape.PointingHand;
        }
        if(HoverPopupActive)
        {
            // Current registered menus contain only enabled choices. Godot has
            // no public row hit-test: any disabled/separator menu conservatively
            // uses Arrow rather than guessing which row is under the pointer.
            var popup=_hoverPopup!;var actionable=_hoverPopupOwner?.Disabled==false && popup.ItemCount>0;
            for(var i=0;actionable && i<popup.ItemCount;i++)actionable=!popup.IsItemDisabled(i) && !popup.IsItemSeparator(i);
            cursor=actionable && new Rect2(Vector2.Zero,popup.Size).HasPoint(popup.GetMousePosition())?Input.CursorShape.PointingHand:Input.CursorShape.Arrow;
        }
        _feedbackCursor = cursor; Input.SetDefaultCursorShape(cursor);
    }
}

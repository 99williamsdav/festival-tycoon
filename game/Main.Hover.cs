using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;

namespace Festival.Game;

public partial class Main
{
    private MeshInstance3D _hoverHighlight = null!;
    private ulong _hoveredColliderId;
    private Input.CursorShape _feedbackCursor;
    private PopupMenu? _hoverPopup;
    private OptionButton? _hoverPopupOwner;
    private readonly Dictionary<ulong,(Vector3 Centre,Vector3 Scale)> _buildingHoverGeometry=[];
    private (Vector3 Centre,Vector3 Scale) BuildingHoverGeometry(Node3D body)
    {
        var key=body.GetInstanceId();if(_buildingHoverGeometry.TryGetValue(key,out var cached))return cached;
        var min=new Vector3(float.PositiveInfinity,0,float.PositiveInfinity);var max=new Vector3(float.NegativeInfinity,0,float.NegativeInfinity);
        void Inspect(Node node)
        {
            if(node is MeshInstance3D mesh && mesh.Mesh is not null)
            {
                var box=mesh.GetAabb();
                for(var x=0;x<2;x++)for(var y=0;y<2;y++)for(var z=0;z<2;z++)
                {
                    var point=body.ToLocal(mesh.ToGlobal(box.Position+box.Size*new Vector3(x,y,z)));
                    min.X=Mathf.Min(min.X,point.X);min.Z=Mathf.Min(min.Z,point.Z);max.X=Mathf.Max(max.X,point.X);max.Z=Mathf.Max(max.Z,point.Z);
                }
            }
            foreach(var child in node.GetChildren())Inspect(child);
        }
        Inspect(body);
        // sqrt(2) times each half-extent encloses all corners, with a small margin.
        var geometry=((min+max)/2,new Vector3((max.X-min.X)*.73f,.3f,(max.Z-min.Z)*.73f));
        _buildingHoverGeometry[key]=geometry;return geometry;
    }
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
        if (_toiletPickOwners.TryGetValue(key, out var toiletId)) return _toiletViews.TryGetValue(toiletId, out var view) &&
            view.Body.IsVisibleInTree() && !view.Body.IsQueuedForDeletion() ? collider : null;
        if (_securityPostPickId != 0 && key == _securityPostPickId) return collider;
        if (_generatorPickId != 0 && key == _generatorPickId)
            return _equipmentVisual is { } generator && generator.IsVisibleInTree() && !generator.IsQueuedForDeletion() ? collider : null;
        if (_medicalFacilityPicks.ContainsKey(key)) return collider;
        if (_pickRegistry.TryGetValue(key,out var item))
            return _visualRegistry.TryGetValue(item.StableId,out var body) && body.IsVisibleInTree() && !body.IsQueuedForDeletion() ? collider : null;
        return null;
    }

    private bool WorldInputOccluded(Vector2 screen)
    {
        if (_festivalPaper is not null || _startSplash is not null || _hudStartConfirmation?.Visible == true || _hearingShade?.Visible == true || HoverPopupActive) return true;
        if (_perkPanel?.IsVisibleInTree() == true && _perkPanel.GetGlobalRect().HasPoint(screen)) return true;
        if (_ownedEffectPopup?.IsVisibleInTree() == true && _ownedEffectPopup.GetGlobalRect().HasPoint(screen)) return true;
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
            if (ResolveWorldHit(screen) is { } collider)
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
                var scale=new Vector3(radius,.3f,radius);var yaw=0f;
                if(_immersionVendorPicks.TryGetValue(key,out var vendorId))
                {
                    var body=_immersionVendors[vendorId];yaw=body.Rotation.Y;
                    var geometry=BuildingHoverGeometry(body);point=body.ToGlobal(geometry.Centre);scale=geometry.Scale;
                }
                else if (_toiletPickOwners.TryGetValue(key, out var toiletId) && _toiletViews.TryGetValue(toiletId, out var toiletView))
                { point = toiletView.Body.GlobalPosition; yaw = toiletView.Body.Rotation.Y; scale = new Vector3(1.5f,.3f,1.7f); }
                else if(key==_securityPostPickId){var post=_responsePostVisuals[ResponseRole.Steward];var geometry=BuildingHoverGeometry(post);yaw=post.Rotation.Y;point=post.ToGlobal(geometry.Centre);scale=geometry.Scale;}
                else if(key==_generatorPickId && _equipmentVisual is { } generator){var geometry=BuildingHoverGeometry(generator);yaw=generator.Rotation.Y;point=generator.ToGlobal(geometry.Centre);scale=geometry.Scale;}
                else if(_medicalFacilityPicks.TryGetValue(key,out var medical) && medical.Facility==MedicalFacility.FirstAid)
                {var tent=_responsePostVisuals[ResponseRole.Medic];var geometry=BuildingHoverGeometry(tent);yaw=tent.Rotation.Y;point=tent.ToGlobal(geometry.Centre);scale=geometry.Scale;}
                _hoverHighlight.Position = new Vector3(point.X,.14f,point.Z);
                _hoverHighlight.Rotation=new(0,yaw,0);_hoverHighlight.Scale=scale; _hoverHighlight.Visible = true;
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

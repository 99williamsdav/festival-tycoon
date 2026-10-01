using Festival.ContentAdapter;
using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using NVector = System.Numerics.Vector3;

namespace Festival.Game;

public partial class Main
{
    private const string CleanupAssets = "res://assets/characters/steward-cleanup-v1/";
    private sealed class CleanupView
    {
        public required Node3D Actor, Root, Body, Picker, Bag, Jaw;
        public required CleanupRig Rig;
        public required Dictionary<string, Node3D> Pivots;
        public MeshInstance3D? Waste;
        public ImmersionProduct? Product;
    }
    private readonly Dictionary<EntityId, CleanupView> _cleanupViews = [];
    private readonly HashSet<string> _cleanupLiftedWaste = new(StringComparer.Ordinal);
    private readonly Dictionary<ImmersionProduct, Mesh> _cleanupWasteMeshes = [];
    private readonly Dictionary<string, CleanupRig> _cleanupRigs = [];
    private GameSession? _cleanupSession;
    private int _cleanupAttempt;
    private static Vector3 CleanupVector(NVector v) => new(v.X, v.Y, v.Z);
    private static NVector CleanupVector(Vector3 v) => new(v.X, v.Y, v.Z);
    private static Transform3D CleanupTransform(CleanupJointTransform t) => new(
        new Basis(new Quaternion(t.Rotation.X, t.Rotation.Y, t.Rotation.Z, t.Rotation.W)), CleanupVector(t.Position));
    private static void CleanupOrdinaryBody(Node3D actor, bool visible)
    {
        if (actor.GetNodeOrNull<Node3D>("RoleBody") is { } body) body.Visible = visible;
        if (actor.GetNodeOrNull<Node3D>("RoleGarment") is { } garment) garment.Visible = visible;
    }
    private static void StowCleanupView(CleanupView view)
    {
        if (GodotObject.IsInstanceValid(view.Root)) view.Root.Visible = false;
        if (view.Waste is not null && GodotObject.IsInstanceValid(view.Waste)) view.Waste.Visible = false;
        if (GodotObject.IsInstanceValid(view.Actor)) CleanupOrdinaryBody(view.Actor, true);
    }
    private void ResetStewardCleanupPresentation()
    {
        foreach (var view in _cleanupViews.Values)
        {
            StowCleanupView(view);
            if (GodotObject.IsInstanceValid(view.Root)) view.Root.QueueFree();
        }
        _cleanupViews.Clear(); _cleanupLiftedWaste.Clear();
    }
    private CleanupView CreateCleanupView(Node3D actor)
    {
        var variant = actor.GetMeta("RoleVariant").AsString();
        if (!_cleanupRigs.TryGetValue(variant, out var rig))
            _cleanupRigs[variant] = rig = CleanupRig.Parse(Godot.FileAccess.GetFileAsString(CleanupAssets + "manifest.json"), variant);
        var root = new Node3D { Name = "StewardCleanup" }; actor.AddChild(root);
        var body = InstantiateAsset(CleanupAssets + $"lwf_steward_cleanup_{variant}_v1.glb"); root.AddChild(body);
        Bodies.ApplyRoleBodyPalette(actor, body);
        var picker = InstantiateAsset(CleanupAssets + "lwf_steward_litter_picker_v1.glb"); root.AddChild(picker);
        var bag = InstantiateAsset(CleanupAssets + "lwf_steward_bin_bag_v1.glb"); root.AddChild(bag);
        var names = new[] { "UpperPivot", "LeftUpperPivot", "LeftForePivot", "LeftHandPivot", "RightUpperPivot", "RightForePivot", "RightHandPivot" };
        return new() { Actor = actor, Root = root, Body = body, Picker = picker, Bag = bag,
            Jaw = (Node3D)picker.FindChild("PickerJawSocket", true, false), Rig = rig,
            Pivots = names.ToDictionary(n => n, n => (Node3D)body.FindChild(n, true, false)) };
    }
    private static void ApplyCleanupPose(CleanupView view, CleanupPoseFrame frame)
    {
        view.Pivots["UpperPivot"].Transform = CleanupTransform(frame.Hip);
        void Arm(string side, CleanupArmPose pose)
        {
            view.Pivots[side + "UpperPivot"].Transform = CleanupTransform(pose.Upper);
            view.Pivots[side + "ForePivot"].Transform = CleanupTransform(pose.Fore);
            view.Pivots[side + "HandPivot"].Transform = CleanupTransform(pose.Hand);
        }
        Arm("Left", frame.Left); Arm("Right", frame.Right);
        view.Picker.Transform = CleanupTransform(frame.Picker); view.Bag.Transform = CleanupTransform(frame.Bag);
    }
    private void AdvanceStewardCleanupPresentation()
    {
        var attempt = _session.CapturePreparation()?.Attempt ?? 0;
        if (_cleanupSession != _session || _cleanupAttempt != attempt)
        { ResetStewardCleanupPresentation(); _cleanupSession = _session; _cleanupAttempt = attempt; }
        _cleanupLiftedWaste.Clear();
        foreach (var (id, view) in _cleanupViews.ToArray())
        {
            if (!_attendeeVisuals.TryGetValue(id, out var actor) || actor != view.Actor || !GodotObject.IsInstanceValid(view.Root))
            {
                StowCleanupView(view);
                if (GodotObject.IsInstanceValid(view.Root)) view.Root.QueueFree();
                _cleanupViews.Remove(id);
            }
            else StowCleanupView(view);
        }
        foreach (var steward in _session.GetStewardResponses())
        {
            var id = new EntityId(steward.WorkerId);
            var visual = StewardCleanupPresentation.Read(_session, steward.WorkerId);
            if (visual.Mode == StewardCleanupMode.Stowed || !_attendeeVisuals.TryGetValue(id, out var actor) ||
                !actor.HasMeta("RoleKey") || actor.GetMeta("RoleKey").AsString() != "steward") continue;
            if (!_cleanupViews.TryGetValue(id, out var view)) _cleanupViews[id] = view = CreateCleanupView(actor);
            view.Root.Visible = true; CleanupOrdinaryBody(actor, false);
            view.Root.Transform = Transform3D.Identity;
            var contact = new Vector3(0, 0, -.2f); var ground = Transform3D.Identity; var centre = Vector3.Zero;
            if (visual.Waste is { } waste)
            {
                ground = GroundWasteTransform(waste, new(waste.XMillimetres / 1000f, 0, waste.ZMillimetres / 1000f));
                centre = new Vector3(0, waste.Product == ImmersionProduct.Chips ? .025f : .075f, 0);
                var worldContact = ground * centre;
                var footing = _session.CaptureCleanupFooting(steward.WorkerId)!.Value;
                var feet = new Vector3(footing.XMillimetres / 1000f, .04f, footing.ZMillimetres / 1000f);
                var offset = worldContact - feet;
                // Turn only the visual child. Normalizing the target direction makes near-foot contact
                // reachable for both fitted rigs and reconstructs identically after loading.
                var yaw = offset.X * offset.X + offset.Z * offset.Z > .0000001f ? Mathf.Atan2(-offset.X, -offset.Z) : 0;
                var heading = new Basis(Vector3.Up, yaw);
                view.Root.GlobalTransform = new(heading, actor.GlobalPosition);
                // Read the reach from authoritative feet, so the final interpolated arrival step
                // cannot change the heading or joint pose compared with a fresh load.
                contact = heading.Inverse() * offset;
            }
            var frame = StewardCleanupKinematics.Evaluate(view.Rig,
                visual.Mode == StewardCleanupMode.GroundPickup ? visual.Progress : -1, CleanupVector(contact));
            // Fail safely on a contact outside the fitted reach envelope: retain ground representation.
            var fitted = frame.Left.Reachable && frame.Right.Reachable;
            if (!fitted)
                frame = StewardCleanupKinematics.Evaluate(view.Rig, -1, new(0, 0, -.2f));
            ApplyCleanupPose(view, frame);
            if (visual.LiftedWaste && fitted && visual.Waste is not null)
            {
                var picked = visual.Waste!;
                if (view.Waste is null || view.Product != picked.Product)
                {
                    if (view.Waste is not null) { view.Waste.Visible = false; view.Waste.QueueFree(); }
                    if (!_cleanupWasteMeshes.TryGetValue(picked.Product, out var mesh))
                        _cleanupWasteMeshes[picked.Product] = mesh = LoadLitterMesh(LitterAsset(picked.Product));
                    view.Waste = new MeshInstance3D { Name = "PickedWaste", Mesh = mesh, MaterialOverride = _sharedLitterMaterial };
                    view.Root.AddChild(view.Waste); view.Product = picked.Product;
                }
                view.Waste.Visible = true;
                view.Waste.GlobalTransform = new(ground.Basis, view.Jaw.GlobalPosition - ground.Basis * centre);
                _cleanupLiftedWaste.Add(picked.Id);
            }
        }
    }
}

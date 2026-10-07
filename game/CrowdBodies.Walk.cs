using Festival.Simulation;
using Godot;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// Guests walk, stand, carry and drink on a rigged body: walk cycles played at the speed they're actually moving (a
/// gentle walk when slow, a brisk festival walk at their usual pace), so their feet stay planted; the same with a cup
/// in hand, or a tray of food flat on the palm; and a sip while they drink or a bite while they eat. Hair and hats ride
/// on the head bone, and a held cup or tray on the hand.
/// </summary>
internal sealed partial class CrowdBodies
{
    private const string RigMeta = "GuestRigged", WalkPlayerMeta = "GuestWalkPlayer", RigModeMeta = "GuestRigMode", CupSocket = "LWF_RightHand_Cup", FoodSocket = "LWF_RightHand_Food";
    // Metres a body covers per second at 1× playback, from the rig's measured strides.
    private static float WalkSpeedAt1x(string sex) => sex == "male" ? 1.200f : 1.164f;
    private const float BriskSpeedAt1x = 1.70f, BriskFrom = 1.45f, MovingFrom = .12f, HurrySpeedAt1x = 2.40f, HurryFrom = 2.05f;
    private static string RiggedFile(string sex) => $"lwf_attendee_{sex}_rigged_test_v1.glb";
    /// <summary>A staff member's rigged body, garment and all; null until it exists, when they keep their still poses.</summary>
    private static string? RiggedRoleFile(string role, string sex) =>
        role is "medic" or "steward" or "maintenance" or "sound" && ResourceLoader.Exists($"res://assets/characters/lwf_{role}_{sex}_rigged_test_v1.glb")
            ? $"lwf_{role}_{sex}_rigged_test_v1.glb" : null;
    private static readonly string[] LoopedClips = ["walk", "idle", "walk_brisk", "walk_carry", "idle_carry", "walk_brisk_carry", "drink", "drink_soft", "carry_litter", "walk_hurry",
        "walk_food", "idle_food", "walk_brisk_food", "eat"];

    // Guests and staff share the rig; their roots name the body, the variant and the pose a little differently.
    private static Node3D? RigBody(Node3D root) => root.GetNodeOrNull<Node3D>("GuestBody") ?? root.GetNodeOrNull<Node3D>("RoleBody");
    private static string RigSex(Node3D root) => (root.HasMeta("GuestPoseVariant") ? root.GetMeta("GuestPoseVariant") : root.GetMeta("RoleVariant")).AsString();
    private static string RigState(Node3D root) => (root.HasMeta("GuestPoseState") ? root.GetMeta("GuestPoseState") : root.GetMeta("RolePoseState")).AsString();

    /// <summary>Hands free, holding a cup or a tray of food, drinking or eating: the rig.</summary>
    private static bool UsesRig(string state, ImmersionProduct? product) => state is "relaxed" or "drink_hold" or "drinking" or "food_hold" or "eating";

    /// <summary>A rigged body ready with its clips.</summary>
    private Node3D RiggedBody(string sex, string? file = null)
    {
        file ??= RiggedFile(sex);
        if (!_guestPoseScenes.TryGetValue(file, out var scene))
        {
            scene = GD.Load<PackedScene>($"res://assets/characters/{file}");
            _guestPoseScenes.Add(file, scene);
        }
        var body = scene.Instantiate<Node3D>();
        if (body.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>().FirstOrDefault() is { } player)
        {
            foreach (var name in LoopedClips)
                if (player.HasAnimation(name)) player.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
            player.SetMeta(WalkPlayerMeta, true);
            player.Play("idle");
        }
        return body;
    }

    /// <summary>What the rig is doing with its hands: free, carrying a cup, drinking, or taking an empty to the bin.</summary>
    public void SetRigActivity(Node3D root, bool drinking, bool litter, ImmersionProduct? product)
    {
        if (!root.HasMeta(RigMeta) || !root.GetMeta(RigMeta).AsBool()) return;
        var state = RigState(root);
        root.SetMeta(RigModeMeta, state == "relaxed" ? "free" : litter ? "litter" : product == ImmersionProduct.Chips ? drinking ? "eat" : "food" :
            drinking ? product == ImmersionProduct.Beer ? "drink" : "drink_soft" : "carry");
    }

    /// <summary>Puts the head's pieces on the head bone of a rigged body, or back on the root for a still one.</summary>
    private static void SyncGuestHead(Node3D root)
    {
        if (root.FindChild(HeadPieces, true, false) is not Node3D head) return;
        var body = RigBody(root);
        var skeleton = body?.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().FirstOrDefault();
        var bone = skeleton?.FindBone("Head") ?? -1;
        if (skeleton is null || bone < 0)
        {
            if (head.GetParent() != root) { head.GetParent().RemoveChild(head); root.AddChild(head); }
            head.Transform = Transform3D.Identity;
            return;
        }
        var attachment = skeleton.GetNodeOrNull<BoneAttachment3D>("HeadAttachment");
        if (attachment is null)
        {
            attachment = new BoneAttachment3D { Name = "HeadAttachment", BoneName = "Head" };
            skeleton.AddChild(attachment);
        }
        if (head.GetParent() != attachment) { head.GetParent().RemoveChild(head); attachment.AddChild(head); }
        // The pieces are modelled in the body's own space, so undo where the head bone sits at rest.
        var skeletonInRoot = root.GlobalTransform.AffineInverse() * skeleton.GlobalTransform;
        head.Transform = (skeletonInRoot * skeleton.GetBoneGlobalRest(bone)).AffineInverse();
    }

    /// <summary>Keeps a held cup in a rigged hand; false for a still body, whose pose anchors place it instead.</summary>
    private static bool AnchorPropToRig(Node3D root, Node3D prop, string product)
    {
        // Food rides flat on the palm; a cup, or anything empty on its way to a bin, in the cup grip.
        var food = product == "chips" && !prop.HasMeta("EmptyWasteProp");
        if (!root.HasMeta(RigMeta) || !root.GetMeta(RigMeta).AsBool() ||
            RigBody(root)?.FindChild(food ? FoodSocket : CupSocket, true, false) is not Node3D socket || !socket.IsInsideTree()) return false;
        // An empty cup on its way to a bin hangs from the low hand, so it's turned upright there.
        var turn = prop.HasMeta("EmptyWasteProp") ? new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2), Vector3.Zero) : Transform3D.Identity;
        prop.GlobalTransform = socket.GlobalTransform * turn;
        prop.Scale = Vector3.One;
        return true;
    }

    /// <summary>Plays a rigged guest's clip for what they're doing, at the pace they're really moving; paused, they hold still.</summary>
    public void AnimateGuest(Node3D root, float metresPerSecond, bool paused)
    {
        if (!root.HasMeta(RigMeta) || !root.GetMeta(RigMeta).AsBool()) return;
        if (RigBody(root)?.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>().FirstOrDefault() is not { } player) return;
        var mode = root.HasMeta(RigModeMeta) ? root.GetMeta(RigModeMeta).AsString() : "free";
        var moving = metresPerSecond > MovingFrom;
        var brisk = metresPerSecond >= BriskFrom;
        // Staff on duty keep a quicker pace than the crowd: a hurry once they're going faster than any stroll.
        var hurry = metresPerSecond >= HurryFrom && player.HasAnimation("walk_hurry");
        var clip = (mode, moving) switch
        {
            ("free", false) => "idle",
            ("free", true) => hurry ? "walk_hurry" : brisk ? "walk_brisk" : "walk",
            ("litter", false) => "idle_carry",
            ("litter", true) => "carry_litter",
            ("drink" or "drink_soft" or "eat", false) => mode,
            ("food", false) => "idle_food",
            ("food" or "eat", true) => brisk ? "walk_brisk_food" : "walk_food",
            (_, false) => "idle_carry",
            _ => brisk ? "walk_brisk_carry" : "walk_carry",
        };
        if (!player.HasAnimation(clip)) clip = moving ? "walk" : "idle";
        if (player.CurrentAnimation != clip) player.Play(clip, customBlend: .2);
        var sex = RigSex(root);
        var walkingClip = clip.StartsWith("walk", System.StringComparison.Ordinal) || clip == "carry_litter";
        var natural = clip == "walk_hurry" ? HurrySpeedAt1x : clip.Contains("brisk") ? BriskSpeedAt1x : WalkSpeedAt1x(sex);
        player.SpeedScale = paused ? 0 : walkingClip ? Mathf.Clamp(metresPerSecond / natural, .05f, 8f) : 1;
    }
}

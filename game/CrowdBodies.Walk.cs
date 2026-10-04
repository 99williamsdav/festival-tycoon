using Festival.Simulation;
using Godot;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// Guests walk, stand, carry and drink on a rigged body: walk cycles played at the speed they're actually moving (a
/// gentle walk when slow, a brisk festival walk at their usual pace), so their feet stay planted; the same with a cup
/// in hand; and a sip while they drink. Hair and hats ride on the head bone and a held cup on the hand. Someone with a
/// tray of chips keeps that pose's still body for now.
/// </summary>
internal sealed partial class CrowdBodies
{
    private const string RigMeta = "GuestRigged", WalkPlayerMeta = "GuestWalkPlayer", RigModeMeta = "GuestRigMode", CupSocket = "LWF_RightHand_Cup";
    // Metres a body covers per second at 1× playback, from the rig's measured strides.
    private static float WalkSpeedAt1x(string sex) => sex == "male" ? 0.942f : 0.916f;
    private const float BriskSpeedAt1x = 1.70f, BriskFrom = 1.25f, MovingFrom = .12f;
    private static string RiggedFile(string sex) => $"lwf_attendee_{sex}_rigged_test_v1.glb";
    private static readonly string[] LoopedClips = ["walk", "idle", "walk_brisk", "walk_carry", "idle_carry", "walk_brisk_carry", "drink", "drink_soft", "carry_litter"];

    /// <summary>Hands free, holding a cup or drinking from one: the rig. A tray of chips: the still pose.</summary>
    private static bool UsesRig(string state, ImmersionProduct? product) =>
        state == "relaxed" || state is "drink_hold" or "drinking" && product is not ImmersionProduct.Chips;

    /// <summary>A rigged body ready with its clips.</summary>
    private Node3D RiggedBody(string sex)
    {
        var file = RiggedFile(sex);
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
        var state = root.GetMeta("GuestPoseState").AsString();
        root.SetMeta(RigModeMeta, state == "relaxed" ? "free" : litter ? "litter" : drinking ? product == ImmersionProduct.Beer ? "drink" : "drink_soft" : "carry");
    }

    /// <summary>Puts the head's pieces on the head bone of a rigged body, or back on the root for a still one.</summary>
    private static void SyncGuestHead(Node3D root)
    {
        if (root.FindChild(HeadPieces, true, false) is not Node3D head) return;
        var body = root.GetNodeOrNull<Node3D>("GuestBody");
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
    private static bool AnchorPropToRig(Node3D root, Node3D prop)
    {
        if (!root.HasMeta(RigMeta) || !root.GetMeta(RigMeta).AsBool() ||
            root.GetNodeOrNull<Node3D>("GuestBody")?.FindChild(CupSocket, true, false) is not Node3D socket || !socket.IsInsideTree()) return false;
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
        if (root.GetNodeOrNull<Node3D>("GuestBody")?.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>().FirstOrDefault() is not { } player) return;
        var mode = root.HasMeta(RigModeMeta) ? root.GetMeta(RigModeMeta).AsString() : "free";
        var moving = metresPerSecond > MovingFrom;
        var brisk = metresPerSecond >= BriskFrom;
        var clip = (mode, moving) switch
        {
            ("free", false) => "idle",
            ("free", true) => brisk ? "walk_brisk" : "walk",
            ("litter", false) => "idle_carry",
            ("litter", true) => "carry_litter",
            ("drink" or "drink_soft", false) => mode,
            (_, false) => "idle_carry",
            _ => brisk ? "walk_brisk_carry" : "walk_carry",
        };
        if (!player.HasAnimation(clip)) clip = moving ? "walk" : "idle";
        if (player.CurrentAnimation != clip) player.Play(clip, customBlend: .2);
        var sex = root.GetMeta("GuestPoseVariant").AsString();
        var walkingClip = clip.StartsWith("walk", System.StringComparison.Ordinal) || clip == "carry_litter";
        var natural = clip.Contains("brisk") ? BriskSpeedAt1x : WalkSpeedAt1x(sex);
        player.SpeedScale = paused ? 0 : walkingClip ? Mathf.Clamp(metresPerSecond / natural, .05f, 8f) : 1;
    }
}

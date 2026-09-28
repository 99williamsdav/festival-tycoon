using Festival.ContentAdapter;
using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private string? _roleCaptureDirectory;
    private int _roleCaptureFrame;
    private readonly List<Node3D> _rolePreviewRoots = [];
    private string _roleCaptureHash = "";

    private void RoleCaptureCheck(bool valid, string reason)
    { if (!valid) throw new InvalidOperationException("ROLE_CAPTURE_ASSERT " + reason); }

    private void RoleCaptureImage(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        RoleCaptureCheck(image.GetWidth() == GetWindow().Size.X && image.GetHeight() == GetWindow().Size.Y,
            "Screenshot dimensions differ from requested native window.");
        var error = image.SavePng(Path.Combine(_roleCaptureDirectory!, name + ".png"));
        RoleCaptureCheck(error == Error.Ok, "Could not save " + name + ": " + error);
    }

    private void VerifyActualRoleRoots()
    {
        var people = _session.CapturePreparation()!.People.Where(p => p.Role is ProtectedPersonRole.Staff or ProtectedPersonRole.Performer).ToArray();
        RoleCaptureCheck(people.Count(p => p.Role == ProtectedPersonRole.Performer) == 9,
            "The fixed three-set band roster changed.");
        RoleCaptureCheck(people.Where(p => p.Role == ProtectedPersonRole.Staff).Select(RoleKey)
            .Order(StringComparer.Ordinal).SequenceEqual(new[] { "maintenance", "medic", "sound", "steward" }),
            "Expected paid sound, maintenance, medic and steward identities are absent.");
        foreach (var person in people)
        {
            var id = new EntityId(person.AgentId);
            RoleCaptureCheck(_attendeeVisuals.TryGetValue(id, out var root) && root.HasMeta("RoleVariant"),
                "Missing stable role root for " + person.AgentId);
            var variant = AttendeePose.Variant(_session.CampaignSeed, person.AgentId);
            RoleCaptureCheck(root!.GetMeta("RoleVariant").AsString() == variant &&
                root.GetMeta("RoleKey").AsString() == RoleKey(person) &&
                root.GetNodeOrNull<Node3D>("RoleBody") is not null &&
                (person.Role == ProtectedPersonRole.Performer) == (root.GetNodeOrNull<Node3D>("RoleGarment") is null),
                "Role, deterministic variant, body or garment mismatch for " + person.AgentId);
            RoleCaptureCheck(root.GetChildren().OfType<StaticBody3D>().Count() == 1 &&
                _attendeePickRegistry.Values.Count(item => item == id) == 1,
                "Role body lost the single stable person picking capsule.");
            if (RoleKey(person) == "maintenance")
                RoleCaptureCheck(root.FindChildren("*", "Label3D", true, false).OfType<Label3D>()
                    .Any(label => label.Text.Contains("MAINTENANCE", StringComparison.Ordinal) &&
                        label.Text.Contains(person.Name.Split(' ')[0].ToUpperInvariant(), StringComparison.Ordinal)),
                    "Maintenance worker lost the existing named world label.");
        }
        var live = _session.CaptureLivePerformance()!;
        RoleCaptureCheck(live.Stage == LiveSetStage.Live && live.Performers.Count(p => p.OnStage) == 3,
            "Actual first set did not place all three booked performers on deck.");
        RoleCaptureCheck(_performerInstruments.Count == 3, "Actual attached performer kits are missing.");
        foreach (var performer in live.Performers.Where(p => p.OnStage))
        {
            var id = new EntityId(performer.AgentId);
            var root = _attendeeVisuals[id];
            var kit = _performerInstruments[id];
            RoleCaptureCheck(kit.GetParent() == root && kit.FindChildren("*PlayingArm", "MeshInstance3D", true, false).Count == 2,
                "Playing kit is not owned by its performer or lacks two fitted arms.");
            RoleCaptureCheck(root.FindChildren("*Idle*Arm", "MeshInstance3D", true, false)
                .OfType<MeshInstance3D>().Count(mesh => !mesh.Visible) == 2,
                "Idle arms remained visible through the playing kit.");
            var players = kit.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>().ToArray();
            RoleCaptureCheck(players.Length == 1 && players[0].IsPlaying() &&
                players[0].GetAnimation("Animation").LoopMode == Animation.LoopModeEnum.Linear,
                "The person-owned playing animation is not running cyclically.");
        }
        RoleCaptureCheck(_stageDrumKit is not null &&
            _stageDrumKit.FindChildren("*Arm*", "MeshInstance3D", true, false).Count == 0,
            "Fixed stage drum hardware retains floating person arms.");
        GD.Print($"ROLE_ACTUAL staff=4 booked_performers=9 on_stage=3 attached_kits=3 stable_pick_capsules=True deterministic_variants=True hardware_only=True hash={_session.CaptureSnapshot().AuthoritativeHash}");
    }

    private void AddRolePreview(string role, string variant, int ordinal, Vector3 position)
    {
        var root = CreateRoleBodyRoot(role, variant, ordinal % 4, ulong.MaxValue - (ulong)ordinal, position);
        root.AddChild(new Label3D { Text = role.ToUpperInvariant() + " · " + variant.ToUpperInvariant(),
            Position = new Vector3(0, 2.1f, 0), FontSize = 27, PixelSize = .008f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
        if (role is "guitarist" or "bassist" or "drummer")
        {
            var index = role == "guitarist" ? 0 : role == "bassist" ? 1 : 2;
            var kit = InstantiateAsset(PerformerKitPath(index, variant));
            kit.Name = "RolePreviewKit";
            ApplyRolePlayingArmPalette(root, kit);
            root.AddChild(kit);
            SetNeutralArmsVisible(root, false);
        }
        RoleCaptureCheck(root.GetNodeOrNull<Node3D>("RoleBody") is not null &&
            (role is "guitarist" or "bassist" or "drummer" || root.GetNodeOrNull<Node3D>("RoleGarment") is not null),
            "Preview actor missing source body or garment.");
        _rolePreviewRoots.Add(root);
    }

    private void ExerciseRoleHeldPoses(Node3D root)
    {
        var id = new EntityId(root.GetMeta("RolePersonId").AsUInt64());
        var variant = root.GetMeta("RoleVariant").AsString();
        var garment = root.GetNodeOrNull<Node3D>("RoleGarment");
        var hair = root.GetMeta("RoleHair").AsInt32();
        if (root.GetNodeOrNull<Node3D>("RolePreviewKit") is { } kit)
        {
            root.RemoveChild(kit); kit.QueueFree();
            SetNeutralArmsVisible(root, true);
        }
        foreach (var (state, product, key) in new[] {
            ("drink_hold", ImmersionProduct.SoftDrink, "soft"),
            ("drinking", ImmersionProduct.SoftDrink, "soft"),
            ("food_hold", ImmersionProduct.Chips, "chips"),
            ("eating", ImmersionProduct.Chips, "chips") })
        {
            SetRoleBodyPose(root, state, product);
            SetImmersionHeldVisual(id, root, key, true, 0, 0);
            var pose = GuestPoseCatalog.Get(variant, state, product);
            var anchor = pose.Anchors[key == "chips" ? "tray" : key];
            var prop = _immersionHeldVisuals[id];
            RoleCaptureCheck(root.GetNodeOrNull<Node3D>("RoleBody") is not null &&
                root.GetMeta("RolePoseFile").AsString() == pose.File &&
                root.GetMeta("RoleHair").AsInt32() == hair &&
                root.GetNodeOrNull<Node3D>("RoleGarment") == garment &&
                prop.GetParent() == root &&
                prop.Position.IsEqualApprox(new Vector3(anchor.X, anchor.Y, anchor.Z)) &&
                prop.RotationDegrees.IsEqualApprox(new Vector3(anchor.RotationX, anchor.RotationY, anchor.RotationZ)) &&
                prop.Scale == Vector3.One,
                "Role pose, stable garment/hair or approved held-item anchor disagrees: " + root.Name + "/" + state);
        }
        // An interrupted hand state removes the visual item but never loses its
        // stable role garment; resumed eligibility reconstructs the same anchor.
        SetImmersionHeldVisual(id, root, "chips", false, 0, 0);
        RoleCaptureCheck(!_immersionHeldVisuals.ContainsKey(id) && root.GetNodeOrNull<Node3D>("RoleGarment") == garment,
            "Interrupted role item did not suspend without losing its garment.");
        SetImmersionHeldVisual(id, root, "chips", true, 0, 0);
        RoleCaptureCheck(_immersionHeldVisuals.ContainsKey(id), "Role item did not resume.");
        SetImmersionHeldVisual(id, root, null, false, 0, 0);
        SetRoleBodyPose(root, "relaxed", null);
        if (root.GetMeta("RoleKey").AsString() is "guitarist" or "bassist" or "drummer")
            RoleCaptureCheck(root.GetMeta("RolePoseFile").AsString().StartsWith("lwf_performer_", StringComparison.Ordinal) &&
                !_immersionHeldVisuals.ContainsKey(id), "Offstage performer did not restore modular neutral body before playing.");
    }

    private void ProcessRoleCapture()
    {
        if (_roleCaptureDirectory is null || ++_roleCaptureFrame % 4 != 0) return;
        try
        {
            switch (_roleCaptureFrame / 4)
            {
                case 1:
                    RoleCaptureCheck(_stageDrumKit is not null && _performerInstruments.Count == 0 &&
                        _stageDrumKit.FindChildren("*Arm*", "MeshInstance3D", true, false).Count == 0,
                        "Pre-show fixed drum hardware has person arms or an attached absent drummer.");
                    GD.Print("ROLE_PRESTART_DRUM hardware_only=True drummer_absent=True arms=0 sticks=0");
                    var perk = _session.CapturePerks()!;
                    CommitEquipmentAction(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
                    CommitEquipmentAction(new SetProgrammeCommand(["act.meadow-lanterns", "act.neon-postcards", "act.field-frequency"]));
                    foreach (var offer in new[] { "staff.steward", "maintenance.worker", "equipment.buy" }) PreparationAccept(offer);
                    PreparationStart();
                    RoleCaptureCheck(_session.PreparedStatus == PreparationStatus.Running, "Could not start approved role proof: " + _preparationMessage);
                    TimetableAdvanceTo(2200);
                    StaffCaptureSend(new SetPausedCommand(true));
                    _foundationPresentation.Reset(_session.CaptureObservation());
                    AdvancePreparationPresentation(0);
                    VerifyActualRoleRoots();
                    _roleCaptureHash = _session.CaptureSnapshot().AuthoritativeHash;
                    _focus = new Vector3(-16, 0, 11); _camera.Size = 21; ApplyCamera();
                    break;
                case 2:
                    RoleCaptureImage("01-actual-stage-and-staff");
                    foreach (var visual in _attendeeVisuals.Values) visual.Visible = false;
                    for (var i = 0; i < 4; i++)
                        foreach (var (variant, side) in new[] { ("male", -1), ("female", 1) })
                            AddRolePreview(new[] { "medic", "steward", "maintenance", "sound" }[i], variant, i * 2 + (side + 1) / 2,
                                new Vector3((i - 1.5f) * 3.3f, .04f, side * 1.2f));
                    _focus = Vector3.Zero; _camera.Size = 20; ApplyCamera();
                    break;
                case 3:
                    RoleCaptureImage("02-both-body-variants-all-staff-workwear");
                    foreach (var root in _rolePreviewRoots) ExerciseRoleHeldPoses(root);
                    GD.Print("ROLE_HELD_STAFF variants=8 poses=drink_hold,drinking,food_hold,eating anchors=body_specific garment_stable=True interruption_resume=True");
                    foreach (var root in _rolePreviewRoots) root.QueueFree();
                    _rolePreviewRoots.Clear();
                    for (var i = 0; i < 3; i++)
                        foreach (var (variant, side) in new[] { ("male", -1), ("female", 1) })
                            AddRolePreview(new[] { "guitarist", "bassist", "drummer" }[i], variant, i * 2 + (side + 1) / 2,
                                new Vector3((i - 1f) * 3.5f, .04f, side * 1.2f));
                    break;
                case 4:
                    RoleCaptureImage("03-both-body-variants-all-band-kits");
                    foreach (var root in _rolePreviewRoots) ExerciseRoleHeldPoses(root);
                    GD.Print("ROLE_HELD_PERFORMER variants=6 offstage_poses=drink_hold,drinking,food_hold,eating anchors=body_specific neutral_modular_restored=True interruption_resume=True");
                    RoleCaptureCheck(_roleCaptureHash == _session.CaptureSnapshot().AuthoritativeHash,
                        "Role previews, held poses or paused presentation changed authoritative gameplay.");
                    GD.Print($"ROLE_CAPTURE_COMPLETE exact_current_hash={_roleCaptureHash} sizes={GetWindow().Size} source_roots=stable preview_variants=14 role_held_poses=True cosmetic_hash_pure=True");
                    GetTree().Quit();
                    break;
            }
        }
        catch (Exception error)
        {
            GD.PushError("ROLE_CAPTURE_FAILED " + error);
            _roleCaptureDirectory = null;
            GetTree().Quit(2);
        }
    }
}

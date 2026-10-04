using Festival.ContentAdapter;
using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Festival.Game;

/// <summary>
/// Builds and re-poses people's bodies: guests from the pose catalogue in one of 16 palette
/// variants, staff and performers as role bodies with their garment and role palette. Pose and
/// material scenes are cached. <c>performerRole</c> gives a performer's instrument slot and
/// <c>playing</c> whether their kit is attached, which hides the idle arms.
/// </summary>
internal sealed partial class CrowdBodies(Node _parent, Func<GameSession> _session, Func<ulong, string, int> _performerRole, Func<EntityId, bool> _playing)
{
    /// <summary>Re-poses a guest or role body; unchanged poses keep their scene.</summary>
    public void SetPose(Node3D root, string state, ImmersionProduct? product)
    {
        if (root.HasMeta("GuestPoseVariant")) SetGuestBodyPose(root, state, product);
        else if (root.HasMeta("RoleVariant")) SetRoleBodyPose(root, state, product);
    }

    /// <summary>Places a held prop at the current pose's hand anchor.</summary>
    public void AnchorProp(Node3D root, Node3D prop, string product)
    {
        if (root.HasMeta("GuestPoseVariant")) ApplyGuestPropAnchor(root, prop, product);
        else if (root.HasMeta("RoleVariant")) ApplyRolePropAnchor(root, prop, product);
    }

    private AttendeePalette? _guestPaletteContract;
    private readonly Dictionary<ulong, string> _guestMaterialCompatibility = [];
    private readonly Dictionary<string, StandardMaterial3D[]> _guestPaletteMaterials = [];
    private AttendeePalette GuestPaletteContract => _guestPaletteContract ??=
        AttendeePalette.Parse(Godot.FileAccess.GetFileAsString("res://assets/characters/attendee_palette_v1_contract.json"));

    private static byte[] GuestPaletteBytes(StandardMaterial3D material)
    {
        using var image = material.AlbedoTexture.GetImage();
        if (image.IsCompressed()) image.Decompress();
        image.Convert(Image.Format.Rgba8);
        // Imported textures may append generated mip levels. The contract edits
        // only the 96x8 base image; never include those extra bytes as swatches.
        image.ClearMipmaps();
        if (image.GetWidth() != AttendeePalette.Width || image.GetHeight() != AttendeePalette.Height)
            throw new InvalidOperationException("Guest body palette dimensions differ from designer contract.");
        return image.GetData();
    }

    private static string GuestMaterialSettings(StandardMaterial3D material, bool includeSampling = true)
    {
        var settings = new StringBuilder();
        foreach (var property in material.GetPropertyList())
        {
            var name = property["name"].AsString();
            if ((property["usage"].AsInt32() & (int)PropertyUsageFlags.Storage) == 0 ||
                name is "albedo_texture" or "resource_name" or "resource_path" or "resource_local_to_scene" or "resource_scene_unique_id") continue;
            if (!includeSampling && name is "texture_filter" or "texture_repeat") continue;
            settings.Append(name).Append('=').Append(material.Get(name).ToString()).Append(';');
        }
        return settings.ToString();
    }

    private StandardMaterial3D GuestPaletteMaterial(StandardMaterial3D source, AttendeeColourChoice choice)
    {
        var sourceId = source.GetInstanceId();
        if (!_guestMaterialCompatibility.TryGetValue(sourceId, out var key))
        {
            var original = GuestPaletteBytes(source);
            key = Convert.ToHexString(SHA256.HashData(original)) + ":" + GuestMaterialSettings(source);
            _guestMaterialCompatibility.Add(sourceId, key);
            if (!_guestPaletteMaterials.ContainsKey(key))
            {
                GD.Print($"ATTENDEE_PALETTE_BASE filter={source.TextureFilter} repeat={source.TextureRepeat} roughness={source.Roughness} dimensions=96x8");
                var variants = new StandardMaterial3D[16];
                for (var clothing = 0; clothing < 4; clothing++)
                    for (var hair = 0; hair < 4; hair++)
                    {
                        using var image = Image.CreateFromData(AttendeePalette.Width, AttendeePalette.Height, false, Image.Format.Rgba8,
                            GuestPaletteContract.Apply(original, new(clothing, hair)));
                        var variant = (StandardMaterial3D)source.Duplicate(false);
                        variant.AlbedoTexture = ImageTexture.CreateFromImage(image);
                        // The approved palette contract requires nearest/clamp.
                        // Source resources and all other material settings stay intact.
                        variant.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
                        variant.TextureRepeat = false;
                        variants[clothing * 4 + hair] = variant;
                    }
                _guestPaletteMaterials.Add(key, variants);
            }
        }
        return _guestPaletteMaterials[key][choice.Clothing * 4 + choice.Hair];
    }

    private void ApplyGuestPalette(Node3D root, Node3D body)
    {
        var choice = new AttendeeColourChoice(root.GetMeta("GuestClothing").AsInt32(), root.GetMeta("GuestHair").AsInt32());
        foreach (var mesh in body.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.Mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D original)
                    throw new InvalidOperationException("Guest pose requires the original standard palette material.");
                mesh.SetSurfaceOverrideMaterial(surface, GuestPaletteMaterial(original, choice));
            }
    }

    private AttendeePoseCatalog? _guestPoseCatalog;
    private readonly Dictionary<string, PackedScene> _guestPoseScenes = [];

    private AttendeePoseCatalog GuestPoseCatalog => _guestPoseCatalog ??=
        AttendeePoseCatalog.Parse(Godot.FileAccess.GetFileAsString("res://assets/characters/attendee_poses_v6_manifest.json"));

    public Node3D AddGuest(EntityId id, Vector3 position)
    {
        var root = new Node3D { Name = $"Guest_{id.Value}", Position = position };
        root.SetMeta("GuestPoseVariant", AttendeePose.Variant(_session().CampaignSeed, id.Value));
        var colours = AttendeePalette.Choice(_session().CampaignSeed, id.Value);
        root.SetMeta("GuestClothing", colours.Clothing); root.SetMeta("GuestHair", colours.Hair);
        _parent.AddChild(root);
        SetGuestBodyPose(root, "relaxed", null);
        AddGuestHead(root, id.Value, root.GetMeta("GuestPoseVariant").AsString());
        SyncGuestHead(root);
        return root;
    }

    private void SetGuestBodyPose(Node3D root, string state, ImmersionProduct? product)
    {
        var asset = GuestPoseCatalog.Get(root.GetMeta("GuestPoseVariant").AsString(), state, product);
        var rigged = UsesRig(state, product);
        var file = rigged ? RiggedFile(root.GetMeta("GuestPoseVariant").AsString()) : asset.File;
        // The rig covers several states with one body: only what it's doing changes.
        if (root.HasMeta("GuestPoseFile") && root.GetMeta("GuestPoseFile").AsString() == file) { root.SetMeta("GuestPoseState", state); return; }
        // The head rides on a rigged body's head bone: take it back before that body goes, or it goes with it.
        if (root.FindChild(HeadPieces, true, false) is Node3D head && head.GetParent() != root) { head.GetParent().RemoveChild(head); root.AddChild(head); }
        if (root.GetNodeOrNull<Node3D>("GuestBody") is { } previous)
        {
            previous.Visible = false;
            root.RemoveChild(previous);
            previous.QueueFree();
        }
        // Hands free, a guest walks and stands on the rigged body; in any other pose, that pose's still body.
        if (!rigged && !_guestPoseScenes.TryGetValue(asset.File, out _))
            _guestPoseScenes.Add(asset.File, GD.Load<PackedScene>($"res://assets/characters/{asset.File}"));
        var body = rigged ? RiggedBody(root.GetMeta("GuestPoseVariant").AsString()) : _guestPoseScenes[asset.File].Instantiate<Node3D>();
        body.Name = "GuestBody";
        body.Transform = Transform3D.Identity;
        root.AddChild(body);
        ApplyGuestPalette(root, body);
        root.SetMeta("GuestPoseFile", file);
        root.SetMeta("GuestPoseState", state);
        root.SetMeta(RigMeta, rigged);
        if (root.IsInsideTree()) SyncGuestHead(root);
    }

    private void ApplyGuestPropAnchor(Node3D root, Node3D prop, string product)
    {
        if (AnchorPropToRig(root, prop)) { prop.RemoveMeta("GuestAnchorKey"); return; }
        var anchorKey = root.GetMeta("GuestPoseFile").AsString() + ":" + product;
        if (prop.HasMeta("GuestAnchorKey") && prop.GetMeta("GuestAnchorKey").AsString() == anchorKey) return;
        var state = root.GetMeta("GuestPoseState").AsString();
        var kind = product == "chips" ? ImmersionProduct.Chips : product == "beer" ? ImmersionProduct.Beer : ImmersionProduct.SoftDrink;
        var asset = GuestPoseCatalog.Get(root.GetMeta("GuestPoseVariant").AsString(), state, kind);
        var anchor = asset.Anchors[product == "chips" ? "tray" : product];
        prop.Position = new(anchor.X, anchor.Y, anchor.Z);
        prop.RotationDegrees = new(anchor.RotationX, anchor.RotationY, anchor.RotationZ);
        if (prop.HasMeta("EmptyWasteProp")) prop.Position -= prop.Basis * new Vector3(0, product == "chips" ? .025f : .075f, 0);
        prop.Scale = Vector3.One;
        prop.SetMeta("GuestAnchorKey", anchorKey);
    }

    private readonly Dictionary<(ulong SourceMaterial, string Role, int Hair, PerformerOutfits.Outfit? Outfit), StandardMaterial3D> _roleBodyMaterials = [];

    private string RoleKey(EditionPerson person)
    {
        if (person.Role == ProtectedPersonRole.Performer)
            return _performerRole(person.AgentId, person.Name) switch
            { 0 => "guitarist", 1 => "bassist", _ => "drummer" };
        var response = _session().GetResponseStaff().SingleOrDefault(item => item.AgentId == person.AgentId);
        if (response is not null) return response.Role == ResponseRole.Medic ? "medic" : "steward";
        if (_session().CapturePreparation()?.MaintenanceWorkerId == person.AgentId) return "maintenance";
        if (person.Role == ProtectedPersonRole.Staff) return "sound";
        throw new InvalidOperationException("Only protected staff and booked performers have role bodies.");
    }

    public Node3D AddRole(EditionPerson person, Vector3 position)
    {
        var role = RoleKey(person);
        var variant = AttendeePose.Variant(_session().CampaignSeed, person.AgentId);
        var root = CreateRoleBodyRoot(role, variant,
            AttendeePalette.Choice(_session().CampaignSeed, person.AgentId).Hair, person.AgentId, position);
        // A band dresses for its genre.
        if (person.Role == ProtectedPersonRole.Performer)
            root.SetMeta("RoleOutfit", PerformerOutfits.For(_session(), person.AgentId, _performerRole(person.AgentId, person.Name)) is { } outfit
                ? $"{outfit.Tee},{outfit.Patch},{outfit.Trousers},{outfit.Hair}" : "");
        AddRoleHead(root, person, variant);
        if (role == "maintenance")
            root.AddChild(WorldText.Speech(new Label3D { Text = person.Name.Split(' ')[0].ToUpperInvariant() + "\nMAINTENANCE",
                Position = new Vector3(0, 2.1f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled }, 28));
        return root;
    }

    private Node3D CreateRoleBodyRoot(string role, string variant, int hair, ulong id, Vector3 position)
    {
        var root = new Node3D { Name = $"Role_{id}", Position = position };
        root.SetMeta("RoleKey", role);
        root.SetMeta("RoleVariant", variant);
        root.SetMeta("RoleHair", hair);
        root.SetMeta("RolePersonId", id);
        _parent.AddChild(root);
        var performer = role is "guitarist" or "bassist" or "drummer";
        SetRoleBodyPose(root, "relaxed", null);
        if (!performer)
        {
            var overlay = Main.InstantiateAsset($"res://assets/characters/lwf_{role}_{variant}_overlay_v2.glb");
            overlay.Name = "RoleGarment";
            overlay.Visible = !(root.HasMeta(RigMeta) && root.GetMeta(RigMeta).AsBool());
            root.AddChild(overlay);
        }
        return root;
    }

    private void SetRoleBodyPose(Node3D root, string state, ImmersionProduct? product)
    {
        var role = root.GetMeta("RoleKey").AsString();
        var variant = root.GetMeta("RoleVariant").AsString();
        // The performer uses exact partitioned idle arms whenever hands are
        // free. Held food/drink temporarily uses the approved body-specific
        // pose, then returns to the partitioned body before the playing kit.
        var modular = role is "guitarist" or "bassist" or "drummer" && state == "relaxed";
        // Staff walk, carry and drink on their rigged body (garment included) once it exists, like guests.
        var rigFile = UsesRig(state, product) ? RiggedRoleFile(role, variant) : null;
        var file = rigFile ?? (modular ? $"lwf_performer_{variant}_body_v2.glb" : GuestPoseCatalog.Get(variant, state, product).File);
        if (root.HasMeta("RolePoseFile") && root.GetMeta("RolePoseFile").AsString() == file) { root.SetMeta("RolePoseState", state); return; }
        // The head rides on a rigged body's head bone: take it back before that body goes.
        if (root.FindChild(HeadPieces, true, false) is Node3D head && head.GetParent() != root) { head.GetParent().RemoveChild(head); root.AddChild(head); }
        if (root.GetNodeOrNull<Node3D>("RoleBody") is { } previous)
        {
            previous.Visible = false;
            root.RemoveChild(previous);
            previous.QueueFree();
        }
        var body = rigFile is not null ? RiggedBody(variant, rigFile) : Main.InstantiateAsset($"res://assets/characters/{file}");
        body.Name = "RoleBody";
        root.AddChild(body);
        ApplyRoleBodyPalette(root, body);
        root.SetMeta("RolePoseFile", file);
        root.SetMeta("RolePoseState", state);
        root.SetMeta(RigMeta, rigFile is not null);
        // The rigged body wears its garment; the still poses wear the separate overlay.
        if (root.GetNodeOrNull<Node3D>("RoleGarment") is { } garment) garment.Visible = rigFile is null;
        if (root.IsInsideTree()) SyncGuestHead(root);
        var id = new EntityId(root.GetMeta("RolePersonId").AsUInt64());
        if (_playing(id)) SetNeutralArmsVisible(body, false);
    }

    private void ApplyRolePropAnchor(Node3D root, Node3D prop, string product)
    {
        if (AnchorPropToRig(root, prop)) { prop.RemoveMeta("RoleAnchorKey"); return; }
        var key = root.GetMeta("RolePoseFile").AsString() + ":" + product;
        if (prop.HasMeta("RoleAnchorKey") && prop.GetMeta("RoleAnchorKey").AsString() == key) return;
        var variant = root.GetMeta("RoleVariant").AsString();
        var state = root.GetMeta("RolePoseState").AsString();
        var kind = product == "chips" ? ImmersionProduct.Chips : product == "beer" ? ImmersionProduct.Beer : ImmersionProduct.SoftDrink;
        var asset = GuestPoseCatalog.Get(variant, state, kind);
        var anchor = asset.Anchors[product == "chips" ? "tray" : product];
        prop.Position = new(anchor.X, anchor.Y, anchor.Z);
        prop.RotationDegrees = new(anchor.RotationX, anchor.RotationY, anchor.RotationZ);
        if (prop.HasMeta("EmptyWasteProp")) prop.Position -= prop.Basis * new Vector3(0, product == "chips" ? .025f : .075f, 0);
        prop.Scale = Vector3.One;
        prop.SetMeta("RoleAnchorKey", key);
    }

    private static PerformerOutfits.Outfit? RoleOutfit(Node3D root)
    {
        if (!root.HasMeta("RoleOutfit") || root.GetMeta("RoleOutfit").AsString() is not { Length: > 0 } text) return null;
        var parts = text.Split(',');
        return new(parts[0], parts[1], parts[2], parts[3].Length > 0 ? parts[3] : null);
    }

    public void ApplyRoleBodyPalette(Node3D root, Node3D body)
    {
        var role = root.GetMeta("RoleKey").AsString();
        var hair = root.GetMeta("RoleHair").AsInt32();
        var outfit = RoleOutfit(root);
        foreach (var mesh in body.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            // The cleanup body contains a fitted vest with its own approved trim palette; a rigged staff body's garment
            // keeps its own palette too.
            if (mesh.Name == "FittedVest" || mesh.Name.ToString().Contains("Garment")) continue;
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.Mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D source)
                    throw new InvalidOperationException("Role body requires the approved 96x8 standard palette material.");
                mesh.SetSurfaceOverrideMaterial(surface, RoleBodyMaterial(source, role, hair, outfit));
            }
        }
    }

    public void PaintPlayingArms(Node3D root, Node3D kit)
    {
        var role = root.GetMeta("RoleKey").AsString();
        var hair = root.GetMeta("RoleHair").AsInt32();
        var outfit = RoleOutfit(root);
        var arms = kit.FindChildren("*PlayingArm", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
        if (arms.Length != 2) throw new InvalidOperationException("A performer kit must have both fitted playing arms.");
        foreach (var mesh in arms)
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.Mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D source)
                    throw new InvalidOperationException("Playing arm requires the approved role body palette.");
                mesh.SetSurfaceOverrideMaterial(surface, RoleBodyMaterial(source, role, hair, outfit));
            }
    }

    private StandardMaterial3D RoleBodyMaterial(StandardMaterial3D source, string role, int hair, PerformerOutfits.Outfit? outfit = null)
    {
        var key = (source.GetInstanceId(), role, hair, outfit);
        if (_roleBodyMaterials.TryGetValue(key, out var cached)) return cached;
        var original = GuestPaletteBytes(source);
        var hairPalette = GuestPaletteContract.Apply(original, new AttendeeColourChoice(0, hair));
        var roleTexture = GD.Load<Texture2D>($"res://assets/characters/lwf_{role}_body_palette_v1.png");
        using var roleImage = roleTexture.GetImage();
        if (roleImage.IsCompressed()) roleImage.Decompress();
        roleImage.Convert(Image.Format.Rgba8);
        roleImage.ClearMipmaps();
        if (roleImage.GetWidth() != AttendeePalette.Width || roleImage.GetHeight() != AttendeePalette.Height)
            throw new InvalidOperationException("Role palette dimensions differ from the approved body atlas.");
        var roleBytes = roleImage.GetData();
        for (var row = 0; row < AttendeePalette.Height; row++)
            for (var slot = 3; slot <= 8; slot++)
                Array.Copy(roleBytes, (row * AttendeePalette.Width + slot * 8) * 4,
                    original, (row * AttendeePalette.Width + slot * 8) * 4, 8 * 4);
        for (var row = 0; row < AttendeePalette.Height; row++)
            for (var slot = 10; slot <= 11; slot++)
                Array.Copy(hairPalette, (row * AttendeePalette.Width + slot * 8) * 4,
                    original, (row * AttendeePalette.Width + slot * 8) * 4, 8 * 4);
        if (outfit is not null)
        {
            // Base and light-reserve swatches take the same colour, as on the concept board.
            void Paint(int slot, string hex)
            {
                var colour = Color.FromHtml(hex);
                for (var row = 0; row < AttendeePalette.Height; row++)
                    for (var x = 0; x < 8; x++)
                    {
                        var at = (row * AttendeePalette.Width + slot * 8 + x) * 4;
                        original[at] = (byte)colour.R8; original[at + 1] = (byte)colour.G8; original[at + 2] = (byte)colour.B8;
                    }
            }
            Paint(3, outfit.Tee); Paint(4, outfit.Tee); Paint(5, outfit.Patch); Paint(6, outfit.Patch);
            Paint(7, outfit.Trousers); Paint(8, outfit.Trousers);
            if (outfit.Hair is { } dyed) { Paint(10, dyed); Paint(11, dyed); }
        }
        using var image = Image.CreateFromData(AttendeePalette.Width, AttendeePalette.Height, false,
            Image.Format.Rgba8, original);
        var result = (StandardMaterial3D)source.Duplicate(false);
        result.AlbedoTexture = ImageTexture.CreateFromImage(image);
        result.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
        result.TextureRepeat = false;
        _roleBodyMaterials.Add(key, result);
        return result;
    }

    public static void SetNeutralArmsVisible(Node3D body, bool visible)
    {
        foreach (var node in body.FindChildren("*", "MeshInstance3D", true, false))
            if (node is MeshInstance3D mesh && mesh.Name.ToString().Contains("Idle", StringComparison.OrdinalIgnoreCase) &&
                mesh.Name.ToString().EndsWith("Arm", StringComparison.OrdinalIgnoreCase))
                mesh.Visible = visible;
    }
}

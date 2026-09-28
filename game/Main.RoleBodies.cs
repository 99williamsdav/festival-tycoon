using Festival.ContentAdapter;
using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private readonly Dictionary<(ulong SourceMaterial, string Role, int Hair), StandardMaterial3D> _roleBodyMaterials = [];

    private string RoleKey(EditionPerson person)
    {
        if (person.Role == ProtectedPersonRole.Performer)
            return PerformerPresentationRole(person.AgentId, person.Name) switch
            { 0 => "guitarist", 1 => "bassist", _ => "drummer" };
        var response = _session.GetResponseStaff().SingleOrDefault(item => item.AgentId == person.AgentId);
        if (response is not null) return response.Role == ResponseRole.Medic ? "medic" : "steward";
        if (_session.CapturePreparation()?.MaintenanceWorkerId == person.AgentId) return "maintenance";
        if (person.Role == ProtectedPersonRole.Staff) return "sound";
        throw new InvalidOperationException("Only protected staff and booked performers have role bodies.");
    }

    private Node3D AddRoleBodyRoot(EditionPerson person, Vector3 position)
    {
        var role = RoleKey(person);
        var variant = AttendeePose.Variant(_session.CampaignSeed, person.AgentId);
        var root = CreateRoleBodyRoot(role, variant,
            AttendeePalette.Choice(_session.CampaignSeed, person.AgentId).Hair, person.AgentId, position);
        if (role == "maintenance")
            root.AddChild(new Label3D { Text = person.Name.Split(' ')[0].ToUpperInvariant() + "\nMAINTENANCE",
                Position = new Vector3(0, 2.1f, 0), FontSize = 36, PixelSize = .009f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
        return root;
    }

    private Node3D CreateRoleBodyRoot(string role, string variant, int hair, ulong id, Vector3 position)
    {
        var root = new Node3D { Name = $"Role_{id}", Position = position };
        root.SetMeta("RoleKey", role);
        root.SetMeta("RoleVariant", variant);
        root.SetMeta("RoleHair", hair);
        root.SetMeta("RolePersonId", id);
        AddChild(root);
        var performer = role is "guitarist" or "bassist" or "drummer";
        SetRoleBodyPose(root, "relaxed", null);
        if (!performer)
        {
            var overlay = InstantiateAsset($"res://assets/characters/lwf_{role}_{variant}_overlay_v1.glb");
            overlay.Name = "RoleGarment";
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
        var file = modular ? $"lwf_performer_{variant}_body_v1.glb" : GuestPoseCatalog.Get(variant, state, product).File;
        if (root.HasMeta("RolePoseFile") && root.GetMeta("RolePoseFile").AsString() == file) return;
        if (root.GetNodeOrNull<Node3D>("RoleBody") is { } previous)
        {
            previous.Visible = false;
            root.RemoveChild(previous);
            previous.QueueFree();
        }
        var body = InstantiateAsset($"res://assets/characters/{file}");
        body.Name = "RoleBody";
        root.AddChild(body);
        ApplyRoleBodyPalette(root, body);
        root.SetMeta("RolePoseFile", file);
        root.SetMeta("RolePoseState", state);
        var id = new EntityId(root.GetMeta("RolePersonId").AsUInt64());
        if (_performerInstruments.ContainsKey(id)) SetNeutralArmsVisible(body, false);
    }

    private void ApplyRolePropAnchor(Node3D root, Node3D prop, string product)
    {
        var key = root.GetMeta("RolePoseFile").AsString() + ":" + product;
        if (prop.HasMeta("RoleAnchorKey") && prop.GetMeta("RoleAnchorKey").AsString() == key) return;
        var variant = root.GetMeta("RoleVariant").AsString();
        var state = root.GetMeta("RolePoseState").AsString();
        var kind = product == "chips" ? ImmersionProduct.Chips : product == "beer" ? ImmersionProduct.Beer : ImmersionProduct.SoftDrink;
        var asset = GuestPoseCatalog.Get(variant, state, kind);
        var anchor = asset.Anchors[product == "chips" ? "tray" : product];
        prop.Position = new(anchor.X, anchor.Y, anchor.Z);
        prop.RotationDegrees = new(anchor.RotationX, anchor.RotationY, anchor.RotationZ);
        prop.Scale = Vector3.One;
        prop.SetMeta("RoleAnchorKey", key);
    }

    private void ApplyRoleBodyPalette(Node3D root, Node3D body)
    {
        var role = root.GetMeta("RoleKey").AsString();
        var hair = root.GetMeta("RoleHair").AsInt32();
        foreach (var mesh in body.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.Mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D source)
                    throw new InvalidOperationException("Role body requires the approved 96x8 standard palette material.");
                mesh.SetSurfaceOverrideMaterial(surface, RoleBodyMaterial(source, role, hair));
            }
    }

    private void ApplyRolePlayingArmPalette(Node3D root, Node3D kit)
    {
        var role = root.GetMeta("RoleKey").AsString();
        var hair = root.GetMeta("RoleHair").AsInt32();
        var arms = kit.FindChildren("*PlayingArm", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
        if (arms.Length != 2) throw new InvalidOperationException("A performer kit must have both fitted playing arms.");
        foreach (var mesh in arms)
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.Mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D source)
                    throw new InvalidOperationException("Playing arm requires the approved role body palette.");
                mesh.SetSurfaceOverrideMaterial(surface, RoleBodyMaterial(source, role, hair));
            }
    }

    private StandardMaterial3D RoleBodyMaterial(StandardMaterial3D source, string role, int hair)
    {
        var key = (source.GetInstanceId(), role, hair);
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
        using var image = Image.CreateFromData(AttendeePalette.Width, AttendeePalette.Height, false,
            Image.Format.Rgba8, original);
        var result = (StandardMaterial3D)source.Duplicate(false);
        result.AlbedoTexture = ImageTexture.CreateFromImage(image);
        result.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
        result.TextureRepeat = false;
        _roleBodyMaterials.Add(key, result);
        return result;
    }
}

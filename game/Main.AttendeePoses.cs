using Festival.Simulation;
using Festival.ContentAdapter;
using Godot;
using System.Collections.Generic;

namespace Festival.Game;

public partial class Main
{
    private AttendeePoseCatalog? _guestPoseCatalog;
    private readonly Dictionary<string, PackedScene> _guestPoseScenes = [];

    private AttendeePoseCatalog GuestPoseCatalog => _guestPoseCatalog ??=
        AttendeePoseCatalog.Parse(Godot.FileAccess.GetFileAsString("res://assets/characters/attendee_poses_v2_manifest.json"));

    private Node3D AddGuestPoseRoot(EntityId id, Vector3 position)
    {
        var root = new Node3D { Name = $"Guest_{id.Value}", Position = position };
        root.SetMeta("GuestPoseVariant", AttendeePose.Variant(_session.CampaignSeed, id.Value));
        var colours = AttendeePalette.Choice(_session.CampaignSeed, id.Value);
        root.SetMeta("GuestClothing", colours.Clothing); root.SetMeta("GuestHair", colours.Hair);
        AddChild(root);
        SetGuestBodyPose(root, "relaxed", null);
        return root;
    }

    private void SetGuestBodyPose(Node3D root, string state, ImmersionProduct? product)
    {
        var asset = GuestPoseCatalog.Get(root.GetMeta("GuestPoseVariant").AsString(), state, product);
        if (root.HasMeta("GuestPoseFile") && root.GetMeta("GuestPoseFile").AsString() == asset.File) return;
        if (root.GetNodeOrNull<Node3D>("GuestBody") is { } previous)
        {
            previous.Visible = false;
            root.RemoveChild(previous);
            previous.QueueFree();
        }
        if (!_guestPoseScenes.TryGetValue(asset.File, out var scene))
        {
            scene = GD.Load<PackedScene>($"res://assets/characters/{asset.File}");
            _guestPoseScenes.Add(asset.File, scene);
        }
        var body = scene.Instantiate<Node3D>();
        body.Name = "GuestBody";
        body.Transform = Transform3D.Identity;
        root.AddChild(body);
        ApplyGuestPalette(root, body);
        root.SetMeta("GuestPoseFile", asset.File);
        root.SetMeta("GuestPoseState", state);
    }

    private void ApplyGuestPropAnchor(Node3D root, Node3D prop, string product)
    {
        var anchorKey = root.GetMeta("GuestPoseFile").AsString() + ":" + product;
        if (prop.HasMeta("GuestAnchorKey") && prop.GetMeta("GuestAnchorKey").AsString() == anchorKey) return;
        var state = root.GetMeta("GuestPoseState").AsString();
        var kind = product == "chips" ? ImmersionProduct.Chips : product == "beer" ? ImmersionProduct.Beer : ImmersionProduct.SoftDrink;
        var asset = GuestPoseCatalog.Get(root.GetMeta("GuestPoseVariant").AsString(), state, kind);
        var anchor = asset.Anchors[product == "chips" ? "tray" : product];
        prop.Position = new(anchor.X, anchor.Y, anchor.Z);
        prop.RotationDegrees = new(anchor.RotationX, anchor.RotationY, anchor.RotationZ);
        prop.Scale = Vector3.One;
        prop.SetMeta("GuestAnchorKey", anchorKey);
    }
}

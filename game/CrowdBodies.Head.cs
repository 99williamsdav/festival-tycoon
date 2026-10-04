using Festival.ContentAdapter;
using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// Hair, caps, flower crowns, sunglasses and beards. The bodies are bald; everyone's hair is a separate piece that sits
/// on the head beside the body, which survives every pose change because the head never moves between poses.
/// </summary>
internal sealed partial class CrowdBodies
{
    private const string HeadPieces = "HeadPieces";

    /// <summary>Puts a guest's look on their head: hair (or not), and whatever they're wearing.</summary>
    private void AddGuestHead(Node3D root, ulong id, string sex)
    {
        var genre = _session().CapturePreparation()?.People.FirstOrDefault(p => p.AgentId == id)?.ExpectedGenre ?? -1;
        var look = AttendeeLooks.For(_session().CampaignSeed, id, sex == "male", genre);
        BuildHead(root, sex, look, pieces => ApplyGuestPalette(root, pieces));
    }

    /// <summary>
    /// Staff and performers keep their hair; a punk band's lead and drummer wear the mohawk, dyed through their outfit.
    /// </summary>
    private void AddRoleHead(Node3D root, EditionPerson person, string sex)
    {
        var look = AttendeeLook.Plain;
        if (person.Role == ProtectedPersonRole.Performer && _performerRole(person.AgentId, person.Name) is 0 or 2 &&
            PerformerOutfits.Genre(_session(), person.AgentId) == FestivalGenre.Punk)
            look = look with { Hair = AttendeeHairStyle.Mohawk };
        BuildHead(root, sex, look, pieces => ApplyRoleBodyPalette(root, pieces));
        SyncGuestHead(root);
    }

    /// <summary>Rebuilds a staff member's head after their body variant changes (verification fixtures only).</summary>
    public void RebuildHead(Node3D root)
    {
        if (root.FindChild(HeadPieces, true, false) is Node3D old) { old.GetParent().RemoveChild(old); old.QueueFree(); }
        BuildHead(root, root.GetMeta("RoleVariant").AsString(), AttendeeLook.Plain, pieces => ApplyRoleBodyPalette(root, pieces));
        SyncGuestHead(root);
    }

    private void BuildHead(Node3D root, string sex, AttendeeLook look, Action<Node3D> paintHair)
    {
        var head = new Node3D { Name = HeadPieces };
        root.AddChild(head);
        // Hair-coloured pieces share the body's palette, so their hair colour (and a band's dye) follows.
        var hair = new Node3D { Name = "HairPieces" };
        head.AddChild(hair);
        var hairFile = look.Hair switch
        {
            AttendeeHairStyle.Bald => null,
            AttendeeHairStyle.Mohawk => $"lwf_hair_{sex}_mohawk_v1",
            _ => look.Cap ? $"lwf_hair_{sex}_default_under_cap_v1" : $"lwf_hair_{sex}_default_v1",
        };
        if (hairFile is not null) hair.AddChild(Main.InstantiateAsset($"res://assets/characters/{hairFile}.glb"));
        if (look.Beard && sex == "male") hair.AddChild(Main.InstantiateAsset("res://assets/characters/lwf_beard_male_v1.glb"));
        paintHair(hair);
        var (crown, visor) = AttendeeLooks.CapColours[look.CapColour];
        var (frame, lens) = AttendeeLooks.SunglassFrames[look.FrameColour];
        var accessories = new List<(int Slot, string Hex)>();
        if (look.Cap)
        {
            head.AddChild(Main.InstantiateAsset($"res://assets/characters/lwf_hat_cap_{sex}_v1.glb"));
            accessories.AddRange([(0, crown), (1, visor), (2, crown)]);
        }
        if (look.FlowerCrown) head.AddChild(Main.InstantiateAsset($"res://assets/characters/lwf_hat_flower_crown_{sex}_v1.glb"));
        if (look.Sunglasses)
        {
            head.AddChild(Main.InstantiateAsset($"res://assets/characters/lwf_sunglasses_{sex}_v1.glb"));
            accessories.AddRange([(4, frame), (5, lens)]);
        }
        PaintAccessories(head, accessories);
    }

    private readonly Dictionary<(ulong Source, string Colours), StandardMaterial3D> _accessoryMaterials = [];

    /// <summary>Recolours the cap and sunglasses swatches of the shared accessory palette for this person.</summary>
    private void PaintAccessories(Node3D head, List<(int Slot, string Hex)> colours)
    {
        if (colours.Count == 0) return;
        var key = string.Join(",", colours.Select(c => $"{c.Slot}:{c.Hex}"));
        foreach (var mesh in head.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.Mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D { AlbedoTexture: { } texture } source ||
                    !source.ResourceName.StartsWith("LWF_CrowdAccessory", StringComparison.Ordinal)) continue;
                if (!_accessoryMaterials.TryGetValue((source.GetInstanceId(), key), out var material))
                {
                    using var image = texture.GetImage();
                    if (image.IsCompressed()) image.Decompress();
                    image.Convert(Image.Format.Rgba8);
                    image.ClearMipmaps();
                    var width = image.GetWidth();
                    var bytes = image.GetData();
                    foreach (var (slot, hex) in colours)
                    {
                        var colour = Color.FromHtml(hex);
                        for (var row = 0; row < image.GetHeight(); row++)
                            for (var x = 0; x < 8; x++)
                            {
                                var at = (row * width + slot * 8 + x) * 4;
                                bytes[at] = (byte)colour.R8; bytes[at + 1] = (byte)colour.G8; bytes[at + 2] = (byte)colour.B8;
                            }
                    }
                    material = (StandardMaterial3D)source.Duplicate(false);
                    material.AlbedoTexture = ImageTexture.CreateFromImage(Image.CreateFromData(width, image.GetHeight(), false, Image.Format.Rgba8, bytes));
                    material.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
                    _accessoryMaterials.Add((source.GetInstanceId(), key), material);
                }
                mesh.SetSurfaceOverrideMaterial(surface, material);
            }
    }
}

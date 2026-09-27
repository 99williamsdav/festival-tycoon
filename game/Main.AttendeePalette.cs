using Festival.ContentAdapter;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Festival.Game;

public partial class Main
{
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

    private static string GuestColourKey(Node3D root) => root.GetMeta("GuestClothing").AsInt32() + ":" + root.GetMeta("GuestHair").AsInt32();

    private void AssertGuestPalette(Node3D root)
    {
        var choice = new AttendeeColourChoice(root.GetMeta("GuestClothing").AsInt32(), root.GetMeta("GuestHair").AsInt32());
        foreach (var mesh in root.GetNode<Node3D>("GuestBody").FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            var original = (StandardMaterial3D)mesh.Mesh.SurfaceGetMaterial(0);
            var applied = (StandardMaterial3D)mesh.GetSurfaceOverrideMaterial(0);
            var bytes = GuestPaletteBytes(original);
            PoseAssert(!ReferenceEquals(original, applied) && !ReferenceEquals(original.AlbedoTexture, applied.AlbedoTexture), "Palette variant mutated shared source resources.");
            PoseAssert(GuestPaletteContract.Apply(bytes, choice).SequenceEqual(GuestPaletteBytes(applied)), "Palette override differs from exact designer RGB/alpha bytes.");
            PoseAssert(GuestMaterialSettings(original, false) == GuestMaterialSettings(applied, false), "Palette variant changed non-sampling imported material flags.");
            PoseAssert(applied.TextureFilter == BaseMaterial3D.TextureFilterEnum.Nearest && !applied.TextureRepeat, "Palette variant did not use designer nearest/clamp sampling.");
            PoseAssert(ReferenceEquals(applied, GuestPaletteMaterial(original, choice)), "Palette cache did not reuse compatible material identity.");
        }
    }
}

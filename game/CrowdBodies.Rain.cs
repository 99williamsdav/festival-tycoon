using Festival.ContentAdapter;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// Guests in the rain: soaked colours once they're wet through, the four rain clips (hunched in it, hands on head or
/// hurrying through it, huddled under cover), and the clear poncho on anyone who bought one. Poncho wearers keep their
/// ordinary clips; so does anyone holding food or a drink.
/// </summary>
internal sealed partial class CrowdBodies
{
    /// <summary>What the rain is doing to someone, for their clip: nothing, out in it, under cover, or hurrying to cover.</summary>
    public enum RainMode { None, Out, Cover, Hurry }

    private const string SoakedMeta = "GuestSoaked", RainModeMeta = "GuestRainMode", PonchoMeta = "GuestPoncho", HatMeta = "HatPiece";
    private const string PonchoNode = "LWF_Poncho_Clear";
    private static readonly string[] RainClips = ["rain_hunched", "rain_huddle", "rain_hands_head", "rain_hurry"];
    private readonly Dictionary<string, AnimationLibrary> _rainLibraries = [];
    private AttendeePalette? _soakedPaletteContract;
    private AttendeePalette SoakedPaletteContract => _soakedPaletteContract ??=
        AttendeePalette.Parse(Godot.FileAccess.GetFileAsString("res://assets/characters/attendee_palette_soaked_v1_contract.json"));

    /// <summary>The four rain clips for one body, loaded once per sex and shared, looping.</summary>
    private AnimationLibrary RainLibrary(string sex)
    {
        if (_rainLibraries.TryGetValue(sex, out var library)) return library;
        var scene = Main.InstantiateAsset($"res://assets/characters/lwf_attendee_{sex}_rain_clips_v1.glb");
        var player = scene.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>().First();
        library = (AnimationLibrary)player.GetAnimationLibrary("").Duplicate(true);
        foreach (var name in RainClips)
            if (library.HasAnimation(name)) library.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
        scene.QueueFree();
        _rainLibraries[sex] = library;
        return library;
    }

    /// <summary>Gives a rigged body's player the rain clips, as "rain/…".</summary>
    private void AddRainClips(AnimationPlayer player, string sex)
    {
        if (!player.HasAnimationLibrary("rain")) player.AddAnimationLibrary("rain", RainLibrary(sex));
    }

    /// <summary>Sets someone's rain look for this frame; only changes cost anything.</summary>
    public void SetRain(Node3D root, bool soaked, RainMode mode, bool poncho)
    {
        root.SetMeta(RainModeMeta, (int)(poncho ? RainMode.None : mode));
        var wasSoaked = root.HasMeta(SoakedMeta) && root.GetMeta(SoakedMeta).AsBool();
        soaked &= !poncho;
        if (soaked != wasSoaked && root.HasMeta("GuestPoseVariant"))
        {
            root.SetMeta(SoakedMeta, soaked);
            if (root.GetNodeOrNull<Node3D>("GuestBody") is { } body) ApplyGuestPalette(root, body);
            if (root.FindChild("HairPieces", true, false) is Node3D hair) ApplyGuestPalette(root, hair);
        }
        var hadPoncho = root.HasMeta(PonchoMeta) && root.GetMeta(PonchoMeta).AsBool();
        if (poncho != hadPoncho)
        {
            root.SetMeta(PonchoMeta, poncho);
            SyncPoncho(root);
        }
    }

    /// <summary>Puts the poncho on the rigged body's skeleton, or takes it off; hats go under the hood.</summary>
    private void SyncPoncho(Node3D root)
    {
        var wanted = root.HasMeta(PonchoMeta) && root.GetMeta(PonchoMeta).AsBool();
        var body = RigBody(root);
        var skeleton = body?.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().FirstOrDefault();
        var worn = skeleton?.GetNodeOrNull<MeshInstance3D>(PonchoNode);
        if (wanted && worn is null && skeleton is not null && root.GetMeta(RigMeta, false).AsBool())
        {
            var scene = Main.InstantiateAsset($"res://assets/characters/lwf_attendee_{RigSex(root)}_poncho_clear_v1.glb");
            var mesh = scene.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().First(item => item.Name == PonchoNode);
            mesh.GetParent().RemoveChild(mesh);
            skeleton.AddChild(mesh);
            mesh.Skeleton = "..";
            mesh.Transform = Transform3D.Identity;
            scene.QueueFree();
        }
        else if (!wanted && worn is not null) { skeleton!.RemoveChild(worn); worn.QueueFree(); }
        if (root.FindChild(HeadPieces, true, false) is Node3D head)
            foreach (var hat in head.GetChildren().OfType<Node3D>().Where(piece => piece.HasMeta(HatMeta))) hat.Visible = !wanted;
    }

    /// <summary>Something worn over the body rather than part of it: the head's pieces riding the skeleton, or the poncho.</summary>
    private static bool WornOnTop(Node3D body, Node mesh)
    {
        if (mesh.Name == PonchoNode) return true;
        for (var node = mesh.GetParent(); node is not null && node != body; node = node.GetParent())
            if (node.Name == HeadPieces) return true;
        return false;
    }

    /// <summary>The colours for this body: their own colourway, darkened through when they're soaked.</summary>
    private bool Soaked(Node3D root) => root.HasMeta(SoakedMeta) && root.GetMeta(SoakedMeta).AsBool();

    private readonly Dictionary<string, StandardMaterial3D[]> _soakedPaletteMaterials = [];

    private StandardMaterial3D SoakedPaletteMaterial(StandardMaterial3D source, AttendeeColourChoice choice)
    {
        var key = source.GetInstanceId().ToString();
        if (!_soakedPaletteMaterials.TryGetValue(key, out var variants))
        {
            var original = GuestPaletteBytes(source);
            variants = new StandardMaterial3D[16];
            for (var clothing = 0; clothing < 4; clothing++)
                for (var hair = 0; hair < 4; hair++)
                {
                    using var image = Image.CreateFromData(AttendeePalette.Width, AttendeePalette.Height, false, Image.Format.Rgba8,
                        SoakedPaletteContract.Apply(original, new(clothing, hair)));
                    var variant = (StandardMaterial3D)source.Duplicate(false);
                    variant.AlbedoTexture = ImageTexture.CreateFromImage(image);
                    variant.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
                    variant.TextureRepeat = false;
                    // A faint sheen on wet cloth.
                    variant.Roughness = .42f;
                    variants[clothing * 4 + hair] = variant;
                }
            _soakedPaletteMaterials[key] = variants;
        }
        return variants[choice.Clothing * 4 + choice.Hair];
    }

    /// <summary>The rain clip for someone free-handed, or null for their ordinary one.</summary>
    private static string? RainClip(Node3D root, AnimationPlayer player, bool moving)
    {
        var mode = (RainMode)root.GetMeta(RainModeMeta, 0).AsInt32();
        if (mode == RainMode.None || !player.HasAnimationLibrary("rain")) return null;
        var clip = mode switch
        {
            RainMode.Cover => moving ? null : "rain_huddle",
            RainMode.Hurry => moving ? "rain_hurry" : "rain_hunched",
            // Out in it: about half walk with their hands on their heads, the rest hurry, picked by who they are.
            _ => !moving ? "rain_hunched" : (root.GetMeta("GuestClothing", 0).AsInt32() + root.GetMeta("GuestHair", 0).AsInt32()) % 2 == 0 ? "rain_hands_head" : "rain_hurry",
        };
        return clip is null ? null : $"rain/{clip}";
    }
}

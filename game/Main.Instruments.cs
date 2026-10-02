using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// Which instruments a band plays for its genre, and in what colours. The lead sings at a mic stand in every genre
/// but Electronic, and Pop's lead only sings, one hand on the mic. Folk plays acoustic and accordion on a compact
/// kit, Metal a flying-V over a double kick, Electronic a laptop and mixing desk with keys. Each act's guitars and drums take their own colours.
/// </summary>
public partial class Main
{
    private string? _stageDrumHardware;

    /// <summary>The genre of the act a performer plays in, or -1 if they aren't booked.</summary>
    private int PerformerGenre(ulong id)
    {
        if (_session.CaptureProgramme() is not { } programme ||
            programme.Performers.FirstOrDefault(p => p.AgentId == id) is not { } performer ||
            performer.SlotIndex < 0 || performer.SlotIndex >= programme.ActIds.Length) return -1;
        return _session.GetFestivalActs().FirstOrDefault(a => a.Id == programme.ActIds[performer.SlotIndex])?.Genre ?? -1;
    }

    private string? PerformerActId(ulong id) => _session.CaptureProgramme() is { } programme &&
        programme.Performers.FirstOrDefault(p => p.AgentId == id) is { SlotIndex: >= 0 } performer && performer.SlotIndex < programme.ActIds.Length
            ? programme.ActIds[performer.SlotIndex] : null;

    /// <summary>The kit a performer plays.</summary>
    private static string? PerformerKitPath(int role, string variant, int genre) => role switch
    {
        0 => genre switch
        {
            FestivalGenre.Folk => $"res://assets/characters/lwf_guitarist_{variant}_acoustic_kit_v2.glb",
            FestivalGenre.Metal => $"res://assets/characters/lwf_guitarist_{variant}_flyingv_kit_v2.glb",
            FestivalGenre.Electronic => $"res://assets/characters/lwf_electronic_{variant}_desk_kit_v2.glb",
            FestivalGenre.Pop => $"res://assets/characters/lwf_singer_{variant}_kit_v2.glb",
            _ => $"res://assets/characters/lwf_guitarist_{variant}_electric_kit_v2.glb",
        },
        1 => genre switch
        {
            FestivalGenre.Electronic => $"res://assets/characters/lwf_keyboardist_{variant}_kit_v2.glb",
            FestivalGenre.Folk => $"res://assets/characters/lwf_accordionist_{variant}_kit_v2.glb",
            _ => $"res://assets/characters/lwf_bassist_{variant}_kit_v2.glb",
        },
        2 => $"res://assets/characters/lwf_drummer_{variant}_kit_v2.glb",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    /// <summary>
    /// Builds what a performer holds for the set: their kit, recoloured for the act, and for a singing lead the mic
    /// stand. A lead who only sings gets just the stand, and keeps their own arms.
    /// </summary>
    private Node3D BuildPerformerKit(ulong id, int role, string variant)
    {
        var genre = PerformerGenre(id);
        var actId = PerformerActId(id) ?? "";
        Node3D kit;
        if (PerformerKitPath(role, variant, genre) is { } path)
        {
            kit = InstantiateAsset(path);
            RecolourInstrument(kit, ActColours(actId, role));
        }
        else
        {
            kit = new Node3D { Name = "SingerKit" };
            kit.SetMeta("NoArms", true);
        }
        if (role == 0 && genre != FestivalGenre.Electronic)
        {
            var mic = InstantiateAsset("res://assets/environment/lwf_mic_stand_v1.glb");
            if (mic.FindChild("MicHead", true, false) is Node3D head)
                head.Position = new Vector3(head.Position.X, variant == "female" ? 1.43f : 1.50f, head.Position.Z);
            kit.AddChild(mic);
        }
        return kit;
    }

    private static readonly (string Top, string Back)[] InstrumentFinishes =
    [
        ("D68A4E", "B9693F"), ("C9553A", "B8442E"), ("EDE3C8", "D9CDB0"), ("2A2A2A", "1A1A1A"),
        ("8CC4B4", "76AE9E"), ("E0CDA4", "C9B68C"), ("54827A", "3D6D69"), ("D4A845", "BF9433"),
    ];
    private static readonly string[] Pickguards = ["EDE6D6", "E8E6E0", "6A3A24", "2A2A2A"];

    /// <summary>An act's colours for one player's instrument: (palette slot, hex), stable for the act.</summary>
    private static (int Slot, string Hex)[] ActColours(string actId, int role)
    {
        var hash = 17u;
        foreach (var c in actId) hash = unchecked(hash * 31 + c);
        hash = unchecked(hash * 31 + (uint)role) * 2654435761u;
        var finish = InstrumentFinishes[(hash >> 8) % (uint)InstrumentFinishes.Length];
        // Drums: kick, snare and floor shells, and the rack toms, in the finish's top colour.
        if (role == 2) return [(12, finish.Top), (13, finish.Top)];
        return [(12, finish.Back), (13, finish.Top), (14, Pickguards[(hash >> 16) % (uint)Pickguards.Length])];
    }

    private readonly Dictionary<(ulong Material, string Colours), StandardMaterial3D> _instrumentMaterials = [];

    /// <summary>Recolours an instrument's own palette (never the performer's arms) in the given swatch slots.</summary>
    private void RecolourInstrument(Node3D root, (int Slot, string Hex)[] colours)
    {
        var key = string.Join(",", colours.Select(c => $"{c.Slot}:{c.Hex}"));
        foreach (var mesh in root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            if (mesh.Name.ToString().EndsWith("PlayingArm", StringComparison.Ordinal)) continue;
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                // Only the instrument's own matte palette: never the strap's crew trim or the body palette.
                if (mesh.Mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D { AlbedoTexture: { } texture } source ||
                    texture.GetWidth() != 192 || !source.ResourceName.EndsWith("_MattePalette", StringComparison.Ordinal)) continue;
                if (!_instrumentMaterials.TryGetValue((source.GetInstanceId(), key), out var material))
                {
                    using var image = texture.GetImage();
                    if (image.IsCompressed()) image.Decompress();
                    image.Convert(Image.Format.Rgba8);
                    image.ClearMipmaps();
                    var bytes = image.GetData();
                    foreach (var (slot, hex) in colours)
                    {
                        var colour = Color.FromHtml(hex);
                        for (var row = 0; row < image.GetHeight(); row++)
                            for (var x = 0; x < 8; x++)
                            {
                                var at = (row * 192 + slot * 8 + x) * 4;
                                bytes[at] = (byte)colour.R8; bytes[at + 1] = (byte)colour.G8; bytes[at + 2] = (byte)colour.B8;
                            }
                    }
                    material = (StandardMaterial3D)source.Duplicate(false);
                    material.AlbedoTexture = ImageTexture.CreateFromImage(Image.CreateFromData(192, image.GetHeight(), false, Image.Format.Rgba8, bytes));
                    material.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
                    _instrumentMaterials.Add((source.GetInstanceId(), key), material);
                }
                mesh.SetSurfaceOverrideMaterial(surface, material);
            }
        }
    }

    /// <summary>The drum hardware for the act on stage (or next up): compact for folk, a double kick for metal, pads for electronic.</summary>
    private void SetStageDrumHardware(FestivalAct? act)
    {
        var file = act?.Genre switch
        {
            FestivalGenre.Folk => "lwf_drum_hardware_compact_v1",
            FestivalGenre.Metal => "lwf_drum_hardware_double_kick_v1",
            FestivalGenre.Electronic => "lwf_drum_hardware_electronic_v1",
            _ => "lwf_drum_hardware_only_v2",
        };
        var colours = ActColours(act?.Id ?? "", 2);
        var wanted = file + "|" + string.Join(",", colours.Select(c => c.Hex));
        if (_stageDrumHardware == wanted && _stageDrumKit is not null && IsInstanceValid(_stageDrumKit)) return;
        _stageDrumHardware = wanted;
        _stageDrumKit?.QueueFree();
        var drumMark = TraversalGrid.CellCentre(new GridCell(93, 152));
        _stageDrumKit = AddAsset($"res://assets/characters/{file}.glb",
            new Vector3(drumMark.XMillimetres / 1000f, 1.19f, drumMark.ZMillimetres / 1000f));
        _stageDrumKit.RotationDegrees = new Vector3(0, -90, 0);
        if (act is not null) RecolourInstrument(_stageDrumKit, colours);
    }
}

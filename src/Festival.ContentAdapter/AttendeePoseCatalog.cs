using System.Text.Json;
using Festival.Simulation;

namespace Festival.ContentAdapter;

public sealed record AttendeePoseAnchor(float X, float Y, float Z, float RotationX, float RotationY, float RotationZ);
public sealed record AttendeePoseAsset(string File, string Sha256, IReadOnlyDictionary<string, AttendeePoseAnchor> Anchors);

// Parses the delivered contract once; presentation uses its original root-local
// transforms rather than copying rounded attachment values into another table.
public sealed class AttendeePoseCatalog
{
    private readonly Dictionary<string, AttendeePoseAsset> _assets = [];
    public AttendeePoseAsset Get(string variant, string state, ImmersionProduct? product) =>
        _assets[AttendeePose.File(variant, state, product)];
    public static AttendeePoseCatalog Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("schema_version").GetInt32() != 1 ||
            root.GetProperty("units").GetString() != "metres" ||
            root.GetProperty("front").GetString() != "Godot -Z" || !root.GetProperty("body_only").GetBoolean())
            throw new InvalidDataException("Unsupported attendee pose contract.");
        var catalog = new AttendeePoseCatalog();
        foreach (var variant in root.GetProperty("variants").EnumerateObject())
            foreach (var asset in variant.Value.EnumerateArray())
            {
                var anchors = new Dictionary<string, AttendeePoseAnchor>();
                foreach (var attachment in asset.GetProperty("attachments").EnumerateObject())
                {
                    var p = attachment.Value.GetProperty("position").EnumerateArray().Select(v => v.GetSingle()).ToArray();
                    var r = attachment.Value.GetProperty("rotation_degrees").EnumerateArray().Select(v => v.GetSingle()).ToArray();
                    var scale = attachment.Value.GetProperty("scale").EnumerateArray().Select(v => v.GetSingle()).ToArray();
                    if (p.Length != 3 || r.Length != 3 || scale.Length != 3 || scale.Any(v => v != 1) || p.Concat(r).Any(v => !float.IsFinite(v)))
                        throw new InvalidDataException("Attendee attachments require finite root-local transforms and identity scale.");
                    anchors.Add(attachment.Name, new(p[0], p[1], p[2], r[0], r[1], r[2]));
                }
                var file = asset.GetProperty("file").GetString()!;
                catalog._assets.Add(file, new(file, asset.GetProperty("sha256").GetString()!, anchors));
            }
        return catalog;
    }
}

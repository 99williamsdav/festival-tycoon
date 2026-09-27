using System.Text.Json;

namespace Festival.ContentAdapter;

public readonly record struct AttendeeColourChoice(int Clothing, int Hair);

// Versioned designer bytes. Colour choice is cosmetic and independent of body
// selection and the simulation's random streams.
public sealed class AttendeePalette
{
    public const int Width = 96;
    public const int Height = 8;
    private readonly Dictionary<int, byte[]>[] _clothing;
    private readonly Dictionary<int, byte[]>[] _hair;
    public IReadOnlyList<string> ClothingIds { get; }
    public IReadOnlyList<string> HairIds { get; }
    private AttendeePalette(Dictionary<int, byte[]>[] clothing, Dictionary<int, byte[]>[] hair, string[] clothingIds, string[] hairIds)
    { _clothing = clothing; _hair = hair; ClothingIds = clothingIds; HairIds = hairIds; }
    public static AttendeePalette Parse(string json)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        var palette = root.GetProperty("palette");
        if (root.GetProperty("version").GetInt32() != 1 || palette.GetProperty("width").GetInt32() != Width ||
            palette.GetProperty("height").GetInt32() != Height || palette.GetProperty("swatch_width").GetInt32() != 8 || palette.GetProperty("alpha").GetInt32() != 255)
            throw new InvalidDataException("Unsupported attendee palette contract.");
        static (Dictionary<int, byte[]>[] Slots, string[] Ids) Read(JsonElement list, int[] required)
        {
            var slots = new List<Dictionary<int, byte[]>>(); var ids = new List<string>();
            foreach (var item in list.EnumerateArray())
            {
                ids.Add(item.GetProperty("id").GetString()!);
                var mapping = item.GetProperty("slots").EnumerateObject().ToDictionary(p => int.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture), p => Convert.FromHexString(p.Value.GetString()!));
                if (!mapping.Keys.Order().SequenceEqual(required) || mapping.Values.Any(rgb => rgb.Length != 3)) throw new InvalidDataException("Unexpected attendee colour slots.");
                slots.Add(mapping);
            }
            if (slots.Count != 4 || ids.Distinct().Count() != 4) throw new InvalidDataException("Expected four versioned colour options.");
            return (slots.ToArray(), ids.ToArray());
        }
        var clothing = Read(root.GetProperty("clothing_colourways"), [3,4,5,6,7,8]);
        var hair = Read(root.GetProperty("hair_colours"), [10,11]);
        return new(clothing.Slots, hair.Slots, clothing.Ids, hair.Ids);
    }
    private static ulong Mix(ulong value)
    {
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
        return value ^ (value >> 31);
    }
    public static AttendeeColourChoice Choice(ulong seed, ulong id)
    {
        var identity = unchecked(id * 6364136223846793005UL + seed);
        return new((int)(Mix(identity ^ 0x434C4F5448563031UL) & 3), (int)(Mix(identity ^ 0x484149525F563031UL) & 3));
    }
    public byte[] Apply(ReadOnlySpan<byte> originalRgba, AttendeeColourChoice choice)
    {
        if (originalRgba.Length != Width * Height * 4 || choice.Clothing is < 0 or > 3 || choice.Hair is < 0 or > 3)
            throw new ArgumentException("Expected a 96x8 RGBA8 palette and approved colour indices.");
        var result = originalRgba.ToArray();
        foreach (var slot in _clothing[choice.Clothing].Concat(_hair[choice.Hair]))
            for (var y = 0; y < Height; y++)
                for (var x = slot.Key * 8; x < slot.Key * 8 + 8; x++)
                    slot.Value.CopyTo(result, (y * Width + x) * 4); // Alpha stays original.
        return result;
    }
}

using Godot;

namespace Festival.Game;

/// <summary>
/// Text in the world (people's remarks, warnings and name tags) kept the same size on screen at every zoom and drawn
/// from a distance field in the HUD's Source Sans, so it stays crisp instead of shrinking to a smudge zoomed out.
/// </summary>
internal static class WorldText
{
    private static FontVariation? _speech;

    /// <summary>The HUD body face at bold weight, drawn as a distance field with room for an outline up to size 55.</summary>
    public static FontVariation SpeechFont => _speech ??= new FontVariation
    {
        BaseFont = Msdf("res://assets/ui/fonts/SourceSans3-Variable.ttf"),
        VariationOpentype = new Godot.Collections.Dictionary { { "wght", 700 } },
    };

    public static FontFile Msdf(string path)
    {
        var font = (FontFile)GD.Load<FontFile>(path).Duplicate();
        font.MultichannelSignedDistanceField = true;
        font.MsdfPixelRange = 40; // At least twice the outline in MSDF units, so the outline isn't clipped by the field.
        return font;
    }

    /// <summary>Restyles a remark or name tag: steady screen size, crisp lettering, drawn above the scenery.</summary>
    public static T Speech<T>(T label, int size) where T : Label3D
    {
        label.Font = SpeechFont;
        label.FontSize = size;
        label.PixelSize = .0011f;
        label.FixedSize = true;
        label.OutlineSize = size * 3 / 10;
        label.NoDepthTest = true;
        label.RenderPriority = 3; // Above the building names.
        return label;
    }
}

namespace Festival.ContentAdapter;

public enum AttendeeHairStyle { Default, Bald, Mohawk }

/// <summary>
/// What's on a guest's head: their hair (as it always was, shaved bald, or a punk's mohawk), and a cap or flower
/// crown, sunglasses and a beard. Cosmetic only, chosen from the campaign seed like their colours.
/// </summary>
/// <param name="CapColour">Index into <see cref="AttendeeLooks.CapColours"/>.</param>
/// <param name="FrameColour">Index into <see cref="AttendeeLooks.SunglassFrames"/>.</param>
public sealed record AttendeeLook(AttendeeHairStyle Hair, bool Cap, bool FlowerCrown, bool Sunglasses, bool Beard,
    int CapColour, int FrameColour)
{
    public static readonly AttendeeLook Plain = new(AttendeeHairStyle.Default, false, false, false, false, 0, 0);
}

public static class AttendeeLooks
{
    // Genres as the simulation numbers them; kept here so the content adapter stays free of the simulation.
    private const int Folk = 0, Pop = 2, Electronic = 3, Punk = 4, Metal = 5;

    /// <summary>Cap crown and visor colours: red, navy, khaki, black, white, green.</summary>
    public static readonly (string Crown, string Visor)[] CapColours =
        [("C9553A", "A8432F"), ("2E3A4F", "232D3E"), ("B8A27A", "9C8964"), ("2A2A2A", "1C1C1C"), ("E8E4DA", "C8C2B4"), ("4F6B4A", "3E563A")];
    /// <summary>Sunglass frame and lens colours: black, tortoiseshell, white, gold.</summary>
    public static readonly (string Frame, string Lens)[] SunglassFrames =
        [("1A1A1A", "2A3540"), ("6A3A24", "3A2E26"), ("E8E6E0", "2A3540"), ("C9A34A", "3A3226")];

    private static ulong Mix(ulong value)
    {
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
        return value ^ (value >> 31);
    }

    private static int Roll(ulong seed, ulong id, ulong salt) =>
        (int)(Mix(unchecked(id * 6364136223846793005UL + seed) ^ salt) % 1000);

    /// <summary>
    /// A guest's look from their sex and the genre they came for. Only punk fans wear mohawks; folk fans favour flower
    /// crowns and beards, metal fans beards, electronic and pop fans sunglasses.
    /// </summary>
    public static AttendeeLook For(ulong seed, ulong id, bool male, int genre)
    {
        var hair = genre == Punk && Roll(seed, id, 0x4D4F48415752UL) < (male ? 300 : 220) ? AttendeeHairStyle.Mohawk
            : male && Roll(seed, id, 0x42414C44UL) < 120 ? AttendeeHairStyle.Bald
            : AttendeeHairStyle.Default;
        // One thing on the head at most; a mohawk takes neither, and a crown needs hair to sit on.
        var cap = hair != AttendeeHairStyle.Mohawk && Roll(seed, id, 0x434150UL) < 140;
        var crown = !cap && hair == AttendeeHairStyle.Default && Roll(seed, id, 0x43524F574EUL) < (genre == Folk ? 150 : 40);
        var glasses = Roll(seed, id, 0x474C41535345UL) < genre switch { Electronic => 250, Pop => 180, _ => 120 };
        var beard = male && Roll(seed, id, 0x4245415244UL) < genre switch { Folk => 450, Metal => 400, _ => 250 };
        return new(hair, cap, crown, glasses, beard,
            Roll(seed, id, 0x43434F4CUL) % CapColours.Length, Roll(seed, id, 0x46434F4CUL) % SunglassFrames.Length);
    }
}

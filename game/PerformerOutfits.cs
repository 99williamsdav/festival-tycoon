using Festival.Simulation;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// What a band wears for its genre: a recolour of the performer body palette's tee, patch and trouser slots, and
/// for some a dyed or greyed head of hair. Colours are the designer's band-genres concept board. Each player in
/// the band (guitar, bass, drums) wears their own of the three.
/// </summary>
internal static class PerformerOutfits
{
    /// <param name="Hair">Hair colour, or null to keep the performer's natural colour.</param>
    internal sealed record Outfit(string Tee, string Patch, string Trousers, string? Hair);

    // Per genre, the guitarist's, bassist's and drummer's outfits.
    private static readonly Outfit[][] ByGenre =
    [
        // Folk: mustard and sage cardigans, brown cords; one greying, one ginger.
        [new("B8935A", "E8DCC0", "6B5440", "8E8A84"), new("6E7F5A", "E8DCC0", "6B5440", null), new("C9A3A0", "F2EBDD", "4E5A44", "9A5A30")],
        // Indie: navy and mustard, black skinny jeans.
        [new("2E3A4F", "D9C27A", "26262A", null), new("D9C27A", "2E3A4F", "4A5A78", "9A8056"), new("8A8F96", "E8DEC4", "26262A", null)],
        // Pop: teal, hot pink, white and gold; platinum and honey hair.
        [new("3CC2C8", "F2F0F5", "F2F0F5", null), new("E04E8A", "F5D547", "F2F0F5", "E9DFC4"), new("F2F0F5", "E04E8A", "E04E8A", "D9A13A")],
        // Electronic: olive and charcoal hoodies.
        [new("4F6B4A", "A0A0A0", "3E3F44", null), new("3E3F44", "7DBF9E", "26262A", null), new("8A8F96", "4F6B4A", "3E3F44", null)],
        // Punk: black, red and white; green, bleached and pink hair.
        [new("1E1E1E", "C23A2E", "B8302A", "4FB548"), new("1E1E1E", "E8E2D6", "2E3A4F", "DCD2B8"), new("E8E2D6", "C23A2E", "1E1E1E", "E8559A")],
        // Metal: denim and black, jet-black hair.
        [new("3F5F8A", "1A1A1A", "1E1F24", "1A1716"), new("1A1A1A", "3F5F8A", "1E1F24", "1A1716"), new("3F5F8A", "1A1A1A", "1E1F24", "6A5A4A")],
    ];

    /// <summary>The outfit for a booked performer, from their act's genre and their place in the band; null if unknown.</summary>
    internal static Outfit? For(GameSession session, ulong performerId, int roleIndex)
    {
        if (session.CaptureProgramme() is not { } programme ||
            programme.Performers.FirstOrDefault(p => p.AgentId == performerId) is not { } performer ||
            performer.SlotIndex < 0 || performer.SlotIndex >= programme.ActIds.Length ||
            session.GetFestivalActs().FirstOrDefault(a => a.Id == programme.ActIds[performer.SlotIndex]) is not { } act ||
            act.Genre < 0 || act.Genre >= ByGenre.Length)
            return null;
        return ByGenre[act.Genre][System.Math.Clamp(roleIndex, 0, 2)];
    }
}

namespace Festival.Simulation;

/// <summary>Music genres. Values are saved, so keep them stable; Indie replaced Rock at 1.</summary>
public static class FestivalGenre
{
    public const int Folk = 0, Indie = 1, Pop = 2, Electronic = 3, Punk = 4, Metal = 5;
    public const int Count = 6;
    public static string Name(int genre) => genre switch
    {
        Folk => "Folk", Indie => "Indie", Pop => "Pop", Electronic => "Electronic", Punk => "Punk", Metal => "Metal", _ => "Unknown",
    };
    /// <summary>The genres whose fans a genre's acts are wary of: credibility there counts against you.</summary>
    public static int[] Clashes(int genre) => genre switch
    {
        Folk => [Electronic], Indie => [Pop], Pop => [Punk, Metal], Electronic => [Folk], Punk => [Pop], Metal => [Pop], _ => [],
    };
}

/// <summary>
/// Advance ticket sales: a fixed price and number sold per tier for now (Tier 1: 25 at £10). The
/// money arrives before preparation and is part of the opening budget; the rest is the starter loan.
/// </summary>
public static class FestivalTickets
{
    public static int PricePennies(int tier) => tier switch { 1 => 1_000, 2 => 1_500, 3 => 3_500, _ => 6_000 };
    /// <summary>Guests (and tickets) per tier: the crowd a festival plans and builds for.</summary>
    public const int GuestsPerTier = 25;
    public static int Sold(int tier) => tier * GuestsPerTier;
    public static long RevenuePennies(int tier) => (long)PricePennies(tier) * Sold(tier);
}

/// <summary>Whether an act will play for the festival as it stands now.</summary>
public enum ActStanding { Available, Stretch, Locked }

/// <summary>
/// The festival's standing with acts: overall reputation and credibility in each genre's scene,
/// all 0–100. It persists across retries and rises after each completed festival.
/// </summary>
public sealed record FestivalStanding(int Reputation, int[] SceneCredibility)
{
    public static FestivalStanding New => new(0, new int[FestivalGenre.Count]);
    /// <summary>A long-established festival every act will play for: later tiers and diagnostics.</summary>
    public static FestivalStanding Established => new(100, new int[FestivalGenre.Count]);
}

/// <summary>
/// The act catalogue and the rules for who will play: an act whose popularity is within 30 of the
/// festival's effective reputation is available at its fee; within 50 it is a stretch booking at
/// 1.5× the fee; beyond that it will not play yet. Each run offers a seeded shortlist.
/// </summary>
public static class ActCatalogue
{
    public const int AvailableReach = 30;
    public const int StretchReach = 50;
    public const int ShortlistSize = 10;
    public const int OutOfReachShown = 5;

    // Fees follow popularity × (0.6 + popularity / 100) pounds; the six original acts keep their fees.
    public static readonly FestivalAct[] All =
    [
        // Folk
        new("act.two-men-harmonium", "Two Men and a Harmonium", FestivalGenre.Folk, 500, 8, 10, 40),
        new("act.parish-ceilidh", "The Parish Council Ceilidh Band", FestivalGenre.Folk, 900, 12, 15, 85),
        new("act.damp-cardigans", "Margaret and the Damp Cardigans", FestivalGenre.Folk, 1100, 15, 25, 60),
        new("act.stile-gate", "Stile & Gate", FestivalGenre.Folk, 1800, 22, 20, 70),
        new("act.muddy-wellies", "The Muddy Wellies", FestivalGenre.Folk, 2200, 26, 30, 45),
        new("act.meadow-lanterns", "Meadow Lanterns", FestivalGenre.Folk, 4000, 40, 20, 80),
        new("act.hay-fever", "The Hay Fever Collective", FestivalGenre.Folk, 5200, 48, 35, 55),
        new("act.whittled-spoons", "The Whittled Spoons", FestivalGenre.Folk, 7600, 62, 45, 75),
        new("act.orchard-chorus", "Orchard Chorus", FestivalGenre.Folk, 7500, 70, 45, 90),
        new("act.bramble-thorne", "Bramble & Thorne", FestivalGenre.Folk, 10800, 78, 65, 80),
        // Indie
        new("act.bus-replacement", "Bus Replacement Service", FestivalGenre.Indie, 400, 6, 20, 15),
        new("act.allotments", "The Allotments", FestivalGenre.Indie, 700, 10, 25, 50),
        new("act.sixth-form-poetry", "Sixth Form Poetry", FestivalGenre.Indie, 1000, 14, 55, 30),
        new("act.gap-year", "Gap Year", FestivalGenre.Indie, 1500, 19, 50, 35),
        new("act.lukewarm-tea", "Lukewarm Tea", FestivalGenre.Indie, 2000, 24, 30, 60),
        new("act.overdue-library-books", "The Overdue Library Books", FestivalGenre.Indie, 3300, 35, 40, 50),
        new("act.planning-permission", "Planning Permission", FestivalGenre.Indie, 4700, 45, 50, 65),
        new("act.barnstorm-circuit", "Barnstorm Circuit", FestivalGenre.Indie, 5500, 55, 35, 65),
        new("act.velvet-bypass", "Velvet Bypass", FestivalGenre.Indie, 8300, 66, 60, 70),
        new("act.cathedral-cities", "The Cathedral Cities", FestivalGenre.Indie, 12100, 84, 75, 75),
        // Pop
        new("act.kerry-co-op", "Kerry from the Co-op", FestivalGenre.Pop, 600, 9, 45, 70),
        new("act.daisy-diaries", "Daisy & the Diaries", FestivalGenre.Pop, 1400, 18, 50, 55),
        new("act.glitter-rota", "Glitter Rota", FestivalGenre.Pop, 3200, 34, 55, 60),
        new("act.the-bunting", "The Bunting", FestivalGenre.Pop, 5000, 47, 40, 80),
        new("act.sugar-tax", "Sugar Tax", FestivalGenre.Pop, 7800, 63, 70, 65),
        new("act.matching-tracksuits", "Matching Tracksuits", FestivalGenre.Pop, 10300, 76, 75, 70),
        new("act.neon-postcards", "Neon Postcards", FestivalGenre.Pop, 11000, 90, 90, 85),
        new("act.heartbreak-hotline", "Heartbreak Hotline", FestivalGenre.Pop, 15200, 97, 95, 80),
        // Electronic
        new("act.dj-spreadsheet", "DJ Spreadsheet", FestivalGenre.Electronic, 500, 7, 30, 90),
        new("act.village-hall-disco", "Village Hall Disco", FestivalGenre.Electronic, 1200, 16, 20, 75),
        new("act.strobe-warning", "Strobe Warning", FestivalGenre.Electronic, 1700, 21, 45, 40),
        new("act.low-battery", "Low Battery", FestivalGenre.Electronic, 3900, 39, 35, 25),
        new("act.modular-compost", "Modular Compost", FestivalGenre.Electronic, 6000, 53, 50, 60),
        new("act.afterparty-nans", "Afterparty at Nan's", FestivalGenre.Electronic, 7200, 60, 40, 70),
        new("act.field-frequency", "Field Frequency", FestivalGenre.Electronic, 8500, 75, 60, 75),
        new("act.sub-bass-badger", "Sub-Bass Badger", FestivalGenre.Electronic, 12600, 86, 80, 65),
        new("act.daybreak-protocol", "Daybreak Protocol", FestivalGenre.Electronic, 15800, 99, 90, 90),
        // Punk
        new("act.neighbourhood-watch", "Neighbourhood Watch", FestivalGenre.Punk, 800, 11, 40, 35),
        new("act.hosepipe-ban", "The Hosepipe Ban", FestivalGenre.Punk, 1600, 20, 45, 30),
        new("act.spat-at-swan", "Spat at a Swan", FestivalGenre.Punk, 2500, 28, 60, 20),
        new("act.unlicensed-bouncy-castle", "Unlicensed Bouncy Castle", FestivalGenre.Punk, 3700, 38, 55, 30),
        new("act.bin-strike", "The Bin Strike", FestivalGenre.Punk, 5800, 52, 60, 40),
        new("act.council-tax", "Council Tax", FestivalGenre.Punk, 8700, 68, 70, 45),
        new("act.copper-static", "Copper Static", FestivalGenre.Punk, 9000, 80, 80, 70),
        new("act.riot-garden-centre", "Riot at the Garden Centre", FestivalGenre.Punk, 13700, 91, 90, 50),
        // Metal
        new("act.doom-fete", "Doom Fete", FestivalGenre.Metal, 1000, 14, 35, 60),
        new("act.tractor-pull", "Tractor Pull", FestivalGenre.Metal, 2700, 30, 50, 55),
        new("act.septic-tank", "The Septic Tank", FestivalGenre.Metal, 4300, 42, 55, 50),
        new("act.pitchfork-uprising", "Pitchfork Uprising", FestivalGenre.Metal, 5500, 50, 60, 60),
        new("act.grimfarrow", "Grimfarrow", FestivalGenre.Metal, 6800, 58, 65, 70),
        new("act.slurry-pit", "Slurry Pit", FestivalGenre.Metal, 9300, 71, 70, 55),
        new("act.combine-harvester-souls", "Combine Harvester of Souls", FestivalGenre.Metal, 11900, 83, 85, 75),
        new("act.thrashing-machine", "Thrashing Machine", FestivalGenre.Metal, 14500, 94, 90, 85),
    ];

    public static FestivalAct? Find(string id) => Array.Find(All, act => act.Id == id);

    /// <summary>Reputation as this act sees it: your scene credibility in its genre helps, clashing scenes hurt.</summary>
    public static int EffectiveReputation(FestivalStanding standing, FestivalAct act) => Math.Clamp(
        standing.Reputation + standing.SceneCredibility[act.Genre] / 2 -
        FestivalGenre.Clashes(act.Genre).Sum(clash => standing.SceneCredibility[clash]) / 4, 0, 100);

    public static ActStanding StandingOf(FestivalStanding standing, FestivalAct act)
    {
        var reach = act.Popularity - EffectiveReputation(standing, act);
        return reach <= AvailableReach ? ActStanding.Available : reach <= StretchReach ? ActStanding.Stretch : ActStanding.Locked;
    }

    /// <summary>The fee this festival pays: stretch bookings cost half as much again.</summary>
    public static int Fee(FestivalStanding standing, FestivalAct act) =>
        StandingOf(standing, act) == ActStanding.Stretch ? act.PricePennies * 3 / 2 : act.PricePennies;

    /// <summary>The overall reputation at which this act would take a stretch booking, scene credibility unchanged.</summary>
    public static int ReputationNeeded(FestivalStanding standing, FestivalAct act) =>
        Math.Clamp(standing.Reputation + act.Popularity - StretchReach - EffectiveReputation(standing, act), 0, 100);

    /// <summary>
    /// This run's offer: about ten acts who will play, or more for more stages (at least one available act per genre where one
    /// exists), then the five locked acts nearest to reach, shown greyed out. Seeded, so a retry or a
    /// reload offers the same acts; booked acts are always included.
    /// </summary>
    public static FestivalAct[] Offer(FestivalStanding standing, ulong seed, int tier, IEnumerable<string> booked, int shortlistSize = ShortlistSize)
    {
        ulong Key(FestivalAct act)
        {
            var hash = 14695981039346656037UL ^ seed ^ ((ulong)tier << 48);
            foreach (var c in act.Id) hash = (hash ^ c) * 1099511628211UL;
            return hash;
        }
        var shuffled = All.OrderBy(Key).ThenBy(act => act.Id, StringComparer.Ordinal).ToArray();
        var reachable = shuffled.Where(act => StandingOf(standing, act) != ActStanding.Locked).ToArray();
        var offer = new List<FestivalAct>();
        for (var genre = 0; genre < FestivalGenre.Count; genre++)
            if (reachable.FirstOrDefault(act => act.Genre == genre && StandingOf(standing, act) == ActStanding.Available) is { } pick) offer.Add(pick);
        foreach (var act in reachable)
        {
            if (offer.Count >= shortlistSize) break;
            if (!offer.Contains(act)) offer.Add(act);
        }
        foreach (var id in booked)
            if (Find(id) is { } act && !offer.Contains(act)) offer.Add(act);
        var nearMisses = shuffled.Where(act => StandingOf(standing, act) == ActStanding.Locked)
            .OrderBy(act => act.Popularity - EffectiveReputation(standing, act)).ThenBy(Key).Take(OutOfReachShown);
        return offer.Concat(nearMisses).ToArray();
    }

    /// <summary>The act popularity a ticket at this price leads guests to expect: about three per pound.</summary>
    public static int ExpectedPopularity(int ticketPricePennies) => Math.Clamp(ticketPricePennies * 3 / 100, 0, 100);

    /// <summary>
    /// After a completed festival: reputation moves 35% of the way toward its result (stars × 20), and
    /// each booked genre's scene moves 25% of the way per set it played.
    /// </summary>
    public static FestivalStanding AfterFestival(FestivalStanding standing, int stars, IEnumerable<int> bookedGenres)
    {
        var target = stars * 20;
        static int Toward(int value, int target, int percent) => Math.Clamp(value + (int)Math.Round((target - value) * percent / 100.0), 0, 100);
        var scenes = standing.SceneCredibility.ToArray();
        foreach (var group in bookedGenres.GroupBy(genre => genre))
            scenes[group.Key] = Toward(scenes[group.Key], target, Math.Min(100, 25 * group.Count()));
        return new(Toward(standing.Reputation, target, 35), scenes);
    }
}

using System.Text.Json;

namespace Festival.Simulation;

/// <summary>How an act feels about playing for you: −100 to +100, 0 until they've played. Kept for the whole campaign.</summary>
public sealed record ActRelationship(string ActId, int Value);

/// <summary>A guest from a set's crowd and the line they gave about it (see <see cref="GigQuotes"/>).</summary>
public sealed record PerformanceQuote(ulong GuestId, bool Fan, int Line);

/// <summary>
/// One set, written down as it ends: when it was due and when it ran, its crowd, how it went, what went wrong, what
/// a few of its crowd said, and how the act's relationship with you moved. The relationship before is the one the act
/// came into this festival with; an act plays one set a festival.
/// </summary>
public sealed record PerformanceRecord(string StageId, int Slot, string ActId,
    long ScheduledStartTick, long ScheduledEndTick, long StartedTick, long EndedTick,
    int PeakCrowd, int SetEndCrowd, int ExpectedCrowd, int AverageEnjoyment, string Reaction, string[] Hiccups,
    ulong[] CrowdIds, PerformanceQuote[] Quotes, int RelationshipBefore, int Delta, int RelationshipAfter, string[] Reasons);

/// <summary>
/// How a gig moves an act's relationship with you, and what that does to their fee. Every number is here.
/// <para>Score = crowd + reaction + enthusiasm − hiccups, held to −30..+25:</para>
/// <list type="bullet">
/// <item>Crowd: the peak and set-end crowds, weighted three to one, against the crowd the act expects: −10 for
/// nobody, 0 at half, +10 at the full expected crowd or more; never above 0 for a set cut short or missed.</item>
/// <item>Reaction: the set-end crowd's average enjoyment, −5 at nothing, 0 at 200, +10 at 500 or more; nothing
/// without a set-end crowd.</item>
/// <item>Enthusiastic applause (<see cref="PerformanceApplauseMath.IsEnthusiastic"/>): +5.</item>
/// <item>Hiccups: never made it on −20, cut short −4, late −4, power cut −6, stopped mid-set −3, boos −6,
/// poor sound −4, a band member collapsed −4.</item>
/// </list>
/// <para>Fee: × (1 − relationship / 200) for a band that likes you, × (1 − relationship / 100) for one that doesn't, so
/// half at +100 and double at −100, rounded to whole pounds.</para>
/// </summary>
public static class GigRules
{
    public const int Lowest = -100, Highest = 100;
    public const int WorstGig = -30, BestGig = 25;
    /// <summary>A liked band's fee falls by 1/200 a point (half at +100); a disliked one's rises by 1/100 a point (double at −100).</summary>
    public const int FeeDivisorLiked = 200, FeeDivisorDisliked = 100;

    public const int CrowdPoints = 10;
    public const int ReactionFloor = -5, ReactionCeiling = 10, ReactionZero = 200, ReactionStep = 30;
    public const int EnthusiasticPoints = 5;
    public const int NoShowPenalty = 20, CutShortPenalty = 4, LatePenalty = 4, PowerCutPenalty = 6, StoppedPenalty = 3,
        BooPenalty = 6, PoorSoundPenalty = 4, CollapsePenalty = 4;
    /// <summary>A set starting more than five seconds after its time counts as late.</summary>
    public const int LateGraceTicks = 400;
    /// <summary>Sound averaging under 40 across the set is poor, as the stage panel calls it.</summary>
    public const int PoorSound = 40;

    // Reaction labels, best to worst.
    public const string Enthusiastic = "enthusiastic", Warm = "warm", Polite = "polite", Smattering = "smattering",
        EmptyField = "empty-field", CutShort = "cut-short", NoShow = "no-show";
    public static readonly string[] Reactions = [Enthusiastic, Warm, Polite, Smattering, EmptyField, CutShort, NoShow];
    // Hiccups, in the order they're listed.
    public const string Late = "late", PowerCut = "power-cut", Stopped = "stopped", Boos = "boos", PoorSoundHiccup = "sound-poor",
        Collapse = "member-collapsed";
    public static readonly string[] HiccupOrder = [Late, PowerCut, Stopped, Boos, PoorSoundHiccup, Collapse];

    /// <summary>
    /// The crowd an act expects at a set: its stage's share of the guests, from 40% for a nobody, through 70% for an act
    /// as popular as the tickets promise, up to 90% for a big draw. Guests come and go for the bar and the loos, so
    /// nobody expects the whole field.
    /// </summary>
    public static int ExpectedCrowd(int guests, int stages, int popularity, int expectedPopularity)
    {
        var share = Math.Clamp(40 + 30 * popularity / Math.Max(1, expectedPopularity), 40, 90);
        return Math.Max(1, guests * share / 100 / Math.Max(1, stages));
    }

    /// <summary>
    /// The reaction label for a set from its facts: hardly anyone there at any point is an empty field, and only a few
    /// of its biggest crowd still there to clap at the end is a smattering.
    /// </summary>
    public static string ReactionOf(bool started, bool cutShort, int peak, int setEnd, int averageEnjoyment, bool enthusiastic, int expected) =>
        !started ? NoShow : cutShort ? CutShort : peak * 4 < expected || setEnd == 0 && peak < 4 ? EmptyField : enthusiastic ? Enthusiastic :
        setEnd * 4 < peak ? Smattering : averageEnjoyment >= 300 ? Warm : Polite;

    public static int CrowdScore(int peak, int setEnd, int expected) =>
        Math.Clamp((3 * peak + setEnd) * CrowdPoints / 2 / Math.Max(1, expected) - CrowdPoints, -CrowdPoints, CrowdPoints);

    public static int ReactionScore(int setEnd, int averageEnjoyment) => setEnd == 0 ? 0 :
        Math.Clamp((averageEnjoyment - ReactionZero) / ReactionStep, ReactionFloor, ReactionCeiling);

    public static int Penalty(string hiccup) => hiccup switch
    {
        Late => LatePenalty, PowerCut => PowerCutPenalty, Stopped => StoppedPenalty, Boos => BooPenalty,
        PoorSoundHiccup => PoorSoundPenalty, Collapse => CollapsePenalty, _ => 0,
    };

    /// <summary>The gig's score and the reasons behind it, biggest first.</summary>
    public static (int Delta, string[] Reasons) Score(string reaction, string[] hiccups, int peak, int setEnd, int expected, int averageEnjoyment)
    {
        var parts = new List<(int Points, string Reason)>();
        var crowd = CrowdScore(peak, setEnd, expected);
        // A crowd that never got the whole set earns the act nothing.
        if (reaction is CutShort or NoShow) crowd = Math.Min(crowd, 0);
        parts.Add((crowd, crowd >= CrowdPoints / 2 ? "a big crowd" : crowd > 0 ? "a fair crowd" : crowd > -CrowdPoints / 2 ? "a thin crowd" : "hardly anyone there"));
        var heard = ReactionScore(setEnd, averageEnjoyment);
        if (heard != 0) parts.Add((heard, heard > 0 ? "the crowd loved it" : "a flat crowd"));
        if (reaction == Enthusiastic) parts.Add((EnthusiasticPoints, "an enthusiastic send-off"));
        if (reaction == NoShow) parts.Add((-NoShowPenalty, "never made it on stage"));
        if (reaction == CutShort) parts.Add((-CutShortPenalty, "cut short"));
        foreach (var hiccup in hiccups) parts.Add((-Penalty(hiccup), HiccupWord(hiccup).ToLowerInvariant()));
        var delta = Math.Clamp(parts.Sum(part => part.Points), WorstGig, BestGig);
        // The two or three that mattered most, in the direction the gig went.
        var reasons = parts.Where(part => part.Points != 0 && (delta >= 0 ? part.Points > 0 : part.Points < 0) || delta == 0 && part.Points != 0)
            .OrderByDescending(part => Math.Abs(part.Points)).Take(3).Select(part => part.Reason).ToArray();
        return (delta, reasons);
    }

    public static int Apply(int relationship, int delta) => Math.Clamp(relationship + delta, Lowest, Highest);

    /// <summary>The fee multiplier's divisor for a relationship: the liked or the disliked slope.</summary>
    private static int FeeDivisor(int relationship) => relationship >= 0 ? FeeDivisorLiked : FeeDivisorDisliked;

    /// <summary>
    /// An act's fee for a festival, given their relationship with you: base × (1 − relationship / divisor), exact at 0,
    /// else to the nearest pound.
    /// </summary>
    public static int Fee(int basePennies, int relationship)
    {
        if (relationship == 0) return basePennies;
        var rel = Math.Clamp(relationship, Lowest, Highest);
        var divisor = FeeDivisor(rel);
        return Math.Max(100, (int)(((long)basePennies * (divisor - rel) + divisor * 50L) / (divisor * 100L) * 100));
    }

    /// <summary>How much the relationship moves the fee, in percent: −50 at +100, +100 at −100.</summary>
    public static decimal FeePercent(int relationship) => Math.Clamp(relationship, Lowest, Highest) is var rel ? -rel * 100m / FeeDivisor(rel) : 0;

    public static string ReactionWord(string reaction) => reaction switch
    {
        Enthusiastic => "Enthusiastic", Warm => "Warm", Polite => "Polite", Smattering => "A smattering",
        EmptyField => "An empty field", CutShort => "Cut short", NoShow => "Never made it on stage", _ => reaction,
    };

    public static string HiccupWord(string hiccup) => hiccup switch
    {
        Late => "Late on stage", PowerCut => "Power cut", Stopped => "Stopped mid-set", Boos => "Boos",
        PoorSoundHiccup => "Poor sound", Collapse => "Band member collapsed", _ => hiccup,
    };

    /// <summary>
    /// The hook for things that happen to a relationship between festivals (a falling-out in the press, a kind word from
    /// a friend of the band): nothing yet, so it hands the relationships on as they are.
    /// </summary>
    public static ActRelationship[]? BetweenFestivals(ActRelationship[]? relationships, ulong seed, int nextTier) => relationships;

    /// <summary>Relationships after a festival: those it opened with, moved by each of its sets.</summary>
    public static ActRelationship[]? After(ActRelationship[]? before, IEnumerable<PerformanceRecord> records)
    {
        var values = (before ?? []).ToDictionary(item => item.ActId, item => item.Value, StringComparer.Ordinal);
        foreach (var record in records) values[record.ActId] = record.RelationshipAfter;
        return Normalised(values);
    }

    /// <summary>Saved shape: non-zero relationships only, sorted by act; null when there are none.</summary>
    public static ActRelationship[]? Normalised(IReadOnlyDictionary<string, int> values)
    {
        var kept = values.Where(item => item.Value != 0).OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new ActRelationship(item.Key, item.Value)).ToArray();
        return kept.Length == 0 ? null : kept;
    }
}

/// <summary>
/// What guests say about a set afterwards: a list of lines for each reaction, one for fans of the act's genre and one
/// for everyone else. The sim picks who is quoted and which line; this table only holds the words.
/// </summary>
public static class GigQuotes
{
    private static readonly Dictionary<string, (string[] Fans, string[] Others)> Lines = new()
    {
        [GigRules.Enthusiastic] = (
            [
                "I've lost my voice and one shoe. Worth it.",
                "Best set I've seen since the school disco. Better, actually.",
                "They had the whole field in the palm of their hand. Slightly sticky hand.",
                "I'd follow them on tour. I'd follow them to Swindon.",
            ],
            [
                "Didn't know a single song. Knew all the words by the end.",
                "Came for the bar, stayed for the encore.",
                "I'm not one for dancing, but my legs had other ideas.",
                "My husband cried. He says it was hay fever. It wasn't.",
            ]),
        [GigRules.Warm] = (
            [
                "Solid set. They really hit their stride after the second song.",
                "Lovely stuff. Proper music, played properly.",
                "Not their best night, but their middling is most people's best.",
                "I'd give it four pints out of five.",
            ],
            [
                "Pleasant. Very pleasant. Like a good cup of tea.",
                "Not my usual thing, but I tapped a foot. Possibly both.",
                "Nice enough lot. The drummer waved at me. Or at the bloke behind me.",
                "Went down better than the chips, and the chips were good.",
            ]),
        [GigRules.Polite] = (
            [
                "They were fine. I wanted more than fine.",
                "Bit of a slow burn. Mostly slow.",
                "I clapped. I'm a fan, I have to clap.",
                "Good songs, shame about the atmosphere.",
            ],
            [
                "It was music. It happened. I was there.",
                "Polite applause, polite band, polite field.",
                "Can't hum a note of it now, but it was nice at the time.",
                "Background music, really. Lovely background, mind.",
            ]),
        [GigRules.Smattering] = (
            [
                "There were more of them on stage than us in front of it. Nearly.",
                "I clapped extra loud to make up for everyone else.",
                "Criminally under-watched. I'll be telling people I was there.",
                "Shame about the turnout. They deserved a field, not a corner of one.",
            ],
            [
                "Me, a man in a bucket hat and a very confused sheep.",
                "I only stopped to tie my lace.",
                "You could hear every bum note. There was room for them.",
                "Intimate, they'd call it. Empty, I'd call it.",
            ]),
        [GigRules.EmptyField] = (
            [
                "I must have been the only one who stayed. Their loss.",
                "By the last song it was just me and the sound desk.",
                "I'd have stayed if anyone else had.",
            ],
            [
                "Was anyone actually playing? I was at the bar.",
                "Heard something from the queue. Didn't fancy it.",
                "Wandered off for a burger. Didn't wander back.",
            ]),
        [GigRules.CutShort] = (
            [
                "Three songs in and the plug got pulled. Story of my life.",
                "They were just warming up. Then they were just cooling down.",
                "I paid for a full set and got a demo tape.",
                "Rock and roll is about the unexpected. That was a bit too unexpected.",
            ],
            [
                "Is that it? I'd only just put my pint down.",
                "One minute music, the next minute a generator having a sulk.",
                "Short and not very sweet.",
                "I've had longer conversations in the toilet queue.",
            ]),
        [GigRules.NoShow] = (
            [
                "Waited an hour for them. They didn't wait for me.",
                "Bought the T-shirt in advance. Bit awkward now.",
                "I'll see them next year. Assuming they find the stage.",
                "Their biggest no-show since the album nobody bought.",
            ],
            [
                "Nobody came on. Nobody told us. Classic.",
                "I stood in a field looking at an empty stage. Could've done that at home.",
                "The roadies got a round of applause. They earned it, frankly.",
                "Silent disco without the headphones.",
            ]),
    };

    public static int Count(string reaction, bool fan) => Lines.TryGetValue(reaction, out var lines) ? (fan ? lines.Fans : lines.Others).Length : 0;

    public static string Line(string reaction, PerformanceQuote quote) =>
        Lines.TryGetValue(reaction, out var lines) && (quote.Fan ? lines.Fans : lines.Others) is var list && quote.Line >= 0 && quote.Line < list.Length
            ? list[quote.Line] : "";

    /// <summary>
    /// Two or three of the set's crowd, picked by a keyed roll on the campaign seed, the stage, the slot and the guest:
    /// steady for a save and its reload. Each says a line for how the set went, as a fan of the genre or not.
    /// </summary>
    public static PerformanceQuote[] Pick(ulong seed, string stageId, int slot, string reaction, IReadOnlyList<ulong> crowd, Func<ulong, bool> fan)
    {
        var stageKey = 1469598103934665603UL;
        foreach (var ch in stageId) stageKey = unchecked((stageKey ^ ch) * 1099511628211UL);
        ulong Roll(ulong id, ulong salt) => Mix(unchecked(seed ^ stageKey ^ ((ulong)slot << 40) ^ salt) + id * 0x9E3779B97F4A7C15UL);
        var wanted = crowd.Count < 3 ? crowd.Count : 2 + (int)(Roll(0, 0x51554F54UL) % 2);
        return crowd.OrderBy(id => Roll(id, 0x57484FUL)).ThenBy(id => id).Take(wanted).Select(id =>
        {
            var isFan = fan(id);
            var count = Count(reaction, isFan);
            return new PerformanceQuote(id, isFan, count == 0 ? 0 : (int)(Roll(id, 0x4C494E45UL) % (ulong)count));
        }).ToArray();
    }

    private static ulong Mix(ulong value)
    {
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}

public sealed partial class GameSession
{
    // Every set that has ended this festival, in the order they ended; null before the first.
    private PerformanceRecord[]? _performanceRecords;
    /// <summary>The sets played (or missed) so far this festival, in the order they ended.</summary>
    public IReadOnlyList<PerformanceRecord> PerformanceRecords => _performanceRecords ?? [];
    internal string? PerformanceRecordsCanonicalJson => _performanceRecords is null ? null : JsonSerializer.Serialize(_performanceRecords);

    /// <summary>An act's relationship with you coming into this festival; what its fee here is priced on.</summary>
    public int ActRelationship(string actId) => RelationshipIn(_preparation?.ActRelationships, actId);
    private static int RelationshipIn(ActRelationship[]? relationships, string actId) =>
        relationships?.FirstOrDefault(item => item.ActId == actId)?.Value ?? 0;
    /// <summary>Relationships as this festival will leave them, once its sets so far are counted.</summary>
    public ActRelationship[]? RelationshipsAfterFestival => GigRules.After(_preparation?.ActRelationships, PerformanceRecords);

    /// <summary>The fan test a quote uses: the same enthusiasm that earns a listener the bigger share of a set.</summary>
    private const int FanEnthusiasm = 60;

    /// <summary>Writes a stage's set down as it finishes, once; only for a booked act.</summary>
    private void RecordPerformance(int stage, LivePerformanceSnapshot live)
    {
        if (_preparation is not { } p || StageProgramme(stage) is not { CurrentSlot: >= 0 } q || StageAct(stage) is not { } act) return;
        var def = Stages[stage];
        var slot = q.CurrentSlot;
        if (PerformanceRecords.Any(record => record.StageId == def.Id && record.Slot == slot)) return;
        _performanceRecords = [.. PerformanceRecords, MakePerformanceRecord(CampaignSeed, p, Stages.Count, def, slot, act, live,
            id => FestivalAffinity(id, act) >= FanEnthusiasm)];
    }

    /// <summary>The record for a finished set: its facts from the live set, then the score and quotes that follow from them.</summary>
    private static PerformanceRecord MakePerformanceRecord(ulong seed, PreparationSnapshot p, int stageCount, FestivalStage def, int slot,
        FestivalAct act, LivePerformanceSnapshot live, Func<ulong, bool> fan)
    {
        var started = live.StartedTick >= 0;
        var cutShort = started && live.LastReaction is not ("set-finished-applause" or "set-finished-muted");
        var setEnd = live.SetEndAudienceCount;
        var average = setEnd == 0 ? 0 : live.SetEndEnjoymentTotal / setEnd;
        var expected = GigRules.ExpectedCrowd(FestivalTickets.Sold(p.Tier), stageCount, act.Popularity,
            ActCatalogue.ExpectedPopularity(FestivalTickets.PricePennies(p.Tier)));
        var enthusiastic = PerformanceApplauseMath.IsEnthusiastic(setEnd, live.SetEndEnjoymentTotal);
        var reaction = GigRules.ReactionOf(started, cutShort, live.PeakListeners, setEnd, average, enthusiastic, expected);
        var hiccups = HiccupsOf(live);
        var (delta, reasons) = GigRules.Score(reaction, hiccups, live.PeakListeners, setEnd, expected, average);
        var before = RelationshipIn(p.ActRelationships, act.Id);
        // Whoever heard some of it; when nobody heard a note, whoever waited for it.
        var crowd = live.Listeners.Where(item => item.ListenedTicks > 0).Select(item => item.AgentId)
            .Concat(live.SetEndAudienceIds).Distinct().Order().ToArray();
        if (crowd.Length == 0) crowd = live.Listeners.Where(item => item.Place is not null).Select(item => item.AgentId).Order().ToArray();
        return new(def.Id, slot, act.Id, p.StartedTick + def.SlotStarts[slot], p.StartedTick + def.SlotEnds[slot], live.StartedTick, live.EndedTick,
            live.PeakListeners, setEnd, expected, average, reaction, hiccups, crowd, GigQuotes.Pick(seed, def.Id, slot, reaction, crowd, fan),
            before, delta, GigRules.Apply(before, delta), reasons);
    }

    private static string[] HiccupsOf(LivePerformanceSnapshot live)
    {
        var hiccups = new List<string>();
        if (live.StartedTick >= 0 && live.StartedTick - live.PlannedTick > GigRules.LateGraceTicks) hiccups.Add(GigRules.Late);
        if (live.PowerCuts > 0) hiccups.Add(GigRules.PowerCut);
        if (live.Stoppages > 0) hiccups.Add(GigRules.Stopped);
        if (live.Boos > 0) hiccups.Add(GigRules.Boos);
        if (live.SoundSamples > 0 && live.SoundTotal < (long)GigRules.PoorSound * live.SoundSamples) hiccups.Add(GigRules.PoorSoundHiccup);
        if (live.MemberCollapsed) hiccups.Add(GigRules.Collapse);
        return hiccups.ToArray();
    }

    /// <summary>
    /// The saved records: each finished set of a booked programme recorded once and in full, no record for a set still to
    /// come, the set now recorded once it has finished, and every record following from its own facts:
    /// its act and times from the programme, its numbers within the guests, its score, reasons and quotes as the rules
    /// give them, and its quotes from guests in its crowd.
    /// </summary>
    private static string? ValidatePersistedPerformanceRecords(SessionPersistenceSnapshot s)
    {
        const string invalid = "Performance records invalid.";
        if (s.PerformanceRecords is not { } records)
        {
            // No records is right only while no set of a booked programme has finished.
            if (s.Programme is not { } booked || s.LivePerformances is not { } sets) return null;
            for (var stage = 0; stage < sets.Length && stage < booked.Stages.Length; stage++)
                if (sets[stage] is { } set && booked.Stages[stage].ActIds.Length > 0 &&
                    set.Stage == LiveSetStage.Finished && set.EndedTick >= set.PlannedTick)
                    return invalid;
            return null;
        }
        if (records.Length == 0 || records.Any(record => record is null || record.StageId is null || record.ActId is null || record.Reaction is null ||
                record.Hiccups is null || record.CrowdIds is null || record.Quotes is null || record.Reasons is null || record.Quotes.Any(quote => quote is null)) ||
            s.Preparation is not { } p || s.Programme is not { } programme || s.LivePerformances is not { } lives || p.Status == PreparationStatus.Preparing)
            return invalid;
        var stages = SavedStages(s);
        var guests = p.People.Where(person => person.Role == ProtectedPersonRole.Guest).ToDictionary(person => person.AgentId);
        for (var stage = 0; stage < stages.Count; stage++)
        {
            var def = stages[stage];
            var q = programme.Stages[stage];
            var live = lives[stage];
            var mine = records.Where(record => record.StageId == def.Id).Select(record => record.Slot).ToArray();
            var due = live is null || q.ActIds.Length != def.SlotCount ? 0 : q.CurrentSlot + (live.Stage == LiveSetStage.Finished && live.EndedTick >= live.PlannedTick ? 1 : 0);
            // The set just finished is always recorded; an earlier one may not be, where a day was jumped ahead.
            if (mine.Any(slot => slot >= due) || due > 0 && q.CurrentSlot + 1 == due && !mine.Contains(q.CurrentSlot)) return invalid;
        }
        foreach (var record in records)
        {
            var stage = FestivalStages.IndexOf(stages, record.StageId);
            if (stage < 0) return invalid;
            var def = stages[stage];
            var q = programme.Stages[stage];
            if (ActCatalogue.Find(record.ActId) is not { } act || record.Slot < 0 || record.Slot >= def.SlotCount || q.ActIds[record.Slot] != record.ActId) return invalid;
            var fan = (ulong id) => guests.TryGetValue(id, out var guest) && Affinity(s.CampaignSeed, id, guest.ExpectedGenre, act) >= FanEnthusiasm;
            var expected = GigRules.ExpectedCrowd(FestivalTickets.Sold(p.Tier), stages.Count, act.Popularity,
                ActCatalogue.ExpectedPopularity(FestivalTickets.PricePennies(p.Tier)));
            var started = record.StartedTick >= 0;
            var (delta, reasons) = GigRules.Score(record.Reaction, record.Hiccups, record.PeakCrowd, record.SetEndCrowd, expected, record.AverageEnjoyment);
            var before = RelationshipIn(p.ActRelationships, act.Id);
            if (record.ScheduledStartTick != p.StartedTick + def.SlotStarts[record.Slot] || record.ScheduledEndTick != p.StartedTick + def.SlotEnds[record.Slot] ||
                started && (record.StartedTick < record.ScheduledStartTick || record.StartedTick >= record.ScheduledEndTick) || record.StartedTick < -1 ||
                record.EndedTick < record.ScheduledStartTick || record.EndedTick > s.CurrentTick ||
                record.PeakCrowd < record.SetEndCrowd || record.SetEndCrowd < 0 || record.PeakCrowd > guests.Count || record.AverageEnjoyment < 0 ||
                record.AverageEnjoyment > FestivalSlotDurationTicks / 80 * 30 || record.ExpectedCrowd != expected ||
                !GigRules.Reactions.Contains(record.Reaction) || (record.Reaction == GigRules.NoShow) == started ||
                !record.Hiccups.SequenceEqual(GigRules.HiccupOrder.Where(record.Hiccups.Contains)) || record.Hiccups.Distinct().Count() != record.Hiccups.Length ||
                !started && record.Hiccups.Any(item => item is not GigRules.Collapse) ||
                !record.CrowdIds.SequenceEqual(record.CrowdIds.Distinct().Order()) || record.CrowdIds.Any(id => !guests.ContainsKey(id)) ||
                record.SetEndCrowd > record.CrowdIds.Length ||
                !record.Quotes.SequenceEqual(GigQuotes.Pick(s.CampaignSeed, def.Id, record.Slot, record.Reaction, record.CrowdIds, fan)) ||
                record.Delta != delta || !record.Reasons.SequenceEqual(reasons) ||
                record.RelationshipBefore != before || record.RelationshipAfter != GigRules.Apply(before, delta))
                return invalid;
            // The set on stage now: its record matches the finished live set.
            var live = lives[stage];
            if (live is not null && q.CurrentSlot == record.Slot)
            {
                var setEnd = live.SetEndAudienceCount;
                if (live.Stage != LiveSetStage.Finished || record.StartedTick != live.StartedTick ||
                    record.PeakCrowd != live.PeakListeners || record.SetEndCrowd != setEnd ||
                    record.AverageEnjoyment != (setEnd == 0 ? 0 : live.SetEndEnjoymentTotal / setEnd) || !record.Hiccups.SequenceEqual(HiccupsOf(live)))
                    return invalid;
            }
        }
        if (records.Select(record => (record.StageId, record.Slot)).Distinct().Count() != records.Length ||
            records.Select(record => record.ActId).Distinct().Count() != records.Length)
            return invalid;
        return null;
    }

    private static string? ValidatePersistedRelationships(PreparationSnapshot p)
    {
        if (p.ActRelationships is not { } relationships) return null;
        return relationships.Length == 0 || relationships.Any(item => item is null || item.ActId is null || ActCatalogue.Find(item.ActId) is null ||
                item.Value is 0 or < GigRules.Lowest or > GigRules.Highest) ||
            !relationships.Select(item => item.ActId).SequenceEqual(relationships.Select(item => item.ActId).Distinct().Order(StringComparer.Ordinal)) ||
            p.Tier == 1 && p.CarriedIn is null
            ? "Act relationships invalid." : null;
    }
}

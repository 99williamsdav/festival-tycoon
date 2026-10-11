using System.Text.Json.Serialization;

namespace Festival.Simulation;

/// <summary>
/// The player steward up on a stage's deck: which stage, the tick they stepped on, and, while that stage's set is
/// playing, the tick the crowd started counting it as a stage invasion (when they stepped on mid-set, or when the set
/// started under them). Stepping down, or the set finishing, ends the invasion; stepping down clears the record.
/// </summary>
public sealed record StewardOnStage(string StageId, long SinceTick)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? InvadedTick { get; init; }
}

/// <summary>A set the player got up on stage during: its stage and slot, and how many times (each one counted on the score).</summary>
public sealed record InvadedSet(string StageId, int Slot, int Count);

/// <summary>
/// A line shouted at the player steward up on a stage: by someone in its crowd or one of its band. Angry while the set
/// plays, milder between sets. <paramref name="StartedTick"/> is when this shout began: one shout per speaker slot at a time.
/// </summary>
public sealed record StageShout(ulong SpeakerId, string Text, bool Band, bool Angry, long StartedTick);

/// <summary>
/// The player steward on a stage, and what it costs them. Only the player can get up there: the deck is reached by
/// its steps (the trailer's north stairs from backstage, the Pond Stage's side stair at its +X end), and nobody else
/// ever routes there but the band. The crowd and band shout (a keyed, rate-limited pick each slot, presentation
/// reads it), the watching crowd's mood sinks while they stay through a set (faster the longer they linger), the act's
/// set gets a "stage invader" hiccup (a one-off relationship hit), and the shift's score counts it.
/// </summary>
public static class StageInvasionRules
{
    /// <summary>First shout comes this long after stepping on (or the set starting under them).</summary>
    public const int FirstShoutTicks = 40;
    /// <summary>A crowd shout every 2 s during a set, every 5 s between sets; the band every 3 s, or 6 s between sets.</summary>
    public const int CrowdEveryTicks = 160, CrowdMildEveryTicks = 400, BandEveryTicks = 240, BandMildEveryTicks = 480;
    /// <summary>The band's shouts fall between the crowd's.</summary>
    public const int BandOffsetTicks = 100;
    /// <summary>How long a shout shows: at most its slot.</summary>
    public const int ShoutTicks = 150;
    /// <summary>Who can shout: guests this close to the player.</summary>
    public const int ShoutRangeMillimetres = 20_000;
    /// <summary>
    /// Each second on the deck during a set costs each of its watching crowd this much satisfaction (of 10,000, before
    /// their prissiness): 2 for the first 15 s, 4 to 45 s, then 8.
    /// </summary>
    public const int MoodLossPerSecond = 2, MoodLossLingerPerSecond = 4, MoodLossLongPerSecond = 8;
    public const int LingerTicks = 1_200, LongTicks = 3_600;
    /// <summary>The drum kit on the drummer's mark: the player keeps this far from its middle.</summary>
    public const int DrumKitClearanceMillimetres = 900;
    /// <summary>The band's marks: the player keeps this far from each, so a performer can always step onto theirs.</summary>
    public const int MarkClearanceMillimetres = 600;
    /// <summary>The grade loses a point per 10 s on a stage during a set, at most 5.</summary>
    public const int GradeTicksPerPoint = 800, GradeMaxPenalty = 5;

    public static readonly string[] CrowdLines =
    [
        "Get off the stage!", "Who's that on the stage?!", "You're not on the bill, mate!", "Is this the support act?",
        "Off! Off! Off!", "Nobody came to see you!", "Don't give up the day job!", "Hi-vis is not a stage outfit!",
    ];
    public static readonly string[] CrowdMildLines = ["Is that the next act?", "Sound check?", "One-two, one-two?", "Are you the roadie?"];
    public static readonly string[] BandLines =
        ["Oi! Get off the stage!", "This is our set!", "Security!... oh wait.", "Mate, we're playing here!", "Get your own gig!"];
    public static readonly string[] BandMildLines = ["We're setting up here, mate.", "Mind the leads!"];

    public static int MoodLoss(long ticksInvaded) =>
        ticksInvaded < LingerTicks ? MoodLossPerSecond : ticksInvaded < LongTicks ? MoodLossLingerPerSecond : MoodLossLongPerSecond;

    public static int GradePenalty(long stageTicks) => (int)Math.Min(GradeMaxPenalty, stageTicks / GradeTicksPerPoint);
}

public sealed partial class GameSession
{
    /// <summary>The stage whose deck boards this cell is on, by index in <see cref="Stages"/>; -1 off every deck.</summary>
    private int DeckStageAt(GridCell cell)
    {
        var stages = Stages;
        for (var i = 0; i < stages.Count; i++)
            if (stages[i].Deck(cell)) return i;
        return -1;
    }

    /// <summary>On a stage's deck or its steps: where the camera rides up.</summary>
    public static bool OnStageCells(IReadOnlyList<FestivalStage> stages, GridCell cell) => stages.Any(stage => stage.Deck(cell) || stage.Stairs(cell));

    /// <summary>The stage the player steward is up on now, if any.</summary>
    public string? PlayerStageId => _stewardShift?.OnStage?.StageId;

    private static bool SetPlaying(LivePerformanceSnapshot? live) => live?.Stage is LiveSetStage.Live or LiveSetStage.Interrupted;

    /// <summary>
    /// After the crowd and band have moved: where the player stands against the stages' decks. Stepping onto one during
    /// its set (or a set starting with them on it) is an invasion, counted once on the score and on the set; each tick of
    /// it counts; each second of it the set's watching crowd loses a little mood, more the longer it lasts.
    /// </summary>
    private void AdvanceStageInvasion(ulong me)
    {
        var shift = _stewardShift!;
        var nav = _navigationAgents[new(me)];
        var stage = DeckStageAt(TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres));
        if (stage < 0)
        {
            if (shift.OnStage is not null) _stewardShift = shift with { OnStage = null };
            return;
        }
        var id = Stages[stage].Id;
        var on = shift.OnStage is { } was && was.StageId == id ? was : new StewardOnStage(id, CurrentTick);
        var live = stage < _livePerformances.Length ? _livePerformances[stage] : null;
        if (!SetPlaying(live)) on = on with { InvadedTick = null };
        else if (on.InvadedTick is null)
        {
            on = on with { InvadedTick = CurrentTick };
            _livePerformances[stage] = live! with { StageInvasions = live.StageInvasions + 1 };
            Score(s => s with { StageInvasions = s.StageInvasions + 1 });
            var slot = StageProgramme(stage)!.CurrentSlot;
            var sets = (_stewardShift!.InvadedSets ?? []).ToList();
            var at = sets.FindIndex(item => item.StageId == id && item.Slot == slot);
            if (at < 0) sets.Add(new(id, slot, 1)); else sets[at] = sets[at] with { Count = sets[at].Count + 1 };
            _stewardShift = _stewardShift with { InvadedSets = sets.ToArray() };
            live = _livePerformances[stage];
        }
        _stewardShift = _stewardShift! with { OnStage = on };
        if (on.InvadedTick is not { } invaded) return;
        Score(s => s with { StageTicks = s.StageTicks + 1 });
        if ((CurrentTick - invaded) % 80 != 79) return;
        var loss = StageInvasionRules.MoodLoss(CurrentTick - invaded);
        foreach (var listener in live!.Listeners)
            if (listener.AtPlace && IsGuest(listener.AgentId) && _persons[listener.AgentId] is { Admitted: true, Departed: false })
                ChangeSatisfaction(listener.AgentId, -UnpleasantFor(listener.AgentId, loss), MoodCause.StageInvader);
    }

    /// <summary>
    /// What's being shouted at the player steward up on a stage right now: at most one from the crowd and one from the
    /// band. A pure function of the day so far (no state, nothing saved): each shout slot's speaker and line are keyed on
    /// the campaign seed, the stage and the slot's tick; lines go round in a keyed order without repeating.
    /// </summary>
    public IReadOnlyList<StageShout> StageShouts()
    {
        if (_stewardShift is not { PlayerId: { } me, OnStage: { } on } || _preparation?.Status != PreparationStatus.Running) return [];
        var stage = FestivalStages.IndexOf(Stages, on.StageId);
        if (stage < 0) return [];
        var def = Stages[stage];
        var live = stage < _livePerformances.Length ? _livePerformances[stage] : null;
        var angry = on.InvadedTick is not null;
        var from = (on.InvadedTick ?? on.SinceTick) + StageInvasionRules.FirstShoutTicks;
        var player = _navigationAgents[new(me)];
        var stageKey = 1469598103934665603UL;
        foreach (var ch in def.Id) stageKey = unchecked((stageKey ^ ch) * 1099511628211UL);
        ulong Roll(long tick, ulong salt) => SplitMix(unchecked(CampaignSeed ^ stageKey ^ salt ^ (ulong)tick * 0x9E3779B97F4A7C15UL));
        var shouts = new List<StageShout>();

        void Shout(IReadOnlyList<ulong> speakers, string[] lines, int every, int offset, bool band, ulong salt)
        {
            var elapsed = CurrentTick - from - offset;
            if (elapsed < 0 || speakers.Count == 0) return;
            var slot = elapsed / every;
            var started = from + offset + slot * every;
            if (CurrentTick - started >= Math.Min(StageInvasionRules.ShoutTicks, every)) return;
            var speaker = speakers[(int)(Roll(started, salt) % (ulong)speakers.Count)];
            // Round the lines in a keyed order: a stride coprime with the count never repeats until all are said.
            var count = lines.Length;
            var stride = 1;
            for (var s = (int)(Roll(on.SinceTick, salt ^ 0x5354UL) % (ulong)count); s < 2 * count; s++)
                if (s > 0 && Gcd(s, count) == 1) { stride = s; break; }
            var first = (long)(Roll(on.SinceTick, salt ^ 0x4C49UL) % (ulong)count);
            shouts.Add(new(speaker, lines[(int)((first + slot * stride) % count)], band, angry, started));
        }

        // The crowd: during the set, its listeners at their places near enough; between sets, anyone in its ground near enough.
        var range = (long)StageInvasionRules.ShoutRangeMillimetres * StageInvasionRules.ShoutRangeMillimetres;
        bool Near(ulong id)
        {
            var nav = _navigationAgents[new(id)];
            long dx = nav.XMillimetres - player.XMillimetres, dz = nav.ZMillimetres - player.ZMillimetres;
            return dx * dx + dz * dz <= range;
        }
        bool CanShout(ulong id) => _persons[id] is { Admitted: true, Departed: false, ConductStage: not (DisorderStage.Argument or DisorderStage.Fight or DisorderStage.Injured) } &&
            !PersonCollapsed(id) && Near(id);
        var crowd = angry
            ? live!.Listeners.Where(l => l.AtPlace && IsGuest(l.AgentId) && CanShout(l.AgentId)).Select(l => l.AgentId).Order().ToList()
            : PeopleIn(PersonView.Roster).Where(p => p.Role == ProtectedPersonRole.Guest && CanShout(p.Id) &&
                TraversalGrid.WorldToCell(_navigationAgents[new(p.Id)].XMillimetres, _navigationAgents[new(p.Id)].ZMillimetres) is var cell &&
                (def.InAudienceArea(cell) || cell.X >= def.NearGround.MinX && cell.X <= def.NearGround.MaxX && cell.Z >= def.NearGround.MinZ && cell.Z <= def.NearGround.MaxZ))
                .Select(p => p.Id).Order().ToList();
        Shout(crowd, angry ? StageInvasionRules.CrowdLines : StageInvasionRules.CrowdMildLines,
            angry ? StageInvasionRules.CrowdEveryTicks : StageInvasionRules.CrowdMildEveryTicks, 0, false, 0x43524F57UL);
        // The band: whoever of this stage's band is up on the deck.
        var band = (live?.Performers ?? []).Where(p => !PersonCollapsed(p.AgentId) &&
            def.Deck(TraversalGrid.WorldToCell(_navigationAgents[new(p.AgentId)].XMillimetres, _navigationAgents[new(p.AgentId)].ZMillimetres)))
            .Select(p => p.AgentId).Order().ToList();
        Shout(band, angry ? StageInvasionRules.BandLines : StageInvasionRules.BandMildLines,
            angry ? StageInvasionRules.BandEveryTicks : StageInvasionRules.BandMildEveryTicks, StageInvasionRules.BandOffsetTicks, true, 0x42414E44UL);
        return shouts;
    }

    private static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; }

    private static ulong SplitMix(ulong value)
    {
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    /// <summary>Every band mark on the open stages, world millimetres.</summary>
    private IEnumerable<(int X, int Z)> BandMarks() => Stages.SelectMany(stage => stage.PerformerMarks).Select(TraversalGrid.CellCentre);

    private static readonly string[] StairIntents = ["performance.visible-stairs", "performance.stage-entry", "performance.stage-exit-stair", "performance.stage-exit-access"];

    /// <summary>A stage's steps while one of its band is walking on or off by them.</summary>
    private bool StairsBusyAt(GridCell cell)
    {
        var stages = Stages;
        for (var i = 0; i < stages.Count; i++)
        {
            if (!stages[i].Stairs(cell) || i >= _livePerformances.Length || _livePerformances[i] is not { } live) continue;
            foreach (var performer in live.Performers)
                if (_navigationAgents.TryGetValue(new(performer.AgentId), out var nav) && nav.Action == AgentNavigationAction.Travelling &&
                    StairIntents.Contains(nav.IntentId)) return true;
        }
        return false;
    }

    /// <summary>The drum kits on the stages' drummers' marks, world millimetres.</summary>
    private IEnumerable<(int X, int Z)> DrumKits() => Stages.Select(stage => TraversalGrid.CellCentre(stage.PerformerMarks[2]));

    /// <summary>
    /// Checked on the restored session: the stage record is exactly where the player stands (the deck they're on, or
    /// none), an invasion is marked only while that stage's set plays, its ticks are in order, and the player isn't
    /// standing in a drum kit, which they can never walk into.
    /// </summary>
    private string? PlayerStageIssue(ulong me)
    {
        var nav = _navigationAgents[new(me)];
        foreach (var (x, z) in DrumKits())
        {
            long dx = nav.XMillimetres - x, dz = nav.ZMillimetres - z;
            if (dx * dx + dz * dz < (long)StageInvasionRules.DrumKitClearanceMillimetres * StageInvasionRules.DrumKitClearanceMillimetres)
                return "The player steward is standing in a drum kit.";
        }
        foreach (var (x, z) in BandMarks())
        {
            long dx = nav.XMillimetres - x, dz = nav.ZMillimetres - z;
            if (dx * dx + dz * dz < (long)StageInvasionRules.MarkClearanceMillimetres * StageInvasionRules.MarkClearanceMillimetres)
                return "The player steward is standing on a band member's mark.";
        }
        var stage = DeckStageAt(TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres));
        var on = _stewardShift!.OnStage;
        if (stage < 0) return on is null ? null : "The player steward's stage record is for a stage they aren't on.";
        if (on is null || on.StageId != Stages[stage].Id) return "The player steward is on a stage the shift doesn't know about.";
        if (on.SinceTick < (_preparation?.StartedTick ?? 0) || on.SinceTick > CurrentTick ||
            on.InvadedTick is { } invaded && (invaded < on.SinceTick || invaded > CurrentTick))
            return "The player steward's stage record is out of order.";
        var live = stage < _livePerformances.Length ? _livePerformances[stage] : null;
        if ((on.InvadedTick is not null) != SetPlaying(live) || on.InvadedTick is { } since && (live!.StageInvasions < 1 || since < live.StartedTick))
            return "The player steward's stage invasion doesn't match the set.";
        // Every tick of an invasion counts, from the one it began on; a day frozen by a death stops a tick short.
        if (on.InvadedTick is { } from && (_stewardShift.Score?.StageTicks ?? 0) < CurrentTick - from + (IsLifecycleEditionFrozen() ? 0 : 1))
            return "The player steward's time on stage is short.";
        return null;
    }
}

public sealed partial class GameSession
{
    /// <summary>
    /// Checked on every save, classic ones too: a "stage invader" hiccup or an invaded set only where there was a
    /// player steward to do it. The shift's list of invaded sets is exactly the day's records that carry the hiccup
    /// and the live sets that count an invasion, each live set's count matches its entry, and the entries' counts add
    /// up to the score's. An earlier festival's carried sets may carry the hiccup only in a hands-on campaign, where
    /// each day had the player on it; they are scored again from their facts like every carried set.
    /// </summary>
    private static string? ValidatePersistedStageInvasions(SessionPersistenceSnapshot s)
    {
        const string invalid = "Stage invasions invalid.";
        if (s.Preparation?.RelationshipHistory?.Any(gig => gig?.Hiccups?.Contains(GigRules.StageInvader) == true) == true && s.Mode != CampaignMode.HandsOn)
            return invalid;
        var keys = new HashSet<(string, int)>();
        foreach (var record in s.PerformanceRecords ?? [])
            if (record?.Hiccups?.Contains(GigRules.StageInvader) == true) keys.Add((record.StageId, record.Slot));
        var liveCounts = new Dictionary<(string, int), int>();
        var lives = s.LivePerformances ?? [];
        for (var i = 0; i < lives.Length; i++)
            if (lives[i] is { StageInvasions: > 0 } live)
            {
                if (s.Programme?.Stages is not { } stages || i >= stages.Length || stages[i].CurrentSlot < 0) return invalid;
                liveCounts[(live.StageId, stages[i].CurrentSlot)] = live.StageInvasions;
                keys.Add((live.StageId, stages[i].CurrentSlot));
            }
        if (s.StewardShift is not { } shift) return keys.Count == 0 ? null : invalid;
        var sets = shift.InvadedSets;
        if (sets is { Length: 0 } || (sets ?? []).Any(item => item is null || item.StageId is null || item.Count < 1 || item.Slot < 0)) return invalid;
        var listed = (sets ?? []).Select(item => (item.StageId, item.Slot)).ToArray();
        if (listed.Distinct().Count() != listed.Length || !keys.SetEquals(listed)) return invalid;
        if (liveCounts.Any(item => sets!.Single(set => (set.StageId, set.Slot) == item.Key).Count != item.Value)) return invalid;
        // A standalone shift's retry has its day again without the player: its sets went with it, its score stays.
        var stale = s.Mode != CampaignMode.HandsOn && s.Preparation is { Attempt: > 1 };
        if (!stale && (sets ?? []).Sum(item => (long)item.Count) != (shift.Score?.StageInvasions ?? 0)) return invalid;
        return null;
    }
}

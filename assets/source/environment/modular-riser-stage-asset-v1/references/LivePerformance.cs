using System.Text.Json;
using System.Text.Json.Serialization;

namespace Festival.Simulation;

public enum LiveSetStage { BeforeSet, Live, Interrupted, Finished }
public sealed record LivePerformer(ulong AgentId, GridCell StageCell, GridCell AccessCell, GridCell StairCell,
    bool AccessReached, bool StairReached, bool OnStage, bool InstrumentAttached)
{
    // Nullable for exact old-save canonical compatibility. Once observed, all
    // three fields are authoritative: a route label alone cannot hide a stall.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? LastStageProgressTick { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ObservedStageXMillimetres { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ObservedStageZMillimetres { get; init; }
}
public sealed record LiveListener(ulong AgentId, GridCell? Place, int Enthusiasm, int ListenedTicks, int EnjoymentEarned, bool AtPlace,
    long LastDecisionTick = -800);
public sealed record LivePerformanceSnapshot(int Version, LiveSetStage Stage, long PlannedTick, long StartedTick,
    long EndedTick, long InterruptedTick, int ReactionSequence, string LastReaction, LivePerformer[] Performers,
    LiveListener[] Listeners)
{
    public ulong[] SetEndAudienceIds { get; init; } = [];
    [JsonIgnore] public int SetEndAudienceCount => SetEndAudienceIds.Length;
    [JsonIgnore] public int SetEndEnjoymentTotal => Listeners.Where(listener => SetEndAudienceIds.Contains(listener.AgentId)).Sum(listener => listener.EnjoymentEarned);
}

public sealed partial class GameSession
{
    public const int LiveSetDurationTicks = 9_600;
    public const int LiveSetArrivalDelayTicks = 2_400;
    public const int LiveSetStageEntryLeadTicks = 640;
    public const int SustainedBooDelayTicks = 320;
    private LivePerformanceSnapshot? _livePerformance;
    public LivePerformanceSnapshot? CaptureLivePerformance() => _livePerformance is null ? null : _livePerformance with
    { Performers = _livePerformance.Performers.ToArray(), Listeners = _livePerformance.Listeners.ToArray(), SetEndAudienceIds = _livePerformance.SetEndAudienceIds.ToArray() };
    internal string? LivePerformanceCanonicalJson => _livePerformance is null ? null : System.Text.Json.JsonSerializer.Serialize(_livePerformance);
    public bool LivePerformanceBoundaryOnNextTick => !IsPaused && _livePerformance is { } live &&
        (live.Stage == LiveSetStage.BeforeSet && CurrentTick + 1 >= live.PlannedTick && live.Performers.All(item => item.OnStage) ||
         live.Stage is LiveSetStage.Live or LiveSetStage.Interrupted && CurrentTick + 1 >= _programme!.SlotEndTick ||
         _programme is { CurrentSlot: < 2 } q && live.Stage == LiveSetStage.Finished && _preparation is { } p && CurrentTick + 1 == p.StartedTick + FestivalSlotStarts[q.CurrentSlot + 1] - LiveSetStageEntryLeadTicks);

    // Backstage at the foot of the trailer's north stairs, then a step on the flight, one column for each player.
    private static readonly GridCell[] StageAccessCells = [new(93, 137), new(94, 137), new(95, 137)];
    private static readonly GridCell[] StageStairCells = [new(93, 141), new(94, 141), new(95, 141)];

    private void StartLivePerformance()
    {
        var p = _preparation!;
        if (_programme is { } programme)
            _programme = programme with { CurrentSlot = Math.Max(0, programme.CurrentSlot), SlotEndTick = p.StartedTick + FestivalSlotEnds[Math.Max(0, programme.CurrentSlot)], Status = "Performers approaching stage" };
        GridCell[] positions = [new(96, 150), new(94, 146), new(93, 152)];
        GridCell[] access = StageAccessCells;
        GridCell[] stairs = StageStairCells;
        var performers = PeopleIn(PersonView.Roster).Where(item => item.Role == ProtectedPersonRole.Performer && _programme!.Performers.Any(role => role.AgentId == item.Id && role.SlotIndex == _programme.CurrentSlot)).Select((item, index) =>
            new LivePerformer(item.Id, positions[index], access[index], stairs[index], false, false, false, false)).ToArray();
        foreach (var performer in performers)
            if (!MedicalOwnsNavigation(performer.AgentId) && !InterventionOwnsTarget(performer.AgentId) && !InterventionOwnsWorker(performer.AgentId) && CurrentTick < _programme!.SlotEndTick)
                ApplyAgentDestination(new(performer.AgentId), new(performer.AccessCell, "performance.side-entry"));
        var listeners = PeopleIn(PersonView.Roster).Where(item => item.Role == ProtectedPersonRole.Guest).Select(item =>
            new LiveListener(item.Id, null, FestivalAffinity(item.Id, CurrentFestivalAct!), 0, 0, false)).ToArray();
        _livePerformance = new(2, LiveSetStage.BeforeSet, p.StartedTick + FestivalSlotStarts[_programme!.CurrentSlot], -1, -1, -1,
            0, "none", performers, listeners);
    }

    private int BookedGenre() => CurrentFestivalAct?.Genre ?? (_preparation!.AcceptedOffers.Contains("act.punk") ? 1 : 0);

    private static bool IsStageApproachRoute(LivePerformer performer, NavigationAgentState agent) =>
        !performer.AccessReached ? agent.IntentId == "performance.side-entry" && agent.Destination == performer.AccessCell :
        !performer.StairReached ? agent.IntentId == "performance.visible-stairs" && agent.Destination == performer.StairCell :
        !performer.OnStage && agent.IntentId == "performance.stage-entry" && agent.Destination == performer.StageCell;

    private void AdvanceLivePerformance()
    {
        AdvanceProgramme();
        if (_livePerformance is not { } live || _preparation is not { Status: PreparationStatus.Running } p) return;
        var hasPower = StagePowered;
        var periodic = CurrentTick % 80 == 0;
        var stageEntryDue = live.Stage != LiveSetStage.Finished &&
            (live.Stage != LiveSetStage.BeforeSet || CurrentTick >= live.PlannedTick - LiveSetStageEntryLeadTicks);
        var waypointArrival = live.Performers.Any(item =>
            _navigationAgents[new(item.AgentId)] is { Action: AgentNavigationAction.Arrived } agent &&
            (live.Stage != LiveSetStage.Finished &&
                 (!item.AccessReached && agent.Destination == item.AccessCell ||
                  item.AccessReached && !item.StairReached && agent.Destination == item.StairCell ||
                  item.StairReached && !item.OnStage && agent.Destination == item.StageCell) ||
             live.Stage == LiveSetStage.Finished &&
                 (agent.Destination == item.StairCell && agent.IntentId == "performance.stage-exit-stair" ||
                  agent.Destination == item.AccessCell && agent.IntentId == "performance.stage-exit-access")));
        if (!periodic && live.Stage == LiveSetStage.BeforeSet && !waypointArrival &&
            CurrentTick < live.PlannedTick) return;
        if (!periodic && live.Stage == LiveSetStage.Finished && !waypointArrival) return;
        var performers = live.Performers.ToArray();
        for (var i = 0; i < performers.Length; i++)
        {
            var performer = performers[i];
            // Medical response owns an awaiting/collapsed performer's position.
            // Stage-entry timing must not pull the patient away mid-treatment.
            if (MedicalOwnsNavigation(performer.AgentId))
            {
                performers[i] = performer with { OnStage = false, InstrumentAttached = false };
                continue;
            }
            var agent = _navigationAgents[new(performer.AgentId)];
            if (agent.Action == AgentNavigationAction.Travelling && IsStageApproachRoute(performer, agent) &&
                (performer.ObservedStageXMillimetres != agent.XMillimetres || performer.ObservedStageZMillimetres != agent.ZMillimetres))
                performer = performer with { LastStageProgressTick = CurrentTick,
                    ObservedStageXMillimetres = agent.XMillimetres, ObservedStageZMillimetres = agent.ZMillimetres };
            if (live.Stage != LiveSetStage.Finished && !performer.AccessReached &&
                agent.Action == AgentNavigationAction.Arrived && agent.Destination == performer.AccessCell)
            {
                performer = performer with { AccessReached = true };
            }
            if (stageEntryDue && performer.AccessReached && !performer.StairReached &&
                agent.Action == AgentNavigationAction.Arrived && agent.Destination == performer.AccessCell)
            {
                ApplyAgentDestination(new(performer.AgentId), new(performer.StairCell, "performance.visible-stairs"));
                agent = _navigationAgents[new(performer.AgentId)];
            }
            if (live.Stage != LiveSetStage.Finished && performer.AccessReached && !performer.StairReached &&
                agent.Action == AgentNavigationAction.Arrived && agent.Destination == performer.StairCell)
            {
                ApplyAgentDestination(new(performer.AgentId), new(performer.StageCell, "performance.stage-entry"));
                performer = performer with { StairReached = true };
                agent = _navigationAgents[new(performer.AgentId)];
            }
            if (live.Stage == LiveSetStage.Finished && agent.Action == AgentNavigationAction.Arrived &&
                agent.Destination == performer.StairCell && agent.IntentId == "performance.stage-exit-stair")
            {
                ApplyAgentDestination(new(performer.AgentId), new(performer.AccessCell, "performance.stage-exit-access"));
                agent = _navigationAgents[new(performer.AgentId)];
            }
            if (live.Stage == LiveSetStage.Finished && agent.Action == AgentNavigationAction.Arrived &&
                agent.Destination == performer.AccessCell && agent.IntentId == "performance.stage-exit-access")
            {
                var index = Array.FindIndex(PeopleIn(PersonView.Roster), item => item.Id == performer.AgentId);
                ApplyAgentDestination(new(performer.AgentId), new(IdlePlace(index), "performance.stage-exit"));
                agent = _navigationAgents[new(performer.AgentId)];
            }
            performers[i] = performer with { OnStage = live.Stage != LiveSetStage.Finished && performer.StairReached &&
                agent.Action == AgentNavigationAction.Arrived && agent.Destination == performer.StageCell };
        }
        var listeners = live.Listeners.ToArray();
        var departed = PeopleIn(PersonView.Roster).Where(item => item.Departed).Select(item => item.Id).ToHashSet();
        var reserved = listeners.Where(item => item.Place is not null && !departed.Contains(item.AgentId)).Select(item => item.Place!.Value).ToHashSet();
        _listenerIndex.Clear();
        for (var i = 0; i < listeners.Length; i++) _listenerIndex.TryAdd(listeners[i].AgentId, i);
        // Places held by listeners still on site, for the one-cell spacing rule below.
        var occupied = new Dictionary<GridCell, int>();
        HashSet<GridCell>? queueGround = null;
        foreach (var item in listeners)
            if (item.Place is { } held && !departed.Contains(item.AgentId)) occupied[held] = occupied.GetValueOrDefault(held) + 1;
        // Four identity cohorts spread bounded decisions. Dwell and a material improvement
        // threshold prevent a settled crowd from continuously chasing tiny score changes.
        foreach (var index in (periodic && live.Stage != LiveSetStage.Finished ? Enumerable.Range(0, listeners.Length)
                     .Where(i => (CurrentTick / 80) % 4 == (long)(listeners[i].AgentId % 4)).OrderBy(i => listeners[i].AgentId)
                     : Enumerable.Empty<int>()))
        {
            var listener = listeners[index];
            if (AudienceNavigationOwned(listener.AgentId) ||
                CurrentTick - listener.LastDecisionTick < 800 ||
                PersonIn(PersonView.Roster, listener.AgentId) is not { Admitted: true, Departed: false }) continue;
            listeners[index] = listener = listener with { LastDecisionTick = CurrentTick };
            var start = _navigationAgents[new(listener.AgentId)];
            var startCell = TraversalGrid.WorldToCell(start.XMillimetres, start.ZMillimetres);
            if (listener.Place is not null && (start.Action != AgentNavigationAction.Arrived || start.Destination != listener.Place)) continue;
            var currentScore = listener.Place is { } current ? PlaceScore(listener, current, startCell, listeners) : int.MaxValue;
            var options = ListeningPlaces().Distinct().Where(cell => !reserved.Contains(cell) &&
                (listener.Place is null || AudienceDistanceSquared(cell, startCell) <= 16) &&
                _traversalGrid!.Get(cell).IsWalkable &&
                !NearQueue(cell, queueGround ??= AllQueueGround()))
                .Where(cell => !PlaceCrowded(cell, listener, departed, occupied))
                .Select(cell => (Cell: cell, Score: PlaceScore(listener, cell, startCell, listeners)))
                .Where(item => listener.Place is null || item.Score + 6 <= currentScore)
                .OrderBy(item => item.Score).ThenBy(item => item.Cell).Take(8);
            foreach (var option in options)
            {
                var route = DeterministicPathfinder.FindPath(_traversalGrid!, startCell, option.Cell);
                if (!route.Found) continue;
                if (listener.Place is { } old) reserved.Remove(old);
                reserved.Add(option.Cell);
                if (!departed.Contains(listener.AgentId))
                {
                    if (listener.Place is { } previous && --occupied[previous] == 0) occupied.Remove(previous);
                    occupied[option.Cell] = occupied.GetValueOrDefault(option.Cell) + 1;
                }
                listeners[index] = listener with { Place = option.Cell, AtPlace = false };
                var local = startCell.X is >= 103 and <= 126 && startCell.Z is >= 131 and <= 169;
                var densityRetreat = local && listener.Place is not null && option.Cell.X > startCell.X &&
                    AudienceDensity(listener, startCell, listeners) > AudienceComfortTolerance(listener);
                ApplyAgentDestination(new(listener.AgentId), new(option.Cell,
                    densityRetreat ? "performance.listen-local-retreat" : local ? "performance.listen-local" : "performance.listen"));
                break;
            }
        }
        Person[]? rewardedPeople = null;
        // How the set is playing, once for every listener this tick.
        var overall = _equipment?.Version == 3 ? CurrentPerformance?.Overall ?? 50 : 50;
        for (var i = 0; i < listeners.Length; i++)
        {
            var listener = listeners[i];
            var atPlace = !departed.Contains(listener.AgentId) && !AudienceNavigationOwned(listener.AgentId) && listener.Place is { } place &&
                _navigationAgents[new(listener.AgentId)].Action == AgentNavigationAction.Arrived &&
                _navigationAgents[new(listener.AgentId)].Destination == place;
            // The prior tick's state earns one tick. A set starting, a new arrival,
            // or restored power cannot award an entire second at this boundary.
            if (live.Stage == LiveSetStage.Live && hasPower && (performers.All(person => person.OnStage)) && listener.AtPlace && atPlace &&
                PersonIn(PersonView.Roster, listener.AgentId)?.Departed != true &&
                CurrentTick > live.StartedTick && CurrentTick <= _programme!.SlotEndTick)
            {
                var listenedTicks = listener.ListenedTicks + 1;
                var earned = listener.EnjoymentEarned;
                if (listenedTicks % 80 == 0)
                {
                    var basis = listener.Enthusiasm >= 90 ? 15 : listener.Enthusiasm >= 60 ? 10 : 5;
                    int gain;
                    // On the power budget the band's play and the sound scale the set; the older scenario keeps its bonuses.
                    if (_equipment?.Version == 3) gain = basis * PerformanceRules.MusicPermille(overall) / 1000;
                    else
                    {
                        var quality = (_equipment?.LoadPercent ?? 80) == 80 ? 75 : 100;
                        var rigBonus = p.OwnedEquipment.Length > 0 ? 5 : 0;
                        gain = (basis + rigBonus + SoundMixingBonus()) * quality / 100;
                    }
                    if (CurrentFestivalAct is { } playing) gain = gain * MusicExpectationPermille(playing.Popularity, ExpectedPopularity) / 1000;
                    rewardedPeople ??= PeopleIn(PersonView.Roster).ToArray();
                    var personIndex = Array.FindIndex(rewardedPeople, item => item.Id == listener.AgentId);
                    var person = rewardedPeople[personIndex];
                    RecordMood(person.Id, Math.Min(10_000, person.Satisfaction + gain) - person.Satisfaction, MoodCause.Music);
                    rewardedPeople[personIndex] = person with { Satisfaction = Math.Min(10_000, person.Satisfaction + gain),
                        MusicRisk = Math.Min(3_000, person.MusicRisk + (listener.Enthusiasm >= 60 ? 0 : 5)) };
                    earned += gain;
                }
                listener = listener with { ListenedTicks = listenedTicks, EnjoymentEarned = earned };
            }
            listeners[i] = listener.AtPlace == atPlace ? listener : listener with { AtPlace = atPlace };
        }
        if (rewardedPeople is not null) foreach (var person in rewardedPeople) SetPresence(person);
        var stage = live.Stage;
        var started = live.StartedTick;
        var ended = live.EndedTick;
        var interrupted = live.InterruptedTick;
        var reaction = live.LastReaction;
        var sequence = live.ReactionSequence;
        var setEndAudienceIds = live.SetEndAudienceIds;
        if (stage == LiveSetStage.BeforeSet && CurrentTick >= live.PlannedTick && CurrentTick < _programme!.SlotEndTick && performers.All(item => item.OnStage))
        {
            stage = LiveSetStage.Live;
            started = CurrentTick;
            reaction = listeners.Count(item => item.AtPlace && item.Enthusiasm >= 65) >= 5 ? "set-start-cheer" : "set-start-muted";
            sequence++;
        }
        if (stage is LiveSetStage.Live or LiveSetStage.Interrupted)
        {
            var powered = StagePowered && (performers.All(person => person.OnStage));
            if (!powered && stage == LiveSetStage.Live)
            {
                stage = LiveSetStage.Interrupted;
                interrupted = CurrentTick;
                reaction = _equipment?.Stage is EquipmentStage.Isolated or EquipmentStage.Terminal ? "silence" : "performer-unavailable";
                sequence++;
            }
            else if (powered && stage == LiveSetStage.Interrupted)
            {
                stage = LiveSetStage.Live;
                interrupted = -1;
                reaction = "resumed";
                sequence++;
            }
            if (stage == LiveSetStage.Interrupted && !StagePowered && CurrentTick - interrupted == SustainedBooDelayTicks)
            {
                var disappointed = listeners.Where(item => item.AtPlace).Select(item => item.AgentId).ToHashSet();
                var people = PeopleIn(PersonView.Roster).Select(item => disappointed.Contains(item.Id) ? item with
                { Satisfaction = Math.Max(0, item.Satisfaction - 100), MusicRisk = Math.Min(3_000, item.MusicRisk + 200) } : item).ToArray();
                foreach (var item in PeopleIn(PersonView.Roster).Where(item => disappointed.Contains(item.Id)))
                    RecordMood(item.Id, Math.Max(0, item.Satisfaction - 100) - item.Satisfaction, MoodCause.MusicCutOff);
                foreach (var person in people) SetPresence(person);
                reaction = disappointed.Count >= 5 ? "sustained-boo" : "sustained-muted";
                sequence++;
            }
            if (CurrentTick >= _programme!.SlotEndTick)
            {
                var applauseEligible = live.Stage == LiveSetStage.Live && stage == LiveSetStage.Live && powered && started >= 0;
                if (applauseEligible)
                    setEndAudienceIds = listeners.Where(listener => listener.AtPlace && listener.ListenedTicks > 0 &&
                        !AudienceNavigationOwned(listener.AgentId)).Select(listener => listener.AgentId).ToArray();
                stage = LiveSetStage.Finished;
                ended = CurrentTick;
                reaction = setEndAudienceIds.Length > 0 ? "set-finished-applause" : applauseEligible ? "set-finished-muted" : "set-finished-interrupted";
                sequence++;
            }
        }
        if (_programme is { } programme && CurrentTick >= programme.SlotEndTick && stage == LiveSetStage.BeforeSet)
        {
            stage = LiveSetStage.Finished;
            ended = programme.SlotEndTick;
            reaction = "slot-missed-not-ready";
            sequence++;
        }
        performers = performers.Select(item => stage == LiveSetStage.Finished
            ? item with { OnStage = false, InstrumentAttached = false }
            : item with { InstrumentAttached = item.OnStage }).ToArray();
        _livePerformance = live with { Stage = stage, StartedTick = started, EndedTick = ended,
            InterruptedTick = interrupted, ReactionSequence = sequence, LastReaction = reaction,
            Performers = performers, Listeners = listeners, SetEndAudienceIds = setEndAudienceIds };
        // An act that played is known from then on: its talent shows in the booking table.
        if (stage == LiveSetStage.Finished && live.Stage != LiveSetStage.Finished && started >= 0 && CurrentFestivalAct is { } played &&
            _preparation is { } seen && !seen.SeenActs.Contains(played.Id))
            _preparation = seen with { SeenActs = seen.SeenActs.Append(played.Id).Order(StringComparer.Ordinal).ToArray() };
        // A high-ego act takes a poor sound system personally.
        if (stage == LiveSetStage.Live && CurrentTick % 80 == 0 && CurrentFestivalAct is { Ego: >= 70 } proud && SoundScore < 40)
            foreach (var performer in performers.Where(item => item.OnStage))
                MutatePerson(performer.AgentId, person => person.Satisfaction = Math.Max(0, person.Satisfaction - 2));
        if (stage == LiveSetStage.Finished && live.Stage != LiveSetStage.Finished)
            foreach (var performer in performers)
            {
                if (MedicalOwnsNavigation(performer.AgentId)) continue;
                if (!performer.StairReached && !ProgrammeStageAccessOccupied(_navigationAgents[new(performer.AgentId)])) continue;
                ApplyAgentDestination(new(performer.AgentId), new(performer.StairCell, "performance.stage-exit-stair"));
            }
    }

    private void FinishLivePerformance()
    {
        if (_livePerformance is { } live && live.Stage != LiveSetStage.Finished)
            _livePerformance = live with { Stage = LiveSetStage.Finished, EndedTick = CurrentTick,
                Performers = live.Performers.Select(item => item with { OnStage = false, InstrumentAttached = false }).ToArray() };
    }

    private static readonly (int MinX, int MaxX, int MinZ, int MaxZ) AudienceBounds =
        (ListeningPlaces().Min(c => c.X), ListeningPlaces().Max(c => c.X), ListeningPlaces().Min(c => c.Z), ListeningPlaces().Max(c => c.Z));

    /// <summary>
    /// The audience's ground in front of the stage: every place a listener can stand, as one rectangle. Nothing is
    /// built here, and a queue only grows into it where nobody's watching from, so the crowd always has somewhere to stand.
    /// </summary>
    public static bool InAudienceArea(GridCell cell) =>
        cell.X >= AudienceBounds.MinX && cell.X <= AudienceBounds.MaxX && cell.Z >= AudienceBounds.MinZ && cell.Z <= AudienceBounds.MaxZ;

    /// <summary>The audience area's corners, in grid cells.</summary>
    public static (GridCell Min, GridCell Max) AudienceArea => (new(AudienceBounds.MinX, AudienceBounds.MinZ), new(AudienceBounds.MaxX, AudienceBounds.MaxZ));

    private static IEnumerable<GridCell> ListeningPlaces()
    {
        // The rotated trailer faces increasing X. This irregular apron sits between
        // the front edge and vehicle track, leaving the visible south-end stairs
        // and generator footprint to terrain/route exclusions.
        for (var band = 0; band < 9; band++)
        for (var lane = -7; lane <= 7; lane++)
        {
            var x = 103 + band * 2 + Math.Abs((lane * 7 + band * 11) % 3);
            var z = 150 + lane * 2 + Math.Abs((band * 5 + lane * 3) % 3) - 1;
            yield return new GridCell(x, z);
        }
    }


    private static int AudienceDistanceSquared(GridCell a, GridCell b) => (a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z);

    /// <summary>Prototype weighted-neighbour comfort, not a real-world people-per-area measurement.</summary>
    public static int AudienceComfortTolerance(LiveListener listener) => 900 + listener.Enthusiasm * 12 + ((int)(listener.AgentId % 7) - 3) * 50;
    public static int AudiencePacePermille(int enthusiasm) => 800 + Math.Clamp(enthusiasm, 0, 100) * 4;
    public int GetAudienceWalkingPacePermille(EntityId id) => _navigationAgents.TryGetValue(id, out var agent) ? AudienceWalkingPace(agent) : 1000;

    public bool ShouldAudienceBackstepFacingStage(EntityId id, double x, double z, double movementX, double movementZ)
    {
        if (!_navigationAgents.TryGetValue(id, out var agent) || agent.Destination is not { } cell ||
            agent.IntentId is not { } intent || AudienceNavigationOwned(id.Value) ||
            _livePerformance is null or { Stage: LiveSetStage.Finished } ||
            !_livePerformance.Listeners.Any(listener => listener.AgentId == id.Value) ||
            PersonIn(PersonView.Roster, id.Value) is not { Admitted: true, Departed: false })
            return false;
        var destination = TraversalGrid.CellCentre(cell);
        return AudienceFacingMath.ShouldBackstep(intent, agent.Action, x, z,
            destination.XMillimetres, destination.ZMillimetres, movementX, movementZ);
    }

    private int AudienceWalkingPace(NavigationAgentState agent) => agent.IntentId is "performance.listen-local" or "performance.listen-local-retreat" &&
        !AudienceNavigationOwned(agent.Id.Value) && _livePerformance?.Listeners.FirstOrDefault(item => item.AgentId == agent.Id.Value) is { } listener
            ? AudiencePacePermille(listener.Enthusiasm) : 1000;

    private int PlaceScore(LiveListener listener, GridCell cell, GridCell start, LiveListener[] listeners)
    {
        var travel = Math.Abs(cell.X - start.X) + Math.Abs(cell.Z - start.Z);
        var sightline = Math.Abs(cell.Z - 150);
        var stableOffset = (int)((listener.AgentId * 17 + (ulong)(cell.X * 13 + cell.Z * 7)) % 5);
        return (cell.X - 103) * 6 + sightline + Math.Max(0, sightline - 10) * 2 + travel + stableOffset +
            Math.Max(0, AudienceDensity(listener, cell, listeners) - AudienceComfortTolerance(listener)) / 20;
    }

    // Listener position by agent id for the pass in progress; entries index the live listeners array.
    private readonly Dictionary<ulong, int> _listenerIndex = [];

    /// <summary>
    /// Whether another on-site listener holds a place within one orthogonal step of <paramref name="cell"/>
    /// (squared distance below 2), ignoring the deciding listener's own place.
    /// </summary>
    private static bool PlaceCrowded(GridCell cell, LiveListener listener, HashSet<ulong> departed, Dictionary<GridCell, int> occupied)
    {
        foreach (var near in new[] { cell, new GridCell(cell.X + 1, cell.Z), new GridCell(cell.X - 1, cell.Z), new GridCell(cell.X, cell.Z + 1), new GridCell(cell.X, cell.Z - 1) })
        {
            var count = occupied.GetValueOrDefault(near);
            if (listener.Place == near && !departed.Contains(listener.AgentId)) count--;
            if (count > 0) return true;
        }
        return false;
    }

    private int AudienceDensity(LiveListener listener, GridCell cell, LiveListener[] listeners)
    {
        var density = 0;
        foreach (var person in PeopleIn(PersonView.Roster).Where(item => item.Admitted && !item.Departed && item.Id != listener.AgentId && MovementOccupant(item.Id)))
        {
            var agent = _navigationAgents[new(person.Id)];
            var actual = TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres);
            var weight = Math.Max(0, 25 - AudienceDistanceSquared(actual, cell)) * 40;
            var other = _listenerIndex.TryGetValue(person.Id, out var at) ? listeners[at] : null;
            // An absent water/rest/escort owner retains a unique return place, but does not
            // invent a second physical body at that place in the comfort calculation.
            if (other?.Place is { } place && !AudienceNavigationOwned(person.Id))
                weight = Math.Max(weight, Math.Max(0, 25 - AudienceDistanceSquared(place, cell)) * 40);
            density += weight;
        }
        return density;
    }

    private static string? ValidatePersistedLivePerformance(LivePerformanceSnapshot? live, SessionPersistenceSnapshot snapshot)
    {
        if (live is null) return null;
        if (snapshot.Preparation is not { } preparation || live.Version != 2 || !Enum.IsDefined(live.Stage) ||
            live.Performers is null || live.Listeners is null || live.Performers.Length != 3 ||
            live.SetEndAudienceIds is null || live.SetEndAudienceIds.Distinct().Count() != live.SetEndAudienceIds.Length ||
            live.SetEndAudienceIds.Any(id => !live.Listeners.Any(listener => listener.AgentId == id && listener.ListenedTicks > 0)) ||
            live.SetEndAudienceIds.Length > 0 && (snapshot.Programme is null || live.Stage != LiveSetStage.Finished || live.StartedTick < 0 ||
                live.EndedTick != snapshot.Programme.SlotEndTick || live.InterruptedTick != -1 || live.LastReaction != "set-finished-applause") ||
            live.LastReaction == "set-finished-applause" && live.SetEndAudienceIds.Length == 0 ||
            live.Listeners.Length != FestivalTickets.Sold(preparation.Tier) || live.PlannedTick < preparation.StartedTick ||
            live.StartedTick > snapshot.CurrentTick || live.EndedTick > snapshot.CurrentTick ||
            live.Performers.Select(item => item.AgentId).Distinct().Count() != 3 ||
            live.Listeners.Select(item => item.AgentId).Distinct().Count() != live.Listeners.Length ||
            live.Listeners.Any(item => item.ListenedTicks < 0 || item.ListenedTicks > (snapshot.Programme is null ? LiveSetDurationTicks : FestivalSlotDurationTicks) ||
                item.EnjoymentEarned < 0 || item.EnjoymentEarned > item.ListenedTicks / 80 * 30 ||
                item.Enthusiasm is < 0 or > 100 || item.LastDecisionTick < -800 || item.LastDecisionTick > snapshot.CurrentTick ||
                item.Place is { } place && (place.X is < 103 or > 122 || place.Z is < 135 or > 165)))
            return "Live performance state invalid.";
        if (snapshot.Programme is { } programme &&
            (live.PlannedTick != preparation.StartedTick + FestivalSlotStarts[programme.CurrentSlot] ||
             live.StartedTick != -1 && (live.StartedTick < live.PlannedTick || live.StartedTick >= programme.SlotEndTick) ||
             live.Stage is LiveSetStage.Live or LiveSetStage.Interrupted && (live.StartedTick < 0 || snapshot.CurrentTick >= programme.SlotEndTick) ||
             live.Stage == LiveSetStage.Finished && preparation.Status != PreparationStatus.Failed && live.EndedTick != programme.SlotEndTick))
            return "Live programme progress exceeds its fixed slot window.";
        GridCell[] stageCells = [new(96, 150), new(94, 146), new(93, 152)];
        var accessCells = StageAccessCells;
        var stairCells = StageStairCells;
        var roster = preparation.People.Where(item => item.Role == ProtectedPersonRole.Performer && (snapshot.Programme is null || snapshot.Programme.Performers.Any(role => role.AgentId == item.AgentId && role.SlotIndex == snapshot.Programme.CurrentSlot))).ToArray();
        for (var i = 0; i < 3; i++)
        {
            var performer = live.Performers[i];
            var nav = snapshot.NavigationAgents?.SingleOrDefault(item => item.Id == performer.AgentId);
            if (performer.AgentId != roster[i].AgentId || performer.StageCell != stageCells[i] ||
                performer.AccessCell != accessCells[i] || performer.StairCell != stairCells[i] || nav is null ||
                performer.StairReached && !performer.AccessReached || performer.OnStage && !performer.StairReached ||
                (performer.LastStageProgressTick is null) != (performer.ObservedStageXMillimetres is null) ||
                (performer.LastStageProgressTick is null) != (performer.ObservedStageZMillimetres is null) ||
                performer.LastStageProgressTick is { } progressTick && (progressTick < 0 || progressTick > snapshot.CurrentTick) ||
                snapshot.CurrentTick < live.PlannedTick - LiveSetStageEntryLeadTicks && performer.StairReached ||
                performer.OnStage && (live.Stage == LiveSetStage.Finished || nav.Action != (int)AgentNavigationAction.Arrived ||
                    nav.DestinationX != performer.StageCell.X || nav.DestinationZ != performer.StageCell.Z) ||
                performer.InstrumentAttached != (performer.OnStage && live.Stage != LiveSetStage.Finished))
                return "Live performer route or attachment invalid.";
        }
        return null;
    }
}

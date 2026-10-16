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
    long LastDecisionTick = -800)
{
    /// <summary>When the guest came over from another stage's crowd; null for anyone who started the set here.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? JoinedTick { get; init; }
}
/// <summary>One stage's set now: its band on the way up or playing, and the crowd watching it.</summary>
public sealed record LivePerformanceSnapshot(int Version, string StageId, LiveSetStage Stage, long PlannedTick, long StartedTick,
    long EndedTick, long InterruptedTick, int ReactionSequence, string LastReaction, LivePerformer[] Performers,
    LiveListener[] Listeners)
{
    public ulong[] SetEndAudienceIds { get; init; } = [];
    /// <summary>What the set-end audience who have since gone to another stage had enjoyed of the set.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int SetEndEnjoymentAway { get; init; }
    /// <summary>The most listeners at their places at once while the set played.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int PeakListeners { get; init; }
    /// <summary>
    /// Listeners at their places, summed over every tick the set was on (the ticks the peak is counted on, from the one it
    /// started to the one it ended): the set's average crowd is this over those ticks (<see cref="AverageCrowdTenths"/>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long ListenerTicks { get; init; }
    /// <summary>How many times the set stopped for lost power, and for a band member leaving their mark.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int PowerCuts { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Stoppages { get; init; }
    /// <summary>How many times the crowd booed a silent stage.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Boos { get; init; }
    /// <summary>The sound reaching the crowd, summed once a second while the set played, and how many seconds that was.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long SoundTotal { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int SoundSamples { get; init; }
    /// <summary>A band member went down for medical help while the set was due or playing.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool MemberCollapsed { get; init; }
    /// <summary>How many times the player steward got up on this stage while the set played: a stage invader.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int StageInvasions { get; init; }
    [JsonIgnore] public int SetEndAudienceCount => SetEndAudienceIds.Length;
    /// <summary>The ticks the set was on up to <paramref name="now"/> (or its end): from the tick it started to the tick it ended, both counted.</summary>
    public long OnTicks(long now) => StartedTick < 0 ? 0 : Math.Max(0, (Stage == LiveSetStage.Finished ? EndedTick : now) - StartedTick + 1);
    /// <summary>The time-weighted average crowd while the set was on, in tenths of a listener, rounded down; 0 if it never started.</summary>
    public int AverageCrowdTenths(long now) => OnTicks(now) is var ticks and > 0 ? (int)(ListenerTicks * 10 / ticks) : 0;
    [JsonIgnore] public int SetEndEnjoymentTotal => SetEndEnjoymentAway +
        Listeners.Where(listener => SetEndAudienceIds.Contains(listener.AgentId)).Sum(listener => listener.EnjoymentEarned);
}

public sealed partial class GameSession
{
    public const int LiveSetDurationTicks = 9_600;
    public const int LiveSetArrivalDelayTicks = 2_400;
    public const int LiveSetStageEntryLeadTicks = 640;
    public const int SustainedBooDelayTicks = 320;
    /// <summary>The most a listener can enjoy of a set in one second (80 ticks) of it.</summary>
    public const int MaxEnjoymentPerSecond = 30;
    // Version 3 names its stage.
    public const int LivePerformanceVersion = 3;
    // One live set per stage, in stage order; empty until the festival opens. Sized when the programme is set up.
    private LivePerformanceSnapshot?[] _livePerformances = new LivePerformanceSnapshot?[1];
    /// <summary>The main stage's set, which the stage panel, audio and band presentation follow.</summary>
    public LivePerformanceSnapshot? CaptureLivePerformance() => CaptureLivePerformance(PrimaryStageId);
    public LivePerformanceSnapshot? CaptureLivePerformance(string stageId) =>
        FestivalStages.IndexOf(Stages, stageId) is var stage and >= 0 && stage < _livePerformances.Length && _livePerformances[stage] is { } live ? CopyOf(live) : null;
    /// <summary>Every stage's set, in stage order.</summary>
    public IReadOnlyList<LivePerformanceSnapshot> CaptureLivePerformances() => _livePerformances.OfType<LivePerformanceSnapshot>().Select(CopyOf).ToArray();
    private static LivePerformanceSnapshot CopyOf(LivePerformanceSnapshot live) => live with
    { Performers = live.Performers.ToArray(), Listeners = live.Listeners.ToArray(), SetEndAudienceIds = live.SetEndAudienceIds.ToArray() };
    // One entry per stage, null for a stage with no set yet; nothing at all before any stage has one.
    private LivePerformanceSnapshot?[]? CapturePersistedLivePerformances() =>
        _livePerformances.Any(live => live is not null) ? _livePerformances.Select(live => live is null ? null : CopyOf(live)).ToArray() : null;
    internal IEnumerable<(string StageId, string Json)> LivePerformanceCanonicalJson()
    {
        foreach (var live in _livePerformances)
            if (live is not null) yield return (live.StageId, JsonSerializer.Serialize(live));
    }
    public bool LivePerformanceBoundaryOnNextTick
    {
        get
        {
            if (IsPaused) return false;
            for (var stage = 0; stage < _livePerformances.Length; stage++)
                if (LiveBoundaryOnNextTick(stage)) return true;
            return false;
        }
    }
    private bool LiveBoundaryOnNextTick(int stage) => _livePerformances[stage] is { } live &&
        (live.Stage == LiveSetStage.BeforeSet && CurrentTick + 1 >= live.PlannedTick && live.Performers.All(item => item.OnStage) ||
         live.Stage is LiveSetStage.Live or LiveSetStage.Interrupted && CurrentTick + 1 >= StageProgramme(stage)!.SlotEndTick ||
         StageProgramme(stage) is { } q && q.CurrentSlot < Stages[stage].SlotCount - 1 && live.Stage == LiveSetStage.Finished && _preparation is { } p &&
             CurrentTick + 1 == p.StartedTick + Stages[stage].SlotStarts[q.CurrentSlot + 1] - LiveSetStageEntryLeadTicks);

    /// <summary>Every stage's first set, as the festival opens.</summary>
    private void StartLivePerformance()
    {
        for (var stage = 0; stage < _livePerformances.Length; stage++) StartStagePerformance(stage);
    }

    private void StartStagePerformance(int stage)
    {
        var p = _preparation!;
        var def = Stages[stage];
        if (StageProgramme(stage) is { } programme)
            SetStageProgramme(stage, programme with { CurrentSlot = Math.Max(0, programme.CurrentSlot), SlotEndTick = p.StartedTick + def.SlotEnds[Math.Max(0, programme.CurrentSlot)], Status = "Performers approaching stage" });
        var q = StageProgramme(stage)!;
        var performers = PeopleIn(PersonView.Roster).Where(item => item.Role == ProtectedPersonRole.Performer && q.Performers.Any(role => role.AgentId == item.Id && role.SlotIndex == q.CurrentSlot)).Select((item, index) =>
            new LivePerformer(item.Id, def.PerformerMarks[index], def.AccessCells[index], def.StairCells[index], false, false, false, false)).ToArray();
        foreach (var performer in performers)
            if (!IsClassicDeceased(performer.AgentId) && !MedicalOwnsNavigation(performer.AgentId) && !InterventionOwnsTarget(performer.AgentId) && !InterventionOwnsWorker(performer.AgentId) && CurrentTick < q.SlotEndTick &&
                !WaitingOnTheLane(performer.AgentId))
                ApplyAgentDestination(new(performer.AgentId), new(performer.AccessCell, "performance.side-entry"));
        // The day's first sets: every guest starts in the trailer stage's crowd. Later sets keep the crowd each stage has.
        var crowd = Stages.Count == 1 || _livePerformances[stage] is not { } previous
            ? stage == 0 ? PeopleIn(PersonView.Roster).Where(item => item.Role == ProtectedPersonRole.Guest).Select(item => item.Id).ToArray() : []
            : previous.Listeners.Select(item => item.AgentId).ToArray();
        var listeners = crowd.Select(id => new LiveListener(id, null, FestivalAffinity(id, StageAct(stage)!), 0, 0, false)).ToArray();
        _livePerformances[stage] = new(LivePerformanceVersion, def.Id, LiveSetStage.BeforeSet, p.StartedTick + def.SlotStarts[q.CurrentSlot], -1, -1, -1,
            0, "none", performers, listeners);
    }

    private int BookedGenre() => CurrentFestivalAct?.Genre ?? (_preparation!.AcceptedOffers.Contains("act.punk") ? 1 : 0);

    private static bool IsStageApproachRoute(LivePerformer performer, NavigationAgentState agent) =>
        !performer.AccessReached ? agent.IntentId == "performance.side-entry" && agent.Destination == performer.AccessCell :
        !performer.StairReached ? agent.IntentId == "performance.visible-stairs" && agent.Destination == performer.StairCell :
        !performer.OnStage && agent.IntentId == "performance.stage-entry" && agent.Destination == performer.StageCell;

    /// <summary>Each stage's running order and set, one stage after another in catalogue order.</summary>
    private void AdvanceLivePerformance()
    {
        AdvanceStageChoices();
        for (var stage = 0; stage < _livePerformances.Length; stage++)
        {
            AdvanceProgramme(stage);
            AdvanceStagePerformance(stage);
        }
    }

    private void AdvanceStagePerformance(int stage)
    {
        if (_livePerformances[stage] is not { } live || _preparation is not { Status: PreparationStatus.Running } p) return;
        var def = Stages[stage];
        var hasPower = StagePoweredAt(stage);
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
        var collapsed = live.MemberCollapsed;
        for (var i = 0; i < performers.Length; i++)
        {
            var performer = performers[i];
            if (IsClassicDeceased(performer.AgentId))
            {
                performers[i] = performer with { OnStage=false, InstrumentAttached=false };
                if(live.Stage!=LiveSetStage.Finished)collapsed=true;
                continue;
            }
            // Medical response owns an awaiting/collapsed performer's position.
            // Stage-entry timing must not pull the patient away mid-treatment.
            if (MedicalOwnsNavigation(performer.AgentId))
            {
                performers[i] = performer with { OnStage = false, InstrumentAttached = false };
                // Down and waiting for a medic, not just off for water or a sit-down.
                if (live.Stage != LiveSetStage.Finished && CurrentTick >= live.PlannedTick - LiveSetStageEntryLeadTicks &&
                    HasClaim(performer.AgentId, PersonClaim.Collapsed | PersonClaim.AwaitingMedic)) collapsed = true;
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
        // One listener to a cell: the places held by listeners still on site.
        var reserved = listeners.Where(item => item.Place is not null && !departed.Contains(item.AgentId)).Select(item => item.Place!.Value).ToHashSet();
        HashSet<GridCell>? queueGround = null;
        CrowdGround? ground = null;
        bool[]? standable = null;
        // Someone else resting on a listener's place, or a responder or anyone else on their way to stand on it, sends the
        // listener aside straight away: nobody shares a cell with someone standing still.
        var claimed = ClaimedAudienceCells();
        var forced = claimed.Count == 0 ? [] : Enumerable.Range(0, listeners.Length)
            .Where(i => listeners[i].Place is { } held && claimed.Contains(held) && !departed.Contains(listeners[i].AgentId)).ToHashSet();
        // Four identity cohorts spread bounded decisions. A full re-weigh waits out the dwell, a step forward into a gap a
        // shorter one, and either must beat staying put by a clear margin, so a settled crowd doesn't chase small changes.
        var deciding = (periodic && live.Stage != LiveSetStage.Finished ? Enumerable.Range(0, listeners.Length)
                .Where(i => (CurrentTick / 80) % 4 == (long)(listeners[i].AgentId % 4)) : Enumerable.Empty<int>())
            .Concat(forced).Distinct().OrderBy(i => listeners[i].AgentId).ToArray();
        foreach (var index in deciding)
        {
            var listener = listeners[index];
            var aside = forced.Contains(index);
            if (AudienceNavigationOwned(listener.AgentId) ||
                PersonIn(PersonView.Roster, listener.AgentId) is not { Admitted: true, Departed: false }) continue;
            var since = CurrentTick - listener.LastDecisionTick;
            // Someone with nowhere free to step to tries again a second later, not every tick of the claim.
            if (aside && since < CrowdStanding.AsideRetryTicks) continue;
            var full = aside || since >= CrowdStanding.DwellTicks;
            if (!full && (since < CrowdStanding.ShuffleDwellTicks || listener.Place is null)) continue;
            if (full) listeners[index] = listener = listener with { LastDecisionTick = CurrentTick };
            var start = _navigationAgents[new(listener.AgentId)];
            var startCell = TraversalGrid.WorldToCell(start.XMillimetres, start.ZMillimetres);
            if (!aside && listener.Place is not null && (start.Action != AgentNavigationAction.Arrived || start.Destination != listener.Place)) continue;
            ground ??= CrowdGroundOf(stage, listeners, departed);
            standable ??= CrowdStandable(ground, claimed, ref queueGround);
            var personalSpace = PersonalSpaceOf(listener);
            ground.LeaveOut(listener.Place);
            var currentCost = listener.Place is { } current && !aside ? CrowdSpotCost(ground, listener, personalSpace, current, startCell) : int.MaxValue;
            bool Free(GridCell cell) => standable[(cell.Z - ground.MinZ) * ground.Width + cell.X - ground.MinX] && !reserved.Contains(cell);
            var candidates = CrowdCandidates(ground, listener.Place, startCell, full).Where(Free).ToArray();
            // Stepping aside in a packed crowd: anywhere on the ground will do if there's no room close by.
            if (aside && candidates.Length == 0) candidates = CrowdCandidates(ground, null, startCell, true).Where(Free).ToArray();
            var options = candidates
                .Select(cell => (Cell: cell, Cost: CrowdSpotCost(ground, listener, personalSpace, cell, startCell)))
                .Where(item => currentCost == int.MaxValue || item.Cost + CrowdStanding.ImprovementThreshold <= currentCost)
                .OrderBy(item => item.Cost).ThenBy(item => item.Cell).Take(8).ToArray();
            var crowdingHere = listener.Place is null ? 0 : ground.CrowdingAt(startCell);
            ground.LeaveOut(null);
            var moved = false;
            // Each try below plans a route; if none can be reached, the listener carries on exactly as they were.
            var was = (start.Action, start.Destination, Route: start.Route.ToList(), start.RouteIndex, start.SegmentOriginXMillimetres,
                start.SegmentOriginZMillimetres, start.SegmentProgressMicrometres, start.MovementRemainder, start.LastSearchExpandedNodes, start.IntentId);
            foreach (var option in options)
            {
                var near = def.NearGround;
                var local = startCell.X >= near.MinX && startCell.X <= near.MaxX && startCell.Z >= near.MinZ && startCell.Z <= near.MaxZ;
                var densityRetreat = local && listener.Place is not null && def.Depth(option.Cell) > def.Depth(startCell) && crowdingHere > personalSpace;
                // The route there is planned once, preferring the crowd's gaps; a spot it can't reach is passed over.
                _crowdRouteCost = CrowdRouteCost(ground);
                ApplyAgentDestination(new(listener.AgentId), new(option.Cell,
                    densityRetreat ? "performance.listen-local-retreat" : local ? "performance.listen-local" : "performance.listen"));
                _crowdRouteCost = null;
                if (_navigationAgents[new(listener.AgentId)].Action == AgentNavigationAction.NoRoute) continue;
                if (listener.Place is { } old) { reserved.Remove(old); ground.Set(old, false); }
                reserved.Add(option.Cell);
                ground.Set(option.Cell, true);
                listeners[index] = listener with { Place = option.Cell, AtPlace = false, LastDecisionTick = CurrentTick };
                moved = true;
                break;
            }
            if (!moved && options.Length > 0)
            {
                start.Action = was.Action; start.Destination = was.Destination; start.Route = was.Route; start.RouteIndex = was.RouteIndex;
                start.SegmentOriginXMillimetres = was.SegmentOriginXMillimetres; start.SegmentOriginZMillimetres = was.SegmentOriginZMillimetres;
                start.SegmentProgressMicrometres = was.SegmentProgressMicrometres; start.MovementRemainder = was.MovementRemainder;
                start.LastSearchExpandedNodes = was.LastSearchExpandedNodes; start.IntentId = was.IntentId;
            }
        }
        Person[]? rewardedPeople = null;
        // How the set is playing, once for every listener this tick.
        var overall = _equipment?.Version == 3 ? StagePerformance(stage)?.Overall ?? 50 : 50;
        var playing = StageAct(stage);
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
                CurrentTick > live.StartedTick && CurrentTick <= StageProgramme(stage)!.SlotEndTick)
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
                    if (playing is not null) gain = gain * MusicExpectationPermille(playing.Popularity, ExpectedPopularity) / 1000;
                    // Where they stand: a fan near the front gets more from it, anyone squeezed past their personal space less.
                    ground ??= CrowdGroundOf(stage, listeners, departed);
                    var crowded = gain * CrowdEnjoymentPermille(ground, listener, listener.Place!.Value) / 1000;
                    gain = crowded > gain ? Math.Min(crowded, Math.Max(gain, MaxEnjoymentPerSecond)) : crowded;
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
        // The set's own record: its biggest crowd, and the sound it was played through.
        var peak = live.PeakListeners;
        var listenerTicks = live.ListenerTicks;
        var soundTotal = live.SoundTotal;
        var soundSamples = live.SoundSamples;
        var atPlaces = 0;
        foreach (var listener in listeners) if (listener.AtPlace) atPlaces++;
        if (periodic && live.Stage == LiveSetStage.Live) { soundTotal += SoundScoreAt(stage); soundSamples++; }
        var powerCuts = live.PowerCuts;
        var stoppages = live.Stoppages;
        var boos = live.Boos;
        var stageState = live.Stage;
        var started = live.StartedTick;
        var ended = live.EndedTick;
        var interrupted = live.InterruptedTick;
        var reaction = live.LastReaction;
        var sequence = live.ReactionSequence;
        var setEndAudienceIds = live.SetEndAudienceIds;
        if (stageState == LiveSetStage.BeforeSet && CurrentTick >= live.PlannedTick && CurrentTick < StageProgramme(stage)!.SlotEndTick && performers.All(item => item.OnStage))
        {
            stageState = LiveSetStage.Live;
            started = CurrentTick;
            reaction = listeners.Count(item => item.AtPlace && item.Enthusiasm >= 65) >= 5 ? "set-start-cheer" : "set-start-muted";
            sequence++;
        }
        if (stageState is LiveSetStage.Live or LiveSetStage.Interrupted)
        {
            var powered = StagePoweredAt(stage) && (performers.All(person => person.OnStage));
            if (!powered && stageState == LiveSetStage.Live)
            {
                stageState = LiveSetStage.Interrupted;
                interrupted = CurrentTick;
                reaction = (Stages[stage].Id==FestivalStages.MainId ? _equipment?.Stage is EquipmentStage.Isolated or EquipmentStage.Terminal || _equipment?.StageCut == true : !StagePoweredAt(stage)) ? "silence" : "performer-unavailable";
                // The power's gone however it went (a cut cable reads as a performer away above); else someone left their mark.
                if (!StagePoweredAt(stage)) powerCuts++; else stoppages++;
                sequence++;
            }
            else if (powered && stageState == LiveSetStage.Interrupted)
            {
                stageState = LiveSetStage.Live;
                interrupted = -1;
                reaction = "resumed";
                sequence++;
            }
            if (stageState == LiveSetStage.Interrupted && !StagePoweredAt(stage) && CurrentTick - interrupted == SustainedBooDelayTicks)
            {
                var disappointed = listeners.Where(item => item.AtPlace).Select(item => item.AgentId).ToHashSet();
                var people = PeopleIn(PersonView.Roster).Select(item => disappointed.Contains(item.Id) ? item with
                { Satisfaction = Math.Max(0, item.Satisfaction - 100), MusicRisk = Math.Min(3_000, item.MusicRisk + 200) } : item).ToArray();
                foreach (var item in PeopleIn(PersonView.Roster).Where(item => disappointed.Contains(item.Id)))
                    RecordMood(item.Id, Math.Max(0, item.Satisfaction - 100) - item.Satisfaction, MoodCause.MusicCutOff);
                foreach (var person in people) SetPresence(person);
                reaction = disappointed.Count >= 5 ? "sustained-boo" : "sustained-muted";
                if (reaction == "sustained-boo") boos++;
                sequence++;
            }
            if (CurrentTick >= StageProgramme(stage)!.SlotEndTick)
            {
                var applauseEligible = live.Stage == LiveSetStage.Live && stageState == LiveSetStage.Live && powered && started >= 0;
                if (applauseEligible)
                    setEndAudienceIds = listeners.Where(InSetEndCrowd).Select(listener => listener.AgentId).ToArray();
                stageState = LiveSetStage.Finished;
                ended = CurrentTick;
                reaction = setEndAudienceIds.Length > 0 ? "set-finished-applause" : applauseEligible ? "set-finished-muted" : "set-finished-interrupted";
                sequence++;
            }
        }
        if (StageProgramme(stage) is { } programme && CurrentTick >= programme.SlotEndTick && stageState == LiveSetStage.BeforeSet)
        {
            stageState = LiveSetStage.Finished;
            ended = programme.SlotEndTick;
            reaction = "slot-missed-not-ready";
            sequence++;
        }
        performers = performers.Select(item => stageState == LiveSetStage.Finished
            ? item with { OnStage = false, InstrumentAttached = false }
            : item with { InstrumentAttached = item.OnStage }).ToArray();
        _livePerformances[stage] = live with { Stage = stageState, StartedTick = started, EndedTick = ended,
            InterruptedTick = interrupted, ReactionSequence = sequence, LastReaction = reaction,
            Performers = performers, Listeners = listeners, SetEndAudienceIds = setEndAudienceIds,
            // Counted on any tick the set was on, including the one it started and the one it ended; never fewer than clapped it out.
            PeakListeners = Math.Max(setEndAudienceIds.Length,
                live.Stage is LiveSetStage.Live or LiveSetStage.Interrupted || stageState is LiveSetStage.Live or LiveSetStage.Interrupted
                ? Math.Max(peak, atPlaces) : peak),
            // The average crowd is counted on the same ticks as the peak.
            ListenerTicks = live.Stage is LiveSetStage.Live or LiveSetStage.Interrupted || stageState is LiveSetStage.Live or LiveSetStage.Interrupted
                ? listenerTicks + atPlaces : listenerTicks, PowerCuts = powerCuts, Stoppages = stoppages, Boos = boos, SoundTotal = soundTotal, SoundSamples = soundSamples,
            MemberCollapsed = collapsed };
        if (stageState == LiveSetStage.Finished && live.Stage != LiveSetStage.Finished) RecordPerformance(stage, _livePerformances[stage]!);
        // An act that played is known from then on: its talent shows in the booking table.
        if (stageState == LiveSetStage.Finished && live.Stage != LiveSetStage.Finished && started >= 0 && StageAct(stage) is { } played &&
            _preparation is { } seen && !seen.SeenActs.Contains(played.Id))
            _preparation = seen with { SeenActs = seen.SeenActs.Append(played.Id).Order(StringComparer.Ordinal).ToArray() };
        // A high-ego act takes a poor sound system personally.
        if (stageState == LiveSetStage.Live && CurrentTick % 80 == 0 && StageAct(stage) is { Ego: >= 70 } && SoundScoreAt(stage) < 40)
            foreach (var performer in performers.Where(item => item.OnStage))
                MutatePerson(performer.AgentId, person => person.Satisfaction = Math.Max(0, person.Satisfaction - 2));
        if (stageState == LiveSetStage.Finished && live.Stage != LiveSetStage.Finished)
            foreach (var performer in performers)
            {
                if (IsClassicDeceased(performer.AgentId) || MedicalOwnsNavigation(performer.AgentId)) continue;
                if (!performer.StairReached && !ProgrammeStageAccessOccupied(def, _navigationAgents[new(performer.AgentId)])) continue;
                ApplyAgentDestination(new(performer.AgentId), new(performer.StairCell, "performance.stage-exit-stair"));
            }
    }

    /// <summary>How near their listening spot a listener can be at the set's end and still be clapping it out: two metres.</summary>
    public const int SetEndNearMillimetres = 2_000;

    /// <summary>
    /// Whether a listener who heard some of the set is in its crowd as it ends: on their spot, within
    /// <see cref="SetEndNearMillimetres"/> of it, or walking back to it to watch. Shuffling to a better spot or coming
    /// back from the bar as the last song ends still counts as the crowd; someone a staff member is walking away doesn't.
    /// </summary>
    private bool InSetEndCrowd(LiveListener listener)
    {
        if (listener.ListenedTicks <= 0 || AudienceNavigationOwned(listener.AgentId)) return false;
        if (listener.AtPlace) return true;
        if (listener.Place is not { } place || PersonIn(PersonView.Roster, listener.AgentId) is not { Admitted: true, Departed: false }) return false;
        var nav = _navigationAgents[new(listener.AgentId)];
        var (x, z) = TraversalGrid.CellCentre(place);
        long dx = nav.XMillimetres - x, dz = nav.ZMillimetres - z;
        return dx * dx + dz * dz <= (long)SetEndNearMillimetres * SetEndNearMillimetres ||
            nav.Destination == place && nav.IntentId?.StartsWith("performance.listen", StringComparison.Ordinal) == true;
    }

    /// <summary>Ends every stage's set at once, as a failed or abandoned day does.</summary>
    private void FinishLivePerformance()
    {
        for (var stage = 0; stage < _livePerformances.Length; stage++)
            if (_livePerformances[stage] is { } live && live.Stage != LiveSetStage.Finished)
            {
                _livePerformances[stage] = live with { Stage = LiveSetStage.Finished, EndedTick = CurrentTick,
                    Performers = live.Performers.Select(item => item with { OnStage = false, InstrumentAttached = false }).ToArray() };
                // A set that wasn't due yet never happened, so there's nothing to write down.
                if (CurrentTick >= live.PlannedTick) RecordPerformance(stage, _livePerformances[stage]!);
            }
    }

    /// <summary>The stage whose current band this person is in; -1 for none.</summary>
    private int LivePerformerStage(ulong id)
    {
        for (var stage = 0; stage < _livePerformances.Length; stage++)
            if (_livePerformances[stage]?.Performers.Any(item => item.AgentId == id) == true) return stage;
        return -1;
    }

    /// <summary>The stage with this guest in its crowd; -1 for none. A guest is in one stage's crowd at a time.</summary>
    private int LiveListenerStage(ulong id)
    {
        for (var stage = 0; stage < _livePerformances.Length; stage++)
            if (_livePerformances[stage]?.Listeners.Any(item => item.AgentId == id) == true) return stage;
        return -1;
    }

    /// <summary>A guest's listening record at <see cref="LiveListenerStage"/>.</summary>
    private LiveListener? LiveListenerOf(ulong id) =>
        LiveListenerStage(id) is var stage and >= 0 ? _livePerformances[stage]!.Listeners.First(item => item.AgentId == id) : null;

    /// <summary>
    /// The audience's ground in front of any of these stages: every place a listener can stand, as one rectangle a stage.
    /// Nothing is built here, and a queue only grows into it where nobody's watching from, so the crowd always has
    /// somewhere to stand.
    /// </summary>
    public static bool InAudienceArea(IReadOnlyList<FestivalStage> stages, GridCell cell)
    {
        foreach (var stage in stages)
            if (stage.InAudienceArea(cell)) return true;
        return false;
    }
    /// <summary>The trailer stage's audience ground, which every festival has.</summary>
    public static bool InAudienceArea(GridCell cell) => FestivalStages.Main.InAudienceArea(cell);
    /// <summary>The audience ground of every stage this festival runs.</summary>
    private bool InStagesAudience(GridCell cell) => InAudienceArea(Stages, cell);

    /// <summary>The main stage's audience area corners, in grid cells.</summary>
    public static (GridCell Min, GridCell Max) AudienceArea => AudienceAreaOf(FestivalStages.Main);
    public static (GridCell Min, GridCell Max) AudienceAreaOf(FestivalStage stage) =>
        (new(stage.AudienceBounds.MinX, stage.AudienceBounds.MinZ), new(stage.AudienceBounds.MaxX, stage.AudienceBounds.MaxZ));

    public static int AudiencePacePermille(int enthusiasm) => 800 + Math.Clamp(enthusiasm, 0, 100) * 4;
    public int GetAudienceWalkingPacePermille(EntityId id) => _navigationAgents.TryGetValue(id, out var agent) ? AudienceWalkingPace(agent) : 1000;

    public bool ShouldAudienceBackstepFacingStage(EntityId id, double x, double z, double movementX, double movementZ)
    {
        if (!_navigationAgents.TryGetValue(id, out var agent) || agent.Destination is not { } cell ||
            agent.IntentId is not { } intent || AudienceNavigationOwned(id.Value) ||
            !_livePerformances.Any(live => live is { Stage: not LiveSetStage.Finished } && live.Listeners.Any(listener => listener.AgentId == id.Value)) ||
            PersonIn(PersonView.Roster, id.Value) is not { Admitted: true, Departed: false })
            return false;
        var destination = TraversalGrid.CellCentre(cell);
        var stage = Stages[LiveListenerStage(id.Value)].Placement;
        return AudienceFacingMath.ShouldBackstep(intent, agent.Action, x, z,
            destination.XMillimetres, destination.ZMillimetres, movementX, movementZ, stage.XMillimetres, stage.ZMillimetres);
    }

    private int AudienceWalkingPace(NavigationAgentState agent) => agent.IntentId is "performance.listen-local" or "performance.listen-local-retreat" &&
        !AudienceNavigationOwned(agent.Id.Value) && LiveListenerOf(agent.Id.Value) is { } listener
            ? AudiencePacePermille(listener.Enthusiasm) : 1000;

    /// <summary>
    /// After a restore has rebuilt the queues: no listener still on site holds a place on a queue's own cell, which live
    /// play never picks (it keeps two cells clear of every queue, a stricter rule than a save need meet).
    /// </summary>
    private string? ListenerPlaceIssue()
    {
        if (_livePerformances.All(live => live is null)) return null;
        var queues = WaterPoints().SelectMany(point => CaptureWaterQueueCells(point.Id))
            // A legacy vendor without saved queue cells sizes its line from current shoppers, never checking places, so it is left out.
            .Concat(_immersion is null ? [] : Vendors.SelectMany(vendor => vendor.QueueCells ?? []))
            .Concat(EffectiveToilets(_facilities).SelectMany(toilet => toilet.QueueCells ?? [])).ToHashSet();
        foreach (var live in _livePerformances)
            foreach (var listener in live?.Listeners ?? [])
                if (listener.Place is { } place && queues.Contains(place) && PersonIn(PersonView.Roster, listener.AgentId) is { Departed: false })
                    return "Live performance listener stands on a queue.";
        return null;
    }

    /// <summary>
    /// Every stage's saved set: none before opening, or one entry a stage, in stage order, null for a stage with no set
    /// yet. Across the stages every guest is in exactly one crowd, and nobody plays in two bands at once.
    /// </summary>
    private static string? ValidatePersistedLivePerformances(SessionPersistenceSnapshot snapshot)
    {
        if (snapshot.LivePerformances is not { } lives) return null;
        const string invalid = "Live performance state invalid.";
        var stages = SavedStages(snapshot);
        if (lives.Length != stages.Count || snapshot.Preparation is not { } preparation || lives.All(live => live is null)) return invalid;
        for (var stage = 0; stage < lives.Length; stage++)
        {
            if (lives[stage] is null)
            {
                if (snapshot.Programme?.Stages[stage] is { CurrentSlot: >= 0 }) return invalid;
                continue;
            }
            if (ValidatePersistedLivePerformance(lives[stage]!, stage, stages, snapshot) is { } error) return error;
        }
        var present = lives.OfType<LivePerformanceSnapshot>().ToArray();
        var listeners = present.SelectMany(live => live.Listeners).Select(listener => listener.AgentId).ToArray();
        var performers = present.SelectMany(live => live.Performers).Select(performer => performer.AgentId).ToArray();
        var guests = preparation.People.Where(person => person.Role == ProtectedPersonRole.Guest).Select(person => person.AgentId).ToArray();
        if (listeners.Distinct().Count() != listeners.Length || performers.Distinct().Count() != performers.Length ||
            listeners.Any(id => !guests.Contains(id)) || present.Length == stages.Count && listeners.Length != guests.Length)
            return invalid;
        // Someone who clapped a set out and has since gone to another stage is in that stage's crowd.
        foreach (var live in present)
            if (live.SetEndAudienceIds.Any(id => !live.Listeners.Any(listener => listener.AgentId == id) && !listeners.Contains(id)) ||
                live.SetEndEnjoymentAway < 0 || live.SetEndEnjoymentAway > 0 && live.SetEndAudienceIds.All(id => live.Listeners.Any(listener => listener.AgentId == id)) ||
                live.SetEndEnjoymentAway > live.SetEndAudienceIds.Length * (FestivalSlotDurationTicks / 80 * 30) ||
                // The set's own record: none of it before the set starts, and its numbers within the crowd and the set.
                live.PeakListeners < live.SetEndAudienceIds.Length || live.PeakListeners > guests.Length ||
                // Never more listener-ticks than the peak on every tick the set was on.
                live.ListenerTicks < 0 || live.ListenerTicks > live.PeakListeners * live.OnTicks(snapshot.CurrentTick) ||
                live.StartedTick < 0 && live.ListenerTicks != 0 ||
                live.PowerCuts < 0 || live.Stoppages < 0 || live.Boos < 0 || live.Boos > live.PowerCuts + live.Stoppages ||
                live.SoundSamples < 0 || live.SoundSamples > FestivalSlotDurationTicks / 80 + 1 || live.SoundTotal < 0 || live.SoundTotal > 100L * live.SoundSamples ||
                live.StartedTick < 0 && (live.PeakListeners != 0 || live.PowerCuts + live.Stoppages + live.Boos + live.SoundSamples != 0) ||
                // Only the player steward invades a stage, and only once its set has started.
                live.StageInvasions < 0 || live.StageInvasions > 10_000 || live.StageInvasions > 0 && (live.StartedTick < 0 || snapshot.StewardShift is null))
                return invalid;
        return null;
    }

    private static string? ValidatePersistedLivePerformance(LivePerformanceSnapshot live, int stage, IReadOnlyList<FestivalStage> stages, SessionPersistenceSnapshot snapshot)
    {
        var def = stages[stage];
        var programme = snapshot.Programme?.Stages[stage];
        var limits = def.AudienceBounds;
        var band = def.PerformerMarks.Count;
        if (snapshot.Preparation is not { } preparation || live.Version != LivePerformanceVersion || live.StageId != def.Id || !Enum.IsDefined(live.Stage) ||
            live.Performers is null || live.Listeners is null || live.Performers.Length != band ||
            live.Performers.Any(item => item is null) || live.Listeners.Any(item => item is null) ||
            live.SetEndAudienceIds is null || live.SetEndAudienceIds.Distinct().Count() != live.SetEndAudienceIds.Length ||
            live.SetEndAudienceIds.Any(id => live.Listeners.Any(listener => listener.AgentId == id && listener.ListenedTicks == 0)) ||
            live.SetEndAudienceIds.Length > 0 && (programme is null || live.Stage != LiveSetStage.Finished || live.StartedTick < 0 ||
                live.EndedTick != programme.SlotEndTick || live.InterruptedTick != -1 || live.LastReaction != "set-finished-applause") ||
            live.LastReaction == "set-finished-applause" && live.SetEndAudienceIds.Length == 0 ||
            live.Listeners.Length > SavedGuestCount(snapshot) || live.PlannedTick < preparation.StartedTick ||
            live.Listeners.Any(item => item.JoinedTick is { } joined && (joined < preparation.StartedTick || joined > snapshot.CurrentTick)) ||
            live.StartedTick > snapshot.CurrentTick || live.EndedTick > snapshot.CurrentTick ||
            live.Performers.Select(item => item.AgentId).Distinct().Count() != band ||
            live.Listeners.Select(item => item.AgentId).Distinct().Count() != live.Listeners.Length ||
            live.Listeners.Any(item => item.ListenedTicks < 0 || item.ListenedTicks > (programme is null ? LiveSetDurationTicks : FestivalSlotDurationTicks) ||
                item.EnjoymentEarned < 0 || item.EnjoymentEarned > item.ListenedTicks / 80 * MaxEnjoymentPerSecond ||
                item.Enthusiasm is < 0 or > 100 || item.LastDecisionTick < -800 || item.LastDecisionTick > snapshot.CurrentTick ||
                item.Place is { } place && (place.X < limits.MinX || place.X > limits.MaxX || place.Z < limits.MinZ || place.Z > limits.MaxZ || def.Access(place))))
            return "Live performance state invalid.";
        // One listener still on site to a cell, and every place on ground that can be stood on.
        var departed = preparation.People.Where(person => person.Departed).Select(person => person.AgentId).ToHashSet();
        var held = live.Listeners.Where(item => item.Place is not null && !departed.Contains(item.AgentId)).Select(item => item.Place!.Value).ToArray();
        var blocked = snapshot.TraversalGrid?.Cells.Where(cell => !cell.IsWalkable).Select(cell => new GridCell(cell.X, cell.Z)).ToHashSet();
        if (held.Distinct().Count() != held.Length || blocked is not null && live.Listeners.Any(item => item.Place is { } place && blocked.Contains(place)))
            return "Live performance listener places overlap or stand on blocked ground.";
        if (programme is not null &&
            (live.PlannedTick != preparation.StartedTick + def.SlotStarts[programme.CurrentSlot] ||
             live.StartedTick != -1 && (live.StartedTick < live.PlannedTick || live.StartedTick >= programme.SlotEndTick) ||
             live.Stage is LiveSetStage.Live or LiveSetStage.Interrupted && (live.StartedTick < 0 || snapshot.CurrentTick >= programme.SlotEndTick) ||
             live.Stage == LiveSetStage.Finished && preparation.Status != PreparationStatus.Failed && live.EndedTick != programme.SlotEndTick))
            return "Live programme progress exceeds its fixed slot window.";
        var roster = preparation.People.Where(item => item.Role == ProtectedPersonRole.Performer && (programme is null || programme.Performers.Any(role => role.AgentId == item.AgentId && role.SlotIndex == programme.CurrentSlot))).ToArray();
        for (var i = 0; i < band; i++)
        {
            var performer = live.Performers[i];
            var nav = snapshot.NavigationAgents?.SingleOrDefault(item => item.Id == performer.AgentId);
            if (performer.AgentId != roster[i].AgentId || performer.StageCell != def.PerformerMarks[i] ||
                performer.AccessCell != def.AccessCells[i] || performer.StairCell != def.StairCells[i] || nav is null ||
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

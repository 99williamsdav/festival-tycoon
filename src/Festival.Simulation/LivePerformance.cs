using System.Text.Json;

namespace Festival.Simulation;

public enum LiveSetStage { BeforeSet, Live, Interrupted, Finished }
public sealed record LivePerformer(ulong AgentId, GridCell StageCell, GridCell AccessCell, GridCell StairCell,
    bool AccessReached, bool StairReached, bool OnStage, bool InstrumentAttached);
public sealed record LiveListener(ulong AgentId, GridCell? Place, int Enthusiasm, int ListenedTicks, int EnjoymentEarned, bool AtPlace,
    long LastDecisionTick = -800);
public sealed record LivePerformanceSnapshot(int Version, LiveSetStage Stage, long PlannedTick, long StartedTick,
    long EndedTick, long InterruptedTick, int ReactionSequence, string LastReaction, LivePerformer[] Performers,
    LiveListener[] Listeners);

public sealed partial class GameSession
{
    public const int LiveSetDurationTicks = 9_600;
    public const int LiveSetArrivalDelayTicks = 2_400;
    public const int LiveSetStageEntryLeadTicks = 640;
    public const int SustainedBooDelayTicks = 320;
    private LivePerformanceSnapshot? _livePerformance;
    public LivePerformanceSnapshot? CaptureLivePerformance() => _livePerformance is null ? null : _livePerformance with
    { Performers = _livePerformance.Performers.ToArray(), Listeners = _livePerformance.Listeners.ToArray() };
    internal string? LivePerformanceCanonicalJson => _livePerformance is null ? null : JsonSerializer.Serialize(_livePerformance);
    public bool LivePerformanceBoundaryOnNextTick => !IsPaused && _livePerformance is { } live &&
        (live.Stage == LiveSetStage.BeforeSet && CurrentTick + 1 >= live.PlannedTick && live.Performers.All(item => item.OnStage) ||
         live.Stage is LiveSetStage.Live or LiveSetStage.Interrupted && CurrentTick + 1 >= live.StartedTick + LiveSetDurationTicks);

    private void StartLivePerformance()
    {
        var p = _preparation!;
        GridCell[] positions = [new(96, 150), new(94, 146), new(93, 152)];
        GridCell[] access = [new(101, 156), new(101, 157), new(101, 158)];
        GridCell[] stairs = [new(99, 156), new(99, 157), new(99, 158)];
        var performers = p.People.Where(item => item.Role == ProtectedPersonRole.Performer).Select((item, index) =>
            new LivePerformer(item.AgentId, positions[index], access[index], stairs[index], false, false, false, false)).ToArray();
        foreach (var performer in performers)
            ApplyAgentDestination(new(performer.AgentId), new(performer.AccessCell, "performance.side-entry"));
        var listeners = p.People.Where(item => item.Role == ProtectedPersonRole.Guest).Select(item =>
            new LiveListener(item.AgentId, null, item.ExpectedGenre != BookedGenre() ? 35 :
                item.AgentId % 3 == 0 ? 65 : 100, 0, 0, false)).ToArray();
        _livePerformance = new(2, LiveSetStage.BeforeSet, CurrentTick + LiveSetArrivalDelayTicks, -1, -1, -1,
            0, "none", performers, listeners);
    }

    private int BookedGenre() => _preparation!.AcceptedOffers.Contains("act.punk") ? 1 : 0;

    private void AdvanceLivePerformance()
    {
        if (_livePerformance is not { } live || _preparation is not { Status: PreparationStatus.Running } p) return;
        var hasPower = _equipment?.Stage is not (EquipmentStage.Isolated or EquipmentStage.Terminal);
        var periodic = CurrentTick % 80 == 0;
        var stageEntryDue = live.Stage == LiveSetStage.BeforeSet && CurrentTick >= live.PlannedTick - LiveSetStageEntryLeadTicks;
        var waypointArrival = live.Performers.Any(item =>
            _navigationAgents[new(item.AgentId)] is { Action: AgentNavigationAction.Arrived } agent &&
            (live.Stage == LiveSetStage.BeforeSet &&
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
            var agent = _navigationAgents[new(performer.AgentId)];
            if (live.Stage == LiveSetStage.BeforeSet && !performer.AccessReached &&
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
            if (live.Stage == LiveSetStage.BeforeSet && performer.AccessReached && !performer.StairReached &&
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
                var index = Array.FindIndex(p.People, item => item.AgentId == performer.AgentId);
                ApplyAgentDestination(new(performer.AgentId), new(PreparedPlace(index), "performance.stage-exit"));
                agent = _navigationAgents[new(performer.AgentId)];
            }
            performers[i] = performer with { OnStage = live.Stage != LiveSetStage.Finished &&
                agent.Action == AgentNavigationAction.Arrived && agent.Destination == performer.StageCell };
        }
        var listeners = live.Listeners.ToArray();
        var reserved = listeners.Where(item => item.Place is not null).Select(item => item.Place!.Value).ToHashSet();
        // A reservation is made once on physical admission. Front-biased fans claim first;
        // a stable individual offset breaks ties without a shared destination or route churn.
        foreach (var index in (periodic ? Enumerable.Range(0, listeners.Length)
                     .OrderByDescending(i => listeners[i].Enthusiasm).ThenBy(i => listeners[i].AgentId)
                     : Enumerable.Empty<int>()))
        {
            var listener = listeners[index];
            if (listener.Place is not null || CurrentTick - listener.LastDecisionTick < 800 ||
                !p.People.Any(item => item.AgentId == listener.AgentId && item.Admitted)) continue;
            listeners[index] = listener = listener with { LastDecisionTick = CurrentTick };
            var start = _navigationAgents[new(listener.AgentId)];
            var startCell = TraversalGrid.WorldToCell(start.XMillimetres, start.ZMillimetres);
            var options = ListeningPlaces().Where(cell => !reserved.Contains(cell) && _traversalGrid!.Get(cell).IsWalkable)
                .Select(cell => (Cell: cell, Score: PlaceScore(listener, cell, startCell, reserved)))
                .OrderBy(item => item.Score).ThenBy(item => item.Cell).Take(8);
            foreach (var option in options)
            {
                var route = DeterministicPathfinder.FindPath(_traversalGrid!, startCell, option.Cell);
                if (!route.Found) continue;
                reserved.Add(option.Cell);
                listeners[index] = listener with { Place = option.Cell };
                ApplyAgentDestination(new(listener.AgentId), new(option.Cell, "performance.listen"));
                break;
            }
        }
        EditionPerson[]? rewardedPeople = null;
        for (var i = 0; i < listeners.Length; i++)
        {
            var listener = listeners[i];
            var atPlace = listener.Place is { } place &&
                _navigationAgents[new(listener.AgentId)].Action == AgentNavigationAction.Arrived &&
                _navigationAgents[new(listener.AgentId)].Destination == place;
            // The prior tick's state earns one tick. A set starting, a new arrival,
            // or restored power cannot award an entire second at this boundary.
            if (live.Stage == LiveSetStage.Live && hasPower && listener.AtPlace && atPlace &&
                CurrentTick > live.StartedTick && CurrentTick <= live.StartedTick + LiveSetDurationTicks)
            {
                var listenedTicks = listener.ListenedTicks + 1;
                var earned = listener.EnjoymentEarned;
                if (listenedTicks % 80 == 0)
                {
                    var quality = (_equipment?.LoadPercent ?? 80) == 80 ? 75 : 100;
                    var rigBonus = p.OwnedEquipment.Length > 0 ? 5 : 0;
                    var staffBonus = p.AcceptedOffers.Contains("staff.engineer") ? 3 : 0;
                    var gain = ((listener.Enthusiasm >= 90 ? 15 : listener.Enthusiasm >= 60 ? 10 : 5) + rigBonus + staffBonus) * quality / 100;
                    rewardedPeople ??= p.People.ToArray();
                    var personIndex = Array.FindIndex(rewardedPeople, item => item.AgentId == listener.AgentId);
                    var person = rewardedPeople[personIndex];
                    rewardedPeople[personIndex] = person with { Satisfaction = Math.Min(10_000, person.Satisfaction + gain),
                        MusicRisk = Math.Min(3_000, person.MusicRisk + (listener.Enthusiasm >= 60 ? 0 : 5)) };
                    earned += gain;
                }
                listener = listener with { ListenedTicks = listenedTicks, EnjoymentEarned = earned };
            }
            listeners[i] = listener.AtPlace == atPlace ? listener : listener with { AtPlace = atPlace };
        }
        if (rewardedPeople is not null) _preparation = p = p with { People = rewardedPeople };
        var stage = live.Stage;
        var started = live.StartedTick;
        var ended = live.EndedTick;
        var interrupted = live.InterruptedTick;
        var reaction = live.LastReaction;
        var sequence = live.ReactionSequence;
        if (stage == LiveSetStage.BeforeSet && CurrentTick >= live.PlannedTick && performers.All(item => item.OnStage))
        {
            stage = LiveSetStage.Live;
            started = CurrentTick;
            reaction = listeners.Count(item => item.AtPlace && item.Enthusiasm >= 65) >= 5 ? "set-start-cheer" : "set-start-muted";
            sequence++;
        }
        if (stage is LiveSetStage.Live or LiveSetStage.Interrupted)
        {
            var powered = _equipment?.Stage is not (EquipmentStage.Isolated or EquipmentStage.Terminal);
            if (!powered && stage == LiveSetStage.Live)
            {
                stage = LiveSetStage.Interrupted;
                interrupted = CurrentTick;
                reaction = "silence";
                sequence++;
            }
            else if (powered && stage == LiveSetStage.Interrupted)
            {
                stage = LiveSetStage.Live;
                interrupted = -1;
                reaction = "resumed";
                sequence++;
            }
            if (stage == LiveSetStage.Interrupted && CurrentTick - interrupted == SustainedBooDelayTicks)
            {
                var disappointed = listeners.Where(item => item.AtPlace).Select(item => item.AgentId).ToHashSet();
                var people = p.People.Select(item => disappointed.Contains(item.AgentId) ? item with
                { Satisfaction = Math.Max(0, item.Satisfaction - 100), MusicRisk = Math.Min(3_000, item.MusicRisk + 200) } : item).ToArray();
                _preparation = p = p with { People = people };
                reaction = disappointed.Count >= 5 ? "sustained-boo" : "sustained-muted";
                sequence++;
            }
            if (CurrentTick >= started + LiveSetDurationTicks)
            {
                stage = LiveSetStage.Finished;
                ended = CurrentTick;
                reaction = listeners.Count(item => item.AtPlace && item.Enthusiasm >= 65) >= 5 ? "set-finished-cheer" : "set-finished-muted";
                sequence++;
            }
        }
        performers = performers.Select((item, index) => stage == LiveSetStage.Finished
            ? item with { OnStage = false, InstrumentAttached = false }
            : item with { InstrumentAttached = index < 2 && item.OnStage }).ToArray();
        _livePerformance = live with { Stage = stage, StartedTick = started, EndedTick = ended,
            InterruptedTick = interrupted, ReactionSequence = sequence, LastReaction = reaction,
            Performers = performers, Listeners = listeners };
        if (stage == LiveSetStage.Finished && live.Stage != LiveSetStage.Finished)
            foreach (var performer in performers)
            {
                ApplyAgentDestination(new(performer.AgentId), new(performer.StairCell, "performance.stage-exit-stair"));
            }
    }

    private void FinishLivePerformance()
    {
        if (_livePerformance is { } live && live.Stage != LiveSetStage.Finished)
            _livePerformance = live with { Stage = LiveSetStage.Finished, EndedTick = CurrentTick,
                Performers = live.Performers.Select(item => item with { OnStage = false, InstrumentAttached = false }).ToArray() };
    }

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

    private static int PlaceScore(LiveListener listener, GridCell cell, GridCell start, HashSet<GridCell> reserved)
    {
        var preferredBand = listener.Enthusiasm >= 90 ? 0 : listener.Enthusiasm >= 60 ? 3 : 6;
        var band = (cell.X - 103) / 2;
        var travel = Math.Abs(cell.X - start.X) + Math.Abs(cell.Z - start.Z);
        var sightline = Math.Abs(cell.Z - 150);
        var stableOffset = (int)((listener.AgentId * 17 + (ulong)(cell.X * 13 + cell.Z * 7)) % 13);
        var density = reserved.Count(other => Math.Abs(other.X - cell.X) <= 3 && Math.Abs(other.Z - cell.Z) <= 3);
        var densityCost = listener.Enthusiasm >= 90 ? 3 : listener.Enthusiasm >= 60 ? 8 : 14;
        return Math.Abs(band - preferredBand) * 35 + sightline * 5 + travel + density * densityCost + stableOffset;
    }

    private static string? ValidatePersistedLivePerformance(LivePerformanceSnapshot? live, SessionPersistenceSnapshot snapshot)
    {
        if (live is null) return null;
        if (snapshot.Preparation is not { } preparation || live.Version != 2 || !Enum.IsDefined(live.Stage) ||
            live.Performers is null || live.Listeners is null || live.Performers.Length != 3 ||
            live.Listeners.Length != preparation.Tier * 20 || live.PlannedTick < preparation.StartedTick ||
            live.StartedTick > snapshot.CurrentTick || live.EndedTick > snapshot.CurrentTick ||
            live.Performers.Select(item => item.AgentId).Distinct().Count() != 3 ||
            live.Listeners.Select(item => item.AgentId).Distinct().Count() != live.Listeners.Length ||
            live.Listeners.Any(item => item.ListenedTicks is < 0 or > LiveSetDurationTicks || item.EnjoymentEarned < 0 ||
                item.Place is { } place && (place.X is < 103 or > 122 || place.Z is < 135 or > 165)))
            return "Live performance state invalid.";
        GridCell[] stageCells = [new(96, 150), new(94, 146), new(93, 152)];
        GridCell[] accessCells = [new(101, 156), new(101, 157), new(101, 158)];
        GridCell[] stairCells = [new(99, 156), new(99, 157), new(99, 158)];
        var roster = preparation.People.Where(item => item.Role == ProtectedPersonRole.Performer).ToArray();
        for (var i = 0; i < 3; i++)
        {
            var performer = live.Performers[i];
            var nav = snapshot.NavigationAgents?.SingleOrDefault(item => item.Id == performer.AgentId);
            if (performer.AgentId != roster[i].AgentId || performer.StageCell != stageCells[i] ||
                performer.AccessCell != accessCells[i] || performer.StairCell != stairCells[i] || nav is null ||
                performer.StairReached && !performer.AccessReached || performer.OnStage && !performer.StairReached ||
                snapshot.CurrentTick < live.PlannedTick - LiveSetStageEntryLeadTicks && performer.StairReached ||
                performer.OnStage && (live.Stage == LiveSetStage.Finished || nav.Action != (int)AgentNavigationAction.Arrived ||
                    nav.DestinationX != performer.StageCell.X || nav.DestinationZ != performer.StageCell.Z) ||
                performer.InstrumentAttached != (i < 2 && performer.OnStage && live.Stage != LiveSetStage.Finished))
                return "Live performer route or attachment invalid.";
        }
        return null;
    }
}

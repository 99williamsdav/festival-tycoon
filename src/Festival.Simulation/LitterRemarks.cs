namespace Festival.Simulation;

// Read-only presentation input; querying this never changes waste, choices or satisfaction.
public sealed record LitterRemarkSituation(ulong AgentId, string? FullBinDisposalId, int NearbyGroundPieces);

public sealed partial class GameSession
{
    public IReadOnlyList<LitterRemarkSituation> CaptureLitterRemarkSituations()
    {
        if (_litter is null || !MedicalOperationsActive) return [];
        EnsureWasteIndices();
        var result = new List<LitterRemarkSituation>();
        foreach (var person in PeopleIn(PersonView.Roster))
        {
            if (!person.Admitted || person.Departed || HigherPriorityOwns(person.Id) || LitterUrgent(person.Id)) continue;
            var nav = _navigationAgents[new(person.Id)];
            var waste = CaptureCarriedWaste(person.Id);
            var fullDisposal = waste is { Approach: { } side, BinId: { } bin, ActionTick: >= 0 } &&
                _binCounts.GetValueOrDefault(bin) >= LitterRules.BinCapacity && AtLitterCell(person.Id, side)
                ? waste.Id : null;
            var nearby = nav.Action == AgentNavigationAction.Travelling
                ? GroundNear(PersonCell(person.Id), LitterRules.GroundEffectRadiusCells).Take(LitterRules.GroundLossCap).Count() : 0;
            result.Add(new(person.Id, fullDisposal, nearby));
        }
        return result;
    }
}

public sealed record LitterPersonCue(ulong AgentId, string Text);

// Small, presentation-only backlog. Reset/load establishes a baseline rather than replaying speech.
public sealed class LitterCuePlanner
{
    public const int LotsOfLitterPieces = 4;
    public const int DurationTicks = 240, GlobalSpacingTicks = 640, PersonCooldownTicks = 3200;
    public static readonly string[] FullBinLines = ["This bin's full!", "Someone needs to empty this!", "It's overflowing!",
        "There's no room in here!", "Does no one empty the bins?", "Are there any other bins?"];
    public static readonly string[] GroundLines = ["What a mess!", "Rubbish everywhere!", "Someone needs to clean this up.",
        "Ew, it's sticky", "Grim.", "Where's the bin?"];
    private sealed record Pending(ulong AgentId, bool FullBin, long Tick);
    private readonly Dictionary<ulong, LitterRemarkSituation> _previous = [];
    private readonly Dictionary<ulong, long> _lastPersonCue = [];
    private readonly List<Pending> _pending = [];
    private LitterPersonCue? _active;
    private long _activeUntil, _lastCueTick = long.MinValue, _lastObservedTick = -1;

    public void Reset(IReadOnlyList<LitterRemarkSituation> situations, long tick)
    {
        _previous.Clear(); _lastPersonCue.Clear(); _pending.Clear(); _active = null;
        _activeUntil = 0; _lastCueTick = long.MinValue; _lastObservedTick = tick;
        foreach (var situation in situations) _previous[situation.AgentId] = situation;
    }

    public LitterPersonCue? Observe(IReadOnlyList<LitterRemarkSituation> situations, long tick, bool speechBlocked = false)
    {
        if (_lastObservedTick < 0 || tick < _lastObservedTick) Reset(situations, tick);
        var current = situations.ToDictionary(s => s.AgentId);
        if (tick > _lastObservedTick)
        {
            foreach (var situation in situations.OrderBy(s => s.AgentId))
            {
                var prior = _previous.GetValueOrDefault(situation.AgentId);
                var fullBin = situation.FullBinDisposalId is not null && situation.FullBinDisposalId != prior?.FullBinDisposalId;
                var lots = situation.NearbyGroundPieces >= LotsOfLitterPieces && (prior?.NearbyGroundPieces ?? 0) < LotsOfLitterPieces;
                if ((fullBin || lots) && !_pending.Any(p => p.AgentId == situation.AgentId) && _pending.Count < 4)
                    _pending.Add(new(situation.AgentId, fullBin, tick));
            }
            _previous.Clear(); foreach (var situation in situations) _previous[situation.AgentId] = situation;
            _lastObservedTick = tick;
        }
        if (_active is { } active && (tick >= _activeUntil || !current.ContainsKey(active.AgentId))) _active = null;
        _pending.RemoveAll(p => tick - p.Tick > DurationTicks * 2 || !current.TryGetValue(p.AgentId, out var s) ||
            (!p.FullBin && s.NearbyGroundPieces < LotsOfLitterPieces));
        if (speechBlocked) { _active = null; return null; }
        if (_active is not null) return _active;
        if (_lastCueTick != long.MinValue && tick - _lastCueTick < GlobalSpacingTicks) return null;
        var next = _pending.OrderByDescending(p => p.FullBin).ThenBy(p => p.Tick).ThenBy(p => p.AgentId)
            .FirstOrDefault(p => !_lastPersonCue.TryGetValue(p.AgentId, out var last) || tick - last >= PersonCooldownTicks);
        if (next is null) return null;
        _pending.Remove(next);
        var lines = next.FullBin ? FullBinLines : GroundLines;
        var text = lines[(int)((next.AgentId + (ulong)next.Tick) % (ulong)lines.Length)];
        _lastPersonCue[next.AgentId] = _lastCueTick = tick; _activeUntil = tick + DurationTicks;
        return _active = new(next.AgentId, text);
    }
}

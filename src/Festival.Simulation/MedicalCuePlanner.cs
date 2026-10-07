namespace Festival.Simulation;

// Presentation-only, deterministic from the current medical snapshot and observed transitions.
// This state is deliberately not authoritative or part of the save/hash contract.
public sealed record MedicalPersonCue(ulong AgentId, string Text, bool Urgent);

public sealed class MedicalCuePlanner
{
    public const int RoutineDurationTicks = 240;
    public const int RoutineSpacingTicks = 120;
    public const int PersonCooldownTicks = 640;
    public const int MaximumVisibleRoutine = 1;
    private const int MaximumPendingRoutine = 4;
    private enum RoutineKind { None, Tradeoff, SeekWater }
    private sealed record Pending(ulong AgentId, RoutineKind Kind, long Tick);
    private sealed record Active(RoutineKind Kind, long UntilTick);
    private readonly Dictionary<ulong, RoutineKind> _previous = [];
    private readonly Dictionary<ulong, long> _previousDecisionTick = [];
    private readonly Dictionary<ulong, Active> _active = [];
    private readonly Dictionary<ulong, long> _lastPersonCue = [];
    private readonly List<Pending> _pending = [];
    private long _lastRoutineTick = long.MinValue;
    private long _lastObservedTick = -1;
    private bool _initialized;

    // Staying put for the band only makes sense to say while a band is actually playing.
    private bool _musicPlaying = true;
    private RoutineKind RoutineFor(MedicalNeed need) => need.Intent switch
    {
        MedicalIntent.SeekWater => RoutineKind.SeekWater,
        MedicalIntent.WatchShow when _musicPlaying && need.Thirst >= 6_500 &&
            (need.Reason.StartsWith("Watching band:", StringComparison.Ordinal) ||
             need.Reason.StartsWith("Band appeal ", StringComparison.Ordinal)) => RoutineKind.Tradeoff,
        _ => RoutineKind.None
    };

    private static MedicalStage StageFor(MedicalSnapshot medical, MedicalNeed need) => need.Stage;

    private static string? UrgentText(MedicalStage stage) => stage switch
    {
        MedicalStage.Distress => "! I might collapse!",
        MedicalStage.Collapsed => "! Help!",
        MedicalStage.Critical => "! Help! I'm fading!",
        _ => null
    };

    public static readonly string[] SeekWaterLines = ["I'm going to get water", "Water break!", "Back in a sec, need a drink"];
    public static readonly string[] TradeoffLines = ["Need water, but I don't\nwant to miss this band","Thirsty… but this is a good one",
        "Water can wait, I love this song"];

    private static string RoutineText(RoutineKind kind, ulong agentId, long tick)
    {
        var lines = kind switch { RoutineKind.Tradeoff => TradeoffLines, RoutineKind.SeekWater => SeekWaterLines, _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
        return lines[(int)((agentId + (ulong)Math.Max(0, tick)) % (ulong)lines.Length)];
    }

    public void Reset(MedicalSnapshot? medical, long tick)
    {
        _previous.Clear(); _previousDecisionTick.Clear(); _active.Clear(); _lastPersonCue.Clear(); _pending.Clear();
        _lastRoutineTick = long.MinValue; _lastObservedTick = tick;
        _initialized = medical is not null;
        if (medical is not null)
            foreach (var need in medical.Needs)
            {
                _previous[need.AgentId] = RoutineFor(need);
                _previousDecisionTick[need.AgentId] = need.LastDecisionTick;
            }
    }

    public IReadOnlyList<MedicalPersonCue> Observe(MedicalSnapshot medical, long tick, bool musicPlaying = true)
    {
        _musicPlaying = musicPlaying;
        if (!_initialized || tick < _lastObservedTick) Reset(medical, tick);
        var urgent = new List<MedicalPersonCue>();
        var candidates = new List<Pending>();
        foreach (var need in medical.Needs)
        {
            var urgentText = UrgentText(StageFor(medical, need));
            var kind = RoutineFor(need);
            if (urgentText is not null)
            {
                urgent.Add(new(need.AgentId, urgentText, true));
                _active.Remove(need.AgentId);
                _pending.RemoveAll(item => item.AgentId == need.AgentId);
            }
            else if (_previousDecisionTick.GetValueOrDefault(need.AgentId, long.MinValue) != need.LastDecisionTick &&
                     _previous.GetValueOrDefault(need.AgentId) != kind && kind != RoutineKind.None)
                candidates.Add(new(need.AgentId, kind, tick));
            // A thirst-only threshold crossing may change today's classification,
            // but it is not a new decision and must not consume the next bark.
            if (_previousDecisionTick.GetValueOrDefault(need.AgentId, long.MinValue) != need.LastDecisionTick)
            {
                _previous[need.AgentId] = kind;
                _previousDecisionTick[need.AgentId] = need.LastDecisionTick;
            }
        }
        _lastObservedTick = tick;
        if (urgent.Count > 0)
        {
            _active.Clear(); _pending.Clear();
            urgent.Sort((a, b) => a.AgentId.CompareTo(b.AgentId));
            return urgent;
        }
        var current = medical.Needs.ToDictionary(item => item.AgentId, RoutineFor);
        foreach (var id in _active.Keys.ToArray())
            if (_active[id].UntilTick <= tick || !current.TryGetValue(id, out var kind) || kind != _active[id].Kind)
                _active.Remove(id);
        _pending.RemoveAll(item => tick - item.Tick > RoutineDurationTicks * 2 ||
            !current.TryGetValue(item.AgentId, out var kind) || kind != item.Kind);
        // Keep one of each common choice before filling the small backlog, so a
        // burst of show-tradeoff choices cannot hide every water decision.
        var orderedCandidates = candidates.Where(item => item.Kind == RoutineKind.Tradeoff).OrderBy(item => item.AgentId).Take(1)
            .Concat(candidates.Where(item => item.Kind == RoutineKind.SeekWater).OrderBy(item => item.AgentId).Take(1))
            .Concat(candidates.OrderBy(item => item.Kind).ThenBy(item => item.AgentId))
            .DistinctBy(item => item.AgentId);
        foreach (var candidate in orderedCandidates)
        {
            if (_pending.Count >= MaximumPendingRoutine) break;
            if (_active.ContainsKey(candidate.AgentId) || _pending.Any(item => item.AgentId == candidate.AgentId)) continue;
            _pending.Add(candidate);
        }
        if (_active.Count < MaximumVisibleRoutine && _pending.Count > 0 &&
            (_lastRoutineTick == long.MinValue || tick - _lastRoutineTick >= RoutineSpacingTicks))
        {
            var next = _pending.FirstOrDefault(item => !_lastPersonCue.TryGetValue(item.AgentId, out var last) ||
                tick - last >= PersonCooldownTicks);
            if (next is not null)
            {
                _pending.Remove(next);
                _active[next.AgentId] = new(next.Kind, tick + RoutineDurationTicks);
                _lastPersonCue[next.AgentId] = _lastRoutineTick = tick;
            }
        }
        urgent.Sort((a, b) => a.AgentId.CompareTo(b.AgentId));
        urgent.AddRange(_active.OrderBy(item => item.Key)
            .Select(item => new MedicalPersonCue(item.Key, RoutineText(item.Value.Kind, item.Key, item.Value.UntilTick), false)));
        return urgent;
    }
}

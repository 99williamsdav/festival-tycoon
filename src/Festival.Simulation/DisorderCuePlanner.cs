namespace Festival.Simulation;

// Presentation-only state. It is rebuilt from authoritative disorder/medical snapshots
// after load and never participates in the simulation hash or save schema.
public enum DisorderCueKind { Shout, Argument, Fight }
public sealed record DisorderPersonCue(ulong AgentId, string Text, DisorderCueKind Kind);

public sealed class DisorderCuePlanner
{
    public const int ShoutDurationTicks = 200;
    public const int ShoutSpacingTicks = 120;
    public const int PersonCooldownTicks = 640;
    public const int MaximumVisibleShouts = 1;
    public const int MaximumUnpairedArguments = 3;
    private sealed record Pending(ulong AgentId, DisorderGrievance Grievance, DisorderStage Stage, long StageTick);
    private sealed record Active(string Text, long UntilTick);
    private readonly Dictionary<ulong, (DisorderStage Stage, long StageTick)> _previous = [];
    private readonly Dictionary<ulong, Active> _active = [];
    private readonly Dictionary<ulong, long> _lastPersonShout = [];
    private readonly List<Pending> _pending = [];
    private long _lastShoutTick = long.MinValue;
    private long _lastObservedTick = -1;
    private bool _initialized;

    public static ulong? CurrentOpponentId(DisorderSnapshot disorder, DisorderPerson person)
    {
        if (person.OpponentId is not { } otherId) return null;
        if (person.Stage == DisorderStage.Fight)
        {
            if (otherId == disorder.SecurityId) return otherId;
            return disorder.People.Any(item => item.AgentId == otherId && item.Stage == DisorderStage.Fight &&
                item.OpponentId == person.AgentId) ? otherId : null;
        }
        if (person.Stage != DisorderStage.Argument) return null;
        if (otherId == disorder.SecurityId)
            return !disorder.SecurityIncapacitated && disorder.ResponseStage == SecurityResponseStage.Confronting &&
                disorder.ResponseTargetId == person.AgentId ? otherId : null;
        // Guest arguments have no assigned pair before a fight; reciprocal IDs
        // here can only be history retained after an earlier confrontation.
        return null;
    }

    public static string CurrentCounterpartInspectorLine(DisorderSnapshot disorder, DisorderPerson person,
        Func<ulong, string> name, Func<ulong, string?> position)
    {
        if (CurrentOpponentId(disorder, person) is not { } id) return "COUNTERPART not established\n";
        var at = position(id);
        return $"COUNTERPART {name(id)}{(at is null ? "" : $" • {at}")}\n";
    }

    public void Reset(DisorderSnapshot? disorder, long tick)
    {
        _previous.Clear(); _active.Clear(); _lastPersonShout.Clear(); _pending.Clear();
        _lastShoutTick = long.MinValue; _lastObservedTick = tick;
        _initialized = disorder is not null;
        if (disorder is not null)
            foreach (var person in disorder.People)
                _previous[person.AgentId] = (person.Stage, person.StageTick);
    }

    public IReadOnlyList<DisorderPersonCue> Observe(DisorderSnapshot disorder, MedicalSnapshot? medical, long tick)
    {
        if (!_initialized || tick < _lastObservedTick) Reset(disorder, tick);
        var urgentMedical = medical?.Needs.Where(item => item.Stage is MedicalStage.Distress or MedicalStage.Collapsed or MedicalStage.Critical)
            .Select(item => item.AgentId).ToHashSet() ?? [];
        if (medical is { } m && m.Stage is MedicalStage.Distress or MedicalStage.Collapsed or MedicalStage.Critical)
            urgentMedical.Add(m.AtRiskGuestId);
        var people = disorder.People.ToDictionary(item => item.AgentId);
        var cues = new List<DisorderPersonCue>();
        var paired = new HashSet<ulong>();
        foreach (var person in disorder.People.OrderBy(item => item.AgentId))
        {
            if (CurrentOpponentId(disorder, person) is not { } otherId) continue;
            var securityOpponent = otherId == disorder.SecurityId;
            if (!urgentMedical.Contains(person.AgentId))
                cues.Add(new(person.AgentId, person.Stage == DisorderStage.Fight ? "FIGHT" : "ARGUMENT",
                    person.Stage == DisorderStage.Fight ? DisorderCueKind.Fight : DisorderCueKind.Argument));
            if (securityOpponent && !urgentMedical.Contains(otherId) && paired.Add(otherId))
                cues.Add(new(otherId, person.Stage == DisorderStage.Fight ? "FIGHT" : "ARGUMENT",
                    person.Stage == DisorderStage.Fight ? DisorderCueKind.Fight : DisorderCueKind.Argument));
            paired.Add(person.AgentId);
        }
        var unpaired = disorder.People.Where(item => item.Stage == DisorderStage.Argument && !paired.Contains(item.AgentId) &&
                !urgentMedical.Contains(item.AgentId)).OrderByDescending(item => item.Pressure).ThenBy(item => item.AgentId)
            .Take(MaximumUnpairedArguments);
        cues.AddRange(unpaired.Select(item => new DisorderPersonCue(item.AgentId, "ARGUMENT", DisorderCueKind.Argument)));
        var fightCues = cues.Where(item => item.Kind == DisorderCueKind.Fight).ToArray();

        foreach (var person in disorder.People)
        {
            var prior = _previous.GetValueOrDefault(person.AgentId);
            if (person.Stage is DisorderStage.Complaint or DisorderStage.Agitated &&
                (prior.Stage != person.Stage || prior.StageTick != person.StageTick) &&
                !urgentMedical.Contains(person.AgentId) && person.Grievance != DisorderGrievance.None)
                _pending.Add(new(person.AgentId, person.Grievance, person.Stage, person.StageTick));
            _previous[person.AgentId] = (person.Stage, person.StageTick);
        }
        _lastObservedTick = tick;
        if (fightCues.Length > 0)
        {
            _active.Clear(); _pending.Clear();
            return fightCues.OrderBy(item => item.AgentId).ToArray();
        }
        _pending.RemoveAll(item => tick - item.StageTick > ShoutDurationTicks * 2 ||
            !people.TryGetValue(item.AgentId, out var person) || person.Stage != item.Stage ||
            person.StageTick != item.StageTick || urgentMedical.Contains(item.AgentId));
        foreach (var id in _active.Keys.ToArray())
            if (_active[id].UntilTick <= tick || !people.TryGetValue(id, out var person) ||
                person.Stage is not (DisorderStage.Complaint or DisorderStage.Agitated) || urgentMedical.Contains(id))
                _active.Remove(id);
        if (_active.Count < MaximumVisibleShouts && _pending.Count > 0 &&
            (_lastShoutTick == long.MinValue || tick - _lastShoutTick >= ShoutSpacingTicks))
        {
            var next = _pending.OrderBy(item => item.StageTick).ThenBy(item => item.AgentId)
                .FirstOrDefault(item => !_lastPersonShout.TryGetValue(item.AgentId, out var last) || tick - last >= PersonCooldownTicks);
            if (next is not null)
            {
                _pending.Remove(next);
                _active[next.AgentId] = new(ShoutText(next), tick + ShoutDurationTicks);
                _lastPersonShout[next.AgentId] = _lastShoutTick = tick;
            }
        }
        cues.AddRange(_active.OrderBy(item => item.Key).Where(item => !cues.Any(cue => cue.AgentId == item.Key))
            .Select(item => new DisorderPersonCue(item.Key, item.Value.Text, DisorderCueKind.Shout)));
        return cues.OrderByDescending(item => item.Kind).ThenBy(item => item.AgentId).ToArray();
    }

    private static string ShoutText(Pending pending)
    {
        var variant = (pending.AgentId + (ulong)Math.Max(0, pending.StageTick)) % 5;
        return pending.Grievance == DisorderGrievance.WaterWait
            ? variant switch { 0 => "Hurry up!", 1 => "This queue is ridiculous!", 2 => "It's an outrage!", 3 => "FFS!", _ => "Grrrr!" }
            : variant switch { 0 => "What the hell?!", 1 => "This is ridiculous!", 2 => "It's an outrage!", 3 => "FFS!", _ => "Grrrr!" };
    }
}

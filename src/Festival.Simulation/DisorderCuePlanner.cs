namespace Festival.Simulation;

// Presentation-only state. It is rebuilt from authoritative disorder/medical snapshots
// after load and never participates in the simulation hash or save schema.
public enum DisorderCueKind { Shout, Argument, Fight }
public sealed record DisorderPersonCue(ulong AgentId, string Text, DisorderCueKind Kind);

public sealed class DisorderCuePlanner
{
    private static bool IsSteward(DisorderSnapshot d, ulong id) => d.Stewards.Any(item => item.WorkerId == id);
    public const int ShoutDurationTicks = 200;
    public const int ShoutSpacingTicks = 120;
    public const int PersonCooldownTicks = 640;
    public const int MaximumVisibleShouts = 1;
    public const int MaximumUnpairedArguments = 3;
    private sealed record Pending(ulong AgentId, DisorderGrievance Grievance, DisorderStage Stage, long StageTick, bool InitialQuestion = false);
    private sealed record Active(string Text, long UntilTick, bool InitialQuestion, bool BandDelayed);
    private readonly Dictionary<ulong, (DisorderStage Stage, long StageTick, DisorderGrievance Grievance)> _previous = [];
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
            if (IsSteward(disorder, otherId)) return otherId;
            return disorder.People.Any(item => item.AgentId == otherId && item.Stage == DisorderStage.Fight &&
                item.OpponentId == person.AgentId) ? otherId : null;
        }
        if (person.Stage != DisorderStage.Argument) return null;
        if (disorder.Stewards.SingleOrDefault(item => item.WorkerId == otherId) is { } response)
            return !response.Incapacitated && response.Stage == SecurityResponseStage.Confronting && response.TargetId == person.AgentId ? otherId : null;
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
                _previous[person.AgentId] = (person.Stage, person.StageTick, person.Grievance);
    }

    public IReadOnlyList<DisorderPersonCue> Observe(DisorderSnapshot disorder, MedicalSnapshot? medical, long tick)
    {
        if (!_initialized || tick < _lastObservedTick) Reset(disorder, tick);
        var urgentMedical = medical?.Needs.Where(item => item.Stage is MedicalStage.Distress or MedicalStage.Collapsed or MedicalStage.Critical)
            .Select(item => item.AgentId).ToHashSet() ?? [];
        var people = disorder.People.ToDictionary(item => item.AgentId);
        var cues = new List<DisorderPersonCue>();
        var paired = new HashSet<ulong>();
        foreach (var person in disorder.People.OrderBy(item => item.AgentId))
        {
            if (CurrentOpponentId(disorder, person) is not { } otherId) continue;
            var securityOpponent = IsSteward(disorder, otherId);
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
            if (person.Grievance == DisorderGrievance.BandDelayed && prior.Grievance != person.Grievance &&
                person.Stage == DisorderStage.Calm && !urgentMedical.Contains(person.AgentId))
                _pending.Add(new(person.AgentId, person.Grievance, person.Stage, person.GrievanceTick, InitialQuestion: true));
            if (person.Stage is DisorderStage.Complaint or DisorderStage.Agitated &&
                (prior.Stage != person.Stage || prior.StageTick != person.StageTick) &&
                !urgentMedical.Contains(person.AgentId) && person.Grievance != DisorderGrievance.None)
                _pending.Add(new(person.AgentId, person.Grievance, person.Stage, person.StageTick));
            _previous[person.AgentId] = (person.Stage, person.StageTick, person.Grievance);
        }
        _lastObservedTick = tick;
        if (fightCues.Length > 0)
        {
            _active.Clear(); _pending.Clear();
            return fightCues.OrderBy(item => item.AgentId).ToArray();
        }
        _pending.RemoveAll(item => tick - item.StageTick > ShoutDurationTicks * 2 ||
            !people.TryGetValue(item.AgentId, out var person) || person.Stage != item.Stage ||
            (item.InitialQuestion ? person.Grievance != DisorderGrievance.BandDelayed || person.GrievanceTick != item.StageTick :
                person.StageTick != item.StageTick) || urgentMedical.Contains(item.AgentId));
        foreach (var id in _active.Keys.ToArray())
            if (_active[id].UntilTick <= tick || !people.TryGetValue(id, out var person) ||
                _active[id].BandDelayed && person.Grievance != DisorderGrievance.BandDelayed ||
                (person.Stage is not (DisorderStage.Complaint or DisorderStage.Agitated) &&
                    !(_active[id].InitialQuestion && person.Stage == DisorderStage.Calm && person.Grievance == DisorderGrievance.BandDelayed)) ||
                urgentMedical.Contains(id))
                _active.Remove(id);
        if (_active.Count < MaximumVisibleShouts && _pending.Count > 0 &&
            (_lastShoutTick == long.MinValue || tick - _lastShoutTick >= ShoutSpacingTicks))
        {
            var next = _pending.OrderBy(item => item.StageTick).ThenBy(item => item.AgentId)
                .FirstOrDefault(item => !_lastPersonShout.TryGetValue(item.AgentId, out var last) || tick - last >= PersonCooldownTicks);
            if (next is not null)
            {
                _pending.Remove(next);
                _active[next.AgentId] = new(ShoutText(next), tick + ShoutDurationTicks, next.InitialQuestion,
                    next.Grievance == DisorderGrievance.BandDelayed);
                _lastPersonShout[next.AgentId] = _lastShoutTick = tick;
            }
        }
        cues.AddRange(_active.OrderBy(item => item.Key).Where(item => !cues.Any(cue => cue.AgentId == item.Key))
            .Select(item => new DisorderPersonCue(item.Key, item.Value.Text, DisorderCueKind.Shout)));
        return cues.OrderByDescending(item => item.Kind).ThenBy(item => item.AgentId).ToArray();
    }

    // What a grumbling guest shouts, mild while complaining and angry once agitated. Each says what's wrong.
    public static readonly string[] BandLateMild = ["Where is the band?", "When are they starting?", "Are they ready yet?", "What's the hold-up?",
        "Shouldn't the band be on?", "Is there even a band?", "Tick tock…", "I paid for music, not silence", "Did they get lost?"];
    public static readonly string[] BandLateAngry = ["Start the music!", "We've waited long enough!", "Where the hell is the band?!", "This delay is ridiculous!",
        "Get on with it!", "BORING!", "We want music!", "Oi! Get on stage!"];
    public static readonly string[] WaterWaitMild = ["I'm parched…", "Come on, it's just water!", "How long does it take?!", "Is this the tap queue?"];
    public static readonly string[] WaterWaitAngry = ["I'm dying of thirst here!", "Some of us are thirsty!", "Hurry UP!", "This queue is ridiculous!"];
    public static readonly string[] QueueWaitMild = ["How long can it take?!", "Is this queue even moving?", "I'll miss the band at this rate", "Come ON…"];
    public static readonly string[] QueueWaitAngry = ["This queue is a joke!", "I've been stood here for ages!", "Some of us want to see the band!", "Hurry UP!"];
    public static readonly string[] MusicCutMild = ["Has the power gone?", "Who pulled the plug?!", "Noooo, I love this one!"];
    public static readonly string[] MusicCutAngry = ["Oi! Turn it back on!", "Bring back the music!", "Who pulled the plug?!"];

    private static string ShoutText(Pending pending)
    {
        var angry = pending.Stage == DisorderStage.Agitated;
        var lines = pending.Grievance switch
        {
            DisorderGrievance.BandDelayed => angry ? BandLateAngry : BandLateMild,
            DisorderGrievance.WaterWait => angry ? WaterWaitAngry : WaterWaitMild,
            DisorderGrievance.QueueWait => angry ? QueueWaitAngry : QueueWaitMild,
            _ => angry ? MusicCutAngry : MusicCutMild,
        };
        return lines[(int)((pending.AgentId + (ulong)Math.Max(0, pending.StageTick)) % (ulong)lines.Length)];
    }
}

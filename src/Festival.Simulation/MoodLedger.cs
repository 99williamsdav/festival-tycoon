namespace Festival.Simulation;

/// <summary>What moves a guest's satisfaction, as the crowd-mood breakdown names it.</summary>
public enum MoodCause { Music, MusicCutOff, FoodAndDrink, FriendlyQueues, FreeWaterPerk, StaffNearby, MudAndPuddles, LitterAndWasps, ToiletSmell, BrokenTap, StuckInToilet,
    Heat, Thirst, LongQueues }

/// <summary>A cause's share of the crowd's recent mood change, in hundredths of a satisfaction percent per guest.</summary>
public sealed record MoodChange(MoodCause Cause, long Change);

/// <summary>
/// Every change to a guest's satisfaction, by cause, over the last few festival minutes: what's lifting the crowd and
/// what's dragging it down. Presentation only. The simulation never reads it, so it isn't saved; a loaded game starts
/// with an empty window that fills again within minutes.
/// </summary>
public sealed partial class GameSession
{
    public const int MoodWindowMinutes = 10;
    private const int MinuteTicks = 80;
    private readonly long[,] _moodBuckets = new long[MoodWindowMinutes, Enum.GetValues<MoodCause>().Length];
    private readonly long[] _moodBucketMinute = Enumerable.Repeat(-1L, MoodWindowMinutes).ToArray();
    /// <summary>Everything recorded since this session began, for checking that no change slips past the ledger.</summary>
    public long MoodRecordedTotal { get; private set; }

    /// <summary>Thirst (of 10,000) from which a guest feels it in their mood: 1 point a festival minute, rising to 5 near the top.</summary>
    public const int MoodThirstFrom = 6_000, MoodThirstStep = 1_000;
    /// <summary>Heat more gently (from 70%, at most 2 a festival minute): there's little the player can do about the weather yet.</summary>
    public const int MoodHeatFrom = 7_000, MoodHeatStep = 3_000;
    /// <summary>What a festival minute in a queue past a guest's own patience costs them.</summary>
    public const int MoodQueueLossPerSecond = 3;

    /// <summary>Once a festival minute: hot and thirsty guests feel it, before it ever comes to a collapse.</summary>
    private void ApplyHeatAndThirstMood()
    {
        if (CurrentTick % MinuteTicks != 0 || _preparation?.Status != PreparationStatus.Running) return;
        foreach (var person in PeopleIn(PersonView.Medical).Where(p => p.Role == ProtectedPersonRole.Guest && p.Admitted && !p.Departed).ToArray())
        {
            if (person.Thirst >= MoodThirstFrom) ChangeSatisfaction(person.Id, -UnpleasantFor(person.Id, 1 + (person.Thirst - MoodThirstFrom) / MoodThirstStep), MoodCause.Thirst);
            if (person.HeatExposure >= MoodHeatFrom) ChangeSatisfaction(person.Id, -UnpleasantFor(person.Id, 1 + (person.HeatExposure - MoodHeatFrom) / MoodHeatStep), MoodCause.Heat);
        }
    }

    /// <summary>Changes a guest's satisfaction by up to <paramref name="delta"/> (kept within 0..10,000) and notes why.</summary>
    private void ChangeSatisfaction(ulong id, int delta, MoodCause cause)
    {
        if (delta == 0) return;
        var applied = 0;
        MutatePerson(id, person =>
        {
            var next = Math.Clamp(person.Satisfaction + delta, 0, 10_000);
            applied = next - person.Satisfaction;
            person.Satisfaction = next;
        });
        RecordMood(id, applied, cause);
    }

    /// <summary>Notes a satisfaction change already applied elsewhere. Only guests count towards the crowd's mood.</summary>
    private void RecordMood(ulong id, int applied, MoodCause cause)
    {
        if (applied == 0 || !IsGuest(id)) return;
        var minute = CurrentTick / MinuteTicks;
        var slot = (int)(minute % MoodWindowMinutes);
        if (_moodBucketMinute[slot] != minute)
        {
            _moodBucketMinute[slot] = minute;
            for (var c = 0; c < _moodBuckets.GetLength(1); c++) _moodBuckets[slot, c] = 0;
        }
        _moodBuckets[slot, (int)cause] += applied;
        MoodRecordedTotal += applied;
    }

    /// <summary>The causes behind the crowd's mood over the last <see cref="MoodWindowMinutes"/> festival minutes, biggest first.</summary>
    public IReadOnlyList<MoodChange> RecentMoodChanges()
    {
        var now = CurrentTick / MinuteTicks;
        var totals = new long[_moodBuckets.GetLength(1)];
        for (var slot = 0; slot < MoodWindowMinutes; slot++)
            if (_moodBucketMinute[slot] >= 0 && now - _moodBucketMinute[slot] < MoodWindowMinutes)
                for (var c = 0; c < totals.Length; c++) totals[c] += _moodBuckets[slot, c];
        return Enum.GetValues<MoodCause>().Select(cause => new MoodChange(cause, totals[(int)cause]))
            .Where(change => change.Change != 0).OrderByDescending(change => Math.Abs(change.Change)).ToArray();
    }
}

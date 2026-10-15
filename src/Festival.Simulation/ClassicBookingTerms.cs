namespace Festival.Simulation;

public enum ClassicLineupChange { None, Comparable, Ordinary, Substantial }
/// <summary>The original advertised act and immutable paid quote; changing a catalogue entry cannot rewrite it.</summary>
public sealed record ClassicAnnouncedAct(string ActId, int Genre, int Popularity, int PaidFeePennies)
{
    public static ClassicAnnouncedAct EmptySlot { get; } = new("", 0, 0, 0);
}

/// <summary>User-approved provisional terms. These calculations have no side effects or hidden fines.</summary>
public static class ClassicBookingTerms
{
    public const int CancellationReputationPenalty = 5;
    public static int RefundPercent(ClassicLineupChange change) => change switch
    { ClassicLineupChange.None => 0, ClassicLineupChange.Comparable => 5, ClassicLineupChange.Ordinary => 20,
      ClassicLineupChange.Substantial => 40, _ => throw new ArgumentOutOfRangeException(nameof(change)) };
    public static int SatisfactionPenalty(ClassicLineupChange change) => change switch
    { ClassicLineupChange.None => 0, ClassicLineupChange.Comparable => 200, ClassicLineupChange.Ordinary => 1_000,
      ClassicLineupChange.Substantial => 2_000, _ => throw new ArgumentOutOfRangeException(nameof(change)) };
    public static int RefundTarget(int originalTickets, ClassicLineupChange highestChange)
    {
        if (originalTickets is < 0 or > GameSession.MaxActivePeople) throw new ArgumentOutOfRangeException(nameof(originalTickets));
        return (originalTickets * RefundPercent(highestChange) + 99) / 100;
    }

    public static ClassicLineupChange Assess(IReadOnlyList<ClassicAnnouncedAct> original,
        IReadOnlyList<FestivalAct?> replacements, ClassicLineupChange previous = ClassicLineupChange.None)
    {
        _ = RefundPercent(previous);
        if (original is null || replacements is null || original.Count is not(3 or 6 or 9) || replacements.Count != original.Count ||
            original.Any(a => a is null || string.IsNullOrWhiteSpace(a.ActId) || a.Genre is < 0 or >= FestivalGenre.Count ||
                a.Popularity is < 0 or > 100 || a.PaidFeePennies < 0) ||
            replacements.Any(a => a is not null && (string.IsNullOrWhiteSpace(a.Id) || a.Genre is < 0 or >= FestivalGenre.Count || a.Popularity is < 0 or > 100)) ||
            original.Select(a => a.ActId).Distinct().Count() != original.Count ||
            replacements.Where(a => a is not null).Select(a => a!.Id).Distinct().Count() != replacements.Count(a => a is not null))
            throw new ArgumentException("Distinct announced acts and matching three-slot stage programmes are required.");
        var removed = original.Select((act, slot) => (Act: act, Headliner: slot % 3 == 2))
            .Where(a => !replacements.Any(b => b?.Id == a.Act.ActId)).ToArray();
        var added = replacements.Where(a => a is null || !original.Any(b => b.ActId == a.Id)).ToArray();
        if (removed.Length == 0) return previous;
        // At most 512 masks for nine slots. Retained acts can move stage/slot without becoming a loss.
        var best=Enumerable.Repeat((int)ClassicLineupChange.Substantial+1,1<<added.Length).ToArray();best[0]=0;
        for(var used=0;used<best.Length;used++)
        {
            var index=System.Numerics.BitOperations.PopCount((uint)used);
            if(index==removed.Length)continue;
            for (var i = 0; i < added.Length; i++)
            {
                if ((used & (1 << i)) != 0) continue;
                var lost = removed[index]; var next = added[i];
                var comparable = next is not null && next.Genre == lost.Act.Genre && next.Popularity >= lost.Act.Popularity - 5;
                var severity = comparable ? ClassicLineupChange.Comparable :
                    lost.Headliner || lost.Act.Popularity - (next?.Popularity ?? 0) >= 15 ? ClassicLineupChange.Substantial : ClassicLineupChange.Ordinary;
                var nextMask=used|(1<<i);
                best[nextMask]=Math.Min(best[nextMask],Math.Max(best[used],(int)severity));
            }
        }
        return (ClassicLineupChange)Math.Max((int)previous,best[^1]);
    }
}

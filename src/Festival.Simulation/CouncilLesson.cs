namespace Festival.Simulation;

public sealed partial class GameSession
{
    /// <summary>
    /// The council's pointed remark at the hearing: the main lesson behind the death, said the way a weary councillor
    /// would. A medical death the medic could have prevented is put down to the medic; otherwise to what caused it.
    /// </summary>
    public string? CouncilLesson()
    {
        if (_equipment?.Stage == EquipmentStage.Terminal) return "Perhaps keeping an eye on the generator capacity would have been smart.";
        // A fight's death goes on record as a disorder death, never a medical one.
        if (_lifecycle?.Casualties.LastOrDefault()?.TransactionId.StartsWith("disorder-death:", StringComparison.Ordinal) == true)
            return InjuryLesson;
        // The death on record names who died; names are unique on a roster.
        if (CaptureMedical() is not { Fatal: true } || _lifecycle?.Casualties.LastOrDefault() is not { } casualty ||
            PeopleIn(PersonView.Roster).FirstOrDefault(p => p.Name == casualty.PersonId) is not { } victim) return null;
        var id = victim.Id;
        var cause = CollapseCauseOf(id);
        if (cause != CollapseCause.Injury && MedicWasTheWeakLink(id)) return "Maybe don't cheap out on lifesaving medics?";
        return cause switch
        {
            CollapseCause.WaspSting => StungByACrowdedBin(id)
                ? "Perhaps it wasn't a good idea leaving a full bin in a crowded area."
                : "Perhaps someone could have emptied that bin before the wasps moved in.",
            CollapseCause.ToiletFumes => "How someone can be left stuck in a loo long enough to die is beyond me.",
            CollapseCause.Drink => "A festival can survive without that last round, you know.",
            CollapseCause.Injury => InjuryLesson,
            _ => "It was hot. People get thirsty. Perhaps more water than one tap's worth?",
        };
    }

    private const string InjuryLesson = "Perhaps someone could have stepped in before it came to blows?";

    /// <summary>A below-standard medic was hired, or nobody was sent to them before they died.</summary>
    private bool MedicWasTheWeakLink(ulong id) =>
        HiredStaff(StaffRole.Medic) is { Grade: < 0 } ||
        !_medical!.Evidence.Any(e => e.Id == "medical:dispatch" && e.Description.EndsWith(MedicDispatchedTo(id), StringComparison.Ordinal));

    /// <summary>The tail of the dispatch record naming the patient: shared with where it's written.</summary>
    private static string MedicDispatchedTo(ulong patientId) => $"dispatched to patient {patientId}.";

    /// <summary>The wasp bin nearest the victim stood among the audience, or with a crowd round it.</summary>
    private bool StungByACrowdedBin(ulong id)
    {
        var at = PersonCell(id);
        var bin = CaptureBins().Where(b => b.Wasps).OrderBy(b => CellDistanceSquared(b.Cell, at)).FirstOrDefault() ??
                  CaptureBins().OrderBy(b => CellDistanceSquared(b.Cell, at)).FirstOrDefault();
        if (bin is null) return false;
        if (InAudienceArea(bin.Cell)) return true;
        const int crowd = 8, reach = 12;
        return PeopleIn(PersonView.Roster).Count(p => p.Admitted && !p.Departed && CellDistanceSquared(PersonCell(p.Id), bin.Cell) <= reach * reach) >= crowd;
    }
}

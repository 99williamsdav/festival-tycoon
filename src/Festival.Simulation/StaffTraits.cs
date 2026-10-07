namespace Festival.Simulation;

// What each staff trait does on the day. Traits come from the hired candidate (see StaffHiring),
// so every effect here is derived and nothing extra is saved.
public sealed partial class GameSession
{
    /// <summary>How much a sneaky alcoholic likes beer, whatever their stated taste.</summary>
    public const int AlcoholicBeerTaste = 100;
    /// <summary>Extra pull of food and drink on a slacker, in the chooser's enjoyment units.</summary>
    public const long SlackerTreatValue = 150_000;
    /// <summary>Satisfaction a charismatic (or abrasive) staff member adds to (or takes from) each nearby guest per second.</summary>
    public const int StaffPresencePerSecond = 4;
    /// <summary>How close a guest must be to feel a staff member's presence.</summary>
    public const int StaffPresenceRadiusMillimetres = 5_000;

    /// <summary>Ticks between each point of toilet need: twice as often for a weak bladder, half as often for an iron one.</summary>
    private int ToiletNeedGainEveryTicks(ulong id) =>
        StaffHas(id, StaffTrait.WeakBladder) || HasIbs(id) ? ToiletRules.NeedGainEveryTicks / 2 :
        StaffHas(id, StaffTrait.IronBladder) ? ToiletRules.NeedGainEveryTicks * 2 : ToiletRules.NeedGainEveryTicks;

    /// <summary>The bar's rule: no beer for abstainers, the very drunk or staff. A sneaky alcoholic gets served anyway, with no limit.</summary>
    private bool BeerAllowed(Person person) =>
        StaffHas(person.Id, StaffTrait.SneakyAlcoholic) ||
        !Teetotal(person) && person.Intoxication < 7_000 && person.Role != ProtectedPersonRole.Staff;

    private static bool SavedBeerBarred(ImmersionPerson person, PreparationSnapshot prep, Dictionary<ulong, StaffCandidate> hired, bool beerFestival) =>
        !(hired.GetValueOrDefault(person.AgentId)?.Has(StaffTrait.SneakyAlcoholic) ?? false) &&
        (person.Abstains && !beerFestival || prep.People.Single(n => n.AgentId == person.AgentId).Role == ProtectedPersonRole.Staff);

    /// <summary>Walking pace share from a dodgy knee.</summary>
    private int StaffGaitPermille(ulong id) => StaffHas(id, StaffTrait.DodgyKnee) ? StaffCatalogue.DodgyKneePacePermille : 1_000;

    /// <summary>How long after the gates open a tardy staff member sets off; zero for everyone else.</summary>
    private int StaffLateTicks(ulong id) => HiredCandidateFor(id)?.LateTicks ?? 0;

    /// <summary>The sound engineer's mix, dulled by drink.</summary>
    private int SoundMixingBonus()
    {
        if (StaffSlotId(StaffRole.Sound) is not { } id || HiredCandidateFor(id) is not { } engineer) return 0;
        return engineer.MixingBonus - (PersonIn(PersonView.Consumption, id)?.Intoxication ?? 0) / 2_500;
    }

    /// <summary>Once a second, charismatic staff lift the guests around them and abrasive staff wear them down.</summary>
    private void ApplyStaffPresence()
    {
        var presences = PeopleIn(PersonView.Roster)
            .Where(person => person.Role == ProtectedPersonRole.Staff && person.Admitted && !person.Departed && !PersonCollapsed(person.Id))
            .Select(person => (person.Id, Effect: StaffHas(person.Id, StaffTrait.Charismatic) ? StaffPresencePerSecond :
                StaffHas(person.Id, StaffTrait.Abrasive) ? -StaffPresencePerSecond : 0))
            .Where(item => item.Effect != 0 && _navigationAgents.ContainsKey(new(item.Id))).ToArray();
        if (presences.Length == 0) return;
        var radius = (long)StaffPresenceRadiusMillimetres * StaffPresenceRadiusMillimetres;
        foreach (var guest in PeopleIn(PersonView.Roster).Where(person => person.Role == ProtectedPersonRole.Guest && person.Admitted && !person.Departed).ToArray())
        {
            var here = _navigationAgents[new(guest.Id)];
            var change = 0;
            foreach (var (staffId, effect) in presences)
            {
                var there = _navigationAgents[new(staffId)];
                long dx = here.XMillimetres - there.XMillimetres, dz = here.ZMillimetres - there.ZMillimetres;
                if (dx * dx + dz * dz <= radius) change += effect;
            }
            if (change != 0) ChangeSatisfaction(guest.Id, change, MoodCause.StaffNearby);
        }
    }
}

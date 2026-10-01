namespace Festival.Simulation;

// The staff market: who is on offer this edition, who was hired into each slot, and the
// slot identities their names and abilities fill.
public sealed partial class GameSession
{
    private (ulong Seed, ulong? MedicId, ulong? StewardId, int Calming, int Confrontation, StaffCandidate[] Candidates)? _staffCandidates;

    /// <summary>This edition's candidates for the roles the festival runs: sound always, medic and steward with their systems.</summary>
    public IReadOnlyList<StaffCandidate> GetStaffCandidates()
    {
        if (_preparation is not { } p) return [];
        var key = (p.OfferSeed, _medical?.MedicId, _disorder?.SecurityId, _disorder?.CalmingSkill ?? 0, _disorder?.ConfrontationSkill ?? 0);
        if (_staffCandidates is not { } cached || (cached.Seed, cached.MedicId, cached.StewardId, cached.Calming, cached.Confrontation) != key)
            _staffCandidates = cached = (key.OfferSeed, key.Item2, key.Item3, key.Item4, key.Item5,
                StaffCandidatesFor(key.OfferSeed, key.Item2, key.Item3, key.Item4, key.Item5));
        return cached.Candidates;
    }

    private static StaffCandidate[] StaffCandidatesFor(ulong seed, ulong? medicId, ulong? stewardId, int calming, int confrontation) =>
        StaffCatalogue.Candidates(seed, new(medicId is { } m ? GetWalkingSpeedPermille(new(m)) : 1_000,
                stewardId is { } s ? GetWalkingSpeedPermille(new(s)) : 1_000, calming, confrontation))
            .Where(c => c.Role == StaffRole.Sound || c.Role == StaffRole.Medic && medicId is not null || c.Role == StaffRole.Steward && stewardId is not null)
            .ToArray();

    private static StaffCandidate[] SavedStaffCandidates(SessionPersistenceSnapshot s) =>
        StaffCandidatesFor(s.Preparation!.OfferSeed, s.Medical?.MedicId, s.Disorder?.SecurityId, s.Disorder?.CalmingSkill ?? 0, s.Disorder?.ConfrontationSkill ?? 0);

    /// <summary>The candidate paid for in a role's main or extra slot this attempt.</summary>
    public StaffCandidate? HiredStaff(StaffRole role, bool extra = false) =>
        _preparation is { } p ? StaffCatalogue.Hired(GetStaffCandidates(), p.AcceptedOffers, role, extra) : null;

    /// <summary>The candidate chosen for a slot: planned while preparing, paid once open.</summary>
    public StaffCandidate? ChosenStaff(StaffRole role, bool extra = false) =>
        _preparation is { } p ? StaffCatalogue.Hired(GetStaffCandidates(), p.Plan is { Committed: false } plan ? plan.OfferIds : p.AcceptedOffers, role, extra) : null;

    /// <summary>The roster slot a main hire fills; extras get their own person when hired.</summary>
    private ulong? StaffSlotId(StaffRole role) => role switch
    {
        StaffRole.Medic => _medical?.MedicId,
        StaffRole.Steward => _disorder?.SecurityId,
        _ => SoundSlotId(PeopleIn(PersonView.Roster).Select(person => (person.Id, person.Role)))
    };

    // The sound engineer is the first staff member the edition creates.
    private static ulong? SoundSlotId(IEnumerable<(ulong Id, ProtectedPersonRole Role)> people) =>
        people.Where(person => person.Role == ProtectedPersonRole.Staff).Select(person => (ulong?)person.Id).Min();

    private void RenameStaffSlot(ulong id, string name) => PreparationView = PreparationView! with
        { People = PreparationView.People.Select(person => person.AgentId == id ? person with { Name = name } : person).ToArray() };

    /// <summary>The saved roster names: vacancies, renamed by each main hire this attempt.</summary>
    private static EditionPerson[] NameHiredStaff(EditionPerson[] people, SessionPersistenceSnapshot s, StaffCandidate[] candidates)
    {
        var accepted = s.Preparation!.AcceptedOffers;
        var names = new Dictionary<ulong, string>();
        void Name(ulong? slot, StaffRole role)
        {
            if (slot is { } id && StaffCatalogue.Hired(candidates, accepted, role, false) is { } hired) names[id] = hired.Name;
        }
        Name(SoundSlotId(people.Select(person => (person.AgentId, person.Role))), StaffRole.Sound);
        Name(s.Medical?.MedicId, StaffRole.Medic);
        Name(s.Disorder?.SecurityId, StaffRole.Steward);
        return people.Select(person => names.TryGetValue(person.AgentId, out var name) ? person with { Name = name } : person).ToArray();
    }

    private (PreparationSnapshot Key, Dictionary<ulong, StaffCandidate> Map)? _hiredByAgent;

    /// <summary>The candidate working as this person this attempt, if any.</summary>
    public StaffCandidate? HiredCandidateFor(ulong id)
    {
        if (_preparation is not { } p) return null;
        if (_hiredByAgent is not { } cached || !ReferenceEquals(cached.Key, p))
            _hiredByAgent = cached = (p, HiredAgents(GetStaffCandidates().ToArray(), p.AcceptedOffers, p.StaffProfiles,
                SoundSlotId(PeopleIn(PersonView.Roster).Select(person => (person.Id, person.Role))), _medical?.MedicId, _disorder?.SecurityId));
        return cached.Map.GetValueOrDefault(id);
    }

    public bool StaffHas(ulong id, StaffTrait trait) => HiredCandidateFor(id)?.Has(trait) == true;

    private static Dictionary<ulong, StaffCandidate> HiredAgents(StaffCandidate[] candidates, string[] accepted, StaffProfile[] extras,
        ulong? soundSlot, ulong? medicId, ulong? stewardId)
    {
        var map = new Dictionary<ulong, StaffCandidate>();
        void Add(ulong? id, StaffRole role, bool extra)
        {
            if (id is { } agent && StaffCatalogue.Hired(candidates, accepted, role, extra) is { } hired) map[agent] = hired;
        }
        Add(soundSlot, StaffRole.Sound, false);
        Add(medicId, StaffRole.Medic, false);
        Add(stewardId, StaffRole.Steward, false);
        Add(extras.SingleOrDefault(item => item.Role == ResponseRole.Medic)?.AgentId, StaffRole.Medic, true);
        Add(extras.SingleOrDefault(item => item.Role == ResponseRole.Steward)?.AgentId, StaffRole.Steward, true);
        return map;
    }

    /// <summary>Who was hired as whom, from a saved snapshot.</summary>
    private static Dictionary<ulong, StaffCandidate> SavedHiredAgents(SessionPersistenceSnapshot s) =>
        s.Preparation is not { } p ? [] : HiredAgents(SavedStaffCandidates(s), p.AcceptedOffers, p.StaffProfiles,
            SoundSlotId(p.People.Select(person => (person.AgentId, person.Role))), s.Medical?.MedicId, s.Disorder?.SecurityId);

    /// <summary>Saved walking speed for a medic or steward: the hired candidate's, else the slot's own.</summary>
    private static int SavedResponderSpeed(SessionPersistenceSnapshot s, StaffCandidate[] candidates, ulong id)
    {
        var p = s.Preparation!;
        var role = id == s.Medical?.MedicId ? StaffRole.Medic : id == s.Disorder?.SecurityId ? StaffRole.Steward : (StaffRole?)null;
        if (role is { } main) return StaffCatalogue.Hired(candidates, p.AcceptedOffers, main, false)?.WalkingSpeedPermille ?? GetWalkingSpeedPermille(new(id));
        return p.StaffProfiles.SingleOrDefault(profile => profile.AgentId == id)?.WalkingSpeedPermille ?? GetWalkingSpeedPermille(new(id));
    }
}

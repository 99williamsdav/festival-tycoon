namespace Festival.Simulation;

public sealed partial class GameSession
{
    private readonly PersonRegistry _persons = new();

    /// <summary>The current state of one protected person, or null when unknown.</summary>
    public Person? CapturePerson(ulong id) => _persons.TryGet(id, out var person) ? person : null;

    private void UpdatePerson(ulong id, Func<Person, Person> change) => _persons.Set(change(_persons[id]));
    private readonly Dictionary<PersonView, (long Version, Person[] People)> _peopleInCache = [];

    /// <summary>
    /// A stable snapshot of the people tracked by one system, in that system's order. The array is
    /// shared until the registry changes, so callers must copy it before writing into it.
    /// </summary>
    private Person[] PeopleIn(PersonView view)
    {
        if (_peopleInCache.TryGetValue(view, out var cached) && cached.Version == _persons.Version) return cached.People;
        var people = _persons.Project(view, person => person);
        _peopleInCache[view] = (_persons.Version, people);
        return people;
    }
    private Person? PersonIn(PersonView view, ulong id) => _persons.IsMember(view, id) ? _persons[id] : null;
    private bool InView(PersonView view, ulong id) => _persons.IsMember(view, id);
    /// <summary>Writes only the food, drink, intoxication and toilet state carried by a working copy.</summary>
    private void SetConsumption(Person changed) => UpdatePerson(changed.Id, current => Person.With(current, changed.ToImmersionPerson()));
    /// <summary>Writes only the pressure, grievance and conduct state carried by a working copy.</summary>
    /// <summary>Writes only the presence and experience state carried by a working copy.</summary>
    private void SetPresence(Person changed) => UpdatePerson(changed.Id, current => Person.With(current, changed.ToEditionPerson()));
    private void SetConduct(Person changed) => UpdatePerson(changed.Id, current => Person.With(current, changed.ToDisorderPerson()));

    private (object? Core, long Version, PreparationSnapshot? View) _preparationView;
    private (object? Core, long Version, ImmersionSnapshot? View) _immersionView;
    private (object? Core, long Version, MedicalSnapshot? View) _medicalView;
    private (object? Core, long Version, DisorderSnapshot? View) _disorderView;

    // System state without its people: the per-person arrays in these snapshots are
    // always null here. People live only in _persons.
    private PreparationSnapshot? _preparation;
    private ImmersionSnapshot? _immersion;
    private MedicalSnapshot? _medical;
    private DisorderSnapshot? _disorder;

    // Read models with people projected in, for capture, hashing and persistence.
    // Assigning one replaces that system's people as well as its state.
    private PreparationSnapshot? PreparationView
    {
        get => View(_preparation, ref _preparationView, core => core with
            { People = _persons.Project(PersonView.Roster, person => person.ToEditionPerson()) });
        set
        {
            if (!IsCurrentView(value?.People, _preparationView))
                _persons.Replace(PersonView.Roster, value?.People, item => item.AgentId, Person.With);
            _preparation = value is null ? null : value with { People = null! };
        }
    }

    private ImmersionSnapshot? ImmersionView
    {
        get => View(_immersion, ref _immersionView, core => core with
            { People = _persons.Project(PersonView.Consumption, person => person.ToImmersionPerson()) });
        set
        {
            if (!IsCurrentView(value?.People, _immersionView))
                _persons.Replace(PersonView.Consumption, value?.People, item => item.AgentId, Person.With);
            _immersion = value is null ? null : value with { People = null! };
        }
    }

    private MedicalSnapshot? MedicalView
    {
        get => View(_medical, ref _medicalView, core => core with
            { Needs = _persons.Project(PersonView.Medical, person => person.ToMedicalNeed()) });
        set
        {
            if (!IsCurrentView(value?.Needs, _medicalView))
                _persons.Replace(PersonView.Medical, value?.Needs, item => item.AgentId, Person.With);
            _medical = value is null ? null : value with { Needs = null! };
        }
    }

    private DisorderSnapshot? DisorderView
    {
        get => View(_disorder, ref _disorderView, core => core with
            { People = _persons.Project(PersonView.Disorder, person => person.ToDisorderPerson()) });
        set
        {
            if (!IsCurrentView(value?.People, _disorderView))
                _persons.Replace(PersonView.Disorder, value?.People, item => item.AgentId, Person.With);
            _disorder = value is null ? null : value with { People = null! };
        }
    }

    private T? View<T>(T? core, ref (object? Core, long Version, T? View) cache, Func<T, T> project) where T : class
    {
        if (core is null) return null;
        if (!ReferenceEquals(cache.Core, core) || cache.Version != _persons.Version || cache.View is null)
            cache = (core, _persons.Version, project(core));
        return cache.View;
    }

    // An unchanged array from the current projection carries no new person state.
    private bool IsCurrentView<T>(object? assigned, (object? Core, long Version, T? View) cache) where T : class =>
        assigned is not null && cache.Version == _persons.Version && cache.View switch
        {
            PreparationSnapshot view => ReferenceEquals(assigned, view.People),
            ImmersionSnapshot view => ReferenceEquals(assigned, view.People),
            MedicalSnapshot view => ReferenceEquals(assigned, view.Needs),
            DisorderSnapshot view => ReferenceEquals(assigned, view.People),
            _ => false,
        };
}

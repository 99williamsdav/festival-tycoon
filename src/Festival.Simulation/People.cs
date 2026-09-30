namespace Festival.Simulation;

/// <summary>
/// Everything the simulation knows about one protected person: identity, presence,
/// traits, needs, health, conduct, held item and current activity. This is the only
/// stored copy of person state; the older per-system records (<see cref="EditionPerson"/>,
/// <see cref="ImmersionPerson"/>, <see cref="MedicalNeed"/>, <see cref="DisorderPerson"/>)
/// are read-model projections of it.
/// </summary>
public sealed record Person(ulong Id)
{
    // Identity and presence.
    public string Name { get; init; } = "";
    public ProtectedPersonRole Role { get; init; }
    public int ExpectedGenre { get; init; }
    public bool Admitted { get; init; }
    public bool Departed { get; init; }

    // Experience.
    public int Satisfaction { get; init; } = 5_000;
    public int MusicRisk { get; init; }

    // Stable traits.
    public int OpeningBudgetPennies { get; init; }
    public bool Abstains { get; init; }
    public int BeerTaste { get; init; }
    public int SoftTaste { get; init; }
    public int PriceReluctance { get; init; }
    public int Temperament { get; init; }
    public int QueueToleranceTicks { get; init; }

    // Needs, on the fixed 0–10,000 scale. StaffThirst is the legacy thirst used only
    // by people without a need profile.
    public int Thirst { get; init; }
    public int HeatExposure { get; init; }
    public int Hunger { get; init; }
    public int HungerResidue { get; init; }
    public int StaffThirst { get; init; } = 3_000;
    public int ToiletNeed { get; init; }
    public MedicalNeedProfile NeedProfile { get; init; }

    // Food, drink and intoxication.
    public ImmersionHeldItem? Held { get; init; }
    public int Intoxication { get; init; }
    public int PendingDose { get; init; }
    public int FoodProtectionTicks { get; init; }
    public int RecoveryResidue { get; init; }
    public int AbsorptionResidue { get; init; }
    public long ShoppingDecisionTick { get; init; } = -800;
    public string? VendorId { get; init; }
    public ImmersionProduct? Order { get; init; }
    public long IntoxicationWarningTick { get; init; } = -1;
    public int SevereTicks { get; init; }
    public long IntoxicationCollapseTick { get; init; } = -1;
    public int CareTicks { get; init; }
    public MedicalStage PriorMedicalStage { get; init; }

    // Toilet visits.
    public int ToiletVisits { get; init; }
    public ToiletVisitStage ToiletStage { get; init; }
    public ToiletVisitKind? ToiletChoice { get; init; }
    public string? ToiletId { get; init; }
    public long? LastToiletChoiceReviewTick { get; init; }

    // Water, rest and medical care.
    public MedicalIntent Intent { get; init; }
    public string Reason { get; init; } = "";
    public long NeedDecisionTick { get; init; }
    public int? WaterQueueSlot { get; init; }
    public long LastWaterTick { get; init; }
    public string WaterPointId { get; init; } = "water.main";
    public long LastWaterChoiceReviewTick { get; init; } = -160;
    public MedicalStage HealthStage { get; init; }
    public long HealthWarningTick { get; init; } = -1;
    public long HealthCollapseTick { get; init; } = -1;
    public long HealthCriticalTick { get; init; } = -1;

    // Conduct and disorder.
    public int Pressure { get; init; }
    public DisorderGrievance Grievance { get; init; }
    public DisorderStage ConductStage { get; init; }
    public long GrievanceTick { get; init; }
    public long ConductStageTick { get; init; }
    public long QueueJoinedTick { get; init; }
    public long InjuryTick { get; init; }
    public long CooldownUntilTick { get; init; }
    public ulong? OpponentId { get; init; }

    internal static Person With(Person person, EditionPerson view) => person with
    {
        Name = view.Name, Role = view.Role, ExpectedGenre = view.ExpectedGenre, Admitted = view.Admitted,
        Departed = view.Departed, Satisfaction = view.Satisfaction, MusicRisk = view.MusicRisk,
    };

    internal EditionPerson ToEditionPerson() =>
        new(Id, Name, Role, ExpectedGenre, Admitted, Departed, Satisfaction, MusicRisk);

    internal static Person With(Person person, ImmersionPerson view) => person with
    {
        OpeningBudgetPennies = view.OpeningBudgetPennies, Hunger = view.Hunger, Abstains = view.Abstains,
        BeerTaste = view.BeerTaste, SoftTaste = view.SoftTaste, PriceReluctance = view.PriceReluctance, Held = view.Held,
        Intoxication = view.Intoxication, PendingDose = view.PendingDose, FoodProtectionTicks = view.FoodProtectionTicks,
        ShoppingDecisionTick = view.LastDecisionTick, VendorId = view.VendorId, Order = view.Order,
        IntoxicationWarningTick = view.WarningTick, SevereTicks = view.SevereTicks, HungerResidue = view.HungerResidue,
        RecoveryResidue = view.RecoveryResidue, AbsorptionResidue = view.AbsorptionResidue,
        IntoxicationCollapseTick = view.CollapseTick, CareTicks = view.CareTicks, PriorMedicalStage = view.PriorMedicalStage,
        StaffThirst = view.StaffThirst, ToiletNeed = view.ToiletNeed, ToiletVisits = view.ToiletVisits,
        ToiletStage = view.ToiletStage, ToiletChoice = view.ToiletChoice, ToiletId = view.ToiletId,
        LastToiletChoiceReviewTick = view.LastToiletChoiceReviewTick,
    };

    internal ImmersionPerson ToImmersionPerson() =>
        new(Id, OpeningBudgetPennies, Hunger, Abstains, BeerTaste, SoftTaste, PriceReluctance, Held, Intoxication,
            PendingDose, FoodProtectionTicks, ShoppingDecisionTick, VendorId, Order, IntoxicationWarningTick, SevereTicks)
        {
            HungerResidue = HungerResidue, RecoveryResidue = RecoveryResidue, AbsorptionResidue = AbsorptionResidue,
            CollapseTick = IntoxicationCollapseTick, CareTicks = CareTicks, PriorMedicalStage = PriorMedicalStage,
            StaffThirst = StaffThirst, ToiletNeed = ToiletNeed, ToiletVisits = ToiletVisits, ToiletStage = ToiletStage,
            ToiletChoice = ToiletChoice, ToiletId = ToiletId, LastToiletChoiceReviewTick = LastToiletChoiceReviewTick,
        };

    internal static Person With(Person person, MedicalNeed view) => person with
    {
        Thirst = view.Thirst, HeatExposure = view.HeatExposure, Intent = view.Intent, Reason = view.Reason,
        NeedDecisionTick = view.LastDecisionTick, WaterQueueSlot = view.QueueSlot, LastWaterTick = view.LastWaterTick,
        NeedProfile = view.Profile, HealthStage = view.Stage, HealthWarningTick = view.WarningTick,
        HealthCollapseTick = view.CollapseTick, HealthCriticalTick = view.CriticalTick, WaterPointId = view.WaterPointId,
        LastWaterChoiceReviewTick = view.LastWaterChoiceReviewTick,
    };

    internal MedicalNeed ToMedicalNeed() =>
        new(Id, Thirst, HeatExposure, Intent, Reason, NeedDecisionTick, WaterQueueSlot, LastWaterTick, NeedProfile,
            HealthStage, HealthWarningTick, HealthCollapseTick, HealthCriticalTick)
        {
            WaterPointId = WaterPointId, LastWaterChoiceReviewTick = LastWaterChoiceReviewTick,
        };

    internal static Person With(Person person, DisorderPerson view) => person with
    {
        Temperament = view.Temperament, QueueToleranceTicks = view.QueueToleranceTicks, Pressure = view.Pressure,
        Grievance = view.Grievance, ConductStage = view.Stage, GrievanceTick = view.GrievanceTick,
        ConductStageTick = view.StageTick, QueueJoinedTick = view.QueueJoinedTick, InjuryTick = view.InjuryTick,
        CooldownUntilTick = view.CooldownUntilTick, OpponentId = view.OpponentId,
    };

    internal DisorderPerson ToDisorderPerson() =>
        new(Id, Temperament, QueueToleranceTicks, Pressure, Grievance, ConductStage, GrievanceTick, ConductStageTick,
            QueueJoinedTick, InjuryTick, CooldownUntilTick, OpponentId);
}

/// <summary>
/// Which per-system projections a person currently appears in, in each projection's
/// stable order. Membership is part of authoritative state: older campaign modes track
/// different subsets of the roster in each system.
/// </summary>
internal enum PersonView { Roster, Consumption, Medical, Disorder }

internal sealed class PersonRegistry
{
    private readonly Dictionary<ulong, Person> _people = [];
    private readonly Dictionary<PersonView, List<ulong>> _members = new()
    {
        [PersonView.Roster] = [], [PersonView.Consumption] = [], [PersonView.Medical] = [], [PersonView.Disorder] = [],
    };
    private readonly Dictionary<PersonView, HashSet<ulong>> _memberSets = new()
    {
        [PersonView.Roster] = [], [PersonView.Consumption] = [], [PersonView.Medical] = [], [PersonView.Disorder] = [],
    };

    /// <summary>Increments on every change so projection caches can be reused safely.</summary>
    public long Version { get; private set; }

    public bool Contains(ulong id) => _people.ContainsKey(id);

    public Person this[ulong id] => _people[id];

    public bool TryGet(ulong id, out Person person) => _people.TryGetValue(id, out person!);

    public bool IsMember(PersonView view, ulong id) => _memberSets[view].Contains(id);

    public IReadOnlyList<ulong> Members(PersonView view) => _members[view];

    public int Count(PersonView view) => _members[view].Count;

    /// <summary>The current state of the person at one position in a view's order.</summary>
    public Person At(PersonView view, int index) => _people[_members[view][index]];

    public void Set(Person person)
    {
        if (!_people.ContainsKey(person.Id)) throw new InvalidOperationException($"Unknown person {person.Id}.");
        _people[person.Id] = person;
        Version++;
    }

    /// <summary>Replaces one projection's membership and the fields it owns, as an array assignment did before.</summary>
    public void Replace<T>(PersonView view, IReadOnlyList<T>? records, Func<T, ulong> id, Func<Person, T, Person> apply)
    {
        var members = _members[view];
        members.Clear();
        _memberSets[view].Clear();
        if (records is not null)
            foreach (var record in records)
            {
                var key = id(record);
                var person = _people.TryGetValue(key, out var existing) ? existing : new Person(key);
                _people[key] = apply(person, record);
                members.Add(key);
                _memberSets[view].Add(key);
            }
        foreach (var orphan in _people.Keys.Where(key => _memberSets.Values.All(set => !set.Contains(key))).ToArray())
            _people.Remove(orphan);
        Version++;
    }

    public T[] Project<T>(PersonView view, Func<Person, T> project) =>
        _members[view].Select(key => project(_people[key])).ToArray();
}

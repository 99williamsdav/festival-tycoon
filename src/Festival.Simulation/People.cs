namespace Festival.Simulation;

/// <summary>
/// Everything the simulation knows about one protected person: identity, presence,
/// traits, needs, health, conduct, held item and current activity. This is the only
/// stored copy of person state; the older per-system records (<see cref="EditionPerson"/>,
/// <see cref="ImmersionPerson"/>, <see cref="MedicalNeed"/>, <see cref="DisorderPerson"/>)
/// are read-model projections of it.
/// <para>
/// The registry holds one mutable instance per person and systems update it in place, so a
/// reference always shows current state. Code that needs a person's state as it was before an
/// update takes an explicit copy (<c>person with { }</c>). Outside the simulation only copies
/// are handed out (<see cref="GameSession.CapturePerson"/>), so the public setters cannot
/// reach live state.
/// </para>
/// </summary>
public sealed record Person(ulong Id)
{
    // Identity and presence.
    public string Name { get; set; } = "";
    public ProtectedPersonRole Role { get; set; }
    public int ExpectedGenre { get; set; }
    public bool Admitted { get; set; }
    public bool Departed { get; set; }

    // Experience.
    public int Satisfaction { get; set; } = 5_000;
    public int MusicRisk { get; set; }

    // Stable traits.
    public int OpeningBudgetPennies { get; set; }
    public bool Abstains { get; set; }
    public int BeerTaste { get; set; }
    public int SoftTaste { get; set; }
    public int PriceReluctance { get; set; }
    public int Temperament { get; set; }
    public int QueueToleranceTicks { get; set; }

    // Needs, on the fixed 0–10,000 scale. StaffThirst is the legacy thirst used only
    // by people without a need profile.
    public int Thirst { get; set; }
    public int HeatExposure { get; set; }
    public int Hunger { get; set; }
    public int HungerResidue { get; set; }
    public int StaffThirst { get; set; } = 3_000;
    public int ToiletNeed { get; set; }
    public MedicalNeedProfile NeedProfile { get; set; }

    // Food, drink and intoxication.
    public ImmersionHeldItem? Held { get; set; }
    public int Intoxication { get; set; }
    public int PendingDose { get; set; }
    public int FoodProtectionTicks { get; set; }
    public int RecoveryResidue { get; set; }
    public int AbsorptionResidue { get; set; }
    public long ShoppingDecisionTick { get; set; } = -800;
    public string? VendorId { get; set; }
    public ImmersionProduct? Order { get; set; }
    public long IntoxicationWarningTick { get; set; } = -1;
    public int SevereTicks { get; set; }
    public long IntoxicationCollapseTick { get; set; } = -1;
    public int CareTicks { get; set; }
    public MedicalStage PriorMedicalStage { get; set; }

    // Toilet visits.
    public int ToiletVisits { get; set; }
    public ToiletVisitStage ToiletStage { get; set; }
    public ToiletVisitKind? ToiletChoice { get; set; }
    public string? ToiletId { get; set; }
    public long? LastToiletChoiceReviewTick { get; set; }

    // Water, rest and medical care.
    public MedicalIntent Intent { get; set; }
    public string Reason { get; set; } = "";
    public long NeedDecisionTick { get; set; }
    public int? WaterQueueSlot { get; set; }
    public long LastWaterTick { get; set; }
    public string WaterPointId { get; set; } = "water.main";
    public long LastWaterChoiceReviewTick { get; set; } = -160;
    public MedicalStage HealthStage { get; set; }
    public long HealthWarningTick { get; set; } = -1;
    public long HealthCollapseTick { get; set; } = -1;
    public long HealthCriticalTick { get; set; } = -1;

    // Conduct and disorder.
    public int Pressure { get; set; }
    public DisorderGrievance Grievance { get; set; }
    public DisorderStage ConductStage { get; set; }
    public long GrievanceTick { get; set; }
    public long ConductStageTick { get; set; }
    public long QueueJoinedTick { get; set; }
    public long InjuryTick { get; set; }
    public long CooldownUntilTick { get; set; }
    public ulong? OpponentId { get; set; }

        /// <summary>Copies only the presence fields from a working copy, as <see cref="With(Person, EditionPerson)"/> does.</summary>
    internal void CopyPresenceFrom(Person other)
    {
        Name = other.Name; Role = other.Role; ExpectedGenre = other.ExpectedGenre; Admitted = other.Admitted;
        Departed = other.Departed; Satisfaction = other.Satisfaction; MusicRisk = other.MusicRisk;
    }

    /// <summary>Copies only the consumption fields from a working copy, as <see cref="With(Person, ImmersionPerson)"/> does.</summary>
    internal void CopyConsumptionFrom(Person other)
    {
        OpeningBudgetPennies = other.OpeningBudgetPennies; Hunger = other.Hunger; Abstains = other.Abstains; BeerTaste = other.BeerTaste;
        SoftTaste = other.SoftTaste; PriceReluctance = other.PriceReluctance; Held = other.Held; Intoxication = other.Intoxication;
        PendingDose = other.PendingDose; FoodProtectionTicks = other.FoodProtectionTicks; ShoppingDecisionTick = other.ShoppingDecisionTick; VendorId = other.VendorId;
        Order = other.Order; IntoxicationWarningTick = other.IntoxicationWarningTick; SevereTicks = other.SevereTicks; HungerResidue = other.HungerResidue;
        RecoveryResidue = other.RecoveryResidue; AbsorptionResidue = other.AbsorptionResidue; IntoxicationCollapseTick = other.IntoxicationCollapseTick; CareTicks = other.CareTicks;
        PriorMedicalStage = other.PriorMedicalStage; StaffThirst = other.StaffThirst; ToiletNeed = other.ToiletNeed; ToiletVisits = other.ToiletVisits;
        ToiletStage = other.ToiletStage; ToiletChoice = other.ToiletChoice; ToiletId = other.ToiletId; LastToiletChoiceReviewTick = other.LastToiletChoiceReviewTick;
    }

    /// <summary>Copies only the conduct fields from a working copy, as <see cref="With(Person, DisorderPerson)"/> does.</summary>
    internal void CopyConductFrom(Person other)
    {
        Temperament = other.Temperament; QueueToleranceTicks = other.QueueToleranceTicks; Pressure = other.Pressure; Grievance = other.Grievance;
        ConductStage = other.ConductStage; GrievanceTick = other.GrievanceTick; ConductStageTick = other.ConductStageTick; QueueJoinedTick = other.QueueJoinedTick;
        InjuryTick = other.InjuryTick; CooldownUntilTick = other.CooldownUntilTick; OpponentId = other.OpponentId;
    }

    /// <summary>Overwrites every field with another copy of the same person.</summary>
    internal void CopyFrom(Person other)
    {
        Name = other.Name; Role = other.Role; ExpectedGenre = other.ExpectedGenre; Admitted = other.Admitted;
        Departed = other.Departed; Satisfaction = other.Satisfaction; MusicRisk = other.MusicRisk; OpeningBudgetPennies = other.OpeningBudgetPennies;
        Abstains = other.Abstains; BeerTaste = other.BeerTaste; SoftTaste = other.SoftTaste; PriceReluctance = other.PriceReluctance;
        Temperament = other.Temperament; QueueToleranceTicks = other.QueueToleranceTicks; Thirst = other.Thirst; HeatExposure = other.HeatExposure;
        Hunger = other.Hunger; HungerResidue = other.HungerResidue; StaffThirst = other.StaffThirst; ToiletNeed = other.ToiletNeed;
        NeedProfile = other.NeedProfile; Held = other.Held; Intoxication = other.Intoxication; PendingDose = other.PendingDose;
        FoodProtectionTicks = other.FoodProtectionTicks; RecoveryResidue = other.RecoveryResidue; AbsorptionResidue = other.AbsorptionResidue; ShoppingDecisionTick = other.ShoppingDecisionTick;
        VendorId = other.VendorId; Order = other.Order; IntoxicationWarningTick = other.IntoxicationWarningTick; SevereTicks = other.SevereTicks;
        IntoxicationCollapseTick = other.IntoxicationCollapseTick; CareTicks = other.CareTicks; PriorMedicalStage = other.PriorMedicalStage; ToiletVisits = other.ToiletVisits;
        ToiletStage = other.ToiletStage; ToiletChoice = other.ToiletChoice; ToiletId = other.ToiletId; LastToiletChoiceReviewTick = other.LastToiletChoiceReviewTick;
        Intent = other.Intent; Reason = other.Reason; NeedDecisionTick = other.NeedDecisionTick; WaterQueueSlot = other.WaterQueueSlot;
        LastWaterTick = other.LastWaterTick; WaterPointId = other.WaterPointId; LastWaterChoiceReviewTick = other.LastWaterChoiceReviewTick; HealthStage = other.HealthStage;
        HealthWarningTick = other.HealthWarningTick; HealthCollapseTick = other.HealthCollapseTick; HealthCriticalTick = other.HealthCriticalTick; Pressure = other.Pressure;
        Grievance = other.Grievance; ConductStage = other.ConductStage; GrievanceTick = other.GrievanceTick; ConductStageTick = other.ConductStageTick;
        QueueJoinedTick = other.QueueJoinedTick; InjuryTick = other.InjuryTick; CooldownUntilTick = other.CooldownUntilTick; OpponentId = other.OpponentId;
    }

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

    /// <summary>Increments only when a view's membership or order changes.</summary>
    public long MembershipVersion { get; private set; }

    public bool Contains(ulong id) => _people.ContainsKey(id);

    public Person this[ulong id] => _people[id];

    public bool TryGet(ulong id, out Person person) => _people.TryGetValue(id, out person!);

    public bool IsMember(PersonView view, ulong id) => _memberSets[view].Contains(id);

    public IReadOnlyList<ulong> Members(PersonView view) => _members[view];

    public int Count(PersonView view) => _members[view].Count;

    /// <summary>The current state of the person at one position in a view's order.</summary>
    public Person At(PersonView view, int index) => _people[_members[view][index]];

    /// <summary>Changes one person in place. Anyone holding this person sees the change.</summary>
    public void Mutate(ulong id, Action<Person> change)
    {
        change(_people[id]);
        Version++;
    }

    /// <summary>Writes a changed copy back into the person's one stable instance.</summary>
    public void Set(Person person)
    {
        if (!_people.TryGetValue(person.Id, out var live)) throw new InvalidOperationException($"Unknown person {person.Id}.");
        if (!ReferenceEquals(live, person)) live.CopyFrom(person);
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
                if (_people.TryGetValue(key, out var existing)) existing.CopyFrom(apply(existing, record));
                else _people[key] = apply(new Person(key), record);
                members.Add(key);
                _memberSets[view].Add(key);
            }
        foreach (var orphan in _people.Keys.Where(key => _memberSets.Values.All(set => !set.Contains(key))).ToArray())
            _people.Remove(orphan);
        Version++;
        MembershipVersion++;
    }

    public T[] Project<T>(PersonView view, Func<Person, T> project) =>
        _members[view].Select(key => project(_people[key])).ToArray();
}

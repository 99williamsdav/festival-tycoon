namespace Festival.Simulation;

/// <summary>
/// The hidden scores and conditions that make each guest a person. Derived from the campaign seed and the
/// guest's id like their opening needs, so nothing new is saved. Scores run 0-100.
/// </summary>
/// <param name="HeatSensitivity">How much faster than usual heat builds: up to half as fast again. Skewed low.</param>
/// <param name="Prissiness">How much more (or less) the unpleasant things cost them: half to one and a half times.</param>
/// <param name="Lightweight">Drinks like anyone else but gets drunk twice as fast on the same beer.</param>
public sealed record GuestCharacter(int HeatSensitivity, int Prissiness, bool WaspAllergy, bool Ibs, bool SlowDrinker, bool Lightweight);

public enum CollapseCause { Heat, Drink, WaspSting, Injury, ToiletFumes }

public static class GuestCharacters
{
    public const int WaspAllergyPercent = 5, IbsPercent = 6, SlowDrinkerPercent = 10, LightweightPercent = 8;
    /// <summary>How many times faster a lightweight's beer goes to their head.</summary>
    public const int LightweightAbsorption = 2;
    public const int SlowDrinkerThirstPerTick = 5;
    /// <summary>Chance each second near a wasp nest that an allergic guest is stung, in percent.</summary>
    public const int StingChancePercent = 10;
    public const int MaximumLabels = 3;

    private static ulong Roll(ulong seed, ulong id, ulong salt)
    {
        var value = unchecked(seed ^ salt) + id * 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    public static GuestCharacter For(ulong seed, ulong id)
    {
        var heat = (int)(Roll(seed, id, 0x48454154UL) % 101);
        return new(heat * heat / 100, (int)(Roll(seed, id, 0x5052495353UL) % 101),
            Roll(seed, id, 0x57415350UL) % 100 < WaspAllergyPercent,
            Roll(seed, id, 0x494253UL) % 100 < IbsPercent,
            Roll(seed, id, 0x534950UL) % 100 < SlowDrinkerPercent,
            Roll(seed, id, 0x4C49474854UL) % 100 < LightweightPercent);
    }

    /// <summary>A loss of satisfaction scaled by how prissy they are: half for the easy-going, half as much again for the fussiest.</summary>
    public static int Unpleasant(int loss, int prissiness) => loss <= 0 ? loss : Math.Max(1, (loss * (50 + prissiness) + 50) / 100);

    // Ordinary first names and slightly characterful surnames: plainer than the staff market, never shared with it.
    private static readonly string[] FirstNames = ["Sarah", "Dan", "Priya", "Tom", "Megan", "Callum", "Aisha", "Josh", "Hannah", "Liam",
        "Chloe", "Ollie", "Zoe", "Ryan", "Niamh", "Jess", "Kieran", "Leah", "Ben", "Amira", "Rhys", "Freya", "Toby", "Imogen",
        "Declan", "Holly", "Ravi", "Ellie", "Connor", "Maisie", "Jamal", "Poppy", "Nathan", "Rosie", "Harvey", "Isla", "Owen", "Tasha"];
    private static readonly string[] LastNames = ["Butterworth", "Hargreaves", "Pennington", "Blackwood", "Fairbrother", "Copeland", "Whitlock",
        "Dunmore", "Pettigrew", "Ashdown", "Holloway", "Crabtree", "Openshaw", "Lockwood", "Gristwood", "Fenwick", "Haddock", "Swindells",
        "Bramley", "Thorne", "Cartwright", "Moorcroft", "Pickles", "Higginbottom", "Sowerby", "Lightfoot", "Westwood", "Clutterbuck"];

    /// <summary>A name for each guest slot this edition: unique first names and surnames, from this campaign's seed.</summary>
    public static string[] Names(ulong seed, int count)
    {
        var random = RandomStreamFactory.Create(seed ^ 0x4755455354UL, RandomStreamId.IndividualBehaviour);
        var firsts = new HashSet<string>(); var lasts = new HashSet<string>(); var names = new string[count];
        for (var i = 0; i < count; i++)
        {
            while (true)
            {
                var first = FirstNames[random.NextUInt32() % (uint)FirstNames.Length];
                var last = LastNames[random.NextUInt32() % (uint)LastNames.Length];
                // Plenty of both for a Tier 2 crowd; repeats only once a list runs out.
                if (firsts.Count < FirstNames.Length && firsts.Contains(first) || lasts.Count < LastNames.Length && lasts.Contains(last)) continue;
                if (names.Take(i).Contains($"{first} {last}")) continue;
                firsts.Add(first); lasts.Add(last); names[i] = $"{first} {last}";
                break;
            }
        }
        return names;
    }
}

public sealed partial class GameSession
{
    private bool IsGuest(ulong id) => PersonIn(PersonView.Roster, id)?.Role == ProtectedPersonRole.Guest;
    public GuestCharacter GuestCharacterOf(ulong id) => GuestCharacters.For(CampaignSeed, id);

    /// <summary>Satisfaction loss from something unpleasant, scaled for a guest by how prissy they are.</summary>
    private int UnpleasantFor(ulong id, int loss) => IsGuest(id) ? GuestCharacters.Unpleasant(loss, GuestCharacterOf(id).Prissiness) : loss;

    /// <summary>Whether a sensitive guest gains an extra point of heat on this gain tick: a share of HeatSensitivity/200.</summary>
    private bool ExtraHeatThisTick(ulong id)
    {
        if (!IsGuest(id)) return false;
        var sensitivity = GuestCharacterOf(id).HeatSensitivity;
        return sensitivity > 0 && (CurrentTick / 4 * sensitivity) % 200 < sensitivity;
    }

    private bool HasIbs(ulong id) => IsGuest(id) && GuestCharacterOf(id).Ibs;
    private int AbsorptionFactor(ulong id) => IsGuest(id) && GuestCharacterOf(id).Lightweight ? GuestCharacters.LightweightAbsorption : 1;

    /// <summary>
    /// The labels a guest's inspector shows: their conditions, then any score far enough from the middle to
    /// stand out. Most guests show none.
    /// </summary>
    public IReadOnlyList<string> GuestLabels(ulong id)
    {
        if (PersonIn(PersonView.Roster, id) is not { Role: ProtectedPersonRole.Guest }) return [];
        var person = _persons[id];
        var c = GuestCharacterOf(id);
        var dickishness = LitterRules.Dickishness(CampaignSeed, id);
        var labels = new List<string>();
        if (c.WaspAllergy) labels.Add("Allergic to wasps");
        if (c.Ibs) labels.Add("Irritably-boweled");
        if (c.Lightweight && InView(PersonView.Consumption, id) && !Teetotal(person)) labels.Add("Lightweight");
        if (dickishness >= 85 && InView(PersonView.Disorder, id) && person.QueueToleranceTicks <= 600) labels.Add("Twat");
        if (dickishness <= LitterRules.GoodyTwoShoesMaximum) labels.Add("Goody two-shoes");
        if (c.Prissiness >= 90) labels.Add("Princess");
        else if (c.Prissiness <= 30 && person.ExpectedGenre == (int)FestivalGenre.Folk) labels.Add("Hippie"); // Unfussy and here for the folk.
        if (c.HeatSensitivity >= 80) labels.Add("Easy to overheat");
        if (InView(PersonView.Consumption, id) && !Teetotal(person) && person.BeerTaste >= 92) labels.Add("Alcoholic");
        if (InView(PersonView.Consumption, id) && person.OpeningBudgetPennies >= 2_300) labels.Add("Rich");
        if (c.SlowDrinker) labels.Add("Slow drinker");
        return labels.Take(GuestCharacters.MaximumLabels).ToArray();
    }

    /// <summary>
    /// Whether this collapse is a sting. Reason text is overwritten once a medic is sent, so this reads the stable
    /// signature instead: only a sting collapses an allergic guest with no warning (warning tick equal to collapse tick).
    /// </summary>
    private bool StungByWasp(ulong id) => IsGuest(id) && GuestCharacterOf(id).WaspAllergy && _persons[id] is { HealthCollapseTick: >= 0 } p &&
        p.HealthWarningTick == p.HealthCollapseTick && !PoisonedByFumes(id); // Fumes collapse without warning too.

    /// <summary>
    /// Why someone collapsed, from signals that outlast the medic's dispatch overwriting their Reason text: a sting
    /// leaves no warning before the collapse, drink leaves an intoxication collapse tick, a fight leaves an injury,
    /// and anything else is heat.
    /// </summary>
    public CollapseCause CollapseCauseOf(ulong id) =>
        StungByWasp(id) ? CollapseCause.WaspSting :
        PoisonedByFumes(id) ? CollapseCause.ToiletFumes :
        PersonIn(PersonView.Disorder, id)?.ConductStage == DisorderStage.Injured ? CollapseCause.Injury :
        _persons[id].IntoxicationCollapseTick >= 0 ? CollapseCause.Drink : CollapseCause.Heat;

    /// <summary>An allergic guest by a wasp-filled bin can be stung: a collapse that needs the medic like any other.</summary>
    private void MaybeWaspSting(ulong id)
    {
        if (_preparation?.Status != PreparationStatus.Running || !IsGuest(id) || !GuestCharacterOf(id).WaspAllergy || StuckInToilet(id) ||
            _persons[id].HealthStage is not (MedicalStage.Clear or MedicalStage.Distress or MedicalStage.Treated)) return;
        if (!FaultRules.Roll(CampaignSeed, "wasp-sting", CurrentTick, id, GuestCharacters.StingChancePercent * 100)) return;
        LeaveWater(id, "Stung by a wasp", reroute: false); LeaveImmersionQueue(id, false);
        var nav = _navigationAgents[new(id)];
        ApplyAgentDestination(new(id), new(TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres), "medical.collapsed"));
        MutatePerson(id, item => { item.HealthStage = MedicalStage.Collapsed; item.HealthWarningTick = CurrentTick; item.HealthCollapseTick = CurrentTick;
            item.Intent = MedicalIntent.Collapsed; item.Reason = "Anaphylaxis from a wasp sting; needs physical medic response"; item.WaterQueueSlot = null; });
        MedicalEvent("medical:collapse", $"{_persons[id].Name} ({id}) collapsed after a wasp sting: allergic reaction.");
        RecordGuestMedicalCollapse(id);
    }
}

namespace Festival.Simulation;

/// <summary>How a set is going: the band (its talent, less any drink on stage) and the sound reaching the crowd.</summary>
/// <param name="DrunkName">The drunkest band member, when drink is costing the set.</param>
public sealed record SetPerformance(FestivalAct Act, int Talent, int Drunkenness, int Band, int Sound, int Overall, string? DrunkName = null)
{
    public string BandWord => PerformanceRules.BandWord(Band);
    public string SoundWord => PerformanceRules.SoundWord(Sound);
}

/// <summary>
/// Band talent and sound quality, kept apart so the player can tell which is letting a set down. Each act has a talent
/// for playing live, loosely following its fame but not always: some famous acts are sloppy and some unknowns brilliant.
/// It is hidden until the act has played for you. Sound comes from the hired rig and the engineer's mixing, dulled by
/// their drinking and a straining generator. Band and sound together scale what every second of a set gives its crowd.
/// </summary>
public static class PerformanceRules
{
    public const int BasicRigSound = 40, StandardRigSound = 65, ProRigSound = 85, MixingStep = 5;
    public const int StrainedSoundPenalty = 15, FaultSoundPenalty = 30;
    /// <summary>Drink on stage: a performer above 2,000 intoxication loses a point of the band's play per 60 more.</summary>
    public const int SoberLimit = 2_000, DrunkStep = 60;
    /// <summary>What a set gives its listeners, per mille: 450 for a disaster, 750 at middling, 1,050 for a triumph.</summary>
    public static int MusicPermille(int overall) => 450 + Math.Clamp(overall, 0, 100) * 6;
    /// <summary>Overall play weighs the band a little over the sound.</summary>
    public static int Overall(int band, int sound) => (band * 3 + sound * 2) / 5;

    public static int RigSound(SoundRig rig) => rig switch { SoundRig.Pro => ProRigSound, SoundRig.Standard => StandardRigSound, _ => BasicRigSound };
    public static string SoundWord(int sound) => sound switch { < 40 => "Poor", < 60 => "Decent", < 80 => "Good", _ => "Great" };
    public static string BandWord(int band) => band switch { < 35 => "Ragged", < 55 => "Shaky", < 75 => "Solid", _ => "Tight" };

    /// <summary>
    /// An act's talent: its popularity plus a swing of up to 35 either way fixed by its id, so fame is a fair guide but
    /// not a promise. Popularity is the campaign's (catalogue popularity moved by the newcomers its sets won over), so
    /// talent moves point for point with it.
    /// </summary>
    public static int Talent(FestivalAct act)
    {
        var hash = 2166136261u;
        foreach (var ch in act.Id) hash = (hash ^ ch) * 16777619u;
        return Math.Clamp(act.Popularity + (int)(hash % 71) - 35, 5, 98);
    }
}

public sealed partial class GameSession
{
    /// <summary>Acts that have played for you, whose talent you now know.</summary>
    public IReadOnlyList<string> SeenActs => _preparation?.SeenActs ?? [];
    public bool TalentKnown(FestivalAct act) => SeenActs.Contains(act.Id);

    /// <summary>The sound reaching the crowd now: the rig and the engineer, less their drinking and a straining generator.</summary>
    public int SoundScore => Math.Clamp(PerformanceRules.RigSound(Rig) + SoundMixingBonus() * PerformanceRules.MixingStep -
        (_equipment?.Stage switch { EquipmentStage.Warning => PerformanceRules.StrainedSoundPenalty, EquipmentStage.DangerousFault => PerformanceRules.FaultSoundPenalty, _ => 0 }), 0, 100);

    /// <summary>The playing act's band members' drunkenness: the worst of them sets the tone.</summary>
    private int StageDrunkenness(int stage) => StageDrunkest(stage).Intoxication;

    /// <summary>The playing act's drunkest band member (the first in line on a tie) and how drunk; nobody between sets.</summary>
    private (ulong? Id, int Intoxication) StageDrunkest(int stage) => StageProgramme(stage) is not { CurrentSlot: >= 0 } q ? (null, 0) :
        q.Performers.Where(p => p.SlotIndex == q.CurrentSlot).Select(p => ((ulong?)p.AgentId, PersonIn(PersonView.Consumption, p.AgentId)?.Intoxication ?? 0))
            .Aggregate(((ulong?)null, 0), (worst, next) => next.Item2 > worst.Item2 ? next : worst);

    /// <summary>How the main stage's set is going, or nothing between sets.</summary>
    public SetPerformance? CurrentPerformance => StagePerformance(0);

    /// <summary>How a stage's set is going, or nothing between sets.</summary>
    private SetPerformance? StagePerformance(int stage)
    {
        if (StageAct(stage) is not { } act || _livePerformances[stage]?.Stage is not (LiveSetStage.Live or LiveSetStage.Interrupted)) return null;
        var talent = PerformanceRules.Talent(act);
        var drunkest = StageDrunkest(stage);
        var drunk = Math.Max(0, drunkest.Intoxication - PerformanceRules.SoberLimit) / PerformanceRules.DrunkStep;
        var band = Math.Clamp(talent - drunk, 0, 100);
        var sound = SoundScoreAt(stage);
        return new(act, talent, drunk, band, sound, PerformanceRules.Overall(band, sound),
            drunk > 0 && drunkest.Id is { } id ? _persons[id].Name : null);
    }

    /// <summary>The act a performer plays in, for how professionally they treat the bar.</summary>
    private FestivalAct? PerformerAct(ulong id) => PerformerStage(id) is var stage and >= 0 && StageProgramme(stage) is { } q &&
        q.ActIds.Length == Stages[stage].SlotCount && q.Performers.FirstOrDefault(p => p.AgentId == id) is { } member
        ? FestivalActs.SingleOrDefault(a => a.Id == q.ActIds[member.SlotIndex]) : null;

    /// <summary>A performer's thirst for beer: their own taste, tempered by how professional their act is.</summary>
    private int PerformerBeerTaste(Person person) =>
        PerformerAct(person.Id) is { } act ? Math.Clamp(person.BeerTaste * (140 - act.Professionalism) / 100, 0, 100) : person.BeerTaste;
}

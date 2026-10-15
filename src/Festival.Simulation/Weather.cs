using System.Text.Json;

namespace Festival.Simulation;

/// <summary>The weather a festival day is set in: Tier 1's heatwave, or Tier 2's heavy rain.</summary>
public enum WeatherKind { Heatwave, Rain, Mild }

/// <summary>How hard it's raining: B, heavy showers, is the norm; C, a downpour, is rare.</summary>
public enum RainIntensity { Dry, Heavy, Downpour, Showers }

/// <summary>One shower, in ticks from opening.</summary>
public sealed record WeatherSpell(long StartTick, long EndTick, RainIntensity Intensity);

/// <summary>
/// A rainy festival's weather: the day's showers (worked out from the seed and tier, so a save can be checked against
/// them), how wet each person is, the straw bales left in the store, and any steward spreading straw.
/// Wet people are listed in ascending id order with their wetness, 1 to 10,000.
/// </summary>
public sealed record WeatherSnapshot(int Version, WeatherKind Kind, WeatherSpell[] Spells, ulong[] WetIds, int[] Wetness)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int StrawBales { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ClassicForecast? ClassicForecast { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public StrawJob? StrawJob { get; init; }
}

/// <summary>The weather as the top bar and forecast show it.</summary>
/// <param name="ChangeTick">When it next changes: the end of this shower, or the start of the next one; -1 when it won't today.</param>
public sealed record WeatherOutlook(WeatherKind Kind, RainIntensity Now, long ChangeTick, RainIntensity Next, bool Downpour, int Showers);

public static class WeatherRules
{
    /// <summary>The weather a survival tier is set in. Freeplay will pick one from its own seeded forecast.</summary>
    public static WeatherKind ForTier(int tier) => CampaignRulesProfile.For(CampaignRuleset.Survival).WeatherForTier(tier);

    // Ticks at 80 a second; a festival minute is 80 ticks, as on the HUD clock.
    public const int FestivalMinuteTicks = 80;
    /// <summary>The first shower lands some time in the first set.</summary>
    public const int FirstShowerFromTicks = 3_600, FirstShowerToTicks = 8_000;
    public const int ShowerFromTicks = 2_000, ShowerToTicks = 4_400;
    public const int DrySpellFromTicks = 1_600, DrySpellToTicks = 3_600;
    /// <summary>A downpour is a rare treat: about one afternoon in four, 6 to 10 festival minutes long.</summary>
    public const int DownpourOneIn = 4;
    public const int DownpourFromTicks = 6 * FestivalMinuteTicks, DownpourToTicks = 10 * FestivalMinuteTicks;

    /// <summary>Wetness a person out in it gains each second, of 10,000.</summary>
    public static int SoakPerSecond(RainIntensity rain) => rain switch { RainIntensity.Showers => 100, RainIntensity.Heavy => 250, RainIntensity.Downpour => 500, _ => 0 };
    /// <summary>Drying each second: slowly under cover while it rains, quicker once it stops.</summary>
    public const int DryUnderCoverPerSecond = 50, DryInTheOpenPerSecond = 100;
    /// <summary>Wetness from which a guest looks soaked.</summary>
    public const int SoakedFrom = 4_000;
    /// <summary>Satisfaction a guest loses each second out in the rain, before prissiness.</summary>
    public static int RainLossPerSecond(RainIntensity rain) => rain switch { RainIntensity.Showers => 1, RainIntensity.Heavy => 2, RainIntensity.Downpour => 5, _ => 0 };
    /// <summary>And for being wet through, in or out of it: up to 2 more a second.</summary>
    public const int WetLossStep = 5_000;
    public static int WetLossPerSecond(int wetness) => wetness / WetLossStep;

    /// <summary>
    /// Thirst comes on slower in the rain: of every five thirst steps a dry day would take, a wet one takes three.
    /// </summary>
    public const int RainThirstSkipOf5 = 2;

    /// <summary>The chooser's discomfort per second for being this wet, and for standing out in it.</summary>
    /// <remarks>
    /// Set against the music a guest wants (2,000 a second with nothing on, about 3,000–5,600 for an act they're lukewarm
    /// about, 5,200–6,800 for a fan's) and the music lost walking to cover and back: the lukewarm leave a heavy shower for
    /// cover once it's set in, and nearly everyone leaves a downpour, except fans, who feel a quarter of it
    /// (<see cref="FanKeenness"/>). Being wet hurts more the wetter they are. A poncho wearer never feels it.
    /// </remarks>
    public static long WetDiscomfort(int wetness) => (long)wetness * wetness / 40_000;
    public static long ExposedDiscomfort(RainIntensity rain) => rain switch { RainIntensity.Showers => 2_000, RainIntensity.Heavy => 6_000, RainIntensity.Downpour => 14_000, _ => 0 };
    /// <summary>Keenness on an act on now (0–100) from which a guest is a fan and puts up with the rain.</summary>
    public const int FanKeenness = 80;

    /// <summary>A poncho: sold at the bars, worn all day. Priced like a pint, cheap to stock.</summary>
    public const int PonchoPricePennies = 300, PonchoCostPennies = 50;
    /// <summary>What a poncho is worth to a guest for each second of forecast rain it would keep off them, in the chooser's terms.</summary>
    public const long PonchoValuePerRainSecond = 450;
    /// <summary>The most rain a guest looks ahead to when weighing a poncho.</summary>
    public const int PonchoLookAheadTicks = 19_200;
    /// <summary>A shower due within this long counts as rain coming, for a poncho.</summary>
    public const int PonchoForecastTicks = 4_800;

    /// <summary>
    /// The ground: rain each ground step on open grass, and what soaks away. Grass drinks a heavy shower; trampled
    /// ground, and the odd hollow, can't, so puddles form where people walk.
    /// </summary>
    public static int RainOnGround(RainIntensity rain) => rain switch { RainIntensity.Showers => 1, RainIntensity.Heavy => 3, RainIntensity.Downpour => 6, _ => 0 };
    public const int GrassDrain = 4;
    /// <summary>Wear past which the ground is trampled enough to churn under feet while it rains.</summary>
    public const int TrampledWear = 60;
    /// <summary>A crowd standing still churns it a quarter as fast as feet on the move.</summary>
    public const int StandingChurnEverySteps = 4;
}

public sealed partial class GameSession
{
    private WeatherSnapshot? _weather;
    // Wetness by person while the day runs; saved sparsely in the weather snapshot.
    private readonly SortedDictionary<ulong, int> _wetness = [];
    private (long Version, string Json)? _weatherCapture;
    private long _weatherVersion;

    /// <summary>The kind of weather this festival is in: rain from Tier 2, the heatwave otherwise.</summary>
    public WeatherKind Weather => _weather?.Kind ?? WeatherKind.Heatwave;
    public bool IsRainyFestival => _weather is { Kind: WeatherKind.Rain };

    public WeatherSnapshot? CaptureWeather()
    {
        if (_weather is not { } w) return null;
        return w with { ClassicForecast=CaptureClassicForecast(), Spells = w.Spells.ToArray(), WetIds = _wetness.Keys.ToArray(), Wetness = _wetness.Values.ToArray() };
    }

    internal string? WeatherCanonicalJson
    {
        get
        {
            if (_weather is null) return null;
            if (_weatherCapture is { } cached && cached.Version == _weatherVersion) return cached.Json;
            var json = JsonSerializer.Serialize(CaptureWeather());
            _weatherCapture = (_weatherVersion, json);
            return json;
        }
    }

    private void SetWeather(WeatherSnapshot weather) { _weather = weather; _weatherVersion++; }

    /// <summary>
    /// The day's showers, from the seed and tier alone: heavy showers with short dry spells from the first set to closing,
    /// and on about one day in four, one of them a downpour.
    /// </summary>
    public static WeatherSpell[] RainForecast(ulong seed, int tier, int dayTicks)
    {
        var random = RandomStreamFactory.Create(TierSeed(seed, tier), RandomStreamId.Weather);
        int Between(int from, int to) => from + (int)(random.NextUInt32() % (uint)(to - from + 1));
        var spells = new List<WeatherSpell>();
        var end = dayTicks - ClosingAfterLastSetTicks;
        long at = Between(WeatherRules.FirstShowerFromTicks, WeatherRules.FirstShowerToTicks);
        while (at < end - WeatherRules.ShowerFromTicks / 2)
        {
            var length = Between(WeatherRules.ShowerFromTicks, WeatherRules.ShowerToTicks);
            spells.Add(new(at, Math.Min(end, at + length), RainIntensity.Heavy));
            at += length + Between(WeatherRules.DrySpellFromTicks, WeatherRules.DrySpellToTicks);
        }
        // The downpour is drawn last, so it never moves the showers around it: a burst in the middle of a heavy shower,
        // which keeps its full length either side.
        if (spells.Count > 1 && random.NextUInt32() % WeatherRules.DownpourOneIn == 0)
        {
            var index = 1 + (int)(random.NextUInt32() % (uint)(spells.Count - 1));
            var shower = spells[index];
            var length = Math.Min(Between(WeatherRules.DownpourFromTicks, WeatherRules.DownpourToTicks), (int)(shower.EndTick - shower.StartTick) - 2);
            var from = shower.StartTick + (shower.EndTick - shower.StartTick - length) / 2;
            spells[index] = shower with { EndTick = from };
            spells.Insert(index + 1, new(from, from + length, RainIntensity.Downpour));
            spells.Insert(index + 2, new(from + length, shower.EndTick, RainIntensity.Heavy));
        }
        return spells.ToArray();
    }

    private static WeatherSnapshot? NewWeather(ulong seed, int tier, int dayTicks,CampaignRuleset ruleset=CampaignRuleset.Survival) => CampaignRulesProfile.For(ruleset).WeatherForTier(tier) switch
    {
        WeatherKind.Rain => new(1, WeatherKind.Rain, RainForecast(seed, tier, dayTicks), [], []),
        WeatherKind.Mild => new(1,WeatherKind.Mild,[],[],[]),
        _ => null,
    };

    /// <summary>Sets the tier's weather up: rain from Tier 2, where nobody heats up and the bar sells ponchos.</summary>
    private static void SetUpWeather(GameSession session, ulong seed, int tier)
    {
        if(session.IsClassicCampaign)
        {
            var (forecast,rain)=ClassicWeatherForecast(session.CampaignSeed,session.FestivalNumber,session.PreparedEditionDurationTicks);
            session._weather=new(1,forecast.Weather,rain,[],[]){ClassicForecast=forecast};
            session._wetness.Clear();
            session.MedicalView=session.MedicalView! with {IsHot=forecast.Weather==WeatherKind.Heatwave,
                Evidence=[new("medical:classic-weather",0,forecast.Summary+" "+forecast.PlanningAdvice)]};
            return;
        }
        session._weather = NewWeather(seed, tier, session.PreparedEditionDurationTicks,session.Identity.Ruleset);
        session._wetness.Clear();
        if (session._weather is not null) session.MedicalView = session.MedicalView! with { IsHot = false,
            Evidence = session.IsClassicCampaign?[new("medical:mild",0,"Mild and dry: fixed temporary Classic conditions, not a random forecast; ordinary medical and safety responses remain active.")]:
                [new("medical:rain", 0, "Heavy showers forecast; free water, a baseline medic and shelter are available before opening.")] };
    }

    /// <summary>Ticks since the gates opened, or -1 before.</summary>
    private long DayTick => _preparation is { Status: PreparationStatus.Running or PreparationStatus.Departing } p ? CurrentTick - p.StartedTick : -1;

    /// <summary>How hard it's raining at this many ticks after opening.</summary>
    public RainIntensity RainAt(long dayTick)
    {
        if (_weather is not { Kind: WeatherKind.Rain } w || dayTick < 0) return RainIntensity.Dry;
        foreach (var spell in w.Spells) if (dayTick >= spell.StartTick && dayTick < spell.EndTick) return spell.Intensity;
        return RainIntensity.Dry;
    }

    /// <summary>How hard it's raining now; dry before the gates open.</summary>
    public RainIntensity RainNow => RainAt(DayTick);

    /// <summary>Rain still to fall in the next <paramref name="within"/> ticks, from <paramref name="dayTick"/>.</summary>
    private long RainTicksWithin(long dayTick, long within)
    {
        if (_weather is not { Kind: WeatherKind.Rain } w) return 0;
        long total = 0;
        foreach (var spell in w.Spells)
            total += Math.Max(0, Math.Min(spell.EndTick, dayTick + within) - Math.Max(spell.StartTick, dayTick));
        return total;
    }

    /// <summary>Ticks until the rain stops, from now, a downpour and the rest of its shower included; 0 when it isn't raining.</summary>
    private long RainTicksLeftInShower(long dayTick)
    {
        if (_weather is not { Kind: WeatherKind.Rain } w) return 0;
        for (var i = 0; i < w.Spells.Length; i++)
        {
            if (dayTick < w.Spells[i].StartTick || dayTick >= w.Spells[i].EndTick) continue;
            var end = w.Spells[i].EndTick;
            for (var j = i + 1; j < w.Spells.Length && w.Spells[j].StartTick == end; j++) end = w.Spells[j].EndTick;
            return end - dayTick;
        }
        return 0;
    }

    /// <summary>The weather now and next, for the top bar; before opening, the day ahead from the gates.</summary>
    public WeatherOutlook CaptureWeatherOutlook()
    {
        if (_weather is not { Kind: WeatherKind.Rain } w) return new(Weather, RainIntensity.Dry, -1, RainIntensity.Dry, false, 0);
        var now = Math.Max(0, DayTick);
        var current = RainAt(now);
        var downpour = w.Spells.Any(spell => spell.Intensity == RainIntensity.Downpour);
        // A downpour sits inside a shower: the spells either side of it are the same shower.
        var showers = w.Spells.Where((spell, i) => i == 0 || w.Spells[i - 1].EndTick != spell.StartTick).Count();
        for (var i = 0; i < w.Spells.Length; i++)
        {
            var spell = w.Spells[i];
            var next = i + 1 < w.Spells.Length && w.Spells[i + 1].StartTick == spell.EndTick ? w.Spells[i + 1].Intensity : RainIntensity.Dry;
            if (now >= spell.StartTick && now < spell.EndTick) return new(w.Kind, current, spell.EndTick, next, downpour, showers);
            if (spell.StartTick > now) return new(w.Kind, current, spell.StartTick, spell.Intensity, downpour, showers);
        }
        return new(w.Kind, current, -1, RainIntensity.Dry, downpour, showers);
    }

    /// <summary>How wet someone is, 0 to 10,000.</summary>
    public int WetnessOf(ulong id) => _wetness.GetValueOrDefault(id);

    /// <summary>Wet enough to look it: the soaked palette and the rain clips.</summary>
    public bool Soaked(ulong id) => _wetness.GetValueOrDefault(id) >= WeatherRules.SoakedFrom;

    /// <summary>
    /// Once a second while the rain festival runs: anyone out in it gets wetter, anyone under cover (a marquee, the bar
    /// canopy, the van counter, the first-aid porch or a portaloo) or in a poncho stops getting wetter and dries, and
    /// once it stops everyone dries. Guests lose mood for standing in it, and for being wet through.
    /// </summary>
    private void AdvanceWeather()
    {
        if (_weather is not { Kind: WeatherKind.Rain } || _preparation?.Status is not (PreparationStatus.Running or PreparationStatus.Departing) ||
            CurrentTick % 80 != 0) return;
        AdvanceStrawJob();
        var rain = RainNow;
        var soak = WeatherRules.SoakPerSecond(rain);
        var changed = false;
        foreach (var person in PeopleIn(PersonView.Roster))
        {
            var id = person.Id;
            var wet = _wetness.GetValueOrDefault(id);
            if (person.Departed) { if (wet > 0) { _wetness.Remove(id); changed = true; } continue; }
            if (!person.Admitted) continue;
            var exposed = rain != RainIntensity.Dry && !UnderCover(id) && !OwnsPoncho(id);
            var next = exposed ? Math.Min(10_000, wet + soak)
                : Math.Max(0, wet - (rain == RainIntensity.Dry ? WeatherRules.DryInTheOpenPerSecond : WeatherRules.DryUnderCoverPerSecond));
            if (next != wet) { if (next == 0) _wetness.Remove(id); else _wetness[id] = next; changed = true; }
            if (person.Role != ProtectedPersonRole.Guest || _preparation.Status != PreparationStatus.Running) continue;
            var loss = (exposed ? WeatherRules.RainLossPerSecond(rain) : 0) + WeatherRules.WetLossPerSecond(next);
            if (loss > 0) ChangeSatisfaction(id, -UnpleasantFor(id, loss), MoodCause.Rain);
        }
        if (changed) _weatherVersion++;
    }

    private void RestoreWeather(WeatherSnapshot? snapshot)
    {
        _wetness.Clear();
        _weather = snapshot is null ? null : snapshot with { ClassicForecast=snapshot.ClassicForecast is{} f?f with{Temperatures=f.Temperatures.ToArray()}:null, Spells = snapshot.Spells.ToArray(), WetIds = [], Wetness = [] };
        for (var i = 0; i < (snapshot?.WetIds.Length ?? 0); i++) _wetness[snapshot!.WetIds[i]] = snapshot.Wetness[i];
        _weatherVersion++;
    }

    /// <summary>
    /// The weather is the tier's: rain from Tier 2 with exactly the showers the seed gives, none in a heatwave. Wet people are
    /// on the roster and dry before opening; the straw store holds no more than was bought.
    /// </summary>
    private static string? ValidatePersistedWeather(SessionPersistenceSnapshot s)
    {
        if (s.Preparation is not { } p) return s.Weather is null ? null : "Weather without a festival.";
        var kind = SavedWeatherKind(s);
        if (!SavedClassic(s) && kind == WeatherKind.Heatwave) return s.Weather is null ? null : "This festival has no rain.";
        if (s.Weather is not { } w) return "A rainy festival's weather is missing.";
        if (w.Version != 1 || w.Kind != kind || w.Spells is null || w.WetIds is null || w.Wetness is null || w.WetIds.Length != w.Wetness.Length ||
            w.Spells.Any(spell => spell is null)) return "Weather state shape invalid.";
        if(kind!=WeatherKind.Rain&&(w.Spells.Length!=0||w.WetIds.Length!=0||w.StrawBales!=0||w.StrawJob is not null))return "A dry festival must remain dry.";
        var day = DayTicksFor(SavedStages(s));
                WeatherSpell[] expected;
        if(SavedClassic(s))
        {
            var generated=ClassicWeatherForecast(s.CampaignSeed,s.CampaignPlanning!.EditionNumber,day);
            var f=w.ClassicForecast;
            if(f is null||f.Version!=generated.Forecast.Version||f.Conditions!=generated.Forecast.Conditions||
                f.Temperatures is null||!f.Temperatures.SequenceEqual(generated.Forecast.Temperatures))
                return "Classic temperature forecast differs from its seeded schedule.";
            expected=generated.Rain;
        }
        else
        {
            if(w.ClassicForecast is not null)return "Classic forecast on a Survival festival.";
            expected=RainForecast(s.CampaignSeed,p.Tier,day);
        }
        if (!w.Spells.SequenceEqual(expected)) return "The showers differ from the day's forecast.";
        var roster = p.People.ToDictionary(person => person.AgentId);
        for (var i = 0; i < w.WetIds.Length; i++)
        {
            if (i > 0 && w.WetIds[i] <= w.WetIds[i - 1]) return "Wet people must be in ascending order without repeats.";
            if (!roster.TryGetValue(w.WetIds[i], out var person) || !person.Admitted) return "Someone wet never came in.";
            if (w.Wetness[i] is < 1 or > 10_000) return "Wetness out of range.";
        }
        if (p.Status == PreparationStatus.Preparing && (w.WetIds.Length != 0 || w.StrawBales != 0 || w.StrawJob is not null))
            return "Draft/retry must start dry.";
        var bought = p.Plan is { Committed: true } plan ? plan.StrawBales : 0;
        if (w.StrawBales < 0 || w.StrawBales > bought || w.StrawJob is not null && w.StrawBales >= bought) return "Straw store out of range.";
        if (s.Immersion?.People.Any(person => person.Held?.Product == ImmersionProduct.Poncho) == true) return "A poncho is worn, not held.";
        return ValidatePersistedShelter(s, p) ?? ValidatePersistedStrawJob(w, s, p);
    }

    /// <summary>Someone out of the rain: under a cover, inside a portaloo, or on the trailer stage's deck.</summary>
    private bool UnderCover(ulong id)
    {
        if (PersonIn(PersonView.Consumption, id) is { ToiletStage: ToiletVisitStage.Using or ToiletVisitStage.Entering }) return true;
        if (!_navigationAgents.TryGetValue(new(id), out var nav)) return false;
        var cell = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
        return CoveredCell(cell) || LivePerformerStage(id) == 0 && HasClaim(id, PersonClaim.Performing);
    }
}

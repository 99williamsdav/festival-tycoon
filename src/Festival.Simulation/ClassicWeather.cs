namespace Festival.Simulation;

public enum ClassicWeatherKind { Mild, Pleasant, Cloudy, Showery, Heatwave, HeavyRain }
public sealed record ForecastTemperature(long Tick, int Celsius);
public sealed record ClassicForecast(int Version, ClassicWeatherKind Conditions, ForecastTemperature[] Temperatures)
{
    public WeatherKind Weather => Conditions switch
    { ClassicWeatherKind.Heatwave => WeatherKind.Heatwave, ClassicWeatherKind.Showery or ClassicWeatherKind.HeavyRain => WeatherKind.Rain, _ => WeatherKind.Mild };
    public string Summary => Conditions switch
    {
        ClassicWeatherKind.Pleasant => "Pleasant and dry.", ClassicWeatherKind.Cloudy => "Cloudy, with a dry day ahead.",
        ClassicWeatherKind.Showery => "Mild, with short showers.", ClassicWeatherKind.Heatwave => "A hot, dry festival day.",
        ClassicWeatherKind.HeavyRain => "Heavy showers, with dry breaks.", _ => "Mild and dry."
    };
    public string PlanningAdvice => Weather switch
    { WeatherKind.Heatwave => "Water, shade and rest for a hot day.", WeatherKind.Rain => "Shelter, ponchos and matting for wet spells.", _ => "A dry day ahead. Water and first aid still matter." };
}

public sealed partial class GameSession
{
    /// <summary>A separate keyed draw: never advances any live gameplay random stream.</summary>
    public static (ClassicForecast Forecast, WeatherSpell[] Rain) ClassicWeatherForecast(ulong campaignSeed, int number, int dayTicks)
    {
        if(number < 1 || dayTicks < 8_000) throw new ArgumentOutOfRangeException(nameof(number));
        var seed=ClassicFestivalSeed(campaignSeed,number);
        var random=RandomStreamFactory.Create(seed ^ 0xF04ECA5720261015UL,RandomStreamId.Weather);
        int Between(int from,int to)=>from+(int)(random.NextUInt32()%(uint)(to-from+1));
        var pick=Between(0,99);
        // Provisional: 80% ordinary dry, 15% ordinary showers, 3% heatwave, 2% heavy rain.
        var kind=pick<35?ClassicWeatherKind.Mild:pick<60?ClassicWeatherKind.Pleasant:pick<80?ClassicWeatherKind.Cloudy:
            pick<95?ClassicWeatherKind.Showery:pick<98?ClassicWeatherKind.Heatwave:ClassicWeatherKind.HeavyRain;
        var low=kind switch {ClassicWeatherKind.Pleasant=>19,ClassicWeatherKind.Cloudy=>13,ClassicWeatherKind.Showery=>14,
            ClassicWeatherKind.Heatwave=>28,ClassicWeatherKind.HeavyRain=>12,_=>16};
        var offset=Between(0,2);
        int[] shape=[1,3,4,2,0];
        var temperatures=Enumerable.Range(0,5).Select(i=>new ForecastTemperature((long)dayTicks*i/4,low+offset+shape[i])).ToArray();
        WeatherSpell[] rain=[];
        if(kind==ClassicWeatherKind.HeavyRain)rain=RainForecast(seed,1,dayTicks);
        else if(kind==ClassicWeatherKind.Showery)
        {
            var start=Between(4_800,dayTicks/2);
            var end=Math.Min(dayTicks-ClosingAfterLastSetTicks,start+Between(1_200,2_400));
            var spells=new List<WeatherSpell>{new(start,end,RainIntensity.Showers)};
            if(Between(0,1)==1)
            {
                var second=end+Between(3_200,6_400);
                if(second<dayTicks-ClosingAfterLastSetTicks-800)spells.Add(new(second,Math.Min(dayTicks-ClosingAfterLastSetTicks,second+Between(800,1_600)),RainIntensity.Showers));
            }
            rain=spells.ToArray();
        }
        return(new(1,kind,temperatures),rain);
    }
    public ClassicForecast? CaptureClassicForecast()=>_weather?.ClassicForecast is not{} f?null:f with{Temperatures=f.Temperatures.ToArray()};
    public int? TemperatureAt(long elapsedTick)
    {
        if(_weather?.ClassicForecast is not{} f)return null;
        var points=f.Temperatures;
        var tick=Math.Clamp(elapsedTick,0,points[^1].Tick);
        for(var i=1;i<points.Length;i++)if(tick<=points[i].Tick)
            return points[i-1].Celsius+(int)((tick-points[i-1].Tick)*(points[i].Celsius-points[i-1].Celsius)/(points[i].Tick-points[i-1].Tick));
        return points[^1].Celsius;
    }
    private static WeatherKind SavedWeatherKind(SessionPersistenceSnapshot s)=>SavedClassic(s)
        ?ClassicWeatherForecast(s.CampaignSeed,s.CampaignPlanning?.EditionNumber??1,DayTicksFor(SavedStages(s))).Forecast.Weather
        :WeatherRules.ForTier(s.Preparation!.Tier);
}

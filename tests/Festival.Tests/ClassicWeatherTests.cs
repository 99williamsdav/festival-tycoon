using System.Text.Json;
using Festival.Presentation;
using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class ClassicWeatherTests
{
    internal static ulong SeedFor(ClassicWeatherKind kind)=>Enumerable.Range(1,10_000).Select(i=>(ulong)i)
        .First(seed=>GameSession.ClassicWeatherForecast(seed,1,38_400).Forecast.Conditions==kind);
    private static GameSession Restore(GameSession s)
    {
        var restored=GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess,restored.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,restored.Session!.CaptureSnapshot().AuthoritativeHash);
        return restored.Session;
    }
    [TestMethod]
    [DataRow(ClassicWeatherKind.Mild)] [DataRow(ClassicWeatherKind.Pleasant)] [DataRow(ClassicWeatherKind.Cloudy)]
    [DataRow(ClassicWeatherKind.Showery)] [DataRow(ClassicWeatherKind.Heatwave)] [DataRow(ClassicWeatherKind.HeavyRain)]
    public void EachWeatherTypeHasAnExactIndependentSavedForecast(ClassicWeatherKind kind)
    {
        var seed=SeedFor(kind);var s=GameSession.CreateClassicCampaign(seed);
        var expected=GameSession.ClassicWeatherForecast(seed,1,s.PreparedEditionDurationTicks);
        Assert.AreEqual(kind,s.CaptureClassicForecast()!.Conditions);
        Assert.AreEqual(expected.Forecast.Weather,s.CurrentFestival.Weather);
        Assert.AreEqual(kind==ClassicWeatherKind.Heatwave,s.CaptureMedical()!.IsHot);
        var before=s.CaptureSnapshot().AuthoritativeHash;
        for(var i=0;i<10;i++)
        {
            CollectionAssert.AreEqual(expected.Rain,s.CaptureWeather()!.Spells);
            CollectionAssert.AreEqual(expected.Forecast.Temperatures,s.CaptureClassicForecast()!.Temperatures);
            GameSession.ClassicWeatherForecast(seed,1,s.PreparedEditionDurationTicks);
        }
        Assert.AreEqual(before,s.CaptureSnapshot().AuthoritativeHash,"Reading/generating forecast must consume no gameplay RNG.");
        s=Restore(s);
        var snap=s.CapturePersistenceSnapshot();
        Assert.IsFalse(GameSession.Restore(snap with {Weather=snap.Weather! with {ClassicForecast=null}}).IsSuccess);
        var temperatures=snap.Weather!.ClassicForecast!.Temperatures.ToArray();temperatures[0]=temperatures[0] with{Celsius=99};
        Assert.IsFalse(GameSession.Restore(snap with {Weather=snap.Weather with {ClassicForecast=snap.Weather.ClassicForecast with{Temperatures=temperatures}}}).IsSuccess);
        Assert.AreEqual(expected.Forecast.Temperatures[0].Celsius,s.TemperatureAt(0));
        Assert.AreEqual(expected.Forecast.Temperatures[^1].Celsius,s.TemperatureAt(s.PreparedEditionDurationTicks));
        Assert.AreEqual(0L,s.CurrentTick);
    }
    [TestMethod]
    public void ProvisionalDistributionIsMostlyOrdinaryAndEachFestivalHasAKeyedDraw()
    {
        var counts=new int[6];var changed=0;
        for(ulong seed=1;seed<=10_000;seed++)
        {
            var first=GameSession.ClassicWeatherForecast(seed,1,38_400);counts[(int)first.Forecast.Conditions]++;
            var next=GameSession.ClassicWeatherForecast(seed,2,38_400);
            if(first.Forecast.Conditions!=next.Forecast.Conditions||!first.Forecast.Temperatures.SequenceEqual(next.Forecast.Temperatures)||!first.Rain.SequenceEqual(next.Rain))changed++;
            Assert.IsTrue(first.Rain.All(r=>r.StartTick>=0&&r.EndTick>r.StartTick&&r.EndTick<=38_400));
            Assert.IsTrue(first.Rain.Zip(first.Rain.Skip(1)).All(p=>p.First.EndTick<=p.Second.StartTick));
        }
        Assert.IsTrue(counts[0]+counts[1]+counts[2] is >7700 and <8300,string.Join(",",counts));
        Assert.IsTrue(counts[4] is >200 and <400);Assert.IsTrue(counts[5] is >100 and <300);
        Assert.IsTrue(changed>9_000,"A next festival has its own keyed schedule, without promising every day differs.");
    }
    [TestMethod]
    [DataRow(ClassicWeatherKind.Showery)] [DataRow(ClassicWeatherKind.HeavyRain)] [DataRow(ClassicWeatherKind.Heatwave)]
    public void ForecastMatchesLiveRainAndExistingHeatAndWetResponses(ClassicWeatherKind kind)
    {
        var s=GameSession.CreateClassicCampaign(SeedFor(kind));NextFestivalTests.Ready(s);
                if(s.IsRainyFestival)
        {
            BuildSession.Accept(s,new SetPreparationStockCommand(24,16){Ponchos=6,StrawBales=2});
            Assert.IsTrue(s.GroundCoverAvailable);
            BuildSession.Accept(s,new GroundCoverCommand(GroundCoverAction.Matting,new(110,154),new(116,154)));
            Restore(s);
        }
        BuildSession.Accept(s,new StartPreparedEditionCommand());
        var spells=s.CaptureWeather()!.Spells;
        foreach(var spell in spells)
        {
            Assert.AreEqual(spells.LastOrDefault(r=>r.StartTick<=spell.StartTick-1&&r.EndTick>spell.StartTick-1)?.Intensity??RainIntensity.Dry,s.RainAt(spell.StartTick-1));
            Assert.AreEqual(spell.Intensity,s.RainAt(spell.StartTick));
        }
        var target=spells.Length>0?spells[0].EndTick:8_000;
        while(s.CurrentTick<target)s.AdvanceWithoutSnapshot((int)Math.Min(80,target-s.CurrentTick));
        Assert.AreEqual(PreparationStatus.Running,s.PreparedStatus);
        if(s.IsRainyFestival)
        {
            Assert.IsTrue(s.CaptureWeather()!.WetIds.Length>0,"Actual exposed people get wet.");
            Assert.IsTrue(s.CaptureMedical()!.Needs.All(n=>n.HeatExposure<=2_500),"Wet days do not heat people.");
        }
        else Assert.IsTrue(s.CaptureMedical()!.Needs.Any(n=>n.HeatExposure>2_500),"Heatwave uses existing heating.");
        s=Restore(s);s.AdvanceWithoutSnapshot(160);Restore(s);
    }
    [TestMethod]
    public void SharedTimestampMappingPreservesElapsedOutputAndSeparatesDurations()
    {
        foreach(var (tick,text) in new(long,string)[]{(-1,"00:00"),(0,"00:00"),(79,"00:00"),(80,"00:01"),(4_800,"01:00"),(38_400,"08:00")})
            Assert.AreEqual(text,new FestivalTimeMapping().Timestamp(tick));
        var before=FestivalTimePresentation.Mapping;
        try
        {
            FestivalTimePresentation.Mapping=new(3600);
            Assert.AreEqual("60:00–68:00",FestivalTimePresentation.Range(0,38_400));
            Assert.AreEqual("08:00",FestivalTimePresentation.Duration(38_400));
            Assert.AreEqual("0:01",FestivalTimePresentation.Duration(80,false));
        }
        finally{FestivalTimePresentation.Mapping=before;}
    }
}

using Festival.Simulation;
int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
foreach(var kind in Enum.GetValues<ClassicWeatherKind>())
{
    var seed=Enumerable.Range(1,10000).Select(x=>(ulong)x).First(x=>GameSession.ClassicWeatherForecast(x,1,38400).Forecast.Conditions==kind);
    var session=GameSession.CreateClassicCampaign(seed);
    var snapshot=session.CapturePersistenceSnapshot();
    var restored=GameSession.Restore(snapshot);
    Check(restored.IsSuccess,"restore "+kind);
    var hash=session.CaptureSnapshot().AuthoritativeHash;
    var restoredHash=restored.Session!.CaptureSnapshot().AuthoritativeHash;
    var weather=snapshot.Weather!;
    var forecast=weather.ClassicForecast!;
    void Reject(ClassicForecast? candidate,string label)
    {
        var result=GameSession.Restore(snapshot with{Weather=weather with{ClassicForecast=candidate}});
        Check(!result.IsSuccess,label+" "+kind);
        Check(result.Error!.Contains("Classic temperature forecast"),"specific rejection "+label);
    }
    Reject(null,"missing forecast");
    Reject(forecast with{Temperatures=null!},"null sample collection");
    Reject(forecast with{Temperatures=[]},"empty samples");
    Reject(forecast with{Temperatures=[null!,..forecast.Temperatures.Skip(1)]},"null sample");
    Reject(forecast with{Temperatures=forecast.Temperatures.Reverse().ToArray()},"reversed samples");
    Reject(forecast with{Temperatures=[forecast.Temperatures[0],..forecast.Temperatures]},"extra sample");
    Reject(forecast with{Version=99},"wrong version");
    Reject(forecast with{Conditions=(ClassicWeatherKind)99},"wrong condition");
    var capture=session.CaptureClassicForecast()!;capture.Temperatures[0]=new(999,999);
    Check(session.TemperatureAt(0)==forecast.Temperatures[0].Celsius,"capture alias");
    var midpoint=forecast.Temperatures[1].Tick/2;
    Check(session.TemperatureAt(midpoint)==forecast.Temperatures[0].Celsius+1,"interpolation");
    Check(session.TemperatureAt(-1)==forecast.Temperatures[0].Celsius,"negative clamp");
    Check(session.TemperatureAt(long.MaxValue)==forecast.Temperatures[^1].Celsius,"end clamp");
    forecast.Temperatures[0]=new(999,999);
    if(weather.Spells.Length>0)weather.Spells[0]=new(0,1,RainIntensity.Downpour);
    Check(session.CaptureSnapshot().AuthoritativeHash==hash,"source unchanged");
    Check(restored.Session.CaptureSnapshot().AuthoritativeHash==restoredHash,"restored unchanged");
    Check(restored.Session.TemperatureAt(0)!=999,"restore alias");
}
Console.WriteLine($"PASS: {checks} independent checks across all six Classic conditions: malformed forecast rejection, capture/restore alias isolation, interpolation and clamping.");

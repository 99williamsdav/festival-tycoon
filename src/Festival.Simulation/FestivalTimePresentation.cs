namespace Festival.Presentation;

/// <summary>Presentation only. Authoritative ticks and schedules never depend on this mapping.</summary>
public sealed record FestivalTimeMapping(long OpeningDisplaySeconds=0)
{
    public string Timestamp(long elapsedTicks)=>FestivalTimePresentation.MinutesSeconds(checked(OpeningDisplaySeconds+Math.Max(0,elapsedTicks)/80),true);
}

public static class FestivalTimePresentation
{
    // Elapsed placeholders remain the default. A future approved real-clock mapping belongs here alone.
    public static FestivalTimeMapping Mapping {get;set;}=new();
    public static string Timestamp(long elapsedTicks)=>Mapping.Timestamp(elapsedTicks);
    public static string Range(long from,long to)=>$"{Timestamp(from)}–{Timestamp(to)}";
    public static string Duration(long ticks,bool padMinutes=true)=>MinutesSeconds(Math.Max(0,ticks)/80,padMinutes);
    public static string Seconds(long ticks)=>$"{Math.Max(0,ticks)/80}s";
    internal static string MinutesSeconds(long seconds,bool padMinutes)=>padMinutes?$"{seconds/60:00}:{seconds%60:00}":$"{seconds/60}:{seconds%60:00}";
}

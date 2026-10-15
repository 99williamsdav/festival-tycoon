using System.Diagnostics;

namespace Festival.Simulation;

public sealed record SimulationWorkTiming(string Name,long Calls,double TotalMilliseconds,double MaximumMilliseconds,int SampledCalls,double P95Milliseconds);

public sealed partial class GameSession
{
    // Opt-in observations only. Never saved, hashed, or consulted by gameplay decisions.
    public bool PerformanceTimingsEnabled {get;set;}
    private sealed class WorkTiming
    {
        public long Calls,Ticks,Maximum;
        // Keep diagnostic observation memory bounded, even if a caller leaves it enabled.
        public readonly List<long> Samples=[];
    }
    private readonly Dictionary<string,WorkTiming> _performanceTimings=[];
    public IReadOnlyList<SimulationWorkTiming> CapturePerformanceTimings()=>_performanceTimings.OrderBy(p=>p.Key,StringComparer.Ordinal)
        .Select(p=>
        {
            var samples=p.Value.Samples.Order().ToArray();
            return new SimulationWorkTiming(p.Key,p.Value.Calls,p.Value.Ticks*1000.0/Stopwatch.Frequency,p.Value.Maximum*1000.0/Stopwatch.Frequency,
                samples.Length,samples[(int)Math.Ceiling(samples.Length*.95)-1]*1000.0/Stopwatch.Frequency);
        }).ToArray();
    private WorkMeasurement MeasureWork(string name)=>new(PerformanceTimingsEnabled?this:null,name);
    private readonly struct WorkMeasurement:IDisposable
    {
        private readonly GameSession? _session;
        private readonly string _name;
        private readonly long _start;
        public WorkMeasurement(GameSession? session,string name){_session=session;_name=name;_start=session is null?0:Stopwatch.GetTimestamp();}
        public void Dispose()
        {
            if(_session is null)return;
            var elapsed=Stopwatch.GetTimestamp()-_start;
            if(!_session._performanceTimings.TryGetValue(_name,out var work))_session._performanceTimings[_name]=work=new();
            work.Calls++;work.Ticks+=elapsed;work.Maximum=Math.Max(work.Maximum,elapsed);
            if(work.Samples.Count<65536)work.Samples.Add(elapsed);
        }
    }
}

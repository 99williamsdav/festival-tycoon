using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

// Opt-in S0.08 instrumentation only. It changes neither tick rules nor scene content.
public partial class Main
{
    private string? _preparationProfileOutput;
    private bool _preparationProfileCapture;
    private bool _preparationProfileDeparture;
    private bool _preparationProfileFullAttempt;
    private long _profileStarted;
    private long _profilePrevious;
    private long _profileStartTick;
    private string _profileStartHash = "";
    private double _profileSimulationMs;
    private double _profilePresentationMs;
    private double _profileCaptureMs;
    private readonly List<PreparationProfileSample> _profileSamples = [];
    private sealed record PreparationProfileSample(long Tick, double FrameMs, double SimulationObservationMs,
        double PresentationHudMs, double CaptureMs, double EngineProcessMs, double RenderCpuMs, double RenderGpuMs, double DrawCalls, string Phase);

    private void FinishPreparationProfileFrame()
    {
        if (_preparationLiveStarted == 0) return;
        var now = Stopwatch.GetTimestamp();
        if (_profileStarted == 0)
        {
            _profileStarted = _profilePrevious = now;
            _profileStartTick = _session.CurrentTick;
            _profileStartHash = _session.CaptureSnapshot().AuthoritativeHash;
            RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
            return;
        }
        var elapsed = Stopwatch.GetElapsedTime(_profileStarted, now).TotalSeconds;
        _profileSamples.Add(new(_session.CurrentTick, Stopwatch.GetElapsedTime(_profilePrevious, now).TotalMilliseconds,
            _profileSimulationMs, _profilePresentationMs, _profileCaptureMs,
            Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000,
            RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()),
            RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()),
            Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
            _session.PreparedStatus == Festival.Simulation.PreparationStatus.Running
                ? (_session.CaptureObservation().NavigationAgents.Any(item => item.Action == Festival.Simulation.AgentNavigationAction.Travelling) ? "Arrival" : "Live")
                : _session.PreparedStatus.ToString()!));
        _profilePrevious = now;
        if (_preparationProfileFullAttempt)
        {
            if (_session.PreparedStatus != Festival.Simulation.PreparationStatus.Finished && elapsed < 660 && !_preparationSaveBlocked) return;
        }
        else if (elapsed < 30) return;
        static object Stats(IEnumerable<double> values)
        {
            var ordered = values.Order().ToArray();
            double P(double fraction) => ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * fraction) - 1, 0, ordered.Length - 1)];
            return new { count = ordered.Length, mean = ordered.Average(), p50 = P(.50), p95 = P(.95), p99 = P(.99), max = ordered[^1] };
        }
        var result = new
        {
            diagnostic = "S0.08", capture = _preparationProfileCapture, departure = _preparationProfileDeparture, fullAttempt = _preparationProfileFullAttempt, debug = OS.IsDebugBuild(),
            godot = Engine.GetVersionInfo()["string"].AsString(), processor = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            resolution = GetWindow().Size.ToString(), renderer = RenderingServer.GetCurrentRenderingMethod(),
            gpu = RenderingServer.GetVideoAdapterName(), vsync = DisplayServer.WindowGetVsyncMode().ToString(),
            seed = _session.CampaignSeed, people = _session.CapturePreparation()!.People.Length,
            startTick = _profileStartTick, endTick = _session.CurrentTick, wallSeconds = elapsed,
            attainedSpeed = (_session.CurrentTick - _profileStartTick) / (80 * elapsed),
            startHash = _profileStartHash, endHash = _session.CaptureSnapshot().AuthoritativeHash,
            peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
            frameMs = Stats(_profileSamples.Select(item => item.FrameMs)),
            simulationObservationMs = Stats(_profileSamples.Select(item => item.SimulationObservationMs)),
            presentationHudMs = Stats(_profileSamples.Select(item => item.PresentationHudMs)),
            captureMs = Stats(_profileSamples.Select(item => item.CaptureMs)),
            engineProcessMs = Stats(_profileSamples.Select(item => item.EngineProcessMs)),
            renderCpuMs = Stats(_profileSamples.Select(item => item.RenderCpuMs)),
            renderGpuMs = Stats(_profileSamples.Select(item => item.RenderGpuMs)),
            drawCalls = Stats(_profileSamples.Select(item => item.DrawCalls)),
            phaseSummaries = _profileSamples.GroupBy(item => item.Phase).Select(group => new
            {
                phase = group.Key, startTick = group.First().Tick, endTick = group.Last().Tick,
                wallSeconds = group.Sum(item => item.FrameMs) / 1000,
                frameMs = Stats(group.Select(item => item.FrameMs)),
                simulationObservationMs = Stats(group.Select(item => item.SimulationObservationMs)),
                presentationHudMs = Stats(group.Select(item => item.PresentationHudMs)), captureMs = Stats(group.Select(item => item.CaptureMs))
            }).ToArray(),
            finalStatus = _session.PreparedStatus.ToString(),
            newestAutosaveExact = !_preparationProfileFullAttempt ? (bool?)null :
                Festival.Persistence.AutosaveRotation.LoadNewestValid(SaveDirectory, _saveCompatibility).Session?.CaptureSnapshot().AuthoritativeHash == _session.CaptureSnapshot().AuthoritativeHash,
            samples = _profileSamples
        };
        // The no-capture arm performs no per-frame filesystem calls. Write once after sampling.
        Directory.CreateDirectory(Path.GetDirectoryName(_preparationProfileOutput!)!);
        File.WriteAllText(_preparationProfileOutput!, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        GetTree().Quit();
    }
}

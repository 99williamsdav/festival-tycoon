using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

public partial class Main
{
    private sealed record EquipmentPerformanceFrame(double Milliseconds, long Tick, bool Focused,
        double CallbackMs, double OutsideCallbackMs, double SimulationCoordinatorObservationMs, double PresentationHudMs,
        double CaptureMs, double DiagnosticControlMs, double RenderQueryMs, double RenderCpuMs, double RenderGpuMs,
        double DeltaMs, long TickAdvance, int ScheduledTicks, double DebtBefore, double DebtAfter, bool ClockOverloaded,
        int Gen0Collections, int Gen1Collections, int Gen2Collections, int VsyncWindow, string VsyncMode, double WallOffsetSeconds, int MaxFps,
        int NativeTimingWindow, bool NativeTimingEnabled);
    private string? _equipmentPerformanceOutput;
    private int _equipmentPerformanceSetupFrames;
    private long _equipmentPerformanceStarted;
    private long _equipmentPerformancePrior;
    private bool _equipmentPerformanceDispatched;
    private long _equipmentCallbackStarted;
    private double _equipmentDeltaMs;
    private double _equipmentDebtBefore;
    private int _equipmentScheduledTicks;
    private long _equipmentPreviousTick;
    private bool _equipmentVsyncDiagnostic;
    private bool _equipmentCappedDiagnostic;
    private bool _nativeTimingControlDiagnostic;
    private bool _nativeTimingEnabled = true;
    private int _nativeTimingWindow;
    private bool? _nativeTimingInitialReproduced;
    private readonly List<(int Window, double Seconds, bool Enabled)> _nativeTimingTransitions = [];
    private DisplayServer.VSyncMode? _equipmentOriginalVsync;
    private int? _equipmentOriginalMaxFps;
    private int _equipmentVsyncWindow;
    private readonly List<(int Window, double Seconds, string Mode)> _equipmentVsyncTransitions = [];
    private readonly List<EquipmentPerformanceFrame> _equipmentPerformanceFrames = [];

    // One opt-in 60-second foreground check. No screenshot, manual save/reload,
    // evidence filesystem calls or timing changes inside the measured window.
    private void ProcessEquipmentPerformanceCheck()
    {
        if (_equipmentPerformanceStarted == 0)
        {
            if (_equipmentPerformanceSetupFrames++ == 0)
            {
                DisplayServer.WindowMoveToForeground();
                if (_nativeTimingControlDiagnostic &&
                    (DisplayServer.WindowGetVsyncMode() != DisplayServer.VSyncMode.Enabled || Engine.MaxFps != 58))
                {
                    GD.PushError("Native timing control requires the unchanged VSync Enabled / 58 FPS project default.");
                    GetTree().Quit(3);
                    return;
                }
                if (_equipmentVsyncDiagnostic || _equipmentCappedDiagnostic)
                {
                    _equipmentOriginalVsync = DisplayServer.WindowGetVsyncMode();
                    DisplayServer.WindowSetVsyncMode(_equipmentCappedDiagnostic ? DisplayServer.VSyncMode.Disabled : DisplayServer.VSyncMode.Enabled);
                    if (_equipmentCappedDiagnostic) { _equipmentOriginalMaxFps = Engine.MaxFps; Engine.MaxFps = 60; }
                }
                RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
                if (_nativeTimingControlDiagnostic) _nativeTimingTransitions.Add((0, 0, true));
                foreach (var id in new[] { "act.folk", "staff.steward", "equipment.buy", "maintenance.worker" })
                    _offerButtons[id].EmitSignal(Button.SignalName.Pressed);
            }
            if (_equipmentPerformanceSetupFrames < 30) return;
            _equipmentPerformanceStarted = _equipmentPerformancePrior = Stopwatch.GetTimestamp();
            if (_equipmentVsyncDiagnostic || _equipmentCappedDiagnostic) _equipmentVsyncTransitions.Add((0, 0, DisplayServer.WindowGetVsyncMode().ToString()));
            _preparationStart.EmitSignal(Button.SignalName.Pressed);
            return;
        }
        var controlStarted = Stopwatch.GetTimestamp();
        if (!_equipmentPerformanceDispatched && _session.CaptureEquipment()!.Stage == EquipmentStage.Warning)
        {
            _equipmentButtons[EquipmentAction.DispatchMaintenance].EmitSignal(Button.SignalName.Pressed);
            _equipmentPerformanceDispatched = true;
        }
        var controlMs = Stopwatch.GetElapsedTime(controlStarted).TotalMilliseconds;
        var queryStarted = Stopwatch.GetTimestamp();
        var renderCpuMs = _nativeTimingEnabled ? RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()) : 0;
        var renderGpuMs = _nativeTimingEnabled ? RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()) : 0;
        var queryMs = Stopwatch.GetElapsedTime(queryStarted).TotalMilliseconds;
        var actualVsyncMode = DisplayServer.WindowGetVsyncMode().ToString();
        var now = Stopwatch.GetTimestamp();
        var seconds = Stopwatch.GetElapsedTime(_equipmentPerformanceStarted, now).TotalSeconds;
        var interval = Stopwatch.GetElapsedTime(_equipmentPerformancePrior, now).TotalMilliseconds;
        var callback = Stopwatch.GetElapsedTime(_equipmentCallbackStarted, now).TotalMilliseconds;
        _equipmentPerformanceFrames.Add(new(interval, _session.CurrentTick, DisplayServer.WindowIsFocused(), callback, interval - callback,
            _profileSimulationMs, _profilePresentationMs, _profileCaptureMs, controlMs, queryMs, renderCpuMs, renderGpuMs,
            _equipmentDeltaMs, _session.CurrentTick - _equipmentPreviousTick, _equipmentScheduledTicks, _equipmentDebtBefore,
            _foundationClock.DebtTicks, _foundationClock.IsOverloaded, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
            _equipmentVsyncWindow, actualVsyncMode, seconds, Engine.MaxFps, _nativeTimingWindow, _nativeTimingEnabled));
        _equipmentPreviousTick = _session.CurrentTick;
        _equipmentPerformancePrior = now;
        if (_equipmentVsyncDiagnostic && seconds < 60)
        {
            var nextWindow = Math.Min(3, (int)(seconds / 15));
            if (nextWindow != _equipmentVsyncWindow)
            {
                DisplayServer.WindowSetVsyncMode(nextWindow % 2 == 0 ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
                _equipmentVsyncWindow = nextWindow;
                _equipmentVsyncTransitions.Add((nextWindow, seconds, DisplayServer.WindowGetVsyncMode().ToString()));
            }
        }
        var inconclusive = false;
        if (_nativeTimingControlDiagnostic && _nativeTimingWindow == 0 && seconds >= 10)
        {
            // Stop rather than repeat the run if its required initial stall regime is absent.
            _nativeTimingInitialReproduced = _equipmentPerformanceFrames.Count(item =>
                item.NativeTimingWindow == 0 && item.WallOffsetSeconds >= 1 && item.OutsideCallbackMs > 200) >= 5;
            inconclusive = !_nativeTimingInitialReproduced.Value;
        }
        if (_nativeTimingControlDiagnostic && !inconclusive && seconds < 40)
        {
            var nextWindow = Math.Min(3, (int)(seconds / 10));
            if (nextWindow != _nativeTimingWindow)
            {
                _nativeTimingWindow = nextWindow;
                _nativeTimingEnabled = nextWindow % 2 == 0;
                RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), _nativeTimingEnabled);
                _nativeTimingTransitions.Add((nextWindow, seconds, _nativeTimingEnabled));
            }
        }
        if (!inconclusive && seconds < (_nativeTimingControlDiagnostic ? 40 : 60)) return;
        if (_nativeTimingControlDiagnostic) RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), false);
        if (_equipmentOriginalVsync is { } original) DisplayServer.WindowSetVsyncMode(original);
        if (_equipmentOriginalMaxFps is { } originalFps) Engine.MaxFps = originalFps;
        var sorted = _equipmentPerformanceFrames.Select(item => item.Milliseconds).Order().ToArray();
        double Percentile(double p) => sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * p) - 1, 0, sorted.Length - 1)];
        var focusedFrames = _equipmentPerformanceFrames.Count(item => item.Focused);
        var speed = _session.CurrentTick / (80 * seconds);
        var completed = _session.CaptureEquipment()!.JobStage == MaintenanceStage.Completed;
        var passed = speed >= .95 && Percentile(.95) <= 25 && focusedFrames == sorted.Length && completed && !_preparationSaveBlocked;
        File.WriteAllText(_equipmentPerformanceOutput!, JsonSerializer.Serialize(new
        {
            diagnostic = _nativeTimingControlDiagnostic ? "R0.02a-native-render-timing-control" :
                _liveMeasurementTier == 0 ? "R0.02-equipment" : "R0.02a-live-performance",
            passed, vsyncDiagnostic = _equipmentVsyncDiagnostic, cappedDiagnostic = _equipmentCappedDiagnostic,
            nativeTimingControl = _nativeTimingControlDiagnostic, initialStallsReproduced = _nativeTimingInitialReproduced,
            nativeTimingTransitions = _nativeTimingTransitions.Select(item => new { window = item.Window, seconds = item.Seconds, enabled = item.Enabled }).ToArray(),
            originalMaxFps = _equipmentOriginalMaxFps, restoredMaxFps = Engine.MaxFps,
            originalVsync = _equipmentOriginalVsync?.ToString(), restoredVsync = DisplayServer.WindowGetVsyncMode().ToString(),
            transitions = _equipmentVsyncTransitions.Select(item => new { window = item.Window, seconds = item.Seconds, actualMode = item.Mode }).ToArray(),
            screenshotsEnabled = false, ordinaryAutosavesEnabled = true, durationSeconds = seconds,
            ticks = _session.CurrentTick, attainedSpeed = speed, people = _session.CapturePreparation()!.People.Length,
            frameCount = sorted.Length, focusedFrames, unfocusedFrames = sorted.Length - focusedFrames,
            p50 = Percentile(.5), p95 = Percentile(.95), p99 = Percentile(.99), max = sorted[^1],
            over33Ms = sorted.Count(item => item > 33.333), over100Ms = sorted.Count(item => item > 100),
            maintenanceCompleted = completed, saveBlocked = _preparationSaveBlocked,
            peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
            engineVersion = Engine.GetVersionInfo()["string"].AsString(), renderer = RenderingServer.GetCurrentRenderingMethod(),
            adapter = RenderingServer.GetVideoAdapterName(), vsync = DisplayServer.WindowGetVsyncMode().ToString(), size = GetWindow().Size.ToString(),
            maxPhysicsStepsPerFrame = Engine.MaxPhysicsStepsPerFrame, physicsTicksPerSecond = Engine.PhysicsTicksPerSecond,
            timeScale = Engine.TimeScale, maxFps = Engine.MaxFps, tickCap = FoundationClock.MaximumTicksPerFrame, debtCap = FoundationClock.MaximumDebtTicks,
            equipment = _session.CaptureEquipment(), livePerformance = _session.CaptureLivePerformance(), frames = _equipmentPerformanceFrames
        }, new JsonSerializerOptions { WriteIndented = true }));
        GetTree().Quit(_nativeTimingControlDiagnostic || passed ? 0 : 2);
    }

    public override void _ExitTree()
    {
        if (_equipmentOriginalVsync is { } original) DisplayServer.WindowSetVsyncMode(original);
        if (_equipmentOriginalMaxFps is { } originalFps) Engine.MaxFps = originalFps;
        if (_nativeTimingControlDiagnostic) RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), false);
    }
}

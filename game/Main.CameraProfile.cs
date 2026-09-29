using Festival.Simulation;
using Festival.Persistence;
using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Festival.Game;

public partial class Main
{
    private sealed record CameraProfileFrame(double Seconds, string Phase, double IntervalMs, double CallbackMs,
        double OutsideCallbackMs, double SimulationMs, double PresentationMs, long AllocatedBytes,
        int Gen0, int Gen1, int Gen2, long Tick, long AutosaveGeneration, bool Focused,
        float FocusX, float FocusZ, int Orientation, float Zoom, bool BoundaryPending,
        bool ToiletOccupied = false, int ToiletQueue = 0, int TravellingVisuals = 0, int MovingVisuals = 0,
        bool PeriodicPending = false);

    private string? _cameraProfileOutput;
    private string? _cameraProfileMode;
    private readonly List<CameraProfileFrame> _cameraProfileFrames = [];
    private readonly Dictionary<EntityId, Vector3> _cameraProfilePriorPositions = [];
    private readonly ConcurrentQueue<(string Stage, double Milliseconds)> _cameraProfileSaveStages = new();
    private long _cameraProfileStarted;
    private long _cameraProfilePrevious;
    private long _cameraProfileCallbackStarted;
    private long _cameraProfileAllocatedStart;
    private Vector3 _cameraProfileHomeFocus;
    private float _cameraProfileHomeZoom;
    private long _cameraProfileInitialTick;
    private string _cameraProfileInitialHash = "";
    private bool _cameraProfileAttemptedLoadWhilePending;
    private string _cameraProfilePhase = "cold-idle";
    private double _cameraProfileSeconds;
    private readonly List<object> _cameraProfileStaffActions = [];
    private int _cameraProfileStaffActionIndex;
    private long _cameraProfileStaffOpeningCash;

    private void PrepareCameraProfile()
    {
        if (_cameraProfileOutput is null) return;
        PersistenceTiming.Observer = (stage, ms) => _cameraProfileSaveStages.Enqueue((stage, ms));
        if (_cameraProfileMode is "live" or "failure" or "live-toilet" or "live-periodic" or "live-slow-boundary")
        {
            var perk = _session.CapturePerks()!;
            CommitEquipmentAction(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
            CommitEquipmentAction(new SetProgrammeCommand(["act.meadow-lanterns", "act.neon-postcards", "act.field-frequency"]));
            PreparationAccept("staff.steward");
            PreparationStart();
            if (_session.PreparedStatus != PreparationStatus.Running)
                throw new InvalidOperationException("Camera profile could not start the current booking campaign: " + _preparationMessage);
            _session.AdvanceWithoutSnapshot(1800);
            if (_cameraProfileMode == "live-toilet")
            {
                // Labelled profile fixture: otherwise no guest reaches the need
                // threshold inside this 50-second camera measurement window.
                var ids = _session.CapturePreparation()!.People.Where(person => person.Role == ProtectedPersonRole.Guest)
                    .Skip(3).Take(2).Select(person => person.AgentId).ToHashSet();
                var immersion = _session.CaptureImmersion()!;
                typeof(GameSession).GetField("_immersion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session,
                    immersion with { People = immersion.People.Select(person => ids.Contains(person.AgentId) ?
                        person with { ToiletNeed = 9_000 } : person).ToArray() });
                var medical = _session.CaptureMedical()!;
                typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session,
                    medical with { Needs = medical.Needs.Select(need => ids.Contains(need.AgentId) ?
                        need with { Thirst = 0 } : need).ToArray() });
            }
            _foundationPresentation.Reset(_session.CaptureObservation());
            RefreshPreparationHud();
        }
        else if (_cameraProfileMode == "staff-draft")
        {
            var perk = _session.CapturePerks()!;
            CommitEquipmentAction(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
            SelectPreparationDockDestination("Staff");
            _cameraProfileStaffOpeningCash = _session.CaptureSnapshot().FestivalFinances.Single().CashPennies;
            while (_cameraProfileSaveStages.TryDequeue(out _)) { }
        }
        else if (_cameraProfileMode != "preparation") throw new InvalidOperationException("Unknown camera profile mode.");
        _cameraProfileHomeFocus = _focus;
        _cameraProfileHomeZoom = _camera.Size;
        _cameraProfileInitialTick = _session.CurrentTick;
        _cameraProfileInitialHash = _session.CaptureSnapshot().AuthoritativeHash;
        _cameraProfileStarted = _cameraProfilePrevious = Stopwatch.GetTimestamp();
        GD.Print($"CAMERA_PROFILE_READY mode={_cameraProfileMode} people={_session.CapturePreparation()!.People.Length} performers={_session.CaptureProgramme()?.Performers.Length ?? 0} tick={_cameraProfileInitialTick} hash={_cameraProfileInitialHash}");
    }

    private void BeginCameraProfileFrame()
    {
        if (_cameraProfileOutput is null) return;
        _cameraProfileCallbackStarted = Stopwatch.GetTimestamp();
        _cameraProfileAllocatedStart = GC.GetAllocatedBytesForCurrentThread();
        _cameraProfileSeconds = Stopwatch.GetElapsedTime(_cameraProfileStarted, _cameraProfileCallbackStarted).TotalSeconds;
        var seconds = _cameraProfileSeconds;
        _cameraProfilePhase = seconds < 5 ? "cold-idle" : seconds < 10 ? "warm-idle" : seconds < 20 ? "pan" :
            seconds < 30 ? "rotate" : seconds < 40 ? "zoom" : "warm-idle-after";
        _focus = _cameraProfileHomeFocus;
        _orientation = 0;
        _camera.Size = _cameraProfileHomeZoom;
        if (_cameraProfilePhase == "pan")
        {
            var t = (float)(seconds - 10);
            _focus += new Vector3(Mathf.Sin(t * 1.25f) * 10f, 0, Mathf.Cos(t * .9f) * 8f);
        }
        else if (_cameraProfilePhase == "rotate")
            _orientation = Math.Min(3, (int)((seconds - 20) / 2.5));
        else if (_cameraProfilePhase == "zoom")
            _camera.Size = Mathf.Clamp(_cameraProfileHomeZoom + Mathf.Sin((float)(seconds - 30) * 1.6f) * 17f, MinZoom, MaxZoom);
        ApplyCamera();
    }

    private void FinishCameraProfileFrame()
    {
        if (_cameraProfileOutput is null) return;
        if (_cameraProfileMode == "staff-draft" && _cameraProfileStaffActionIndex < 8 &&
            _cameraProfileSeconds >= 5 + _cameraProfileStaffActionIndex * 2)
        {
            var index = _cameraProfileStaffActionIndex++;
            var started = Stopwatch.GetTimestamp();
            _offerButtons["staff.steward"].EmitSignal(BaseButton.SignalName.Pressed);
            var clickMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var selected = _session.CapturePreparationPlan()!.OfferIds.Contains("staff.steward");
            if (selected != (index % 2 == 0) || _session.CapturePreparation()!.Payments.Length != 0 ||
                _session.CaptureSnapshot().FestivalFinances.Single().CashPennies != _cameraProfileStaffOpeningCash ||
                !_offerButtons["staff.steward"].Text.StartsWith(selected ? "REMOVE" : "HIRE", StringComparison.Ordinal))
                throw new InvalidOperationException($"Staff draft click {index} did not visibly and atomically toggle the unpaid hire.");
            _cameraProfileStaffActions.Add(new { index, seconds = _cameraProfileSeconds, clickToVisibleMs = clickMs,
                selected, generation = _autosaveGeneration, hash = _session.CaptureSnapshot().AuthoritativeHash });
            GD.Print($"STAFF_DRAFT_CLICK index={index} selected={selected} clickToVisibleMs={clickMs:0.###} generation={_autosaveGeneration}");
        }
        if (_cameraProfileMode == "failure" && _boundarySaveTask is not null && _cameraProfileFailureInjected &&
            !_cameraProfileAttemptedLoadWhilePending)
        {
            var source = _session;
            PreparationLoad();
            if (!ReferenceEquals(source, _session)) throw new InvalidOperationException("Load replaced the source while an atomic boundary was pending.");
            _cameraProfileAttemptedLoadWhilePending = true;
        }
        var toilet = _cameraProfileMode == "live-toilet" ? _session.CaptureToilet() : null;
        var travellingVisuals = 0;
        var movingVisuals = 0;
        foreach (var agent in _session.CaptureObservation().NavigationAgents)
        {
            if (!_attendeeVisuals.TryGetValue(agent.Id, out var visual)) continue;
            if (agent.Action == AgentNavigationAction.Travelling)
            {
                travellingVisuals++;
                if (_cameraProfilePriorPositions.TryGetValue(agent.Id, out var prior) &&
                    visual.Position.DistanceSquaredTo(prior) > 0.00000001f) movingVisuals++;
            }
            _cameraProfilePriorPositions[agent.Id] = visual.Position;
        }
        var now = Stopwatch.GetTimestamp();
        var interval = Stopwatch.GetElapsedTime(_cameraProfilePrevious, now).TotalMilliseconds;
        var callback = Stopwatch.GetElapsedTime(_cameraProfileCallbackStarted, now).TotalMilliseconds;
        _cameraProfileFrames.Add(new(_cameraProfileSeconds, _cameraProfilePhase, interval, callback,
            interval - callback, _profileSimulationMs, _profilePresentationMs,
            GC.GetAllocatedBytesForCurrentThread() - _cameraProfileAllocatedStart,
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), _session.CurrentTick,
            _autosaveGeneration, DisplayServer.WindowIsFocused(), _focus.X, _focus.Z, _orientation, _camera.Size,
            _boundarySaveTask is not null, toilet?.OwnerId is not null, toilet?.Queue.Length ?? 0,
            travellingVisuals, movingVisuals, _periodicSaveTask is not null));
        _cameraProfilePrevious = now;
        if (_cameraProfileMode == "failure")
        {
            if (!_preparationSaveBlocked) return;
            var sourceHash = _session.CaptureSnapshot().AuthoritativeHash;
            if (!_cameraProfileFailureInjected || !_cameraProfileAttemptedLoadWhilePending ||
                _session.CurrentTick != _boundarySourceTick || sourceHash != _boundarySourceHash ||
                _autosaveGeneration != _boundarySourceGeneration || !_preparationMessage.Contains("retry", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Failed boundary changed the visible source, generation, or retry status.");
            var prior = AutosaveRotation.LoadNewestValid(SaveDirectory, _saveCompatibility);
            if (!prior.IsSuccess || Directory.GetFiles(SaveDirectory, "*.tmp").Length != 0)
                throw new InvalidOperationException("Failed boundary damaged the prior valid autosave or left a temporary file.");
            var pendingPan = _cameraProfileFrames.Where(frame => frame.BoundaryPending && frame.Phase == "pan").ToArray();
            var pendingPanRange = pendingPan.Select(frame => frame.FocusX).DefaultIfEmpty().Max() -
                pendingPan.Select(frame => frame.FocusX).DefaultIfEmpty().Min();
            if (pendingPan.Length < 2 || pendingPanRange <= .001f)
                throw new InvalidOperationException("Camera did not continue panning while the failed save was pending.");
            File.WriteAllText(_cameraProfileOutput, JsonSerializer.Serialize(new
            {
                mode = "failure", sourceTick = _session.CurrentTick, sourceHash,
                priorValidTick = prior.Session!.CurrentTick, priorHash = prior.Session.CaptureSnapshot().AuthoritativeHash,
                failurePreservedSource = true, priorAutosaveValid = true, pendingLoadRejected = true,
                pendingCameraFrames = pendingPan.Length, pendingPanFocusRange = pendingPanRange,
                frames = _cameraProfileFrames
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"CAMERA_PROFILE_FAILURE_COMPLETE tick={_session.CurrentTick} prior={prior.Session.CurrentTick} hash={sourceHash}");
            GetTree().Quit();
            return;
        }
        if (_cameraProfileMode == "staff-draft" && _cameraProfileSeconds >= 22)
        {
            if (_draftSavePipeline?.HasPending == true)
            {
                if (_cameraProfileSeconds >= 60)
                    throw new InvalidOperationException("Staff draft saves did not settle within 60 seconds.");
                return;
            }
            var hash = _session.CaptureSnapshot().AuthoritativeHash;
            var loaded = SaveFileAdapter.LoadSlot(SaveDirectory, AutosaveRotation.SlotForGeneration(_autosaveGeneration - 1), _saveCompatibility);
            if (!loaded.IsSuccess || loaded.Session!.CaptureSnapshot().AuthoritativeHash != hash || _autosaveGeneration < 9)
                throw new InvalidOperationException("Staff draft final save/reload or sequence was not exact: " + loaded.Error);
            File.WriteAllText(_cameraProfileOutput, JsonSerializer.Serialize(new
            {
                mode = "staff-draft", actions = _cameraProfileStaffActions,
                stages = _cameraProfileSaveStages.Select(item => new { stage = item.Stage, milliseconds = item.Milliseconds }).ToArray(),
                exactFinalReload = true, finalHash = hash, frames = _cameraProfileFrames
            }, new JsonSerializerOptions { WriteIndented = true }));
            PersistenceTiming.Observer = null;
            GD.Print($"STAFF_DRAFT_PROFILE_COMPLETE actions={_cameraProfileStaffActions.Count} exactReload=True");
            GetTree().Quit();
            return;
        }
        if (_cameraProfileSeconds < 50 || _boundarySaveTask is not null || _periodicSaveTask is not null) return;
        var frames = _cameraProfileFrames;
        var sorted = frames.Select(frame => frame.IntervalMs).Order().ToArray();
        double P(double p) => sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * p) - 1, 0, sorted.Length - 1)];
        var finalHash = _session.CaptureSnapshot().AuthoritativeHash;
        var finalSave = SaveFileAdapter.SaveSlot(SaveDirectory, "camera-profile-final",
            new SaveWriteRequest(_session, _saveCompatibility, "current-version-camera-profile", DateTimeOffset.UtcNow));
        if (!finalSave.IsSuccess) throw new InvalidOperationException("Camera profile final save failed: " + finalSave.Error);
        var finalReload = SaveFileAdapter.LoadSlot(SaveDirectory, "camera-profile-final", _saveCompatibility);
        if (!finalReload.IsSuccess || finalReload.Session!.CaptureSnapshot().AuthoritativeHash != finalHash)
            throw new InvalidOperationException("Camera profile final current-version save/reload was not exact: " + finalReload.Error);
        File.WriteAllText(_cameraProfileOutput, JsonSerializer.Serialize(new
        {
            mode = _cameraProfileMode, seed = 20260922, durationSeconds = _cameraProfileSeconds,
            people = _session.CapturePreparation()!.People.Length,
            performers = _session.CaptureProgramme()?.Performers.Length ?? 0,
            renderer = RenderingServer.GetCurrentRenderingMethod(), adapter = RenderingServer.GetVideoAdapterName(),
            vsync = DisplayServer.WindowGetVsyncMode().ToString(), maxFps = Engine.MaxFps,
            viewport = GetWindow().Size.ToString(), engineVersion = Engine.GetVersionInfo()["string"].AsString(),
            initialTick = _cameraProfileInitialTick, initialHash = _cameraProfileInitialHash,
            finalTick = _session.CurrentTick, finalHash, exactFinalReload = true,
            toiletWees = _session.CaptureToilet()?.WeeCount,
            toiletPoos = _session.CaptureToilet()?.PooCount,
            frameCount = frames.Count, focusedFrames = frames.Count(frame => frame.Focused),
            p50 = P(.5), p95 = P(.95), p99 = P(.99), max = sorted[^1],
            over33Ms = sorted.Count(ms => ms > 33.333), over100Ms = sorted.Count(ms => ms > 100),
            peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
            saveStages = _cameraProfileSaveStages.Select(item => new { stage = item.Stage, milliseconds = item.Milliseconds }).ToArray(),
            frames
        }, new JsonSerializerOptions { WriteIndented = true }));
        PersistenceTiming.Observer = null;
        GD.Print($"CAMERA_PROFILE_COMPLETE mode={_cameraProfileMode} frames={frames.Count} p95={P(.95):0.###} p99={P(.99):0.###} max={sorted[^1]:0.###} over100={sorted.Count(ms => ms > 100)} focused={frames.Count(frame => frame.Focused)} tick={_session.CurrentTick}");
        GetTree().Quit();
    }
}

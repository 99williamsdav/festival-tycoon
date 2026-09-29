using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Festival.Game;

public partial class Main
{
    private Task<SaveOperationResult>? _boundarySaveTask;
    private sealed record PendingBoundarySave(Task<SaveOperationResult> Save, SessionPersistenceSnapshot Checkpoint, long Generation);
    private readonly Queue<PendingBoundarySave> _boundaryPendingSaves = new();
    private SessionPersistenceSnapshot? _boundaryLastDurableSnapshot;
    private long _boundaryNextGeneration;
    private bool _boundaryBatchFailed;
    private GameSession? _boundarySourceSession;
    private SessionPersistenceSnapshot? _boundarySourceSnapshot;
    private Task<SaveOperationResult>? _periodicSaveTask;
    private long _boundarySourceTick;
    private long _boundarySourceGeneration;
    private string _boundarySourceHash = "";
    private bool _cameraProfileFailureInjected;
    private bool UseResponsiveBoundarySaves => _cameraProfileOutput is not null || OS.GetCmdlineUserArgs().Length == 0;
    private static bool BoundaryOnNextTick(GameSession session) => session.ImmersionBoundaryOnNextTick ||
        session.PreparationBoundaryOnNextTick || session.EquipmentBoundaryOnNextTick ||
        session.LivePerformanceBoundaryOnNextTick || session.MedicalBoundaryOnNextTick || session.DisorderBoundaryOnNextTick;

    private void StartResponsiveBoundarySave()
    {
        // Each boundary crosses immediately. Immutable checkpoints are persisted
        // in order; a failed write rolls back to the last durable checkpoint.
        var source = _session;
        var priorBoundary = _boundarySaveTask;
        var priorPeriodic = priorBoundary is null ? _periodicSaveTask : null;
        if (priorBoundary is null)
        {
            _boundarySourceSession = source;
            _boundarySourceTick = source.CurrentTick;
            _boundarySourceGeneration = _autosaveGeneration;
            var sourceCaptureStarted = PersistenceTiming.Start();
            _boundarySourceHash = source.CaptureSnapshot().AuthoritativeHash;
            _boundarySourceSnapshot = source.CapturePersistenceSnapshot();
            _boundaryLastDurableSnapshot = _boundarySourceSnapshot;
            PersistenceTiming.Record("live.boundary-source-capture", sourceCaptureStarted);
            _boundaryNextGeneration = _autosaveGeneration + (priorPeriodic is null ? 0 : 1);
            _boundaryBatchFailed = false;
        }
        source.AdvanceWithoutSnapshot(1);
        _foundationPresentation.Advance(source.CaptureObservation());
        var captureStarted = PersistenceTiming.Start();
        var checkpoint = source.CapturePersistenceSnapshot();
        PersistenceTiming.Record("live.boundary-checkpoint-capture", captureStarted);
        var directory = SaveDirectory;
        var compatibility = _saveCompatibility;
        var generation = _boundaryNextGeneration++;
        var now = DateTimeOffset.UtcNow;
        Action<SaveFailurePoint>? failureInjector = null;
        if (_cameraProfileMode == "failure" && !_cameraProfileFailureInjected && source.CurrentTick >= 2900)
        {
            _cameraProfileFailureInjected = true;
            failureInjector = _ => throw new IOException("Scripted camera-profile boundary save failure");
        }
        else if (_cameraProfileMode == "live-slow-boundary")
            failureInjector = _ => Thread.Sleep(650); // labelled slow-disk diagnostic, worker only
        _boundarySaveTask = Task.Run(async () =>
        {
            if (priorBoundary is not null)
            {
                var earlier = await priorBoundary.ConfigureAwait(false);
                if (!earlier.IsSuccess) return SaveOperationResult.Failure("Dependent boundary was not saved after an earlier failure.");
            }
            if (priorPeriodic is not null)
            {
                try { await priorPeriodic.ConfigureAwait(false); }
                catch { /* The periodic failure is reported separately; the boundary still needs a slot. */ }
            }
            return AutosaveRotation.SaveCaptured(directory, checkpoint, compatibility, now, generation, failureInjector);
        });
        _boundaryPendingSaves.Enqueue(new(_boundarySaveTask, checkpoint, generation));
        if (priorBoundary is null)
        {
            _preparationMessage = "Saving the weekend boundary; movement and camera continue.";
            RefreshPreparationHud();
        }
    }

    private bool FinishResponsiveBoundarySave()
    {
        var processed = false;
        while (_boundaryPendingSaves.TryPeek(out var pending) && pending.Save.IsCompleted)
        {
            processed = true;
            _boundaryPendingSaves.Dequeue();
            SaveOperationResult result;
            try { result = pending.Save.GetAwaiter().GetResult(); }
            catch (Exception error) { result = SaveOperationResult.Failure(error.Message); }
            if (_boundaryBatchFailed) continue;
            if (!ReferenceEquals(_session, _boundarySourceSession) || _session.CurrentTick < _boundarySourceTick + 1)
            {
                _boundaryBatchFailed = true;
                _preparationSaveBlocked = true;
                _preparationMessage = "Boundary save finished after the active session changed. Reload the current campaign.";
                _foundationClock.ResetBoundary(); RefreshPreparationHud();
                continue;
            }
            if (!result.IsSuccess)
            {
                _boundaryBatchFailed = true;
                RollBackFailedBoundary("Edition boundary not applied; fix the save location then retry. " + result.Error);
                continue;
            }
            _boundaryLastDurableSnapshot = pending.Checkpoint;
            _autosaveGeneration = Math.Max(_autosaveGeneration, pending.Generation + 1);
        }
        if (_boundaryPendingSaves.Count == 0 && processed)
        {
            _boundarySaveTask = null;
            _boundarySourceSession = null;
            _boundarySourceSnapshot = null;
            _boundaryLastDurableSnapshot = null;
            if (!_boundaryBatchFailed)
                _preparationMessage = $"{_session.CapturePreparation()!.Status} boundary autosaved.";
        }
        return processed;
    }

    private void RollBackFailedBoundary(string error)
    {
        var restored = GameSession.Restore(_boundaryLastDurableSnapshot!);
        if (!restored.IsSuccess) throw new InvalidOperationException("Could not restore the pre-save boundary: " + restored.Error);
        _session = restored.Session!;
        _preparationSaveBlocked = true;
        _preparationMessage = error;
        _foundationClock.ResetBoundary();
        _foundationPresentation.Reset(_session.CaptureObservation());
        var active = _session.CapturePreparation()!.People.Where(person => !person.Departed)
            .Select(person => new EntityId(person.AgentId)).ToHashSet();
        foreach (var (id, visual) in _attendeeVisuals)
        {
            if (!active.Contains(id)) continue;
            visual.Show();
            foreach (var child in visual.FindChildren("*", "StaticBody3D", true, false))
            {
                if (child is not StaticBody3D body) continue;
                body.CollisionLayer = 1;
                _attendeePickRegistry[body.GetInstanceId()] = id;
            }
        }
        RefreshPreparationHud();
    }

    private void StartPeriodicAutosave()
    {
        var captureStarted = PersistenceTiming.Start();
        var checkpoint = _session.CapturePersistenceSnapshot();
        PersistenceTiming.Record("live.periodic-capture", captureStarted);
        var directory = SaveDirectory;
        var compatibility = _saveCompatibility;
        var generation = _autosaveGeneration;
        var now = DateTimeOffset.UtcNow;
        _periodicSaveTask = Task.Run(() => AutosaveRotation.SaveCaptured(directory, checkpoint, compatibility, now, generation));
    }

    private void FinishPeriodicAutosave()
    {
        if (_periodicSaveTask is not { IsCompleted: true } pending) return;
        _periodicSaveTask = null;
        try
        {
            var result = pending.GetAwaiter().GetResult();
            if (result.IsSuccess) _autosaveGeneration++;
            else { _preparationMessage = $"Periodic autosave failed: {result.Error}"; RefreshPreparationHud(); }
        }
        catch (Exception error)
        {
            _preparationMessage = $"Periodic autosave failed: {error.Message}";
            RefreshPreparationHud();
        }
    }

    private static bool CameraOnlyInput(InputEvent input) => input switch
    {
        InputEventMouseMotion => true,
        InputEventMouseButton mouse when mouse.ButtonIndex is MouseButton.Middle or MouseButton.WheelUp or MouseButton.WheelDown => true,
        InputEventKey key when key.Keycode is Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Down or Key.Left or Key.Right or Key.Q or Key.E => true,
        _ => false,
    };

    private bool RejectActionDuringBoundarySave()
    {
        if (_boundarySaveTask is null && _periodicSaveTask is null) return false;
        _preparationMessage = "Saving the campaign; try again in a moment.";
        RefreshPreparationHud();
        return true;
    }

    private bool RejectActionDuringDraftSave()
    {
        if (_draftSavePipeline?.HasPending != true) return false;
        _preparationMessage = "Staff changes are saving; this other action can be retried when they finish.";
        RefreshPreparationHud();
        return true;
    }
}

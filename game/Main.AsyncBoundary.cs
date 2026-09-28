using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Festival.Game;

public partial class Main
{
    private Task<SaveOperationResult>? _boundarySaveTask;
    private GameSession? _boundarySourceSession;
    private SessionPersistenceSnapshot? _boundarySourceSnapshot;
    private Task<SaveOperationResult>? _periodicSaveTask;
    private long _boundarySourceTick;
    private long _boundarySourceGeneration;
    private bool _boundaryFollowedPeriodic;
    private string _boundarySourceHash = "";
    private bool _cameraProfileFailureInjected;
    private bool UseResponsiveBoundarySaves => _cameraProfileOutput is not null || OS.GetCmdlineUserArgs().Length == 0;
    private static bool BoundaryOnNextTick(GameSession session) => session.ImmersionBoundaryOnNextTick ||
        session.PreparationBoundaryOnNextTick || session.EquipmentBoundaryOnNextTick ||
        session.LivePerformanceBoundaryOnNextTick || session.MedicalBoundaryOnNextTick || session.DisorderBoundaryOnNextTick;

    private void StartResponsiveBoundarySave(int ticksAfterBoundary)
    {
        // The live session crosses the boundary immediately. The worker persists an
        // immutable checkpoint; a failed write rolls the speculative ticks back.
        _foundationClock.RequeueUnprocessedTicks(ticksAfterBoundary);
        var source = _session;
        _boundarySourceSession = source;
        _boundarySourceTick = source.CurrentTick;
        _boundarySourceGeneration = _autosaveGeneration;
        var priorPeriodic = _periodicSaveTask;
        _boundaryFollowedPeriodic = priorPeriodic is not null;
        _boundarySourceHash = source.CaptureSnapshot().AuthoritativeHash;
        _boundarySourceSnapshot = source.CapturePersistenceSnapshot();
        source.AdvanceWithoutSnapshot(1);
        _foundationPresentation.Advance(source.CaptureObservation());
        var checkpoint = source.CapturePersistenceSnapshot();
        var directory = SaveDirectory;
        var compatibility = _saveCompatibility;
        var generation = _autosaveGeneration + (_boundaryFollowedPeriodic ? 1 : 0);
        var now = DateTimeOffset.UtcNow;
        Action<SaveFailurePoint>? failureInjector = null;
        if (_cameraProfileMode == "failure" && !_cameraProfileFailureInjected && source.CurrentTick >= 2900)
        {
            _cameraProfileFailureInjected = true;
            failureInjector = _ => throw new IOException("Scripted camera-profile boundary save failure");
        }
        _boundarySaveTask = Task.Run(async () =>
        {
            if (priorPeriodic is not null)
            {
                try { await priorPeriodic.ConfigureAwait(false); }
                catch { /* The periodic failure is reported separately; the boundary still needs a slot. */ }
            }
            return AutosaveRotation.SaveCaptured(directory, checkpoint, compatibility, now, generation, failureInjector);
        });
        _preparationMessage = "Saving the weekend boundary; camera remains responsive.";
        RefreshPreparationHud();
    }

    private bool FinishResponsiveBoundarySave()
    {
        if (_boundarySaveTask is not { IsCompleted: true } pending) return false;
        _boundarySaveTask = null;
        if (!ReferenceEquals(_session, _boundarySourceSession) || _session.CurrentTick < _boundarySourceTick + 1 ||
            (_autosaveGeneration != _boundarySourceGeneration &&
             !(_boundaryFollowedPeriodic && _autosaveGeneration == _boundarySourceGeneration + 1)))
        {
            _preparationSaveBlocked = true;
            _preparationMessage = "Boundary save finished after the active session changed; result was not installed. Reload the current campaign.";
            _foundationClock.ResetBoundary(); RefreshPreparationHud();
            _boundarySourceSession = null;
            _boundarySourceSnapshot = null;
            _ = pending.Exception;
            return true;
        }
        SaveOperationResult result;
        try { result = pending.GetAwaiter().GetResult(); }
        catch (Exception error)
        {
            RollBackFailedBoundary("Edition boundary not applied; fix the save location then retry. " + error.Message);
            return true;
        }
        if (!result.IsSuccess)
        {
            RollBackFailedBoundary("Edition boundary not applied; fix the save location then retry. " + result.Error);
            return true;
        }
        _boundarySourceSession = null;
        _boundarySourceSnapshot = null;
        _autosaveGeneration = Math.Max(_autosaveGeneration, _boundarySourceGeneration + (_boundaryFollowedPeriodic ? 1 : 0)) + 1;
        _preparationMessage = $"{_session.CapturePreparation()!.Status} boundary autosaved.";
        return true;
    }

    private void RollBackFailedBoundary(string error)
    {
        var restored = GameSession.Restore(_boundarySourceSnapshot!);
        if (!restored.IsSuccess) throw new InvalidOperationException("Could not restore the pre-save boundary: " + restored.Error);
        _session = restored.Session!;
        _boundarySourceSession = null;
        _boundarySourceSnapshot = null;
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
        var checkpoint = _session.CapturePersistenceSnapshot();
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
}

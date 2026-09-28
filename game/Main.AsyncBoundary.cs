using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Festival.Game;

public partial class Main
{
    private Task<PreparationAdvanceResult>? _boundarySaveTask;
    private GameSession? _boundarySourceSession;
    private long _boundarySourceTick;
    private long _boundarySourceGeneration;
    private string _boundarySourceHash = "";
    private bool _cameraProfileFailureInjected;
    private bool UseResponsiveBoundarySaves => _cameraProfileOutput is not null || OS.GetCmdlineUserArgs().Length == 0;
    private static bool BoundaryOnNextTick(GameSession session) => session.ImmersionBoundaryOnNextTick ||
        session.PreparationBoundaryOnNextTick || session.EquipmentBoundaryOnNextTick ||
        session.LivePerformanceBoundaryOnNextTick || session.MedicalBoundaryOnNextTick || session.DisorderBoundaryOnNextTick;

    private void StartResponsiveBoundarySave(int ticksAfterBoundary)
    {
        // The worker commits the boundary tick once; only its scheduled tail is debt.
        _foundationClock.RequeueUnprocessedTicks(ticksAfterBoundary);
        var source = _session;
        _boundarySourceSession = source;
        _boundarySourceTick = source.CurrentTick;
        _boundarySourceGeneration = _autosaveGeneration;
        _boundarySourceHash = source.CaptureSnapshot().AuthoritativeHash;
        var snapshot = source.CapturePersistenceSnapshot();
        var directory = SaveDirectory;
        var compatibility = _saveCompatibility;
        var generation = _autosaveGeneration;
        var now = DateTimeOffset.UtcNow;
        Action<SaveFailurePoint>? failureInjector = null;
        if (_cameraProfileMode == "failure" && !_cameraProfileFailureInjected && source.CurrentTick >= 2900)
        {
            _cameraProfileFailureInjected = true;
            failureInjector = _ => throw new IOException("Scripted camera-profile boundary save failure");
        }
        _boundarySaveTask = Task.Run(() => PreparationAdvanceCoordinator.AdvanceCapturedBoundary(
            directory, source, snapshot, compatibility, now, generation, failureInjector));
        _preparationMessage = "Saving the weekend boundary; camera remains responsive.";
        RefreshPreparationHud();
    }

    private bool FinishResponsiveBoundarySave()
    {
        if (_boundarySaveTask is not { IsCompleted: true } pending) return false;
        _boundarySaveTask = null;
        if (!ReferenceEquals(_session, _boundarySourceSession) || _session.CurrentTick != _boundarySourceTick ||
            _autosaveGeneration != _boundarySourceGeneration)
        {
            _preparationSaveBlocked = true;
            _preparationMessage = "Boundary save finished after the active session changed; result was not installed. Reload the current campaign.";
            _foundationClock.ResetBoundary(); RefreshPreparationHud();
            _boundarySourceSession = null;
            _ = pending.Exception;
            return true;
        }
        _boundarySourceSession = null;
        PreparationAdvanceResult result;
        try { result = pending.GetAwaiter().GetResult(); }
        catch (Exception error)
        {
            _preparationSaveBlocked = true;
            _preparationMessage = "Edition boundary not applied; fix the save location then retry. " + error.Message;
            _foundationClock.ResetBoundary(); RefreshPreparationHud();
            return true;
        }
        if (!result.IsSuccess)
        {
            _preparationSaveBlocked = true;
            _preparationMessage = result.Error!;
            _foundationClock.ResetBoundary(); RefreshPreparationHud();
            return true;
        }
        _session = result.Session;
        _autosaveGeneration++;
        _preparationMessage = $"{_session.CapturePreparation()!.Status} boundary autosaved.";
        _foundationPresentation.Advance(_session.CaptureObservation());
        return true;
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
        if (_boundarySaveTask is null) return false;
        _preparationMessage = "Saving the weekend boundary; try again in a moment.";
        RefreshPreparationHud();
        return true;
    }
}

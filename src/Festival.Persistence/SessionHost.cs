using Festival.Simulation;
using System;
using System.Threading.Tasks;

namespace Festival.Persistence;

/// <summary>
/// Owns the running campaign: the one <see cref="GameSession"/>, its fixed-step clock and its saves.
/// Views read <see cref="Session"/> and send every change through <see cref="Submit"/>,
/// <see cref="Execute"/> or <see cref="ExecuteMilestone"/>; nothing else replaces the session.
///
/// There is one save policy. Changed state is written in the background at most every
/// 30 unpaused seconds; festival start, failure, results and Council decisions are saved
/// immediately, and a milestone command only takes effect once its save succeeds. A failed
/// save keeps play going and retries; any earlier valid slot stays intact.
/// </summary>
public sealed class SessionHost
{
    public const string ManualSlot = "manual-preparation";

    private readonly RealTimeAutosaveScheduler _autosave = new();
    private long _generation;
    private long _revision = 1;
    private long _savedRevision;
    private long _savingRevision;
    private long _savingGeneration;
    private Task<SaveOperationResult>? _cadenceSave;
    private bool _cadenceDue;

    public SessionHost(GameSession session, string saveDirectory, SaveCompatibility compatibility)
    {
        Session = session;
        SaveDirectory = saveDirectory;
        Compatibility = compatibility;
        _generation = AutosaveRotation.NextGeneration(saveDirectory, compatibility);
    }

    public GameSession Session { get; private set; }
    public string SaveDirectory { get; }
    public SaveCompatibility Compatibility { get; }
    /// <summary>Fixed 80-tick/s scheduling and render interpolation for the session.</summary>
    public FoundationClock Clock { get; } = new();
    /// <summary>The latest save outcome worth telling the player, once; null when there is none.</summary>
    public string? Notice { get; private set; }
    /// <summary>Set while the latest save attempt failed; cleared by the next successful save.</summary>
    public string? SaveError { get; private set; }

    public string? TakeNotice() { var notice = Notice; Notice = null; return notice; }

    public CommandEnvelope Envelope(SessionCommand command) => new(
        new CommandId(1_010_000UL + Session.NextSubmissionSequence), Session.CampaignId, Session.Phase,
        Session.CurrentTick, Session.NextSubmissionSequence, null, command);

    /// <summary>Applies a command directly; accepted changes are saved with the next cadence write.</summary>
    public CommandResult Submit(SessionCommand command)
    {
        var result = Session.Execute(Envelope(command));
        if (result.IsAccepted) MarkChanged();
        return result;
    }

    /// <summary>Applies a command, restoring the prior state exactly if it is rejected.</summary>
    public bool Execute(SessionCommand command, out string? error)
    {
        var before = Session.CapturePersistenceSnapshot();
        var result = Session.Execute(Envelope(command));
        error = result.IsAccepted ? null : result.Message;
        if (result.IsAccepted) { MarkChanged(); return true; }
        Session = Restore(before, "Rejected command rollback failed: ");
        return false;
    }

    /// <summary>A command that only takes effect once its resulting state is durable.</summary>
    public bool ExecuteMilestone(SessionCommand command, string reason, out string? error)
    {
        var before = Session.CapturePersistenceSnapshot();
        if (!Execute(command, out error)) return false;
        if (SaveMilestone(reason)) return true;
        Session = Restore(before, "Milestone rollback failed: ");
        MarkChanged();
        error = Notice;
        return false;
    }

    /// <summary>Writes the current state now (after any background write finishes).</summary>
    public bool SaveMilestone(string reason, bool force = true)
    {
        if (_cadenceSave is not null)
        {
            try { _cadenceSave.GetAwaiter().GetResult(); }
            catch { /* Poll reports the failure; the milestone still gets its own attempt. */ }
            PollCadenceSave();
        }
        if (!force && _revision == _savedRevision) return true;
        var revision = _revision;
        SaveOperationResult result;
        try { result = AutosaveRotation.SaveCaptured(SaveDirectory, Session.CapturePersistenceSnapshot(), Compatibility, DateTimeOffset.UtcNow, _generation); }
        catch (Exception exception) { result = SaveOperationResult.Failure(exception.Message); }
        if (!result.IsSuccess)
        {
            Notice = reason + " save failed; any earlier valid slot remains intact. " + result.Error;
            SaveError = "The latest changes remain in play. Any earlier valid save remains intact. Select Retry save before leaving.";
            return false;
        }
        _savedRevision = revision;
        _generation++;
        _autosave.Rebase();
        _cadenceDue = false;
        Notice = reason + " saved.";
        SaveError = null;
        return true;
    }

    /// <summary>
    /// Advances the simulation by the ticks this frame is owed. <paramref name="onTick"/> sees every
    /// tick (for render interpolation); stops early at failure or results, which are saved at once.
    /// </summary>
    public int Advance(double delta, Action<GameSession> onTick)
    {
        PollCadenceSave();
        Clock.IsPaused = Session.IsPaused || Session.CapturePreparation()!.Status is not (PreparationStatus.Running or PreparationStatus.Departing);
        var ticks = Clock.Schedule(delta);
        var previousStatus = Session.PreparedStatus;
        var hadResult = Session.CompletedFestivalResult is not null;
        for (var tick = 0; tick < ticks; tick++)
        {
            Session.AdvanceWithoutSnapshot(1);
            onTick(Session);
            MarkChanged();
            var failed = previousStatus != PreparationStatus.Failed && Session.PreparedStatus == PreparationStatus.Failed;
            var finished = !hadResult && Session.CompletedFestivalResult is not null;
            if (failed || finished) { SaveMilestone(failed ? "Failure" : "Results"); return tick + 1; }
            previousStatus = Session.PreparedStatus;
            hadResult = Session.CompletedFestivalResult is not null;
        }
        return ticks;
    }

    /// <summary>Starts a background write when the cadence is due and something changed.</summary>
    public void AdvanceSaves(double delta)
    {
        PollCadenceSave();
        if (Session.IsPaused || Session.PreparedStatus is not (PreparationStatus.Preparing or PreparationStatus.Running or PreparationStatus.Departing))
            return;
        if (_autosave.Advance(delta)) _cadenceDue = true;
        if (!_cadenceDue || _cadenceSave is not null) return;
        if (_revision == _savedRevision) { _cadenceDue = false; return; }
        var snapshot = Session.CapturePersistenceSnapshot();
        _savingRevision = _revision;
        _savingGeneration = _generation;
        var directory = SaveDirectory; var compatibility = Compatibility; var generation = _savingGeneration;
        _cadenceSave = Task.Run(() => AutosaveRotation.SaveCaptured(directory, snapshot, compatibility, DateTimeOffset.UtcNow, generation));
        _cadenceDue = false;
        Notice = "Saving changed campaign state in the background.";
    }

    public SaveOperationResult SaveManual() =>
        SaveFileAdapter.SaveSlot(SaveDirectory, ManualSlot, new SaveWriteRequest(Session, Compatibility, "manual", DateTimeOffset.UtcNow));

    /// <summary>Loads the manual slot, replacing the session only if it holds a prepared weekend.</summary>
    public bool LoadManual(out string? error)
    {
        if (_cadenceSave is not null)
        {
            try { _cadenceSave.GetAwaiter().GetResult(); }
            catch { /* Poll reports the failure before the loaded session replaces this one. */ }
            PollCadenceSave();
        }
        var result = SaveFileAdapter.LoadSlot(SaveDirectory, ManualSlot, Compatibility);
        if (!result.IsSuccess || result.Session!.CapturePreparation() is null)
        {
            error = result.Error ?? "Save is not a prepared weekend.";
            return false;
        }
        Session = result.Session;
        MarkChanged(); _autosave.Rebase(); SaveError = null; Clock.ResetBoundary();
        error = null;
        return true;
    }

    /// <summary>Replaces the session with a brand-new campaign that has never been saved.</summary>
    public void StartNewCampaign(GameSession session)
    {
        Session = session;
        _generation = AutosaveRotation.NextGeneration(SaveDirectory, Compatibility);
        _cadenceSave = null; _cadenceDue = false; _revision = 1; _savedRevision = 0;
        SaveError = null;
        _autosave.Rebase();
        Clock.ResetBoundary();
    }

    private void MarkChanged() => _revision++;

    private void PollCadenceSave()
    {
        if (_cadenceSave is not { IsCompleted: true } task) return;
        _cadenceSave = null;
        SaveOperationResult result;
        try { result = task.GetAwaiter().GetResult(); }
        catch (Exception exception) { result = SaveOperationResult.Failure(exception.Message); }
        if (result.IsSuccess)
        {
            _savedRevision = Math.Max(_savedRevision, _savingRevision);
            _generation = Math.Max(_generation, _savingGeneration + 1);
            if (_savedRevision == _revision) Notice = "Background save complete.";
            SaveError = null;
        }
        else
        {
            Notice = "Background save failed; changes remain in play and will retry. " + result.Error;
            SaveError = "The latest changes remain in play. Any earlier valid save remains intact. Retry now or keep playing for the next 30-second attempt.";
        }
    }

    private static GameSession Restore(SessionPersistenceSnapshot snapshot, string failure)
    {
        var restored = GameSession.Restore(snapshot);
        if (!restored.IsSuccess) throw new InvalidOperationException(failure + restored.Error);
        return restored.Session!;
    }
}

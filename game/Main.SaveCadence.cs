using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Festival.Game;

public partial class Main
{
    // Development capture modes retain their historical per-action fixtures.
    // Ordinary play trades at most 30 unpaused seconds of changes for responsive edits.
    private bool RelaxedSaveCadence => (OS.GetCmdlineUserArgs().Length == 0 || _saveCadenceCaptureOutput is not null || _playtestCaptureDirectory is not null) && _session.BuildModeEnabled;
    private long _saveRevision;
    private long _savedRevision;
    private long _savingRevision;
    private long _savingGeneration;
    private Task<SaveOperationResult>? _cadenceSaveTask;
    private bool _cadenceDue;
    private int _cadenceWriteAttempts;
    private int _cadenceMilestoneAttempts;
    private int _cadenceFailures;
    private int _cadenceFixtureFailure = 1;
    private bool _cadenceFailNextMilestone;
    private bool _cadenceSaveError;
    private CanvasLayer? _cadenceErrorLayer;
    private Label? _cadenceErrorLabel;
    private string? _saveCadenceCaptureOutput;
    private int _saveCadenceCaptureStage;
    private long _saveCadenceCaptureStarted;
    private long _saveCadencePendingTick;
    private bool _saveCadenceMovedWhilePending;
    private readonly List<double> _saveCadenceClickMs = [];

    private void MarkSaveDirty() => _saveRevision++;

    private void ShowCadenceSaveError(string message)
    {
        _cadenceSaveError = true;
        if (_cadenceErrorLayer is null)
        {
            _cadenceErrorLayer = new CanvasLayer { Layer = 30 };
            AddChild(_cadenceErrorLayer);
            var width = Math.Min(480, GetViewport().GetVisibleRect().Size.X - 30);
            var panel = HudPanel(_cadenceErrorLayer, new Vector2(15, 65), new Vector2(width, 130));
            var column = new VBoxContainer();
            panel.AddChild(column);
            column.AddChild(HudLabel("Campaign save needs attention", 18));
            _cadenceErrorLabel = HudLabel(message);
            column.AddChild(_cadenceErrorLabel);
            column.AddChild(ButtonText("Retry save", () =>
            {
                SaveCadenceMilestone("Retry");
                RefreshPreparationHud();
            }));
        }
        _cadenceErrorLabel!.Text = message;
        _cadenceErrorLayer.Visible = true;
    }

    private void ClearCadenceSaveError()
    {
        _cadenceSaveError = false;
        if (_cadenceErrorLayer is not null) _cadenceErrorLayer.Visible = false;
    }

    private bool ExecuteWithoutImmediateSave(SessionCommand command, out string? error)
    {
        var before = _session.CapturePersistenceSnapshot();
        var result = _session.Execute(CampaignEnvelope(command));
        error = result.IsAccepted ? null : result.Message;
        if (result.IsAccepted) MarkSaveDirty();
        else
        {
            var restored = GameSession.Restore(before);
            if (!restored.IsSuccess) throw new InvalidOperationException("Rejected command rollback failed: " + restored.Error);
            _session = restored.Session!;
        }
        return result.IsAccepted;
    }

    private void PollCadenceSave()
    {
        if (_cadenceSaveTask is not { IsCompleted: true } task) return;
        _cadenceSaveTask = null;
        SaveOperationResult result;
        try { result = task.GetAwaiter().GetResult(); }
        catch (Exception exception) { result = SaveOperationResult.Failure(exception.Message); }
        if (result.IsSuccess)
        {
            _savedRevision = Math.Max(_savedRevision, _savingRevision);
            _autosaveGeneration = Math.Max(_autosaveGeneration, _savingGeneration + 1);
            if (_savedRevision == _saveRevision) _preparationMessage = "Background save complete.";
            ClearCadenceSaveError();
        }
        else
        {
            _cadenceFailures++;
            _preparationMessage = "Background save failed; changes remain in play and will retry. " + result.Error;
            ShowCadenceSaveError("The latest changes remain in play. Any earlier valid save remains intact. Retry now or keep playing for the next 30-second attempt.");
        }
        RefreshPreparationHud();
    }

    private void AdvanceCadenceSave(double delta)
    {
        PollCadenceSave();
        if (_session.IsPaused || _session.PreparedStatus is not (PreparationStatus.Preparing or PreparationStatus.Running or PreparationStatus.Departing))
            return;
        if (_autosaveScheduler.Advance(delta)) _cadenceDue = true;
        if (!_cadenceDue || _cadenceSaveTask is not null) return;
        if (_saveRevision == _savedRevision) { _cadenceDue = false; return; }
        var snapshot = _session.CapturePersistenceSnapshot();
        _savingRevision = _saveRevision;
        _savingGeneration = _autosaveGeneration;
        var directory = SaveDirectory;
        var compatibility = _saveCompatibility;
        var generation = _savingGeneration;
        _cadenceWriteAttempts++;
        Action<SaveFailurePoint>? injector = _saveCadenceCaptureOutput is null ? null : _ =>
        {
            Thread.Sleep(650); // labelled slow/failing-disk fixture; worker only
            if (Interlocked.Exchange(ref _cadenceFixtureFailure, 0) == 1)
                throw new IOException("Labelled first periodic write failure");
        };
        _cadenceSaveTask = Task.Run(() => AutosaveRotation.SaveCaptured(directory, snapshot, compatibility,
            DateTimeOffset.UtcNow, generation, injector));
        _cadenceDue = false;
        _preparationMessage = "Saving changed campaign state in the background.";
    }

    private bool SaveCadenceMilestone(string reason, bool force = true)
    {
        if (_cadenceSaveTask is not null)
        {
            try { _cadenceSaveTask.GetAwaiter().GetResult(); }
            catch { /* Poll reports the failure; the fresh milestone still gets its own attempt. */ }
            PollCadenceSave();
        }
        if (!force && _saveRevision == _savedRevision) return true;
        var snapshot = _session.CapturePersistenceSnapshot();
        var revision = _saveRevision;
        _cadenceMilestoneAttempts++;
        Action<SaveFailurePoint>? injector = _saveCadenceCaptureOutput is null ? null : _ =>
        {
            if (_cadenceFailNextMilestone)
            {
                _cadenceFailNextMilestone = false;
                throw new IOException("Labelled milestone write failure");
            }
        };
        var result = AutosaveRotation.SaveCaptured(SaveDirectory, snapshot, _saveCompatibility,
            DateTimeOffset.UtcNow, _autosaveGeneration, injector);
        if (!result.IsSuccess)
        {
            _preparationMessage = reason + " save failed; any earlier valid slot remains intact. " + result.Error;
            ShowCadenceSaveError("The latest changes remain in play. Any earlier valid save remains intact. Select Retry save before leaving.");
            RefreshPreparationHud();
            return false;
        }
        _savedRevision = revision;
        _autosaveGeneration++;
        _autosaveScheduler.Rebase();
        _cadenceDue = false;
        _preparationMessage = reason + " saved.";
        ClearCadenceSaveError();
        return true;
    }

    private bool ExecuteMilestoneCommand(SessionCommand command, string reason, out string? error)
    {
        // The milestone (Start / Council decision) is published only if its
        // resulting state is durable. Ordinary edits do not pay this IO cost.
        var before = _session.CapturePersistenceSnapshot();
        if (!ExecuteWithoutImmediateSave(command, out error)) return false;
        if (SaveCadenceMilestone(reason)) return true;
        var restored = GameSession.Restore(before);
        if (!restored.IsSuccess) throw new InvalidOperationException("Milestone rollback failed: " + restored.Error);
        _session = restored.Session!;
        MarkSaveDirty();
        error = _preparationMessage;
        return false;
    }

    private void ProcessSaveCadenceCapture()
    {
        if (_saveCadenceCaptureOutput is null) return;
        try
        {
            if (_saveCadenceCaptureStage == 0)
            {
                GD.Print("SAVE_CADENCE_CAPTURE_SETUP");
                var perk = _session.CapturePerks()!;
                CommitEquipmentAction(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
                CommitEquipmentAction(new UseDefaultBuildLayoutCommand());
                CommitEquipmentAction(new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"]));
                for (var index = 0; index < 9; index++)
                {
                    var clickStarted = Stopwatch.GetTimestamp();
                    _offerButtons["staff.steward"].EmitSignal(BaseButton.SignalName.Pressed);
                    _saveCadenceClickMs.Add(Stopwatch.GetElapsedTime(clickStarted).TotalMilliseconds);
                }
                if (_cadenceWriteAttempts != 0 || _cadenceMilestoneAttempts != 0 ||
                    Directory.Exists(SaveDirectory) && Directory.EnumerateFiles(SaveDirectory).Any())
                    throw new InvalidOperationException("Ordinary draft edits wrote a save before the cadence or opening milestone.");
                PreparationStart();
                if (_session.PreparedStatus != PreparationStatus.Running || _cadenceMilestoneAttempts != 1)
                    throw new InvalidOperationException("Festival start did not create exactly one milestone save.");
                _saveCadenceCaptureStarted = Stopwatch.GetTimestamp();
                _saveCadenceCaptureStage = 1;
                return;
            }
            var seconds = Stopwatch.GetElapsedTime(_saveCadenceCaptureStarted).TotalSeconds;
            if (_saveCadenceCaptureStage == 1 && _cadenceSaveTask is not null)
            {
                _saveCadencePendingTick = _session.CurrentTick;
                ExecuteWithoutImmediateSave(new SetPausedCommand(true), out _);
                ExecuteWithoutImmediateSave(new SetPausedCommand(false), out _);
                _saveCadenceCaptureStage = 2;
            }
            if (_saveCadenceCaptureStage == 2 && _cadenceSaveTask is not null && _session.CurrentTick > _saveCadencePendingTick)
                _saveCadenceMovedWhilePending = true;
            if (seconds < 6.8 || _cadenceSaveTask is not null) return;
            if (_cadenceFailures != 1 || _cadenceWriteAttempts < 2 || !_saveCadenceMovedWhilePending)
                throw new InvalidOperationException($"Failed-write retry, coalescing or movement while pending was not demonstrated: failures={_cadenceFailures} writes={_cadenceWriteAttempts} moved={_saveCadenceMovedWhilePending} stage={_saveCadenceCaptureStage} tick={_session.CurrentTick}.");
            var prior = AutosaveRotation.LoadNewestValid(SaveDirectory, _saveCompatibility);
            if (!prior.IsSuccess) throw new InvalidOperationException("A failed write lost the previous valid slot: " + prior.Error);
            var writesBeforePause = _cadenceWriteAttempts;
            ExecuteWithoutImmediateSave(new SetPausedCommand(true), out _);
            AdvanceCadenceSave(60); // labelled artificial elapsed time; paused play does not accrue cadence
            if (_cadenceWriteAttempts != writesBeforePause)
                throw new InvalidOperationException("Paused play scheduled a background write.");
            ExecuteWithoutImmediateSave(new SetPausedCommand(false), out _);
            if (!SaveCadenceMilestone("Fixture final")) throw new InvalidOperationException(_preparationMessage);
            var loaded = AutosaveRotation.LoadNewestValid(SaveDirectory, _saveCompatibility);
            var hash = _session.CaptureSnapshot().AuthoritativeHash;
            if (!loaded.IsSuccess || loaded.Session!.CaptureSnapshot().AuthoritativeHash != hash)
                throw new InvalidOperationException("Final milestone did not reload exactly: " + loaded.Error);
            var writesBeforeUnchanged = _cadenceWriteAttempts;
            AdvanceCadenceSave(2.1); // labelled artificial due window with no changed state
            if (_cadenceWriteAttempts != writesBeforeUnchanged)
                throw new InvalidOperationException("Unchanged state scheduled a redundant background write.");
            var priorHash = loaded.Session.CaptureSnapshot().AuthoritativeHash;
            _cadenceFailNextMilestone = true;
            if (SaveCadenceMilestone("Labelled terminal") || !_cadenceSaveError || _cadenceErrorLayer?.Visible != true)
                throw new InvalidOperationException("Failed terminal milestone did not expose a visible retry action.");
            var afterFailure = AutosaveRotation.LoadNewestValid(SaveDirectory, _saveCompatibility);
            if (!afterFailure.IsSuccess || afterFailure.Session!.CaptureSnapshot().AuthoritativeHash != priorHash)
                throw new InvalidOperationException("Failed terminal milestone damaged the previous valid slot.");
            if (!SaveCadenceMilestone("Retry") || _cadenceSaveError || _cadenceErrorLayer?.Visible != false)
                throw new InvalidOperationException("Terminal milestone retry did not clear the visible error.");
            File.WriteAllText(_saveCadenceCaptureOutput, JsonSerializer.Serialize(new
            {
                mode = "changed-state-2s-diagnostic", productionCadenceSeconds = RealTimeAutosaveScheduler.ProductionCadenceSeconds,
                draftEdits = 9, buildPlacementCommands = 1, draftWriteAttempts = 0,
                clickToVisibleMilliseconds = _saveCadenceClickMs,
                milestoneWrites = _cadenceMilestoneAttempts, periodicWriteAttempts = _cadenceWriteAttempts,
                failedPeriodicWrites = _cadenceFailures, movedWhileWritePending = _saveCadenceMovedWhilePending,
                pausedCadenceSkipped = true, unchangedCadenceSkipped = true,
                failedMilestonePreservedSlot = true, visibleRetrySucceeded = true,
                finalTick = _session.CurrentTick, exactFinalReload = true, finalHash = hash
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"SAVE_CADENCE_CAPTURE_COMPLETE periodic={_cadenceWriteAttempts} failures={_cadenceFailures} tick={_session.CurrentTick}");
            _saveCadenceCaptureOutput = null;
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError("SAVE_CADENCE_CAPTURE_FAILED " + exception);
            _saveCadenceCaptureOutput = null;
            GetTree().Quit(2);
        }
    }
}

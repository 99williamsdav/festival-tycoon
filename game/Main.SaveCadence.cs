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
    // Ordinary launches (no user arguments) and the frame profile use background cadence saves.
    private bool RelaxedSaveCadence => (OS.GetCmdlineUserArgs().Length == 0 || _preparationProfileOutput is not null);
    private long _saveRevision;
    private long _savedRevision;
    private long _savingRevision;
    private long _savingGeneration;
    private Task<SaveOperationResult>? _cadenceSaveTask;
    private bool _cadenceDue;
    private bool _cadenceSaveError;
    private CanvasLayer? _cadenceErrorLayer;
    private Label? _cadenceErrorLabel;

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
        Action<SaveFailurePoint>? injector = null;
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
        Action<SaveFailurePoint>? injector = null;
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

}

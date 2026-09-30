using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;

namespace Festival.Game;

// Shared helpers for the scripted evidence captures that drive the Build route.
public partial class Main
{
    private bool _attendeePoseCaptureCompleted;
    private Action<Festival.Persistence.SaveFailurePoint>? _planCaptureFailureInjector;

    private void ProcessAttendeePoseCaptureFrame()
    {
        if (!_attendeePoseCapture || _attendeePoseCaptureCompleted) return;
        try { ProcessAttendeePoseCapture(); }
        catch (Exception error) { GD.PushError("ATTENDEE_POSE_CAPTURE_FAILED " + error); GetTree().Quit(1); _attendeePoseCaptureCompleted = true; }
    }

    private void AttendeePoseImage(string name) =>
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_attendeePoseCaptureDirectory!, name + ".png"));

    /// <summary>
    /// Advances a running edition to a tick relative to its start, acting as a careful
    /// operator: the ordinary medic answers visible warnings. No needs, positions or deadlines are injected.
    /// </summary>
    private void TimetableAdvanceTo(long relativeTick)
    {
        var target = _session.CapturePreparation()!.StartedTick + relativeTick;
        if (_session.CurrentTick >= target) return;
        StaffCaptureSend(new SetPausedCommand(false));
        while (_session.CurrentTick < target && _session.PreparedStatus != PreparationStatus.Failed)
        {
            var medical = _session.CaptureMedical()!;
            foreach (var need in medical.Needs.Where(need => need.Stage is MedicalStage.Distress or MedicalStage.Collapsed or MedicalStage.Critical))
            {
                var command = new MedicalCommand(need.AgentId, MedicalAction.DispatchMedic, medical.MedicId);
                if (_session.ValidateCommand(CampaignEnvelope(command)) is null) { StaffCaptureSend(command); break; }
            }
            _session.AdvanceWithoutSnapshot((int)Math.Min(80, target - _session.CurrentTick));
        }
        if (_session.PreparedStatus == PreparationStatus.Failed)
            throw new InvalidOperationException($"Capture operator run failed at {_session.CurrentTick}; {_session.CaptureLifecycleSnapshot()!.Casualties.Last().PersonId}");
        StaffCaptureSend(new SetPausedCommand(true));
        _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary(); RefreshPreparationHud();
    }
}

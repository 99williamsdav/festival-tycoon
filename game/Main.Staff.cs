using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Festival.Game;

public partial class Main
{
    private int _staffCaptureFrame;

    private void StaffCaptureSend(SessionCommand command)
    {
        var result = _session.Execute(CampaignEnvelope(command));
        if (!result.IsAccepted) throw new InvalidOperationException($"Staff fixture command rejected: {result.Message}");
    }
    private void StaffCaptureAdvance(int ticks)
    {
        StaffCaptureSend(new SetPausedCommand(false)); _session.AdvanceWithoutSnapshot(ticks); StaffCaptureSend(new SetPausedCommand(true));
        _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary(); RefreshPreparationHud();
    }

    private void RefreshStaffControls()
    {
        var p = _session.CapturePreparation();
        if (p is null) return;
    }

    private static string StaffAbilityText(StaffProfile p) => $"{p.Role.ToString().ToUpperInvariant()} ABILITIES\nWALKING  {p.WalkingSpeedPermille / 10m:0}% of standard speed\n" +
        (p.Role == ResponseRole.Medic ? $"TREATMENT  {p.TreatmentTicks / 80m:0.0}s at 1× • starts after arrival" :
            $"CALMING  {p.CalmingSkill}/10000\nFIGHTING  {p.ConfrontationSkill}/10000 • abilities, not success chances");

    private string ResponseStaffInspectorText(ulong id)
    {
        var profile = _session.GetResponseStaff().SingleOrDefault(item => item.AgentId == id);
        if (profile is null) return "";
        var medic = _session.GetMedicResponses().SingleOrDefault(item => item.WorkerId == id);
        var steward = _session.GetStewardResponses().SingleOrDefault(item => item.WorkerId == id);
        var intervention = _session.CaptureStaffInterventions().SingleOrDefault(item => item.WorkerId == id &&
            item.Stage is StaffInterventionStage.Travelling or StaffInterventionStage.Guiding or StaffInterventionStage.Escorting);
        var target = intervention is not null ? (ulong?)intervention.GuestId :
            medic?.Stage is MedicalResponseStage.Travelling or MedicalResponseStage.Treating or MedicalResponseStage.Removing ? medic.PatientId :
            steward?.Stage is SecurityResponseStage.Travelling or SecurityResponseStage.Calming or SecurityResponseStage.Confronting ? steward.TargetId : null;
        var targetName = target is { } personId ? _session.CapturePreparation()!.People.Single(item => item.AgentId == personId).Name : "None";
        var nav = _session.CaptureObservation().NavigationAgents.SingleOrDefault(item => item.Id.Value == id);
        var eta = _session.EstimateStaffTravelTicks(id);
        var treatmentRemaining = medic?.Stage == MedicalResponseStage.Treating ? $" • treatment {System.Math.Max(0, medic.StartedTick + profile.TreatmentTicks - _session.CurrentTick) / 80m:0.0}s left" : "";
        return $"{StaffAbilityText(profile)}\n{_session.ResponseStaffStatus(id)} • TARGET {targetName}{treatmentRemaining}\n" +
            $"{intervention?.Description ?? medic?.Description ?? steward?.Description}\nROUTE {nav?.Action} • ETA {(eta is { } ticks ? $"~{ticks / 80m:0.0}s plus crowd delays" : "unavailable")}\nAssign from the target person's inspector\n";
    }
}

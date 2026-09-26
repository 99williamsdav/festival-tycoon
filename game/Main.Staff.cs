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
    private readonly Dictionary<string, Button> _staffEffectButtons = [];
    private readonly Dictionary<ulong, Button> _staffDispatchButtons = [];
    private Label? _staffFoundationHeading;
    private string? _staffCaptureDirectory;
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
    private void StaffCaptureImage(string name) => GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_staffCaptureDirectory!, name + ".png"));
    private void ProcessStaffCapture()
    {
        if (_staffCaptureDirectory is null) return;
        _staffCaptureFrame++;
        if (_staffCaptureFrame == 4)
        {
            foreach (var effect in new[] { "staff.medic-slot", "staff.steward-slot", "staff.role-training" }) _staffEffectButtons[effect].EmitSignal(Button.SignalName.Pressed);
            if (_session.CapturePreparation()!.People.Length != 26) throw new InvalidOperationException("Slots created free labour.");
            _preparationMessage = "LABELLED STAFF FOUNDATION DEMO • slots add no workers; training is free."; RefreshPreparationHud();
            if (_staffFoundationHeading!.GetParent().GetParent() is ScrollContainer scroll) scroll.EnsureControlVisible(_staffEffectButtons["staff.role-training"]);
        }
        if (_staffCaptureFrame == 6)
        {
            StaffCaptureImage("capacity-no-free-labour");
            foreach (var offer in new[] { "staff.extra-medic", "staff.extra-steward", "maintenance.worker", "act.folk", "staff.steward", "equipment.buy" }) _offerButtons[offer].EmitSignal(Button.SignalName.Pressed);
            if (_session.GetResponseStaff().Count != 4) throw new InvalidOperationException("Paid optional hires failed.");
            _preparationMessage = "LABELLED STAFF DEMO • Avery and Sam paid £30 each for this weekend."; RefreshPreparationHud();
            if (_staffFoundationHeading!.GetParent().GetParent() is ScrollContainer scroll) scroll.EnsureControlVisible(_offerButtons["staff.extra-medic"]);
        }
        if (_staffCaptureFrame == 8) StaffCaptureImage("paid-weekend-hires");
        if (_staffCaptureFrame == 10)
        {
            if (_staffFoundationHeading!.GetParent().GetParent() is ScrollContainer scroll) scroll.ScrollVertical = 0;
            PreparationStart();
            StaffCaptureAdvance(1500);
            StaffCaptureSend(new MedicalCommand(_session.CaptureMedical()!.AtRiskGuestId, MedicalAction.GuideToRest));
            StaffCaptureAdvance(1000);
            var ids = _session.CapturePreparation()!.People.Where(item => item.Role == ProtectedPersonRole.Guest).Take(2).Select(item => item.AgentId).ToArray();
            var medical = _session.CaptureMedical()!;
            // Labelled development warning fixture only. Travel, treatment, ownership and save paths are production logic.
            typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session,
                medical with { Needs = medical.Needs.Select(item => ids.Contains(item.AgentId) ? item with { Stage = MedicalStage.Distress, WarningTick = _session.CurrentTick } : item).ToArray() });
            var workers = _session.GetResponseStaff().Where(item => item.Role == ResponseRole.Medic).ToArray();
            for (var index = 0; index < 2; index++) { SelectAttendee(new EntityId(ids[index])); CommitMedicalAction(MedicalAction.DispatchMedic, workers[index].AgentId); }
            if (_session.GetMedicResponses().Count(item => item.Stage == MedicalResponseStage.Travelling) != 2) throw new InvalidOperationException("Independent medic responses failed.");
            SelectAttendee(new EntityId(workers[1].AgentId));
            _preparationMessage = "LABELLED TWO-WARNING FIXTURE • two named medics independently travelling."; RefreshPreparationHud();
            _focus = new Vector3(-5, 0, 15); _camera.Size = 40; ApplyCamera();
        }
        if (_staffCaptureFrame == 12)
        {
            StaffCaptureImage("two-medics-travelling");
            SelectAttendee(new EntityId(_session.GetMedicResponses().First().PatientId!.Value));
        }
        if (_staffCaptureFrame == 13) StaffCaptureImage("named-dispatch-busy-buttons");
        if (_staffCaptureFrame == 14)
        {
            SelectAttendee(new EntityId(_session.GetResponseStaff().Single(item => item.Name == "Avery Brooks").AgentId));
            for (var ticks = 0; ticks < 3000 && !_session.GetMedicResponses().Any(item => item.Stage == MedicalResponseStage.Treating); ticks++) StaffCaptureAdvance(1);
            _preparationMessage = "LABELLED DEMO • physical arrival starts individual treatment clock."; RefreshPreparationHud();
        }
        if (_staffCaptureFrame == 16) StaffCaptureImage("individual-treatment");
        if (_staffCaptureFrame == 18)
        {
            StaffCaptureAdvance(2000);
            if (_session.GetMedicResponses().Count(item => item.Stage == MedicalResponseStage.Completed) != 2) throw new InvalidOperationException("Both medical jobs did not finish.");
            StaffCaptureSend(new EquipmentCommand(EquipmentAction.Isolate));
            for (var ticks = 0; ticks < 1500 && _session.CaptureDisorder()!.People.Count(item => item.Stage == DisorderStage.Complaint) < 2; ticks++) StaffCaptureAdvance(1);
            var targets = _session.CaptureDisorder()!.People.Where(item => item.Stage == DisorderStage.Complaint).Take(2).Select(item => item.AgentId).ToArray();
            var workers = _session.GetResponseStaff().Where(item => item.Role == ResponseRole.Steward).ToArray();
            if (targets.Length != 2) throw new InvalidOperationException("Two causal complaints not found.");
            for (var index = 0; index < 2; index++) { SelectAttendee(new EntityId(targets[index])); CommitDisorderAction(DisorderAction.DispatchSecurity, workers[index].AgentId); }
            if (_session.GetStewardResponses().Count(item => item.Stage == SecurityResponseStage.Travelling) != 2) throw new InvalidOperationException("Independent steward responses failed.");
            SelectAttendee(new EntityId(workers[1].AgentId)); _preparationMessage = "LABELLED DEMO • two independent steward routes to real music-cutoff complaints."; RefreshPreparationHud();
            GD.Print($"STAFF_CAPTURE concurrent_medics=2 completed_medics=2 concurrent_stewards=2 people={_session.CapturePreparation()!.People.Length} hash={_session.CaptureSnapshot().AuthoritativeHash}");
        }
        if (_staffCaptureFrame == 20) StaffCaptureImage("two-stewards-travelling");
        if (_staffCaptureFrame == 22)
        {
            StaffCaptureAdvance(80); PreparationSave();
            StaffCaptureImage("named-steward-route");
            GD.Print("STAFF_CAPTURE_COMPLETE labelled_fixture=true existing_assets=true"); GetTree().Quit();
        }
    }

    private void BuildStaffFoundationControls(VBoxContainer box)
    {
        if (_session.CaptureDisorder() is null) return;
        _staffFoundationHeading = LabelText("FREE STAFF FOUNDATION DEMO • FUTURE PERKS", 13, new Color("29352c"));
        box.AddChild(_staffFoundationHeading);
        foreach (var (effect, text) in new[] { ("staff.medic-slot", "EXTRA MEDIC SLOT • FREE DEMO"),
            ("staff.steward-slot", "EXTRA STEWARD SLOT • FREE DEMO"), ("staff.role-training", "ALL MEDICS / STEWARDS UPGRADED • FREE DEMO") })
        {
            var button = ButtonText(text, () => CommitEquipmentAction(new ApplyStaffFoundationEffectCommand(effect)));
            button.TooltipText = FestivalCopy("Durable future-perk effect seam. No payment and no free worker. Extra hire contracts cost £30 each weekend; existing sound and maintenance contracts are unchanged.");
            box.AddChild(button); _staffEffectButtons.Add(effect, button);
        }
    }

    private void RefreshStaffControls()
    {
        var p = _session.CapturePreparation();
        if (p is null) return;
        if (_staffFoundationHeading is not null) _staffFoundationHeading.Visible = p.Status == PreparationStatus.Preparing;
        foreach (var (effect, button) in _staffEffectButtons)
        {
            button.Visible = p.Status == PreparationStatus.Preparing;
            button.Disabled = _session.ValidateCommand(CampaignEnvelope(new ApplyStaffFoundationEffectCommand(effect))) is not null;
        }
        if (_medicalActionInspector is null) return;
        foreach (var button in _staffDispatchButtons.Values) button.Visible = false;
        foreach (var profile in _session.GetResponseStaff())
        {
            if (profile.AgentId == _session.CaptureMedical()?.MedicId || profile.AgentId == _session.CaptureDisorder()?.SecurityId) continue;
            if (!_staffDispatchButtons.TryGetValue(profile.AgentId, out var button))
            {
                button = ButtonText($"DISPATCH {profile.Name.Split(' ')[0].ToUpperInvariant()}", () => {
                    if (profile.Role == ResponseRole.Medic) CommitMedicalAction(MedicalAction.DispatchMedic, profile.AgentId);
                    else CommitDisorderAction(DisorderAction.DispatchSecurity, profile.AgentId);
                });
                button.AddThemeFontSizeOverride("font_size", 12);
                _medicalActionInspector.AddChild(button); _staffDispatchButtons.Add(profile.AgentId, button);
            }
            button.Visible = true;
            var target = _selectedAttendeeId?.Value;
            SessionCommand? command = target is not { } id ? null : profile.Role == ResponseRole.Medic
                ? new MedicalCommand(id, MedicalAction.DispatchMedic, profile.AgentId) : new DisorderCommand(DisorderAction.DispatchSecurity, id, profile.AgentId);
            var issue = command is null ? "Select an affected person first." : _session.ValidateCommand(CampaignEnvelope(command))?.Message;
            button.Disabled = issue is not null;
            button.TooltipText = $"{StaffAbilityText(profile)}\n{issue ?? "Available for this target"}";
        }
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
        var target = intervention is not null ? (ulong?)intervention.GuestId : medic?.PatientId ?? steward?.TargetId;
        var targetName = target is { } personId ? _session.CapturePreparation()!.People.Single(item => item.AgentId == personId).Name : "None";
        var nav = _session.CaptureObservation().NavigationAgents.SingleOrDefault(item => item.Id.Value == id);
        var eta = _session.EstimateStaffTravelTicks(id);
        var treatmentRemaining = medic?.Stage == MedicalResponseStage.Treating ? $" • treatment {System.Math.Max(0, medic.StartedTick + profile.TreatmentTicks - _session.CurrentTick) / 80m:0.0}s left" : "";
        return $"{StaffAbilityText(profile)}\nJOB {intervention?.Stage.ToString() ?? medic?.Stage.ToString() ?? steward?.Stage.ToString()} • TARGET {targetName}{treatmentRemaining}\n" +
            $"{intervention?.Description ?? medic?.Description ?? steward?.Description}\nROUTE {nav?.Action} • ETA {(eta is { } ticks ? $"~{ticks / 80m:0.0}s plus crowd delays" : "unavailable")}\nAssign from the target person's inspector\n";
    }
}

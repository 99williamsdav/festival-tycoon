using Festival.Simulation;
using Festival.Persistence;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private VBoxContainer? _staffInterventionInspector;
    private readonly Dictionary<ResponseRole, OptionButton> _staffInterventionChoices = [];
    private readonly Dictionary<ResponseRole, ulong[]> _staffInterventionWorkerIds = [];
    private readonly Dictionary<(ResponseRole Role, StaffInterventionAction Action), Button> _staffInterventionButtons = [];

    private void BuildStaffInterventionControls(VBoxContainer detail)
    {
        _staffInterventionInspector = new VBoxContainer { Visible = false };
        detail.AddChild(_staffInterventionInspector);
        _staffInterventionInspector.AddChild(LabelText("PHYSICAL STAFF HELP • ARRIVAL FIRST", 12, new Color("29352c")));
        foreach (var role in new[] { ResponseRole.Steward, ResponseRole.Medic })
        {
            var choice = new OptionButton { CustomMinimumSize = new Vector2(365, 32) };
            choice.AddThemeFontSizeOverride("font_size", 12);
            choice.ItemSelected += _ => RefreshStaffInterventionControls();
            _staffInterventionChoices.Add(role, choice);
            _staffInterventionWorkerIds.Add(role, []);
            _staffInterventionInspector.AddChild(choice);
            foreach (var action in role == ResponseRole.Steward
                         ? new[] { StaffInterventionAction.GuideToWater, StaffInterventionAction.LeaveWaterQueue, StaffInterventionAction.EscortOut }
                         : new[] { StaffInterventionAction.GuideToRest, StaffInterventionAction.EscortOut })
            {
                var button = ButtonText("", () => CommitStaffIntervention(role, action));
                button.AddThemeFontSizeOverride("font_size", 12);
                _staffInterventionButtons.Add((role, action), button);
                _staffInterventionInspector.AddChild(button);
            }
        }
    }

    private ulong? ChosenInterventionWorker(ResponseRole role)
    {
        var index = _staffInterventionChoices[role].Selected;
        var ids = _staffInterventionWorkerIds[role];
        return index >= 0 && index < ids.Length ? ids[index] : null;
    }

    private void RefreshStaffInterventionControls()
    {
        if (_staffInterventionInspector is null) return;
        var target = MedicalSelectedGuest();
        _staffInterventionInspector.Visible = target is not null && _selectedMedicalFacility is null &&
            _session.PreparedStatus == PreparationStatus.Running;
        foreach (var role in new[] { ResponseRole.Steward, ResponseRole.Medic })
        {
            var profiles = _session.GetResponseStaff().Where(profile => profile.Role == role).ToArray();
            var ids = profiles.Select(profile => profile.AgentId).ToArray();
            var choice = _staffInterventionChoices[role];
            if (!_staffInterventionWorkerIds[role].SequenceEqual(ids))
            {
                var previous = ChosenInterventionWorker(role);
                choice.Clear(); _staffInterventionWorkerIds[role] = ids;
                foreach (var profile in profiles) choice.AddItem($"{role}: {profile.Name}");
                if (ids.Length > 0) choice.Select(Math.Max(0, Array.IndexOf(ids, previous ?? 0)));
            }
            choice.Disabled = ids.Length == 0;
            var workerId = ChosenInterventionWorker(role);
            var worker = profiles.SingleOrDefault(profile => profile.AgentId == workerId);
            foreach (var ((buttonRole, action), button) in _staffInterventionButtons)
            {
                if (buttonRole != role) continue;
                var label = action switch { StaffInterventionAction.GuideToWater => "GUIDE TO WATER",
                    StaffInterventionAction.LeaveWaterQueue => "LEAVE WATER QUEUE", StaffInterventionAction.GuideToRest => "GUIDE TO REST", _ => "ESCORT OUT" };
                button.Text = $"ASK {(worker?.Name.Split(' ')[0].ToUpperInvariant() ?? role.ToString().ToUpperInvariant())}: {label}";
                var command = target is { } guest && workerId is { } staff ? new StaffInterventionCommand(guest, staff, action) : null;
                var issue = command is null ? "Choose an on-duty worker and a person." : _session.ValidateCommand(CampaignEnvelope(command))?.Message;
                button.Disabled = issue is not null;
                button.TooltipText = (worker is null ? "" : StaffAbilityText(worker) + "\n") +
                    (issue ?? "Worker must physically reach this person first. Escort completes only at the gate; deadlines remain active.");
            }
        }
    }

    private void CommitStaffIntervention(ResponseRole role, StaffInterventionAction action)
    {
        if (MedicalSelectedGuest() is not { } guest || ChosenInterventionWorker(role) is not { } worker) return;
        var result = EquipmentCommandCoordinator.Execute(SaveDirectory, _session, new StaffInterventionCommand(guest, worker, action),
            _saveCompatibility, DateTimeOffset.UtcNow, _autosaveGeneration);
        if (result.IsSuccess)
        {
            _session = result.Session; _autosaveGeneration++; _preparationSaveBlocked = false;
            _preparationMessage = "Staff request autosaved. Physical arrival comes before guidance; escort ends at the gate.";
        }
        else { _preparationMessage = result.Error!; if (result.Autosave is not null) _preparationSaveBlocked = true; }
        RefreshPreparationHud();
    }

    private string StaffInterventionTargetText(ulong id)
    {
        var job = _session.CaptureStaffInterventions().SingleOrDefault(item => item.GuestId == id &&
            item.Stage is StaffInterventionStage.Travelling or StaffInterventionStage.Guiding or StaffInterventionStage.Escorting);
        if (job is null) return "";
        var name = _session.CapturePreparation()!.People.Single(person => person.AgentId == job.WorkerId).Name;
        return $"STAFF HELP  {name} • {job.Action} • {job.Stage}\n{job.Description}\n";
    }

    private string? ActiveStaffInterventionSummary(ulong workerId)
    {
        var job = _session.CaptureStaffInterventions().SingleOrDefault(item => item.WorkerId == workerId &&
            item.Stage is StaffInterventionStage.Travelling or StaffInterventionStage.Guiding or StaffInterventionStage.Escorting);
        return job is null ? null : $"{job.Action} • {job.Stage}";
    }
}

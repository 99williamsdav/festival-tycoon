using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private Label? _disorderSummary;
    private readonly Dictionary<DisorderAction, Button> _disorderButtons = [];
    private string? _disorderCaptureDirectory;
    private string _disorderCaptureMode = "music";
    private int _disorderCaptureFrame;

    private void BuildDisorderControls(VBoxContainer box)
    {
        _disorderSummary = LabelText("", 14, new Color("713c35"));
        _disorderSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _disorderSummary.CustomMinimumSize = new Vector2(370, 145);
        box.AddChild(_disorderSummary);
        foreach (var (action, label) in new[] {
            (DisorderAction.CloseWater, "CLOSE WATER SAFELY"),
            (DisorderAction.ReopenWater, "REOPEN WATER"),
            (DisorderAction.RestoreMusic, "SAFE RESET MUSIC") })
        {
            var button = ButtonText(label, () => CommitDisorderAction(action));
            button.AddThemeFontSizeOverride("font_size", 12);
            box.AddChild(button);
            _disorderButtons.Add(action, button);
        }
        box.AddChild(ButtonText("ISOLATE STAGE POWER", () => CommitEquipmentAction(new EquipmentCommand(EquipmentAction.Isolate))));
    }

    private void BuildDisorderActionInspector()
    {
        if (_session.CaptureDisorder() is null || _medicalActionInspector is null) return;
        foreach (var (action, label) in new[] {
            (DisorderAction.DispatchSecurity, "DISPATCH SECURITY"),
            (DisorderAction.SafeEgress, "SAFE EGRESS") })
        {
            var button = ButtonText(label, () => CommitDisorderAction(action));
            button.AddThemeFontSizeOverride("font_size", 12);
            button.CustomMinimumSize = new Vector2(183, 30);
            _medicalActionInspector.AddChild(button);
            _disorderButtons.Add(action, button);
        }
    }

    private string DisorderPersonInspectorText(ulong id)
    {
        var d = _session.CaptureDisorder();
        if (d is null) return "";
        if (id == d.SecurityId)
            return $"SECURITY • {(d.SecurityIncapacitated ? "INJURED • NEEDS MEDIC" : d.ResponseStage)}\n" +
                $"RESPONSE {d.Response}\n";
        var person = d.People.SingleOrDefault(item => item.AgentId == id);
        if (person is null) return "";
        return $"DISORDER • {person.Stage} • pressure {person.Pressure / 100m:0}%\n" +
            $"CAUSE {person.Grievance} • {(person.Stage == DisorderStage.Injured ? "FIRST AID NEEDED" : "reduce pressure or dispatch security")}\n";
    }

    private void CommitDisorderAction(DisorderAction action)
    {
        var personTargeted = action is DisorderAction.DispatchSecurity or DisorderAction.SafeEgress;
        var selected = _selectedAttendeeId?.Value;
        if (personTargeted && (selected is null || _session.CaptureDisorder()?.People.Any(item => item.AgentId == selected) != true))
        {
            _preparationMessage = "Select an affected guest before choosing a person action.";
            RefreshPreparationHud();
            return;
        }
        var command = new DisorderCommand(action, personTargeted ? selected : null);
        var result = DisorderCommandCoordinator.Execute(SaveDirectory, _session, command, _saveCompatibility,
            DateTimeOffset.UtcNow, _autosaveGeneration);
        if (result.IsSuccess)
        {
            _session = result.Session; _autosaveGeneration++;
            _preparationSaveBlocked = false;
            _preparationMessage = "Disorder action committed and autosaved.";
        }
        else { _preparationMessage = result.Error!; if (result.Autosave is not null) _preparationSaveBlocked = true; }
        RefreshPreparationHud();
    }

    private void RefreshDisorderActionInspector()
    {
        var d = _session.CaptureDisorder();
        if (d is null) return;
        foreach (var action in new[] { DisorderAction.DispatchSecurity, DisorderAction.SafeEgress })
        {
            if (!_disorderButtons.TryGetValue(action, out var button)) continue;
            var selected = _selectedAttendeeId?.Value;
            var error = selected is { } id ? _session.ValidateCommand(CampaignEnvelope(new DisorderCommand(action, id))) : null;
            button.Disabled = selected is null || error is not null;
            button.TooltipText = selected is null ? "Select an affected guest." : error?.Message ?? "";
        }
    }

    private void RefreshDisorderControls()
    {
        if (_disorderSummary is null || _session.CaptureDisorder() is not { } d) return;
        var notable = d.People.Where(item => item.Stage is not (DisorderStage.Calm or DisorderStage.Resolved))
            .OrderByDescending(item => item.Pressure).ThenBy(item => item.AgentId).FirstOrDefault();
        var name = notable is null ? "No active dispute" :
            _session.CapturePreparation()!.People.Single(item => item.AgentId == notable.AgentId).Name;
        var latest = d.Evidence.LastOrDefault();
        _disorderSummary.Text = $"DISORDER • {(d.WaterClosed ? "WATER CLOSED" : "WATER OPEN")}\n" +
            $"{name}{(notable is null ? "" : $" • {notable.Stage} • pressure {notable.Pressure / 100m:0}% • {notable.Grievance}")}\n" +
            $"Security {d.ResponseStage} • {d.Response}\n" +
            $"{(latest is null ? "No complaint" : latest.Description)}\n" +
            "Complaint and argument precede any confrontation. Select a person for response/egress.";
        foreach (var action in new[] { DisorderAction.CloseWater, DisorderAction.ReopenWater, DisorderAction.RestoreMusic })
        {
            var button = _disorderButtons[action];
            var error = _session.ValidateCommand(CampaignEnvelope(new DisorderCommand(action)));
            button.Disabled = error is not null;
            button.TooltipText = error?.Message ?? "";
        }
        RefreshDisorderActionInspector();
    }

    private void ProcessDisorderCapture()
    {
        if (_disorderCaptureDirectory is null) return;
        _disorderCaptureFrame++;
        if (_disorderCaptureFrame == 4)
            foreach (var id in new[] { "act.folk", "staff.steward", "equipment.buy" }) _offerButtons[id].EmitSignal(Button.SignalName.Pressed);
        if (_disorderCaptureFrame == 6) _preparationStart.EmitSignal(Button.SignalName.Pressed);
        if (_disorderCaptureFrame == 8)
        {
            if (_disorderCaptureMode == "music")
            {
                while (_session.CaptureLivePerformance()!.Stage != LiveSetStage.Live && _session.CurrentTick < 4_000)
                    _session.AdvanceWithoutSnapshot(1);
                _session.Execute(CampaignEnvelope(new MedicalCommand(_session.CaptureMedical()!.AtRiskGuestId, MedicalAction.GuideToRest)));
                _session.Execute(CampaignEnvelope(new EquipmentCommand(EquipmentAction.Isolate)));
            }
            else
            {
                _session.Execute(CampaignEnvelope(new MedicalCommand(_session.CaptureMedical()!.AtRiskGuestId, MedicalAction.GuideToRest)));
            }
            while (!_session.CaptureDisorder()!.People.Any(item => item.Stage == DisorderStage.Complaint &&
                   item.Grievance == (_disorderCaptureMode == "music" ? DisorderGrievance.MusicCutoff : DisorderGrievance.WaterWait)) &&
                   _session.CurrentTick < 5_000)
                _session.AdvanceWithoutSnapshot(1);
            if (!_session.CaptureDisorder()!.People.Any(item => item.Stage == DisorderStage.Complaint &&
                item.Grievance == (_disorderCaptureMode == "music" ? DisorderGrievance.MusicCutoff : DisorderGrievance.WaterWait)))
                throw new InvalidOperationException("Disorder capture did not produce a causal complaint.");
            _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
            var person = _session.CaptureDisorder()!.People.First(item => item.Stage == DisorderStage.Complaint &&
                item.Grievance == (_disorderCaptureMode == "music" ? DisorderGrievance.MusicCutoff : DisorderGrievance.WaterWait));
            SelectAttendee(new EntityId(person.AgentId));
            RefreshPreparationHud();
            GD.Print($"DISORDER_CAPTURE mode={_disorderCaptureMode} tick={_session.CurrentTick} person={person.AgentId} stage={person.Stage} pressure={person.Pressure}");
        }
        if (_disorderCaptureFrame == 12)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_disorderCaptureDirectory, "complaint.png"));
            if (_disorderCaptureMode == "music") _disorderButtons[DisorderAction.RestoreMusic].EmitSignal(Button.SignalName.Pressed);
            else _disorderButtons[DisorderAction.CloseWater].EmitSignal(Button.SignalName.Pressed);
        }
        if (_disorderCaptureFrame == 14)
        {
            _session.AdvanceWithoutSnapshot(8);
            _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
            RefreshPreparationHud();
        }
        if (_disorderCaptureFrame == 16)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_disorderCaptureDirectory, "prevented.png"));
            GD.Print($"DISORDER_CAPTURE resolved={_session.CaptureDisorder()!.People.Count(item => item.Grievance == DisorderGrievance.None)} waterClosed={_session.CaptureDisorder()!.WaterClosed} set={_session.CaptureLivePerformance()!.Stage}");
            GetTree().Quit();
        }
    }
}

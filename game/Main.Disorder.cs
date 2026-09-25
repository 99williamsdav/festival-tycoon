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
    // R0.04 saves retain their internal security identifiers and authored text.
    // Present both new and previously saved response prose as steward language.
    private static string StewardWording(string value) => value
        .Replace("SECURITY", "STEWARD", StringComparison.Ordinal)
        .Replace("Security", "Steward", StringComparison.Ordinal)
        .Replace("security", "steward", StringComparison.Ordinal)
        .Replace("GUARD", "STEWARD", StringComparison.Ordinal)
        .Replace("Guard", "Steward", StringComparison.Ordinal)
        .Replace("guard", "steward", StringComparison.Ordinal);

    private Label? _disorderSummary;
    private readonly Dictionary<DisorderAction, Button> _disorderButtons = [];
    private string? _disorderCaptureDirectory;
    private string _disorderCaptureMode = "music";
    private int _disorderCaptureFrame;
    private ulong _securityPostPickId;
    private bool _selectedSecurityPost;
    private Button? _securityPostWorkerButton;

    private void BuildDisorderWorld()
    {
        var centre = TraversalGrid.CellCentre(GameSession.DisorderSecurityPostCell);
        var position = new Vector3(centre.XMillimetres / 1000f, 0, centre.ZMillimetres / 1000f);
        AddAsset("res://assets/environment/lwf_security_post_v1.glb", position);
        var pick = new StaticBody3D { Position = position + new Vector3(0, 1.55f, 0),
            CollisionLayer = 1, CollisionMask = 0 };
        pick.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2.38f, 3.1f, 2.33f) } });
        AddChild(pick);
        _securityPostPickId = pick.GetInstanceId();
    }

    private void BuildSecurityPostInspectorAction(VBoxContainer parent)
    {
        if (_session.CaptureDisorder() is null) return;
        _securityPostWorkerButton = ButtonText("SELECT JORDAN • STEWARD", () =>
            SelectAttendee(new EntityId(_session.CaptureDisorder()!.SecurityId)));
        _securityPostWorkerButton.Visible = false;
        parent.AddChild(_securityPostWorkerButton);
    }

    private void SelectSecurityPost()
    {
        _selected = null; _selectedAttendeeId = null; _selectedMedicalFacility = null;
        _selectedSecurityPost = true;
        RefreshMedicalNeedBars(null);
        RefreshMedicalActionInspector();
        RefreshDisorderActionInspector();
        var centre = TraversalGrid.CellCentre(GameSession.DisorderSecurityPostCell);
        _highlight.Position = new Vector3(centre.XMillimetres / 1000f, .08f, centre.ZMillimetres / 1000f);
        _highlight.Scale = new Vector3(2.4f, 1, 2.4f); _highlight.Visible = true;
        RefreshSecurityPostInspector();
        GD.Print("SECURITY_POST_SELECTED");
    }

    private void ClearSecurityPostSelection()
    {
        _selectedSecurityPost = false;
        if (_securityPostWorkerButton is not null) _securityPostWorkerButton.Visible = false;
    }

    private void RefreshSecurityPostInspector()
    {
        if (!_selectedSecurityPost || _session.CaptureDisorder() is not { } d) return;
        var people = _session.CapturePreparation()!.People;
        var worker = people.Single(item => item.AgentId == d.SecurityId);
        var workerAvailable = _attendeeVisuals.ContainsKey(new EntityId(d.SecurityId));
        if (_securityPostWorkerButton is not null)
        {
            _securityPostWorkerButton.Visible = workerAvailable;
            _securityPostWorkerButton.Disabled = !workerAvailable;
        }
        var position = _session.CaptureSnapshot().NavigationAgents.SingleOrDefault(item => item.Id.Value == d.SecurityId);
        var target = d.ResponseTargetId is { } id ? people.Single(item => item.AgentId == id).Name : "None";
        _inspectorTitle.Text = "Steward post • Jordan Hale";
        _inspectorBody.Text = $"POST  open public approach • gate route clear\n" +
            $"WORKER  {(worker.Admitted ? d.SecurityIncapacitated ? "injured • needs medic" : "on site" : "walking in")}\n" +
            $"POSITION  {(position is null ? "not yet arrived" : $"{position.XMillimetres / 1000m:0.00} m, {position.ZMillimetres / 1000m:0.00} m")}\n" +
            $"RESPONSE  {d.ResponseStage} • target {target}\n{StewardWording(d.Response)}\n" +
            "Select an affected guest to DISPATCH STEWARD or use SAFE EGRESS in their inspector. " +
            (workerAvailable ? "Select Jordan here if he needs medical help. " :
                "Jordan can be selected after the edition starts and his physical visual exists. ") +
            "Stewards do not teleport or guarantee de-escalation.";
    }

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
            (DisorderAction.DispatchSecurity, "DISPATCH STEWARD"),
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
            return $"STEWARD • {(d.SecurityIncapacitated ? "INJURED • NEEDS MEDIC" : d.ResponseStage)}\n" +
                $"RESPONSE {StewardWording(d.Response)}\n";
        var person = d.People.SingleOrDefault(item => item.AgentId == id);
        if (person is null) return "";
        return $"DISORDER • {person.Stage} • pressure {person.Pressure / 100m:0}%\n" +
            $"CAUSE {person.Grievance} • {(person.Stage == DisorderStage.Injured ? "FIRST AID NEEDED" : "reduce pressure or dispatch a steward")}\n";
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
        else { _preparationMessage = StewardWording(result.Error!); if (result.Autosave is not null) _preparationSaveBlocked = true; }
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
            button.TooltipText = selected is null ? "Select an affected guest." : StewardWording(error?.Message ?? "");
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
            $"Steward {d.ResponseStage} • {StewardWording(d.Response)}\n" +
            $"{(latest is null ? "No complaint" : StewardWording(latest.Description))}\n" +
            "Complaint and argument precede any confrontation. Select a person for response/egress.";
        foreach (var action in new[] { DisorderAction.CloseWater, DisorderAction.ReopenWater, DisorderAction.RestoreMusic })
        {
            var button = _disorderButtons[action];
            var error = _session.ValidateCommand(CampaignEnvelope(new DisorderCommand(action)));
            button.Disabled = error is not null;
            button.TooltipText = StewardWording(error?.Message ?? "");
        }
        RefreshDisorderActionInspector();
        RefreshSecurityPostInspector();
    }

    private void ProcessDisorderCapture()
    {
        if (_disorderCaptureDirectory is null) return;
        _disorderCaptureFrame++;
        if (_disorderCaptureMode == "post-prep")
        {
            ProcessSecurityPostPreparationCapture();
            return;
        }
        if (_disorderCaptureMode == "layout" && _disorderCaptureFrame == 3)
        {
            // Keep the ordinary South angle; widen only for a whole-site
            // beginning/active layout comparison.
            _focus = new Vector3(0, 0, -4); _camera.Size = 70f; ApplyCamera();
        }
        if (_disorderCaptureFrame == 4)
            foreach (var id in new[] { "act.folk", "staff.steward", "equipment.buy" }) _offerButtons[id].EmitSignal(Button.SignalName.Pressed);
        if (_disorderCaptureMode == "layout" && _disorderCaptureFrame == 5)
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_disorderCaptureDirectory, "beginning-south.png"));
        if (_disorderCaptureFrame == 6) _preparationStart.EmitSignal(Button.SignalName.Pressed);
        if (_disorderCaptureMode == "layout")
        {
            ProcessLayoutCapture();
            return;
        }
        if (_disorderCaptureMode == "steward")
        {
            ProcessStewardCapture();
            return;
        }
        if (_disorderCaptureMode == "post")
        {
            ProcessSecurityPostCapture();
            return;
        }
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

    private void ProcessLayoutCapture()
    {
        if (_disorderCaptureFrame == 8)
        {
            var medical = _session.CaptureMedical()!;
            var disorder = _session.CaptureDisorder()!;
            while (_session.CurrentTick < 4_000)
            {
                var agents = _session.CaptureSnapshot().NavigationAgents;
                if (agents.Single(item => item.Id.Value == medical.MedicId).Action == AgentNavigationAction.Arrived &&
                    agents.Single(item => item.Id.Value == disorder.SecurityId).Action == AgentNavigationAction.Arrived &&
                    _session.CaptureLivePerformance()!.Stage == LiveSetStage.Live)
                    break;
                _session.AdvanceWithoutSnapshot(1);
            }
            var liveAgents = _session.CaptureSnapshot().NavigationAgents;
            var medic = liveAgents.Single(item => item.Id.Value == medical.MedicId);
            var security = liveAgents.Single(item => item.Id.Value == disorder.SecurityId);
            if (medic.Destination != GameSession.MedicalMedicCell || medic.Action != AgentNavigationAction.Arrived ||
                security.Destination != GameSession.DisorderSecurityBaseCell || security.Action != AgentNavigationAction.Arrived)
                throw new InvalidOperationException("Layout capture workers did not reach their physical bases.");
            _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
            RefreshPreparationHud();
            GD.Print($"R004_LAYOUT_CAPTURE tick={_session.CurrentTick} medic={medic.Destination} security={security.Destination} orientation=South zoom=70");
        }
        if (_disorderCaptureFrame == 10)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_disorderCaptureDirectory!, "active-south.png"));
            GetTree().Quit();
        }
    }

    private void ProcessStewardCapture()
    {
        var preparation = _session.CapturePreparation()!;
        var jordan = new EntityId(_session.CaptureDisorder()!.SecurityId);
        var riley = new EntityId(_session.CaptureMedical()!.MedicId);
        var casey = new EntityId(preparation.People.Single(item => item.Name == "Casey Vale").AgentId);
        if (_disorderCaptureFrame == 8)
        {
            if (_attendeeVisuals[jordan].GetNodeOrNull<Node3D>("StewardYokeCue") is null ||
                _attendeeVisuals[riley].GetNodeOrNull<Node3D>("StewardYokeCue") is not null ||
                _attendeeVisuals[casey].GetNodeOrNull<Node3D>("StewardYokeCue") is not null)
                throw new InvalidOperationException("Approved steward accessory is not exclusive to Jordan's existing body.");
            while (_session.CurrentTick < 120) _session.AdvanceWithoutSnapshot(1);
            _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
            var travelling = _session.CaptureSnapshot().NavigationAgents.Single(item => item.Id == jordan);
            if (travelling.Action != AgentNavigationAction.Travelling)
                throw new InvalidOperationException("Steward motion capture requires a real travelling route.");
            _focus = new Vector3(travelling.XMillimetres / 1000f, 0, travelling.ZMillimetres / 1000f);
            _camera.Size = 32f; _orientation = 0; ApplyCamera();
            RefreshPreparationHud();
            GD.Print($"STEWARD_CAPTURE moving tick={_session.CurrentTick} position={travelling.XMillimetres},{travelling.ZMillimetres}");
        }
        if (_disorderCaptureFrame == 10)
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_disorderCaptureDirectory!, "moving-south-32.png"));
        if (_disorderCaptureFrame == 11)
        {
            while (_session.CaptureSnapshot().NavigationAgents.Single(item => item.Id == jordan).Action != AgentNavigationAction.Arrived &&
                   _session.CurrentTick < 2_000)
                _session.AdvanceWithoutSnapshot(1);
            var arrived = _session.CaptureSnapshot().NavigationAgents.Single(item => item.Id == jordan);
            if (arrived.Action != AgentNavigationAction.Arrived || arrived.Destination != GameSession.DisorderSecurityBaseCell)
                throw new InvalidOperationException("Jordan did not reach the steward post for front/rear review.");
            _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
            _focus = new Vector3(arrived.XMillimetres / 1000f, 0, arrived.ZMillimetres / 1000f);
            ApplyCamera(); RefreshPreparationHud();
            GD.Print($"STEWARD_CAPTURE base tick={_session.CurrentTick} position={arrived.XMillimetres},{arrived.ZMillimetres}");
        }
        if (_disorderCaptureFrame == 13)
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_disorderCaptureDirectory!, "post-south-32.png"));
        if (_disorderCaptureFrame == 14) { _orientation = 2; ApplyCamera(); }
        if (_disorderCaptureFrame == 16)
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_disorderCaptureDirectory!, "post-north-32.png"));
        if (_disorderCaptureFrame == 17) { _orientation = 0; ApplyCamera(); SelectAttendee(jordan); }
        if (_disorderCaptureFrame == 19)
        {
            var selectedText = _inspectorTitle.Text + _inspectorBody.Text + _disorderSummary!.Text +
                _disorderButtons[DisorderAction.DispatchSecurity].Text;
            if (!selectedText.Contains("Steward", StringComparison.OrdinalIgnoreCase) ||
                selectedText.Contains("security", StringComparison.OrdinalIgnoreCase) ||
                selectedText.Contains("guard", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Player-facing steward wording is incomplete.");
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_disorderCaptureDirectory!, "selected-steward-32.png"));
        }
        if (_disorderCaptureFrame == 20)
        {
            SelectAttendee(riley);
            var medic = _session.CaptureSnapshot().NavigationAgents.Single(item => item.Id == riley);
            _focus = new Vector3(medic.XMillimetres / 1000f, 0, medic.ZMillimetres / 1000f);
            ApplyCamera();
        }
        if (_disorderCaptureFrame == 22)
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_disorderCaptureDirectory!, "comparison-medic-32.png"));
        if (_disorderCaptureFrame == 23)
        {
            SelectAttendee(casey);
            var plain = _session.CaptureSnapshot().NavigationAgents.Single(item => item.Id == casey);
            _focus = new Vector3(plain.XMillimetres / 1000f, 0, plain.ZMillimetres / 1000f);
            ApplyCamera();
        }
        if (_disorderCaptureFrame == 25)
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_disorderCaptureDirectory!, "comparison-plain-32.png"));
        if (_disorderCaptureFrame == 26)
        {
            SelectAttendee(jordan);
            var arrived = _session.CaptureSnapshot().NavigationAgents.Single(item => item.Id == jordan);
            _focus = new Vector3(arrived.XMillimetres / 1000f, 0, arrived.ZMillimetres / 1000f);
            _camera.Size = 18f; _orientation = 0; ApplyCamera();
        }
        if (_disorderCaptureFrame == 28)
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_disorderCaptureDirectory!, "detail-south-18.png"));
        if (_disorderCaptureFrame == 29) { _orientation = 2; ApplyCamera(); }
        if (_disorderCaptureFrame == 31)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_disorderCaptureDirectory!, "detail-north-18.png"));
            var body = _attendeeVisuals[jordan];
            var yoke = body.GetNode<Node3D>("StewardYokeCue");
            var original = body.Rotation;
            var upright = yoke.GlobalTransform.Basis.Y;
            body.Rotation = new Vector3(Mathf.Pi / 2f, original.Y, 0);
            var horizontal = yoke.GlobalTransform.Basis.Y;
            body.Rotation = original;
            if (upright.DistanceTo(horizontal) < .5f)
                throw new InvalidOperationException("Steward accessory did not follow the medical horizontal body pose.");
            GD.Print("STEWARD_CAPTURE verified=yoke-exclusive role=steward wording=steward-only zoom=32 orientations=South,North motion=physical horizontal-pose=attached");
            GetTree().Quit();
        }
    }

    private void ProcessSecurityPostCapture()
    {
        if (_disorderCaptureFrame == 8)
        {
            var id = _session.CaptureDisorder()!.SecurityId;
            while (_session.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == id).Action != AgentNavigationAction.Arrived &&
                   _session.CurrentTick < 2_000)
                _session.AdvanceWithoutSnapshot(1);
            var security = _session.CaptureSnapshot().NavigationAgents.Single(item => item.Id.Value == id);
            if (security.Destination != GameSession.DisorderSecurityBaseCell || security.Action != AgentNavigationAction.Arrived)
                throw new InvalidOperationException("Security did not physically reach the approved post approach.");
            _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
            _focus = new Vector3(-7, 0, 17); _camera.Size = 32f; ApplyCamera();
            RefreshPreparationHud();
            GD.Print($"SECURITY_POST_CAPTURE security={id} base={security.Destination} tick={_session.CurrentTick}");
        }
        if (_disorderCaptureFrame == 10)
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_disorderCaptureDirectory!, "post-default.png"));
        if (_disorderCaptureFrame == 11)
        {
            var centre = TraversalGrid.CellCentre(GameSession.DisorderSecurityPostCell);
            Pick(_camera.UnprojectPosition(new Vector3(centre.XMillimetres / 1000f, 1.5f, centre.ZMillimetres / 1000f)));
            if (!_selectedSecurityPost) throw new InvalidOperationException("Security post did not resolve through the normal pick ray.");
        }
        if (_disorderCaptureFrame == 13)
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_disorderCaptureDirectory!, "post-selected.png"));
        if (_disorderCaptureFrame == 14)
        {
            _securityPostWorkerButton!.EmitSignal(Button.SignalName.Pressed);
            if (_selectedAttendeeId?.Value != _session.CaptureDisorder()!.SecurityId)
                throw new InvalidOperationException("Post action did not select the physical security worker.");
        }
        if (_disorderCaptureFrame == 16)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_disorderCaptureDirectory!, "worker-selected.png"));
            GD.Print("SECURITY_POST_CAPTURE picked=True workerSelected=True zoom=32 orientation=South");
            GetTree().Quit();
        }
    }

    private void ProcessSecurityPostPreparationCapture()
    {
        if (_disorderCaptureFrame == 3)
        {
            _focus = new Vector3(-7, 0, 17); _camera.Size = 32f; ApplyCamera();
        }
        if (_disorderCaptureFrame == 4)
        {
            var centre = TraversalGrid.CellCentre(GameSession.DisorderSecurityPostCell);
            Pick(_camera.UnprojectPosition(new Vector3(centre.XMillimetres / 1000f, 1.5f, centre.ZMillimetres / 1000f)));
            if (!_selectedSecurityPost || _session.CapturePreparation()!.Status != PreparationStatus.Preparing ||
                _attendeeVisuals.Count != 0 || _securityPostWorkerButton!.Visible)
                throw new InvalidOperationException("Preparation post selection exposed a worker without a physical visual.");
            SelectAttendee(new EntityId(_session.CaptureDisorder()!.SecurityId));
            if (!_selectedSecurityPost || _selectedAttendeeId is not null || !_highlight.Visible)
                throw new InvalidOperationException("Unavailable attendee selection changed the preparation inspector.");
        }
        if (_disorderCaptureFrame == 6)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_disorderCaptureDirectory!, "post-preparation.png"));
            GD.Print("SECURITY_POST_PREPARATION selected=True workerActionVisible=False missingVisualGuard=True");
            GetTree().Quit();
        }
    }
}

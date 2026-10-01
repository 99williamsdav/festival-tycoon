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
    private ulong _securityPostPickId;
    private bool _selectedSecurityPost;
    private Button? _securityPostWorkerButton;
    private readonly DisorderCuePlanner _disorderCuePlanner = new();
    private readonly Dictionary<ulong, Label3D> _disorderCueLabels = [];
    private readonly HashSet<ulong> _fightShaking = [];
    private readonly Dictionary<ulong, float> _preFightFacing = [];

    private void ResetDisorderCuePresentation()
    {
        foreach (var id in _fightShaking)
            if (_attendeeVisuals.TryGetValue(new EntityId(id), out var visual))
                visual.Rotation = new Vector3(visual.Rotation.X,
                    _preFightFacing.TryGetValue(id, out var priorYaw) ? priorYaw : visual.Rotation.Y, 0);
        _fightShaking.Clear();
        _preFightFacing.Clear();
        foreach (var label in _disorderCueLabels.Values) label.QueueFree();
        _disorderCueLabels.Clear();
        var disorder = _session.CaptureDisorder();
        _disorderCuePlanner.Reset(disorder, _session.CurrentTick);
        if (disorder is null) return;
        foreach (var id in disorder.People.Select(item => item.AgentId).Concat(_session.GetStewardResponses().Select(item => item.WorkerId)).Distinct())
        {
            if (!_attendeeVisuals.ContainsKey(new EntityId(id))) continue;
            var label = new Label3D { Visible = false, FontSize = 52, PixelSize = .011f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                OutlineSize = 18, OutlineModulate = new Color("2c2020"), NoDepthTest = true };
            AddChild(label);
            _disorderCueLabels.Add(id, label);
        }
    }

    private void AdvanceDisorderCuePresentation()
    {
        if (_session.CaptureDisorder() is not { } disorder) return;
        AdvanceFightShakePresentation(disorder);
        foreach (var label in _disorderCueLabels.Values) label.Visible = false;
        var medical = _session.CaptureMedical();
        var cues = _disorderCuePlanner.Observe(disorder, medical, _session.CurrentTick);
        if (cues.Count > 0 && medical is not null)
        {
            var urgent = medical.Needs.Where(item => item.Stage is MedicalStage.Distress or MedicalStage.Collapsed or MedicalStage.Critical)
                .Select(item => item.AgentId).ToHashSet();
            foreach (var (id, label) in _medicalCueLabels)
                if (!urgent.Contains(id)) label.Visible = false;
        }
        foreach (var cue in cues)
        {
            if (_hudAlerts is not null && cue.Kind is DisorderCueKind.Argument or DisorderCueKind.Fight) continue;
            if (!_disorderCueLabels.TryGetValue(cue.AgentId, out var label) ||
                !_attendeeVisuals.TryGetValue(new EntityId(cue.AgentId), out var visual)) continue;
            if (_medicalCueLabels.TryGetValue(cue.AgentId, out var medicalLabel)) medicalLabel.Visible = false;
            label.Text = cue.Text;
            label.Position = visual.Position + new Vector3(0, cue.Kind == DisorderCueKind.Shout ? 2.95f :
                (cue.AgentId % 2 == 0 ? 3.05f : 3.55f), 0);
            label.Modulate = cue.Kind switch
            {
                DisorderCueKind.Fight => new Color("ff6b65"),
                DisorderCueKind.Argument => new Color("ffb061"),
                _ => new Color("ffe4a1")
            };
            label.Visible = true;
        }
    }

    private void AdvanceFightShakePresentation(DisorderSnapshot disorder)
    {
        var fighters = disorder.People.Where(person => person.Stage == DisorderStage.Fight).ToArray();
        var fighting = fighters.Select(person => person.AgentId).ToHashSet();
        foreach (var response in _session.GetStewardResponses())
            if (fighters.Any(person => person.OpponentId == response.WorkerId)) fighting.Add(response.WorkerId);
        foreach (var id in _fightShaking.Except(fighting))
        {
            if (_attendeeVisuals.TryGetValue(new EntityId(id), out var visual))
            {
                var moving = _session.CaptureObservation().NavigationAgents.Any(agent =>
                    agent.Id.Value == id && agent.Action == AgentNavigationAction.Travelling);
                visual.Rotation = new Vector3(visual.Rotation.X,
                    !moving && _preFightFacing.TryGetValue(id, out var priorYaw) ? priorYaw : visual.Rotation.Y, 0);
            }
            _preFightFacing.Remove(id);
        }
        _fightShaking.Clear();
        var anchors = fighting.Where(id => _attendeeVisuals.ContainsKey(new EntityId(id)))
            .ToDictionary(id => id, id => _attendeeVisuals[new EntityId(id)].Position);
        var time = (float)((_session.CurrentTick + _host.Clock.InterpolationFraction) / 80.0);
        foreach (var id in fighting)
        {
            if (!_attendeeVisuals.TryGetValue(new EntityId(id), out var visual)) continue;
            _preFightFacing.TryAdd(id, visual.Rotation.Y);
            var person = fighters.FirstOrDefault(item => item.AgentId == id);
            var counterpart = person?.OpponentId ?? fighters.FirstOrDefault(item => item.OpponentId == id)?.AgentId;
            var fightTick = person?.StageTick ?? fighters.FirstOrDefault(item => item.OpponentId == id)?.StageTick ?? _session.CurrentTick;
            if (counterpart is { } otherId && anchors.TryGetValue(id, out var anchor) &&
                anchors.TryGetValue(otherId, out var other))
            {
                var toward = other - anchor;
                toward.Y = 0;
                var distance = toward.Length();
                if (distance > .001f)
                {
                    var ease = Mathf.Clamp((float)((_session.CurrentTick - fightTick +
                        _host.Clock.InterpolationFraction) / 48.0), 0f, 1f);
                    var pull = Mathf.Min(.4f, Mathf.Max(0f, (distance - 1.15f) * .5f)) * ease;
                    visual.Position += toward / distance * pull;
                    // Presentation runs after UpdatePersonFacing: the live opponent wins over
                    // travel/stage headings for both guest pairs and guest–steward fights.
                    visual.Rotation = new Vector3(visual.Rotation.X,
                        Mathf.Atan2(-toward.X, -toward.Z), visual.Rotation.Z);
                }
            }
            var phase = time * Mathf.Tau * 5.5f + id * 0.83f;
            // Presentation-only jostle and modest convergence; authoritative positions stay put.
            visual.Position += new Vector3(Mathf.Sin(phase) * .09f, 0,
                Mathf.Sin(phase * 1.3f) * .055f);
            visual.Rotation = new Vector3(visual.Rotation.X, visual.Rotation.Y,
                Mathf.Sin(phase * .75f) * .055f);
            _fightShaking.Add(id);
        }
    }

    private void BuildDisorderWorld()
    {
        var centre = TraversalGrid.CellCentre(GameSession.DisorderSecurityPostCell);
        var position = new Vector3(centre.XMillimetres / 1000f, 0, centre.ZMillimetres / 1000f);
        var post = AddAsset("res://assets/environment/lwf_security_post_v1.glb", position);
        post.RotationDegrees = new Vector3(0, 90, 0);
        var pick = new StaticBody3D { Position = position + new Vector3(0, 1.55f, 0),
            CollisionLayer = 1, CollisionMask = 0 };
        pick.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2.38f, 3.1f, 2.33f) } });
        AddChild(pick);
        _securityPostPickId = pick.GetInstanceId();
        _responsePostVisuals[ResponseRole.Steward]=post;_responsePostPicks[ResponseRole.Steward]=pick;
        var postName=BuildingName("STEWARD POST",Vector3.Zero);AddChild(postName);_responsePostLabels[ResponseRole.Steward]=postName;SyncResponsePosts();
    }

    private void BuildSecurityPostInspectorAction(VBoxContainer parent)
    {
        if (_session.CaptureDisorder() is null) return;
        _securityPostWorkerButton = ButtonText("SELECT JORDAN • STEWARD", () =>
            SelectAttendee(new EntityId(_session.CaptureDisorder()!.SecurityId)));
        _securityPostWorkerButton.Visible = false;
        parent.AddChild(_securityPostWorkerButton);
        _stewardMoveButton=ButtonText("Move",()=>BeginResponsePostPlacement(ResponseRole.Steward));_stewardMoveButton.Visible=false;parent.AddChild(_stewardMoveButton);
    }

    private void SelectSecurityPost()
    {
        _selectedBinId = null;
        _selectedGenerator = false;
        _selectedImmersionVendor = null;
        _selected = null; _selectedAttendeeId = null; _selectedMedicalFacility = null;
        RefreshSatisfactionBar(null);
        RefreshStagePowerAction();
        _selectedSecurityPost = true;
        RefreshMedicalNeedBars(null);
        RefreshMedicalActionInspector();
        RefreshDisorderActionInspector();
        var centre = TraversalGrid.CellCentre(_session.CaptureResponsePost(ResponseRole.Steward).Cell);
        _highlight.Position = new Vector3(centre.XMillimetres / 1000f, .08f, centre.ZMillimetres / 1000f);
        _highlight.Scale = new Vector3(2.4f, 1, 2.4f); _highlight.Visible = true;
        RefreshSecurityPostInspector();
        GD.Print("SECURITY_POST_SELECTED");
    }

    private void ClearSecurityPostSelection()
    {
        _selectedGenerator = false;
        _selectedSecurityPost = false;
        if (_securityPostWorkerButton is not null) _securityPostWorkerButton.Visible = false;
    }

    private void RefreshSecurityPostInspector()
    {
        RefreshResponsePostMoveButtons();
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
        var security = d.Stewards[0];
        var target = security.TargetId is { } id ? people.Single(item => item.AgentId == id).Name : "None";
        _inspectorTitle.Text = $"Steward post • {worker.Name}";
        _inspectorBody.Text = $"POST  open public approach • gate route clear\n" +
            $"WORKER  {(worker.Admitted ? d.SecurityIncapacitated ? "injured • needs medic" : "on site" : "walking in")}\n" +
            $"POSITION  {(position is null ? "not yet arrived" : $"{position.XMillimetres / 1000m:0.00} m, {position.ZMillimetres / 1000m:0.00} m")}\n" +
            $"RESPONSE  {security.Stage} • target {target}\n{StewardWording(security.Description)}\n" +
            "Select an affected guest to dispatch a steward, or ask a named steward/medic to physically escort them to the gate. " +
            (workerAvailable ? "Select Jordan here if he needs medical help. " :
                "Jordan can be selected after the weekend starts and his physical visual exists. ") +
            "Stewards do not teleport or guarantee de-escalation.";
    }

    private void BuildDisorderControls(VBoxContainer box)
    {
        _disorderSummary = LabelText("", 14, new Color("713c35"));
        _disorderSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _disorderSummary.CustomMinimumSize = new Vector2(370, 145);
        box.AddChild(_disorderSummary);
    }

    private void BuildDisorderStageInspector(VBoxContainer parent)
    {
        if (_session.CaptureDisorder() is null) return;
        var button = ButtonText("SAFE RESET MUSIC", () => CommitDisorderAction(DisorderAction.RestoreMusic));
        button.AddThemeFontSizeOverride("font_size", 12);
        button.Visible = false;
        parent.AddChild(button);
        _disorderButtons.Add(DisorderAction.RestoreMusic, button);
    }

    private void BuildDisorderActionInspector()
    {
        if (_session.CaptureDisorder() is null || _medicalActionInspector is null) return;
        foreach (var (action, label) in new[] {
            (DisorderAction.DispatchSecurity, "Send steward") })
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
        if (_session.GetResponseStaff().Any(item => item.AgentId == id))
            return ResponseStaffInspectorText(id);
        var person = d.People.SingleOrDefault(item => item.AgentId == id);
        if (person is null) return "";
        var counterpartLine = DisorderCuePlanner.CurrentCounterpartInspectorLine(d, person,
            otherId => _session.CapturePreparation()!.People.SingleOrDefault(item => item.AgentId == otherId)?.Name ?? $"person {otherId}",
            otherId => _session.CaptureSnapshot().NavigationAgents.SingleOrDefault(item => item.Id.Value == otherId) is { } position
                ? $"{position.XMillimetres / 1000m:0.0}, {position.ZMillimetres / 1000m:0.0} m" : null);
        return $"DISORDER • {person.Stage} • pressure {person.Pressure / 100m:0}%\n" +
            $"CAUSE {person.Grievance} • {(person.Stage == DisorderStage.Injured ? "FIRST AID NEEDED" : person.Grievance == DisorderGrievance.WaterWait ? "dispatch a steward; attendees choose their taps" : "reduce pressure or dispatch a steward")}\n" +
            counterpartLine;
    }

    private void CommitDisorderAction(DisorderAction action, ulong? workerId = null)
    {
        var personTargeted = action is DisorderAction.DispatchSecurity or DisorderAction.SafeEgress;
        var selected = _selectedAttendeeId?.Value;
        if (personTargeted && (selected is null || _session.CaptureDisorder()?.People.Any(item => item.AgentId == selected) != true))
        {
            _preparationMessage = "Select an affected guest before choosing a person action.";
            RefreshPreparationHud();
            return;
        }
        var command = new DisorderCommand(action, personTargeted ? selected : null, workerId);
        if (action == DisorderAction.DispatchSecurity && workerId is null)
        {
            if (_session.SelectRoleResponse(ResponseRole.Steward, selected!.Value, out var issue) is not DisorderCommand roleCommand)
            { _preparationMessage = issue!; RefreshPreparationHud(); return; }
            command = roleCommand;
        }
        if (_host.Execute(command, out var error))
            _preparationMessage = "Disorder action applied; next background save is within 30 unpaused seconds.";
        else _preparationMessage = StewardWording(error!);
        RefreshPreparationHud();
    }

    private void RefreshDisorderActionInspector()
    {
        var d = _session.CaptureDisorder();
        if (_disorderButtons.TryGetValue(DisorderAction.RestoreMusic, out var stageButton))
            stageButton.Visible = d is not null && _selected?.Kind == FarmObjectKind.TrailerStage;
        if (d is null) return;
        foreach (var action in new[] { DisorderAction.DispatchSecurity, DisorderAction.SafeEgress })
        {
            if (!_disorderButtons.TryGetValue(action, out var button)) continue;
            var selected = _selectedAttendeeId?.Value;
            string? issue = "Select an affected guest.";
            if (selected is { } id) _session.SelectRoleResponse(ResponseRole.Steward, id, out issue);
            button.Disabled = issue is not null;
            button.TooltipText = StewardWording(issue ?? "Send nearest available suitable steward; physical arrival required.");
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
            string.Join("\n", _session.GetStewardResponses().Select(job => $"{_session.CapturePreparation()!.People.Single(item => item.AgentId == job.WorkerId).Name.Split(' ')[0]}: {(job.Incapacitated ? "INJURED • MEDIC NEEDED" : ActiveStaffInterventionSummary(job.WorkerId) ?? job.Stage.ToString())}")) + "\n" +
            $"{(latest is null ? "No complaint" : StewardWording(latest.Description))}\n" +
            "Complaint and argument precede any confrontation. Select a person for staff help; attendees choose their taps. Essential water stays available.";
        foreach (var action in new[] { DisorderAction.RestoreMusic })
        {
            var button = _disorderButtons[action];
            var error = _session.ValidateCommand(CampaignEnvelope(new DisorderCommand(action)));
            button.Disabled = error is not null;
            button.TooltipText = StewardWording(error?.Message ?? "");
        }
        RefreshDisorderActionInspector();
        RefreshSecurityPostInspector();
    }


}

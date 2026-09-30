using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Festival.Game;

public partial class Main
{
    private string? _layoutCaptureDirectory;
    private int _layoutCaptureFrame;
    private int _layoutCaptureStep;
    private string _layoutCosmeticHash = "";
    private static readonly PropertyInfo LayoutMedicalField = typeof(GameSession).GetProperty("MedicalView", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo LayoutDisorderField = typeof(GameSession).GetProperty("DisorderView", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo LayoutApplyDestination = typeof(GameSession).GetMethod("ApplyAgentDestination", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private void LayoutImage(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        if (image.GetWidth() != GetWindow().Size.X || image.GetHeight() != GetWindow().Size.Y)
            throw new InvalidOperationException("Native PNG dimensions differ from capture window.");
        var error = image.SavePng(Path.Combine(_layoutCaptureDirectory!, name + ".png"));
        if (error != Error.Ok) throw new InvalidOperationException("Layout image failed: " + error);
    }
    private void LayoutFacingGuard(StaffProfile worker, bool idle)
    {
        var id = new EntityId(worker.AgentId); var nav = _session.CaptureObservation().NavigationAgents.Single(n => n.Id == id);
        var body = _attendeeVisuals[id]; var priorHash = _session.CaptureSnapshot().AuthoritativeHash;
        UpdatePersonFacing(id, body, body.Position, nav.Action, false, false, 1);
        var expected = worker.Role == ResponseRole.Medic ? Vector3.Back : Vector3.Right;
        var dot = (-body.Basis.Z).Normalized().Dot(expected);
        if (idle && dot < .999f) throw new InvalidOperationException("Idle actor outward front failed: " + worker.Name);
        if (priorHash != _session.CaptureSnapshot().AuthoritativeHash) throw new InvalidOperationException("Facing changed authoritative state.");
        GD.Print($"LAYOUT_STAFF worker={worker.Name} role={worker.Role} idle={idle} action={nav.Action} actual_front_dot={dot:F4} hash_pure=True");
    }
    private void LayoutBusyAndCollapseGuards()
    {
        var medical = _session.CaptureMedical()!; var disorder = _session.CaptureDisorder()!;
        var hash = _session.CaptureSnapshot().AuthoritativeHash;
        foreach (var worker in _session.GetResponseStaff())
        {
            var id = new EntityId(worker.AgentId); var body = _attendeeVisuals[id];
            var job = worker.Role == ResponseRole.Medic ? "treatment" : "calming";
            // Labelled temporary ownership guards at the duty base. These do not
            // claim naturally occurring treatment/calming or advance any fixture job.
            if (worker.Role == ResponseRole.Medic)
                LayoutMedicalField.SetValue(_session, worker.AgentId == medical.MedicId
                    ? medical with { ResponseStage = MedicalResponseStage.Treating, ResponsePatientId = medical.AtRiskGuestId }
                    : medical with { ExtraResponses = medical.ExtraResponses.Select(r => r.WorkerId == worker.AgentId ? r with { Stage = MedicalResponseStage.Treating, PatientId = medical.AtRiskGuestId } : r).ToArray() });
            else
                LayoutDisorderField.SetValue(_session, worker.AgentId == disorder.SecurityId
                    ? disorder with { ResponseStage = SecurityResponseStage.Calming, ResponseTargetId = medical.AtRiskGuestId }
                    : disorder with { ExtraResponses = disorder.ExtraResponses.Select(r => r.WorkerId == worker.AgentId ? r with { Stage = SecurityResponseStage.Calming, TargetId = medical.AtRiskGuestId } : r).ToArray() });
            body.Rotation = new Vector3(0, .37f, 0);
            _lastPresentedPersonPositions[id] = body.Position;
            UpdatePersonFacing(id, body, body.Position, AgentNavigationAction.Arrived, false, false, 1);
            if (worker.Role == ResponseRole.Steward)
            {
                var targetBody = _attendeeVisuals[new EntityId(medical.AtRiskGuestId)];
                var toward = targetBody.Position - body.Position;
                var expectedYaw = Mathf.Atan2(-toward.X, -toward.Z);
                if (Math.Abs(Mathf.Wrap(body.Rotation.Y - expectedYaw, -Mathf.Pi, Mathf.Pi)) > .001f)
                    throw new InvalidOperationException("Attending steward did not face the live person: " + worker.Name);
                GD.Print($"LAYOUT_STEWARD_ATTENDING worker={worker.Name} target={medical.AtRiskGuestId} face_target=True");
            }
            else if (Math.Abs(body.Rotation.Y - .37f) > .001f)
                throw new InvalidOperationException("Active duty job snapped idle: " + worker.Name);
            LayoutMedicalField.SetValue(_session, medical); LayoutDisorderField.SetValue(_session, disorder);
            if (worker.Role == ResponseRole.Steward)
            {
                body.Rotation = new Vector3(0, .37f, 0);
                LayoutDisorderField.SetValue(_session, worker.AgentId == disorder.SecurityId
                    ? disorder with { ResponseStage = SecurityResponseStage.Calming, ResponseTargetId = medical.AtRiskGuestId }
                    : disorder with { ExtraResponses = disorder.ExtraResponses.Select(r => r.WorkerId == worker.AgentId ? r with { Stage = SecurityResponseStage.Calming, TargetId = medical.AtRiskGuestId } : r).ToArray() });
                LayoutMedicalField.SetValue(_session, medical with { Needs = medical.Needs.Select(n => n.AgentId == medical.AtRiskGuestId
                    ? n with { Intent = MedicalIntent.Collapsed, Stage = MedicalStage.Collapsed } : n).ToArray() });
                UpdatePersonFacing(id, body, body.Position, AgentNavigationAction.Arrived, false, false, 1);
                if (Math.Abs(body.Rotation.Y - .37f) > .001f) throw new InvalidOperationException("Collapsed target still controlled steward facing.");
                LayoutMedicalField.SetValue(_session, medical); LayoutDisorderField.SetValue(_session, disorder);
                GD.Print($"LAYOUT_STEWARD_INVALID_TARGET worker={worker.Name} collapsed_target_ignored=True");
                LayoutDisorderField.SetValue(_session, worker.AgentId == disorder.SecurityId
                    ? disorder with { SecurityIncapacitated = true, ResponseStage = SecurityResponseStage.Failed }
                    : disorder with { ExtraResponses = disorder.ExtraResponses.Select(r => r.WorkerId == worker.AgentId ? r with { Incapacitated = true, Stage = SecurityResponseStage.Failed } : r).ToArray() });
                UpdatePersonFacing(id, body, body.Position, AgentNavigationAction.Arrived, false, false, 1);
                if (Math.Abs(body.Rotation.Y - .37f) > .001f) throw new InvalidOperationException("Incapacitated steward snapped idle.");
                LayoutDisorderField.SetValue(_session, disorder);
            }
            // A live intervention owns the worker even when navigation has stopped.
            LayoutMedicalField.SetValue(_session, medical with { StaffInterventions = [new(worker.AgentId, medical.AtRiskGuestId,
                StaffInterventionAction.GuideToRest, StaffInterventionStage.Guiding, _session.CurrentTick, _session.CurrentTick, -1, _session.CurrentTick, null, "labelled stopped intervention guard")] });
            UpdatePersonFacing(id, body, body.Position, AgentNavigationAction.Arrived, false, false, 1);
            if (Math.Abs(body.Rotation.Y - .37f) > .001f) throw new InvalidOperationException("Intervention snapped idle.");
            LayoutMedicalField.SetValue(_session, medical);
            // Authoritative arrival may precede the last rendered movement. Even a
            // 1mm previous render delta must not snap an otherwise idle actor outward.
            _lastPresentedPersonPositions[id] = body.Position - Vector3.Back * .001f;
            UpdatePersonFacing(id, body, body.Position, AgentNavigationAction.Arrived, false, false, 1);
            if (Math.Abs(body.Rotation.Y - .37f) > .001f) throw new InvalidOperationException("Final interpolated movement snapped idle.");
            _lastPresentedPersonPositions[id] = body.Position;
            // Production collapse presentation skips the facing update entirely.
            LayoutMedicalField.SetValue(_session, medical with { Needs = medical.Needs.Select(n => n.AgentId == worker.AgentId
                ? n with { Intent = MedicalIntent.Collapsed, Stage = MedicalStage.Collapsed } : n).ToArray() });
            AdvancePreparationPresentation(0);
            if (Math.Abs(body.Rotation.X - Mathf.Pi / 2) > .001f) throw new InvalidOperationException("Collapse lost precedence.");
            LayoutMedicalField.SetValue(_session, medical);
            body.Rotation = Vector3.Zero;
            GD.Print($"LAYOUT_LABELLED_GUARD worker={worker.Name} {job}_precedence=True stopped_intervention_precedence=True incapacity_excluded=True final_1mm_renderdelta_excluded=True collapse_precedence=True");
        }
        if (hash != _session.CaptureSnapshot().AuthoritativeHash) throw new InvalidOperationException("Temporary guards did not restore exact source state.");
    }
    private void ProcessLayoutPolishCapture()
    {
        if (_layoutCaptureDirectory is null || ++_layoutCaptureFrame % 4 != 0) return;
        try { RunLayoutPolishCaptureStep(); }
        catch (Exception error)
        {
            GD.PushError("LAYOUT_CAPTURE_FAILED " + error);
            _layoutCaptureDirectory = null;
            GetTree().Quit(2);
        }
    }
    private void RunLayoutPolishCaptureStep()
    {
        switch (_layoutCaptureStep++)
        {
            case 0:
                if (IncludeGenericServicePointDiagnostic || _visualRegistry.ContainsKey("farm.service-point") || _pickRegistry.Values.Any(v => v.StableId == "farm.service-point"))
                    throw new InvalidOperationException("Normal generic service mesh/pick omission failed.");
                SelectObject(LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.service-point"));
                if (_selected is not null) throw new InvalidOperationException("Omitted object became interactable.");
                var small = _visualRegistry["farm.small-barn"]; var large = _visualRegistry["farm.large-barn"];
                var smallAsset = small.GetChildren().OfType<Node3D>().First(); var largeAsset = large.GetChildren().OfType<Node3D>().First();
                // Door centre from each approved asset record, evaluated through the
                // instantiated asset transform (including any hidden child transform).
                var smallAxis = (smallAsset.ToGlobal(new Vector3(0,0,4.34f)) - smallAsset.ToGlobal(Vector3.Zero)).Normalized();
                var largeAxis = (largeAsset.ToGlobal(new Vector3(0,0,5.79f)) - largeAsset.ToGlobal(Vector3.Zero)).Normalized();
                if (smallAxis.Dot(largeAxis) < .999f || smallAxis.Dot(Vector3.Back) < .999f)
                    throw new InvalidOperationException("Actual barn door world axes differ.");
                var sb = GetCombinedMeshBounds([small]); var lb = GetCombinedMeshBounds([large]);
                if (sb.Intersects(lb)) throw new InvalidOperationException("Actual barn render bounds overlap.");
                var pickBox = small.GetChildren().OfType<CollisionShape3D>().Single();
                if (pickBox.Shape is not BoxShape3D { Size.X: > 14.6f, Size.Z: < 10.1f } || Math.Abs(small.Rotation.Y) > .001f)
                    throw new InvalidOperationException("Small barn selection footprint did not rotate with its real body.");
                _layoutCosmeticHash = _session.CaptureSnapshot().AuthoritativeHash;
                _hudPreparationToggle!.EmitSignal(Button.SignalName.Pressed);
                _focus = new Vector3(8,0,-19); _camera.Size = 44; _orientation = 0; ApplyCamera();
                GD.Print($"LAYOUT_BARNS actual_door_axis_dot={smallAxis.Dot(largeAxis):F4} front_positive_z=True render_bounds_overlap=False selection_yaw0=True generic_normal_mesh_pick_absent=True content={LowerWitteringFarmScenario.ContentCompatibilityHash}");
                return;
            case 1: LayoutImage("barns-front-fixed-view-0"); Rotate(1); return;
            case 2: LayoutImage("barns-fixed-view-1"); Rotate(1); return;
            case 3: LayoutImage("barns-fixed-view-2"); Rotate(1); return;
            case 4: LayoutImage("barns-fixed-view-3"); Rotate(1); return;
            case 5:
                if (_layoutCosmeticHash != _session.CaptureSnapshot().AuthoritativeHash) throw new InvalidOperationException("Camera/omission changed authoritative state.");
                foreach(var effect in new[]{"staff.medic-slot","staff.steward-slot"}) StaffCaptureSend(new ApplyStaffFoundationEffectCommand(effect));
                foreach(var offer in new[]{"staff.extra-medic","staff.extra-steward","staff.steward"}) StaffCaptureSend(new AcceptPreparationOfferCommand(offer));
                StaffCaptureSend(new SetProgrammeCommand(["act.meadow-lanterns","act.barnstorm-circuit","act.field-frequency"]));
                PreparationStart(); StaffCaptureAdvance(1800); AdvancePreparationPresentation(0);
                _preparationMessage = "LABELLED LAYOUT DEMO • legacy capacity slots; two £30 hires; 1800 accelerated production ticks.";
                foreach(var worker in _session.GetResponseStaff()) LayoutFacingGuard(worker,true);
                _focus = ImmersionPosition(GameSession.MedicalMedicCell); _camera.Size = 16; ApplyCamera(); RefreshPreparationHud();
                return;
            case 6: LayoutImage("medics-idle-outward"); _focus=ImmersionPosition(GameSession.DisorderSecurityBaseCell); ApplyCamera(); return;
            case 7:
                LayoutImage("stewards-idle-outward"); LayoutBusyAndCollapseGuards();
                foreach(var worker in _session.GetResponseStaff())
                {
                    var nav=_session.CaptureSnapshot().NavigationAgents.Single(n=>n.Id.Value==worker.AgentId);
                    // Explicit routing fixture invokes the existing production routing
                    // seam; normal prepared commands correctly forbid arbitrary routes.
                    LayoutApplyDestination.Invoke(_session,[nav.Id,new SetAgentDestinationCommand(new(nav.Destination!.Value.X,nav.Destination.Value.Z+4),"layout-labelled-walk-guard"),false]);
                }
                StaffCaptureAdvance(20); AdvancePreparationPresentation(0); return;
            case 8:
                foreach(var worker in _session.GetResponseStaff())
                {
                    var id=new EntityId(worker.AgentId);var body=_attendeeVisuals[id];var nav=_session.CaptureObservation().NavigationAgents.Single(n=>n.Id==id);
                    if(nav.Action!=AgentNavigationAction.Travelling)throw new InvalidOperationException("Labelled real walking route did not remain travelling.");
                    _lastPresentedPersonPositions[id]=body.Position-Vector3.Back*.1f;
                    UpdatePersonFacing(id,body,body.Position,nav.Action,false,false,1);
                    if((-body.Basis.Z).Dot(Vector3.Back)<.999f)throw new InvalidOperationException("Actual walking facing overridden by idle.");
                    var duty=worker.Role==ResponseRole.Medic?GameSession.MedicalMedicCell:GameSession.DisorderSecurityBaseCell;
                    var baseline=worker.Role==ResponseRole.Medic?_session.CaptureMedical()!.MedicId:_session.CaptureDisorder()!.SecurityId;
                    if(worker.AgentId!=baseline)duty=new(duty.X+2,duty.Z);
                    LayoutApplyDestination.Invoke(_session,[id,new SetAgentDestinationCommand(duty,"layout-labelled-return-guard"),false]);
                    GD.Print($"LAYOUT_LABELLED_WALK_GUARD worker={worker.Name} route_action={nav.Action} previous_render_position_initialized_0_1m=True travel_heading_precedence=True");
                }
                LayoutImage("stewards-labelled-production-walking"); StaffCaptureAdvance(120); AdvancePreparationPresentation(0); return;
            case 9:
                foreach(var worker in _session.GetResponseStaff()) LayoutFacingGuard(worker,true);
                LayoutImage("stewards-returned-outward");
                var oldSession=_session; var hash=_session.CaptureSnapshot().AuthoritativeHash;PreparationSave();
                var independentlyLoaded=SaveFileAdapter.LoadSlot(SaveDirectory,"manual-preparation",_saveCompatibility);
                if(!independentlyLoaded.IsSuccess || independentlyLoaded.Session!.CaptureSnapshot().AuthoritativeHash!=hash)
                    throw new InvalidOperationException("Native actual save file failed: "+independentlyLoaded.Error);
                PreparationLoad();
                if(ReferenceEquals(oldSession,_session) || hash!=_session.CaptureSnapshot().AuthoritativeHash)throw new InvalidOperationException("Native current file restore failed or differs.");
                GD.Print($"LAYOUT_CAPTURE_COMPLETE barn_actual_axes=True four_fixed_quarterturn_views=True generic_normal_mesh_pick_absent=True idle_four_workers=True labelled_busy_collapse_intervention_guards=True labelled_real_route_and_initialized_renderdelta_walk_return=True cosmetic_hash_pure=True exact_file_restore=True hash={hash} size={GetWindow().Size}");
                GetTree().Quit(); return;
        }
    }
}

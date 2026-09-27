using Festival.Simulation;
using Festival.Persistence;
using Godot;
using System;
using System.IO;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private string? _postCaptureDirectory;
    private int _postCaptureFrame;
    private string _postCaptureImage="";
    private Vector2 _postCapturePointer;
    private string _postCaptureHash="";
    private Vector2 PostScreen(GridCell cell)=>_camera.UnprojectPosition(ImmersionPosition(cell));
    private void PostPointer(Vector2 point){_postCapturePointer=point;HoverMovePointer(point);}
    private void PostShot(string name)=>_postCaptureImage=name;
    private void PostAssert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private void PostHover(ResponseRole? role,string? vendor,string image)
    {
        var body=role is {} r?_responsePostVisuals[r]:_immersionVendors[vendor!];
        _focus=body.GlobalPosition;_camera.Size=50;ApplyCamera();ClearSelection();
        PostPointer(_camera.UnprojectPosition(body.GlobalPosition+Vector3.Up*1.5f));PostShot(image);
    }
    private void ProcessResponsePostCapture()
    {
        if(_postCaptureDirectory is null)return;
        try
        {
            var frame=++_postCaptureFrame;
            if(_movingResponsePost is not null)UpdateResponsePostPreview(_postCapturePointer);
            HoverMovePointer(_postCapturePointer);UpdateHoverFeedback(_postCapturePointer);
            if(frame%6==5 && _postCaptureImage!="")
            {
                var image=GetViewport().GetTexture().GetImage();PostAssert(image.GetWidth()==GetWindow().Size.X && image.GetHeight()==GetWindow().Size.Y,"Native capture dimensions differ");
                PostAssert(image.SavePng(Path.Combine(_postCaptureDirectory,_postCaptureImage+".png"))==Error.Ok,"PNG failed");
                GD.Print($"POST_CAPTURE image={_postCaptureImage} mode={_movingResponsePost} candidate={_postCandidate} issue={_postIssue??"none"} hover={_hoveredColliderId} scale={_hoverHighlight.Scale} centre={_hoverHighlight.Position} yaw={_hoverHighlight.Rotation.Y} zoom={_camera.Size} hash={_session.CaptureSnapshot().AuthoritativeHash}");_postCaptureImage="";
            }
            if(frame%6!=0)return;
            switch(frame/6)
            {
                case 1:
                    GD.Print("POST_CAPTURE_SETUP scripted_native=True manual_desktop_QA=False fixture=legacy_capacity_paid_hires accelerated_ticks=True isolated_saves=True");
                    _hudWorkspaceOpen=false;_hudProgrammeOpen=false;RefreshHudWorkspace();
                    _focus=ImmersionPosition(GameSession.MedicalTentCell);ApplyCamera();SelectMedicalFacility(MedicalFacility.FirstAid);
                    PostAssert(_firstAidMoveButton?.Visible==true && _firstAidMoveButton.Text=="Move","First aid contextual Move missing");PostShot("01-first-aid-context-move");break;
                case 2:
                    _postCaptureHash=_session.CaptureSnapshot().AuthoritativeHash;
                    _firstAidMoveButton!.EmitSignal(BaseButton.SignalName.Pressed);
                    _focus=ImmersionPosition(new(80,120));ApplyCamera();PostPointer(PostScreen(new(80,120)));UpdateResponsePostPreview(_postCapturePointer);PostShot("02-valid-tent-preview");break;
                case 3:
                    PostAssert(_postIssue is null && _postPreview!.Visible && _hudPlacement!.Visible,"Valid tent preview/bar missing");
                    _UnhandledInput(new InputEventKey{Keycode=Key.Period,Pressed=true});UpdateResponsePostPreview(_postCapturePointer);PostShot("03-rotated-tent-preview");break;
                case 4:
                    PostAssert(_postQuarterTurns==1 && _postIssue is null,"Tent rotation rejected");
                    PostPointer(PostScreen(GameSession.MedicalWaterCell));UpdateResponsePostPreview(_postCapturePointer);PostShot("04-invalid-tent-preview");break;
                case 5:
                    PostAssert(_postIssue is not null && _feedbackCursor==Input.CursorShape.Forbidden,"Invalid preview/cursor missing");
                    CommitResponsePostPlacement(_postCapturePointer);PostAssert(_postCaptureHash==_session.CaptureSnapshot().AuthoritativeHash,"Invalid commit changed state");
                    _UnhandledInput(new InputEventKey{Keycode=Key.Escape,Pressed=true});PostAssert(_movingResponsePost is null && !_hudPlacement!.Visible && _postCaptureHash==_session.CaptureSnapshot().AuthoritativeHash,"Cancel mutated state or retained HUD preview");PostShot("05-cancelled-tent");break;
                case 6:
                    BeginResponsePostPlacement(ResponseRole.Medic);_postQuarterTurns=1;_focus=ImmersionPosition(new(80,120));ApplyCamera();PostPointer(PostScreen(new(80,120)));UpdateResponsePostPreview(_postCapturePointer);CommitResponsePostPlacement(_postCapturePointer);
                    PostAssert(_movingResponsePost is null && !_hudPlacement!.Visible && _session.CaptureResponsePost(ResponseRole.Medic)==new ResponsePostPlacement(new(80,120),1),"Tent commit/autosave failed or retained HUD preview");
                    PostHover(ResponseRole.Medic,null,"06-moved-tent-hover-default-zoom");break;
                case 7:
                    PostAssert(_hoveredColliderId==_responsePostPicks[ResponseRole.Medic].GetInstanceId(),"Moved tent first-hit hover failed");Pick(_postCapturePointer);PostAssert(_selectedMedicalFacility==MedicalFacility.FirstAid,"Moved tent pick failed");
                    _focus=ImmersionPosition(GameSession.DisorderSecurityPostCell);ApplyCamera();SelectSecurityPost();PostAssert(_stewardMoveButton?.Visible==true,"Steward Move missing");PostShot("07-steward-context-move");break;
                case 8:
                    _stewardMoveButton!.EmitSignal(BaseButton.SignalName.Pressed);_focus=ImmersionPosition(new(145,170));ApplyCamera();PostPointer(PostScreen(new(145,170)));UpdateResponsePostPreview(_postCapturePointer);PostShot("08-steward-valid-preview");break;
                case 9:
                    PostAssert(_postIssue is null,"Steward valid site failed");_UnhandledInput(new InputEventKey{Keycode=Key.Comma,Pressed=true});UpdateResponsePostPreview(_postCapturePointer);PostAssert(_postQuarterTurns==0 && _postIssue is null,"Steward rotation failed");PostShot("09-steward-rotated-preview");break;
                case 10:
                    _UnhandledInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=_postCapturePointer});PostAssert(_movingResponsePost is null,"Steward commit failed");PostHover(ResponseRole.Steward,null,"10-moved-steward-hover-default-zoom");break;
                case 11:
                    PostAssert(_hoveredColliderId==_responsePostPicks[ResponseRole.Steward].GetInstanceId(),"Moved steward hover failed");Pick(_postCapturePointer);PostAssert(_selectedSecurityPost,"Moved steward pick failed");PostHover(null,"food","11-food-hover-default-zoom");break;
                case 12:
                    PostAssert(_hoveredColliderId==_immersionVendors["food"].GetInstanceId(),"Food hover failed");PostHover(null,"drinks","12-bar-hover-default-zoom");break;
                case 13:
                    PostAssert(_hoveredColliderId==_immersionVendors["drinks"].GetInstanceId(),"Bar hover failed");
                    foreach(var role in Enum.GetValues<ResponseRole>())
                    {
                        var body=_responsePostVisuals[role];var geometry=BuildingHoverGeometry(body);
                        var dimensions=geometry.Scale/.73f;
                        GD.Print($"POST_CAPTURE measured_post={role} local_centre={geometry.Centre} rendered_xz={dimensions.X},{dimensions.Z} ring_radii={geometry.Scale.X},{geometry.Scale.Z}");
                        PostAssert(Math.Abs(dimensions.X-(role==ResponseRole.Medic?3.365f:2.38f))<.05f && Math.Abs(dimensions.Z-(role==ResponseRole.Medic?4.1225f:2.33f))<.05f,"Approved post rendered extents differ");
                    }
                    PostAssert(_immersionVendors["drinks"].GetNode<Label3D>("VendorCategoryLabel").Text=="BAR","Venue name differs");
                    PostAssert(!_hudWorkspace!.FindChildren("*","Button",true,false).OfType<Button>().Any(b=>b.Text=="Inspect drinks stall"),"Removed shortcut still present");
                    PreparationSave();_postCaptureHash=_session.CaptureSnapshot().AuthoritativeHash;BeginResponsePostPlacement(ResponseRole.Medic);PostPointer(PostScreen(new(80,125)));UpdateResponsePostPreview(_postCapturePointer);PreparationLoad();
                    PostAssert(_movingResponsePost is null && _postPreview is null && _session.CaptureSnapshot().AuthoritativeHash==_postCaptureHash,"Load did not clear preview/restore exact state");
                    SelectHudTab("Site & water");_hudWorkspaceOpen=true;RefreshHudWorkspace();PostShot("13-site-shortcut-absent-save-load");break;
                case 14:
                    _hudWorkspaceOpen=false;RefreshHudWorkspace();
                    foreach(var effect in new[]{"staff.medic-slot","staff.steward-slot"})CommitEquipmentAction(new ApplyStaffFoundationEffectCommand(effect));
                    foreach(var offer in new[]{"staff.extra-medic","staff.extra-steward","staff.steward"})CommitEquipmentAction(new AcceptPreparationOfferCommand(offer));
                    CommitEquipmentAction(new SetProgrammeCommand(["act.meadow-lanterns","act.barnstorm-circuit","act.field-frequency"]));PreparationStart();TimetableAdvanceTo(1800);_session.Execute(CampaignEnvelope(new SetPausedCommand(true)));_foundationPresentation.Reset(_session.CaptureObservation());
                    _focus=ImmersionPosition(new(80,120));ApplyCamera();PostShot("14-baseline-hired-medic-fronts");break;
                case 15:
                    foreach(var worker in _session.GetResponseStaff())
                    {
                        var nav=_session.CaptureObservation().NavigationAgents.Single(n=>n.Id.Value==worker.AgentId);
                        PostAssert(_session.IdleResponseStaffRole(nav.Id,nav.XMillimetres,nav.ZMillimetres)==worker.Role,"Worker not physically idle at moved front: "+worker.Name);
                        var body=_attendeeVisuals[nav.Id];var front=GameSession.RotateWaterOffset(new(0,1),_session.CaptureResponsePost(worker.Role).QuarterTurns);
                        var direction=-body.GlobalBasis.Z;PostAssert(direction.Dot(new Vector3(front.X,0,front.Z))>.999,"Worker outward facing incorrect: "+worker.Name);
                    }
                    _focus=ImmersionPosition(new(145,170));ApplyCamera();PostShot("15-baseline-hired-steward-fronts");break;
                case 16:
                    PostAssert(_stageWorldCue!.Text=="TRAILER STAGE","Live stage name changed");_focus=new(-16,0,11);ApplyCamera();PostShot("16-live-stable-stage-name");break;
                case 17:
                    // Production preparation commands exercise rotated food/bar assemblies;
                    // isolated copy keeps the already-proven live state untouched.
                    PreparationSave();var savedLive=_session;var liveHash=_session.CaptureSnapshot().AuthoritativeHash;
                    PreparationLoad();PostAssert(!ReferenceEquals(savedLive,_session) && liveHash==_session.CaptureSnapshot().AuthoritativeHash,"Live exact load failed");
                    _session=GameSession.CreateImmersionCampaign(20260922);_foundationPresentation.Reset(_session.CaptureObservation());
                    foreach(var visual in _attendeeVisuals.Values)visual.QueueFree();_attendeeVisuals.Clear();_attendeePickRegistry.Clear();
                    SyncResponsePosts();SyncImmersionWorld();ResetLivePerformancePresentation();
                    var food=_session.CaptureImmersion()!.Vendors.Single(v=>v.Id=="food");CommitEquipmentAction(new PlaceImmersionVendorCommand("food",food.Cell,1));
                    PostAssert(_session.CaptureImmersion()!.Vendors.Single(v=>v.Id=="food").QuarterTurns==1,"Rotated food fixture command failed");
                    PostHover(null,"food","17-rotated-food-hover-default-zoom");break;
                case 18:
                    PostAssert(_hoveredColliderId==_immersionVendors["food"].GetInstanceId() && Math.Abs(_hoverHighlight.Rotation.Y-Mathf.Pi/2)<.01f,"Rotated food ring/picking mismatch");
                    var bar=_session.CaptureImmersion()!.Vendors.Single(v=>v.Id=="drinks");CommitEquipmentAction(new PlaceImmersionVendorCommand("drinks",bar.Cell,1));
                    PostAssert(_session.CaptureImmersion()!.Vendors.Single(v=>v.Id=="drinks").QuarterTurns==1,"Rotated bar fixture command failed");
                    PostHover(null,"drinks","18-rotated-bar-hover-default-zoom");break;
                case 19:
                    PostAssert(_hoveredColliderId==_immersionVendors["drinks"].GetInstanceId() && Math.Abs(_hoverHighlight.Rotation.Y-Mathf.Pi/2)<.01f,"Rotated bar ring/picking mismatch");
                    PreparationSave();_postCaptureHash=_session.CaptureSnapshot().AuthoritativeHash;var prior=_session;PreparationLoad();PostAssert(!ReferenceEquals(prior,_session) && _postCaptureHash==_session.CaptureSnapshot().AuthoritativeHash,"Live save continuation failed");
                    GD.Print($"POST_CAPTURE_COMPLETE move_rotate_cancel_invalid_commit=True exact_save_load=True loaded_preview_clear=True moved_picking=True baseline_hired_fronts_outward=True rings_four_default_zoom=True rotated_vendors=True BAR=True stable_TRAILER_STAGE=True shortcut_absent=True hash={_postCaptureHash} content={LowerWitteringFarmScenario.ContentCompatibilityHash}");GetTree().Quit();break;
            }
        }
        catch(Exception e){GD.PushError("POST_CAPTURE_FAILED "+e);_postCaptureDirectory=null;GetTree().Quit(2);}
    }
}

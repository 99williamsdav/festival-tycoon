using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private readonly Dictionary<ResponseRole,Node3D> _responsePostVisuals=[];
    private readonly Dictionary<ResponseRole,StaticBody3D> _responsePostPicks=[];
    private readonly Dictionary<ResponseRole,Label3D> _responsePostLabels=[];
    private ResponseRole? _movingResponsePost;
    private int _postQuarterTurns;
    private GridCell? _postCandidate;
    private string? _postIssue;
    private Node3D? _postPreview;
    private MeshInstance3D? _postFootprintPreview;
    private Label3D? _postPreviewLabel;
    private readonly List<MeshInstance3D> _postFrontMarkers=[];
    private Button? _firstAidMoveButton;
    private Button? _stewardMoveButton;
    private static string PostAsset(ResponseRole role)=>role==ResponseRole.Medic?"res://assets/environment/lwf_first_aid_point_v2.glb":"res://assets/environment/lwf_security_post_v1.glb";
    private static Label3D BuildingName(string text,Vector3 position)=>new() { Text=text,Position=position,
        FontSize=80,PixelSize=.014f,OutlineSize=9,OutlineModulate=new Color("26332f"),
        Modulate=new Color("fff3d6"),Billboard=BaseMaterial3D.BillboardModeEnum.Enabled };
    private void SyncResponsePosts()
    {
        foreach(var role in _responsePostVisuals.Keys)
        {
            var post=_session.CaptureResponsePost(role);
            var point=ImmersionPosition(post.Cell);
            _responsePostVisuals[role].Position=point;
            _responsePostVisuals[role].RotationDegrees=new(0,post.QuarterTurns*90,0);
            _responsePostPicks[role].Position=point+new Vector3(0,role==ResponseRole.Medic?1.35f:1.55f,0);
            // Existing square tent proxy and post proxy sizes/eligibility are retained.
            _responsePostPicks[role].RotationDegrees=new(0,role==ResponseRole.Medic?0:(post.QuarterTurns-1)*90,0);
            _responsePostLabels[role].Position=point+new Vector3(0,role==ResponseRole.Medic?3.3f:3.7f,0);
        }
    }
    private void RefreshResponsePostMoveButtons()
    {
        if(_firstAidMoveButton is not null)_firstAidMoveButton.Visible=_selectedMedicalFacility==MedicalFacility.FirstAid && _session.PreparedStatus==PreparationStatus.Preparing;
        if(_stewardMoveButton is not null)_stewardMoveButton.Visible=_selectedSecurityPost && _session.PreparedStatus==PreparationStatus.Preparing;
    }
    private void BeginResponsePostPlacement(ResponseRole role)
    {
        if(_session.PreparedStatus!=PreparationStatus.Preparing)return;
        CancelWaterPlacement();CancelImmersionPlacement();CancelResponsePostPlacement();ClearSelection();
        _movingResponsePost=role;_postQuarterTurns=_session.CaptureResponsePost(role).QuarterTurns;
        _postPreview=AddAsset(PostAsset(role),Vector3.Zero);
        _postPreviewLabel=BuildingName("",new(0,4,0));_postPreview.AddChild(_postPreviewLabel);
        _postFootprintPreview=new() { MaterialOverride=new StandardMaterial3D { Transparency=BaseMaterial3D.TransparencyEnum.Alpha,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded } };AddChild(_postFootprintPreview);
        for(var i=0;i<2;i++) {var marker=new MeshInstance3D {Mesh=new CylinderMesh {TopRadius=.3f,BottomRadius=.3f,Height=.035f},MaterialOverride=new StandardMaterial3D {Transparency=BaseMaterial3D.TransparencyEnum.Alpha,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded}};AddChild(marker);_postFrontMarkers.Add(marker);}
        _preparationMessage="Move response post: click valid grass. Comma/period rotate; right-click or Esc cancels.";RefreshPreparationHud();
    }
    private void CancelResponsePostPlacement(bool committed=false)
    {
        var hadPreview=_movingResponsePost is not null;
        _movingResponsePost=null;_postCandidate=null;_postIssue=null;
        foreach(var node in new Node3D?[]{_postPreview,_postFootprintPreview}.Concat(_postFrontMarkers))if(node is not null){node.Visible=false;node.QueueFree();}
        _postFrontMarkers.Clear();_postPreview=null;_postFootprintPreview=null;_postPreviewLabel=null;
        if(hadPreview)
        {
            _hoveredColliderId=0;_hoverHighlight.Visible=false;
            _preparationMessage=committed?"Response post moved and autosaved.":"Response post movement cancelled.";
            RefreshPreparationHud();
        }
    }
    private void RotateResponsePost(int direction)
    { _postQuarterTurns=(_postQuarterTurns+direction+4)%4;_postCandidate=null;UpdateResponsePostPreview(GetViewport().GetMousePosition()); }
    private void UpdateResponsePostPreview(Vector2 screen)
    {
        if(_movingResponsePost is not { } role || _postPreview is null)return;
        if(WorldInputOccluded(screen)){_postCandidate=null;_postPreview.Visible=false;_postFootprintPreview!.Visible=false;foreach(var m in _postFrontMarkers)m.Visible=false;return;}
        var origin=_camera.ProjectRayOrigin(screen);var ray=_camera.ProjectRayNormal(screen);
        if(Mathf.Abs(ray.Y)<.001f || -origin.Y/ray.Y<=0)return;
        var world=origin+ray*(-origin.Y/ray.Y);var cell=TraversalGrid.WorldToCell(Mathf.RoundToInt(world.X*1000),Mathf.RoundToInt(world.Z*1000));
        if(_postCandidate==cell)return;
        _postCandidate=cell;_postIssue=_session.ValidateCommand(CampaignEnvelope(new MoveResponsePostCommand(role,cell,_postQuarterTurns)))?.Message;
        _postPreview.Position=ImmersionPosition(cell);_postPreview.RotationDegrees=new(0,_postQuarterTurns*90,0);_postPreview.Visible=true;
        _postPreviewLabel!.RotationDegrees=new(0,-_postQuarterTurns*90,0);
        _postPreviewLabel.Text=_postIssue is null?"VALID • CLICK TO MOVE":"INVALID";
        _preparationMessage=_postIssue??"Valid response post site • click to move and autosave.";RefreshPreparationHud();
        var color=_postIssue is null?new Color(.25f,.78f,.38f,.4f):new Color(.9f,.24f,.18f,.4f);
        var extent=role==ResponseRole.Medic?3.5f:2.5f;
        _postFootprintPreview!.Mesh=new BoxMesh {Size=new(extent,.035f,extent)};
        _postFootprintPreview.Position=ImmersionPosition(cell)+new Vector3(0,.08f,0);_postFootprintPreview.Visible=true;
        ((StandardMaterial3D)_postFootprintPreview.MaterialOverride!).AlbedoColor=color;
        var prep=_session.CapturePreparation()!;
        prep=role==ResponseRole.Medic?prep with{FirstAidPlacement=new(cell,_postQuarterTurns)}:prep with{StewardPostPlacement=new(cell,_postQuarterTurns)};
        for(var i=0;i<2;i++){var m=_postFrontMarkers[i];m.Position=ImmersionPosition(GameSession.ResponsePostHome(prep,role,i==1))+new Vector3(0,.08f,0);m.Visible=true;((StandardMaterial3D)m.MaterialOverride!).AlbedoColor=color;}
    }
    private void CommitResponsePostPlacement(Vector2 screen)
    {
        UpdateResponsePostPreview(screen);
        if(_movingResponsePost is not {} role || _postCandidate is not {} cell || _postIssue is not null)return;
        var previousSession=_session;
        CommitEquipmentAction(new MoveResponsePostCommand(role,cell,_postQuarterTurns));
        if(!ReferenceEquals(previousSession,_session) && _session.CaptureResponsePost(role)==new ResponsePostPlacement(cell,_postQuarterTurns))CancelResponsePostPlacement(true);
    }
}

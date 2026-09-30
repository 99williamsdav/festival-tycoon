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
            var placed = _session.CaptureBuildPlacements().Any(item => item.Kind ==
                (role == ResponseRole.Medic ? BuildServiceKind.FirstAid : BuildServiceKind.StewardPost));
            _responsePostVisuals[role].Visible = placed;
            _responsePostPicks[role].CollisionLayer = placed ? 1u : 0u;
            _responsePostLabels[role].Visible = placed;
            if (!placed) continue;
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
        BeginBuildPlacement(role == ResponseRole.Medic ? BuildServiceKind.FirstAid : BuildServiceKind.StewardPost,
            role == ResponseRole.Medic ? "first-aid" : "steward-post");
    }
}

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
    private Button? _firstAidMoveButton;
    private Button? _stewardMoveButton;
    private static string PostAsset(ResponseRole role)=>role==ResponseRole.Medic?"res://assets/environment/lwf_first_aid_point_v2.glb":"res://assets/environment/lwf_security_post_v1.glb";
    private static FontVariation? _signFont;
    /// <summary>The signs' lettering: the HUD's Zilla Slab, slightly spaced, drawn from a distance field so it stays crisp.</summary>
    private static FontVariation SignFont => _signFont ??= new FontVariation
    {
        BaseFont = SignFontFile(), SpacingGlyph = 2,
    };
    private static FontFile SignFontFile()
    {
        var font = (FontFile)GD.Load<FontFile>("res://assets/ui/fonts/ZillaSlab-Bold.ttf").Duplicate();
        font.MultichannelSignedDistanceField = true;
        font.MsdfPixelRange = 40; // At least twice the outline in MSDF units, so the outline isn't clipped by the field.
        return font;
    }

    /// <summary>
    /// A building's name on the field. It keeps the same size on screen at every zoom, so it never shrinks to an
    /// illegible smudge zoomed out or swamps the view zoomed in, and it's lettered in the HUD's slab face.
    /// </summary>
    private static Label3D BuildingName(string text,Vector3 position,int size=40)=>new() { Text=text,Position=position,
        Font=SignFont,FontSize=size,PixelSize=.0011f,FixedSize=true,OutlineSize=size*3/10,OutlineModulate=new Color("1d2a25"),
        Modulate=new Color("fff3d6"),Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,RenderPriority=2,NoDepthTest=true };
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

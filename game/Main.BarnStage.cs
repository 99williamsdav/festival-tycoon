using Festival.Simulation;
using Godot;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private RoofFade? _barnFade;
    private Node3D? _barnDrums;
    private string? _barnDrumHardware;
    private OmniLight3D[] _barnStageLights = [];
    private Node3D? _barnGenerator;
    private string? _barnGeneratorVariant;
    private Label3D? _barnStageHeading;
    private static bool BarnFadeSurface(string name) => name is "Barn_Roof" or "Barn_WallFade" or "Barn_DoorFade" or "Barn_TrussFade";
    private static void ClipTrackAtBarnFloor(Node3D track)
    {
        foreach(var mesh in track.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>())
        for(var i=0;i<mesh.Mesh!.GetSurfaceCount();i++)
        {
            if(mesh.GetActiveMaterial(i) is not StandardMaterial3D original)continue;
            var material=new ShaderMaterial{Shader=new Shader{Code="""
                shader_type spatial;
                uniform sampler2D palette : source_color, filter_nearest, repeat_enable;
                uniform vec4 tint : source_color = vec4(1.0);
                uniform float roughness = 1.0;
                uniform float metallic = 0.0;
                varying vec2 field;
                void vertex(){field=(MODEL_MATRIX*vec4(VERTEX,1.0)).xz;}
                void fragment(){
                    bool floor_inside = abs(field.x)<=9.5429 && field.y>=-28.5733 && field.y<=-17.4267;
                    bool threshold = field.y>=-17.4267 && field.y<=-16.35 &&
                        (abs(field.x)<=3.9 || (field.x>=-8.65 && field.x<=-6.65));
                    if(floor_inside || threshold)discard;
                    ALBEDO=texture(palette,UV).rgb*tint.rgb;ROUGHNESS=roughness;METALLIC=metallic;
                }
                """}};
            material.SetShaderParameter("palette",original.AlbedoTexture);material.SetShaderParameter("tint",original.AlbedoColor);
            material.SetShaderParameter("roughness",original.Roughness);material.SetShaderParameter("metallic",original.Metallic);
            mesh.SetSurfaceOverrideMaterial(i,material);
        }
    }

    private void BuildBarnStageWorld(StaticBody3D body, Node3D model)
    {
        _barnFade = RoofFade.Attach(model,BarnFadeSurface,twoSided:false);
        // Only sparse frame targets pick the building. The authoritative walls are in BarnLayout, not this ray layer.
        foreach (var (x,z,height) in new[] {(-4.1f,6f,5.4f),(4.1f,6f,5.4f),(-8.75f,5.9f,2.8f),(-6.55f,5.9f,2.8f),
            (-9.8f,-5.9f,5.4f),(9.8f,-5.9f,5.4f),(-9.8f,5.9f,5.4f),(9.8f,5.9f,5.4f)})
            body.AddChild(new CollisionShape3D {Position=new(x,height/2,z),Shape=new BoxShape3D {Size=new(.25f,height,.25f)}});
        var stage = new Node3D {Name="BarnStageKit",Position=new(6.2f,0,-1.5f),RotationDegrees=new(0,-90,0)};
        body.AddChild(stage);
        var riser = InstantiateAsset("res://assets/environment/modular-riser-v1/lwf_modular_riser_stage_v1.glb"); stage.AddChild(riser);
        if (riser.FindChild("RiserAccess",true,false) is not Node3D access) throw new System.InvalidOperationException("Approved riser has no RiserAccess.");
        access.RotationDegrees = new(0,180,0); // Mesh's original shared origin, never its bounding-box centre.
        foreach(var side in new[]{-1f,1f})
        {var speaker=InstantiateAsset("res://assets/environment/modular-riser-v1/lwf_modular_riser_speaker_v1.glb");speaker.Position=new(side*2.42f,.9f,1.37f);stage.AddChild(speaker);}
        foreach(var p in new[]{new Vector3(-8.6f,3.4f,-5.2f),new(0,3.4f,-5.2f),new(8.6f,3.4f,-5.2f),new(-8.6f,3.4f,5.2f),new(8.6f,3.4f,5.2f)})
            body.AddChild(new OmniLight3D {Position=p,OmniRange=8,LightColor=new("ffd18a"),LightEnergy=.45f});
        _barnStageLights=[new OmniLight3D {Position=new(6.2f,4.5f,-1.5f),OmniRange=9,LightColor=new("ffd9aa"),LightEnergy=0}];
        foreach(var light in _barnStageLights)body.AddChild(light);
        _barnStageHeading=BuildingName("BARN STAGE",new(0,11.5f,0));body.AddChild(_barnStageHeading);
        AttachNowPlayingLabel(BarnLayout.Stage.Id,_barnStageHeading);
    }
    private void ProcessBarnStage(double delta)
    {
        _barnFade?.Update(_session.BarnOccupants>0 || _selected?.Kind==FarmObjectKind.LargeBarn,Time.GetTicksMsec()/1000.0);
        var live=_session.CaptureLivePerformance(BarnLayout.Stage.Id);
        if(live is null) {if(_barnDrums is not null){_barnDrums.QueueFree();_barnDrums=null;_barnDrumHardware=null;}return;}
        var act=live.Performers.Any(p=>p.InstrumentAttached) ? _session.CurrentStageAct(live.StageId) : _session.UpcomingStageAct(live.StageId)??_session.CurrentStageAct(live.StageId);
        if(_session.PreparedStatus!=PreparationStatus.Failed && !(_session.PreparedStatus is PreparationStatus.Departing or PreparationStatus.Finished && act is null))
            _barnDrums=DrumHardware(BarnLayout.Stage,act,ref _barnDrumHardware,_barnDrums);
        if(_session.CaptureStageGenerator(BarnLayout.Stage.Id) is {} generator)
        {
            var variant=generator.Stage switch {EquipmentStage.Warning=>"overloaded",EquipmentStage.DangerousFault or EquipmentStage.Terminal=>"fault",EquipmentStage.Isolated=>"isolated",_=>"normal"};
            if(_barnGenerator is null || _barnGeneratorVariant!=variant)
            {
                _barnGenerator?.QueueFree();_barnGeneratorVariant=variant;
                _barnGenerator=AddAsset($"res://assets/equipment/lwf_hire_generator_{variant}_v1.glb",new(generator.XMillimetres/1000f,0,generator.ZMillimetres/1000f));
                _barnGenerator.AddChild(BuildingName("BARN GENERATOR",new(0,2.3f,0),28));
            }
        }
        if(_selected?.Kind==FarmObjectKind.LargeBarn)
        {
            _inspectorTitle.Text="Barn Stage";
            _inspectorBody.Text=$"{_session.BarnOccupants} inside · sheltered interior\n"+
                (_session.BarnDiagnostic?$"Controlled fixture · {act?.Name??"Awaiting set"}\nNormal stage allocation unchanged.":"Sheltered hall · no sets booked here for this festival.");
        }
    }
}

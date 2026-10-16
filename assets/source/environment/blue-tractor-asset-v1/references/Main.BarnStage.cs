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
    private Node3D? _barnStageKit;
    private Node3D[] _barnConversionParts = [];
    private OmniLight3D[] _barnVenueLights = [];
    private ulong _barnGeneratorPickId;
    private bool _selectedBarnGenerator, _barnGeneratorShaking;
    private Button? _barnCutButton;
    private Node3D? _barnPowerChip;
    private Label3D? _barnPowerChipText;
    private Sprite3D? _barnPowerChipIcon;
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
        _barnConversionParts = model.FindChildren("*", "", true, false).OfType<Node3D>().Where(n =>
            n.Name.ToString() is "Barn_SignBacking" or "Barn_Name" or "Barn_ExitSign" || n.Name.ToString().StartsWith("Barn_Lamp_", System.StringComparison.Ordinal)).ToArray();
        var stage = _barnStageKit = new Node3D {Name="BarnStageKit",Position=new(6.2f,0,-1.5f),RotationDegrees=new(0,-90,0)};
        body.AddChild(stage);
        var riser = InstantiateAsset("res://assets/environment/modular-riser-v1/lwf_modular_riser_stage_v1.glb"); stage.AddChild(riser);
        if (riser.FindChild("RiserAccess",true,false) is not Node3D access) throw new System.InvalidOperationException("Approved riser has no RiserAccess.");
        access.RotationDegrees = new(0,180,0); // Mesh's original shared origin, never its bounding-box centre.
        foreach(var side in new[]{-1f,1f})
        {var speaker=InstantiateAsset("res://assets/environment/modular-riser-v1/lwf_modular_riser_speaker_v1.glb");speaker.Position=new(side*2.42f,.9f,1.37f);stage.AddChild(speaker);}
        _barnVenueLights = new[]{new Vector3(-8.6f,3.4f,-5.2f),new(0,3.4f,-5.2f),new(8.6f,3.4f,-5.2f),new(-8.6f,3.4f,5.2f),new(8.6f,3.4f,5.2f)}
            .Select(p => new OmniLight3D {Position=p,OmniRange=8,LightColor=new("ffd18a"),LightEnergy=.45f}).ToArray();
        foreach(var light in _barnVenueLights) body.AddChild(light);
        _barnStageLights=[new OmniLight3D {Position=new(6.2f,4.5f,-1.5f),OmniRange=9,LightColor=new("ffd9aa"),LightEnergy=0}];
        foreach(var light in _barnStageLights)body.AddChild(light);
        _barnStageHeading=BuildingName("BARN STAGE",new(0,11.5f,0));body.AddChild(_barnStageHeading);
        AttachNowPlayingLabel(BarnLayout.Stage.Id,_barnStageHeading);
        SyncBarnConversion();
    }
    private void SyncBarnConversion()
    {
        var active = _session.BarnStageOpen;
        if (_barnStageKit is not null) _barnStageKit.Visible = active;
        foreach(var part in _barnConversionParts) part.Visible = active;
        if (_barnStageHeading is not null) _barnStageHeading.Visible = active;
        foreach(var light in _barnVenueLights) light.Visible = active && _session.StagePoweredAt(FestivalStages.BarnId);
        foreach(var light in _barnStageLights) light.Visible = active;
        if (active) return;
        if (_selectedBarnGenerator) ClearSelection();
        _barnDrums?.QueueFree(); _barnDrums = null; _barnDrumHardware = null;
        _barnGenerator?.QueueFree(); _barnGenerator = null; _barnGeneratorVariant = null;
        if (_barnPowerChip is not null) _barnPowerChip.Visible = false;
    }
    private void ProcessBarnStage(double delta)
    {
        SyncBarnConversion();
        _barnFade?.Update(_session.BarnOccupants>0 || _selected?.Kind==FarmObjectKind.LargeBarn,Time.GetTicksMsec()/1000.0);
        if (!_session.BarnStageOpen)
        {
            if (_selected?.Kind == FarmObjectKind.LargeBarn)
            { _inspectorTitle.Text = "Large barn"; _inspectorBody.Text = $"{_session.BarnOccupants} inside · sheltered farm barn"; }
            return;
        }
        SyncBarnGenerator();
        var live=_session.CaptureLivePerformance(BarnLayout.Stage.Id);
        var act=live?.Performers.Any(p=>p.InstrumentAttached) == true ? _session.CurrentStageAct(FestivalStages.BarnId) : _session.UpcomingStageAct(FestivalStages.BarnId)??_session.CurrentStageAct(FestivalStages.BarnId);
        if(_session.PreparedStatus!=PreparationStatus.Failed && !(_session.PreparedStatus is PreparationStatus.Departing or PreparationStatus.Finished && act is null))
            _barnDrums=DrumHardware(BarnLayout.Stage,act,ref _barnDrumHardware,_barnDrums);
        if(_selected?.Kind==FarmObjectKind.LargeBarn)
        {
            var status = _session.PreparedStatus == PreparationStatus.Preparing ? "Before opening" : _session.CaptureProgramme(FestivalStages.BarnId)?.Status;
            _inspectorTitle.Text="Barn Stage";
            _inspectorBody.Text=$"{_session.BarnOccupants} inside · sheltered interior\n{act?.Name??"Awaiting booking"} · {status}\n"+
                $"Sound {_session.SoundScoreAt(FestivalStages.BarnId)}/100 · {PerformanceRules.SoundWord(_session.SoundScoreAt(FestivalStages.BarnId))}";
        }
        if (_selectedBarnGenerator) RefreshBarnGeneratorInspector();
    }
    private void SyncBarnGenerator()
    {
        if(_session.CaptureStageGenerator(BarnLayout.Stage.Id) is {} generator)
        {
            var variant=generator.Stage switch {EquipmentStage.Warning=>"overloaded",EquipmentStage.DangerousFault or EquipmentStage.Terminal=>"fault",EquipmentStage.Isolated=>"isolated",_=>"normal"};
            if(_barnGenerator is null || _barnGeneratorVariant!=variant)
            {
                _barnGenerator?.QueueFree();_barnGeneratorVariant=variant;
                _barnGenerator=AddAsset($"res://assets/equipment/lwf_hire_generator_{variant}_v1.glb",new(generator.XMillimetres/1000f,0,generator.ZMillimetres/1000f));
                _barnGeneratorShaking = AddGeneratorSmoke(_barnGenerator,"hire_generator",generator.Stage);
                var pick = new StaticBody3D { CollisionLayer=1, CollisionMask=1, Position=new(.17f,1,0) };
                pick.AddChild(new CollisionShape3D {Shape=new BoxShape3D {Size=new(3.8f,2,1.5f)}});
                _barnGenerator.AddChild(pick); _barnGeneratorPickId=pick.GetInstanceId();
                _barnGenerator.AddChild(BuildingName("BARN GENERATOR",new(0,1.4f,0),28));
            }
            var home = new Vector3(generator.XMillimetres/1000f,0,generator.ZMillimetres/1000f);
            _barnGenerator!.Position = _barnGeneratorShaking && !_session.IsPaused
                ? home + new Vector3(Mathf.Sin((float)_generatorClock*125f)*.015f,0,Mathf.Cos((float)_generatorClock*97f)*.015f) : home;
            if (_barnPowerChip is null)
            {
                _barnPowerChip = new Node3D(); AddChild(_barnPowerChip);
                _barnPowerChipIcon = new Sprite3D {PixelSize=.0018f,FixedSize=true,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest=true,RenderPriority=3,Offset=new(-16,0),Layers=EyeHiddenLayer};
                _barnPowerChip.AddChild(_barnPowerChipIcon);
                _barnPowerChipText = WorldText.Speech(new Label3D {Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,HorizontalAlignment=HorizontalAlignment.Left,Offset=new(4,0),OutlineModulate=new("17302a")},28);
                _barnPowerChipText.Layers=EyeHiddenLayer;_barnPowerChip.AddChild(_barnPowerChipText);
            }
            var power = _session.CaptureStagePower(FestivalStages.BarnId);
            var trouble = generator.Stage is EquipmentStage.Warning or EquipmentStage.DangerousFault || power?.Over == true && _session.PreparedStatus == PreparationStatus.Running;
            _barnPowerChip.Visible = power is not null && (trouble || _selectedBarnGenerator || _session.PreparedStatus == PreparationStatus.Preparing && _hudWorkspaceOpen && HudPageSelected("Supplies")) && !EyeViewActive;
            _barnPowerChip.Position=home+new Vector3(0,2.6f,0);_barnPowerChipText!.Text=power is null?"":$"{power.Total} / {power.Capacity}";
            _barnPowerChipText.Modulate=trouble?new Color("ffb08a"):new Color("fff7e1");
            _barnPowerChipIcon!.Texture=GD.Load<Texture2D>(trouble?"res://assets/ui/lwf_power_chip_zap_alert_v1.svg":"res://assets/ui/lwf_power_chip_zap_v1.svg");
        }
    }
    private bool TrySelectBarnGenerator(Node collider)
    {
        if (!_session.BarnStageOpen || _barnGenerator is null || collider.GetInstanceId()!=_barnGeneratorPickId) return false;
        ClearSelection(); _selectedBarnGenerator=true;
        _highlight.Position=_barnGenerator.Position+new Vector3(0,.08f,0);_highlight.Scale=new(3,1,3);_highlight.Visible=true;
        RefreshBarnGeneratorInspector(); return true;
    }
    private void RefreshBarnGeneratorInspector()
    {
        var generator=_session.CaptureStageGenerator(FestivalStages.BarnId);var power=_session.CaptureStagePower(FestivalStages.BarnId);
        _inspectorTitle.Text="Barn Stage generator";
        _inspectorBody.Text=generator is null?"Runs the Barn Stage only.":
            $"POWER {power?.Total??0} / {power?.Capacity??generator.Capacity} · {PondGeneratorState(generator,power)}\n"+
            (_session.PowerPooled && power is not null?PowerParts(power)+PooledSupplyText():$"Barn Stage {power?.Stage??0} ({PowerRules.RigName(_session.Rig)})")+
            "\nHired with the stage; outside the barn, clear of its doors.";
        RefreshStagePowerAction();RefreshContextPanelVisibility();
    }
    private void BuildBarnStageAction(VBoxContainer parent)
    {
        _barnCutButton=ButtonText("Cut Barn Stage power",()=>CommitEquipmentAction(new StageGeneratorCommand(FestivalStages.BarnId,StageGeneratorAction.Isolate)));
        _barnCutButton.Visible=false;parent.AddChild(_barnCutButton);
    }
    private void RefreshBarnCutButton()
    {
        if(_barnCutButton is null)return;
        _barnCutButton.Visible=_session.BarnStageOpen && (_selectedBarnGenerator || _selected?.Kind==FarmObjectKind.LargeBarn) && _session.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing;
        if(_barnCutButton.Visible)
        {
            var error=_session.ValidateCommand(CampaignEnvelope(new StageGeneratorCommand(FestivalStages.BarnId,StageGeneratorAction.Isolate)));
            _barnCutButton.Disabled=error is not null;
            _barnCutButton.TooltipText="Cuts the Barn Stage's music and rig draw; the Trailer Stage keeps its own power.\n"+(error?.Message??"Cut the Barn Stage's power.");
        }
    }
}

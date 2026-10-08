using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private readonly PondClock _pondClock = new();
    private Node3D _pondWorld = null!;
    private readonly Node3D[] _pondDucks = new Node3D[2];
    private readonly Node3D[] _pondWakes = new Node3D[2];
    private ShaderMaterial _pondWater = null!;
    private readonly ShaderMaterial[] _pondWakeMaterials = new ShaderMaterial[2];
    private const float PondWaterline = .075f;

    private void BuildPondWorld()
    {
        _pondWorld = new Node3D { Name="FarmPond",Position=new(24,0,24.5f) };AddChild(_pondWorld);
        var basin=InstantiateAsset("res://assets/environment/pond-polish-v1/lwf_pond_polish_static_v1.glb");
        _pondWorld.AddChild(basin);
        // The farm's strong warm light clips the isolated-review palette. Keep the delivered quiet bank readable.
        foreach(var mesh in basin.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>())
            if(mesh.Name!="PondWater"&&mesh.GetActiveMaterial(0) is StandardMaterial3D original)
            {
                var material=(StandardMaterial3D)original.Duplicate();material.AlbedoColor=new Color(.55f,.55f,.55f);
                mesh.MaterialOverride=material;
            }
        var water=basin.FindChildren("PondWater","MeshInstance3D",true,false).OfType<MeshInstance3D>().Single();
        _pondWater=new ShaderMaterial{Shader=new Shader{Code=PondWaterShader}};water.MaterialOverride=_pondWater;
        var wakeMesh=PondWakeMesh();
        for(var i=0;i<2;i++)
        {
            _pondDucks[i]=InstantiateAsset($"res://assets/environment/pond-polish-v1/lwf_duck_{(i==0?"drake":"brown")}_v1.glb");
            _pondWorld.AddChild(_pondDucks[i]);
            foreach(var mesh in _pondDucks[i].FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>())
                if(mesh.GetActiveMaterial(0) is StandardMaterial3D original)
                {
                    var material=(StandardMaterial3D)original.Duplicate();material.AlbedoColor=new Color(.8f,.8f,.8f);
                    mesh.MaterialOverride=material;
                }
            _pondWakeMaterials[i]=new ShaderMaterial{Shader=new Shader{Code=PondWakeShader}};
            _pondWakes[i]=new MeshInstance3D{Name="DuckWake"+i,Mesh=wakeMesh,MaterialOverride=_pondWakeMaterials[i],
                CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
            _pondWorld.AddChild(_pondWakes[i]);
        }
        ApplyPondPose();
        BuildPondWillow();
    }

    private void ProcessPond(double delta)
    {
        var terminal=_session.PreparedStatus is PreparationStatus.Failed or PreparationStatus.Finished;
        var before=_pondClock.Seconds;
        _pondClock.Advance(delta,_session.IsPaused,terminal,StartMenu.IsOpen||!_pondWorld.IsVisibleInTree());
        if(_pondClock.Seconds!=before)ApplyPondPose();
    }

    private void ApplyPondPose()
    {
        var time=_pondClock.Seconds;
        _pondWater.SetShaderParameter("phase",(float)time);
        for(var i=0;i<2;i++)
        {
            var pose=(i==0?PondPaths.Drake:PondPaths.Brown).Sample(time);
            _pondDucks[i].Position=new((float)pose.X,PondWaterline+(float)pose.Bob,(float)pose.Z);
            _pondDucks[i].Rotation=new(0,(float)pose.Yaw,0);
            _pondWakes[i].Position=new((float)pose.X,PondWaterline+.002f,(float)pose.Z);
            _pondWakes[i].Rotation=new(0,(float)pose.Yaw,0);
            _pondWakeMaterials[i].SetShaderParameter("strength",(float)Math.Clamp(pose.Speed/.1,0,1));
            _pondWakeMaterials[i].SetShaderParameter("bob",(float)Math.Abs(pose.Bob)/.003f);
        }
    }

    private static ArrayMesh PondWakeMesh()
    {
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        for(var arc=0;arc<3;arc++)for(var j=0;j<16;j++)
        {
            var radius=.22f+arc*.11f;
            Vector3 P(int index,float edge)
            {
                var angle=Mathf.Pi*.5f+index*Mathf.Pi/16;
                return new(-.18f+(radius+edge)*Mathf.Cos(angle),0,(radius+edge)*.65f*Mathf.Sin(angle));
            }
            var a=P(j,-.008f);var b=P(j,.008f);var c=P(j+1,-.008f);var d=P(j+1,.008f);
            foreach(var point in new[]{a,b,c,b,d,c}){surface.SetNormal(Vector3.Up);surface.AddVertex(point);}
        }
        return surface.Commit();
    }

    private const string PondWaterShader="""
        shader_type spatial;
        uniform float phase = 0.0;
        varying vec2 pond;
        void vertex(){pond=VERTEX.xz;}
        void fragment(){
            float drift=sin(pond.x*.85+pond.y*.55+phase*.18);
            float gleam=pow(.5+.5*sin(pond.x*1.4-pond.y*.7-phase*.13),8.0);
            // Convert the delivered linear vertex tint for the Compatibility presentation material.
            vec3 base=pow(max(COLOR.rgb,vec3(0.0)),vec3(1.0/2.2))*.72;
            ALBEDO=base*(1.0+drift*.025)+vec3(.006,.008,.007)*gleam;
            ROUGHNESS=.5;SPECULAR=.18;
        }
        """;
    private const string PondWakeShader="""
        shader_type spatial;
        render_mode unshaded,cull_disabled,depth_draw_never;
        uniform float strength = 0.0;
        uniform float bob = 0.0;
        void fragment(){ALBEDO=vec3(.63,.76,.70);ALPHA=.14*strength+.025*bob;}
        """;
}

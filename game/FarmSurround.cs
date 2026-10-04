using Godot;
using System;
using System.Collections.Generic;

namespace Festival.Game;

/// <summary>Replaceable, decorative countryside. No simulation, picking, physics or navigation nodes.</summary>
internal static class FarmSurround
{
    internal const float InnerEdge = 31.95f; // Small overlap beneath the existing perimeter, never over playable grass.
    internal static float HalfExtent(float aspect) => Mathf.Ceil(CameraRig.PanLimit +
        CameraRig.MaxZoom * (Mathf.Max(1, aspect) + 1) / Mathf.Sin(Mathf.Atan2(58, 72)) + 64);

    internal static Node3D Create(float aspect)
    {
        var root = new Node3D { Name = "DecorativeFarmSurround" };
        var extent = HalfExtent(aspect);
        root.SetMeta("HalfExtent", extent);
        var ground = Material(new Color("8c9d82"), true);
        var vertices = new List<Vector3>();
        void Rect(float x0, float z0, float x1, float z1)
        {
            vertices.AddRange([new(x0,-.025f,z0),new(x1,-.025f,z1),new(x1,-.025f,z0),
                new(x0,-.025f,z0),new(x0,-.025f,z1),new(x1,-.025f,z1)]);
        }
        Rect(-extent,-extent,extent,-InnerEdge); Rect(-extent,InnerEdge,extent,extent);
        Rect(-extent,-InnerEdge,-InnerEdge,InnerEdge); Rect(InnerEdge,-InnerEdge,extent,InnerEdge);
        AddMesh(root,"ContinuousGround",Triangles(vertices,true),ground);

        // The authoritative gate/track centre is x=0, z=32. Bend only after clearing its open apron.
        Vector2[] lane = [new(0,31.8f),new(0,39),new(2,44),new(7,48),new(16,50),new(60,50),new(95,57),new(extent,57)];
        AddMesh(root,"LaneVerge",Ribbon(lane,5.2f,-.012f),Material(new("a6a17b")));
        AddMesh(root,"ContinuedEntranceLane",Ribbon(lane,3.9f,-.005f),Material(new("b7a17b")));

        var hedgeTransforms = new List<Transform3D>();
        void HedgeLine(Vector2 a, Vector2 b)
        {
            var length=a.DistanceTo(b); var count=Math.Max(1,(int)(length/5));
            var direction=(b-a).Normalized(); var yaw=-Mathf.Atan2(direction.Y,direction.X);
            for(var i=0;i<count;i++)
            {
                var p=a.Lerp(b,(i+.5f)/count);
                // Road gaps; the open buffer around the playable hedge is kept entirely clear.
                if(p.X>0 && p.Y>44 && p.Y<62) continue;
                var scale=new Vector3(length/count*.85f,.5f+(i%3)*.06f,.75f);
                hedgeTransforms.Add(new(new Basis(Vector3.Up,yaw) * Basis.FromScale(scale),new(p.X,.43f,p.Y)));
            }
        }
        foreach(var x in new[]{-116f,-65f,65f,116f}) HedgeLine(new(x,-extent),new(x,extent));
        foreach(var z in new[]{-112f,-64f,68f,119f}) HedgeLine(new(-extent,z),new(extent,z));
        AddInstances(root,"LowFieldDivisions",Crown(),Material(new("64764f")),hedgeTransforms);

        var trunks=new List<Transform3D>(); var crowns=new List<Transform3D>();
        Vector2[] clusters=[new(-48,-45),new(44,-51),new(-50,45),new(49,68),new(-100,16),new(106,-26),new(6,-110),new(-15,115),new(-128,-117),new(128,126)];
        for(var c=0;c<clusters.Length;c++) for(var i=0;i<3;i++)
        {
            var p=clusters[c]+new Vector2(i*3.2f,(i%2)*3.7f); var s=.85f+((c+i)%4)*.12f;
            trunks.Add(new(Basis.Identity.Scaled(new(.32f*s,2.2f*s,.32f*s)),new(p.X,1.1f*s,p.Y)));
            crowns.Add(new(new Basis(Vector3.Up,c*.7f+i).Scaled(new(2.1f*s,2.3f*s,1.8f*s)),new(p.X,3*s,p.Y)));
        }
        AddInstances(root,"SharedTreeTrunks",new CylinderMesh{TopRadius=.65f,BottomRadius=1,Height=1,RadialSegments=6},Material(new("82755f")),trunks);
        AddInstances(root,"SharedFacetedCrowns",Crown(),Material(new("809365")),crowns);
        return root;
    }

    private static ArrayMesh Triangles(List<Vector3> vertices,bool flip=false)
    {
        var s=new SurfaceTool(); s.Begin(Mesh.PrimitiveType.Triangles);
        for(var i=0;i<vertices.Count;i+=3)
        {
            if(flip)(vertices[i+1],vertices[i+2])=(vertices[i+2],vertices[i+1]);
            var normal=(vertices[i+2]-vertices[i]).Cross(vertices[i+1]-vertices[i]).Normalized();
            for(var j=0;j<3;j++){s.SetNormal(normal);s.AddVertex(vertices[i+j]);}
        }
        return s.Commit();
    }
    private static ArrayMesh Ribbon(Vector2[] points,float width,float y)
    {
        var v=new List<Vector3>(); var left=new List<Vector3>();var right=new List<Vector3>();
        for(var i=0;i<points.Length;i++)
        {
            var tangent=(points[Math.Min(i+1,points.Length-1)]-points[Math.Max(i-1,0)]).Normalized();
            var side=new Vector2(-tangent.Y,tangent.X)*width*.5f;
            left.Add(new(points[i].X+side.X,y,points[i].Y+side.Y));right.Add(new(points[i].X-side.X,y,points[i].Y-side.Y));
        }
        for(var i=0;i<points.Length-1;i++)v.AddRange([left[i],left[i+1],right[i+1],left[i],right[i+1],right[i]]);
        return Triangles(v,true);
    }
    private static ArrayMesh Crown()
    {
        var v=new List<Vector3>();const int n=7;
        Vector3 Ring(int i,float height,float radius,float twist=0)=>new(Mathf.Cos(i*Mathf.Tau/n+twist)*radius,height,Mathf.Sin(i*Mathf.Tau/n+twist)*radius);
        for(var i=0;i<n;i++)
        {
            var a=Ring(i,-.45f,.75f);var b=Ring(i+1,-.45f,.75f);var c=Ring(i,.35f,1,.2f);var d=Ring(i+1,.35f,1,.2f);
            v.AddRange([new(0,-1,0),b,a,a,b,c,b,d,c,c,d,new(0,1,0)]);
        }
        return Triangles(v);
    }
    private static void AddMesh(Node3D root,string name,Mesh mesh,Material material)=>root.AddChild(new MeshInstance3D
        {Name=name,Mesh=mesh,MaterialOverride=material,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
    private static void AddInstances(Node3D root,string name,Mesh mesh,Material material,List<Transform3D> transforms)
    {
        var multi=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=mesh,InstanceCount=transforms.Count};
        for(var i=0;i<transforms.Count;i++)multi.SetInstanceTransform(i,transforms[i]);
        root.AddChild(new MultiMeshInstance3D{Name=name,Multimesh=multi,MaterialOverride=material,
            CastShadow=name.StartsWith("Shared")?GeometryInstance3D.ShadowCastingSetting.On:GeometryInstance3D.ShadowCastingSetting.Off});
    }
    private static ShaderMaterial Material(Color color,bool fields=false)
    {
        var m=new ShaderMaterial{Shader=new Shader{Code=SceneryShader}};
        m.SetShaderParameter("tint",color);m.SetShaderParameter("fields",fields);return m;
    }
    private const string SceneryShader="""
        shader_type spatial;
        render_mode cull_disabled;
        uniform vec4 tint : source_color;
        uniform bool fields = false;
        varying vec3 world;
        void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;}
        void fragment(){
            vec3 c=tint.rgb;
            float edge=max(abs(world.x),abs(world.z));
            if(fields){
                vec2 cell=vec2(step(-116.0,world.x)+step(-65.0,world.x)+step(65.0,world.x)+step(116.0,world.x),
                    step(-112.0,world.z)+step(-64.0,world.z)+step(68.0,world.z)+step(119.0,world.z));
                float h=fract(sin(dot(cell,vec2(12.9898,78.233)))*43758.5453);
                vec3 pasture=c*mix(.91,1.06,h);
                vec3 crop=c*vec3(1.12,1.02,.88);
                c=mix(c,mix(pasture,crop,step(.76,h)),smoothstep(37.0,45.0,edge)*.55);
                c*=1.0+.018*sin(world.x*.31+sin(world.z*.19))+.01*sin(world.z*.57);
            }
            // Local atmospheric perspective only. Never affects the arena, labels, HUD or near verge.
            float haze=smoothstep(65.0,190.0,edge)*.23;
            c=mix(c,vec3(dot(c,vec3(.3,.59,.11))),.25)*.58;
            ALBEDO=mix(c,vec3(.22,.25,.23),haze);
            ROUGHNESS=1.0; SPECULAR=0.1;
        }
        """;
}

public partial class Main
{
    private Node3D _farmSurround=null!;
    private void BuildFarmSurround()
    {
        void Rebuild()
        {
            var size=GetViewport().GetVisibleRect().Size;
            if(_farmSurround is not null){RemoveChild(_farmSurround);_farmSurround.QueueFree();}
            _farmSurround=FarmSurround.Create(size.X/Mathf.Max(1,size.Y));AddChild(_farmSurround);
        }
        Rebuild();GetViewport().SizeChanged+=Rebuild;
    }
}

using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

// Opt-in renderer/interaction evidence. Uses an unstarted campaign and never writes a player save.
public partial class Main
{
    private string? _surroundEvidenceOutput;
    private int _surroundEvidenceFrame;
    private readonly List<object> _surroundChecks=[];
    private readonly List<double> _surroundTimes=[],_surroundDraws=[],_surroundPrimitives=[];
    private readonly List<object> _surroundPerformance=[];
    private long _surroundLastTimestamp;
    private string _surroundHash="";

    private void ProcessSurroundEvidence()
    {
        if(_surroundEvidenceOutput is null)return;
        var frame=_surroundEvidenceFrame++;
        if(frame==0)
        {
            Directory.CreateDirectory(_surroundEvidenceOutput);
            var perk=_session.CapturePerks()!;
            foreach(var command in new SessionCommand[]{new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,perk.Hand[0]),new UseDefaultBuildLayoutCommand()})
            {
                var result=_session.Execute(CampaignEnvelope(command));
                if(!result.IsAccepted)throw new InvalidOperationException(result.Message);
            }
            SyncBuildWorld();_hudWorkspaceOpen=_buildDrawerOpen=false;_boxOfficeSeenAttempt=1;RefreshPreparationHud();
            Engine.MaxFps=0;DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            _surroundHash=_session.CaptureSnapshot().AuthoritativeHash.ToString();
            if(_farmSurround.FindChildren("*","CollisionObject3D",true,false).Count>0 ||
               _farmSurround.FindChildren("*","NavigationRegion3D",true,false).Count>0)
                throw new InvalidOperationException("Scenery must be visual-only");
            foreach(var x in new[]{-40,0,40})foreach(var z in new[]{-40,0,40})
            {
                if(x==0&&z==0)continue;
                var cell=TraversalGrid.WorldToCell(x*1000,z*1000);
                if(_session.ValidateCommand(CampaignEnvelope(new PlaceBuildServiceCommand(BuildServiceKind.Bin,cell))) is null)
                    throw new InvalidOperationException("Outside scenery became placeable");
                var hit=GetViewport().World3D.DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(new(x,20,z),new(x,-2,z)));
                if(hit.Count>0)throw new InvalidOperationException("Background steals physics picks");
            }
        }
        // Six views per orientation: overview, four maximum-pan corners and the gate at minimum zoom.
        const int viewFrames=8, viewCount=24;
        if(frame<viewFrames*viewCount)
        {
            var index=frame/viewFrames;var pose=index%6;
            if(frame%viewFrames==0)
            {
                if(pose==0 && index>0)_rig.Rotate(1);
                var focus=pose is >0 and <5 ? new Vector3(pose is 1 or 2 ? -24:24,0,pose is 1 or 3 ? -24:24):
                    pose==5 ? new Vector3(0,0,24):Vector3.Zero;
                _rig.Frame(focus,pose==0?72:pose==5?18:82);
            }
            if(frame%viewFrames==6)
            {
                var size=GetViewport().GetVisibleRect().Size;var extent=_farmSurround.GetMeta("HalfExtent").AsSingle();
                var max=0f;
                foreach(var p in new[]{Vector2.Zero,new Vector2(size.X,0),size,new Vector2(0,size.Y)})
                {
                    var origin=_rig.Camera.ProjectRayOrigin(p);var ray=_rig.Camera.ProjectRayNormal(p);
                    var at=origin+ray*((-.025f-origin.Y)/ray.Y);
                    max=Mathf.Max(max,Mathf.Max(Mathf.Abs(at.X),Mathf.Abs(at.Z)));
                }
                if(max>=extent)throw new InvalidOperationException("Visible outer ground edge");
                var name=$"{index/6}-{_rig.OrientationName.ToLowerInvariant()}-{(pose==0?"overview":pose==5?"gate-close":"pan-"+pose)}";
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_surroundEvidenceOutput,name+".png"));
                _surroundChecks.Add(new{name,maxGroundCorner=max,extent,covered=true,zoom=_rig.Camera.Size,focus=_rig.Focus.ToString()});
            }
            return;
        }
        // Four matched orientation pairs, 30 warm-up + 90 measured frames per visible/hidden state.
        var pf=frame-viewFrames*viewCount;var phase=pf/120;var within=pf%120;
        if(phase<8)
        {
            if(within==0)
            {
                if(phase%2==0){_rig.Rotate(1);_rig.Frame(Vector3.Zero,82);}
                _farmSurround.Visible=phase%2==1;
                _surroundTimes.Clear();_surroundDraws.Clear();_surroundPrimitives.Clear();
            }
            var now=Stopwatch.GetTimestamp();
            if(within>=30)
            {
                _surroundTimes.Add(Stopwatch.GetElapsedTime(_surroundLastTimestamp,now).TotalMilliseconds);
                _surroundDraws.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
                _surroundPrimitives.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame));
            }
            _surroundLastTimestamp=now;
            if(within==119)
            {
                object Stats(List<double> a){var sorted=a.Order().ToArray();return new{mean=a.Average(),median=sorted[sorted.Length/2],p95=sorted[(int)(sorted.Length*.95)]};}
                _surroundPerformance.Add(new{orientation=_rig.OrientationName,scenery=_farmSurround.Visible,frames=_surroundTimes.Count,
                    frameMs=Stats(_surroundTimes),drawCalls=Stats(_surroundDraws),primitives=Stats(_surroundPrimitives)});
            }
            return;
        }
        var unchanged=_surroundHash==_session.CaptureSnapshot().AuthoritativeHash.ToString();
        if(!unchanged)throw new InvalidOperationException("Scenery fixture changed simulation");
        File.WriteAllText(Path.Combine(_surroundEvidenceOutput,"surround-evidence.json"),JsonSerializer.Serialize(new{
            passed=true,resolution=GetWindow().Size.ToString(),views=_surroundChecks,simulationUnchanged=unchanged,
            noCollisionOrNavigationNodes=true,eightOutsidePlacementAndPickProbesRejected=true,performance=_surroundPerformance,
            caveat="Uncapped vsync-off paused preparation fixture, 90 samples/state after warm-up; wall-frame timing includes whole app and desktop noise, not isolated GPU timing or live-festival FPS certification. Captures are scripted, no human playthrough."},new JsonSerializerOptions{WriteIndented=true}));
        GD.Print("SURROUND_EVIDENCE_COMPLETE passed=true");GetTree().Quit();
    }
}

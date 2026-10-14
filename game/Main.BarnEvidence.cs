using Festival.Simulation;
using Festival.Simulation.Fixtures;
using Festival.Persistence;
using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Festival.Game;

// Opt-in controlled evidence. Uses production navigation/performance and never opens/writes player saves.
public partial class Main
{
    private string? _barnEvidenceOutput;
    private int _barnFrame,_barnTicks;
    private int _barnReentryFrame=-1;
    private ulong _barnLastWall;
    private ulong _barnEmptyWall,_barnDeselectedWall;
    private bool _barnEmptyHoldChecked,_barnDeselectedHoldChecked;
    private Label _barnEvidenceLabel=null!;
    private readonly List<object> _barnChecks=[],_barnTrace=[];
    private readonly List<object> _barnFadeTrace=[];
    private IDictionary _barnNav=null!;
    private ulong _barnGuest,_barnPerformer,_barnStaff;
    private readonly Dictionary<ulong,(int X,int Z)> _barnSavedPositions=[];
    private void BarnCheck(string name,bool passed)
    { _barnChecks.Add(new{name,passed}); if(!passed)throw new InvalidOperationException("Barn evidence failed: "+name); }
    private void BarnPicture(string name)=>GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_barnEvidenceOutput!,name+".png"));
    private void StandBarnFixture(ulong id,int x,int z)
    {
        var state=_barnNav[new EntityId(id)]!;var type=state.GetType();
        type.GetProperty("XMillimetres")!.SetValue(state,x);type.GetProperty("ZMillimetres")!.SetValue(state,z);
        type.GetProperty("Action")!.SetValue(state,AgentNavigationAction.Arrived);type.GetProperty("IntentId")!.SetValue(state,null);
    }
    private void EmptyBarnFixture()
    {foreach(DictionaryEntry pair in _barnNav)StandBarnFixture(((EntityId)pair.Key).Value,20_000,25_000);}
    private string BarnPoseFingerprint()=>string.Join(";",_attendeeVisuals.Where(p=>p.Value.Visible &&
        _session.CaptureLivePerformance("stage.barn")!.Performers.Any(actor=>actor.AgentId==p.Key.Value)).Select(p=>
        p.Key+":"+p.Value.Transform+":"+string.Join("|",p.Value.FindChildren("*","AnimationPlayer",true,false).OfType<AnimationPlayer>().Select(a=>a.AssignedAnimation+":"+a.CurrentAnimationPosition))));
    private void ProcessBarnEvidence(double delta)
    {
        try { CaptureBarnEvidence(); }
        catch(Exception error)
        {
            Directory.CreateDirectory(_barnEvidenceOutput!);File.WriteAllText(Path.Combine(_barnEvidenceOutput!,"failure.txt"),error.ToString());
            File.WriteAllText(Path.Combine(_barnEvidenceOutput!,"failed-fade-trace.json"),JsonSerializer.Serialize(_barnFadeTrace,new JsonSerializerOptions{WriteIndented=true}));
            GD.PushError(error.ToString());GetTree().Quit(2);
        }
    }
    private void CaptureBarnEvidence()
    {
        // --fixed-fps fixes logical delta but can run capture-free frames faster than the normal limiter.
        // Pace this diagnostic only, so the marquee helper's wall-clock empty hold is measured honestly.
        var now=Time.GetTicksMsec();if(_barnLastWall>0 && now<_barnLastWall+34)OS.DelayMsec((int)(_barnLastWall+34-now));
        _barnLastWall=Time.GetTicksMsec();
        var frame=_barnFrame++;
        if(frame==0)
        {
            Directory.CreateDirectory(_barnEvidenceOutput!);Directory.CreateDirectory(Path.Combine(_barnEvidenceOutput!,"motion"));
            Engine.MaxFps=30;DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            _host=new SessionHost(BarnStageFixture.Started(20260922),Path.Combine(_barnEvidenceOutput!,"unused-diagnostic-save"),_saveCompatibility);
            _session.AdvanceWithoutSnapshot(8_000);_host.Clock.ResetBoundary();_foundationPresentation.Reset(_session.CaptureObservation());BuildAttendee();
            _hudWorkspaceOpen=_buildDrawerOpen=false;_boxOfficeSeenAttempt=1;RefreshPreparationHud();Dock.Readiness?.Hide();
            var canvas=new CanvasLayer{Layer=90};AddChild(canvas);_barnEvidenceLabel=new Label{Position=new(20,72),Size=new(500,100),AutowrapMode=TextServer.AutowrapMode.WordSmart};canvas.AddChild(_barnEvidenceLabel);
            _barnEvidenceLabel.AddThemeFontSizeOverride("font_size",16);
            _rig.Frame(new(0,3.5f,-23),26);_rig.Rotate(1);
            BarnCheck("distinct-stage-and-generator",_session.Stages[1].Id=="stage.barn" && _session.CaptureStageGenerator("stage.barn") is {XMillimetres:7_000,ZMillimetres:-13_500});
            BarnCheck("normal-two-stage-allocation",FestivalStages.For(2).Count==2 && FestivalStages.For(2)[1].Id==FestivalStages.BarnId);
            BarnCheck("explicit-barn-fade-groups",_barnFade?.RoofCount==9);
            var barn=_visualRegistry["farm.large-barn"];
            BarnCheck("no-full-building-pick-box",barn.GetChildren().OfType<CollisionShape3D>().All(c=>c.Shape is BoxShape3D box && box.Size.X<.3f && box.Size.Z<.3f));
            var materials=barn.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>().Where(m=>!m.Name.ToString().EndsWith("_Shadow",StringComparison.Ordinal))
                .SelectMany(m=>Enumerable.Range(0,m.Mesh!.GetSurfaceCount()).Select(i=>m.GetActiveMaterial(i))).OfType<StandardMaterial3D>().ToArray();
            BarnCheck("barn-backface-culling-retained",materials.Where(m=>BarnFadeSurface(m.ResourceName)).All(m=>m.CullMode==BaseMaterial3D.CullModeEnum.Back));
            BarnCheck("floor-frame-signs-opaque",materials.Where(m=>m.ResourceName.StartsWith("Barn_",StringComparison.Ordinal)&&!BarnFadeSurface(m.ResourceName)).All(m=>m.Transparency==BaseMaterial3D.TransparencyEnum.Disabled&&m.AlbedoColor.A==1));
            BarnCheck("shadow-twins-present",barn.FindChildren("*_Shadow","MeshInstance3D",true,false).OfType<MeshInstance3D>().Count(m=>m.CastShadow==GeometryInstance3D.ShadowCastingSetting.ShadowsOnly)==9);
            var other=InstantiateAsset("res://assets/environment/barn-stage-v1/lwf_barn_stage_v1.glb");
            var separate=RoofFade.Attach(other,BarnFadeSurface,twoSided:false);separate.Update(true,0);
            BarnCheck("per-instance-fade-isolation",Mathf.Abs(separate.Alpha-.3f)<.001f&&_barnFade!.Alpha==1);other.Free();
        }
        if(frame<450)
        {
            _barnTicks+=80;_session.AdvanceWithoutSnapshot(_barnTicks/30);_barnTicks%=30;
            _barnEvidenceLabel.Text="CONTROLLED BARN FIXTURE · production navigation / real programme\nExisting second-slot timings, paid crew and outside generator; campaign conversion opens at Tier 2.";
            ProcessDayCycle(1.0/30);ProcessRain();AdvancePreparationPresentation(1.0/30);ProcessBarnStage(1.0/30);
            var live=_session.CaptureLivePerformance("stage.barn")!;
            _barnTrace.Add(new{frame,tick=_session.CurrentTick,live.Stage,live.PlannedTick,live.StartedTick,occupants=_session.BarnOccupants,
                performers=live.Performers.Select(p=>new{p.AgentId,p.OnStage,p.InstrumentAttached,position=_session.CaptureObservation().NavigationAgents.First(n=>n.Id.Value==p.AgentId),body=_attendeeVisuals[new(p.AgentId)].Position.ToString()}).ToArray()});
            BarnPicture($"motion/{frame:D4}");return;
        }
        if(frame==450)
        {
            // Ordinary elapsed simulation lets an audience cross from the field. This interval is not movie footage.
            while(_session.CurrentTick<13_000 && _session.PreparedStatus==PreparationStatus.Running &&
                _session.CaptureLivePerformance("stage.barn")!.Listeners.Count(p=>p.AtPlace)<3)_session.AdvanceWithoutSnapshot(80);
            AdvancePreparationPresentation(1.0/30);ProcessBarnStage(1.0/30);
            var live=_session.CaptureLivePerformance("stage.barn")!;
            BarnCheck("band-walked-up-and-plays",live.Stage==LiveSetStage.Live && live.Performers.All(p=>p.OnStage&&p.InstrumentAttached));
            BarnCheck("audience-actually-reaches-indoor-listening",live.Listeners.Any(p=>p.AtPlace));
            foreach(var p in live.Performers)
            {var body=_attendeeVisuals[new(p.AgentId)];BarnCheck("band-west-facing-"+p.AgentId,(-body.Transform.Basis.Z).Dot(Vector3.Left)>.99f);BarnCheck("band-deck-height-"+p.AgentId,Mathf.Abs(body.Position.Y-.9f)<.002f);}
            _session.Execute(CampaignEnvelope(new SetPausedCommand(true)));AdvancePreparationPresentation(0);
            var before=BarnPoseFingerprint();var tick=_session.CurrentTick;
            for(var i=0;i<5;i++)AdvancePreparationPresentation(100);
            BarnCheck("pause-exact-band-pose",before==BarnPoseFingerprint()&&tick==_session.CurrentTick);
            _session.Execute(CampaignEnvelope(new SetPausedCommand(false)));AdvancePreparationPresentation(1.0/30);
            _session.Execute(CampaignEnvelope(new SetPausedCommand(true)));AdvancePreparationPresentation(0);
            BuildStartSplash();before=BarnPoseFingerprint();for(var i=0;i<5;i++)AdvancePreparationPresentation(100);
            BarnCheck("menu-over-paused-host-exact-band-pose",before==BarnPoseFingerprint());StartMenu.Close();
            _session.Execute(CampaignEnvelope(new SetPausedCommand(false)));
        }
        if(frame<810)
        {
            var local=frame-450;var view=local/15;
            if(local%15==0)
            {
                var angle=view%4;while(_rig.OrientationName!=new[]{"South","West","North","East"}[angle])_rig.Rotate(1);
                _rig.Frame(new(0,3.5f,-23),view/4%2==0?18:62);_dayFractionOverride=view/8==0?.35f:.9f;
                if(view/8==2)
                    typeof(GameSession).GetField("_weather",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(_session,new WeatherSnapshot(1,WeatherKind.Rain,[new(0,100_000,RainIntensity.Downpour)],[],[]));
            }
            _barnEvidenceLabel.Text=$"CONTROLLED BARN INSPECTION · {_rig.OrientationName} / camera {_rig.Camera.Size}\nAuthority held; day light / late light / rain override labelled. Original .30 fade; full shelter shadows.";
            ProcessDayCycle(1.0/30);ProcessRain();AdvancePreparationPresentation(1.0/30);ProcessBarnStage(1.0/30);
            if(local%15==12)
            {
                var light=view/8==0?"day":view/8==1?"late":"rain-late";
                BarnPicture($"{light}-{_rig.OrientationName}-zoom{_rig.Camera.Size:0}");
                var visible=_session.CaptureObservation().NavigationAgents.Where(n=>BarnLayout.Interior(n.XMillimetres,n.ZMillimetres))
                    .Select(n=>n.Id).Where(id=>_attendeeVisuals[id].IsVisibleInTree()).ToArray();
                var picked=visible.Count(id=>
                {var body=_attendeeVisuals[id];var screen=_rig.Camera.UnprojectPosition(body.GlobalPosition+new Vector3(0,1.4f,0));var hit=ResolveWorldHit(screen);return hit is not null&&_attendeePickRegistry.GetValueOrDefault(hit.GetInstanceId())==id;});
                BarnCheck($"visible-occupant-picking-{light}-{_rig.OrientationName}-{_rig.Camera.Size}",picked>0);
                _barnFadeTrace.Add(new{frame,light,angle=_rig.OrientationName,zoom=_rig.Camera.Size,visiblePeople=visible.Length,exactPersonRayHits=picked,alpha=_barnFade!.Alpha});
            }
            return;
        }
        if(frame==810)
        {
            _barnNav=(IDictionary)typeof(GameSession).GetField("_navigationAgents",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(_session)!;
            foreach(var n in _session.CaptureObservation().NavigationAgents)_barnSavedPositions[n.Id.Value]=(n.XMillimetres,n.ZMillimetres);
            var people=_session.CapturePreparation()!.People;
            _barnGuest=people.First(p=>p.Role==ProtectedPersonRole.Guest&&p.Admitted&&!p.Departed).AgentId;
            _barnPerformer=_session.CaptureLivePerformance("stage.barn")!.Performers[0].AgentId;
            _barnStaff=_session.StageEngineerId("stage.barn")!.Value;
            EmptyBarnFixture();ClearSelection();_barnEmptyWall=Time.GetTicksMsec();_dayFractionOverride=.35f;_rig.Frame(new(0,3.5f,-23),26);
            typeof(GameSession).GetField("_weather",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(_session,new WeatherSnapshot(1,WeatherKind.Heatwave,[],[],[]));
        }
        if(frame<1260)
        {
            if(frame==930)SelectObject(LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.large-barn"));
            if(frame==970){ClearSelection();_barnDeselectedWall=Time.GetTicksMsec();}
            if(frame==1040){var p=_barnSavedPositions[_barnPerformer];StandBarnFixture(_barnPerformer,p.X,p.Z);}
            if(frame==1090){EmptyBarnFixture();StandBarnFixture(_barnGuest,0,-23_000);}
            if(frame==1140){EmptyBarnFixture();StandBarnFixture(_barnStaff,-4_000,-23_000);}
            if(frame==1170)EmptyBarnFixture();
            // Observe the tween state: wall-clock holds can expire in fewer logical frames when rendering stalls.
            if(frame>1170 && _barnReentryFrame<0 && _barnFade!.Alpha>.3f && _barnFade.Alpha<1)
            {
                BarnCheck("empty-recovery-in-progress",_session.BarnOccupants==0&&_barnFade!.Alpha>.3f&&_barnFade.Alpha<1);
                StandBarnFixture(_barnGuest,0,-23_000);_barnReentryFrame=frame;
            }
            _barnEvidenceLabel.Text="CONTROLLED OCCUPANCY / SELECTION CHECK · authority held\nExplicitly repositioned inspection actors; no movement claim. Any indoor guest, performer or staff reveals the shell.";
            ProcessDayCycle(1.0/30);ProcessRain();AdvancePreparationPresentation(1.0/30);ProcessBarnStage(1.0/30);
            _barnFadeTrace.Add(new{frame,timeMs=Time.GetTicksMsec(),occupants=_session.BarnOccupants,selected=_selected?.Kind==FarmObjectKind.LargeBarn,alpha=_barnFade!.Alpha});
            if(frame>810 && frame<=880 && !_barnEmptyHoldChecked && _barnFade!.Alpha>.301f)
            {BarnCheck("empty-one-second-hold",Time.GetTicksMsec()-_barnEmptyWall>=1000);_barnEmptyHoldChecked=true;}
            if(frame==880){BarnCheck("empty-restores-opaque",_barnEmptyHoldChecked&&_session.BarnOccupants==0&&_barnFade!.Alpha>.999f);BarnPicture("empty-opaque");}
            if(frame==950){BarnCheck("selected-empty-reveals",_session.BarnOccupants==0&&Mathf.Abs(_barnFade!.Alpha-.3f)<.001f);BarnPicture("selected-empty-translucent");}
            if(frame>970 && frame<=1030 && !_barnDeselectedHoldChecked && _barnFade!.Alpha>.301f)
            {BarnCheck("deselected-empty-hold",Time.GetTicksMsec()-_barnDeselectedWall>=1000);_barnDeselectedHoldChecked=true;}
            if(frame==1030)BarnCheck("deselected-restores-opaque",_barnDeselectedHoldChecked&&_barnFade!.Alpha>.999f);
            if(frame is 1060 or 1110 or 1160)
            {BarnCheck("lone-role-occupancy-"+frame,_session.BarnOccupants==1&&Mathf.Abs(_barnFade!.Alpha-.3f)<.001f);BarnPicture(frame==1060?"lone-performer":frame==1110?"lone-guest":"lone-staff");}
            if(frame==1230){BarnCheck("re-entry-reverses-recovery",_barnReentryFrame>1170&&frame-_barnReentryFrame>=9&&_session.BarnOccupants==1&&Mathf.Abs(_barnFade!.Alpha-.3f)<.001f);BarnPicture("re-entry-translucent");}
            return;
        }
        BarnCheck("diagnostic-never-saves",!Directory.Exists(Path.Combine(_barnEvidenceOutput!,"unused-diagnostic-save")));
        BarnCheck("inside-shelter-outside-apron-uncovered",_session.CoveredCell(BarnLayout.Cell(0,0))&&!_session.CoveredCell(BarnLayout.Cell(0,6_250)));
        var json=new JsonSerializerOptions{WriteIndented=true};
        File.WriteAllText(Path.Combine(_barnEvidenceOutput!,"checks.json"),JsonSerializer.Serialize(_barnChecks,json));
        File.WriteAllText(Path.Combine(_barnEvidenceOutput!,"trace.json"),JsonSerializer.Serialize(_barnTrace,json));
        File.WriteAllText(Path.Combine(_barnEvidenceOutput!,"fade-and-picking.json"),JsonSerializer.Serialize(_barnFadeTrace,json));
        GD.Print("BARN_EVIDENCE_COMPLETE");GetTree().Quit();
    }
}

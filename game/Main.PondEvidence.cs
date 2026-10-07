using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Festival.Game;

// Opt-in paused preparation render fixture; no player save and no simulation duck actors.
public partial class Main
{
    private string? _pondEvidenceOutput;
    private int _pondEvidenceFrame;
    private readonly List<object> _pondEvidenceChecks=[];
    private readonly List<object> _pondEvidencePerformance=[];
    private readonly List<double> _pondEvidenceTimes=[],_pondEvidenceDraws=[];
    private long _pondEvidenceTimestamp;

    private string PondVisualState()=>JsonSerializer.Serialize(new {
        clock=_pondClock.Seconds,ducks=_pondDucks.Select(n=>n.Transform.ToString()),wakes=_pondWakes.Select(n=>n.Transform.ToString()),
        phase=_pondWater.GetShaderParameter("phase").AsDouble(),
        strength=_pondWakeMaterials.Select(m=>m.GetShaderParameter("strength").AsDouble()),
        bob=_pondWakeMaterials.Select(m=>m.GetShaderParameter("bob").AsDouble())});

    private void CheckPondFreeze(string name,Action enter,Action leave)
    {
        enter();var before=PondVisualState();var hash=_session.CaptureSnapshot().AuthoritativeHash;
        ProcessPond(600);var frozen=before==PondVisualState();var unchanged=hash==_session.CaptureSnapshot().AuthoritativeHash;
        leave();var time=_pondClock.Seconds;ProcessPond(.02);var resumed=Math.Abs(_pondClock.Seconds-time-.02)<1e-9;
        _pondEvidenceChecks.Add(new{name,frozen,simulationUnchanged=unchanged,resumedWithoutJump=resumed});
        if(!frozen||!unchanged||!resumed)throw new InvalidOperationException("Pond freeze failed: "+name);
    }

    private void SetupPondEvidence()
    {
        Directory.CreateDirectory(_pondEvidenceOutput!);
        var perk=_session.CapturePerks()!;
        foreach(var command in new SessionCommand[]{new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,perk.Hand[0]),new UseDefaultBuildLayoutCommand()})
        {
            var result=_session.Execute(CampaignEnvelope(command));if(!result.IsAccepted)throw new InvalidOperationException(result.Message);
        }
        SyncBuildWorld();_hudWorkspaceOpen=_buildDrawerOpen=false;_boxOfficeSeenAttempt=1;RefreshPreparationHud();
        Engine.MaxFps=0;DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        if(_pondWorld.Position!=new Vector3(24,0,24.5f)||_pondWorld.FindChildren("*","CollisionObject3D",true,false).Count!=0)
            throw new InvalidOperationException("Pond transform/physics changed");
        var hash=_session.CaptureSnapshot().AuthoritativeHash;ProcessPond(.02);
        if(hash!=_session.CaptureSnapshot().AuthoritativeHash)throw new InvalidOperationException("Pond changed simulation");
        void Pause(bool value)
        {
            var result=_session.Execute(CampaignEnvelope(new SetPausedCommand(value)));
            if(!result.IsAccepted)throw new InvalidOperationException(result.Message);
        }
        CheckPondFreeze("game-pause",()=>Pause(true),()=>Pause(false));
        var field=typeof(GameSession).GetField("_preparation",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var preparation=(PreparationSnapshot)field.GetValue(_session)!;
        foreach(var status in new[]{PreparationStatus.Failed,PreparationStatus.Finished})
            CheckPondFreeze("terminal-"+status,()=>field.SetValue(_session,preparation with{Status=status}),()=>field.SetValue(_session,preparation));
        CheckPondFreeze("pond-hidden",()=>_pondWorld.Hide(),()=>_pondWorld.Show());
        CheckPondFreeze("start-menu",BuildStartSplash,StartMenu.Close);
        var manifest=JsonDocument.Parse(File.ReadAllText("C:/Projects/festival-tycoon/assets/source/environment/pond-polish-asset-v1/manifest.json"));
        var cells=manifest.RootElement.GetProperty("blocked_cells").EnumerateArray().Select(p=>new GridCell(p[0].GetInt32(),p[1].GetInt32())).ToArray();
        var terrain=new TraversalGrid(Festival.Simulation.Fixtures.NavigationFixture.CreateLowerWitteringTerrain());
        foreach(var cell in cells)
        {
            if(terrain.Get(cell).IsWalkable||_session.ValidateCommand(CampaignEnvelope(new PlaceBuildServiceCommand(BuildServiceKind.Bin,cell))) is null)
                throw new InvalidOperationException("Pond cell became navigable/placeable: "+cell);
        }
        _pondEvidenceChecks.Add(new{name="existing-pond-footprint",blockedCells=cells.Length,unchanged=true,noNewPickingBodies=true});
    }

    private void ProcessPondEvidence()
    {
        if(_pondEvidenceOutput is null)return;
        if(_pondEvidenceFrame==0)
        {
            try { SetupPondEvidence(); }
            catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(2);_pondEvidenceOutput=null;return;}
        }
        Dock.Readiness?.Hide();
        var frame=_pondEvidenceFrame++;
        const int poses=12,framesPerPose=8;
        if(frame<poses*framesPerPose)
        {
            var index=frame/framesPerPose;var view=index%3;
            if(frame%framesPerPose==0)
            {
                if(view==0&&index>0)_rig.Rotate(1);
                _rig.Frame(view==2?Vector3.Zero:new(24,0,24.5f),view==0?11:view==1?32:66);
                _pondClock.Reset(32+index*9);ApplyPondPose();
            }
            if(frame%framesPerPose==6)
            {
                var name=$"{index/3}-{_rig.OrientationName.ToLowerInvariant()}-{(view==0?"close":view==1?"game32":"farm")}";
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_pondEvidenceOutput,name+".png"));
            }
            return;
        }
        var after=frame-poses*framesPerPose;
        var clip=OS.GetCmdlineUserArgs().Contains("--pond-sequence");
        if(clip&&after<181)
        {
            var directory=Path.Combine(_pondEvidenceOutput,"sequence");Directory.CreateDirectory(directory);
            if(after>0)GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory,$"{after-1:000}.png"));
            _rig.Frame(new(24,0,24.5f),10);_pondClock.Reset(52+after/12.0);ApplyPondPose();
            return;
        }
        var pf=after-(clip?181:0);var phase=pf/120;var within=pf%120;
        if(phase<2)
        {
            if(within==0)
            {
                _rig.Frame(new(24,0,24.5f),32);_pondWorld.Visible=phase==1;
                _pondEvidenceTimes.Clear();_pondEvidenceDraws.Clear();
            }
            var now=Stopwatch.GetTimestamp();
            if(within>=30)
            {
                _pondEvidenceTimes.Add(Stopwatch.GetElapsedTime(_pondEvidenceTimestamp,now).TotalMilliseconds);
                _pondEvidenceDraws.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
            }
            _pondEvidenceTimestamp=now;
            if(within==119)
            {
                var sorted=_pondEvidenceTimes.Order().ToArray();
                _pondEvidencePerformance.Add(new{pondVisible=_pondWorld.Visible,frames=90,medianFrameMs=sorted[45],p95FrameMs=sorted[85],drawCalls=_pondEvidenceDraws.Average()});
            }
            return;
        }
        File.WriteAllText(Path.Combine(_pondEvidenceOutput,"pond-evidence.json"),JsonSerializer.Serialize(new {
            passed=true,resolution=GetWindow().Size.ToString(),checks=_pondEvidenceChecks,performance=_pondEvidencePerformance,
            caveat="Scripted staged preparation fixture. Whole-app vsync-off wall-frame comparison of full pond hidden/visible, not isolated GPU cost or live-festival FPS certification; no manual playthrough."},new JsonSerializerOptions{WriteIndented=true}));
        GD.Print("POND_EVIDENCE_COMPLETE passed=true");GetTree().Quit();
    }
}

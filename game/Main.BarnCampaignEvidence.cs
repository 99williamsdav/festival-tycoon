using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Festival.Game;

// Actual campaign evidence: normal menu, commands, simulation and renderers; no actor/forecast/cash mutations.
// Explicit time stepping and saves stay under the requested diagnostic output directory.
public partial class Main
{
    private string? _barnCampaignEvidenceOutput;
    private int _barnCampaignFrame, _barnCampaignTicks;
    private ulong _barnCampaignLastWall;
    private readonly List<object> _barnCampaignChecks=[], _barnCampaignTrace=[];
    private readonly List<(CanvasLayer Layer,bool Visible)> _barnCampaignLayers=[];
    private string _barnCampaignFreshHash="";
    private Label _barnCampaignCaption=null!;
    private void CampaignBarnCheck(string name,bool passed)
    { _barnCampaignChecks.Add(new{name,passed});if(!passed)throw new InvalidOperationException("Barn campaign evidence failed: "+name); }
    private void CampaignBarnPicture(string name)=>GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_barnCampaignEvidenceOutput!,name+".png"));
    private async void CampaignBarnMotionPicture(string name)
    {
        try
        {
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            CampaignBarnPicture(name);
        }
        catch(Exception error)
        {
            File.WriteAllText(Path.Combine(_barnCampaignEvidenceOutput!,"failure.txt"),error.ToString());
            GD.PushError(error.ToString());GetTree().Quit(2);
        }
    }
    private Button CampaignBarnButton(string name)=>FindChildren(name,"Button",true,false).OfType<Button>().First(b=>b.IsVisibleInTree());
    private void CampaignBarnPress(string name)=>CampaignBarnButton(name).EmitSignal(Button.SignalName.Pressed);
    private void CampaignBarnSend(SessionCommand command)
    {var result=_host.Submit(command);CampaignBarnCheck("command-"+command.GetType().Name,result.IsAccepted);}
    private void CampaignBarnHideHud()
    {
        _barnCampaignLayers.Clear();
        foreach(var layer in GetChildren().OfType<CanvasLayer>().Where(l=>!l.IsQueuedForDeletion()&&l.Layer<90))
        {_barnCampaignLayers.Add((layer,layer.Visible));layer.Hide();}
    }
    private void CampaignBarnRestoreHud()
    {
        foreach(var (layer,visible) in _barnCampaignLayers)if(IsInstanceValid(layer)&&!layer.IsQueuedForDeletion())layer.Visible=visible;
        _barnCampaignLayers.Clear();
    }
    private void CampaignBarnRender(double delta)
    {ProcessDayCycle(delta);ProcessRain();ProcessPondStage(delta);ProcessBarnStage(delta);AdvancePreparationPresentation(delta);SyncStageNowPlaying();}
    private void CampaignBarnRunTo(int tick)
    {
        while(_session.CurrentTick<tick&&_session.PreparedStatus==PreparationStatus.Running)_session.AdvanceWithoutSnapshot(Math.Min(80,tick-(int)_session.CurrentTick));
        CampaignBarnCheck("reached-tick-"+tick,_session.CurrentTick==tick&&_session.PreparedStatus==PreparationStatus.Running);
        _host.Clock.ResetBoundary();_foundationPresentation.Reset(_session.CaptureObservation());
    }
    private void CampaignBarnView(int angle)
    {while(_rig.OrientationName!=new[]{"South","West","North","East"}[angle])_rig.Rotate(1);_rig.Frame(new(0,3.5f,-23),36);}
    private AudioStreamPlayer? CampaignBarnMusic(string id)
    {
        var sounds=(IDictionary)typeof(FestivalAudio).GetField("_stages",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(Audio)!;
        return sounds[id] is {} sound?(AudioStreamPlayer)sound.GetType().GetField("Music")!.GetValue(sound)!:null;
    }
    private void ProcessBarnCampaignEvidence()
    {
        var now=Time.GetTicksMsec();if(_barnCampaignLastWall>0&&now<_barnCampaignLastWall+34)OS.DelayMsec((int)(_barnCampaignLastWall+34-now));
        _barnCampaignLastWall=Time.GetTicksMsec();
        try{CaptureBarnCampaignEvidence();}
        catch(Exception error)
        {
            Directory.CreateDirectory(_barnCampaignEvidenceOutput!);File.WriteAllText(Path.Combine(_barnCampaignEvidenceOutput!,"failure.txt"),error.ToString());
            File.WriteAllText(Path.Combine(_barnCampaignEvidenceOutput!,"failure-state.json"),JsonSerializer.Serialize(new{tick=_session.CurrentTick,checks=_barnCampaignChecks,barn=_session.CaptureLivePerformance(FestivalStages.BarnId),trailer=_session.CaptureLivePerformance(FestivalStages.MainId),generator=_session.CaptureStageGenerator(FestivalStages.BarnId)},new JsonSerializerOptions{WriteIndented=true}));
            GD.PushError(error.ToString());GetTree().Quit(2);
        }
    }
    private void CaptureBarnCampaignEvidence()
    {
        var frame=_barnCampaignFrame++;
        if(frame==0)
        {
            Directory.CreateDirectory(_barnCampaignEvidenceOutput!);Directory.CreateDirectory(Path.Combine(_barnCampaignEvidenceOutput!,"motion"));
            _challengeFlowEnabled=true;BuildStartSplash();
            CampaignBarnCheck("isolated-campaign-save-host",_host.SaveDirectory==Path.Combine(_barnCampaignEvidenceOutput!,"diagnostic-saves"));
            var canvas=new CanvasLayer{Layer=90};AddChild(canvas);_barnCampaignCaption=new Label{Position=new(20,72),Size=new(660,68),AutowrapMode=TextServer.AutowrapMode.WordSmart};canvas.AddChild(_barnCampaignCaption);
            _barnCampaignCaption.AddThemeFontSizeOverride("font_size",16);
        }
        if(frame==8){CampaignBarnPicture("menu");CampaignBarnPress("DevelopmentTiers");}
        if(frame==16)CampaignBarnPress("DevelopmentTier1");
        if(frame==24){CampaignBarnPicture("tier-one-heatwave");CampaignBarnPress("PrepareFestival");}
        if(frame==32)
        {
            CampaignBarnCheck("fresh-tier-one-ordinary-barn",!_session.BarnStageOpen&&!_session.SecondStageOpen&&_session.CapturePreparation()!.OpeningCashPennies==45_000);
            CampaignBarnHideHud();_barnCampaignCaption.Text="TIER 1 · ordinary farm barn\nNo stage sign, conversion kit, venue lamps or outside stage generator. Actual fresh campaign.";
        }
        if(frame>=36&&frame<100)
        {
            var view=(frame-36)/16;if((frame-36)%16==0)CampaignBarnView(view);
            CampaignBarnRender(1.0/30);
            if((frame-36)%16==12)
            {
                CampaignBarnPicture("tier-one-"+_rig.OrientationName);
                CampaignBarnCheck("ordinary-barn-dressing-hidden-"+_rig.OrientationName,_barnConversionParts.Length==8&&_barnConversionParts.All(n=>!n.Visible)&&_barnStageKit!.Visible==false&&_barnStageHeading!.Visible==false&&_barnGenerator is null&&_barnDrums is null&&_barnVenueLights.All(l=>!l.Visible)&&_barnStageLights.All(l=>!l.Visible));
                CampaignBarnCheck("ordinary-shell-retained-"+_rig.OrientationName,_visualRegistry["farm.large-barn"].Visible&&_barnFade!.Alpha==1);
            }
            return;
        }
        if(frame==100)
        {
            _selected=LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.large-barn");CampaignBarnView(0);
            _barnCampaignCaption.Text="TIER 1 · selected ordinary barn\nSame roof/interior reveal and permanent cover; no conversion dressing or phantom deck.";
        }
        if(frame>=100&&frame<112)CampaignBarnRender(1.0/30);
        if(frame==111){CampaignBarnPicture("tier-one-selected-interior");CampaignBarnCheck("ordinary-selection-reveals-without-conversion",_barnFade!.Alpha<=.301f&&!_barnStageKit!.Visible);}
        if(frame==112){ClearSelection();CampaignBarnRestoreHud();_barnCampaignCaption.Text="";BuildStartSplash();}
        if(frame==120)CampaignBarnPress("DevelopmentTiers");
        if(frame==128)
        {
            CampaignBarnPress("DevelopmentTier2");_barnCampaignFreshHash=_session.CaptureSnapshot().AuthoritativeHash.ToString();
            CampaignBarnCheck("direct-tier-two-trailer-barn",_session.Stages.Select(s=>s.Id).SequenceEqual(new[]{FestivalStages.MainId,FestivalStages.BarnId})&&!_session.PondStageOpen);
            CampaignBarnCheck("canonical-carry-unchanged",_session.CapturePreparation()!.CarriedIn is{CashPennies:37_260,DebtPennies:20_000}&&_session.CapturePreparation()!.OpeningCashPennies==112_260);
        }
        if(frame==136){CampaignBarnPicture("tier-two-heavy-rain");CampaignBarnCheck("rain-reveal-before-preparation",_challengeReveal.IsOpen&&_challengeReveal.Artwork!.Texture.ResourcePath.EndsWith("heavy-rain-a.png",StringComparison.Ordinal));CampaignBarnPress("PrepareFestival");}
        if(frame==144)
        {
            var perk=_session.CapturePerks()!;CampaignBarnSend(new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,perk.Hand[0]));
            CampaignBarnSend(new UseDefaultBuildLayoutCommand());
            var acts=_session.GetFestivalActs().Where(a=>_session.ActStandingOf(a)==ActStanding.Available).OrderBy(_session.ActFee).ThenBy(a=>a.Id,StringComparer.Ordinal).Select(a=>a.Id).ToArray();
            CampaignBarnSend(new SetProgrammeCommand(acts[..3]));CampaignBarnSend(new SetProgrammeCommand(acts[3..6]){StageId=FestivalStages.BarnId});
            CampaignBarnSend(new SetPreparationStockCommand(40,32));
            var crew=_session.GetStaffCandidates().GroupBy(c=>c.Role).Select(g=>g.OrderBy(c=>c.Traits.Length>0).ThenBy(c=>c.Grade>0).ThenBy(c=>Math.Abs(c.Grade)).ThenBy(c=>c.Id,StringComparer.Ordinal).First()).ToArray();
            foreach(var person in crew)CampaignBarnSend(new AcceptPreparationOfferCommand(person.Id));
            foreach(var role in new[]{StaffRole.Sound,StaffRole.Medic})
            {var extra=_session.GetStaffCandidates().Where(c=>c.Role==role&&!crew.Any(p=>p.Id==c.Id)).OrderBy(c=>c.Traits.Length>0).ThenBy(c=>c.Grade>0).ThenBy(c=>Math.Abs(c.Grade)).ThenBy(c=>c.Id,StringComparer.Ordinal).First();CampaignBarnSend(new AcceptPreparationOfferCommand(extra.ExtraOfferId));}
            CampaignBarnCheck("normal-paid-two-stage-plan-ready",_session.GetPreparationStartBlockers().Count==0&&_session.PreparationPlanCost<112_260);RefreshPreparationHud();
        }
        if(frame==152)
        {
            CampaignBarnPicture("tier-two-box-office");
            FindChildren("*","Button",true,false).OfType<Button>().First(b=>b.IsVisibleInTree()&&b.Text=="Start planning").EmitSignal(Button.SignalName.Pressed);
        }
        if(frame==160)
        {
            CampaignBarnPicture("tier-two-programme");
            CampaignBarnSend(new StartPreparedEditionCommand());_hudWorkspaceOpen=_buildDrawerOpen=false;_boxOfficeSeenAttempt=1;RefreshPreparationHud();
            CampaignBarnRunTo(8_000);BuildAttendee();CampaignBarnHideHud();CampaignBarnView(0);
            _barnCampaignCaption.Text="TIER 2 · actual campaign / Heavy Rain\nReal paid crew, band stair arrival and covered audience; generator just outside. No actor or weather overrides.";
        }
        if(frame>=160&&frame<610)
        {
            _barnCampaignTicks+=80;_session.AdvanceWithoutSnapshot(_barnCampaignTicks/30);_barnCampaignTicks%=30;
            CampaignBarnRender(1.0/30);
            var live=_session.CaptureLivePerformance(FestivalStages.BarnId)!;
            _barnCampaignTrace.Add(new{frame,tick=_session.CurrentTick,rain=_session.RainNow,live.Stage,live.StartedTick,occupants=_session.BarnOccupants,
                performers=live.Performers.Select(p=>new{p.AgentId,p.OnStage,p.InstrumentAttached,nav=_session.CaptureObservation().NavigationAgents.First(n=>n.Id.Value==p.AgentId),body=_attendeeVisuals[new(p.AgentId)].Position.ToString()}).ToArray()});
            CampaignBarnMotionPicture($"motion/{frame-160:D4}");return;
        }
        if(frame==610)
        {
            CampaignBarnCheck("actual-first-set-on-time",_session.CaptureLivePerformance(FestivalStages.BarnId) is{Stage:LiveSetStage.Live,StartedTick:9_000});
            var spell=_session.CaptureWeather()!.Spells.First(s=>s.EndTick>12_000&&s.StartTick<17_000);
            CampaignBarnRunTo((int)Math.Max(12_000,spell.StartTick+80));
            CampaignBarnCheck("actual-rain-during-covered-music",_session.RainNow!=RainIntensity.Dry);
            var live=_session.CaptureLivePerformance(FestivalStages.BarnId)!;var nav=_session.CaptureObservation().NavigationAgents.ToDictionary(n=>n.Id.Value);
            CampaignBarnCheck("real-audience-arrived-under-cover",live.Listeners.Any(p=>p.AtPlace&&BarnLayout.Interior(nav[p.AgentId].XMillimetres,nav[p.AgentId].ZMillimetres)));
            CampaignBarnCheck("paid-engineer-reached-barn",nav.TryGetValue(_session.StageEngineerId(FestivalStages.BarnId)!.Value,out var engineer)&&BarnLayout.Interior(engineer.XMillimetres,engineer.ZMillimetres));
            CampaignBarnSend(new SetPausedCommand(true));
        }
        if(frame>=612&&frame<676)
        {
            var view=(frame-612)/16;if((frame-612)%16==0)CampaignBarnView(view);CampaignBarnRender(1.0/30);
            if((frame-612)%16==12)
            {
                CampaignBarnPicture("tier-two-live-rain-"+_rig.OrientationName);
                CampaignBarnCheck("conversion-visible-only-active-"+_rig.OrientationName,_barnConversionParts.All(n=>n.Visible)&&_barnStageKit!.Visible&&_barnStageHeading!.Visible&&_barnGenerator!.IsVisibleInTree()&&_pondStage?.Visible!=true&&_pondGeneratorVisual is null);
                CampaignBarnCheck("outside-generator-position-"+_rig.OrientationName,_barnGenerator!.Position==new Vector3(7,0,-13.5f)&&!BarnLayout.Interior(7_000,-13_500));
                var live=_session.CaptureLivePerformance(FestivalStages.BarnId)!;
                CampaignBarnCheck("all-roles-on-actual-deck-"+_rig.OrientationName,live.Performers.All(p=>p.OnStage&&p.InstrumentAttached&&Mathf.Abs(_attendeeVisuals[new(p.AgentId)].Position.Y-.9f)<.002f));
            }
            return;
        }
        if(frame==676)
        {
            // The naturally rainy inspection may outlast Trailer's first set. Use the real second-set overlap for cutoff/audio.
            CampaignBarnSend(new SetPausedCommand(false));CampaignBarnRunTo(21_000);
            CampaignBarnCheck("real-second-act-on-time",_session.CaptureLivePerformance(FestivalStages.BarnId) is{Stage:LiveSetStage.Live,StartedTick:20_600});
            CampaignBarnSend(new SetPausedCommand(true));
            CampaignBarnRestoreHud();CampaignBarnView(0);_barnCampaignCaption.Text="";RefreshPreparationHud();CampaignBarnRender(0);
            var screen=_rig.Camera.UnprojectPosition(_barnGenerator!.Position+new Vector3(.17f,1,0));var hit=ResolveWorldHit(screen);
            CampaignBarnCheck("outside-generator-nearest-hit-pick",hit is not null&&TrySelectBarnGenerator(hit));
            CampaignBarnCheck("contextual-barn-power-action",_barnCutButton!.Visible&&!_barnCutButton.Disabled&&_inspectorTitle.Text=="Barn Stage generator");
        }
        if(frame==684)
        {
            CampaignBarnPicture("tier-two-generator-context");_barnCutButton!.EmitSignal(Button.SignalName.Pressed);
            CampaignBarnSend(new SetPausedCommand(false));_session.AdvanceWithoutSnapshot(80);CampaignBarnRender(1.0/30);
            CampaignBarnCheck("context-cut-only-barn",_session.CaptureLivePerformance(FestivalStages.BarnId)!.Stage==LiveSetStage.Interrupted&&_session.CaptureLivePerformance(FestivalStages.MainId)!.Stage==LiveSetStage.Live);
            CampaignBarnCheck("barn-audio-stopped",CampaignBarnMusic(FestivalStages.BarnId)?.Playing==false);
            CampaignBarnCheck("trailer-audio-kept-playing",CampaignBarnMusic(FestivalStages.MainId)?.Playing==true);
            CampaignBarnCheck("barn-now-playing-hidden-after-cut",!_stageNowPlaying[FestivalStages.BarnId].Visible&&_stageNowPlaying[FestivalStages.MainId].Visible);
            var save=SaveFileAdapter.SaveSlot(_host.SaveDirectory,"rain-live",new(_session,_saveCompatibility,"diagnostic",DateTimeOffset.UtcNow));
            var loaded=SaveFileAdapter.LoadSlot(_host.SaveDirectory,"rain-live",_saveCompatibility);
            CampaignBarnCheck("actual-live-cut-save-roundtrip",save.IsSuccess&&loaded.IsSuccess&&loaded.Session!.CaptureSnapshot().AuthoritativeHash==_session.CaptureSnapshot().AuthoritativeHash&&loaded.Session.BarnStageOpen);
        }
        if(frame==692){CampaignBarnRender(1.0/30);CampaignBarnPicture("tier-two-barn-cut-trailer-live");BuildStartSplash();SyncStageNowPlaying();CampaignBarnCheck("menu-hides-all-now-playing",_stageNowPlaying.Values.All(l=>!l.Visible));}
        if(frame==700)
        {var hash=_session.CaptureSnapshot().AuthoritativeHash;CampaignBarnPress("EnterFestival");CampaignBarnCheck("ordinary-menu-resume-keeps-state",hash==_session.CaptureSnapshot().AuthoritativeHash&&_session.BarnStageOpen);BuildStartSplash();}
        if(frame==708)CampaignBarnPress("DevelopmentTiers");
        if(frame==716)
        {
            CampaignBarnPress("DevelopmentTier2");CampaignBarnCheck("direct-restart-restores-reference",_barnCampaignFreshHash==_session.CaptureSnapshot().AuthoritativeHash.ToString()&&_session.CaptureStageGenerator(FestivalStages.BarnId)!.Stage==EquipmentStage.Resolved);
            CloseChallengeReveal();BuildStartSplash();
        }
        if(frame==724)CampaignBarnPress("DevelopmentTiers");
        if(frame==732)
        {CampaignBarnPress("DevelopmentTier1");CampaignBarnCheck("tier-one-reset-removes-conversion-and-generator",!_session.BarnStageOpen&&!_barnStageKit!.Visible&&_barnConversionParts.All(n=>!n.Visible)&&_barnGenerator is null&&_barnDrums is null);}
        if(frame==740)
        {
            File.WriteAllText(Path.Combine(_barnCampaignEvidenceOutput!,"checks.json"),JsonSerializer.Serialize(new{passed=true,executable=OS.GetExecutablePath(),checks=_barnCampaignChecks,caveat="Scripted actual campaign/menu/gameplay. Movie advances normal simulation at 80 ticks/s; four-angle views hold an actual naturally rainy live state. No actor, forecast or cash mutations. Council retry is covered by simulation regressions."},new JsonSerializerOptions{WriteIndented=true}));
            File.WriteAllText(Path.Combine(_barnCampaignEvidenceOutput!,"trace.json"),JsonSerializer.Serialize(_barnCampaignTrace,new JsonSerializerOptions{WriteIndented=true}));
            GD.Print("BARN_CAMPAIGN_EVIDENCE_COMPLETE");GetTree().Quit();
        }
    }
}

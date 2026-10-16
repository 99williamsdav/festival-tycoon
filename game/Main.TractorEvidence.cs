using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

// Actual native scene/handlers. Camera and explicit time stepping are the only presentation fixture controls.
public partial class Main
{
    private string? _tractorEvidenceOutput;
    private int _tractorEvidenceStep;
    private ulong _tractorEvidenceNext;
    private string _tractorParkedHash="";
    private void TractorCheck(string name,bool passed)=>ArchitectureCheck("tractor-"+name,passed);
    private void TractorView(int angle,float size=18)
    {while(_rig.OrientationName!=new[]{"South","West","North","East"}[angle])_rig.Rotate(1);_rig.Frame(new(0,2,-23),size);}
    private void TractorHiddenHud(){CloseClassicForecast();CloseChallengeReveal();StartMenu.Close();CampaignBarnHideHud();}
    private void TractorPicture(string name)
    {
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_tractorEvidenceOutput!,name+".png"));
        if(_session.BarnTractorPresent)
        {
            var materials=_barnTractor!.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>()
                .SelectMany(m=>Enumerable.Range(0,m.Mesh!.GetSurfaceCount()).Select(m.GetActiveMaterial)).OfType<StandardMaterial3D>().ToArray();
            var glass=materials.Where(m=>m.ResourceName=="Tractor_Glass").ToArray();
            TractorCheck(name+"-opaque-body",materials.Length==34&&glass.Length>0&&glass.Length<materials.Length&&materials.Where(m=>m.ResourceName!="Tractor_Glass").All(m=>m.AlbedoColor.A==1&&m.Transparency==BaseMaterial3D.TransparencyEnum.Disabled));
            TractorCheck(name+"-retained-cab-glass",glass.Length>0&&glass.All(m=>m.AlbedoColor.A is >0 and <1&&m.Transparency!=BaseMaterial3D.TransparencyEnum.Disabled));
        }
    }
    private void TractorPresence(bool present)
    {
        TractorCheck("declared-current-revision",_session.BarnGeometryVersion==1);
        TractorCheck("mesh-and-authoritative-state-agree",_session.BarnTractorPresent==present&&_barnTractor!.Visible==present);
        TractorCheck("conversion-kit-and-signs",_barnStageKit!.Visible==!present&&_barnConversionParts.All(p=>p.Visible==!present));
        if(_session.TraversalGrid is {} grid)
            TractorCheck("grid-and-mesh-agree",BarnTractorLayout.SolidCells().All(c=>grid.Get(c).IsWalkable!=present));
    }
    private void ProcessTractorEvidence(double delta)
    {
        try
        {
            Directory.CreateDirectory(_tractorEvidenceOutput!);
            ProcessDayCycle(delta);ProcessRain();ProcessPondStage(delta);ProcessBarnStage(delta);AdvancePreparationPresentation(delta);
            if(Time.GetTicksMsec()<_tractorEvidenceNext)return;
            var step=_tractorEvidenceStep++;
            _tractorEvidenceNext=Time.GetTicksMsec()+1000;
            if(step==0)
            {
                SwitchToFestival(GameSession.CreateBuildCampaign(20260922).WithCurrentBarnGeometry());
                TractorHiddenHud();TractorView(0);ArchitectureSave("current-ordinary-preparation");
                var meshes=_barnTractor!.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>().ToArray();
                var bounds=new Aabb();var first=true;var triangles=0;var surfaces=0;
                foreach(var mesh in meshes)
                {
                    var local=(_barnTractor.GlobalTransform.AffineInverse()*mesh.GlobalTransform)*mesh.GetAabb();bounds=first?local:bounds.Merge(local);first=false;
                    var imported=(ArrayMesh)mesh.Mesh!;
                    for(var i=0;i<imported.GetSurfaceCount();i++){var indices=imported.SurfaceGetArrayIndexLen(i);triangles+=(indices>0?indices:imported.SurfaceGetArrayLen(i))/3;surfaces++;}
                }
                TractorCheck("unchanged-import-geometry",meshes.Length==7&&triangles==14962&&surfaces==34&&bounds.Position.DistanceTo(new(-1.127f,0,-1.86f))<.0001f&&bounds.End.DistanceTo(new(1.127f,2.7545f,2.19f))<.0001f);
                TractorCheck("grounded-world-placement",_barnTractor.GlobalPosition.DistanceTo(new(0,0,-20.9f))<.0001f);
                File.WriteAllText(Path.Combine(_tractorEvidenceOutput!,"import.json"),JsonSerializer.Serialize(new{meshes=meshes.Length,triangles,surfaces,min=bounds.Position.ToString(),max=bounds.End.ToString(),world=_barnTractor.GlobalPosition.ToString()}));
            }
            if(step>=1&&step<=4)
            {TractorPresence(true);TractorPicture($"ordinary-solid-{step-1}");if(step<4)TractorView(step);else{SelectObject(LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.large-barn"));TractorView(0);}}
            if(step>=5&&step<=8)
            {TractorCheck("actual-roof-faded",_barnFade!.Alpha<.4f);TractorPicture($"ordinary-faded-{step-5}");if(step<8)TractorView(step-4);else TractorView(0,62);}
            if(step==9){TractorPicture("ordinary-default62-faded");ClearSelection();_tractorEvidenceNext+=1200;}
            if(step==10){TractorCheck("actual-roof-solid",_barnFade!.Alpha>.99f);TractorPicture("ordinary-default62-solid");ArchitectureReady();_tractorParkedHash=_session.CaptureSnapshot().AuthoritativeHash;TractorHiddenHud();TractorView(0,30);}
            if(step==11)
            {TractorPresence(true);ArchitectureSave("current-ordinary-live");ParkCampaignHost();BeginHandsOn(GameSession.CreateHandsOnCampaign(20260922).WithCurrentBarnGeometry());TractorHiddenHud();}
            if(step==12)
            {TractorPresence(true);ArchitectureSave("current-hands-on-preparation");BeginStewardShift(GameSession.CreateStewardShift(20260922,1,currentBarnGeometry:true));TractorHiddenHud();}
            if(step==13)
            {TractorPresence(true);ArchitectureSave("current-steward-live");LeaveHandsOnHost();TractorCheck("parked-host-keeps-exact-state",_session.CaptureSnapshot().AuthoritativeHash==_tractorParkedHash);StartDevelopmentTier(2);TractorHiddenHud();SelectObject(LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.large-barn"));TractorView(0);}
            if(step>=14&&step<=17)
            {TractorPresence(false);TractorPicture($"converted-faded-{step-14}");if(step<17)TractorView(step-13);else{ArchitectureSave("current-converted-preparation");BeginClassicCampaign(GameSession.CreateClassicCampaign(20260922).WithCurrentBarnGeometry());TractorHiddenHud();}}
            if(step==18)
            {TractorPresence(true);ArchitectureSave("current-classic-preparation");ShowClassicDevelopmentPicker();GetNode<CheckBox>(FindChildren("ClassicDevGuarantee","CheckBox",true,false).Single().GetPath()).ButtonPressed=true;GetNode<CheckBox>(FindChildren("ClassicDevCash","CheckBox",true,false).Single().GetPath()).ButtonPressed=true;ArchitecturePress("ClassicDevDefaults");TractorHiddenHud();PreparationStart();_session.AdvanceWithoutSnapshot(11000);_foundationPresentation.Reset(_session.CaptureObservation());TractorView(0,62);}
            if(step==19)
            {TractorPresence(false);TractorCheck("development-save-directory-isolated",_host.SaveDirectory==Path.Combine(_tractorEvidenceOutput!,"development-saves"));TractorCheck("paid-hundred-attendee-three-live-stages",_session.OnSiteAttendeeCount==100&&_session.CapturePreparation()!.People.Length==133&&_session.CaptureLivePerformances().Count(l=>l.Stage==LiveSetStage.Live&&l.Performers.All(p=>p.OnStage))==3);TractorPicture("current-three-stage-133-live");ArchitectureSave("current-three-stage-live");}
            if(step==20)
            {for(var tick=0;tick<65000&&_session.PreparedStatus!=PreparationStatus.Finished;tick+=800)_session.AdvanceWithoutSnapshot(800);TractorCheck("current-three-stage-complete",_session.PreparedStatus==PreparationStatus.Finished&&_session.CapturePreparation()!.People.All(p=>p.Admitted&&p.Departed)&&_session.PerformanceRecords.Count==9);ArchitectureSave("current-three-stage-finished");var next=_session.CreateNextFestival();TractorCheck("next-keeps-revision",next.BarnGeometryVersion==1);BeginClassicCampaign(next);TractorHiddenHud();}
            if(step==21)
            {TractorPresence(false);ArchitectureSave("current-next-festival");BeginClassicCampaign(GameSession.CreateClassicDevelopmentStart(5,true,true).WithCurrentBarnGeometry());TractorHiddenHud();TractorView(0,25);}
            if(step==22)
            {TractorPresence(true);TractorPicture("current-optional-barn-inactive");ArchitectureSave("current-optional-barn-inactive");SwitchToFestival(GameSession.CreateBuildCampaign(20260922));TractorHiddenHud();}
            if(step==23)
            {TractorCheck("legacy-zero-has-no-phantom-tractor",_session.BarnGeometryVersion==0&&!_session.BarnTractorPresent&&!_barnTractor!.Visible);File.WriteAllText(Path.Combine(_tractorEvidenceOutput!,"checks.json"),JsonSerializer.Serialize(new{executable=OS.GetExecutablePath(),checks=_architectureChecks},new JsonSerializerOptions{WriteIndented=true}));GetTree().Quit();}
        }
        catch(Exception e){File.WriteAllText(Path.Combine(_tractorEvidenceOutput!,"failure.txt"),e.ToString());GD.PushError(e.ToString());GetTree().Quit(2);}
    }
}

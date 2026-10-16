using Festival.Simulation;
using Festival.ContentAdapter;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

// Opt-in native evidence. Save root is selected before the first SessionHost is created.
public partial class Main
{
    private string? _foodVanEvidenceOutput;
    private int _foodVanStep, _foodVanLiveVariant, _foodVanLiveTicks;
    private ulong _foodVanNext;
    private bool _foodVanLive, _foodVanOfferSeen;
    private string? _foodVanOfferReceipt;
    private string _foodVanCaption="";
    private Label? _foodVanLabel;
    private readonly System.Collections.Generic.List<object> _foodVanLiveSamples=[];
    private string _foodVanShadowHash="";
    private ulong _foodVanShadowBody;
    private static readonly ImmersionProduct[] FoodVanProducts=[ImmersionProduct.Chips,ImmersionProduct.Pizza,ImmersionProduct.Curry];
    private void FoodCheck(string name,bool passed)=>ServerCheck("food-van-"+name,passed);
    private static string FoodName(ImmersionProduct p)=>p.ToString().ToLowerInvariant();
    private void FoodSend(SessionCommand command)
    {var result=_host.Submit(command);FoodCheck(command.GetType().Name+" "+result.Message,result.IsAccepted);}
    private void FoodHideHud()
    {CloseChallengeReveal();StartMenu.Close();CampaignBarnHideHud();_hudWorkspaceOpen=_buildDrawerOpen=false;}
    private void FoodPicture(string name)=>GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_foodVanEvidenceOutput!,name+".png"));
    private void FoodCaption(string text)
    {_foodVanCaption=text;if(_foodVanLabel is not null)_foodVanLabel.Text="FOOD VAN · NATIVE GAME · "+text;}
    private void FoodFrame(Node3D body,int angle,float size)
    {while(_rig.OrientationName!=new[]{"South","West","North","East"}[angle])_rig.Rotate(1);_rig.Frame(body.GlobalPosition+new Vector3(0,1.2f,0),size);}
    private void FoodSelect(ImmersionProduct product)
    {
        _immersionVendors.TryGetValue(Stalls.FirstVan,out var old);var art=old?.GetMeta("TraderArt", "").AsString();
        var desired=FoodTraders.All.Single(t=>t.Product==product);
        if(_session.TraderBookedElsewhere(Stalls.FirstVan,desired) is {} other)
        {
            var spare=FoodTraders.All.First(t=>t.Id!=desired.Id&&t.FromTier<=2&&_session.TraderBookedElsewhere(other,t) is null);
            FoodSend(new ChooseFoodTraderCommand(spare.Id,other));
        }
        FoodSend(new ChooseFoodTraderCommand(desired.Id));
        SyncImmersionWorld();ProcessServiceServers(0);
        if(old is not null&&art!=_session.TraderAt(Stalls.FirstVan)!.Art)
            FoodCheck("trader-rebuild-removes-old-pick",old.IsQueuedForDeletion()&&!_immersionVendorPicks.ContainsKey(old.GetInstanceId())&&_serverViews[Stalls.FirstVan].Vendor==_immersionVendors[Stalls.FirstVan]);
    }
    private void CheckFoodModel(ImmersionProduct product)
    {
        var body=_immersionVendors[Stalls.FirstVan];
        var assembly=body.GetNode<Node3D>("ImmersionFoodVan/ApprovedFoodVanAssembly");
        var model=assembly.GetNode<Node3D>("ApprovedFoodVanModel");
        var mesh=model.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>().Single();
        var imported=(ArrayMesh)mesh.Mesh;
        var triangles=Enumerable.Range(0,imported.GetSurfaceCount()).Sum(i=>(imported.SurfaceGetArrayIndexLen(i)>0?imported.SurfaceGetArrayIndexLen(i):imported.SurfaceGetArrayLen(i))/3);
        var expected=product==ImmersionProduct.Chips?2370:product==ImmersionProduct.Pizza?2066:5244;
        File.WriteAllText(Path.Combine(_foodVanEvidenceOutput!,FoodName(product)+"-import.json"),JsonSerializer.Serialize(new{triangles,surfaces=imported.GetSurfaceCount(),children=assembly.GetChildren().Select(n=>n.Name.ToString()),position=assembly.Position.ToString(),resource=model.SceneFilePath}));
        var hasMenu=ResourceLoader.Exists($"res://assets/environment/lwf_food_van_menu_board_{_session.TraderAt(Stalls.FirstVan)!.Art}_v1.glb");
        FoodCheck(FoodName(product)+"-exact-model",assembly.Position==new Vector3(-2.15f,0,0)&&triangles==expected&&imported.GetSurfaceCount()==4&&assembly.GetChildCount()==(hasMenu?2:1));
        FoodCheck(FoodName(product)+"-curated-materials",mesh.MaterialOverride is null&&Enumerable.Range(0,4).All(i=>mesh.GetSurfaceOverrideMaterial(i) is null)&&imported.SurfaceGetMaterial(0).ResourceName=="FT2026_ProductionPalette");
        var menu=assembly.GetNodeOrNull<Node3D>("FoodVanChalkMenu");
        FoodCheck(FoodName(product)+"-external-chalk-menu",hasMenu?menu?.Position==new Vector3(4.10f,0,2.30f):menu is null);
        var vents=model.FindChildren(VanSteamName,"",true,false).OfType<CpuParticles3D>().ToArray();
        FoodCheck(FoodName(product)+"-steam-markers",vents.Length==(product==ImmersionProduct.Curry?2:0)&&vents.All(v=>v.Position==Vector3.Zero&&v.GetParent().Name.ToString().StartsWith("LWF_FoodVan_SteamVent_",StringComparison.Ordinal)));
        var bounds=mesh.Transform*mesh.GetAabb();
        FoodCheck(FoodName(product)+"-reservation-fit",bounds.Position.X>=-1.60f&&bounds.End.X<=4.90f&&bounds.Position.Z>=-1.75f&&bounds.End.Z<=1.75f);
        var server=_serverViews[Stalls.FirstVan];
        FoodCheck(FoodName(product)+"-unchanged-server",server.Root.Position==new Vector3(0,.47f,.88f)&&server.Root.Scale==Vector3.One&&Math.Abs(server.Root.Rotation.Y-Mathf.Pi)<.0001f);
        var hash=_session.CaptureSnapshot().AuthoritativeHash;BeginBuildPlacement(BuildServiceKind.FoodVan,Stalls.FirstVan);
        FoodCheck(FoodName(product)+"-real-placement-ghost",_buildGhost?.GetNodeOrNull<Node3D>("ApprovedFoodVanAssembly/ApprovedFoodVanModel") is not null&&_session.CaptureSnapshot().AuthoritativeHash==hash);
        CancelBuildPlacement();FoodHideHud();
    }
    private void FoodPose(ImmersionProduct product,string sex)
    {
        var id=Stalls.FirstVan;var body=_immersionVendors[id];
        if(_serverViews.TryGetValue(id,out var prior)){prior.Root.Hide();prior.Root.QueueFree();}
        var view=CreateServiceServer(id,body,sex);_serverViews[id]=view;
        CheckServerBlends(view,FoodName(product)+"-"+sex,true);
        // Explicitly exercise every imported clip plus .14s transitions, including retract/reset/idle.
        var skeleton=view.Root.FindChildren("*","Skeleton3D",true,false).OfType<Skeleton3D>().Single();
        var feet=new[]{skeleton.FindBone("LeftFoot"),skeleton.FindBone("RightFoot")};
        var baseline=feet.Select(skeleton.GetBoneGlobalPose).Select(t=>t.Origin).ToArray();var maxFoot=0f;
        foreach(var phase in Enum.GetValues<ServerPhase>())
        {
            view.Player.Play(view.Clips[phase],.14);
            for(var i=0;i<=30;i++){view.Player.Advance(view.Lengths[phase]/30);for(var f=0;f<2;f++)maxFoot=Math.Max(maxFoot,skeleton.GetBoneGlobalPose(feet[f]).Origin.DistanceTo(baseline[f]));}
        }
        FoodCheck(FoodName(product)+"-"+sex+"-all-six-clips-and-crossfades",maxFoot<.001f);
        PoseServerEvidence(view,1,product);
    }
    private void FoodPick(int angle)
    {
        var body=_immersionVendors[Stalls.FirstVan];var vendor=_session.CaptureVendors().Single(v=>v.Id==Stalls.FirstVan);
        var service=TraversalGrid.CellCentre(GameSession.ImmersionServiceCell(vendor));
        var expected=body.ToGlobal(new Vector3(0,0,2));
        FoodCheck("service-anchor-turn"+angle,Math.Abs(expected.X-service.XMillimetres/1000f)<.001&&Math.Abs(expected.Z-service.ZMillimetres/1000f)<.001);
        foreach(var local in new[]{new Vector3(0,3.8f,0),new Vector3(-3.60f,1,0)})
        {
            var point=body.ToGlobal(local);
            var query=PhysicsRayQueryParameters3D.Create(point+Vector3.Up*12,point-Vector3.Up*4,1);
            var hit=_rig.Camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
            FoodCheck("physical-pick-turn"+angle+"-"+local,hit.TryGetValue("collider",out var collider)&&collider.AsGodotObject()==body&&_immersionVendorPicks[body.GetInstanceId()]==Stalls.FirstVan);
        }
        FoodCheck("shared-click-resolver-turn"+angle,ResolveWorldHit(_rig.Camera.UnprojectPosition(body.ToGlobal(new Vector3(0,2,0))))==body);
    }
    private void BeginFoodLive(ImmersionProduct product)
    {
        SwitchToFestival(GameSession.CreateDevelopmentFestival(20260922,2).WithCurrentBarnGeometry());
        ArchitectureReady(()=>FoodSelect(product));FoodHideHud();
        FoodCheck("live-save-isolated",_host.SaveDirectory==Path.Combine(_foodVanEvidenceOutput!,"management-saves"));
        FoodSend(new SetPausedCommand(false));_foodVanLiveTicks=0;_foodVanOfferSeen=false;_foodVanOfferReceipt=null;
        ProcessServiceServers(0);FoodFrame(_immersionVendors[Stalls.FirstVan],_session.CaptureVendors().Single(v=>v.Id==Stalls.FirstVan).QuarterTurns,6);
        FoodCaption(FoodName(product)+" · live normal simulation · inspection6");
    }
    private void AdvanceFoodLive()
    {
        var product=FoodVanProducts[_foodVanLiveVariant];var id=Stalls.FirstVan;
        for(var i=0;i<240;i++)
        {
            _session.AdvanceWithoutSnapshot(1);_foodVanLiveTicks++;
            ProcessServiceServers(1d/80);ProcessVanSteam();SyncWorldEffectsPause();
            var view=_serverViews[id];var vendor=_session.CaptureVendors().Single(v=>v.Id==id);
            if(!_foodVanOfferSeen&&view.State.Phase==ServerPhase.Offer&&view.Prop==product)
            {
                _foodVanOfferSeen=true;_foodVanOfferReceipt=view.State.Transaction;
                var nav=_session.CaptureSnapshot().NavigationAgents.Single(a=>a.Id.Value==vendor.OwnerId);
                var counter=GameSession.ImmersionServiceCell(vendor);var centre=TraversalGrid.CellCentre(counter);
                FoodCheck(FoodName(product)+"-real-owner-at-counter",vendor.Queue.Length>0&&vendor.Queue[0]==vendor.OwnerId&&nav.Action==AgentNavigationAction.Arrived&&nav.Destination==counter&&Math.Abs(nav.XMillimetres-centre.XMillimetres)<=10&&Math.Abs(nav.ZMillimetres-centre.ZMillimetres)<=10);
                _foodVanLiveSamples.Add(new{product=FoodName(product),tick=_session.CurrentTick,owner=vendor.OwnerId,queue=vendor.Queue,nav.XMillimetres,nav.ZMillimetres,action=nav.Action.ToString(),serviceCell=counter});
                FoodSend(new SetPausedCommand(true));
                var pausedHash=_session.CaptureSnapshot().AuthoritativeHash;var pose=ServerFingerprint(view);ProcessServiceServers(30);ProcessVanSteam();SyncWorldEffectsPause();
                var steam=view.Vendor.FindChildren(VanSteamName,"",true,false).OfType<CpuParticles3D>().ToArray();
                FoodCheck(FoodName(product)+"-real-pause-freezes",ServerFingerprint(view)==pose&&_session.CaptureSnapshot().AuthoritativeHash==pausedHash&&steam.All(s=>s.SpeedScale==0));
                FoodCheck(FoodName(product)+"-actual-busy-steam",steam.Length==(product==ImmersionProduct.Curry?2:0)&&steam.All(s=>s.Emitting));
                FoodSend(new SetPausedCommand(false));
                var hash=_session.CaptureSnapshot().AuthoritativeHash;ProcessServiceServers(0);
                FoodCheck(FoodName(product)+"-presentation-does-not-transact",_session.CaptureSnapshot().AuthoritativeHash==hash);
                _foundationPresentation.Reset(_session.CaptureObservation());AdvancePreparationPresentation(0);
                FoodCaption(FoodName(product)+" · actual owner / offer · sale not completed");return;
            }
            if(_foodVanOfferSeen&&view.State.Transaction is {} receipt&&receipt!=_foodVanOfferReceipt)
            {
                var sale=_session.CaptureImmersion()!.Purchases.Single(p=>p.Id==receipt);
                FoodCheck(FoodName(product)+"-real-sale-empty-server",sale.Product==product&&Stalls.Of(sale)==id&&view.Prop is null&&view.State.Phase==ServerPhase.Retract);
                var buyer=_session.CaptureImmersion()!.People.Single(p=>p.AgentId==sale.AgentId);
                FoodCheck(FoodName(product)+"-customer-held-receipt",buyer.Held?.TransactionId==receipt&&buyer.Held.Product==product);
                _foundationPresentation.Reset(_session.CaptureObservation());AdvancePreparationPresentation(0);
                FoodCheck(FoodName(product)+"-customer-visible-food",_immersionHeldVisuals.TryGetValue(new(sale.AgentId),out var prop)&&prop.Visible&&_immersionHeldProducts[new(sale.AgentId)]==HeldAsset(product));
                var count=_session.CaptureImmersion()!.Purchases.Length;var hash=_session.CaptureSnapshot().AuthoritativeHash;
                for(var sample=0;sample<12;sample++)ProcessServiceServers(.02);
                FoodCheck(FoodName(product)+"-no-duplicate-transaction",_session.CaptureImmersion()!.Purchases.Length==count&&_session.CaptureSnapshot().AuthoritativeHash==hash);
                ArchitectureSave(FoodName(product)+"-actual-sale");
                FoodSend(new SetPausedCommand(true));FoodCaption(FoodName(product)+" · actual sale / customer-held food");
                _foodVanLive=false;_foodVanStep=40+_foodVanLiveVariant*2;return;
            }
            if(_foodVanLiveTicks>24000)throw new InvalidOperationException("No real food handoff for "+product);
        }
    }
    private void ProcessFoodVanEvidence(double delta)
    {
        try
        {
            Directory.CreateDirectory(_foodVanEvidenceOutput!);
            if(_foodVanLabel is not null)_foodVanLabel.Text="FOOD VAN · NATIVE GAME · "+_foodVanCaption;
            ProcessDayCycle(delta);ProcessRain();ProcessVanSteam();SyncWorldEffectsPause();
            if(_foodVanLive){if(_foodVanOfferSeen)FoodPicture(FoodName(FoodVanProducts[_foodVanLiveVariant])+"-actual-offer");AdvanceFoodLive();return;}
            if(Time.GetTicksMsec()<_foodVanNext)return;
            _foodVanNext=Time.GetTicksMsec()+350;var step=_foodVanStep++;
            if(step==0)
            {
                var perk=_session.CapturePerks()!;FoodSend(new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,perk.Hand[0]));FoodSend(new UseDefaultBuildLayoutCommand());
                SyncBuildWorld();ProcessServiceServers(0);FoodHideHud();
                var canvas=new CanvasLayer{Layer=90};AddChild(canvas);_foodVanLabel=new Label{Position=new(16,16)};canvas.AddChild(_foodVanLabel);
                FoodCheck("initial-host-isolated-before-commands",_host.SaveDirectory==Path.Combine(_foodVanEvidenceOutput!,"management-saves"));
            }
            if(step>=1&&step<=27)
            {
                var index=(step-1)/9;var mode=(step-1)%9;var product=FoodVanProducts[index];
                if(mode==0){FoodSelect(product);CheckFoodModel(product);FoodPose(product,"male");FoodPose(product,"female");}
                else if(mode<=5){FoodPicture(FoodName(product)+"-"+(mode<=4?"angle"+(mode-1)+"-inspection6":"minimum18"));if(mode<=4)FoodPick(mode-1);}
                if(mode<=3)
                {
                    FoodSend(new MoveBuildServiceCommand(Stalls.FirstVan,_session.CaptureVendors().Single(v=>v.Id==Stalls.FirstVan).Cell,mode));SyncImmersionWorld();
                    FoodFrame(_immersionVendors[Stalls.FirstVan],0,6);FoodCaption(FoodName(product)+" · placed quarter-turn "+mode+" · inspection6");
                }
                if(mode==4){FoodFrame(_immersionVendors[Stalls.FirstVan],0,18);FoodCaption(FoodName(product)+" · minimum18");}
                if(mode==5){FoodPicture(FoodName(product)+"-minimum18");FoodFrame(_immersionVendors[Stalls.FirstVan],0,62);FoodCaption(FoodName(product)+" · default62");}
                if(mode==6)
                {
                    FoodPicture(FoodName(product)+"-default62");
                    FoodSend(new MoveBuildServiceCommand(Stalls.FirstVan,_session.CaptureVendors().Single(v=>v.Id==Stalls.FirstVan).Cell,product==ImmersionProduct.Pizza?1:2));SyncImmersionWorld();
                    FoodFrame(_immersionVendors[Stalls.FirstVan],0,6);FoodCaption(FoodName(product)+" · shadow comparison ON · diagnostic inspection6");
                }
                if(mode==7)
                {
                    FoodCheck("normal-directional-shadows-enabled",_sun.ShadowEnabled);FoodPicture(FoodName(product)+"-shadow-on");
                    _foodVanShadowHash=_session.CaptureSnapshot().AuthoritativeHash;_foodVanShadowBody=_immersionVendors[Stalls.FirstVan].GetInstanceId();
                    _sun.ShadowEnabled=false;FoodCaption(FoodName(product)+" · shadows OFF · diagnostic comparison only");
                }
                if(mode==8)
                {
                    FoodPicture(FoodName(product)+"-shadow-off-diagnostic");_sun.ShadowEnabled=true;
                    FoodCheck("shadow-probe-restores-light-with-same-model-and-state",_sun.ShadowEnabled&&_immersionVendors[Stalls.FirstVan].GetInstanceId()==_foodVanShadowBody&&_session.CaptureSnapshot().AuthoritativeHash==_foodVanShadowHash);
                }
            }
            if(step==28){_foodVanLiveVariant=0;BeginFoodLive(FoodVanProducts[0]);_foodVanLive=true;}
            if(step>=40&&step<=44&&step%2==0)
            {
                FoodPicture(FoodName(FoodVanProducts[_foodVanLiveVariant])+"-actual-sale");
                if(++_foodVanLiveVariant<3){BeginFoodLive(FoodVanProducts[_foodVanLiveVariant]);_foodVanLive=true;}
                else
                {
                    FoodCheck("all-server-checks-pass",_serverEvidenceChecks.All(c=>JsonSerializer.SerializeToElement(c).GetProperty("passed").GetBoolean()));
                    File.WriteAllText(Path.Combine(_foodVanEvidenceOutput!,"checks.json"),JsonSerializer.Serialize(new{executable=OS.GetExecutablePath(),checks=_serverEvidenceChecks,architectureChecks=_architectureChecks,liveSamples=_foodVanLiveSamples,caveat="Staged native orientations and imported clip sampling plus actual normal simulation purchases. Inspection6 is enlarged; minimum18/default62 are normal zooms. No performance claim."},new JsonSerializerOptions{WriteIndented=true}));GetTree().Quit();
                }
            }
        }
        catch(Exception e){File.WriteAllText(Path.Combine(_foodVanEvidenceOutput!,"failure.txt"),e.ToString());GD.PushError(e.ToString());GetTree().Quit(2);}
    }
}

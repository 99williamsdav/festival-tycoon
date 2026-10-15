using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Festival.Game;

// Explicit scripted native verification. All files live under the supplied evidence directory.
public partial class Main
{
    private int _classicStressFrame;
    private string? _classicStressProfileKind;
    private Label? _classicStressCaption;
    private string _classicStressOrdinaryHash="";
    private Dictionary<string,string> _classicStressOrdinaryFiles=[];
    private readonly List<object> _classicStressFrames=[];
    private string[] _classicStressAnnounced=[];
    private string _classicStressBeforeEdit="";
    private long _classicStressProfileReady,_classicStressProfileStarted,_classicStressPreviousFrame;
    private long _classicStressProfileStartTick;
    private double _classicStressProfileEngineSeconds;
    private readonly List<(double Frame,double Simulation,double Presentation,double Autosave,double RenderCpu,double RenderGpu)> _classicStressSamples=[];
    private static string StressFileHash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private Dictionary<string,string> StressNormalFiles()=>Directory.Exists(ClassicSaveDirectory)?Directory.GetFiles(ClassicSaveDirectory,"*",SearchOption.AllDirectories)
        .ToDictionary(path=>path,StressFileHash,StringComparer.Ordinal):[];
    private void StressPicture(string name)=>GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_classicStressEvidenceOutput!,name+".png"));
    private void StressCheck(string name,bool passed)=>ArchitectureCheck("stress-"+name,passed);
    private void StressOverview()
    {
        StartMenu.Close();CloseClassicForecast();CloseChallengeReveal();_classicForecastAccepted=true;
        CloseClassicBookingScreen();_classicProgrammeRequested=false;
        _hudWorkspaceOpen=_buildDrawerOpen=_hudProgrammeOpen=false;_boxOfficeSeenAttempt=1;ClearSelection();
        _rig.Frame(new(4,0,0),62);RefreshPreparationHud();
    }
    private void StressRender(double delta)
    {
        ProcessDayCycle(delta);ProcessRain();ProcessGround(delta);ProcessGroundCover();ProcessGateSign(delta);
        ProcessStageSet(delta);ProcessPondStage(delta);ProcessBarnStage(delta);ProcessMoments(delta);ProcessPressPhoto(delta);
        ProcessFieldNotes(delta);ProcessGardenGate(delta);ProcessCows(delta);ProcessLavSucker(delta);ProcessMoods(delta);
        ProcessChatter(delta);ProcessGenerator(delta);ProcessVanSteam();SyncWorldEffectsPause();ProcessStrawMark(delta);
        AdvancePreparationPresentation(delta);ProcessServiceServers(delta);ProcessPond(delta);SeparateSpeech();RefreshContextPanelVisibility();
        AdvanceFinanceFeedback(delta);_urgentAlertDisplay.Advance(delta);RenderUrgentAlerts();
    }
    private void StressNormalReady()
    {
        BeginClassicCampaign(GameSession.CreateClassicCampaign(20260922));CloseClassicForecast();_classicForecastAccepted=true;
        var perk=_session.CapturePerks()!;ArchitectureSend(new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,perk.Hand[0]));
        ArchitectureSend(new SetClassicTicketPriceCommand(500));
        var acts=_session.GetFestivalActs().Where(a=>_session.ActStandingOf(a)==ActStanding.Available).OrderBy(_session.ActFee).ThenBy(a=>a.Id,StringComparer.Ordinal).Take(3).Select(a=>a.Id).ToArray();
        ArchitectureSend(new SetProgrammeCommand(acts));ArchitectureSend(new ConfirmClassicBookingsCommand());ArchitectureSend(new UseDefaultBuildLayoutCommand());
        foreach(var c in _session.GetStaffCandidates().GroupBy(c=>c.Role).Select(g=>g.OrderBy(c=>c.Traits.Length).ThenBy(c=>c.WagePennies).First()))ArchitectureSend(new AcceptPreparationOfferCommand(c.Id));
        ArchitectureSend(new SetPreparationStockCommand(40,32));
    }
    private void ProcessClassicStressEvidence(double delta)
    {
        try
        {
            Directory.CreateDirectory(_classicStressEvidenceOutput!);
            if(_classicStressProfileKind is not null){ProcessClassicStressProfile(delta);return;}
            var frame=_classicStressFrame++;
            if(frame==0)
            {
                _challengeFlowEnabled=true;BeginClassicCampaign(GameSession.CreateClassicCampaign(20260922));CloseClassicForecast();
                StressCheck("ordinary-initial-save",SaveMilestone("Normal Classic isolation baseline"));
                _classicStressOrdinaryHash=_session.CaptureSnapshot().AuthoritativeHash;_classicStressOrdinaryFiles=StressNormalFiles();
                CloseClassicForecast();_classicForecastAccepted=true;
                BuildStartSplash();
            }
            if(frame==8){ArchitecturePress("DevelopmentTiers");}
            if(frame==16){StressPicture("01-development-menu");ArchitecturePress("ClassicDevelopment");}
            if(frame==24)
            {
                StressPicture("02-development-default-options");
                var guarantee=GetNode<CheckBox>(FindChildren("ClassicDevGuarantee","CheckBox",true,false).Single().GetPath());
                var cash=GetNode<CheckBox>(FindChildren("ClassicDevCash","CheckBox",true,false).Single().GetPath());
                StressCheck("diagnostic-options-off",!guarantee.ButtonPressed&&!cash.ButtonPressed);
                guarantee.ButtonPressed=true;cash.ButtonPressed=true;
            }
            if(frame==32){StressPicture("03-explicit-diagnostic-options");ArchitecturePress("ClassicDevDefaults");}
            if(frame==40){StressPicture("04-three-stage-forecast");ArchitecturePress("ForecastContinue");}
            if(frame==48)
            {
                StressCheck("defaults-three-stages-nine-paid-contracts",_session.IsClassicDevelopment&&_session.Stages.Count==3&&_session.CaptureClassicBookingDetails()!.Contracts.Length==9);
                StressCheck("guaranteed-hundred-attendees",_session.CaptureClassicBookingDetails()!.TicketHolders==100);
                StressCheck("test-cash-grant-provenance",_session.CapturePreparation()!.OpeningCashPennies==150000&&_session.CapturePreparation()!.CarriedIn!.CashPennies==26190);
                SelectHudTab("Staff");_hudWorkspaceOpen=true;RefreshPreparationHud();
            }
            if(frame==56)
            {
                StressCheck("three-visible-engineer-slots",_staffSlots["sound"].Card.IsVisibleInTree()&&_staffSlots["extra-sound"].Card.IsVisibleInTree()&&_staffSlots["extra-sound-2"].Card.IsVisibleInTree());
                StressPicture("05-three-engineer-staff");_classicProgrammeRequested=true;RefreshClassicTickets();
            }
            if(frame==64){StressPicture("06-trailer-bookings");StressSelectStage(1);}
            if(frame==72){StressPicture("07-barn-bookings");StressSelectStage(2);}
            if(frame==80)
            {
                StressPicture("08-pond-bookings");ArchitectureSave("development-preparation");
                StressCheck("normal-save-files-unchanged",_classicStressOrdinaryFiles.All(p=>File.Exists(p.Key)&&StressFileHash(p.Key)==p.Value)&&StressNormalFiles().Count==_classicStressOrdinaryFiles.Count);
                StressCheck("normal-host-unchanged",_classicCampaignHost!.Session.CaptureSnapshot().AuthoritativeHash==_classicStressOrdinaryHash);
                StressCheck("save-directory-isolated",_host.SaveDirectory==ClassicDevelopmentSaveDirectory&&_host.SaveDirectory!=ClassicSaveDirectory);
                _classicStressAnnounced=_session.CaptureClassicBookingDetails()!.Contracts.Select(a=>a.ActId).ToArray();
                _classicStressBeforeEdit=_session.CaptureSnapshot().AuthoritativeHash;
            }
            if(frame==88)ArchitecturePress("ClassicRemove3");
            if(frame==90)ArchitecturePress("ClassicKeepContract");
            if(frame==92)
            {
                StressCheck("pond-cancel-restores-local-slot-focus",GetViewport().GuiGetFocusOwner()?.Name=="ClassicSet3");
                ArchitecturePress("ClassicRemove3");
            }
            if(frame==96)
            {
                StressCheck("pond-change-preview-does-not-mutate",_session.CaptureSnapshot().AuthoritativeHash==_classicStressBeforeEdit);
                StressPicture("11-pond-headliner-contract-preview");ArchitecturePress("ClassicApplyContract");
            }
            if(frame==104)
            {
                StressCheck("pond-removal-updates-only-slot-nine",_session.CapturePreparationPlan()!.ActIds[8]==""&&_session.CapturePreparationPlan()!.ActIds.Take(8).SequenceEqual(_classicStressAnnounced.Take(8)));
                StressCheck("nine-slot-refund-real-holders",_session.CaptureClassicBookingDetails()!.RefundedTickets==40&&_session.CapturePreparation()!.People.Count(p=>p.Role==ProtectedPersonRole.Guest)==60);
                ArchitectureSave("pond-contract-refund");StressPicture("12-pond-empty-headliner");
                ArchitectureSend(new RebookClassicProgrammeCommand(_classicStressAnnounced));
            }
            if(frame==112)ConfirmClassicCancellation();
            if(frame==120)
            {
                ArchitecturePress("ClassicApplyContract");
                StressCheck("cancellation-keeps-diagnostic-provenance",_session.IsClassicDevelopment&&_session.Stages.Count==3&&_session.CaptureClassicBookingDetails()!.Phase==ClassicBookingPhase.Booking&&_session.CapturePersistentCampaignState()!.CashPennies==150000);
                ArchitectureSave("development-cancelled");
            }
            if(frame==128)StressSingleStage(2,FestivalStages.BarnId);
            if(frame==144)StressPicture("13-barn-only-own-generator-cut");
            if(frame==152)StressSingleStage(4,FestivalStages.PondId);
            if(frame==168)StressPicture("14-pond-only-own-generator-cut");
            if(frame==176)
            {
                BeginClassicCampaign(GameSession.CreateClassicDevelopmentTestDefaults(true,true));StressOverview();
                foreach(var (kind,count) in new[]{(BuildServiceKind.FoodVan,5),(BuildServiceKind.WaterTap,4),(BuildServiceKind.Toilet,4),
                    (BuildServiceKind.Bar,3),(BuildServiceKind.FirstAid,2),(BuildServiceKind.StewardPost,2),(BuildServiceKind.Marquee,2),(BuildServiceKind.Bin,14)})
                    while(_session.CaptureBuildPlacements().Count(p=>p.Kind==kind)<count)StressPlace(kind);
                SyncBuildWorld();ArchitectureSave("large-construction-layout");OpenBuildCatalogue(BuildServiceKind.FoodVan);
                StressCheck("additional-response-visuals",_additionalPostViews.Count==2&&_session.GetResponseStaff().Count==2);
            }
            if(frame==184)
            {
                StressCheck("camera-controls-clear-readiness",_mapControls is null||!_mapControls.IsVisibleInTree()||Dock.Readiness is null||!Dock.Readiness.GetGlobalRect().Intersects(_mapControls.GetGlobalRect()));
                StressPicture("15-food-traders-page-one");ArchitecturePress("BuildTradersNext");
            }
            if(frame==192){StressPicture("16-food-traders-page-two");OpenBuildCatalogue(BuildServiceKind.Bin);ArchitecturePress("BuildExpandBin");}
            if(frame==200){StressPicture("17-placed-bins-page-one");ArchitecturePress("BuildPlacedNextBin");}
            if(frame==208)
            {
                StressPicture("18-placed-bins-page-two");_buildDrawerOpen=false;RefreshHudWorkspace();_rig.Frame(new(-4,0,3),56);
                StressCheck("all-added-rest-areas-real",_session.CaptureRestCentres().Count==2);
                var extra=_session.CaptureBuildPlacements().Single(p=>p.Kind==BuildServiceKind.FirstAid&&p.Id.Contains(".extra-",StringComparison.Ordinal));
                ArchitectureSend(new RemoveBuildServiceCommand(extra.Id));SyncBuildWorld();
                StressCheck("extra-post-removal-cleans-view-and-rest",_additionalPostViews.Count==1&&_session.CaptureRestCentres().Count==1);
            }
            if(frame==216)StressPicture("19-additional-buildings-world");
            if(frame==224)
            {
                BeginClassicCampaign(GameSession.CreateClassicDevelopmentTestDefaults(true,true));StressOverview();
                StressCheck("explicit-reset-restores-reference",_session.CapturePreparation()!.OpeningCashPennies==150000&&_session.CapturePreparation()!.SeenActs.Length==3&&_session.CaptureClassicHistory()!.CompletedFestivals.Length==1);
                StressCheck("normal-save-still-byte-identical",_classicStressOrdinaryFiles.All(p=>File.Exists(p.Key)&&StressFileHash(p.Key)==p.Value));
            }
            if(frame==240)
            {
                StressOverview();PreparationStart();
                StressCheck("paid-site-and-full-roster",_session.CapturePreparation()!.People.Length==133&&_session.CapturePreparation()!.SetupPayments!.Length==1);
                _session.AdvanceWithoutSnapshot(11000);_foundationPresentation.Reset(_session.CaptureObservation());
                ArchitectureSave("development-live");
                var layer=new CanvasLayer{Layer=95};AddChild(layer);_classicStressCaption=Ui.Text("",17,Ui.BarText,Ui.BodyBold);
                var captionBox=new PanelContainer{Position=Ui.S(16,552),CustomMinimumSize=Ui.S(890,86)};
                captionBox.AddThemeStyleboxOverride("panel",Ui.Box(new Color(Ui.BarDeep,.94f),6,padX:12,padY:8));layer.AddChild(captionBox);captionBox.AddChild(_classicStressCaption);
                StressCheck("all-three-stages-live",_session.CaptureLivePerformances().Count(l=>l.Stage==LiveSetStage.Live&&l.Performers.All(p=>p.OnStage))==3);
            }
            if(frame>=241&&frame<=660)
            {
                if(frame>241)StressPicture($"clip-{frame-242:D4}");
                var clip=frame-241;
                if(clip==140)_rig.Frame(new(0,0,-23),22);
                if(clip==280)_rig.Frame(new(18,0,14),23);
                _session.AdvanceWithoutSnapshot(4);_foundationPresentation.Reset(_session.CaptureObservation());
                var p=_session.CapturePreparation()!;
                _classicStressCaption!.Text=$"SCRIPTED GAMEPLAY · CLASSIC DEVELOPMENT TEST\n100 ticket-holders + 27 performers + 6 staff = 133 people\n{_session.OnSiteAttendeeCount} attendees on site · 3 fixed stages · guaranteed attendance / cash grant enabled";
                _classicStressFrames.Add(new{frame=clip,tick=_session.CurrentTick,attendees=_session.OnSiteAttendeeCount,
                    people=p.People.Length,admitted=p.People.Count(p=>p.Admitted),departed=p.People.Count(p=>p.Departed),
                    sets=_session.CaptureLivePerformances().Select(l=>new{l.StageId,l.Stage,ready=l.Performers.Count(p=>p.OnStage),listeners=l.Listeners.Count(l=>l.AtPlace)})});
                StressRender(.05);return;
            }
            if(frame==661)
            {
                StressPicture("clip-0419");_classicStressCaption!.GetParent<Control>().Hide();
                File.WriteAllText(Path.Combine(_classicStressEvidenceOutput!,"clip-counts.json"),JsonSerializer.Serialize(new{fps=20,frames=_classicStressFrames,fixture="Real paid test preset; fast-forward to tick11000, then four production ticks per frame. No position/need edits."},new JsonSerializerOptions{WriteIndented=true}));
                for(var tick=0;tick<65000&&_session.PreparedStatus!=PreparationStatus.Finished;tick+=800)_session.AdvanceWithoutSnapshot(800);
                StressCheck("full-festival-finished",_session.PreparedStatus==PreparationStatus.Finished&&_session.CapturePreparation()!.People.All(p=>p.Admitted&&p.Departed));
                StressCheck("nine-set-results",_session.PerformanceRecords.Count==9&&_session.CompletedFestivalResult!.AdmittedGuests==100);
                ArchitectureSave("development-finished");RefreshPreparationHud();RefreshFestivalPaper();
            }
            if(frame==669){StressPicture("09-hundred-attendee-results");}
            if(frame==677)
            {
                var cash=_session.CapturePersistentCampaignState()!.CashPennies;ArchitecturePress("NextFestival");
                StressCheck("next-carries-real-cash-without-grant",_session.CapturePreparation()!.OpeningCashPennies==cash&&_session.IsClassicDevelopment&&_session.FestivalNumber==3);
                ArchitectureSave("development-next");
            }
            if(frame==685)
            {
                StressCheck("next-forecast-visible",ClassicForecastOpen&&!ResultsPaper.IsOpen);
                StressPicture("10-next-test-festival");
                File.WriteAllText(Path.Combine(_classicStressEvidenceOutput!,"checks.json"),JsonSerializer.Serialize(new{executable=OS.GetExecutablePath(),checks=_architectureChecks},new JsonSerializerOptions{WriteIndented=true}));
                GetTree().Quit();return;
            }
            StressRender(0);
        }
        catch(Exception e){File.WriteAllText(Path.Combine(_classicStressEvidenceOutput!,"failure.txt"),e.ToString());GD.PushError(e.ToString());GetTree().Quit(2);}
    }
    private void StressSelectStage(int index)
    {
        var picker=FindChildren("ClassicStagePicker","OptionButton",true,false).OfType<OptionButton>().First(p=>p.IsVisibleInTree());
        StressCheck("booking-has-three-stage-choices",picker.ItemCount==3);picker.Select(index);picker.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);
    }
    private void StressPlace(BuildServiceKind kind)
    {
        for(var z=110;z<=186;z+=4)for(var x=72;x<=186;x+=4)for(var turn=0;turn<4;turn++)
            if(_host.Submit(new PlaceBuildServiceCommand(kind,new(x,z),turn)).IsAccepted)return;
        throw new InvalidOperationException("No fitting physical site for extra "+kind);
    }
    private void StressSingleStage(int mask,string stageId)
    {
        BeginClassicCampaign(GameSession.CreateClassicDevelopmentStart(mask,true,true));StressOverview();
        var perk=_session.CapturePerks()!;ArchitectureSend(new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,perk.Hand[0]));
        var acts=_session.GetFestivalActs().Where(a=>_session.ActStandingOf(a)!=ActStanding.Locked).OrderBy(_session.ActFee).Take(3).Select(a=>a.Id).ToArray();
        ArchitectureSend(new SetProgrammeCommand(acts){StageId=stageId});ArchitectureSend(new ConfirmClassicBookingsCommand());ArchitectureSend(new UseDefaultBuildLayoutCommand());
        foreach(var c in _session.GetStaffCandidates().GroupBy(c=>c.Role).Select(g=>g.OrderBy(c=>c.Traits.Length).ThenBy(c=>c.WagePennies).First()))ArchitectureSend(new AcceptPreparationOfferCommand(c.Id));
        StressOverview();PreparationStart();_session.AdvanceWithoutSnapshot(11000);_foundationPresentation.Reset(_session.CaptureObservation());
        var live=_session.CaptureLivePerformance()!;
        StressCheck(stageId+"-only-live-with-own-crew",_session.Stages.Count==1&&live.StageId==stageId&&live.Stage==LiveSetStage.Live&&live.Performers.All(p=>p.OnStage)&&_session.CaptureProgramme() is not null);
        var covered=(bool)typeof(GameSession).GetMethod("UnderCover",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(_session,[live.Performers[0].AgentId])!;
        StressCheck(stageId+"-physical-weather-cover",covered==(stageId==FestivalStages.BarnId));
        ArchitectureSend(new StageGeneratorCommand(stageId,StageGeneratorAction.Isolate));
        StressCheck(stageId+"-own-generator-at-zero",!_session.StagePoweredAt(stageId)&&_session.CapturePower().Stage==0);
        ArchitectureSave(stageId.Replace('.', '-')+"-only-live-cut");_rig.Frame(stageId==FestivalStages.BarnId?new(0,0,-23):new(18,0,14),24);
    }
    private void ProcessClassicStressProfile(double delta)
    {
        if(_classicStressProfileReady==0)
        {
            if(_classicStressProfileKind=="baseline")StressNormalReady();
            else if(_classicStressProfileKind=="stress")BeginClassicCampaign(GameSession.CreateClassicDevelopmentTestDefaults(true,true));
            else throw new ArgumentException("Use baseline or stress for the Classic profile.");
            StressOverview();PreparationStart();_session.AdvanceWithoutSnapshot(8000);_foundationPresentation.Reset(_session.CaptureObservation());_host.Clock.ResetBoundary();
            RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(),true);
            _classicStressProfileReady=_classicStressPreviousFrame=Stopwatch.GetTimestamp();return;
        }
        var now=Stopwatch.GetTimestamp();var frameMs=Stopwatch.GetElapsedTime(_classicStressPreviousFrame,now).TotalMilliseconds;_classicStressPreviousFrame=now;
        var begin=Stopwatch.GetTimestamp();_host.Advance(delta,s=>_foundationPresentation.Advance(s.CaptureObservation()));
        var rendered=Stopwatch.GetTimestamp();StressRender(delta);var saved=Stopwatch.GetTimestamp();_host.AdvanceSaves(delta);var end=Stopwatch.GetTimestamp();
        if(Stopwatch.GetElapsedTime(_classicStressProfileReady,now).TotalSeconds<3)return;
        if(_classicStressProfileStarted==0){_classicStressProfileStarted=now;_classicStressProfileStartTick=_session.CurrentTick;_session.PerformanceTimingsEnabled=true;}
        _classicStressProfileEngineSeconds+=delta;
        _classicStressSamples.Add((frameMs,Stopwatch.GetElapsedTime(begin,rendered).TotalMilliseconds,Stopwatch.GetElapsedTime(rendered,saved).TotalMilliseconds,Stopwatch.GetElapsedTime(saved,end).TotalMilliseconds,
            RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid())+RenderingServer.GetFrameSetupTimeCpu(),RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid())));
        var elapsed=Stopwatch.GetElapsedTime(_classicStressProfileStarted,now).TotalSeconds;if(elapsed<90)return;
        static object Stats(IEnumerable<double> values)
        {var sorted=values.Order().ToArray();double P(double p)=>sorted[Math.Clamp((int)Math.Ceiling(sorted.Length*p)-1,0,sorted.Length-1)];return new{count=sorted.Length,mean=sorted.Average(),p50=P(.5),p95=P(.95),p99=P(.99),max=sorted[^1]};}
        var p=_session.CapturePreparation()!;var cadence=AutosaveRotation.LoadNewestValid(_host.SaveDirectory,_saveCompatibility);
        var result=new{diagnostic="Classic native production-method profile",kind=_classicStressProfileKind,debug=OS.IsDebugBuild(),
            godot=Engine.GetVersionInfo()["string"].AsString(),os=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            cpuCount=System.Environment.ProcessorCount,architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            gpu=RenderingServer.GetVideoAdapterName(),renderer=RenderingServer.GetCurrentRenderingMethod(),resolution=GetWindow().Size.ToString(),
            vsync=DisplayServer.WindowGetVsyncMode().ToString(),camera="Frame (4,0,0), distance62",audioMuted=Audio.Muted,
            attendees=p.People.Count(p=>p.Role==ProtectedPersonRole.Guest),performers=p.People.Count(p=>p.Role==ProtectedPersonRole.Performer),
            staff=p.People.Count(p=>p.Role==ProtectedPersonRole.Staff),people=p.People.Length,stages=_session.Stages.Count,
            wallSeconds=elapsed,engineDeltaSeconds=_classicStressProfileEngineSeconds,startTick=_classicStressProfileStartTick,endTick=_session.CurrentTick,attainedSpeed=(_session.CurrentTick-_classicStressProfileStartTick)/(80*elapsed),
            frameMs=Stats(_classicStressSamples.Select(s=>s.Frame)),simulationObservationMs=Stats(_classicStressSamples.Select(s=>s.Simulation)),
            presentationAudioHudMs=Stats(_classicStressSamples.Select(s=>s.Presentation)),autosaveSchedulingMs=Stats(_classicStressSamples.Select(s=>s.Autosave)),
            viewportRenderCpuMs=Stats(_classicStressSamples.Select(s=>s.RenderCpu)),viewportRenderGpuMs=Stats(_classicStressSamples.Select(s=>s.RenderGpu)),
            simulationWork=_session.CapturePerformanceTimings(),cadenceSaveValid=cadence.IsSuccess,cadenceTick=cadence.Session?.CurrentTick,
            saveError=_host.SaveError,peakWorkingSetBytes=Process.GetCurrentProcess().PeakWorkingSet64,
            limitation="Scripted same-camera native run; normal production simulation, presentation/audio and autosave methods. Input and hover processing omitted. Wall-frame values include engine/OS scheduling; 3-second warmup excluded."};
        File.WriteAllText(Path.Combine(_classicStressEvidenceOutput!,"profile.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        StressPicture("profile-final");StressCheck("profile-autosave-valid",cadence.IsSuccess&&cadence.Session!.CurrentTick>8000&&_host.SaveError is null);
        GetTree().Quit();
    }
}

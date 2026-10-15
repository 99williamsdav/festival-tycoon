using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Festival.Game;

// Opt-in native menu/save/continuation diagnostic. Never reads or writes a player's save namespace.
public partial class Main
{
    private string? _classicEvidenceOutput;
    private int _classicEvidenceFrame;
    private readonly List<object> _classicEvidenceJournal=[];
    private string _classicParkedHash="";
    private ulong _classicOriginalId;
    private void ClassicCheckTopControls()
    {
        var viewport=GetViewport().GetVisibleRect();
        foreach(var name in new[]{"FestivalMenu","FestivalAlerts"})
        {
            var button=FindChildren(name,"Button",true,false).OfType<Button>().First(b=>b.IsVisibleInTree());var rect=button.GetGlobalRect();
            ArchitectureCheck(name+"-fully-inside-classic-viewport",!button.Disabled&&rect.Position.X>=viewport.Position.X&&
                rect.Position.Y>=viewport.Position.Y&&rect.End.X<=viewport.End.X&&rect.End.Y<=viewport.End.Y);
        }
    }
    private void ClassicPicture(string name)=>GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_classicEvidenceOutput!,name+".png"));
    private void ClassicObserve(string name)
    {
        _classicEvidenceJournal.Add(new{name,fixture=name.StartsWith("injected",StringComparison.Ordinal),state=_session.CapturePersistenceSnapshot()});
    }
    private void ClassicReady(bool start=true)
    {
        if(ClassicForecastOpen)ArchitecturePress("ForecastContinue");
        ArchitectureCheck("classic-preparation-and-trailer",_session.IsClassicCampaign&&_session.PreparedStatus==PreparationStatus.Preparing&&
            _session.CurrentFestival.Tier==1&&_session.Stages.Count==1&&_session.CurrentFestival.Weather==_session.CaptureClassicForecast()!.Weather&&!_challengeReveal.IsOpen);
        var perk=_session.CapturePerks()!;
        ArchitectureSend(new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,perk.Hand[0]));
        var acts=_session.GetFestivalActs().Where(a=>_session.ActStandingOf(a)==ActStanding.Available).OrderBy(_session.ActFee)
            .ThenBy(a=>a.Id,StringComparer.Ordinal).Take(3).Select(a=>a.Id).ToArray();
        ArchitectureSend(new SetProgrammeCommand(acts));ArchitectureSend(new ConfirmClassicBookingsCommand());
        ArchitectureSend(new UseDefaultBuildLayoutCommand());ArchitectureSend(new SetPreparationStockCommand(24,16));
        foreach(var person in _session.GetStaffCandidates().GroupBy(c=>c.Role).Select(g=>g.OrderBy(c=>c.Traits.Length>0)
            .ThenBy(c=>c.Grade>0).ThenBy(c=>Math.Abs(c.Grade)).ThenBy(c=>c.Id,StringComparer.Ordinal).First()))
            ArchitectureSend(new AcceptPreparationOfferCommand(person.Id));
        ArchitectureCheck("classic-normal-plan-ready",_session.GetPreparationStartBlockers().Count==0);
        ClassicObserve("normal-festival-"+_session.FestivalNumber+"-bands-paid-site-draft");
        if(!start){RefreshPreparationHud();return;}
        PreparationStart();
        ArchitectureCheck("classic-paid-opening",_session.PreparedStatus==PreparationStatus.Running&&_session.CapturePreparation()!.SetupPayments!.Length==1);
        _session.AdvanceWithoutSnapshot(8_000);ArchitectureSend(new SetPausedCommand(true));
        _foundationPresentation.Reset(_session.CaptureObservation());AdvancePreparationPresentation(0);RefreshPreparationHud();
        ArchitectureCheck("classic-opening-save",SaveMilestone("Classic live checkpoint"));ArchitectureSave("classic-live-"+_session.FestivalNumber);
        ClassicObserve("normal-festival-"+_session.FestivalNumber+"-live");
    }
    private void ClassicFinish(bool injected=false)
    {
        ArchitectureSend(new SetPausedCommand(false));
        for(var ticks=0;ticks<60_000&&_session.PreparedStatus!=PreparationStatus.Finished;ticks+=800)_session.AdvanceWithoutSnapshot(800);
        ArchitectureCheck("classic-festival-finished",_session.PreparedStatus==PreparationStatus.Finished&&_session.CanStartNextFestival);
        ArchitectureCheck("classic-actual-results-save",SaveMilestone("Classic results"));
        ArchitectureSave("classic-finished-"+_session.FestivalNumber);RefreshPreparationHud();RefreshFestivalPaper();
        ClassicObserve((injected?"injected-festival-":"normal-festival-")+_session.FestivalNumber+"-closed");
    }
    private void ClassicNext()
    {
        var prior=_session.CapturePreparation()!;var closed=_session.CapturePersistentCampaignState()!;var number=_session.FestivalNumber;
        var button=FindChildren("NextFestival","Button",true,false).OfType<Button>().First(b=>b.IsVisibleInTree());
        ArchitectureCheck("classic-next-tooltip-no-fixed-tier-advance",button.TooltipText.Contains($"Festival {number+1}:")&&!button.TooltipText.Contains("Barn Stage"));
        button.EmitSignal(Button.SignalName.Pressed);
        ArchitectureCheck("classic-next-canonical-carry",_session.FestivalNumber==number+1&&_session.CampaignId.Value==_classicOriginalId&&
            _session.CapturePersistentCampaignState()!.CashPennies==closed.CashPennies&&_session.CapturePersistentCampaignState()!.Loan==closed.Loan&&
            _session.CapturePreparation()!.Reputation==prior.Reputation&&_session.CapturePreparation()!.SceneCredibility.SequenceEqual(prior.SceneCredibility)&&
            _session.CapturePreparation()!.SeenActs.SequenceEqual(prior.SeenActs)&&_session.CapturePreparation()!.OwnedEquipment.SequenceEqual(prior.OwnedEquipment));
        ArchitectureCheck("classic-day-state-reset",_session.CurrentTick==0&&_session.CaptureLifecycleSnapshot() is null&&
            _session.CapturePreparation()!.AcceptedOffers.Length==0&&_session.CapturePreparation()!.BuildPlacements.Length==0&&
            _session.PerformanceRecords.Count==0&&_session.CaptureClassicHistory()!.DeathPenalties.Length==0);
    }
    private void ClassicInjectDeath()
    {
        ArchitectureSend(new SetPausedCommand(false));
        var victim=_session.CapturePreparation()!.People.First(p=>p.Role==ProtectedPersonRole.Guest&&p.Admitted&&!p.Departed);
        var tick=_session.CurrentTick;var before=_session.CapturePreparation()!.Reputation;
        typeof(GameSession).GetMethod("UpdatePerson",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(_session,
            [victim.AgentId,(Func<Person,Person>)(p=>p with{HealthStage=MedicalStage.Critical,Thirst=10000,Intent=MedicalIntent.Collapsed,
                HealthWarningTick=tick-6000,HealthCollapseTick=tick-_session.IncidentTiming.MedicalDeathDelayTicks,HealthCriticalTick=tick-1})]);
        _session.AdvanceWithoutSnapshot(1);
        ArchitectureCheck("injected-causal-death-continues-once",_session.PreparedStatus==PreparationStatus.Running&&_session.IsClassicDeceased(victim.AgentId)&&
            _session.CapturePreparation()!.Reputation==Math.Max(0,before-10)&&_session.CaptureLifecycleSnapshot()!.Hearings.Count==0);
        GD.Print("CLASSIC_DIAGNOSTIC_INJECTED_OVERDUE_MEDICAL_TRACK no-cash-or-reputation-edit");
    }
    private void ProcessClassicEvidence()
    {
        try
        {
            var frame=_classicEvidenceFrame++;
            if(frame==0){Directory.CreateDirectory(_classicEvidenceOutput!);_challengeFlowEnabled=true;CloseChallengeReveal();BuildStartSplash();}
            if(frame==8)ClassicPicture("01-native-mode-menu");
            if(frame==16)
            {
                ArchitecturePress("ClassicCampaign");_classicOriginalId=_session.CampaignId.Value;
                ArchitectureCheck("classic-menu-identity-own-save-host",_session.Identity==CampaignIdentity.ClassicManagement&&
                    _host.SaveDirectory==Path.Combine(_classicEvidenceOutput!,"classic-saves")&&_campaignHost!.Session.Identity==CampaignIdentity.Management);
            }
            if(frame==24)ClassicPicture("02-classic-box-office");
            if(frame==32)ClassicReady();
            if(frame==40){ClassicCheckTopControls();ClassicPicture("03-first-classic-live");}
            if(frame==42)SaveWarningClick("FestivalMenu");
            if(frame==44){ArchitectureCheck("classic-menu-pointer-opens",_hudMenu!.Visible);ClassicPicture("03b-classic-menu-accessible");}
            if(frame==46)SaveWarningClick("FestivalMenu");
            if(frame==47)ArchitectureCheck("classic-menu-pointer-closes",!_hudMenu!.Visible);
            if(frame==48)ClassicFinish();
            if(frame==56){ClassicCheckResultsPresentation("first-results");ClassicPicture("04-first-results-next-festival");}
            if(frame==64){ClassicNext();ClassicReady();}
            if(frame==72){ClassicFinish();ClassicNext();ClassicReady();}
            if(frame==80){ClassicFinish();ClassicNext();ClassicReady();}
            if(frame==88){ClassicCheckTopControls();ClassicPicture("05-fourth-classic-live");}
            if(frame==96)
            {
                _classicParkedHash=_session.CaptureSnapshot().AuthoritativeHash.ToString();BuildStartSplash();ArchitecturePress("EnterFestival");
                ArchitectureCheck("survival-management-retained-separately",_session.Identity==CampaignIdentity.Management&&_host.SaveDirectory==SaveDirectory);
            }
            if(frame==104)
            {
                BuildStartSplash();ArchitecturePress("ContinueClassic");
                ArchitectureCheck("parked-classic-resumes-exact-state",_session.IsClassicCampaign&&_session.CaptureSnapshot().AuthoritativeHash.ToString()==_classicParkedHash);
                ArchitectureCheck("manual-classic-save",_host.SaveManual().IsSuccess);
                var hash=_session.CaptureSnapshot().AuthoritativeHash;PreparationLoad();
                ArchitectureCheck("actual-manual-load-preserves-classic",_session.CaptureSnapshot().AuthoritativeHash==hash&&_session.FestivalNumber==4);
            }
            if(frame==112)
            {
                ClassicInjectDeath();ClassicInjectDeath();ArchitectureSend(new SetPausedCommand(true));
                _foundationPresentation.Reset(_session.CaptureObservation());AdvancePreparationPresentation(0);RefreshPreparationHud();
                ArchitectureCheck("two-causal-deaths-no-hearing-or-freeze",_session.CaptureClassicHistory()!.DeathPenalties.Length>=2&&_session.PreparedStatus==PreparationStatus.Running);
                ArchitectureSave("injected-classic-deaths-live");ClassicObserve("injected-live-repeated-deaths");
            }
            if(frame==120){ClassicCheckTopControls();ClassicPicture("06-recorded-deaths-live-day-continues");}
            if(frame==122)ShowClassicDeaths();
            if(frame==124)ClassicPicture("06b-recorded-death-details");
            if(frame==126)FindChildren("*","AcceptDialog",true,false).OfType<AcceptDialog>()
                .First(d=>d.Title.StartsWith("Deaths recorded",StringComparison.Ordinal)).EmitSignal(AcceptDialog.SignalName.Confirmed);
            if(frame==128)
            {
                ClassicFinish(true);ArchitectureCheck("deaths-truthful-results",_session.CompletedFestivalResult!.Deaths>=2&&
                    FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.Text.Contains("Deaths Overshadow Festival",StringComparison.Ordinal)));
            }
            if(frame==136)ClassicPicture("07-deaths-reported-in-results");
            if(frame==144)
            {
                var old=_session.CampaignId;StartClassicCampaign();
                ArchitectureCheck("classic-new-campaign-resets-own-state",_session.CampaignId!=old&&_session.FestivalNumber==1&&
                    _session.CaptureClassicHistory()!.CompletedFestivals.Length==0&&_session.CapturePreparation()!.CarriedIn is null&&
                    _session.CapturePersistentCampaignState()!.CashPennies==20000);
                File.WriteAllText(Path.Combine(_classicEvidenceOutput!,"checks.json"),JsonSerializer.Serialize(_architectureChecks,new JsonSerializerOptions{WriteIndented=true}));
                File.WriteAllText(Path.Combine(_classicEvidenceOutput!,"journal.json"),JsonSerializer.Serialize(_classicEvidenceJournal,new JsonSerializerOptions{WriteIndented=true}));
                GD.Print("CLASSIC_NATIVE_PASS checks="+_architectureChecks.Count);GetTree().Quit();
            }
        }
        catch(Exception error)
        {
            Directory.CreateDirectory(_classicEvidenceOutput!);File.WriteAllText(Path.Combine(_classicEvidenceOutput!,"failure.txt"),error.ToString());
            File.WriteAllText(Path.Combine(_classicEvidenceOutput!,"checks.json"),JsonSerializer.Serialize(_architectureChecks));
            File.WriteAllText(Path.Combine(_classicEvidenceOutput!,"failure-state.json"),JsonSerializer.Serialize(_session.CapturePersistenceSnapshot()));
            GD.PushError(error.ToString());GetTree().Quit(2);
        }
    }
}

using Festival.Presentation;
using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

public partial class Main
{
    private string? _weatherEvidenceOutput;
    private int _weatherEvidenceFrame;
    private string _forecastUnchanged="";
    private readonly List<object> _weatherJournal=[];
    private static ulong WeatherEvidenceSeed(ClassicWeatherKind kind)=>Enumerable.Range(1,10_000).Select(i=>(ulong)i)
        .First(seed=>GameSession.ClassicWeatherForecast(seed,1,38_400).Forecast.Conditions==kind);
    private void WeatherPicture(string name)=>GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_weatherEvidenceOutput!,name+".png"));
    private void ProcessWeatherEvidence()
    {
        try
        {
            var frame=_weatherEvidenceFrame++;var index=frame/48;var step=frame%48;
            if(index>=6)
            {
                Directory.CreateDirectory(_weatherEvidenceOutput!);
                File.WriteAllText(Path.Combine(_weatherEvidenceOutput!,"evidence.json"),JsonSerializer.Serialize(new{executable=OS.GetExecutablePath(),checks=_architectureChecks,journal=_weatherJournal,
                    caveat="Deterministic known-seed forecast fixtures; normal paid commands and real pointer hit-testing; no manual balance/performance claim."},new JsonSerializerOptions{WriteIndented=true}));
                GD.Print("CLASSIC_WEATHER_NATIVE_PASS checks="+_architectureChecks.Count);GetTree().Quit();return;
            }
            var kind=(ClassicWeatherKind)index;var prefix=$"{index+1:00}-{kind}";
            if(step==0)
            {
                Directory.CreateDirectory(_weatherEvidenceOutput!);_challengeFlowEnabled=true;
                BeginClassicCampaign(GameSession.CreateClassicCampaign(WeatherEvidenceSeed(kind)));_classicOriginalId=_session.CampaignId.Value;
                _forecastUnchanged=_session.CaptureSnapshot().AuthoritativeHash.ToString();
                ArchitectureCheck("forecast-before-perk-"+kind,ClassicForecastOpen&&!_challengeReveal.IsOpen&&_session.CapturePerks()!.Pending&&_session.CurrentTick==0);
                _weatherJournal.Add(new{name=prefix+"-forecast",state=_session.CapturePersistenceSnapshot()});
            }
                        if(step==4)
            {
                File.WriteAllText(Path.Combine(_weatherEvidenceOutput!,prefix+"-geometry.json"),JsonSerializer.Serialize(new{
                    layerVisible=_classicForecastLayer?.Visible,
                    controls=_classicForecastLayer?.GetChildren().OfType<Control>().Select(c=>new{name=c.Name.ToString(),size=c.Size.ToString(),scale=c.Scale.ToString(),children=c.GetChildren().OfType<Control>().Select(child=>new{name=child.Name.ToString(),size=child.Size.ToString(),scale=child.Scale.ToString()})}),
                    buttons=FindChildren("Forecast*","Button",true,false).OfType<Button>().Select(b=>new{name=b.Name.ToString(),rect=b.GetGlobalRect().ToString()})
                }));
                ArchitectureCheck("forecast-visible-sized-root-"+kind,_classicForecastLayer!.Visible&&_classicForecastLayer.GetChildren().OfType<Control>().First().Size==GetViewport().GetVisibleRect().Size);
                ArchitectureCheck("forecast-real-button-rect-"+kind,FindChildren("ForecastContinue","Button",true,false).OfType<Button>().Single(b=>b.IsVisibleInTree()).GetGlobalRect().Size.X>200);
                var details=FindChildren("ForecastWetWindows","RichTextLabel",true,false).OfType<RichTextLabel>().Single(l=>l.IsVisibleInTree());
                File.WriteAllText(Path.Combine(_weatherEvidenceOutput!,prefix+"-wet-label.json"),JsonSerializer.Serialize(new{contentHeight=details.GetContentHeight(),size=details.Size.ToString(),rect=details.GetGlobalRect().ToString(),text=details.Text}));
                ArchitectureCheck("all-wet-window-labels-fit-"+kind,details.GetContentHeight()<=details.Size.Y&&details.GetGlobalRect().End.X<GetViewport().GetVisibleRect().End.X);
                WeatherPicture(prefix+"-forecast");SaveWarningClick("ForecastMenu");
            }
            if(step==6)
            {
                ArchitectureCheck("forecast-back-menu-no-mutation-"+kind,StartMenu.IsOpen&&!ClassicForecastOpen&&_session.CaptureSnapshot().AuthoritativeHash.ToString()==_forecastUnchanged);
                ArchitecturePress("ContinueClassic");
            }
            if(step==8)
            {
                ArchitectureCheck("forecast-reopens-exact-"+kind,ClassicForecastOpen&&_session.CaptureSnapshot().AuthoritativeHash.ToString()==_forecastUnchanged);
                SaveWarningClick("ForecastContinue");
            }
            if(step==10)ArchitectureCheck("forecast-continue-to-existing-perk-"+kind,!ClassicForecastOpen&&Perks.Panel?.Visible==true&&_session.CurrentTick==0);
            if(step==12)
            {
                ClassicReady(false);
                ArchitectureCheck("booking-retains-known-forecast-"+kind,_session.CaptureClassicBookingDetails()!.Phase==ClassicBookingPhase.SitePreparation&&
                    _session.CaptureClassicForecast()!.Conditions==kind&&_session.CurrentFestival.Weather==_session.CaptureClassicForecast()!.Weather);
                if(_session.IsRainyFestival)
                {
                    ArchitectureSend(new SetPreparationStockCommand(24,16){Ponchos=6,StrawBales=2});
                    ArchitectureSend(new GroundCoverCommand(GroundCoverAction.Matting,new(110,154),new(116,154)));
                }
                _forecastUnchanged=_session.CaptureSnapshot().AuthoritativeHash.ToString();
                if(_classicBookingScreen?.Visible==true)SaveWarningClick("ClassicBookingBack");
                if(BoxOfficeOpen)SaveWarningClick("StartPlanning");
            }
            if(step==14)SaveWarningClick("FestivalMenu");
            if(step==18){WeatherPicture(prefix+"-menu-route");SaveWarningClick("ReopenForecast");}
            if(step==20)
            {
                File.WriteAllText(Path.Combine(_weatherEvidenceOutput!,prefix+"-reopen.json"),JsonSerializer.Serialize(new{open=ClassicForecastOpen,same=_session.CaptureSnapshot().AuthoritativeHash.ToString()==_forecastUnchanged,menuVisible=_hudMenu?.Visible,boxOffice=BoxOfficeOpen}));
                ArchitectureCheck("draft-reopen-does-not-reroll-or-reset-"+kind,ClassicForecastOpen&&_session.CaptureSnapshot().AuthoritativeHash.ToString()==_forecastUnchanged);
                ArchitectureSave("forecast-draft-"+kind);
                SaveWarningClick("ForecastContinue");
            }
            if(step==22)
            {
                PreparationSave();var hash=_session.CaptureSnapshot().AuthoritativeHash;PreparationLoad();
                ArchitectureCheck("draft-manual-save-resume-"+kind,_session.CaptureSnapshot().AuthoritativeHash==hash);
                PreparationStart();ArchitectureCheck("forecast-paid-start-"+kind,_session.PreparedStatus==PreparationStatus.Running);
                var rain=_session.CaptureWeather()!.Spells;
                var target=rain.Length==0?8000:rain[0].StartTick+(rain[0].EndTick-rain[0].StartTick)/2;
                _session.AdvanceWithoutSnapshot((int)target);
                _foundationPresentation.Reset(_session.CaptureObservation());AdvancePreparationPresentation(0);RefreshPreparationHud();
                ArchitectureCheck("forecast-matches-live-rain-"+kind,_session.RainNow==_session.RainAt(target));
                ArchitectureCheck("forecast-live-temperature-"+kind,FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.IsVisibleInTree()&&l.Text==$"{_session.TemperatureAt(target)}°C"));
                ArchitectureCheck("forecast-existing-heat-"+kind,_session.CaptureMedical()!.IsHot==(kind==ClassicWeatherKind.Heatwave));
                ClassicCheckTopControls();ArchitectureSave("forecast-live-"+kind);
                _weatherJournal.Add(new{name=prefix+"-live",state=_session.CapturePersistenceSnapshot()});
            }
            if(step>=22&&step<30){ProcessDayCycle(0);ProcessRain();ProcessGround(0);}
            if(step==28)
            {
                ArchitectureCheck("live-rain-rig-matches-dry-or-wet-"+kind,_rainRig!.Visible==(_session.RainNow!=RainIntensity.Dry));
                WeatherPicture(prefix+"-live");
            }
            if(step==32&&index==3)
            {
                ClassicFinish();
                ArchitectureCheck("ordinary-shower-results-not-heavy-rain",FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.IsVisibleInTree()&&l.Text.Contains("shower"))&&
                    !FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.IsVisibleInTree()&&l.Text.Contains("heavy shower")));
            }
            if(step==36&&index==3)WeatherPicture("10-ordinary-showers-reported");
            if(step==32&&index==0)
            {
                ClassicFinish();ClassicNext();
                ArchitectureCheck("natural-next-opens-forecast-with-keyed-data",ClassicForecastOpen&&_session.FestivalNumber==2&&
                    _session.CaptureWeather()!.Spells.SequenceEqual(GameSession.ClassicWeatherForecast(_session.CampaignSeed,2,_session.PreparedEditionDurationTicks).Rain));
                _weatherJournal.Add(new{name="natural-next-forecast",state=_session.CapturePersistenceSnapshot()});
            }
            if(step==36&&index==0)WeatherPicture("07-natural-next-forecast");
            if(step==38&&index==0)
            {
                var before=_session.CaptureSnapshot().AuthoritativeHash;
                CloseClassicForecast();FestivalTimePresentation.Mapping=new(3600);_classicForecastAccepted=false;RefreshClassicForecast();
                ArchitectureCheck("shared-mapping-forecast-runtime-labels",FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.IsVisibleInTree()&&l.Text=="60:00"));
                ArchitectureCheck("shared-mapping-no-simulation-change",_session.CaptureSnapshot().AuthoritativeHash==before&&FestivalTimePresentation.Duration(38400)=="08:00");
            }
            if(step==40&&index==0)
            {
                WeatherPicture("08-diagnostic-shared-mapping-only");ContinueClassicForecast();ClassicReady(false);
                if(BoxOfficeOpen)ArchitecturePress("StartPlanning");
                SelectHudTab("Programme");RefreshPreparationHud();
                ArchitectureCheck("shared-mapping-classic-programme",_classicBookingScreen?.Visible==true&&
                    _classicBookingScreen.FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.IsVisibleInTree()&&l.Text.Contains("61:00–62:45")));
            }
            if(step==42&&index==0)
            {
                PreparationStart();_session.AdvanceWithoutSnapshot(8000);
                _foundationPresentation.Reset(_session.CaptureObservation());AdvancePreparationPresentation(0);RefreshPreparationHud();
                ArchitectureCheck("shared-mapping-live-clock-duration-separate",FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.IsVisibleInTree()&&l.Text=="61:40 / 08:00"));
                ArchitectureCheck("shared-mapping-world-band-next",_liveSetCue!.Text.Contains("NEXT · "+FestivalClockText(_session.UpcomingFestivalTick-_session.CapturePreparation()!.StartedTick)));
                ClassicInjectDeath();_weatherJournal.Add(new{name="injected-overdue-medical-shared-mapping-only",state=_session.CapturePersistenceSnapshot()});
            }
            if(step==44&&index==0)
            {
                ClassicFinish(true);
                var projected=FestivalEvidenceText(_session,_session.CaptureClassicHistory()!.DeathPenalties[^1].Casualty.Cause);
                ArchitectureCheck("shared-mapping-casualty-newspaper-text",FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.IsVisibleInTree()&&l.Text.Contains(projected)));
            }
            if(step==47&&index==0){WeatherPicture("09-diagnostic-mapping-results");FestivalTimePresentation.Mapping=new();}
            if(step==46&&index==0)
            {
                ArchitecturePress("PerformancesTab");
                ArchitectureCheck("shared-mapping-results-timestamps",FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.IsVisibleInTree()&&l.Text.Contains("61:00–62:45")));

            }
        }
        catch(Exception error)
        {
            FestivalTimePresentation.Mapping=new();Directory.CreateDirectory(_weatherEvidenceOutput!);
            File.WriteAllText(Path.Combine(_weatherEvidenceOutput!,"failure.txt"),error.ToString());
            File.WriteAllText(Path.Combine(_weatherEvidenceOutput!,"checks.json"),JsonSerializer.Serialize(_architectureChecks));
            GD.PushError(error.ToString());GetTree().Quit(2);
        }
    }
}

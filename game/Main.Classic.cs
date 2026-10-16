using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.Linq;
using CryptographicRandom=System.Security.Cryptography.RandomNumberGenerator;

namespace Festival.Game;

public partial class Main
{
    private SessionHost? _classicCampaignHost;
    private SessionHost? _classicDevelopmentHost;
    private string ClassicSaveDirectory=>_architectureEvidenceOutput is not null
        ?System.IO.Path.Combine(_architectureEvidenceOutput,"classic-saves"):ProjectSettings.GlobalizePath("user://saves/r0-classic-v101");
    private GameSession? SavedClassicCampaign()=>_classicCampaignHost?.Session??
        (AutosaveRotation.LoadNewestValid(ClassicSaveDirectory,_saveCompatibility).Session is{IsClassicCampaign:true,IsClassicDevelopment:false} saved?saved:null);

    private void PickClassicCampaign()
    {
        if(SavedClassicCampaign() is not{} saved){StartClassicCampaign();return;}
        var dialog=new ConfirmationDialog{Title="Start a new Classic campaign?",DialogText="Your Classic autosaves will be replaced. Continue your existing campaign or start fresh.",
            OkButtonText="Start new",CancelButtonText="Cancel",Theme=HudTheme()};
        AddChild(dialog);
        dialog.AddButton("Continue",false,"continue");
        dialog.Confirmed+=()=>{dialog.QueueFree();StartClassicCampaign();};
        dialog.Canceled+=()=>dialog.QueueFree();
        dialog.CustomAction+=action=>{if(action=="continue"){dialog.QueueFree();BeginClassicCampaign(saved);}};
        dialog.PopupCentered(new Vector2I(480,180));
    }
    private void StartClassicCampaign()
    {
        if(_host is not null)_menuCampaignIds.Add(_session.CampaignId.Value);
        ulong seed;
        do seed=BitConverter.ToUInt64(CryptographicRandom.GetBytes(sizeof(ulong)));
        while(seed==0||_menuCampaignIds.Contains(seed));
        _menuCampaignIds.Add(seed);
        BeginClassicCampaign(GameSession.CreateClassicCampaign(seed).WithCurrentBarnGeometry());
    }
    private void BeginClassicCampaign(GameSession session)
    {
        LeaveHandsOnHost();ParkCampaignHost();
        if(session.IsClassicDevelopment)
        {_classicDevelopmentHost??=new SessionHost(session,ClassicDevelopmentSaveDirectory,_saveCompatibility);_host=_classicDevelopmentHost;}
        else{_classicCampaignHost??=new SessionHost(session,ClassicSaveDirectory,_saveCompatibility);_host=_classicCampaignHost;}
        SwitchToFestival(session);StartMenu.Close();RefreshChallengeReveal();
        _preparationMessage=$"Classic{(_session.IsClassicDevelopment?" DEVELOPMENT TEST":"")} · Festival {_session.FestivalNumber}. {_session.CaptureClassicForecast()!.Summary} Choose three acts per open stage and hire your crew.";
        RefreshPreparationHud();
        GD.Print($"CLASSIC_CAMPAIGN_OPENED id={_session.CampaignId.Value} seed={_session.CampaignSeed} festival={_session.FestivalNumber} status={_session.PreparedStatus}");
    }
    private void ShowClassicDeaths()
    {
        var dialog=new AcceptDialog{Title=$"Deaths recorded · Festival {_session.FestivalNumber}",Theme=HudTheme()};
        dialog.Theme.SetStylebox("panel","AcceptDialog",HudStyle(HudPaper,16));
        var text=new RichTextLabel{CustomMinimumSize=new Vector2(600,240),BbcodeEnabled=false,ScrollActive=true,
            Text=string.Join("\n\n",_session.CaptureClassicHistory()!.DeathPenalties.Select(d=>FestivalClockText(d.Casualty.Tick-(_session.CapturePreparation()?.StartedTick??0))+" · "+FestivalEvidenceText(_session,d.Casualty.Cause)))+
                "\n\nEach death reduces reputation by 10, stopping at zero. No fine. The festival continues."};
        text.AddThemeColorOverride("default_color",HudInk);
        text.AddThemeFontOverride("normal_font",Ui.Body);
        text.AddThemeFontSizeOverride("normal_font_size",14);
        dialog.AddChild(text);
        AddChild(dialog);dialog.Confirmed+=()=>dialog.QueueFree();dialog.Canceled+=()=>dialog.QueueFree();
        dialog.PopupCentered(new Vector2I(640,320));
    }
}

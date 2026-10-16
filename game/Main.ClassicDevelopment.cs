using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private CanvasLayer? _classicDevelopmentPicker;
    private string? _classicStressEvidenceOutput;
    private string ClassicDevelopmentSaveDirectory=>_classicStressEvidenceOutput is {} output?System.IO.Path.Combine(output,"development-saves"):
        ProjectSettings.GlobalizePath("user://saves/r0-classic-development-v1");
    private GameSession? SavedClassicDevelopment()=>_classicDevelopmentHost?.Session??
        (AutosaveRotation.LoadNewestValid(ClassicDevelopmentSaveDirectory,_saveCompatibility).Session is{IsClassicDevelopment:true} saved?saved:null);
    private void ShowClassicDevelopmentPicker()
    {
        _classicDevelopmentPicker?.QueueFree();
        _classicDevelopmentPicker=new CanvasLayer{Layer=30,Name="ClassicDevelopmentPicker"};AddChild(_classicDevelopmentPicker);
        var background=new ColorRect{Color=new Color("142630")};_classicDevelopmentPicker.AddChild(background);background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var sheet=new PanelContainer{Position=(GetViewport().GetVisibleRect().Size-Ui.S(760,540))/2,Size=Ui.S(760,540),Theme=HudTheme()};
        sheet.AddThemeStyleboxOverride("panel",Ui.Sheet(24,22));_classicDevelopmentPicker.AddChild(sheet);
        var box=new VBoxContainer();box.AddThemeConstantOverride("separation",Ui.Px(12));sheet.AddChild(box);
        box.AddChild(Ui.Heading("Classic · development test",30));
        var explanation=Ui.Text("Synthetic 40/100 reputation milestone · 100-attendee licence\nRecorded normal clear: two newspaper stars, £261.90 cash, 14 earned reputation, three learned acts and their actual relationships.\nThe milestone and venue access are diagnostic. Saves stay in a separate test campaign.",16,Ui.InkMuted);
        explanation.AutowrapMode=TextServer.AutowrapMode.WordSmart;box.AddChild(explanation);
        box.AddChild(Ui.Heading("Open stages",21));var stages=new HBoxContainer();box.AddChild(stages);
        var choices=FestivalStages.All.Select((stage,index)=>new CheckBox{Name="ClassicDevStage"+index,Text=StageTitle(stage),ButtonPressed=index==0}).ToArray();
        foreach(var choice in choices)stages.AddChild(choice);
        var guarantee=new CheckBox{Name="ClassicDevGuarantee",Text="Guarantee 100 ticket-holders"};box.AddChild(guarantee);
        var cash=new CheckBox{Name="ClassicDevCash",Text="Top up test cash to £1,500"};box.AddChild(cash);
        var note=Ui.Text("Both options start off. Normal ticket demand still applies unless guaranteed.\nUse defaults opens all three stages, books the bands and plans the crew/site/stock. Ordinary site costs are paid at Start.",14,Ui.InkMuted);
        note.AutowrapMode=TextServer.AutowrapMode.WordSmart;box.AddChild(note);
        var buttons=new HBoxContainer();box.AddChild(buttons);
        void Begin(bool defaults)
        {
            var mask=choices.Select((choice,index)=>choice.ButtonPressed?1<<index:0).Sum();
            var session=defaults?GameSession.CreateClassicDevelopmentTestDefaults(guarantee.ButtonPressed,cash.ButtonPressed):
                GameSession.CreateClassicDevelopmentStart(mask,guarantee.ButtonPressed,cash.ButtonPressed);
            _classicDevelopmentPicker?.QueueFree();_classicDevelopmentPicker=null;
            BeginClassicCampaign(session.WithCurrentBarnGeometry());SaveMilestone("Classic development start");
        }
        var start=Ui.Style(new Button{Name="ClassicDevStart",Text="Start with these choices"},Ui.ButtonKind.Primary,16);buttons.AddChild(start);start.Pressed+=()=>Begin(false);
        var defaultsButton=Ui.Style(new Button{Name="ClassicDevDefaults",Text="Use defaults for test · all 3 stages"},Ui.ButtonKind.Secondary,16);buttons.AddChild(defaultsButton);defaultsButton.Pressed+=()=>Begin(true);
        foreach(var choice in choices)choice.Toggled+=_=>start.Disabled=!choices.Any(c=>c.ButtonPressed);
        var footer=new HBoxContainer();box.AddChild(footer);
        if(SavedClassicDevelopment() is {} saved)
        {
            var resume=Ui.Style(new Button{Name="ClassicDevContinue",Text="Continue development test"},Ui.ButtonKind.Secondary,16);footer.AddChild(resume);
            resume.Pressed+=()=>{_classicDevelopmentPicker?.QueueFree();_classicDevelopmentPicker=null;BeginClassicCampaign(saved);};
        }
        var back=Ui.Style(new Button{Name="ClassicDevBack",Text="Back"},Ui.ButtonKind.Quiet,16);footer.AddChild(back);
        back.Pressed+=()=>{_classicDevelopmentPicker?.QueueFree();_classicDevelopmentPicker=null;};
        start.GrabFocus();
    }
}

using Festival.Presentation;
using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

/// <summary>Approved forecast composition. Only the decorative mosaic is raster artwork; labels are native runtime text.</summary>
internal partial class ClassicForecastScreen : Control
{
    private GameSession _session=null!;
    private readonly Control _canvas=new(){MouseFilter=MouseFilterEnum.Stop,ClipContents=true};
    private Button _continue=null!,_menu=null!;
    private ClassicForecast _forecast=null!;
    private WeatherSpell[] _spells=[];
    private long _day;
    internal static string WeatherArtPath(ClassicWeatherKind kind) => kind switch
    {
        ClassicWeatherKind.Pleasant => "res://assets/ui/forecast/pleasant-fine.png",
        ClassicWeatherKind.Mild => "res://assets/ui/forecast/mild.png",
        ClassicWeatherKind.Cloudy => "res://assets/ui/forecast/cloudy.png",
        ClassicWeatherKind.Showery => "res://assets/ui/forecast/showery-mixed.png",
        ClassicWeatherKind.Heatwave => "res://assets/ui/forecast/heatwave-forecast-v2.png",
        ClassicWeatherKind.HeavyRain => "res://assets/ui/forecast/heavy-rain-forecast-v3.png",
        _ => "res://assets/ui/forecast/weather-mosaic.png"
    };
    public void Build(GameSession session,Action proceed,Action menu)
    {
        _session=session;_forecast=session.CaptureClassicForecast()!;_spells=session.CaptureWeather()!.Spells;_day=session.PreparedEditionDurationTicks;
        MouseFilter=MouseFilterEnum.Stop;SetAnchorsPreset(LayoutPreset.TopLeft);Size=GetViewport().GetVisibleRect().Size;
        var background=new ColorRect{Color=Ui.BarDeep,MouseFilter=MouseFilterEnum.Stop};background.SetAnchorsPreset(LayoutPreset.FullRect);AddChild(background);
        AddChild(_canvas);_canvas.Size=new(1280,720);
        Text($"CLASSIC{(session.IsClassicDevelopment?" DEVELOPMENT TEST":"")} · LOWER WITTERING FARM",52,39,800,25,16,Ui.BarMuted);
        Text($"Festival {session.FestivalNumber} forecast",52,70,900,55,40,Ui.BarText,Ui.Slab);
        Text(_spells.Any(r=>r.Intensity==RainIntensity.Downpour)?"Heavy showers, with a downpour.":_forecast.Summary,52,124,920,35,24,Ui.BarText);
        Text($"{_forecast.Temperatures.Min(p=>p.Celsius)}–{_forecast.Temperatures.Max(p=>p.Celsius)}°C · "+(_spells.Length==0?"Dry throughout":_forecast.Conditions==ClassicWeatherKind.Showery?"Mostly dry":"Wet spells with dry breaks"),52,163,900,28,18,Ui.BarMuted);
        var art=new TextureRect{Name="ForecastWeatherArt",ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,Texture=GD.Load<Texture2D>(WeatherArtPath(_forecast.Conditions)),Position=new(1012,37),Size=new(216,144),MouseFilter=MouseFilterEnum.Ignore};_canvas.AddChild(art);
        var chart=new ForecastPlot{Position=new(0,0),Size=new(1280,720),MouseFilter=MouseFilterEnum.Ignore};_canvas.AddChild(chart);chart.Configure(session);
        Text("Your festival day",79,229,500,34,26,Ui.Ink,Ui.Slab);
        Text("Festival clock · "+FestivalTimePresentation.Range(0,_day),820,233,381,28,16,Ui.InkMuted,align:HorizontalAlignment.Right);
        Text("Programme",79,session.Stages.Count>1?250:283,170,26,18,Ui.Ink,Ui.BodySemi);
        Text("Temperature",79,378,170,26,18,Ui.Ink,Ui.BodySemi);Text("°C",79,405,170,23,16,Ui.InkMuted);
        Text("Rainfall",79,470,170,26,18,Ui.Ink,Ui.BodySemi);Text("Intensity",79,496,170,24,16,Ui.InkMuted);
        float X(long tick)=>270+914f*tick/_day;
        var stage=session.Stages[0];
        if(session.Stages.Count>1)
        {
            foreach(var (active,row) in session.Stages.Select((active,row)=>(active,row)))
            {
                Text(Main.StageTitle(active),79,281+row*20,170,20,14,Ui.Ink);
                foreach(var(start,slot) in active.SlotStarts.Select((start,slot)=>(start,slot)))
                    Text($"Set {slot+1}",X(start),281+row*20,X(active.SlotEnds[slot])-X(start),20,14,Ui.Ink,align:HorizontalAlignment.Center);
            }
        }
        else
        {
            Text("Arrivals",270,281,X(stage.SlotStarts[0])-270,29,18,Ui.Ink,Ui.BodySemi,HorizontalAlignment.Center);
            foreach(var (start,i) in stage.SlotStarts.Select((start,i)=>(start,i)))
            {
                var x=X(start);var width=X(stage.SlotEnds[i])-x;
                Text($"Set {i+1}",x,281,width,29,18,Ui.Ink,Ui.BodySemi,HorizontalAlignment.Center);
                Text(FestivalTimePresentation.Range(start,stage.SlotEnds[i]),x-12,316,width+24,25,16,Ui.InkMuted,align:HorizontalAlignment.Center);
            }
        }
        for(var i=0;i<5;i++)
        {
            var point=_forecast.Temperatures[i];var x=X(point.Tick);var y=ForecastPlot.Y(point.Celsius,_forecast);
            Text(point.Celsius+"°",x-34,y-36,68,30,23,Ui.Ink,Ui.Slab,HorizontalAlignment.Center);
            Text(FestivalTimePresentation.Timestamp(point.Tick),x-46,518,92,25,16,Ui.Ink,align:HorizontalAlignment.Center);
        }
        long at=0;
        foreach(var spell in _spells)
        {
            if(spell.StartTick>at)RainLabel("Dry",at,spell.StartTick,Ui.Ink);
            RainLabel(RainName(spell.Intensity),spell.StartTick,spell.EndTick,spell.Intensity==RainIntensity.Downpour?Ui.PaperBright:Ui.Ink);
            at=spell.EndTick;
        }
        if(at<_day)RainLabel("Dry",at,_day,Ui.Ink);
        var intervals=_spells.Length==0?"Dry throughout · "+FestivalTimePresentation.Range(0,_day):string.Join(" · ",_spells.GroupBy(r=>r.Intensity).Select(group=>RainName(group.Key)+" "+string.Join(", ",group.Select(r=>FestivalTimePresentation.Range(r.StartTick,r.EndTick)))));
        var details=new RichTextLabel{Name="ForecastWetWindows",AutowrapMode=TextServer.AutowrapMode.WordSmart,
            Position=new(271,534),Size=new(816,49),ScrollActive=false,BbcodeEnabled=false,Text=intervals,MouseFilter=MouseFilterEnum.Ignore};
        details.AddThemeFontOverride("normal_font",Ui.Body);details.AddThemeFontSizeOverride("normal_font_size",16);
        details.AddThemeColorOverride("default_color",Ui.InkMuted);_canvas.AddChild(details);
        Text("Closing",1100,544,84,25,16,Ui.InkMuted,align:HorizontalAlignment.Right);
        Text(_forecast.PlanningAdvice,250,636,620,44,20,Ui.BarMuted);
        _menu=Button("ForecastMenu","‹  Main menu",48,625,169,52,false,menu);
        _continue=Button("ForecastContinue","Continue to preparation  →",907,625,325,52,true,proceed);
        _continue.FocusNext=_continue.GetPathTo(_menu);_continue.FocusPrevious=_continue.GetPathTo(_menu);
        _menu.FocusNext=_menu.GetPathTo(_continue);_menu.FocusPrevious=_menu.GetPathTo(_continue);
        Resized+=Layout;Layout();CallDeferred(nameof(FocusInitialAction));
        void RainLabel(string value,long from,long to,Color color)
        {if(X(to)-X(from)>80)Text(value,X(from),470,X(to)-X(from),30,16,color,align:HorizontalAlignment.Center);}
    }
    public static string RainName(RainIntensity rain)=>rain switch{RainIntensity.Showers=>"Showers",RainIntensity.Heavy=>"Heavy",RainIntensity.Downpour=>"Downpour",_=>"Dry"};
    public override void _Process(double delta){var target=GetViewport().GetVisibleRect().Size;if(Size!=target)Size=target;}
    private void FocusInitialAction(){if(IsInsideTree()&&!IsQueuedForDeletion()&&_continue.IsInsideTree()&&!_continue.IsQueuedForDeletion())_continue.GrabFocus();}
    private void Layout()
    {
        var scale=Math.Min(Size.X/1280f,Size.Y/720f);_canvas.Scale=new(scale,scale);_canvas.Position=(Size-new Vector2(1280,720)*scale)/2;
    }
    private Label Text(string value,float x,float y,float w,float h,int size,Color color,Font? font=null,HorizontalAlignment align=HorizontalAlignment.Left)
    {
        var label=new Label{Text=value,Position=new(x,y),Size=new(w,h),MouseFilter=MouseFilterEnum.Ignore,HorizontalAlignment=align};
        label.AddThemeFontOverride("font",font??Ui.Body);label.AddThemeFontSizeOverride("font_size",size);label.AddThemeColorOverride("font_color",color);_canvas.AddChild(label);return label;
    }
    private Button Button(string name,string text,float x,float y,float w,float h,bool primary,Action action)
    {
        var b=new Button{Name=name,Text=text,Position=new(x,y),Size=new(w,h),MouseDefaultCursorShape=CursorShape.PointingHand};
        b.AddThemeFontOverride("font",Ui.BodySemi);b.AddThemeFontSizeOverride("font_size",20);
        b.AddThemeColorOverride("font_color",primary?Ui.Ink:Ui.BarText);b.AddThemeColorOverride("font_focus_color",primary?Ui.Ink:Ui.BarText);b.AddThemeColorOverride("font_pressed_color",primary?Ui.Ink:Ui.BarText);b.AddThemeColorOverride("font_hover_color",primary?Ui.Ink:Ui.BarText);
        StyleBoxFlat Box(Color color,int border)=>new(){BgColor=color,BorderColor=Ui.BarMuted,BorderWidthLeft=border,BorderWidthRight=border,BorderWidthTop=border,BorderWidthBottom=border,CornerRadiusTopLeft=6,CornerRadiusTopRight=6,CornerRadiusBottomLeft=6,CornerRadiusBottomRight=6};
        b.AddThemeStyleboxOverride("normal",Box(primary?Ui.Gold:Ui.BarDeep,primary?0:1));b.AddThemeStyleboxOverride("hover",Box(primary?Ui.Gold.Lightened(.1f):Ui.BarRaised,1));b.AddThemeStyleboxOverride("pressed",Box(Ui.GoldShadow,1));
        var focus=Box(Colors.Transparent,2);focus.BorderColor=Ui.BarText;focus.ExpandMarginLeft=focus.ExpandMarginRight=focus.ExpandMarginTop=focus.ExpandMarginBottom=5;b.AddThemeStyleboxOverride("focus",focus);
        b.Pressed+=action;_canvas.AddChild(b);return b;
    }
}

internal partial class ForecastPlot:Control
{
    private GameSession _session=null!;
    public void Configure(GameSession session){_session=session;QueueRedraw();}
    public static float Y(int c,ClassicForecast f)=>420-(c-f.Temperatures.Min(p=>p.Celsius))*5;
    public override void _Draw()
    {
        if(_session is null)return;
        var f=_session.CaptureClassicForecast()!;var day=_session.PreparedEditionDurationTicks;
        float X(long tick)=>270+914f*tick/day;
        var paper=new StyleBoxFlat{BgColor=Ui.Paper,BorderColor=Ui.PaperEdge,BorderWidthLeft=1,BorderWidthRight=1,BorderWidthTop=1,BorderWidthBottom=1,CornerRadiusTopLeft=9,CornerRadiusTopRight=9,CornerRadiusBottomLeft=9,CornerRadiusBottomRight=9};
        DrawStyleBox(paper,new(48,211,1184,374));DrawLine(new(48,606),new(1232,606),Ui.BarLine);
        DrawRect(new(270,280,914,32),Ui.PaperRule);
        var stage=_session.Stages[0];
        if(_session.Stages.Count>1)
        {
            foreach(var(active,row) in _session.Stages.Select((active,row)=>(active,row)))
            foreach(var(start,i) in active.SlotStarts.Select((start,i)=>(start,i)))
                DrawRect(new(X(start),281+row*20,X(active.SlotEnds[i])-X(start),18),row%2==0?Ui.TealWash:Ui.GoldWash);
        }
        else foreach(var (start,i) in stage.SlotStarts.Select((t,i)=>(t,i)))DrawRect(new(X(start),280,X(stage.SlotEnds[i])-X(start),32),Ui.TealWash);
        DrawRect(new(270,465,914,40),Ui.PaperBright);DrawRect(new(270,465,914,40),Ui.TealLine,false);
        foreach(var spell in _session.CaptureWeather()!.Spells)
        {
            var x=X(spell.StartTick);var end=X(spell.EndTick);var c=spell.Intensity==RainIntensity.Downpour?Ui.Teal:Ui.TealWash;
            DrawRect(new(x,465,end-x,40),c);
            for(float stripe=x;stripe<end;stripe+=spell.Intensity==RainIntensity.Showers?9:5)
                DrawLine(new(stripe,505),new(Math.Min(end,stripe+25),465),spell.Intensity==RainIntensity.Downpour?Ui.TealLine:Ui.TealLine.Darkened(.15f));
        }
        var points=f.Temperatures;
        for(var i=0;i<points.Length;i++)
        {
            var p=new Vector2(X(points[i].Tick),Y(points[i].Celsius,f));
            DrawDashedLine(new(p.X,355),new(p.X,465),Ui.PaperEdge,1,3);
            if(i>0)DrawLine(new(X(points[i-1].Tick),Y(points[i-1].Celsius,f)),p,Ui.GoldShadow,3,true);
            DrawCircle(p,6,Ui.GoldShadow);DrawCircle(p,3,Ui.Paper);
        }
    }
}

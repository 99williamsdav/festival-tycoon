using Festival.Simulation;
using Godot;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private CanvasLayer? _classicForecastLayer;
    private bool _classicForecastAccepted;
    private Button? _forecastMenuButton;
    private readonly List<(CanvasLayer Layer,bool Visible)> _forecastHiddenLayers=[];
    private bool ClassicForecastOpen=>_classicForecastLayer is not null;
    private void RefreshClassicForecast()
    {
        if(!_challengeFlowEnabled||StartMenu.IsOpen||_classicForecastAccepted||ClassicForecastOpen||
            !_session.IsClassicCampaign||_session.PreparedStatus!=PreparationStatus.Preparing)return;
        CancelBuildPlacement();ClearSelection();
        _forecastHiddenLayers.Clear();
        foreach(var layer in GetChildren().OfType<CanvasLayer>().Where(l=>!l.IsQueuedForDeletion()))
        {_forecastHiddenLayers.Add((layer,layer.Visible));layer.Hide();}
        _classicForecastLayer=new CanvasLayer{Layer=26,Name="ClassicForecast"};AddChild(_classicForecastLayer);
        var screen=new ClassicForecastScreen{Name="ForecastScreen"};_classicForecastLayer.AddChild(screen);
        screen.Build(_session,ContinueClassicForecast,ClassicForecastMenu);
    }
    private void CloseClassicForecast()
    {
        if(_classicForecastLayer is not null){_classicForecastLayer.Hide();_classicForecastLayer.QueueFree();_classicForecastLayer=null;}
        foreach(var (layer,visible) in _forecastHiddenLayers)
            if(GodotObject.IsInstanceValid(layer)&&!layer.IsQueuedForDeletion())layer.Visible=visible;
        _forecastHiddenLayers.Clear();
    }
    private void ContinueClassicForecast()
    {
        _classicForecastAccepted=true;CloseClassicForecast();GetViewport().SetInputAsHandled();RefreshPreparationHud();
        CallDeferred(nameof(FocusClassicPreparation));
    }
    private void FocusClassicPreparation()
    {
        if(Perks.Panel?.Visible==true&&!Perks.Panel.IsQueuedForDeletion())
            Perks.Panel.FindChildren("*","Button",true,false).OfType<Button>().FirstOrDefault(b=>b.IsInsideTree()&&!b.IsQueuedForDeletion()&&!b.Disabled&&b.IsVisibleInTree())?.GrabFocus();
    }
    private void ClassicForecastMenu(){CloseClassicForecast();BuildStartSplash();}
    private void ReopenClassicForecast()
    {
        if(!_session.IsClassicCampaign||_session.PreparedStatus!=PreparationStatus.Preparing)return;
        _classicForecastAccepted=false;RefreshClassicForecast();
    }
}

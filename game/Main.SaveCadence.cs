using Godot;
using System;

namespace Festival.Game;

public partial class Main
{
    private CanvasLayer? _cadenceErrorLayer;
    private Label? _cadenceErrorLabel;

    /// <summary>Shows the host's latest save news in the HUD and its failure panel.</summary>
    private void SyncSaveStatus()
    {
        var changed = false;
        if (_host.TakeNotice() is { } notice) { _preparationMessage = notice; changed = true; }
        if (_host.SaveError is { } error) ShowCadenceSaveError(error);
        else if (_cadenceErrorLayer?.Visible == true) _cadenceErrorLayer.Visible = false;
        if (changed) RefreshPreparationHud();
    }

    /// <summary>Saves a milestone now and reports the outcome.</summary>
    private bool SaveMilestone(string reason)
    {
        var saved = _host.SaveMilestone(reason);
        SyncSaveStatus();
        return saved;
    }

    private void ShowCadenceSaveError(string message)
    {
        if (_cadenceErrorLayer is null)
        {
            _cadenceErrorLayer = new CanvasLayer { Layer = 30 };
            AddChild(_cadenceErrorLayer);
            var width = Math.Min(480, GetViewport().GetVisibleRect().Size.X - 30);
            var panel = HudPanel(_cadenceErrorLayer, new Vector2(Ui.Gutter, Ui.TopBar + 8), new Vector2(width, 130));
            var column = new VBoxContainer();
            panel.AddChild(column);
            column.AddChild(HudLabel("Campaign save needs attention", 18));
            _cadenceErrorLabel = HudLabel(message);
            column.AddChild(_cadenceErrorLabel);
            column.AddChild(ButtonText("Retry save", () => SaveMilestone("Retry")));
        }
        _cadenceErrorLabel!.Text = message;
        _cadenceErrorLayer.Visible = true;
    }
}

using Godot;
using System;

namespace Festival.Game;

public partial class Main
{
    private CanvasLayer? _cadenceErrorLayer;
    private Label? _cadenceErrorLabel;
    private Control? _cadenceErrorPanel;
    private Control? _cadenceRetryStatus;

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
            var viewport = GetViewport().GetVisibleRect().Size;
            var width = Math.Min(340, viewport.X - 30);
            var panel = HudPanel(_cadenceErrorLayer, new Vector2(Ui.Gutter, viewport.Y - 150), new Vector2(width, 130));
            _cadenceErrorPanel = panel;
            panel.MouseFilter = Control.MouseFilterEnum.Ignore;
            var column = new VBoxContainer();
            column.MouseFilter = Control.MouseFilterEnum.Ignore;
            panel.AddChild(column);
            var heading = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            column.AddChild(heading);
            var title = HudLabel("Save failed", 16);
            title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            title.MouseFilter = Control.MouseFilterEnum.Ignore;
            heading.AddChild(title);
            var close = ButtonText("X", () => { _host.DismissSaveWarning(); SyncSaveStatus(); });
            close.Name = "DismissSaveWarning";
            heading.AddChild(close);
            _cadenceErrorLabel = HudLabel(message);
            _cadenceErrorLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            column.AddChild(_cadenceErrorLabel);
            var retry = ButtonText("Retry save", () => SaveMilestone("Retry"));
            retry.Name = "RetryCampaignSave";
            column.AddChild(retry);
            var statusPanel = HudPanel(_cadenceErrorLayer,
                new Vector2(Ui.Gutter, viewport.Y - Ui.Dock - 56), new Vector2(220, 44));
            statusPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
            _cadenceRetryStatus = statusPanel;
            var status = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            statusPanel.AddChild(status);
            var statusLabel = HudLabel("Save failed", 13);
            statusLabel.AutowrapMode = TextServer.AutowrapMode.Off;
            statusLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            status.AddChild(statusLabel);
            var statusRetry = ButtonText("Retry save", () => SaveMilestone("Retry"));
            statusRetry.Name = "RetryDismissedSave";
            status.AddChild(statusRetry);
        }
        _cadenceErrorLabel!.Text = message;
        var viewportHeight = GetViewport().GetVisibleRect().Size.Y;
        _cadenceErrorPanel!.Position = new Vector2(Ui.Gutter,
            Math.Max(Ui.TopBar + 8, viewportHeight - Math.Max(130, _cadenceErrorPanel.Size.Y) - Ui.Dock - 8));
        _cadenceErrorPanel!.Visible = _host.SaveWarningVisible;
        _cadenceRetryStatus!.Visible = !_host.SaveWarningVisible;
        _cadenceErrorLayer.Visible = true;
    }
}

using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private PerkPanel? _perkView;
    private PerkPanel Perks => _perkView ??= new(this, LayoutOwnedPerkWorkspace);
    private bool _ownedWorkspaceConstrained;
    private int _ownedWorkspaceLayoutGeneration;
    private int _ownedContextScroll;
    private int _ownedContextClosedScroll;
    private GameSession? _ownedContextSession;
    private string _ownedContextIdentity = "";
    private string ContextScrollIdentity => $"{_selected?.StableId}|{_selectedAttendeeId}|{_selectedMedicalFacility}|{_selectedWaterPointId}|{_selectedImmersionVendor}|{_selectedToilet}|{_selectedSecurityPost}|{_selectedGenerator}|{_selectedBinId}|{_selectedMarqueeId}";
    private readonly System.Collections.Generic.Dictionary<int, int> _ownedWorkspaceScroll = [];
    private readonly System.Collections.Generic.Dictionary<int, int> _ownedWorkspaceClosedScroll = [];

    private void LayoutOwnedPerkWorkspace()
    {
        if (_hudWorkspace is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        var height = Math.Min(Ui.S(522), size.Y - Ui.ContentTop - Ui.Dock - Ui.S(12));
        var constrained = Perks.Panel?.Visible == true && _session.CapturePerks()?.Pending == false;
        if(constrained != _ownedWorkspaceConstrained) _ownedWorkspaceLayoutGeneration++;
        if (_ownedWorkspaceConstrained && !constrained && _hudTabs is not null)
        {
            _ownedContextScroll = _hudContextScroll?.ScrollVertical ?? 0;
            _ownedContextSession = _session; _ownedContextIdentity = ContextScrollIdentity;
            for(var i=0;i<_hudTabs.GetChildCount();i++) _ownedWorkspaceScroll[i]=((ScrollContainer)_hudTabs.GetChild(i)).ScrollVertical;
            CaptureOwnedWorkspaceClosedScroll();
        }
        if (constrained)
            height = Math.Min(height, size.Y - Ui.Dock - 8 - 220 - 10 - Ui.ContentTop);
        // The Programme and Staff sheets are tables with a column beside them, so they take the full width.
        var bookingPage = Booking.IsBuilt && HudProgrammeSelected() || _staffPanelBuilt && HudPageSelected("Staff");
        if (bookingPage && !constrained) height = Math.Min(Ui.S(530), size.Y - Ui.ContentTop - (_session.PreparedStatus == PreparationStatus.Preparing ? Ui.Dock + Ui.S(12) : 60));
        _hudWorkspace.Position = new Vector2(Ui.Gutter, Ui.ContentTop);
        _hudWorkspace.Size = new Vector2(bookingPage && !constrained ? size.X - 2 * Ui.Gutter : Ui.S(690), height);
        LayoutOwnedContext(constrained);
        if (!_ownedWorkspaceConstrained && constrained && _hudTabs is not null)
        {
            if (_hudContextScroll is not null && ReferenceEquals(_ownedContextSession, _session) && _ownedContextIdentity == ContextScrollIdentity) RestoreOwnedWorkspaceScroll(_hudContextScroll,
                _hudContextScroll.ScrollVertical != _ownedContextClosedScroll ? _hudContextScroll.ScrollVertical : _ownedContextScroll);
            foreach(var (index, offset) in _ownedWorkspaceScroll)
            {
                var tab=(ScrollContainer)_hudTabs.GetChild(index);
                // A clamp from the larger viewport is cosmetic; a player scroll while
                // closed is a new preference and must take precedence over that cache.
                var desired=_ownedWorkspaceClosedScroll.TryGetValue(index,out var closed) && tab.ScrollVertical!=closed ? tab.ScrollVertical : offset;
                RestoreOwnedWorkspaceScroll(tab, desired);
            }
        }
        _ownedWorkspaceConstrained = constrained;
    }
    private void LayoutOwnedContext(bool constrained)
    {
        if (_contextPanel is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        var y = _session.PreparedStatus == PreparationStatus.Preparing ? Ui.ContentTop : Stage.Bottom + Ui.S(12);
        var height = size.Y - y - Ui.S(64) - 12;
        if (constrained) height = Math.Min(height, size.Y - Ui.S(64) - 8 - 220 - 10 - y);
        // Stop above the rotate and zoom row during preparation.
        if (_session.PreparedStatus == PreparationStatus.Preparing) height = Math.Min(height, size.Y - Ui.Dock - Ui.S(16) - Ui.S(44) - Ui.S(8) - y);
        // Fit the contents, so a short panel isn't mostly empty and a long one (a person, with their buttons) gets
        // the room it needs before it has to scroll.
        if (_hudContextScroll?.GetChildCount() > 0 && _hudContextScroll.GetChild(0) is Control detail)
            height = Math.Min(height, detail.GetCombinedMinimumSize().Y + _contextPanel.GetThemeStylebox("panel").GetMinimumSize().Y + 2);
        _contextPanel.Position = new Vector2(size.X - 300, y);
        _contextPanel.Size = new Vector2(300, height);
    }
    private async void CaptureOwnedWorkspaceClosedScroll()
    {
        var generation = _ownedWorkspaceLayoutGeneration;
        var session = _session;
        var context = ContextScrollIdentity;
        // Container layout clamps scroll after the refresh's deferred calls. Observe
        // the settled expanded viewport, rather than mistaking that clamp for input.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if(generation != _ownedWorkspaceLayoutGeneration || !ReferenceEquals(session, _session) || _ownedWorkspaceConstrained || _hudTabs is null) return;
        for(var i=0;i<_hudTabs.GetChildCount();i++) _ownedWorkspaceClosedScroll[i]=((ScrollContainer)_hudTabs.GetChild(i)).ScrollVertical;
        if (context == ContextScrollIdentity) _ownedContextClosedScroll = _hudContextScroll?.ScrollVertical ?? 0;
    }
    private async void RestoreOwnedWorkspaceScroll(ScrollContainer tab, int offset)
    {
        var generation = _ownedWorkspaceLayoutGeneration;
        var session = _session;
        var context = ContextScrollIdentity;
        var startingOffset = tab.ScrollVertical;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if(generation == _ownedWorkspaceLayoutGeneration && ReferenceEquals(session, _session) && _ownedWorkspaceConstrained &&
            (tab != _hudContextScroll || context == ContextScrollIdentity) &&
            GodotObject.IsInstanceValid(tab) && tab.ScrollVertical == startingOffset) tab.ScrollVertical=offset;
    }
}

using Festival.ContentAdapter;
using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Festival.Game;

public partial class Main
{
    private string? _hoverCaptureDirectory;
    private int _hoverCaptureFrame;
    private Vector2 _hoverCapturePoint;
    private string _hoverCaptureHash = "";
    private string _hoverCaptureImage = "";
    private MedicalSnapshot? _hoverSavedMedical;
    private Control? _hoverUiButton;
    private EntityId? _hoverCollapsedPerson;
    private bool HoverUiTarget(Control target)
    {
        for(var node=GetViewport().GuiGetHoveredControl();node is not null;node=node.GetParent() as Control)
            if(node==target)return true;
        return false;
    }
    private void HoverAssert(bool condition,string message) { if (!condition) throw new InvalidOperationException(message); }
    private void HoverCaptureImage(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        HoverAssert(image.GetWidth()==GetWindow().Size.X && image.GetHeight()==GetWindow().Size.Y,"Capture dimensions differ from native window");
        HoverAssert(image.SavePng(Path.Combine(_hoverCaptureDirectory!,name+".png"))==Error.Ok,"PNG save failed");
        GD.Print($"HOVER_CAPTURE image={name} hovered={_hoveredColliderId} cursor={_feedbackCursor} hash={_session.CaptureSnapshot().AuthoritativeHash}");
    }
    private void HoverMovePointer(Vector2 point)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position=point,GlobalPosition=point },true);
    }
    private void HoverAt(Vector3 world,string image)
    {
        _hoverCapturePoint = _camera.UnprojectPosition(world); _hoverCaptureImage = image;
        HoverAssert(GetViewport().GetVisibleRect().HasPoint(_hoverCapturePoint),"Fixture target offscreen: "+image);
        HoverMovePointer(_hoverCapturePoint);
    }
    private Vector2 HoverWaterPlacementPoint(bool valid)
    {
        for(var x=45;x<=180;x+=5)for(var z=70;z<=165;z+=5)
        {
            var centre=TraversalGrid.CellCentre(new GridCell(x,z));
            var screen=_camera.UnprojectPosition(new Vector3(centre.XMillimetres/1000f,0,centre.ZMillimetres/1000f));
            if(!GetViewport().GetVisibleRect().HasPoint(screen)||screen.Y<190||screen.Y>GetWindow().Size.Y-140||screen.X<70||screen.X>GetWindow().Size.X-360||WorldInputOccluded(screen))continue;
            UpdateWaterPlacementPreview(screen);
            if(_waterPlacementCandidate is not null && (_waterPlacementIssue is null)==valid)return screen;
        }
        throw new InvalidOperationException("No visible unoccluded placement fixture point with preview clearance; valid="+valid);
    }
    private Vector2 HoverPersonPixel(EntityId id)
    {
        var body=_attendeeVisuals[id];var centre=_camera.UnprojectPosition(body.ToGlobal(new Vector3(0,.85f,0)));
        for(var y=-30;y<=30;y+=2)for(var x=-30;x<=30;x+=2)
        {
            var point=centre+new Vector2(x,y);
            if(!GetViewport().GetVisibleRect().HasPoint(point)||WorldInputOccluded(point))continue;
            if(ResolveWorldHit(point) is { } hit && _attendeePickRegistry.TryGetValue(hit.GetInstanceId(),out var person) && person==id)return point;
        }
        throw new InvalidOperationException("No visible exact collapsed-person pixel within bounded search");
    }
    private void ProcessHoverCapture()
    {
        if (_hoverCaptureDirectory is null) return;
        try
        {
            var frame = ++_hoverCaptureFrame;
            if(_hoverUiButton is not null)
            {
                _hoverCapturePoint=_hoverUiButton is TabBar targetTabs ? targetTabs.GlobalPosition+targetTabs.GetTabRect(_hudTabs!.CurrentTab).GetCenter() : _hoverUiButton.GetGlobalRect().GetCenter();
                HoverMovePointer(_hoverCapturePoint);
            }
            if(_hoverCaptureImage=="09-valid-placement" && frame%5==2)
            { _hoverCapturePoint=HoverWaterPlacementPoint(true);HoverMovePointer(_hoverCapturePoint); }
            if(_waterPlacementMode!=WaterPlacementMode.None)UpdateWaterPlacementPreview(_hoverCapturePoint);
            if (_hoverCaptureImage=="16-collapsed-initialized-fixture" && frame%5==3)
            { _hoverCapturePoint=HoverPersonPixel(_hoverCollapsedPerson!.Value);HoverMovePointer(_hoverCapturePoint); }
            UpdateHoverFeedback(_hoverCapturePoint);
            if (frame%5==4 && _hoverCaptureImage!="") { HoverCaptureImage(_hoverCaptureImage); _hoverCaptureImage=""; }
            if(frame%5!=0)return;
            switch(frame/5)
            {
                case 1:
                    GD.Print("HOVER_CAPTURE_SETUP scripted_native=True manual_desktop_QA=False fixture=owned_card_states_visual_dense_collapsed save_namespace=isolated");
                    // Labelled empty owned-panel presentation fixture. Restore
                    // the ordinary initial draft immediately after its image.
                    var p=_session.CapturePerks()!;
                    typeof(GameSession).GetField("_perks",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(_session,p with {Pending=false,Hand=[]});
                    _perksExpanded=true; RefreshPerkHud(); _hoverCaptureImage="01-empty-owned"; break;
                case 2:
                    _session=GameSession.CreatePerkCampaign(20260922); p=_session.CapturePerks()!;
                    CommitEquipmentAction(new ChoosePerkCommand(p.DraftAttempt,p.Cursor,p.Hand[0]));
                    _perksExpanded=true; RefreshPerkHud(); _hoverCaptureImage="02-partial-owned"; break;
                case 3:
                    PerkCaptureFullFixture(); p=_session.CapturePerks()!;
                    CommitEquipmentAction(new SkipPerksCommand(p.DraftAttempt,p.Cursor));
                    CommitEquipmentAction(new SetProgrammeCommand(["act.meadow-lanterns","act.barnstorm-circuit","act.field-frequency"]));
                    CommitEquipmentAction(new AcceptPreparationOfferCommand("staff.steward"));
                    ResetFinanceFeedback();_perksExpanded=true;RefreshPerkHud();_hoverCaptureImage="03-full-owned"; break;
                case 4:
                    var grid=_perkBody!.GetNode<GridContainer>("EquippedPerkCards");HoverAssert(grid.GetChildCount()==5 && grid.GetChildren().All(c=>c is PanelContainer),"Five full owned cards missing");
                    var scroll=(ScrollContainer)_perkPanel!.GetChild(0); scroll.ScrollVertical=1000;
                    if(scroll.GetVScrollBar().IsVisibleInTree())_hoverUiButton=scroll.GetVScrollBar();
                    _hoverCaptureImage="04-full-owned-bottom";break;
                case 5:
                    if(_hoverUiButton is ScrollBar bar)
                    {
                        HoverAssert(HoverUiTarget(bar),"Owned scrollbar motion missed actual scrollbar");
                        HoverAssert(bar.MouseDefaultCursorShape==(bar.MaxValue-bar.MinValue>bar.Page?Control.CursorShape.PointingHand:Control.CursorShape.Arrow),"Owned scrollbar usability cursor wrong");
                    }
                    _hoverUiButton=null;_perksExpanded=false;RefreshPerkHud();_hudWorkspaceOpen=false;_hudProgrammeOpen=false;RefreshHudWorkspace();
                    _focus=new Vector3(-12,0,-14);_camera.Size=50;ApplyCamera();
                    _hoverCaptureHash=_session.CaptureSnapshot().AuthoritativeHash;
                    HoverAt(_visualRegistry["farm.farmhouse"].GlobalPosition+Vector3.Up*2,"05-farmhouse-hover");break;
                case 6:
                    UpdateHoverFeedback(_hoverCapturePoint);HoverAssert(_hoveredColliderId!=0,"Farmhouse hover missing");
                    var hit=ResolveWorldHit(_hoverCapturePoint)!;Pick(_hoverCapturePoint);HoverAssert(_selected?.StableId=="farm.farmhouse","Shared farmhouse click differs");
                    _hoverCaptureImage="06-selected-and-hover";break;
                case 7:
                    HoverMovePointer(new Vector2(640,500)); UpdateHoverFeedback(new Vector2(640,500));
                    HoverAssert(_highlight.Visible,"Hover removed independent selection");
                    _focus=new Vector3(4,0,8);ApplyCamera(); ClearSelection();
                    HoverAt(_immersionVendors["food"].GlobalPosition+Vector3.Up,"07-vendor-hover");break;
                case 8:
                    UpdateHoverFeedback(_hoverCapturePoint);HoverAssert(_hoveredColliderId!=0,"Actual vendor hover missing");Pick(_hoverCapturePoint);HoverAssert(_selectedImmersionVendor=="food","Vendor click differs");
                    ClearSelection();HoverAt(_primaryWaterVisual!.GlobalPosition+Vector3.Up,"08-water-hover");break;
                case 9:
                    UpdateHoverFeedback(_hoverCapturePoint);HoverAssert(_hoveredColliderId!=0,"Water hover missing");Pick(_hoverCapturePoint);HoverAssert(_selectedMedicalFacility==MedicalFacility.Water,"Water click differs");
                    ClearSelection();BeginWaterPlacement(true);_hoverCaptureImage="09-valid-placement";break;
                case 10:
                    UpdateWaterPlacementPreview(_hoverCapturePoint);UpdateHoverFeedback(_hoverCapturePoint);HoverAssert(_feedbackCursor==Input.CursorShape.Cross && !_hoverHighlight.Visible,"Valid placement cursor priority missing");
                    _hoverCapturePoint=HoverWaterPlacementPoint(false);HoverMovePointer(_hoverCapturePoint);UpdateWaterPlacementPreview(_hoverCapturePoint);_hoverCaptureImage="10-invalid-placement";break;
                case 11:
                    UpdateHoverFeedback(_hoverCapturePoint);HoverAssert(_feedbackCursor==Input.CursorShape.Forbidden,"Invalid placement cursor missing");CancelWaterPlacement();
                    HoverAssert(_hoverCaptureHash==_session.CaptureSnapshot().AuthoritativeHash,"Hover/selection/placement preview changed hash");
                    ShowHudStartConfirmation();_hoverCaptureImage="11-modal-blocks-world";break;
                case 12:
                    UpdateHoverFeedback(_hoverCapturePoint);HoverAssert(!_hoverHighlight.Visible && WorldInputOccluded(_hoverCapturePoint),"Native modal world blocker missing");
                    // Native subwindow cursor handlers are source-reviewed;
                    // this viewport capture proves modal world occlusion only.
                    _hudStartConfirmation!.Hide();
                    _hudWorkspaceOpen=true;RefreshHudWorkspace(); _hoverUiButton=_preparationStart;_hoverCapturePoint=_preparationStart!.GetGlobalRect().GetCenter();HoverMovePointer(_hoverCapturePoint);_hoverCaptureImage="12-enabled-ui-hover";break;
                case 13:
                    UpdateHoverFeedback(_hoverCapturePoint);HoverAssert(HoverUiTarget(_preparationStart!),"Native viewport motion missed intended button");HoverAssert(!_preparationStart!.Disabled && _preparationStart.MouseDefaultCursorShape==Control.CursorShape.PointingHand,"Enabled UI hand missing");HoverAssert(WorldInputOccluded(_hoverCapturePoint) && !_hoverHighlight.Visible,"UI world hover leaked");
                    _preparationStart!.Disabled=true;_hoverCaptureImage="13-disabled-ui";break;
                case 14:
                    UpdateHoverFeedback(_hoverCapturePoint);HoverAssert(_preparationStart!.MouseDefaultCursorShape==Control.CursorShape.Arrow,"Disabled UI advertised action");
                    _hoverUiButton=null;RefreshPreparationHud();_hudWorkspaceOpen=false;RefreshHudWorkspace();PreparationSave();_hoverCaptureHash=_session.CaptureSnapshot().AuthoritativeHash;var priorSession=_session;PreparationLoad();
                    HoverAssert(!ReferenceEquals(priorSession,_session),"Ordinary load did not replace session");
                    HoverAssert(_hoverCaptureHash==_session.CaptureSnapshot().AuthoritativeHash,"Save/load changed hash");
                    PreparationStart();TimetableAdvanceTo(1800);_session.Execute(CampaignEnvelope(new SetPausedCommand(true)));_foundationPresentation.Reset(_session.CaptureObservation());
                    _hoverCaptureImage="14-live-people";break;
                case 15:
                    _hudWorkspaceOpen=false;_hudProgrammeOpen=false;RefreshHudWorkspace();ClearSelection();
                    var person=_attendeeVisuals.First();_focus=person.Value.GlobalPosition;_camera.Size=20;ApplyCamera();
                    HoverAt(person.Value.GlobalPosition+Vector3.Up*.85f,"15-person-hover");break;
                case 16:
                    UpdateHoverFeedback(_hoverCapturePoint);hit=ResolveWorldHit(_hoverCapturePoint)!;
                    HoverAssert(hit is not null && _attendeePickRegistry.ContainsKey(hit.GetInstanceId()),"Person hover missing");Pick(_hoverCapturePoint);HoverAssert(_selectedAttendeeId==_attendeePickRegistry[hit!.GetInstanceId()],"Dense physical person click differs from hover");
                    _hoverCollapsedPerson=_selectedAttendeeId;var body=_attendeeVisuals[_selectedAttendeeId!.Value];
                    _hoverSavedMedical=_session.CaptureMedical()!;_hoverCaptureHash=_session.CaptureSnapshot().AuthoritativeHash;
                    typeof(GameSession).GetProperty("MedicalView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(_session,_hoverSavedMedical with { Needs=_hoverSavedMedical.Needs.Select(n=>n.AgentId==_selectedAttendeeId.Value.Value?n with {Stage=MedicalStage.Collapsed,Intent=MedicalIntent.Collapsed}:n).ToArray() });
                    _foundationPresentation.Reset(_session.CaptureObservation());
                    body.Rotation=new Vector3(Mathf.Pi/2,0,0);HoverAt(body.ToGlobal(new Vector3(0,.85f,0)),"16-collapsed-initialized-fixture");break;
                case 17:
                    body=_attendeeVisuals[_hoverCollapsedPerson!.Value];HoverAssert(Math.Abs(body.Rotation.X-Mathf.Pi/2)<.01,"Collapsed production presentation did not persist");
                    HoverAssert(ResolveWorldHit(_hoverCapturePoint) is { } collapsedHit && _attendeePickRegistry.TryGetValue(collapsedHit.GetInstanceId(),out var collapsedId) && collapsedId==_hoverCollapsedPerson,"Ray missed the initialized collapsed person");
                    UpdateHoverFeedback(_hoverCapturePoint);hit=ResolveWorldHit(_hoverCapturePoint)!;HoverAssert(hit is not null && _attendeePickRegistry.ContainsKey(hit.GetInstanceId()),"Rotated collapsed capsule hover missing");Pick(_hoverCapturePoint);HoverAssert(_selectedAttendeeId==_attendeePickRegistry[hit!.GetInstanceId()],"Collapsed click differs from hover");
                    var id=_selectedAttendeeId!.Value;body=_attendeeVisuals[id];_attendeeVisuals.Remove(id);UpdateHoverFeedback(_hoverCapturePoint);HoverAssert(_hoveredColliderId==0,"Missing visual retained hover");_attendeeVisuals.Add(id,body);
                    typeof(GameSession).GetProperty("MedicalView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(_session,_hoverSavedMedical);_foundationPresentation.Reset(_session.CaptureObservation());
                    HoverAssert(_hoverCaptureHash==_session.CaptureSnapshot().AuthoritativeHash,"Temporary collapse fixture did not restore exact authoritative state");
                    _focus=new Vector3(45,0,45);ApplyCamera();HoverAt(new Vector3(45,0,45),"17-empty-ground");break;
                case 18:
                    UpdateHoverFeedback(_hoverCapturePoint);HoverAssert(_hoveredColliderId==0 && !_hoverHighlight.Visible,"Empty ground retained hover");
                    // Fresh preparation copy: global pending perk is resolved
                    // normally so only actionable readiness owners are marked.
                    ClearSelection();foreach(var visual in _attendeeVisuals.Values)visual.QueueFree();_attendeeVisuals.Clear();_attendeePickRegistry.Clear();
                    _session=GameSession.CreatePerkCampaign(20260922);_foundationPresentation.Reset(_session.CaptureObservation());_foundationClock.ResetBoundary();ResetFinanceFeedback();SyncExtraWaterWorld();SyncImmersionWorld();ResetLivePerformancePresentation();
                    p=_session.CapturePerks()!;CommitEquipmentAction(new ChoosePerkCommand(p.DraftAttempt,p.Cursor,p.Hand[0]));
                    _hudWorkspaceOpen=true;RefreshPreparationHud();
                    HoverAssert(_session.GetPreparationStartBlockers().Count==2,"Expected two readiness owning tabs");
                    _hoverCaptureImage="18-readiness-multiple-blockers";break;
                case 19:
                    SelectHudTab("Programme");var tabs=_hudTabs!.GetTabBar();_hoverUiButton=tabs;
                    _hoverCapturePoint=tabs.GlobalPosition+tabs.GetTabRect(_hudTabs.CurrentTab).GetCenter();HoverMovePointer(_hoverCapturePoint);
                    _hoverCaptureImage="19-readiness-selected-hover-programme";break;
                case 20:
                    HoverAssert(HoverUiTarget(_hudTabs!.GetTabBar()),"Readiness tab motion did not reach actual tab bar");_hoverUiButton=null;
                    CommitEquipmentAction(new SetProgrammeCommand(["act.meadow-lanterns","act.barnstorm-circuit","act.field-frequency"]));
                    HoverAssert(_session.GetPreparationStartBlockers().Count==1,"Programme blocker did not clear");
                    SelectHudTab("Staff");_hoverCaptureImage="20-readiness-staff-only";break;
                case 21:
                    CommitEquipmentAction(new AcceptPreparationOfferCommand("staff.steward"));
                    HoverAssert(_session.GetPreparationStartBlockers().Count==0 && !_preparationStart!.Disabled,"Ready preparation still blocked");
                    _hoverCaptureHash=_session.CaptureSnapshot().AuthoritativeHash;
                    var collapse=_hudWorkspace!.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.TooltipText=="Collapse preparation");
                    HoverAssert(collapse.Text=="×" && collapse.CustomMinimumSize.X>=38 && collapse.CustomMinimumSize.Y>=38,"Preparation X contract missing");
                    collapse.EmitSignal(BaseButton.SignalName.Pressed);HoverAssert(!_hudWorkspace.Visible,"Preparation X did not collapse");_hudPreparationToggle!.EmitSignal(BaseButton.SignalName.Pressed);HoverAssert(_hudWorkspace.Visible,"Preparation reopen failed");
                    HoverAssert(_hoverCaptureHash==_session.CaptureSnapshot().AuthoritativeHash,"Collapse/reopen changed authoritative state");
                    _hoverCaptureImage="21-readiness-ready-optional-normal";break;
                case 22:
                    PerkCaptureFullFixture();p=_session.CapturePerks()!;BeginPerkChoice(p.Hand[0]);_hoverCaptureImage="22-replacement-selection";break;
                case 23:
                    _pendingPerkReplacement=_session.CapturePerks()!.Equipped[0];_perkHudKey="";RefreshPerkHud();_hoverCaptureHash=_session.CaptureSnapshot().AuthoritativeHash;_hoverCaptureImage="23-replacement-confirmation";break;
                case 24:
                    CancelPerkConfirmation();HoverAssert(_hoverCaptureHash==_session.CaptureSnapshot().AuthoritativeHash,"Replacement cancellation changed hash");p=_session.CapturePerks()!;CommitEquipmentAction(new SkipPerksCommand(p.DraftAttempt,p.Cursor));
                    _perksExpanded=true;RefreshPerkHud();
                    collapse=_perkBody!.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.TooltipText.StartsWith("Collapse perks",StringComparison.Ordinal));
                    HoverAssert(collapse.Text=="×" && collapse.CustomMinimumSize.X>=38,"Perks X contract missing");_hoverCaptureHash=_session.CaptureSnapshot().AuthoritativeHash;
                    collapse.EmitSignal(BaseButton.SignalName.Pressed);HoverAssert(!_perkPanel!.Visible,"Perks X failed");_perkToggle!.EmitSignal(BaseButton.SignalName.Pressed);HoverAssert(_perkPanel.Visible,"Perks reopen failed");HoverAssert(_hoverCaptureHash==_session.CaptureSnapshot().AuthoritativeHash,"Perks collapse changed hash");
                    _hoverCaptureImage="24-owned-reopened-after-cancel";break;
                case 25:
                    _perksExpanded=false;RefreshPerkHud();SelectHudTab("Programme");_programmeChoices[0].ShowPopup();break;
                case 26:
                    var popup=_programmeChoices[0].GetPopup();HoverAssert(HoverPopupActive && ReferenceEquals(popup,_hoverPopup) && WorldInputOccluded(Vector2.Zero),"Registered dropdown popup did not block world");
                    HoverAssert(Enumerable.Range(0,popup.ItemCount).All(i=>!popup.IsItemDisabled(i)&&!popup.IsItemSeparator(i)),"Current dropdown rows are not all enabled");
                    popup.SetItemDisabled(0,true);UpdateHoverFeedback(Vector2.Zero);HoverAssert(_feedbackCursor==Input.CursorShape.Arrow,"Disabled native row menu advertised action");
                    popup.SetItemDisabled(0,false);popup.Hide();HoverAssert(!HoverPopupActive,"Hidden dropdown retained modal blocker");
                    GD.Print($"HOVER_CAPTURE_COMPLETE native=True shared_ray=True actual_vendor_water=True ui_occlusion=True modal=True placement_priority=True independent_selection=True missing_visual_clear=True collapsed_initialized_fixture=True save_restore_exact=True readiness_multiple_to_ready=True owned_empty_partial_full=True replacement_cancel_exact=True collapse_reopen_exact=True dropdown_modal=True conservative_disabled_menu_arrow=True content={LowerWitteringFarmScenario.ContentCompatibilityHash}");GetTree().Quit();break;
            }
        }
        catch(Exception ex){GD.PushError("HOVER_CAPTURE_FAILED "+ex);GetTree().Quit(2);_hoverCaptureDirectory=null;}
    }
}

using Festival.ContentAdapter;
using Festival.Persistence;
using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Festival.Game;

public partial class Main
{
    private string? _perkPopoutCaptureDirectory;
    private int _perkPopoutCaptureFrame;
    private string? _perkPopoutShot;
    private string _perkPopoutHash = "";
    private int _popoutTab;
    private int _popoutTabScroll;
    private int _popoutContextScroll;
    private Label? _popoutContextFixtureLabel;
    private void PopoutLongContextFixture()
    {
        // A retained diagnostic child avoids periodic inspector read-model refresh
        // replacing the fixture prose and clamping its deliberately deep offset.
        if (_popoutContextFixtureLabel is not null) return;
        _popoutContextFixtureLabel = HudLabel("LABELLED CONTEXT SCROLL PRESENTATION FIXTURE\n" + string.Join("\n", Enumerable.Range(1,24).Select(i=>$"Context diagnostic line {i}")));
        _inspectorBody.GetParent().AddChild(_popoutContextFixtureLabel);
    }
    private void PopoutSelect(int slot, string id)
    {
        var choice = _programmeChoices[slot];
        var index = Enumerable.Range(0,choice.ItemCount).Single(i => choice.GetItemMetadata(i).AsString()==id);
        choice.Select(index); choice.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);
        HoverAssert(_programmeDraft.SequenceEqual(_session.CapturePreparationPlan()!.ActIds),"Dropdown draft diverged from saved plan");
    }
    private void PopoutPure() => HoverAssert(_session.CaptureSnapshot().AuthoritativeHash==_perkPopoutHash,"Popout presentation changed authoritative state");
    private void ProcessPerkPopoutCapture()
    {
        if(_perkPopoutCaptureDirectory is null)return;
        try
        {
            var frame=++_perkPopoutCaptureFrame;
            if(frame/12==13 && frame%12==4)
                ((ScrollContainer)_hudTabs!.GetCurrentTabControl()).ScrollVertical=10000;
            if(frame/12==24 && frame%12==4) _hudContextScroll!.ScrollVertical=10000;
            if(frame%12==11 && _perkPopoutShot is { } shot)
            {
                if(_perkPanel!.Visible && _session.CapturePerks()?.Pending!=true)
                {
                    var bounds=_perkPanel.GetGlobalRect();
                    HoverAssert(Math.Abs(bounds.Size.X-(GetWindow().Size.X-20))<=1 && bounds.Size.Y<=220 && Math.Abs(bounds.GetCenter().X-GetWindow().Size.X/2f)<=1 && bounds.End.Y<=GetWindow().Size.Y-55,$"Popout footprint wrong: {bounds}");
                    HoverAssert(!_hudWorkspace!.Visible || _hudWorkspace.GetGlobalRect().End.Y<=bounds.Position.Y-5,$"Popout overlaps preparation: {bounds} {_hudWorkspace.GetGlobalRect()}");
                    HoverAssert(!_contextPanel!.Visible || !bounds.Intersects(_contextPanel.GetGlobalRect()),"Popout overlaps context");
                    if(_hudWorkspace.Visible) {
                        HoverAssert(_preparationStart.GetGlobalRect().End.Y<=_hudWorkspace.GetGlobalRect().End.Y && !_perkPanel.GetGlobalRect().HasPoint(_preparationStart.GetGlobalRect().GetCenter()),"Start clipped/occluded");
                        HoverAssert(_hudStartReason!.GetGlobalRect().End.Y<=_hudWorkspace.GetGlobalRect().End.Y,"Cost footer clipped");
                    }
                    if (_ownedEffectPopup?.Visible == true) {
                        HoverAssert(GetViewport().GetVisibleRect().Encloses(_ownedEffectPopup.GetGlobalRect()), "Visible focus tooltip outside viewport");
                        HoverAssert(!_ownedEffectPopup.GetGlobalRect().Intersects(_preparationStart.GetGlobalRect()) && !_ownedEffectPopup.GetGlobalRect().Intersects(_hudStartReason!.GetGlobalRect()), "Tooltip covers Start/cost footer");
                        HoverAssert(_ownedEffectText!.GetLineCount()*_ownedEffectText.GetThemeFont("font").GetHeight(16)<=_ownedEffectText.Size.Y+2,"Tooltip copy clipped");
                    }
                    if(shot!="07-five-focus-effect") HoverMovePointer(bounds.Position+new Vector2(5,5));UpdateHoverFeedback(bounds.GetCenter());
                    HoverAssert(WorldInputOccluded(bounds.GetCenter()) && _hoveredColliderId==0,"Popout leaked pointer to world");
                    var cards=_ownedPerkScroll!.GetNode<HBoxContainer>("EquippedPerkCards");
                    HoverAssert(cards.GetChildCount()==5,"Owned slots missing");
                    foreach(var slot in cards.GetChildren().OfType<Control>()) { var card=slot.GetChild<Control>(0); HoverAssert(card.Size.Y<=156 && card.Size.X==156 && _ownedPerkScroll.GetGlobalRect().Encloses(card.GetGlobalRect()),"All five whole cards must be simultaneously visible"); }
                    GD.Print($"POPOUT_BOUNDS panel={bounds} workspace={_hudWorkspace.GetGlobalRect()} start={_preparationStart.GetGlobalRect()} cards={cards.Size} scroll={_ownedPerkScroll.GetGlobalRect()}");
                }
                var image=GetViewport().GetTexture().GetImage();
                HoverAssert(image.GetWidth()==GetWindow().Size.X && image.GetHeight()==GetWindow().Size.Y,"Wrong native size");
                HoverAssert(image.SavePng(Path.Combine(_perkPopoutCaptureDirectory,shot+".png"))==Error.Ok,"PNG failed");
                GD.Print($"POPOUT_CAPTURE image={shot} hash={_session.CaptureSnapshot().AuthoritativeHash} cost={_session.PreparationPlanCost}");_perkPopoutShot=null;
            }
            if(frame%12!=0)return;
            switch(frame/12)
            {
                case 1:
                    var p=_session.CapturePerks()!;
                    // Labelled presentation-only empty owned fixture, no progression claim.
                    typeof(GameSession).GetField("_perks",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(_session,p with {Pending=false,Hand=[]});
                    _perksExpanded=false;RefreshPreparationHud();_perkPopoutHash=_session.CaptureSnapshot().AuthoritativeHash;
                    _perkPopoutShot="01-empty-collapsed";break;
                case 2:_perkToggle!.EmitSignal(BaseButton.SignalName.Pressed);PopoutPure();_perkPopoutShot="02-empty-open";break;
                case 3:
                    HeaderButton(_perkBody!,"Collapse perks").EmitSignal(BaseButton.SignalName.Pressed);PopoutPure();
                    HoverAssert(!_perkPanel!.Visible,"X did not collapse");_perkPopoutShot="03-empty-x-collapsed";break;
                case 4:_perkToggle!.EmitSignal(BaseButton.SignalName.Pressed);PopoutPure();_perkPopoutShot="04-empty-reopened";break;
                case 5:
                    PerkCaptureFullFixture();p=_session.CapturePerks()!;CommitEquipmentAction(new SkipPerksCommand(p.DraftAttempt,p.Cursor));
                    // Labelled long-title full-capacity presentation fixture, with derived effects synchronized.
                    p=_session.CapturePerks()!;typeof(GameSession).GetField("_perks",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(_session,p with {Equipped=[..p.Equipped.Take(4),"something-in-the-water"]});
                    typeof(GameSession).GetMethod("SynchronizePerkEffects",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(_session,[]);
                    _perksExpanded=false;RefreshPreparationHud();_perkPopoutHash=_session.CaptureSnapshot().AuthoritativeHash;_perkPopoutShot="05-five-collapsed";break;
                case 6:_perkToggle!.EmitSignal(BaseButton.SignalName.Pressed);PopoutPure();_perkPopoutShot="06-five-open";break;
                case 7:
                    HoverMovePointer(_perkPanel!.Position+new Vector2(5,5));
                    var focusCard=_ownedPerkScroll!.GetNode<HBoxContainer>("EquippedPerkCards").GetChild<Control>(4).GetChild<Control>(0);
                    focusCard.GrabFocus();PopoutPure();_perkPopoutShot="07-five-focus-effect";break;
                case 8:
                    var focusId=_session.CapturePerks()!.Equipped[4];
                    HoverAssert(_ownedEffectPopup!.Visible && _ownedEffectText!.Text==PerkCatalogue.All.Single(x=>x.Id==focusId).Effect,"Focus did not visibly expose exact catalogue effect");
                    var focusA=_ownedFocusedCard!;var hoverB=_ownedPerkScroll!.GetNode<HBoxContainer>("EquippedPerkCards").GetChild<Control>(0).GetChild<Control>(0);
                    hoverB.EmitSignal(Control.SignalName.MouseEntered);HoverAssert(_ownedEffectTarget==hoverB,"Hover did not override focus");
                    hoverB.EmitSignal(Control.SignalName.MouseExited);HoverAssert(_ownedEffectTarget==focusA,"Hover exit lost focused effect");
                    hoverB.EmitSignal(Control.SignalName.FocusExited);HoverAssert(_ownedEffectTarget==focusA,"Unrelated exit hid focus effect");
                    var last=_ownedPerkScroll!.GetNode<HBoxContainer>("EquippedPerkCards").GetChild<Control>(4);
                    HoverAssert(last.GetGlobalRect().End.X<=_ownedPerkScroll.GetGlobalRect().End.X && last.GetGlobalRect().Position.X>=_ownedPerkScroll.GetGlobalRect().Position.X && last.GetGlobalRect().End.Y<=_ownedPerkScroll.GetGlobalRect().End.Y,"Fifth card not fully accessible by scrolling");
                    SelectObject(LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.trailer-stage"));RefreshContextPanelVisibility();
                    HeaderButton(_perkBody!,"Collapse perks").EmitSignal(BaseButton.SignalName.Pressed);_perkToggle!.EmitSignal(BaseButton.SignalName.Pressed);
                    HoverAssert(!_ownedEffectPopup.Visible,"Effect popup survived collapse/reopen");
                    HoverAssert(_selected?.StableId=="farm.trailer-stage" && _contextPanel!.Visible,"Collapse/reopen cleared selection");PopoutPure();_perkPopoutShot="08-five-reopened-long-context";break;
                case 9:
                    last=_ownedPerkScroll!.GetNode<HBoxContainer>("EquippedPerkCards").GetChild<Control>(4);
                    HoverAssert(last.GetGlobalRect().End.X<=_ownedPerkScroll.GetGlobalRect().End.X && last.GetGlobalRect().Position.X>=_ownedPerkScroll.GetGlobalRect().Position.X,"Horizontal scroll not retained on reopen");
                    _session=GameSession.CreateEditableCampaign(20260922);p=_session.CapturePerks()!;CommitEquipmentAction(new ChoosePerkCommand(p.DraftAttempt,p.Cursor,p.Hand[0]));
                    _perksExpanded=false;_perkHudKey="";SelectHudTab("Programme");RefreshPreparationHud();
                    HoverAssert(!_programmeBook!.Visible,"Redundant lineup save button visible");
                    PopoutSelect(0,"act.meadow-lanterns");
                    HoverAssert(_session.PreparationPlanCost==_session.GetFestivalActs().Single(a=>a.Id=="act.meadow-lanterns").PricePennies && _preparationStart.Disabled,"Immediate subtotal/readiness wrong");
                    var autosaved=AutosaveRotation.LoadNewestValid(SaveDirectory,_saveCompatibility);
                    HoverAssert(autosaved.IsSuccess && autosaved.Session!.CaptureSnapshot().AuthoritativeHash==_session.CaptureSnapshot().AuthoritativeHash,"Immediate dropdown autosave differs from displayed state");
                    PreparationSave();PreparationLoad();HoverAssert(_session.CapturePreparationPlan()!.ActIds.SequenceEqual(new[]{"act.meadow-lanterns","",""}),"Partial save/reload changed slots");
                    _perkPopoutShot="09-immediate-partial-lineup";break;
                case 10:
                    var hash=_session.CaptureSnapshot().AuthoritativeHash;
                    _planCaptureFailureInjector=_=>throw new IOException("labelled popout save failure");PopoutSelect(0,"act.orchard-chorus");_planCaptureFailureInjector=null;
                    HoverAssert(hash==_session.CaptureSnapshot().AuthoritativeHash && _preparationSaveBlocked && _preparationMessage.Contains("labelled popout save failure"),"Save failure not explicit/pure");
                    _perkPopoutShot="10-failed-lineup-save-coherent";break;
                case 11:
                    PopoutSelect(1,"act.barnstorm-circuit");PopoutSelect(2,"act.field-frequency");
                    hash=_session.CaptureSnapshot().AuthoritativeHash;PopoutSelect(0,"act.barnstorm-circuit");HoverAssert(hash==_session.CaptureSnapshot().AuthoritativeHash,"Duplicate selection accepted");
                    PreparationAccept("staff.steward");_perkPopoutShot="11-immediate-complete-lineup";break;
                case 12:
                    var displayed=_programmeDraft.ToArray();var total=_session.PreparationPlanCost;PreparationStart();
                    HoverAssert(_session.CaptureProgramme()!.ActIds.SequenceEqual(displayed) && _session.CapturePreparation()!.SetupPayments!.Length==1,"Start differs from displayed lineup");
                    var paidHash=_session.CaptureSnapshot().AuthoritativeHash;PreparationStart();HoverAssert(paidHash==_session.CaptureSnapshot().AuthoritativeHash,"Duplicate Start mutated");
                    HoverAssert(_session.CaptureSnapshot().FestivalFinances.Single().CashPennies==80000-total,"Start charge differs from displayed plan");
                    _session.Execute(CampaignEnvelope(new SetPausedCommand(true)));
                    _perkPopoutShot="12-start-displayed-lineup-once";break;
                case 13:
                    _session=GameSession.CreateEditableCampaign(20260922);p=_session.CapturePerks()!;CommitEquipmentAction(new ChoosePerkCommand(p.DraftAttempt,p.Cursor,p.Hand[0]));
                    _perksExpanded=true;_perkHudKey="";SelectHudTab("Site & water");RefreshPreparationHud();
                    _perkPopoutHash=_session.CaptureSnapshot().AuthoritativeHash;
                    ((ScrollContainer)_hudTabs!.GetCurrentTabControl()).ScrollVertical=10000;
                    _perkPopoutShot="13-partial-site-open-scroll";break;
                case 14:
                    _popoutTab=_hudTabs!.CurrentTab;_popoutTabScroll=((ScrollContainer)_hudTabs.GetCurrentTabControl()).ScrollVertical;
                    HoverAssert(_popoutTabScroll>0,"Deep tab scroll fixture did not move");
                    GD.Print($"POPOUT_TAB_SCROLL open={_popoutTabScroll}");
                    HeaderButton(_perkBody!,"Collapse perks").EmitSignal(BaseButton.SignalName.Pressed);PopoutPure();
                    HoverAssert(_hudWorkspace!.Size.Y==Math.Min(520,GetWindow().Size.Y-150),"Workspace did not restore");
                    _perkPopoutShot="14-partial-site-closed-restored";break;
                case 15:_perkToggle!.EmitSignal(BaseButton.SignalName.Pressed);PopoutPure();_perkPopoutShot="15-partial-site-reopened";break;
                case 16:
                    HoverAssert(_hudTabs!.CurrentTab==_popoutTab && ((ScrollContainer)_hudTabs.GetCurrentTabControl()).ScrollVertical==_popoutTabScroll,"Active tab/scroll not preserved on reopen");
                    GD.Print($"POPOUT_TAB_SCROLL reopened={((ScrollContainer)_hudTabs.GetCurrentTabControl()).ScrollVertical}");
                    SelectObject(LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.farmhouse"));RefreshContextPanelVisibility();
                    _perkPopoutShot="16-partial-selected-context";break;
                case 17:
                    HeaderButton(_perkBody!,"Collapse perks").EmitSignal(BaseButton.SignalName.Pressed);PopoutPure();_perkPopoutShot="17-closed-before-user-scroll";break;
                case 18:
                    ((ScrollContainer)_hudTabs!.GetCurrentTabControl()).ScrollVertical=0;
                    _perkToggle!.EmitSignal(BaseButton.SignalName.Pressed);PopoutPure();_perkPopoutShot="18-reopened-user-scroll-retained";break;
                case 19:
                    HoverAssert(((ScrollContainer)_hudTabs!.GetCurrentTabControl()).ScrollVertical==0,"Closed tab scroll preference overwritten");
                    // Schedule A's deep restore, then replace it with a fresh B preference
                    // before either two-frame layout callback can settle.
                    ((ScrollContainer)_hudTabs.GetCurrentTabControl()).ScrollVertical=10000;
                    HeaderButton(_perkBody!,"Collapse perks").EmitSignal(BaseButton.SignalName.Pressed);
                    _perkToggle!.EmitSignal(BaseButton.SignalName.Pressed);
                    HeaderButton(_perkBody!,"Collapse perks").EmitSignal(BaseButton.SignalName.Pressed);
                    ((ScrollContainer)_hudTabs.GetCurrentTabControl()).ScrollVertical=0;
                    _perkToggle.EmitSignal(BaseButton.SignalName.Pressed);PopoutPure();
                    _perkPopoutShot="19-rapid-toggle-new-scroll";break;
                case 20:
                    HoverAssert(((ScrollContainer)_hudTabs!.GetCurrentTabControl()).ScrollVertical==0,"Stale rapid-toggle restore replaced fresh scroll");
                    ((ScrollContainer)_hudTabs.GetCurrentTabControl()).ScrollVertical=10000;
                    HeaderButton(_perkBody!,"Collapse perks").EmitSignal(BaseButton.SignalName.Pressed);_perkToggle!.EmitSignal(BaseButton.SignalName.Pressed);
                    _session=GameSession.CreateEditableCampaign(20260922);p=_session.CapturePerks()!;CommitEquipmentAction(new ChoosePerkCommand(p.DraftAttempt,p.Cursor,p.Hand[0]));
                    ((ScrollContainer)_hudTabs.GetCurrentTabControl()).ScrollVertical=0;
                    _perkPopoutHash=_session.CaptureSnapshot().AuthoritativeHash;_perkPopoutShot="20-session-replacement-new-scroll";break;
                case 21:
                    HoverAssert(((ScrollContainer)_hudTabs!.GetCurrentTabControl()).ScrollVertical==0,"Stale session restore replaced new session scroll");
                    PreparationSave();
                    HoverAssert(_preparationMessage==FestivalCopy("Preparation / live weekend saved."),"Owned tooltip load fixture failed to save normal state");
                    var savedSession=_session;
                    var loadedFocus=PerkDescendants(_ownedPerkScroll!).OfType<PanelContainer>().Single(c=>c.Name.ToString().StartsWith("Equipped_"));
                    loadedFocus.GrabFocus();HoverAssert(_ownedEffectPopup!.Visible,"Normal owned load fixture has no visible tooltip");
                    PreparationLoad();PopoutPure();HoverAssert(!ReferenceEquals(savedSession,_session) && !_ownedEffectPopup.Visible,"Successful same-cursor load did not clear visible owned tooltip");
                    GD.Print("POPOUT_LOAD normal_save_success=true session_restored=true visible_tooltip_cleared=true same_cursor=true");
                    PerkCaptureFullFixture();_perkPopoutShot="21-full-replacement-draft";break;
                case 22:
                    p=_session.CapturePerks()!;BeginPerkChoice(p.Hand[0]);
                    HoverAssert(p.Equipped.All(id=>PerkDescendants(_perkBody!).OfType<Label>().Any(l=>l.Text==PerkCatalogue.All.Single(x=>x.Id==id).Effect)),"Replacement selector omits visible loss effects");
                    _perkPopoutHash=_session.CaptureSnapshot().AuthoritativeHash;_perkPopoutShot="22-replacement-visible-choice-effects";break;
                case 23:
                    _pendingPerkReplacement=_session.CapturePerks()!.Equipped[0];_perkHudKey="";RefreshPerkHud();PopoutPure();
                    _perkPopoutShot="23-replacement-visible-gain-loss";break;
                case 24:
                    CancelPerkConfirmation();PopoutPure();HoverAssert(!_ownedEffectPopup!.Visible,"Draft cancellation retained effect popup");
                    _session=GameSession.CreateEditableCampaign(20260922);p=_session.CapturePerks()!;CommitEquipmentAction(new ChoosePerkCommand(p.DraftAttempt,p.Cursor,p.Hand[0]));
                    _perksExpanded=true;RefreshPerkHud();SelectMedicalFacility(MedicalFacility.Water,"water.main");PopoutLongContextFixture();
                    _perkPopoutHash=_session.CaptureSnapshot().AuthoritativeHash;_perkPopoutShot="24-labelled-context-deep-scroll";break;
                case 25:
                    _popoutContextScroll=_hudContextScroll!.ScrollVertical;HoverAssert(_popoutContextScroll>0,"Deep context fixture did not scroll");
                    HeaderButton(_perkBody!,"Collapse perks").EmitSignal(BaseButton.SignalName.Pressed);PopoutPure();
                    HoverAssert(_contextPanel!.Size.Y==Math.Min(380,GetWindow().Size.Y-_contextPanel.Position.Y-60),"Context normal height not restored");
                    _perkPopoutShot="25-context-closed-normal-height";break;
                case 26:_perkToggle!.EmitSignal(BaseButton.SignalName.Pressed);PopoutPure();_perkPopoutShot="26-context-reopened-same-identity";break;
                case 27:
                    HoverAssert(_hudContextScroll!.ScrollVertical==_popoutContextScroll,"Same-context deep scroll lost");
                    HeaderButton(_perkBody!,"Collapse perks").EmitSignal(BaseButton.SignalName.Pressed);
                    SelectGenerator();PopoutLongContextFixture();_hudContextScroll.ScrollVertical=0;
                    _perkToggle!.EmitSignal(BaseButton.SignalName.Pressed);PopoutPure();_perkPopoutShot="27-changed-context-closed";break;
                case 28:
                    HoverAssert(_hudContextScroll!.ScrollVertical==0,"Previous context cache replaced new closed selection scroll");
                    _hudContextScroll.ScrollVertical=10000;HeaderButton(_perkBody!,"Collapse perks").EmitSignal(BaseButton.SignalName.Pressed);
                    _perkToggle!.EmitSignal(BaseButton.SignalName.Pressed);
                    SelectMedicalFacility(MedicalFacility.Water,"water.main");PopoutLongContextFixture();_hudContextScroll.ScrollVertical=0;
                    PopoutPure();_perkPopoutShot="28-changed-context-during-restore";break;
                case 29:
                    HoverAssert(_hudContextScroll!.ScrollVertical==0,"Deferred restore changed new facility context scroll");
                    GD.Print($"POPOUT_CONTEXT same_identity_deep={_popoutContextScroll} closed_change_guard=true deferred_change_guard=true restored_height=true");
                    GD.Print($"POPOUT_CAPTURE_COMPLETE native={GetWindow().Size} empty=True five=True reopen=True selection_preserved=True pointer_occluded=True purity=True immediate_lineup=True incomplete_reload=True failed_save_coherent=True duplicate_rejected=True exact_start_once=True manual_desktop_QA=False hash={_session.CaptureSnapshot().AuthoritativeHash}");GetTree().Quit();break;
            }
        }
        catch(Exception error){GD.PushError("POPOUT_CAPTURE_FAILED "+error);GetTree().Quit(2);}
    }
}

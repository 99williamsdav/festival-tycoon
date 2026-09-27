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
            if(frame%12==11 && _perkPopoutShot is { } shot)
            {
                if(_perkPanel!.Visible && _session.CapturePerks()?.Pending!=true)
                {
                    var bounds=_perkPanel.GetGlobalRect();
                    HoverAssert(bounds.Position.X>=_hudWorkspace!.GetGlobalRect().End.X+5 && bounds.End.X<=GetWindow().Size.X-310 && bounds.End.Y<=GetWindow().Size.Y-55,$"Popout overlaps protected HUD region: panel={bounds} workspace={_hudWorkspace.GetGlobalRect()}");
                    HoverMovePointer(bounds.GetCenter());UpdateHoverFeedback(bounds.GetCenter());
                    HoverAssert(WorldInputOccluded(bounds.GetCenter()) && _hoveredColliderId==0,"Popout leaked pointer to world");
                    HoverAssert(_perkBody!.GetNode<GridContainer>("EquippedPerkCards").GetChildCount()==5,"Owned slots missing");
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
                    _perksExpanded=false;RefreshPreparationHud();_perkPopoutHash=_session.CaptureSnapshot().AuthoritativeHash;_perkPopoutShot="05-five-collapsed";break;
                case 6:_perkToggle!.EmitSignal(BaseButton.SignalName.Pressed);PopoutPure();_perkPopoutShot="06-five-open";break;
                case 7:
                    _perkScroll!.ScrollVertical=10000;PopoutPure();_perkPopoutShot="07-five-bottom-accessible";break;
                case 8:
                    var last=_perkBody!.GetNode<GridContainer>("EquippedPerkCards").GetChild<Control>(4);
                    HoverAssert(last.GetGlobalRect().End.Y<=_perkScroll!.GetGlobalRect().End.Y && last.GetGlobalRect().Position.Y>=_perkScroll.GetGlobalRect().Position.Y,"Fifth card not fully accessible by scrolling");
                    SelectObject(LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.farmhouse"));RefreshContextPanelVisibility();
                    HeaderButton(_perkBody!,"Collapse perks").EmitSignal(BaseButton.SignalName.Pressed);_perkToggle!.EmitSignal(BaseButton.SignalName.Pressed);
                    HoverAssert(_selected?.StableId=="farm.farmhouse" && _contextPanel!.Visible,"Collapse/reopen cleared selection");PopoutPure();_perkPopoutShot="08-five-reopened-selection";break;
                case 9:
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
                    GD.Print($"POPOUT_CAPTURE_COMPLETE native={GetWindow().Size} empty=True five=True reopen=True selection_preserved=True pointer_occluded=True purity=True immediate_lineup=True incomplete_reload=True failed_save_coherent=True duplicate_rejected=True exact_start_once=True manual_desktop_QA=False hash={_session.CaptureSnapshot().AuthoritativeHash}");GetTree().Quit();break;
            }
        }
        catch(Exception error){GD.PushError("POPOUT_CAPTURE_FAILED "+error);GetTree().Quit(2);}
    }
}

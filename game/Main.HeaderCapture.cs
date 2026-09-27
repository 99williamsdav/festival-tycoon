using Festival.ContentAdapter;
using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private string? _headerCaptureDirectory;
    private int _headerCaptureFrame;
    private Button? _headerCaptureButton;
    private string _headerCaptureHash="";
    private string _headerCaptureImage="";

    private static Button HeaderButton(Node panel,string tooltip) => panel.FindChildren("*","Button",true,false)
        .OfType<Button>().Single(b=>b.TooltipText.StartsWith(tooltip,StringComparison.Ordinal));
    private void HeaderAssertButton()
    {
        var button=_headerCaptureButton!;
        HoverAssert(button.IsVisibleInTree() && button.Text=="×","Header action is not rendered symbol-only");
        HoverAssert(button.TooltipText.Length>5 && button.CustomMinimumSize.X>=38 && button.CustomMinimumSize.Y>=38,"Header metadata or hit target missing");
        HoverAssert(button.Size.X>=38 && button.Size.Y>=38 && HoverUiTarget(button),"Header actual rendered target/hover failed");
        HoverAssert(!button.Disabled && button.MouseDefaultCursorShape==Control.CursorShape.PointingHand,"Header hand cursor missing");
        GD.Print($"HEADER_CONTROL symbol={button.Text} tooltip={button.TooltipText} target={button.Size.X}x{button.Size.Y} actual_hover=True");
    }
    private void HeaderAssertPure() => HoverAssert(_session.CaptureSnapshot().AuthoritativeHash==_headerCaptureHash,"Header close/reopen changed authoritative state");
    private void HeaderImage(string name)
    {
        var image=GetViewport().GetTexture().GetImage();
        HoverAssert(image.GetWidth()==GetWindow().Size.X && image.GetHeight()==GetWindow().Size.Y,"Header PNG differs from native resolution");
        HoverAssert(image.SavePng(Path.Combine(_headerCaptureDirectory!,name+".png"))==Error.Ok,"Header PNG write failed");
    }
    private void ProcessHeaderCapture()
    {
        if(_headerCaptureDirectory is null)return;
        try
        {
            var frame=++_headerCaptureFrame;
            if(_headerCaptureButton is not null)
            {
                var point=_headerCaptureButton.GetGlobalRect().GetCenter();HoverMovePointer(point);UpdateHoverFeedback(point);
            }
            if(frame%5==4 && _headerCaptureImage!=""){HeaderAssertButton();HeaderImage(_headerCaptureImage);_headerCaptureImage="";}
            if(frame%5!=0)return;
            switch(frame/5)
            {
                case 1:
                    var p=_session.CapturePerks()!;
                    var chosen=_session.Execute(CampaignEnvelope(new ChoosePerkCommand(p.DraftAttempt,p.Cursor,p.Hand[0])));
                    HoverAssert(chosen.IsAccepted,"Header capture initial ordinary perk choice failed");
                    _perksExpanded=false;_hudWorkspaceOpen=true;RefreshPreparationHud();
                    SelectObject(LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.farmhouse"));RefreshContextPanelVisibility();
                    _headerCaptureHash=_session.CaptureSnapshot().AuthoritativeHash;
                    _headerCaptureButton=HeaderButton(_contextPanel!,"Close selected object panel");_headerCaptureImage="01-context-header-symbol";
                    GD.Print("HEADER_CAPTURE_SETUP scripted_native=True viewport_motion=True close_path=Pressed_signal reopen_path=existing_handlers manual_desktop_QA=False ordinary_perk_choice=True");break;
                case 2:
                    HeaderAssertButton();_headerCaptureButton!.EmitSignal(BaseButton.SignalName.Pressed);RefreshContextPanelVisibility();
                    HoverAssert(_selected is null && !_highlight.Visible && !_contextPanel!.Visible,"Context X did not preserve ClearSelection behavior");HeaderAssertPure();
                    SelectObject(LowerWitteringFarmScenario.CreateReadModel().GetRequiredObject("farm.farmhouse"));RefreshContextPanelVisibility();
                    HoverAssert(_contextPanel!.Visible && _selected?.StableId=="farm.farmhouse","Context reopen failed");HeaderAssertPure();
                    _headerCaptureButton=HeaderButton(_hudWorkspace!,"Collapse preparation");_headerCaptureImage="02-preparation-header-symbol";break;
                case 3:
                    HeaderAssertButton();_headerCaptureButton!.EmitSignal(BaseButton.SignalName.Pressed);
                    HoverAssert(!_hudWorkspace!.Visible,"Preparation X failed");_hudPreparationToggle!.EmitSignal(BaseButton.SignalName.Pressed);
                    HoverAssert(_hudWorkspace.Visible,"Preparation reopen failed");HeaderAssertPure();
                    _perksExpanded=true;RefreshPerkHud();_headerCaptureButton=HeaderButton(_perkBody!,"Collapse perks");_headerCaptureImage="03-perks-header-symbol";break;
                case 4:
                    HeaderAssertButton();_headerCaptureButton!.EmitSignal(BaseButton.SignalName.Pressed);
                    HoverAssert(!_perkPanel!.Visible,"Perks X failed");_perkToggle!.EmitSignal(BaseButton.SignalName.Pressed);
                    HoverAssert(_perkPanel.Visible,"Perks reopen failed");HeaderAssertPure();
                    _headerCaptureButton=HeaderButton(_perkBody!,"Collapse perks");_headerCaptureImage="04-perks-reopened";break;
                case 5:
                    HeaderAssertPure();GD.Print($"HEADER_CAPTURE_COMPLETE native={GetWindow().Size.X}x{GetWindow().Size.Y} rendered_headers=3 symbol_only=True tooltip=True target38=True actual_hover_hand=True context_clear_selection=True close_reopen_pure=True hash={_headerCaptureHash} content={LowerWitteringFarmScenario.ContentCompatibilityHash}");GetTree().Quit();break;
            }
        }
        catch(Exception error){GD.PushError("HEADER_CAPTURE_FAILED "+error);_headerCaptureDirectory=null;GetTree().Quit(2);}
    }
}

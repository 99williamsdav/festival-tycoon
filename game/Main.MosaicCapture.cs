using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Festival.Game;

public partial class Main
{
    private string? _mosaicCaptureDirectory;
    private int _mosaicCaptureFrame;
    private string _mosaicHash = "";
    private void MosaicFixture(int step)
    {
        // Presentation fixtures only: R0 has no natural progression to all eight perks.
        var all = PerkCatalogue.All.Select(p => p.Id).ToArray();
        var p = _session.CapturePerks()!;
        var draft = step < 3;
        var ids = draft ? all.Skip(step * 3).Take(3).ToArray() : step < 8 ? all.Take(5).ToArray() : all.Skip(5).ToArray();
        if(draft && ids.Length == 2) ids = [..ids, all[0]];
        typeof(GameSession).GetField("_perks", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_session,
            p with { Pending = draft, Hand = draft ? ids : [], Equipped = draft ? [] : ids });
        _preparationMessage = "LABELLED MOSAIC PRESENTATION FIXTURE · catalogue artwork/layout only · no natural progression";
        _perksExpanded = true; _perkHudKey = ""; RefreshPreparationHud();
        _mosaicHash = _session.CaptureSnapshot().AuthoritativeHash;
        GD.Print($"MOSAIC_FIXTURE step={step} mode={(draft ? "draft" : "compact")} ids={string.Join(',', ids)} natural_progression=false");
    }
    private void ProcessMosaicCapture()
    {
        if(_mosaicCaptureDirectory is null) return;
        try
        {
            var frame = ++_mosaicCaptureFrame;
            var step = (frame - 1) / 12;
            if(step == 11) { GD.Print("MOSAIC_CAPTURE_COMPLETE native=true draft_all=8 compact_all=8 contain=true linear=true native_copy=true labelled_presentation_fixture=true"); GetTree().Quit(); return; }
            if(frame % 12 == 1) MosaicFixture(step);
            if(frame % 12 == 5 && step >= 3) _ownedPerkScroll!.ScrollHorizontal = (step < 8 ? step - 3 : step - 8) * 260;
            if(frame % 12 != 11) return;
            HoverAssert(_session.CaptureSnapshot().AuthoritativeHash == _mosaicHash, "Mosaic presentation changed authoritative state");
            var cards = step < 3 ? PerkDescendants(_perkBody!).OfType<PanelContainer>().Where(c => c.CustomMinimumSize == new Vector2(248,358)).ToArray()
                : _ownedPerkScroll!.GetNode<HBoxContainer>("EquippedPerkCards").GetChildren().OfType<PanelContainer>().Where(c => PerkDescendants(c).OfType<TextureRect>().Any()).ToArray();
            var ids = step < 3 ? _session.CapturePerks()!.Hand : _session.CapturePerks()!.Equipped;
            HoverAssert(cards.Length == ids.Length, "Mosaic card count mismatch");
            for(var i = 0; i < ids.Length; i++)
            {
                var card = cards[i]; var perk = PerkCatalogue.All.Single(p => p.Id == ids[i]);
                var art = PerkDescendants(card).OfType<TextureRect>().Single();
                HoverAssert(art.Texture is not null && art.Texture.ResourcePath == $"res://assets/ui/perks/{perk.Id}.png", "Wrong runtime artwork mapping");
                HoverAssert(art.Texture!.GetWidth()==1536 && art.Texture.GetHeight()==1024, "Artwork master dimensions changed");
                HoverAssert(art.StretchMode==TextureRect.StretchModeEnum.KeepAspectCentered && art.TextureFilter==CanvasItem.TextureFilterEnum.Linear, "Artwork fit/filter changed");
                var labels = PerkDescendants(card).OfType<Label>().ToArray();
                HoverAssert(labels.Any(l => l.Text==perk.Name) && labels.Any(l=>l.Text==perk.Effect), "Native catalogue copy missing");
                foreach(var label in labels) HoverAssert(label.GetLineCount()*label.GetThemeFont("font").GetHeight(label.GetThemeFontSize("font_size")) <= label.Size.Y + 2, $"Native text clipped: {perk.Id} {label.Text}");
                HoverAssert(step < 3 ? card.Size.X>=248 && card.Size.Y>=358 : card.Size.X>=250 && card.Size.Y<=160, "Card footprint changed");
                GD.Print($"MOSAIC_ASSET id={perk.Id} texture={art.Texture.ResourcePath} master=1536x1024 slot={art.Size} title={perk.Name} native_effect=true");
            }
            if(step >= 3)
            {
                var bounds = _perkPanel!.GetGlobalRect();
                HoverAssert(Math.Abs(bounds.Size.X - GetWindow().Size.X*.45f)<=1 && bounds.Size.Y<=220, "Shallow popout footprint changed");
                var target = cards[step < 8 ? step-3 : step-8].GetGlobalRect();
                var viewport = _ownedPerkScroll!.GetGlobalRect();
                HoverAssert(target.Position.X >= viewport.Position.X-1 && target.End.X<=viewport.End.X+1, "Target compact artwork/card not fully accessible");
                HoverAssert(!_hudWorkspace!.Visible || _hudWorkspace.GetGlobalRect().End.Y<=bounds.Position.Y-5,"Popout overlaps preparation");
            }
            else foreach(var card in cards) HoverAssert(_perkPanel!.GetGlobalRect().Encloses(card.GetGlobalRect()),"Draft card outside panel");
            var image = GetViewport().GetTexture().GetImage();
            HoverAssert(image.GetWidth()==GetWindow().Size.X && image.GetHeight()==GetWindow().Size.Y,"Mosaic capture not native size");
            var name = step < 3 ? $"{step+1:00}-draft-{step+1}" : $"{step+1:00}-compact-{ids[step<8 ? step-3 : step-8]}";
            HoverAssert(image.SavePng(Path.Combine(_mosaicCaptureDirectory,name+".png"))==Error.Ok,"Mosaic screenshot write failed");
            GD.Print($"MOSAIC_CAPTURE image={name} viewport={image.GetWidth()}x{image.GetHeight()} hash={_mosaicHash}");
        }
        catch(Exception error) { GD.PrintErr("MOSAIC_CAPTURE_FAILED "+error); GetTree().Quit(2); _mosaicCaptureDirectory=null; }
    }
}

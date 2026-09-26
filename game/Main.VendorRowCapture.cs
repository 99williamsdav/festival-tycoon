using Festival.Simulation;
using Godot;
using System;
using System.IO;

namespace Festival.Game;

public partial class Main
{
    private string? _vendorRowCaptureDirectory;
    private int _vendorRowCaptureFrame;
    private void ProcessVendorRowCapture()
    {
        if (_vendorRowCaptureDirectory is null) return;
        try
        {
            var frame = ++_vendorRowCaptureFrame;
            if (frame == 4)
            {
                foreach (var command in new[] { new PlaceImmersionVendorCommand("food", new(144,119),0), new PlaceImmersionVendorCommand("drinks",new(160,120),0) })
                {
                    var result = _session.Execute(CampaignEnvelope(command));
                    if (!result.IsAccepted) throw new InvalidOperationException(result.Message);
                }
                SyncImmersionWorld(); ClearSelection();
                AddChild(new Label3D { Text = "CANDIDATE ROW · ALL FRONTS +Z", Position = new Vector3(6,4,-4.25f), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 36 });
            }
            if (frame == 6) GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_vendorRowCaptureDirectory,"candidate-default.png"));
            if (frame == 7) { _focus = new Vector3(6,0,-4.25f); _camera.Size = 34; ApplyCamera(); }
            if (frame is 9 or 11 or 13 or 15)
            {
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_vendorRowCaptureDirectory,$"candidate-{OrientationNames[_orientation]}.png"));
                if (frame != 15) Rotate(1);
                else { GD.Print("VENDOR_ROW_CANDIDATE_COMPLETE normal_placement_validation=True temporary_fixture_only=True"); GetTree().Quit(); }
            }
        }
        catch (Exception error) { GD.PushError("VENDOR_ROW_CANDIDATE_FAILED " + error); GetTree().Quit(1); }
    }
}

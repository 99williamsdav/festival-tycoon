using Festival.Simulation;
using Godot;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private readonly Dictionary<string, Label3D> _faultRemarkLabels = [];

    /// <summary>Shouts from a stuck cubicle above the toilet, and a grumble above whoever broke a tap.</summary>
    private void AdvanceFaultCuePresentation()
    {
        var shown = new HashSet<string>();
        foreach (var fault in _session.CaptureFaults()?.Faults ?? [])
        {
            if (FaultRules.Remark(fault, _session.CurrentTick) is not { } text) continue;
            Vector3 position;
            if (fault.Kind == FacilityFaultKind.StuckInToilet)
            {
                if (_session.CaptureToilets().SingleOrDefault(t => t.Id == fault.FacilityId) is not { } toilet) continue;
                position = ImmersionPosition(toilet.Cell) + new Vector3(0, 3.6f, 0); // Above the sign on the roof.
            }
            else if (_attendeeVisuals.TryGetValue(new(fault.VictimId), out var body) && body.Visible) position = body.Position + new Vector3(0, 2.35f, 0);
            else continue;
            if (!_faultRemarkLabels.TryGetValue(fault.Id, out var label))
            {
                label = new Label3D { FontSize = 38, PixelSize = .009f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    Modulate = new Color("fff7e1"), OutlineSize = 14 };
                AddChild(label); _faultRemarkLabels[fault.Id] = label;
            }
            label.Text = text; label.Position = position; label.Visible = true; shown.Add(fault.Id);
        }
        foreach (var (id, label) in _faultRemarkLabels) if (!shown.Contains(id)) label.Visible = false;
    }
}

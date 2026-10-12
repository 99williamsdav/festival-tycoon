using Festival.ContentAdapter;
using Godot;
using System.Collections.Generic;

namespace Festival.Game;

public partial class Main
{
    private readonly Dictionary<string, Label3D> _stageNowPlaying = [];
    private void AttachNowPlayingLabel(string stageId, Label3D heading)
    {
        var label = BuildingName("", Vector3.Zero, 28);
        label.Name = "NowPlayingLabel"; label.Visible = false;
        // Both lines share one billboard anchor. Pixel offset stays below the heading
        // when the orthographic zoom or camera orientation changes.
        label.Offset = new Vector2(0, -48);
        heading.AddChild(label); _stageNowPlaying[stageId] = label;
    }
    private void SyncStageNowPlaying()
    {
        var menu = StartMenu.IsOpen || ResultsPaper.IsOpen || _challengeReveal.IsOpen || _fieldGuide is not null;
        var lives = FramePerformances;
        foreach (var (id, label) in _stageNowPlaying)
        {
            var text = StageNowPlayingText.Read(_session, id, lives, menu);
            if (label.Text != (text ?? "")) label.Text = text ?? "";
            label.Visible = text is not null;
        }
    }
}

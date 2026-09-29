using Festival.Simulation;
using Godot;
using System;
using System.IO;

namespace Festival.Game;

public partial class Main
{
    private string? _queueChoiceCaptureDirectory;
    private QueuedServiceChoiceFixtureResult? _queueChoiceEvidence;
    private int _queueChoiceCaptureFrames;

    private void BuildQueueChoiceCaptureLabel()
    {
        var evidence = _queueChoiceEvidence ?? throw new InvalidOperationException("Queue-choice fixture absent.");
        var layer = new CanvasLayer { Layer = 30 };
        AddChild(layer);
        var panel = HudPanel(layer, new Vector2(15, 76), new Vector2(720, 106));
        var label = HudLabel($"DEVELOPMENT FIXTURE · SHARED QUEUE CHOICE\n" +
            $"Toilets: {evidence.ToiletFrom} → {evidence.ToiletTo} · old place forfeited · owner stayed\n" +
            $"Water: {evidence.WaterFrom} → {evidence.WaterTo} · old place forfeited · owner stayed", 16);
        panel.AddChild(label);
    }

    private void ProcessQueueChoiceCapture()
    {
        if (_queueChoiceCaptureDirectory is null || ++_queueChoiceCaptureFrames < 12) return;
        try
        {
            var evidence = _queueChoiceEvidence ?? throw new InvalidOperationException("Queue-choice evidence absent.");
            if (evidence.ToiletFrom == evidence.ToiletTo || evidence.WaterFrom == evidence.WaterTo ||
                !evidence.ToiletOwnerRetained || !evidence.OldToiletPlaceForfeited ||
                !evidence.WaterOwnerRetained || !evidence.OldWaterPlaceForfeited ||
                _session.CaptureSnapshot().AuthoritativeHash != evidence.AuthoritativeHash)
                throw new InvalidOperationException("Queue-choice fixture assertions changed before capture.");
            var path = Path.Combine(_queueChoiceCaptureDirectory, "shared-queue-choice.png");
            var error = GetViewport().GetTexture().GetImage().SavePng(path);
            if (error != Error.Ok) throw new InvalidOperationException($"Could not save {path}: {error}");
            GD.Print($"QUEUE_CHOICE_CAPTURE toilet={evidence.ToiletFrom}->{evidence.ToiletTo} water={evidence.WaterFrom}->{evidence.WaterTo} " +
                $"old_places_forfeited=True owners_retained=True hash={evidence.AuthoritativeHash}");
            _queueChoiceCaptureDirectory = null;
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError("QUEUE_CHOICE_CAPTURE_FAILED " + error);
            _queueChoiceCaptureDirectory = null;
            GetTree().Quit(2);
        }
    }
}

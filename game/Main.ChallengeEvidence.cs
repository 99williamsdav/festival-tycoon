using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

public partial class Main
{
    private string? _challengeEvidenceOutput;
    private int _challengeEvidenceFrame;
    private string _challengeEvidenceHash = "";
    private string _challengeEvidencePerks = "";
    private Transform3D _challengeEvidenceCamera;

    private void ProcessChallengeEvidence()
    {
        if (_challengeEvidenceOutput is null) return;
        try { CaptureChallengeEvidence(); }
        catch (Exception error) { GD.PushError(error.ToString()); _challengeEvidenceOutput = null; GetTree().Quit(2); }
    }

    private void ChallengeCheck(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private void CaptureChallengeEvidence()
    {
        var frame = _challengeEvidenceFrame++;
        if (frame == 0)
        {
            Directory.CreateDirectory(_challengeEvidenceOutput!);
            ChallengeCheck(_challengeReveal.IsOpen, "Reveal must precede the pending perk draft");
            _challengeEvidenceHash = _session.CaptureSnapshot().AuthoritativeHash.ToString();
            _challengeEvidencePerks = JsonSerializer.Serialize(_session.CapturePerks());
            _challengeEvidenceCamera = _rig.Camera.GlobalTransform;
        }
        if (frame == 12)
        {
            ChallengeCheck(_challengeReveal.Continue!.HasFocus(), "Continue should hold keyboard focus");
            ChallengeCheck(_challengeHiddenLayers.All(item => !item.Layer.Visible), "Underlying HUD must be hidden");
            var image = _challengeReveal.Artwork!;
            ChallengeCheck(Math.Abs(image.Size.X / image.Size.Y - 1.5f) < .001f, "Art keeps full 3:2 proportions");
            ChallengeCheck(image.GetGlobalRect().End.Y < _challengeReveal.Continue!.GetGlobalRect().Position.Y, "Art and controls cannot overlap");
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_challengeEvidenceOutput!, "reveal.png"));
            _UnhandledInput(new InputEventKey { Keycode = Key.Space, Pressed = true });
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new(200, 200) });
            ChallengeCheck(_rig.Camera.GlobalTransform == _challengeEvidenceCamera, "Modal input must not move camera");
            ChallengeCheck(_challengeEvidenceHash == _session.CaptureSnapshot().AuthoritativeHash.ToString(), "No simulation/commands during reveal");
            _challengeReveal.Continue!.EmitSignal(Button.SignalName.Pressed);
            ChallengeCheck(!_challengeReveal.IsOpen && _session.CapturePreparation()!.ChallengeAcknowledged, "Continue enters preparation");
            ChallengeCheck(_challengeEvidencePerks == JsonSerializer.Serialize(_session.CapturePerks()), "Continue preserves perk draw");
            ChallengeCheck(Perks.Panel?.IsVisibleInTree() == true && _session.CapturePerks()!.Pending, "Required perk draft follows reveal");
        }
        if (frame == 24)
        {
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_challengeEvidenceOutput!, "perk-after-continue.png"));
            var hash = _session.CaptureSnapshot().AuthoritativeHash;
            BuildStartSplash(); EnterFestival(); RefreshPreparationHud();
            ChallengeCheck(!_challengeReveal.IsOpen && hash == _session.CaptureSnapshot().AuthoritativeHash, "Ordinary menu/resume must not repeat or mutate");
            var restored = GameSession.Restore(_session.CapturePersistenceSnapshot());
            ChallengeCheck(restored.IsSuccess && restored.Session!.CapturePreparation()!.ChallengeAcknowledged, "Acknowledgement survives saved-state restore");
        }
        if (frame == 36)
        {
            var tier = _session.CapturePreparation()!.Tier;
            SwitchToFestival(GameSession.CreateDevelopmentFestival(20260922, tier));
            ChallengeCheck(_challengeReveal.IsOpen && !_session.CapturePreparation()!.ChallengeAcknowledged, "Fresh festival resets reveal");
        }
        if (frame == 48)
        {
            var tier = _session.CapturePreparation()!.Tier;
            File.WriteAllText(Path.Combine(_challengeEvidenceOutput!, "challenge-evidence.json"), JsonSerializer.Serialize(new {
                passed = true, tier, challenge = FestivalChallenges.ForTier(tier)!.Id, artAspect = "3:2 uncropped",
                revealBeforePerks = true, pendingPerkPreserved = true, underlyingHudHidden = true, noSimulationOrCameraChange = true,
                continuation = true, menuResumeDoesNotRepeat = true, freshFestivalResets = true, acknowledgementRestores = true,
                keyboardFocus = true, caveat = "Scripted exported UI checks; no manual playthrough or controller hardware test. Existing tier weather simulation unchanged by reveal. Retry reset verified by simulation tests." }, new JsonSerializerOptions { WriteIndented = true }));
            GetTree().Quit();
        }
    }
}

using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;

namespace Festival.Game;

// Opt-in paused capture of Dav's lorry and hose in each of a loo's eight bays (the far flank's four have the hose mirrored),
// placed with the same pose and hose transform the live park uses. Presentation only; never opens or writes saves.
public partial class Main
{
    private string? _lavHoseEvidenceOutput;
    private int _lavHoseEvidenceFrame;
    private Node3D? _lavHoseEvidenceTruck, _lavHoseEvidenceHose;

    private void ProcessLavHoseEvidence()
    {
        if (_lavHoseEvidenceOutput is null) return;
        try { CaptureLavHoseEvidence(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(2); _lavHoseEvidenceOutput = null; }
    }

    private void CaptureLavHoseEvidence()
    {
        var frame = _lavHoseEvidenceFrame++;
        if (frame == 0)
        {
            Directory.CreateDirectory(_lavHoseEvidenceOutput!);
            void Send(SessionCommand command)
            {
                var result = _session.Execute(CampaignEnvelope(command));
                if (!result.IsAccepted) throw new InvalidOperationException($"Lav hose evidence: {command}: {result.Message}");
            }
            var perk = _session.CapturePerks()!;
            Send(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0])); Send(new UseDefaultBuildLayoutCommand());
            Send(new SetProgrammeCommand(["act.meadow-lanterns", "act.overdue-library-books", "act.glitter-rota"]));
            foreach (var candidate in _session.GetStaffCandidates().GroupBy(c => c.Role)
                         .Select(group => group.OrderBy(c => c.Traits.Length).ThenBy(c => Math.Abs(c.Grade)).First()).ToArray())
                Send(new AcceptPreparationOfferCommand(candidate.Id));
            Send(new SetPreparationStockCommand(40, 32));
            Send(new StartPreparedEditionCommand());
            _session.AdvanceWithoutSnapshot(400);
            Send(new SetPausedCommand(true)); SyncBuildWorld(); BuildAttendee();
            _foundationPresentation.Reset(_session.CaptureObservation()); _host.Clock.ResetBoundary();
            _hudWorkspaceOpen = _buildDrawerOpen = false; _boxOfficeSeenAttempt = 1; RefreshPreparationHud();
            AdvancePreparationPresentation(0);
        }
        Dock.Readiness?.Hide();
        // Eight bays, each from two sides of the camera: a shot every six frames.
        var shot = frame / 6;
        if (shot < LavSuckerRules.BaySides * 2)
        {
            var side = shot / 2;
            var toilet = _toiletViews.OrderBy(pair => pair.Key, StringComparer.Ordinal).First().Value.Body;
            if (frame % 6 == 0)
            {
                if (shot % 2 == 0)
                {
                    _lavHoseEvidenceTruck?.QueueFree(); _lavHoseEvidenceHose?.QueueFree();
                    var truck = InstantiateAsset(LavTruckAsset); truck.Name = "LavHoseEvidenceTruck"; AddChild(truck);
                    (truck.Position, truck.Rotation) = LavParkedPose(toilet, side);
                    var hose = InstantiateAsset(LavHoseAsset); hose.Name = "LavHoseEvidenceHose"; toilet.AddChild(hose);
                    hose.Transform = new Transform3D(LavBayBasis(side), Vector3.Zero);
                    (_lavHoseEvidenceTruck, _lavHoseEvidenceHose) = (truck, hose);
                }
                if (shot > 0) _rig.Rotate(2);
                _rig.Frame(toilet.Position.Lerp(_lavHoseEvidenceTruck!.Position, .35f), 9);
            }
            if (frame % 6 == 4)
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_lavHoseEvidenceOutput!,
                    $"bay{side}-{(side >= 4 ? "mirrored" : "plain")}-{_rig.OrientationName}.png"));
            return;
        }
        GD.Print("LAV_HOSE_EVIDENCE_COMPLETE"); GetTree().Quit();
    }
}

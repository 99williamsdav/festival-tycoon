using Festival.Simulation;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Festival.Game;

// Opt-in paused capture of real simulated live sets; no saved game or schedule-only label fixture.
public partial class Main
{
    private string? _nowPlayingEvidenceOutput;
    private int _nowPlayingEvidenceFrame;
    private void ProcessNowPlayingEvidence()
    {
        if (_nowPlayingEvidenceOutput is null) return;
        try { CaptureNowPlayingEvidence(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(2); _nowPlayingEvidenceOutput = null; }
    }
    private void CaptureNowPlayingEvidence()
    {
        var frame = _nowPlayingEvidenceFrame++;
        if (frame == 0)
        {
            Directory.CreateDirectory(_nowPlayingEvidenceOutput!);
            if (_stageNowPlaying.Values.Any(label => label.Visible)) throw new InvalidOperationException("Preparation must not show a playing act");
            void Send(SessionCommand command)
            {
                var result = _session.Execute(CampaignEnvelope(command));
                if (!result.IsAccepted) throw new InvalidOperationException($"Now-playing evidence: {command}: {result.Message}");
            }
            var perk = _session.CapturePerks()!;
            Send(new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0])); Send(new UseDefaultBuildLayoutCommand());
            Send(new SetProgrammeCommand(["act.meadow-lanterns", "act.overdue-library-books", "act.glitter-rota"]));
            Send(new SetProgrammeCommand(["act.low-battery", "act.unlicensed-bouncy-castle", "act.septic-tank"]) { StageId = _session.Stages[1].Id });
            var crew = _session.GetStaffCandidates().GroupBy(c => c.Role)
                .Select(group => group.OrderBy(c => c.Traits.Length).ThenBy(c => c.Grade > 0).ThenBy(c => Math.Abs(c.Grade)).First()).ToArray();
            foreach (var candidate in crew) Send(new AcceptPreparationOfferCommand(candidate.Id));
            var extra = _session.GetStaffCandidates().Where(c => c.Role == StaffRole.Sound && !crew.Any(h => h.Id == c.Id))
                .OrderBy(c => c.Traits.Length).ThenBy(c => Math.Abs(c.Grade)).First();
            Send(new AcceptPreparationOfferCommand(extra.ExtraOfferId)); Send(new SetPreparationStockCommand(40, 32));
            Send(new StartPreparedEditionCommand());
            while (_session.CaptureLivePerformances().Count(l => l.Stage == LiveSetStage.Live) != 2 && _session.CurrentTick < 20_000) _session.AdvanceWithoutSnapshot(16);
            if (_session.CaptureLivePerformances().Count(l => l.Stage == LiveSetStage.Live) != 2) throw new InvalidOperationException("No overlapping actual live sets");
            Send(new SetPausedCommand(true)); SyncBuildWorld(); BuildAttendee();
            _foundationPresentation.Reset(_session.CaptureObservation()); _host.Clock.ResetBoundary();
            _hudWorkspaceOpen = _buildDrawerOpen = false; _boxOfficeSeenAttempt = 1; RefreshPreparationHud();
            AdvancePreparationPresentation(0); SyncStageNowPlaying();
            var hash = _session.CaptureSnapshot().AuthoritativeHash;
            foreach (var stage in _session.Stages)
            {
                var label = _stageNowPlaying[stage.Id]; var expected = "Now Playing: " + _session.CurrentStageAct(stage.Id)!.Name;
                if (!label.Visible || label.Text != expected || label.FontSize >= ((Label3D)label.GetParent()).FontSize || label.Position != Vector3.Zero)
                    throw new InvalidOperationException("Active label/anchor assertion failed");
            }
            SyncStageNowPlaying();
            if (hash != _session.CaptureSnapshot().AuthoritativeHash) throw new InvalidOperationException("Label changed authoritative state");
            BuildStartSplash(); SyncStageNowPlaying();
            if (_stageNowPlaying.Values.Any(label => label.Visible)) throw new InvalidOperationException("Menu must hide all playing lines");
            StartMenu.Close(); SyncStageNowPlaying();
            if (_stageNowPlaying.Values.Any(label => !label.Visible)) throw new InvalidOperationException("Paused live acts must resume their labels");
            File.WriteAllText(Path.Combine(_nowPlayingEvidenceOutput!, "state-checks.json"), JsonSerializer.Serialize(new {
                passed = true, pausedActualSets = true, tick = _session.CurrentTick,
                labels = _stageNowPlaying.Select(p => new { stageId = p.Key, text = p.Value.Text, fontSize = p.Value.FontSize, offset = p.Value.Offset.ToString() }),
                simulationUnchanged = true, preparationHidden = true, menuHidden = true, pausedNamesRestored = true }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Dock.Readiness?.Hide();
        var index = frame / 5;
        if (index < 8)
        {
            if (frame % 5 == 0)
            {
                if (index == 4) _rig.Rotate(1);
                var second = index % 4 >= 2;
                var place = _session.Stages[1].Placement;
                _rig.Frame(second ? new(place.XMillimetres/1000f,0,place.ZMillimetres/1000f) : new(-16, 0, 11), index % 2 == 0 ? 18 : 62);
            }
            if (frame % 5 == 3) GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_nowPlayingEvidenceOutput!,
                $"{(index % 4 >= 2 ? "second-stage" : "trailer")}-{_rig.OrientationName}-{(index % 2 == 0 ? "minimum18" : "default62")}.png"));
            return;
        }
        GD.Print("NOW_PLAYING_EVIDENCE_COMPLETE"); GetTree().Quit();
    }
}

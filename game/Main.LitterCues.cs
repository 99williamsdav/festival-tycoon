using Festival.Simulation;
using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Festival.Game;

public partial class Main
{
    private readonly LitterCuePlanner _litterCuePlanner = new();
    private GameSession? _litterCueSession;
    private int _litterCueAttempt;
    private long _litterCueSampleTick = -1;
    private IReadOnlyList<LitterRemarkSituation> _litterCueSituations = [];
    private Label3D? _litterRemarkLabel;
    private bool? _litterRemarkProof;
    private bool _fullBinRemarkProved, _groundRemarkProved;

    private void AdvanceLitterCuePresentation()
    {
        if ((_litterRemarkProof ??= OS.GetCmdlineUserArgs().Contains("--capture-litter-remarks")) == true && _litterEvidenceOutput is not null)
            StageLitterRemarkProof();
        if (_litterRemarkLabel is not null) _litterRemarkLabel.Visible = false;
        var tick = _session.CurrentTick; var attempt = _session.CapturePreparation()?.Attempt ?? 0;
        if (_litterCueSession != _session || _litterCueAttempt != attempt || tick < _litterCueSampleTick)
        {
            _litterCueSession = _session; _litterCueAttempt = attempt; _litterCueSampleTick = tick;
            _litterCueSituations = _session.CaptureLitterRemarkSituations();
            _litterCuePlanner.Reset(_litterCueSituations, tick);
        }
        if (!_session.IsPaused && tick - _litterCueSampleTick >= LitterRules.SecondTicks)
        {
            _litterCueSampleTick = tick; _litterCueSituations = _session.CaptureLitterRemarkSituations();
        }
        // Waits for a gap in the crowd's three bubbles, and for any emergency to pass.
        var speechBlocked = _litterRemarkLabel?.Visible != true && SpeechCrowded() ||
            (_session.CaptureMedical()?.Needs.Any(p => p.Stage is MedicalStage.Distress or MedicalStage.Collapsed or MedicalStage.Critical) ?? false) ||
            (_session.CaptureDisorder()?.People.Any(p => p.Stage is DisorderStage.Argument or DisorderStage.Fight) ?? false);
        var cue = _litterCuePlanner.Observe(_litterCueSituations, tick, speechBlocked);
        if (cue is null || !_attendeeVisuals.TryGetValue(new(cue.AgentId), out var body) || !body.Visible ||
            _session.CapturePerson(cue.AgentId) is not { Admitted: true, Departed: false }) return;
        if (_litterRemarkLabel is null)
        {
            _litterRemarkLabel = WorldText.Speech(new Label3D {
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = MoodColour(Mood.Grumble) }, 36);
            AddChild(_litterRemarkLabel);
        }
        _litterRemarkLabel.Text = cue.Text; _litterRemarkLabel.Position = body.Position + new Vector3(0, 2.35f, 0);
        _litterRemarkLabel.Visible = true;
    }

    // Opt-in visual fixture, reusing the labelled paused litter capture. Never executes in normal play.
    private void StageLitterRemarkProof()
    {
        if (_litterEvidenceFrame is 2 or 14)
        {
            var waste = _session.CaptureLitter()!.Pieces.First(p => p.Location == WasteLocation.Carried);
            var bin = _session.CaptureBins().Single(); var side = new GridCell(bin.Cell.X + 2, bin.Cell.Z);
            var position = TraversalGrid.CellCentre(side);
            var agents = (IDictionary)typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_session)!;
            var nav = agents[new EntityId(waste.ProducerId)]!;
            void Set(string name, object value) => nav.GetType().GetProperty(name)!.SetValue(nav, value);
            Set("XMillimetres", position.XMillimetres); Set("ZMillimetres", position.ZMillimetres); Set("Destination", side);
            Set("SegmentOriginXMillimetres", position.XMillimetres); Set("SegmentOriginZMillimetres", position.ZMillimetres);
            Set("Action", _litterEvidenceFrame == 2 ? AgentNavigationAction.Arrived : AgentNavigationAction.Travelling);
            if (_litterEvidenceFrame == 2) EvidenceInvoke(_session, "SetWaste", waste with { BinId = bin.Id, Approach = side, ActionTick = 0 });
            else EvidenceInvoke(_session, "DropWaste", waste, false);
            // Labelled synthetic encounter time, no simulation advancement or save.
            var tick = _litterEvidenceFrame == 2 ? 80L : 800L;
            typeof(GameSession).GetProperty("CurrentTick")!.SetValue(_session, tick);
            _litterCueSession = _session; _litterCueAttempt = _session.CapturePreparation()!.Attempt;
            _litterCueSampleTick = tick; _litterCueSituations = _session.CaptureLitterRemarkSituations();
            _litterCuePlanner.Reset([], tick - 1);
            _foundationPresentation.Reset(_session.CaptureObservation()); _host.Clock.ResetBoundary();
        }
        if (_litterEvidenceFrame == 8)
        {
            _fullBinRemarkProved = _litterRemarkLabel?.Visible == true &&
                LitterCuePlanner.FullBinLines.Contains(_litterRemarkLabel.Text);
            SaveLitterFrame("08-full-bin-remark");
        }
        if (_litterEvidenceFrame == 20)
        {
            _groundRemarkProved = _litterRemarkLabel?.Visible == true &&
                LitterCuePlanner.GroundLines.Contains(_litterRemarkLabel.Text);
            SaveLitterFrame("09-ground-litter-remark");
        }
        if (_litterEvidenceFrame == 22)
        {
            GD.Print($"LITTER_REMARKS_EVIDENCE_COMPLETE full={_fullBinRemarkProved} ground={_groundRemarkProved}");
            GetTree().Quit(_fullBinRemarkProved && _groundRemarkProved ? 0 : 2);
        }
    }
}

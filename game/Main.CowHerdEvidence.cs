using Festival.Simulation;
using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Festival.Game;

// Explicit native gameplay fixture: starting positions only are staged. The simulation owns every later move.
public partial class Main
{
    private string? _cowHerdEvidenceOutput;
    private int _cowHerdEvidenceFrame;
    private ulong _cowHerdEvidenceWorker;
    private string _cowHerdEvidenceId="";
    private Label? _cowHerdEvidenceLabel;
    private readonly List<object> _cowHerdEvidencePositions=[];
    private void SetupCowHerdEvidence()
    {
        Directory.CreateDirectory(_cowHerdEvidenceOutput!);
        void Send(SessionCommand c) { var r=_host.Submit(c); if(!r.IsAccepted) throw new InvalidOperationException($"Cow evidence {c}: {r.Message}"); }
        var perk=_session.CapturePerks()!;
        Send(new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,perk.Hand[0])); Send(new UseDefaultBuildLayoutCommand());
        Send(new SetProgrammeCommand(["act.meadow-lanterns","act.barnstorm-circuit","act.neon-postcards"]));
        foreach(var group in _session.GetStaffCandidates().GroupBy(c=>c.Role)) Send(new AcceptPreparationOfferCommand(group.OrderBy(c=>c.Traits.Length).ThenBy(c=>c.Grade>0).First().Id));
        Send(new SetPreparationStockCommand(40,32)); Send(new StartPreparedEditionCommand());
        _session.AdvanceWithoutSnapshot(2_000);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var cows=(CowsSnapshot)typeof(GameSession).GetMethod("BreakGate",flags)!.Invoke(_session,[_session.CaptureCows()!,_session.CapturePreparation()!])!;
        var at=TraversalGrid.CellCentre(new(165,122));
        var cow=cows.Loose[0] with {XMillimetres=at.XMillimetres,ZMillimetres=at.ZMillimetres,Activity=CowActivity.Grazing,Route=[],RouteIndex=0,UntilTick=_session.CurrentTick+4_000};
        typeof(GameSession).GetField("_cows",flags)!.SetValue(_session,cows with {Loose=[cow]});
        _cowHerdEvidenceId=cow.Id; _cowHerdEvidenceWorker=_session.GetStewardResponses()[0].WorkerId;
        var agents=(IDictionary)typeof(GameSession).GetField("_navigationAgents",flags)!.GetValue(_session)!;
        var nav=agents[new EntityId(_cowHerdEvidenceWorker)]!; var start=TraversalGrid.CellCentre(new(161,122));
        void Set(string key,object value)=>nav.GetType().GetProperty(key)!.SetValue(nav,value);
        Set("XMillimetres",start.XMillimetres); Set("ZMillimetres",start.ZMillimetres); Set("SegmentOriginXMillimetres",start.XMillimetres); Set("SegmentOriginZMillimetres",start.ZMillimetres);
        Set("Route",new List<GridCell>()); Set("RouteIndex",0); Set("SegmentProgressMicrometres",0); Set("Action",AgentNavigationAction.Idle);
        Send(new HerdCowCommand(cow.Id));
        if(_session.CaptureCows()!.Loose.Single(c=>c.Id==cow.Id).HerderId!=_cowHerdEvidenceWorker) throw new InvalidOperationException("Wrong fixture worker");
        SyncBuildWorld(); BuildAttendee(); ClearSelection();
        _hudWorkspaceOpen=_buildDrawerOpen=false; _boxOfficeSeenAttempt=1; RefreshPreparationHud();
        var gate=TraversalGrid.CellCentre(CowRules.GateInside);
        _rig.Frame(new((at.XMillimetres+gate.XMillimetres)/2000f,0,at.ZMillimetres/1000f),15);
        var canvas=new CanvasLayer{Layer=95}; AddChild(canvas);
        _cowHerdEvidenceLabel=new Label{Position=new(20,76)}; _cowHerdEvidenceLabel.AddThemeFontSizeOverride("font_size",18); canvas.AddChild(_cowHerdEvidenceLabel);
    }
    private void ProcessCowHerdEvidence()
    {
        try
        {
            if(_cowHerdEvidenceFrame==0) SetupCowHerdEvidence();
            else
            {
                // The viewport is the previous rendered state. Position records were taken for precisely that state.
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_cowHerdEvidenceOutput!,$"frame-{_cowHerdEvidenceFrame-1:D4}.png"));
            }
            if(_cowHerdEvidenceFrame>=420)
            {
                var complete=!_session.CaptureCows()!.Loose.Any(c=>c.Id==_cowHerdEvidenceId);
                File.WriteAllText(Path.Combine(_cowHerdEvidenceOutput!,"positions.json"),JsonSerializer.Serialize(new{executable=OS.GetExecutablePath(),fps=20,complete,
                    fixture="One-time staged initial cow/steward positions; real HerdCow command and simulation; current native rigs; no root pulls, cow speed change or per-frame position injection.",positions=_cowHerdEvidencePositions},new JsonSerializerOptions{WriteIndented=true}));
                GetTree().Quit(complete?0:2); return;
            }
            _session.AdvanceWithoutSnapshot(4);
            _foundationPresentation.Reset(_session.CaptureObservation()); _host.Clock.ResetBoundary();
            ProcessCows(.05); AdvancePreparationPresentation(.05); ProcessServiceServers(.05);
            var nav=_session.CaptureObservation().NavigationAgents.Single(a=>a.Id.Value==_cowHerdEvidenceWorker);
            var cow=_session.CaptureCows()!.Loose.FirstOrDefault(c=>c.Id==_cowHerdEvidenceId);
            var gap=cow is null?0:Math.Sqrt(Math.Pow(nav.XMillimetres-cow.XMillimetres,2)+Math.Pow(nav.ZMillimetres-cow.ZMillimetres,2));
            var phase=cow is null?"COMPLETE · ordinary movement resumed":cow.Driving?"HERD RETURN · short lead, cow's pace":"FETCH · ordinary walking pace";
            _cowHerdEvidenceLabel!.Text=$"SCRIPTED GAMEPLAY · {phase}\nInitial positions staged once · all subsequent movement simulated · gap {gap/1000:F2} m";
            _cowHerdEvidencePositions.Add(new{frame=_cowHerdEvidenceFrame,tick=_session.CurrentTick,worker=_cowHerdEvidenceWorker,nav.XMillimetres,nav.ZMillimetres,nav.IntentId,cow,gap});
            _cowHerdEvidenceFrame++;
        }
        catch(Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
}

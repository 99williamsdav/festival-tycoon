using Festival.Simulation;
using Festival.Persistence;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Festival.Game;

public partial class Main
{
    private string? _audienceCaptureDirectory;
    private int _audienceCaptureFrame;
    private int _audienceFixtureInitialX;
    private bool _audienceRefinementCapture;
    private ulong _audienceRefinementGuest;
    private GridCell _audienceRefinementStart;

    private void AudienceCaptureImage(string name) => GetViewport().GetTexture().GetImage()
        .SavePng(Path.Combine(_audienceCaptureDirectory!, name + ".png"));

    private void AudienceCaptureRearFixture()
    {
        // Explicit development-only density setup: put the existing forty guests
        // at unique rear cells ONCE. Every subsequent step is ordinary physical motion.
        var live = _session.CaptureLivePerformance()!;
        var listeners = live.Listeners.ToArray();
        var agents = typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_session)!;
        var route = typeof(GameSession).GetMethod("ApplyAgentDestination", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var index = 0; index < listeners.Length; index++)
        {
            var cell = new GridCell(118 + index % 3 * 2, 138 + index / 3 * 2);
            if (!_session.TraversalGrid!.Get(cell).IsWalkable) throw new InvalidOperationException("Rear fixture cell is obstructed.");
            var listener = listeners[index];
            var agent = agents.GetType().GetProperty("Item")!.GetValue(agents, [new EntityId(listener.AgentId)])!;
            var centre = TraversalGrid.CellCentre(cell);
            foreach (var (name, value) in new[] { ("XMillimetres", centre.XMillimetres), ("ZMillimetres", centre.ZMillimetres),
                ("SegmentOriginXMillimetres", centre.XMillimetres), ("SegmentOriginZMillimetres", centre.ZMillimetres) })
                agent.GetType().GetProperty(name)!.SetValue(agent, value);
            route.Invoke(_session, [new EntityId(listener.AgentId), new SetAgentDestinationCommand(cell, "performance.listen"), false]);
            listeners[index] = listener with { Place = cell, AtPlace = true, LastDecisionTick = _session.CurrentTick - 800 };
        }
        typeof(GameSession).GetField("_livePerformance", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_session, live with { Listeners = listeners });
        _audienceFixtureInitialX = listeners.Sum(listener => listener.Place!.Value.X);
        ClearSelection();
        _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
        GD.Print($"AUDIENCE_CAPTURE_REAR_SETUP labelled_position_fixture=True people={listeners.Length} x_sum={_audienceFixtureInitialX} front_clear=True");
    }

    private void AudienceCapturePhysicalState(string name)
    {
        var live = _session.CaptureLivePerformance()!;
        var ids = live.Listeners.Select(listener => listener.AgentId).ToHashSet();
        var guests = _session.CaptureObservation().NavigationAgents.Where(agent => ids.Contains(agent.Id.Value)).ToArray();
        GD.Print($"AUDIENCE_CAPTURE_STATE name={name} tick={_session.CurrentTick} assigned_front={live.Listeners.Count(listener => listener.Place?.X <= 110)} physical_front={guests.Count(agent => TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres).X <= 110)} watching={live.Listeners.Count(listener => listener.AtPlace)} physical_x_sum={guests.Sum(agent => TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres).X)} hash={_session.CaptureSnapshot().AuthoritativeHash}");
    }

    private void AudienceRefinementFixture(GridCell target, GridCell[] neighbours)
    {
        // Separate labelled initial geometry for each refinement, not a normal spawn.
        var live = _session.CaptureLivePerformance()!;
        _audienceRefinementGuest = live.Listeners[0].AgentId;
        _audienceRefinementStart = target;
        var agents = typeof(GameSession).GetField("_navigationAgents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_session)!;
        var route = typeof(GameSession).GetMethod("ApplyAgentDestination", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var listeners = live.Listeners.ToArray();
        for (var index = 0; index < listeners.Length; index++)
        {
            var cell = index == 0 ? target : index <= neighbours.Length ? neighbours[index - 1] : new GridCell(180, 110 + index * 2);
            var agent = agents.GetType().GetProperty("Item")!.GetValue(agents, [new EntityId(listeners[index].AgentId)])!;
            var centre = TraversalGrid.CellCentre(cell);
            foreach (var (name, value) in new[] { ("XMillimetres", centre.XMillimetres), ("ZMillimetres", centre.ZMillimetres),
                ("SegmentOriginXMillimetres", centre.XMillimetres), ("SegmentOriginZMillimetres", centre.ZMillimetres) })
                agent.GetType().GetProperty(name)!.SetValue(agent, value);
            route.Invoke(_session, [new EntityId(listeners[index].AgentId), new SetAgentDestinationCommand(cell, "labelled.refinement-initial-layout"), false]);
            listeners[index] = listeners[index] with { Place = index == 0 ? target : null, AtPlace = index == 0,
                Enthusiasm = index == 0 ? 35 : listeners[index].Enthusiasm,
                LastDecisionTick = _session.CurrentTick - (index == 0 ? 800 : 0) };
        }
        typeof(GameSession).GetField("_livePerformance", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_session, live with { Listeners = listeners });
        _foundationPresentation.Reset(_session.CaptureObservation()); _foundationClock.ResetBoundary();
        _focus = new Vector3(-12, 0, 12); _camera.Size = 20; ApplyCamera();
        SelectAttendee(new EntityId(_audienceRefinementGuest));
        GD.Print($"AUDIENCE_REFINEMENT_SETUP labelled_geometry=True guest={_audienceRefinementGuest} start={target} neighbours={neighbours.Length}");
    }

    private void AudienceRefinementPose(NavigationObservation before, NavigationObservation after, bool expectedBackstep)
    {
        var id = after.Id;
        var prior = new Vector3(before.XMillimetres / 1000f, .04f, before.ZMillimetres / 1000f);
        var current = new Vector3(after.XMillimetres / 1000f, .04f, after.ZMillimetres / 1000f);
        var movement = current - prior;
        var backstep = _session.ShouldAudienceBackstepFacingStage(id, after.XMillimetres, after.ZMillimetres,
            after.XMillimetres - before.XMillimetres, after.ZMillimetres - before.ZMillimetres);
        if (backstep != expectedBackstep || movement.LengthSquared() < .000036f)
            throw new InvalidOperationException("Actual refinement movement did not match scoped facing.");
        var visual = _attendeeVisuals[id];
        _lastPresentedPersonPositions[id] = prior;
        UpdatePersonFacing(id, visual, current, after.Action, false, false, 1);
        var expected = expectedBackstep
            ? new Vector3(AudienceFacingMath.StageXMillimetres / 1000f - current.X, 0, AudienceFacingMath.StageZMillimetres / 1000f - current.Z)
            : movement;
        var facing = -visual.Basis.Z;
        if (facing.Normalized().Dot(expected.Normalized()) < .99f)
            throw new InvalidOperationException("Rendered refinement heading does not match actual stage/travel direction.");
        GD.Print($"AUDIENCE_REFINEMENT_FACING backstep={backstep} intent={after.IntentId} before={before.XMillimetres},{before.ZMillimetres} after={after.XMillimetres},{after.ZMillimetres} dot={facing.Normalized().Dot(expected.Normalized()):F3}");
    }

    private void ProcessAudienceRefinementCapture()
    {
        if (_audienceCaptureFrame == 8)
        {
            AudienceCaptureImage("natural-refined-forty-person-audience");
            AudienceRefinementFixture(new(103, 165), []);
        }
        if (_audienceCaptureFrame == 10)
        {
            AudienceCaptureImage("labelled-extreme-lateral-before");
            StaffCaptureAdvance(320);
            var listener = _session.CaptureLivePerformance()!.Listeners.Single(item => item.AgentId == _audienceRefinementGuest);
            if (!listener.AtPlace || listener.Place!.Value.Z >= _audienceRefinementStart.Z)
                throw new InvalidOperationException("Extreme-side listener did not physically recenter.");
            GD.Print($"AUDIENCE_REFINEMENT_LATERAL arrived=True before={_audienceRefinementStart} after={listener.Place}");
        }
        if (_audienceCaptureFrame == 12)
        {
            AudienceCaptureImage("physical-extreme-lateral-after");
            AudienceRefinementFixture(new(106, 150), [new(104,148), new(104,150), new(104,152), new(106,148), new(106,152)]);
        }
        if (_audienceCaptureFrame == 14)
        {
            AudienceCaptureImage("labelled-density-retreat-before");
            InterventionCaptureUntil(() => _session.CaptureObservation().NavigationAgents.Single(item => item.Id.Value == _audienceRefinementGuest).IntentId == "performance.listen-local-retreat", 320);
            var before = _session.CaptureObservation().NavigationAgents.Single(item => item.Id.Value == _audienceRefinementGuest);
            StaffCaptureAdvance(20);
            var after = _session.CaptureObservation().NavigationAgents.Single(item => item.Id.Value == _audienceRefinementGuest);
            AudienceRefinementPose(before, after, true);
        }
        if (_audienceCaptureFrame == 16)
        {
            AudienceCaptureImage("physical-density-retreat-stage-facing");
            var before = _session.CaptureObservation().NavigationAgents.Single(item => item.Id.Value == _audienceRefinementGuest);
            var cell = TraversalGrid.WorldToCell(before.XMillimetres, before.ZMillimetres);
            typeof(GameSession).GetMethod("ApplyAgentDestination", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_session,
                [before.Id, new SetAgentDestinationCommand(new(cell.X + 3, cell.Z), "medical.rest"), false]);
            StaffCaptureAdvance(20);
            var after = _session.CaptureObservation().NavigationAgents.Single(item => item.Id.Value == _audienceRefinementGuest);
            AudienceRefinementPose(before, after, false);
        }
        if (_audienceCaptureFrame == 18)
        {
            AudienceCaptureImage("ordinary-route-facing-reset");
            GD.Print("AUDIENCE_REFINEMENT_CAPTURE_COMPLETE lateral_physical=True retreat_stage_facing=True ordinary_route_reset=True labelled_initial_geometry=True existing_assets=True");
            GetTree().Quit();
        }
    }

    private void ProcessAudienceCapture()
    {
        if (_audienceCaptureDirectory is null) return;
        _audienceCaptureFrame++;
        if (_audienceRefinementCapture && _audienceCaptureFrame >= 8)
        {
            ProcessAudienceRefinementCapture();
            return;
        }
        if (_audienceCaptureFrame == 4)
        {
            foreach (var offer in new[] { "act.folk", "staff.steward", "equipment.buy" }) _offerButtons[offer].EmitSignal(Button.SignalName.Pressed);
            PreparationStart(); StaffCaptureSend(new EquipmentCommand(EquipmentAction.ShedLoad)); StaffCaptureAdvance(3200);
            _focus = new Vector3(-10, 0, 11); _camera.Size = 29; ApplyCamera();
            var indifferent = _session.CaptureLivePerformance()!.Listeners.First(listener => listener.Enthusiasm == 35);
            SelectAttendee(new EntityId(indifferent.AgentId));
            _preparationMessage = "LABELLED AUDIENCE DEMO • ordinary forty guests; interest affects comfort, not a distance tier."; RefreshPreparationHud();
            AudienceCapturePhysicalState("natural");
        }
        if (_audienceCaptureFrame == 6)
        {
            AudienceCaptureImage("natural-audience-comfort-inspector");
            ClearSelection();
            foreach (var layer in GetChildren().OfType<CanvasLayer>()) layer.Visible = false;
        }
        if (_audienceCaptureFrame == 8)
        {
            AudienceCaptureImage("natural-forty-person-audience");
            AudienceCaptureRearFixture();
        }
        if (_audienceCaptureFrame == 10)
        {
            AudienceCaptureImage("labelled-rear-density-before");
            StaffCaptureAdvance(960); AudienceCapturePhysicalState("density-12s");
        }
        if (_audienceCaptureFrame == 12)
        {
            AudienceCaptureImage("physical-density-after-12s");
            StaffCaptureAdvance(2400); AudienceCapturePhysicalState("density-42s");
            var saved = SaveFileAdapter.SaveSlot(SaveDirectory, "manual-audience-density", new SaveWriteRequest(_session, _saveCompatibility, "labelled-audience-density", DateTimeOffset.UtcNow));
            var loaded = SaveFileAdapter.LoadSlot(SaveDirectory, "manual-audience-density", _saveCompatibility);
            if (!saved.IsSuccess || !loaded.IsSuccess || loaded.Session!.CaptureSnapshot().AuthoritativeHash != _session.CaptureSnapshot().AuthoritativeHash)
                throw new InvalidOperationException("Audience-density save did not round-trip.");
            var continuation = GameSession.Restore(_session.CapturePersistenceSnapshot()).Session!;
            StaffCaptureAdvance(2400);
            var command = new CommandEnvelope(new CommandId(1_010_000UL + continuation.NextSubmissionSequence), continuation.CampaignId, continuation.Phase,
                continuation.CurrentTick, continuation.NextSubmissionSequence, null, new SetPausedCommand(false));
            if (!continuation.Execute(command).IsAccepted) throw new InvalidOperationException("Audience replay unpause failed.");
            continuation.AdvanceWithoutSnapshot(2400);
            command = new(new CommandId(1_010_000UL + continuation.NextSubmissionSequence), continuation.CampaignId, continuation.Phase,
                continuation.CurrentTick, continuation.NextSubmissionSequence, null, new SetPausedCommand(true));
            if (!continuation.Execute(command).IsAccepted || continuation.CaptureSnapshot().AuthoritativeHash != _session.CaptureSnapshot().AuthoritativeHash)
                throw new InvalidOperationException("Audience physical continuation diverged after reload.");
            AudienceCapturePhysicalState("density-72s");
        }
        if (_audienceCaptureFrame == 14)
        {
            AudienceCaptureImage("physical-density-after-72s");
            var live = _session.CaptureLivePerformance()!;
            var ids = live.Listeners.Select(listener => listener.AgentId).ToHashSet();
            var guests = _session.CaptureObservation().NavigationAgents.Where(agent => ids.Contains(agent.Id.Value)).ToArray();
            if (_session.PreparedStatus != PreparationStatus.Running ||
                guests.Sum(agent => TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres).X) >= _audienceFixtureInitialX ||
                guests.Count(agent => TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres).X <= 110) < 5)
                throw new InvalidOperationException("Rear density did not redistribute physically into available forward space.");
            GD.Print("AUDIENCE_CAPTURE_COMPLETE physical_redistribution=True replay=True no_teleports_after_labelled_setup=True existing_assets=True");
            GetTree().Quit();
        }
    }
}

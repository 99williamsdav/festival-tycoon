using Festival.ContentAdapter;
using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private sealed class FightView(FightChoreography state) { public FightChoreography State = state; }
    private sealed class ArgumentView(long tick, double wait) { public long Tick = tick; public double Seconds, Wait = wait; public int Gesture; }
    private readonly Dictionary<FightPairKey, FightView> _fightViews = [];
    private readonly Dictionary<ulong, ArgumentView> _argumentViews = [];
    private readonly HashSet<ulong> _fisticuffsActors = [];
    private readonly HashSet<FightPairKey> _fightBaseline = [];
    private GameSession? _fightSession;
    private int _fightAttempt;
    private bool FisticuffsPaused => CharacterPresentationPaused || StartMenu.IsOpen || ResultsPaper.IsOpen || _challengeReveal.IsOpen || _fieldGuide is not null;

    private bool FisticuffsActorFrozen(EntityId id, Node3D body) => CrowdBodies.FightOwned(body) &&
        (FisticuffsPaused || !body.IsVisibleInTree() || _fightViews.Keys.Any(pair =>
            (pair.Initiator == id.Value || pair.Opponent == id.Value) &&
            (!_attendeeVisuals.TryGetValue(new(pair.Initiator), out var a) || !a.IsVisibleInTree() ||
             !_attendeeVisuals.TryGetValue(new(pair.Opponent), out var b) || !b.IsVisibleInTree())));

    private void ResetFisticuffsPresentation()
    {
        foreach (var id in _fisticuffsActors)
            if (_attendeeVisuals.TryGetValue(new(id), out var body)) Bodies.EndFisticuffs(body);
        _fisticuffsActors.Clear(); _fightViews.Clear(); _argumentViews.Clear(); _fightBaseline.Clear();
        foreach (var pair in FightPresentation.Read(_session)) _fightBaseline.Add(pair.Key);
        _fightSession = _session; _fightAttempt = _session.CapturePreparation()?.Attempt ?? 0;
    }

    private void AdvanceFisticuffsPresentation(double delta)
    {
        if (_fisticuffsEvidenceOutput is not null && _fightEvidenceProfileDisabled) return;
        if (_fightSession != _session || _fightAttempt != (_session.CapturePreparation()?.Attempt ?? 0)) ResetFisticuffsPresentation();
        // Pause/menu freezes ownership, pending transitions, blends and facing as well as clip clocks.
        if (FisticuffsPaused)
        {
            if (_session.PreparedStatus is PreparationStatus.Failed or PreparationStatus.Finished)
            { _fightViews.Clear(); _argumentViews.Clear(); _fightBaseline.Clear(); }
            return;
        }
        var active = FightPresentation.Read(_session);
        var present = new HashSet<ulong>();
        var keys = active.Select(p => p.Key).ToHashSet();
        foreach (var stale in _fightViews.Keys.Where(k => !keys.Contains(k)).ToArray()) _fightViews.Remove(stale);
        foreach (var pair in active)
        {
            if (!_attendeeVisuals.TryGetValue(new(pair.Key.Initiator), out var a) || !_attendeeVisuals.TryGetValue(new(pair.Key.Opponent), out var b)) continue;
            present.Add(pair.Key.Initiator); present.Add(pair.Key.Opponent);
            // A hidden pair has one frozen coordinator; the visible participant cannot swing alone.
            if (!a.IsVisibleInTree() || !b.IsVisibleInTree()) continue;
            if (Bodies.FisticuffsLengths(a) is not { } first || Bodies.FisticuffsLengths(b) is not { } second) continue;
            if (!_fightViews.TryGetValue(pair.Key, out var view))
            {
                var baseline = _fightBaseline.Remove(pair.Key) || _session.CurrentTick - pair.Key.Tick > 80;
                _fightViews[pair.Key] = view = new(new(pair.Key.Initiator, pair.Key.Opponent, pair.Key.Tick, baseline));
            }
            var lengths = new FightClipLengths(Math.Max(first.Enter, second.Enter), Math.Max(first.Guard, second.Guard),
                Math.Max(first.Swing, second.Swing), Math.Max(first.Flinch, second.Flinch), Math.Max(first.StandDown, second.StandDown));
            // Reach/contact only after actual positions and opposing facing are safe.
            if (!pair.Moving && pair.Handler is null) { FaceFisticuffs(a, b.Position, delta); FaceFisticuffs(b, a.Position, delta); }
            var toward = (b.Position - a.Position).Normalized();
            var aligned = pair.Handler is not null || (-a.Transform.Basis.Z).Dot(toward) > .99 && b.Transform.Basis.Z.Dot(toward) > .99;
            var raiseHands = pair.DistanceMetres >= .7 && aligned && FisticuffsSpaceClear(pair.Key);
            var safe = !pair.Moving && raiseHands && pair.DistanceMetres <= .85;
            view.State.Advance(delta, false, pair.Handler is not null, safe, lengths);
            if (!pair.Moving)
            {
                if (pair.Handler is { } handler && _attendeeVisuals.TryGetValue(new(handler), out var steward))
                { FaceFisticuffs(a, steward.Position, delta); FaceFisticuffs(b, steward.Position, delta); }
                // Legacy very-close encounters keep lowered hands rather than overlapping raised arms.
                var low = new FightPoseSample(FightPose.StandDown, lengths.StandDown);
                Bodies.ApplyFisticuffs(a, !raiseHands ? low : view.State.First, delta);
                Bodies.ApplyFisticuffs(b, !raiseHands ? low : view.State.Second, delta);
            }
            else { Bodies.EndFisticuffs(a); Bodies.EndFisticuffs(b); }
        }
        var arguments = (_session.CaptureDisorder()?.People ?? []).Where(p => p.Stage == DisorderStage.Argument && !present.Contains(p.AgentId) &&
            _session.ImmersionHandsAvailable(p.AgentId) && _session.CaptureImmersion()?.People.FirstOrDefault(i => i.AgentId == p.AgentId)?.Held is null &&
            _session.CaptureCarriedWaste(p.AgentId) is null).ToArray();
        var argumentIds = arguments.Select(p => p.AgentId).ToHashSet();
        foreach (var stale in _argumentViews.Keys.Where(id => !argumentIds.Contains(id)).ToArray()) _argumentViews.Remove(stale);
        foreach (var arguer in arguments)
        {
            if (!_attendeeVisuals.TryGetValue(new(arguer.AgentId), out var body) || !body.IsVisibleInTree()) continue;
            if (!_argumentViews.TryGetValue(arguer.AgentId, out var view) || view.Tick != arguer.StageTick)
                _argumentViews[arguer.AgentId] = view = new(arguer.StageTick, 1 + arguer.AgentId % 5 * .37);
            view.Seconds += Math.Clamp(delta, 0, .1);
            // Only the small arguing gesture. It never enters the guarded fight choreography.
            if (view.Seconds >= view.Wait && view.Seconds < view.Wait + .75)
            { present.Add(arguer.AgentId); Bodies.ApplyArgument(body, view.Seconds - view.Wait, delta); }
            if (view.Seconds >= view.Wait + .75)
            { view.Seconds = 0; view.Gesture++; view.Wait = 2.8 + (arguer.AgentId + (ulong)view.Gesture * 17) % 8 * .31; }
        }
        foreach (var id in _fisticuffsActors.Except(present).ToArray())
            if (_attendeeVisuals.TryGetValue(new(id), out var body))
            {
                if (body.IsVisibleInTree()) Bodies.EndFisticuffs(body);
                else present.Add(id); // Drain stale encounters, retaining the hidden pose until latest activity can reclaim it.
            }
        _fisticuffsActors.Clear(); _fisticuffsActors.UnionWith(present);
    }

    private bool FisticuffsSpaceClear(FightPairKey pair)
    {
        var agents = _session.CaptureObservation().NavigationAgents;
        foreach (var id in new[] { pair.Initiator, pair.Opponent })
        {
            var actor = agents.First(n => n.Id.Value == id); var cell = TraversalGrid.WorldToCell(actor.XMillimetres, actor.ZMillimetres);
            for (var x = -1; x <= 1; x++) for (var z = -1; z <= 1; z++)
            {
                var ground = _session.TraversalGrid!.Get(new(cell.X + x, cell.Z + z));
                if (!ground.IsWalkable || ground.SlopePermille != 0) return false;
            }
            foreach (var other in agents)
            {
                if (other.Id.Value == pair.Initiator || other.Id.Value == pair.Opponent) continue;
                var dx = (long)other.XMillimetres - actor.XMillimetres; var dz = (long)other.ZMillimetres - actor.ZMillimetres;
                if (dx * dx + dz * dz < 750L * 750) return false;
            }
        }
        return true;
    }

    private static void FaceFisticuffs(Node3D body, Vector3 target, double delta)
    {
        var toward = target - body.Position;
        if (new Vector2(toward.X, toward.Z).LengthSquared() < .0001) return;
        var yaw = Mathf.Atan2(-toward.X, -toward.Z);
        body.Rotation = new(body.Rotation.X, Mathf.LerpAngle(body.Rotation.Y, yaw, Math.Min(1, (float)delta * 15)), 0);
    }
}

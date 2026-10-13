using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private const string LavTruckAsset = "res://assets/environment/lwf_honey_wagon_v1.glb";
    private const string LavHoseAsset = "res://assets/environment/lwf_honey_wagon_hose_v1.glb";
    private const float LavWheelRadius = .42f, HoseBulgeEvery = 2f, HoseBulgeTakes = .6f, HoseBulgeScale = 1.9f;
    // Where the art parks the tanker beside a loo (portaloo-local, metres), rear to the door so the hose reaches: bay 0.
    // The sim's other bays turn this round the loo a quarter at a time (1-3), or take the loo's other flank (4-7).
    private static readonly Vector3 LavParkedOffset = new(-3.4f, 0, -.9f);

    /// <summary>A bay's turn round the loo and its flank, as a loo-local transform: the art's parked spot and hose, moved there.</summary>
    private static Basis LavBayBasis(int side) =>
        new Basis(Vector3.Up, side % 4 * Mathf.Pi / 2) * Basis.FromScale(new Vector3(side >= 4 ? -1 : 1, 1, 1));

    private sealed class LavTruckView
    {
        public required Node3D Body;
        public required Node3D[] Wheels;
        public StandardMaterial3D? Beacon;
        public Node3D? Hose;
        public (Node3D Segment, Vector3 Scale)[] Segments = [];
        public Vector3 Last;
        public double Clock;
    }
    private readonly Dictionary<string, LavTruckView> _lavTrucks = [];

    private void ProcessLavSucker(double delta)
    {
        // The day finishes when the last guest is off site; a tanker still on a job then goes with the crew, out of sight.
        var calls = _session.PreparedStatus == PreparationStatus.Finished ? [] :
            _session.CaptureLavSucker()?.Calls.Where(c => c.Stage != LavSuckerStage.Gone).ToArray() ?? [];
        foreach (var gone in _lavTrucks.Keys.Where(id => calls.All(c => c.Id != id)).ToArray())
        {
            _lavTrucks[gone].Hose?.QueueFree(); _lavTrucks[gone].Body.QueueFree(); _lavTrucks.Remove(gone);
        }
        var paused = CharacterPresentationPaused;
        var step = paused ? 0 : delta;
        foreach (var call in calls)
        {
            if (!_lavTrucks.TryGetValue(call.Id, out var view))
            {
                var body = InstantiateAsset(LavTruckAsset); body.Name = "DavsLavSucker-" + call.Id; AddChild(body);
                view = new LavTruckView
                {
                    Body = body, Last = new(call.XMillimetres / 1000f, 0, call.ZMillimetres / 1000f),
                    Wheels = new[] { "FL", "FR", "RL", "RR" }.Select(w => body.FindChild("LWF_HoneyWagon_Wheel_" + w, true, false) as Node3D)
                        .OfType<Node3D>().ToArray(),
                    Beacon = BeaconLamp(body),
                };
                _lavTrucks.Add(call.Id, view);
            }
            view.Clock += step;
            var toilet = _toiletViews.GetValueOrDefault(call.ToiletId)?.Body;
            var parked = toilet is not null && (call.Stage == LavSuckerStage.Pumping ||
                call.Stage == LavSuckerStage.Arriving && call.RouteIndex >= call.Route.Length);
            if (parked)
            {
                // Snapped to its bay beside the loo (the art's spot, turned round the loo or on its other flank), whichever
                // way the loo's been turned; the lorry itself is never mirrored, only where it stands.
                view.Body.Position = toilet!.Position + toilet.Basis * (LavBayBasis(call.BaySide) * LavParkedOffset);
                view.Body.Rotation = new Vector3(0, toilet.Rotation.Y + Mathf.Pi + call.BaySide % 4 * Mathf.Pi / 2, 0);
            }
            else
            {
                var at = new Vector3(call.XMillimetres / 1000f, 0, call.ZMillimetres / 1000f);
                var moved = at - view.Last;
                // Nose first (the model faces −Z), wheels turning with the ground covered.
                if (moved.LengthSquared() > 1e-6f)
                    view.Body.Rotation = new Vector3(0, Mathf.LerpAngle(view.Body.Rotation.Y, Mathf.Atan2(-moved.X, -moved.Z), .25f), 0);
                foreach (var wheel in view.Wheels) wheel.RotateX(-moved.Length() / LavWheelRadius);
                view.Body.Position = at;
                view.Last = at;
            }
            if (parked) view.Last = view.Body.Position;
            // The orange light turns while Dav's on site; off once he's away down the road.
            if (view.Beacon is { } lamp)
            {
                var on = call.Stage != LavSuckerStage.Leaving && Mathf.Sin((float)view.Clock * 9f) > 0;
                lamp.EmissionEnergyMultiplier = on ? 4f : .2f;
            }
            ProcessLavHose(view, call.Stage == LavSuckerStage.Pumping ? toilet : null, call.BaySide);
        }
    }

    /// <summary>The hose from tanker to door while pumping, a lump sliding down it from the loo every couple of seconds.</summary>
    private void ProcessLavHose(LavTruckView view, Node3D? toilet, int side)
    {
        if (toilet is null)
        {
            if (view.Hose is not null) { view.Hose.QueueFree(); view.Hose = null; view.Segments = []; }
            return;
        }
        if (view.Hose is null)
        {
            view.Hose = InstantiateAsset(LavHoseAsset); view.Hose.Name = "LavHose"; toilet.AddChild(view.Hose);
            view.Segments = Enumerable.Range(0, 40).Select(i => view.Hose.FindChild($"LWF_Hose_Seg_{i:00}", true, false) as Node3D)
                .OfType<Node3D>().Select(n => (n, n.Scale)).ToArray();
            view.Clock = 0;
        }
        // From the loo to wherever the lorry stands: the art's hose, turned (or flipped) with the bay.
        view.Hose.Transform = new Transform3D(LavBayBasis(side), Vector3.Zero);
        var phase = (float)(view.Clock % HoseBulgeEvery) / HoseBulgeTakes;
        // Travelling from the loo end (39) to the truck end (00); a soft bump about three segments wide.
        var centre = (1 - phase) * (view.Segments.Length + 4) - 2;
        for (var i = 0; i < view.Segments.Length; i++)
        {
            var (segment, scale) = view.Segments[i];
            var d = (i - centre) / 2.2f;
            var bulge = phase <= 1 ? 1 + (HoseBulgeScale - 1) * Mathf.Exp(-d * d) : 1;
            segment.Scale = new Vector3(scale.X * bulge, scale.Y, scale.Z * bulge);
        }
    }

    private static StandardMaterial3D? BeaconLamp(Node3D truck)
    {
        foreach (var mesh in truck.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
            for (var s = 0; s < mesh.GetSurfaceOverrideMaterialCount(); s++)
                if (mesh.Mesh?.SurfaceGetMaterial(s) is StandardMaterial3D source && source.ResourceName == "LWF_HoneyWagon_BeaconLamp")
                {
                    // A copy per truck, so two tankers don't blink in step by sharing one material.
                    var lamp = (StandardMaterial3D)source.Duplicate();
                    lamp.EmissionEnabled = true;
                    if (lamp.Emission == Colors.Black) lamp.Emission = new Color("ff9a1f");
                    mesh.SetSurfaceOverrideMaterial(s, lamp);
                    return lamp;
                }
        return null;
    }
}

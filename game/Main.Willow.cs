using Godot;
using System;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private Node3D _pondWillow = null!;
    private Control? _willowEvidenceReceipt;

    private void BuildPondWillow()
    {
        _pondWillow = InstantiateAsset("res://assets/environment/pond-willow-v1/lwf_pond_willow_v1.glb");
        _pondWillow.Name = "PondWillow";
        _pondWillow.Position = new(4.5f, 0, .5f);
        _pondWillow.Rotation = new(0, Mathf.Pi, 0);
        _pondWorld.AddChild(_pondWillow);
        // Match the pond's quiet palette under the farm's stronger warm light; share one correction across both parts.
        var meshes = _pondWillow.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
        var material = (StandardMaterial3D)meshes[0].GetActiveMaterial(0).Duplicate();
        material.AlbedoColor = new Color(.6f, .6f, .6f);
        foreach (var mesh in meshes) mesh.MaterialOverride = material;
    }

    private void SetupWillowEvidence()
    {
        // Capture-only: remove the unrelated receipt covering this corner of the scene, without changing the player UI.
        _willowEvidenceReceipt = typeof(PreparationDock)
            .GetField("_receipt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(Dock) as Control;
        if (_pondWillow is null || _pondWillow.FindChildren("*", "CollisionObject3D", true, false).Count != 0 ||
            _pondWillow.FindChildren("*", "NavigationRegion3D", true, false).Count != 0 ||
            _pondWillow.FindChildren("*", "AnimationPlayer", true, false).Count != 0)
            throw new InvalidOperationException("Willow must be a delivered static decorative asset");
        var nodes = _pondWillow.FindChildren("*", "Node", true, false).Append(_pondWillow);
        if (nodes.Any(n => _pickRegistry.ContainsKey(n.GetInstanceId()) || _attendeePickRegistry.ContainsKey(n.GetInstanceId())))
            throw new InvalidOperationException("Willow registered a gameplay pick");
        var before = _pondWillow.Transform;
        ProcessPond(.1);
        if (before != _pondWillow.Transform) throw new InvalidOperationException("Static willow moved");
        var meshes = _pondWillow.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
        var surfaces = meshes.Sum(m => m.Mesh.GetSurfaceCount());
        var materials = meshes.SelectMany(m => Enumerable.Range(0, m.Mesh.GetSurfaceCount())
            .Select(i => m.GetActiveMaterial(i).GetInstanceId())).Distinct().Count();
        if (surfaces != 2 || materials != 1) throw new InvalidOperationException("Willow shared-mesh/material contract changed");
        _pondEvidenceChecks.Add(new { name = "decorative-willow", noCollisionNavigationPickingOrAnimation = true,
            localPosition = _pondWillow.Position.ToString(), yaw = _pondWillow.Rotation.Y, staticTransform = true,
            meshSurfaces = surfaces, sharedMaterials = materials });

        // Opt-in visual scale/sightline markers using existing body meshes; these are not simulation people.
        var markers = new Node3D { Name = "WillowEvidenceGuestStandIns" }; AddChild(markers);
        var points = new[] { new Vector3(19, 0, 24), new Vector3(20, 0, 28), new Vector3(24, 0, 19),
            new Vector3(28, 0, 20), new Vector3(29, 0, 25), new Vector3(25, 0, 30) };
        for (var i = 0; i < points.Length; i++)
        {
            var marker = InstantiateAsset($"res://assets/characters/lwf_attendee_{(i % 2 == 0 ? "female" : "male")}_relaxed_v2.glb");
            marker.Position = points[i]; markers.AddChild(marker);
            var label = new Label3D { Text = $"Visual stand-in {i + 1}", Position = new(0, 2, 0),
                FontSize = 24, PixelSize = .005f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled };
            marker.AddChild(label);
        }
        _pondEvidenceChecks.Add(new { name = "sightline-fixture", visualStandIns = points.Select(p => p.ToString()),
            caveat = "Existing static body meshes are visual scale markers only, not active guests or a crowd-load test." });
    }
}

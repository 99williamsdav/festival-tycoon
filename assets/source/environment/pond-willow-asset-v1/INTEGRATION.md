# Selected willow integration

8 October 2026: the approved deep-weeping v2 concept is recorded in [approval](../pond-willow-concepts-v1/APPROVAL.md). Builder selected the Designer's completed [handoff](HANDOFF.txt), editable Blender source and `runtime/lwf_pond_willow_v1.glb`; runtime SHA-256 is `6a1f072690b2ea968b083861271e456496e3fc6c4c53bad33034fe0a61474d32`.

`game/Main.Willow.cs` attaches the separate static tree once beneath the unchanged pond root, at pond-local `(4.5,0,0.5)`, yaw PI, identity scale. One shared material duplicate multiplies diffuse colour by 0.6 to preserve leaf detail under the farm's warm lighting. Palette bytes, geometry, double-sided opaque rendering and roughness remain as delivered. No breeze registration, animation, physics/navigation/picking or placement registration is added.

Original pond/source and both duck routes remain intact. The Designer's sampled clearance and camera occlusion limits apply to this exact geometry and placement. Dense water-reaching drapes intentionally hide the brown duck in South/West; no promise of complete duck or nearby guest visibility is made. The capture fixture uses labelled static guest body stand-ins for sightlines, not active simulation guests. Native captures and measured cost are recorded in `briefs/results/pond-willow.md`.

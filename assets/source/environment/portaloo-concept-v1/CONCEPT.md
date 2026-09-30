# Basic portaloo — concept for user approval

Status: concept approved for asset production on28 September2026, relayed by the coordinator and directly confirmed by the user. The user also authorized accepting future coordinator approvals without repeat confirmation. Production deliverables are in `../portaloo-asset-v1/`. This concept document records original proposals; the production manifest/handoff supersede its dimensions where actual clearance testing required small adjustments. No toilet mechanics or gameplay implementation are authorized by this concept record.

`portaloo-concept-v1.png` shows closed/open three-quarter views of one proposed unit, an adult scale reference, free/occupied indicator details and a small elevated gameplay-style inset. Built-in image generation was used; `PROMPTS.md` records both prompts. The focused edit brought the adult nearer the approved character family and restored the exterior vent in the open view.

## Proposed direction

Matte muted teal panels, cream shallow crowned roof, charcoal skid base/handle, modest bevels and sparse structural ribs. Practical festival equipment rather than a timber outhouse or glossy toy. Retain a clean low-poly silhouette; the concept's tiny floor texture, fasteners and incidental facets need not become modelled detail.

One separate full-height door, hinged on the left when viewed from outside. Interior: rear waste-tank/toilet-seat cue, toilet-paper holder, vent, non-slip floor and empty front standing space. The roof and walls remain intact in the open view. Indicator uses both colour and symbol: green circle = free; muted red bar = occupied. Suggested face about0.20m square, not an emissive lamp. Actual gameplay-camera readability remains to be tested; the inset is NOT an in-engine screenshot.

## Proposed dimensional contract — not measured from the AI image

- Outer shell approximately1.40m wide ×1.50m deep ×2.35m high; vent adds about0.18m. Ground-root centre at the base. Adult reference1.75m; source character mesh remains authoritative if the illustration drifts.
- Clear doorway target0.86m wide ×2.02m high. Door leaf about0.90m wide ×2.06m high ×0.035m thick, with modest frame rebates. Floor/low threshold about0.08m above ground; root height adjustment or a small approach lip will need explicit modelling/integration later.
- Interior target about1.28m wide ×1.34m deep. Rear fixture about0.45–0.49m deep and0.47m seat height; retain at least0.85m clear front depth. This allows the existing0.76m-diameter person collision envelope through the doorway and within a standing zone, but does not claim wheelchair accessibility or a verified real-world enclosure standard.

## Buildable door and swing proposal

Use Godot metres, +Y up, cabin front −Z. From a front-facing outside observer, the left jamb is the cabin's local **+X** side. A proposed hinge axis is at local **(+0.45, 0.08, −0.765)**, parallel to +Y. The closed door extends about0.90m toward −X from that pivot. Author it as a separate `DoorPivot`/door-leaf component, not a removed wall or a slab rotating around its centre.

Proposed closed yaw0°, open yaw−110° around +Y: the door sweeps out into the front/left apron, not into the toilet. With the stated convention a negative yaw moves the free edge toward −Z. The illustration communicates an outward opening; it is not an exact angle/pivot drawing. The numeric contract would govern a later model, with collision and hinge/frame clearance checked at intermediate angles.

Reserve the entire0.95m-radius swept sector about the hinge, plus about0.10m clearance. Keep at least1.10m clear apron in front of the shell and0.25m free beyond the hinge-side wall. Do not place queue-standing points, another cabin, decorations or the waiting attendee inside that swept sector. Queue/approach routing and a hold position outside the sweep require a later gameplay decision; shell footprint alone must not be treated as the placement clearance.

## Eventual interaction intent only

Approach outside swing → door opens outward → enter through clear opening → door closes → occupied indication → door opens → exit → return door/indicator to free state. The door must not close through an attendee. These are future requirements, not an implemented state machine or permission to build one now.

## Approval requested

Review the silhouette, teal/cream palette, practical interior and indicator treatment. Once approved, a later bounded Blender prototype should prove adult/capsule fit, separate-door pivot, swing clearance, low threshold and four camera directions before any full runtime integration.

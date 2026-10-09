# blender -b --python handspace.py -- <male|female> <out.json>
# Where the right hand sits relative to LWF_RightHand_Food over every frame of the food clips: deformed body vertices
# weighted mostly to RightHand (hand + fingers), expressed in the socket's local frame (Blender axes, Z up from the palm).
import bpy, sys, json
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]; SEX, OUTP = a[0], a[1]
O = "C:/Projects/festival-tycoon/assets/source/characters/rigged-test-v1/out/"
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
bpy.ops.import_scene.gltf(filepath=O + f"lwf_attendee_{SEX}_rigged_test_v1.glb")
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
sock = next(o for o in bpy.data.objects if o.name.startswith("LWF_RightHand_Food"))
body = next(o for o in bpy.data.objects if o.type == 'MESH' and o.vertex_groups.get("RightHand"))
gi = {g.name: g.index for g in body.vertex_groups}
hand_groups = {i for n, i in gi.items() if n.startswith("RightHand") or n.startswith("Right") and any(k in n for k in ("Thumb", "Index", "Middle", "Ring", "Little"))}
ids = [v.index for v in body.data.vertices if sum(g.weight for g in v.groups if g.group in hand_groups) > 0.5]
for t in rig.animation_data.nla_tracks: t.mute = True
res = {"verts": len(ids), "clips": {}}
for act in ("idle_food", "walk_food", "walk_brisk_food", "eat"):
    A = bpy.data.actions[next(n for n in bpy.data.actions.keys() if n == act or n.startswith(act + "_"))]
    rig.animation_data.action = A
    lo = Vector((9, 9, 9)); hi = Vector((-9, -9, -9)); f0, f1 = int(A.frame_range[0]), int(A.frame_range[1])
    for f in range(f0, f1 + 1):
        sc.frame_set(f); bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get(); ev = body.evaluated_get(dg); me = ev.to_mesh()
        inv = sock.matrix_world.inverted()
        for i in ids:
            p = inv @ (ev.matrix_world @ me.vertices[i].co)
            lo = Vector((min(lo[k], p[k]) for k in range(3))); hi = Vector((max(hi[k], p[k]) for k in range(3)))
        ev.to_mesh_clear()
    res["clips"][act] = dict(lo=[round(v, 3) for v in lo], hi=[round(v, 3) for v in hi])
json.dump(res, open(OUTP, "w"), indent=1); print("HAND", json.dumps(res))

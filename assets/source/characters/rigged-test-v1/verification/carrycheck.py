import bpy, math, json
from mathutils import Vector, Matrix
O = "C:/Projects/festival-tycoon/assets/source/characters/rigged-test-v1/out/"
MAN = json.load(open("C:/Projects/festival-tycoon/assets/source/characters/attendee-v6-draft/poses/attendee_poses_v6_manifest.json"))
for sex in ("male", "female"):
    bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
    bpy.ops.import_scene.gltf(filepath=O + f"lwf_attendee_{sex}_rigged_test_v1.glb")
    rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    sock = next(o for o in bpy.data.objects if o.name.startswith("LWF_RightHand_Cup"))
    print("SOCK", sex, sock.parent and sock.parent.name, sock.parent_type, sock.parent_bone)
    for t in rig.animation_data.nla_tracks: t.mute = True
    def at(act, fr):
        rig.animation_data.action = bpy.data.actions[next(n for n in bpy.data.actions.keys() if n.startswith(act + "_") or n == act)]
        sc.frame_set(fr); bpy.context.view_layer.update(); M = sock.matrix_world
        p = M.translation; e = M.to_euler()
        return [round(p.x, 4), round(p.z, 4), round(-p.y, 4)], [round(math.degrees(e.x), 1), round(math.degrees(e.z), 1), round(-math.degrees(e.y), 1)]
    hold = next(v for v in MAN["variants"][sex] if v["state"] == "drink_hold")["attachments"]["beer"]
    beer = next(v for v in MAN["variants"][sex] if v["state"] == "drinking" and v["product"] == "beer")["attachments"]["beer"]
    print("IDLE_CARRY f1", sex, at("idle_carry", 1), "anchor", hold["position"])
    print("WALK_CARRY f1/f14", sex, at("walk_carry", 1), at("walk_carry", 14))
    print("DRINK f36", sex, at("drink", 36), "anchor", beer["position"], beer["rotation_degrees"])
    print("NAMES", [a.name for a in bpy.data.actions])

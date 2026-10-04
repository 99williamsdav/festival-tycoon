import bpy, math, sys, bmesh, os
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index("--") + 1:]; VIEW, ACT, OUTP = a[0], a[1], a[2]
O = "C:/Projects/festival-tycoon/assets/source/characters/rigged-test-v1/out/"
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 16; sc.view_settings.view_transform = 'Standard'
w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.83, 0.88, 0.76, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.6
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sun.data.energy = 3.8; sc.collection.objects.link(sun); sun.rotation_euler = (math.radians(40), 0, math.radians(30))
g = bpy.data.objects.new("ground", bpy.data.meshes.new("g")); sc.collection.objects.link(g)
bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=60); bm.to_mesh(g.data); bm.free()
gm = bpy.data.materials.new("grass"); gm.use_nodes = True; gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.21, 0.38, 0.08, 1); g.data.materials.append(gm)
rigs = []
for row, sex in enumerate(("male", "female")):
    before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=O + f"lwf_attendee_{sex}_rigged_test_v1.glb")
    new = [o for o in bpy.data.objects if o not in before]
    rig = next(o for o in new if o.type == 'ARMATURE')
    hb = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=f"C:/Projects/festival-tycoon/game/assets/characters/lwf_hair_{sex}_default_v1.glb")
    for h in [o for o in bpy.data.objects if o not in hb]:
        mw = h.matrix_world.copy(); h.parent = rig; h.parent_type = 'BONE'; h.parent_bone = "Head"
        bpy.context.view_layer.update(); h.matrix_world = mw
    rigs.append((sex, rig))
    for t in rig.animation_data.nla_tracks if rig.animation_data else []: t.mute = True
    rig.animation_data_create(); names = sorted(n for n in bpy.data.actions.keys() if n.startswith(ACT + "_LWF") or n.startswith(ACT + "_") and n[len(ACT) + 1:].startswith("LWF")); rig.animation_data.action = bpy.data.actions[names[row]]
for sex, rig in rigs:
    acts = [x for x in bpy.data.actions if x.name.startswith(ACT)]
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam; cam.data.type = 'ORTHO'
N = 8; SPAN = 20 if ACT.startswith("walk_brisk") else 27; frames = [1 + round(i * SPAN / N) for i in range(N)] if ACT.startswith("walk") or ACT == "carry_litter" else [1 + i * 6 for i in range(N)]
sc.render.resolution_x, sc.render.resolution_y = 260, 300
tiles = []
for row, (sex, rig) in enumerate(rigs):
    others = [r for s, r in rigs if r is not rig]
    for f in frames:
        for o in bpy.data.objects:
            if o.type in ('MESH', 'ARMATURE') and o.name != "ground": o.hide_render = False
        for r in others:
            for o in [r] + list(r.children_recursive): o.hide_render = True
        sc.frame_set(f)
        if VIEW == "side": d = Vector((1, 0, 0.05)).normalized(); cam.data.ortho_scale = 2.1; tgt = Vector((0, 0, 0.88))
        else: d = Vector((50.9, -50.9, 58)).normalized(); cam.data.ortho_scale = 2.4; tgt = Vector((0, 0, 0.85))
        cam.location = tgt + d * 10; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        p = OUTP.replace(".png", f"_{sex}_{f}.png"); sc.render.filepath = p; bpy.ops.render.render(write_still=True); tiles.append(p)
open(OUTP.replace(".png", ".txt"), "w").write("\n".join(tiles))
print("ACTIONS", [a.name for a in bpy.data.actions])

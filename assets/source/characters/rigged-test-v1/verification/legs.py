import bpy, math, sys
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]; FILE, OUTP = a[0], a[1]
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 16; sc.view_settings.view_transform = 'Standard'
w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True; w.node_tree.nodes["Background"].inputs[0].default_value = (0.75, 0.84, 0.62, 1)
sun = bpy.data.objects.new("s", bpy.data.lights.new("s", 'SUN')); sun.data.energy = 3.5; sc.collection.objects.link(sun); sun.rotation_euler = (0.8, 0, 0.6)
bpy.ops.import_scene.gltf(filepath=FILE)
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
for t in rig.animation_data.nla_tracks: t.mute = True
cam = bpy.data.objects.new("c", bpy.data.cameras.new("c")); sc.collection.objects.link(cam); sc.camera = cam; cam.data.type = 'ORTHO'; cam.data.ortho_scale = 1.35
sc.render.resolution_x, sc.render.resolution_y = 300, 330
tiles = []
rig.animation_data.action = bpy.data.actions[next(k for k in bpy.data.actions.keys() if k.startswith("walk_hurry_LWF"))]
for fr in (1, 5, 9, 13):
    sc.frame_set(fr)
    for yaw in (90, 30, 200):
        d = Vector((math.sin(math.radians(yaw)), math.cos(math.radians(yaw)), 0.25)).normalized()
        cam.location = Vector((0, 0, 0.62)) + d * 5; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        p = OUTP.replace(".png", f"_{fr}_{yaw}.png"); sc.render.filepath = p; bpy.ops.render.render(write_still=True); tiles.append(p)
open(OUTP.replace(".png", ".txt"), "w").write("\n".join(tiles))

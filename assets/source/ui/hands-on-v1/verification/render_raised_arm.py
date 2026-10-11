# the installed left radio arm in its talk raise (+0.03, +0.05, +0.04 camera space), and a bigger "menu" raise
import bpy, math, sys, os
from mathutils import Vector
OUTD = sys.argv[sys.argv.index("--") + 1]; G = "C:/Projects/festival-tycoon/game/assets/characters/fp/"
for name, off in (("raised", Vector((0.03, 0.05, 0.04))), ("menu", Vector((0.07, 0.08, 0.06)))):
    bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sc.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(40), math.radians(-35), 0); sun.data.energy = 3.2; sun.data.color = (1.0, 0.95, 0.86)
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True; w.node_tree.nodes["Background"].inputs[0].default_value = (0.62, 0.70, 0.78, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 1.1
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.rotation_euler = (math.radians(90), 0, 0); cam.data.sensor_fit = 'VERTICAL'; cam.data.angle = math.radians(72); cam.data.clip_start = 0.01
    before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=G + "lwf_fp_arm_left_radio_v1.glb")
    for o in [o for o in bpy.data.objects if o not in before and o.parent is None]: o.location = Vector((off.x, -off.z, off.y))
    sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 32; sc.render.film_transparent = True
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720; sc.view_settings.view_transform = 'Standard'
    sc.render.filepath = os.path.join(OUTD, f"arm_{name}.png"); bpy.ops.render.render(write_still=True)

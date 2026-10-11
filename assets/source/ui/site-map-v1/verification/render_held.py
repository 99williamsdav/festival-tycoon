# blender -b --python render_held.py -- <out_dir> : the INSTALLED lwf_fp_arm_right_siteplan_v1.glb through the 72 deg eye camera,
# at the documented raise: e = 1 - (1 - t)^3 of REST -> 0. t = 1 (up) and t = 0.5 (half way), transparent, 1280 x 720.
import bpy, sys, os, math
from mathutils import Vector
OUTD = sys.argv[sys.argv.index("--") + 1]; G = "C:/Projects/festival-tycoon/game/assets/characters/fp/lwf_fp_arm_right_siteplan_v1.glb"
REST = Vector((0.10, -0.62, 0.12))
for name, t in (("held-up", 1.0), ("held-half", 0.5)):
    bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sc.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(40), math.radians(-35), 0); sun.data.energy = 2.3; sun.data.color = (1.0, 0.95, 0.86)
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True; w.node_tree.nodes["Background"].inputs[0].default_value = (0.62, 0.70, 0.78, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.85
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.rotation_euler = (math.radians(90), 0, 0); cam.data.sensor_fit = 'VERTICAL'; cam.data.angle = math.radians(72); cam.data.clip_start = 0.01
    before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=G)
    e = 1 - (1 - t) ** 3; off = REST * (1 - e)
    for o in [o for o in bpy.data.objects if o not in before and o.parent is None]: o.location = Vector((off.x, -off.z, off.y))
    sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 48; sc.render.film_transparent = True
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720; sc.view_settings.view_transform = 'Standard'
    sc.render.filepath = os.path.join(OUTD, f"_{name}.png"); bpy.ops.render.render(write_still=True)

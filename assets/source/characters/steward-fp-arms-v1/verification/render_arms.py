# blender -b --python render_arms.py -- <out_dir>
# Renders the INSTALLED first-person arm GLBs through the prototype camera (72 deg vertical FOV, 1280 x 720, transparent),
# at the documented blend states: idle, each action fully in, calm half way in, and calm at the rest offset (should be off screen).
import bpy, sys, os, math
from mathutils import Vector
OUTD = sys.argv[sys.argv.index("--") + 1]; G = "C:/Projects/festival-tycoon/game/assets/characters/fp/"
REST = Vector((0.12, -0.55, 0.15))                                       # INTEGRATION.md: the right arm's off-screen rest offset (camera space)
def scene():
    bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sc.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(-50), math.radians(-35), 0); sun.data.energy = 3.2; sun.data.color = (1.0, 0.95, 0.86)
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True; w.node_tree.nodes["Background"].inputs[0].default_value = (0.62, 0.70, 0.78, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 1.1
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.rotation_euler = (math.radians(90), 0, 0)                      # looks along Blender +Y = Godot -Z, up Blender +Z = Godot +Y
    cam.data.sensor_fit = 'VERTICAL'; cam.data.angle = math.radians(72); cam.data.clip_start = 0.01
    sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 32; sc.render.film_transparent = True
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720; sc.view_settings.view_transform = 'Standard'; return sc
def imp(f, offset=Vector()):
    before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=G + f)
    for o in [o for o in bpy.data.objects if o not in before and o.parent is None]:
        o.location = Vector((offset.x, -offset.z, offset.y))                  # Godot camera-space offset -> Blender (glTF import is Y-up -> Z-up)
for name, parts in (("idle", [("lwf_fp_arm_left_radio_v1.glb", 1)]),
                    ("calm", [("lwf_fp_arm_left_radio_v1.glb", 1), ("lwf_fp_arm_right_calm_v1.glb", 1)]),
                    ("calm-half", [("lwf_fp_arm_left_radio_v1.glb", 1), ("lwf_fp_arm_right_calm_v1.glb", 0.5)]),
                    ("calm-rest", [("lwf_fp_arm_left_radio_v1.glb", 1), ("lwf_fp_arm_right_calm_v1.glb", 0)]),
                    ("door", [("lwf_fp_arm_left_radio_v1.glb", 1), ("lwf_fp_arm_right_door_v1.glb", 1)]),
                    ("litter", [("lwf_fp_arm_left_radio_v1.glb", 1), ("lwf_fp_arm_right_litter_v1.glb", 1)])):
    sc = scene()
    for f, blend in parts:
        e = 1 - (1 - blend) ** 3                                                # ease-out cubic, as specified
        imp(f, REST * (1 - e))
    sc.render.filepath = os.path.join(OUTD, f"_arms_{name}.png"); bpy.ops.render.render(write_still=True)

# blender -b --python sockcam.py -- <male|female> <prop.glb> <out_prefix>
# Close views centred on LWF_RightHand_Food, four azimuths around the hand, for four food-clip frames.
import bpy, math, sys
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]; SEX, PROP, OUTP = a[0], a[1], a[2]
O = "C:/Projects/festival-tycoon/assets/source/characters/rigged-test-v1/out/"
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 16; sc.view_settings.view_transform = 'Standard'
sc.render.resolution_x, sc.render.resolution_y = 320, 260
world = bpy.data.worlds.new("w"); sc.world = world; world.color = (0.55, 0.62, 0.5)
bpy.ops.import_scene.gltf(filepath=O + f"lwf_attendee_{SEX}_rigged_test_v1.glb")
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
sock = next(o for o in bpy.data.objects if o.name.startswith("LWF_RightHand_Food"))
before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=PROP)
for o in [o for o in bpy.data.objects if o not in before and o.parent is None]:
    o.parent = sock; o.matrix_parent_inverse.identity(); o.location = (0, 0, 0); o.rotation_euler = (0, 0, 0)
for t in rig.animation_data.nla_tracks: t.mute = True
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sc.collection.objects.link(sun)
sun.rotation_euler = (math.radians(45), 0, math.radians(30)); sun.data.energy = 3
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 0.42
tiles = []
for act, fr in (("idle_food", 1), ("walk_food", 11), ("eat", 30), ("walk_brisk_food", 5)):
    rig.animation_data.action = bpy.data.actions[next(n for n in bpy.data.actions.keys() if n == act or n.startswith(act + "_"))]
    sc.frame_set(fr); bpy.context.view_layer.update()
    tgt = sock.matrix_world.translation.copy() + Vector((0, 0, -0.02))
    for az, el in ((30, 20), (120, 20), (210, 20), (300, -15)):
        r = 3; A_, E = math.radians(az), math.radians(el)
        cam.location = tgt + Vector((r * math.sin(A_) * math.cos(E), -r * math.cos(A_) * math.cos(E), r * math.sin(E)))
        cam.rotation_euler = (tgt - cam.location).to_track_quat('-Z', 'Y').to_euler()
        p = f"{OUTP}_{act}_{fr}_{az}.png"; sc.render.filepath = p; bpy.ops.render.render(write_still=True); tiles.append(p)
print("TILES", len(tiles))

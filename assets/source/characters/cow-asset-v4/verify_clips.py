# blender -b --python verify_clips.py -- <out_dir>: side views of graze and idle at four points of each loop, plus walk at four
import bpy, math, sys, os
OUTD = sys.argv[sys.argv.index("--") + 1]; os.makedirs(OUTD, exist_ok=True)
HERE = os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
bpy.ops.import_scene.gltf(filepath=os.path.join(HERE, "runtime", "lwf_cow_v4.glb"))
for o in list(bpy.data.objects):
    if o.name.startswith("Icosphere"): bpy.data.objects.remove(o)
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
for t in rig.animation_data.nla_tracks: t.mute = True
w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True; w.node_tree.nodes["Background"].inputs[0].default_value = (0.82, 0.86, 0.78, 1)
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sun.data.energy = 3.5; sc.collection.objects.link(sun); sun.rotation_euler = (math.radians(40), 0, math.radians(35))
sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 16; sc.view_settings.view_transform = 'Standard'
gm = bpy.data.materials.new("g"); gm.use_nodes = True; gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.25, 0.42, 0.14, 1)
bpy.ops.mesh.primitive_plane_add(size=20); bpy.context.object.data.materials.append(gm)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam; cam.data.type = 'ORTHO'; cam.data.ortho_scale = 3.2
cam.location = (0, -12, 0.75 + 12 * math.tan(math.radians(14))); cam.rotation_euler = (math.radians(76), 0, 0)
sc.render.resolution_x, sc.render.resolution_y = 460, 300
for clip, frames in (("graze", (1, 25, 37, 49)), ("idle", (1, 25, 49, 73)), ("walk", (1, 11, 21, 31))):
    rig.animation_data.action = next(a for a in bpy.data.actions if a.name.startswith(clip))
    for i, f in enumerate(frames):
        sc.frame_set(f); sc.render.filepath = os.path.join(OUTD, f"{clip}_{i}.png"); bpy.ops.render.render(write_still=True)

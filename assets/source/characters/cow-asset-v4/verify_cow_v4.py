# blender -b --python verify_cow_v4.py -- <out_dir>
# Independent check on the exported GLB: plays 'walk' with the root moving forward at the reported speed, measures each
# hoof's drift while planted, renders a side-view strip over a 0.25 m grid, and a game-camera strip of a gentle turn.
import bpy, math, sys, os, json
from mathutils import Vector as V, Matrix, Euler
HERE = os.path.dirname(os.path.abspath(__file__))
OUTD = sys.argv[sys.argv.index("--") + 1]; os.makedirs(OUTD, exist_ok=True)
REP = json.load(open(os.path.join(HERE, "build-report.json")))["walk"]
SPEED, CYCLE = REP["speed_m_per_s_at_1x"], REP["cycle_seconds"]; FPS = 24; N = round(CYCLE * FPS) + 1
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene; sc.render.fps = FPS
bpy.ops.import_scene.gltf(filepath=os.path.join(HERE, "runtime", "lwf_cow_v4.glb"))
for o in list(bpy.data.objects):
    if o.name.startswith("Icosphere"): bpy.data.objects.remove(o)        # importer's bone-display sphere
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
if rig.animation_data:
    for t in rig.animation_data.nla_tracks: t.mute = True
rig.animation_data_create()
act = next(a for a in bpy.data.actions if a.name == "walk" or a.name.startswith("walk_"))
rig.animation_data.action = act
print("ACTIONS", [a.name for a in bpy.data.actions])

# ---- drift: per hoof, find planted spans (sole within 3 mm of the ground, hoof level) over two cycles, measure the world drift
def hoof_points(name):
    pb = rig.pose.bones[name]; M = rig.matrix_world @ pb.matrix
    return [M @ (pb.bone.matrix_local.inverted() @ (pb.bone.head_local + V((dx, 0, -0.13)))) for dx in (-0.065, 0.105)]
OFFS = {"HindL": 0.0, "FrontL": 0.25, "HindR": 0.5, "FrontR": 0.75}; DUTY = REP["stance_share"]
res = {}
for leg, off in OFFS.items():
    stance, creep = {}, 0.0
    prev = None
    for k in range(2 * (N - 1)):
        f = 1 + k % (N - 1); t = k / FPS; ph = (k % (N - 1)) / (N - 1); lp = (ph + off) % 1.0
        rig.location = (SPEED * t, 0, 0); sc.frame_set(f); bpy.context.view_layer.update()
        heel, toe = hoof_points(leg + "Hoof")
        if lp < DUTY:                                   # planted by the clip's own timing: the contact point must not move
            key = round((t - lp * CYCLE) * FPS)          # which stance this is (its strike frame)
            u = lp / DUTY; stance.setdefault((key, u > 0.78), []).append(toe if u > 0.78 else heel)   # heel while flat, toe while rolling
        else:                                           # in swing: any ground contact that moves is creep
            low = min(heel.z, toe.z)
            if low < 0.002 and prev is not None: creep = max(creep, (V((toe.x, toe.y)) - V((prev.x, prev.y))).length)
            prev = toe if low < 0.002 else None
    drift = 0.0
    for pts in stance.values():
        drift = max(drift, max((math.hypot(a.x - b.x, a.y - b.y) for a in pts for b in pts), default=0.0))
    res[leg] = dict(stances=len({k for k, _ in stance}), max_planted_drift_mm=round(drift * 1000, 2), max_swing_ground_creep_mm=round(creep * 1000, 2),
                    max_sole_height_in_stance_mm=round(max(abs(p.z) for pts in stance.values() for p in pts) * 1000, 2))
print("DRIFT", json.dumps(res))
json.dump(dict(speed=SPEED, cycle=CYCLE, drift=res), open(os.path.join(OUTD, "walk_drift_from_glb.json"), "w"), indent=1)

# ---- scene for renders
w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True; w.node_tree.nodes["Background"].inputs[0].default_value = (0.82, 0.86, 0.78, 1)
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sun.data.energy = 3.5; sc.collection.objects.link(sun); sun.rotation_euler = (math.radians(40), 0, math.radians(35))
sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 16; sc.view_settings.view_transform = 'Standard'
gm = bpy.data.materials.new("g"); gm.use_nodes = True; gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.25, 0.42, 0.14, 1)
lm = bpy.data.materials.new("l"); lm.use_nodes = True; lm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.9, 0.9, 0.85, 1)
bpy.ops.mesh.primitive_plane_add(size=40, location=(5, 0, -0.002)); bpy.context.object.data.materials.append(gm)
for i in range(-8, 60):                                                   # 0.25 m grid lines across the walk
    bpy.ops.mesh.primitive_plane_add(size=1, location=(i * 0.25, 0, 0.0)); o = bpy.context.object; o.scale = (0.006, 3, 1); o.data.materials.append(lm)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam; cam.data.type = 'ORTHO'
sc.render.resolution_x, sc.render.resolution_y = 520, 300
tiles = []
cam.data.ortho_scale = 3.4
for i in range(8):                                                        # side view: camera follows the cow, grid stays put
    k = round(i * (N - 1) / 8); t = k / FPS; rig.location = (SPEED * t, 0, 0); rig.rotation_euler = (0, 0, 0)
    sc.frame_set(1 + k); cam.location = (SPEED * t, -12, 0.75 + 12 * math.tan(math.radians(14))); cam.rotation_euler = (math.radians(76), 0, 0)
    p = os.path.join(OUTD, f"side_{i}.png"); sc.render.filepath = p; bpy.ops.render.render(write_still=True); tiles.append(p)
# gentle turn: 20 degrees per second along an arc, seen from the game camera; 8 frames over 3 s
sc.render.resolution_x, sc.render.resolution_y = 420, 420
d = V((50.9, -50.9, 58)).normalized(); cam.data.ortho_scale = 4.2
yaw = 0.0; pos = V((0, 0, 0)); rate = math.radians(20); dt = 1 / FPS; frames = {}
for k in range(72):
    frames[k] = (pos.copy(), yaw); pos += V((math.cos(yaw), math.sin(yaw), 0)) * SPEED * dt; yaw += rate * dt
for i in range(8):
    k = i * 9; p_, y_ = frames[k]; rig.location = p_; rig.rotation_euler = (0, 0, y_)
    sc.frame_set(1 + k % (N - 1)); cam.location = p_ + V((0, 0, 0.7)) + d * 20; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    p = os.path.join(OUTD, f"turn_{i}.png"); sc.render.filepath = p; bpy.ops.render.render(write_still=True)
print("DONE")

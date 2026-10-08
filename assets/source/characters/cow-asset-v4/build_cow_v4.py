# Cow v4: the v3 mesh and rig unchanged, with production clips for loose cows.
#   blender -b --python build_cow_v4.py
# -> runtime/lwf_cow_v4.glb, source/lwf_cow_v4.blend, build-report.json
# Clips: walk (foot-planted, 0.5 m/s at 1x), graze, idle, alert (v3's Alert, unchanged).
# Rig facts (v3): forward Blender +X (Godot +X); every leg bone (Upper, Lower, Hoof) is parented to Body, not chained,
# so legs are posed in armature space: 2-bone IK against a planned hoof path, keeping each bone's rest roll.
import bpy, math, json, os
from mathutils import Vector as V, Matrix, Quaternion
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "..", "cow-asset-v3", "source", "lwf_cow_v3.blend")
os.makedirs(os.path.join(HERE, "runtime"), exist_ok=True); os.makedirs(os.path.join(HERE, "source"), exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=os.path.abspath(SRC))
sc = bpy.context.scene; sc.render.fps = 24; FPS = 24
rig = bpy.data.objects["LWF_Cow"]; arm = rig.data; body_mesh = bpy.data.objects["CowBody"]
alert_v3 = bpy.data.actions["Alert"]
rig.animation_data_create()
for t in list(rig.animation_data.nla_tracks): rig.animation_data.nla_tracks.remove(t)
REST = {b.name: b.matrix_local.copy() for b in arm.bones}
def rest_head(n): return REST[n].to_translation()
def rest_rot(n): return REST[n].to_3x3().normalized().to_4x4()
LEGS = {}
for n in ("FrontL", "FrontR", "HindL", "HindR"):
    hip, knee, ankle = rest_head(n + "Upper"), rest_head(n + "Lower"), rest_head(n + "Hoof")
    LEGS[n] = dict(hip=hip, knee=knee, ankle=ankle, L1=(knee - hip).length, L2=(ankle - knee).length, front=n.startswith("Front"))
SOLE = 0.13                   # ankle height above the flat sole
TOE, HEEL = 0.105, -0.065     # toe / heel x relative to the ankle (hoof mesh)


def reset_pose():
    for p in rig.pose.bones:
        p.rotation_mode = 'QUATERNION'; p.matrix_basis = Matrix.Identity(4)


def set_arm(name, head, delta_q):
    """armature-space pose: bone head at 'head', rotated by delta_q (armature axes) from its rest orientation"""
    rig.pose.bones[name].matrix = Matrix.Translation(head) @ delta_q.to_matrix().to_4x4() @ rest_rot(name)


def aim(name, a, b):
    """rest direction -> (b - a), keeping the bone's roll (shortest-arc delta)"""
    rd = (REST[name].to_3x3() @ V((0, 1, 0))).normalized()
    set_arm(name, a, rd.rotation_difference((b - a).normalized()))


def upd(): bpy.context.view_layer.update()


def body_pose(bob=0.0, sway=0.0, roll=0.0, pitch=0.0, yaw=0.0):
    """Body moves about the barrel centre; returns the armature-space body matrix applied to rest points"""
    piv = V((-0.12, 0.0, 1.1))
    R = (Quaternion((0, 0, 1), math.radians(yaw)) @ Quaternion((0, 1, 0), math.radians(pitch)) @ Quaternion((1, 0, 0), math.radians(roll)))
    M = Matrix.Translation(V((0, sway, bob)) + piv) @ R.to_matrix().to_4x4() @ Matrix.Translation(-piv)
    pb = rig.pose.bones["Body"]; pb.matrix = M @ REST["Body"]; upd()
    return M


def rot2(x, z, a):
    return x * math.cos(a) - z * math.sin(a), x * math.sin(a) + z * math.cos(a)


def leg_ik(n, M, sole_x, lift, pitch):
    """place leg n: the hoof's sole reference point (under the rest ankle) at body-frame x 'sole_x', height 'lift';
    pitch (radians, negative = heel up about the toe). Returns reach fraction."""
    L = LEGS[n]; y = L["ankle"].y
    if pitch > 0:                                          # toe up: rolling over the heel, the heel stays put
        hx, hz = rot2(-HEEL, SOLE, pitch); ankle = V((sole_x + HEEL + hx, y, lift + hz))
    else:                                                  # heel up: rolling over the toe, the toe stays put
        hx, hz = rot2(-TOE, SOLE, pitch); ankle = V((sole_x + TOE + hx, y, lift + hz))
    hip = M @ L["hip"]
    d = ankle - hip; D = d.length; reach = D / (L["L1"] + L["L2"])
    if D > (L["L1"] + L["L2"]) * 0.999:
        d = d * ((L["L1"] + L["L2"]) * 0.999 / D); ankle = hip + d; D = d.length
    a = (L["L1"] ** 2 + D * D - L["L2"] ** 2) / (2 * L["L1"] * D); a = max(-1.0, min(1.0, a))
    ang = math.acos(a)
    dn = d.normalized(); fwd = V((1, 0, 0)); side_ax = dn.cross(fwd).cross(dn).normalized()   # in-plane perpendicular, pointing +x-ish
    bend = side_ax if L["front"] else -side_ax                                               # front carpus forward, hind hock back
    knee = hip + dn * (L["L1"] * math.cos(ang)) + bend * (L["L1"] * math.sin(ang))
    aim(n + "Upper", hip, knee); aim(n + "Lower", knee, ankle)
    hd = V((math.cos(pitch), 0, math.sin(pitch)))
    aim(n + "Hoof", ankle, ankle + hd * 0.12)
    return reach


def head_pose(neck_pitch=0.0, head_pitch=0.0, head_yaw=0.0, neck_yaw=0.0, ears=(0.0, 0.0), tail=(0.0, 0.0, 0.0)):
    """degrees; pitch + = down. Rotations about each bone's head in armature axes, applied down the chain"""
    Mb = rig.pose.bones["Body"].matrix @ REST["Body"].inverted()
    nh = Mb @ rest_head("Neck")
    qb = Mb.to_quaternion()
    qn = Quaternion((0, 0, 1), math.radians(neck_yaw)) @ Quaternion((0, 1, 0), math.radians(neck_pitch))
    set_arm("Neck", nh, qb @ qn); upd()
    Mn = rig.pose.bones["Neck"].matrix @ REST["Neck"].inverted()
    hh = Mn @ rest_head("Head")
    qh = Mn.to_quaternion() @ Quaternion((0, 0, 1), math.radians(head_yaw)) @ Quaternion((0, 1, 0), math.radians(head_pitch))
    set_arm("Head", hh, qh); upd()
    Mh = rig.pose.bones["Head"].matrix @ REST["Head"].inverted()
    for e, a in (("EarL", ears[0]), ("EarR", ears[1])):
        set_arm(e, Mh @ rest_head(e), Mh.to_quaternion() @ Quaternion((1, 0, 0), math.radians(a)))
    th = Mb @ rest_head("TailBase")
    qt = qb @ Quaternion((0, 0, 1), math.radians(tail[0])) @ Quaternion((1, 0, 0), math.radians(tail[1]))
    set_arm("TailBase", th, qt); upd()
    Mt = rig.pose.bones["TailBase"].matrix @ REST["TailBase"].inverted()
    set_arm("TailTip", Mt @ rest_head("TailTip"), Mt.to_quaternion() @ Quaternion((1, 0, 0), math.radians(tail[2])))
    upd()


def key_all(frame):
    for p in rig.pose.bones:
        p.keyframe_insert("location", frame=frame); p.keyframe_insert("rotation_quaternion", frame=frame); p.keyframe_insert("scale", frame=frame)


def new_action(name):
    reset_pose(); act = bpy.data.actions.new(name); rig.animation_data.action = act; return act


def smooth(t): t = max(0.0, min(1.0, t)); return t * t * (3 - 2 * t)


# ================================================================ walk
SPEED = 0.50                                   # m/s at 1x
N = 41; CYCLE = (N - 1) / FPS                  # 40 intervals = 1.667 s for one full cycle (four footfalls)
STRIDE = SPEED * CYCLE                         # metres per cycle
DUTY = 0.66                                    # stance share of each leg's cycle (a walk: 2-3 feet always down)
S = STRIDE * DUTY                              # distance a planted hoof travels back under the body
ROCK = math.radians(28); HEEL_UP = math.radians(14); LIFT_F, LIFT_H = 0.11, 0.09
OFFS = {"HindL": 0.0, "FrontL": 0.25, "HindR": 0.5, "FrontR": 0.75}    # lateral-sequence walk: LH, LF, RH, RF


def foot(n, lp):
    """sole x offset from the rest ankle, lift, pitch at leg phase lp (0 = hoof strike)"""
    L = LEGS[n]; lift_max = LIFT_F if L["front"] else LIFT_H
    if lp < DUTY:
        u = lp / DUTY; x = S / 2 - S * u
        pitch = -ROCK * ((u - 0.78) / 0.22) ** 1.3 if u > 0.78 else (HEEL_UP * (1 - u / 0.15) if u < 0.15 else 0.0)
        return x, 0.0, pitch
    w = (lp - DUTY) / (1 - DUTY); e = smooth(w)
    x = -S / 2 + S * e
    lift = lift_max * math.sin(math.pi * w) ** 0.9                       # zero exactly at strike; clears the ground quickly at toe-off
    pitch = -ROCK * (1 - smooth(w * 1.6)) + HEEL_UP * smooth((w - 0.45) / 0.55)        # toe-up for a heel strike
    return x, lift, pitch


def body_walk(ph):
    t = 2 * math.pi * ph
    bob = BOB0 + 0.012 * math.cos(2 * t)                                   # two dips per cycle, as each pair bears weight
    sway = 0.018 * math.sin(t - 0.6)                                       # weight shifts towards the side in stance
    return dict(bob=bob, sway=sway, roll=1.6 * math.sin(t - 0.6), pitch=0.8 * math.sin(2 * t + 0.4), yaw=1.2 * math.sin(t))


def walk_frame(f, apply=True):
    ph = f / (N - 1)
    M = body_walk(ph); Mb = body_pose(**M)
    reach = 0.0
    for n, off in OFFS.items():
        lp = (ph + off) % 1.0; x, lift, pitch = foot(n, lp)
        sole_x = LEGS[n]["ankle"].x + x
        reach = max(reach, leg_ik(n, Mb, sole_x, lift, pitch))
    nod = 4.0 * math.cos(4 * math.pi * (ph - 0.25))                       # head nods with each foreleg strike (LF 0.25, RF 0.75)
    head_pose(neck_pitch=14 + nod, head_pitch=-3 - 0.4 * nod, head_yaw=1.5 * math.sin(2 * math.pi * ph),
              ears=(4 * math.sin(2 * math.pi * ph), -4 * math.sin(2 * math.pi * ph + 1)),
              tail=(6 * math.sin(2 * math.pi * ph), 0.0, 4 * math.sin(2 * math.pi * ph - 0.8)))
    return reach


# lower the body until every pose is reachable (the legs are nearly straight at rest)
BOB0 = -0.02
new_action("walk_probe")
while True:
    worst = max(walk_frame(f) for f in range(0, N - 1, 2))
    if worst <= 0.985 or BOB0 < -0.12: break
    BOB0 -= 0.005
bpy.data.actions.remove(bpy.data.actions["walk_probe"])
walk = new_action("walk")
for f in range(N): walk_frame(f); key_all(f + 1)

# slide check: world sole points of planted hooves with the root moving forward at SPEED
slides = {}; reach_max = 0.0
for n, off in OFFS.items():
    pts = []
    for f in range(N - 1):
        ph = f / (N - 1); lp = (ph + off) % 1.0
        if lp >= DUTY * 0.97: continue
        sc.frame_set(f + 1); upd()
        pb = rig.pose.bones[n + "Hoof"]; Mh = rig.matrix_world @ pb.matrix
        toe = Mh @ (pb.bone.matrix_local.inverted() @ (rest_head(n + "Hoof") + V((TOE, 0, -SOLE))))
        heel = Mh @ (pb.bone.matrix_local.inverted() @ (rest_head(n + "Hoof") + V((HEEL, 0, -SOLE))))
        u = lp / DUTY; p = toe if u > 0.78 else heel                    # the contact that is meant to be fixed
        pts.append((round(u, 3), V((p.x + SPEED * (lp * CYCLE), p.y, p.z))))       # time since this hoof struck, unwrapped
    us = [u for u, _ in pts]
    ref_heel = [p for u, p in pts if u <= 0.78]; ref_toe = [p for u, p in pts if u > 0.78]
    def spread(ps): return max(((a - b).length for a in ps for b in ps), default=0.0)
    slides[n] = dict(heel_mm=round(spread(ref_heel) * 1000, 1), toe_mm=round(spread(ref_toe) * 1000, 1),
                     sole_height_mm=round(max((abs(p.z) for _, p in pts), default=0) * 1000, 1))

# ================================================================ graze (4 s loop): head down cropping, tail flicks
GN = 97
graze = new_action("graze")
for f in range(GN):
    t = f / (GN - 1); w = 2 * math.pi * t
    Mb = body_pose(bob=-0.012, pitch=2.5 + 0.3 * math.sin(w), roll=0.4 * math.sin(w))
    for n in LEGS: leg_ik(n, Mb, LEGS[n]["ankle"].x, 0.0, 0.0)
    tug = max(0.0, math.sin(2 * math.pi * 4 * t)) ** 3                  # four bites a loop: a quick upward tug
    flick = math.exp(-((t - 0.38) / 0.035) ** 2) - 0.8 * math.exp(-((t - 0.45) / 0.03) ** 2) + 0.9 * math.exp(-((t - 0.82) / 0.035) ** 2)
    head_pose(neck_pitch=82 - 6 * tug + 2 * math.sin(w), head_pitch=-30 + 3 * tug, head_yaw=6 * math.sin(2 * w), neck_yaw=4 * math.sin(w + 0.5),
              ears=(10 + 6 * math.sin(3 * w), 8 - 6 * math.sin(3 * w + 1)),
              tail=(28 * flick + 3 * math.sin(w), 0.0, 18 * flick))
    key_all(f + 1)

# ================================================================ idle (4 s loop): head up, chewing the cud
IN = 97
idle = new_action("idle")
for f in range(IN):
    t = f / (IN - 1); w = 2 * math.pi * t
    Mb = body_pose(bob=0.004 * math.sin(2 * w) - 0.004, roll=0.3 * math.sin(w))   # breathing
    for n in LEGS: leg_ik(n, Mb, LEGS[n]["ankle"].x, 0.0, 0.0)
    chew = math.sin(2 * math.pi * 6 * t)                                     # six chews a loop, a sideways grind
    twitch = math.exp(-((t - 0.6) / 0.03) ** 2)
    head_pose(neck_pitch=2 + 1.0 * math.sin(w), head_pitch=1.5 * abs(chew), head_yaw=3.0 * chew,
              ears=(6 + 25 * twitch, 4 - 3 * math.sin(w)), tail=(4 * math.sin(w), 0.0, 3 * math.sin(w - 1)))
    key_all(f + 1)

# ================================================================ alert: v3's Alert, kept, renamed for the clip convention
alert = alert_v3.copy(); alert.name = "alert"
for a in list(bpy.data.actions):
    if a.name in ("Alert", "Idle", "WalkPreview"): bpy.data.actions.remove(a)
rig.animation_data.action = None
for act in (idle, walk, graze, alert):
    act.use_fake_user = True; tr = rig.animation_data.nla_tracks.new(); tr.name = act.name; tr.strips.new(act.name, 1, act); tr.mute = True
reset_pose(); sc.frame_set(1)
rig["forward"] = "Blender +X / Godot +X"
rig["walk"] = f"foot-planted in place: move the root forward at {SPEED} m/s at 1x ({STRIDE:.4f} m per {CYCLE:.4f} s cycle)"
bpy.ops.object.select_all(action='DESELECT'); rig.select_set(True); body_mesh.select_set(True); bpy.context.view_layer.objects.active = rig
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "source", "lwf_cow_v4.blend"))
bpy.ops.export_scene.gltf(filepath=os.path.join(HERE, "runtime", "lwf_cow_v4.glb"), export_format='GLB', use_selection=True,
                          export_animations=True, export_animation_mode='ACTIONS', export_force_sampling=True, export_yup=True)
report = dict(base="cow v3 mesh, rig, weights and palette unchanged", forward="Godot +X", fps=FPS,
              animations={"walk": f"{N} frames ({CYCLE:.4f} s), loops", "graze": f"{GN} frames (4.0 s), loops", "idle": f"{IN} frames (4.0 s), loops", "alert": "v3 Alert, unchanged (37 frames)"},
              walk=dict(speed_m_per_s_at_1x=SPEED, metres_per_cycle=round(STRIDE, 4), cycle_seconds=round(CYCLE, 4), stance_share=DUTY,
                        footfall_order="LH 0, LF 0.25, RH 0.5, RF 0.75 (lateral-sequence walk)", body_drop_m=round(-BOB0, 3),
                        planted_hoof_slide=slides, note="move the root forward at metres_per_cycle per cycle_seconds; scale playback by actual speed / 0.5"))
json.dump(report, open(os.path.join(HERE, "build-report.json"), "w"), indent=2)
print("COW_V4", json.dumps(report["walk"]))

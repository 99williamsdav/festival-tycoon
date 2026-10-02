# Band instruments second pass: accordionist and singer kits, with arms posed by the designer's v6 generator.
#   blender -b --python build_posed_kits.py -- <out_dir>      then   python merge_animations.py <out_dir>/lwf_{accordionist,singer}_*_kit_v2.glb
# The arms come from attendee-v6-draft/build_attendee.py run with an added "play" pose, exactly as the v2 kits
# (role-assets-v2/build_performer_v6.py): two-bone IK on the relaxed segment lengths, the sleeve following the upper
# arm, a v6 mitten hand. An arm that changes shape over the loop is one mesh with one morph target per sampled IK pose
# (neighbouring samples are close, so blending between them never visibly shortens the arm); an arm that only nudges
# is a rigid rotation about the shoulder, as before. Performer-local frame: origin at the feet, +Y front, +Z up.
import bpy, bmesh, sys, os, json, math
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

V3 = Vector
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = argv[0] if argv else os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
ONLY = argv[1:]                                     # optional: kit names to build (default: all four)
ROOT = "C:/Projects/festival-tycoon/assets/source/characters/"
CH = "C:/Projects/festival-tycoon/game/assets/characters/"
sys.path.insert(0, ROOT + "role-assets-v2")
from refit_common import islands
GEN = ROOT + "attendee-v6-draft/build_attendee.py"
MIC_Y = {"male": 1.50, "female": 1.43}             # MicHead height the game sets per body (lwf_mic_stand_v1)
MIC_STAND_Y = 0.50                                  # the stand's distance in front of the performer
REPORT = json.load(open(os.path.join(OUT, "posed_kits_report.json"))) if ONLY and os.path.exists(os.path.join(OUT, "posed_kits_report.json")) else {}

# ============================================================== the v6 generator with a play pose
_src = open(GEN).read()
_src = _src.replace('assert POSE in ("relaxed",', 'assert POSE == "play" or POSE in ("relaxed",')
_HOOK = '''elif POSE == "play":
    CTX = dict(K=K, MOUTH=MOUTH, hw=hw, F=F, rel={sx: [j * K for j in relaxed_joints(sx)] for sx in (-1, 1)})
    CTX["sh"] = {sx: CTX["rel"][sx][0] for sx in (-1, 1)}
    CTX["L"] = {sx: (CTX["rel"][sx][1] - CTX["rel"][sx][0]).length + (CTX["rel"][sx][2] - CTX["rel"][sx][1]).length for sx in (-1, 1)}
    for _sx, (_W, _pole, _hand) in PLAN(CTX).items():
        STATE["need"][_sx] = (_W - CTX["sh"][_sx]).length / CTX["L"][_sx]
        pose_arm(_sx, (lambda bm, h=_hand, W=_W: HAND(bm, W, h, gloft, R, hw)), _W, V3(_pole))
    STATE["ctx"] = CTX
'''
_MARK = "# ---------------------------------------------------------------- legs / trousers"
_src = _src.replace("\n" + _MARK, "\n" + _HOOK + "\n" + _MARK, 1)
_src = _src[:_src.index("# attachment sockets")]
_CODE = compile(_src, GEN, "exec")


def HAND(bm, W, h, gloft, R, hw):
    """v6 mitten with thumb plus an index finger, one topology for every pose so poses can blend as morph targets.
    h: d = wrist->knuckles, w = towards the thumb (the hand's width), curl 0 open .. 1 fist, point 0..1 (index out).
    With point 0 the finger is a small stub hidden inside the palm."""
    d = V3(h["d"]).normalized(); w = V3(h["w"]); w = (w - d * w.dot(d)).normalized()
    c, p = h.get("curl", 0.0), h.get("point", 0.0)
    rings = [(0.000, 0.017, 0.026), (0.030, 0.021, 0.040), (0.065 * (1 - 0.25 * c), 0.019 * (1 + 0.20 * c), 0.038),
             (0.088 * (1 - 0.40 * c), 0.011 * (1 + 0.90 * c), 0.026 * (1 + 0.20 * c))]
    gloft(bm, [R(W + d * a, t * hw, wd * hw, t=d) for a, t, wd in rings], 8, offset=0.5, ref=w)
    tb = W + d * 0.028 + w * 0.030 * hw
    td = (d * (0.8 + 0.2 * c) + w * (0.6 - 0.45 * c)).normalized()
    gloft(bm, [R(tb, 0.010, 0.011, t=td), R(tb + td * 0.036, 0.008, 0.009, t=td)], 6, offset=0.5, ref=w)
    fb = W + d * 0.040 + w * 0.020 * hw
    fl, fr = (0.008 + 0.064 * p) * hw, (0.004 + 0.0045 * p) * hw
    gloft(bm, [R(fb, fr * 1.1, fr * 1.2, t=d), R(fb + d * fl * 0.6, fr, fr * 1.1, t=d), R(fb + d * fl, fr * 0.8, fr * 0.85, t=d)],
          6, offset=0.5, ref=w)


def seg_dist(p, a, b):
    ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab) / ab.dot(ab)))
    return (a + ab * t - p).length


def generate(sex, plan, torso_side=-1):
    """run the generator with plan(ctx) -> {side: (wrist, pole, hand)}; return both arms and the body as plain data"""
    state = {"need": {}}
    g = dict(__name__="__gen__", PLAN=plan, HAND=HAND, STATE=state)
    sys.argv = [sys.argv[0], "--", sex, os.path.join(OUT, "_scratch.blend"), ROOT + f"attendee-v6-draft/{sex}-attendee-v6.blend", "play"]
    exec(_CODE, g)
    body, K, ARMS = g["body"], g["K"], g["ARMS"]
    # partition exactly as build_performer_v6: sleeves, linings, arm tubes and hands go to the nearest arm chain
    bm = bmesh.new(); bm.from_mesh(body.data); bm.faces.ensure_lookup_table()
    dl = bm.verts.layers.deform.active
    gname = {vg.index: vg.name for vg in body.vertex_groups}
    part = lambda f: gname[next(iter(f.verts[0][dl].keys()))]
    chains = {sx: [j * K for j in ((ARMS[sx]["sh"], ARMS[sx]["el"], ARMS[sx]["wr"]) if ARMS[sx] else g["relaxed_joints"](sx))]
              for sx in (-1, 1)}
    tee = [isl for isl in islands(bm, bm.faces) if {part(f) for f in isl} == {"Tee"}]
    torso = max(tee, key=len)
    limb = {-1: set(), 1: set()}
    torso_idx = {f.index for f in torso}
    for isl in islands(bm, bm.faces):
        names = {part(f) for f in isl}
        if not names <= {"Arms", "SleeveLining", "Tee"}: continue
        if {f.index for f in isl} == torso_idx:
            # the v2 kits carry the tee torso in a playing arm (the v2 split compares islands with `is`, which never
            # matches, so BodyCore has no torso). Keep that contract: hiding the idle arms must not hide the shirt.
            # It goes in the arm that does not morph, so the morph targets stay small.
            limb[torso_side].update(torso_idx); continue
        c = sum((v.co for f in isl for v in f.verts), V3()) / sum(len(f.verts) for f in isl)
        sx = min((-1, 1), key=lambda k: min(seg_dist(c, a, b) for a, b in zip(chains[k], chains[k][1:])))
        limb[sx].update(f.index for f in isl)
    me = body.data; uvl = me.uv_layers[0].data
    def take(faces):
        vmap, verts, polys, uvs = {}, [], [], []
        for fi in sorted(faces):
            p = me.polygons[fi]; ids = []
            for li, vi in zip(p.loop_indices, p.vertices):
                if vi not in vmap: vmap[vi] = len(verts); verts.append(me.vertices[vi].co.copy())
                ids.append(vmap[vi]); uvs.append(tuple(uvl[li].uv))
            polys.append(ids)
        return dict(verts=verts, polys=polys, uvs=uvs)
    allf = set(range(len(me.polygons)))
    out = {sx: take(limb[sx]) for sx in (-1, 1)}
    out["core"] = take(allf - limb[-1] - limb[1])
    out["bare"] = {sx: take(limb[sx] - torso_idx) for sx in (-1, 1)}    # arm, sleeve and hand only (for checks)
    out["solid"] = take((allf - limb[-1] - limb[1]) | torso_idx)          # the body as it looks: core plus the torso
    hair = {f.index for f in bm.faces if part(f) == "Hair"}
    out["drape"] = take(((allf - limb[-1] - limb[1]) | torso_idx) - hair)  # what straps lie on (under the hair)
    out["ctx"] = state["ctx"]; out["need"] = state["need"]
    out["chains"] = chains
    sc = os.path.join(OUT, "_scratch.blend")
    if os.path.exists(sc): os.remove(sc)
    return out

# ============================================================== kit scene helpers
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene; sc.render.fps = 24


def arm_material(role):
    """the v2 kits' arm material (role body palette, 96x8): the game repaints it with the performer's own palette"""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=CH + f"lwf_{role}_male_kit_v2.glb")
    new = [o for o in bpy.data.objects if o not in before]
    mat = next(o for o in new if o.name.endswith("PlayingArm")).data.materials[0]
    for o in new: bpy.data.objects.remove(o)
    for a in list(bpy.data.actions): bpy.data.actions.remove(a)
    return mat


def make_arm(name, data, pivot, mat):
    me = bpy.data.meshes.new(name)
    me.from_pydata([v - pivot for v in data["verts"]], [], data["polys"])
    uv = me.uv_layers.new(name="UVMap")
    for l, u in zip(uv.data, data["uvs"]): l.uv = u
    for p in me.polygons: p.use_smooth = False
    me.materials.append(mat)
    o = bpy.data.objects.new(name, me); o.location = pivot
    bpy.context.scene.collection.objects.link(o)
    return o


def add_shape_keys(o, datas, pivot):
    """datas[0] is the basis; one key per further pose (same topology)"""
    o.shape_key_add(name="Basis")
    keys = []
    for i, d in enumerate(datas[1:], 1):
        assert len(d["verts"]) == len(o.data.vertices), "pose topology differs"
        k = o.shape_key_add(name=f"Pose{i:02d}", from_mix=False)
        for j, v in enumerate(d["verts"]): k.data[j].co = v - pivot
        keys.append(k)
    return keys


def palette_material(name, img_name, nslots, slots, fill="808080"):
    m = bpy.data.materials.new(name); m.use_nodes = True
    img = bpy.data.images.new(img_name, 8 * nslots, 8)
    px = []
    for y in range(8):
        for s in range(nslots):
            hx = slots.get(s, fill); rgb = [int(hx[k:k + 2], 16) / 255 for k in (0, 2, 4)]
            px += (rgb + [1.0]) * 8
    img.pixels = px; img.pack()
    tx = m.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = img; tx.interpolation = 'Closest'
    b = m.node_tree.nodes["Principled BSDF"]; b.inputs["Roughness"].default_value = 0.7
    m.node_tree.links.new(tx.outputs[0], b.inputs["Base Color"])
    return m


class Builder:
    """bmesh with palette UVs; slot -> u = (8*slot+4)/W"""
    def __init__(self, name, nslots):
        self.name, self.ns = name, nslots; self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new("UVMap")
    def paint(self, faces, slot):
        for f in faces:
            for l in f.loops: l[self.uv].uv = ((8 * slot + 4) / (8.0 * self.ns), 0.5)
    def box(self, lo, hi, slot, front_slot=None):
        """axis-aligned box from corner lo to corner hi; the +Y face optionally in front_slot"""
        lo, hi = V3(lo), V3(hi)
        r = bmesh.ops.create_cube(self.bm, size=1.0, matrix=Matrix.Translation((lo + hi) / 2) @ Matrix.Diagonal((*(hi - lo), 1)))
        faces = {f for v in r["verts"] for f in v.link_faces}
        self.paint(faces, slot)
        if front_slot is not None:
            for f in faces:
                f.normal_update()
                if f.normal.y > 0.9: self.paint([f], front_slot)
        return faces
    def link(self, mat, location=(0, 0, 0)):
        bmesh.ops.recalc_face_normals(self.bm, faces=list(self.bm.faces))
        me = bpy.data.meshes.new(self.name); self.bm.to_mesh(me); self.bm.free(); me.materials.append(mat)
        for p in me.polygons: p.use_smooth = False
        o = bpy.data.objects.new(self.name, me); o.location = location
        bpy.context.scene.collection.objects.link(o); return o


def export(name, frame_end):
    sc = bpy.context.scene; sc.frame_start, sc.frame_end = 1, frame_end
    objs = list(sc.objects)
    bpy.ops.object.select_all(action='SELECT')
    path = os.path.join(OUT, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True, export_extras=True,
                              export_animations=True, export_animation_mode='SCENE', export_force_sampling=True,
                              export_frame_range=True, export_morph=True, export_morph_normal=True, export_morph_animation=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, name + ".blend"))
    tris = sum(len(p.vertices) - 2 for o in objs if o.type == 'MESH' for p in o.data.polygons)
    REPORT.setdefault(name, {}).update(tris=tris, nodes=sorted(o.name for o in objs), frames=frame_end, fps=24)
    print(f"EXPORTED {path} tris={tris}")


def bvh_of(data):
    return BVHTree.FromPolygons([V3(v) for v in data["verts"]], data["polys"])


def inside(tree, p, direction=V3((0.3713, 0.5571, 0.7428))):
    hits, o = 0, p.copy()
    while True:
        loc, n, i, d = tree.ray_cast(o, direction)
        if loc is None: return hits % 2 == 1
        hits += 1; o = loc + direction * 1e-5


def arm_clearance(arm, core_tree, chain):
    """forearm and hand only (the sleeve top is meant to sit in the torso): closest gap to the body, verts inside it"""
    sh, el, wr = chain
    pts = [V3(v) for v in arm["verts"] if (V3(v) - sh).length > 0.55 * (el - sh).length]
    gap = min(core_tree.find_nearest(p)[3] for p in pts)
    return round(gap * 1000, 1), sum(inside(core_tree, p) for p in pts)

def smooth(t): return t * t * (3 - 2 * t)


def orth(d, w):
    d = V3(d).normalized(); w = V3(w); return d, (w - d * w.dot(d)).normalized()

# ============================================================== singer (Pop lead at the mic stand)
# Right hand on the mic; the left arm loops through reaching out to the crowd, a raised fist (with a pump) and a point.
SING_N = 121                    # 5 s at 24 fps
SING_STEP = 6                   # one sampled IK pose every 6 frames: 20 morph samples
# key poses for the left arm, in shoulder-relative directions (x out to the performer's left is -x), as a fraction of
# full reach, with an elbow pole and the hand: d fingers, w thumb side, curl, point
SING_KEYS = [
    (0,   "rest"),
    (18,  dict(dir=(-0.38, 0.78, 0.42), reach=0.90, pole=(-0.5, -0.2, -1.0), d=(-0.30, 0.85, 0.40), w=(-1, 0, 0.35), curl=0.1, point=0)),
    (32,  dict(dir=(-0.55, 0.70, 0.48), reach=0.92, pole=(-0.6, -0.2, -1.0), d=(-0.45, 0.80, 0.45), w=(-1, 0, 0.40), curl=0.1, point=0)),
    (44,  dict(dir=(-0.38, 0.78, 0.42), reach=0.90, pole=(-0.5, -0.2, -1.0), d=(-0.30, 0.85, 0.40), w=(-1, 0, 0.35), curl=0.1, point=0)),
    (60,  dict(dir=(-0.28, 0.22, 1.00), reach=0.86, pole=(-1.0, 0.0, -0.3), d=(-0.10, 0.15, 1.0), w=(1, 0, 0), curl=1.0, point=0)),
    (67,  dict(dir=(-0.34, 0.26, 0.85), reach=0.74, pole=(-1.0, 0.0, -0.3), d=(-0.10, 0.20, 1.0), w=(1, 0, 0), curl=1.0, point=0)),
    (74,  dict(dir=(-0.28, 0.22, 1.00), reach=0.86, pole=(-1.0, 0.0, -0.3), d=(-0.10, 0.15, 1.0), w=(1, 0, 0), curl=1.0, point=0)),
    (92,  dict(dir=(-0.14, 1.00, 0.24), reach=0.97, pole=(-1.0, -0.2, -0.5), d=(-0.12, 1.0, 0.22), w=(0, 0, 1), curl=1.0, point=1.0)),
    (104, dict(dir=(-0.48, 0.88, 0.22), reach=0.97, pole=(-1.0, -0.2, -0.5), d=(-0.45, 0.88, 0.20), w=(0, 0, 1), curl=1.0, point=1.0)),
    (120, "rest"),
]


def sing_left_at(ctx, f):
    """the left arm's (wrist, pole, hand) at frame f, by easing between neighbouring key poses"""
    sx = -1; S, L = ctx["sh"][sx], ctx["L"][sx]
    sh, el, wr = ctx["rel"][sx]
    def resolve(k):
        if k == "rest":
            mid = sh.lerp(wr, 0.5)
            return dict(W=wr.copy(), pole=(el - mid).normalized(), d=V3((sx * 0.004, 0.006, -1)), w=V3((0, 1, 0)), curl=0.15, point=0.0)
        hand_len = 0.040                         # wrist -> hand centre (shorter for a fist), as a fraction of reach
        Wd = V3(k["dir"]).normalized() * (k["reach"] * L)
        return dict(W=S + Wd, pole=V3(k["pole"]).normalized(), d=V3(k["d"]).normalized(), w=V3(k["w"]), curl=k["curl"], point=k["point"])
    for (f0, k0), (f1, k1) in zip(SING_KEYS, SING_KEYS[1:]):
        if f0 <= f <= f1:
            a, b = resolve(k0), resolve(k1); t = smooth((f - f0) / (f1 - f0))
            W = a["W"].lerp(b["W"], t)
            pole = a["pole"].lerp(b["pole"], t).normalized()
            d, w = orth(a["d"].lerp(b["d"], t), a["w"].lerp(b["w"], t))
            return W, pole, dict(d=d, w=w, curl=a["curl"] + (b["curl"] - a["curl"]) * t, point=a["point"] + (b["point"] - a["point"]) * t)
    raise ValueError(f)


def mic_geometry(sex):
    """the mic stand's capsule and handle in the performer frame (from build_instruments.py's MicHead)"""
    mh = MIC_Y[sex]
    axis = V3((0, -math.sin(math.radians(70)), math.cos(math.radians(70))))       # handle -> capsule
    return dict(grille=V3((0, 0.155, mh + 0.012)), grille_r=0.026, handle_c=V3((0, 0.19, mh)), axis=axis,
                handle_end=V3((0, 0.19, mh)) - axis * 0.045)


def sing_right(ctx, sex):
    """right fist round the mic handle, just behind the grille; knuckles across the handle, wrist below and outside"""
    m = mic_geometry(sex)
    C = m["handle_c"] - m["axis"] * 0.022 + V3((0.004, 0, -0.002))   # grip centre on the handle, behind the grille
    d, w = orth((-0.30, 0.05, 1.0), m["axis"])
    W = C - d * 0.036
    return W, (1.0, 0.25, -0.8), dict(d=d, w=w, curl=1.0, point=0.0)


def build_singer(sex):
    poses = []
    frames = list(range(0, SING_N - 1, SING_STEP))
    for f in frames:
        plan = lambda ctx, f=f: {-1: sing_left_at(ctx, f), 1: sing_right(ctx, sex)}
        poses.append(generate(sex, plan, torso_side=1))
    reset(); mat = arm_material("guitarist")
    ctx = poses[0]["ctx"]
    piv = {sx: ctx["sh"][sx] for sx in (-1, 1)}
    L = make_arm("LWF_Singer_LeftPlayingArm", poses[0][-1], piv[-1], mat)
    R = make_arm("LWF_Singer_RightPlayingArm", poses[0][1], piv[1], mat)
    keys = add_shape_keys(L, [p[-1] for p in poses], piv[-1])
    # weights: piecewise linear between neighbouring samples; the last sample blends back into the basis (frame 0)
    for fr in range(SING_N):
        i = fr // SING_STEP; t = (fr - i * SING_STEP) / SING_STEP
        wts = [0.0] * len(keys)
        a, b = i % len(frames), (i + 1) % len(frames)
        if a > 0: wts[a - 1] += 1 - t
        if b > 0: wts[b - 1] += t
        for k, wv in zip(keys, wts):
            k.value = wv; k.keyframe_insert("value", frame=fr + 1)
    name = f"lwf_singer_{sex}_kit_v2"
    export(name, SING_N)
    # checks: every sampled pose clear of the body; the mic hand round the handle and clear of the grille and face
    core = bvh_of(poses[0]["solid"])
    gaps = [arm_clearance(p["bare"][-1], bvh_of(p["solid"]), p["chains"][-1]) for p in poses]
    m = mic_geometry(sex)
    rv = [V3(v) for v in poses[0]["bare"][1]["verts"]]
    hand = [v for v in rv if (v - m["handle_c"]).length < 0.09]
    grille_gap = min((v - m["grille"]).length for v in hand) - m["grille_r"]
    near = min(range(len(rv)), key=lambda i: seg_dist(rv[i], m["handle_end"], m["grille"]))
    REPORT[name].update(
        left_arm="20 morph samples of IK poses (one every 6 frames), weights blended linearly between neighbours",
        loop="0 rest, 0.75 s reach out to the crowd (with a lean further out), 2.5 s raised fist with a pump, 3.8 s point that sweeps across the crowd, back to rest at 5 s",
        reach_fraction_max=round(max(p["need"][-1] for p in poses), 3),
        left_forearm_gap_to_body_mm_min=min(g[0] for g in gaps), left_forearm_verts_inside_body=sum(g[1] for g in gaps),
        right_forearm_gap_to_body_mm=arm_clearance(poses[0]["bare"][1], core, poses[0]["chains"][1])[0],
        mic_head_y=MIC_Y[sex],
        mic_hand_closest_to_handle_mm=round(seg_dist(rv[near], m["handle_end"], m["grille"]) * 1000, 1),
        mic_reach_fraction=round(poses[0]["need"][1], 3))

# ============================================================== accordionist (Folk role 1)
ACC_N = 49                      # 2 s: the bellows draw open and squeeze shut once
ACC_SLOTS = {11: "E8E2D0", 12: "B8442E", 13: "C9553A", 14: "2A2A2A", 15: "1A1A1A", 16: "F2F0EA", 17: "141414",
             18: "C8CACC", 19: "4A3426", 20: "D8DADC", 21: "9A9DA2"}
# 11 bass buttons, 12 body back/sides/top, 13 body front, 14 bellows, 15 bellows end frames, 16 white keys,
# 17 black keys, 18 grille, 19 straps, 20 bellows corners, 21 trim and clasps


def acc_layout(ctx, core):
    """accordion boxes sized to the body: hangs on the chest below the shoulders, keyboard under the right hand"""
    f = 1.0 if not ctx["F"] else 0.94
    S_R, S_L = ctx["sh"][1], ctx["sh"][-1]
    H, D = 0.36 * f, 0.17 * f
    zc = S_R.z - 0.20 * f
    zb, zt = zc - H / 2, zc + H / 2
    x_t1 = S_R.x * 0.88; Tw = 0.10 * f
    chest = max(v.y for v in core["verts"] if abs(v.x) < 0.16 and zb - 0.02 < v.z < zt + 0.02)
    yb = chest + 0.015
    return dict(f=f, H=H, D=D, zc=zc, zb=zb, zt=zt, yb=yb, yf=yb + D, yc=yb + D / 2, x_t1=x_t1, x_t0=x_t1 - Tw,
                B0=0.11 * f, B1=0.18 * f, Bw=0.075 * f, key_h=0.020, chest=chest)


def acc_hands(ctx, lay, open_):
    hw = ctx["hw"]
    B = lay["B1"] if open_ else lay["B0"]
    x_b1 = lay["x_t0"] - B - lay["Bw"]
    # right: palm on the keys (outer +X face of the treble end), fingers forward along the keys, thumb up
    dR, wR = orth((0, 1.0, -0.30), (0, 0.3, 1.0))
    CR = V3((lay["x_t1"] + lay["key_h"] + 0.021 * hw + 0.002, lay["yc"] + 0.012, lay["zc"] + 0.02))
    # left: palm on the bass buttons (outer -X face of the bass end), under the hand strap, thumb up
    dL, wL = orth((0, 1.0, -0.20), (0, 0.2, 1.0))
    CL = V3((x_b1 - 0.004 - 0.021 * hw - 0.002, lay["yc"] + 0.005, lay["zc"] + 0.03))
    return {1: (CR - dR * 0.046, (1.0, -0.35, -0.7), dict(d=dR, w=wR, curl=0.35)),
            -1: (CL - dL * 0.046, (-1.0, -0.35, -0.7), dict(d=dL, w=wL, curl=0.35))}, CR, CL


def build_accordion_meshes(lay, mat, core_tree, CL, hw):
    f = lay["f"]; zb, zt, zc, yb, yf, yc = lay["zb"], lay["zt"], lay["zc"], lay["yb"], lay["yf"], lay["yc"]
    x_t0, x_t1 = lay["x_t0"], lay["x_t1"]
    # treble end: body, keyboard on the outer face, grille on the front, trim at the bellows edge, register tabs
    t = Builder("LWF_Accordion_TrebleEnd", 24)
    t.box((x_t0, yb, zb), (x_t1, yf, zt), 12, front_slot=13)
    t.box((x_t1, yb + 0.012, zb + 0.02), (x_t1 + lay["key_h"] * 0.6, yf - 0.006, zt - 0.02), 16)        # white keys
    span = (zt - 0.03) - (zb + 0.03); n = 14
    for i in range(n):                                                                                      # black keys, 2-3 groups
        if i % 7 in (2, 6): continue
        z = zb + 0.03 + (i + 0.5) * span / n
        t.box((x_t1 + 0.004, yb + 0.012, z - 0.006), (x_t1 + lay["key_h"], yb + 0.012 + 0.6 * (yf - yb - 0.02), z + 0.006), 17)
    t.box((x_t0 + 0.012, yf, zc - 0.01), (x_t1 - 0.012, yf + 0.004, zt - 0.022), 18)                      # grille
    t.box((x_t0 - 0.004, yb - 0.002, zb - 0.002), (x_t0 + 0.006, yf + 0.002, zt + 0.002), 21)             # bellows-edge trim
    for i in range(4):                                                                                      # register tabs on top
        x = x_t0 + 0.02 + i * (x_t1 - x_t0 - 0.03) / 4
        t.box((x, yf - 0.03, zt), (x + 0.012, yf - 0.01, zt + 0.008), 11)
    treble = t
    # bellows: pleated rings from the treble edge towards -x; the node scales along x to open and close
    b = Builder("LWF_Accordion_Bellows", 24)
    folds = 6; inset = 0.016 * f
    hy, hz = (yf - yb) / 2 - 0.004, (zt - zb) / 2 - 0.004
    rings = []
    for i in range(2 * folds + 1):
        x = -lay["B0"] * i / (2 * folds)
        e = 0.0 if i % 2 == 0 else inset
        rings.append([b.bm.verts.new((x, sy * (hy - e), sz * (hz - e))) for sy, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(4):
            fc = b.bm.faces.new((r0[k], r0[(k + 1) % 4], r1[(k + 1) % 4], r1[k])); b.paint([fc], 14)
    for i in range(0, 2 * folds + 1, 2):                                                                    # front corner protectors
        x = -lay["B0"] * i / (2 * folds)
        w = 0.006 if 0 < i < 2 * folds else 0.0
        if w == 0: continue
        for sz in (-1, 1):
            b.box((x - w, hy - 0.010, sz * hz - 0.010), (x + w, hy + 0.003, sz * hz + 0.010), 20)
    for x in (0.0, -lay["B0"]):                                                                             # end frames
        for sy, sz, size in ((0, -1, (0.006, 2 * hy + 0.006, 0.008)), (0, 1, (0.006, 2 * hy + 0.006, 0.008)),
                             (-1, 0, (0.006, 0.008, 2 * hz)), (1, 0, (0.006, 0.008, 2 * hz))):
            c = V3((x, sy * hy, sz * hz)); s = V3(size)
            b.box(c - s / 2, c + s / 2, 15)
    bellows = b
    # bass end (built with its bellows-side face at x = 0; the node slides along x): buttons on the outer face,
    # a hand strap over the back of the left hand
    s = Builder("LWF_Accordion_BassEnd", 24)
    Bw = lay["Bw"]
    s.box((-Bw, yb, zb), (0, yf, zt), 12, front_slot=13)
    s.box((-0.006, yb - 0.002, zb - 0.002), (0.004, yf + 0.002, zt + 0.002), 21)
    for r in range(5):
        for c in range(3):
            y = yf - 0.022 - c * 0.020 - (r % 2) * 0.010; z = zc - 0.09 * f + r * 0.045 * f
            s.box((-Bw - 0.006, y - 0.005, z - 0.005), (-Bw, y + 0.005, z + 0.005), 11)
    strap_x = (CL.x - (lay["x_t0"] - lay["B0"])) - 0.021 * hw - 0.008                                       # node-relative, outside the hand
    sy0, sy1 = CL.y - 0.032, CL.y + 0.012
    s.box((strap_x - 0.004, sy0, zb + 0.03), (strap_x + 0.004, sy1, zt - 0.03), 19)
    for z in (zb + 0.03, zt - 0.048):
        s.box((strap_x, sy0, z), (-Bw, sy1, z + 0.018), 19)
    bass = s
    tr = treble.link(mat)
    bl = bellows.link(mat, location=(x_t0, yc, zc))
    bo = bass.link(mat, location=(x_t0 - lay["B0"], 0, 0))
    return tr, bl, bo


def strap_path(core_tree, pts, off=0.007):
    """drape points onto the body: each (point, outward direction) is cast in from outside along -direction and set
    `off` above the first surface it meets"""
    out = []
    for p, d, *reach in pts:
        d = V3(d).normalized()
        loc = core_tree.ray_cast(p + d * (reach[0] if reach else 0.3), -d)[0]
        out.append(loc + d * off if loc is not None else p)
    return out


def ribbon(b, path, width, thick, slot, normals_from):
    """a flat strap along path; normals_from(p) gives the outward direction at each point"""
    rings = []
    for i, p in enumerate(path):
        t = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
        n = normals_from(p); n = (n - t * n.dot(t)).normalized()
        sdir = t.cross(n).normalized()
        rings.append([b.bm.verts.new(p + sdir * sw * width / 2 + n * nt * thick) for sw, nt in ((-1, 0), (1, 0), (1, 1), (-1, 1))])
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(4):
            b.paint([b.bm.faces.new((r0[k], r0[(k + 1) % 4], r1[(k + 1) % 4], r1[k]))], slot)
    b.paint([b.bm.faces.new(list(reversed(rings[0]))), b.bm.faces.new(rings[-1])], slot)


def build_straps(lay, mat, core_tree):
    """two shoulder straps from the treble end's top over the shoulders, down the back to a cross strap"""
    b = Builder("LWF_Accordion_Straps", 24)
    zt, yb = lay["zt"], lay["yb"]
    def outward(p):
        loc, n, i, dist = core_tree.find_nearest(p); return n
    backs = []
    for sx, x0 in ((1, lay["x_t1"] - 0.03), (-1, lay["x_t0"] + 0.015)):
        xs = sx * 0.095
        raw = [(V3((x0 + (xs - x0) * 0.35, 0, zt + 0.03)), (0, 1, 0.15)), (V3((x0 + (xs - x0) * 0.7, 0, zt + 0.08)), (0, 1, 0.3)),
               (V3((xs, 0, zt + 0.12)), (0, 1, 0.8)), (V3((xs, 0.03, zt + 0.10)), (0, 0, 1), 0.12),
               (V3((xs, -0.01, zt + 0.10)), (0, 0, 1), 0.12), (V3((xs, -0.05, zt + 0.10)), (0, -0.4, 1), 0.12), (V3((xs * 1.03, 0, zt + 0.10)), (0, -1, 0.5)),
               (V3((xs * 1.06, 0, zt + 0.02)), (0, -1, 0.1)), (V3((xs * 1.08, 0, zt - 0.08)), (0, -1, 0))]
        path = [V3((x0, yb + 0.01, zt - 0.01))] + strap_path(core_tree, raw, off=0.008)
        ribbon(b, path, 0.032, 0.004, 19, outward)
        backs.append(path[-1])
    cross = strap_path(core_tree, [(backs[0].lerp(backs[1], i / 6) * V3((1, 0, 1)), (0, -1, 0)) for i in range(7)], off=0.012)
    ribbon(b, cross, 0.030, 0.004, 19, outward)
    return b.link(mat)


def build_accordionist(sex):
    # one generator run to measure the body, then the closed and open poses
    probe = generate(sex, lambda ctx: {})
    lay = acc_layout(probe["ctx"], probe["solid"])
    poses, hands = [], None
    for open_ in (False, True):
        def plan(ctx, open_=open_):
            arms, CR, CL = acc_hands(ctx, lay, open_); return arms
        poses.append(generate(sex, plan))
    _, CR, CL = acc_hands(poses[0]["ctx"], lay, False)
    reset(); mat = arm_material("bassist")
    amat = palette_material("LWF_Accordion_MattePalette", "lwf_accordion_palette", 24, ACC_SLOTS)
    ctx = poses[0]["ctx"]; piv = {sx: ctx["sh"][sx] for sx in (-1, 1)}
    L = make_arm("LWF_Accordionist_LeftPlayingArm", poses[0][-1], piv[-1], mat)
    R = make_arm("LWF_Accordionist_RightPlayingArm", poses[0][1], piv[1], mat)
    key = add_shape_keys(L, [poses[0][-1], poses[1][-1]], piv[-1])[0]; key.name = "BellowsOpen"
    core_tree = bvh_of(poses[0]["solid"])
    tr, bl, bo = build_accordion_meshes(lay, amat, core_tree, CL, ctx["hw"])
    st = build_straps(lay, amat, bvh_of(poses[0]["drape"]))
    dB = lay["B1"] - lay["B0"]
    R.rotation_mode = 'QUATERNION'
    for fr in range(ACC_N):
        t = fr / (ACC_N - 1) * 2 * math.pi
        w = (1 - math.cos(t)) / 2                                  # 0 closed -> 1 open -> 0 closed
        key.value = w; key.keyframe_insert("value", frame=fr + 1)
        bl.scale = (1 + w * dB / lay["B0"], 1, 1); bl.keyframe_insert("scale", frame=fr + 1)
        bo.location = (lay["x_t0"] - lay["B0"] - w * dB, 0, 0); bo.keyframe_insert("location", frame=fr + 1)
        # right hand: fingers working up and down the keys (rotation about the shoulder's x axis keeps the palm on the keys)
        a = math.radians(1.8) * math.sin(4 * t) + math.radians(0.8) * math.sin(6 * t + 1.0)
        R.rotation_quaternion = Matrix.Rotation(a, 4, 'X').to_quaternion(); R.keyframe_insert("rotation_quaternion", frame=fr + 1)
    name = f"lwf_accordionist_{sex}_kit_v2"
    export(name, ACC_N)
    # checks
    def gaps_to_box(verts, lo, hi):
        inside_n = sum(1 for v in verts if all(lo[i] < v[i] < hi[i] for i in range(3)))
        return inside_n
    hw = ctx["hw"]
    boxes = [(V3((lay["x_t0"] - B - lay["Bw"], lay["yb"], lay["zb"])), V3((lay["x_t1"], lay["yf"], lay["zt"]))) for B in (lay["B0"], lay["B1"])]
    REPORT[name].update(
        layout={k: round(v, 3) for k, v in lay.items()},
        bellows="LWF_Accordion_Bellows scales along its local x (Godot x) from 1 to %.2f; LWF_Accordion_BassEnd slides %.0f mm to the performer's left with it" % (1 + dB / lay["B0"], dB * 1000),
        left_arm="morph target BellowsOpen (IK pose with the hand on the open bass end), weight in step with the bellows",
        right_arm="rigid rotation about the shoulder's x axis, about 2 degrees: the hand works up and down the keys",
        arm_verts_inside_accordion={s: [gaps_to_box([V3(v) for v in p["bare"][sx]["verts"]], lo, hi) for p, (lo, hi) in zip(poses, boxes)] for s, sx in (("left", -1), ("right", 1))},
        forearm_gap_to_body_mm={s: [arm_clearance(p["bare"][sx], bvh_of(p["solid"]), p["chains"][sx])[0] for p in poses] for s, sx in (("left", -1), ("right", 1))},
        accordion_gap_to_chest_mm=round((lay["yb"] - lay["chest"]) * 1000, 1),
        reach_fraction={s: [round(p["need"][sx], 3) for p in poses] for s, sx in (("left", -1), ("right", 1))},
        recolour="lwf_accordion_palette (192x8, 24 slots, material LWF_Accordion_MattePalette): 12 body back/sides/top, 13 body front, 14 bellows, "
                 "11 bass buttons and register tabs, 15 bellows end frames, 16 white keys, 17 black keys, 18 grille, 19 straps, 20 bellows corners, 21 trim")


for sex in ("male", "female"):
    if not ONLY or f"lwf_accordionist_{sex}_kit_v2" in ONLY: build_accordionist(sex)
    if not ONLY or f"lwf_singer_{sex}_kit_v2" in ONLY: build_singer(sex)
json.dump(REPORT, open(os.path.join(OUT, "posed_kits_report.json"), "w"), indent=1, default=str)
print("DONE")

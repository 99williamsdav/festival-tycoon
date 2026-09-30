# Procedural low-poly festival attendee, v6 direction.
# usage: blender -b --python build_attendee.py -- female|male OUT.blend PALETTE_SOURCE.blend [POSE] [PROPS_DIR]
# POSE: relaxed | drink_hold | food_hold | drinking_beer | drinking_soft | eating
import bpy, bmesh, sys, math, json, hashlib
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
SEX, OUT, PAL_SRC = argv[0], argv[1], argv[2]
POSE = argv[3] if len(argv) > 3 else "relaxed"
PROPS_DIR = argv[4] if len(argv) > 4 else "C:/Projects/festival-tycoon/assets/runtime/props"
assert POSE in ("relaxed", "drink_hold", "food_hold", "drinking_beer", "drinking_soft", "eating"), POSE
F = SEX == "female"

# ---------------------------------------------------------------- palette / material
bpy.ops.wm.read_factory_settings(use_empty=True)
with bpy.data.libraries.load(PAL_SRC, link=False) as (src, dst):
    dst.materials = ["DirectionA adult shared recolour palette"]
MAT = bpy.data.materials["DirectionA adult shared recolour palette"]
SLOT = dict(skin=0, mouth=1, tee=3, patch=5, trousers=7, dark=9, hair=10)
V3 = Vector

# ---------------------------------------------------------------- helpers
def spow(c, p):
    return math.copysign(abs(c) ** (2.0 / p), c)

def frame(t, ref):
    t = t.normalized()
    f = ref - ref.dot(t) * t
    if f.length < 1e-6:
        f = V3((1, 0, 0)) - V3((1, 0, 0)).dot(t) * t
    f.normalize()
    s = f.cross(t)
    return s, f

def loft(bm, rings, n, offset=0.5, power=2.0, ref=V3((0, 1, 0)), cap0=True, cap1=True):
    """rings: dicts with c, rx, ry (front), ryb (back, optional), bulge (front wedge), tilt (front z lift)."""
    cs = [r["c"] for r in rings]
    loops = []
    for i, r in enumerate(rings):
        if "t" in r:
            t = r["t"]
        elif i == 0:
            t = cs[1] - cs[0]
        elif i == len(rings) - 1:
            t = cs[-1] - cs[-2]
        else:
            t = (cs[i + 1] - cs[i - 1])
        rf = ref(r["c"]) if callable(ref) else ref
        s, f = frame(t, rf)
        ring = []
        for k in range(n):
            th = 2 * math.pi * (k + offset) / n
            c, sn = math.cos(th), math.sin(th)
            ry = r["ry"] if sn >= 0 else r.get("ryb", r["ry"])
            p = r["c"] + s * (r["rx"] * spow(c, power)) + f * (ry * spow(sn, power))
            if sn > 0:
                p += f * (r.get("bulge", 0) * sn ** 4)
                p.z += r.get("tilt", 0) * sn ** 2
            if "zmin" in r:
                p.z = max(p.z, r["zmin"])
            ring.append(bm.verts.new(p))
        loops.append(ring)
    faces = []
    for a, b in zip(loops, loops[1:]):
        for k in range(n):
            faces.append(bm.faces.new((a[k], a[(k + 1) % n], b[(k + 1) % n], b[k])))
    if cap0:
        faces.append(bm.faces.new(list(reversed(loops[0]))))
    if cap1:
        faces.append(bm.faces.new(loops[-1]))
    return faces

PARTS = []  # (name, bmesh, slot)

def part(name, slot):
    bm = bmesh.new()
    PARTS.append((name, bm, slot))
    return bm

def finish(bm):
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return BVHTree.FromBMesh(bm)

def surf(bvh, x, z, y0=1.0, d=V3((0, -1, 0)), off=0.0018):
    hit = bvh.ray_cast(V3((x, y0, z)), d)
    loc, nrm = hit[0], hit[1]
    return loc + nrm * off

def decal(name, slot, bvh, xs, zs, off=0.0018):
    bm = part(name, slot)
    grid = [[bm.verts.new(surf(bvh, x, z, off=off)) for x in xs] for z in zs]
    for j in range(len(zs) - 1):
        for i in range(len(xs) - 1):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    return bm

def lin(a, b, n):
    return [a + (b - a) * i / (n - 1) for i in range(n)]

# ---------------------------------------------------------------- proportions
H = 1.70 if F else 1.76
S = H / 1.70  # male scaled slightly taller
z = lambda v: v * S
shx = 0.152 if F else 0.178
sk = 1.0 if F else 1.16
ua = 0.047 if F else 0.053
hw = 1.12 if F else 1.25
def R(c, rx, ry, **kw):
    d = dict(c=V3(c), rx=rx, ry=ry); d.update(kw); return d

# ---------------------------------------------------------------- neck + head
bm = part("Head", SLOT["skin"])
nz = z(1.36)
loft(bm, [R((0, -0.012, nz), 0.046, 0.044), R((0, -0.012, nz + 0.10), 0.043, 0.042),
          R((0, -0.004, nz + 0.14), 0.044, 0.044)], 8, offset=0.5)
HS = 1.14
hc = V3((0, 0, H + 0.03 - 0.255 * HS))  # chin height reference
cz = hc.z
if F:
    head = [  # dz, cy, rx, ry(front), ryb
        (0.000, 0.034, 0.026, 0.020, 0.024),
        (0.034, 0.024, 0.058, 0.052, 0.050),
        (0.068, 0.010, 0.080, 0.076, 0.076),
        (0.100, 0.002, 0.088, 0.086, 0.092),
        (0.150, 0.000, 0.091, 0.086, 0.100),
        (0.195, -0.004, 0.087, 0.078, 0.100),
        (0.232, -0.010, 0.068, 0.058, 0.084),
        (0.255, -0.012, 0.030, 0.026, 0.040),
    ]
else:
    head = [
        (0.000, 0.036, 0.036, 0.022, 0.026),
        (0.032, 0.026, 0.070, 0.054, 0.054),
        (0.066, 0.010, 0.088, 0.080, 0.080),
        (0.100, 0.002, 0.092, 0.088, 0.094),
        (0.150, 0.000, 0.094, 0.088, 0.102),
        (0.195, -0.004, 0.090, 0.080, 0.102),
        (0.232, -0.010, 0.072, 0.060, 0.086),
        (0.255, -0.012, 0.032, 0.028, 0.042),
    ]
loft(bm, [R((0, cy, cz + dz), rx, ry, ryb=rb) for dz, cy, rx, ry, rb in head], 12, offset=0.5, power=2.5)
HEAD_BVH = finish(bm)

# nose
nb = part("Nose", SLOT["skin"])
top = surf(HEAD_BVH, 0, cz + 0.128, off=-0.004)
bl = surf(HEAD_BVH, -0.013, cz + 0.088, off=-0.004)
br = surf(HEAD_BVH, 0.013, cz + 0.088, off=-0.004)
tip = surf(HEAD_BVH, 0, cz + 0.092, off=0.0) + V3((0, 0.026 if F else 0.030, 0))
vs = [nb.verts.new(p) for p in (top, bl, br, tip)]
nb.faces.new((vs[0], vs[1], vs[3])); nb.faces.new((vs[0], vs[3], vs[2])); nb.faces.new((vs[1], vs[2], vs[3]))
nb.faces.new((vs[0], vs[2], vs[1]))
finish(nb)

# eyes + mouth decals
ez = cz + 0.138
for sx in (-1, 1):
    decal("Eyes", SLOT["dark"], HEAD_BVH, [sx * 0.040 - 0.0075, sx * 0.040 + 0.0075], [ez - 0.017, ez + 0.017], off=0.0015)
if F: decal("Mouth", SLOT["mouth"], HEAD_BVH, [-0.016, 0.0, 0.016], [cz + 0.046, cz + 0.050], off=0.0012)

# ears (visible on male, tucked under hair on female)
bm = part("Ears", SLOT["skin"])
for sx in (-1, 1):
    ex = sx * 0.090
    loft(bm, [R((ex, -0.012, cz + 0.105), 0.010, 0.016, t=V3((sx, 0, 0))),
              R((ex + sx * 0.012, -0.018, cz + 0.110), 0.008, 0.014)], 6, ref=V3((0, 0, 1)))
finish(bm)

# ---------------------------------------------------------------- hair
bm = part("Hair", SLOT["hair"])
HCEN = V3((0, -0.01, cz + 0.15))
outward = lambda c: (V3((c.x, c.y, 0)) - V3((HCEN.x, HCEN.y, 0))) if (V3((c.x, c.y, 0)) - V3((HCEN.x, HCEN.y, 0))).length > 1e-4 else V3((0, 1, 0))

def headr(ang, zz):
    """approximate head radius at angle (0=+X, 90=front) near crown heights"""
    c, s = math.cos(ang), math.sin(ang)
    rx, ry = 0.091, 0.087 if s > 0 else 0.100
    return math.hypot(rx * c, ry * s)

if F:
    import random
    rng = random.Random(11)
    HC = V3((0, -0.012, 0))
    # hair shell rows: (dz above chin, rx, ry front, ry back, face-opening half-angle in degrees)
    HROWS = [(0.288, 0.040, 0.036, 0.046, 0),
             (0.272, 0.078, 0.068, 0.090, 0),
             (0.242, 0.100, 0.090, 0.112, 0),
             (0.202, 0.109, 0.098, 0.121, 0),
             (0.150, 0.112, 0.096, 0.125, 60),
             (0.100, 0.116, 0.094, 0.127, 62),
             (0.050, 0.124, 0.090, 0.126, 60),
             (0.000, 0.134, 0.084, 0.121, 56),
             (-0.040, 0.146, 0.078, 0.116, 52),
             (-0.068, 0.156, 0.074, 0.114, 50)]

    def ell(rx, ryf, ryb, th, p=2.2):
        c, sn = math.cos(th), math.sin(th)
        return V3((rx * spow(c, p), (ryf if sn >= 0 else ryb) * spow(sn, p), 0))

    def shell_pt(th_deg, dz, out=0.0):
        rows = HROWS
        for a, b in zip(rows, rows[1:]):
            if b[0] <= dz <= a[0]:
                t = (a[0] - dz) / (a[0] - b[0])
                r = [a[i] + (b[i] - a[i]) * t for i in range(1, 4)]
                break
        else:
            r = list((rows[0] if dz > rows[0][0] else rows[-1])[1:4])
        e = ell(*r, math.radians(th_deg))
        e = e * (1 + out / max(e.length, 1e-4))
        return V3((HC.x + e.x, HC.y + e.y, cz + dz))

    def thicken(bm, faces, t):
        fs = set(faces)
        bm.normal_update()
        inner = {}
        for v in {v for f in faces for v in f.verts}:
            n = V3()
            for f in v.link_faces:
                if f in fs:
                    n += f.normal
            inner[v] = bm.verts.new(v.co - n.normalized() * t)
        for f in faces:
            bm.faces.new([inner[v] for v in reversed(f.verts)])
        for f in faces:
            for l in f.loops:
                e = l.edge
                if sum(1 for g in e.link_faces if g in fs) == 1:
                    a, b = l.vert, l.link_loop_next.vert
                    bm.faces.new((b, a, inner[a], inner[b]))

    # two shells, open at the face, each ending in jagged turned-out points:
    # a longer under layer and a shorter outer layer that reads as the concept's layering
    def shell(rows, seed, drops, flare, top_closed, t=0.015):
        rng = random.Random(seed)
        N = 22
        ph = math.pi / N * 0.5
        last = len(rows) - 1
        grid = []
        for ri, (dz, rx, ryf, ryb, cut) in enumerate(rows):
            ring = []
            for k in range(N):
                th = 2 * math.pi * k / N + ph
                e = ell(rx, ryf, ryb, th) * (1 + (rng.uniform(-0.025, 0.025) if ri > 1 else 0))
                zz = cz + dz + (rng.uniform(-0.004, 0.004) if ri > 0 else 0)
                if ri == last:
                    drop = rng.uniform(0.45, 1.0) * max(drops) if k % 2 else rng.uniform(0.0, 0.18) * max(drops)
                    zz -= drop
                    e = e * (1 + flare * drop / max(drops))
                ring.append(bm.verts.new(V3((HC.x + e.x, HC.y + e.y, zz))))
            grid.append(ring)

        def is_open(ri, k):
            cut = rows[ri][4]
            th = 2 * math.pi * (k + 0.5) / N + ph
            return cut and abs((math.degrees(th) - 90 + 180) % 360 - 180) < cut

        faces = []
        if top_closed:
            apex = bm.verts.new(V3((0.004, -0.014, cz + rows[0][0] + 0.008)))
            faces += [bm.faces.new((apex, grid[0][(k + 1) % N], grid[0][k])) for k in range(N)]
        for ri in range(last):
            a, b = grid[ri], grid[ri + 1]
            for k in range(N):
                if not is_open(ri + 1, k):
                    faces.append(bm.faces.new((a[k], a[(k + 1) % N], b[(k + 1) % N], b[k])))
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        hcen = V3((HC.x, HC.y, cz + 0.12))
        if sum(f.normal.dot(f.calc_center_median() - hcen) for f in faces) < 0:
            bmesh.ops.reverse_faces(bm, faces=faces)
        thicken(bm, faces, t)

    # under layer: starts beneath the outer layer, reaches the shoulders
    shell([(0.150, 0.108, 0.090, 0.118, 60),
           (0.080, 0.118, 0.088, 0.123, 62),
           (0.020, 0.130, 0.082, 0.120, 58),
           (-0.030, 0.142, 0.076, 0.114, 54)], seed=5,
          drops=[0.000, 0.030, 0.010, 0.040, 0.006, 0.022, 0.016], flare=0.14, top_closed=False, t=0.012)
    # outer layer: crown volume down to jaw level, flicked points over the under layer
    shell([(0.288, 0.040, 0.036, 0.046, 0),
           (0.272, 0.078, 0.068, 0.090, 0),
           (0.242, 0.101, 0.091, 0.113, 0),
           (0.202, 0.111, 0.099, 0.123, 0),
           (0.150, 0.120, 0.100, 0.130, 60),
           (0.095, 0.130, 0.098, 0.135, 62),
           (0.050, 0.140, 0.094, 0.137, 60)], seed=11,
          drops=[0.000, 0.040, 0.014, 0.052, 0.006, 0.030, 0.020, 0.046], flare=0.16, top_closed=True)

    def lock(path, widths, thick, n=6):
        """path of (theta deg, dz, outward offset) hugging the hair shell; thickness follows the skull normal"""
        pts = [shell_pt(th, dz, o) for th, dz, o in path]
        hc3 = V3((HC.x, HC.y, cz + 0.13))
        loft(bm, [R(p, w / 2, t) for p, w, t in zip(pts, widths, thick)], n, offset=0.0, power=1.5,
             ref=lambda c: (c - hc3).normalized())

    # swept fringe: off-centre parting, across the forehead, down the far side of the face
    lock([(116, 0.262, -0.020), (100, 0.256, 0.010), (82, 0.230, 0.024), (62, 0.198, 0.030),
          (44, 0.166, 0.028), (30, 0.130, 0.024), (22, 0.098, 0.014), (17, 0.072, 0.006)],
         [0.050, 0.112, 0.150, 0.150, 0.125, 0.100, 0.070, 0.016],
         [0.014, 0.022, 0.030, 0.032, 0.030, 0.024, 0.016, 0.006], n=8)
    # smaller framing lock on the parting side
    lock([(122, 0.250, 0.004), (136, 0.200, 0.012), (144, 0.130, 0.014), (148, 0.060, 0.016), (150, 0.005, 0.022)],
         [0.050, 0.070, 0.068, 0.050, 0.012], [0.012, 0.016, 0.016, 0.012, 0.005])
else:
    # short swept crop: cap with raised fringe mass and faceted quiff
    loft(bm, [
        R((0, -0.016, cz + 0.060), 0.090, 0.070, ryb=0.100, tilt=0.130),
        R((0, -0.014, cz + 0.105), 0.101, 0.092, ryb=0.114, tilt=0.080),
        R((0, -0.014, cz + 0.170), 0.104, 0.098, ryb=0.114, tilt=0.050),
        R((0, -0.016, cz + 0.228), 0.098, 0.090, ryb=0.108, tilt=0.018),
        R((0.006, -0.018, cz + 0.268), 0.070, 0.070, ryb=0.080),
        R((0.012, -0.020, cz + 0.286), 0.028, 0.030),
    ], 12, offset=0.5, power=2.3)
    P = [V3((-0.050, 0.020, cz + 0.285)), V3((-0.005, 0.070, cz + 0.268)), V3((0.045, 0.098, cz + 0.232)),
         V3((0.080, 0.090, cz + 0.200))]
    loft(bm, [R(p, w / 2, t) for p, w, t in zip(P, [0.06, 0.12, 0.10, 0.03], [0.020, 0.026, 0.020, 0.008])],
         6, offset=0.0, power=1.6, ref=lambda c: (c - HCEN).normalized())
finish(bm)


# ---------------------------------------------------------------- final scale (head + hair set the height)
HEADPARTS = ("Head", "Nose", "Eyes", "Mouth", "Ears", "Hair")
def head_tf(name, co):
    d = co - V3((0, 0, cz))
    if name == "Head" and d.z < 0:
        return V3((d.x * HS, d.y * HS, d.z)) + V3((0, 0, cz))
    return V3((0, 0, cz)) + d * HS
TOP = max(head_tf(n, v.co).z for n, b, _ in PARTS if n in HEADPARTS for v in b.verts)
K = (1.70 if F else 1.78) / TOP  # build coords * K = final metres
MOUTH = head_tf("Mouth", surf(HEAD_BVH, 0, cz + 0.048, off=0.0012)) * K

def gloft(bm, rings, n, **kw):
    """loft with ring centres/radii given in FINAL metres"""
    out = []
    for r in rings:
        r = dict(r); r["c"] = r["c"] / K
        for key in ("rx", "ry", "ryb"):
            if key in r:
                r[key] = r[key] / K
        out.append(r)
    return loft(bm, out, n, **kw)

# ---------------------------------------------------------------- props (fixed real size, never scaled)
PROPFILES = {"beer": "lwf_beer_cup_v2.glb", "soft": "lwf_soft_drink_cup_v1.glb", "tray": "lwf_chips_tray_v1.glb"}
PROPS = {}
def load_prop(key):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=PROPS_DIR + "/" + PROPFILES[key])
    obs = [o for o in bpy.data.objects if o not in before]
    for o in obs:
        if o.type == 'MESH':
            o.data.transform(o.matrix_world); o.matrix_world = Matrix.Identity(4)
    mesh = [o for o in obs if o.type == 'MESH'][0]
    bvh = BVHTree.FromPolygons([v.co.copy() for v in mesh.data.vertices], [tuple(p.vertices) for p in mesh.data.polygons])
    PROPS[key] = dict(objs=obs, bvh=bvh, mesh=mesh)
    return PROPS[key]

def cup_radius(key, h):
    hit = PROPS[key]["bvh"].ray_cast(V3((0.3, 0, h)), V3((-1, 0, 0)))
    return hit[0].x

def ik(S_, W, Lu, Lf, pole):
    d = (W - S_).length
    if d > (Lu + Lf) * 0.999:
        print(f"IK stretch {d:.3f} > {Lu + Lf:.3f}; clamping reach")
        W = S_ + (W - S_).normalized() * (Lu + Lf) * 0.999; d = (W - S_).length
    a = (Lu * Lu - Lf * Lf + d * d) / (2 * d)
    h = math.sqrt(max(Lu * Lu - a * a, 0))
    ax = (W - S_).normalized()
    p = pole - ax * pole.dot(ax)
    return S_ + ax * a + p.normalized() * h, W

def relaxed_joints(sx):
    sh = V3((sx * (shx + 0.004), -0.004, z(1.275)))
    el = V3((sx * (shx + 0.052), -0.012, z(1.05)))
    wr = V3((sx * (shx + 0.066), 0.014, z(0.84)))
    return sh, el, wr

# grip sizes in final metres
GT = 0.015                   # half thickness of the gripping hand
GH = 0.036 if F else 0.040   # half height of the gripping hand

def cup_hand(key, M, h=-0.004, yaw=0.0):
    """right hand wrapped round a cup; returns (builder, wrist, radius) in final metres. M: cup world matrix.
    yaw turns the grip round the cup axis (positive brings the wrist round to the cup's outer side)."""
    M = M @ Matrix.Rotation(math.radians(yaw), 4, 'Z')
    rc = max(cup_radius(key, h - GH), cup_radius(key, h), cup_radius(key, h + GH)) + 0.002
    up = M.to_3x3() @ V3((0, 0, 1))
    P = lambda phi, r, hh: M @ V3((r * math.cos(math.radians(phi)), r * math.sin(math.radians(phi)), hh))
    wrist = P(-68, rc + 0.036, h - 0.010)
    def build(bm):
        gloft(bm, [R(wrist, 0.026, 0.028),
                   R(P(-40, rc + GT + 0.006, h - 0.004), GT * 1.1, GH * 0.85),
                   R(P(-12, rc + GT + 0.001, h), GT, GH),
                   R(P(22, rc + GT, h), GT * 0.95, GH),
                   R(P(55, rc + GT * 0.9, h), GT * 0.85, GH * 0.95),
                   R(P(88, rc + GT * 0.8, h), GT * 0.7, GH * 0.85),
                   R(P(106, rc + GT * 0.6, h), GT * 0.4, GH * 0.55)], 8, offset=0.5, ref=up)
        gloft(bm, [R(P(-28, rc + GT + 0.004, h + GH * 0.55), 0.011, 0.012),
                   R(P(-62, rc + 0.011, h + GH * 0.85), 0.010, 0.011),
                   R(P(-88, rc + 0.008, h + GH * 0.8), 0.007, 0.008)], 6, offset=0.5, ref=up)
    return build, wrist, rc

def tray_hand(T):
    """right hand palm-up under the tray, thumb steadying its outer edge"""
    pz = T.z - 0.025 - 0.013 - 0.0006
    wrist = V3((T.x + 0.022, T.y - 0.090, pz - 0.004))
    Y = V3((0, 1, 0))
    def build(bm):
        gloft(bm, [R(wrist, 0.026, 0.028, t=Y),
                   R(V3((T.x + 0.016, T.y - 0.055, pz)), 0.036, 0.014, t=Y),
                   R(V3((T.x + 0.010, T.y - 0.010, pz)), 0.040, 0.013, t=Y),
                   R(V3((T.x + 0.006, T.y + 0.030, pz + 0.001)), 0.034, 0.011, t=Y),
                   R(V3((T.x + 0.004, T.y + 0.048, pz + 0.002)), 0.020, 0.007, t=Y)],
              8, offset=0.5, ref=V3((0, 0, 1)))
        gloft(bm, [R(V3((T.x + 0.060, T.y - 0.050, pz)), 0.012, 0.011),
                   R(V3((T.x + 0.098, T.y - 0.030, T.z - 0.022)), 0.011, 0.010),
                   R(V3((T.x + 0.100, T.y - 0.012, T.z + 0.010)), 0.008, 0.008)], 6, offset=0.5)
    return build, wrist

def mouth_hand(sx):
    """empty hand raised to the mouth, fingers and thumb pinching at the lips"""
    Fp = MOUTH + V3((sx * 0.006, 0.006, -0.002))
    W = MOUTH + V3((sx * 0.062, 0.040, -0.088))
    def build(bm):
        d = Fp - W
        gloft(bm, [R(W, 0.024, 0.026), R(W + d * 0.35, 0.020, 0.034), R(W + d * 0.72, 0.016, 0.026),
                   R(Fp, 0.007, 0.009)], 8, offset=0.5, ref=V3((0, 0, 1)))
        gloft(bm, [R(W + d * 0.40 + V3((0, 0.004, -0.022)), 0.011, 0.012),
                   R(Fp + V3((0, 0.000, -0.012)), 0.007, 0.007)], 6, offset=0.5)
    return build, W

# ---------------------------------------------------------------- pose solve
ARMS = {-1: None, 1: None}   # None = relaxed; else dict(sh, el, wr in build coords, hand builder)
ATTACH = {}                  # product -> prop world matrix (final metres)
def pose_arm(sx, hand_build, wrist_final, pole):
    sh, el0, wr0 = relaxed_joints(sx)
    Lu, Lf = (el0 - sh).length * K, (wr0 - el0).length * K
    E, W = ik(sh * K, wrist_final, Lu, Lf, pole)
    ARMS[sx] = dict(sh=sh, el=E / K, wr=W / K, hand=hand_build)

sh_r = relaxed_joints(1)[0] * K
if POSE == "drink_hold":
    for key in ("beer", "soft"):
        load_prop(key)
    C = sh_r + V3((0.022, 0.240, -0.130))
    M = Matrix.Translation(C)
    hb, wr, _ = cup_hand("soft", M)   # soft is the wider cup; the same grip fits both
    pose_arm(1, hb, wr, V3((0.35, -0.55, -1)))
    ATTACH = {"beer": M, "soft": M}
elif POSE in ("food_hold", "eating"):
    load_prop("tray")
    T = sh_r + V3((-0.040, 0.245, -0.118))
    M = Matrix.Translation(T)
    hb, wr = tray_hand(T)
    pose_arm(1, hb, wr, V3((0.35, -0.55, -1)))
    ATTACH = {"tray": M}
    if POSE == "eating":
        hb2, wr2 = mouth_hand(-1)
        pose_arm(-1, hb2, wr2, V3((-1, 0.1, -0.7)))
elif POSE == "drinking_beer":
    load_prop("beer")
    rot = Matrix.Rotation(math.radians(55), 4, 'X')
    A = MOUTH + V3((0, 0.001, 0)) - (rot @ V3((0, -0.046, 0.077)))
    M = Matrix.Translation(A) @ rot
    hb, wr, _ = cup_hand("beer", M)
    pose_arm(1, hb, wr, V3((1, 0.0, -0.6)))
    ATTACH = {"beer": M}
elif POSE == "drinking_soft":
    load_prop("soft")
    # tilt the cup so the straw meets the lips while the cup body clears the chest
    rot = Matrix.Rotation(math.radians(25), 4, 'X')
    A = MOUTH + V3((0, 0.002, 0)) - (rot @ V3((0.012, 0, 0.142)))
    M = Matrix.Translation(A) @ rot
    hb, wr, _ = cup_hand("soft", M, yaw=62)
    pose_arm(1, hb, wr, V3((1, 0.0, -0.6)))
    ATTACH = {"soft": M}

# ---------------------------------------------------------------- legs / trousers
bm = part("Trousers", SLOT["trousers"])
hipw = 0.150 if F else 0.145
loft(bm, [
    R((0, 0, 0.78 * S), 0.11, 0.075),
    R((0, 0, 0.84 * S), hipw - 0.004, 0.095, ryb=0.102 if F else 0.095),
    R((0, 0, 0.90 * S), hipw - 0.004, 0.096, ryb=0.100 if F else 0.094),
    R((0, 0, 0.93 * S), hipw - 0.012, 0.092, ryb=0.095),
], 10)
for sx in (-1, 1):
    x0 = 0.080 * sx
    loft(bm, [
        R((x0, 0.0, 0.86 * S), 0.084, 0.086),
        R((x0 * 1.02, 0.004, 0.72 * S), 0.080, 0.080),
        R((x0 * 1.05, 0.010, 0.47 * S), 0.067, 0.070),          # knee
        R((x0 * 1.07, 0.004, 0.28 * S), 0.066, 0.068),
        R((x0 * 1.09, 0.006, 0.13), 0.070, 0.072),               # slight break over shoe
        R((x0 * 1.09, 0.010, 0.085), 0.072, 0.074),
    ], 8, offset=0.5)
finish(bm)

# ---------------------------------------------------------------- shoes
bm = part("Shoes", SLOT["dark"])
for sx in (-1, 1):
    x0 = 0.088 * sx
    rings = []
    for y, w, h, sz in [(-0.085, 0.046, 0.046, 0.046), (-0.068, 0.058, 0.060, 0.058), (-0.01, 0.064, 0.068, 0.066),
                        (0.07, 0.064, 0.056, 0.054), (0.14, 0.060, 0.046, 0.045), (0.190, 0.048, 0.036, 0.036),
                        (0.207, 0.028, 0.024, 0.028)]:
        rings.append(dict(c=V3((x0, y, sz)), rx=w, ry=h, zmin=0.0, t=V3((0, 1, 0))))
    loft(bm, rings, 8, offset=0.5, power=2.6, ref=V3((0, 0, 1)))
finish(bm)

# ---------------------------------------------------------------- torso / tee
bm = part("Tee", SLOT["tee"])
if F:
    torso = [
        R((0, 0.000, z(0.885)), 0.155, 0.104, ryb=0.106),
        R((0, 0.000, z(0.95)), 0.143, 0.096, ryb=0.098),
        R((0, 0.000, z(1.03)), 0.128, 0.088, ryb=0.090),
        R((0, 0.004, z(1.12)), 0.136, 0.095, ryb=0.090, bulge=0.012),
        R((0, 0.004, z(1.19)), 0.146, 0.100, ryb=0.090, bulge=0.030),
        R((0, 0.000, z(1.26)), 0.150, 0.094, ryb=0.092, bulge=0.012),
        R((0, -0.004, z(1.32)), 0.158, 0.080, ryb=0.084),
        R((0, -0.008, z(1.365)), 0.110, 0.060, ryb=0.064),
        R((0, -0.010, z(1.385)), 0.060, 0.048, ryb=0.050),
    ]
else:
    torso = [
        R((0, 0.000, z(0.885)), 0.158, 0.104, ryb=0.100),
        R((0, 0.000, z(0.95)), 0.155, 0.102, ryb=0.098),
        R((0, 0.004, z(1.05)), 0.158, 0.104, ryb=0.098),
        R((0, 0.006, z(1.16)), 0.170, 0.110, ryb=0.100, bulge=0.008),
        R((0, 0.004, z(1.25)), 0.182, 0.108, ryb=0.100),
        R((0, -0.002, z(1.32)), 0.188, 0.090, ryb=0.092),
        R((0, -0.008, z(1.37)), 0.130, 0.068, ryb=0.070),
        R((0, -0.010, z(1.395)), 0.066, 0.052, ryb=0.054),
    ]
loft(bm, torso, 12, offset=0.5, power=2.3)
# sleeves (follow the upper arm when it is posed)
for sx in (-1, 1):
    arm = ARMS[sx]
    if arm is None:
        loft(bm, [
            R((sx * (shx - 0.03), -0.006, z(1.305)), 0.046 * sk, 0.052 * sk),
            R((sx * (shx + 0.012), -0.004, z(1.275)), 0.052 * sk, 0.056 * sk),
            R((sx * (shx + 0.036), 0.000, z(1.20)), 0.053 * sk, 0.056 * sk),
            R((sx * (shx + 0.044), 0.002, z(1.178)), 0.056 * sk, 0.059 * sk),
        ], 8, offset=0.5)
    else:
        sh, el = arm["sh"], arm["el"]
        u = (el - sh).normalized(); Lu = (el - sh).length
        o = V3((sx * 0.008, 0, 0))
        loft(bm, [
            R((sx * (shx - 0.03), -0.006, z(1.305)), 0.046 * sk, 0.052 * sk),
            R(sh + o + u * Lu * 0.02, 0.052 * sk, 0.056 * sk),
            R(sh + o + u * Lu * 0.33, 0.053 * sk, 0.056 * sk),
            R(sh + o + u * Lu * 0.43, 0.056 * sk, 0.059 * sk),
        ], 8, offset=0.5)
TEE_BVH = finish(bm)

# ---------------------------------------------------------------- chest patch (flush decal)
decal("Patch", SLOT["patch"], TEE_BVH, lin(-0.065, 0.065, 11), lin(z(1.19), z(1.285), 9), off=0.0035)


# ---------------------------------------------------------------- arms / hands
lining = part("SleeveLining", SLOT["tee"])
bm = part("Arms", SLOT["skin"])
for sx in (-1, 1):
    arm = ARMS[sx]
    if arm is None:
        sh, el, wr = relaxed_joints(sx)
    else:
        sh, el, wr = arm["sh"], arm["el"], arm["wr"]
    # the part of the upper arm inside the sleeve is tee fabric, so the armpit gap reads as shirt
    loft(lining, [R(sh, ua * 1.03, ua * 1.03), R(sh.lerp(el, 0.36), ua * 1.0, ua * 1.02)], 8, offset=0.5)
    loft(bm, [
        R(sh.lerp(el, 0.30), ua * 0.97, ua * 0.99),
        R(sh.lerp(el, 0.5), ua * 0.95, ua * 0.98),
        R(el, ua * 0.80, ua * 0.84),
        R(el.lerp(wr, 0.4), ua * 0.82, ua * 0.86),
        R(wr, ua * 0.62, ua * 0.66),
    ], 8, offset=0.5)
    if arm is not None:
        arm["hand"](bm)
        continue
    # mitten hand: flattened across X (palm faces thigh)
    d = V3((sx * 0.004, 0.006, -1)).normalized()
    loft(bm, [
        R(wr + d * 0.000, 0.017 * hw, 0.026 * hw),
        R(wr + d * 0.030, 0.021 * hw, 0.040 * hw),
        R(wr + d * 0.065, 0.019 * hw, 0.038 * hw),
        R(wr + d * 0.088, 0.011 * hw, 0.026 * hw),
    ], 8, offset=0.5, ref=V3((0, 1, 0)))
    # thumb
    tb = wr + d * 0.028 + V3((-sx * 0.010, 0.030 * hw, 0))
    loft(bm, [
        R(tb, 0.010, 0.011, t=V3((-sx * 0.3, 0.3, -1))),
        R(tb + V3((-sx * 0.006, 0.010, -0.035)), 0.008, 0.009),
    ], 6, offset=0.5)
finish(bm)
finish(lining)

# ---------------------------------------------------------------- assemble
objs = []
for name, bm, slot in PARTS:
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    for p in me.polygons:
        p.use_smooth = False
    me.materials.append(MAT)
    uv = me.uv_layers.new(name="PaletteUV")
    u = (8 * slot + 4) / 96.0
    for l in uv.data:
        l.uv = (u, 0.5)
    for v in me.vertices:
        v.co = (head_tf(name, v.co) if name in HEADPARTS else v.co) * K
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    vg = ob.vertex_groups.new(name=name)
    vg.add(list(range(len(me.vertices))), 1.0, 'REPLACE')
    objs.append(ob)

bpy.ops.object.select_all(action='DESELECT')
for o in objs:
    o.select_set(True)
bpy.context.view_layer.objects.active = objs[0]
bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
body.name = f"Attendee_{SEX}_{POSE}_v6"
me = body.data
bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces)); bm.to_mesh(me); bm.free()

# attachment sockets (metadata only) and review props (never exported with the body)
sockets = {}
for key, Mx in ATTACH.items():
    nm = "Attachment_Food" if key == "tray" else "Attachment_Cup"
    if nm not in sockets:
        sock = bpy.data.objects.new(nm, None)
        bpy.context.scene.collection.objects.link(sock); sock.matrix_world = Mx; sockets[nm] = sock
shown = "soft" if POSE == "drink_hold" else None
for key, pr in PROPS.items():
    for o in pr["objs"]:
        if key in ATTACH and (shown is None or key == shown):
            o.name = "REVIEW_PROP_" + o.name
            o.matrix_world = ATTACH[key] @ o.matrix_world
        else:
            bpy.data.objects.remove(o)

zs = [v.co.z for v in me.vertices]
tris = sum(len(p.vertices) - 2 for p in me.polygons)
print(f"BUILD {body.name} tris={tris} verts={len(me.vertices)} height={max(zs) - min(zs):.3f} K={K:.4f}")
bpy.ops.wm.save_as_mainfile(filepath=OUT)

# body-only GLB + manifest entry
glb = OUT[:-6] + ".glb"
bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
for sck in sockets.values():
    sck.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.export_scene.gltf(filepath=glb, export_format='GLB', use_selection=True, export_yup=True, export_animations=False)
raw = open(glb, "rb").read()
def godot(Mx):
    p = Mx.to_translation(); e = Mx.to_euler('XYZ')
    return {"position": [round(p.x, 5), round(p.z, 5), round(-p.y, 5)],
            "rotation_degrees": [round(math.degrees(e.x), 3), round(math.degrees(e.z), 3), round(-math.degrees(e.y), 3)],
            "scale": [1, 1, 1]}
entry = {"state": "drinking" if POSE.startswith("drinking") else POSE,
         "product": POSE.split("_")[1] if POSE.startswith("drinking") else None,
         "sha256": hashlib.sha256(raw).hexdigest(), "triangles": tris,
         "attachments": {k: godot(v) for k, v in ATTACH.items()},
         "mouth_target_godot": [round(MOUTH.x, 5), round(MOUTH.z, 5), round(-MOUTH.y, 5)]}
open(OUT[:-6] + ".json", "w").write(json.dumps(entry, indent=1))

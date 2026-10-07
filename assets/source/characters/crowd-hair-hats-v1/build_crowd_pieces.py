# Crowd hair & hats v1: production head pieces for the v2 guest bodies (and the performer bodies, which share the head).
#   blender -b --python build_crowd_pieces.py -- <out_dir>
# Every piece is authored in the guest root frame (feet at the origin, facing Godot -Z) and is placed as a sibling of
# GuestBody at the identity transform: the head is byte-identical in every guest pose and in lwf_performer_*_body_v2.
# Reads the ORIGINAL bodies with baked hair from attendee-v6-draft/poses (generator output), so it can run before or after
# strip_baked_hair.py has been applied to the game copies.
import bpy, bmesh, math, sys, os, json, hashlib
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
V3 = Vector
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = argv[0] if argv else os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
SRC = "C:/Projects/festival-tycoon/assets/source/characters/attendee-v6-draft/poses/"
MANIFEST = json.load(open(SRC + "attendee_poses_v6_manifest.json"))
HAIR_U = (8 * 10 + 4) / 96            # guest palette slot 10 (hair base); slot 11 is the hair light reserve
REPORT = {}

# accessory palette (128x8, 16 slots, u = (8*slot+4)/128). Recolour per guest: 0-1 cap, 4-5 sunglasses, 8-10 flowers.
# 3, 9 and 15 are the daisy crown (v1 daisy chain, 2026-10-07): 3 dark plum outline, 9 white petals, 15 sunflower centre.
ACC = {0: "C9553A", 1: "A8432F", 2: "C9553A", 3: "3A1F2A", 4: "1A1A1A", 5: "2A3540", 8: "E58FA5", 9: "FBF7EE", 10: "E8C547",
       11: "D9A33A", 12: "5E8A4A", 15: "FFD21F"}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_glb(path):
    before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=path)
    objs = [o for o in bpy.data.objects if o not in before]
    for o in objs:
        if o.type == 'MESH': o.data.transform(o.matrix_world); o.matrix_world = Matrix.Identity(4)
    return objs


def slot_of(me, p, W=96):
    return int(me.uv_layers[0].data[p.loop_indices[0]].uv[0] * W / 8)


def smooth(t): t = max(0.0, min(1.0, t)); return t * t * (3 - 2 * t)


def table(points):
    pts = sorted(points)
    def f(az):
        a = abs(((az + 180) % 360) - 180)
        for (a0, v0), (a1, v1) in zip(pts, pts[1:]):
            if a0 <= a <= a1: return v0 + (v1 - v0) * ((a - a0) / (a1 - a0) if a1 > a0 else 0)
        return pts[-1][1]
    return f


class Head:
    """landmarks and ray targets from the relaxed body with its hair removed"""
    def __init__(self, body, hair):
        me = body.data; verts = [v.co.copy() for v in me.vertices]
        skin, eyes, allp = [], [], []
        for p in me.polygons:
            s = slot_of(me, p); vs = [verts[i] for i in p.vertices]; allp.append(list(p.vertices))
            if s == 9 and min(v.z for v in vs) > 1.3: eyes += vs
            if s in (0, 1, 2, 9) and min(v.z for v in vs) > 1.3: skin.append(list(p.vertices))
        self.eyeZ = sum(v.z for v in eyes) / len(eyes); self.eyeX = sum(abs(v.x) for v in eyes) / len(eyes)
        self.eyeY = max(v.y for v in eyes)
        hv = [verts[i] for poly in skin for i in poly if verts[i].z > self.eyeZ - 0.12]
        self.top = max(v.z for v in hv)
        band = [v for v in hv if abs(v.z - self.eyeZ) < 0.03]
        self.C = V3((0, (min(v.y for v in band) + max(v.y for v in band)) / 2, self.eyeZ + 0.01))
        self.head_tree = BVHTree.FromPolygons(verts, skin)
        self.body_tree = BVHTree.FromPolygons(verts, allp)
        hverts = [v.co.copy() for v in hair.data.vertices]
        self.hair_tree = BVHTree.FromPolygons(hverts, [list(p.vertices) for p in hair.data.polygons])
        n = len(verts)
        self.full_tree = BVHTree.FromPolygons(verts + hverts, skin + [[n + i for i in p.vertices] for p in hair.data.polygons])

    def dirv(self, az, el):
        return V3((math.sin(math.radians(az)) * math.cos(math.radians(el)), math.cos(math.radians(az)) * math.cos(math.radians(el)), math.sin(math.radians(el))))

    def hit(self, az, el, tree=None):
        d = self.dirv(az, el)
        loc, n, i, dist = (tree or self.head_tree).ray_cast(self.C + d * 0.5, -d)
        if loc is None: loc, n, i, dist = (tree or self.head_tree).find_nearest(self.C + d * 0.12)
        return loc, n, d

    def el_for_z(self, az, z, tree=None):
        lo, hi = -85.0, 86.0
        for _ in range(28):
            mid = (lo + hi) / 2
            if self.hit(az, mid, tree)[0].z < z: lo = mid
            else: hi = mid
        return (lo + hi) / 2


class Mesh:
    def __init__(self, name, W):
        self.name, self.W = name, W; self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new("UVMap")
    def v(self, p): return self.bm.verts.new(p)
    def face(self, vs, slot):
        try: f = self.bm.faces.new(vs)
        except ValueError: return None
        for l in f.loops: l[self.uv].uv = ((8 * slot + 4) / self.W, 0.5)
        return f
    def link(self, mat, recalc=True):
        """recalc=False keeps the authored winding (single-sided open parts that must face out, like the crown's flowers)"""
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=1e-5)
        if recalc: bmesh.ops.recalc_face_normals(self.bm, faces=list(self.bm.faces))
        me = bpy.data.meshes.new(self.name); self.bm.to_mesh(me); self.bm.free(); me.materials.append(mat)
        for p in me.polygons: p.use_smooth = False
        o = bpy.data.objects.new(self.name, me); bpy.context.scene.collection.objects.link(o); return o


def accessory_material():
    m = bpy.data.materials.new("LWF_CrowdAccessory_MattePalette"); m.use_nodes = True
    img = bpy.data.images.new("lwf_crowd_accessory_palette", 128, 8)
    px = []
    for y in range(8):
        for s in range(16):
            hx = ACC.get(s, "808080"); px += ([int(hx[k:k + 2], 16) / 255 for k in (0, 2, 4)] + [1.0]) * 8
    img.pixels = px; img.pack()
    tx = m.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = img; tx.interpolation = 'Closest'
    b = m.node_tree.nodes["Principled BSDF"]; b.inputs["Roughness"].default_value = 0.8
    m.node_tree.links.new(tx.outputs[0], b.inputs["Base Color"]); return m


def export(objs, name, extra=None):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(OUT, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True, export_animations=False)
    tris = sum(len(p.vertices) - 2 for o in objs for p in o.data.polygons)
    REPORT[name] = dict(file=name + ".glb", nodes=[o.name for o in objs], triangles=tris,
                        sha256=hashlib.sha256(open(path, "rb").read()).hexdigest(), **(extra or {}))
    print(f"EXPORTED {name} tris={tris}")


def ring_faces(m, a, b, slot):
    n = len(a)
    for k in range(n): m.face((a[k], a[(k + 1) % n], b[(k + 1) % n], b[k]), slot)


def tube(m, path, r, slot, n=4):
    rings = []
    for i, p in enumerate(path):
        t = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
        a = t.cross(V3((0, 0, 1))); a = a.normalized() if a.length > 1e-4 else V3((1, 0, 0)); b = t.cross(a)
        rings.append([m.v(p + (a * math.cos(2 * math.pi * k / n) + b * math.sin(2 * math.pi * k / n)) * r) for k in range(n)])
    for r0, r1 in zip(rings, rings[1:]): ring_faces(m, r0, r1, slot)
    m.face(list(reversed(rings[0])), slot); m.face(rings[-1], slot)


# ============================================================== pieces
def build_sex(sex):
    reset()
    objs = import_glb(SRC + f"lwf_attendee_{sex}_relaxed_v2.glb")
    body = next(o for o in objs if o.type == 'MESH')
    guest_mat = body.data.materials[0]
    # split the baked hair off a copy of the body: hair = palette slots 10-11
    hair = body.copy(); hair.data = body.data.copy(); bpy.context.scene.collection.objects.link(hair)
    for ob, keep_hair in ((hair, True), (body, False)):
        bm = bmesh.new(); bm.from_mesh(ob.data); uvl = bm.loops.layers.uv.active
        dead = [f for f in bm.faces if (int(f.loops[0][uvl].uv[0] * 96 / 8) in (10, 11)) != keep_hair]
        bmesh.ops.delete(bm, geom=dead, context='FACES')
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
        bm.to_mesh(ob.data); bm.free()
    H = Head(body, hair)
    acc = accessory_material()
    for o in objs:
        if o is not body: bpy.data.objects.remove(o)
    bpy.data.objects.remove(body)
    hair.name = "LWF_Hair_Default"; hair.data.name = "LWF_Hair_Default"
    hair_data = hair.data

    def only(*keep):
        for o in list(bpy.context.scene.objects):
            if o not in keep: bpy.context.scene.collection.objects.unlink(o)

    # ---- default hair: exactly today's baked hair
    only(hair); export([hair], f"lwf_hair_{sex}_default_v1", dict(note="today's baked hair, unchanged geometry"))

    # ---- cap band (z by angle, 0 = front) shared by the cap and the hair trim
    band = lambda az: H.eyeZ + 0.050 - 0.036 * smooth((abs(((az + 180) % 360) - 180) - 60) / 100)
    CUT = 0.014                                     # hair above band + CUT goes; the cap's band edge covers the cut

    # ---- default hair under a cap: everything above the cut removed; below it (sides, back, the female lengths) kept
    trim = bpy.data.objects.new("LWF_Hair_DefaultUnderCap", hair_data.copy()); bpy.context.scene.collection.objects.link(trim)
    bm = bmesh.new(); bm.from_mesh(trim.data)
    def above(v):
        d = v.co - H.C; az = math.degrees(math.atan2(d.x, d.y))
        return v.co.z > band(az) + CUT
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if any(above(v) for v in f.verts)], context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bm.to_mesh(trim.data); bm.free()
    trim_tree = BVHTree.FromPolygons([v.co.copy() for v in trim.data.vertices], [list(p.vertices) for p in trim.data.polygons]) if trim.data.polygons else None
    only(trim); export([trim], f"lwf_hair_{sex}_default_under_cap_v1", dict(note="default hair with everything above the cap band removed"))

    # ---- cap: crown hugging the scalp, eased out over whatever hair remains at the band so the cut edge is covered
    m = Mesh("LWF_Hat_Cap", 128)
    COLS, ROWS = 12, 6
    grid = []
    for j in range(COLS):
        az = -180 + 360 * j / COLS
        e0 = H.el_for_z(az, band(az))
        # hair thickness at the band in this column (0 where the trimmed hair doesn't reach)
        S0, n0, d0 = H.hit(az, e0)
        t_band = 0.0
        if trim_tree:
            for dz in (0.0, 0.006, 0.012):
                Sx, nx, dx = H.hit(az, H.el_for_z(az, band(az) + dz))
                hloc = trim_tree.ray_cast(H.C + dx * 0.5, -dx)[0]
                if hloc is not None: t_band = max(t_band, (hloc - H.C).length - (Sx - H.C).length)
        col = []
        for i in range(ROWS):
            u = i / (ROWS - 1)
            el = e0 + (86 - e0) * u ** 0.9
            S, n, d = H.hit(az, el)
            o = (n + d).normalized()
            t = 0.007 + (t_band + 0.004) * (1 - u) ** 1.6
            col.append(m.v(S + o * t))
        grid.append(col)
    for j in range(COLS):
        j1 = (j + 1) % COLS
        for i in range(ROWS - 1): m.face((grid[j][i], grid[j1][i], grid[j1][i + 1], grid[j][i + 1]), 0)
    apex_hit = H.hit(0, 90)[0]
    apex = m.v(apex_hit + V3((0, 0, 0.009)))
    for j in range(COLS): m.face((grid[j][-1], grid[(j + 1) % COLS][-1], apex), 0)
    # band rim: a short inward skirt so the edge has thickness
    inner = [m.v(grid[j][0].co + (H.C - grid[j][0].co).normalized() * 0.006 + V3((0, 0, 0.004))) for j in range(COLS)]
    ring_faces(m, [grid[j][0] for j in range(COLS)], inner, 1)
    # visor over the front, curved down slightly
    front = [j for j in range(COLS) if abs(-180 + 360 * j / COLS) <= 60]
    front.sort(key=lambda j: -180 + 360 * j / COLS)
    vt, vb, vo = [], [], []
    for j in front:
        az = -180 + 360 * j / COLS
        p = grid[j][0].co; dvec = V3((p.x - H.C.x, p.y - H.C.y, 0)).normalized()
        w = 0.070 * max(0.0, math.cos(math.radians(az))) ** 0.6 + 0.012
        q = p + dvec * w + V3((0, 0, -0.012))
        vo.append(m.v(q)); vb.append(m.v(q + V3((0, 0, -0.005))))
        vt.append(grid[j][0])
    base_b = [m.v(v.co + V3((0, 0, -0.005))) for v in vt]
    for k in range(len(front) - 1):
        m.face((vt[k], vo[k], vo[k + 1], vt[k + 1]), 1)
        m.face((vo[k + 1], vo[k], vb[k], vb[k + 1]), 1)
        m.face((base_b[k + 1], vb[k + 1], vb[k], base_b[k]), 1)
    for k in (0, len(front) - 1):
        m.face((vt[k], base_b[k], vb[k], vo[k]) if k == 0 else (vt[k], vo[k], vb[k], base_b[k]), 1)
    # button
    bt = apex.co.copy(); b0 = [m.v(bt + V3((0.008 * math.cos(a), 0.008 * math.sin(a), -0.002))) for a in (0, 2.09, 4.19)]
    btop = m.v(bt + V3((0, 0, 0.007)))
    for k in range(3): m.face((b0[k], b0[(k + 1) % 3], btop), 2)
    cap = m.link(acc)
    only(cap); export([cap], f"lwf_hat_cap_{sex}_v1", dict(note="wear with lwf_hair_*_default_under_cap_v1 (or bald); never with the mohawk"))

    # ---- flower crown: a daisy chain, one generous fit over the default hair (approved option C, flower-crown board).
    # Seven 6.8 cm white daisies with sunflower centres and dark plum outlines, tilted up to face the game camera, on a 14 mm
    # vine band. Every part is single-sided and wound to face out/up (the inside of the band sits against the hair), so
    # the mesh is linked without recalculating normals.
    m = Mesh("LWF_Hat_FlowerCrown", 128)
    zf = lambda az: H.top - 0.050 + 0.012 * math.cos(math.radians(az))
    N = 14
    pts = []
    for k in range(N):
        az = -180 + 360 * k / N
        el = H.el_for_z(az, zf(az), H.full_tree)
        S, n, d = H.hit(az, el, H.full_tree)
        out = V3((d.x, d.y, 0)).normalized(); pts.append((S + out * 0.005, out, az))
    BH, BT = 0.014, 0.005
    lo = [m.v(p + o * BT) for p, o, _ in pts]; hi = [m.v(p + o * BT + V3((0, 0, BH))) for p, o, _ in pts]
    hin = [m.v(p + V3((0, 0, BH))) for p, o, _ in pts]
    ring_faces(m, lo, hi, 12); ring_faces(m, hi, hin, 12)                 # outer face and top of the band

    def fan(ring, centre, slot):
        for j in range(len(ring)): m.face((ring[j], ring[(j + 1) % len(ring)], centre), slot)

    def daisy(p, nrm, R):
        s = nrm.cross(V3((0, 0, 1))).normalized(); u = s.cross(nrm).normalized()
        circ = lambda r, k, off, inner=1.0: [m.v(p + nrm * off + (s * math.cos(2 * math.pi * j / k) + u * math.sin(2 * math.pi * j / k)) * (r if j % 2 == 0 else r * inner)) for j in range(k)]
        fan(circ(R * 1.18, 8, -0.0025), m.v(p + nrm * -0.0025), 3)          # outline disc behind the petals
        fan(circ(R, 16, 0.0, 0.55), m.v(p + nrm * 0.004), 9)               # eight white petals, slightly cupped
        fan(circ(R * 0.36, 6, 0.0055), m.v(p + nrm * 0.008), 15)           # sunflower centre

    for az in (0, 51.4, 102.9, 154.3, -154.3, -102.9, -51.4):
        p, out, _ = min(pts, key=lambda q: abs(((q[2] - az + 180) % 360) - 180))
        daisy(p + out * 0.008 + V3((0, 0, 0.011)), (out * 0.6 + V3((0, 0, 1))).normalized(), 0.034)
    crown = m.link(acc, recalc=False)
    only(crown); export([crown], f"lwf_hat_flower_crown_{sex}_v1", dict(note="daisy chain; sits on the default hair (not bald, not the mohawk)"))

    # ---- sunglasses: fitted to the face; arms to the ear line (under long hair)
    m = Mesh("LWF_Sunglasses", 128)
    ys = []
    lens_pts = {}
    for sx in (-1, 1):
        cx, cz = sx * H.eyeX, H.eyeZ - 0.004
        ring = []
        for k in range(6):
            t = 2 * math.pi * (k + 0.5) / 6
            x = cx + 0.025 * math.cos(t); z = cz + 0.017 * math.sin(t) * (1.0 if math.sin(t) > 0 else 1.15)
            h = H.head_tree.ray_cast(V3((x, 0.5, z)), V3((0, -1, 0)))[0]
            ring.append(V3((x, (h.y if h else H.eyeY) + 0.010, z))); ys.append(ring[-1].y)
        lens_pts[sx] = (cx, cz, ring)
    yl = max(ys)
    for sx, (cx, cz, ring) in lens_pts.items():
        lens = [m.v(V3((p.x, yl, p.z))) for p in ring]; c = m.v(V3((cx, yl + 0.001, cz)))
        for k in range(6): m.face((lens[k], lens[(k + 1) % 6], c), 5)
        fr = [m.v(V3((cx + (p.x - cx) * 1.15, yl - 0.003, cz + (p.z - cz) * 1.18))) for p in ring]
        for k in range(6): m.face((fr[k], fr[(k + 1) % 6], lens[(k + 1) % 6], lens[k]), 4)
        side = H.head_tree.ray_cast(V3((sx * 0.4, H.C.y - 0.02, cz + 0.006)), V3((-sx, 0, 0)))[0]
        ex = (side.x if side else sx * 0.09) + sx * 0.005
        tube(m, [V3((cx + sx * 0.029, yl - 0.004, cz + 0.006)), V3((ex, yl - 0.03, cz + 0.006)), V3((ex, H.C.y - 0.035, cz + 0.004))], 0.003, 4, n=3)
    tube(m, [V3((-H.eyeX + 0.024, yl - 0.001, H.eyeZ + 0.007)), V3((H.eyeX - 0.024, yl - 0.001, H.eyeZ + 0.007))], 0.0032, 4, n=3)
    glasses = m.link(acc)
    only(glasses); export([glasses], f"lwf_sunglasses_{sex}_v1")

    # ---- mohawk: shaved sides, tall spiked fin from the forehead to the nape (also for punk band performers)
    m = Mesh("LWF_Hair_Mohawk", 96)
    rows = []
    for k in range(15):
        u = k / 14
        a = -42 + u * 165
        az = 0 if a < 0 else 180
        el = 90 + a if a < 0 else 90 - a
        S, n, d = H.hit(az, el)
        h = (0.040 + 0.110 * math.sin(math.pi * min(1.0, u * 1.15)) ** 0.7) * (1.0 if k % 2 == 0 else 0.70)
        w = 0.024 * (1 - 0.3 * u)
        lean = V3((0, -0.012, 0)) if az == 180 else V3((0, 0.006, 0))
        rows.append((S + d * 0.002 - V3((w, 0, 0)), S + d * 0.002 + V3((w, 0, 0)), S + d * h + lean))
    vs = [[m.v(a), m.v(b), m.v(c)] for a, b, c in rows]
    for r0, r1 in zip(vs, vs[1:]):
        m.face((r0[0], r1[0], r1[2], r0[2]), 10); m.face((r0[2], r1[2], r1[1], r0[1]), 10); m.face((r0[1], r1[1], r1[0], r0[0]), 10)
    m.face((vs[0][0], vs[0][2], vs[0][1]), 10); m.face((vs[-1][1], vs[-1][2], vs[-1][0]), 10)
    mo = m.link(guest_mat)
    only(mo); export([mo], f"lwf_hair_{sex}_mohawk_v1", dict(note="guests: punk fans only; punk bands: same mesh, any palette"))

    # ---- beard (male): jaw, chin and moustache with a mouth notch, one layer with a skirt to the skin
    if sex == "male":
        mt = MANIFEST["variants"]["male"][0]["mouth_target"]
        mouth = V3((mt[0], -mt[2], mt[1]))
        m = Mesh("LWF_Beard", 96)
        lower = table([(0, -0.190), (40, -0.175), (80, -0.120), (100, -0.060)])
        upper = table([(0, -0.072), (25, -0.064), (50, -0.040), (75, 0.0), (100, 0.012)])
        COLS, ROWS = 12, 5
        mz = mouth.z - H.eyeZ
        grid, inner = [], []
        for j in range(COLS + 1):
            az = -100 + 200 * j / COLS
            lo_, up_ = lower(az), upper(az)
            # row heights: two row edges sit just below and above the mouth, so the notch is one clean row band
            mb, mtop = mz - 0.010, mz + 0.008
            if up_ <= mtop + 0.004: zs = [lo_ + (up_ - lo_) * i / (ROWS - 1) for i in range(ROWS)]
            else: zs = [lo_, (lo_ + mb) / 2, mb, mtop, up_]
            col, ci = [], []
            for zz in zs:
                # horizontal ray at a fixed height: always the outermost surface (radial rays slip under the chin)
                d = V3((math.sin(math.radians(az)), math.cos(math.radians(az)), 0))
                o0 = V3((H.C.x, H.C.y, H.eyeZ + zz))
                S, n, _, _ = H.head_tree.ray_cast(o0 + d * 0.4, -d)
                if S is None: S, n, _, _ = H.head_tree.find_nearest(o0 + d * 0.08)
                o = (n + d).normalized()
                t = 0.008 + 0.006 * smooth((-0.10 - (S.z - H.eyeZ)) / 0.06)
                dm = math.hypot((S.x - mouth.x) / 0.030, (S.z - mouth.z) / 0.016)
                t *= 0.45 + 0.55 * smooth(dm - 0.7)            # thinner round the mouth
                col.append(S + o * t); ci.append(S + o * 0.0015)
            grid.append(col); inner.append(ci)
        gv = [[m.v(p) for p in col] for col in grid]; iv = [[m.v(p) for p in col] for col in inner]
        notch = [j for j in range(COLS) if abs(-100 + 200 * (j + 0.5) / COLS) < 18]
        for j in range(COLS):
            for i in range(ROWS - 1):
                if j in notch and i == 2: continue                                                   # the mouth
                m.face((gv[j][i], gv[j + 1][i], gv[j + 1][i + 1], gv[j][i + 1]), 10)
            m.face((gv[j + 1][0], gv[j][0], iv[j][0], iv[j + 1][0]), 10)
            m.face((gv[j][-1], gv[j + 1][-1], iv[j + 1][-1], iv[j][-1]), 10)
            if j in notch:                                                                           # notch rims
                m.face((gv[j][2], gv[j + 1][2], iv[j + 1][2], iv[j][2]), 10)
                m.face((gv[j + 1][3], gv[j][3], iv[j][3], iv[j + 1][3]), 10)
        for j in (0, COLS):
            for i in range(ROWS - 1): m.face((gv[j][i], gv[j][i + 1], iv[j][i + 1], iv[j][i]), 10)
        for j0, j1 in ((notch[0], notch[0]), (notch[-1] + 1, notch[-1] + 1)):
            m.face((gv[j0][2], gv[j0][3], iv[j0][3], iv[j0][2]) if j0 == notch[0] else (gv[j0][3], gv[j0][2], iv[j0][2], iv[j0][3]), 10)
        beard = m.link(guest_mat)
        only(beard); export([beard], "lwf_beard_male_v1", dict(note="combines with every male style; mouth notch for the drinking and eating poses",
                                                               mouth=[round(x, 4) for x in mouth]))
    REPORT.setdefault("_heads", {})[sex] = dict(eyeZ=round(H.eyeZ, 4), top=round(H.top, 4), band_front=round(band(0), 4), band_back=round(band(180), 4))


for sex in ("male", "female"):
    build_sex(sex)
json.dump(REPORT, open(os.path.join(OUT, "crowd_pieces_report.json"), "w"), indent=1)
print("DONE")

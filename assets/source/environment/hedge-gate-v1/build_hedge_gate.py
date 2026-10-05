# West hedge crew gate, production: wrought-iron garden gate between stone piers (leaf on its own hinge-pivoted node),
# the flagstone path to the farmhouse door (one baked mesh), a flower pot, the short lane beyond the hedge, and
# hedge_gate_layout_v1.json (world placements, Godot metres and yaw degrees about +Y).
#   blender -b --python build_hedge_gate.py -- <out_dir>
# Authoring: Blender (x, y, z) = Godot (x, -z, y); glTF export_yup.
import bpy, bmesh, math, sys, os, json, hashlib, random
from mathutils import Vector as V, Matrix, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = argv[0] if argv else os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
REPORT = {}


def G(x, y, z):
    """Godot point -> Blender point"""
    return V((x, -z, y))


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def export(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(OUT, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True, export_animations=False)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, name + ".blend"))
    ws = [o.matrix_world @ v.co for o in objs if o.type == 'MESH' for v in o.data.vertices]
    REPORT[name] = dict(file=name + ".glb", nodes=[o.name for o in objs],
                        triangles=sum(len(p.vertices) - 2 for o in objs if o.type == 'MESH' for p in o.data.polygons),
                        bounds_godot=[[round(min(w.x for w in ws), 3), round(min(w.z for w in ws), 3), round(min(-w.y for w in ws), 3)],
                                      [round(max(w.x for w in ws), 3), round(max(w.z for w in ws), 3), round(max(-w.y for w in ws), 3)]],
                        sha256=hashlib.sha256(open(path, "rb").read()).hexdigest())
    print("EXPORTED", name, REPORT[name]["triangles"])


# ---------------------------------------------------------------- shared palette (128 x 8, 16 swatches, Closest)
PAL = ["2b2e31", "3d4246", "b09a75", "9f8c6b", "beab87", "d2c5a4", "b4ac95", "c9c0a6",
       "b5623c", "5d7a44", "e58fa5", "e8c547", "c2ad86", "b7a17b", "a6a17b", "a8936c"]
IRON, IRON_HI, STONE, STONE2, STONE3, CAP, FLAG, FLAG2, POT, PLANT, FLOWER, FLOWER2, GRAVEL, LANE, VERGE, RUT = range(16)
def U(slot): return ((8 * slot + 4) / 128.0, 0.5)
def palette_mat():
    m = bpy.data.materials.new("LWF_HedgeGate_MattePalette"); m.use_nodes = True
    img = bpy.data.images.new("lwf_hedge_gate_palette", 128, 8); px = []
    for y in range(8):
        for hx in PAL: px += ([int(hx[i:i + 2], 16) / 255 for i in (0, 2, 4)] + [1.0]) * 8
    img.pixels = px; img.pack()
    tx = m.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = img; tx.interpolation = 'Closest'
    b = m.node_tree.nodes["Principled BSDF"]; b.inputs["Roughness"].default_value = 0.85; b.inputs["Specular IOR Level"].default_value = 0.3
    m.node_tree.links.new(tx.outputs[0], b.inputs["Base Color"]); return m


class Kit:
    """one bmesh per mesh; every face takes a fixed UV (a palette swatch centre). Coordinates are GODOT (x, y, z)."""
    def __init__(self, name): self.name = name; self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new("UVMap")
    def _paint(self, verts, uv):
        for f in {f for v in verts for f in v.link_faces}:
            for l in f.loops: l[self.uv].uv = uv
    def box(self, c, s, slot, yaw=0.0, tilt=None):
        """c centre, s (sx, sy, sz) Godot sizes; yaw degrees about +Y; tilt (axis, deg) applied first in the box frame"""
        R = Matrix.Rotation(math.radians(yaw), 4, 'Z')
        if tilt: R = R @ Matrix.Rotation(math.radians(tilt[1]), 4, {'x': 'X', 'z': 'Y'}[tilt[0]])
        M = Matrix.Translation(G(*c)) @ R @ Matrix.Diagonal((s[0], s[2], s[1], 1))
        self._paint(bmesh.ops.create_cube(self.bm, size=1.0, matrix=M)["verts"], U(slot))
    def bar(self, a, b, r, slot, seg=4):
        """square-ish iron bar between Godot points a and b (seg 4 = square section)"""
        a, b = G(*a), G(*b); d = b - a
        M = Matrix.Translation((a + b) / 2) @ d.to_track_quat('Z', 'Y').to_matrix().to_4x4() @ Matrix.Rotation(math.pi / 4, 4, 'Z')
        self._paint(bmesh.ops.create_cone(self.bm, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=d.length + r, matrix=M)["verts"], U(slot))
    def cone(self, c, r, h, slot, seg=4):
        M = Matrix.Translation(G(*c) + V((0, 0, h / 2))) @ Matrix.Rotation(math.pi / 4, 4, 'Z')
        self._paint(bmesh.ops.create_cone(self.bm, cap_ends=True, segments=seg, radius1=r, radius2=0.0, depth=h, matrix=M)["verts"], U(slot))
    def cyl(self, c, r, h, slot, seg=8, r2=None):
        M = Matrix.Translation(G(*c) + V((0, 0, h / 2)))
        self._paint(bmesh.ops.create_cone(self.bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=h, matrix=M)["verts"], U(slot))
    def ball(self, c, r, slot, sub=1):
        M = Matrix.Translation(G(*c)) @ Matrix.Diagonal((r, r, r, 1))
        self._paint(bmesh.ops.create_icosphere(self.bm, subdivisions=sub, radius=1.0, matrix=M)["verts"], U(slot))
    def flat(self, poly, y, slot):
        """flat polygon at height y from Godot (x, z) points (counter-clockwise seen from above is fine either way)"""
        vs = [self.bm.verts.new(G(x, y, z)) for x, z in poly]; f = self.bm.faces.new(vs)
        if f.normal.z < 0: f.normal_flip()
        for l in f.loops: l[self.uv].uv = U(slot)
    def link(self, mat, at=None, rot_z=0.0, recalc=True):
        """at: Godot origin of the object (geometry was authored relative to it); recalc=False keeps flat strips facing up"""
        if recalc: bmesh.ops.recalc_face_normals(self.bm, faces=list(self.bm.faces))
        me = bpy.data.meshes.new(self.name); self.bm.to_mesh(me); self.bm.free(); me.materials.append(mat)
        for p in me.polygons: p.use_smooth = False
        o = bpy.data.objects.new(self.name, me); bpy.context.scene.collection.objects.link(o)
        if at is not None: o.location = G(*at)
        o.rotation_euler = (0, 0, math.radians(rot_z)); return o


# ================================================================ 1. the gate (origin = opening centre, world (-32, 0, -2))
# Gate-local frame = world axes: the gate line runs along z, the field is +x, the lane is -x.
# Opening between the hedge ends: z -1.0 .. +1.0 local. Piers fill the ends; the clear way is 1.16 m.
PW, PH = 0.44, 1.55                                  # pier section and body height
PZ = 0.80                                            # pier centres at local z -0.80 (north, house side) and +0.80 (south)
INNER = PZ - PW / 2                                  # pier inner faces at +-0.58
HINGE_Z = -INNER + 0.03                              # hinge axis 3 cm off the north pier's face
LEAF_L = 2 * INNER - 0.03 - 0.03                     # to 3 cm short of the south pier
reset(); pm = palette_mat(); rng = random.Random(5)
k = Kit("LWF_GardenGate_Piers")
for zc in (-PZ, PZ):
    y = 0.0; i = 0
    while y < PH - 0.01:                             # coursed stone, alternating tones and a hair of set-back
        h = min(0.22 if i % 2 == 0 else 0.2, PH - y); w = PW - (0.0 if i % 2 == 0 else 0.016)
        k.box((0, y + h / 2, zc), (w, h - 0.012, w), [STONE, STONE2, STONE3][rng.randrange(3)])
        k.box((0, y + h - 0.006, zc), (w - 0.03, 0.012, w - 0.03), CAP)            # mortar line
        y += h; i += 1
    k.box((0, PH + 0.045, zc), (PW + 0.08, 0.09, PW + 0.08), CAP)                    # cap stone
    k.box((0, PH + 0.11, zc), (PW - 0.06, 0.04, PW - 0.06), CAP)
    k.cyl((0, PH + 0.13, zc), 0.07, 0.07, CAP, seg=8)                                # finial neck and ball
    k.ball((0, PH + 0.30, zc), 0.12, CAP, sub=2)
# overthrow: two concentric iron arcs springing from the piers' inner faces, tied by short bars, a ring on top
def arc(z0, z1, y0, rise, n=12):
    pts = []
    for j in range(n + 1):
        t = j / n; z = z0 + (z1 - z0) * t
        pts.append((0, y0 + rise * math.sin(math.pi * t) ** 0.85, z))
    return pts
O1 = arc(-INNER, INNER, 1.45, 0.78); O2 = arc(-INNER, INNER, 1.45, 0.60)
for a, b in zip(O1, O1[1:]): k.bar(a, b, 0.045, IRON)            # 90 mm: stays whole at zoom 62
for a, b in zip(O2, O2[1:]): k.bar(a, b, 0.035, IRON)
for j in (3, 6, 9): k.bar(O2[j], O1[j], 0.03, IRON)
top = O1[6][1]
ring = [(0, top + 0.16 + 0.13 * math.cos(2 * math.pi * j / 10), 0.13 * math.sin(2 * math.pi * j / 10)) for j in range(11)]
for a, b in zip(ring, ring[1:]): k.bar(a, b, 0.032, IRON)
k.cone((0, top + 0.29, 0), 0.04, 0.12, IRON)
for zc in (-INNER, INNER): k.box((0, 1.47, zc - math.copysign(0.02, zc)), (0.09, 0.12, 0.05), IRON)   # arch shoes into the piers
for zc in (-INNER, INNER):                                                                   # hinge pins / latch keep
    for y in (0.3, 1.0): k.box((0, y, zc - math.copysign(0.02, zc)), (0.05, 0.05, 0.05), IRON_HI)
piers = k.link(pm)

# leaf: authored along its own +x from the hinge axis (x 0) to the latch (x LEAF_L), bottom 0.10 m clear of the path
k = Kit("LWF_GardenGate_Leaf")
L = LEAF_L; YB, YT = 0.12, 1.20
for x in (0.025, L - 0.025): k.box((x, (YB + YT) / 2 + 0.02, 0), (0.065, YT - YB + 0.04, 0.045), IRON)   # stiles
for y in (YB, 0.42, YT): k.box((L / 2, y, 0), (L, 0.06, 0.04), IRON)                            # rails
k.box((L / 2, (YB + 0.42) / 2, 0), (L - 0.04, 0.42 - YB, 0.008), IRON_HI)                        # solid kick panel
NB = 3
for j in range(1, NB + 1):                                                                       # uprights, 32 mm, spear tops
    x = L * j / (NB + 1)
    k.box((x, (0.42 + YT) / 2 + 0.06, 0), (0.042, YT - 0.42 + 0.12, 0.042), IRON)
    k.cone((x, YT + 0.12, 0), 0.05, 0.13, IRON)
for j in range(NB + 1):                                                                          # dog-bar loops between uprights
    x0, x1 = L * j / (NB + 1), L * (j + 1) / (NB + 1); xm = (x0 + x1) / 2
    k.bar((x0 + 0.02, 0.86, 0), (xm, 0.70, 0), 0.022, IRON); k.bar((xm, 0.70, 0), (x1 - 0.02, 0.86, 0), 0.022, IRON)
k.box((L - 0.07, 0.95, 0.03), (0.07, 0.1, 0.02), IRON_HI)                                        # latch
SHUT_YAW, OPEN_YAW = -90.0, 0.0
leaf = k.link(pm, at=(0, 0, HINGE_Z), rot_z=SHUT_YAW)
export([piers, leaf], "lwf_garden_gate_iron_v1")
REPORT["lwf_garden_gate_iron_v1"].update(
    origin="opening centre on the ground; gate line along local z, field side +x",
    leaf_node="LWF_GardenGate_Leaf", leaf_pivot_local=[0.0, 0.0, round(HINGE_Z, 3)], leaf_length_m=round(LEAF_L, 3),
    leaf_shut_yaw_deg=SHUT_YAW, leaf_open_yaw_deg=OPEN_YAW, clear_opening_m=round(2 * INNER, 3),
    note="leaf geometry runs along its own +x from the hinge; rotation.y -90 = shut (runs to +z), 0 = open (+x, into the field)")

# ================================================================ 2. flagstone path (one mesh; origin = opening centre)
GATE = (-32.0, -2.0)
PATH_W = [(-31.95, -2.0), (-26.2, -2.0), (-21.35, -6.3), (-21.35, -8.55)]
PATH = [(x - GATE[0], z - GATE[1]) for x, z in PATH_W]
reset(); pm = palette_mat(); rng = random.Random(11)
k = Kit("LWF_GardenPath_Flagstones")
def run(a, b, end, t0):
    A, B = V(a), V(b); d = B - A; L = d.length; dv = d.normalized(); nv = V((-dv.y, dv.x))
    yaw = math.degrees(math.atan2(-dv.y, dv.x)); t = t0
    while t < L - (0 if end else 0.25):
        l = min(rng.uniform(0.36, 0.52), L - t + 0.1)
        for side in (-0.24, 0.24):
            c = A + dv * (t + l / 2) + nv * (side * 0.95 + rng.uniform(-0.03, 0.03))
            k.box((c.x, 0.035 + rng.uniform(-0.004, 0.004), c.y), (l - 0.025, 0.07, 0.44), rng.choice((FLAG, FLAG2)), yaw=yaw + rng.uniform(-3, 3))
        t += l
for i in range(len(PATH) - 1): run(PATH[i], PATH[i + 1], i == len(PATH) - 2, 0.35 if i else 0.0)
for i in range(1, len(PATH) - 1):
    d0 = (V(PATH[i]) - V(PATH[i - 1])).normalized(); d1 = (V(PATH[i + 1]) - V(PATH[i])).normalized(); h = (d0 + d1).normalized()
    k.box((PATH[i][0], 0.036, PATH[i][1]), (0.92, 0.072, 0.92), FLAG2, yaw=math.degrees(math.atan2(-h.y, h.x)))
k.box((-0.05, 0.02, 0.0), (0.5, 0.04, 1.16), CAP)                                                 # threshold stone between the piers
path = k.link(pm)
export([path], "lwf_garden_path_flagstones_v1")
REPORT["lwf_garden_path_flagstones_v1"].update(origin="the gate opening centre, world (-32, 0, -2), yaw 0",
                                              route_world=PATH_W, width_m=0.95, top_m=0.074)

# ================================================================ 3. flower pot
reset(); pm = palette_mat(); k = Kit("LWF_GardenPot")
k.cyl((0, 0, 0), 0.15, 0.30, POT, seg=8, r2=0.18); k.cyl((0, 0.30, 0), 0.19, 0.04, POT, seg=8)
k.ball((0, 0.42, 0), 0.2, PLANT, sub=1)
for j in range(6): k.ball((0.13 * math.cos(j * 1.05), 0.53, 0.13 * math.sin(j * 1.05)), 0.045, FLOWER if j % 2 else FLOWER2, sub=1)
pot = k.link(pm)
export([pot], "lwf_garden_pot_flowers_v1")
REPORT["lwf_garden_pot_flowers_v1"].update(origin="ground centre", height_m=0.6)

# ================================================================ 4. lane beyond the hedge (origin = opening centre)
# Sits just above the FarmSurround ground (y -0.025) and below the field (y 0); matches the surround lane colours.
reset(); pm = palette_mat(); k = Kit("LWF_HedgeGateLane")
Z0, Z1, TAPER = -12.0, 10.0, 4.0                    # local z extent and the length over which each end fades out
def strip(xc, w, y, slot, n=24):
    left, right = [], []
    for j in range(n + 1):
        z = Z0 + (Z1 - Z0) * j / n
        f = min(1.0, (z - Z0) / TAPER, (Z1 - z) / TAPER); f = max(0.05, f) ** 0.7
        left.append((xc - w / 2 * f, z)); right.append((xc + w / 2 * f, z))
    for j in range(n):
        k.flat([left[j], right[j], right[j + 1], left[j + 1]], y, slot)
strip(-3.05, 6.1, -0.018, VERGE)                    # verge (world x -38.1 .. -32.0), up to the field edge
strip(-3.6, 3.0, -0.012, LANE)                       # lane (world x -37.1 .. -34.1)
for xc in (-4.3, -2.9): strip(xc, 0.4, -0.009, RUT)  # wheel ruts
k.flat([(-2.1, -0.75), (0.02, -0.65), (0.02, 0.65), (-2.1, 0.75)], -0.006, GRAVEL)                 # apron to the gate
k.flat([(-5.1, -1.6), (-2.1, -1.4), (-2.1, 1.4), (-5.1, 1.6)], -0.007, GRAVEL)                    # van pull-in
lane = k.link(pm, recalc=False)
export([lane], "lwf_hedge_gate_lane_v1")
REPORT["lwf_hedge_gate_lane_v1"].update(origin="the gate opening centre, world (-32, 0, -2), yaw 0",
                                       world_extent=dict(x=[-38.1, -31.95], z=[-14.0, 8.0]), heights="y -0.018 .. -0.006, under the field")

# ================================================================ 5. layout
LAYOUT = dict(
    note="Godot world metres; yaw degrees about +Y (turns local +x). Built by build_hedge_gate.py.",
    hedge_remove=[dict(file="lwf_hedge_straight_8m_c_v1.glb", x=-32.0, z=0.0, yaw=90,
                       why="Main.BuildHedgeBoundary west run i = 0 (Hedge(i + 6) = 'c'), covers z 0 .. -8")],
    hedge_add=[dict(file="lwf_hedge_gate_end_v1.glb", x=-32.0, z=0.0, yaw=90, covers_z=[0.0, -1.0]),
               dict(file="lwf_hedge_gate_end_v1.glb", x=-32.0, z=-4.0, yaw=270, covers_z=[-4.0, -3.0]),
               dict(file="lwf_hedge_straight_4m_a_v1.glb", x=-32.0, z=-4.0, yaw=90, covers_z=[-4.0, -8.0])],
    gate=dict(file="lwf_garden_gate_iron_v1.glb", x=-32.0, z=-2.0, yaw=0, leaf_node="LWF_GardenGate_Leaf",
              hinge_world=[-32.0, round(-2.0 + HINGE_Z, 3)], leaf_shut_yaw=SHUT_YAW, leaf_open_yaw=OPEN_YAW,
              clear_opening_m=round(2 * INNER, 3), leaf_length_m=round(LEAF_L, 3)),
    path=dict(file="lwf_garden_path_flagstones_v1.glb", x=-32.0, z=-2.0, yaw=0),
    pots=[dict(file="lwf_garden_pot_flowers_v1.glb", x=-22.45, z=-8.85, yaw=0),
          dict(file="lwf_garden_pot_flowers_v1.glb", x=-20.25, z=-8.85, yaw=40),
          dict(file="lwf_garden_pot_flowers_v1.glb", x=-31.2, z=-0.75, yaw=15)],
    lane=dict(file="lwf_hedge_gate_lane_v1.glb", x=-32.0, z=-2.0, yaw=0),
    barrier_gap_closure=dict(file="lwf_hedge_gate_end_v1.glb", x=-31.45, z=7.75, yaw=0, covers_x=[-31.55, -30.45],
                             why="a short hedge spur from the west hedge towards the backstage west closure's end at x -30.2"))
json.dump(LAYOUT, open(os.path.join(OUT, "hedge_gate_layout_v1.json"), "w"), indent=1)
json.dump(REPORT, open(os.path.join(OUT, "hedge_gate_report.json"), "w"), indent=1)
print("DONE")

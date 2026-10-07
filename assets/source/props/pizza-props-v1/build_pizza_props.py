# Pizza props v1 production (approved: held B and litter 1 from Documents/Festival Tycoon concepts/pizza-props/).
#   blender -b --python build_pizza_props.py
# -> out/lwf_pizza_plate_v1.glb        held: a big slice drooping over the far edge of a paper plate, flat named materials
#    out/lwf_litter_pizza_plate_v1.glb  litter: a creased plate with grease, a tomato smear and a crust, on the litter palette
# Blender Z up -> Godot Y up.
#   Held origin: the presentation grip centre, as lwf_chips_tray_v1; the plate's underside is at z -0.025. The plate base
#     is a closed 9 mm slab (-0.025..-0.016) so the rig's palm-up fingers (up to about z -0.019 in LWF_RightHand_Food space)
#     stay inside it. The hand sits under the -Y half, so the slice's drooping tip hangs over the +Y edge, away from it.
#   Litter origin: base centre resting on z 0, as lwf_litter_chips_tray_v1.
import bpy, bmesh, math, os, json, hashlib
from mathutils import Vector
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
LITTER_PALETTE = "C:/Projects/festival-tycoon/assets/source/environment/litter-assets-v1/textures/lwf_litter_palette_v1.png"
REPORT = {}


def lin(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in c) + (1,)


def flat_mat(name, hexc, double=False):
    m = bpy.data.materials.new(name); m.use_nodes = True; m.diffuse_color = lin(hexc)
    b = m.node_tree.nodes["Principled BSDF"]; b.inputs["Base Color"].default_value = lin(hexc); b.inputs["Roughness"].default_value = 0.85
    m.use_backface_culling = not double; return m


class Kit:
    """faces carry either a material index (held) or a palette slot (litter)"""
    def __init__(self): self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new("UVMap")
    def face(self, pts, mi=0, slot=None):
        f = self.bm.faces.new([self.bm.verts.new(p) for p in pts]); f.material_index = mi
        if slot is not None:
            for l in f.loops: l[self.uv].uv = ((slot + 0.5) / 10, 0.5)
        return f
    def prism(self, outline, z0, z1, top, side, bottom=None, slot=None):
        n = len(outline)
        self.face([(x, y, z1) for x, y in outline], top, slot)
        if bottom is not None: self.face([(x, y, z0) for x, y in reversed(outline)], bottom, slot)
        for i in range(n):
            (ax, ay), (bx, by) = outline[i], outline[(i + 1) % n]
            self.face([(ax, ay, z0), (bx, by, z0), (bx, by, z1), (ax, ay, z1)], side, slot)
    def bar(self, pts, r, mi=0, slot=None):
        """a square-section bar along a polyline (crusts), capped"""
        secs = []
        for i, p in enumerate(pts):
            p = Vector(p); d = (Vector(pts[min(i + 1, len(pts) - 1)]) - Vector(pts[max(i - 1, 0)])).normalized()
            side = d.cross(Vector((0, 0, 1))).normalized(); up = side.cross(d).normalized()
            secs.append([p + side * r, p + up * r, p - side * r, p - up * r])
        for a, b in zip(secs, secs[1:]):
            for k in range(4): self.face([a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]], mi, slot)
        self.face(secs[0][::-1], mi, slot); self.face(secs[-1], mi, slot)
    def obj(self, name, mats):
        me = bpy.data.meshes.new(name); self.bm.to_mesh(me); self.bm.free()
        for m in mats: me.materials.append(m)
        for p in me.polygons: p.use_smooth = False
        o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); return o


def export(o, name, **meta):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    path = os.path.join(OUT, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True, export_apply=True, export_animations=False)
    me = o.data; me.calc_loop_triangles(); vs = [v.co for v in me.vertices]
    lo = [round(min(v[i] for v in vs), 4) for i in range(3)]; hi = [round(max(v[i] for v in vs), 4) for i in range(3)]
    REPORT[name] = dict(file=name + ".glb", triangles=len(me.loop_triangles), materials=[m.name for m in me.materials], bounds_blender=[lo, hi],
                        sha256=hashlib.sha256(open(path, "rb").read()).hexdigest(), **meta)
    print("EXPORTED", name, REPORT[name]["triangles"], lo, hi)


def ring_pts(r, n, a0=0.0): return [(r * math.cos(a0 + 2 * math.pi * i / n), r * math.sin(a0 + 2 * math.pi * i / n)) for i in range(n)]


# ================================================================ held: lwf_pizza_plate_v1
bpy.ops.wm.read_factory_settings(use_empty=True)
mats = [flat_mat("Paper plate", "f2eee3", True), flat_mat("Paper plate rim", "ddd6c5", True), flat_mat("Plate fluting", "e6e0d0", True),
        flat_mat("Cheese", "efc95e"), flat_mat("Cheese melt", "f4dc8e"), flat_mat("Tomato", "c8402e"), flat_mat("Pepperoni", "a42c22"),
        flat_mat("Crust", "cf9450"), flat_mat("Pizza base", "b98245")]
PLATE, RIM, FLUTE, CHEESE, MELT, TOMATO, PEPP, CRUST, BASE = range(9)
k = Kit(); N = 16
Z0, ZT, RB, R, LIP = -0.025, -0.016, 0.060, 0.075, 0.010
k.prism(ring_pts(RB, N), Z0, ZT, PLATE, RIM, bottom=RIM)                         # the closed base slab (foot)
inner, outer = ring_pts(RB, N), ring_pts(R, N)
for i in range(N):                                                               # sloped fluted rim, single surface, double-sided
    j = (i + 1) % N
    k.face([(*inner[i], ZT), (*inner[j], ZT), (*outer[j], ZT + LIP), (*outer[i], ZT + LIP)], FLUTE if i % 2 else RIM)

# the slice: apex over the +Y edge, crust towards -Y (resting over the palm, on the plate)
L, HA, TH = 0.165, math.radians(19), 0.008
AP = Vector((0.0, 0.098)); HEAD = math.radians(270)
ZS = ZT + 0.0012
ROWS = [0.0, 0.14, 0.30, 0.62, 1.0]; COLS = 3
def droop(t): return -0.024 * max(0.0, (0.30 - t) / 0.30) ** 1.5
def P(t, s, top):
    a = HEAD + HA * s; r = L * t
    return (AP.x + r * math.cos(a), AP.y + r * math.sin(a), ZS + droop(t) + (TH if top else 0.0))
for top in (True, False):
    for ri in range(len(ROWS) - 1):
        t0, t1 = ROWS[ri], ROWS[ri + 1]
        for c in range(COLS):
            s0, s1 = -1 + 2 * c / COLS, -1 + 2 * (c + 1) / COLS
            q = [P(t0, 0, top), P(t1, s0, top), P(t1, s1, top)] if ri == 0 else [P(t0, s0, top), P(t1, s0, top), P(t1, s1, top), P(t0, s1, top)]
            if not top: q = q[::-1]
            k.face(q, (CHEESE if (ri + c) % 3 else MELT) if top else BASE)
for s in (-1, 1):                                                                # cut sides show the tomato layer
    for ri in range(len(ROWS) - 1):
        q = [P(ROWS[ri], s, False), P(ROWS[ri + 1], s, False), P(ROWS[ri + 1], s, True), P(ROWS[ri], s, True)]
        k.face(q if s > 0 else q[::-1], TOMATO)
k.face([P(1, -1 + 2 * c / COLS, False) for c in range(COLS + 1)] + [P(1, 1 - 2 * c / COLS, True) for c in range(COLS + 1)], TOMATO)   # the crust-end wall
k.bar([(lambda p: (p[0], p[1], p[2] - TH * 0.1))(P(1.0, -1 + 2 * c / 4, True)) for c in range(5)], TH * 0.85, CRUST)
for t, s in ((0.40, -0.35), (0.55, 0.40), (0.78, -0.30), (0.82, 0.45)):          # pepperoni, hexagonal discs
    x, y, z = P(t, s, True)
    k.prism([(x + px, y + py) for px, py in ring_pts(0.0115, 6)], z - 0.0004, z + 0.0016, PEPP, PEPP)
held = k.obj("LWF_PizzaPlateV1", mats)
held["origin_contract"] = "presentation grip centre; plate underside at z -0.025 like lwf_chips_tray_v1; metres; Blender Z up / Godot Y up"
export(held, "lwf_pizza_plate_v1", origin="grip centre; plate underside at Blender z -0.025 (Godot y -0.025)", plate_diameter_m=2 * R,
       droop="slice tip over the +Y edge (Godot -Z), away from the palm under the -Y half", socket="LWF_RightHand_Food / tray anchor")

# ================================================================ litter: lwf_litter_pizza_plate_v1 (litter palette slots)
bpy.ops.wm.read_factory_settings(use_empty=True)
pm = bpy.data.materials.new("LWF_Litter_Shared_Opaque"); pm.use_nodes = True
bs = pm.node_tree.nodes["Principled BSDF"]; bs.inputs["Roughness"].default_value = 0.9
tx = pm.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = bpy.data.images.load(LITTER_PALETTE); tx.interpolation = 'Closest'
pm.node_tree.links.new(tx.outputs["Color"], bs.inputs["Base Color"]); pm.use_backface_culling = False
S_PAPER, S_RIM, S_GREASE, S_CRUST, S_SMEAR = 9, 3, 6, 5, 2
k = Kit(); N = 16; R, RB, LIP = 0.095, 0.074, 0.010
TAN = math.tan(math.radians(35)); CREASE = 0.015
def bend(x, y, z): return (x, y, z + max(0.0, x - CREASE) * TAN)
rings = [(0.0, 0.0), (RB * 0.5, 0.0), (RB, 0.0), (RB + (R - RB) * 0.55, LIP * 0.75), (R, LIP)]
grid = [[bend(r * math.cos(2 * math.pi * i / N), r * math.sin(2 * math.pi * i / N), z) for i in range(N)] for r, z in rings]
for ri in range(len(rings) - 1):
    for i in range(N):
        j = (i + 1) % N; slot = S_PAPER if ri < 2 else S_RIM
        if ri == 0: k.face([grid[0][i], grid[1][i], grid[1][j]], slot=slot)
        else: k.face([grid[ri][i], grid[ri + 1][i], grid[ri + 1][j], grid[ri][j]], slot=slot)
def stain(cx, cy, rx, ry, rot, slot, n=8):
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n; w = 1 + 0.18 * math.sin(3 * a + 1.3)
        x, y = rx * w * math.cos(a), ry * w * math.sin(a)
        x, y = x * math.cos(rot) - y * math.sin(rot), x * math.sin(rot) + y * math.cos(rot)
        p = bend(cx + x, cy + y, 0.0); pts.append((p[0], p[1], p[2] + 0.0015))
    k.face(pts, slot=slot)
stain(-0.022, 0.014, 0.036, 0.026, 0.4, S_GREASE); stain(0.042, -0.024, 0.019, 0.014, -0.3, S_GREASE)
stain(-0.036, -0.026, 0.012, 0.008, 0.9, S_SMEAR)
k.bar([(-0.060, 0.042, 0.008), (-0.042, 0.060, 0.008), (-0.018, 0.067, 0.008), (0.002, 0.062, 0.008)], 0.0075, slot=S_CRUST)
lit = k.obj("LWF_Litter_Pizza_Plate_V1", [pm])
export(lit, "lwf_litter_pizza_plate_v1", origin="base centre resting on z 0, as lwf_litter_chips_tray_v1", plate_diameter_m=2 * R,
       palette="lwf_litter_palette_v1: 9 paper, 3 rim, 6 grease, 5 crust, 2 tomato smear", held_grip_anchor_godot=[0, 0.025, 0])
json.dump(REPORT, open(os.path.join(OUT, "pizza_props_report.json"), "w"), indent=1)

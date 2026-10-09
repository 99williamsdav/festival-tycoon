# Curry props v1 production (approved: held A and litter A from Documents/Festival Tycoon concepts/curry-van/).
#   blender -b --python build_curry_props.py
# -> out/lwf_curry_tray_v1.glb         held: a black takeaway tray, rice one side, curry with chunks the other, a naan on the rice
#    out/lwf_litter_curry_tray_v1.glb  litter: the empty tray with a curry smear, rice grains and a naan crust, on the litter palette
# Blender Z up -> Godot Y up.
#   Held origin: the presentation grip centre, as lwf_chips_tray_v1 and lwf_pizza_plate_v1; the tray's underside is at z -0.025.
#     The base is a closed 9 mm slab (-0.025..-0.016) so the rig's palm-up fingers (up to about z -0.019 in
#     LWF_RightHand_Food space) stay inside it, as the pizza plate.
#   Litter origin: base centre resting on z 0, as lwf_litter_chips_tray_v1.
import bpy, bmesh, math, os, json, hashlib, random
from mathutils import Vector, Matrix
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
    """faces carry either a material index (held) or a litter palette slot (litter, one shared material)"""
    def __init__(self, slots=False): self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new("UVMap"); self.slots = slots
    def _mark(self, faces, k):
        for f in faces:
            if self.slots:
                f.material_index = 0
                for l in f.loops: l[self.uv].uv = ((k + 0.5) / 10, 0.5)
            else: f.material_index = k
    def face(self, pts, k):
        f = self.bm.faces.new([self.bm.verts.new(p) for p in pts]); self._mark([f], k); return f
    def prism(self, outline, z0, z1, k_top, k_side, k_bottom=None):
        self.face([(x, y, z1) for x, y in outline], k_top)
        if k_bottom is not None: self.face([(x, y, z0) for x, y in reversed(outline)], k_bottom)
        for i in range(len(outline)):
            (ax, ay), (bx, by) = outline[i], outline[(i + 1) % len(outline)]
            self.face([(ax, ay, z0), (bx, by, z0), (bx, by, z1), (ax, ay, z1)], k_side)
    def tray(self, w0, d0, w1, d1, z0, z1, k, base=None, flange=0.008):
        """tapered open tray: floor (or none, if a base slab is given), four walls, a flat flange round the top"""
        lo = [(-w0, -d0), (w0, -d0), (w0, d0), (-w0, d0)]; hi = [(-w1, -d1), (w1, -d1), (w1, d1), (-w1, d1)]
        out = [(-w1 - flange, -d1 - flange), (w1 + flange, -d1 - flange), (w1 + flange, d1 + flange), (-w1 - flange, d1 + flange)]
        if base is None: self.face([(x, y, z0) for x, y in lo], k)
        for i in range(4):
            j = (i + 1) % 4
            self.face([(*lo[i], z0), (*lo[j], z0), (*hi[j], z1), (*hi[i], z1)], k)
            self.face([(*hi[i], z1), (*hi[j], z1), (*out[j], z1), (*out[i], z1)], k)
    def blob(self, c, r, k, sq=(1, 1, 0.5), sub=1, seed=0, flat_bottom=None, inside=None):
        vs = bmesh.ops.create_icosphere(self.bm, subdivisions=sub, radius=1.0,
                                        matrix=Matrix.Translation(c) @ Matrix.Diagonal((r * sq[0], r * sq[1], r * sq[2], 1)))["verts"]
        rng = random.Random(seed)
        for v in vs:
            v.co += Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * r * 0.08
            if flat_bottom is not None: v.co.z = max(v.co.z, flat_bottom)
            if inside is not None:                                                # keep the filling inside the tapered walls
                w0, d0, w1, d1, z0, z1, m = inside; t = min(1.0, max(0.0, (v.co.z - z0) / (z1 - z0)))
                v.co.x = max(-(w0 + (w1 - w0) * t - m), min(w0 + (w1 - w0) * t - m, v.co.x))
                v.co.y = max(-(d0 + (d1 - d0) * t - m), min(d0 + (d1 - d0) * t - m, v.co.y))
        self._mark({f for v in vs for f in v.link_faces}, k)
    def naan(self, c, rx, ry, rz, k, k_char, seed=1, tilt=(0.0, 0.0), th=0.006):
        n = 12; R = Matrix.Rotation(rz, 3, 'Z') @ Matrix.Rotation(tilt[0], 3, 'X') @ Matrix.Rotation(tilt[1], 3, 'Y')
        pts = [Vector((rx * (1 + 0.12 * math.sin(3 * a + seed)) * math.cos(a), ry * (1 + 0.12 * math.sin(3 * a + seed)) * math.sin(a), 0))
               for a in (2 * math.pi * i / n for i in range(n))]
        top = [c + R @ (p + Vector((0, 0, th))) for p in pts]; bot = [c + R @ p for p in pts]
        self.face(top, k); self.face(bot[::-1], k)
        for i in range(n): j = (i + 1) % n; self.face([bot[i], bot[j], top[j], top[i]], k)
        rng = random.Random(seed)
        for s in range(5):                                                        # char spots, just proud of the top
            q = c + R @ Vector((rng.uniform(-0.6, 0.6) * rx, rng.uniform(-0.5, 0.5) * ry, th + 0.0005))
            self.blob(q, 0.008, k_char, sq=(1, 0.7, 0.15), sub=0, seed=s)
    def obj(self, name, mats):
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts[:], dist=1e-6)
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


# ================================================================ held: lwf_curry_tray_v1
bpy.ops.wm.read_factory_settings(use_empty=True)
mats = [flat_mat("Black tray", "2b2e31", True), flat_mat("Rice", "f2ecd9"), flat_mat("Curry sauce", "b5651d"), flat_mat("Curry chunks", "e08a3a"),
        flat_mat("Naan", "e7b864"), flat_mat("Naan char", "a0682a"), flat_mat("Coriander", "3d7f2a")]
TRAY, RICE, CURRY, CHUNK, NAAN, CHAR, HERB = range(7)
k = Kit()
Z0, ZT = -0.025, -0.016
k.prism([(-0.075, -0.05), (0.075, -0.05), (0.075, 0.05), (-0.075, 0.05)], Z0, ZT, TRAY, TRAY, TRAY)    # closed base slab
k.tray(0.075, 0.05, 0.088, 0.06, ZT, 0.012, TRAY, base=True)                                           # walls and flange: 18.8 x 13.6 cm
IN = (0.075, 0.05, 0.088, 0.06, ZT, 0.012, 0.003)                                                       # tray interior, 3 mm margin
k.blob(Vector((-0.035, 0.0, -0.004)), 0.05, RICE, sq=(0.85, 1.0, 0.38), sub=2, seed=2, flat_bottom=ZT + 0.001, inside=IN)
k.blob(Vector((0.038, 0.0, -0.006)), 0.05, CURRY, sq=(0.85, 1.0, 0.30), sub=2, seed=3, flat_bottom=ZT + 0.001, inside=IN)
for i, (x, y) in enumerate(((0.03, -0.02), (0.05, 0.015), (0.02, 0.025), (0.06, -0.015))):
    k.blob(Vector((x, y, 0.007)), 0.012, CHUNK, sq=(1, 1, 0.7), sub=1, seed=10 + i, inside=IN)
for x, y in ((0.025, 0.0), (0.045, -0.03)): k.blob(Vector((x, y, 0.012)), 0.006, HERB, sq=(1, 1, 0.3), sub=0)
k.naan(Vector((-0.035, 0.0, 0.018)), 0.050, 0.033, math.radians(70), NAAN, CHAR, seed=4, tilt=(0.0, math.radians(-6)))
held = k.obj("LWF_CurryTrayV1", mats)
held["origin_contract"] = "presentation grip centre; tray underside at z -0.025 like lwf_chips_tray_v1; metres; Blender Z up / Godot Y up"
export(held, "lwf_curry_tray_v1", origin="grip centre; tray underside at Blender z -0.025 (Godot y -0.025)", tray_size_m=[0.192, 0.136],
       socket="LWF_RightHand_Food / tray anchor")

# ================================================================ litter: lwf_litter_curry_tray_v1 (litter palette slots)
bpy.ops.wm.read_factory_settings(use_empty=True)
pm = bpy.data.materials.new("LWF_Litter_Shared_Opaque"); pm.use_nodes = True
bs = pm.node_tree.nodes["Principled BSDF"]; bs.inputs["Roughness"].default_value = 0.9
tx = pm.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = bpy.data.images.load(LITTER_PALETTE); tx.interpolation = 'Closest'
pm.node_tree.links.new(tx.outputs["Color"], bs.inputs["Base Color"]); pm.use_backface_culling = False
S_DARK, S_SMEAR, S_CREAM, S_CHAR, S_NAAN = 8, 2, 3, 6, 7
k = Kit(slots=True)
k.prism([(-0.075, -0.05), (0.075, -0.05), (0.075, 0.05), (-0.075, 0.05)], 0.0, 0.004, S_DARK, S_DARK, S_DARK)   # closed floor
k.tray(0.075, 0.05, 0.088, 0.06, 0.004, 0.037, S_DARK, base=True)
def stain(cx, cy, rx, ry, rot, slot, z, n=9):
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n; w = 1 + 0.2 * math.sin(3 * a + 1.1)
        x, y = rx * w * math.cos(a), ry * w * math.sin(a)
        pts.append((cx + x * math.cos(rot) - y * math.sin(rot), cy + x * math.sin(rot) + y * math.cos(rot), z))
    k.face(pts, slot)
stain(0.012, -0.006, 0.05, 0.03, 0.25, S_SMEAR, 0.0055)                                  # the curry smear across the floor
stain(0.045, 0.028, 0.014, 0.01, -0.4, S_SMEAR, 0.0055)
for i, (x, y) in enumerate(((-0.05, 0.025), (-0.04, 0.032), (-0.055, 0.012), (-0.02, -0.035))):
    k.blob(Vector((x, y, 0.0065)), 0.006, S_CREAM, sq=(1.3, 0.8, 0.4), sub=0, seed=30 + i)   # left-over rice grains
k.naan(Vector((0.028, 0.012, 0.0062)), 0.035, 0.022, math.radians(-30), S_NAAN, S_CHAR, seed=7, th=0.005)   # the naan crust
lit = k.obj("LWF_Litter_Curry_Tray_V1", [pm])
export(lit, "lwf_litter_curry_tray_v1", origin="base centre resting on z 0, as lwf_litter_chips_tray_v1", tray_size_m=[0.192, 0.136],
       palette="lwf_litter_palette_v1: 8 tray, 2 curry smear, 3 rice, 7 naan, 6 naan char", held_grip_anchor_godot=[0, 0.025, 0])
json.dump(REPORT, open(os.path.join(OUT, "curry_props_report.json"), "w"), indent=1)

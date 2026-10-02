# Band instruments v1: electric + flying-V guitarist kits, compact + double-kick drum hardware, a mic stand, and
# electronic "desk" kits. Built FROM the approved v2 kits so arms, strap, origins and animation conventions match exactly.
#   blender -b --python build_instruments.py -- <out_dir>
# Performer-local frame (as the v2 kits): origin at the performer's feet, Blender +Y = the performer's front
# (Godot -Z), +Z up. The acoustic kits are byte copies of the existing v2 guitarist kits (already acoustic): see README.
import bpy, bmesh, math, sys, os, json
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = argv[0] if argv else os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
CH = "C:/Projects/festival-tycoon/game/assets/characters/"
REPORT = {}

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene; sc.render.fps = 24
def imp(name):
    before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=CH + name + ".glb")
    return [o for o in bpy.data.objects if o not in before]
def export(name, frame_end=None, animated=True):
    sc = bpy.context.scene
    if frame_end: sc.frame_start, sc.frame_end = 1, frame_end
    objs = [o for o in sc.objects]
    bpy.ops.object.select_all(action='SELECT')
    path = os.path.join(OUT, name + ".glb")
    kw = dict(filepath=path, export_format='GLB', use_selection=True, export_yup=True, export_extras=True)
    if animated:
        kw.update(export_animations=True, export_animation_mode='SCENE', export_force_sampling=True, export_frame_range=True)
    else:
        kw.update(export_animations=False)
    bpy.ops.export_scene.gltf(**kw)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, name + ".blend"))
    tris = sum(len(p.vertices) - 2 for o in objs if o.type == 'MESH' for p in o.data.polygons)
    REPORT.setdefault(name, {}).update(tris=tris, nodes=sorted(o.name for o in objs))
    print(f"EXPORTED {path} tris={tris}")

def palette_slots(img, slots):
    """overwrite whole 8px swatches of a palette image: {slot: 'RRGGBB'} (sRGB bytes)"""
    W, H = img.size; px = list(img.pixels)
    for s, hx in slots.items():
        rgb = [int(hx[k:k + 2], 16) / 255 for k in (0, 2, 4)]
        for y in range(H):
            for x in range(8 * s, 8 * s + 8):
                j = (y * W + x) * 4; px[j:j + 3] = rgb
    img.pixels = px; img.pack()

class Builder:
    """bmesh with palette UVs; slot -> u = (8*slot+4)/W"""
    def __init__(self, name, nslots):
        self.name, self.ns = name, nslots; self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new("UVMap")
    def paint(self, faces, slot):
        for f in faces:
            for l in f.loops: l[self.uv].uv = ((8 * slot + 4) / (8.0 * self.ns), 0.5)
    def poly_prism(self, ring_front, ring_back, front_slot, back_slot, side_slot):
        fv = [self.bm.verts.new(p) for p in ring_front]; bv = [self.bm.verts.new(p) for p in ring_back]
        f = self.bm.faces.new(fv); b = self.bm.faces.new(list(reversed(bv)))
        sides = [self.bm.faces.new((fv[i], bv[i], bv[(i + 1) % len(fv)], fv[(i + 1) % len(fv)])) for i in range(len(fv))]
        tri = bmesh.ops.triangulate(self.bm, faces=[f, b])
        self.paint([x for x in tri["faces"] if x.normal.length >= 0], 0)
        # repaint: front faces are those whose centre is on the front plane
        return fv, bv, sides
    def box(self, M, size, slot):
        r = bmesh.ops.create_cube(self.bm, size=1.0, matrix=M @ Matrix.Diagonal((*size, 1)))
        self.paint({f for v in r["verts"] for f in v.link_faces}, slot)
    def cyl(self, M, r1, r2, depth, slot, seg=8):
        r = bmesh.ops.create_cone(self.bm, cap_ends=True, segments=seg, radius1=r1, radius2=r2, depth=depth, matrix=M)
        self.paint({f for v in r["verts"] for f in v.link_faces}, slot)
    def link(self, mat):
        bmesh.ops.recalc_face_normals(self.bm, faces=list(self.bm.faces))
        me = bpy.data.meshes.new(self.name); self.bm.to_mesh(me); self.bm.free(); me.materials.append(mat)
        for p in me.polygons: p.use_smooth = False
        o = bpy.data.objects.new(self.name, me); bpy.context.scene.collection.objects.link(o); return o

# ============================================================== electric and flying-V guitarist kits
# Same palette layout as the acoustic (lwf_guitarist_palette, 24 slots): body back/sides 12, body top 13,
# pickguard 14, neck 15, fret markers 16, strings/frets 20, hardware/pickups 21, headstock 11.
BODY_SIDE, BODY_TOP, GUARD, NECK, MARK, STRING, HW, HEAD = 12, 13, 14, 15, 16, 20, 21, 11
def guitar_frame(acoustic):
    """derive the playing geometry from the approved acoustic: string line, headstock and fretboard plane"""
    me = acoustic.data; uv = me.uv_layers[0].data; Mw = acoustic.matrix_world
    def verts(slot):
        return [Mw @ me.vertices[i].co for p in me.polygons if int(uv[p.loop_indices[0]].uv[0] * 24) == slot for i in p.vertices]
    head = verts(11); strings = verts(20); top = verts(13)
    hc = sum(head, Vector()) / len(head)
    lowest = max(strings, key=lambda v: (v - hc).length)                     # the bridge end of the strings
    a = Vector((hc.x - lowest.x, 0, hc.z - lowest.z)).normalized()          # along the neck, in the x-z plane
    p = Vector((a.z, 0, -a.x))                                               # across the strings, in plane
    if p.z < 0: p = -p
    y_board = max(v.y for v in strings)                                      # fretboard/strings front
    y_top = sum(v.y for v in top) / len(top)
    return dict(B=Vector((lowest.x, 0, lowest.z)), a=a, p=p, head=hc, y_board=y_board, y_top=y_top,
                L=(Vector((hc.x, 0, hc.z)) - Vector((lowest.x, 0, lowest.z))).length)

ELECTRIC = [(-0.20, 0.0), (-0.19, 0.10), (-0.12, 0.16), (0.0, 0.155), (0.08, 0.12), (0.14, 0.10), (0.20, 0.13), (0.245, 0.12),
            (0.22, 0.07), (0.17, 0.045), (0.17, -0.045), (0.21, -0.08), (0.20, -0.12), (0.14, -0.11), (0.06, -0.13),
            (-0.04, -0.16), (-0.13, -0.15), (-0.19, -0.09)]
ELECTRIC_GUARD = [(-0.13, 0.11), (0.02, 0.115), (0.12, 0.07), (0.12, -0.03), (0.03, -0.11), (-0.12, -0.12), (-0.165, -0.02)]
FLYING_V = [(0.15, 0.035), (-0.06, 0.035), (-0.33, 0.25), (-0.38, 0.19), (-0.13, 0.0), (-0.38, -0.19), (-0.33, -0.25),
            (-0.06, -0.035), (0.15, -0.035)]
FLYING_V_GUARD = [(0.09, 0.03), (-0.05, 0.03), (-0.13, 0.085), (-0.15, 0.06), (-0.08, 0.0), (-0.15, -0.06), (-0.13, -0.085),
                  (-0.05, -0.03), (0.09, -0.03)]

def build_guitar(sex, style):
    reset(); objs = imp(f"lwf_guitarist_{sex}_kit_v2")
    acoustic = next(o for o in objs if o.name.startswith("LWF_Guitarist_AcousticGuitar"))
    fr = guitar_frame(acoustic)
    orig_mat = acoustic.data.materials[0]; mat = orig_mat.copy()
    img_node = [n for n in mat.node_tree.nodes if n.type == 'TEX_IMAGE'][0]
    img = img_node.image.copy(); img.name = "lwf_guitarist_palette"; img_node.image = img
    if style == "electric":
        palette_slots(img, {BODY_SIDE: "B8442E", BODY_TOP: "C9553A", GUARD: "EDE6D6", HW: "2A2A2A"})   # red body, cream guard
        outline, guard = ELECTRIC, ELECTRIC_GUARD
    else:
        palette_slots(img, {BODY_SIDE: "141414", BODY_TOP: "1E1E1E", GUARD: "E8E6E0", HW: "2A2A2A"})   # black body, white guard
        outline, guard = FLYING_V, FLYING_V_GUARD
    old_mesh = acoustic.data; bpy.data.objects.remove(acoustic); bpy.data.meshes.remove(old_mesh)
    bpy.data.materials.remove(orig_mat); mat.name = "LWF_Guitarist_MattePalette"
    B, a, p, yb = fr["B"], fr["a"], fr["p"], fr["y_board"]
    def W(u, v, y): q = B + a * u + p * v; return Vector((q.x, y, q.z))
    g = Builder("LWF_Guitarist_" + ("ElectricGuitar" if style == "electric" else "FlyingVGuitar"), 24)
    y_front, y_back = yb - 0.03, yb - 0.075                                     # 45 mm solid body under the fretboard
    fv, bv, sides = g.poly_prism([W(u, v, y_front) for u, v in outline], [W(u, v, y_back) for u, v in outline], 0, 0, 0)
    for f in list(g.bm.faces):
        c = f.calc_center_median()
        g.paint([f], BODY_TOP if abs(c.y - y_front) < 1e-4 else BODY_SIDE)
    gf = [g.bm.verts.new(W(u, v, y_front + 0.002)) for u, v in guard]
    gface = g.bm.faces.new(gf); tri = bmesh.ops.triangulate(g.bm, faces=[gface]); g.paint(tri["faces"], GUARD)
    # neck, fretboard, strings, headstock, pickups, bridge and knobs
    L = fr["L"]; mid = lambda u0, u1: (u0 + u1) / 2
    rot = Matrix.Rotation(math.atan2(a.x, a.z), 4, 'Y')                         # local +Z of a box -> along the neck
    n0 = 0.12 if style == "electric" else 0.10
    g.box(Matrix.Translation(W(mid(n0, L - 0.07), 0, yb - 0.022)) @ rot, (0.052, 0.024, L - 0.07 - n0), NECK)
    for k in range(4):
        v = -0.018 + k * 0.012
        g.box(Matrix.Translation(W(mid(-0.03, L - 0.07), v, yb - 0.006)) @ rot, (0.003, 0.004, L - 0.04), STRING)
    for u in (0.35, 0.45, 0.55): g.box(Matrix.Translation(W(u, 0, yb - 0.008)) @ rot, (0.012, 0.004, 0.012), MARK)
    g.box(Matrix.Translation(W(L, 0.0, yb - 0.022)) @ rot, (0.075, 0.02, 0.15), HEAD)
    for u in ((0.03, 0.11) if style == "electric" else (0.03, 0.10)):
        g.box(Matrix.Translation(W(u, 0, y_front + 0.008)) @ rot, (0.075, 0.012, 0.022), HW)
    g.box(Matrix.Translation(W(-0.04, 0, y_front + 0.008)) @ rot, (0.08, 0.012, 0.03), HW)
    for (u, v) in (((-0.11, -0.10), (-0.07, -0.12)) if style == "electric" else ((-0.20, 0.11), (-0.24, 0.13))):
        g.cyl(Matrix.Translation(W(u, v, y_front + 0.012)) @ Matrix.Rotation(math.pi / 2, 4, 'X'), 0.012, 0.012, 0.02, HW, seg=6)
    g.link(mat)
    name = f"lwf_guitarist_{sex}_{'electric' if style == 'electric' else 'flyingv'}_kit_v2"
    export(name, frame_end=17)
    REPORT[name].update(body_slots={"sides_back": BODY_SIDE, "top": BODY_TOP, "pickguard": GUARD}, palette="lwf_guitarist_palette (192x8, 24 slots)")

for sex in ("male", "female"):
    build_guitar(sex, "electric"); build_guitar(sex, "flyingv")

# ============================================================== drum hardware variants (same origin/orientation as v2)
def classify_shell(c):
    if c.z > 1.02: return "tom_left" if c.x < 0 else "tom_right"
    if c.z > 0.82 and c.x < -0.1: return "snare"
    return "kick"
def classify_cymbal(c):
    if -0.555 < c.x < -0.28 and c.z < 0.85: return "snare_stand"
    if c.x < -0.3: return "crash"
    if c.x > 0.3: return "ride"
    return "other"
def drum_variant(name, mode):
    reset(); objs = imp("lwf_drum_hardware_only_v2")
    for o in objs:
        if o.type != 'MESH': continue
        bm = bmesh.new(); bm.from_mesh(o.data)
        cls = classify_shell if "Shells" in o.name else classify_cymbal
        if mode == "compact":
            dead = [f for f in bm.faces if cls(f.calc_center_median()) in ("tom_left", "ride")]
            bmesh.ops.delete(bm, geom=dead, context='FACES')
        elif mode == "double" and "Shells" in o.name:
            kick = [f for f in bm.faces if cls(f.calc_center_median()) == "kick"]
            dup = bmesh.ops.duplicate(bm, geom=kick)
            new_faces = [g for g in dup["geom"] if isinstance(g, bmesh.types.BMFace)]
            centre = Vector((0.0, 0.45, 0.50))
            for faces, dx in ((kick, -0.29), (new_faces, 0.29)):
                vs = {v for f in faces for v in f.verts}
                for v in vs:
                    v.co = centre + (v.co - centre) * 0.85 + Vector((dx, 0, -0.5 * 0.15 * 0 - 0.07))
        bm.to_mesh(o.data); bm.free()
    export(name, animated=False)
drum_variant("lwf_drum_hardware_compact_v1", "compact")
drum_variant("lwf_drum_hardware_double_kick_v1", "double")
REPORT["lwf_drum_hardware_compact_v1"].update(kept="kick, snare (+stand), right rack tom, crash (+stand); removed left tom and ride")
REPORT["lwf_drum_hardware_double_kick_v1"].update(kept="all v2 pieces; the kick is scaled 0.85 and doubled at x = -0.29 / +0.29")

# ============================================================== mic stand (performer-local; MicHead moves vertically)
reset()
mm = bpy.data.materials.new("LWF_MicStand_Matte"); mm.use_nodes = True
img = bpy.data.images.new("lwf_mic_stand_palette", 32, 8)
palette_slots(img, {0: "1E1E1E", 1: "8E9196", 2: "3A3A3A", 3: "C9A24A"})
tx = mm.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = img; tx.interpolation = 'Closest'
b = mm.node_tree.nodes["Principled BSDF"]; b.inputs["Roughness"].default_value = 0.6; mm.node_tree.links.new(tx.outputs[0], b.inputs["Base Color"])
STAND_Y = 0.50                                  # 0.5 m in front of the performer: clear of the guitar body, neck and both hands
pole = Builder("MicStand", 4)
pole.cyl(Matrix.Translation((0, STAND_Y, 0.015)), 0.17, 0.17, 0.03, 0, seg=10)           # weighted base
pole.cyl(Matrix.Translation((0, STAND_Y, 0.56)), 0.012, 0.012, 1.08, 0, seg=6)            # outer tube to 1.10
pole.cyl(Matrix.Translation((0, STAND_Y, 1.10)), 0.02, 0.02, 0.03, 2, seg=6)              # clutch
pole_o = pole.link(mm)
head = Builder("MicHead", 4)
# MicHead's origin sits at mouth height (node y); its geometry: inner tube down into the outer, a short boom back to the mouth
head.cyl(Matrix.Translation((0, STAND_Y, -0.32)), 0.008, 0.008, 0.26, 1, seg=6)          # inner tube -0.45..-0.19
bvec = Vector((0, 0.16 - STAND_Y, 0.19)); bl = bvec.length
head.cyl(Matrix.Translation(Vector((0, STAND_Y, -0.19)) + bvec / 2) @ bvec.to_track_quat('Z', 'Y').to_matrix().to_4x4(), 0.007, 0.007, bl, 0, seg=6)
head.cyl(Matrix.Translation((0, 0.19, 0.0)) @ Matrix.Rotation(math.radians(70), 4, 'X'), 0.016, 0.022, 0.09, 0, seg=8)   # mic body
head.cyl(Matrix.Translation((0, 0.155, 0.012)), 0.026, 0.026, 0.035, 1, seg=8)                                           # grille
head_o = head.link(mm)
MOUTH = {"male": 1.50, "female": 1.43}
head_o.location = (0, 0, MOUTH["male"])
export("lwf_mic_stand_v1", animated=False)
REPORT["lwf_mic_stand_v1"].update(frame="performer-local (same as the kits); place at the performer's position and facing",
                                  stand_y_in_front_m=STAND_Y, mic_capsule_local=[0, 0.155, "MicHead y"],
                                  MicHead_y={"male": MOUTH["male"], "female": MOUTH["female"]}, mic_to_face_gap_m=0.06)

# ============================================================== electronic desk kits (arms only + one looping animation)
def desk_kit(sex):
    reset(); objs = imp(f"lwf_drummer_{sex}_kit_v2")
    for o in list(objs):
        if "Stick" in o.name: bpy.data.objects.remove(o)
    for a in list(bpy.data.actions): bpy.data.actions.remove(a)
    arms = {}
    for o in bpy.context.scene.objects:
        if "LeftPlayingArm" in o.name: o.name = "LWF_Desk_LeftPlayingArm"; arms["L"] = o
        if "RightPlayingArm" in o.name: o.name = "LWF_Desk_RightPlayingArm"; arms["R"] = o
    N = 49                                       # 2 s loop at 24 fps
    for side, o in arms.items():
        o.rotation_mode = 'QUATERNION'; o.animation_data_clear()
        for fr in range(1, N + 1, 2):
            t = (fr - 1) / (N - 1) * 2 * math.pi
            if side == "L":                       # fader nudge: a small forward/back push, twice per loop
                q = Matrix.Rotation(math.radians(3.5) * math.sin(2 * t), 4, 'X').to_quaternion()
            else:                                 # knob twiddle: small side-to-side wrist work, with a pause
                q = (Matrix.Rotation(math.radians(5) * math.sin(3 * t) * max(0.0, math.sin(t)), 4, 'Y') @
                     Matrix.Rotation(math.radians(1.5) * math.sin(t), 4, 'X')).to_quaternion()
            o.rotation_quaternion = q; o.keyframe_insert("rotation_quaternion", frame=fr)
    export(f"lwf_electronic_{sex}_desk_kit_v2", frame_end=N)
    REPORT[f"lwf_electronic_{sex}_desk_kit_v2"].update(animation="one 49-frame loop at 24 fps: left fader nudge + right knob twiddle",
                                                     hands_reach_m="about 0.40-0.45 in front, 1.04-1.12 above the feet")
for sex in ("male", "female"):
    desk_kit(sex)

# ============================================================== keyboardist kits (arms + stand + keyboard + one keys loop)
def hand_tip(o):
    """the hand end of a playing arm: the vertices furthest forward (+Y), in world space"""
    ws = [o.matrix_world @ v.co for v in o.data.vertices]
    ymax = max(w.y for w in ws); tip = [w for w in ws if w.y > ymax - 0.06]
    return sum(tip, Vector()) / len(tip)
def keyboardist_kit(sex):
    reset(); objs = imp(f"lwf_drummer_{sex}_kit_v2")
    for o in list(objs):
        if "Stick" in o.name: bpy.data.objects.remove(o)
    for a in list(bpy.data.actions): bpy.data.actions.remove(a)
    arms = {}
    for o in bpy.context.scene.objects:
        if "LeftPlayingArm" in o.name: o.name = "LWF_Keyboardist_LeftPlayingArm"; arms["L"] = o
        if "RightPlayingArm" in o.name: o.name = "LWF_Keyboardist_RightPlayingArm"; arms["R"] = o
    hl, hr = hand_tip(arms["L"]), hand_tip(arms["R"])
    key_top = min(hl.z, hr.z) - 0.035                     # keys just under the fingertips
    ky = (hl.y + hr.y) / 2 - 0.02                          # hands over the front third of the keys
    km = bpy.data.materials.new("LWF_Keyboard_MattePalette"); km.use_nodes = True
    img = bpy.data.images.new("lwf_keyboard_palette", 64, 8)
    palette_slots(img, {0: "1E1E20", 1: "F2F0EA", 2: "141414", 3: "8E9196", 4: "B8442E", 5: "2A2A2E"})
    # 0 keyboard body (per-act colour), 1 white keys, 2 black keys, 3 stand metal, 4 accent strip, 5 end cheeks
    tx = km.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = img; tx.interpolation = 'Closest'
    bsdf = km.node_tree.nodes["Principled BSDF"]; bsdf.inputs["Roughness"].default_value = 0.7
    km.node_tree.links.new(tx.outputs[0], bsdf.inputs["Base Color"])
    k = Builder("LWF_Keyboardist_Keyboard", 8)
    W_, D_, T_ = 1.0, 0.30, 0.08
    cy = ky + 0.10; cz = key_top - T_ / 2
    k.box(Matrix.Translation((0, cy, cz)), (W_, D_, T_), 0)                                  # body
    k.box(Matrix.Translation((0, cy - 0.06, key_top + 0.004)), (W_ - 0.12, 0.13, 0.008), 1)   # white keys
    for i in range(14):                                                                     # black keys, 2-3 grouping
        if i % 7 in (2, 6): continue
        k.box(Matrix.Translation((-0.42 + i * 0.062, cy - 0.025, key_top + 0.012)), (0.022, 0.07, 0.012), 2)
    k.box(Matrix.Translation((0, cy + 0.09, key_top + 0.002)), (W_ - 0.12, 0.06, 0.004), 4)   # accent strip / screen
    for x in (-W_ / 2 + 0.03, W_ / 2 - 0.03):
        k.box(Matrix.Translation((x, cy, cz + 0.01)), (0.06, D_ + 0.01, T_ + 0.02), 5)
    leg = key_top - T_ - 0.01                                                                # X-stand under the keyboard
    for s in (-1, 1):
        for xs in (-0.32, 0.32):
            M = Matrix.Translation((xs, cy, leg / 2)) @ Matrix.Rotation(s * math.atan2(D_ + 0.2, leg), 4, 'X')
            k.box(M, (0.03, 0.03, math.hypot(leg, D_ + 0.2)), 3)
        k.box(Matrix.Translation((0, cy + s * (D_ / 2 + 0.1), 0.015)), (0.7, 0.03, 0.03), 3)
    k.box(Matrix.Translation((0, cy, leg)), (0.7, 0.03, 0.03), 3)
    k.link(km)
    N = 25                                           # 1 s loop at 24 fps: alternating light key presses
    for side, o in arms.items():
        o.rotation_mode = 'QUATERNION'; o.animation_data_clear(); ph = 0.0 if side == "L" else math.pi * 0.5
        for fr in range(1, N + 1):
            t = (fr - 1) / (N - 1) * 2 * math.pi
            press = max(0.0, math.sin(2 * t + ph)) ** 2
            q = (Matrix.Rotation(math.radians(2.5) * press, 4, 'X') @
                 Matrix.Rotation(math.radians(1.2) * math.sin(t + ph), 4, 'Z')).to_quaternion()
            o.rotation_quaternion = q; o.keyframe_insert("rotation_quaternion", frame=fr)
    name = f"lwf_keyboardist_{sex}_kit_v2"
    export(name, frame_end=N)
    REPORT[name].update(key_top_m=round(key_top, 3), keyboard_centre_y_m=round(cy, 3),
                        recolour="lwf_keyboard_palette (64x8, 8 slots): 0 body, 1 white keys, 2 black keys, 3 stand, 4 accent strip, 5 end cheeks")
for sex in ("male", "female"):
    keyboardist_kit(sex)

# ============================================================== electronic drum kit: a palette-only variant of v2
reset(); objs = imp("lwf_drum_hardware_only_v2")
done = set()
for o in objs:
    if o.type != 'MESH': continue
    for slot in o.material_slots:
        n = [x for x in slot.material.node_tree.nodes if x.type == 'TEX_IMAGE'][0]
        if n.image.name in done: continue
        palette_slots(n.image, {12: "2A2A2E", 13: "34343A", 14: "1E1E1E", 16: "3A3A3E", 22: "2E2E30", 21: "9A9DA2", 15: "9A9DA2"})
        done.add(n.image.name)
export("lwf_drum_hardware_electronic_v1", animated=False)
REPORT["lwf_drum_hardware_electronic_v1"].update(note="v2 geometry; palette recolour: charcoal shells, black rubber pads/heads, rubber cymbal pads, silver stands")

json.dump(REPORT, open(os.path.join(OUT, "instruments_report.json"), "w"), indent=1, default=str)

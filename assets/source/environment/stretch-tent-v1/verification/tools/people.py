# Rigged guests for the rain board: poses (hunched, hands over head, hurrying, huddle), a wet tint, and ponchos.
# exec()'d into a Blender script; needs bpy, math, Vector, Matrix in globals.
import bpy, math, json, random
from mathutils import Vector, Matrix
RIGDIR = "C:/Projects/festival-tycoon/assets/source/characters/rigged-test-v1/out/"
CONTRACT_P = json.load(open("C:/Projects/festival-tycoon/game/assets/characters/attendee_palette_v1_contract.json"))
WET_SLOTS = (3, 4, 7, 8, 10, 11)                       # shirt, trousers, hair
_palette_cache = {}


def palette_image(base_img, ci, hi, wet):
    key = (base_img.name, ci, hi, wet)
    if key in _palette_cache: return _palette_cache[key]
    im = base_img.copy(); px = list(im.pixels); W = im.size[0]
    slots = dict(CONTRACT_P["clothing_colourways"][ci]["slots"]); slots.update(CONTRACT_P["hair_colours"][hi]["slots"])
    for sl, hx in slots.items():
        rgb = [int(hx[k:k + 2], 16) / 255 for k in (0, 2, 4)]
        if wet and int(sl) in WET_SLOTS: rgb = [c * 0.58 for c in rgb]          # soaked: about 40% darker
        for y in range(im.size[1]):
            for x in range(8 * int(sl), 8 * int(sl) + 8):
                j = (y * W + x) * 4; px[j:j + 3] = rgb
    im.pixels = px; _palette_cache[key] = im; return im


def solve2(S, W, l1, l2, pole):
    """two-bone IK: elbow/knee position for shoulder S, target W, bending towards pole"""
    d = W - S; L = d.length; L = min(L, (l1 + l2) * 0.999); dn = d.normalized()
    a = (l1 * l1 + L * L - l2 * l2) / (2 * L); h = math.sqrt(max(0.0, l1 * l1 - a * a))
    p = (pole - dn * pole.dot(dn)).normalized()
    return S + dn * a + p * h, S + dn * L


class Guest:
    def __init__(self, sex="male", ci=0, hi=0, wet=False, name="g"):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=RIGDIR + f"lwf_attendee_{sex}_rigged_test_v1.glb")
        self.objs = [o for o in bpy.data.objects if o not in before]
        for o in list(self.objs):
            if o.name.startswith("Icosphere"): bpy.data.objects.remove(o); self.objs.remove(o)
        self.rig = next(o for o in self.objs if o.type == 'ARMATURE'); self.body = next(o for o in self.objs if o.type == 'MESH')
        self.rig.animation_data_clear() if self.rig.animation_data else None
        for pb in self.rig.pose.bones: pb.rotation_mode = 'QUATERNION'
        self.root = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(self.root)
        self.rig.parent = self.root
        for i, m in enumerate(self.body.data.materials):                       # colourway (+ wet)
            m2 = m.copy(); self.body.data.materials[i] = m2
            for nd in m2.node_tree.nodes:
                if nd.type == 'TEX_IMAGE' and nd.image: nd.image = palette_image(nd.image, ci, hi, wet); nd.interpolation = 'Closest'
            b = m2.node_tree.nodes.get("Principled BSDF")
            if b and wet: b.inputs["Roughness"].default_value = 0.42
        self.wet = wet; self.sex = sex
        self.L = {b.name: b.length for b in self.rig.data.bones}
        vs = [v.co for v in self.body.data.vertices]
        hg = self.body.vertex_groups.get("Head").index
        hv = [v.co for v in self.body.data.vertices if any(g.group == hg and g.weight > 0.5 for g in v.groups)]
        self.head_top = max(v.z for v in hv); self.head_c = sum(hv, Vector()) / len(hv)
        self.head_r = max((Vector((v.x, v.y, 0)) - Vector((self.head_c.x, self.head_c.y, 0))).length for v in hv)

    # ---- posing in armature space
    def upd(self): bpy.context.view_layer.update()
    def aim(self, bone, d):
        self.upd(); pb = self.rig.pose.bones[bone]; rest = self.rig.data.bones[bone].matrix_local
        q = (rest.to_3x3() @ Vector((0, 1, 0))).rotation_difference(Vector(d).normalized())
        pb.matrix = Matrix.Translation(pb.head) @ q.to_matrix().to_4x4() @ rest.to_3x3().to_4x4()
    def head_of(self, bone): self.upd(); return self.rig.pose.bones[bone].head.copy()
    def arm_to(self, side, wrist, pole, hand_dir):
        S = self.head_of(f"{side}UpperArm"); E, W = solve2(S, Vector(wrist), self.L[f"{side}UpperArm"], self.L[f"{side}LowerArm"], Vector(pole))
        self.aim(f"{side}UpperArm", E - S); self.aim(f"{side}LowerArm", W - E); self.aim(f"{side}Hand", hand_dir)
    def leg_to(self, side, ankle, pole=(0, 1, 0)):
        S = self.head_of(f"{side}UpperLeg"); K, A = solve2(S, Vector(ankle), self.L[f"{side}UpperLeg"], self.L[f"{side}LowerLeg"], Vector(pole))
        self.aim(f"{side}UpperLeg", K - S); self.aim(f"{side}LowerLeg", A - K); self.aim(f"{side}Foot", (0, 0.95, -0.31))
    def spine(self, lean, neck, head=None):
        self.aim("Spine", (0, lean * 0.5, 1)); self.aim("Chest", (0, lean, 1)); self.aim("Neck", (0, neck, 1)); self.aim("Head", (0, head if head is not None else neck, 1))

    def pose(self, kind, k=1.0):
        s = lambda side: -1 if side == "Left" else 1
        if kind == "idle": return
        if kind == "hunched":                                            # shoulders up, head down, arms wrapped round the chest
            self.spine(0.42, 0.65, 0.85); self.upd()
            for side in ("Left", "Right"):
                sh = self.head_of(f"{side}UpperArm")
                self.arm_to(side, sh + Vector((-s(side) * 0.21, 0.19, -0.09)), (s(side) * 0.7, -0.2, -0.6), (-s(side) * 1.0, 0.1, 0.25))
            for side in ("Left", "Right"): self.leg_to(side, Vector((s(side) * 0.09, 0.02, 0.10)))
        if kind == "hands_head":                                          # hands clasped on top of the head
            self.spine(0.12, 0.25); self.upd()
            top = self.head_top_posed()
            for side in ("Left", "Right"):
                self.arm_to(side, top + Vector((s(side) * 0.085, 0.0, 0.035)), (s(side) * 1.0, 0.2, 0.15), (-s(side) * 1.0, 0.1, -0.12))
        if kind == "hurry":                                               # hunched mid-stride, arms tucked
            self.aim("Hips", (0, 0.10, 1)); self.spine(0.38, 0.6, 0.8); self.upd()
            self.leg_to("Left", Vector((-0.085, 0.22, 0.12)), (0, 1, 0)); self.leg_to("Right", Vector((0.085, -0.18, 0.17)), (0, 1, 0))
            self.aim("RightFoot", (0, 0.6, -0.8))
            for side, f in (("Left", -1), ("Right", 1)):
                sh = self.head_of(f"{side}UpperArm")
                self.arm_to(side, sh + Vector((-s(side) * 0.05, 0.12 + 0.06 * f, -0.30)), (s(side) * 0.3, -1, 0), (-s(side) * 0.3, 1, 0.5))
        if kind == "walk":
            self.leg_to("Left", Vector((-0.085, 0.24, 0.12))); self.leg_to("Right", Vector((0.085, -0.22, 0.16))); self.aim("RightFoot", (0, 0.6, -0.8))
            self.aim("LeftUpperArm", (0, -0.25, -1)); self.aim("LeftLowerArm", (0, -0.1, -1)); self.aim("LeftHand", (0, -0.1, -1))
            self.aim("RightUpperArm", (0, 0.3, -1)); self.aim("RightLowerArm", (0, 0.45, -1)); self.aim("RightHand", (0, 0.45, -1))
        if kind == "huddle":                                              # arms folded, a little hunched, standing close
            self.spine(0.15, 0.3, 0.35); self.upd()
            for side in ("Left", "Right"):
                sh = self.head_of(f"{side}UpperArm")
                self.arm_to(side, sh + Vector((-s(side) * 0.19, 0.17, -0.17)), (s(side) * 0.6, -0.4, -0.6), (-s(side) * 1.0, 0.1, 0.2))
        self.upd()

    def head_top_posed(self):
        self.upd(); pb = self.rig.pose.bones["Head"]; rest = self.rig.data.bones["Head"].matrix_local
        m = pb.matrix @ rest.inverted()
        return m @ Vector((self.head_c.x, self.head_c.y, self.head_top))

    def place(self, loc, yaw_deg):
        self.root.location = loc; self.root.rotation_euler = (0, 0, math.radians(yaw_deg)); return self


# ---------------------------------------------------------------- poncho: a hooded drape hung from the body's own silhouette
def body_profile(body, nang=48, zs=None):
    """max radius of the rest body per (height band, direction) around the body axis"""
    zs = zs or [0.70 + 0.02 * i for i in range(40)]
    prof = [[0.0] * nang for _ in zs]
    for v in body.data.vertices:
        c = v.co
        if c.z < zs[0] - 0.02 or c.z > zs[-1] + 0.02: continue
        a = math.atan2(c.y, c.x); ai = int(round((a % (2 * math.pi)) / (2 * math.pi) * nang)) % nang
        zi = min(len(zs) - 1, max(0, int(round((c.z - zs[0]) / (zs[1] - zs[0])))))
        r = math.hypot(c.x, c.y)
        for da in (-1, 0, 1):
            prof[zi][(ai + da) % nang] = max(prof[zi][(ai + da) % nang], r * math.cos(da * 2 * math.pi / nang))
    return zs, prof


def make_poncho(guest, style, hem=0.80, hood=True, name="poncho"):
    zs = [hem + 0.02 * i for i in range(int((1.47 - hem) / 0.02) + 1)]
    zs, prof = body_profile(guest.body, 48, zs); nang = 48; nz = len(zs)
    top_i = nz - 1
    R = [[0.0] * nang for _ in range(nz)]
    for a in range(nang):                                                  # hang: cumulative max from the top down, plus flare
        run = 0.0
        for i in range(nz - 1, -1, -1):
            run = max(run, prof[i][a] + 0.035)
            R[i][a] = run
    zsh = 1.28
    for i in range(nz):
        for a in range(nang):
            if zs[i] < zsh: R[i][a] += (zsh - zs[i]) * 0.16
    for _ in range(3):                                                     # smooth round the body
        R = [[(R[i][(a - 1) % nang] + 2 * R[i][a] + R[i][(a + 1) % nang]) / 4 for a in range(nang)] for i in range(nz)]
    for a in range(nang): R[top_i][a] = max(0.085, min(R[top_i][a], 0.10))  # neck opening
    me = bpy.data.meshes.new(name); verts = []; faces = []
    for i in range(nz):
        for a in range(nang):
            th = 2 * math.pi * a / nang; r = R[i][a]
            wob = 0.012 * math.sin(th * 7 + i * 0.4) * max(0, (zsh - zs[i])) / (zsh - hem)   # soft folds towards the hem
            verts.append(((r + wob) * math.cos(th), (r + wob) * math.sin(th), zs[i]))
    for i in range(nz - 1):
        for a in range(nang):
            b = (a + 1) % nang; faces.append((i * nang + a, i * nang + b, (i + 1) * nang + b, (i + 1) * nang + a))
    hood_faces = []
    if hood:                                                               # hood up: a shell round the back, sides and top of the head
        hc = guest.head_c; hr = guest.head_r + 0.035; top = guest.head_top + 0.03
        base = len(verts); rings = 8; seg = 24
        for j in range(rings + 1):
            ph = math.pi / 2 * j / rings                                   # 0 at the neck line, 90 deg at the crown
            for kk in range(seg + 1):
                t = -math.pi * 0.85 + 1.7 * math.pi * kk / seg            # leaves the face (+Y) open
                th = t - math.pi / 2                                       # centred on the back (-Y)
                rr = hr * math.cos(ph) * (1.0 if j > 0 else 1.05)
                verts.append((hc.x + rr * math.cos(th), hc.y - 0.01 + rr * math.sin(th) * 1.08, 1.43 + (top - 1.43) * math.sin(ph)))
        for j in range(rings):
            for kk in range(seg):
                f = (base + j * (seg + 1) + kk, base + j * (seg + 1) + kk + 1, base + (j + 1) * (seg + 1) + kk + 1, base + (j + 1) * (seg + 1) + kk)
                faces.append(f); hood_faces.append(len(faces) - 1)
    me.from_pydata(verts, [], faces); me.update()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    uv = me.uv_layers.new()
    for p in me.polygons:                                                  # planar UVs from the back: x across, z up
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co; uv.data[li].uv = (0.5 + co.x / 1.1, (co.z - hem) / (1.70 - hem))
    mats = poncho_mats(style)
    for m in mats: me.materials.append(m)
    for p in me.polygons:
        c = p.center; p.material_index = 1 if (len(mats) > 1 and c.y < -0.02 and p.index not in hood_faces and abs(c.x) < 0.45) else 0
        p.use_smooth = True
    sol = o.modifiers.new("solid", 'SOLIDIFY'); sol.thickness = 0.006; sol.offset = 1
    # skin to the guest's rig in its rest pose
    for pb in guest.rig.pose.bones: pb.matrix_basis = Matrix()
    guest.upd()
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); guest.rig.select_set(True); bpy.context.view_layer.objects.active = guest.rig
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    return o


PONCHO_COLOURS = {"yellow": "f2c230", "pink": "e0457b", "lime": "9bc53d", "orange": "f08a24", "sky": "4fa3d9", "purple": "7a4fb0"}


def _lin(h): return tuple(((int(h[i:i + 2], 16) / 255 + 0.055) / 1.055) ** 2.4 if int(h[i:i + 2], 16) / 255 > 0.04045 else int(h[i:i + 2], 16) / 255 / 12.92 for i in (0, 2, 4)) + (1,)


def poncho_mats(style):
    kind, arg = (style.split(":", 1) + [""])[:2]
    m = bpy.data.materials.new("poncho_" + style); m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = 0.3
    if kind == "clear":                                                   # clear PVC: see-through, a faint blue cast
        b.inputs["Base Color"].default_value = _lin("dfeef5"); b.inputs["Alpha"].default_value = 0.32
        m.blend_method = 'BLEND'; b.inputs["Roughness"].default_value = 0.08
        return [m]
    if kind == "colour":
        b.inputs["Base Color"].default_value = _lin(PONCHO_COLOURS[arg]); return [m]
    # branded: festival teal with the back print
    b.inputs["Base Color"].default_value = _lin("1f5f5a")
    p = bpy.data.materials.new("poncho_print"); p.use_nodes = True; pb_ = p.node_tree.nodes["Principled BSDF"]; pb_.inputs["Roughness"].default_value = 0.3
    tx = p.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = bpy.data.images.load(arg); p.node_tree.links.new(tx.outputs[0], pb_.inputs["Base Color"])
    return [m, p]

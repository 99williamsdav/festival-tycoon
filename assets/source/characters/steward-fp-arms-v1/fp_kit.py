# First-person steward arm geometry kit (exec'd by build_fp_arms.py). Built right-handed for the radio, as on the concept board;
# the build mirrors it so the radio sits in the LEFT hand and the right hand does the actions.
# Camera space throughout (Blender world == Godot camera space: +X right, +Y up, -Z forward), for the prototype's 72 deg camera.
import bpy, bmesh, math, sys
from mathutils import Vector, Matrix, Quaternion
sc = bpy.context.scene


def lin(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in c) + (1,)
MATS = {}
def M(name, hexc, rough=0.8, emit=0.0, metal=0.0):
    if name in MATS: return MATS[name]
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = lin(hexc); b.inputs["Roughness"].default_value = rough; b.inputs["Metallic"].default_value = metal
    if emit: b.inputs["Emission Color"].default_value = lin(hexc); b.inputs["Emission Strength"].default_value = emit
    MATS[name] = m; return m
SKIN, TEE, HIVIS, SILVER, DARK, RADIO, SCREEN, GOLD = ("skin", "c88f65"), ("tee", "397677"), ("hivis", "d8f126"), ("silver", "bfc4b7"), ("dark", "30373b"), ("radio", "242b2d"), ("screen", "6acfd1"), ("gold", "ffd166")


def frame(z, x_hint=Vector((1, 0, 0))):
    z = z.normalized(); x = (x_hint - z * x_hint.dot(z)); x = x.normalized() if x.length > 1e-6 else z.orthogonal().normalized(); y = z.cross(x)
    return Matrix((x, y, z)).transposed()


def tube(p0, p1, r0, r1, mat, n=8, x_hint=Vector((1, 0, 0))):
    """a tapered n-sided tube from p0 to p1, capped"""
    p0, p1 = Vector(p0), Vector(p1); R = frame(p1 - p0, x_hint); bm = bmesh.new(); rings = []
    for p, r in ((p0, r0), (p1, r1)):
        rings.append([bm.verts.new(p + R @ Vector((r * math.cos(2 * math.pi * k / n + math.pi / n), r * math.sin(2 * math.pi * k / n + math.pi / n), 0))) for k in range(n)])
    for k in range(n): bm.faces.new((rings[0][k], rings[0][(k + 1) % n], rings[1][(k + 1) % n], rings[1][k]))
    bm.faces.new(rings[0][::-1]); bm.faces.new(rings[1])
    return obj(bm, mat)


def box(c, size, mat, R=Matrix.Identity(3), bevel=0.0):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts: v.co = Vector(c) + R @ Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    if bevel: bmesh.ops.bevel(bm, geom=bm.edges[:], offset=bevel, segments=1, affect='EDGES')
    return obj(bm, mat)


def obj(bm, mat):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new("p"); bm.to_mesh(me); bm.free(); me.materials.append(M(*mat))
    for p in me.polygons: p.use_smooth = False
    o = bpy.data.objects.new("p", me); sc.collection.objects.link(o); return o


def rot(axis, deg): return Matrix.Rotation(math.radians(deg), 3, axis)


def mitten(wrist, R, mat, open_hand=False, grip=0.0, scale=1.0):
    """a v6-style mitten in hand frame R (columns: x = thumb side, y = palm normal, z = towards the fingertips) at the wrist"""
    s = scale; w = Vector(wrist)
    def P(x, y, z): return w + R @ Vector((x * s, y * s, z * s))
    box(P(0, 0, 0.045), (0.078 * s, 0.030 * s, 0.075 * s), mat, R, 0.006)                         # palm
    if open_hand:
        box(P(-0.004, 0, 0.115), (0.074 * s, 0.022 * s, 0.07 * s), mat, R, 0.006)                   # straight fingers
    else:                                                                                       # fingers curled forward over the grip
        Rc = R @ rot('X', -70 - grip * 30)
        box(P(-0.004, 0.012, 0.095), (0.074 * s, 0.024 * s, 0.05 * s), mat, Rc, 0.006)
        box(P(-0.004, 0.034, 0.090), (0.072 * s, 0.022 * s, 0.035 * s), mat, R @ rot('X', -150), 0.006)
    Rt = R @ rot('Y', 35) @ rot('X', -20 if open_hand else -55)                                   # thumb
    box(P(0.042, 0.012, 0.045), (0.022 * s, 0.022 * s, 0.055 * s), mat, Rt, 0.005)


def sleeve(elbow, wrist, style, side):
    e, w = Vector(elbow), Vector(wrist); d = (w - e).normalized()
    if style == "A":                                                                            # bare forearm, hi-vis wristband
        tube(e, w, 0.050, 0.037, SKIN)
        band0 = w - d * 0.06; tube(band0, band0 + d * 0.034, 0.0415, 0.040, HIVIS, 10); tube(band0 + d * 0.012, band0 + d * 0.021, 0.0422, 0.0418, SILVER, 10)
    elif style == "B":                                                                          # hi-vis jacket sleeve, two reflective bands, dark cuff
        cuff = w - d * 0.012
        tube(e, cuff, 0.063, 0.049, HIVIS, 8)
        for t in (0.30, 0.55):
            p = e + (cuff - e) * t; rr = 0.063 + (0.049 - 0.063) * t
            tube(p, p + d * 0.030, rr + 0.002, rr + 0.0015, SILVER, 8)
        tube(cuff - d * 0.028, cuff + d * 0.005, 0.0505, 0.049, DARK, 8)
        tube(cuff, w + d * 0.01, 0.038, 0.036, SKIN, 8)
    else:                                                                                       # teal long sleeve, hi-vis armband, fingerless glove
        tube(e, w - d * 0.01, 0.056, 0.043, TEE, 8)
        p = e + (w - e) * 0.25; tube(p, p + d * 0.06, 0.0565, 0.055, HIVIS, 8); tube(p + d * 0.022, p + d * 0.036, 0.0572, 0.056, SILVER, 8)


HANDMAT = {"A": SKIN, "B": SKIN, "C": DARK}


def radio_in_right_hand(style):
    # radio tilted back toward the viewer and turned a little left, held upright in a low-poly fist
    Rr = rot('Y', -16) @ rot('X', 22) @ rot('Z', 10)
    c = Vector((0.185, -0.115, -0.40))
    wrist = c + Rr @ Vector((0, -0.085, -0.004)); elbow = Vector((0.43, -0.52, -0.16))
    sleeve(elbow, wrist, style, "R")
    box(c, (0.052, 0.112, 0.030), RADIO, Rr, 0.006)
    box(c + Rr @ Vector((0, 0.030, 0.0155)), (0.034, 0.024, 0.002), SCREEN, Rr)                   # little screen, facing the camera
    for k in range(3): box(c + Rr @ Vector((0, 0.002 - k * 0.009, 0.0155)), (0.030, 0.003, 0.002), DARK, Rr)   # speaker grille
    tube(c + Rr @ Vector((0.015, 0.056, 0)), c + Rr @ Vector((0.015, 0.11, 0)), 0.0055, 0.0045, RADIO, 6)     # antenna
    tube(c + Rr @ Vector((-0.014, 0.056, 0)), c + Rr @ Vector((-0.014, 0.066, 0)), 0.006, 0.006, GOLD, 8)      # channel knob
    box(c + Rr @ Vector((-0.027, 0.004, 0)), (0.004, 0.026, 0.012), GOLD, Rr)                     # push-to-talk on the side
    hm = HANDMAT[style]
    box(c + Rr @ Vector((0.002, -0.040, 0.002)), (0.068, 0.060, 0.050), hm, Rr, 0.008)           # the fist round the radio's lower half
    for k in range(3):                                                                          # knuckle steps across the front
        box(c + Rr @ Vector((0.004, -0.024 - k * 0.016, 0.026)), (0.062, 0.013, 0.008), hm, Rr, 0.003)
    box(c + Rr @ Vector((-0.030, -0.018, 0.020)), (0.018, 0.046, 0.018), hm, Rr @ rot('Z', -18), 0.004)   # thumb up the side
    if style == "C":                                                                            # bare fingertips out of the glove
        box(c + Rr @ Vector((0.006, -0.032, 0.031)), (0.056, 0.03, 0.004), SKIN, Rr, 0.002)


def left_action(style, pose):
    if pose == "calm":                                                                          # palm out, "steady now"
        elbow, wrist = (-0.44, -0.50, -0.20), (-0.215, -0.205, -0.47)
        sleeve(elbow, wrist, style, "L")
        mitten(wrist, Rp_calm(), HANDMAT[style], open_hand=True, scale=0.82)
    elif pose == "litter":                                                                      # reaching down and forward, pinching a crushed cup
        elbow, wrist = (-0.36, -0.46, -0.14), (-0.13, -0.215, -0.42)
        sleeve(elbow, wrist, style, "L")
        R = frame(Vector((0.3, -0.55, -0.75)), Vector((1, 0, 0)))
        mitten(wrist, R @ rot('Z', 180), HANDMAT[style], grip=0.4)
        cup = Vector(wrist) + R @ Vector((0, 0.03, 0.13))
        tube(cup + Vector((0, -0.05, 0)), cup + Vector((0.012, 0.035, 0.01)), 0.032, 0.026, ("cup", "e8e2d4"), 7, Vector((1, 0.3, 0)))
        tube(cup + Vector((0.002, -0.02, 0)), cup + Vector((0.008, 0.01, 0.005)), 0.0335, 0.031, ("cupband", "c8352b"), 7, Vector((1, 0.3, 0)))
    elif pose == "door":                                                                        # reaching straight out to turn a handle
        elbow, wrist = (-0.36, -0.40, -0.18), (-0.02, -0.17, -0.60)
        sleeve(elbow, wrist, style, "L")
        R = frame(Vector((0.15, 0.05, -1)), Vector((0, 1, 0)))
        mitten(wrist, R @ rot('Z', 90), HANDMAT[style], grip=0.7)


def Rp_calm():
    # columns: thumb side (to the right, +X), palm normal (forward, -Z), fingertips (up and a touch forward)
    x = Vector((1, 0, 0.0)); z = Vector((0.08, 1, -0.25)).normalized(); y = z.cross(x).normalized(); x = y.cross(z)
    return Matrix((x, y, z)).transposed() @ rot('Z', 0)



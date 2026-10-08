# exec()'d by farm_scene.py: the production honey-wagon files placed by honey_wagon_layout_v1.json relative to a portaloo.
# Option bulge = comma list of segment indices to scale up (to show the bulge rig), src = out | game
import json
SRC = "C:/Projects/festival-tycoon/assets/source/environment/honey-wagon-v1/out/"
LJ = json.load(open(SRC + "honey_wagon_layout_v1.json"))
LX, LZ, LYAW = 6.0, 17.0, 180.0


def loo_to_world(p):
    a = math.radians(LYAW); x, y, z = p                      # Godot yaw: local +X -> (cos, 0, -sin), local +Z -> (sin, 0, cos)
    return (LX + x * math.cos(a) + z * math.sin(a), y, LZ - x * math.sin(a) + z * math.cos(a))


def own(f, x, z, yaw, y=0.0):
    global A
    if OPTS.get("src", "out") == "out":
        _a = A; A = SRC; o = place(f, x, z, yaw, y=y); A = _a; return o
    return place("environment/" + f, x, z, yaw, y=y)


place("environment/portaloo/lwf_portaloo_v1.glb", LX, LZ, LYAW)
t = LJ["truck"]; wx, _, wz = loo_to_world(t["local"]); own(t["file"], wx, wz, LYAW + t["yaw"])
h = LJ["hose"]; own(h["file"], LX, LZ, LYAW)
s = LJ["sign"]; door = (0.45, 0.08, -0.795)
sx, sy, sz = loo_to_world((door[0] + s["local"][0], door[1] + s["local"][1], door[2] + s["local"][2])); own(s["file"], sx, sz, LYAW, y=sy)
if "bulge" in OPTS:
    for c in bpy.data.collections:
        if c.name.startswith(h["file"]):
            want = {f"LWF_Hose_Seg_{int(i):02d}" for i in OPTS["bulge"].split("-")}
            for o in c.objects:
                if o.name.split(".")[0] in want: o.scale = (1.9, 1.9, 1.0)     # Blender local X/Y = Godot local X/Z (radial)

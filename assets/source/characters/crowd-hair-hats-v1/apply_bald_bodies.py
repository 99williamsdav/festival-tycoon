# Ship bald bodies: strip the baked hair from the game's guest pose GLBs and performer bodies (and the role-assets-v2
# performer copies), then refresh the hashes and triangle counts the tests check. Idempotent.
#   python apply_bald_bodies.py
# attendee-v6-draft/poses keeps the generator output WITH hair: build_crowd_pieces.py cuts the default hair from it.
# Regenerating the guest bodies = run the generator, copy into game/assets/characters, run this script.
import hashlib, json, os, struct, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from strip_baked_hair import strip
ROOT = "C:/Projects/festival-tycoon/"
GAME = ROOT + "game/assets/characters/"
ROLE = ROOT + "assets/source/characters/role-assets-v2/"


def tris(path):
    raw = open(path, "rb").read(); jlen = struct.unpack_from("<I", raw, 12)[0]; j = json.loads(raw[20:20 + jlen])
    return sum(j["accessors"][p["indices"]]["count"] // 3 for m in j["meshes"] for p in m["primitives"])


sha = lambda p: hashlib.sha256(open(p, "rb").read()).hexdigest()
man_path = GAME + "attendee_poses_v6_manifest.json"
man = json.load(open(man_path))
for sex, variants in man["variants"].items():
    for v in variants:
        p = GAME + v["file"]; r, _ = strip(p)
        v["sha256"] = sha(p); v["triangles"] = tris(p)
        print(f"{v['file']}: -{r} tris -> {v['triangles']}")
open(man_path, "w").write(json.dumps(man, indent=1))
for sex in ("male", "female"):
    name = f"lwf_performer_{sex}_body_v2"
    for p in (GAME + name + ".glb", ROLE + name + ".glb"):
        r, _ = strip(p); print(f"{p}: -{r} tris")
    rep = json.load(open(ROLE + name + ".json"))
    rep["sha256"] = sha(GAME + name + ".glb"); rep["triangles"] = tris(GAME + name + ".glb")
    rep["hair"] = "baked hair stripped (crowd-hair-hats-v1/apply_bald_bodies.py); wear lwf_hair_" + sex + "_default_v1 or another hair piece"
    json.dump(rep, open(ROLE + name + ".json", "w"), indent=1)

# Merge every glTF animation in a GLB into one animation named "Animation" (the v2 kit convention: the game loops
# each AnimationPlayer animation with Play(), so a kit must carry ONE animation with every arm's channel).
# Rewrites only the JSON chunk; the binary buffer is untouched.  python merge_animations.py file.glb [...]
import json, struct, sys

def merge(path):
    raw = open(path, "rb").read()
    magic, version, _ = struct.unpack_from("<III", raw, 0)
    jlen, jtype = struct.unpack_from("<II", raw, 12)
    j = json.loads(raw[20:20 + jlen]); rest = raw[20 + jlen:]
    anims = j.get("animations", [])
    if len(anims) <= 1:
        if anims: anims[0]["name"] = "Animation"
    else:
        merged = {"name": "Animation", "channels": [], "samplers": []}
        for a in anims:
            off = len(merged["samplers"]); merged["samplers"] += a["samplers"]
            for c in a["channels"]:
                c = dict(c); c["sampler"] += off; merged["channels"].append(c)
        j["animations"] = [merged]
    body = json.dumps(j, separators=(",", ":")).encode()
    body += b" " * ((4 - len(body) % 4) % 4)
    out = struct.pack("<III", magic, version, 12 + 8 + len(body) + len(rest)) + struct.pack("<II", len(body), jtype) + body + rest
    open(path, "wb").write(out)
    print("merged", path, "->", len(j.get("animations", [])), "animation(s),", sum(len(a["channels"]) for a in j.get("animations", [])), "channels")

for p in sys.argv[1:]:
    merge(p)
